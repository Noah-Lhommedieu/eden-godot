namespace EdenClient.Data.Tables;

/// <summary>
/// Tables du chemin d'éveil (destiny road) côté client —
/// <c>data/db/C_DestinyRoad.ini</c> (<c>|V.2|42|</c>),
/// <c>data/db/C_DestinyEvent.ini</c> (<c>|V.2|8|</c>) et
/// <c>data/db/t_destinyevent.ini</c> (noms anglais des événements).
///
/// Colonnes (miroir du <c>DestinyDataParser</c> serveur — désasm
/// CDestinyRoadQuery::Deserialize zone @0x085d7880 et
/// CDestinyEventQuery::Deserialize @0x085d6000) :
///   road  : col0 id, col1 tier (level/10 requis), col2 backdrop,
///           cols3-41 = 39 groupes d'événements de cases ;
///   event : col0 id, col1 group, col2 code icône
///           (<c>fate01</c> → <c>UI/uiicon</c>, <c>S81016xx</c> →
///           itemicon), col3 nom Big5, col5 type d'effet
///           (0 rien, 1 or, 2 exp, 3 NP+CP, 4/6 monstre, 5 enchant,
///           7 dés en plus), col6 value, col7 param2 ;
///   texte : col1 = nom EN (+ description multi-lignes éventuelle).
/// </summary>
public static class DestinyTable
{
    /// <summary>Une route de C_DestinyRoad.ini.</summary>
    public sealed class RoadDef
    {
        public int Id { get; init; }
        /// <summary>Col1 — palier de niveau (tier).</summary>
        public int Tier { get; init; }
        /// <summary>Col2 — nom du décor (BackDrop01…).</summary>
        public string Backdrop { get; init; } = "";
        /// <summary>Cols3-41 — groupes d'événements des 39 cases.</summary>
        public int[] Cells { get; } = new int[39];
    }

    /// <summary>Un événement de C_DestinyEvent.ini.</summary>
    public sealed class EventDef
    {
        public int Id { get; init; }
        /// <summary>Col1 — groupe (clé de pioche des cases).</summary>
        public int Group { get; init; }
        /// <summary>Col2 — code (icône uiicon/itemicon + clé nom).</summary>
        public string Code { get; init; } = "";
        /// <summary>Col5 — type : 0 rien, 1 or, 2 exp, 3 NP+CP,
        /// 4/6 monstre, 5 enchant, 7 dés en plus.</summary>
        public int Type { get; init; }
        /// <summary>Col6 — valeur (montant, mob id ou enchant id).</summary>
        public long Value { get; init; }
        /// <summary>Col7 — second paramètre (nb de mobs / durée).</summary>
        public long Param2 { get; init; }
    }

    /// <summary>Charge C_DestinyRoad.ini → id → route.</summary>
    public static Dictionary<int, RoadDef> LoadRoads(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, RoadDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 3 || !int.TryParse(row[0], out var id))
                continue;
            var r = new RoadDef
            {
                Id = id,
                Tier = IniTable.GetInt(row, 1),
                Backdrop = IniTable.GetStr(row, 2),
            };
            for (var i = 0; i < r.Cells.Length; i++)
                r.Cells[i] = IniTable.GetInt(row, 3 + i);
            dict[id] = r;
        }
        return dict;
    }

    /// <summary>Charge C_DestinyEvent.ini → id → événement.</summary>
    public static Dictionary<int, EventDef> LoadEvents(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, EventDef>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 6 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new EventDef
            {
                Id = id,
                Group = IniTable.GetInt(row, 1),
                Code = IniTable.GetStr(row, 2),
                Type = IniTable.GetInt(row, 5),
                Value = IniTable.GetLong(row, 6),
                Param2 = IniTable.GetLong(row, 7),
            };
        }
        return dict;
    }

    /// <summary>Libellé court du type d'événement (tooltips).</summary>
    public static string TypeName(int type) => type switch
    {
        1 => "Gold",
        2 => "EXP",
        3 => "NP + CP",
        4 or 6 => "Monster",
        5 => "Enchant",
        7 => "Extra dice",
        _ => "",
    };

    // ------------------- accès paresseux global -------------------

    private static readonly object _lock = new();
    private static Dictionary<int, RoadDef>? _roads;
    private static Dictionary<int, EventDef>? _events;
    private static Dictionary<int, TextTable.TextRow>? _text;

    /// <summary>Définition de route (null si absente).</summary>
    public static RoadDef? Road(int id) =>
        Ensure(ref _roads, "data/db/C_DestinyRoad.ini", LoadRoads)
            .TryGetValue(id, out var r) ? r : null;

    /// <summary>Définition d'événement (null si absent).</summary>
    public static EventDef? Event(int id) =>
        Ensure(ref _events, "data/db/C_DestinyEvent.ini", LoadEvents)
            .TryGetValue(id, out var e) ? e : null;

    /// <summary>Nom anglais d'un événement (t_destinyevent col1 — la
    /// première ligne ; la suite décrit l'effet). Repli
    /// « event #id ».</summary>
    public static string EventName(int id)
    {
        if (Ensure(ref _text, "data/db/t_destinyevent.ini",
                TextTable.Load).TryGetValue(id, out var t))
        {
            var raw = TextResolver.Clean(t.Field(1));
            var firstLine = raw.Split('\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
            if (firstLine.Length > 0 && firstLine[0].Length > 0)
                return firstLine[0];
        }
        return $"event #{id}";
    }

    /// <summary>Texte complet d'un événement (nom + description —
    /// toutes les lignes du champ).</summary>
    public static string EventText(int id)
    {
        if (Ensure(ref _text, "data/db/t_destinyevent.ini",
                TextTable.Load).TryGetValue(id, out var t))
        {
            var raw = TextResolver.Clean(t.Field(1));
            if (raw.Length > 0) return raw;
        }
        return EventName(id);
    }

    private static Dictionary<int, T> Ensure<T>(
        ref Dictionary<int, T>? cache, string relPath,
        Func<string, Dictionary<int, T>> loader)
    {
        if (cache != null) return cache;
        lock (_lock)
        {
            if (cache != null) return cache;
            try
            {
                var p = TextResolver.ResolveFile?.Invoke(relPath);
                cache = p != null ? loader(p) : new();
            }
            catch (Exception e)
            {
                TextResolver.Log?.Invoke(
                    $"[destiny] {relPath} : {e.Message}");
                cache = new();
            }
            return cache;
        }
    }
}
