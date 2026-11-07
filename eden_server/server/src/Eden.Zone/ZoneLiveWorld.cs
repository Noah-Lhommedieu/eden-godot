using System.Collections.Concurrent;
using Eden.Core;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>
/// État vivant partagé de la zone — monstres instanciés par node avec
/// handles de sight stables, HP courant et respawn. Équivalent minimal du
/// registre CMonster côté lapis.
/// </summary>
public sealed class ZoneLiveWorld
{
    /// <summary>Monstre vivant (spawn scène + HP courant).</summary>
    public sealed class LiveMob
    {
        private readonly object _combatGate = new();
        private int _combatGeneration;
        private bool _combatLoopRunning;

        public required uint Handle;
        public required int MobId;
        public required int NodeId;
        /// <summary>Instance propriétaire — 0 = scène partagée,
        /// sinon la clé de groupe (team.Id / -charId, cf.
        /// CNodeGroup natif : chaque groupe a sa copie des nœuds
        /// de type instance, zone @0x0812b430).</summary>
        public int InstanceKey;
        public float X, Y, Z, Facing;
        public int Hp;
        public int MaxHp;
        /// <summary>Patience courante (jauge Break Point) — miroir de
        /// <c>CMonster+0x78</c> (i32, <c>BornMonster</c> @0x084b02b0 :
        /// = <c>MaxPatience</c>). Initialisée paresseusement depuis
        /// <see cref="Def"/>, rechargée au respawn et au retour de
        /// leash. Peut devenir négative (drain sans plancher).</summary>
        public int Patience
        {
            get
            {
                if (!_patienceReady) { _patience = MaxPatience; _patienceReady = true; }
                return _patience;
            }
            set { _patience = value; _patienceReady = true; }
        }
        private int _patience;
        private bool _patienceReady;
        /// <summary>Patience max (<c>CCharFormula::MaxPatience</c>
        /// @0x083f3d30) — envoyée au client dans le 298 (champ +0x4c).</summary>
        public int MaxPatience =>
            Eden.Core.Game.BreakPointRules.MaxPatience(Def);
        /// <summary>Flag one-shot de rupture (<c>CMonster+0x7c</c>) : posé
        /// quand la patience passe sous 1, remis à 0 au respawn
        /// (<c>BornMonster</c>) — déclenche le stagger une seule fois.</summary>
        public bool BpBroken;
        /// <summary>Le cast en cours ne peut pas être interrompu —
        /// <c>CEC_CantBreak</c> @0x0851aa30 (flag <c>entity+0x3c</c> bit 3).
        /// Aucune donnée S_Enchant du dépôt ne le pose pour l'instant.</summary>
        public bool CantBreak;
        /// <summary>Numéro du cast temporisé en cours — incrémenté à chaque
        /// wind-up ; un blast schedulé dont le numéro ne correspond plus
        /// a été interrompu (<c>SpellBreak</c> natif).</summary>
        public int CastSerial;
        /// <summary>Sort en cours de wind-up (0 = aucun) — pour le 287 de rupture.</summary>
        public ushort CastingSpellId;
        /// <summary>Dernier lancer de chaque Countdown skill (3 paliers
        /// S_Ai) — voir <c>MobSkillRules</c>.</summary>
        public DateTime[] CountdownLastCast = new DateTime[3];
        /// <summary>Remet la patience au max et efface la rupture
        /// (respawn / leash — <c>CAIFSMReturn::Update</c> @0x084dd1b0).</summary>
        public void ResetPatience()
        {
            Patience = MaxPatience;
            BpBroken = false;
        }
        /// <summary>Puissance d'attaque (S_Monster col 18).</summary>
        public int AttackPower;
        /// <summary>Id de motion d'attaque (S_Monster col 14 — DB id dans
        /// Motion.ini, ex. 186 -> m025 'attack01').</summary>
        public int AttackMotionId;
        /// <summary>kfIds d'attaque du modèle (Motion.ini, entrées
        /// `attack*`) — le natif CMonsterKfm::GetAttackID en pioche un au
        /// hasard pour le field5 du 272 (capture : 0x3fd/0x3fe).
        /// Repli sur [1021] (= 0x3fd, défaut natif) si le modèle n'en
        /// configure aucune.</summary>
        public int[] AttackKfIds =
            new[] { MotionParser.DefaultAttackKfId };
        /// <summary>Pioche d'un kfId d'attaque — `random() % count`
        /// comme le natif.</summary>
        public int NextAttackKfId() =>
            AttackKfIds[Random.Shared.Next(AttackKfIds.Length)];
        /// <summary>Table de haine du monstre : charId → points. Seule
        /// la commande AttrHateReset (1120) l'alimente pour l'instant ;
        /// la cible active reste <see cref="TargetCharId"/>.</summary>
        public readonly Dictionary<int, int> Hate = new();
        /// <summary>AttrHateReset natif (<c>CEC_AttrHateReset::Execute
        /// </c> @0x08507570) : chaque entrée de haine vaut 1, puis le
        /// lanceur devient la cible forcée du monstre.</summary>
        public void ResetHate(int casterCharId)
        {
            foreach (var k in Hate.Keys.ToList()) Hate[k] = 1;
            Hate[casterCharId] = Math.Max(1,
                Hate.GetValueOrDefault(casterCharId));
            TargetCharId = casterCharId;
        }
        public int Level;
        /// <summary>Définition S_Monster — source des snapshots combat
        /// (`CombatantSnapshot.FromMonster` : attaque col14, variance
        /// col15, def col18, hit/dodge/crits cols19-24, résists
        /// cols25-33, élément col13). Null si le mob n'a pas de ligne
        /// S_Monster chargée.</summary>
        public MonsterParser.MonsterDef? Def;
        public long ExpReward;
        public bool Alive = true;
        /// <summary>charIds ayant résolu un sort sur le mob (kill
        /// Limitless — anti-leech : un sort lancé est requis pour le
        /// point d'Exp Ultime, pas une auto-attaque). Vidé au
        /// reset/respawn du mob.</summary>
        public readonly HashSet<int> SkillHitCharIds = new();
        public DateTime DeadAt;
        /// <summary>Séquence de mort déjà émise (305, loot, exp) —
        /// posée par FinishMobDeathAsync : les coups/procs/sorts qui
        /// retombent sur le cadavre ne doivent pas rejouer la séquence
        /// (double 305 + double loot/exp constaté en live quand un proc
        /// multi-stroke schedulait KillMobAsync juste avant la mort par
        /// sort du même tick). Remise à zéro au respawn.</summary>
        public bool DeathEmitted;
        public int RespawnSeconds = 30;
        /// <summary>Famille de monde (world boss / 70-S / mutant).</summary>
        public Eden.Core.Game.WorldBoss.BossKind Kind =>
            Eden.Core.Game.WorldBoss.WorldBossCatalog.KindOf(MobId);
        /// <summary>World boss fixe 58001-58030 (hors cristal 58015).</summary>
        public bool IsWorldBoss => Kind ==
            Eden.Core.Game.WorldBoss.BossKind.WorldBoss;
        /// <summary>Index textindex d'annonce d'apparition (col19 de la
        /// ligne reborn_monsters, 0 = aucune).</summary>
        public int AnnounceTextId;
        /// <summary>Échéance du prochain respawn (posée à la mort,
        /// effacée au respawn) — compte à rebours des world boss.</summary>
        public DateTime? RespawnAt;
        /// <summary>Fin de vie d'un spawn dynamique (mutant), null sinon.</summary>
        public DateTime? ExpireAt;
        /// <summary>charIds ayant frappé le mob — destinataires du
        /// courrier de récompense des mutants.</summary>
        public readonly HashSet<int> Attackers = new();
        /// <summary>Né via Event_RebornMonster (SpawnGroup) — la route
        /// [mob_patrol_mode] est alors une marche unique vers
        /// l'objectif (élites d'invasion → cristal 據點), pas une
        /// boucle : MobWanderStepAsync l'ancre au dernier waypoint.
        /// Natif : le reborn né par event suit sa route comme tout
        /// monstre porteur d'un patrolId (CAI::Init roam mode 1).</summary>
        public bool EventSpawned;
        /// <summary>Clef d'événement à la mort (reborn_monsters col8 —
        /// CRebornMonster+0x34, OneDie → ExecuteCommand) : cristaux
        /// 據點 (S005 bleu=3 / rouge=4 — transition).</summary>
        public int DeathEventId;
        /// <summary>Clef d'événement à la naissance (col9 —
        /// CRebornMonster+0x38, ScriptEntry au Born) : mobs-« timer »
        /// d'invasion (56038/56039 → events spawn/despawn).</summary>
        public int BornEventId;
        /// <summary>Durée de vie du reborn en secondes (col11) — mort
        /// naturelle à born+TTL, évaluant la clef col8 (56039 :
        /// 420 s → event 2 ; 60019 : 1200 s → event 4).</summary>
        public int LifetimeSeconds;
        /// <summary>Respawn brut de la ligne (col4 — 0 = jamais : le
        /// cycle est porté par les events, pas par ScheduleRespawn).</summary>
        public int RowRespawnSeconds;
        /// <summary>Handle de la structure assiégée par l'élite en bout
        /// de route (cristal 據點 vivant ≤30 u) — 0 = pas de siège.</summary>
        public uint SiegeMobHandle;
        /// <summary>Prochain coup porté au cristal assiégé.</summary>
        public DateTime NextSiegeAt;
        public int PatrolId;
        /// <summary>Route de patrouille résolue ([mob_patrol_mode] de
        /// la scène, roam mode 1 natif) — null si le mob n'a pas de
        /// chemin tracé (roam mode 0, errance ±rayon du home).</summary>
        public SceneParser.PatrolRoute? Patrol;
        /// <summary>Indice du prochain waypoint de <see cref="Patrol"/>
        /// — séquentiel en boucle (SetNewGoalPatrol natif).</summary>
        public int PatrolIndex;
        /// <summary>Résolution PatrolId→route déjà tentée (évite un
        /// lookup de scène à chaque tick de wander).</summary>
        public bool PatrolResolved;
        /// <summary>Mob de donjon sur emplacement fixe : ni errance ni
        /// patrouille, il attend les joueurs.</summary>
        public bool Stationary;
        /// <summary>Id de la ligne reborn_monsters (col0) — c'est le
        /// « group » des Event_RebornMonster/MonsterAction/KillMonster
        /// natifs (monster+0x360 = spawn+0x24, désasm zone
        /// @0x823069) : Event_RebornMonster 33 5 7 cible les mobs
        /// issus de la ligne 33 de reborn_monsters.</summary>
        public int SpawnId;
        /// <summary>charId du joueur ciblé (riposte).</summary>
        public int TargetCharId;
        /// <summary>Position du mob au moment de l'aggro — l'ancre du
        /// leash natif : `CAI+0x44/0x48` est posé sur la position
        /// courante au premier contact (désasm AI msg 0x6a @0x84d7xxx),
        /// pas sur le point de spawn. `CheckOutOfGuardArea` compare
        /// la distance cible↔CE point à max_hunt_range.</summary>
        public float AggroX, AggroY;
        /// <summary>Dernier coup reçu — fin d'aggro si trop vieux.</summary>
        public DateTime LastHitAt;
        /// <summary>Dernier coup PORTÉ au joueur — règle native des
        /// instances non-boss : le mob ne déaggro jamais par distance,
        /// mais rend l'aggro s'il n'a pas pu toucher pendant ~3 min.</summary>
        public DateTime LastDamageDealtAt;
        /// <summary>Boucle de riposte en cours.</summary>
        public bool InCombat;
        /// <summary>Fin de stun — tant que l'horloge est sous cette
        /// date, la boucle de riposte saute le déplacement et l'attaque
        /// (effet des sorts à ResultSpellId, ex. Soul Leap→59280).
        /// État purement serveur : aucun paquet d'enchant n'est émis
        /// tant que son format natif n'est pas prouvé.</summary>
        public DateTime StunnedUntil;
        /// <summary>Fin du verrou d'IA posé par <c>Event_MonsterAction</c>
        /// (<c>CAI::SetEventAction</c> zone @0x084d5120 : CAI+0xec = fin
        /// de verrou). Tant que l'horloge est sous cette date le mob
        /// reste figé (ni wander, ni riposte) et un second ordre
        /// d'action est ignoré (natif : <c>if (CAI+0xec != 0) return</c>).</summary>
        public DateTime EventActionUntil;
        /// <summary>Vrai tant que le verrou d'action d'événement court.</summary>
        public bool EventActionLocked(DateTime now) => EventActionUntil > now;
        /// <summary>Enchant actif sur le mob — équivalent d'un
        /// <c>CEnchant</c> du <c>CEnchantMgr</c> natif (tout
        /// ILifeEntity en porte un, AddEnchant @0x847ea90). Jamais
        /// persisté : le registre meurt avec le mob/son respawn.</summary>
        public sealed class MobEnchant
        {
            public int EffectId;
            public int CasterId;        // charId du lanceur (attribution DoT)
            public int RestSeconds;     // -9 = durée propre, jamais expirée
            public short StackCount = 1;
            public short Level = 1;
            /// <summary>HP nets par tick de 1 s (3001 args[2]>0 régén /
            /// 3002 args[0] signé = DoT).</summary>
            public int HpPerTick;
            /// <summary>Verrous 2055 — bit0 NoMove, bit1 NoAttack
            /// (seuls pertinents côté mob ; vars 3-6 = sorts/items/
            /// sneak joueur).</summary>
            public int LockFlags;
            /// <summary>dur==-9 → icône cachée : pas de 299/300
            /// (gate natif IsHideIcon).</summary>
            public bool Hidden;
        }
        /// <summary>Registre des enchants actifs (effect_id → état) —
        /// vidé à la mort et au respawn.</summary>
        public readonly Dictionary<int, MobEnchant> Enchants = new();
        /// <summary>Au moins un enchant actif porte NoMove (2055 var1)
        /// — le mob n'avance ni ne wander.</summary>
        public bool MoveLocked
        { get { foreach (var e in Enchants.Values) if ((e.LockFlags & 1) != 0) return true; return false; } }
        /// <summary>Au moins un enchant actif porte NoAttack (2055 var2)
        /// — le mob ne riposte pas (la poursuite reste possible).</summary>
        public bool AttackLocked
        { get { foreach (var e in Enchants.Values) if ((e.LockFlags & 2) != 0) return true; return false; } }
        /// <summary>Position de spawn — centre du rayon de wander : le
        /// mob dérive autour de CE point (éparpillement initial inclus)
        /// sans jamais quitter la zone de son node.</summary>
        public float HomeX, HomeY;
        /// <summary>Prochain déplacement idle autorisé — wander natif :
        /// un mob vivant hors combat repart vers un point aléatoire
        /// proche de son spawn après un délai aléatoire.</summary>
        public DateTime NextWanderAt;
        /// <summary>Prochain tick de détection passive — cadence native
        /// de `CAI::DoSeekEnemy` (~2 s entre deux recherches circulaires
        /// de cible, timer CAI+0x88). Ignoré tant que le mob combat.</summary>
        public DateTime NextAggroCheckAt;
        /// <summary>Compteur d'actionId 302 (chaque action du mob prend
        /// un nouvel id — requis par le client pour rejouer les anims).</summary>
        public int ActCounter;
        /// <summary>charId du maître (0 = monstre sauvage). Non nul =
        /// invocation créée par CEC_SummonPet → CNode::BornPet natif.</summary>
        public int OwnerCharId;
        /// <summary>charId du joueur dont un item de quête a invoqué ce
        /// mob (CEC_SummonInEventArea natif : BornMonster puis
        /// BecomeFightingCandicate sur le lanceur). 0 = spawn de scène.
        /// Un mob invoqué ne respawne pas (BornMonster hors table
        /// reborn_monsters) — voir <see cref="NoRespawn"/>.</summary>
        public int SummonedByCharId;
        /// <summary>Mort définitive : pas de ScheduleRespawn (mobs
        /// invoqués par item de quête).</summary>
        public bool NoRespawn;
        /// <summary>Invocation stationnaire (BornTotemPet natif) — ne
        /// suit pas le maître et n'erre jamais.</summary>
        public bool IsTotem;
        /// <summary>effect_id S_Enchant qui a créé l'invocation — la
        /// suppression du statut désinvoque le pet (RemoveEnchantByID
        /// natif sur pet+0x3f0).</summary>
        public int SourceEnchantId;
        /// <summary>Prochain coup du pet (cadence assiste ~1,6 s).</summary>
        public DateTime NextPetAttackAt;
        /// <summary>Prochain essai de ramassage du pet en mode loot
        /// (recul après échec : sac plein, roll d'équipe…).</summary>
        public DateTime NextPetLootAt;
        /// <summary>Slot elf d'origine (0-9) quand le pet vient d'un
        /// œuf partner (CKFElf+0x3c natif — pet+0x40c chez le natif) ;
        /// -1 pour les invocations d'enchant.</summary>
        public short ElfLoc = -1;
        /// <summary>Pet partner invoqué (CElfPet natif, IsA==0x50b) —
        /// jamais ciblable ni attaquable : `IsMyEnemy` retourne false
        /// pour les 0x50b côté char (0x0840e490) comme côté mob
        /// (0x08492e10). Les invocations d'enchant (CPet 0x44b)
        /// restent, elles, attaquables.</summary>
        public bool IsElfPet => ElfLoc >= 0;
        /// <summary>Nom affiché du pet elf — surnom CKFElf+0x28 sinon
        /// nom du partner (pet+0x414 natif, envoyé dans le 298).</summary>
        public string? PetName;
        /// <summary>elf_id de la table native `elf` (CKFElf+4) —
        /// pour la persistance hp/statut au rappel.</summary>
        public int ElfId;
        /// <summary>Mode du pet (CElfPet+0x3ec natif — NCID 187) :
        /// 2 = passif (suit sans attaquer), sinon assiste.</summary>
        public int ElfMode;
        /// <summary>Cible verrouillée du pet (pet+0xd0 natif —
        /// `SetPetInstruction(100, cible)` via NCID 188 instr 1) :
        /// handle du mob à attaquer en priorité, 0 = aucune (repli
        /// sur l'assistance aux ennemis du maître).</summary>
        public uint PetLockTarget;
        /// <summary>Prochaine riposte autorisée du mob — la boucle de
        /// poursuite tick à 300 ms mais la frappe respecte la cadence
        /// S_Monster AttackSpeed (col17 ×100 ms).</summary>
        public DateTime NextRiposteAt;
        /// <summary>Wind-up d'un sort temporisé (S_Spell col26) —
        /// tant que cette échéance n'est pas passée le mob reste en
        /// place sans frapper ; le blast schedulé vérifie la
        /// génération de combat + AttackLocked (stun annule).</summary>
        public DateTime CastingUntil;
        /// <summary>Le mob a déjà porté une attaque de base sur sa
        /// cible courante (outcome ≠ miss) — RÈGLE PROPRIÉTAIRE :
        /// condition de tout lancer de sort (pas de flag équivalent
        /// trouvé dans le décompilé natif ; le séquencement natif
        /// n'arme le pick de sort qu'en fin de pipeline `Attack()`
        /// de <c>CAIFSMCombat</c>, zone @0x084daf40). Remis à zéro à
        /// chaque nouvel engagement (<see cref="BeginCombat"/>),
        /// désaggro, mort et respawn.</summary>
        public bool HasLandedMelee;

