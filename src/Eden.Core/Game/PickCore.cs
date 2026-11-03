namespace EdenClient.GameInput;

/// <summary>
/// Math de picking écran — <b>pure, testable hors Godot</b>.
/// Le picking entité se fait en espace écran : chaque entité est
/// projetée via la caméra (<c>UnprojectPosition</c>) et on prend la
/// plus proche du curseur dans un rayon en pixels — robuste sans
/// physique ni colliders.
/// </summary>
public static class PickCore
{
    /// <summary>Index du point le plus proche de
    /// <paramref name="cursor"/> dans un rayon <paramref name="radiusPx"/>,
    /// −1 si aucun. Distances euclidiennes en pixels.</summary>
    public static int NearestInRadius(
        IReadOnlyList<(float X, float Y)> points,
        float cursorX, float cursorY, float radiusPx)
    {
        var best = -1;
        var bestD = radiusPx * radiusPx;
        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - cursorX;
            var dy = points[i].Y - cursorY;
            var d = dx * dx + dy * dy;
            if (d <= bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Vrai si le déplacement souris reste sous le seuil
    /// « clic » (au-delà c'est un drag d'orbite). Seuil EE ~5-6 px.</summary>
    public static bool IsClick(float dragDistancePx, float threshold = 6f)
        => dragDistancePx < threshold;
}
