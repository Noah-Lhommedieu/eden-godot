using EdenClient.Data;
using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Arbre natif de l'archive (<see cref="CollectionTree"/>) :
/// spécification extraite de rf_game/client_game @0x00fe58b0..e0 et
/// validée sur corpus/fr-full (c_collection col3 ↔ kinds c_item).</summary>
public class CollectionTreeTests
{
    /// <summary>Comptes c_collection par onglet (col3) — relevés sur
    /// corpus/fr-full V.3|8| (7970 cartes, 2026-11).</summary>
    private static readonly Dictionary<int, int> ExpectedTabCounts = new()
    {
        [1] = 1108, [2] = 427, [3] = 1637, [4] = 441, [5] = 249,
        [6] = 58, [7] = 87, [8] = 70, [9] = 56, [10] = 55,
        [11] = 56, [12] = 54, [13] = 75, [14] = 56, [15] = 39,
        [16] = 74, [17] = 58, [18] = 56, [19] = 80, [20] = 79,
        [21] = 59, [22] = 83, [23] = 345, [24] = 555, [25] = 345,
        [26] = 340, [27] = 120, [28] = 519, [29] = 704, [30] = 75,
        [31] = 10,
    };

    [Fact]
    public void Leaves_32Native_Groups4_18_6_4()
    {
        Assert.Equal(32, CollectionTree.Leaves.Length);
        Assert.Equal(4, CollectionTree.Leaves.Count(l => l.Group == 0));
        Assert.Equal(18, CollectionTree.Leaves.Count(l => l.Group == 1));
        Assert.Equal(6, CollectionTree.Leaves.Count(l => l.Group == 2));
        Assert.Equal(4, CollectionTree.Leaves.Count(l => l.Group == 3));
        // Un onglet = une feuille (clé de rangement unique).
        Assert.Equal(32,
            CollectionTree.Leaves.Select(l => l.Tab).Distinct().Count());
    }

    [Fact]
    public void Leaves_TabOrder_Native()
    {
        // Ordre natif DAT_00fe58e0 : costumes 1-4, armes 6-22 puis la
        // fusille (tab31), armures/accessoires 23-28, spécial
        // (montures tab5, familiers 29, boîte 30, divers 32).
        var tabs = CollectionTree.Leaves.Select(l => l.Tab).ToArray();
        Assert.Equal(
            new[] { 1, 2, 3, 4,
                    6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                    18, 19, 20, 21, 22, 31,
                    23, 24, 25, 26, 27, 28,
                    5, 29, 30, 32 },
            tabs);
    }

    [Theory]
    [InlineData(1, 0)]   // Coiffe → Appearance
    [InlineData(4, 0)]   // Armes (costume) → Appearance
    [InlineData(6, 1)]   // Poignards → Weapon
    [InlineData(22, 1)]  // Bouclier → Weapon
    [InlineData(31, 1)]  // Fusille → Weapon (fin du groupe)
    [InlineData(23, 2)]  // Armure Lourde → Armor
    [InlineData(28, 2)]  // Trophée → Armor
    [InlineData(5, 3)]   // Montures → Special Items
    [InlineData(29, 3)]  // Familier → Special Items
    [InlineData(30, 3)]  // Boîte Mystique → Special Items
    [InlineData(32, 3)]  // Autres objets → Special Items
    [InlineData(99, 3)]  // onglet inconnu → repli Special Items
    public void GroupOf_Tab(int tab, int group) =>
        Assert.Equal(group, CollectionTree.GroupOf(tab));

    [Theory]
    [InlineData(6, "g1|t6")]
    [InlineData(5, "g3|t5")]
    [InlineData(31, "g1|t31")]
    [InlineData(32, "g3|t32")]
    [InlineData(99, "g3|t32")]  // onglet inconnu → feuille Autres objets
    public void KeyOf_StableKeys(int tab, string key) =>
        Assert.Equal(key, CollectionTree.KeyOf(tab));

    /// <summary>Les libellés natifs existent tous dans t_textindex
    /// (ids vérifiés sur le corpus TW/EN : 1715-1724, 2757-2761, 5465,
    /// 2870-2881, 8155, 5426, 5520, 2886 ; groupes FR équivalents).</summary>
    [Fact]
    public void Leaves_TextIndexIds_Documented()
    {
        var ids = CollectionTree.Leaves.Select(l => l.TextIndex).ToArray();
        Assert.Equal(new[]
        {
            1715, 1716, 1717, 1718,
            1720, 1721, 1722, 1723, 1724, 2757, 2758, 2759, 2760,
            5465, 2761, 2870, 2871, 2872, 2873, 2874, 2875, 8155,
            2876, 2877, 2878, 2879, 2880, 2881,
            1719, 5426, 5520, 2886,
        }, ids);
        Assert.Equal(4, CollectionTree.GroupTextIndex.Length);
        Assert.Equal(4, CollectionTree.GroupEnNames.Length);
    }

    // ------------------- corpus FR réel -----------------------------

