namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser des tables de collection de cartes —
/// <c>S_Collection.ini</c> (définitions par item_id de carte) et
/// <c>S_CollectionPoint.ini</c> (enchants accordés par seuil de
/// points). Mapping <c>CCollectionQuery::Deserialize</c> natif :
/// <c>[u32 id][str name][u8 flag][s16][s16][str text][i32 locate]
/// [s16 points][s16 group]</c> ; S_CollectionPoint :
/// <c>[id][s16][s16][enchant][5×(key,threshold)]</c>.
/// </summary>
public static class CollectionParser
{
    /// <summary>Définition d'une carte de collection
    /// (<c>GameData::CCollectionData</c>).</summary>
    public sealed class CollectionDef
    {
        public int Id;                       // = item_id de la carte
        /// <summary>flag (+0x08) — 1 → compteur total clé 1001,
        /// 0 → clé 1000 (UpdateCollectionPoint natif).</summary>
        public bool Special;
        public int Locate;                   // +0x14 : masque de région
        public int Points;                   // +0x18 : points par carte
        public int Group;                    // +0x1a : clé de groupe
    }

    /// <summary>Récompense de points — enchant appliqué quand tous
    /// les couples (clé → seuil) non nuls sont atteints dans la map
    /// de compteurs du perso (<c>RefreshCollectionPointAward</c>).</summary>
    public sealed class CollectionPointAward
    {
        public int Id;
        public int Enchant;                  // +0x0c : enchant accordé
        public readonly List<(short Key, int Threshold)> Conds = new();
    }

    public static Dictionary<int, CollectionDef> ParseCollection(
        string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, CollectionDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 9 || !int.TryParse(row[0], out var id))
                continue;
            map[id] = new CollectionDef
            {
                Id = id,
                Special = IniTable.GetInt(row, 2) == 1,
                Locate = IniTable.GetInt(row, 6),
                Points = IniTable.GetInt(row, 7),
                Group = IniTable.GetInt(row, 8),
            };
        }
        return map;
    }

    public static List<CollectionPointAward> ParseCollectionPoint(
        string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<CollectionPointAward>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 6 || !int.TryParse(row[0], out var id))
                continue;
            var award = new CollectionPointAward
            {
                Id = id,
                Enchant = IniTable.GetInt(row, 3),
            };
            // Colonnes 4..13 : jusqu'à 5 paires (clé, seuil) —
            // les paires à clé 0 sont ignorées par le refresh natif.
            for (var i = 0; i < 5; i++)
            {
                var key = IniTable.GetInt(row, 4 + i * 2);
                var threshold = IniTable.GetInt(row, 5 + i * 2);
                if (key != 0)
                    award.Conds.Add(((short)key, threshold));
            }
            list.Add(award);
        }
        return list;
    }
}
