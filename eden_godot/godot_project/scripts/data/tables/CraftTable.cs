namespace EdenClient.Data.Tables;

/// <summary>
/// Recettes des ordres de travail (métiers NPC 12-17) côté client —
/// fenêtre works ouverte par le 496 EnterWorks.
///
/// Layouts <b>identiques</b> aux tables serveur (vérifié par
/// comparaison cellule-à-cellule, mêmes offsets que
/// <c>WorkOrderParser</c> $EDEN) :
/// - <c>C_FamilyCollect.ini</c> (|V.2|47) : col0 id, col1 catégorie
///   (0/1/2 — HYPOTHESIS : workType-12 → mining/planting/summon),
///   col2 item catalyseur, col3 nom, col4 qté catalyseur, col8 exp,
///   col9 instantané, col10 durée (min), col13 or, puis produits par
///   groupes de 4 {id,nom,qté,taux%} depuis col14.
/// - <c>c_familycombine.ini</c> (|V.1|44) : col0 id, col1 catégorie
///   (3-7,20 — HYPOTHESIS : 3/4/5 ↔ types 15/16/17), col9 or,
///   col10 item produit, col11 nom produit, col12 qté, col13 succès%,
///   col14 malédiction%, puis matériaux {id,nom,qté} par groupes de 3
///   depuis col15.
/// </summary>
public static class CraftTable
{
    /// <summary>Recette de collecte (CCollectData client).</summary>
    public sealed class CollectRecipe
    {
        public int Id { get; set; }                // col0 — workId wire
        public int Category { get; set; }          // col1
        public int CatalystItemId { get; set; }    // col2
        public string Name { get; set; } = "";     // col3
        public int CatalystQty { get; set; }       // col4
        public int Exp { get; set; }               // col8
        public bool Instant { get; set; }          // col9 == 1
        public int DurationMin { get; set; }       // col10
        public int Gold { get; set; }              // col13
        /// <summary>Produits {itemId, nom, qté, taux %}.</summary>
        public List<(int ItemId, string Name, int Qty, float Rate)>
            Products { get; } = new();
    }

    /// <summary>Recette de combinaison (CCombineData client).</summary>
    public sealed class CombineRecipe
    {
        public int Id { get; set; }                // col0 — workId wire
        public int Category { get; set; }          // col1
        public bool Instant { get; set; }          // col5 == 1
        public int DurationMin { get; set; }       // col6
        public int Gold { get; set; }              // col9
        public int ProductItemId { get; set; }     // col10
        public string ProductName { get; set; } = ""; // col11
        public int ProductQty { get; set; }        // col12
        public int SuccessRate { get; set; }       // col13
        public int CurseRate { get; set; }         // col14
        /// <summary>Matériaux {itemId, nom, qté} — le parchemin de
        /// recette (catalyseur à glisser) est listé parmi eux.</summary>
        public List<(int ItemId, string Name, int Qty)> Materials { get; }
            = new();
    }

    /// <summary>Charge C_FamilyCollect.ini → id → recette.</summary>
    public static Dictionary<int, CollectRecipe> LoadCollect(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, CollectRecipe>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id))
                continue;
            var r = new CollectRecipe
            {
                Id = id,
                Category = IniTable.GetInt(row, 1),
                CatalystItemId = IniTable.GetInt(row, 2),
                Name = IniTable.GetStr(row, 3),
                CatalystQty = IniTable.GetInt(row, 4),
                Exp = IniTable.GetInt(row, 8),
                Instant = IniTable.GetInt(row, 9) == 1,
                DurationMin = IniTable.GetInt(row, 10),
                Gold = IniTable.GetInt(row, 13),
            };
            for (var c = 14; c + 3 < row.Length; c += 4)
            {
                var pid = IniTable.GetInt(row, c);
                if (pid <= 0) continue;
                r.Products.Add((pid, IniTable.GetStr(row, c + 1),
                    IniTable.GetInt(row, c + 2),
                    IniTable.GetFloat(row, c + 3)));
            }
            dict[id] = r;
        }
        return dict;
    }

    /// <summary>Charge c_familycombine.ini → id → recette.</summary>
    public static Dictionary<int, CombineRecipe> LoadCombine(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, CombineRecipe>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id))
                continue;
            var r = new CombineRecipe
            {
                Id = id,
                Category = IniTable.GetInt(row, 1),
                Instant = IniTable.GetInt(row, 5) == 1,
                DurationMin = IniTable.GetInt(row, 6),
                Gold = IniTable.GetInt(row, 9),
                ProductItemId = IniTable.GetInt(row, 10),
                ProductName = IniTable.GetStr(row, 11),
                ProductQty = IniTable.GetInt(row, 12),
                SuccessRate = IniTable.GetInt(row, 13),
                CurseRate = IniTable.GetInt(row, 14),
            };
            for (var c = 15; c + 2 < row.Length; c += 3)
            {
                var mid = IniTable.GetInt(row, c);
                if (mid <= 0) continue;
                r.Materials.Add((mid, IniTable.GetStr(row, c + 1),
                    IniTable.GetInt(row, c + 2)));
            }
            dict[id] = r;
        }
        return dict;
    }

    /// <summary>Types de métier (service NPC → NCID 496).</summary>
    public static class WorkTypes
    {
        public const int Mining = 12, Planting = 13, Summon = 14,
            Alchemy = 15, Casting = 16, Enchanting = 17;
        public static bool IsCollect(int t) => t is >= 12 and <= 14;
        public static bool IsCombine(int t) => t is >= 15 and <= 17;

        /// <summary>Kind de catalyseur requis par type (S_Item col8) —
        /// miroir <c>WorkTypes.RequiredCatalystKind</c> serveur :
        /// 12→0x33, 13→0x32, 14→0x34, 15→0x35, 16→0x36, 17→0x37.</summary>
        public static int CatalystKind(int type) => type switch
        {
            Mining => 0x33, Planting => 0x32, Summon => 0x34,
            Alchemy => 0x35, Casting => 0x36, Enchanting => 0x37,
            _ => 0,
        };
    }
}