        /// <summary>Acquiert l'unique boucle de riposte de la cible active.</summary>
        public int? TryStartCombatLoop(int targetCharId)
        {
            lock (_combatGate)
            {
                if (!Alive || !InCombat || TargetCharId != targetCharId ||
                    _combatLoopRunning)
                    return null;
                _combatLoopRunning = true;
                return _combatGeneration;
            }
        }

        /// <summary>Vérifie que la tâche appartient toujours à
        /// l'engagement actif et que le mob est vivant.</summary>
        public bool IsCombatLoopCurrent(int generation, int targetCharId)
        {
            lock (_combatGate)
                return Alive && InCombat && TargetCharId == targetCharId &&
                       generation == _combatGeneration;
        }

        /// <summary>Vérifie la cible active avant un paquet différé de
        /// l'engagement (swing/twin/prise d'aggro).</summary>
        public bool IsCombatTarget(int targetCharId)
        {
            lock (_combatGate)
                return Alive && InCombat && TargetCharId == targetCharId;
        }

        /// <summary>Lecture du token à capturer pour un évènement
        /// différé de cet engagement.</summary>
        public int CombatGeneration
        {
            get { lock (_combatGate) return _combatGeneration; }
        }

        /// <summary>Vérifie une continuation différée sans accepter un
        /// nouvel engagement de la même cible.</summary>
        public bool IsCombatTarget(int generation, int targetCharId)
        {
            lock (_combatGate)
                return Alive && InCombat && TargetCharId == targetCharId &&
                       generation == _combatGeneration;
        }

