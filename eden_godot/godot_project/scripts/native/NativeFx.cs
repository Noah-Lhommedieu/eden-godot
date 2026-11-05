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
    /// <summary>Inverse le défilement V quand le chargeur a flippé les UV du mesh (v' = 1 - v).
    /// Faux pour les chemins production : NifScene copie les UV NIF bruts (v=0 en haut, comme
    /// Gamebryo) ; un -Tv appliqué ici inversait le scroll (flammes r253 pointe→base au lieu de
    /// base→pointe). Vrai uniquement pour les arbres à UV flippés (DemoTree, bancs).</summary>
    public bool InvertV;
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

    static readonly Dictionary<NifPixelData, Texture2D?> s_pix = new();

    /// <summary>Texture embarquée (NiPixelData) → ImageTexture RGBA8 avec mips (null si format inconnu).</summary>
    public static Texture2D? PixelTexture(NifPixelData px)
    {
        lock (s_pix)
        {
            if (s_pix.TryGetValue(px, out var c)) return c;
            Image? img = null;
            if (px.Mips.Length > 0)
            {
                var (w, h, off) = px.Mips[0];
                var d = px.Data;
                if (w > 0 && h > 0 && off >= 0)
                {
                    int bpp = Math.Max(1, px.BytesPerPixel);
                    switch (px.Format)
                    {
                        case 0 or 1 when off + w * h * bpp <= d.Length && bpp >= 3:
                        {
                            var rgba = new byte[w * h * 4];
                            for (int i = 0; i < w * h; i++)
                            {
                                rgba[i * 4 + 3] = 255;
                                for (int k = 0; k < bpp && k < 4; k++)
                                {
                                    int ch = px.Channels[k];
                                    if (ch is >= 0 and < 4) rgba[i * 4 + ch] = d[off + i * bpp + k];
                                }
                            }
                            img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
                            break;
                        }
                        case 4 or 5 or 6:
                        {
                            var f = px.Format == 4 ? Image.Format.Dxt1 : px.Format == 5 ? Image.Format.Dxt3 : Image.Format.Dxt5;
                            int need = ((w + 3) / 4) * ((h + 3) / 4) * (px.Format == 4 ? 8 : 16);
                            if (off + need > d.Length) break;
                            img = Image.CreateFromData(w, h, false, f, d.AsSpan(off, need).ToArray());
                            img.Decompress();
                            img.Convert(Image.Format.Rgba8);
                            break;
                        }
                    }
                }
            }
            Texture2D? tex = null;
            if (img != null) { img = MipmapsPremult(img); tex = ImageTexture.CreateFromImage(img); }
            return s_pix[px] = tex;
        }
    }

    /// <summary>Mips en alpha prémultiplié puis division par alpha : le filtre box de
    /// <see cref="Image.GenerateMipmaps"/> mélange le RGB des texels transparents (souvent noir)
    /// aux bords → liseré sombre autour des sprites alpha-blendés. En prémultipliant d'abord
    /// puis en divisant, chaque niveau garde la couleur du voisinage opaque.</summary>
    static Image MipmapsPremult(Image img)
    {
        if (img.GetFormat() != Image.Format.Rgba8) { img.GenerateMipmaps(); return img; }
        int w = img.GetWidth(), h = img.GetHeight();
        if (w <= 1 && h <= 1) return img;
        var levels = new List<byte[]> { img.GetData() };
        Premult(levels[0]);
        int cw = w, ch = h;
        var cur = levels[0];
        while (cw > 1 || ch > 1)
        {
            int nw = Math.Max(1, cw >> 1), nh = Math.Max(1, ch >> 1);
            var nx = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0, cnt = 0;
                    for (int dy = 0; dy < 2 && y * 2 + dy < ch; dy++)
                        for (int dx = 0; dx < 2 && x * 2 + dx < cw; dx++)
                        {
                            int o = ((y * 2 + dy) * cw + x * 2 + dx) * 4;
                            r += cur[o]; g += cur[o + 1]; b += cur[o + 2]; a += cur[o + 3]; cnt++;
                        }
                    int oo = (y * nw + x) * 4;
                    nx[oo] = (byte)(r / cnt); nx[oo + 1] = (byte)(g / cnt);
                    nx[oo + 2] = (byte)(b / cnt); nx[oo + 3] = (byte)(a / cnt);
                }
            levels.Add(nx); cur = nx; cw = nw; ch = nh;
        }
        int total = 0;
        foreach (var l in levels) { Unpremult(l); total += l.Length; }
        var all = new byte[total];
        int pos = 0;
        foreach (var l in levels) { Array.Copy(l, 0, all, pos, l.Length); pos += l.Length; }
        return Image.CreateFromData(w, h, true, Image.Format.Rgba8, all);
    }

    static void Premult(byte[] d)
    {
        for (int i = 0; i + 3 < d.Length; i += 4)
        {
            int a = d[i + 3];
            if (a >= 255) continue;
            d[i] = (byte)(d[i] * a / 255); d[i + 1] = (byte)(d[i + 1] * a / 255); d[i + 2] = (byte)(d[i + 2] * a / 255);
        }
    }

    static void Unpremult(byte[] d)
    {
        for (int i = 0; i + 3 < d.Length; i += 4)
        {
            int a = d[i + 3];
            if (a is 0 or >= 255) continue;
            d[i] = (byte)Math.Min(255, d[i] * 255 / a);
            d[i + 1] = (byte)Math.Min(255, d[i + 1] * 255 / a);
            d[i + 2] = (byte)Math.Min(255, d[i + 2] * 255 / a);
        }
    }

    public static Shader GetShader(int blend, bool twoSided, bool depthTest, bool depthWrite, bool repeat = true)
    {
        string key = $"{blend}{twoSided}{depthTest}{depthWrite}{repeat}";
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
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, {(repeat ? "repeat_enable" : "repeat_disable")};
uniform vec4 albedo = vec4(1.0);
uniform vec3 emissive = vec3(0.0);
uniform vec3 uv_offset = vec3(0.0);
uniform vec3 uv_scale = vec3(1.0);
uniform vec3 uv_center = vec3(0.5, 0.5, 0.0);
uniform vec2 uv_clamp = vec2(0.0);
uniform float uv_rot = 0.0;
uniform float alpha_cut = 0.5;
uniform bool use_tex = true;
varying vec4 vcol;
void vertex() {{ vcol = COLOR; }}
void fragment() {{
    vec2 p = (UV - uv_center.xy) * uv_scale.xy;
    float c = cos(uv_rot), s = sin(uv_rot);
    p = vec2(c * p.x - s * p.y, s * p.x + c * p.y) + uv_center.xy + uv_offset.xy;
    // ClampMode mixte NIF (1 = clamp S, 2 = clamp T) : un seul axe se borne,
    // l'autre garde la répétition (défilement UV animé).
    if (uv_clamp.x > 0.5) p.x = clamp(p.x, 0.0, 1.0);
    if (uv_clamp.y > 0.5) p.y = clamp(p.y, 0.0, 1.0);
    vec4 t = use_tex ? texture(albedo_texture, p) : vec4(1.0);
    // Émissif DANS le facteur de la texture (pipeline fixe Gamebryo :
    // tex × (emissive + ambient·Ia)) — l'ajouter à plat (« + emissive »)
    // rend tout matériau émissif≥1 uniformément blanc (halos r014 :
    // emissive=1,1,1 écrasait la flamme bleue de R00251.dds).
    ALBEDO = t.rgb * (albedo.rgb + emissive) * vcol.rgb;
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
        // ClampMode NIF : 0 = CLAMP_S_CLAMP_T → repeat désactivé (sinon les UV qui dépassent ou
        // le filtre en bord de quad rabattent les texels du bord opposé — liseré visible).
        // 1/2 (mixte) et 3 (WRAP) gardent la répétition pour les défilements UV.
        // EDEN_FX_CLAMP=0 : forcer l'ancien comportement (repeat partout) — preuve A/B.
        bool repeat = System.Environment.GetEnvironmentVariable("EDEN_FX_CLAMP") == "0"
                      || tex?.Base == null || tex.Base.ClampMode != 0;
        var sm = new ShaderMaterial
        {
            Shader = GetShader(blend, twoSided, zb?.Test ?? true, particle ? false : (zb?.Write ?? true), repeat),
        };
        var st = new MatState(sm, o.InvertV);
        if (nm != null)
        {
            st.Diffuse = nm.Diffuse; st.Emissive = nm.Emissive; st.Alpha = nm.Alpha;
        }
        if (alpha != null && alpha.Test) sm.SetShaderParameter("alpha_cut", alpha.Threshold / 255f);
        if (tex?.Base is { } b)
        {
            // Clamp par axe : 0 = les deux, 1 = S seul, 2 = T seul. Mode 0 passe aussi
            // par repeat_disable du sampler (halo de filtrage en bord de quad).
            if (repeat && b.ClampMode is 1 or 2)
                sm.SetShaderParameter("uv_clamp", new Godot.Vector2(b.ClampMode == 1 ? 1 : 0, b.ClampMode == 2 ? 1 : 0));
            if (b.HasTransform) { st.Tu = b.Translation.X; st.Tv = b.Translation.Y; st.Su = b.Scale.X; st.Sv = b.Scale.Y; st.Rot = b.Rotation; st.Center = b.Center; }
            if (b.File != null) st.SetTextureFile(b.File, o);
            else if (b.Source?.Pixels is { } px) st.Texture = PixelTexture(px);
            if (st.Texture == null) sm.SetShaderParameter("use_tex", false);   // fichier absent / pixels illisibles
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
