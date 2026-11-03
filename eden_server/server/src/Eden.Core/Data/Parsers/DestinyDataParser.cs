namespace Eden.Core.Data.Parsers;

/// <summary>
/// Tables du chemin d'éveil (destiny road) — `S_DestinyRoad.ini` et
/// `S_DestinyEvent.ini`, reversées depuis
/// `GameData::CDestinyRoadQuery::Deserialize` (zone @ 0x085d7880) et
/// `GameData::CDestinyEventQuery::Deserialize` (zone @ 0x085d6000).
/// </summary>
public static class DestinyDataParser
{
    /// <summary>Définition d'une route (`S_DestinyRoad.ini`,
    /// format `id|tier|backdrop|cell_1..cell_39|`).
    /// `CInitDestinyRoadMap` natif indexe les routes par tier :
    /// `DestinyRoadLevelMap[level/10] -> liste d'ids`.</summary>
    public sealed class DestinyRoadDef
    {
        /// <summary>Col 0 → CDestinyRoadData+0x00 : id de route.</summary>
        public int Id { get; set; }
        /// <summary>Col 1 → +0x04 : tier de niveau (level/10 du perso
        /// doit être ≥ — map `DestinyRoadLevelMap`).</summary>
        public int Tier { get; set; }
        /// <summary>Col 2 → +0x08 : nom du décor (BackDrop01..).</summary>
        public string Backdrop { get; set; } = "";
        /// <summary>Cols 3..41 → vector&lt;u16&gt; +0x0c : groupes
        /// d'événements des 39 cases (rempli à 39 par le natif).</summary>
        public int[] Cells { get; set; } = new int[39];
    }

    /// <summary>Événement de case (`S_DestinyEvent.ini`,
    /// `id|group|nom|desc|str|type|value|param2|`).</summary>
    public sealed class DestinyEventDef
    {
        /// <summary>Col 0 → CDestinyEventData+0x00 : id.</summary>
        public int Id { get; set; }
        /// <summary>Col 1 → +0x04 : groupe (clé de
        /// `DestinyEventGroupMap` — les cases d'une route référencent
        /// des groupes, pas des events).</summary>
        public int Group { get; set; }
        /// <summary>Col 2 → +0x08 : nom (fate01…).</summary>
        public string Name { get; set; } = "";
        /// <summary>Col 5 → +0x14 u8 : type d'effet —
        /// 0 rien, 1 or, 2 exp, 3 NP+CP, 4/6 monstre, 5 enchant,
        /// 7 dés en plus.</summary>
        public int Type { get; set; }
        /// <summary>Col 6 → +0x18 : valeur (montant, mob id ou
        /// enchant id selon le type).</summary>
        public long Value { get; set; }
        /// <summary>Col 7 → +0x1c : second paramètre (compte de mobs
        /// pour 4/6, durée en ticks pour 5).</summary>
        public long Param2 { get; set; }
    }

    /// <summary>Parse `S_DestinyRoad.ini` (12 routes, tiers 0-10).</summary>
    public static List<DestinyRoadDef> ParseRoads(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<DestinyRoadDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 3 || !int.TryParse(row[0], out var id))
                continue;
            var def = new DestinyRoadDef
            {
                Id = id,
                Tier = IniTable.GetInt(row, 1),
                Backdrop = IniTable.GetStr(row, 2),
            };
            // CDestinyRoadData : le vector de cases est borné à 39
            // (fill_insert jusqu'à 0x27) — les cellules suivent le
            // backdrop, groupe d'event par case.
            for (var i = 0; i < def.Cells.Length; i++)
                def.Cells[i] = IniTable.GetInt(row, 3 + i);
            list.Add(def);
        }
        return list;
    }

    /// <summary>Parse `S_DestinyEvent.ini` (221 events).</summary>
    public static List<DestinyEventDef> ParseEvents(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<DestinyEventDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 6 || !int.TryParse(row[0], out var id))
                continue;
            list.Add(new DestinyEventDef
            {
                Id = id,
                Group = IniTable.GetInt(row, 1),
                Name = IniTable.GetStr(row, 2),
                Type = IniTable.GetInt(row, 5),
                Value = IniTable.GetLong(row, 6),
                Param2 = IniTable.GetLong(row, 7),
            });
        }
        return list;
    }

    /// <summary>`DestinyRoadLevelMap` natif : tier → ids de routes
    /// (InitDestinyRoadMap @ 0x080daf00, mission/zone). La route jouée
    /// est piochée au hasard dans `map[level/10]` — lower_bound :
    /// plus grand tier ≤ level/10.</summary>
    public static SortedDictionary<int, List<int>> BuildRoadLevelMap(
        IEnumerable<DestinyRoadDef> roads)
    {
        var map = new SortedDictionary<int, List<int>>();
        foreach (var r in roads)
        {
            if (!map.TryGetValue(r.Tier, out var l))
                map[r.Tier] = l = new List<int>();
            l.Add(r.Id);
        }
        return map;
    }

    /// <summary>`DestinyEventGroupMap` natif : groupe → ids
    /// d'events (piochés uniformément à la création/reroll).</summary>
    public static Dictionary<int, List<int>> BuildEventGroupMap(
        IEnumerable<DestinyEventDef> events)
    {
        var map = new Dictionary<int, List<int>>();
        foreach (var e in events)
        {
            if (!map.TryGetValue(e.Group, out var l))
                map[e.Group] = l = new List<int>();
            l.Add(e.Id);
        }
        return map;
    }
}