        /// <summary>Pose ou change la cible d'aggro ; une nouvelle cible
        /// invalide les continuations asynchrones de l'engagement précédent.</summary>
        public void BeginCombat(int targetCharId, float anchorX, float anchorY)
        {
            lock (_combatGate)
            {
                if (!InCombat || TargetCharId != targetCharId)
                {
                    _combatGeneration++;
                    _combatLoopRunning = false;
                    AggroX = anchorX;
                    AggroY = anchorY;
                    LastDamageDealtAt = DateTime.UtcNow;
                    HasLandedMelee = false;   // nouveau combat → un
                                              // premier hit avant cast
                }
                InCombat = true;
                TargetCharId = targetCharId;
            }
        }

        /// <summary>Libère le combat si aucune nouvelle génération ne l'a
        /// remplacé. Retourne vrai uniquement pour l'engagement possédé.</summary>
        public bool FinishCombatLoop(int generation)
        {
            lock (_combatGate)
            {
                if (generation != _combatGeneration) return false;
                _combatLoopRunning = false;
                InCombat = false;
                TargetCharId = 0;
                return true;
            }
        }

        /// <summary>Annule la boucle et la cible avant d'émettre la mort.</summary>
        public void MarkDead()
        {
            lock (_combatGate)
            {
                Alive = false;
                _combatGeneration++;
                _combatLoopRunning = false;
                InCombat = false;
                TargetCharId = 0;
                HasLandedMelee = false;
            }
        }

