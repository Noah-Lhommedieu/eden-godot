using Godot;

namespace EdenClient.World;

/// <summary>
/// Eau animée par <c>.ifl</c> (Gamebryo Image Frame List).
///
/// Les tuiles eau .fsm (kind=1) portent typiquement :
///   slot0 = texture de base (« water_env.dds », « magma_01.dds »),
///   slot1 = « waterani_normal.ifl » → frames caustNN.dds (64×64).
///
/// <see cref="BuildMaterial"/> produit un ShaderMaterial
/// (<c>res://shaders/water.gdshader</c>) partagé par (ifl, base) :
/// l'animation est entièrement GPU — les frames vivent dans un
/// <see cref="Texture2DArray"/> et le shader cycle via
/// <c>mod(TIME*fps)</c>, sans écriture matériau par frame.
///
/// Fallbacks : .ifl absent/non résolu → <c>frame_count=0</c>, la
/// base seule dérive lentement (défilement UV shader) ; frames de
/// tailles hétérogènes (Texture2DArray exige uniformité) → première
/// frame statique. Le matériau reste translucide comme avant
/// (teinte bleutée, alpha 0.62-0.68).
/// </summary>
public static class WaterAnimator
{
    /// <summary>Cadence de défilement des frames .ifl (ips).</summary>
    public static float Fps { get; set; } = 12f;

    /// <summary>Settings &gt; Special FX « Water Wave » : ondulation des
    /// vertex + cycle des frames de caustiques. Réappliqué sur tous les
    /// matériaux en cache et sur ceux construits ensuite.</summary>
    public static bool Waves { get; private set; } = true;

    public static void SetWaves(bool on)
    {
        Waves = on;
        foreach (var m in _matCache.Values)
        {
            m.SetShaderParameter("wave", on ? 1f : 0f);
            m.SetShaderParameter("fps", on ? Fps : 0f);
        }
    }

    private static Shader? _waterShader;
    private static readonly Dictionary<string, ShaderMaterial>
        _matCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (Texture2DArray? Arr,
        int Count)> _frameCache = new(StringComparer.OrdinalIgnoreCase);

    private static Shader WaterShader => _waterShader ??=
        GD.Load<Shader>("res://shaders/water.gdshader");

    /// <summary>Vide les caches à la sortie de la scène de jeu
    /// (RefCounted — le déréférencement suffit).</summary>
    public static void ReleaseCaches()
    {
        _matCache.Clear();
        _frameCache.Clear();
    }

    /// <summary>Matériau eau d'une tuile : cherche un slot .ifl,
    /// construit (ou puise du cache) le ShaderMaterial animé ;
    /// texture de base = premier slot .dds/.png valide.</summary>
    public static Material BuildMaterial(TerrainTile tile)
    {
        string? iflName = null;
        string baseTex = "";
        for (var i = 0; i < FsmFile.TexSlotCount; i++)
        {
            var slot = tile.GetTexSlot(i);
            if (!slot.Used || slot.Name.Length == 0) continue;
            if (slot.Name.EndsWith(".ifl",
                    StringComparison.OrdinalIgnoreCase))
                iflName ??= slot.Name;
            else if (baseTex.Length == 0)
                baseTex = slot.Name;
        }

        var key = $"{iflName}|{baseTex}";
        if (_matCache.TryGetValue(key, out var cached))
            return cached;

        var mat = new ShaderMaterial { Shader = WaterShader };
        var tex = baseTex.Length > 0
            ? AssetCatalog.GetTexture($"map/texture/{baseTex}")
            : null;
        mat.SetShaderParameter("base_tex",
            (Texture2D?)tex ?? AssetCatalog.White);
        // Teinte : opaque blanchâtre quand une vraie base existe
        // (water_env est quasi blanc — bleui par tint), sinon la
        // couleur pleine historique.
        var tint = tex != null
            ? new Color(0.75f, 0.9f, 1f, 0.68f)
            : new Color(0.45f, 0.65f, 0.8f, 0.62f);
        mat.SetShaderParameter("tint", tint);
        mat.SetShaderParameter("fps", Waves ? Fps : 0f);
        mat.SetShaderParameter("wave", Waves ? 1f : 0f);

        var (arr, count) = LoadFrames(iflName);
        mat.SetShaderParameter("frames",
            (Texture2DArray?)arr ?? WhiteArray());
        mat.SetShaderParameter("frame_count", count);

        _matCache[key] = mat;
        return mat;
    }

    /// <summary>Charge les frames d'un .ifl en Texture2DArray
    /// (résolution : ifl sous <c>map/texture/</c> en raw, frames sous
    /// <c>assets/textures/map/texture/&lt;nom&gt;.png</c>). Null/0 si
    /// introuvable ou tailles hétérogènes.</summary>
    private static (Texture2DArray?, int) LoadFrames(string? iflName)
    {
        if (iflName == null) return (null, 0);
        if (_frameCache.TryGetValue(iflName, out var hit))
            return hit;

        (Texture2DArray?, int) result = (null, 0);
        var path = AssetCatalog.ResolveRawAsset(
            $"map/texture/{iflName}");
        if (path != null)
        {
            var frames = IflFile.Load(path).Frames;
            var images = new List<Image>();
            foreach (var name in frames)
            {
                var t = AssetCatalog.GetTexture($"map/texture/{name}");
                var img = t?.GetImage();
                if (img == null) continue;
                if (images.Count > 0 &&
                    (img.GetWidth() != images[0].GetWidth() ||
                     img.GetHeight() != images[0].GetHeight() ||
                     img.GetFormat() != images[0].GetFormat()))
                {
                    GD.Print($"[water] {iflName}: frame «{name}» " +
                        $"{img.GetWidth()}x{img.GetHeight()} {img.GetFormat()}" +
                        " ≠ — array annulé");
                    images.Clear();
                    break;
                }
                images.Add(img);
            }
            if (images.Count > 0)
            {
                var arr = new Texture2DArray();
                if (arr.CreateFromImages(
                        new Godot.Collections.Array<Image>(images))
                    == Error.Ok)
                    result = (arr, images.Count);
                else
                    GD.PrintErr($"[water] {iflName}: Texture2DArray KO");
            }
        }
        else
        {
            GD.Print($"[water] {iflName} introuvable sous map/texture");
        }
        _frameCache[iflName] = result;
        return result;
    }

    /// <summary>Array 1 couche blanche (sampler non-null requis par
    /// le shader quand frame_count=0).</summary>
    private static Texture2DArray? _whiteArr;
    private static Texture2DArray WhiteArray()
    {
        if (_whiteArr != null) return _whiteArr;
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(new Color(1f, 1f, 1f, 1f));
        var arr = new Texture2DArray();
        arr.CreateFromImages(new Godot.Collections.Array<Image> { img });
        _whiteArr = arr;
        return arr;
    }
}
