using Eden.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Database;
using Eden.Core.Hot;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Core.Networking;
using Npgsql;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Eden.Zone;
using Eden.Zone.Handlers;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.Zone — Serveur de Zone/Instance C# (.NET 8)
//  Reversé fidèlement depuis lapis::ZoneServer (Port 5201 / 10166)
//  Gère les entités vivantes, le combat, les déplacements, les quêtes et l'IA.
//  Les handlers CNE_* sont groupés par domaine dans Handlers/*Handlers.cs.
// -----------------------------------------------------------------------------

var port = EdenConfig.ZonePort;
var nodeId = EdenConfig.NodeId;
var zoneId = EdenConfig.ZoneId;
var ticketIp = EdenConfig.TicketIp;
var ticketPort = EdenConfig.TicketPort;
var worldLinkIp = EdenConfig.WorldLinkIp;
var worldLinkPort = EdenConfig.WorldLinkPort;
// NpgsqlDataSource par rôle (DB-2 : base unique edenfr, un search_path par
// rôle game/account/member) — pools propriétaires partagés par tous les repos.
var db = DbConnectionFactory.Create(DbRole.Game);
var accountDb = DbConnectionFactory.Create(DbRole.Account);
var memberDb = DbConnectionFactory.Create(DbRole.Member);
await EdenObservability.StartAsync("zone", db);
var dataDir = EdenConfig.DataDir
              ?? Path.Combine(AppContext.BaseDirectory, "data");

EdenLog.Write("=================================================");
EdenLog.Write("        EDEN ETERNAL - ZONE SERVER (.NET 8)      ");
EdenLog.Write("=================================================");
EdenLog.Write($"[Zone] Port d'écoute : {port} (TCP)");
EdenLog.Write($"[Zone] Instance NodeId : {nodeId}");
EdenLog.Write($"[Zone] Base de données : {DbConnectionFactory.Describe(DbRole.Game)}");

// --- Données décodées (server_us/Data/db_decoded) ---
// balance.ini + EDEN_BAL_* : ratios custom (param.<id>, AgiToMoveSpeed,
// MoveSpeedCapPct, BlockRateCapPct) — chargé AVANT les tables pour que
// les formules voient les surcharges.
Eden.Core.Game.GameBalance.Load(dataDir);
Eden.Core.Game.ServerConfig.Init();   // server.config.txt (taux/événements, rechargé à chaud)
var gameData = new GameDataService(dataDir);
gameData.LoadAll();
if (gameData.MissingFiles.Count > 0)
    EdenLog.Write($"[Zone] {gameData.MissingFiles.Count} .ini absents " +
        $"de '{dataDir}' : {string.Join(", ", gameData.MissingFiles.Take(8))}…");
gameData.RequireLoaded();
EdenLog.Write($"[Zone] Données: {gameData.Items.Count} items, " +
    $"{gameData.Monsters.Count} monstres, {gameData.Spells.Count} sorts");

// Manifest gamedata (phase 4d) — empreinte SHA-256 du corpus *.ini
// servie au handshake de version ; le dataDir réel de CE service est
// amorcé (son repli AppContext/data n'est pas visible d'EdenConfig).
Eden.Core.Data.GameDataManifest.PrimeDirectory(dataDir);
EdenLog.Write(Eden.Core.Data.GameDataManifest.Current is { } manifest
    ? $"[Zone] gamedata manifest sha256={manifest.Hex} ({manifest.FileCount} .ini, {manifest.Directory})"
    : "[Zone] gamedata manifest : absent — la vérification stricte ne pourra pas comparer");

// --- Composition des services et dépôts ---
var inventoryRepo = new InventoryRepository(db);
var characterRepository = new CharacterRepository(db, HotWiring.CreateHotStore("Zone"));
var inventoryService = new InventoryService(inventoryRepo, gameData.Items);
var monsterSvc = MonsterService.LoadFromDataDir(dataDir);
// Tri des références mobs (S_Monster cols 40-49) selon les tables réelles :
// SpellIds = sorts actifs (S_Spell), ClassDropIds = tables BP
// (S_ClassDropItem) — les plages natives se chevauchent.
monsterSvc.ResolveRefs(
    gameData.Spells.Select(sp => sp.Id).ToHashSet(),
    gameData.ClassDrops.Select(t => t.Id).ToHashSet());

var sceneSvc = new SceneService(monsterSvc);
// Tables de récompenses des autels (edenfr_account) — indisponible =
// service coupé natif (texte 0x1c5c à l'ouverture), jamais bloquant.
var lotteryRepo = new Eden.Core.Database.Repositories.LotteryRepository(
    db, accountDb);
var lotterySvc = new Eden.Core.Game.Lottery.LotteryService(
    lotteryRepo, gameData.Parameters);
try { await lotterySvc.LoadAsync(); }
catch (Exception ex)
{ EdenLog.Write($"[Zone] Tables lottery edenfr_account injoignables : {ex.Message}"); }

// Cube Horadric — CHoradricCubeQuery natif (S_ItemChest.ini) +
// LoadCubeGroup2Item (index inversé de la col75 de S_Item).
var cubeSvc = new Eden.Core.Game.Cube.CubeService(
    gameData.Items, gameData.Enchants);
try
{
    var chests = Eden.Core.Data.Parsers.ShopAndMiscParsers
        .ParseItemChests(Path.Combine(dataDir, "S_ItemChest.ini"));
    // S_ItemChestExtra.ini : coffres EDENFR absents de l'oracle US
    // (ex. coffres cosmétiques 10001-10090) — overlay prioritaire.
    var chestExtra = Eden.Core.Data.ReforgedDataLocator.FindFile(
        "S_ItemChestExtra.ini", dataDir, AppContext.BaseDirectory);
    if (chestExtra != null)
        foreach (var m in Eden.Core.Data.Parsers.ShopAndMiscParsers
                     .ParseItemChests(chestExtra))
        {
            chests.RemoveAll(c => c.Id == m.Id);
            chests.Add(m);
        }
    cubeSvc.LoadRecipes(chests);
}
catch (Exception ex)
{ EdenLog.Write($"[Zone] S_ItemChest.ini illisible : {ex.Message}"); }

var magicBoxSvc = new Eden.Core.Game.MagicBox.MagicBoxService(
    gameData.Items, gameData.ItemCombos, gameData.Parameters);

// Rates d'exp — EXP_RATE / CLASS_EXP_RATE (défaut 100 pour les
// tests demandés : "rate à 100, je veux que mes tests soient
// rapides"). Appliquées uniquement aux kills de mobs.
var expRate = EdenConfig.ExpRate;
var classExpRate = EdenConfig.ClassExpRate;
EdenLog.Write($"[Zone] exp_rate={expRate} class_exp_rate={classExpRate}");

