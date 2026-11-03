using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

public static class ShopAndMiscParsers
{
    // ---------- S_Store.ini (|V.1|93|) ----------
    // Triplets [itemId|nom|count] a partir de col 3.

    public sealed class StoreDef
    {
        public int Id { get; set; }
        public List<(int ItemId, string Name, int Count)> Items { get; } = new();
    }

    public static List<StoreDef> ParseStores(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<StoreDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 6 || !int.TryParse(row[0], out var id)) continue;
            var store = new StoreDef { Id = id };
            for (var i = 3; i + 2 < row.Length; i += 3)
            {
                var itemId = IniTable.GetInt(row, i);
                if (itemId <= 0) break;
                store.Items.Add((itemId, IniTable.GetStr(row, i + 1),
                    IniTable.GetInt(row, i + 2)));
            }
            list.Add(store);
        }
        return list;
    }

    // ---------- S_ItemChest.ini (|V.3|32|) ----------
    // Table cube Horadric + coffres : layout verrouillé sur
    // GameData::CHoradricCubeQuery::Deserialize @0x85dbcd0 (désasm
    // zone) — lecture séquentielle des colonnes :
    //   [0]  id recette (clé, envoyée par le client en 232 field1)
    //   [1]  main ingredient item id (+0x04)
    //   [2]  s16 grade/type (+0x10)
    //   [3]  u16 (+0x12, version>1)
    //   [4]  nom (+0x08)
    //   [5]  u16 qualité requise des aux (+0x14 : 0=libre, 1=accepte
    //        qualité 0 — CheckCubeCosts @0x80ba380)
    //   [6]  u16 quantité main (+0x0c)
    //   [7..18] 4 × {id,nom,quantité} sous-ingrédients (+0x18 vector)
    //   [19] i32 coût or (+0x24)
    //   [20] s16 taux de réussite % (+0x28 — dice<rate)
    //   [21] i32 produit : id item <100000, id groupe ≥100000
    //        (pool via col75 S_Item), 0 = transforme/aux (+0x2c)
    //   [22] s16 niveau requis du personnage (+0x38)
    //   [23] nom produit (+0x30)
    //   [24] s16 quantité produite (+0x34, cap natif 3/stacks)
    //   [25..29] f32 poids qualité sur 10000, clés {0,1,2,4,6}
    //        (map +0x3c — PickAColor roll cumulé)
    //   [30] s16 param RandItemCombo (+0x54)
    //   [31] set<u32> `;` items applicables (+0x5c)

    /// <summary>Sous-ingrédient de recette cube — triplet
    /// {id, nom, quantité} de S_ItemChest (SIngredient natif).</summary>
    public sealed class CubeIngredient
    {
        public int ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Amount { get; set; }
    }

    public sealed class ItemChestDef
    {
        public int Id { get; set; }
        /// <summary>Item ingrédient principal (col1, +0x04) — aussi clé
        /// de groupe pour les recettes "groupe−aux".</summary>
        public int ItemId { get; set; }      // 200033
        public int Grade { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Qualité requise des items auxiliaires (col5, +0x14) :
        /// 0 = pas de contrôle, 1 = accepte aussi la qualité 0, sinon
        /// la qualité exacte est exigée.</summary>
        public int AuxQualityReq { get; set; }
        /// <summary>Quantité requise de l'ingrédient principal (col6).</summary>
        public int MainAmount { get; set; }
        /// <summary>Jusqu'à 4 sous-ingrédients (cols 7-18).</summary>
        public List<CubeIngredient> Subs { get; } = new(4);
        /// <summary>Coût en or prélevé à l'exécution (col19, +0x24).</summary>
        public int Gold { get; set; }
        /// <summary>Taux de réussite en % (col20, +0x28) — le cube
        /// chanceux ajoute son bonus (enchant 8007 param1).</summary>
        public int SuccessRate { get; set; }
        /// <summary>Produit (col21, +0x2c) : &lt;100000 = id item direct,
        /// ≥100000 = id de groupe → pool = tous les items dont la col75
        /// contient cette clé, 0 = pool = ids des items auxiliaires.</summary>
        public int ProductKey { get; set; }
        /// <summary>Niveau de personnage requis (col22, +0x38) — 999 =
        /// recette désactivée (coffres non fabriquables).</summary>
        public int RequiredLevel { get; set; }
        public string ProductName { get; set; } = string.Empty;
        /// <summary>Quantité produite (col24, +0x34).</summary>
        public int ProductAmount { get; set; }
        /// <summary>Poids qualité cumulés sur 10000 par clé EQualityType
        /// {0,1,2,4,6} (cols 25-29) — PickAColor roll cumulé natif.</summary>
        public Dictionary<int, float> QualityWeights { get; } = new();
        /// <summary>Param transmis à RandItemCombo (col30, +0x54).</summary>
        public int ComboParam { get; set; }
        /// <summary>Ids applicables du set col31 (+0x5c) — restriction du
        /// pool de produits.</summary>
        public HashSet<int> ApplicableSet { get; } = new();
        public int Price { get; set; }       // col 17
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    public static List<ItemChestDef> ParseItemChests(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<ItemChestDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            var def = new ItemChestDef
            {
                Id = id,
                ItemId = IniTable.GetInt(row, 1),
                Grade = IniTable.GetInt(row, 2),
                Name = IniTable.GetStr(row, 4),
                AuxQualityReq = IniTable.GetInt(row, 5),
                MainAmount = IniTable.GetInt(row, 6),
                Gold = IniTable.GetInt(row, 19),
                SuccessRate = IniTable.GetInt(row, 20),
                ProductKey = IniTable.GetInt(row, 21),
                RequiredLevel = IniTable.GetInt(row, 22),
                ProductName = IniTable.GetStr(row, 23),
                ProductAmount = IniTable.GetInt(row, 24),
                ComboParam = IniTable.GetInt(row, 30),
                Price = IniTable.GetInt(row, 17),
                Raw = row,
            };
            for (var i = 0; i < 4; i++)
            {
                // 4 sous-ingrédients {id,nom,amt} aux cols 7/10/13/16
                var subId = IniTable.GetInt(row, 7 + i * 3);
                if (subId == 0) continue;
                def.Subs.Add(new CubeIngredient
                {
                    ItemId = subId,
                    Name = IniTable.GetStr(row, 8 + i * 3),
                    Amount = IniTable.GetInt(row, 9 + i * 3),
                });
            }
            // 5 poids qualité clés {0,1,2,4,6} (map native +0x3c)
            int[] qKeys = { 0, 1, 2, 4, 6 };
            for (var i = 0; i < qKeys.Length; i++)
            {
                var w = float.TryParse(IniTable.GetStr(row, 25 + i),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var f) ? f : 0f;
                if (w > 0f) def.QualityWeights[qKeys[i]] = w;
            }
            foreach (var tok in IniTable.GetStr(row, 31).Split(';'))
                if (int.TryParse(tok.Trim().Split(' ')[0], out var g) && g != 0)
                    def.ApplicableSet.Add(g);
            list.Add(def);
        }
        return list;
    }

    // ---------- S_ItemCombo.ini (|V.4|42|) ----------
    // affixes aleatoires: "G00003|力量的|17#18#19#24#25#26#34#|..."

    public sealed class ItemComboDef
    {
        public int Id { get; set; }
        public string GroupCode { get; set; } = string.Empty;  // G00003
        public string AffixName { get; set; } = string.Empty;  // 力量的
        public List<int> StatIds { get; } = new();             // 17#18#19...
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int Rank { get; set; }
        public int Weight { get; set; }
    }

    public static List<ItemComboDef> ParseItemCombos(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<ItemComboDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            var c = new ItemComboDef
            {
                Id = id,
                GroupCode = IniTable.GetStr(row, 1),
                AffixName = IniTable.GetStr(row, 2),
                MinLevel = IniTable.GetInt(row, 5),
                MaxLevel = IniTable.GetInt(row, 6),
                Rank = IniTable.GetInt(row, 7),
                Weight = IniTable.GetInt(row, 8),
            };
            foreach (var s in IniTable.GetStr(row, 3)
                .Split('#', System.StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(s, out var statId)) c.StatIds.Add(statId);
            list.Add(c);
        }
        return list;
    }

    // ---------- S_ClassRankBuff.ini (|V.3|134|) ----------
    // buffs de rang de classe par palier: triplets [seuil|?|spellId]?

    public sealed class ClassRankBuffDef
    {
        /// <summary>Champ 0 = simple index de ligne, ignoré par le natif
        /// (<c>Skip(1)</c>) — ce n'est PAS le classId (KB-02 : lignes
        /// 20+ décalées : ligne 20 → classe 21, ligne 42 → classe 59).
        /// Ne jamais s'en servir comme clé de lookup.</summary>
        public int Id { get; set; }
        /// <summary>Champ 1 = classId : la vraie clé (QueryClassRankBuff
        /// native, byte classId de player_classlist).</summary>
        public int ClassId { get; set; }
        /// <summary>Palier (index 1..20) → enchant permanent.
        /// La clé de la map native `map&lt;u8,SRankBuffData&gt;` est
        /// l'index d'itération uVar16, PAS un champ du fichier —
        /// désasm CClassRankBuffQuery::Deserialize @0x85c44xx.
        /// RefreshJPAward @0x8417c10 : rang = floor(min(classLv,
        /// S_Parameter 578=100) × 0.2), lookup EXACT de ce rang dans
        /// la map, puis EffectToLife(6ᵉ champ, -2, 0x1000).</summary>
        public List<(int Rank, int EnchantId)> Tiers { get; } = new();
    }

    /// <summary>S_ClassRankBuff.ini — 20 paliers par classe, groupes
    /// de 6 champs ; seul le 6ᵉ (id enchant) est consommé serveur,
    /// les 5 autres sont des métadonnées d'affichage client
    /// (C_ClassRankBuff.ini — exigence de palier).</summary>
    /// <summary>Ligne rankbuff d'une classe — par champ 1 (<c>ClassId</c>),
    /// jamais par l'index de ligne. Un id à préfixe racial (≥100) est
    /// ramené à son rang local.</summary>
    public static ClassRankBuffDef? FindClassRankBuff(
        IEnumerable<ClassRankBuffDef> defs, int classId)
    {
        var local = classId % 100;
        return defs.FirstOrDefault(d => d.ClassId == local);
    }

    public static List<ClassRankBuffDef> ParseClassRankBuffs(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<ClassRankBuffDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 8 || !int.TryParse(row[0], out var id)) continue;
            var b = new ClassRankBuffDef { Id = id, ClassId = IniTable.GetInt(row, 1) };
            var rank = 0;
            for (var i = 2; i + 5 < row.Length; i += 6)
            {
                rank++;
                var enchantId = IniTable.GetInt(row, i + 5);
                if (enchantId > 0)
                    b.Tiers.Add((rank, enchantId));
            }
            list.Add(b);
        }
        return list;
    }

    // ---------- S_DailyAward.ini (|V.1|6|) ----------

    public sealed class DailyAwardDef
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int Day { get; set; }
        public int Count { get; set; }
        public int Points { get; set; }
    }

    public static List<DailyAwardDef> ParseDailyAwards(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<DailyAwardDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new DailyAwardDef
            {
                Id = id,
                ItemId = IniTable.GetInt(row, 1),
                Day = IniTable.GetInt(row, 2),
                Count = IniTable.GetInt(row, 3),
                Points = IniTable.GetInt(row, 4),
            });
        }
        return list;
    }
}
