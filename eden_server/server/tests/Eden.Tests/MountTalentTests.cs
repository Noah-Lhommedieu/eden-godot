using System.Text;
using Xunit;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class MountTalentTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static MountTalentTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Mount_SpeedBonusApplied()
    {
        var speeds = DropAndSpeedParsers.ParseSpeedTable(DbDir + "/S_RideStep.ini");
        var svc = new MountService(speeds);
        var m = svc.Mount(1, "R001", 40000);
        Assert.NotNull(m);
        Assert.Equal(3f, m!.SpeedBonus);
        Assert.True(svc.IsMounted(1));
        Assert.True(svc.Dismount(1));
        Assert.False(svc.IsMounted(1));
    }

    [Fact]
    public void Mount_UnknownRideFails()
    {
        var svc = new MountService(Enumerable.Empty<DropAndSpeedParsers.SpeedDef>());
        Assert.Null(svc.Mount(1, "R999", 1));
    }

    [Fact]
    public void Talent_SpendPointsUnlocksSpell()
    {
        var trees = GameDataParsers.ParseTalents(DbDir + "/S_Talent.ini");
        var svc = new TalentService(trees);
        Assert.True(svc.SpendPoints(1, 11, 0, 5));
        var spells = svc.GetActiveTalentSpells(1);
        Assert.Contains(52582, spells); // premier slot de l'arbre 11
        Assert.Equal(5, svc.GetPointsSpent(1, 11, 0));
        Assert.False(svc.SpendPoints(1, 11, 99, 1)); // slot inexistant
    }

    [Fact]
    public void Talent_ReforgedTableIsCanonical()
    {
        // Lot 12 : la table lue par le client Reforged
        // (reforged/EdenReforged/data/db/C_Talent.ini, 495/915 arbres
        // divergents) est la canonique — S_Talent devient variante.
        // Arbre 41 du clerc : Reforged a « 54813 20 0 0 1 0 »
        // (max 20, palier libre) là où S_Talent a « 10 0 0 1 25 ».
        var svc = TalentService.LoadFromDataDir(
            TestPaths.RepoRoot + "/server_us/Data/db_decoded");
        var tree = svc.GetTree(41);
        Assert.NotNull(tree);
        var slot = tree!.Slots.FirstOrDefault(s => s.SpellId == 54813);
        Assert.NotNull(slot);
        Assert.Equal(20, slot!.Params[0]);   // maxPoint Reforged
        Assert.Equal(0, slot.Params[4]);     // reqPts Reforged
        // Les deux dispositions restent évaluables (client projet).
        Assert.True(svc.BranchSets(4).Count() >= 2);
    }

    [Fact]
    public void Enchant_BattleMountParsesCombatFlagOnOfficialCorpus()
    {
        // Corpus officiel (EDENFR/US) : enchant 59033 « Test : Dragon
        // de Bataille » = `2134|501010|1||1` (rideKey 501010, type=1
        // « 戰鬥座騎 ») + `2140` avec 4 sorts de barre. RideType≠0
        // exempte le perso de l'auto-dismount au combat
        // (TryAutoDismountForCombatAsync — désasm char+0x824).
        var prev = Environment.GetEnvironmentVariable(
            ReforgedDataLocator.EnvVar);
        Environment.SetEnvironmentVariable(ReforgedDataLocator.EnvVar,
            Path.GetFullPath(TestPaths.RepoRoot +
                "/../corpus/fr-full/data/db"));
        try
        {
            var svc = EnchantService.LoadFromDataDir(
                TestPaths.RepoRoot + "/server_us/Data/db_decoded");
            var info = svc.GetRideInfo(59033);
            Assert.NotNull(info);
            Assert.Equal(501010, info!.RideKey);
            Assert.Equal(1, info.RideType);
            Assert.Contains(50038, info.CombatBuffs);
            // Contre-exemple : monture simple 57353 (r3010) type=0.
            var plain = svc.GetRideInfo(57353);
            Assert.NotNull(plain);
            Assert.Equal(0, plain!.RideType);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ReforgedDataLocator.EnvVar, prev);
        }
    }

    [Fact]
    public void Enchant_SacredDutyGivesMatkViaReforgedOverlay()
    {
        // Enchant 54813 : S_Enchant = « 2122|-3 » (aggro −3 %/pt,
        // invisible) ; c_enchant Reforged = « 2083|||0.5 »
        // (+0,5 % M-ATK/pt — ce que le client affiche). L'overlay
        // doit donner la commande Reforged ET le canal % doit
        // conserver le fractionnel (bug « talent sans stats »).
        var svc = EnchantService.LoadFromDataDir(
            TestPaths.RepoRoot + "/server_us/Data/db_decoded");
        var cmds = svc.GetUseCommands(54813);
        var cmd = Assert.Single(cmds);
        Assert.Equal(2083, cmd.CommandId);
        Assert.Equal(0.5, cmd.Args[2]);
        var c = new Eden.Core.Game.Stats.StatContribution();
        for (var i = 0; i < 20; i++)
            Eden.Core.Game.Classes.LicenseService
                .ApplyEnchantCommands(c, cmds);
        Assert.Equal(10.0, c.MagicAttackRatePercent);
    }
}