// EnchantService via LoadFromDataDir : applique l'overlay Reforged
// (c_enchant/c_itemmallenchant) — la table que le client joué affiche
// est canonique pour les valeurs d'enchant (lot 12 : S_Enchant 54813
// « 2122|-3 » vs c_enchant « 2083|||0.5 » rendait Sacred Duty sans
// effet). gameData.Enchants reste la table native de référence.
var enchantSvc = EnchantService.LoadFromDataDir(dataDir);
var services = new ZoneServices
{
    NodeId = nodeId,
    ExpRate = expRate,
    ClassExpRate = classExpRate,
    GameData = gameData,
    Scenes = sceneSvc,
    Sessions = new ZoneSessionManager(),
    Entities = new ZoneEntityRegistry(),
    LegacyHandlers = new ZonePacketHandlers(characterRepository, nodeId),

    Combat = CombatService.LoadFromDataDir(dataDir),
    Spells = SpellService.LoadFromDataDir(dataDir),
    Missions = MissionService.LoadFromDataDir(dataDir),
    Gm = GmCommandService.LoadFromDataDir(dataDir),
    Teams = new TeamService(),
    Guilds = new GuildService(),
    Lovers = new LoverService(),
    Friends = new FriendService(),
    Auctions = CreateAuctionService(db),
    Enchants = enchantSvc,
    Monsters = monsterSvc,
    MonsterAi = MonsterAiService.LoadFromDataDir(dataDir),
    Loot = new LootService(gameData.DropTables, gameData.ClassDrops),
    Inventory = inventoryService,
    Bank = new BankService(new BankRepository(db), gameData.Items),
    Trade = new TradeService(
        new TradeRepository(db), inventoryRepo, inventoryService,
        // Delegates or : check de solde dans ModifyGoldAsync (la tx
        // SettleGoldAsync du repo garde l'atomicité réelle).
        characterRepository.GetGoldAsync,
        characterRepository.SetGoldAsync),
    Progression = new ProgressionService(characterRepository, gameData.Levels),
    // Limitless — le .ini des récompenses du pass (limitless_pass.ini)
    // vit dans DATA_DIR comme balance.ini ; EDEN_LIMITLESS_PASS_INI
    // peut pointer ailleurs. Rechargé à chaud via //ll_reload.
    Limitless = new Eden.Core.Game.Limitless.LimitlessService(db,
        EdenConfig.LimitlessPassIni
        ?? Path.Combine(dataDir, "limitless_pass.ini")),
    // Mana Infusion — SetEquipTrainItem/GainEquipExp natifs
    // (S_EquipTrain × multiplicateurs S_Parameter 496-506).
    EquipTrain = new EquipTrainService(inventoryRepo, gameData),
    Shops = new ShopService(inventoryService, gameData.Stores,
        gameData.Items, gameData.Parameters,
        characterRepository.GetGoldAsync,
        characterRepository.SetGoldAsync,
        // Delta atomique `gold + @d` (AddGoldAsync) — le pair
        // get→set absolu perdait les écritures concurrentes.
        characterRepository.AddGoldAsync),
    Elfs = ElfService.LoadFromDataDir(dataDir),
    Dungeons = DungeonService.LoadFromDataDir(dataDir),
    Talents = TalentService.LoadFromDataDir(dataDir),
    Achievements = AchievementService.LoadFromDataDir(dataDir),
    ItemMall = new ItemMallService(),
    Territory = new TerritoryWarService(),
    Colosseum = new ColosseumService(),
    Versus = new VersusService(),
    BattleRoyale = new BattleRoyaleService(),
    Matchmaking = new MatchmakingService(new TeamService()),
    Stalls = new Eden.Core.Game.Vending.StallService(),
    Rankings = new RankingService(),
    Rooms = new Eden.Core.Game.Services.RoomRegistry(),
    RankInfos = new Eden.Core.Game.Services.RankInfoCache(),
    Lottery = lotterySvc,
    Cube = cubeSvc,
    MagicBox = magicBoxSvc,
    Inlay = new Eden.Core.Game.Inlay.InlayService(),
    Works = new Eden.Core.Game.Work.WorkOrderService(gameData,
        new Eden.Core.Game.Inlay.InlayService()),
    CombineStation = new Eden.Core.Game.Work.CombineStationService(
        gameData),

    Characters = characterRepository,
    InventoryRepo = inventoryRepo,
    Mail = new MailRepository(db),
    Tickets = HotWiring.CreateTicketRepository("Zone", db),
    Classes = new ClassRepository(db),
    // Certificats de classe — LicenseService miroir des vecteurs
    // natifs +0xaf0/+0xafc/+0xb08/+0xb14 (SetCurrentClassLicense
    // @0x82714f0 / InvokeSpellStonesSet @0x8418aa0).
    Licenses = new Eden.Core.Game.Classes.LicenseService(
        new Eden.Core.Database.Repositories.LicenseRepository(
            db),
        gameData.Items.ToDictionary(i => i.Id),
        gameData.EquipSetPool ?? new Eden.Core.Data.Parsers
            .EquipSetParser.EquipSetPool(
                Array.Empty<Eden.Core.Data.Parsers.EquipSetParser
                    .EquipSetDef>(),
                new Dictionary<int, int>()),
        enchantSvc, gameData.EnchantDescriptions),
    AdvancedAbilities = new AdvancedAbilityRepository(db),
    LifeSkills = new LifeSkillRepository(db),
    MissionRepo = new MissionRepository(db),
    LotteryRepo = lotteryRepo,
    WorksRepo = new Eden.Core.Database.Repositories.WorkRepository(
        db),
    DestinyRepo = new DestinyRoadRepository(db),
    FastDressRepo =
        new Eden.Core.Database.Repositories.FastDressRepository(db),
    FortuneRepo =
        new Eden.Core.Database.Repositories.FortuneBagRepository(db),
    CustomClassRepo =
        new Eden.Core.Database.Repositories.CustomClassRepository(db),
    DailyAwardRepo =
        new Eden.Core.Database.Repositories.DailyAwardRepository(db),
    GuideRepo =
        new Eden.Core.Database.Repositories.NewPlayerGuideRepository(db),
    BattlefieldRepo =
        new Eden.Core.Database.Repositories.BattlefieldRepository(db),
    PayEventRepo =
        new Eden.Core.Database.Repositories.PayEventRepository(db),
    EvaluateRepo =
        new Eden.Core.Database.Repositories.EvaluateRepository(db),
    AchievementRepo =
        new Eden.Core.Database.Repositories.AchievementRepository(db),
    LifelongRepo =
        new Eden.Core.Database.Repositories.LifelongRepository(db),
    Evaluates = new Eden.Core.Game.Evaluate.EvaluateService(),
    // Familiers de combat — S_Partner/S_PartnerGrow + table native
    // `elf` (CKFElfManager natif).
    Partners = Eden.Core.Data.Parsers.PartnerParser.Parse(
        Path.Combine(dataDir, "S_Partner.ini")),
    PartnerGrows = Eden.Core.Data.Parsers.PartnerParser.ParseGrow(
        Path.Combine(dataDir, "S_PartnerGrow.ini")),
    ElfRepo = new Eden.Core.Database.Repositories.ElfRepository(
        db),
    PlayerEnchants = new Eden.Core.Database.Repositories
        .PlayerEnchantRepository(db),
    FamilyBank = new FamilyBankRepository(db),
    Social = new SocialRepository(db),
    Users = new Eden.Core.Database.Repositories.UserRepository(
        memberDb),
};

