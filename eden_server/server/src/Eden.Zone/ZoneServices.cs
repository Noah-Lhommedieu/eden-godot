using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>
/// Contexte partagé des services/dépôts de la zone — passé aux modules
/// de handlers pour éviter les listes de paramètres kilométriques et
/// garder Program.cs limité au bootstrap.
/// </summary>
public sealed record ZoneServices
{
    public const int FamilyBankCapacity =
        Eden.Core.Database.Repositories.FamilyBankRepository
            .StorageSlotCapacity;

    // Données statiques (tables .ini décodées)
    public required GameDataService GameData { get; init; }
    public required SceneService Scenes { get; init; }

    // Registres runtime
    public required ZoneSessionManager Sessions { get; init; }
    public required ZoneEntityRegistry Entities { get; init; }
    /// <summary>Monde vivant partagé — monstres/NPC instanciés par node.</summary>
    public ZoneLiveWorld Live { get; init; } = new();
    public required Eden.Zone.Handlers.ZonePacketHandlers LegacyHandlers { get; init; }

    // Services de gameplay
    public required CombatService Combat { get; init; }
    public required SpellService Spells { get; init; }
    public required MissionService Missions { get; init; }
    public required GmCommandService Gm { get; init; }
    public required TeamService Teams { get; init; }
    public required GuildService Guilds { get; init; }
    public required LoverService Lovers { get; init; }
    public required FriendService Friends { get; init; }
    public required AuctionService Auctions { get; init; }
    public required EnchantService Enchants { get; init; }
    public required MonsterService Monsters { get; init; }
    public required MonsterAiService MonsterAi { get; init; }
    public required LootService Loot { get; init; }
    public required InventoryService Inventory { get; init; }
    public required BankService Bank { get; init; }
    public required TradeService Trade { get; init; }
    public required ProgressionService Progression { get; init; }
    /// <summary>Exp Ultime & Limitless Pass (chantier 3 —
    /// nullable : les chemins de test n'instancient pas).</summary>
    public Eden.Core.Game.Limitless.LimitlessService? Limitless
    { get; init; }
    public required ShopService Shops { get; init; }
    public required ElfService Elfs { get; init; }
    public required DungeonService Dungeons { get; init; }
    public required TalentService Talents { get; init; }
    public required AchievementService Achievements { get; init; }
    public required TerritoryWarService Territory { get; init; }
    /// <summary>Arène Colosseum — mode/schedId courant, files par
    /// catégorie et matchs runtime (miroir CColosseumFactory zone +
    /// mission natifs, voir ColosseumService).</summary>
    public required ColosseumService Colosseum { get; init; }
    /// <summary>Instances versus 5v5 — file + matchs runtime
    /// (miroir CVersusInstanceFactory mission natif).</summary>
    public required VersusService Versus { get; init; }
    /// <summary>Battle Royale FFA 30 joueurs — file + instances
    /// (miroir CBattleRoyaleFactory mission natif).</summary>
    public required BattleRoyaleService BattleRoyale { get; init; }
    public required MatchmakingService Matchmaking { get; init; }
    public required RankingService Rankings { get; init; }
    /// <summary>Registre des salles personnelles — miroir
    /// `CZoneServer+0x564` rempli par les pushes ZMis 779.</summary>
    public required RoomRegistry Rooms { get; init; }
    /// <summary>Cache des classements — miroir `CZoneServer+0x5e0`
    /// rempli par les pushes ZMis 763.</summary>
    public required RankInfoCache RankInfos { get; init; }
    /// <summary>Moteur des autels Eden Crystal/Gem — tables
    /// edenfr_account.*lottery, tirage pondéré et paliers hebdo.</summary>
    public required Eden.Core.Game.Lottery.LotteryService Lottery { get; init; }
    /// <summary>Cube Horadric — recettes S_ItemChest, groupes col75 de
    /// S_Item, simulation CheckCubeCosts / ExecuteCubeOp natifs.</summary>
    public required Eden.Core.Game.Cube.CubeService Cube { get; init; }
    /// <summary>Boîte magique — conversion d'énergie + scouring de
    /// combos (CheckMagicBoxCosts/DoMagicBox natifs, S_ItemCombo).</summary>
    public required Eden.Core.Game.MagicBox.MagicBoxService MagicBox
        { get; init; }

