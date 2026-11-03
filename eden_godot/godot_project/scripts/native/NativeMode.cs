namespace EdenClient.Native;

/// <summary>
/// Le natif est le seul chemin (phase G, 2026-11-02) : .nif/.kf/.kfm/.dds d'origine, plus aucun
/// GLB ni PNG dérivé. Les propriétés restent pour les points d'appel existants.
/// </summary>
public static class NativeMode
{
    public static bool Has(string kind) => true;
    public static bool Tex => true;
    public static bool Mesh => true;
    public static bool Skin => true;
    public static bool Anim => true;
    public static bool Fx => true;
}