// Les mutations d'inventaire du trade (réserve, restitution,
// échange) déclenchent l'update_quest_item natif → objectifs
// ItemCollect réévalués côté mission.
services.Trade.OnInventoryChanged = services.NotifyItemChanged;

// Exp Ultime — charge perks + courbe de coût. Tolérant : les
// tables limitless n'existent pas tant que la migration
// limitless/db/001_limitless.sql n'est pas appliquée — le service
// reste actif mais les perks sont vides (aucun gain en pratique).
if (services.Limitless != null)
{
    try { await services.Limitless.ReloadAsync(); }
    catch (Exception ex)
    {
        EdenLog.Write($"[Limitless] reload ignoré (tables " +
            $"absentes ?) : {ex.Message}");
    }
}

// ===== Stages d'ITEMS (ordre : equipment → gems → fortify →
// costumes, AVANT licenses/…/buffs) — règle d'or : une source de
// stats = un stage nommé ordonné, jamais d'injection ad hoc. =====
// Liste d'équipement (tous conteneurs 0..5) chargée UNE fois par
// résolution : clé « equipment.all » du StatFetch (seedée à l'entrée,
// sinon lue en DB puis mémorisée pour les stages suivants).
async Task<IReadOnlyList<Eden.Core.Game.Inventory.ItemEntity>>
    LoadStatEquipment(int charId, StatFetch? fetch)
{
    if (fetch?.TryGet<IReadOnlyList<
            Eden.Core.Game.Inventory.ItemEntity>>(
            "equipment.all", out var all) == true && all != null)
        return all;
    var loaded = await inventoryRepo.GetAllEquipmentEntitiesAsync(charId);
    fetch?.Put("equipment.all", loaded);
    return loaded;
}

// « equipment » — armes/armures/glyphes portés (RefreshEQsAttr natif :
// conteneurs <2 ou ==5, identifiés ; STR/AGI/INT/WIS/LCK ÷ diviseur,
// HP/MP) → canal PRE (dans le produit avec le % de classe). Seul le
// set actif (conteneur 0) + glyphes (5) compte, pas le 2e set.
services.StatPipeline.AddStage("equipment", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    var worn = all.Where(i => i.BagId is 0 or 5);
    return StatContribution.FromEquipment(EquipmentStatService.Sum(
        worn, gameData.Items, services.EquipTrain.ItemTrainAdds));
});

// « gems » — gemmes incrustées (embedded_id1-6 = item_id de la gemme)
// : toutes les colonnes stats S_Item de la gemme.
services.StatPipeline.AddStage("gems", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    return StatContribution.FromBonusStats(EquipmentStatService.SumGems(
        all.Where(i => i.BagId is 0 or 5), gameData.Items));
});

// « fortify » — +N (S_Parameter 56 arme / 55 armure, +3 %/niveau de la
// valeur de base) + enchants des scrolls (witchcraft) des pièces
// portées. magicbox_enchant1-3 : stage « magicbox » ci-dessous ;
// gold_enchant : stage « goldenchant » ci-dessous (id S_Enchant natif).
services.StatPipeline.AddStage("fortify", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    var worn = all.Where(i => i.BagId is 0 or 5).ToList();
    var c = StatContribution.FromBonusStats(
        EquipmentStatService.SumFortify(worn, gameData.Items,
            gameData.Parameters.Get(
                Eden.Core.Data.Parsers.ParameterParser.WeaponEnchantBonus, 3f),
            gameData.Parameters.Get(
                Eden.Core.Data.Parsers.ParameterParser.ArmorEnchantBonus, 3f)));
    foreach (var eid in EquipmentStatService.WitchcraftEnchantIds(
                 worn, gameData.Items, costume: false))
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, services.Enchants.GetUseCommands(eid));
    return c;
});

// « magicbox » — combos de boîte magique (magicbox_enchant1-3 = ids
// S_ItemCombo, tiers 1/3/5) : la table combo→effet est S_ItemCombo
// col 10 = id S_Enchant (combos AttrType 4-6), traduit par les mêmes
// commandes que tout enchant.
services.StatPipeline.AddStage("magicbox", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    var c = new StatContribution();
    foreach (var eid in EquipmentStatService.MagicBoxEnchantIds(
                 all.Where(i => i.BagId is 0 or 5), gameData.ItemCombos))
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, services.Enchants.GetUseCommands(eid));
    return c;
});

// « goldenchant » — gold_enchant (u16 clone+0x6c) = id S_Enchant posé
// sur la pièce (ModifyItemTransGold @0x08356020 : « Update Item
// GoldEnchant … enchant_id=%hd »), appliqué comme tout enchant d'item.
services.StatPipeline.AddStage("goldenchant", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    var c = new StatContribution();
    foreach (var eid in EquipmentStatService.GoldEnchantIds(
                 all.Where(i => i.BagId is 0 or 5)))
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, services.Enchants.GetUseCommands(eid));
    return c;
});

// « costumes » — pages costume (conteneurs 2-4) : zéro stat propre
// (RefreshEQsAttr les saute) ; seuls les enchants posés (scrolls
// witchcraft kind 79) comptent.
services.StatPipeline.AddStage("costumes", async (charId, _, fetch) =>
{
    var all = await LoadStatEquipment(charId, fetch);
    var c = new StatContribution();
    foreach (var eid in EquipmentStatService.WitchcraftEnchantIds(
                 all, gameData.Items, costume: true))
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, services.Enchants.GetUseCommands(eid));
    return c;
});

