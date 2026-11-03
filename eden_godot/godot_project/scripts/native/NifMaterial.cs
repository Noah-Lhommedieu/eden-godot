using System.Text.RegularExpressions;
using EdenClient.World;
using Godot;

namespace EdenClient.Native;

/// <summary>
/// NifShape (propriétés Gamebryo effectives) → StandardMaterial3D équivalent à ce que
/// l'import glTF donnait (couleur diffuse+alpha, émissif, texture de base, alpha blend/test,
/// 2 faces). <see cref="World.ToonMaterializer"/> le convertit ensuite en toon_lit comme pour les GLB.
/// Résolution des textures : le NIF ne donne qu'un nom de fichier nu (« UA00101.dds »), on l'indexe
/// par nom de base sous les arbres de textures de la racine de données, avec les mêmes replis que
/// convert.py (TextureIndex.resolve).
/// </summary>
public static class NifMaterials
{
    // ---- index des .dds ---------------------------------------------------
    static readonly object s_lock = new();
    static Dictionary<string, List<string>>? s_byBase;     // « ua00101 » → rels
    static Dictionary<string, string>? s_byRel;            // « map/texture/ua00101 » → rel
    static string[] s_keys = Array.Empty<string>();         // clés triées de s_byBase
    static readonly Dictionary<string, string?> s_memo = new(StringComparer.OrdinalIgnoreCase);
    static readonly string[] TexTops = { "map", "char", "monster", "ride", "item", "effect", "house", "decorate" };

    /// <summary>Vrai si la racine de données est un dossier loose indexable, ou si des EPK sont montés.</summary>
    public static bool Available => Directory.Exists(AssetCatalog.RawAssetsRoot) || Vfs.EdenVfs.HasPacks;

    static void EnsureIndex()
    {
        if (s_byBase != null) return;
        lock (s_lock)
        {
            if (s_byBase != null) return;
            var byBase = new Dictionary<string, List<string>>();
            var byRel = new Dictionary<string, string>();
            var root = AssetCatalog.RawAssetsRoot;
            void Add(string rel)
            {
                var b = Path.GetFileNameWithoutExtension(rel).ToLowerInvariant();
                if (!byBase.TryGetValue(b, out var l)) byBase[b] = l = new List<string>();
                l.Add(rel);
                byRel[rel[..^4].ToLowerInvariant()] = rel;
            }
            if (!Vfs.EdenVfs.LooseAllowed)
            {
                // Pack seul : l'index vient des entrées EPK (clés normalisées en minuscules).
                foreach (var top in TexTops)
                    foreach (var e in Vfs.EdenVfs.ListDir(Vfs.EdenVfs.Scheme + top, true))
                        if (e.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                            Add(Vfs.EdenVfs.IsEpk(e) ? Vfs.EdenVfs.EpkRel(e) : e);
            }
            else
            foreach (var top in Directory.EnumerateDirectories(root))
            {
                var tn = Path.GetFileName(top);
                if (Array.IndexOf(TexTops, tn.ToLowerInvariant()) < 0) continue;
                foreach (var f in Directory.EnumerateFiles(top, "*.dds", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive }))
                    Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
            }
            GD.Print($"[native] index DDS : {byRel.Count} chemins, {byBase.Count} noms ({(Vfs.EdenVfs.LooseAllowed ? "loose" : "EPK")})");
            foreach (var l in byBase.Values) l.Sort(StringComparer.OrdinalIgnoreCase);
            s_keys = byBase.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            s_byRel = byRel;
            s_byBase = byBase;
        }
    }

    static readonly Regex SufRx = new(@"^(.+?)(_\d+|_nrm|_n|_s)$", RegexOptions.Compiled);
    static readonly Regex PfxRx = new(@"^(?:ts|ak|dm|dvgn|a)([a-z]+\d{4,}.*)$", RegexOptions.Compiled);

