using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

public static class DropAndSpeedParsers
{
    // ---------- S_DropItem.ini (|V.3|176|) ----------

    public sealed class DropEntry
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;  // Big5
        public int Quantity { get; set; }
        public float ChancePercent { get; set; }              // chance ponderee
    }

    public sealed class DropTable
    {
        public int Id { get; set; }                            // monstre/groupe
        public string Name { get; set; } = string.Empty;
        /// <summary>Pool 0 « normal » (cols 10-89) — <c>GenerateDrop(0)</c>.</summary>
        public List<DropEntry> Entries { get; } = new();
        /// <summary>Pool 1 « break » (cols 95-174) — <c>GenerateDrop(1)</c>,
        /// tiré en plus quand la patience du mob est sous 1 à la mort
        /// (<c>CMonster::GiveItem</c> @0x08496590 : trophées, armures,
        /// pièces — ex. 58471 Herak : « Cœur de Herak » 30 %).</summary>
        public List<DropEntry> BreakEntries { get; } = new();
        /// <summary>Nombre de tirages du pool 0 (col 6 → <c>+0x1c</c>
        /// short du bloc SMonsterDrop ; <c>GenerateDrop</c> boucle
        /// <c>count</c> fois, 0 = rien).</summary>
        public int RollCount { get; set; }
        /// <summary>Nombre de tirages du pool break (col 91).</summary>
        public int BreakRollCount { get; set; }
        /// <summary>Or droppé — header S_DropItem cols 4-6 (1-idx) :
        /// chance %, centre, demi-étendue. GiveGold natif @08497200 :
        /// or = centre − spread + rand(spread×2), émis si le jet
        /// passe la chance. Preuve ItemLog : 56001 (80|2|1) → 1-2 or.
        /// </summary>
        public int GoldChancePct { get; set; }
        public int GoldCenter { get; set; }
        public int GoldSpread { get; set; }
    }

    /// <summary>
    /// S_DropItem.ini — indexée par mobId (56001 → drops du mob 56001)
    /// ou par itemId pour les contenus d'objets ouvrables (20863…).
    /// Deux pools (normal cols 11-90, « break » cols 96-175) de 20
    /// entrées <c>itemId|nom|qté|chance%</c>. Pour les mobs les chances
    /// somment ≤ 100 (le reste = rien) ; pour les items ouvrables elles
    /// somment à 100 (drop exclusif garanti).
    /// </summary>
    public static List<DropTable> ParseDropItem(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var tables = new List<DropTable>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 14 || !int.TryParse(row[0], out var id)) continue;
            var t = new DropTable { Id = id, Name = IniTable.GetStr(row, 1) };

            // Header : cols 4-6 (1-idx) = drop d'or
            // (chance%|centre|spread) — GiveGold @08497200.
            t.GoldChancePct = IniTable.GetInt(row, 3);
            t.GoldCenter = IniTable.GetInt(row, 4);
            t.GoldSpread = IniTable.GetInt(row, 5);

            // SMonsterDrop (GenerateDrop @0x08091720) : 2 blocs de 0x54
            // octets → pool 0 (cols 6-90 : en-tête 6-10, 20 entrées
            // 11-90) et pool 1 « break » (cols 91-175 : en-tête 91-95,
            // 20 entrées 96-175). Chaque pool a son nombre de tirages
            // (col 6 / 91).
            t.RollCount = IniTable.GetInt(row, 6);
            t.BreakRollCount = IniTable.GetInt(row, 91);
            ParsePool(row, 11, 91, t.Entries);
            ParsePool(row, 96, 176, t.BreakEntries);
            tables.Add(t);
        }
        return tables;
    }

    /// <summary>Lit les entrées d'un pool de <c>S_DropItem</c> : groupes de
    /// 4 champs <b>itemId | nom | qté | chance%</b> à partir de
    /// <paramref name="from"/> (pas de 4), dans [from, to). Layout prouvé
    /// sur les données : somme des chances = 100 dans 3 138 pools, et le
    /// pool break de Herak (58471) = 4 armures 7,5 % + Cœur 30 % +
    /// 2 reliques 20 % = 100. Un groupe vide est sauté ; un groupe non
    /// reconnu décale la lecture d'une colonne (resynchro).
    /// ⚠ L'ancien parseur lisait « rate|item|nom|qté » à partir de la
    /// col 10 : chaque objet héritait de la chance/qté de son voisin.
    /// </summary>
    private static void ParsePool(string[] row, int from, int to,
        List<DropEntry> entries)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var i = from;
        to = Math.Min(to, row.Length);
        while (i + 3 < to)
        {
            var f0 = row[i]; var f1 = row[i + 1];
            var f2 = row[i + 2]; var f3 = row[i + 3];
            if (f0.Length == 0 && f1.Length == 0 && f2.Length == 0 &&
                f3.Length == 0)
            {
                i += 4;
                continue;
            }
            var itemOk = int.TryParse(f0, out var item) && item > 0;
            var qtyOk = float.TryParse(f2,
                System.Globalization.NumberStyles.Float, inv, out var qty);
            var rateOk = float.TryParse(f3,
                System.Globalization.NumberStyles.Float, inv, out var rate);
            if (itemOk && qtyOk && rateOk && !int.TryParse(f1, out _))
            {
                entries.Add(new DropEntry
                {
                    ItemId = item,
                    ItemName = f1,
                    Quantity = qty > 0 ? (int)qty : 1,
                    ChancePercent = rate,
                });
                i += 4;
            }
            else i++;
        }
    }

    // ---------- S_MonsterStep.ini / S_RideStep.ini (V.1,4,) ----------

    public sealed class SpeedDef
    {
        public string Code { get; set; } = string.Empty;   // M001 / R001
        public string Name { get; set; } = string.Empty;   // Big5
        public float WalkSpeed { get; set; }
        public float RunSpeed { get; set; }
    }

    /// <summary>Tables de vitesse: monstres (375) et montures.</summary>
    public static List<SpeedDef> ParseSpeedTable(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<SpeedDef>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var row in table.Rows)
        {
            if (row.Length < 4 || string.IsNullOrWhiteSpace(row[0])) continue;
            list.Add(new SpeedDef
            {
                Code = row[0],
                Name = row[1],
                WalkSpeed = float.TryParse(row[2],
                    System.Globalization.NumberStyles.Float, inv, out var w) ? w : 0,
                RunSpeed = float.TryParse(row[3],
                    System.Globalization.NumberStyles.Float, inv, out var r) ? r : 0,
            });
        }
        return list;
    }
}