// Étape « licenses » du pipeline de stats — les enchants des
// certificats posés (+0xafc) et des combos actifs (+0xb14) entrent
// dans la somme modulée (EffectToLife(-2,0x1000) natif appliqué par
// ReplaceSpellStones/InvokeSpellStonesSet).
services.StatPipeline.AddStage("licenses",
    (charId, _, _) => Task.FromResult(
        services.Licenses.ProvideStats(charId)));

// Migration KB-04 (idempotente) : éveillées historiques lv1/aa1 → 80/80.
try
{
    var promoted = await services.Classes.PromoteAwakenedAsync();
    if (promoted > 0)
        Eden.Core.Logging.EdenLog.Write(
            $"[Zone] migration KB-04 : {promoted} classe(s) éveillée(s) → lv80/aa80");
}
catch (Exception ex)
{
    Eden.Core.Logging.EdenLog.Write($"[Zone] migration KB-04 ignorée : {ex.Message}");
}

// Étape « classrank » — Class Ability Bonus natif (RefreshJPAward
// @0x8417c10) : pour CHAQUE classe du personnage (player_classlist),
// rang = floor(min(classLv, S_Parameter 578=100) × 0.2) ; le rang
// indexe le palier S_ClassRankBuff de la classe dont le 6ᵉ champ
// est un enchant permanent (EffectToLife durée -2, flag 0x1000).
// Les bonus de toutes les classes montées se cumulent.
services.StatPipeline.AddStage("classrank", async (charId, ct, fetch) =>
{
    var c = new StatContribution();
    var cap = services.GameData.Parameters.Get(578, 100f);
    var defs = services.GameData.ClassRankBuffs;
    if (defs.Count == 0) return c;
    // Phase 2 : la classlist préchargée par le pipeline d'entrée
    // évite la requête quand le fetch la fournit.
    var rows = fetch?.TryGet<IReadOnlyList<ClassProgressEntity>>(
        "classlist", out var cl) == true ? cl!
        : await services.Classes.GetClassesByCharacterIdAsync(charId);
    foreach (var row in rows)
    {
        var rank = (int)(Math.Min(row.ClassLevel, cap) * 0.2f);
        if (rank == 0) continue;
        // KB-02 : clé = champ 1 (ClassId), pas l'index de ligne (Id).
        var tier = Eden.Core.Data.Parsers.ShopAndMiscParsers
            .FindClassRankBuff(defs, row.ClassId)
            ?.Tiers.FirstOrDefault(t => t.Rank == rank);
        if (tier is { EnchantId: > 0 } t)
            Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
                c, services.Enchants.GetUseCommands(t.EnchantId));
    }
    return c;
});

// Étape « talents » — talents de classe (advanced_ability) : le natif
// applique l'enchant de chaque talent de la classe COURANTE via
// EffectToLife(-2, flags=0x1000) — UNE FOIS PAR POINT investi
// (InvokeAAEnchants @0x8417830 : boucle uVar8 < point) → contribution
// = args × points, icône cachée (IsHideIcon : sets bit 0x1000).
// Arbres : (race-1)*100 + class ×10 + {1,2,3} (AddAAEffectPoint).
services.StatPipeline.AddStage("talents", async (charId, ct, fetch) =>
{
    var c = new StatContribution();
    var effects = fetch?.TryGet<Dictionary<short, short>>(
        "aa", out var aa) == true ? aa!
        : await services.AdvancedAbilities.GetEffectsAsync(charId);
    if (effects.Count == 0) return c;
    var chr = fetch?.TryGet<PlayerCharacterEntity>(
        "char", out var pc) == true ? pc
        : await services.Characters.GetCharacterByIdAsync(charId);
    if (chr == null) return c;
    var treeBase = (chr.RaceId - 1) * 100 + chr.ClassId;
    // Union serveur + variantes clients (Reforged) — cf. TalentService.
    var owned = services.Talents.OwnedSpellIds(treeBase);
    foreach (var (effectId, point) in effects)
        if (point > 0 && owned.Contains(effectId))
            for (var p = 0; p < point; p++)
                Eden.Core.Game.Classes.LicenseService
                    .ApplyEnchantCommands(c, services.Enchants
                        .GetUseCommands((ushort)effectId));
    return c;
});

// Étape « titles » — SetPresentAppellation natif @0x080f2400 :
// l'enchant du titre ACTIF (S_Title col3, player_characters.
// appellation) est appliqué tant que le titre est sélectionné ;
// swap → RemoveEnchantByID(ancien) + EffectToLife(nouveau,-2,0x3020).
// La branche « enchant permanent à l'obtention » d'AddAppellation
// (@0x8103a40) est du code mort natif : CTitleQuery::Deserialize
// (@0x086361xx) fixe data+0x10 = 4, jamais 2 — SEUL le titre actif
// compte, pas la collection entière.
// Étape « limitless » — perks Limitless actifs du perso
// (chantier 3) : codes mappés sur les canaux secondaires natifs.
// max_hp va par les vitals (pas de canal StatContribution),
// drop/craft/exp_ultime_mult lus à leurs call-sites.
services.StatPipeline.AddStage("limitless", async (charId, ct, fetch) =>
{
    var c = new StatContribution();
    // La clé présente à null = service absent lu hors-boucle
    // (même repli que services.Limitless == null).
    if (fetch?.TryGet<IReadOnlyList<Eden.Core.Game.Limitless
            .LimitlessService.Perk>>(
            "limitless.perks", out var pl) == true)
    {
        if (pl == null) return c;
        foreach (var p in pl) AddPerk(c, p);
        return c;
    }
    if (services.Limitless == null) return c;
    foreach (var p in await services.Limitless
                 .GetActivePerksAsync(charId))
        AddPerk(c, p);
    return c;

    static void AddPerk(StatContribution c,
        Eden.Core.Game.Limitless.LimitlessService.Perk p)
    {
        var v = (int)p.Value;
        switch (p.Code)
        {
            case "attack_speed_pct": c.AddSecondary(attackSpeedPct: v); break;
            case "cast_speed_pct": c.AddSecondary(castSpeedPct: v); break;
            case "move_speed_pct": c.AddSecondary(moveSpeedPct: v); break;
            case "defence": c.AddSecondary(defence: v); break;
            case "block_pct": c.AddSecondary(blockPct: v); break;
            case "parry": c.AddSecondary(parry: v); break;
            case "crit_rate_pct": c.AddSecondary(critRatePct: v); break;
            case "phys_attack_pct": c.AddSecondary(physAttackPct: v); break;
            case "magic_attack_pct": c.AddSecondary(magicAttackPct: v); break;
        }
    }
});

