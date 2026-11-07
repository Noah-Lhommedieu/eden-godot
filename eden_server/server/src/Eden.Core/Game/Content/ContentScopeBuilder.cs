using Eden.Core.Game.Services;

namespace Eden.Core.Game.Content;

/// <summary>Assemble les entrées de <see cref="ContentScope.Compute"/> depuis les tables chargées et les scènes.</summary>
public static class ContentScopeBuilder
{
    public static ContentScopeResult Build(GameDataService gd, SceneService scenes)
    {
        var maps = new List<ScopeMap>();
        foreach (var n in gd.Nodes)
        {
            var scene = scenes.GetScene(n.MapCode);
            if (scene == null) continue;
            maps.Add(new ScopeMap(n.Id, n.MinLevel,
                scene.Monsters.Select(m => m.MonsterId).Distinct().ToList(),
                scene.Npcs.Select(p => p.NpcId).Distinct().ToList()));
        }
        var drops = gd.DropTables.ToDictionary(d => d.Id,
            d => (IReadOnlyList<int>)d.Entries.Concat(d.BreakEntries).Select(e => e.ItemId).Where(i => i > 0).Distinct().ToList());
        var stores = gd.Stores.ToDictionary(s => s.Id, s => s.Items.Select(i => i.ItemId).ToList());
        var storeItemsByNpc = gd.Npcs.Where(p => p.StoreId > 0 && stores.ContainsKey(p.StoreId))
            .ToDictionary(p => p.Id, p => (IReadOnlyList<int>)stores[p.StoreId]);
        var recipes = gd.WorkCombineById.Values.Where(r => r.ProductItemId > 0)
            .Select(r => (r.ProductItemId, (IReadOnlyList<int>)r.Materials.Select(m => m.ItemId).Where(i => i > 0).ToList()));
        return ContentScope.Compute(maps, gd.Monsters.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()),
            gd.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First()),
            drops, storeItemsByNpc, gd.Missions, recipes);
    }
}
