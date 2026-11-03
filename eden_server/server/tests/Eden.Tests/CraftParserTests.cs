using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;

namespace Eden.Tests;

public class CraftParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static CraftParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseElfCombine_LoadsRecipes()
    {
        var recipes = CraftRecipeParser.ParseElfCombine(DbDir + "/S_ElfCombine.ini");
        Assert.True(recipes.Count > 900);
        // Recette 1: result 30725 qty 1
        var r1 = recipes[0];
        Assert.Equal(1, r1.Id);
        Assert.Equal(30725, r1.ResultItemId);
        Assert.Equal(1, r1.ResultQuantity);
        Assert.Equal(50, r1.GoldCost);
    }

    [Fact]
    public void ParseElfCombine_BonusScriptsDecoded()
    {
        var recipes = CraftRecipeParser.ParseElfCombine(DbDir + "/S_ElfCombine.ini");
        var r7 = recipes.Find(r => r.Id == 7);
        Assert.NotNull(r7);
        Assert.True(r7!.BonusScripts.Count >= 3);
        Assert.Equal(2522, r7.BonusScripts[0].ItemId1);
        Assert.Contains("Friend Plus", r7.BonusScripts[0].Effect1);
    }

    [Fact]
    public void ParseFamilyCombine_LoadsRecipes()
    {
        var recipes = CraftRecipeParser.ParseFamilyCombine(DbDir + "/S_FamilyCombine.ini");
        Assert.True(recipes.Count > 3000);
        var r1 = recipes[0];
        Assert.Equal(1, r1.Id);
        Assert.Equal(40002, r1.ResultItemId);
        Assert.Equal(7, r1.Category);
        Assert.Equal(2048, r1.Flags);
    }

    [Fact]
    public void ParseItemCombo_LoadsCombos()
    {
        var combos = CraftRecipeParser.ParseItemCombo(DbDir + "/S_ItemCombo.ini");
        Assert.NotEmpty(combos);
        var c1 = combos[0];
        Assert.Equal(1, c1.Id);
        Assert.Equal("G00003", c1.ComboCode);
        Assert.Contains(17, c1.ApplicableItemTypes);
        Assert.Contains(34, c1.ApplicableItemTypes);
    }

    [Fact]
    public void IniTable_S_Item_ParsesClearText()
    {
        // S_Item.ini n'est PAS chiffre — format |V.11|78|
        var table = IniTable.ParseFile(DbDir + "/S_Item.ini", Encoding.GetEncoding("Big5"));
        Assert.Equal(78, table.ColumnCount);
        Assert.True(table.Rows.Count > 1000);
    }
}

public class EnchantParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    [Fact]
    public void ParseEnchant_LoadsEntries()
    {
        var enchants = EnchantParser.Parse(DbDir + "/S_Enchant.ini");
        Assert.True(enchants.Count > 19000);
    }

    [Fact]
    public void ParseEnchant_FirstEntryCorrect()
    {
        var enchants = EnchantParser.Parse(DbDir + "/S_Enchant.ini");
        var e1 = enchants[0];
        Assert.Equal(10001, e1.Id);
        Assert.Equal("E0302", e1.GroupCode);
        Assert.Equal("s14131", e1.SpellRef);
        Assert.Equal(4, e1.Level);
        Assert.Equal(8208, e1.TriggerType);
        Assert.True(e1.Enabled);
    }

    [Fact]
    public void EnchantService_EvaluateAppliesSpell()
    {
        var enchants = EnchantParser.Parse(DbDir + "/S_Enchant.ini");
        var svc = new Eden.Core.Game.Services.EnchantService(enchants);
        // enchant 10005 applique spell 637 avec chance 100
        var results = svc.Evaluate(10005, Eden.Core.Game.Services.EnchantService.TriggerEvent.OnAttack);
        Assert.Contains(results, r => r.SpellId == 637);
    }
}

public class ItemParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static ItemParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseItems_LoadsAllItems()
    {
        var items = ItemParser.Parse(DbDir + "/S_Item.ini");
        Assert.True(items.Count > 20000); // 20426 items (lignes physiques incluent descriptions multi-lignes)
        var item1 = items[0];
        Assert.Equal(1, item1.Id);
        Assert.Equal("Q00134", item1.Code);
        Assert.Equal("G00005", item1.TypeCode);
        // col14 = niveau requis natif (col8 = kind=44 — le 44 ancien
        // du test indexait la mauvaise colonne).
        Assert.Equal(65, item1.RequiredLevel);
        Assert.Equal(7500, item1.BuyPrice);
        Assert.Equal(2250000, item1.SellPrice);
    }
}

