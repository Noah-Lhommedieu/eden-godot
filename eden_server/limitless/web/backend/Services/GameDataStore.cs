using System.Text.Json;

namespace Limitless.Api.Services;

/// <summary>Magasin des datasets wiki exportés par
/// `tools/export_web_data.py` (JSON versionnés — numérique serveur
/// S_*.ini + libellés EN T_*.ini, cf. docs/generated/WEB-DATA-AUDIT.md).
/// Chargement paresseux par dataset, index id→row, requêtes paginées
/// avec recherche (nom/id) et filtre `champ=valeur` sur les scalaires
/// de premier niveau.</summary>
public sealed class GameDataStore
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _json = new()
        { PropertyNameCaseInsensitive = true };
    private readonly Dictionary<string, Dataset> _sets = new();
    private readonly object _gate = new();

    public sealed class Dataset
    {
        public required string Name { get; init; }
        public required List<JsonElement> Rows { get; init; }
        public required Dictionary<int, int> ById { get; init; }
        public int Untranslated { get; init; }
    }

    public record DatasetMeta(string Id, int Count, int Untranslated);

    public record PagedResult<T>(int Total, int Page, int Size,
        IReadOnlyList<T> Items);

    public GameDataStore(IConfiguration cfg, IWebHostEnvironment env)
    {
        _dir = cfg["Wiki:DataDir"]
            ?? Path.Combine(env.ContentRootPath, "App_Data", "wiki");
    }

    /// <summary>Datasets disponibles (fichiers présents, hors index).</summary>
    public IReadOnlyList<DatasetMeta> Datasets()
    {
        var list = new List<DatasetMeta>();
        if (!Directory.Exists(_dir)) return list;
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            // talents_reforged = dataset technique (variante C_Talent
            // servie par /api/talents?variant=) — pas dans le rail.
            if (name == "index" || name == "talents_reforged") continue;
            var ds = Get(name);
            if (ds != null)
                list.Add(new DatasetMeta(name, ds.Rows.Count,
                    ds.Untranslated));
        }
        return list.OrderBy(d => d.Id).ToList();
    }

    /// <summary>Charge (et met en cache) un dataset ; null si absent.</summary>
    public Dataset? Get(string name)
    {
        if (!IsSafeName(name)) return null;
        lock (_gate)
        {
            if (_sets.TryGetValue(name, out var hit)) return hit;
            var path = Path.Combine(_dir, name + ".json");
            if (!File.Exists(path)) return null;
            var rows = JsonSerializer
                .Deserialize<List<JsonElement>>(
                    File.ReadAllText(path), _json)
                ?? new List<JsonElement>();
            var byId = new Dictionary<int, int>(rows.Count);
            var untranslated = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.Number &&
                    idEl.TryGetInt32(out var id))
                    byId.TryAdd(id, i);
                else if (rows[i].TryGetProperty("classId", out var cEl) &&
                         cEl.TryGetInt32(out var cid))
                    byId.TryAdd(cid, i);
                if (rows[i].TryGetProperty("untranslated", out var u) &&
                    u.ValueKind == JsonValueKind.True)
                    untranslated++;
            }
            var ds = new Dataset
            {
                Name = name, Rows = rows, ById = byId,
                Untranslated = untranslated,
            };
            _sets[name] = ds;
            return ds;
        }
    }

    /// <summary>Détail d'une entrée par id (ou classId pour talents).</summary>
    public JsonElement? Detail(string name, int id)
    {
        var ds = Get(name);
        if (ds == null || !ds.ById.TryGetValue(id, out var i))
            return null;
        return ds.Rows[i];
    }

    /// <summary>Requête paginée : `q` = nom (contient, insensible
    /// casse) ou id exact ; chaque `f` = filtre "champ:valeur"
    /// (répétable — ET logique). Formes supportées :
    ///   champ:val     égalité (scalaire racine ou « certificat I/II »)
    ///   champ&lt;v / champ&gt;v / champ&lt;=v / champ&gt;=v   comparaison numérique
    ///   champ.sous:val   filtre dans un tableau d'objets
    ///                    (ex. maps.name:S001, drops.id:32631,
    ///                    bpDrops.classGroup:14, materials.id:44025)
    ///   champ.*:val    la valeur apparaît dans un tableau scalaire
    ///                    (ex. usedInCrafts:12, requiredClasses.name)
    /// </summary>
    public PagedResult<JsonElement> Query(string name, string? q,
        IEnumerable<string>? filters, int page, int size)
    {
        var ds = Get(name);
        if (ds == null)
            return new PagedResult<JsonElement>(0, page, size,
                Array.Empty<JsonElement>());
        IEnumerable<JsonElement> src = ds.Rows;
        if (filters != null)
            foreach (var f in filters)
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                var pred = BuildPredicate(f);
                if (pred != null) src = src.Where(pred);
            }
        if (!string.IsNullOrWhiteSpace(q))
        {
            src = src.Where(e => Matches(e, q));
        }
        var total = src.Count();
        size = Math.Clamp(size, 1, 120);
        page = Math.Max(page, 1);
        var items = src.Skip((page - 1) * size).Take(size).ToList();
        return new PagedResult<JsonElement>(total, page, size, items);
    }

    /// <summary>Compile un filtre « champ[:op]:valeur » en prédicat.
    /// Les opérateurs &lt;/&gt;/&lt;=/&gt;= se notent suffixés au champ
    /// (« level&gt;50 »). « champ.sous:v » teste les éléments d'un
    /// tableau de objets ; « champ.*:v » un tableau de scalaires.</summary>
    private static Func<JsonElement, bool>? BuildPredicate(string f)
    {
        string field, val;
        char? op = null;
        // 1) forme « champ>v » / « champ<=v » — l'opérateur est dans
        //    le filtre, pas de ':' requis.
        var opi = f.IndexOfAny(new[] { '>', '<' });
        if (opi > 0)
        {
            field = f[..opi].Trim();
            var rest = f[opi..];
            if (rest.StartsWith(">=", StringComparison.Ordinal))
            { op = 'G'; val = rest[2..].Trim(); }
            else if (rest.StartsWith("<=", StringComparison.Ordinal))
            { op = 'L'; val = rest[2..].Trim(); }
            else
            { op = rest[0]; val = rest[1..].Trim(); }
        }
        else
        {
            // 2) forme « champ:valeur » — « champ=valeur » accepté
            //    aussi (alias : un '=' tapé pour ':' ne doit pas
            //    ignorer le filtre en silence, retour utilisateur
            //    lot12 — « color=violet » affichait tout).
            var sep = f.LastIndexOf(':');
            var eq = f.LastIndexOf('=');
            if (eq > sep) sep = eq;
            if (sep <= 0) return null;
            field = f[..sep].Trim();
            val = f[(sep + 1)..].Trim();
        }
        if (field.Length == 0 || val.Length == 0) return null;
        var dot = field.IndexOf('.');
        if (dot > 0)
        {
            var arr = field[..dot];
            var sub = field[(dot + 1)..];
            if (sub == "*")
                return e => ScalarArrayContains(e, arr, val);
            return e => ArrayFieldMatches(e, arr, sub, op, val);
        }
        if (op != null)
            return e => NumberCompare(e, field, op.Value, val);
        return e => FieldMatches(e, field, val);
    }

    /// <summary>Comparaison numérique sur un scalaire de premier
    /// niveau (« level&gt;50 »). Les valeurs non numériques sont
    /// ignorées.</summary>
    private static bool NumberCompare(JsonElement e, string field,
        char op, string val)
    {
        if (!double.TryParse(val,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v))
            return false;
        if (!e.TryGetProperty(field, out var el) ||
            el.ValueKind != JsonValueKind.Number ||
            !el.TryGetDouble(out var n))
            return false;
        return op switch
        {
            '>' => n > v, '<' => n < v,
            'G' => n >= v, 'L' => n <= v,
            _ => false,
        };
    }

    /// <summary>Filtre dans un tableau d'objets : vrai si AU MOINS
    /// un élément a `sub` qui matche val (égalité insensible casse,
    /// ou comparaison numérique si un opérateur est donné).</summary>
    private static bool ArrayFieldMatches(JsonElement e, string arr,
        string sub, char? op, string val)
    {
        if (!e.TryGetProperty(arr, out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty(sub, out var el))
                continue;
            if (op != null)
            {
                if (el.ValueKind == JsonValueKind.Number &&
                    el.TryGetDouble(out var n) &&
                    double.TryParse(val,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var v))
                {
                    var hit = op switch
                    {
                        '>' => n > v, '<' => n < v,
                        'G' => n >= v, 'L' => n <= v,
                        _ => false,
                    };
                    if (hit) return true;
                }
                continue;
            }
            if (ScalarEquals(el, val)) return true;
        }
        return false;
    }

    /// <summary>Vrai si le tableau scalaire `arr` contient `val`
    /// (nombre exact ou sous-chaîne insensible casse).</summary>
    private static bool ScalarArrayContains(JsonElement e, string arr,
        string val)
    {
        if (!e.TryGetProperty(arr, out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var el in list.EnumerateArray())
            if (ScalarEquals(el, val)) return true;
        return false;
    }

    private static bool ScalarEquals(JsonElement el, string val) =>
        el.ValueKind switch
        {
            JsonValueKind.String => string.Equals(el.GetString(), val,
                StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => el.TryGetDouble(out var n) &&
                double.TryParse(val,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v) && n == v,
            JsonValueKind.True or JsonValueKind.False =>
                bool.TryParse(val, out var b) &&
                el.GetBoolean() == b,
            _ => false,
        };

    private static bool Matches(JsonElement e, string q)
    {
        if (e.TryGetProperty("id", out var idEl) &&
            idEl.ValueKind == JsonValueKind.Number &&
            idEl.TryGetInt32(out var id) &&
            id.ToString() == q)
            return true;
        if (e.TryGetProperty("id", out var idStr) &&
            idStr.ValueKind == JsonValueKind.String &&
            string.Equals(idStr.GetString(), q,
                StringComparison.OrdinalIgnoreCase))
            return true;
        if (e.TryGetProperty("classId", out var cEl) &&
            cEl.TryGetInt32(out var cid) &&
            cid.ToString() == q)
            return true;
        foreach (var prop in e.EnumerateObject())
        {
            if (prop.Name is not ("name" or "className" or "map"
                or "title"))
                continue;
            if (prop.Value.ValueKind == JsonValueKind.String &&
                prop.Value.GetString()!.Contains(q,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool FieldMatches(JsonElement e, string field,
        string val)
    {
        if (!e.TryGetProperty(field, out var el)) return false;
        return ScalarEquals(el, val);
    }

    private static bool IsSafeName(string name) =>
        name.Length is > 0 and < 40 &&
        name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
}
