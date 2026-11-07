using Eden.Core.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour les tables de craft:
/// - S_ElfCombine.ini  (V.5,23 — craft par familiers/elfes, 957 recettes)
/// - S_FamilyCombine.ini (|V.1|44| — craft guilde/famille, 3275 recettes)
/// - S_ItemCombo.ini   (|V.4|42| — combos/sets d'items)
///
/// Reversed: CCubeCrafting, CCombineOp, CCompoundOp, CCubeOp
///
/// Format S_ElfCombine (23 cols, ','):
///   [0]  Recipe ID
///   [1]  ?type (vide ou 8)
///   [2]  Elf/race requirement (1, 3...)
///   [3]  ?param (8)
///   [4]  ?value (30)
///   [5]  ?val2 (5)
///   [6]  Gold cost (50, 250, 500) / ou count
///   [7]  Material item id (30266)
///   [9]  Result item id (35011)
///   [11] ?qty (8)
///   [12] Result item id (30725)
///   [14] Result qty (1)
///   [18] Flags/bitmask (256, 512, 95...)
///   [19-22] Scripts de bonus: "#itemId#itemId#prob#Effect Min/Max##"
///           ex: "#2522#2622#3411#Friend Plus 2 5#SoulAbility Plus 50 100##"
///
/// Format S_FamilyCombine (44 cols, '|'):
///   [0]  Recipe ID
///   [1]  Category (7)
///   [13] Flags/bitmask (512, 1024, 2048)
///   [18] Result item id (40002)
///   [19] Result name (Big5: １級防禦卷軸)
///   [20] Result qty (1)
/// </summary>
public static class CraftRecipeParser
{
    public sealed class ElfCombineRecipe
    {
        public int Id { get; set; }
        public int ElfType { get; set; }        // col 2 — race/elf requirement
        public int GoldCost { get; set; }       // col 6
        public int MaterialItemId { get; set; } // col 7
        public int ResultItemId { get; set; }   // col 12 (ou 9 pour type 8)
        public int ResultQuantity { get; set; } // col 14
        public int Flags { get; set; }          // col 18
        public List<BonusScript> BonusScripts { get; set; } = new(); // cols 19-22
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    public sealed class BonusScript
    {
        public int ItemId1 { get; set; }
        public int ItemId2 { get; set; }
        public int Weight { get; set; }         // probabilite/rarete
        public string Effect1 { get; set; } = string.Empty;
        public string Effect2 { get; set; } = string.Empty;
        public string Effect3 { get; set; } = string.Empty;
    }

    public sealed class FamilyCombineRecipe
    {
        public int Id { get; set; }
        public int Category { get; set; }       // col 1 (7 = scroll?)
        public int Flags { get; set; }          // col 13
        public int ResultItemId { get; set; }   // col 18
        public string ResultName { get; set; } = string.Empty; // col 19 (Big5)
        public int ResultQuantity { get; set; } // col 20
        /// <summary>Taux de base du renforcement (CCombineData+0x32,
        /// s16 — échelle native ~2048/1024/512/256 ramenée à un rand
        /// 0-100 dans DoStrengthenItem). Renseigné pour les lignes de
        /// stations de renfort (id 1-20) : 1-10 = armes, 11-20 =
        /// armures — même valeur que <see cref="Flags"/> (col13).</summary>
        public int StrengthenRate => Flags;
        /// <summary>Matériaux autorisés (SStuff natif — triplets
        /// [itemId][nom][qté] à partir de la col 18 pour les stations
        /// de renfort : 40002-40010 pierres d'arme, 40011-40019
        /// d'armure, 40192/40193+48739/48740 pierres interdites).
        /// DoStrengthenItem n'applique le taux que si l'item matériau
        /// est dans cette liste.</summary>
        public List<(int ItemId, int Count)> StrengthenMaterials { get; }
            = new();
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    public sealed class ItemCombo
    {
        public int Id { get; set; }
        public string ComboCode { get; set; } = string.Empty;  // col 1 (G00003)
        public string NamePrefix { get; set; } = string.Empty; // col 2 (力量的)
        public List<int> ApplicableItemTypes { get; set; } = new(); // col 3 "17#18#19#..."
        /// <summary>AttrType (col 4 → CItemCombo+0x14) — 4=arme,
        /// 5=armure, 6=accessoire ; autres valeurs = autres systèmes.
        /// GetRandMagicBoxItemCombo exige &gt;3 et == attrType de l'item.</summary>
        public int AttrType { get; set; }
        public int MinLevel { get; set; }       // col 5 (1, 3, 5, 7)
        public int MaxLevel { get; set; }       // col 6 (2, 4, 6, 8)
        /// <summary>Col 7 (CItemCombo+0x18, s16) : **groupe** d'exclusion
        /// pour AddComboStrengthen (un seul combo du groupe par item,
        /// désasm @0x080a2160) ; niveau d'étoile 1-5 pour les combos
        /// magicbox (AttrType 4-6).</summary>
        public int Tier { get; set; }
        /// <summary>Col 8 (CItemCombo+0x1a, s8) : **poids** du tirage
        /// pondéré (20/5/2…) dans AddComboStrengthen et
        /// GetRandMagicBoxItemCombo.</summary>
        public int Value { get; set; }
        /// <summary>Col 9 (CItemCombo+0x1b, u8) : flag natif — quand ≠0,
        /// AddComboStrengthen saute TOUTES les exclusions (id déjà posé,
        /// groupe, tier&gt;3). 0 sur toutes les lignes EDENFR.</summary>
        public int Flag { get; set; }
        public int Rank { get; set; }           // col 11 (2,3,4,5)
        /// <summary>col 10 — id S_Enchant porté par les combos de la
        /// boîte magique (AttrType 4-6, ids ≥ 3904 : « Durabilité I »,
        /// « Bénédiction de Vie I »…) ; 0 pour les affixes classiques
        /// (stats par colonne 11-40). Table combo→effet de
        /// magicbox_enchant1-3 (stage « magicbox »).</summary>
        public int EnchantId { get; set; }
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    /// <summary>Parse S_ElfCombine.ini</summary>
    public static List<ElfCombineRecipe> ParseElfCombine(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var recipes = new List<ElfCombineRecipe>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 15 || !int.TryParse(row[0], out var id)) continue;

            var r = new ElfCombineRecipe
            {
                Id = id,
                ElfType = IniTable.GetInt(row, 2),
                GoldCost = IniTable.GetInt(row, 6),
                MaterialItemId = IniTable.GetInt(row, 7),
                ResultItemId = IniTable.GetInt(row, 12) != 0
                    ? IniTable.GetInt(row, 12)
                    : IniTable.GetInt(row, 9),
                ResultQuantity = IniTable.GetInt(row, 14),
                Flags = IniTable.GetInt(row, 18),
                Raw = row,
            };

            for (var c = 19; c <= 22 && c < row.Length; c++)
            {
                var script = ParseBonusScript(row[c]);
                if (script != null) r.BonusScripts.Add(script);
            }

            recipes.Add(r);
        }