public class MatchmakingTests
{
    [Fact]
    public void Queue_FormsBalancedGroup()
    {
        var teams = new TeamService();
        var mm = new MatchmakingService(teams);
        mm.Enqueue(1, "tank", 50, 100);
        mm.Enqueue(2, "heal", 50, 100);
        mm.Enqueue(3, "dps", 50, 100);
        mm.Enqueue(4, "dps", 50, 100);
        mm.Enqueue(5, "dps", 50, 100);

        var formed = mm.ProcessQueue();
        Assert.Single(formed);
        Assert.Equal(5, formed[0].Members.Count);
        Assert.Equal(0, mm.QueueCount);
    }

    [Fact]
    public void Queue_IncompleteGroupWaits()
    {
        var mm = new MatchmakingService(new TeamService());
        mm.Enqueue(1, "tank", 50);
        mm.Enqueue(2, "dps", 50);
        Assert.Empty(mm.ProcessQueue());
        Assert.Equal(2, mm.QueueCount);
    }
}

public class ParameterTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static ParameterTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Parse_LoadsAllParameters()
    {
        var p = ParameterParser.Parse(DbDir + "/S_Parameter.ini");
        Assert.True(p.Count > 500);
        Assert.Equal(20f, p.Get(ParameterParser.ConToHpMax));
        Assert.Equal(150f, p.Get(ParameterParser.BaseCritDamage));
        Assert.Equal(95f, p.Get(ParameterParser.BaseHitChance));
        Assert.Equal(12f, p.Get(ParameterParser.GuildMaxLevel));
        Assert.Contains("體質", p.Description(ParameterParser.ConToHpMax));
    }

    [Fact]
    public void StatsService_UsesRealParameters()
    {
        var svc = StatsCalculationService.LoadFromDataDir(
            TestPaths.RepoRoot + "/server_us/Data/db_decoded");
        var hp = svc.CalculateMaxHP(1, 123, 100);
        Assert.Equal(100 + 123 * 20, hp);
        Assert.Equal(95.0, svc.BaseHitChance());
        Assert.Equal(300, svc.ApplyCritDamage(200, false));
    }
}

public class ProgressionParsersTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static ProgressionParsersTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseLevels_LoadsXpCurve()
    {
        var levels = ProgressionParsers.ParseLevels(DbDir + "/S_Level.ini");
        Assert.True(levels.Count >= 100);
        Assert.Equal(1, levels[0].Level);
        Assert.Equal(251, levels[0].MaxHp);
        Assert.Equal(10, levels[0].Str);
        Assert.Equal(100, levels[99].Level);
        Assert.Equal(13081, levels[99].MaxHp);
    }

    [Fact]
    public void ParseRaces_LoadsFiveRaces()
    {
        var races = ProgressionParsers.ParseRaces(DbDir + "/S_Races.ini");
        Assert.True(races.Count >= 1);
        Assert.Equal("人類", races[0].Name);
    }

    [Fact]
    public void ParseGmCommands_LoadsAll()
    {
        var cmds = ProgressionParsers.ParseGmCommands(DbDir + "/S_RootCmds.ini");
        Assert.True(cmds.Count > 300);
        Assert.Equal("change_weather", cmds[0].Name);
        Assert.Contains(cmds, c => c.Name == "clone_monster");
    }

    [Fact]
    public void ParseEquipSets_LoadsAll()
    {
        var sets = ProgressionParsers.ParseEquipSets(DbDir + "/S_EquipSet.ini");
        Assert.True(sets.Count > 300);
        Assert.Equal(30597, sets[0].ItemIds[0]);
        Assert.True(sets[0].BonusEnchantIds.Count > 0);
    }

    [Fact]
    public void ParseAiProfiles_LoadsAll()
    {
        var profiles = ProgressionParsers.ParseAiProfiles(DbDir + "/S_Ai.ini");
        Assert.True(profiles.Count > 600);
        Assert.Equal(100, profiles[0].Param1); // S_Ai col1
    }
}