services.StatPipeline.AddStage("titles", async (charId, ct, fetch) =>
{
    var c = new StatContribution();
    // Titre actif — la colonne appellation du perso est déjà lue
    // par le pipeline d'entrée (évite GetActiveAppellationAsync).
    var active = fetch?.TryGet<int>("appellation", out var ap) == true
        ? ap
        : await services.Characters
            .GetActiveAppellationAsync(charId);
    if (active > 0)
    {
        var t = services.GameData.Titles.FirstOrDefault(
            x => x.Id == active);
        if (t is { SpellId: > 0 })
            Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
                c, services.Enchants.GetUseCommands(t.SpellId));
    }
    return c;
});

// « herotrait » — trait héroïque choisi à la création (8 passifs
// natifs : spell 50001-50008 « J00n » → enchant 52001-52008, désasm
// CreateCharacterIntoDB world : Feature<8 → player_spells 0xc351+f ;
// GetHeroFeature zone @0x840ace0 = id enchant − 52000). Permanent,
// sans durée : mêmes commandes que les autres enchants (S_Enchant :
// ex. 52001 = 2002 AttrMaxHP% + 2013 AttrMaxMP% 12).
services.StatPipeline.AddStage("herotrait", async (charId, _, fetch) =>
{
    var c = new StatContribution();
    var flag = fetch?.TryGet<int>("create_flag", out var cf) == true
        ? cf
        : await services.Characters.GetCreationFlagAsync(charId);
    if (flag is >= 1 and <= 8)
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, services.Enchants.GetUseCommands(52000 + flag));
    return c;
});

// Étape « buffs » — statuts temporaires actifs persistés dans
// player_enchants (AddEnchant natif : buffs de sorts, HoT/DoT,
// invincibilité de migration). Seules les commandes à effet de stat
// (HP/MP/dmg…) comptent ; les commandes tick/summon sont ignorées par
// ApplyEnchantCommands. Les lignes à rest_time=0 sont déjà purgées.
services.StatPipeline.AddStage("buffs", async (charId, ct, fetch) =>
{
    var c = new StatContribution();
    // Les effets de talents AA sont gérés par le stage « talents »
    // (×points) — on exclut leurs lignes éventuelles de
    // player_enchants pour ne pas double-compter.
    var aaEffects = fetch?.TryGet<Dictionary<short, short>>(
        "aa", out var aa) == true ? aa!
        : await services.AdvancedAbilities.GetEffectsAsync(charId);
    var aaIds = new HashSet<int>();
    foreach (var id in aaEffects.Keys) aaIds.Add(id & 0xffff);
    var seen = new HashSet<int>();
    var enchants = fetch?.TryGet<IReadOnlyList<PlayerEnchantEntity>>(
        "enchants", out var en) == true ? en!
        : await services.PlayerEnchants.GetEnchantsAsync(charId);
    foreach (var e in enchants)
    {
        if (e.RestTime == 0 || aaIds.Contains(e.EffectId & 0xffff)) continue;
        // Enchant de ride actif — compté par le stage « ride »
        // (RideStates) ; le compter ici doublerait 2051/2002 (vitesse
        // et HP% des montures de combat).
        if (services.RideStates.TryGetValue(charId, out var rs) &&
            (e.EffectId & 0xffff) == (rs.EnchantId & 0xffff)) continue;
        // Ligne de ride sans monture active (connexion à froid : le
        // restore va la purger) — aucune stat de monture à pied.
        if (!Eden.Zone.ZoneEntryFlow.ShouldRestoreEnchant(
                services.Enchants.GetRideInfo(e.EffectId) != null,
                services.RideStates.ContainsKey(charId))) continue;
        seen.Add(e.EffectId & 0xffff);
        // Mode-1 natif (2081/2082) : référence au niveau du sort —
        // player_enchants.level, miroir de SEffectData+0x3e.
        // stack_count = cumul de l'enchant stackable (S_Enchant col59,
        // CEnchantMgr::Add/Reset natif @0x08569a50 : stats cumulées
        // tant que counter < max — non stackable = 1).
        for (var stk = Math.Max(1, e.StackCount); stk-- > 0;)
            Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
                c, services.Enchants.GetUseCommands(e.EffectId),
                services.Spells.GetReference(e.Level));
    }
    // Enchants de session non persistés — les CEnchant natifs posés
    // flag 0x1000 (procs d'équipement via CEnchantMgr::OnAttackEnchant
    // /OnBeAttackEnchant, talents AA) contribuent aux stats sans
    // ligne player_enchants : sans eux le proc se déclenchait (icône
    // 299) mais aucune stat n'arrivait au combat. Les effectIds AA
    // et ceux déjà couverts par une ligne DB sont exclus.
    if (services.SessionEnchants.TryGetValue(charId, out var smap))
        foreach (var kv in smap)
        {
            var eid = kv.Key & 0xffff;
            if (aaIds.Contains(eid) || seen.Contains(eid)) continue;
            // Valeur packée level | stacks<<16 (cumul stackable).
            for (var stk = Math.Max(1, kv.Value >> 16); stk-- > 0;)
                Eden.Core.Game.Classes.LicenseService
                    .ApplyEnchantCommands(
                        c, services.Enchants.GetUseCommands(eid),
                        services.Spells.GetReference(kv.Value & 0xffff));
        }
    return c;
});

// Étape « ride » — l'enchant de monture actif (RideStates) peut
// porter des bonus de stats en plus de 2134/2051/2140 : les montures
// de combat « 戰鬥座騎 » ajoutent p.ex. 2002 (AttrMaxHP %) — enchants
// 55880/59441/42858. Les commandes ride/vitesse n'ont pas de nom de
// stat dans EnchantCommands → ignorées par ApplyEnchantCommands.
services.StatPipeline.AddStage("ride", (charId, ct, _) =>
{
    var c = new StatContribution();
    if (services.RideStates.TryGetValue(charId, out var r))
        // 2051 (vitesse) exclu : déjà compté par RideStates →
        // CombatantSnapshot.MoveSpeedBonusPct (RideSpeedRules).
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, Eden.Zone.RideSpeedRules.PipelineCommands(
                services.Enchants.GetUseCommands(r.EnchantId)));
    return Task.FromResult(c);
});

// Commandes GM ("//nom args" en chat) — handlers async avec accès
// session/inventaire (clone_item, gain_gold, points item mall…).
GmHandlers.Register(services.Gm, services);
// Commandes GM de guilde/hall (create_guild, guild_level,
// give_building, place_npc…) — même registre.
Eden.Zone.Handlers.GmGuildHandlers.Register(services.Gm, services);

