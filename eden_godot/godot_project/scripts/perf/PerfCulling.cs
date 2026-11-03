using Godot;

namespace EdenClient.Perf;

/// <summary>
/// Culling à distance des petits props de map — appliqué UNE FOIS au
/// proto GLB dans <see cref="AssetCatalog.LoadGlbNode"/> (les
/// <see cref="Node.Duplicate"/> des instances héritent des valeurs).
///
/// Politique : chaque <see cref="MeshInstance3D"/> reçoit un
/// <see cref="GeometryInstance3D.VisibilityRangeEnd"/> fonction du
/// rayon de sa bounding box locale (échelle du sous-arbre incluse) :
///
///   rayon &lt; 0,75 u →  60 u   (cailloux, pots, petits débris)
///   rayon &lt; 2 u    → 120 u   (mobilier, barriques, torches)
///   rayon &lt; 6 u    → 220 u   (kiosques, cartes, buissons)
///   rayon &lt; 15 u   → 400 u   (grosses structures légères)
///   rayon ≥ 15 u   →   0 (jamais cullé — bâtiments, terrain, murs)
///
/// Au-delà de ces distances les props sont de toute façon perdus dans
/// le fog ini (FogSightRange ~150-500 u selon la map) ou font moins
/// d'un pixel. La sortie est adoucie par
/// <see cref="GeometryInstance3D.VisibilityRangeFadeModeEnum.Self"/> (dither
/// sur ~8 % de la plage) — pas de « pop » franc.
///
/// Périmètre volontairement restreint : uniquement les protos sous
/// <c>models/map/model/</c> hors <c>sky/</c> (le dôme suit la caméra,
/// il doit toujours être rendu). Les GLB de monstres/persos
/// (<c>monster/</c>, <c>char/</c>, <c>ride/</c>) ne sont PAS cullés :
/// leur visibilité est déjà bornée par le sight-range réseau (~300 u)
/// et la cible/labels doivent rester cohérents à distance.
///
/// Mesuré sur S001 (lot G1, docs/PERF.md) : draw calls 3813 → ~2 000
/// en vue spawn (les 3 769 objets sont presque tous des petits props).
/// </summary>
public static class PerfCulling
{
    /// <summary>Seuils (rayon local maxi → portée de visibilité).</summary>
    public static float RangeFor(float radius) => radius switch
    {
        < 0.75f => 60f,
        < 2.0f => 120f,
        < 6.0f => 220f,
        < 15.0f => 400f,
        _ => 0f,
    };

    /// <summary>Applique la politique au proto GLB
    /// (<paramref name="absPath"/> sert à borner le périmètre :
    /// <c>models/map/model/</c> hors <c>/sky/</c> uniquement).</summary>
    public static void ApplyGlbProto(Node3D proto, string absPath)
    {
        var p = absPath.Replace('\\', '/');
        if (!p.Contains("/map/model/") || p.Contains("/sky/"))
            return;
        Apply(proto, 1f);
    }

    /// <summary>Échelle accumulée des ancêtres (le GLB objets scale
    /// rarement, mais les GLB « prefab » ont des nœuds intermédiaires
    /// scalés — le rayon doit être estimé post-scale).</summary>
    private static void Apply(Node node, float parentScale)
    {
        var scale = parentScale;
        if (node is Node3D n3)
        {
            var bs = n3.Transform.Basis.Scale;
            scale *= MathF.Max(MathF.Abs(bs.X),
                MathF.Max(MathF.Abs(bs.Y), MathF.Abs(bs.Z)));
        }
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var r = mi.Mesh.GetAabb().Size.Length() * 0.5f * scale;
            var range = RangeFor(r);
            if (range > 0f)
            {
                mi.VisibilityRangeEnd = range;
                mi.VisibilityRangeEndMargin = range * 0.08f;
                mi.VisibilityRangeFadeMode =
                    GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
        }
        foreach (var child in node.GetChildren())
            Apply(child, scale);
    }
}