public class MonsterParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static MonsterParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseMonsters_LoadsAll()
    {
        var monsters = MonsterParser.Parse(DbDir + "/S_Monster.ini");
        Assert.True(monsters.Count > 12000);
    }

    [Fact]
    public void ParseMonsters_FirstEntryIsRoseKnight()
    {
        var monsters = MonsterParser.Parse(DbDir + "/S_Monster.ini");
        var m = monsters[0];
        Assert.Equal(20001, m.Id);
        Assert.Equal("M312040", m.ModelCode);
        Assert.Equal(80, m.Level);
        Assert.Equal(202589, m.HP);
        Assert.False(m.IsBoss); // 202k HP < 1M
    }

    [Fact]
    public void MonsterService_SpawnAndKill()
    {
        var monsters = MonsterParser.Parse(DbDir + "/S_Monster.ini");
        var svc = new Eden.Core.Game.Services.MonsterService(monsters);
        var m = svc.Spawn(20001, 100, 0, 0, 0);
        Assert.NotNull(m);
        Assert.Equal(202589, m!.CurrentHP);
        Assert.False(svc.ApplyDamage(m.InstanceId, 1000).Any());
        Assert.True(m.IsAlive);
        svc.ApplyDamage(m.InstanceId, 300000);
        Assert.False(m.IsAlive);
    }

    [Fact]
    public void MonsterService_BossHasResistances()
    {
        var monsters = MonsterParser.Parse(DbDir + "/S_Monster.ini");
        var svc = new Eden.Core.Game.Services.MonsterService(monsters);
        var turret = svc.GetDefinition(20002); // 飛箭 9M HP
        Assert.NotNull(turret);
        Assert.True(turret!.IsBoss);
        Assert.All(turret.Resistances, r => Assert.Equal(100, r));
    }
}

public class SpellParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static SpellParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseSpells_LoadsAll()
    {
        var spells = SpellParser.Parse(DbDir + "/S_Spell.ini");
        Assert.True(spells.Count > 4000);
        var s1 = spells[0];
        Assert.Equal(50001, s1.Id);
        Assert.Equal("J001", s1.Code);
        Assert.Equal(52001, s1.ResultSpellId);
    }

    [Fact]
    public void SpellService_BuffStacking()
    {
        var spells = SpellParser.Parse(DbDir + "/S_Spell.ini");
        var svc = new Eden.Core.Game.Services.SpellService(spells);
        Assert.True(svc.ApplySpell(50001, 100, 200));
        Assert.True(svc.ApplySpell(50001, 100, 200));
        var buffs = svc.GetBuffs(200);
        Assert.Single(buffs);
        Assert.Equal(2, buffs[0].Stacks);
        svc.ClearAllBuffs(200);
        Assert.Empty(svc.GetBuffs(200));
    }
}

public class MissionParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static MissionParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private sealed class FakePlayer : Eden.Core.Game.Services.MissionService.IPlayerContext
    {
        public int CharacterId { get; set; } = 1;
        public int Level { get; set; } = 99;
        public int Gender { get; set; } = 0;
        public int Fms { get; set; } = 999;
        public bool HasBuff(int b) => true;
        public int ItemCount(int i) => 99;
    }

    [Fact]
    public void ParseMissions_LoadsAll()
    {
        var missions = MissionParser.Parse(DbDir + "/S_Mission.ini");
        Assert.True(missions.Count > 2800);
        var m = missions[0];
        Assert.Equal(1009, m.Id);
        Assert.Equal("script01", m.ScriptName);
    }

    [Fact]
    public void ParseScript_ExtractsRewardCommands()
    {
        var cmds = MissionParser.ParseScript(
            "#:RewardGold 7:RewardExp 38:RewardReputation 1 + 100:RewardItem 44025 1::::");
        Assert.True(cmds.Count >= 4);
        Assert.Equal("RewardGold", cmds[0].Name);
        Assert.Equal("7", cmds[0].Args[0]);
        Assert.Equal("RewardItem", cmds[3].Name);
        Assert.Equal("44025", cmds[3].Args[0]);
    }

    [Fact]
    public void MissionService_AcceptAndComplete()
    {
        var missions = MissionParser.Parse(DbDir + "/S_Mission.ini");
        var svc = new Eden.Core.Game.Services.MissionService(missions);
        var player = new FakePlayer();
        Assert.True(svc.CanAccept(player, 1009));
        var state = svc.Accept(player, 1009);
        Assert.NotNull(state);
        svc.NotifyEvent(1, "NPCTalk", 50003);
        if (svc.IsComplete(1, 1009))
        {
            var rewards = svc.Complete(1, 1009);
            Assert.Contains(rewards, r => r.Name == "RewardGold");
        }
    }

    [Fact]
    public void MissionService_LevelGateBlocksAccept()
    {
        var missions = MissionParser.Parse(DbDir + "/S_Mission.ini");
        var svc = new Eden.Core.Game.Services.MissionService(missions);
        var lowLvl = new FakePlayer { Level = 1 };
        // mission avec condition If_CharLevel >= 80 doit etre refusee
        var gated = missions.Find(m => m.Conditions.Any(c =>
            c.Name == "If_CharLevel" && c.Args.Count > 1 &&
            int.TryParse(c.Args[1], out var v) && v >= 80));
        Assert.NotNull(gated);
        Assert.False(svc.CanAccept(lowLvl, gated!.Id));
    }
}

