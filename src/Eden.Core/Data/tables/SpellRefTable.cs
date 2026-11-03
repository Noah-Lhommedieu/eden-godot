namespace EdenClient.Data.Tables;

/// <summary>
/// C_SpellReference.ini — coefficients par palier de niveau de sort
/// (clé = niveau). Deux usages client :
///   - coût MP affiché d'un sort = col1 (MpCoef) × base C_Spell col32
///     / 100 (formule native <c>CCharFormula::CostMp</c> @0x83f44d0) ;
///   - placeholders <c>#B&lt;n&gt;$&lt;v&gt;#</c> des descriptions
///     T_Enchant : valeur affichée = v × coef(niveau, col n) / 100 —
///     vérifié sur Encourage (T_Enchant 52018 « STR +#B21$200# » →
///     « STR +244 » au palier 90, coef col21 = 122).
/// </summary>
public static class SpellRefTable
{
    private static Dictionary<int, int[]>? _rows;

    /// <summary>Coefficient du palier <paramref name="level"/> à la
    /// colonne <paramref name="col"/> (0 = introuvable).</summary>
    public static int Coef(int level, int col)
    {
        var t = Ensure();
        return t.TryGetValue(level, out var r) && col < r.Length
            ? r[col] : 0;
    }

    /// <summary>Plus haut palier connu (pour borner « next level »).</summary>
    public static int MaxLevel { get; private set; }

    private static Dictionary<int, int[]> Ensure()
    {
        if (_rows != null) return _rows;
        var rows = new Dictionary<int, int[]>();
        try
        {
            var p = TextResolver.ResolveFile?.Invoke(
                "data/db/c_spellreference.ini")
                ?? TextResolver.ResolveFile?.Invoke(
                    "data/db/C_SpellReference.ini");
            if (p != null)
            {
                var max = 0;
                foreach (var row in IniTable.ParseFile(p).Rows)
                {
                    if (row.Length < 2 ||
                        !int.TryParse(row[0], out var lv)) continue;
                    var vals = new int[row.Length];
                    for (var i = 0; i < row.Length; i++)
                        vals[i] = IniTable.GetInt(row, i);
                    rows[lv] = vals;
                    if (lv > max) max = lv;
                }
                MaxLevel = max;
            }
        }
        catch (Exception e)
        {
            TextResolver.Log?.Invoke(
                $"[spellref] c_spellreference.ini : {e.Message}");
        }
        _rows = rows;
        return rows;
    }

    /// <summary>Remplace les <c>#B&lt;n&gt;$&lt;v&gt;#</c> d'une
    /// description par la valeur mise à l'échelle du palier
    /// <paramref name="level"/> (<c>v × coef(level, n) / 100</c>,
    /// plancher natif si le palier manque → valeur de base).</summary>
    public static string ResolveScaledValues(string text, int level)
    {
        var i = 0;
        while (i < text.Length)
        {
            var h = text.IndexOf("#B", i, StringComparison.Ordinal);
            if (h < 0) break;
            var j = h + 2;
            while (j < text.Length && char.IsDigit(text[j])) j++;
            if (j >= text.Length || text[j] != '$' || j == h + 2)
            { i = h + 2; continue; }
            var k = j + 1;
            while (k < text.Length && char.IsDigit(text[k])) k++;
            if (k >= text.Length || text[k] != '#')
            { i = h + 2; continue; }
            if (!int.TryParse(text.AsSpan(h + 2, j - h - 2), out var col) ||
                !int.TryParse(text.AsSpan(j + 1, k - j - 1), out var v))
            { i = h + 2; continue; }
            var c = Coef(level, col);
            var shown = c > 0 ? v * c / 100 : v;
            text = text[..h] + shown + text[(k + 1)..];
            i = h + 1;
        }
        return text;
    }
}
