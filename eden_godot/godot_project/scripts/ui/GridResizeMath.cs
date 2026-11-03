namespace EdenClient.Ui;

/// <summary>Calculs purs du redimensionnement d'une grille de cases
/// fixes ancrée en haut-droite (barre buffs/nerfs, A4.3) : la poignée
/// du coin bas-gauche règle les colonnes (axe X, vers la gauche) ET
/// les rangées visibles (axe Y, vers le bas).</summary>
public static class GridResizeMath
{
    public static int Cols(float right, float mouseX, float cell, float sep,
        int max) => Math.Clamp(
            (int)Math.Round((right - mouseX + sep) / (cell + sep)), 1, max);

    /// <param name="top">Haut de la zone des cases (sous l'entête).</param>
    public static int Rows(float top, float mouseY, float cell, float sep,
        int max) => Math.Clamp(
            (int)Math.Round((mouseY - top + sep) / (cell + sep)), 1, max);
}

/// <summary>Panoramique vertical de la caméra d'aperçu item (clic droit
/// + glisser) : borné pour ne pas perdre le mannequin.</summary>
public static class PreviewPanMath
{
    public const float Limit = 1.6f, PerPixel = 0.006f;

    /// <summary>Glisser vers le haut (dy &lt; 0) monte la caméra.</summary>
    public static float Apply(float pan, float relativeY) =>
        Math.Clamp(pan - relativeY * PerPixel, -Limit, Limit);
}