// --- Scènes de maps : spawn monstres/PNJ depuis Data/scene/S*.ini ---
// DATA_DIR peut être un symlink (bin/Release/data -> db_decoded) :
// dataDir/../scene résoudrait alors DANS bin/Release au lieu de
// server_us/Data/scene → 0 scène chargée, 0 mob/PNJ hors spawn
// (bug live : entités distantes invisibles). On résout le lien.
var realDataDir = Directory.ResolveLinkTarget(dataDir, true)?.FullName
                  ?? dataDir;
var sceneDir = EdenConfig.SceneDir
               ?? Path.Combine(realDataDir, "..", "scene");
if (Directory.Exists(sceneDir))
{
    sceneSvc.LoadScenes(sceneDir);
    var nodeMapDir = EdenConfig.NodeMapDir
                     ?? Path.Combine(sceneDir, "..", "NodeMap");
    EdenLog.Write($"[Zone] {sceneSvc.LoadNodeMaps(nodeMapDir)} cartes " +
        $"de murs invisibles (.smp) chargées depuis {nodeMapDir}");
    foreach (var code in sceneSvc.SceneCodes)
        foreach (var m in sceneSvc.SpawnMapMonsters(code, nodeId))
            services.Entities.SpawnMonster(m.Definition);
    EdenLog.Write($"[Zone] {sceneSvc.SceneCount} scènes chargées, " +
        $"{services.Entities.All.Count()} entités spawnées");

    // Préchauffage du monde vivant — EnsureNodePopulated est lazy :
    // sans ça le 1er joueur entrant sur une map payait le populate
    // (centaines de LiveMob sous _nodesLock) sur le fil de la boucle
    // acteur. Tables S_Monster/Motion/S_Npc bindées au boot puis
    // populate des maps « chaudes » (inst 0 = scène partagée) AVANT
    // l'ouverture du listener.
    services.Live.BindGameData(gameData);
    var warmed = PrewarmLiveNodes(services, sceneSvc);
    if (warmed.Count > 0)
        EdenLog.Write($"[Zone] Préchauffage monde vivant : " +
            $"{warmed.Count} nœud(s) — {string.Join(',', warmed)}");
}

// Référence anticipée du flow — affectation plus bas ; la boucle
// acteur de zone (respawn, trésors, chase, casts) est créée juste
// après puisqu'elle en a besoin.
Eden.Zone.ZoneEntryFlow? zoneEntryFlow = null;

// Persistance sociale : write-through GuildService/FriendService/LoverService
if (services.Social != null)
{
    services.Guilds.Attach(services.Social);
    services.Friends.Attach(services.Social);
    services.Lovers.Attach(services.Social);
    // Fire-and-forget MAIS loggé : un échec du chargement initial
    // laissait _guilds vide à vie (ex. signature Dapper des lignes
    // membres) — aucune trace sans ce try/catch (bug live 2026-10-26 :
    // "Can't find family" sur le 176 + liste de villes 502 vide).
    _ = Task.Run(async () =>
    {
        try { await services.Guilds.LoadAsync(); }
        catch (Exception ex)
        {
            EdenLog.Write("[Zone] GuildService.LoadAsync a échoué : " +
                ex.Message);
        }
    });
    // Territoires : defs S_Territory + propriétaires territory_war +
    // inscriptions des camps d'élimination depuis familybattle_ranks
    // (init CTerritoryQuery::LoadDB mission natif). Les phases sont
    // recalculées à la volée depuis S_Schedule à chaque requête.
    _ = Task.Run(async () =>
    {
        try
        {
            var wars = await services.Social.GetTerritoryWarsAsync();
            services.Territory.LoadTerritories(gameData.Territories,
                wars.Select(w => (w.TerritoryId, w.OwnFamilyId,
                    w.OwnTimes)));
            var ranks = await services.Social
                .GetFamilyBattleRanksAsync();
            foreach (var r in ranks)
            {
                // phase_type = sched type 301-315 → terr def de même
                // type ; phase_index alterne les camps (natif : maps
                // +0x30/+0x48 remplies à l'inscription — ici dérivées
                // du classement persisté du round).
                var terr = gameData.Territories.Values
                    .FirstOrDefault(d => d.Type == r.PhaseType);
                if (terr != null)
                    services.Territory.RegisterFamily(terr.Id,
                        r.FamilyId, defender: r.PhaseIndex % 2 == 0);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] init territoires : {ex.Message}");
        }
    });
}

// --- Liens internes : T_ (TicketServer) + ZW (World) ---
// Le client T_ valide les tickets zone comme le natif (ServerUseTicket NCID 1
// -> ClientUseTicket NCID 3). Le lien ZW remonte les entrées au monde
// (ResetTime -> ClientResetTime qui termine l'écran de chargement côté client).
var ticketClient = new Eden.Core.Networking.TicketServerClient(ticketIp, ticketPort);
try { await ticketClient.ConnectAsync(); }
catch (Exception ex) { EdenLog.Write($"[Zone] T_ TicketServer injoignable : {ex.Message}"); }

var worldLink = new Eden.Zone.Links.ZoneWorldLink(worldLinkIp, worldLinkPort, zoneId);
worldLink.Start();

// Lien ZM -> MissionServer natif (:7654) : dialogues/quêtes NPC, états de
// mission, symboles quête — relayés byte-par-byte comme la zone native.
var missionIp = EdenConfig.MissionHost;
var missionPort = EdenConfig.MissionPort;
var missionLink = new Eden.Zone.Links.ZoneMissionLink(missionIp, missionPort, zoneId);
missionLink.Start();
services.Mission = missionLink;
services.World = worldLink;

zoneEntryFlow = new Eden.Zone.ZoneEntryFlow(ticketClient, worldLink,
    missionLink, characterRepository, services, dataDir);

// Boucle acteur de zone (PeriodicTimer 100 ms + file de commandes) —
// remplace le System.Timers.Timer 1 s : respawn des mobs et cycle des
// trésors tournent désormais sur le fil unique de la boucle, exceptions
// bornées, plus d'empilement fire-and-forget.
var zoneLoop = new Eden.Zone.ZoneLoop(services, zoneEntryFlow);
services.Loop = zoneLoop;

// Livraisons item mall : le monde pousse des ordres ZW 24 — le flow
// délivre en inventaire et répond ZW 12 (serial + create_time).
worldLink.ItemMallDelivery = zoneEntryFlow.HandleItemMallDeliveryAsync;