public class GameDataParsersTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static GameDataParsersTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseClassDropItem_LoadsTables()
    {
        var tables = GameDataParsers.ParseClassDropItem(DbDir + "/S_ClassDropItem.ini");
        Assert.True(tables.Count > 200);
        var t = tables[0];
        Assert.Equal(59013, t.Id);
        Assert.NotEmpty(t.Entries);
        Assert.Equal(33747, t.Entries[0].ItemId);
        Assert.Equal(100, t.Entries[0].Chance);
    }

    [Fact]
    public void ParseTalents_LoadsTrees()
    {
        var trees = GameDataParsers.ParseTalents(DbDir + "/S_Talent.ini");
        Assert.True(trees.Count > 900);
        var t = trees[0];
        Assert.Equal(11, t.Id);
        Assert.NotEmpty(t.Slots);
        Assert.Equal(52582, t.Slots[0].SpellId);
    }

    [Fact]
    public void ParseTitles_LoadsTitles()
    {
        var titles = GameDataParsers.ParseTitles(DbDir + "/S_Title.ini");
        Assert.True(titles.Count > 300);
        var t = titles[0];
        Assert.Equal(1, t.Id);
        Assert.Equal(53090, t.SpellId);
    }

    [Fact]
    public void ParseAchievements_LoadsAll()
    {
        var list = GameDataParsers.ParseAchievements(DbDir + "/S_Achievement.ini");
        Assert.True(list.Count > 3800);
        Assert.Equal(58001, list[0].Id);
    }

    [Fact]
    public void ParseNodes_LoadsMaps()
    {
        var nodes = GameDataParsers.ParseNodes(DbDir + "/S_Node.ini");
        Assert.True(nodes.Count > 300);
        var n = nodes[0];
        Assert.Equal(1, n.Id);
        Assert.Equal("S001", n.MapCode);
        Assert.Equal("聖徽城", n.Name);
    }
}

public class ElfParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static ElfParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseElfs_LoadsAll()
    {
        var elfs = ElfParser.Parse(DbDir + "/S_Elf.ini");
        Assert.True(elfs.Count > 200);
        var e = elfs[0];
        Assert.Equal(1, e.Id);
        Assert.Equal("J10101", e.Code);
        Assert.Equal(50, e.Level);
        Assert.Equal(56001, e.SpellId);
        Assert.Contains(3001, e.SkillIds);
        Assert.NotEmpty(e.MoodScripts);
    }

    [Fact]
    public void ElfService_AcquireAndManage()
    {
        var elfs = ElfParser.Parse(DbDir + "/S_Elf.ini");
        var svc = new Eden.Core.Game.Services.ElfService(elfs);
        var inst = svc.Acquire(42, 1);
        Assert.NotNull(inst);
        Assert.Equal(100, inst!.Energy);
        Assert.True(svc.ConsumeEnergy(inst.InstanceId, 30));
        Assert.Equal(70, inst.Energy);
        Assert.False(svc.ConsumeEnergy(inst.InstanceId, 500));
        svc.Feed(inst.InstanceId, 50, 10);
        Assert.Equal(120, inst.Energy);
        svc.AdjustMood(inst.InstanceId, -50);
        Assert.Equal(60, inst.Mood);
    }
}