    /// <summary>Nom de texture du NIF → chemin relatif .dds sous la racine de données (ou null).</summary>
    public static string? ResolveTexture(string nifName, string prefix)
    {
        if (string.IsNullOrEmpty(nifName)) return null;
        EnsureIndex();
        var key = prefix + "|" + nifName;
        lock (s_memo)
            if (s_memo.TryGetValue(key, out var m)) return m;
        var r = ResolveCore(nifName, prefix);
        lock (s_memo) s_memo[key] = r;
        return r;
    }

    static string? ResolveCore(string nifName, string prefix)
    {
        var p = nifName.Replace('\\', '/').Trim();
        while (p.StartsWith("./") || p.StartsWith("/")) p = p.StartsWith("./") ? p[2..] : p[1..];
        var low = p.ToLowerInvariant();
        var dot = low.LastIndexOf('.');
        var stem = dot > 0 ? low[..dot] : low;
        if (s_byRel!.TryGetValue(stem, out var exact)) return exact;
        var bstem = stem[(stem.LastIndexOf('/') + 1)..];
        var byBase = s_byBase!;
        List<string>? hits = byBase.GetValueOrDefault(bstem);
        if (hits == null)
        {
            var m0 = SufRx.Match(bstem);
            if (m0.Success && m0.Groups[1].Length >= 4)
            {
                bstem = m0.Groups[1].Value;
                hits = byBase.GetValueOrDefault(bstem);
            }
        }
        // Variante « xxxxx01 » : seulement pour un stem à variante (>= 5 car.). Avec « m109 » (4 car.) la règle donnait
        // « m1 »+« 06 » = m106 : la texture d'un AUTRE monstre ; le repli par préfixe (m10901) est le bon (TEXTURE-AUDIT.md).
        if (hits == null && bstem.Length >= 5 && char.IsDigit(bstem[^1]) && char.IsDigit(bstem[^2]))
            foreach (var v in new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "51", "52", "53", "54", "55" })
                if ((hits = byBase.GetValueOrDefault(bstem[..^2] + v)) != null) break;
        if (hits == null && bstem.Length >= 3)
        {
            var l = new List<string>();
            foreach (var k in s_keys.Where(k => k.StartsWith(bstem, StringComparison.Ordinal))
                         .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal))
                l.AddRange(byBase[k]);
            if (l.Count > 0) hits = l;
        }
        if (hits == null && bstem.Length >= 4)
        {
            var l = new List<string>();
            foreach (var k in s_keys.Where(k => k.EndsWith(bstem, StringComparison.Ordinal) && k.Length - bstem.Length <= 4)
                         .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal))
                l.AddRange(byBase[k]);
            if (l.Count > 0) hits = l;
        }
        if (hits == null && bstem.Length > 5 && char.IsDigit(bstem[^1]) && char.IsDigit(bstem[^2]))
        {
            var rx = new Regex("^[a-z]{0,4}" + Regex.Escape(bstem[..^2]) + @"(\d\d).*$");
            var want = int.Parse(bstem[^2..]);
            var best = s_keys.Select(k => (k, m: rx.Match(k))).Where(x => x.m.Success)
                .OrderBy(x => Math.Abs(int.Parse(x.m.Groups[1].Value) - want)).ThenBy(x => x.k.Length)
                .ThenBy(x => x.k, StringComparer.Ordinal).FirstOrDefault();
            if (best.k != null) hits = byBase[best.k];
        }
        if (hits == null && bstem.Length >= 7)
        {
            var m2 = PfxRx.Match(bstem);
            if (m2.Success) hits = byBase.GetValueOrDefault(m2.Groups[1].Value);
        }
        if (hits == null || hits.Count == 0) return null;
        if (prefix.Length > 0)
        {
            var pref = prefix.ToLowerInvariant().TrimEnd('/') + "/texture/";
            foreach (var h in hits)
                if (h.StartsWith(pref, StringComparison.OrdinalIgnoreCase)) return h;
        }
        foreach (var h in hits)
            if (!h.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)) return h;
        return hits[0];
    }

    // ---- matériau -----------------------------------------------------------

    /// <summary>Slot d'albedo : Base, sinon Detail, Dark, Decal 0 (comme convert.py).</summary>
    public static NifTexMap? AlbedoMap(NifTexturingProperty? t)
    {
        if (t == null) return null;
        return t.Base ?? t.Detail ?? t.Dark ?? (t.Decals.Length > 0 ? t.Decals[0] : null);
    }

    /// <summary>Nom de fichier de la source (File Name, sinon Name qui ressemble à une image).</summary>
    public static string SourceName(NifTexMap m)
    {
        var s = m.Source;
        if (s == null) return m.File ?? "";
        if (!string.IsNullOrEmpty(s.FileName)) return s.FileName;
        if (!string.IsNullOrEmpty(m.File)) return m.File!;
        var n = s.Name ?? "";
        return n.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
               n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ? n : "";
    }

    public sealed class Cache
    {
        public readonly Dictionary<(object?, object?, object?, object?), StandardMaterial3D> Mats = new();
        public int MissingTextures;
    }

    public static StandardMaterial3D For(NifShape sh, string texPrefix, Cache cache)
    {
        var key = ((object?)sh.Material, (object?)sh.Texturing, (object?)sh.Alpha, (object?)sh.Stencil);
        if (cache.Mats.TryGetValue(key, out var hit)) return hit;
        var m = new StandardMaterial3D { Roughness = 0.8f, Metallic = 0f };
        var mat = sh.Material;
        if (mat != null)
        {
            float C(float v) => Math.Clamp(v, 0f, 1f);
            // Variantes « N » (UE715N…) : diffuse = 0 et ambient = 1. Gamebryo calcule
            // ambient·lumièreAmbiante + diffuse·lumièresDirectes : un diffuse nul laisse
            // l'objet éclairé par l'ambiante seule (jamais noir). On prend donc l'ambient
            // quand le diffuse est nul, sinon la plante sortait en silhouette noire.
            var baseCol = mat.Diffuse.LengthSquared() < 0.0004f && mat.Ambient.LengthSquared() > 0.0004f
                ? mat.Ambient : mat.Diffuse;
            m.AlbedoColor = new Color(C(baseCol.X), C(baseCol.Y), C(baseCol.Z), C(mat.Alpha));
            var e = mat.Emissive;
            if (e.X > 0.001f || e.Y > 0.001f || e.Z > 0.001f)
            {
                m.EmissionEnabled = true;
                m.Emission = new Color(Math.Min(e.X, 1f), Math.Min(e.Y, 1f), Math.Min(e.Z, 1f));
            }
        }
        var map = AlbedoMap(sh.Texturing);
        if (map != null)
        {
            var srcName = SourceName(map);
            if (srcName.Length > 0) m.SetMeta("nif_tex", srcName);
            var rel = ResolveTexture(srcName, texPrefix);
            if (rel == null) cache.MissingTextures++;
            else
            {
                var tex = AssetCatalog.GetTexture(rel);
                if (tex != null) m.AlbedoTexture = tex;
                m.SetMeta("src_tex", Path.GetFileNameWithoutExtension(rel).ToLowerInvariant());
            }
        }
        if (sh.Alpha != null)
        {
            // Même sémantique que l'import glTF de Godot (convert.py : blend prioritaire, sinon test) :
            // BLEND -> ALPHA_DEPTH_PRE_PASS + depth_draw OPAQUE_ONLY (pas Alpha pur : le PropBatcher
            // rejette ce blend vrai, ToonMaterializer ne convertit que Alpha -> AlphaHash).
            if (sh.Alpha.Blend && sh.Alpha.DstBlend == 0)
            {
                // dst=ONE (flags 0xD/0x100D) : blend additif natif — sans ça le fond noir de la
                // texture reste opaque (rectangles noirs autour des glows/plantes lumineuses).
                m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                m.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            }
            else if (sh.Alpha.Blend)
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                m.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
            }
            else if (sh.Alpha.Test)
            {
                m.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                m.AlphaScissorThreshold = sh.Alpha.Threshold / 255f;
            }
        }
        if (sh.Stencil is { DrawMode: 3 }) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        cache.Mats[key] = m;
        return m;
    }
}
