using System.Collections.Concurrent;
using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>État par session de <see cref="ZoneEntryFlow"/> — miroir des
/// champs runtime du CCharacter natif (position, sight, combat,
/// dialogues, opérations, métiers, étals). Agrégat volontairement plat :
/// le natif porte aussi tout sur CCharacter ; un split par domaine
/// exigerait ~300 réécritures pour aucun gain runtime.</summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Vue mutable minimale de l'état runtime du perso pour
    /// les handlers GM (<c>GmCommandService.Context.State</c> est un
    /// <c>object?</c> — <c>EntryState</c> est privé) : la synchro or
    /// après un gain/set évite que le prochain <c>SetGoldAsync</c>
    /// d'un achat écrase la valeur DB avec l'ancienne en mémoire.</summary>
    public interface IGmCharRuntime
    {
        int Gold { get; set; }
        /// <summary>Node courant (501/502 = halls de famille —
        /// nécessaire aux commandes GM de hall).</summary>
        short NodeId { get; }
        /// <summary>Position courante — cible de <c>//place_npc</c>
        /// (le NPC est posé sous les pieds du GM).</summary>
        float PosX { get; }
        float PosZ { get; }
        /// <summary>Orientation courante — reprise par le NPC posé.</summary>
        float Facing { get; }
        /// <summary>Famille dont le hall est visité (char+0xaac
        /// natif) — guilde cible par défaut des commandes GM de
        /// hall quand le perso est dans une ville de famille.</summary>
        int VisitedFamilyInstanceId { get; }
    }

    /// <summary>État mutable par session — propriété exclusive du fil
    /// de la boucle (modèle acteur). <c>internal</c> pour les tests
    /// d'install (Eden.Tests) ; jamais exposé hors assembly.</summary>
    internal sealed class EntryState : IGmCharRuntime
    {
        // Implémentation explicite de IGmCharRuntime — les champs
        // publics ci-dessous restent des champs (accès boucle direct).
        short IGmCharRuntime.NodeId => NodeId;
        float IGmCharRuntime.PosX => PosX;
        float IGmCharRuntime.PosZ => PosZ;
        float IGmCharRuntime.Facing => Facing;
        int IGmCharRuntime.VisitedFamilyInstanceId =>
            VisitedFamilyInstanceId;

        /// <summary>Compteur de ticks de régénération — autosave DB
        /// tous les 12 ticks (≈1 min) dans PlayerRegenStepAsync.</summary>
        public int RegenTicks;
        public uint CharId;
        public string CharName = "";
        public bool BurstSent;
        public bool Migrated;
        /// <summary>Chargement d'entrée hors-boucle en vol (Phase 2) —
        /// posé par la commande NCID 0 sur la boucle puis JAMAIS
        /// relu par elle : <see cref="TryHandleAsync"/> attend sa
        /// complétion avant de router le paquet suivant, ce qui
        /// preserve l'ordre par session (ticket → echo → burst)
        /// sans bloquer les autres commandes de la boucle. La task
        /// se termine APRÈS l'exécution de la commande d'install
        /// atomique — un handler qui passe la passerelle voit donc
        /// un état entièrement installé. Volatile : écrite sur la
        /// boucle, lue depuis les threads de lecture de session.</summary>
        public volatile Task? EntryLoad;
        /// <summary>Données d'entrée chargées hors-boucle et posées
        /// par l'install atomique — utilisées par les patches du
        /// burst (310 : progression de classe, 311 : équipement+sorts)
        /// et par les étapes différées de MigrationDone (enchants à
        /// restaurer, expirations, lifelong, limitless, stats) pour
        /// éviter de re-requêter la DB sur la boucle.</summary>
        public EntryBundle? Entry;
        /// <summary>Les données <see cref="Entry"/> sont périmées —
        /// posé quand équipement/sorts/classe changent après
        /// l'install (invalidations WeaponResolved/SpellLevelsDirty/
        /// changement de classe) : les patches 310/311 retombent
        /// alors sur les requêtes live d'origine.</summary>
        public bool EntryDirty;
        public short NodeId;
        public float PosX, PosZ;
        public int PlayerHp = 400, PlayerMaxHp = 400;
        /// <summary>Blob d'apparence 32B persisté (player_characters.appearance_data)
        /// — patché dans le corps du 310 (ClientEnterNodePlayerData).</summary>
        public byte[] Appearance = new byte[32];
        public NetSession? Session;
        /// <summary>sight handle u32 -> template id (NPC 50xxx / monstre 56xxx).</summary>
        public readonly ConcurrentDictionary<uint, int> HandleToEntity = new();
        /// <summary>Handles actuellement dans la vue du client (sight).
        /// Concurrent — muté aussi par les tâches de fond (despawn de
        /// cadavre, boucle de combat) pendant qu'UpdateSightAsync itère :
        /// un HashSet corrompu renvoyait des Contains faux en permanence
        /// et plus aucune entité n'était ré-émise après un retrait.</summary>
        public readonly ConcurrentDictionary<uint, byte> Sent = new();
        /// <summary>Positions par handle pour le recalcul de sight
        /// (concurrent pour la même raison).</summary>
        public readonly ConcurrentDictionary<uint, (float X, float Y)> EntityPos = new();
        /// <summary>Payloads 298 rejoués (handle -> packet brut capturé,
        /// corps sans ncid) pour réémission lors du retour en vue.</summary>
        public readonly ConcurrentDictionary<uint, byte[]> Replayed = new();
        /// <summary>Handles des NPCs générés depuis la scène (vs rejoués).</summary>
        public readonly HashSet<uint> Generated = new();
        /// <summary>index option -> id du dialogue corps (S_/T_Dialogue).</summary>
        public int[] DialogBodyIds = Array.Empty<int>();
        public int DialogNpcHandle;
        public int DialogNpcId;
        /// <summary>LocalId u16 de scène [npc] — uid attendu par la mission
        /// (champ monster->0x324->0x4 du protocole natif).</summary>
        public int DialogNpcUid;
        /// <summary>Compteur de requêtes dialog envoyées à la mission.</summary>
        public int DialogSeq;
        /// <summary>Dernier seq honoré par une réponse 56/57 de la mission.</summary>
        public int DialogAcked;
        /// <summary>Options du dialogue courant renvoyées par la mission
        /// (miroir des CMissionDialog natives) — le client ne renvoie que
        /// l'index s16 ; le 128 re-émet les champs de l'entrée choisie.</summary>
        public List<MissionDialog> MissionDialogs = new();
        /// <summary>ctx (greeting id) du dernier 56 mission — le gossip
        /// local le réutilise pour garder le texte parlé du NPC au lieu
        /// d'écraser la fenêtre par un corps NULL.</summary>
        public int? MissionCtx;
        /// <summary>Or du perso chargé à l'entrée — overlay 280 attr 2.
        /// Propriété : implémente <see cref="IGmCharRuntime"/>.</summary>
        public int Gold { get; set; }
        /// <summary>Le client vient de changer de node (portail) — le prochain
        /// D4 rejoue le burst sans les entités de l'ancien node.</summary>
        public bool NodeChanged;

        // --- État de combat / cible ---
        /// <summary>Dernière cible sélectionnée (TargetUpdate 124).</summary>
        public uint TargetHandle;
        /// <summary>Dernière direction reportée (TurnSelf 123).</summary>
        public float Facing;
        /// <summary>Type de mouvement du dernier MoveSelf — miroir de
        /// char+0x54 natif (s16 final du CNE1 : 0/3/5/6/8 = strafes et
        /// marche arrière, 7 = course avant…). Recopié tel quel dans
        /// le byte flag du 326 MovePrediction — le client des
        /// observateurs en tire l'anim (course/strafe/monture).</summary>
        public short MoveFlag = 7;
        /// <summary>Horodatage (Stopwatch) du dernier MoveSelf accepté —
        /// base du contrôle de vitesse serveur anti-triche (0 = aucun).</summary>
        public long LastMoveTicks;
        /// <summary>Le joueur est en état de combat (flags 302 envoyés).</summary>
        public bool InCombat;
        /// <summary>Compteur d'actionId 302 — chaque nouvelle action
        /// (attaque, fin, marche) prend l'id suivant, sinon le client
        /// confond les actions et ne rejoue pas les animations.</summary>
        public int ActCounter;
        /// <summary>actionId de la chaîne d'attaque courante.</summary>
        public byte AttackAct;
        /// <summary>flag[0]=0x2000 posé (chaîne d'actions offensive en
        /// cours — capture : posé par le 1er cast, jamais par la mêlée ;
        /// purgé à la sortie de combat par `0000 sel=0`).</summary>
        public bool ActionChain;
        /// <summary>Nouvel actionId (302). Ne change pas AttackAct —
        /// celui-ci est posé uniquement au début d'une attaque.</summary>
        public byte NextAction()
        {
            var v = (byte)(++ActCounter & 0x7f);
            return v == 0 ? (byte)(ActCounter = 1) : v;
        }
        /// <summary>Le client a stoppé son attaque auto (type==2).</summary>
        public bool AutoAttacking;
        /// <summary>Pool de kfIds d'attaque selon l'arme équipée —
        /// résolu à l'entrée et au changement d'équipement via
        /// MotionParser.PlayerAttackKfIds (table CCharKfm::Integration
        /// native). Champ5 du 272 ClientMeleeAttack — le déclencheur
        /// réel de l'animation de swing côté client.</summary>
        public IReadOnlyList<int> WeaponKfIds =
            MotionParser.PlayerAttackKfIds(0);
        /// <summary>Portée d'attaque effective = S_Item col48 × 1.5
        /// (CCharFormula::AttackRange) + rayons des entités + 1.5 de
        /// tolérance native (IGameObj::HowClose). Défaut mêlée ~4.5+3.</summary>
        public float AttackRange = 7.5f;
        /// <summary>ItemId de l'arme résolue (0 = désarmé) — trace.</summary>
        public int WeaponItemId;
        /// <summary>Kind S_Item de l'arme principale résolue (col8 →
        /// CItemData+0x20 ; 0 = désarmé). Sert au flag projectile de la
        /// mêlée — les kinds arc/fusil/canon ajoutent un temps de vol
        /// dist/DefaultFlySpeed au CEventDelayDamage natif.</summary>
        public int WeaponKind;
        /// <summary>Kind S_Item de l'arme de main gauche (loc 8 natif ;
        /// 0 = pas d'arme off-hand — un bouclier n'est pas une arme).
        /// Sert à l'index « shoot » de <see cref="MotionParser.ShootIndex"/>
        /// (CCharKfm::GetBlastID @0x080e03f0 — dual → 10/0xb).</summary>
        public int WeaponOffKind;
        /// <summary>Résolution d'arme déjà tentée (évite une requête DB
        /// par swing quand le joueur est désarmé).</summary>
        public bool WeaponResolved;
        /// <summary>Snapshot combat du joueur — construit avec
        /// ResolveWeaponAsync (S_Level + S_Class + agrégat équipement +
        /// ItemDefs des slots d'arme) ; invalidé avec WeaponResolved au
        /// changement d'équipement. Null = désarmé jamais résolu.</summary>
        public Eden.Zone.Combat.CombatantSnapshot? Combat;
        /// <summary>Ids d'enchant de proc portés par l'équipement
        /// (S_Item col23 useEnchant dont S_Enchant a un déclencheur
        /// col41 ≠ 0 — pipeline CEnchantMgr::OnAttackEnchant natif).
        /// Collectés dans ResolveWeaponAsync avec l'agrégat
        /// équipement ; invalidés avec WeaponResolved.</summary>
        public List<int>? ProcEnchantIds;
        /// <summary>Cast en cours (spellId, cible, début) — mouvement/coup →
        /// 408 UseItemTimeBreak (corps vide) comme CheckCastBreak natif.</summary>
        public ushort CastingSpell;
        public uint CastingTarget;
        public CancellationTokenSource? CastCts;
        /// <summary>Génération monotone du cast en cours — incrémentée à
        /// chaque nouveau cast. Une séquence annulée/terminée ne purge
        /// CastingSpell que si elle est encore la génération courante :
        /// sans elle, le catch OCE d'un cast remplacé effaçait le
        /// CastingSpell du NOUVEAU cast (course constatée au spam
        /// skill-en-course).</summary>
        public int CastSeq;
        /// <summary>Dernier cast instantané résolu (anti-double-exécution
        /// par retry client pendant le swing ~600 ms).</summary>
        public ushort LastSpellId;
        public uint LastSpellTarget;
        public DateTime LastSpellAt;
        /// <summary>Cache spellmaster_id → niveau (player_spells) —
        /// peuplé au premier cast puis invalidé aux learn/upgrade ;
        /// sans lui chaque sort payait une requête DB complète sur le
        /// fil de la boucle (latence constatée au cast).</summary>
        public Dictionary<int, int>? SpellLevels;
        /// <summary>Classe courante du perso (changement de classe).</summary>
        public int ClassId;
        public int Level = 1;
        /// <summary>Garde-robe costumes — miroir de la hashtable
        /// char+0xab0 native (class → vector&lt;SCharacterCloset&gt;,
        /// slots positionnels 24B = 3 couleurs u64) remplie depuis
        /// <c>player_closet</c> à l'entrée.</summary>
        public SortedDictionary<byte, List<(ulong Helmet, ulong Clothes,
            ulong Cloak)>> Closets = new();
        /// <summary>Slot de costume actif (char+0x4a6 &amp; 0x3f natif —
        /// <c>player_characters.style</c>) écrit par ClosetSet 167.</summary>
        public byte ClosetIndex;
        /// <summary>Couleurs de costume équipées (char+0x48e/+0x496/
        /// +0x49e natifs — <c>player_characters.helmet_color/
        /// clothes_color/cloak_color</c>), renvoyées dans le 283
        /// mask 0x1e00.</summary>
        public ulong HelmetColor, ClothesColor, CloakColor;
        // --- Collection de cartes (char+0x55c / +0xb68 / +0xe14 /
        // +0xe28 natifs — table `collection`, points S_Collection) ---
        /// <summary>item_id des cartes déposées — la map native porte
        /// collection_id(=item_id) → item ; le serial de la ligne
        /// `collection` vit en DB (MoveItemTo/FromCollection).</summary>
        public readonly HashSet<int> CollectedItemIds = new();
        /// <summary>Total des points de collection (char+0xb68) —
        /// renvoyé en i32 dans le 472 ClientUpdateCollection.</summary>
        public int CollectionPoints;
        /// <summary>Points par groupe : clés = groupe S_Collection +
        /// 1000 (normales) / 1001 (flag) — map native char+0xe14
        /// lue par RefreshCollectionPointAward.</summary>
        public readonly SortedDictionary<short, int>
            CollectionGroupPoints = new();
        /// <summary>Enchants de récompense actifs (vec char+0xe28) —
        /// retirés puis réévalués à chaque modification.</summary>
        public readonly List<int> CollectionAwardIds = new();
        /// <summary>Dernier dépôt accepté — rate-limit natif ~500 ms
        /// du 150 SaveCollection (char+0xc?? horodaté).</summary>
        public DateTime LastCollectionSaveAt = DateTime.MinValue;
        /// <summary>Dernier trigger d'événement pay — rate-limit natif
        /// 1 s de TriggerPayEvent (horodatage µs char+0x650/0x654).</summary>
        public DateTime LastPayEventAt = DateTime.MinValue;
        // --- Calendrier de connexion (char+0xdd0..0xdf4 natifs —
        // table daily_awards, NCID 258) ---
        /// <summary>Index du jour courant (0..27, char+0xdd0).</summary>
        public short DailyDayIndex;
        /// <summary>Unix time du dernier login (char+0xdd4).</summary>
        public int DailyLoginTime;
        /// <summary>Secondes en ligne cumulées du jour (char+0xdd8).</summary>
        public int DailyCooldown;
        /// <summary>Bitmask des jours complétés (char+0xddc, u64).</summary>
        public ulong DailyAwardDays;
        /// <summary>Bitmask des jours réclamés (char+0xde4, u64).</summary>
        public ulong DailyAwardRecords;
        /// <summary>Statut de login (char+0xdec u8).</summary>
        public byte DailyLoginStatus;
        /// <summary>Delta de la session courante (char+0xdf0) —
        /// now-login_time, remis à 0 aux updates.</summary>
        public int DailySessionDelta;
        /// <summary>Marqueur de session chargée (char+0xdf4 —
        /// annule le flag status du rollover).</summary>
        public bool DailySessionFlag;
        /// <summary>État chargé depuis daily_awards.</summary>
        public bool DailyLoaded;
        /// <summary>Flags globaux du système (daily_awards_set →
        /// zone +0x638/+0x639 ; désactivé → 615 type 4).</summary>
        public bool DailyEnabled = true;
        // --- Bot client (char+0xaa8/0xaa9 — NCID 164) ---
        public bool BotFlag1, BotFlag2;
        // --- Stats battlefield (char+0x97c..0x9e0 — NCID 162 →
        // 482 : 12 mois + 12 carrière + 2 inutilisés) ---
        /// <summary>Les 26 compteurs battlefield, chargés à la
        /// demande depuis battlefield_month/career.</summary>
        public int[]? BattlefieldStats;
        // --- Tutoriel (maps char+0xc18 natives — NCID 218,
        // new_player_guide) ---
        /// <summary>{guide_id → (trigger count, closed count)} —
        /// noeud natif {u16 id, s16 trigger, s16 closed}.</summary>
        public readonly SortedDictionary<ushort, (ushort Trigger,
            ushort Closed)> NewPlayerGuide = new();
        /// <summary>Maps guide chargées (lazy au premier 218).</summary>
        public bool NewPlayerGuideLoaded;
        /// <summary>Mode dieu GM (<c>//god</c>) — les dégâts reçus sont
        /// nuls. Toggle runtime, non persisté.</summary>
        public bool GodMode;
        /// <summary>Services du dialogue courant : index option -> service
        /// parsé (3=shop, autre = transport/etc) — routage ReplyDialog.
        /// Extra = 4e champ #:Service (ex. instance_dialog arg2).</summary>
        public readonly Dictionary<int, (int Type, int Param, int Title, int Extra)>
            DialogServices = new();
        /// <summary>Contexte shop ouvert : id de boutique S_Store (-1 = aucun).</summary>
        public int OpenShopId = -1;
        /// <summary>Dernier passage de sight (throttle — un scan par
        /// MoveSelf gelait la file d'envoi).</summary>
        public DateTime LastSightAt = DateTime.MinValue;

        // --- Identité / progression (patch NCID 310 + état combat) ---
        public int RaceId;
        public int GenderId;
        public long Experience;
        /// <summary>create_flag DB = trait héroïque (1..8 → enchant
        /// 52000+n), 0 = aucun — poussé par le pong custom 9058.</summary>
        public int CreateFlag;
        public int ClassLevel = 1;
        public int PlayerMp, PlayerMaxMp;
        /// <summary>Le joueur est mort (305+531 émis) — attaques et déplacements
        /// bloqués jusqu'au Revive (20).</summary>
        public bool Dead;

        // --- Activités recommandées hebdo (S_RecommendActivity) ---
        // Modèle natif : char+0xc8c ts, map progression (+0xc90),
        // map pending/claimed (+0xca8 flag byte à +0x12), vec tracés.
        /// <summary>Progression par recommend_id (map native +0xc90).</summary>
        public readonly SortedDictionary<ushort, uint> RecommendProgress = new();
        /// <summary>État terminé : 0 = objectif atteint non réclamé,
        /// 1 = récompense réclamée (map +0xca8, flag à +0x12).</summary>
        public readonly SortedDictionary<ushort, byte> RecommendFinished = new();
        /// <summary>Ids tracés par le joueur (vec ed+0x7c4 mission).</summary>
        public readonly List<ushort> RecommendTracing = new();
        /// <summary>Requête d'instance versus en attente (exec+0x684
        /// natif) — posée au relais join 252, levée au leave.</summary>
        public bool VersusPending;
        /// <summary>Id de l'instance versus courante (char+0xbb4
        /// natif — ≠0 = en match).</summary>
        public int VersusInstanceId;
        /// <summary>Camp versus 0/1 (char+0xdb0 natif — posé par
        /// Mission_TC_EnterVersusInstance, lu pour le spawn de camp
        /// et le scoreboard).</summary>
        public int VersusCamp;
        /// <summary>Id du Battle Royale courant (char+0xbb4 /
        /// +0xd90 natifs — ≠0 = en BR).</summary>
        public int BattleRoyaleId;
        /// <summary>bfId du match battlefield en cours (0 = pas en
        /// match) — sert au relay kill PvP → mission (score).</summary>
        public int BattlefieldId;
        /// <summary>crusadeId de la bataille de siège en cours
        /// (char+0xdfc natif — posé à l'EnterBattleCrusadeAnswer,
        /// remis à 0 au leave/close).</summary>
        public int CrusadeId;
        /// <summary>Point de retour de croisade — position sauvée à
        /// l'entrée du siège (exec+0xf0/+0xf4/+0xf8 natifs du
        /// NotifyEnterBattleCrusade mission) et restaurée au
        /// leave/close. 0 = aucun.</summary>
        public int CrusadeReturnNode;
        public float CrusadeReturnX;
        public float CrusadeReturnY;

        // --- Territory war / family battle (char+0xb70.. natifs) ---
        /// <summary>Id du territoire/battle en cours
        /// (char+0xb70 natif — posé depuis exec+0x118 à la migration
        /// vers le node de bataille, remis à 0 au leave
        /// @0x0815af00).</summary>
        public int TerritoryId;
        /// <summary>Camp 1/2 dans la bataille (u16 char+0xb74 natif,
        /// 0xffff = aucun — reset à 0xffff par CEventTerritoryClose
        /// et au leave).</summary>
        public ushort TerritoryCamp = 0xffff;
        /// <summary>Kills perso dans la bataille (u16 char+0xb9c
        /// natif — émis dans le 532 ClientTerritoryKilledInfo).</summary>
        public int TerritoryKills;
        /// <summary>Dernière phase territoire notifiée via le 516 —
        /// le natif broadcast à chaque transition (TerritoryWarStart
        /// type 1, FinishByNewEnding type 2) ; le tick n'émet que
        /// sur changement (-1 = jamais émis).</summary>
        public short TerritoryPhaseSeen = -1;
        /// <summary>Timestamp du dernier refresh/reset (char+0xc88).</summary>
        public int RecommendTime;

        // --- Class lifelong (map char+0xc58 + or char+0xc54 natifs) ---
        /// <summary>Compteurs par groupe waken (S_Class col2) —
        /// chargés de `class_lifelong` à l'entrée (copie exec→zone
        /// native), deltas persistés immédiatement pour les types 0-2
        /// et par lots de 10 pour le daily (pending s16 +0x24).</summary>
        public readonly SortedDictionary<short, Eden.Core.Database
            .Repositories.LifelongCounters> Lifelong = new();
        /// <summary>Médailles « Crystal Cross » (lifelong_gold,
        /// char+0xc54) — cap natif 9999999, affiché via 555.</summary>
        public int LifelongGold;
        /// <summary>Jour UTC du dernier reset daily — exec+0x760 natif
        /// (tm_mday) : le compteur type 3 repart à 0 au nouveau jour.</summary>
        public int LifelongDay = -1;
        /// <summary>Miroir prestige zone — map char+0x6c4 natif
        /// (clé = prestige_id, valeur = SPrestigeData {level, exp}),
        /// rempli par les commandes monde <c>update_prestige</c>
        /// (<c>CCharacter::UpdatePrestige</c> @0x08419ee0). Source des
        /// gates <c>GetPrestigeLevel</c>/<c>GetPrestigeExp</c> natifs
        /// (battlefield, pay events, conditions de quêtes).</summary>
        public readonly SortedDictionary<short, (short Level, int Exp)>
            Prestige = new();
        /// <summary>Licence dual-wield active (char+0x77e natif) — posée
        /// par la commande d'usage UseTwoWeapon (id 9101,
        /// CEC_UseTwoWeapon @0x855bc30) ; propagée au client comme
        /// attr 1000 dans 280 ClientUpdateAttr.</summary>
        public bool TwoWeaponUnlocked;
        /// <summary>Perso assis (PlayerAction) — régénération accélérée.</summary>
        public bool Sitting;
        /// <summary>Aire de réapparition (player_characters.revive_area_id)
        /// — envoyée dans 531 ClientReviveInfo.</summary>
        public int ReviveAreaId;
        /// <summary>Dernière aire NORMALE traversée (composite
        /// node&lt;&lt;16|id — player_characters.last_normal_area_id,
        /// positionné par le verbe `tpa` des dialogues).</summary>
        public int LastNormalAreaId;
        /// <summary>Event-areas contenant actuellement le perso — la
        /// détection de franchissement (grille spatiale de
        /// CCharacter::MoveSelf natif : chaque IGameObj d'aire reçoit
        /// le mouvement) ne tire qu'à l'ENTRÉE, jamais en continu.</summary>
        public readonly HashSet<int> InsideAreas = new();
        /// <summary>Token d'annulation des boucles par-perso (régén).</summary>
        public CancellationTokenSource? LoopCts;
        /// <summary>Enchants actifs du perso — miroir CEnchantMgr
        /// (effect_id → CEnchant : rest, level, ticks HP, chaînes).</summary>
        public readonly Dictionary<int, ActiveStatus> Statuses = new();
        /// <summary>Modificateurs d'attribut de sort par entité —
        /// miroir <c>ILifeEntity::AddSpellAttrModify</c> natif :
        /// (kind, spellId) → total signé (%). Kinds : 0=Range,
        /// 1=Radius, 2=CoolDown, 3=PhyCriRates, 4=PhyCriDamages,
        /// 5=MagicCriRates, 6=MagicCriDamages.</summary>
        public readonly Dictionary<(int Kind, int SpellId), int>
            SpellAttrMods = new();
        /// <summary>Verrous d'action agrégés (commande S_Enchant
        /// 2055 → compteurs CEC_State* natifs) — recalculé sur la
        /// boucle à chaque add/remove de statut, lisible hors-boucle
        /// (int atomique) par les handlers stateless via
        /// <c>ZoneServices.StateLocksOf</c>. Bits : 1=NoMove,
        /// 2=NoAttack, 4=NoUseSkill Kungfu, 8=NoUseSkill Magic,
        /// 16=NoUseItem.</summary>
        public int StateLocks;
        /// <summary>Invincibilité post-migration (char+0x3c bit7 natif,
        /// levée par OnMigrationInvincibleTimeout) — les mobs ne
        /// peuvent pas engager/infliger tant que la date n'est pas
        /// passée.</summary>
        public DateTime MigrationInvincibleUntil = DateTime.MinValue;
        /// <summary>Duel en cours : charId de l'adversaire (0 = pas de
        /// duel — miroir de char+0x95c = -1 natif). Posé à l'accept
        /// (136 → 453), purgé par FinishDuelAsync (mort, téléport,
        /// déco — CDuelFactory::DuelFinish natif).</summary>
        public uint DuelOpponentId;
        /// <summary>Nom de l'adversaire capté à l'accept — sert au
        /// DuelResult si l'adversaire a quitté la zone entre-temps.</summary>
        public string DuelOpponentName = "";
        /// <summary>Duel effectivement engagé (453 DuelBegin émis) —
        /// distingue "perdu" (résultat 1) de "annulé" (résultat 2) dans
        /// la fin native.</summary>
        public bool DuelActive;
        /// <summary>Requête de duel reçue en attente de réponse —
        /// charId du demandeur (CDuelInviteeOp natif).</summary>
        public uint DuelPendingFrom;
        /// <summary>Requête de duel envoyée en attente — charId de la
        /// cible (CDuelRequestOp natif).</summary>
        public uint DuelPendingTo;
        /// <summary>Numéro de série du duel courant (clé AllDuelData
        /// native) — identifie le countdown/round schedulé.</summary>
        public int DuelSerial;
        /// <summary>Routes proposées dans le dernier 393 EnterTransport —
        /// la sélection 88 du client est résolue sur la table S_Transport.</summary>
        public List<short> TransportRoutes = new();
        /// <summary>Opération "shop" active (COperation natif, char+0x670) :
        /// code de fenêtre du protocole — 9 renfort (op 0x26),
        /// 10 combine (0x27), 22 métier (0x39), 28 cube (0x52),
        /// 31 boîte magique (0x58), -1 aucune. Posée par PlayerAction
        /// (13/14/16/101), les services NPC (9/10/31) et les
        /// parchemins OpenShop.</summary>
        public int ShopOpType = -1;
        /// <summary>UID du NPC lié à l'opération (0 = ouverture directe
        /// par l'UI — TargetInTalkingRange(0) natif retourne vrai).</summary>
        public int ShopOpNpcUid;
        /// <summary>Sight-handle du NPC de l'opération — la portée parlée
        /// se vérifie par axe |dx|,|dz| ≤ 10 (DAT_087cea88).</summary>
        public int ShopOpNpcHandle;
        /// <summary>Renforcement validé en cours de cast (CNE_119 →
        /// enchant 13 natif) — annulable par StrengthenClose.</summary>
        public PendingStrengthen? PendingStrengthen;
        /// <summary>Groupe de métier natif (player_characters.life_skill,
        /// char+0xcc8) : 1 = analyse joaillerie, 2-5 = professions de
        /// production ; -1 → dérivation au chargement (CheckLifeSkill
        /// mission : nibble profession 0 → groupe 1) puis persistance.</summary>
        public int LifeSkill = -1;
        /// <summary>Recettes apprises — miroir de la hashtable
        /// char+0xb84 (CLifeSkill : [u32 exp][u32 due]) remplie depuis
        /// `player_life_skill` à l'entrée.</summary>
        public readonly Dictionary<int, (int Exp, int Due)> LifeSkillLearned = new();
        /// <summary>Cast métier en cours (enchant LifeSkillCastID=211
        /// natif — CLifeSkillOp::SetData + flag +0x14) — résolu par
        /// EndLifeSkill à l'expiration (~5s), annulé par
        /// StrengthenClose 22.</summary>
        public PendingLifeSkill? PendingLifeSkill;
        /// <summary>Cast cube Horadric en cours (enchant CubeCrafting
        /// natif — CCubeOp::SetItemData + flag +0x64) — résolu par
        /// ExecuteCubeOp à l'expiration (~1.5s), annulé par
        /// StrengthenClose 28.</summary>
        public PendingCube? PendingCube;
        /// <summary>Cast boîte magique en cours (enchant MagicBoxGoing
        /// natif — CMagicBoxOp::SetItemData + flag +0x38) — résolu par
        /// ExecuteMagicBox à l'expiration (~1.5s), annulé par
        /// StrengthenClose 31.</summary>
        public PendingMagicBox? PendingMagicBox;
        /// <summary>Cast station combine en cours (enchant CombineItemID
        /// natif — CCombineOp::SetItemData + flag op+0x14) — résolu par
        /// DoCombineItem à l'expiration (0x28×100 ms), annulé par
        /// StrengthenClose 10.</summary>
        public PendingCombine? PendingCombine;
        /// <summary>Octets de visibilité d'équipement natifs
        /// (char+0x4ab/+0x4ac) — togglés par PlayerAction
        /// EquipAction (codes 7/8) et renvoyés via ModelModify 283.</summary>
        public int Vis4ab, Vis4ac;
        /// <summary>Octet carte-personnage natif (char+0x4ad) — bit2
        /// écrit par CNE_PlayerCCDataSave, renvoyé via ModelModify
        /// flag 0x8000. Persisté avec les données carte (TODO CC).</summary>
        public int Vis4ad;
        /// <summary>Index de carte active natif (char+0xd98) — écrit
        /// par PlayerCCDataSave, persisté dans
        /// `player_characters.current_custom_data_index`.</summary>
        public int ActiveCCIndex;
        /// <summary>Les 3 cartes de classe personnalisée (vecteur
        /// char+0xd9c natif — records `CCustomClass` 0x14 :
        /// equip_type +5, weapon_type +6, sorts +8) — chargées depuis
        /// `custom_class_data` (LoadCustomClassData natif).</summary>
        public readonly List<Eden.Core.Game.Classes.CustomClassSlot>
            CustomClassSlots = new();
        /// <summary>Compteur d'échecs de renfort (char+0xd80 natif,
        /// persisté côté mission via `TempStrengthenFailTime`) — le pity
        /// bonus = compteur × EquipAddition[niveau].</summary>
        public int StrengthenFailCount;
        /// <summary>Opération CTransportShop ouverte — le natif exige une
        /// opération 0x25 active pour exécuter le 88 et appelle
        /// ResetOperation en sortie (succès ET échec : or insuffisant,
        /// route inconnue). Un 88 sans fenêtre ouverte (paquet tardif
        /// que le client réémet après le TP) est ignoré — c'est ce qui
        /// renvoyait le joueur à Aven après un vrai transport.</summary>
        public bool TransportOpen;
        /// <summary>Niveau de privilège (player_characters.privilege) —
        /// gate des commandes texte NCID 3 (désasm TextCommand::Execute :
        /// char+0x63c > 0 requis, sinon bail silencieux).</summary>
        public int Privilege;
        /// <summary>Dernière aire GATEWAY déclenchée (clé composite
        /// keyId|node&lt;&lt;16) — anti-retrigger natif char+0x7a4 : un
        /// second 15 pour la même aire est rejeté par le texte 1637.
        /// Réinitialisé à chaque migration de node.</summary>
        public uint LastGatewayKey;
        /// <summary>account_id du perso — clé de
        /// `player_lottery_point` (points hebdo par compte).</summary>
        public int AccountId;
        /// <summary>Type IsA de l'op lottery ouverte (-1 aucune) :
        /// 0x39 (fenêtre 22), 0x54 (fenêtre 150 = ingame),
        /// 0x56 (fenêtre 151 = normal+diamond) — CCharacter+0x670.</summary>
        public int LotteryOp = -1;
        /// <summary>Cast lottery en cours (op+8 « going » natif +
        /// garde anti-double-cast).</summary>
        public PendingLottery? PendingLottery;
        /// <summary>États persistés des trois autels (lignes edenfr_db
        /// lottery / in_game_lottery / new_lottery) — chargés à
        /// l'entrée, toujours non-null après load.</summary>
        public Eden.Core.Game.Lottery.PlayerLotteryState LotteryNormal
            = new(), LotteryInGame = new(), LotteryDiamond = new();
        /// <summary>Points/paliers hebdo du compte
        /// (`player_lottery_point`).</summary>
        public Eden.Core.Game.Lottery.LotteryWeekPoints LotteryWeek
            = new();
        /// <summary>Les 11 CWork natifs (char+0xb20..+0xb44) —
        /// chargés depuis player_works à l'entrée.</summary>
        public Eden.Core.Game.Work.WorkSlot[] Works =
            System.Array.Empty<Eden.Core.Game.Work.WorkSlot>();
        /// <summary>Type de la fenêtre works ouverte (service NPC
        /// 12-17 → shopType natif de l'opération works, -1 aucune) —
        /// le 170 PlayerWork n'est valide que quand une op works est
        /// ouverte (ToWork exige op+4 == workType).</summary>
        public int WorkOpType = -1;
        /// <summary>Flag op+0x16 natif — posé par le premier ToWork
        /// réussi ; la consommation différée des ordres instantanés
        /// (EndCollect/EndCombine instant) l'exige.</summary>
        public bool WorkOpStarted;
        /// <summary>Emplacements enregistrés pour la consommation
        /// différée (SetAllItemLoc natif) par work_num — catalyseur,
        /// booster et matériaux figés à l'armement.</summary>
        public readonly Dictionary<int, WorkDeferredLocs>
            WorkDeferredLocs = new();
        /// <summary>Jetons d'annulation des timers de complétion par
        /// work_num (nettoyés à la déconnexion).</summary>
        public readonly System.Threading.CancellationTokenSource?[]
            WorkCts =
                new System.Threading.CancellationTokenSource?[
                    Eden.Core.Game.Work.WorkSlot.MaxSlots];

        // --- Familiers de combat (elf pets — CKFElfManager natif) ---
        /// <summary>Slots d'elfs du tableau natif char+0x8ac — clé =
        /// loc 0-9. Chargés paresseusement à la première requête
        /// client (110 ou invocation).</summary>
        public readonly Dictionary<short, ElfSlot> ElfSlots = new();
        /// <summary>Table elf chargée pour ce personnage.</summary>
        public bool ElfsLoaded;
        /// <summary>Sight-handle du CElfPet live (char+0x7dc natif —
        /// un seul elf invoqué à la fois).</summary>
        public uint ElfPetHandle;
        /// <summary>Fusion d'elfs en cours (CPetUpgradeOp natif —
        /// char+0x670). Posé par NCID 227 quand CanUpgrade passe,
        /// résolu ~4 s plus tard (durée 0x28 du cast natif) ;
        /// (loc1, loc2, sac matériau, slot matériau).</summary>
        public (short Elf1Loc, short Elf2Loc, short MatBag,
            short MatSlot)? PendingElfMix;
        /// <summary>Loc de l'elf en état « mix » (char+0x8dc natif —
        /// SetElfStatus(1) le pose, -1 = aucun).</summary>
        public short MixElfLoc = -1;
        /// <summary>Template_id de l'elf en mix (char+0xbf8 natif —
        /// SetMixElfTemplateID ; émis dans le 567).</summary>
        public int MixElfTemplateId;
        /// <summary>Enchants intégrés appliqués au joueur par l'elf
        /// en mix (vecteur char+0x8c4 natif — peuplé par
        /// UpdateElfMixSpell, purgé par RemoveMixElf via
        /// ElfRemoveSpell/RemoveEnchantByID).</summary>
        public readonly List<int> MixSpellEnchants = new();
        /// <summary>Enchants passifs appliqués au joueur par le pet
        /// invoqué (vecteur char+0x8d0 natif — peuplé par
        /// CElfPet::InvokePassivities/UpdateBattleSpell depuis
        /// battle_spell_4/5 (spells famille 9 → enchant lié), purgé
        /// par CallElfBack/ToIdle/ToMix).</summary>
        public readonly List<int> PetGrantedEnchants = new();

        // --- Succès (exec+0x528/+0x53c/+0x548 mission natif) ---
        /// <summary>Progression des succès : achievement_id →
        /// (success_num, point) — map exec+0x528 natif, chargée
        /// paresseusement depuis player_characters_achievement.</summary>
        public Dictionary<int, (int SuccessNum, int Point)>?
            Achievements;
        /// <summary>Total de points de succès — exec+0x53c natif
        /// (plafond 999999999 ; texte 0x6ab au dépassement).</summary>
        public long AchievementTotalPoint;
        /// <summary>Cumuls par clé de groupe — map exec+0x548 natif
        /// (CumulativeAchievement : clés col5 et col6*100+col7 ; les
        /// totaux type 2/3 du 480 en lisent les clés 1 et 5).</summary>
        public readonly Dictionary<short, int> AchievementGroups =
            new();
        /// <summary>Passage de seuils de récompense déjà notifiés —
        /// évite de relancer add_appellation/mail à chaque grant
        /// (natif : le franchissement ne se produit qu'une fois car
        /// old &lt; seuil &lt;= new n'est vrai qu'au passage).</summary>
        public readonly HashSet<int> AchievementRewarded = new();

        // --- Titres (appellations — NCIDs 93/94, 396/397) ---
        /// <summary>Titre actif — char+0x6fc natif, persisté dans
        /// `player_characters.appellation`. L'enchant du titre actif
        /// (S_Title col3) est le SEUL bonus de titre compté :
        /// le chemin « grant permanent » natif est du code mort
        /// (CTitleQuery::Deserialize fixe data+0x10 = 4 ≠ 2).</summary>
        public int ActiveTitle;
        /// <summary>Compteur de version du titre — char+0x4df natif,
        /// incrémenté à chaque set (3e octet du 396).</summary>
        public byte TitleSeq;

        // --- Gladiator trial (CEvaluate natif — NCID 255/607/608) ---
        /// <summary>evalId de l'instance d'évaluation active —
        /// char+0xbb4 natif, posé par CreateEvaluate à l'entrée du
        /// trial et remis à 0 par CharacterLeave. 0 = hors trial.</summary>
        public int EvalId;
        /// <summary>Compteurs d'entrées journalières par nœud —
        /// map native char+0xcd4 chargée depuis player_node_times à
        /// la première utilisation (CheckNodeTimes zone @0x8409fe0).</summary>
        public Dictionary<int, int>? NodeTimes;

        // --- Tableau de recrutement d'instance (NCID 220 — champs
        // natifs char+0xc40/+0xc42/+0xc44, désasm zone @0x08100c40) ---
        /// <summary>État de la file de recrutement — +0xc40 natif :
        /// -1 = rien, 0 = en file, 2 = matched/transport en cours,
        /// 3 = transporté dans l'instance.</summary>
        public short RecruitState = -1;
        /// <summary>Rôle choisi dans la requête (+0xc42 natif — les
        /// règles de match utilisent les valeurs 0-2).</summary>
        public short RecruitRole = -1;
        /// <summary>Nœuds demandés (+0xc44 natif — vidée quand
        /// l'inscription est annulée ou aboutit).</summary>
        public List<short> RecruitNodes = new();

        /// <summary>Instance de nœud courante — 0 = scène partagée ;
        /// sur un nœud de type instance (IsAnyInstance natif) :
        /// team.Id en équipe, -charId en solo (CNodeGroup natif —
        /// chaque groupe a sa copie des mobs/drops/entités du node).
        /// Posé à la migration et au refresh d'appartenance
        /// (join/leave/disband d'équipe).</summary>
        public int InstanceGroup;

        // --- Chemin d'éveil (SDestinyRoad natif, char+0xbc8) ---
        /// <summary>Route active — null = pas de route (le bloc natif
        /// est alors Reset ; player_destiny_road n'a pas de ligne).</summary>
        public DestinyState? Destiny;
        /// <summary>Vitalité destin (char+0xbe8 /
        /// player_characters.destiny_vit) — cap = 100 × S_Parameter 569,
        /// +1 toutes les 72 s (CEventDestinyVitAdd).</summary>
        public int DestinyVit;
        /// <summary>L'événement de régén vit est planifié sur la boucle
        /// (char+0xbec != 0 natif) — ne jamais en planifier deux.</summary>
        public bool DestinyVitRegen;

        // --- Tenues rapides (maps char+0x58c live / char+0x5a4 working) ---
        /// <summary>Records fast_dress chargés à l'entrée —
        /// `CNE_DBA_ClientFastDress::LoadFastDress` natif. Null tant
        /// que la table n'a pas été lue.</summary>
        public Eden.Core.Game.Inventory.FastDressState? FastDress;

        // --- Sac chanceux (machine char+0xc70..0xc81 native) ---
        /// <summary>Sac en cours (char+0xc70 natif) — 0 = aucun.</summary>
        public int LuckyBagId;
        /// <summary>Item gagné en attente de récupération
        /// (char+0xc74 = pointeur item natif → ici l'item_id).</summary>
        public int LuckyBagPendingItemId;
        /// <summary>Quantité du lot en attente (char+0xc78).</summary>
        public int LuckyBagPendingCount;
        /// <summary>Nombre de lancers déjà faits sur ce sac
        /// (char+0xc7c) — base de la chance d'échec
        /// (min(rolls×param452, param453)).</summary>
        public int LuckyBagRolls;
        /// <summary>Drapeau bulletin du lot en attente (char+0xc80) —
        /// déclenche l'annonce écran au GetReward.</summary>
        public bool LuckyBagBulletin;
        /// <summary>Drapeau d'annulation de cast (char+0xc81) — posé
        /// quand le lancer enchanté est interrompu ; bloque le roll
        /// tant qu'il n'est pas relancé.</summary>
        public bool LuckyBagBlocked;

        // --- Sacs de fortune instantanés (OpenFortuneBag natif) ---
        /// <summary>Compteurs d'ouverture par sac — map char+0x574
        /// native (item_id du sac → s16 compteur), chargée depuis
        /// `player_fortune_bag` par account_id. Remise à 0 quand un
        /// lot bulletin sort (pity natif).</summary>
        public readonly Dictionary<ushort, short> FortuneCounters =
            new();

        // --- Hall de famille (char+0xaac natif) ---
        /// <summary>family_id du hall de famille visité — posé par le
        /// flux de visite (<c>Mission_TC_VisitFamily</c> →
        /// <c>char+0xaac = fm_id</c> puis transport vers le node
        /// <c>house_open</c>). 0 = pas dans une instance de hall ;
        /// <c>FamilyInstanceGetData</c> (132) exige ≠0 (sinon silence
        /// natif) et émet 487 depuis la map des bâtiments. Propriété :
        /// l'écriture viendra du lot visite/ville (FamilyTownEnter).</summary>
        public int VisitedFamilyInstanceId { get; set; }
        /// <summary>Les NPCs du hall visité (housekeeper + items
        /// 0x2a/0x52 du conteneur 0, <c>SFamily::InitFamilyNpc</c>
        /// natif) ont été injectés dans la vue — reset à chaque
        /// migration de node.</summary>
        public bool FamilyNpcsLoaded { get; set; }
        /// <summary>Point de retour d'instance (exec+0xf0/+0xf4/+0xf8
        /// « CrossZoneData » natif → char+0xa98/+0xa9c/+0xaa0 zone) :
        /// dernier node quitté dont le type ∉ {9,10,13,15,16,17,19}
        /// (ces types ont un retour dédié) + position au moment du
        /// départ. Consommé par la sortie de hall familial
        /// (176 familyId=-1) ; persisté dans
        /// player_characters.cross_zone_*.</summary>
        public int CrossZoneNode { get; set; }
        public double CrossZoneX { get; set; }
        public double CrossZoneY { get; set; }
        /// <summary>Types de node natifs exclus de CrossZoneData —
        /// instances à retour dédié (9/10/13/15/16/17/19, désasm
        /// mission `CNE_ZMis_MissionServerCrossZone::Execute`
        /// @0x8134a60) + 18 : le gladiator trial a son propre champ
        /// de retour natif (char+0x638) — chez nous CrossZoneNode
        /// sert de point de retour et ne doit PAS être écrasé par le
        /// nœud du trial à la sortie.</summary>
        public static readonly HashSet<int> CrossZoneExcludedTypes =
            new() { 9, 10, 13, 15, 16, 17, 18, 19 };
    }

    /// <summary>Route du destin en cours — miroir `SDestinyRoad` natif
    /// (char+0xbc8 → player_destiny_road). Les champs gardent les
    /// noms de colonnes natives ; les traductions de sens sont dans
    /// ZoneEntryFlow.Destiny.cs.</summary>
    public sealed class DestinyState
    {
        /// <summary>road_id (+0xbc8) — 0 = aucune route.</summary>
        public short RoadId;
        /// <summary>road_type (+0xbca).</summary>
        public short RoadType;
        /// <summary>step (+0xbcc) — case courante 0..38.</summary>
        public short Step;
        /// <summary>dice_time (+0xbce) — dés restants (10 à la
        /// création, colonne DB `dice_time`).</summary>
        public short RollsLeft;
        /// <summary>exp_buff (+0xbd0) — réserve de Destiny Points
        /// (DP), coût 1250 (param 376) par avancée, cap 12500.</summary>
        public int DpPool;
        /// <summary>dice_point (+0xbd4) — valeur du dernier dé /
        /// points de mouvement en attente.</summary>
        public short MovePoints;
        /// <summary>function_state (+0xbd6) — bitmask des fonctions
        /// utilisées (1&lt;&lt;funcId).</summary>
        public short FunctionState;
        /// <summary>+0xbe4 u8 — item « choix du dé » armé : l'op 3
        /// devient reroll choisi au lieu d'une fonction.</summary>
        public byte RerollFlag;
        /// <summary>Event ids des 39 cases (+0xbd8, vec&lt;s16&gt;).</summary>
        public readonly short[] Slots = new short[39];
        /// <summary>Reset natif (SDestinyRoad::Reset).</summary>
        public void Reset()
        {
            RoadId = 0; RoadType = 0; Step = 0; RollsLeft = 0;
            DpPool = 0; MovePoints = 0; FunctionState = 0;
            RerollFlag = 0;
            Array.Clear(Slots);
        }
    }

    /// <summary>Emplacements figés à l'armement pour la consommation
    /// différée des ordres instantanés (SetAllItemLoc — bag/slot/itemId
    /// du catalyseur et du booster).</summary>
    public sealed class WorkDeferredLocs
    {
        public int CatalystBag = -1, CatalystSlot = -1, CatalystItemId;
        public int BoosterBag = -1, BoosterSlot = -1, BoosterItemId;
    }

    /// <summary>Cast lottery en vol — posé par le Go (choix du
    /// paiement déjà enregistré dans l'état), résolu par le timer
    /// comme l'achèvement de l'enchant natif.</summary>
    public sealed class PendingLottery
    {
        /// <summary>Autel du cast (0/1/2 — dispatch du Result).</summary>
        public Eden.Core.Game.Lottery.AltarKind Altar;
        /// <summary>Jeton d'annulation (StrengthenClose 22/150/151).</summary>
        public CancellationTokenSource? Cts;
    }

    /// <summary>Cast station combine en vol (CCombineOp::SetItemData
    /// natif — les 5 valeurs figées à l'armement + flag op+0x14).</summary>
    public sealed class PendingCombine
    {
        /// <summary>Recette S_FamilyCombine catégorie 6.</summary>
        public int RecipeId;
        /// <summary>Catalyseur (matériau[0]) — sac et loc figés.</summary>
        public int CatBag = -1, CatSlot = -1;
        /// <summary>Booster CChanceItem optionnel (−1 = absent).</summary>
        public int BoostBag = -1, BoostSlot = -1;
        /// <summary>Jeton d'annulation (StrengthenClose 10).</summary>
        public CancellationTokenSource? Cts;
    }
}
