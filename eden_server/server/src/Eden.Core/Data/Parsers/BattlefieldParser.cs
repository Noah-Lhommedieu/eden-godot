using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Battlefield.ini — donjons/champs de bataille (V.4,59, ',').
///
///   [0]  Battlefield ID
///   [1]  Nom (Big5: 霜靈谷戰場 = Frost Spirit Valley)
///   [2]  Type/mode (1)
///   [3]  Nb joueurs requis (3)
///   [4]  Map id (16756)
///   [5]  ?(202)
///   [6]  Niveau min (5)
///   [8]  ?(27)
///   [10-18] Paliers de recompenses (counts)
///   [19-28] Paliers 2
///   [30-38] Rewards par palier (29000, 44000... — XP/gold?)
///   [39-48] Rewards 2
///   [49-58] Rewards 3
/// </summary>
public static class BattlefieldParser
{
    public sealed class BattlefieldDef
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Mode { get; set; }
        public int RequiredPlayers { get; set; }
        public int MapId { get; set; }
        public int MinLevel { get; set; }
        public int[] RewardTiers1 { get; set; } = new int[9];
        public int[] RewardTiers2 { get; set; } = new int[9];
        public int[] RewardTiers3 { get; set; } = new int[9];
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    public static List<BattlefieldDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<BattlefieldDef>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            var b = new BattlefieldDef
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                Mode = IniTable.GetInt(row, 2),
                RequiredPlayers = IniTable.GetInt(row, 3),
                MapId = IniTable.GetInt(row, 4),
                MinLevel = IniTable.GetInt(row, 6),
                Raw = row,
            };
            for (var i = 0; i < 9; i++)
            {
                b.RewardTiers1[i] = IniTable.GetInt(row, 31 + i);
                b.RewardTiers2[i] = IniTable.GetInt(row, 40 + i);
                b.RewardTiers3[i] = IniTable.GetInt(row, 50 + i);
            }
            list.Add(b);
        }
        return list;
    }
}
