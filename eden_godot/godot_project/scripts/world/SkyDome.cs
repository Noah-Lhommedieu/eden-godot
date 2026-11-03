using Godot;

namespace EdenClient.World;

/// <summary>
/// Dôme de ciel natif (<c>map/model/sky/&lt;code&gt;.glb</c> — le code
/// vient de <c>Sxxx.ini [Scene] Sky&lt;Phase&gt;</c>). Le mesh est un
/// panorama cylindrique (~2,2 km de rayon, coords natives Z-up) : il
/// est placé sous la racine « World » tournée et **suit la caméra** —
/// l'horizon peint reste donc toujours à hauteur d'œil, comme dans le
/// client original.
///
/// Rendu : <c>res://shaders/sky_dome.gdshader</c> — non éclairé,
/// fog_disabled (sinon le dôme serait saturé de brouillard), cull
/// disabled (vu de l'intérieur), alpha conservé (quad « sun » blend).
/// Le nœud « sun » du glTF donne la direction du soleil —
/// <see cref="SunDirectionEden"/>.
/// </summary>
public partial class SkyDome : Node3D
{
    private static Shader? _skyShader;
    private Shader SkyShader => _skyShader ??=
        GD.Load<Shader>("res://shaders/sky_dome.gdshader");

    /// <summary>Direction du soleil en coords natives Eden (normalisée)
    /// — lue depuis le nœud « sun » du dôme chargé ; zéro si absente.</summary>
    public System.Numerics.Vector3 SunDirectionEden { get; private set; }

    /// <summary>Charge le dôme <paramref name="skyCode"/> (« N201 ») ;
    /// null si absent.</summary>
    public static SkyDome? Create(string skyCode)
    {
        if (skyCode.Length == 0 ||
            !AssetCatalog.TryGetGlb($"map/model/sky/{skyCode}",
                out var path))
            return null;
        var model = AssetCatalog.LoadGlbNode(path);
        if (model == null) return null;
        var dome = new SkyDome { Name = $"Sky_{skyCode}" };
        dome.AddChild(model);
        dome.Setup(model, skyCode);
        GD.Print($"[SkyDome] {skyCode}: sunDir={dome.SunDirectionEden}");
        return dome;
    }

    private void Setup(Node3D model, string skyCode)
    {
        // Nœud « sun » → direction (avant de masquer : on garde le
        // quad visible — sprite soleil voulu).
        if (model.FindChild("sun", true, false) is Node3D sun)
        {
            var p = sun.Position;
            var len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            if (len > 1f)
                SunDirectionEden = new System.Numerics.Vector3(
                    p.X / len, p.Y / len, p.Z / len);
        }
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(model);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                mi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
                for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                {
                    var src = mi.GetSurfaceOverrideMaterial(i)
                              ?? mi.Mesh.SurfaceGetMaterial(i);
                    var texName = mi.Name.ToString()
                        .Contains("sun", StringComparison.OrdinalIgnoreCase)
                        ? "sun" : $"{skyCode}01";
                    var overrideTex = AssetCatalog.GetTexture(
                        $"map/texture/{texName}.dds");
                    mi.SetSurfaceOverrideMaterial(i,
                        SkyMaterialFor(src, overrideTex));
                }
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
    }

    /// <summary>ShaderMaterial ciel pour un matériau source : la
    /// texture albedo (ou émission — le dome exporte baseColorFactor=0
    /// + emissive=blanc) est la couleur finale.</summary>
    private ShaderMaterial SkyMaterialFor(Material? src,
        Texture2D? overrideTex)
    {
        var mat = new ShaderMaterial { Shader = SkyShader };
        Texture2D? tex = overrideTex;
        var modulate = new Vector4(1f, 1f, 1f, 1f);
        if (src is StandardMaterial3D sm)
        {
            tex ??= sm.AlbedoTexture ?? sm.EmissionTexture;
            var c = sm.AlbedoColor;
            // Dome : baseColorFactor noir + emissive blanc → modulate
            // blanc (la texture porte la couleur).
            if (c.R > 0.01f || c.G > 0.01f || c.B > 0.01f)
                modulate = new Vector4(c.R, c.G, c.B, c.A);
        }
        mat.SetShaderParameter("albedo_tex",
            (Texture2D?)tex ?? AssetCatalog.White);
        mat.SetShaderParameter("modulate", modulate);
        return mat;
    }

    /// <summary>Suit la caméra courante (coords natives sous « World »).</summary>
    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var g = cam.GlobalPosition;
        // inverse de ToGodot : eden = (gx, −gz, gy)
        Position = new Vector3(g.X, -g.Z, g.Y);
    }
}