// fortune_bag : le monde pousse les lots (ZW 26, phases 0/1/2) —
// le registre zone remplace la map CZoneServer+0x124 native.
worldLink.FortuneBagUpdate += (phase, records) =>
{
    // Mutation du registre routée sur le fil de la boucle (modèle
    // acteur — la map +0x124 native est aussi lue par les handlers).
    _ = zoneLoop.PostAsync(_ =>
    {
        services.FortuneBags.Apply(phase, records);
        if (phase == 2)
            EdenLog.Write($"[Zone] FortuneBag chargé : " +
                $"{services.FortuneBags.Count} sacs");
        return Task.CompletedTask;
    });
};

// Famille : pushes monde→zone 31/32/33 (natifs Change/New/RemoveFamily)
// — le flow applique au registre guilde et émet 346 aux persos.
worldLink.FamilyPush += (kind, body) =>
    _ = zoneEntryFlow.HandleFamilyPushAsync(kind, body);

// Commandes texte monde→zone (ZW 23, natif CNC_ZW_ZoneServerTextCommand)
// — ex. `update_prestige` (miroir prestige char+0x6c4).
worldLink.TextCommand += text =>
    _ = zoneEntryFlow.HandleWorldTextCommandAsync(text);

// --- Dispatch des commandes CNE_* par domaine ---
var dispatcher = new PacketDispatcher();
var legacy = services.LegacyHandlers;
dispatcher.Register(() => new ChatMessage(),
    (session, msg, token) => legacy.HandleZoneChatAsync(session, msg, token));
EntryHandlers.Register(dispatcher, services);
CombatHandlers.Register(dispatcher, services);
MovementHandlers.Register(dispatcher, services);
InventoryHandlers.Register(dispatcher, services);
TradeHandlers.Register(dispatcher, services);
ShopHandlers.Register(dispatcher, services);
SocialHandlers.Register(dispatcher, services);
TeamHandlers.Register(dispatcher, services);
ElfHandlers.Register(dispatcher, services);
InstanceHandlers.Register(dispatcher, services);
PlayerHandlers.Register(dispatcher, services);
ItemActionHandlers.Register(dispatcher, services);
// Vendor* (étal joueur) : interceptés par ZoneEntryFlow.Vending.cs
// avant le dispatcher — l'ancien VendorHandlers était spéculatif.
FamilyHandlers.Register(dispatcher, services);
HousingHandlers.Register(dispatcher, services);
EquipTrainHandlers.Register(dispatcher, services);
MiscHandlers.Register(dispatcher, services);

// --- Crypto handshake : RSA-2048 e=257 → clé RC4 5 octets (CMSTCPChannel)
// en mode legacy ; TLS 1.3 en mode modern (défaut) — la clé RSA n'est
// alors plus matérialisée (retirée en phase4-c2).
var rsaKey = EdenConfig.LegacyTransport
    ? EdenCrypto.CreateServerRsa() : null;
var cryptoHandshake = EdenConfig.ZoneCrypto;
var handshakeNeeded = !EdenConfig.LegacyTransport || cryptoHandshake;

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
EdenLog.Write($"[Zone] Serveur prêt sur 0.0.0.0:{port} — " +
    $"{(EdenConfig.LegacyTransport
        ? cryptoHandshake ? "crypto RSA/RC4" : "crypto DÉSACTIVÉE"
        : "TLS 1.3")}\n");

// Arrêt gracieux : SIGINT (Ctrl+C) ET SIGTERM (kill/systemd) →
// stoppe l'accept, puis on vide la boucle avant de sortir. Pas de
// Generic Host ici (lot borné) — le pattern CancellationToken seul
// suffit.
var stopCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;          // ne pas tuer le process tout de suite
    stopCts.Cancel();
};
using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration
    .Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,
        ctx =>
        {
            ctx.Cancel = true; // on se suicide proprement nous-mêmes
            stopCts.Cancel();
        });

while (true)
{
    TcpClient tcpClient;
    try
    {
        tcpClient = await listener.AcceptTcpClientAsync(stopCts.Token);
    }
    catch (OperationCanceledException)
    {
        break; // arrêt demandé — on sort pour vider la boucle
    }
    // Borne de sessions — au-delà, refus immédiat plutôt que de
    // laisser des connexions stagner sans slot (EDEN_ZONE_MAX_SESSIONS).
    if (services.Sessions.Count >= EdenConfig.ZoneMaxSessions)
    {
        EdenLog.Write(
            $"[Zone] Refus connexion ({services.Sessions.Count} sessions " +
            $"≥ EDEN_ZONE_MAX_SESSIONS={EdenConfig.ZoneMaxSessions})");
        tcpClient.Dispose();
        continue;
    }
    _ = Task.Run(async () =>
    {
        // Transport par EDEN_TRANSPORT : TLS 1.3 (modern, défaut) ou
        // RSA/RC4 lapis quand ZONE_CRYPTO actif / TCP clair sinon
        // (legacy) — les deux derrière la même NetSession.
        await using var session = new NetSession(
            TransportFactory.ForAcceptedSocket(tcpClient, rsaKey,
                cryptoHandshake));
        // Envois non bloquants : la boucle acteur n'attend jamais un
        // destinataire lent/mort (burst 200 entrées du 2026-10-26 :
        // ~70 files pleines × 5 s = boucle figée ~6 min).
        session.DropOnFullQueue = true;
        services.Sessions.Add(session);
        EdenLog.Write($"[Zone] [CONNEXION] Joueur depuis {session.RemoteEndPoint}");

        try
        {
            if (handshakeNeeded)
            {
                if (!await session.StartTransportAsync())
                {
                    EdenLog.Write($"[Zone] Handshake échoué : {session.CloseReason}");
                    return;
                }
            }

            // Handshake de version (phase 4d) — transport modern
            // seulement : la première trame doit être VersionHello
            // (0xFFFF) ; le gate répond Ack/Reject puis, sur refus,
            // ferme proprement — avant tout dispatch zone.
            var pending = new List<byte[]>();
            if (!EdenConfig.LegacyTransport && EdenConfig.VersionCheck)
            {
                using var gateCts = new CancellationTokenSource(
                    NetSession.HandshakeTimeout);
                var gate = await VersionGate.HandshakeAsync(session,
                    VersionGate.Options.FromConfig(), gateCts.Token);
                if (!gate.Accepted)
                {
                    EdenLog.Write($"[Zone] [VERSION_REJECT] {session.RemoteEndPoint} : {gate.Reason}");
                    return;
                }
                var h = gate.Hello!;
                EdenLog.Write($"[Zone] [VERSION_OK] {session.RemoteEndPoint} " +
                    $"client={h.ClientVersion} proto={h.Proto} ({gate.Reason})");
                if (gate.Remaining.Count > 0)
                    pending.AddRange(gate.Remaining);
            }

            while (true)
            {
                var packets = pending.Count > 0
                    ? pending
                    : await session.ReadPacketsAsync();
                pending = new List<byte[]>();
                if (packets.Count == 0) break;
                foreach (var payload in packets)
                {
                    if (payload.Length < 2) continue;
                    var opcode = BitConverter.ToUInt16(payload, 0);
                    if (NetDebug.Enabled)
                        EdenLog.Write($"[PKT-IN] NCID={opcode} {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 50)))}");
                    try
                    {
                        // Une seule copie du corps — réutilisée par le flow
                        // puis par le dispatcher (avant : payload[2..] +
                        // AsSpan(2).ToArray() = 2 allocations par paquet).
                        var body = payload.AsSpan(2).ToArray();
                        EdenMetrics.PacketDispatchDelayMs.Record(
                            System.Diagnostics.Stopwatch.GetElapsedTime(
                                session.LastPacketsTimestamp)
                                .TotalMilliseconds,
                            new KeyValuePair<string, object?>("ncid", opcode));
                        // Chemin d'entrée piloté par captures (ticket, burst, migration).
                        var flowStart = System.Diagnostics.Stopwatch
                            .GetTimestamp();
                        if (await zoneEntryFlow.TryHandleAsync(session, opcode, body, CancellationToken.None))
                        {
                            EdenMetrics.PacketHandleMs.Record(
                                System.Diagnostics.Stopwatch.GetElapsedTime(
                                    flowStart).TotalMilliseconds,
                                new KeyValuePair<string, object?>("ncid", opcode),
                                new KeyValuePair<string, object?>("path", "zone_flow"));
                            continue;
                        }
                        var inStream = new InBinStream(body);
                        var dispatched = await dispatcher.DispatchAsync(opcode, inStream, session);
                        if (!dispatched)
                            EdenLog.Write($"[Zone] <-- opcode={opcode} inconnu ({payload.Length}B)");
                    }
                    catch (Exception pex)
                    {
                        // Une erreur de handler (SQL, parse…) ne doit JAMAIS
                        // fermer la session — le client perdrait la connexion.
                        EdenLog.Write(
                            $"[Zone] Erreur handler NCID={opcode} : {pex}");
                        // Trace des derniers paquets : une erreur handler
                        // suit souvent un body désaligné.
                        EdenLog.Write(session.DumpPacketTrace());
                    }
                }
            }
            if (session.CloseReason is { } reason)
                EdenLog.Write(
                    $"[Zone] Session {session.RemoteEndPoint} fermée : {reason}");
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] Erreur session ({session.RemoteEndPoint}) : {ex.Message}");
            EdenLog.Write(session.DumpPacketTrace());
        }
        finally
        {
            await zoneEntryFlow.OnDisconnectAsync(session);
            if (session.CharacterId is { } cid)
            {
                services.Entities.Remove(cid);
                services.Teams.LeaveTeam(cid);
            }
            services.Sessions.Remove(session);
            EdenLog.Write($"[Zone] [DECONNEXION] Joueur {session.RemoteEndPoint} a quitté la zone.");
        }
    });
}

