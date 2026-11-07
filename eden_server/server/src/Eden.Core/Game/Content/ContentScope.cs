using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Content;

/// <summary>Contenu « présent en jeu » (PLAN-ACTION-3 §4.3) : identifiants triés par type.</summary>
public sealed record ContentScopeResult(
    IReadOnlyList<int> Maps, IReadOnlyList<int> Monsters, IReadOnlyList<int> Npcs,
    IReadOnlyList<int> Items, IReadOnlyList<int> Quests);

/// <summary>Une map avec les mobs et PNJ posés dans sa scène (entrée de <see cref="ContentScope.Compute"/>).</summary>
public sealed record ScopeMap(int NodeId, int MinLevel, IReadOnlyList<int> MonsterIds, IReadOnlyList<int> NpcIds);

/// <summary>
/// Règle UNIQUE « présent en jeu et ≤ Lv90 », partagée par le wiki, l'admin (filtre « valide ») et l'anglais.
/// Une map compte si elle a une scène et un niveau d'entrée ≤ <see cref="MaxLevel"/> ; un monstre si posé dans une
/// map comptée et de niveau ≤ 90 ; un PNJ s'il est posé dans une map comptée ; un objet s'il a une SOURCE
/// (drop d'un monstre compté, boutique d'un PNJ compté, récompense d'une quête comptée, produit d'une recette dont
/// tous les matériaux sont eux-mêmes comptés) ET un niveau requis ≤ 90 ; une quête si son niveau mini (If_CharLevel) ≤ 90.
/// </summary>
public static class ContentScope
{
    public const int MaxLevel = 90;

    /// <summary>
    /// Nom inutilisable en jeu : idéogrammes chinois résiduels (texte FR absent), marqueurs de test ou d'emplacement
    /// (« Place Holder », « Test Cottage »), vide. Ces entrées sont HORS périmètre (PLAN-ACTION-3 §4.3 : placeholders exclus).
    /// </summary>
    public static bool IsPlaceholderName(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || name.Any(c => c is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF')
        || name.Contains("Place Holder", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Test ", StringComparison.OrdinalIgnoreCase);

    public static ContentScopeResult Compute(
        IEnumerable<ScopeMap> maps,
        IReadOnlyDictionary<int, MonsterParser.MonsterDef> monsters,
        IReadOnlyDictionary<int, ItemParser.ItemDef> items,
        IReadOnlyDictionary<int, IReadOnlyList<int>> dropsByMonster,
        IReadOnlyDictionary<int, IReadOnlyList<int>> storeItemsByNpc,
        IEnumerable<MissionParser.MissionDef> missions,
        IEnumerable<(int ProductItemId, IReadOnlyList<int> MaterialItemIds)> recipes)
    {
        var mapIds = new SortedSet<int>(); var mobIds = new SortedSet<int>(); var npcIds = new SortedSet<int>();
        foreach (var m in maps.Where(m => m.MinLevel <= MaxLevel))
        {
            mapIds.Add(m.NodeId);
            foreach (var id in m.MonsterIds)
                if (monsters.TryGetValue(id, out var d) && d.Level <= MaxLevel) mobIds.Add(id);
            foreach (var id in m.NpcIds) npcIds.Add(id);
        }
        var quests = new SortedSet<int>(missions.Where(q => MinLevelOf(q) <= MaxLevel).Select(q => q.Id));
        var reachable = new HashSet<int>();
        foreach (var id in mobIds)
            if (dropsByMonster.TryGetValue(id, out var d)) reachable.UnionWith(d);
        foreach (var id in npcIds)
            if (storeItemsByNpc.TryGetValue(id, out var s)) reachable.UnionWith(s);
        foreach (var q in missions.Where(q => quests.Contains(q.Id)))
            reachable.UnionWith(RewardItems(q));
        var recipeList = recipes.ToList();
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (product, mats) in recipeList)
                if (!reachable.Contains(product) && mats.All(reachable.Contains)) { reachable.Add(product); grew = true; }
        }
        var inScope = reachable.Where(id => items.TryGetValue(id, out var it) && it.RequiredLevel <= MaxLevel
            && !string.IsNullOrWhiteSpace(it.Name)).OrderBy(id => id).ToList();
        return new ContentScopeResult(mapIds.ToList(), mobIds.ToList(), npcIds.ToList(), inScope, quests.ToList());
    }

    /// <summary>Niveau minimum d'une quête = plus grand <c>If_CharLevel &gt;= N</c> de ses conditions (0 si aucun).</summary>
    public static int MinLevelOf(MissionParser.MissionDef q) =>
        q.Conditions.Where(c => c.Name == "If_CharLevel")
            .Select(c => c.Args.Select(a => int.TryParse(a, out var n) ? n : 0).DefaultIfEmpty(0).Max())
            .DefaultIfEmpty(0).Max();

    /// <summary>Objets donnés en récompense (RewardItem / RewardChooseItem : premier argument numérique de chaque paire).</summary>
    public static IEnumerable<int> RewardItems(MissionParser.MissionDef q) =>
        q.Rewards.Where(r => r.Name is "RewardItem" or "RewardChooseItem")
            .SelectMany(r => r.Args.Take(1)).Select(a => int.TryParse(a, out var n) ? n : 0).Where(n => n > 0);
}
