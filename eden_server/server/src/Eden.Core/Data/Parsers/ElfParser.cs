using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Elf.ini — 226 familiers (V.7,42, separateur ',').
///
/// Colonnes:
///   [0]  Elf ID
///   [1]  Code (J10101)
///   [2]  Type/race (1)
///   [3]  Nom partie 1 (Big5: 幻狐•)
///   [4]  Nom partie 2 (Big5: 碗糕)
///   [5]  Niveau (50)
///   [6-11] Stats de base (mood, energie, amitie...)
///   [18] Spell id associe (56001)
///   [22-24] Spell/skill ids (3001, 3002, 3301)
///   [26-28] Stat params + valeur (1261, 1270, 45)
///   [29] Skill id (3305)
///   [31-33] Stat params 2 (1271, 1280, 55)
///   [34] Cout/valeur (1000)
///   [35] ?(80)
///   [36-41] Scripts de mood/bonus "#id#id#weight#Effect Min Max##"
///           (meme format que les bonus scripts de S_ElfCombine)
///
/// Reversed: CElfContainer, CNE_CZ_ZoneServerElf*, CEC_Elf* (mood/work/energy)
/// </summary>
public static class ElfParser
{
    public sealed class ElfDef
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;        // J10101
        public int RaceType { get; set; }                       // col 2
        public string Name { get; set; } = string.Empty;        // cols 3+4
        public int Level { get; set; }
        public int[] BaseStats { get; set; } = new int[6];      // cols 6-11
        public int SpellId { get; set; }                        // col 18
        public List<int> SkillIds { get; set; } = new();        // cols 22-24, 29
        public int Cost { get; set; }                           // col 34
        public List<CraftRecipeParser.BonusScript> MoodScripts { get; set; } = new(); // cols 36-41
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    public static List<ElfDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var elfs = new List<ElfDef>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 20 || !int.TryParse(row[0], out var id)) continue;

            var e = new ElfDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                RaceType = IniTable.GetInt(row, 2),
                Name = (IniTable.GetStr(row, 3) + IniTable.GetStr(row, 4)).Trim(),
                Level = IniTable.GetInt(row, 5),
                SpellId = IniTable.GetInt(row, 18),
                Cost = IniTable.GetInt(row, 34),
                Raw = row,
            };

            for (var i = 0; i < 6; i++)
                e.BaseStats[i] = IniTable.GetInt(row, 6 + i);
            for (var c = 22; c <= 24 && c < row.Length; c++)
            {
                var v = IniTable.GetInt(row, c);
                if (v > 0) e.SkillIds.Add(v);
            }
            var sk = IniTable.GetInt(row, 29);
            if (sk > 0) e.SkillIds.Add(sk);

            for (var c = 36; c <= 41 && c < row.Length; c++)
            {
                var script = ParseBonus(row[c]);
                if (script != null) e.MoodScripts.Add(script);
            }

            elfs.Add(e);
        }

        return elfs;
    }

    private static CraftRecipeParser.BonusScript? ParseBonus(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('#', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        return new CraftRecipeParser.BonusScript
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
