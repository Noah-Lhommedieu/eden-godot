using System.Numerics;
using System.Text;
using EdenClient.Fx;
using EdenClient.World;
using Godot;
using GMatrix = System.Numerics.Matrix4x4;
using NMat = EdenClient.World.NifMaterial;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace EdenClient.Native;

/// <summary>Réglages de <see cref="NativeFx.Attach"/>.</summary>
public sealed class NativeFxOptions
{
    /// <summary>Au-delà (m, distance caméra - racine) l'effet est mis en pause et masqué.</summary>
    public float CullDistance = 120f;
    /// <summary>Pas d'horloge ni de simulation quand la racine est invisible dans l'arbre.</summary>
    public bool PauseWhenHidden = true;
    public float TimeScale = 1f;
    public uint Seed = 20260211;
    /// <summary>Préfixe des textures NIF (noms nus) pour <see cref="TextureLoader"/>.</summary>
    public string TexPrefix = "effect/texture/";
    /// <summary>Résolution d'une texture (chemin relatif d'asset). Défaut : AssetCatalog.GetTexture.</summary>
    public Func<string, Texture2D?>? TextureLoader;
    /// <summary>Remplace les matériaux des géométries par ceux de NativeFx (blend additif/mixte,
    /// UV, alpha, couleur pilotés). Faux : on pilote le matériau existant (Standard / toon_lit).</summary>
    public bool OwnMaterials = true;
    /// <summary>Oriente les NiBillboardNode vers la caméra.</summary>
    public bool HandleBillboards = true;
    /// <summary>Le V des UV est inversé par le chargeur (v' = 1 - v) : inverse le défilement V.</summary>
    public bool InvertV = true;
    /// <summary>Particules déjà en vol au départ (s).</summary>
    public float Prewarm = 0f;
    /// <summary>Séquence NiControllerSequence embarquée à lire (null : la première, s'il y en a).</summary>
    public string? Sequence;
    /// <summary>Modèles de jeu (montures, persos, monstres, décors) : ne remplace que les matériaux ciblés par un
    /// contrôleur de matériau (UV, alpha, couleur, flip) ; le reste garde le rendu toon normal.</summary>
    public bool OnlyAnimatedMaterials;
}

/// <summary>Bilan d'un Attach (couverture, tests).</summary>
public sealed class NativeFxReport
{
    public int ParticleSystems, Geoms, MorphsBuilt, MorphsSkipped;
    public Dictionary<string, int> Drivers = new();
    public List<string> Notes = new();
    public void Count(string k) => Drivers[k] = Drivers.GetValueOrDefault(k) + 1;
    public void Note(string n) { if (!Notes.Contains(n)) Notes.Add(n); }
}

internal interface IFxDriver
{
    void Update(float t, float dt);
    void SetVisible(bool v) { }
}

/// <summary>Point d'entrée unique : ajoute particules et contrôleurs animés à un arbre NifScene.</summary>
public static class NativeFx
{
    /// <summary>
    /// Ajoute à <paramref name="root"/> (arbre construit depuis <paramref name="nif"/>, nœuds repérés
    /// par la méta « nif_block ») : systèmes de particules (MultiMesh billboards), défilement UV, alpha,
    /// couleur de matériau, flipbooks (NiFlipController + .ifl), visibilité, transform, morphs, billboards.
    /// Retourne l'hôte (nœud enfant de root) qui porte l'horloge ; il se libère avec root.
    /// </summary>
    public static NativeFxHost Attach(Node3D root, NifFile nif, NativeFxOptions? options = null)
    {
        options ??= new NativeFxOptions();
        var host = new NativeFxHost { Name = "NativeFx", Options = options, Root = root, Nif = nif };
        root.AddChild(host);
        host.Build();
        return host;
    }

    // --- utilitaires partagés ---------------------------------------------------------------

    /// <summary>Matrice System.Numerics (row-vector) -> Transform3D (v' = B·v).</summary>
    public static Transform3D ToTransform(GMatrix m) => new(
        new Godot.Basis(new Godot.Vector3(m.M11, m.M12, m.M13), new Godot.Vector3(m.M21, m.M22, m.M23),
            new Godot.Vector3(m.M31, m.M32, m.M33)),
        new Godot.Vector3(m.M41, m.M42, m.M43));

    public static GMatrix ToMatrix(Transform3D t)
    {
        var b = t.Basis; var o = t.Origin;
        return new GMatrix(b.Column0.X, b.Column0.Y, b.Column0.Z, 0, b.Column1.X, b.Column1.Y, b.Column1.Z, 0,
            b.Column2.X, b.Column2.Y, b.Column2.Z, 0, o.X, o.Y, o.Z, 1);
    }

    public static Godot.Color Col(Vector3 v, float a = 1f) => new(v.X, v.Y, v.Z, a);

    static readonly Dictionary<string, Shader> s_shaders = new();

    /// <summary>Mode de fusion NIF : 0 opaque, 1 test alpha, 2 mélange, 3 additif.</summary>
    public static int BlendMode(NifAlphaProperty? a)
    {
        if (a == null) return 0;
        if (a.Blend) return a.DstBlend == 0 ? 3 : 2;
        return a.Test ? 1 : 0;
    }