        /// <summary>Annule une cible active sans tuer le mob (mort du
        /// joueur, migration ou suppression d'aggro).</summary>
        public void CancelCombat()
        {
            lock (_combatGate)
            {
                _combatGeneration++;
                _combatLoopRunning = false;
                InCombat = false;
                TargetCharId = 0;
                HasLandedMelee = false;
            }
        }

        /// <summary>Réinitialise les boucles au respawn sans conserver les
        /// continuations de l'engagement précédent.</summary>
        public void ResetCombatForRespawn()
        {
            CancelCombat();
            Alive = true;
            DeathEmitted = false;
            DeadAt = default;
            Enchants.Clear();        // le registre ne survit pas au respawn
            CastingUntil = default;  // ni le wind-up d'un sort en vol
            CastSerial++;            // ni le blast d'un cast interrompu
            CountdownLastCast = new DateTime[3];
            ResetPatience();         // BornMonster : mob+0x78 = max, 0x7c = 0
        }

        public byte NextAction()
        {
            var v = (byte)(++ActCounter & 0x7f);
            return v == 0 ? (byte)(ActCounter = 1) : v;
        }
    }

    /// <summary>Entité visible quelconque (monstre vivant ou NPC statique).</summary>
    public readonly record struct SeenEntity(uint Handle, int EntityId,
        bool IsMonster, float X, float Y, float Z, float Facing);

    private readonly ConcurrentDictionary<uint, LiveMob> _mobs = new();
    private readonly ConcurrentDictionary<uint, int> _npcHandles = new();
    /// <summary>Entités visibles par (nodeId, instance) — inst=0 =
    /// scène partagée ; les nœuds de type instance (IsAnyInstance
    /// natif) ont une population propre à chaque groupe d'équipe
    /// (CNodeGroup natif).</summary>
    private readonly ConcurrentDictionary<(int Node, int Inst),
        List<SeenEntity>> _nodes = new();
    /// <summary>Verrou des mutations de `_nodes` — les List&lt;SeenEntity&gt;
    /// sont mutées (Add/RemoveAll) depuis les events pendant que la
    /// sight les copie ; verrou unique suffisant (mutations rares).</summary>
    private readonly object _nodesLock = new();
    private uint _nextMobHandle = 0xFFFF_8000;   // famille native des mobs (0xffffabXX)
    private uint _nextNpcHandle = 0x88C8_0001;   // famille native des NPCs : 0x88c8/0x88c9 (handles rejoués ≥ 0x88c8cec1)

    /// <summary>Définitions S_Monster (injectées au démarrage).</summary>
    public IReadOnlyDictionary<int, MonsterParser.MonsterDef> MonsterDefs { get; set; }
        = new Dictionary<int, MonsterParser.MonsterDef>();

    /// <summary>kfIds d'attaque par préfixe de modèle (Motion.ini) —
    /// injecté au démarrage depuis GameData.AttackKfIds.</summary>
    public IReadOnlyDictionary<string, int[]> AttackKfIds { get; set; }
        = new Dictionary<string, int[]>();

    /// <summary>Masques Locate des templates NPC (id → S_Npc body[17],
    /// <c>CNpcData+0x38</c>) — injecté au démarrage depuis
    /// GameData.Npcs. Filtre natif de <c>GetAllNPCTemplate</c>
    /// @0x08368260 : mask!=0 sans le bit <c>EdenConfig.ZoneLocate</c>
    /// → NPC régional/event jamais spawné.</summary>
    public IReadOnlyDictionary<int, int> NpcLocateMasks { get; set; }
        = new Dictionary<int, int>();

    /// <summary>Règle native du filtre Locate : mask==0 → toujours
    /// visible, sinon le bit <paramref name="locate"/> doit être posé
    /// (US=5, cf. ConvertLocate @0x08589260).</summary>
    private static bool LocateVisible(int mask) =>
        mask == 0 ||
        ((mask >> (Eden.Core.EdenConfig.ZoneLocate & 0x1f)) & 1) != 0;

    /// <summary>Templates NPC de contenu US dont le masque Locate du
    /// dump est marqué pour une AUTRE région — la base S_Npc est une
    /// data internationale où le mask reflète la région d'origine du
    /// NPC, pas sa présence en version US. Critère de vérité retenu :
    /// le NPC possède une entrée dans <c>T_Npc(1).ini</c> du client US
    /// (seuls les NPC du contenu US y sont localisés — les 53 NPC de
    /// festival CN/HK/JP/KR/TH n'y figurent pas). Audit complet :
    /// docs/temp-plans/2026-10-24-lot10-prompt10-G-notes.md.
    /// Exemples : Gisèle 51283 (mask KR), Aven Knight 50890 (CN),
    /// Class Master 50955 (VN), téléporteurs 53288/53292 (JP).</summary>
    private static readonly HashSet<int> UsContentNpcIds = new()
    {
        50430, 50590, 50889, 50890, 50955, 51254, 51255, 51256,
        51273, 51274, 51283, 51327, 51429, 51529, 51531, 51536,
        51537, 51769, 51770, 53287, 53288, 53290, 53292, 53342,
        53348, 53349, 53350, 53351, 53352, 53385, 53390, 53391,
        53437, 53481, 53482, 53499, 53500,
    };

    /// <summary>Un template NPC est spawnable si son masque Locate
    /// inclut la localisation du serveur (0 = pas d'entrée S_Npc ou
    /// mask=0 → visible) OU s'il fait partie du contenu US localisé
    /// du client (cf. <see cref="UsContentNpcIds"/>) — le masque du
    /// dump marque alors une région d'origine obsolète.</summary>
    public bool NpcSpawnable(int npcId) =>
        !NpcLocateMasks.TryGetValue(npcId, out var mask) ||
        LocateVisible(mask) || UsContentNpcIds.Contains(npcId);

    /// <summary>Injecte les tables de définitions lues au populate
    /// (S_Monster HP/attaque/niveau, kfIds d'attaque Motion.ini,
    /// masques Locate S_Npc). Idempotent : n'écrase que les tables
    /// encore vides — appelée au boot avant le préchauffage, puis en
    /// filet de sécurité à la première entrée (tests sans boot
    /// complet).</summary>
    public void BindGameData(GameDataService gameData)
    {
        if (_nodeTypes.Count == 0)
            foreach (var n in gameData.Nodes) _nodeTypes[n.Id] = n.Type;
        if (MonsterDefs.Count == 0 && gameData.Monsters.Count > 0)
            MonsterDefs = gameData.Monsters.ToDictionary(m => m.Id);
        if (AttackKfIds.Count == 0 && gameData.AttackKfIds.Count > 0)
            AttackKfIds = gameData.AttackKfIds;
        // Masques Locate des NPC (S_Npc body[17] → CNpcData+0x38) —
        // filtre natif GetAllNPCTemplate : NPC région/event jamais né.
        if (NpcLocateMasks.Count == 0 && gameData.Npcs.Count > 0)
            NpcLocateMasks = gameData.Npcs.ToDictionary(n => n.Id,
                n => n.LocateMask);
    }

