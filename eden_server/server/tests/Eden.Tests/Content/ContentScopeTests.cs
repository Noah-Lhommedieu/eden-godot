using Eden.Core.Data.Parsers;
using Eden.Core.Game.Content;

namespace Eden.Tests.Content;

/// <summary>Règle « présent en jeu et ≤ Lv90 » (C3) sur un petit monde synthétique.</summary>
public class ContentScopeTests
{
    private static MonsterParser.MonsterDef Mob(int id, int lv) => new() { Id = id, Level = lv };
    private static ItemParser.ItemDef Item(int id, int lv = 0, string name = "x") => new() { Id = id, RequiredLevel = lv, Name = name };
    private static MissionParser.MissionDef Quest(int id, int minLv, params int[] rewards) => new()
    {
        Id = id,
        Conditions = { new() { Name = "If_CharLevel", Args = { ">=", minLv.ToString() } } },
        Rewards = rewards.Select(r => new MissionParser.ScriptCommand { Name = "RewardItem", Args = { r.ToString(), "1" } }).ToList(),
    };

    private static ContentScopeResult Run(params ScopeMap[] maps) => ContentScope.Compute(maps,
        new Dictionary<int, MonsterParser.MonsterDef> { [1] = Mob(1, 10), [2] = Mob(2, 95) },
        new Dictionary<int, ItemParser.ItemDef> { [100] = Item(100), [101] = Item(101, 95), [102] = Item(102), [103] = Item(103), [104] = Item(104, 0, ""), [105] = Item(105), [106] = Item(106) },
        new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 100, 101 }, [2] = new[] { 102 } },
        new Dictionary<int, IReadOnlyList<int>> { [7] = new[] { 104 } },
        new[] { Quest(500, 20, 103), Quest(501, 199, 102) },
        new[] { (105, (IReadOnlyList<int>)new[] { 100, 103 }), (106, (IReadOnlyList<int>)new[] { 100, 999 }) });

    [Fact]
    public void MonsterAndDropsFollowMapAndLevel()
    {
        var r = Run(new ScopeMap(1, 1, new[] { 1, 2 }, new[] { 7 }), new ScopeMap(2, 120, new[] { 1 }, Array.Empty<int>()));
        Assert.Equal(new[] { 1 }, r.Maps);
        Assert.Equal(new[] { 1 }, r.Monsters);          // mob 2 = niveau 95
        Assert.Equal(new[] { 7 }, r.Npcs);
        Assert.DoesNotContain(101, r.Items);            // niveau requis 95
        Assert.DoesNotContain(102, r.Items);            // drop d'un mob hors périmètre, quête 501 (niv. 199) exclue
        Assert.DoesNotContain(104, r.Items);            // sans nom
    }

    [Fact]
    public void QuestRewardAndRecipeClosure()
    {
        var r = Run(new ScopeMap(1, 1, new[] { 1 }, new[] { 7 }));
        Assert.Equal(new[] { 500 }, r.Quests);
        Assert.Contains(103, r.Items);                  // récompense de la quête 500
        Assert.Contains(105, r.Items);                  // recette : 100 + 103 atteignables
        Assert.DoesNotContain(106, r.Items);            // matériau 999 inatteignable
    }
}