    public static Shader GetShader(int blend, bool twoSided, bool depthTest, bool depthWrite)
    {
        string key = $"{blend}{twoSided}{depthTest}{depthWrite}";
        lock (s_shaders)
        {
            if (s_shaders.TryGetValue(key, out var s)) return s;
            var rm = new StringBuilder("unshaded");
            rm.Append(twoSided || blend != 0 ? ", cull_disabled" : ", cull_back");
            if (blend == 3) rm.Append(", blend_add"); else if (blend == 2) rm.Append(", blend_mix");
            if (!depthTest) rm.Append(", depth_test_disabled");
            rm.Append(blend >= 2 && !depthWrite ? ", depth_draw_never" : ", depth_draw_opaque");
            string alpha = blend switch
            {
                0 => "ALPHA = 1.0;",
                1 => "ALPHA = t.a * albedo.a * vcol.a; ALPHA_SCISSOR_THRESHOLD = alpha_cut;",
                _ => "ALPHA = t.a * albedo.a * vcol.a;",
            };
            var code = $@"shader_type spatial;
render_mode {rm};
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo = vec4(1.0);
uniform vec3 emissive = vec3(0.0);
uniform vec3 uv_offset = vec3(0.0);
uniform vec3 uv_scale = vec3(1.0);
uniform vec3 uv_center = vec3(0.5, 0.5, 0.0);
uniform float uv_rot = 0.0;
uniform float alpha_cut = 0.5;
uniform bool use_tex = true;
varying vec4 vcol;
void vertex() {{ vcol = COLOR; }}
void fragment() {{
    vec2 p = (UV - uv_center.xy) * uv_scale.xy;
    float c = cos(uv_rot), s = sin(uv_rot);
    p = vec2(c * p.x - s * p.y, s * p.x + c * p.y) + uv_center.xy + uv_offset.xy;
    vec4 t = use_tex ? texture(albedo_texture, p) : vec4(1.0);
    ALBEDO = (t.rgb * albedo.rgb + emissive) * vcol.rgb;
    {alpha}
}}";
            return s_shaders[key] = new Shader { Code = code };
        }
    }

    /// <summary>Matériau NativeFx d'après les propriétés NIF (géométrie ou système de particules).</summary>
    public static (ShaderMaterial mat, MatState state) MakeMaterial(
        NMat? nm, NifTexturingProperty? tex, NifAlphaProperty? alpha, NifZBufferProperty? zb,
        bool twoSided, NativeFxOptions o, bool particle)
    {
        int blend = BlendMode(alpha);
        var sm = new ShaderMaterial
        {
            Shader = GetShader(blend, twoSided, zb?.Test ?? true, particle ? false : (zb?.Write ?? true)),
        };
        var st = new MatState(sm, o.InvertV);
        if (nm != null)
        {
            st.Diffuse = nm.Diffuse; st.Emissive = nm.Emissive; st.Alpha = nm.Alpha;
        }
        if (alpha != null && alpha.Test) sm.SetShaderParameter("alpha_cut", alpha.Threshold / 255f);
        if (tex?.Base is { } b)
        {
            if (b.HasTransform) { st.Tu = b.Translation.X; st.Tv = b.Translation.Y; st.Su = b.Scale.X; st.Sv = b.Scale.Y; st.Rot = b.Rotation; st.Center = b.Center; }
            if (b.File != null) st.SetTextureFile(b.File, o);
        }
        else sm.SetShaderParameter("use_tex", false);
        st.Dirty = true; st.Apply();
        return (sm, st);
    }
}

/// <summary>État animé d'un matériau NativeFx ; les contrôleurs écrivent, Apply pousse au shader.</summary>
public sealed class MatState
{
    public readonly ShaderMaterial Mat;
    readonly bool _invertV;
    public float Tu, Tv, Su = 1, Sv = 1, Rot;
    public Vector2 Center = new(0.5f, 0.5f);
    public Vector3 Diffuse = Vector3.One, Emissive = Vector3.Zero;
    public float Alpha = 1f;
    public Texture2D? Texture;
    public bool Dirty = true;
    public MatState(ShaderMaterial m, bool invertV) { Mat = m; _invertV = invertV; }

    public void SetTextureFile(string file, NativeFxOptions o)
    {
        var loader = o.TextureLoader ?? AssetCatalog.GetTexture;
        Texture = loader(o.TexPrefix + file) ?? loader(file);
        Dirty = true;
    }

    public void Apply()
    {
        if (!Dirty) return;
        Dirty = false;
        Mat.SetShaderParameter("uv_offset", new Godot.Vector3(Tu, _invertV ? -Tv : Tv, 0));
        Mat.SetShaderParameter("uv_scale", new Godot.Vector3(Su, Sv, 1));
        Mat.SetShaderParameter("uv_center", new Godot.Vector3(Center.X, Center.Y, 0));
        Mat.SetShaderParameter("uv_rot", Rot);
        Mat.SetShaderParameter("albedo", new Godot.Color(Diffuse.X, Diffuse.Y, Diffuse.Z, Alpha));
        Mat.SetShaderParameter("emissive", new Godot.Vector3(Emissive.X, Emissive.Y, Emissive.Z));
        if (Texture != null) { Mat.SetShaderParameter("albedo_texture", Texture); Mat.SetShaderParameter("use_tex", true); }
    }
}