    private static string? FindFrRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName,
                   "corpus", "fr-full", "data", "db", "c_collection.ini")))
            dir = dir.Parent;
        return dir == null ? null
            : Path.Combine(dir.FullName, "corpus", "fr-full");
    }

    /// <summary>Chaque carte de c_collection possède une feuille :
    /// les onglets FR (1-31) sont tous couverts par l'arbre natif, et
    /// les comptes par onglet valent les comptes attendus.</summary>
    [Fact]
    public void Corpus_EveryCardTab_HasLeaf()
    {
        var fr = FindFrRoot();
        if (fr == null) return;   // environnement sans corpus
        CollectionTable.PathProvider = rel =>
        {
            var p = Path.Combine(fr, rel.ToLowerInvariant());
            return File.Exists(p) ? p : null;
        };
        var cards = CollectionTable.Cards();
        Assert.Equal(7970, cards.Count);
        var leafTabs = CollectionTree.Leaves.Select(l => l.Tab)
            .ToHashSet();
        var perTab = new Dictionary<int, int>();
        foreach (var c in cards)
        {
            Assert.Contains(c.Tab, leafTabs);
            perTab[c.Tab] = perTab.GetValueOrDefault(c.Tab) + 1;
        }
        foreach (var (tab, n) in ExpectedTabCounts)
            Assert.Equal(n, perTab.GetValueOrDefault(tab, 0));
    }

    /// <summary>Jointure c_item + c_itemmall (ordre de
    /// InventoryWindow.EnsureItemTable : normal puis mall) — chaque
    /// onglet arme porte le kind attendu et aucun onglet costume
    /// n'est rangé par EquipLoc.</summary>
    [Fact]
    public void Corpus_WeaponTabs_MatchKinds()
    {
        var fr = FindFrRoot();
        if (fr == null) return;
        var items = ItemTable.Load(
            Path.Combine(fr, "data/db/c_item.ini"));
        foreach (var kv in ItemTable.Load(
                     Path.Combine(fr, "data/db/c_itemmall.ini")))
            items.TryAdd(kv.Key, kv.Value);
        CollectionTable.PathProvider = rel =>
        {
            var p = Path.Combine(fr, rel.ToLowerInvariant());
            return File.Exists(p) ? p : null;
        };
        // onglet → kind c_item dominant attendu (vérifié corpus).
        var kindOfTab = new Dictionary<int, int>
        {
            [6] = 17, [7] = 18, [8] = 19, [9] = 24, [10] = 25,
            [11] = 26, [12] = 20, [13] = 21, [14] = 22, [15] = 23,
            [16] = 27, [17] = 28, [18] = 29, [19] = 31, [20] = 33,
            [21] = 32, [22] = 34, [31] = 30,
        };
        foreach (var c in CollectionTable.Cards())
        {
            if (!kindOfTab.TryGetValue(c.Tab, out var kind)) continue;
            Assert.True(items.TryGetValue(c.Id, out var d),
                $"carte {c.Id} (tab {c.Tab}) sans définition item");
            Assert.Equal(kind, d!.Kind);
        }
    }

    /// <summary>Régression « Jambe » : la catégorie archive n'est PAS
    /// l'EquipLoc/kind c_item. Corpus FR :
    /// <list type="bullet">
    /// <item>l'onglet 1 « Coiffe » mêle des EquipLoc 1 (tête), 4 ET 5 —
    /// un regroupement par emplacement afficherait des coiffes sous
    /// « Jambe »/autres (bug rapporté) ;</item>
    /// <item>le kind dominant des onglets 1-3 est 68 (costume), avec
    /// des exceptions natives (montures kind 44 archivées en coiffe…) ;</item>
    /// <item>l'onglet 4 « Armes » porte les kinds costume-arme 61-67,
    /// jamais les kinds d'arme réelles 17-34 du groupe 1.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Corpus_CostumeTabs_AreNotEquipSlots()
    {
        var fr = FindFrRoot();
        if (fr == null) return;
        var items = ItemTable.Load(
            Path.Combine(fr, "data/db/c_item.ini"));
        foreach (var kv in ItemTable.Load(
                     Path.Combine(fr, "data/db/c_itemmall.ini")))
            items.TryAdd(kv.Key, kv.Value);
        CollectionTable.PathProvider = rel =>
        {
            var p = Path.Combine(fr, rel.ToLowerInvariant());
            return File.Exists(p) ? p : null;
        };
        var locsByTab = new Dictionary<int, HashSet<int>>();
        var kindsByTab = new Dictionary<int, Dictionary<int, int>>();
        var tab4WeaponKinds = new List<(int id, int kind)>();
        foreach (var c in CollectionTable.Cards())
        {
            if (c.Tab is < 1 or > 4) continue;
            if (!items.TryGetValue(c.Id, out var d)) continue;
            (locsByTab.TryGetValue(c.Tab, out var locs)
                ? locs : locsByTab[c.Tab] = new()).Add(d!.EquipLoc);
            var kinds = kindsByTab.TryGetValue(c.Tab, out var k)
                ? k : kindsByTab[c.Tab] = new();
            kinds[d.Kind] = kinds.GetValueOrDefault(d.Kind) + 1;
            if (c.Tab == 4 && d.Kind is >= 17 and <= 34)
                tab4WeaponKinds.Add((c.Id, d.Kind));
        }
        // Tab1 « Coiffe » : l'EquipLoc ne peut pas servir de catégorie —
        // les coiffes-archives couvrent tête ET d'autres emplacements.
        Assert.True(locsByTab[1].Count >= 3,
            $"tab1 ne couvre que {locsByTab[1].Count} EquipLoc — " +
            "l'onglet seul fait foi, pas l'emplacement");
        // Kind dominant 68 (costume) sur les onglets 1-3.
        foreach (var tab in new[] { 1, 2, 3 })
        {
            var dominant = kindsByTab[tab]
                .MaxBy(kv => kv.Value).Key;
            Assert.Equal(68, dominant);
        }
        // L'onglet 4 « Armes » (costume) : aucun kind d'arme réel 17-34.
        Assert.Empty(tab4WeaponKinds);
    }
}
