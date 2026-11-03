namespace EdenClient.Ui;

/// <summary>Découpe pure d'un intervalle en tuiles de taille native
/// (bords 9-slice « Tiled » Falagard) — la dernière tuile est tronquée
/// (fraction &lt; 1) au lieu d'être étirée.</summary>
public static class TileMath
{
    /// <summary>Tuiles de <paramref name="tile"/> px couvrant
    /// <paramref name="length"/> px : (offset, longueur, fraction UV).
    /// Tuile ≤ 1 px ou longueur ≤ tuile → un seul segment étiré.</summary>
    public static List<(float Offset, float Len, float Frac)> Spans(
        float length, float tile)
    {
        var r = new List<(float, float, float)>();
        if (length <= 0)
            return r;
        if (tile <= 1.5f || length <= tile)
        {
            r.Add((0, length, 1f));
            return r;
        }
        // Plafond anti-explosion de sommets (rect énorme / tuile fine).
        var n = (int)MathF.Ceiling(length / tile);
        if (n > 64)
        {
            r.Add((0, length, 1f));
            return r;
        }
        for (var i = 0; i < n; i++)
        {
            var off = i * tile;
            var len = MathF.Min(tile, length - off);
            r.Add((off, len, len / tile));
        }
        return r;
    }
}
