namespace EdenClient.World;

/// <summary>
/// Requêtes de hauteur de terrain sur un <see cref="FsmFile"/> —
/// bilinéaire sur les vertices de la tuile (kind=0) couvrant le point.
/// Utilisé par le contrôleur joueur, le clic-sol et le placement des
/// entités. Classe PURE (testable hors Godot).
/// </summary>
public static class TerrainSampler
{
    /// <summary>Hauteur monde (z Eden) au point (x, y) du plan sol,
    /// ou false si hors terrain couvert (pas de tuile kind=0).</summary>
    public static bool TryGetHeight(FsmFile fsm, float x, float y,
        out float z)
    {
        z = 0f;
        var tile = FindTile(fsm, x, y);
        if (tile == null) return false;
        z = tile.GetHeightLocal(x, y);
        return true;
    }

    /// <summary>Hauteur ou 0 si hors carte.</summary>
    public static float GetHeight(FsmFile fsm, float x, float y) =>
        TryGetHeight(fsm, x, y, out var z) ? z : 0f;

    /// <summary>Tuile de terrain couvrant le point monde (x, y).</summary>
    public static TerrainTile? FindTile(FsmFile fsm, float x, float y)
    {
        var tw = fsm.TileWorldSize;
        if (tw <= 0) return null;
        return fsm.GetTerrainTile(
            (int)MathF.Floor(x / tw), (int)MathF.Floor(y / tw));
    }

    /// <summary>
    /// Intersection d'un rayon (origine + dir, en coords Eden z-up) avec
    /// le heightfield : marche le long du rayon puis affine par
    /// dichotomie. Utilisé pour le clic-sol. Retourne false si le rayon
    /// ne croise pas le terrain dans <paramref name="maxDist"/>.
    /// </summary>
    /// <param name="origin">Origine du rayon (x,y,z Eden).</param>
    /// <param name="dir">Direction normalisée (x,y,z Eden).</param>
    /// <param name="hit">Point d'impact (x,y,z Eden).</param>
    /// <param name="maxDist">Portée max le long du rayon.</param>
    /// <param name="step">Pas de marche (u).</param>
    public static bool RaycastHeight(FsmFile fsm,
        System.Numerics.Vector3 origin, System.Numerics.Vector3 dir,
        out System.Numerics.Vector3 hit,
        float maxDist = 2000f, float step = 1.0f)
    {
        hit = default;
        // f(t) = origin.z + t*dir.z - hauteur(x(t), y(t)) — cherche un
        // changement de signe.
        var prevT = 0f;
        var prevF = SignedDelta(fsm, origin, dir, 0f);
        if (prevF <= 0f && TryGetHeight(fsm, origin.X, origin.Y, out _))
        {
            hit = origin;
            return true;
        }
        for (var t = step; t <= maxDist; t += step)
        {
            var f = SignedDelta(fsm, origin, dir, t);
            if (float.IsNaN(f)) continue; // hors carte — continuer
            if (f <= 0f)
            {
                // Dichotomie entre prevT et t.
                var lo = prevT;
                var hi = t;
                for (var i = 0; i < 24; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (SignedDelta(fsm, origin, dir, mid) > 0f)
                        lo = mid;
                    else
                        hi = mid;
                }
                var tHit = (lo + hi) * 0.5f;
                var p = origin + dir * tHit;
                // Recaler z sur la hauteur exacte du terrain.
                hit = TryGetHeight(fsm, p.X, p.Y, out var z)
                    ? new System.Numerics.Vector3(p.X, p.Y, z)
                    : p;
                return true;
            }
            prevT = t;
            prevF = f;
        }
        return false;
    }

    /// <summary>z(rayon à t) − hauteur terrain sous le point ; NaN si
    /// le point au sol est hors carte.</summary>
    private static float SignedDelta(FsmFile fsm,
        System.Numerics.Vector3 o, System.Numerics.Vector3 d, float t)
    {
        var x = o.X + d.X * t;
        var y = o.Y + d.Y * t;
        return TryGetHeight(fsm, x, y, out var z)
            ? o.Z + d.Z * t - z
            : float.NaN;
    }
}
