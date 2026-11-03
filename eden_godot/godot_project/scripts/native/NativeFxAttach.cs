using EdenClient.World;
using Godot;

namespace EdenClient.Native;

/// <summary>Anime les modèles de jeu instanciés (montures, persos, monstres, décors) : particules, défilement d'UV,
/// alpha, couleur, flipbooks, visibilité, transforms de nœud — ce que <see cref="NativeFx"/> fait pour les effets de sort.
/// Posé comme enfant du proto (BuildNativeProto) : <c>Duplicate()</c> le copie dans chaque instance, qui s'anime à l'entrée dans l'arbre.</summary>
public partial class NativeFxAttach : Node
{
    public string NifPath { get; set; } = "";

    /// <summary>Le .nif porte-t-il quelque chose à animer au-delà des KF (contrôleurs de matériau/nœud, particules, flips) ?</summary>
    public static bool Needed(NifFile nif)
    {
        if (nif.ParticleSystems.Count > 0) return true;
        foreach (var c in nif.Controllers)
            if (c.Type is "NiTextureTransformController" or "NiAlphaController" or "NiMaterialColorController"
                or "NiFlipController" or "NiVisController" or "NiGeomMorpherController") return true;
            else if (c.Type == "NiTransformController" && c.Interpolator != null) return true;
        return false;
    }

    public override void _Ready() => CallDeferred(nameof(Attach));

    // Différé : le parent est encore en train d'ajouter ses enfants à l'entrée dans l'arbre (« add_child() failed »).
    void Attach()
    {
        if (!IsInsideTree() || GetParent() is not Node3D root || NifPath.Length == 0) return;
        try
        {
            var nif = AssetCatalog.GetNifCached(NifPath);
            if (nif == null) return;
            var i = NifPath.Replace('\\', '/').IndexOf("/map/", StringComparison.OrdinalIgnoreCase);
            NativeFx.Attach(root, nif, new NativeFxOptions
            {
                OnlyAnimatedMaterials = true,
                HandleBillboards = System.Environment.GetEnvironmentVariable("EDEN_NATIVE_BB") == "0",
                CullDistance = 80f,
                TexPrefix = "",
                TextureLoader = name => LoadTex(name),
            });
        }
        catch (Exception ex) { GD.PrintErr($"[nativefx] {NifPath} : {ex.Message}"); }
    }

    static Texture2D? LoadTex(string name)
    {
        var stem = name.Replace('\\', '/');
        var baseName = stem.Contains('.') ? stem[..stem.LastIndexOf('.')] : stem;
        var rel = NifMaterials.ResolveTexture(System.IO.Path.GetFileName(name), "");
        return rel != null ? AssetCatalog.GetTexture(rel) : AssetCatalog.GetTexture(baseName + ".dds");
    }
}