        return recipes;
    }

    /// <summary>Parse S_FamilyCombine.ini</summary>
    public static List<FamilyCombineRecipe> ParseFamilyCombine(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var recipes = new List<FamilyCombineRecipe>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 21 || !int.TryParse(row[0], out var id)) continue;

            var recipe = new FamilyCombineRecipe
            {
                Id = id,
                Category = IniTable.GetInt(row, 1),
                Flags = IniTable.GetInt(row, 13),
                ResultItemId = IniTable.GetInt(row, 18),
                ResultName = row.Length > 19 ? row[19] : string.Empty,
                ResultQuantity = IniTable.GetInt(row, 20),
                Raw = row,
            };

            // Stations de renfort (ids 1-20, désasm DoStrengthenItem
            // @0x80a2b00) : les matériaux sont des triplets
            // [itemId][nom][qté] à partir de la col 18 — vector
            // CCombineData::SStuff (12 octets).
            if (id is >= 1 and <= 20)
                for (var c = 18; c + 2 < row.Length; c += 3)
                {
                    var matId = IniTable.GetInt(row, c);
                    if (matId <= 0) continue;
                    recipe.StrengthenMaterials.Add(
                        (matId, IniTable.GetInt(row, c + 2)));
                }

            recipes.Add(recipe);
        }

        return recipes;
    }

    /// <summary>Parse S_ItemCombo.ini</summary>
    public static List<ItemCombo> ParseItemCombo(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var combos = new List<ItemCombo>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 12 || !int.TryParse(row[0], out var id)) continue;

            var types = new List<int>();
            if (row.Length > 3 && !string.IsNullOrWhiteSpace(row[3]))
            {
                foreach (var t in row[3].Split('#', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(t, out var tv)) types.Add(tv);
            }

            combos.Add(new ItemCombo
            {
                Id = id,
                ComboCode = row.Length > 1 ? row[1] : string.Empty,
                NamePrefix = row.Length > 2 ? row[2] : string.Empty,
                ApplicableItemTypes = types,
                AttrType = IniTable.GetInt(row, 4),
                MinLevel = IniTable.GetInt(row, 5),
                MaxLevel = IniTable.GetInt(row, 6),
                Tier = IniTable.GetInt(row, 7),
                Value = IniTable.GetInt(row, 8),
                Flag = IniTable.GetInt(row, 9),
                Rank = IniTable.GetInt(row, 11),
                EnchantId = IniTable.GetInt(row, 10),
                Raw = row,
            });
        }

        return combos;
    }

    /// <summary>
    /// Parse un script bonus: "#2522#2622#3411#Friend Plus 2 5#SoulAbility Plus 50 100##"
    /// → ItemId1=2522, ItemId2=2622, Weight=3411, effets.
    /// </summary>
    private static BonusScript? ParseBonusScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('#', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        return new BonusScript
        {
            ItemId1 = int.TryParse(parts[0], out var i1) ? i1 : 0,
            ItemId2 = parts.Length > 1 && int.TryParse(parts[1], out var i2) ? i2 : 0,
            Weight = parts.Length > 2 && int.TryParse(parts[2], out var w) ? w : 0,
            Effect1 = parts.Length > 3 ? parts[3] : string.Empty,
            Effect2 = parts.Length > 4 ? parts[4] : string.Empty,
            Effect3 = parts.Length > 5 ? parts[5] : string.Empty,
        };
    }
}
