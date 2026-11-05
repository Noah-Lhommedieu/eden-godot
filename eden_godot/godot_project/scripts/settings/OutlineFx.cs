using Godot;

namespace EdenClient.Settings;

/// <summary>
/// Contour « coque inversée » (MaterialOverlay) conscient de l'alpha.
/// Bug « bloc noir sur la toile du Germe Fétide » (mob 58160/M609) : la
/// toile est un quad en alpha-test (NiAlphaProperty 0x12EC, seuil 125)
/// dans la même texture que le corps ; l'overlay opaque d'origine
/// redessinait ce quad entier en noir (fond transparent compris) dès que
/// <c>outline=all</c> (config live) — invisible dans les bancs de test
/// où le contour est coupé.
/// Règles : surface opaque -> overlay opaque inchangé ; mesh à UNE
/// surface alpha-test/blend -> overlay qui rejette les mêmes texels
/// (même texture, même seuil) ; blend additif (lueurs) ou mesh
/// multi-surfaces mêlant alpha et opaque -> pas de contour (pas de bloc
/// noir plutôt qu'un contour faux).
/// </summary>
internal static class OutlineFx
{
    private const string AlphaOutlineShader = @"
shader_type spatial;
render_mode unshaded, cull_front;
uniform vec4 outline_color : source_color = vec4(0.05, 0.04, 0.04, 1.0);
uniform float thickness = 0.022;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_tint : source_color = vec4(1.0);
uniform float alpha_cut = 0.5;
void vertex() {
    vec4 wp = MODEL_MATRIX * vec4(VERTEX, 1.0);
    vec3 wn = normalize(mat3(MODEL_MATRIX) * NORMAL);
    wp.xyz += wn * thickness;
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * wp;
}
void fragment() {
    float a = texture(albedo_tex, UV).a * albedo_tint.a;
    if (a < alpha_cut) discard;
    ALBEDO = outline_color.rgb;
}";

    private static Shader? _shader;
    private static readonly Dictionary<(ulong, int), ShaderMaterial> _cache = new();

    /// <summary>Pose (ou retire) le contour sur <paramref name="mi"/>.</summary>
    public static void Apply(MeshInstance3D mi, bool on, Material opaqueOutline)
    {
        if (!on) { mi.MaterialOverlay = null; return; }
        var n = mi.Mesh!.GetSurfaceCount();
        BaseMaterial3D? alpha = null;
        var transparent = 0;
        for (var i = 0; i < n; i++)
        {
            if (mi.GetActiveMaterial(i) is not BaseMaterial3D b ||
                b.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
                continue;
            transparent++;
            alpha = b;
        }
        if (transparent == 0) { mi.MaterialOverlay = opaqueOutline; return; }
        if (n != 1 || alpha == null || alpha.AlbedoTexture == null ||
            alpha.BlendMode == BaseMaterial3D.BlendModeEnum.Add)
        {
            mi.MaterialOverlay = null;
            return;
        }
        mi.MaterialOverlay = For(alpha);
    }

    private static ShaderMaterial For(BaseMaterial3D b)
    {
        var tex = b.AlbedoTexture!;
        var scissor = b.Transparency ==
            BaseMaterial3D.TransparencyEnum.AlphaScissor;
        var cut = scissor ? b.AlphaScissorThreshold : 0.5f;
        var key = (tex.GetInstanceId(), (int)(cut * 1000f));
        if (_cache.TryGetValue(key, out var hit) &&
            GodotObject.IsInstanceValid(hit)) return hit;
        _shader ??= new Shader { Code = AlphaOutlineShader };
        var m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("albedo_tex", tex);
        m.SetShaderParameter("albedo_tint", b.AlbedoColor);
        m.SetShaderParameter("alpha_cut", cut);
        _cache[key] = m;
        return m;
    }
}