    /// <summary>Test « case de mur invisible » (grille .smp) sur la
    /// scène — posé au boot (<c>SceneService.IsSceneBlocked</c>) ; null =
    /// pas de contrôle (tests sans boot).</summary>
    public Func<SceneParser.Scene, float, float, bool>? WallCheck { get; set; }

    /// <summary>Position d'un mob éparpillé autour de (cx, cy) dans ±10 u
    /// (native : positions aléatoires autour du point). Dans un donjon
    /// ~40 % des tirages tombaient dans la roche (cases .smp bloquées) :
    /// on retire jusqu'à 12 fois, puis on se replie sur le point de
    /// scène lui-même (jamais bloqué, vérifié sur les 120 donjons).
    /// </summary>
    private (float X, float Y) ScatterAround(SceneParser.Scene? scene,
        float cx, float cy)
    {
        for (var t = 0; t < 12; t++)
        {
            var x = cx + (float)(Random.Shared.NextDouble() * 20 - 10);
            var y = cy + (float)(Random.Shared.NextDouble() * 20 - 10);
            if (scene == null || WallCheck == null ||
                !WallCheck(scene, x, y))
                return (x, y);
        }
        return (cx, cy);
    }

    /// <summary>Entité immobile (aucune attaque ni portée : coffre,
    /// machine, œuf, cible de quête à 1 PV…) née seule sur sa ligne
    /// [reborn_monsters] : pas d'éparpillement, position et facing
    /// exacts de la scène (le point est placé à la main par les
    /// level designers).</summary>
    private static bool IsPinnedSpawn(
        Eden.Core.Data.Parsers.MonsterParser.MonsterDef? def, int count) =>
        count == 1 && def != null &&
        def.AttackPower <= 0 && def.AttackRangeMax <= 0;

    /// <summary>Peuple un (node,instance) depuis sa scène (idempotent).
    /// Chaque entrée reborn_monsters crée Count mobs éparpillés autour
    /// du point. ATTENTION : la liste retournée est mutable (events la
    /// mutent) — les énumérateurs doivent la copier (ToArray) avant
    /// itération.</summary>
    public IReadOnlyList<SeenEntity> EnsureNodePopulated(int nodeId,
        int inst, SceneParser.Scene? scene)
    {
        lock (_nodesLock)
            return _nodes.GetOrAdd((nodeId, inst),
                _ => PopulateNode(nodeId, inst, scene));
    }

    /// <summary>Construction de la liste d'entités d'un (node,inst) —
    /// appelée sous `_nodesLock` (spawn initial + mobs/NPC de la
    /// scène). Les mobs portent `InstanceKey` pour que les accès
    /// par handle (GetMob) retrouvent leur instance.</summary>
    /// <summary>Nodes sans monstres (villes). À étendre au besoin.</summary>
    private readonly Dictionary<int, int> _nodeTypes = new();

    /// <summary>Scène à emplacements de mobs FIXES (comportement d'IA, PAS la notion de « donjon » :
    /// celle-ci est <c>MapKind</c>) : c_node type 5, 7, 11 et 18 — [mob_patrol_mode] tous « fixe ».</summary>
    public bool HasFixedMobLayout(int nodeId) =>
        _nodeTypes.TryGetValue(nodeId, out var t) && t is 5 or 7 or 11 or 18;

    private static readonly HashSet<int> PeacefulNodes = new() { 1, 94, 768 };

    private List<SeenEntity> PopulateNode(int nodeId, int inst,
        SceneParser.Scene? scene)
    {
        {
            var list = new List<SeenEntity>();
            if (scene == null) return list;
            foreach (var m in scene.Monsters)
            {
                // H7 : villes pacifiques (Aven = node 1, « newer towns »
                // 94/768) — pas de monstres, le client n'a pas de
                // modèle à y afficher.
                if (PeacefulNodes.Contains(nodeId)) break;
                // Ligne dormante (reborn+0x38 == 0 : cristaux rouges,
                // escouades légion, mobs d'event) — n'apparaît que via
                // Event_RebornMonster. Preuve : GetAllRebornMonsterInfo.
                if (!m.InitiallyActive) continue;
                MonsterDefs.TryGetValue(m.MonsterId, out var def);
                // Filtre Locate natif (CMonsterData+0xb8, S_Monster
                // idx59) : mobs région/event hors de notre locate.
                if (def != null && !LocateVisible(def.LocateMask))
                    continue;
                var count = m.Count > 0 ? m.Count : 1;
                for (var i = 0; i < count; i++)
                {
                    // Éparpillement natif : positions aléatoires dans ~±10u.
                    // Exception : entité immobile unique (coffre, machine,
                    // œuf/cible de quête…) — posée EXACTEMENT au point
                    // [reborn_monsters] avec le facing de la scène.
                    var pinned = IsPinnedSpawn(def, count);
                    var (x, y) = pinned ? (m.X, m.Y)
                        : ScatterAround(scene, m.X, m.Y);
                    // Donjon : groupe à emplacements fixes — le mob i
                    // naît sur le point i de sa route (pas d'éparpillement
                    // dans la roche) et n'en bouge plus.
                    var fixedSpot = false;
                    if (HasFixedMobLayout(nodeId) &&
                        scene.Patrols.TryGetValue(m.PatrolId, out var pr) &&
                        pr.IsStationary && pr.Waypoints.Count > 0)
                    {
                        var wp = pr.Waypoints[Math.Min(i,
                            pr.Waypoints.Count - 1)];
                        x = wp.X; y = wp.Z;
                        fixedSpot = true;
                    }
                    var h = Interlocked.Increment(ref _nextMobHandle) - 1;
                    var mob = new LiveMob
                    {
                        Handle = h, MobId = m.MonsterId, NodeId = nodeId,
                        InstanceKey = inst,
                        X = x, Y = y, Z = m.Z,
                        Facing = pinned ? m.Facing : (float)(Random.Shared.NextDouble() * Math.PI * 2),
                        MaxHp = def != null ? (int)Math.Min(def.HP, int.MaxValue) : 150,
                        AttackPower = def?.AttackPower ?? 20,
                        AttackMotionId = def?.AttackMotionId ?? 1,
                        Def = def,
                        // Pool d'attaques du modèle (préfixe du code
                        // modèle : M025010 -> m025) — repli 1021 comme
                        // le défaut natif de GetAttackID.
                        AttackKfIds = def != null &&
                            AttackKfIds.TryGetValue(
                                MotionParser.ModelPrefix(def.ModelCode),
                                out var kfs) && kfs.Length > 0
                                ? kfs
                                : new[] { MotionParser.DefaultAttackKfId },
                        Level = def?.Level ?? 1,
                        ExpReward = def?.ExpReward ?? 0,
                        RespawnSeconds = m.RespawnSeconds > 0 ? m.RespawnSeconds : 30,
                        RowRespawnSeconds = m.RespawnSeconds,
                        PatrolId = m.PatrolId,
                        Stationary = fixedSpot,
                        SpawnId = m.AreaId,
                        AnnounceTextId = m.AnnounceTextId,
                        DeathEventId = m.DeathEventId,
                        BornEventId = m.BornEventId,
                        LifetimeSeconds = m.LifetimeSeconds,
                        HomeX = x, HomeY = y,
                        // Premier errand décalé par mob — sinon tout un
                        // spawn bouge en même temps (visuel artificiel).
                        NextWanderAt = DateTime.UtcNow +
                            TimeSpan.FromSeconds(3 + Random.Shared.Next(10)),
                    };
                    mob.Hp = mob.MaxHp;
                    _mobs[h] = mob;
                    list.Add(new SeenEntity(h, m.MonsterId, true,
                        x, y, m.Z, mob.Facing));
                }
            }
            foreach (var n in scene.Npcs)
            {
                // Filtre Locate natif des templates NPC — les NPC
                // région/event (festivals, 網咖…) ne naissent pas sur
                // notre locate (GetAllNPCTemplate @0x08368260).
                if (!NpcSpawnable(n.NpcId)) continue;
                var h = Interlocked.Increment(ref _nextNpcHandle) - 1;
                _npcHandles[h] = n.NpcId;              // template id (dialog/shop)
                // eid = npcId du template. Les "eid négatifs" des captures
                // natives (-15276 = 0xC456) sont en fait les npcId 50260
                // lus en s16 signé — le client les relit en u16 et
                // résout le template/modèle/nom. Le LocalId scène ne
                // sert PAS au client (vérifié : ErrTID sinon).
                list.Add(new SeenEntity(h, n.NpcId, false,
                    n.X, n.Y, n.Z, n.Facing));
            }
            return list;
        }
    }

