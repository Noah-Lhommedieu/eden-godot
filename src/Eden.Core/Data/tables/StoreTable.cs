namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/db/C_Store.ini</c> (<c>|V.1|93|</c>) — catalogues des
/// boutiques PNJ : la réponse 304 EnterShop n'ouvre que le storeId,
/// c'est cette table qui donne la liste des articles (même règle que
/// le serveur — <c>ShopAndMiscParsers.ParseStores</c>).
///
/// Ligne : `storeId|?|?|` puis triplets `[itemId|nom Big5|count]` à
/// partir de la colonne 3 (le « nom » embarque le prix en Big5 genre
/// « 5銀之魔法書 » — le prix réel vient de c_item col31).
/// </summary>
public static class StoreTable
{
    /// <summary>Un store (boutique).</summary>
    public sealed class StoreDef
    {
        /// <summary>Col 0 — id du store (= Field2 du 304).</summary>
        public int Id { get; init; }
        /// <summary>Triplets (itemId, nom Big5, count/flag).</summary>
        public List<(int ItemId, string Name, int Count)> Items { get; }
            = new();
    }

    private static Dictionary<int, StoreDef>? _stores;
    private static bool _tried;

    /// <summary>Toutes les boutiques.</summary>
    public static IEnumerable<StoreDef> All => Ensure().Values;

    /// <summary>Store par id, null si la table est absente/id inconnu.</summary>
    public static StoreDef? Get(int storeId) =>
        Ensure().TryGetValue(storeId, out var s) ? s : null;

    /// <summary>Chargement paresseux via
    /// <see cref="TextResolver.ResolveFile"/> (même chaîne de résolution
    /// que les autres tables data/db).</summary>
    private static Dictionary<int, StoreDef> Ensure()
    {
        if (_stores != null || _tried) return _stores ??= new();
        _tried = true;
        var path = (TextResolver.ResolveFile ?? ResolveDefault)(
            "data/db/C_Store.ini");
        _stores = new Dictionary<int, StoreDef>();
        if (path == null) return _stores;
        try
        {
            var table = IniTable.ParseFile(path);
            foreach (var row in table.Rows)
            {
                if (row.Length < 6 ||
                    !int.TryParse(row[0], out var id))
                    continue;
                var store = new StoreDef { Id = id };
                for (var i = 3; i + 2 < row.Length; i += 3)
                {
                    var itemId = IniTable.GetInt(row, i);
                    if (itemId <= 0) break;
                    store.Items.Add((itemId,
                        IniTable.GetStr(row, i + 1),
                        IniTable.GetInt(row, i + 2)));
                }
                _stores[id] = store;
            }
        }
        catch (Exception e)
        {
            TextResolver.Log?.Invoke($"[store] C_Store.ini : {e.Message}");
        }
        return _stores;
    }

    /// <summary>Repli de résolution raw_assets/ quand
    /// <see cref="TextResolver.ResolveFile"/> n'est pas branché
    /// (tests hors Godot) — partagé par les autres tables du fichier.</summary>
    internal static string? ResolveDefault(string relPath)
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env))
        {
            var p = Path.Combine(env, relPath);
            if (File.Exists(p)) return p;
        }
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "raw_assets", relPath);
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }
}

/// <summary>
/// <c>data/db/C_Transport.ini</c> (<c>|V.10|59|</c>) — routes de
/// transporteurs : `routeId|nom Big5|…13 paliers de coût par niveau…`.
/// L'id de route = l'aire destination (t_node/T_Transport). Noms EN
/// via <c>T_Transport.ini</c> (format Count : `id|nom|`).
/// </summary>
public static class TransportTable
{
    private static Dictionary<int, string>? _names;
    private static bool _tried;

    /// <summary>Nom EN d'une route (T_Transport.ini col1), null si
    /// absent — le caller retombe sur t_node/#id.</summary>
    public static string? RouteName(int routeId)
    {
        Ensure();
        return _names != null &&
               _names.TryGetValue(routeId, out var n) ? n : null;
    }

    /// <summary>Coût d'une route pour un niveau de perso (13 paliers
    /// de 10 niveaux — même indexation que ShopHandlers serveur :
    /// clamp((level-1)/10, 0, 12)).</summary>
    public static int RouteCost(int routeId, int level)
    {
        Ensure();
        return _costs != null &&
               _costs.TryGetValue(routeId, out var c) && c.Length > 0
            ? c[Math.Clamp((level - 1) / 10, 0, c.Length - 1)] : 0;
    }

    private static Dictionary<int, int[]>? _costs;

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        _names = new Dictionary<int, string>();
        _costs = new Dictionary<int, int[]>();
        try
        {
            var path = (TextResolver.ResolveFile ?? StoreTable.ResolveDefault)(
                "data/db/C_Transport.ini");
            if (path != null)
            {
                var table = IniTable.ParseFile(path);
                foreach (var row in table.Rows)
                {
                    if (row.Length < 3 ||
                        !int.TryParse(row[0], out var id))
                        continue;
                    // Cols 4.. — les paliers de coût (entiers > 0
                    // contigus, « c1 » = fin de plage).
                    var costs = new List<int>();
                    for (var i = 3; i < row.Length; i++)
                    {
                        if (int.TryParse(row[i], out var c) && c > 0)
                            costs.Add(c);
                    }
                    _costs[id] = costs.ToArray();
                }
            }
            var tpath = (TextResolver.ResolveFile ?? StoreTable.ResolveDefault)(
                "data/db/T_Transport.ini");
            if (tpath != null)
            {
                var table = IniTable.ParseFile(tpath);
                foreach (var row in table.Rows)
                {
                    if (row.Length < 2 ||
                        !int.TryParse(row[0], out var id))
                        continue;
                    var name = TextResolver.Clean(row[1]);
                    if (name.Length > 0) _names[id] = name;
                }
            }
        }
        catch (Exception e)
        {
            TextResolver.Log?.Invoke($"[transport] : {e.Message}");
        }
    }
}

/// <summary>Cache paresseux de c_item.ini (~71k lignes — chargé à la
/// première demande, ~2 s) pour prix/stats d'items hors inventaire
/// (boutiques, tooltips).</summary>
public static class ItemTableCache
{
    private static Dictionary<int, ItemTable.ItemDef>? _items;
    private static bool _tried;

    /// <summary>Définition d'item par id, null si absent.</summary>
    public static ItemTable.ItemDef? Get(int itemId) =>
        Ensure().TryGetValue(itemId, out var d) ? d : null;

    private static Dictionary<int, ItemTable.ItemDef> Ensure()
    {
        if (_items != null || _tried) return _items ??= new();
        _tried = true;
        _items = new Dictionary<int, ItemTable.ItemDef>();
        // c_item.ini puis c_itemmall.ini (même layout |V.11| — les ids
        // mall 40001+ n'existent pas dans c_item : icônes du mall).
        foreach (var rel in new[]
                 { "data/db/c_item.ini", "data/db/c_itemmall.ini" })
        {
            var path = (TextResolver.ResolveFile ?? StoreTable.ResolveDefault)(
                rel);
            if (path == null) continue;
            try
            {
                foreach (var (id, def) in ItemTable.Load(path))
                    _items.TryAdd(id, def);
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke($"[items] {rel} : {e.Message}");
            }
        }
        return _items;
    }
}
