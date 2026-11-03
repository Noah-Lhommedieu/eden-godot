namespace EdenClient.Data.Tables;

/// <summary>Arbres de talents de classe — <c>C_Talent.ini</c> : une ligne
/// par branche (id = classe×10 + branche 1..3) ; 8 colonnes, chacune
/// « spellId maxPoints preSpell prePoints 1 niveauRequis » (vide = trou).
/// Le serveur indexe ses règles sur les mêmes lignes
/// (<c>ZoneEntryFlow.AdvancedAbility</c>).</summary>
public static class TalentTable
{
    public sealed class Slot
    {
        public int SpellId { get; init; }
        public int MaxPoints { get; init; }
        public int PreSpell { get; init; }
        public int PrePoints { get; init; }
        public int RequiredLevel { get; init; }
    }

    /// <summary>branche (0..2) → 8 colonnes (null = vide) pour une classe de base.</summary>
    public static Slot?[][]? Branches(Dictionary<int, Slot?[]> all, int classId)
    {
        var res = new Slot?[3][];
        var any = false;
        for (var b = 0; b < 3; b++)
        {
            if (all.TryGetValue(classId * 10 + b + 1, out var row))
            { res[b] = row; any = true; }
            else res[b] = new Slot?[8];
        }
        return any ? res : null;
    }

    public static Dictionary<int, Slot?[]> Load(string path)
    {
        var d = new Dictionary<int, Slot?[]>();
        var t = IniTable.ParseFile(path);
        foreach (var row in t.Rows)
        {
            if (row.Length < 3 || !int.TryParse(row[0], out var id)) continue;
            var cells = new Slot?[8];
            for (var c = 0; c < 8 && c + 2 < row.Length; c++)
            {
                var w = row[c + 2].Split(' ',
                    StringSplitOptions.RemoveEmptyEntries);
                if (w.Length < 2 || !int.TryParse(w[0], out var sp)) continue;
                int G(int i) => i < w.Length && int.TryParse(w[i], out var v) ? v : 0;
                cells[c] = new Slot
                {
                    SpellId = sp, MaxPoints = G(1), PreSpell = G(2),
                    PrePoints = G(3), RequiredLevel = G(5),
                };
            }
            d[id] = cells;
        }
        return d;
    }
}