    /// <summary>Alloue un handle NPC hors-liste-de-node
    /// (0x88C8xxxx) et l'associe au template — pour les entités
    /// propres à un perso (NPCs du hall de famille visité, statues)
    /// qui ne vivent pas dans <c>_nodes</c> : le node 501/502 est
    /// partagé entre visiteurs de familles différentes, chacun voit
    /// les NPCs de SA famille (<c>SFamily::InitFamilyNpc</c> natif —
    /// les NPCs de hall appartiennent à l'instance visitée).</summary>
    public uint AllocNpcHandle(int npcId)
    {
        var h = Interlocked.Increment(ref _nextNpcHandle) - 1;
        _npcHandles[h] = npcId;
        return h;
    }

    /// <summary>Handle -> entité (mob vivant ou id NPC).</summary>
    public bool TryGetEntityId(uint handle, out int entityId)
    {
        if (_mobs.TryGetValue(handle, out var m)) { entityId = m.MobId; return true; }
        return _npcHandles.TryGetValue(handle, out entityId);
    }

    public LiveMob? GetMob(uint handle) =>
        _mobs.TryGetValue(handle, out var m) ? m : null;

    /// <summary>Tous les mobs vivants du monde — scan du tick
    /// d'enchants (1 s). Snapshot de ConcurrentDictionary : safe hors
    /// boucle en lecture.</summary>
    public IEnumerable<LiveMob> AllMobs => _mobs.Values;

    /// <summary>Mobs vivants d'un (node,instance) (combat, AoE,
    /// déaggro).</summary>
    public IEnumerable<LiveMob> MobsOf(int nodeId, int inst) =>
        _mobs.Values.Where(m => m.NodeId == nodeId &&
                                m.InstanceKey == inst);

    /// <summary>Mobs d'un groupe de spawn (reborn_monsters col0) dans
    /// un (node,instance) — cible des Event_MonsterAction/KillMonster
    /// natifs (CNode::MonsterActionByHandle itère les monstres dont
    /// monster+0x360 == groupId).</summary>
    public IEnumerable<LiveMob> GroupMobs(int nodeId, int inst,
        int spawnId) =>
        _mobs.Values.Where(m => m.NodeId == nodeId &&
                                m.InstanceKey == inst &&
                                m.SpawnId == spawnId);

    /// <summary>Instancie les mobs d'une ligne reborn_monsters
    /// (Event_RebornMonster natif). Ajoute les entrées à la liste
    /// d'entités visibles du node pour que la sight les voie.
    /// Retourne les entités créées (pour le broadcast ClientSeeMob).</summary>
    public IReadOnlyList<SeenEntity> SpawnGroup(int nodeId, int inst,
        SceneParser.MonsterSpawn spawn, SceneParser.Scene? scene = null)
    {
        var created = new List<SeenEntity>();
        List<SeenEntity> entities;
        lock (_nodesLock)
            entities = _nodes.GetOrAdd((nodeId, inst),
                _ => new List<SeenEntity>());
        MonsterDefs.TryGetValue(spawn.MonsterId, out var def);
        // Même filtre Locate qu'au populate — le natif saute la ligne
        // reborn au chargement : un event ne peut pas la ressusciter.
        if (def != null && !LocateVisible(def.LocateMask))
            return created;
        var count = spawn.Count > 0 ? spawn.Count : 1;
        for (var i = 0; i < count; i++)
        {
            var pinned = IsPinnedSpawn(def, count);
            var (x, y) = pinned ? (spawn.X, spawn.Y)
                : ScatterAround(scene, spawn.X, spawn.Y);
            var h = Interlocked.Increment(ref _nextMobHandle) - 1;
            var mob = new LiveMob
            {
                Handle = h, MobId = spawn.MonsterId, NodeId = nodeId,
                InstanceKey = inst,
                X = x, Y = y, Z = spawn.Z,
                Facing = pinned ? spawn.Facing : (float)(Random.Shared.NextDouble() * Math.PI * 2),
                MaxHp = def != null ? (int)Math.Min(def.HP, int.MaxValue) : 150,
                AttackPower = def?.AttackPower ?? 20,
                AttackMotionId = def?.AttackMotionId ?? 1,
                Def = def,
                AttackKfIds = def != null &&
                    AttackKfIds.TryGetValue(
                        MotionParser.ModelPrefix(def.ModelCode),
                        out var kfs) && kfs.Length > 0
                        ? kfs
                        : new[] { MotionParser.DefaultAttackKfId },
                Level = def?.Level ?? 1,
                ExpReward = def?.ExpReward ?? 0,
                RespawnSeconds = spawn.RespawnSeconds > 0
                    ? spawn.RespawnSeconds : 30,
                RowRespawnSeconds = spawn.RespawnSeconds,
                EventSpawned = true,
                PatrolId = spawn.PatrolId,
                SpawnId = spawn.AreaId,
                AnnounceTextId = spawn.AnnounceTextId,
                DeathEventId = spawn.DeathEventId,
                BornEventId = spawn.BornEventId,
                LifetimeSeconds = spawn.LifetimeSeconds,
                HomeX = x, HomeY = y,
                NextWanderAt = DateTime.UtcNow +
                    TimeSpan.FromSeconds(3 + Random.Shared.Next(10)),
            };
            mob.Hp = mob.MaxHp;
            _mobs[h] = mob;
            var e = new SeenEntity(h, spawn.MonsterId, true,
                x, y, spawn.Z, mob.Facing);
            lock (_nodesLock)
                entities.Add(e);
            created.Add(e);
        }
        return created;
    }