public class DropParserTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static DropParserTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseDropItem_LoadsTables()
    {
        var tables = DropAndSpeedParsers.ParseDropItem(DbDir + "/S_DropItem.ini");
        Assert.True(tables.Count > 4000);
        var t = tables[0];
        Assert.Equal(79, t.Id);
        Assert.NotEmpty(t.Entries);
        Assert.Equal(100f, t.Entries[0].ChancePercent);
    }

    [Fact]
    public void ParseDropItem_WeightedTable()
    {
        var tables = DropAndSpeedParsers.ParseDropItem(DbDir + "/S_DropItem.ini");
        var multi = tables.Find(t => t.Id == 20863)!;
        Assert.True(multi.Entries.Count >= 4);
        Assert.Equal(100f, multi.Entries.Sum(e => e.ChancePercent));
    }

    [Fact]
    public void ParseMonsterStep_LoadsSpeeds()
    {
        var speeds = DropAndSpeedParsers.ParseSpeedTable(DbDir + "/S_MonsterStep.ini");
        Assert.True(speeds.Count > 300);
        Assert.Equal("M001", speeds[0].Code);
        Assert.Equal(1.9f, speeds[0].WalkSpeed);
        Assert.Equal(4.5f, speeds[0].RunSpeed);
    }
}

public class LootServiceTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static LootServiceTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void RollDropTable_ReturnsItem()
    {
        var drops = DropAndSpeedParsers.ParseDropItem(DbDir + "/S_DropItem.ini");
        var classDrops = GameDataParsers.ParseClassDropItem(DbDir + "/S_ClassDropItem.ini");
        var svc = new Eden.Core.Game.Services.LootService(drops, classDrops);

        // Table 79 = 100% chance → toujours un drop
        var loot = svc.RollDropTable(79);
        Assert.NotNull(loot);
        Assert.Equal(80, loot!.ItemId); // col 11 = 80 = itemId
    }

    [Fact]
    public void RollClassDrop_Chance100AlwaysDrops()
    {
        var drops = DropAndSpeedParsers.ParseDropItem(DbDir + "/S_DropItem.ini");
        var classDrops = GameDataParsers.ParseClassDropItem(DbDir + "/S_ClassDropItem.ini");
        var svc = new Eden.Core.Game.Services.LootService(drops, classDrops);

        var loot = svc.RollClassDrop(59013); // table avec chances=100
        Assert.NotEmpty(loot);
        Assert.Equal(33747, loot[0].ItemId);
    }
}

public class WorldParsersTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static WorldParsersTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void ParseNpcs_LoadsAll()
    {
        var npcs = WorldParsers.ParseNpcs(DbDir + "/S_Npc.ini");
        Assert.True(npcs.Count > 100);
        var n = npcs[0];
        Assert.Equal(50001, n.Id);
        Assert.Equal("n106010", n.ModelCode);
        Assert.Contains("Talk", n.TalkScript);
    }

    [Fact]
    public void ParseDialogues_LoadsAll()
    {
        var map = WorldParsers.ParseDialogues(DbDir + "/S_Dialogue.ini");
        Assert.True(map.Count > 1000);
        Assert.True(map.ContainsKey(1));
    }

    [Fact]
    public void ParseTransport_LoadsRoutes()
    {
        var routes = WorldParsers.ParseTransport(DbDir + "/S_Transport.ini");
        Assert.True(routes.Count >= 40);
        Assert.Equal("聖徽城", routes[0].Name);
        Assert.Equal(75, routes[0].Costs[0]);
        Assert.Equal(375, routes[0].Costs[12]);
    }

    [Fact]
    public void ParseLifeSkills_LoadsNodes()
    {
        var nodes = WorldParsers.ParseLifeSkills(DbDir + "/S_LifeSkill.ini");
        Assert.NotEmpty(nodes);
        var n = nodes[0];
        Assert.Equal(11, n.Id);
        Assert.Equal(36792, n.SourceItemId);
        Assert.Equal(200, n.TimeSeconds);
        Assert.True(n.Products.Count >= 4);
        Assert.Equal(1086, n.Products[0].ItemId);
    }

    [Fact]
    public void ParsePartners_LoadsAll()
    {
        var partners = WorldParsers.ParsePartners(DbDir + "/S_Partner.ini");
        Assert.NotEmpty(partners);
        Assert.Equal(21834, partners[0].Id);
        Assert.Equal("I00557", partners[0].ItemCode);
    }
}
