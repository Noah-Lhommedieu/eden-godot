using EdenClient.Data;

namespace EdenClient.Data.Tables;

/// <summary>
/// Archive (collection de cartes/trophées) — <c>data/db/c_collection.ini</c>
/// (<c>|V.4|9|</c>, même layout que S_Collection serveur : col0 item_id
/// de la carte, col1 nom Big5, col2 flag special → clé de compteur 1001
/// sinon 1000, col4 ordre, col5 catégorie, col6 locate, col7 points,
/// col8 groupe) et <c>c_collectionpoint.ini</c> (col0 id, col3 enchant,
/// cols 4-13 = 5 paires (clé, seuil) → enchant permanent quand TOUTES
/// les paires non nulles sont atteintes — RefreshCollectionPointAward).
/// </summary>
public static class CollectionTable
{
    public sealed class Card
    {
        public int Id { get; init; }
        public string NameBig5 { get; init; } = "";
        public bool Special { get; init; }
        public int Order { get; init; }
        public string Category { get; init; } = "";
        public int Points { get; init; }
        public int Group { get; init; }
        /// <summary>Col3 — onglet natif du trésor (5 = montures,
        /// 29 = …) : c'est lui, pas le kind c_item, qui range la
        /// carte dans l'arbre de l'archive.</summary>
        public int Tab { get; init; }
    }

    public sealed class Award
    {
        public int Id { get; init; }
        public int Enchant { get; init; }
        public List<(int Key, int Threshold)> Conds { get; } = new();
    }

    /// <summary>Résolution des fichiers (posée par le client Godot).</summary>
    public static Func<string, string?>? PathProvider;

    private static List<Card>? _cards;
    private static List<Award>? _awards;
    private static readonly object _lock = new();

    public static IReadOnlyList<Card> Cards()
    {
        lock (_lock)
        {
            if (_cards != null) return _cards;
            var res = new List<Card>();
            try
            {
                var path = PathProvider?.Invoke("data/db/c_collection.ini");
                if (path != null)
                    foreach (var row in IniTable.ParseFile(path,
                                 EdenEncoding.Big5).Rows)
                    {
                        if (row.Length < 9 ||
                            !int.TryParse(row[0], out var id)) continue;
                        res.Add(new Card
                        {
                            Id = id,
                            NameBig5 = IniTable.GetStr(row, 1),
                            Special = IniTable.GetInt(row, 2) == 1,
                            Tab = IniTable.GetInt(row, 3),
                            Order = IniTable.GetInt(row, 4),
                            Category = IniTable.GetStr(row, 5),
                            Points = IniTable.GetInt(row, 7),
                            Group = IniTable.GetInt(row, 8),
                        });
                    }
            }
            catch { /* table absente : archive vide */ }
            return _cards = res;
        }
    }

    private static Dictionary<int, string>? _names;

    /// <summary>Noms FR de <c>t_collection.ini</c> (id|nom|) — repli des
    /// cartes dont l'item mall porte un gabarit « Testing ID » / « Ver.
    /// ID » (cartes d'autres régions : 190 cartes du corpus FR).</summary>
    public static string? CollectionName(int id)
    {
        lock (_lock)
        {
            if (_names == null)
            {
                _names = new Dictionary<int, string>();
                try
                {
                    var path = PathProvider?.Invoke("data/db/t_collection.ini");
                    if (path != null)
                        foreach (var kv in TextTable.Load(path))
                            _names[kv.Key] = kv.Value.Field(1).Trim();
                }
                catch (Exception e)
                {
#if GODOT
                    Godot.GD.PrintErr($"[archive] t_collection : {e.Message}");
#else
                    _ = e;
#endif
                }
            }
            return _names.TryGetValue(id, out var n) ? n : null;
        }
    }

    /// <summary>Gabarits de test laissés dans t_item/t_itemmall (« Testing
    /// ID », « JP Testing ID », « CN Ver. ID », « Ver. Thaïlandaise
    /// ID »…) ou vides / « ? » : jamais un vrai nom.</summary>
    public static bool IsPlaceholderName(string? n) =>
        string.IsNullOrWhiteSpace(n) || n.Trim() == "?" ||
        n.EndsWith(" ID", StringComparison.Ordinal);

    /// <summary>Nom affiché d'une carte : nom d'item (t_itemmall/t_item)
    /// s'il est réel, sinon t_collection, sinon libellé neutre — jamais
    /// de gabarit « Testing ID » ni de Big5 brut.</summary>
    public static string DisplayName(Card c, string? itemName)
    {
        if (!IsPlaceholderName(itemName)) return itemName!.Trim();
        var t = CollectionName(c.Id);
        if (!IsPlaceholderName(t)) return t!;
        return "Objet sans nom";
    }

    public static IReadOnlyList<Award> Awards()
    {
        lock (_lock)
        {
            if (_awards != null) return _awards;
            var res = new List<Award>();
            try
            {
                var path = PathProvider?.Invoke(
                    "data/db/c_collectionpoint.ini");
                if (path != null)
                    foreach (var row in IniTable.ParseFile(path,
                                 EdenEncoding.Big5).Rows)
                    {
                        if (row.Length < 6 ||
                            !int.TryParse(row[0], out var id)) continue;
                        var a = new Award
                        { Id = id, Enchant = IniTable.GetInt(row, 3) };
                        for (var i = 0; i < 5; i++)
                        {
                            var key = IniTable.GetInt(row, 4 + i * 2);
                            if (key != 0)
                                a.Conds.Add((key,
                                    IniTable.GetInt(row, 5 + i * 2)));
                        }
                        res.Add(a);
                    }
            }
            catch { /* table absente : pas d'aperçu de récompense */ }
            return _awards = res;
        }
    }

    /// <summary>Compteurs par clé (groupe, 1000 normal, 1001 special)
    /// recalculés depuis les cartes possédées — miroir serveur
    /// (UpdateCollectionPoint).</summary>
    public static Dictionary<int, int> GroupPoints(IEnumerable<int> owned)
    {
        var byId = Cards().ToDictionary(c => c.Id);
        var res = new Dictionary<int, int>();
        foreach (var id in owned)
        {
            if (!byId.TryGetValue(id, out var c)) continue;
            res[c.Group] = res.GetValueOrDefault(c.Group) + c.Points;
            var k = c.Special ? 1001 : 1000;
            res[k] = res.GetValueOrDefault(k) + c.Points;
        }
        return res;
    }
}