    /// <summary>Fait naître un monstre hors table reborn_monsters
    /// (mutants : <c>BornMonster</c> natif sans ligne de scène). Le mob
    /// ne respawne pas (<see cref="LiveMob.NoRespawn"/>) et expire à
    /// <paramref name="expireAt"/>. Null si le monstre n'a pas de
    /// définition S_Monster. L'appelant doit avoir peuplé le node
    /// (<see cref="EnsureNodePopulated"/>) pour que le spawn ne
    /// masque pas le peuplement initial.</summary>
    public (LiveMob Mob, SeenEntity Entity)? SpawnDynamic(int nodeId,
        int inst, int mobId, float x, float y, float z,
        DateTime expireAt)
    {
        if (!MonsterDefs.TryGetValue(mobId, out var def)) return null;
        var h = Interlocked.Increment(ref _nextMobHandle) - 1;
        var mob = new LiveMob
        {
            Handle = h, MobId = mobId, NodeId = nodeId,
            InstanceKey = inst, X = x, Y = y, Z = z,
            Facing = (float)(Random.Shared.NextDouble() * Math.PI * 2),
            MaxHp = (int)Math.Min(def.HP, int.MaxValue),
            AttackPower = def.AttackPower,
            AttackMotionId = def.AttackMotionId,
            Def = def,
            AttackKfIds = AttackKfIds.TryGetValue(
                    MotionParser.ModelPrefix(def.ModelCode), out var kfs)
                && kfs.Length > 0
                ? kfs : new[] { MotionParser.DefaultAttackKfId },
            Level = def.Level, ExpReward = def.ExpReward,
            RespawnSeconds = 0, NoRespawn = true, ExpireAt = expireAt,
            HomeX = x, HomeY = y,
            NextWanderAt = DateTime.UtcNow +
                TimeSpan.FromSeconds(3 + Random.Shared.Next(10)),
        };
        mob.Hp = mob.MaxHp;
        _mobs[h] = mob;
        var e = new SeenEntity(h, mobId, true, x, y, z, mob.Facing);
        lock (_nodesLock)
            _nodes.GetOrAdd((nodeId, inst), _ => new List<SeenEntity>())
                .Add(e);
        return (mob, e);
    }

    /// <summary>Retire un mob de la liste de sight et du registre
    /// (mutant expiré ou cadavre purgé).</summary>
    public void Remove(LiveMob mob)
    {
        lock (_nodesLock)
            if (_nodes.TryGetValue((mob.NodeId, mob.InstanceKey),
                    out var entities))
                entities.RemoveAll(e => e.Handle == mob.Handle);
        _mobs.TryRemove(mob.Handle, out _);
    }

    /// <summary>Statut des 26 boss fixes (vivant / mort + échéance de
    /// respawn / node non peuplé) pour le compte à rebours.</summary>
    public IReadOnlyList<Eden.Core.Game.WorldBoss.WorldBossCatalog.StatusRow>
        WorldBossStatus() =>
        Eden.Core.Game.WorldBoss.WorldBossCatalog.BuildStatus(
            _mobs.Values.Where(m => m.IsWorldBoss && m.OwnerCharId == 0)
                .Select(m => (m.MobId, m.Alive, m.RespawnAt)));

    /// <summary>Retire les mobs d'un groupe de spawn (Event_KillMonster
    /// natif). Retourne les mobs retirés pour le broadcast unsight.</summary>
    public IReadOnlyList<LiveMob> DespawnGroup(int nodeId, int inst,
        int spawnId)
    {
        var gone = GroupMobs(nodeId, inst, spawnId).ToList();
        lock (_nodesLock)
            if (_nodes.TryGetValue((nodeId, inst), out var entities))
                foreach (var m in gone)
                    entities.RemoveAll(e => e.Handle == m.Handle);
        foreach (var m in gone)
            _mobs.TryRemove(m.Handle, out _);
        return gone;
    }

    /// <summary>Hauteur de terrain approximative en (x,y) — déléguée à
    /// <see cref="SceneParser.Scene.FloorZ"/> (position/event_area/mob
    /// le plus proche — plus dense que les seules entités). Le natif
    /// échantillonne la heightmap du moteur. Sans ça un pet invoqué à
    /// z=0 rend ~5 u sous le terrain (« pet sous la map » en live).</summary>
    public float GroundHeightAt(SceneParser.Scene? scene, float x,
        float y, float fallback = 0f)
    {
        var z = scene?.FloorZ(x, y) ?? 0f;
        return z != 0f ? z : fallback;
    }

    /// <summary>Invoque un pet/totem lié à un joueur — miroir de
    /// `CNode::BornPet`/`BornTotemPet` natifs (zone/decompiled.c
    /// @0x84b51f0/@0x84b4490) : monstre sans respawn ni wander, tué
    /// avec son enchant source. L'entrée rejoint la liste de sight du
    /// node — le prochain tick UpdateSightAsync la pousse en 298.</summary>
    public SeenEntity SpawnPet(int nodeId, int inst, int monsterId,
        int ownerCharId,
        float x, float y, float z, float facing, bool isTotem,
        int enchantId)
    {
        MonsterDefs.TryGetValue(monsterId, out var def);
        var h = Interlocked.Increment(ref _nextMobHandle) - 1;
        var mob = new LiveMob
        {
            Handle = h, MobId = monsterId, NodeId = nodeId,
            InstanceKey = inst,
            X = x, Y = y, Z = z,
            Facing = facing,
            MaxHp = def != null ? (int)Math.Min(def.HP, int.MaxValue) : 150,
            AttackPower = def?.AttackPower ?? 20,
            AttackMotionId = def?.AttackMotionId ?? 1,
            Def = def,
            AttackKfIds = def != null &&
                AttackKfIds.TryGetValue(
                    MotionParser.ModelPrefix(def.ModelCode),
                    out var kfs) && kfs.Length > 0
                    ? kfs
                    : new[] { MotionParser.DefaultAttackKfId },
            Level = def?.Level ?? 1,
            ExpReward = 0,
            RespawnSeconds = 0,
            SpawnId = 0,
            HomeX = x, HomeY = y,
            OwnerCharId = ownerCharId,
            IsTotem = isTotem,
            SourceEnchantId = enchantId,
            // Pas de wander — le suivi du maître passe par le tick pets.
            NextWanderAt = DateTime.MaxValue,
        };
        mob.Hp = mob.MaxHp;
        _mobs[h] = mob;
        var e = new SeenEntity(h, monsterId, true, x, y, z, facing);
        lock (_nodesLock)
            _nodes.GetOrAdd((nodeId, inst),
                _ => new List<SeenEntity>()).Add(e);
        return e;
    }

    /// <summary>Pets invoqués d'un joueur (OwnerCharId != 0).</summary>
    public IEnumerable<LiveMob> PetsOf(int ownerCharId) =>
        _mobs.Values.Where(m => m.OwnerCharId == ownerCharId);