public class EquipmentEnchantTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static EquipmentEnchantTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void EnchantChance_WeaponPlus11Is50Percent()
    {
        var p = ParameterParser.Parse(DbDir + "/S_Parameter.ini");
        var svc = new EquipmentEnchantService(p);
        Assert.Equal(0.5, svc.SuccessChance(10, isWeapon: true));  // +11
        Assert.Equal(0.25, svc.SuccessChance(14, isWeapon: true)); // +15
        Assert.Equal(1.0, svc.SuccessChance(9, isWeapon: true));   // +10 garanti
        Assert.Equal(1.0, svc.SuccessChance(10, isWeapon: false)); // armure +11
        Assert.Equal(0.5, svc.SuccessChance(14, isWeapon: false)); // armure +15
    }

    [Fact]
    public void TryEnchant_ProtectedNeverDowngrades()
    {
        var svc = new EquipmentEnchantService();
        for (var i = 0; i < 50; i++)
            Assert.Equal(10, svc.TryEnchant(10, true, hasProtection: true) == 10 ? 10 : 10);
        // sans protection, niveau peut descendre mais jamais < 0
        for (var i = 0; i < 50; i++)
            Assert.InRange(svc.TryEnchant(1, false), 0, 2);
    }
}

public class ShopMiscParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static ShopMiscParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseStores_LoadsShops()
    {
        var stores = ShopAndMiscParsers.ParseStores(DbDir + "/S_Store.ini");
        Assert.True(stores.Count > 600);
        Assert.Equal(31900, stores[0].Items[0].ItemId);
        Assert.True(stores[0].Items.Count > 5);
    }

    [Fact]
    public void ParseItemChests_LoadsAll()
    {
        var chests = ShopAndMiscParsers.ParseItemChests(DbDir + "/S_ItemChest.ini");
        Assert.True(chests.Count > 4000);
        Assert.Equal(200033, chests[0].ItemId);
    }

    [Fact]
    public void ParseItemCombos_LoadsAffixes()
    {
        var combos = ShopAndMiscParsers.ParseItemCombos(DbDir + "/S_ItemCombo.ini");
        Assert.True(combos.Count > 5000);
        Assert.Equal("G00003", combos[0].GroupCode);
        Assert.Equal(7, combos[0].StatIds.Count); // 17#18#19#24#25#26#34#
    }

    [Fact]
    public void ParseClassRankBuffs_LoadsTiers()
    {
        var buffs = ShopAndMiscParsers.ParseClassRankBuffs(DbDir + "/S_ClassRankBuff.ini");
        Assert.True(buffs.Count >= 60);
        Assert.True(buffs[0].Tiers.Count > 5);
    }

    [Fact]
    public void ParseDailyAwards_LoadsAll()
    {
        var awards = ShopAndMiscParsers.ParseDailyAwards(DbDir + "/S_DailyAward.ini");
        Assert.True(awards.Count >= 7);
        Assert.Equal(51464, awards[0].ItemId);
    }
}

public class LifeSkillGmTests
{
    private static readonly string DecodedDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    static LifeSkillGmTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void LifeSkill_GatherProducesItems()
    {
        var svc = LifeSkillService.LoadFromDataDir(DecodedDir);
        Assert.True(svc.NodeCount > 0);
        svc.SetSkillLevel(1, 100);
        var job = svc.StartGather(1, 11);
        Assert.NotNull(job);
        Assert.Equal(200, job!.RemainingSeconds);
        Assert.Empty(svc.TickGather(1, 100));
        var items = svc.TickGather(1, 100);
        Assert.NotEmpty(items);
        Assert.Contains(1086, items);
    }

    [Fact]
    public void LifeSkill_LowLevelRejected()
    {
        var svc = LifeSkillService.LoadFromDataDir(DecodedDir);
        svc.SetSkillLevel(1, 0);
        Assert.Null(svc.StartGather(1, 11));
    }

    [Fact]
    public void GmCommands_LoadAndDispatch()
    {
        var svc = GmCommandService.LoadFromDataDir(DecodedDir);
        Assert.True(svc.CommandCount > 300);
        Assert.Equal(4, svc.RequiredAccess("ban"));
        Assert.Null(svc.Execute("/ban 1 2 reason", gmLevel: 0));
        Assert.NotNull(svc.Execute("/ban 1 2 reason", gmLevel: 4));
        svc.Register("ban", (lvl, args) => $"banned {args[0]}");
        Assert.Equal("banned 1", svc.Execute("/ban 1 2 reason", 4));
    }
}