// Arrêt gracieux : plus d'accept — on coupe le listener, vide la
// boucle (commandes postées en vol + scheduled restants) puis on
// ferme les sessions en parallèle (chaque dispose est borné ~3 s par
// le drain — en parallèle l'arrêt total reste borné aussi).
EdenLog.Write("[Zone] Arrêt demandé — vidange de la boucle…");
listener.Stop();
// Sauvegarde de tous les persos AVANT de détruire la boucle (le
// disconnect passe par RunOnLoopAsync et échouerait ensuite).
await zoneEntryFlow.SaveAllOnShutdownAsync();
await zoneLoop.DisposeAsync();
await Task.WhenAll(services.Sessions.All.Select(s => s.DisposeAsync()
    .AsTask()));
EdenLog.Write("[Zone] Arrêt propre.");

/// <summary>AuctionService + persistance (write-through + rechargement).</summary>
static AuctionService CreateAuctionService(NpgsqlDataSource db)
{
    var svc = new AuctionService();
    svc.Attach(new AuctionRepository(db));
    _ = svc.LoadAsync();
    return svc;
}

/// <summary>Préchauffage boot du monde vivant — peuple les
/// (node, inst=0) des maps « chaudes » pour que
/// <c>EnsureNodePopulated</c> ne tourne plus sur la boucle à la
/// première entrée d'un joueur. Cibles : maps à spawn joueur (aire
/// d'event <c>BORN</c> ou <c>born_area</c> indexant [position]) +
/// <c>EDEN_ZONE_PREWARM_MAPS</c>. Retourne les ids effectivement
/// peuplés (au moins une entité).</summary>
static List<int> PrewarmLiveNodes(ZoneServices services,
    SceneService sceneSvc)
{
    var targets = new SortedSet<int>();
    foreach (var code in sceneSvc.SceneCodes)
    {
        if (!TrySceneNodeId(code, out var nid)) continue;
        var scene = sceneSvc.GetScene(code);
        if (scene is { } s &&
            (s.BornPositionIndex > 0 ||
             s.EventAreas.Any(a => a.Type == "BORN")))
            targets.Add(nid);
    }
    foreach (var tok in (EdenConfig.ZonePrewarmMaps ?? "").Split(',',
                 StringSplitOptions.RemoveEmptyEntries |
                 StringSplitOptions.TrimEntries))
    {
        if (TrySceneNodeId(tok, out var nid))
            targets.Add(nid);
        else
            EdenLog.Write($"[Zone] EDEN_ZONE_PREWARM_MAPS : entrée " +
                $"« {tok} » ignorée (attendu « S001 » ou id)");
    }

    var done = new List<int>();
    foreach (var nid in targets)
    {
        // Nœud instancié (CNodeGroup natif) : sa population se crée
        // par groupe à l'entrée — la copie inst=0 serait morte.
        var type = services.GameData.Nodes
            .FirstOrDefault(n => n.Id == nid)?.Type ?? 0;
        if (ZoneEntryFlow.InstanceNodeTypes.Contains(type)) continue;
        if (services.Live.EnsureNodePopulated(nid, 0,
                sceneSvc.GetScene($"S{nid:D3}")).Count > 0)
            done.Add(nid);
    }
    return done;
}

/// <summary>Code de map « S001 » ou id brut « 1 » → nodeId 1.
/// Retourne false si la valeur n'est pas exploitable.</summary>
static bool TrySceneNodeId(string code, out int nodeId)
{
    code = code.Trim();
    if (code.Length >= 2 && code[0] is 'S' or 's')
        code = code[1..];
    return int.TryParse(code, out nodeId) && nodeId > 0;
}
