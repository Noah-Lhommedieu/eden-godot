using System.Collections.Generic;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser des tables d'ordres de travail (métiers 12-17) :
/// - <c>S_FamilyCollect.ini</c> → <see cref="WorkCollectDef"/>
///   (types 12 mining / 13 planting / 14 summon — CCollectData,
///   désasm <c>GameData::CCollectQuery::Deserialize</c> @0x85cxxxx,
///   lecture séquentielle des tokens).
/// - <c>S_FamilyCombine.ini</c> → <see cref="WorkCombineDef"/>
///   (types 15 alchemy / 16 casting / 17 enchanting — CCombineData,
///   désasm <c>GameData::CCombineQuery::Deserialize</c> @0x85d373b).
///
/// Le même S_FamilyCombine.ini alimente aussi
/// <c>CraftRecipeParser.ParseFamilyCombine</c> (recettes du cube/
/// strengthen — modèle différent, on ne le touche pas).
/// </summary>
public static class WorkOrderParser
{
    /// <summary>Recette de collecte (CCollectData). Ordre des colonnes
    /// = ordre des lectures du Deserialize natif :
    /// col0=workId(+0), col1=+4, col2=+8 id catalyseur, col3=nom(+0xc),
    /// col4=+0x10 qté catalyseur, col5=+0x14 niveau bâtiment requis,
    /// col6=+0x18, col7=+0x1c, col8=+0x20 exp joueur (GainExp au
    /// WorkResultOK), col9=+0x24 flag instant, col10=+0x28 durée,
    /// col11=+0x2c taux usure bâtiment, col12=+0x30 usure, col13=+0x34
    /// coût or, puis produits par groupes de 4 {id, nom, qté, taux}
    /// (SStuff +0x38, 16 octets : i32 id, i32?, i32 qté, float taux —
    /// cf. EndCollect @0x8166f60 qui lit piVar23[0]/[2]/+0xc).</summary>
    public sealed class WorkCollectDef
    {
        public int Id { get; set; }                    // col0 — clé
        public int CatalystItemId { get; set; }        // col2 (+0x08)
        public string Name { get; set; } = string.Empty; // col3
        public int CatalystAmount { get; set; }        // col4 (+0x10)
        public int BuildingLevel { get; set; }         // col5 (+0x14)
        public int FamilyExp { get; set; }             // col6 (+0x18)
        public int BuildingExp { get; set; }           // col7 (+0x1c)
        public int Exp { get; set; }                   // col8 (+0x20)
        public bool Instant { get; set; }              // col9==1 (+0x24)
        public int Duration { get; set; }              // col10 (+0x28)
        public int BuildingDamageChance { get; set; }  // col11 (+0x2c)
        public int BuildingDamage { get; set; }        // col12 (+0x30)
        public int Gold { get; set; }                  // col13 (+0x34)
        /// <summary>Produits possibles — EndCollect tire chaque
        /// entrée indépendamment (taux ajusté par decline/ext_succès)
        /// et empile jusqu'à 4 SWorkItem.</summary>
        public List<Product> Products { get; } = new();

        /// <summary>SStuff natif : {id i32, nom, qté i32, taux}.</summary>
        public sealed class Product
        {
            public int ItemId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public float Rate { get; set; }            // % de réussite
        }
    }

    /// <summary>Recette de combinaison (CCombineData). Ordre des
    /// colonnes = Deserialize natif @0x85d373b :
    /// col0=workId(+0), col1=+4 catégorie, col2=+8 byte niveau
    /// bâtiment, col3=+0xc, col4=+0x10, col5=+0x14 flag instant,
    /// col6=+0x18 durée, col7=+0x1c taux usure, col8=+0x20 usure,
    /// col9=+0x24 coût or, col10=+0x28 item produit, col11=+0x2c nom,
    /// col12=+0x30 s16 qté produit, col13=+0x32 s16 taux succès,
    /// col14=+0x3c taux malédiction, puis matériaux par groupes de
    /// 3 {id, nom, qté} (vecteur SStuff +0x40, pas de 4e champ).</summary>
    public sealed class WorkCombineDef
    {
        public int Id { get; set; }                    // col0 — clé
        public int Category { get; set; }              // col1 (+0x04)
        public int BuildingLevel { get; set; }         // col2 (+0x08 byte)
        public int FamilyExp { get; set; }             // col3 (+0x0c)
        public int BuildingExp { get; set; }           // col4 (+0x10)
        public bool Instant { get; set; }              // col5==1 (+0x14)
        public int Duration { get; set; }              // col6 (+0x18)
        public int BuildingDamageChance { get; set; }  // col7 (+0x1c)
        public int BuildingDamage { get; set; }        // col8 (+0x20)
        public int Gold { get; set; }                  // col9 (+0x24)
        public int ProductItemId { get; set; }         // col10 (+0x28)
        public string ProductName { get; set; } = string.Empty; // col11
        public int ProductQuantity { get; set; }       // col12 (+0x30)
        public int SuccessRate { get; set; }           // col13 (+0x32)
        public int CurseRate { get; set; }             // col14 (+0x3c)
        /// <summary>Matériaux (vecteur +0x40) — le parchemin de
        /// recette soumis comme catalyseur est lui-même listé
        /// (ex. 31736 dans la recette 21).</summary>
        public List<Material> Materials { get; } = new();