    /// <summary>Sertissage de gemmes (inlay) — UseItemToItemControl
    /// natif case 0x2d : validation cible/gemme, sélection de la
    /// première socket libre, roll d'ouverture à la création
    /// (ChangeEmbeddedAmount @0x809baf0).</summary>
    public required Eden.Core.Game.Inlay.InlayService Inlay
        { get; init; }

    /// <summary>Étals joueurs (market booth) — miroir du CStall
    /// natif char+0x7ec : goods/viewers par vendeur, validations
    /// CheckBeginStall* et verrou d'achat.</summary>
    public required Eden.Core.Game.Vending.StallService Stalls
        { get; init; }

    /// <summary>Ordres de travail (métiers 12-17) — ToWork/
    /// CheckToCollect/CheckToCombine/EndCollect/EndCombine natifs :
    /// validation du démarrage, tirages de produits et plan de
    /// consommation (aucun I/O — le flux zone orchestre).</summary>
    public required Eden.Core.Game.Work.WorkOrderService Works
        { get; init; }

    /// <summary>Station combine (service 10, CCombineOp 0x27) —
    /// CheckCombineItem/DoCombineItem natifs : recettes
    /// S_FamilyCombine catégorie 6, cast 0x28 unités, produit ×1.</summary>
    public required Eden.Core.Game.Work.CombineStationService
        CombineStation { get; init; }

    /// <summary>Sacs de fortune — registre map&lt;bag_id, BagDef&gt;
    /// natif (CZoneServer+0x124) rempli par les pushes ZW 26
    /// (FortuneUpdating = verrou pendant les phases 0→2).</summary>
    public FortuneBagRegistry FortuneBags { get; } = new();

    /// <summary>Mana Infusion (EquipTrain) — sélection NCID 230,
    /// localisation NCID 570, EXP d'entraînement au kill
    /// (charExp × S_Parameter 496), 15 canaux de stats.
    /// Miroir de CCharacter::GainEquipExp/SetEquipTrainItem natifs.
    /// </summary>
    public required EquipTrainService EquipTrain { get; init; }

    /// <summary>Lien interne vers le MissionServer natif — les commandes
    /// DBA texte (mail, MissionServiceOp…) et les requêtes de dialogue
    /// y transitent. Null si le lien n'est pas encore établi.</summary>
    public Eden.Zone.Links.ZoneMissionLink? Mission { get; set; }