    /// <summary>Fait apparaître un monstre hostile ciblant un joueur —
    /// `CNode::BornMonster` + `BecomeFightingCandicate` natifs
    /// (DestingBornMonster @0x0810ce70 : event destiny 4/6, spawn à la
    /// position du joueur, aggro immédiate, respawn 0).</summary>
    public LiveMob SpawnHostileMob(int nodeId, int inst, int monsterId,
        int targetCharId, float x, float y, float z, float facing)
    {
        MonsterDefs.TryGetValue(monsterId, out var def);
        var h = Interlocked.Increment(ref _nextMobHandle) - 1;
        var mob = new LiveMob
        {
            Handle = h, MobId = monsterId, NodeId = nodeId,
            InstanceKey = inst,
            X = x, Y = y, Z = z, Facing = facing,
            MaxHp = def != null ? (int)Math.Min(def.HP, int.MaxValue) : 150,
            AttackPower = def?.AttackPower ?? 20,
            AttackMotionId = def?.AttackMotionId ?? 1,
            Def = def,
            AttackKfIds = def != null &&
                AttackKfIds.TryGetValue(
                    MotionParser.ModelPrefix(def.ModelCode),
                    out var kfs) && kfs.Length > 0
                    ? kfs
                    : new[] { MotionParser.DefaultAttackKfId },
            Level = def?.Level ?? 1,
            ExpReward = def != null ? (int)Math.Min(def.ExpReward,
                int.MaxValue) : 0,
            RespawnSeconds = 0,
            SpawnId = 0,
            HomeX = x, HomeY = y,
            NextWanderAt = DateTime.MaxValue,
            // CMonster+0xd0 natif = le joueur déclencheur (owner —
            // utilisé aussi pour le despawn d'expiration param 363).
            OwnerCharId = targetCharId,
        };
        mob.Hp = mob.MaxHp;
        _mobs[h] = mob;
        lock (_nodesLock)
            _nodes.GetOrAdd((nodeId, inst), _ => new List<SeenEntity>())
                .Add(new SeenEntity(h, monsterId, true, x, y, z, facing));
        // BecomeFightingCandicate natif — aggro immédiate sur le
        // joueur qui a déclenché l'event.
        mob.BeginCombat(targetCharId, x, z);
        return mob;
    }

    /// <summary>Retire un pet du monde (sight + registre) — renvoie
    /// false si le handle n'est pas un pet.</summary>
    public bool DespawnPet(uint handle)
    {
        if (!_mobs.TryRemove(handle, out var mob) || mob.OwnerCharId == 0)
            return false;
        lock (_nodesLock)
            if (_nodes.TryGetValue((mob.NodeId, mob.InstanceKey),
                    out var entities))
                entities.RemoveAll(e => e.Handle == handle);
        return true;
    }

    /// <summary>Fait apparaître un NPC (Event_BornNpc natif —
    /// CNode::BornNpc). Retourne handle + entité de sight.</summary>
    public SeenEntity SpawnNpc(int nodeId, int inst, int npcId,
        float x, float y, float z, float facing)
    {
        var h = Interlocked.Increment(ref _nextNpcHandle) - 1;
        _npcHandles[h] = npcId;
        var e = new SeenEntity(h, npcId, false, x, y, z, facing);
        lock (_nodesLock)
            _nodes.GetOrAdd((nodeId, inst),
                _ => new List<SeenEntity>()).Add(e);
        return e;
    }

    /// <summary>Déplace un NPC de scène (marche scriptée — l'entité de
    /// sight est remplacée à la nouvelle position ; le client anime la
    /// marche via le 326 cur→dst émis par l'appelant, même convention
    /// que le wander des mobs). Retourne l'ancienne position, ou null
    /// si le handle n'est pas un NPC de ce (node,instance).</summary>
    public (float X, float Y)? MoveNpc(int nodeId, int inst, uint handle,
        float x, float y, float facing)
    {
        lock (_nodesLock)
        {
            if (!_nodes.TryGetValue((nodeId, inst), out var entities))
                return null;
            var i = entities.FindIndex(e => e.Handle == handle && !e.IsMonster);
            if (i < 0) return null;
            var old = entities[i];
            entities[i] = old with { X = x, Y = y, Facing = facing };
            return (old.X, old.Y);
        }
    }

    /// <summary>NPCs vivants du (node,instance) dont le template vaut
    /// <paramref name="npcId"/> (cible des commandes par TID).</summary>
    public IReadOnlyList<SeenEntity> NpcsOfTemplate(int nodeId, int inst,
        int npcId)
    {
        lock (_nodesLock)
        {
            if (!_nodes.TryGetValue((nodeId, inst), out var entities))
                return Array.Empty<SeenEntity>();
            return entities.Where(e => !e.IsMonster && e.EntityId == npcId)
                .ToList();
        }
    }

    /// <summary>Retire les NPCs du (node,instance) dont le template
    /// vaut `npcId` (Event_KillNpc natif). Retourne les handles
    /// retirés.</summary>
    public IReadOnlyList<uint> DespawnNpcs(int nodeId, int inst,
        int npcId)
    {
        List<uint> gone;
        lock (_nodesLock)
        {
            if (!_nodes.TryGetValue((nodeId, inst), out var entities))
                return Array.Empty<uint>();
            gone = entities
                .Where(e => !e.IsMonster && e.EntityId == npcId)
                .Select(e => e.Handle).ToList();
            foreach (var h in gone)
                entities.RemoveAll(e => e.Handle == h);
        }
        foreach (var h in gone)
            _npcHandles.TryRemove(h, out _);
        return gone;
    }

    /// <summary>Copie des clés (nodeId, inst) peuplées pour une
    /// instance — sert à l'occupancy-check du reset (163) et au
    /// ménage (disband / déco solo).</summary>
    public IReadOnlyList<int> InstanceNodes(int inst) =>
        _nodes.Keys.Where(k => k.Inst == inst)
            .Select(k => k.Node).ToList();

    /// <summary>Détruit toutes les instances d'un groupe —
    /// <c>CNodeGroup::RemoveAllNode</c> natif : les entités de chaque
    /// (node,inst) sont supprimées de la sight et du registre
    /// (mobs ET npcs). Sans effet sur inst 0 (partagé).</summary>
    public void RemoveInstances(int inst)
    {
        if (inst == 0) return;
        var keys = _nodes.Keys.Where(k => k.Inst == inst).ToList();
        lock (_nodesLock)
            foreach (var k in keys)
            {
                if (!_nodes.TryRemove(k, out var entities)) continue;
                foreach (var e in entities)
                    if (e.IsMonster)
                        _mobs.TryRemove(e.Handle, out _);
                    else
                        _npcHandles.TryRemove(e.Handle, out _);
            }
        // Mobs de cette instance sans entrée sight (hostiles event).
        foreach (var m in _mobs.Values
                     .Where(m => m.InstanceKey == inst).ToList())
            _mobs.TryRemove(m.Handle, out _);
    }

    /// <summary>Réanime un monstre mort.</summary>
    public void Respawn(LiveMob mob)
    {
        mob.Hp = mob.MaxHp;
        mob.RespawnAt = null;
        mob.Attackers.Clear();
        mob.ResetCombatForRespawn();
        // Le natif respawne au point de spawn (CNode::RebornMonster) —
        // sinon le mob réapparaissait là où son cadavre a disparu.
        mob.X = mob.HomeX;
        mob.Y = mob.HomeY;
        // Le wander repart depuis la position courante vers le home —
        // délai initial comme au spawn.
        mob.NextWanderAt = DateTime.UtcNow +
            TimeSpan.FromSeconds(3 + Random.Shared.Next(10));
    }
}