        /// <summary>Triplet matériau natif {id i32, nom, qté i32} —
        /// stride 0xc dans le vecteur +0x40 (CheckToCombine lit
        /// [0]=id, [2]=qté).</summary>
        public sealed class Material
        {
            public int ItemId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Quantity { get; set; }
        }
    }

    /// <summary>Parse S_FamilyCollect.ini → liste de WorkCollectDef.
    /// Produits : groupes de 4 colonnes à partir de col14,
    /// {id, nom, qté, taux} — id 0/vide → ignoré.</summary>
    public static List<WorkCollectDef> ParseCollect(string path)
    {
        var table = IniTable.ParseFile(path);
        var result = new List<WorkCollectDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id))
                continue;
            var def = new WorkCollectDef
            {
                Id = id,
                CatalystItemId = IniTable.GetInt(row, 2),
                Name = IniTable.GetStr(row, 3),
                CatalystAmount = IniTable.GetInt(row, 4),
                BuildingLevel = IniTable.GetInt(row, 5),
                FamilyExp = IniTable.GetInt(row, 6),
                BuildingExp = IniTable.GetInt(row, 7),
                Exp = IniTable.GetInt(row, 8),
                Instant = IniTable.GetInt(row, 9) == 1,
                Duration = IniTable.GetInt(row, 10),
                BuildingDamageChance = IniTable.GetInt(row, 11),
                BuildingDamage = IniTable.GetInt(row, 12),
                Gold = IniTable.GetInt(row, 13),
            };
            for (var c = 14; c + 3 < row.Length; c += 4)
            {
                var pid = IniTable.GetInt(row, c);
                if (pid <= 0) continue;
                def.Products.Add(new WorkCollectDef.Product
                {
                    ItemId = pid,
                    Name = IniTable.GetStr(row, c + 1),
                    Quantity = IniTable.GetInt(row, c + 2),
                    Rate = IniTable.GetFloat(row, c + 3),
                });
            }
            result.Add(def);
        }
        return result;
    }

    /// <summary>Parse S_FamilyCombine.ini → liste de WorkCombineDef.
    /// Matériaux : groupes de 3 colonnes à partir de col15,
    /// {id, nom, qté} — id 0/vide → ignoré.</summary>
    public static List<WorkCombineDef> ParseCombine(string path)
    {
        var table = IniTable.ParseFile(path);
        var result = new List<WorkCombineDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id))
                continue;
            var def = new WorkCombineDef
            {
                Id = id,
                Category = IniTable.GetInt(row, 1),
                BuildingLevel = IniTable.GetInt(row, 2),
                FamilyExp = IniTable.GetInt(row, 3),
                BuildingExp = IniTable.GetInt(row, 4),
                Instant = IniTable.GetInt(row, 5) == 1,
                Duration = IniTable.GetInt(row, 6),
                BuildingDamageChance = IniTable.GetInt(row, 7),
                BuildingDamage = IniTable.GetInt(row, 8),
                Gold = IniTable.GetInt(row, 9),
                ProductItemId = IniTable.GetInt(row, 10),
                ProductName = IniTable.GetStr(row, 11),
                ProductQuantity = IniTable.GetInt(row, 12),
                SuccessRate = IniTable.GetInt(row, 13),
                CurseRate = IniTable.GetInt(row, 14),
            };
            for (var c = 15; c + 2 < row.Length; c += 3)
            {
                var mid = IniTable.GetInt(row, c);
                if (mid <= 0) continue;
                def.Materials.Add(new WorkCombineDef.Material
                {
                    ItemId = mid,
                    Name = IniTable.GetStr(row, c + 1),
                    Quantity = IniTable.GetInt(row, c + 2),
                });
            }
            result.Add(def);
        }
        return result;
    }
}
