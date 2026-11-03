using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan futur-feature/01 côté client : anneaux du tracker,
/// bouton « Use » (item de quête en sac), repères d'aire/de site
/// d'usage, ops de partage (87 op 6/7) et chaînes FR.</summary>
public class QuestObjectiveLogicTests
{
    [Theory]
    [InlineData(MissionObjectives.Kind.ItemCollect, 3, 10, 2, 5, 2)]   // clé partagée ignorée
    [InlineData(MissionObjectives.Kind.ItemCollect, 0, 10, 10, 10, 10)]
    [InlineData(MissionObjectives.Kind.Treasure, 0, 77, 2, 2, 2)]      // sac > compteur
    [InlineData(MissionObjectives.Kind.Treasure, 1, 77, 0, 3, 1)]
    [InlineData(MissionObjectives.Kind.Kill, 4, 0, 99, 5, 4)]          // pas d'item
    [InlineData(MissionObjectives.Kind.Treasure, 0, 77, 9, 2, 2)]      // borné
    public void Progress_UsesBagForItemObjectives(
        MissionObjectives.Kind k, int counter, int itemId, int owned,
        int req, int expected) =>
        Assert.Equal(expected,
            QuestObjectiveLogic.Progress(k, counter, itemId, owned, req));

    [Fact]
    public void AllDone_NeedsEveryObjective()
    {
        Assert.True(QuestObjectiveLogic.AllDone(new[] { (2, 2), (1, 1) }));
        Assert.False(QuestObjectiveLogic.AllDone(new[] { (2, 2), (0, 1) }));
        Assert.False(QuestObjectiveLogic.AllDone(
            Array.Empty<(int, int)>()));
        Assert.True(QuestObjectiveLogic.ShowsCount(
            MissionObjectives.Kind.Kill));
        Assert.False(QuestObjectiveLogic.ShowsCount(
            MissionObjectives.Kind.NpcTalk));
    }

    [Fact]
    public void IsUsable_UseTypeOrEnchant()
    {
        Assert.True(QuestObjectiveLogic.IsUsable(2, 0));
        Assert.True(QuestObjectiveLogic.IsUsable(0, 53031));
        Assert.False(QuestObjectiveLogic.IsUsable(0, 0));
    }

    [Fact]
    public void FindStack_LowestBagThenSlot()
    {
        var slots = new[]
        {
            (Bag: 1, Slot: 0, ItemId: 32990),
            (Bag: 0, Slot: 9, ItemId: 32990),
            (Bag: 0, Slot: 3, ItemId: 32990),
            (Bag: 0, Slot: 1, ItemId: 5),
        };
        Assert.Equal((0, 3), QuestObjectiveLogic.FindStack(slots, 32990));
        Assert.Null(QuestObjectiveLogic.FindStack(slots, 777));
    }

    private static string[] EnchantRow(params (int Cmd, int[] Vars)[] blocks)
    {
        var raw = Enumerable.Repeat("", 60).ToArray();
        for (var k = 0; k < blocks.Length; k++)
        {
            var at = 9 + k * 7;
            raw[at] = blocks[k].Cmd.ToString();
            for (var i = 0; i < blocks[k].Vars.Length; i++)
                raw[at + 1 + i] = blocks[k].Vars[i].ToString();
        }
        return raw;
    }

    /// <summary>Enchant 53031 (Sound Wave Device) : 1075 « 58334,1,10,160 ».</summary>
    [Fact]
    public void UseSiteOf_DecodesSummonInEventArea()
    {
        var site = QuestObjectiveLogic.UseSiteOf(EnchantRow(
            (1075, new[] { 58334, 1, 10, 160 })));
        Assert.Equal((10, 160, 58334), site);
        // Bloc positionnel en 2ᵉ position.
        Assert.Equal((21, 154, 58398), QuestObjectiveLogic.UseSiteOf(
            EnchantRow((2051, new[] { 5 }),
                (2045, new[] { 58398, 1, 21, 154 }))));
        // 1074 (pas de node) → usage au lanceur : aucun site.
        Assert.Null(QuestObjectiveLogic.UseSiteOf(EnchantRow(
            (1074, new[] { 21871, 1 }))));
        Assert.Null(QuestObjectiveLogic.UseSiteOf(new string[5]));
    }

    [Fact]
    public void ScenePoints_AreaTravelAndUseSite()
    {
        var sc = new SceneTable.Scene { MapCode = "S010" };
        sc.EventAreas.Add(new SceneTable.EventArea
            { Id = 7, X = 100f, Y = 200f, Radius = 3, Type = "GATEWAY" });
        sc.Gateways.Add(new SceneTable.Gateway
            { RowId = 1, EventAreaId = 7, KeyId = 51 });
        sc.PositionGroups[160] = new()
            { (355.6f, 341.8f, 3.9f, 1.5f) };
        Assert.Equal((100f, 200f), QuestObjectiveLogic.AreaTravelPoint(sc, 51));
        Assert.Null(QuestObjectiveLogic.AreaTravelPoint(sc, 52));
        Assert.Equal((355.6f, 341.8f), QuestObjectiveLogic.UseSitePoint(sc, 160));
        Assert.Null(QuestObjectiveLogic.UseSitePoint(sc, 161));
    }

    [Fact]
    public void ShareOps_Wire()
    {
        // Partage : op 6, mid 500, 0xFFFF — puis réponse op 7 flag 1/0.
        var w = new BinWriter();
        new ZoneMissionListOp { Field1 = MissionListOps.ShareStart,
            Field2 = 500, Field3 = 0xFFFF }.Serialize(w);
        Assert.Equal(Convert.FromHexString("0600f4010000ffff"), w.ToArray());
        w = new BinWriter();
        new ZoneMissionListOp { Field1 = MissionListOps.ShareAnswer,
            Field2 = 500, Field3 = 1 }.Serialize(w);
        Assert.Equal(Convert.FromHexString("0700f40100000100"), w.ToArray());
        Assert.Equal((short)7, MissionSheetOps.Share);
    }

    [Fact]
    public void Objective_ItemIdDefaultsToZero()
    {
        Assert.Equal(0, new MissionObjectives.Objective(1, "x", 1).ItemId);
        Assert.Equal(32990,
            new MissionObjectives.Objective(1, "x", 1, 32990).ItemId);
    }

    [Fact]
    public void FrenchTsv_HasQuestPlanStrings()
    {
        var dir = Directory.GetCurrentDirectory();
        string? path = null;
        for (var i = 0; i < 8 && path == null; i++)
        {
            var p = Path.Combine(dir, "godot_project", "gamedata",
                "ui_text_en_fr.tsv");
            if (File.Exists(p)) path = p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        Assert.NotNull(path);
        var lines = File.ReadAllLines(path!);
        foreach (var key in new[] { "Use {0}", "Use", "Share", "Accept",
                     "Decline" })
            Assert.Contains(lines, l => l.StartsWith(key + "\t",
                StringComparison.Ordinal));
    }
}