    /// <summary>Lien interne vers le WorldServer natif — commandes
    /// texte monde (CNC_ZW_WorldServerTextCommand 636, ex. « exchange
    /// item » de CNE_CZ_ZoneServerExchangeItemPin @0x8280fc0).</summary>
    public Eden.Zone.Links.ZoneWorldLink? World { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : applique les
    /// commandes d'usage d'un enchant consommable (S_Enchant blocs
    /// cmd/vars — HP/MP/téléport/chaîne) sur l'EntryState live du
    /// joueur. Arguments : session, charId, enchantId, token.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        int, CancellationToken, Task>? ItemUseApply { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : pré-contrôle
    /// <c>CanExecute</c> des commandes d'usage d'un enchant (natif
    /// DoUseItem : CheckExecute AVANT consommation — refus = item
    /// conservé). Aujourd'hui : SummonInEventArea (node requis, clé de
    /// position — texte 0x5c0). Arguments : session, charId, enchantId,
    /// token ; retourne false = usage refusé.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        CancellationToken, Task<bool>>? ItemUseCanExecute { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : garde mort/vie
    /// de l'usage d'item. Mort : seuls les items de revive passent ;
    /// vivant : un item de revive est refusé (non gaspillé). Arguments :
    /// session, charId, itemId, enchantId d'usage ; false = refus.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int, int,
        CancellationToken, Task<bool>>? ItemUseLifeGate { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : canalisation
    /// d'usage d'un item temporisé (S_Item col20 → CItemData+0x54 ≠ 0 —
    /// `DoUseItem` @0x80b8a10 : émet 407 ClientUseItemTime, mémorise le
    /// control en char+0x90 et planifie CEventUseItemTime à +N
    /// décisecondes ; à l'échéance l'usage réel s'exécute). Arguments :
    /// session, charId, itemId (template), serial, bag, slot, useTimeDs,
    /// complete (l'usage à exécuter sur l'item re-lu à l'échéance),
    /// token. Retour : true = canalisation démarrée ou déjà en cours —
    /// le handler s'arrête sans ack (char+0x90 ≠ 0 → return 0 natif).</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int, long,
        short, short, int,
        Func<Eden.Core.Game.Inventory.ItemEntity, CancellationToken,
            Task>,
        CancellationToken, Task<bool>>? ItemUseChannel { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : ops de partage
    /// de quête du C→S 87 (op 6/7 + garde des ops &lt; 6). Arguments :
    /// session, charId, op, missionId, arg (flag d'acceptation de l'op
    /// 7), token ; retourne true si l'op est consommée.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, short, int,
        int, CancellationToken, Task<bool>>? MissionShareOp { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : ouverture d'un
    /// sac chanceux (ChanceLuckyBagStart natif — item_id dans
    /// [50001,51001)) sur l'EntryState live. Arguments : session,
    /// charId, bagItemId, token. Retour : true = l'usage a réussi et
    /// l'item peut être consommé (le natif valide AVANT de consommer
    /// — CheckExecute @0x084ec520).</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        CancellationToken, Task<bool>>? LuckyBagUse { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : ouverture
    /// instantanée d'un sac de fortune (OpenFortuneBag natif — item_id
    /// dans [49001,50001)∪[51001,53001)). Arguments : session, charId,
    /// bagItemId, isBound de l'item sac consommé, token. Retour : true
    /// = ouverture effective — sinon l'item n'est PAS consommé
    /// (check slots libres natif).</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        bool, CancellationToken, Task<bool>>? FortuneBagUse { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : invalide le
    /// cache de pools d'animation d'arme (CCharKfm natif — pools
    /// single/dual par kind) quand l'équipement change. Sans lui les
    /// anims d'auto-attaque restent figées sur l'arme précédente.
    /// Argument : SessionId de la connexion concernée.</summary>
    public Action<Guid>? InvalidateWeaponAnim { get; set; }

    // Dépôts PostgreSQL
    public required CharacterRepository Characters { get; init; }
    public required InventoryRepository InventoryRepo { get; init; }
    public required MailRepository Mail { get; init; }
    public required ITicketRepository Tickets { get; init; }
    public required ClassRepository Classes { get; init; }
    public required IAdvancedAbilityRepository AdvancedAbilities { get; init; }
    /// <summary>Recettes métiers apprises (`player_life_skill` —
    /// remplit char+0xb84 natif).</summary>
    public required ILifeSkillRepository LifeSkills { get; init; }
    public required MissionRepository MissionRepo { get; init; }
    /// <summary>État lottery des persos + points hebdo du compte
    /// (edenfr_db lottery/in_game_lottery/new_lottery +
    /// player_lottery_point) + tables de récompenses edenfr_account.</summary>
    public required Eden.Core.Database.Repositories.ILotteryRepository LotteryRepo { get; init; }
    /// <summary>player_works — les 11 CWork natifs (char+0xb20)
    /// persistés par CWork::SaveWork/ResetWork.</summary>
    public required Eden.Core.Database.Repositories.WorkRepository WorksRepo
        { get; init; }
    /// <summary>player_destiny_road + destiny_vit — le bloc SDestinyRoad
    /// natif (char+0xbc8) et la vitalité (exec+0x6b6).</summary>
    public required DestinyRoadRepository DestinyRepo { get; init; }
    /// <summary>fast_dress — les 72 records natifs (24 clés × 3 pages)
    /// des tenues rapides (maps char+0x58c/char+0x5a4).</summary>
    public required Eden.Core.Database.Repositories.FastDressRepository
        FastDressRepo { get; init; }
    /// <summary>player_fortune_bag — compteurs d'ouverture par compte
    /// (map char+0x574 native).</summary>
    public required Eden.Core.Database.Repositories.FortuneBagRepository
        FortuneRepo { get; init; }
    /// <summary>custom_class_data + current_custom_data_index —
    /// les 3 cartes de classe personnalisée (char+0xd9c/char+0xd98
    /// natifs ; Load/SaveCustomClassData de la mission).</summary>
    public required Eden.Core.Database.Repositories.CustomClassRepository
        CustomClassRepo { get; init; }
    /// <summary>daily_awards + daily_awards_set — état du calendrier
    /// de connexion par perso (char+0xdd0..0xdf0 natifs) et les
    /// flags globaux du système (zone +0x638/+0x639).</summary>
    public required Eden.Core.Database.Repositories.DailyAwardRepository
        DailyAwardRepo { get; init; }
    /// <summary>new_player_guide — compteurs trigger/closed par id
    /// de guide (maps char+0xc18 natives, NCID 218).</summary>
    public required Eden.Core.Database.Repositories.NewPlayerGuideRepository
        GuideRepo { get; init; }
    /// <summary>Stats battlefield month/career (162 → 482).</summary>
    public required Eden.Core.Database.Repositories.BattlefieldRepository
        BattlefieldRepo { get; init; }
    /// <summary>class_lifelong + player_characters.lifelong_gold —
    /// compteurs par groupe waken (map char+0xc58) et médailles
    /// Crystal Cross (char+0xc54), NCIDs 554/555.</summary>
    public required Eden.Core.Database.Repositories.LifelongRepository
        LifelongRepo { get; init; }
    /// <summary>player_characters_achievement + rank/prestige —
    /// progression des succès (map exec+0x528 mission natif,
    /// portée zone).</summary>
    public required Eden.Core.Database.Repositories.AchievementRepository
        AchievementRepo { get; init; }
    /// <summary>Lecture `prestige` pour les conditions
    /// `If_CharReputation` des événements pay (NCID 253).</summary>
    public required Eden.Core.Database.Repositories.PayEventRepository
        PayEventRepo { get; init; }
    /// <summary>player_evaluate / team_evaluate / player_node_times
    /// — records et compteurs du gladiator trial (NCID 255/607/608).</summary>
    public required Eden.Core.Database.Repositories.EvaluateRepository
        EvaluateRepo { get; init; }
    /// <summary>Registre des instances CEvaluate actives —
    /// CEvaluateFactory natif (map evalId→CEvaluate). Muté
    /// exclusivement sur la boucle de zone.</summary>
    public required Eden.Core.Game.Evaluate.EvaluateService Evaluates
        { get; init; }
    /// <summary>File de recrutement d'instance (NCID 220) —
    /// board CTeamManager natif map&lt;node,multimap&lt;role,charId&gt;&gt;.
    /// Muté exclusivement sur la boucle de zone.</summary>
    public Eden.Core.Game.Services.RecruitBoard Recruits { get; }
        = new();

    /// <summary>S_Partner.ini — définitions des familiers de combat
    /// indexées par id de l'item œuf (CPartnerData natif, ex. 23993
    /// Combat Bunny Sprite).</summary>
    public IReadOnlyDictionary<int,
        Eden.Core.Data.Parsers.PartnerParser.PartnerDef> Partners
        { get; set; } = new Dictionary<int,
            Eden.Core.Data.Parsers.PartnerParser.PartnerDef>();

    /// <summary>S_PartnerGrow.ini — stats par niveau indexées par
    /// grow (CPartnerGrowData natif : attack/hit/crit/atkspd ×10).</summary>
    public IReadOnlyDictionary<int,
        Eden.Core.Data.Parsers.PartnerParser.GrowDef> PartnerGrows
        { get; set; } = new Dictionary<int,
            Eden.Core.Data.Parsers.PartnerParser.GrowDef>();

    /// <summary>Table native `elf` de edenfr_db — slots de familiers
    /// persistés (nullable : non configuré hors zone).</summary>
    public Eden.Core.Database.Repositories.ElfRepository? ElfRepo
        { get; set; }

    /// <summary>Boucle acteur de la zone (créée dans Program.cs après
    /// le flow) — scheduling des événements différés sur le fil
    /// unique : casts, twins de combat, retours de mob.</summary>
    public ZoneLoop? Loop { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : usage d'un
    /// item œuf kind 60 → AddElf natif (INSERT elf + 413). Arguments :
    /// session, charId, item œuf (ses champs embedded/extra_combo/
    /// train portent les données d'un pet re-scellé — AddElf les lit),
    /// token → vrai si l'elf a été créé.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int,
        Eden.Core.Game.Inventory.ItemEntity,
        CancellationToken, Task<bool>>? ElfUseItem { get; set; }
    /// <summary>Callback enregistré par ZoneEntryFlow : usage d'un
    /// livre de sorts pet (UseType 6 → <c>UseItemToElfControl</c>
    /// natif — enchant 6035-6038 ChangePet*Spell[+]). Arguments :
    /// session, charId, enchantId, loc elf (field8 du paquet 19),
    /// token → vrai si le sort a été appris (item consommé).</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int, int,
        CancellationToken, Task<bool>>? ElfBookApply { get; set; }
    /// <summary>Callback enregistré par ZoneEntryFlow : delta atomique
    /// sur <c>player_characters.lifelong_gold</c> + miroir live
    /// <c>EntryState.LifelongGold</c> (boucle) — utilisé par les
    /// achats de fame shop priceType 5 (ShopHandlers stateless).
    /// Arguments : session, charId, delta, token → nouveau total.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        CancellationToken, Task<int>>? LifelongGoldDelta { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : item déplacé
    /// → <c>CItemFactory::UpdateFastDressFromInfo</c> natif — met à
    /// jour la position source des records fast_dress qui pointent le
    /// serial. Arguments : charId, serial, conType (0 sac/1 équipe),
    /// loc, index (n° sac / groupe d'équipement).</summary>
    public Func<int, int, int, int, int, Task>? FastDressItemMoved
        { get; set; }
    /// <summary>Callback enregistré par ZoneEntryFlow :
    /// <c>player_spells</c> modifié (learn/upgrade) → invalide le
    /// cache <c>EntryState.SpellLevels</c> du perso. Argument :
    /// charId.</summary>
    public Action<int>? SpellLevelsDirty { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : injecte un
    /// NPC de hall de famille dans la vue du perso (entité rejouée
    /// 298, miroir de <c>CNode::BornNPC</c> dans
    /// <c>SFamily::InitFamilyNpc</c>) puis émet le 298 si la session
    /// est fournie. Arguments : state (EntryState via
    /// GmCommandService.Context.State), tid S_Npc, x, y, z, facing →
    /// handle sight alloué (0 si l'état n'est pas un EntryState).</summary>
    public Func<object?, int, float, float, float, float, uint>?
        HallNpcInject { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : retire le NPC
    /// de hall rejoué le plus proche de (x, y) — sortie de vue 269
    /// émise par l'appelant si besoin. Arguments : state (EntryState),
    /// x, y, rayon max → handle retiré (0 si aucun dans le rayon).</summary>
    public Func<object?, float, float, float, uint>?
        HallNpcRemove { get; set; }
    /// <summary>Callback enregistré par ZoneEntryFlow : verrous
    /// d'action agrégés des statuts actifs du perso (commande
    /// S_Enchant 2055 → compteurs CEC_State* natifs). Bits :
    /// 1=NoMove, 2=NoAttack, 4=NoUseSkill Kungfu, 8=NoUseSkill Magic,
    /// 16=NoUseItem. Argument : charId → bitmask.</summary>
    public Func<int, int>? StateLocksOf { get; set; }
    /// <summary>Callback enregistré par ZoneEntryFlow : réémission du
    /// mot d'état complet du perso — 302 <c>CNC_CZ_ClientFlagModify</c>
    /// (<c>NodifyFlagChange</c> natif @0x084e3c00) à l'acteur + CanSee
    /// après une mutation du bit ride (montée/descente par item,
    /// CheckRide au déplacement d'item). L'<c>EntryState</c> vit sur
    /// la boucle — les handlers stateless y délèguent. Arguments :
    /// session, charId, token.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int,
        CancellationToken, Task>? RideFlagNotify { get; set; }

    /// <summary>Montée à cheval complète — déléguée à la boucle :
    /// pose le CEnchant de ride réel (299 + statut + persistance)
    /// puis l'état natif (RideStates + 302 bit9 + 283 mask=2 +
    /// barre 545). Arguments : session, charId, serial item, id
    /// d'enchant de ride.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int,
        int, CancellationToken, Task>? RideApply { get; set; }

    /// <summary>Descente de monture complète — séquence
    /// DismountAsync (retrait statut/300 + 302 + 283 mask2=0 + 545
    /// vide + purge DB). Arguments : session, charId, token.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int,
        CancellationToken, Task>? RideRemove { get; set; }

    /// <summary>player_enchants — statuts actifs persistés
    /// (Insert/Update/Delete Enchant natifs) pour restauration à la
    /// reconnexion.</summary>
    public required IPlayerEnchantRepository PlayerEnchants { get; init; }
    public required FamilyBankRepository FamilyBank { get; init; }
    public SocialRepository? Social { get; init; }

    /// <summary>edenfr_member.tb_user — soldes item mall (pvalues/bonus) :
    /// utilisé par les commandes GM de crédit de points. null si la
    /// base membre n'est pas configurée.</summary>
    public UserRepository? Users { get; init; }

    public required int NodeId { get; init; }

    /// <summary>Rate d'exp perso appliquée aux kills de mobs —
    /// config `EXP_RATE` (défaut 100 pour les tests). Les exp de
    /// quête/récompenses n'y passent pas (comportement demandé :
    /// rate sur l'exp "que donne les mobs").</summary>
    public double ExpRate { get; init; } = 100.0;

    /// <summary>Rate d'exp de CLASSE des kills — config
    /// `CLASS_EXP_RATE` (défaut = ExpRate).</summary>
    public double ClassExpRate { get; init; } = 100.0;

    /// <summary>État de ride par perso — charId → (serial de l'item
    /// monture, enchant id). Équivalent des champs natifs
    /// CCharacter+0x7f4 (serial monture) / +0x7f8 (ride en cours) :
    /// posé à l'usage de l'item (commande 2134 de son enchant),
    /// retiré par CheckRide quand l'item quitte son conteneur ou
    /// que le joueur redescend.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<int,
        (int ItemSerial, int EnchantId)> RideStates { get; } = new();

    /// <summary>Enchants de session non persistés par perso —
    /// charId → (effectId → level). Miroir des CEnchant natifs posés
    /// avec flag 0x1000 (procs d'équipement, talents AA) : ils vivent
    /// dans le CEnchantMgr en mémoire, jamais dans player_enchants —
    /// et contribuent quand même aux stats. Le stage « buffs » du
    /// StatPipeline les lit en complément des lignes persistées ;
    /// purgé à la déconnexion (le statut meurt avec le perso).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<int,
        System.Collections.Concurrent.ConcurrentDictionary<int, int>>
        SessionEnchants { get; } = new();

    /// <summary>Trésors au sol (items droppés quand l'inventaire est
    /// plein) — miroir du registre CTreasure natif par node
    /// (voir <see cref="GroundTreasureService"/>).</summary>
    public GroundTreasureService GroundTreasure { get; } = new();
    /// <summary>Rolls de loot d'équipe en cours
    /// (<see cref="RollTreasureService"/> — RollitemManager natif).</summary>
    public RollTreasureService RollTreasure { get; } = new();

    /// <summary>Certificats de classe — miroir des vecteurs
    /// `CCharacter+0xaf0/+0xafc/+0xb08/+0xb14` natifs (slots de
    /// licences, enchants posés, combos). Construit dans Program
    /// avec le repo `player_license` + `EquipSetPool`.</summary>
    public required Eden.Core.Game.Classes.LicenseService Licenses
    { get; init; }

    /// <summary>Callback enregistré par ZoneEntryFlow : dépose un item
    /// au sol en trésor quand l'inventaire du joueur déborde (fallback
    /// natif DropTreasureOnFloor). Arguments : session, itemId, count.
    /// Les handlers stateless l'appellent quand AddItemAsync échoue
    /// pour "Inventory full".</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int, int, Task>?
        TreasureDrop { get; set; }

    /// <summary>Callback enregistré par ZoneEntryFlow : pousse les
    /// 392 ClientTeamMemberPosition (positions des coéquipiers migrés)
    /// à un perso — <c>CPlayerFactory::NotifyTeamMemberPos</c> natif
    /// @0x80fc7c0, appelé par <c>NotifyTeamData</c>
    /// (<c>OnMigrationDone</c>) et <c>RecvWorldTeamJoinTeam</c>.
    /// Arguments : session destinataire, charId, token.</summary>
    public Func<Eden.Core.Networking.Session.NetSession, int,
        CancellationToken, Task>? TeamMemberPositions { get; set; }

    /// <summary>Cooldowns d'items d'usage — miroir de la map native
    /// CCharacter+0x6a8 (voir <see cref="ItemCooldownService"/>).</summary>
    public ItemCooldownService ItemCooldowns { get; } = new();

    /// <summary>Downtimes de sorts par personnage — miroir des
    /// <c>CSpellMaster</c> natifs (+0x14 dernier sort, +0x18/+0x1c
    /// expiration ; <c>PutInDownTime</c> @0x84f3820 /
    /// <c>CheckInDownTime</c> @0x84f3720).</summary>
    public SpellCooldownService SpellCooldowns { get; } = new();

    /// <summary>Pipeline de stats ordonné — contributions post-
    /// équipement : titres/permanents → costumes → buffs/procs
    /// (étapes 3-5 de l'ordre demandé). L'étape « equipment » est
    /// appliquée directement par <c>ProfileStatEmitter</c> (il a déjà
    /// l'agrégat en main pour le snapshot combat). Enregistrer ici
    /// les sources additionnelles quand leurs données arrivent —
    /// voir <see cref="Eden.Core.Game.Stats.StatPipeline"/>.</summary>
    public Eden.Core.Game.Stats.StatPipeline StatPipeline { get; } =
        new();

    /// <summary>Version du modèle par perso — équivalent de
    /// CCharacter+0x4dc : incrémenté à chaque `ClientModelModify`
    /// (283) émis pour ce perso (ride, changement de classe,
    /// équipement). Le client rejette les paquets dont la version
    /// n'a pas avancé.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<int, byte>
        ModelVersions { get; } = new();

    /// <summary>Notifie la mission du changement de stock d'un item
    /// (146 zone→mission) — miroir de `CGlobalInfo::
    /// UpdateQuestItemStatus` natif @0x8096b10 : à chaque insert/
    /// delete du miroir d'inventaire, les objectifs ItemCollect sont
    /// réévalués. No-op si le lien mission est coupé. À appeler sur
    /// TOUTES les mutations d'inventaire (achat, vente, loot, échange,
    /// retrait stockage, consommation…).</summary>
    public void NotifyItemChanged(int charId, int itemId)
    {
        if (itemId > 0 && Mission is { Connected: true } link)
            _ = link.SendQuestItemChangedAsync((uint)charId, itemId);
    }
}
