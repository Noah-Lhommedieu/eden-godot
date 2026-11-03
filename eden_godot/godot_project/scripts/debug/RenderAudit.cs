using System.Text;
using Godot;
using EdenClient.Native;

namespace EdenClient.Debugging;

/// <summary>Audit de RENDU des textures (≠ audit de résolution
/// DDS) : instancie chaque .nif par le même chemin que le jeu
/// (<see cref="AssetCatalog.LoadGlbNode"/> = NifScene + ToonMaterializer)
/// et compte, par mesh, si la texture que le NIF référence est
/// réellement posée sur le matériau et si le mesh est visible.
/// Args : --audit-dirs=map,char,monster,ride,item,decorate,house
/// --audit-limit=N --audit-out=chemin.tsv (+ .summary.tsv).
/// Classes d'écart (colonne « classe ») :
///   REF_NON_RESOLUE  : le NIF cite une texture, le résolveur ne la trouve pas
///   REF_DECODE_ECHEC : fichier trouvé mais GetTexture null
///   MASQUE_TEXTURE   : mesh texturé masqué par ToonMaterializer
///   VCOL_NOIR        : albedo posé mais COLOR_0 ≈ noir consommé (rendu noir)
///   ALPHA_PLAT       : mesh alpha dont la texture est posée mais sans alpha utile
///   SANS_TEXTURE_NIF : visible, le NIF ne cite aucune texture (info)
///   OK               : texture citée = texture posée, visible
/// Plus, par NIF, les slots cités mais non rendus (dark/detail/glow/decal…).</summary>
public partial class RenderAudit : Node
{
    sealed class Row
    {
        public string Cat = "", Nif = "", Mesh = "", Cls = "", Detail = "";
    }

    public override void _Ready()
    {
        var dirs = new List<string> { "map", "char", "monster", "ride", "item", "decorate", "house" };
        var limit = int.MaxValue;
        var outp = "/tmp/render-audit.tsv";
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--audit-dirs=")) dirs = a[13..].Split(',').ToList();
            else if (a.StartsWith("--audit-limit=")) limit = int.Parse(a[14..]);
            else if (a.StartsWith("--audit-out=")) outp = a[12..];
        }
        var root = AssetCatalog.RawAssetsRoot;
        var rows = new List<Row>();
        var cnt = new SortedDictionary<string, int>();
        var slotCnt = new SortedDictionary<string, int>();
        void Bump(SortedDictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
        foreach (var top in dirs)
        {
            var n = 0;
            var list = Vfs.EdenVfs.ListDir(Path.Combine(root, top, "model"), true)
                .Where(e => e.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e, StringComparer.Ordinal).ToList();
            foreach (var e in list)
            {
                if (n++ >= limit) break;
                var rel = Vfs.EdenVfs.IsEpk(e) ? Vfs.EdenVfs.EpkRel(e) : Path.GetRelativePath(root, e).Replace('\\', '/');
                var relNo = rel[..^4];
                if (relNo.Contains("/sky/", StringComparison.OrdinalIgnoreCase)) continue;
                try { AuditOne(top, relNo, e, rows, cnt, slotCnt, Bump); }
                catch (Exception ex) { GD.PrintErr($"[audit] {relNo} : {ex.Message}"); Bump(cnt, $"{top}\tERREUR"); }
            }
            GD.Print($"[audit] {top} : {n} nif");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outp))!);
        var sb = new StringBuilder("categorie\tnif\tmesh\tclasse\tdetail\n");
        foreach (var r in rows.Where(r => r.Cls != "OK").OrderBy(r => r.Cat).ThenBy(r => r.Cls).ThenBy(r => r.Nif))
            sb.Append($"{r.Cat}\t{r.Nif}\t{r.Mesh}\t{r.Cls}\t{r.Detail}\n");
        File.WriteAllText(outp, sb.ToString());
        var ss = new StringBuilder("categorie\tclasse\tmeshes\n");
        foreach (var (k, v) in cnt) ss.Append($"{k}\t{v}\n");
        foreach (var (k, v) in slotCnt) ss.Append($"{k}\t{v}\n");
        File.WriteAllText(Path.ChangeExtension(outp, null) + ".summary.tsv", ss.ToString());
        GD.Print("[audit] " + ss.ToString().Replace("\n", " | "));
        GetTree().Quit();
    }

    /// <summary><c>--black-scan</c> : parcourt la scène de jeu (MeshInstance3D
    /// et MultiMeshInstance3D visibles) et regroupe les matériaux dont
    /// l'albedo est quasi noir (texture ou couleur) — formes noires au sol.</summary>
    public static void BlackScan(Node root)
    {
        var groups = new SortedDictionary<string, (int n, string ex)>();
        foreach (var l in root.FindChildren("*", "DirectionalLight3D", true, false).Cast<DirectionalLight3D>())
            GD.Print($"[render-state] light {l.GetPath()} shadow={l.ShadowEnabled} energy={l.LightEnergy} mode={l.DirectionalShadowMode} maxDist={l.DirectionalShadowMaxDistance} bias={l.ShadowBias} normalBias={l.ShadowNormalBias} opacity={l.ShadowOpacity}");
        foreach (var we in root.FindChildren("*", "WorldEnvironment", true, false).Cast<WorldEnvironment>())
            if (we.Environment is { } e)
                GD.Print($"[render-state] env {we.GetPath()} ssao={e.SsaoEnabled} ssil={e.SsilEnabled} sdfgi={e.SdfgiEnabled} glow={e.GlowEnabled} amb={e.AmbientLightSource}:{e.AmbientLightColor}x{e.AmbientLightEnergy} tonemap={e.TonemapMode} exposure={e.TonemapExposure} fog={e.FogEnabled} adj={e.AdjustmentEnabled}");
        var dg = new SortedDictionary<string, (int n, string ex)>();
        foreach (var d in root.FindChildren("*", "Decal", true, false).Cast<Decal>())
        {
            var tex = d.TextureAlbedo;
            var k = $"size={d.Size} tex={(tex == null ? "AUCUNE" : $"{tex.GetWidth()}x{tex.GetHeight()} {tex.ResourcePath}")} mod={d.Modulate} emis={d.EmissionEnergy} cullMask={d.CullMask} vis={d.Visible}";
            dg.TryGetValue(k, out var g); dg[k] = (g.n + 1, g.ex ?? d.GetPath().ToString());
        }
        foreach (var kv in dg.OrderByDescending(x => x.Value.n).Take(10))
            GD.Print($"[decal] n={kv.Value.n} {kv.Key} ex={kv.Value.ex}");
        var types = new SortedDictionary<string, (int n, string ex)>();
        foreach (var v in root.FindChildren("*", "VisualInstance3D", true, false).Cast<VisualInstance3D>())
        {
            if (!v.IsVisibleInTree()) continue;
            var k = v.GetType().Name + (v is GpuParticles3D || v is CpuParticles3D ? "" : "");
            types.TryGetValue(k, out var g); types[k] = (g.n + 1, g.ex ?? v.GetPath().ToString());
        }
        foreach (var kv in types) GD.Print($"[types] {kv.Key} n={kv.Value.n} ex={kv.Value.ex}");
        var vp = root.GetViewport();
        GD.Print($"[render-state] viewport msaa3d={vp.Msaa3D} ssaa={vp.ScreenSpaceAA} scale3d={vp.Scaling3DScale} debanding={vp.UseDebanding} hdr2d={vp.UseHdr2D}");
        foreach (var n in root.FindChildren("*", "GeometryInstance3D", true, false).Cast<GeometryInstance3D>())
        {
            if (!n.IsVisibleInTree()) continue;
            var multi = n is MultiMeshInstance3D;
            Mesh? mesh = (n as MeshInstance3D)?.Mesh ?? (n as MultiMeshInstance3D)?.Multimesh?.Mesh;
            if (mesh == null) continue;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var mat = n.MaterialOverride ?? (n as MeshInstance3D)?.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i);
                var tex = Albedo(mat);
                var col = mat is StandardMaterial3D sm ? sm.AlbedoColor : Colors.White;
                var kind = mat?.GetType().Name ?? "null";
                var tr = mat is StandardMaterial3D st ? st.Transparency.ToString() : "-";
                string? why = null;
                if (tex != null && TexDark(tex)) why = "TEX_NOIRE";
                else if (col.Luminance < 0.08f && !(mat is StandardMaterial3D { Transparency: not BaseMaterial3D.TransparencyEnum.Disabled } ) ) why = tex == null ? "COULEUR_NOIRE" : "COULEUR_NOIRE_TEXTUREE";
                else if (System.Environment.GetEnvironmentVariable("SCAN_ALPHA") == "1" && tr != "Disabled" && tr != "-") why = "ALPHA";
                if (why == null) continue;
                var src = mat != null && mat.HasMeta("src_tex") ? mat.GetMeta("src_tex").AsString() : (tex?.ResourcePath ?? "-");
                var key = $"{why} {kind} tr={tr} multi={multi} tex={src}";
                groups.TryGetValue(key, out var g);
                groups[key] = (g.n + 1, g.ex ?? n.GetPath().ToString());
            }
        }
        foreach (var kv in groups.OrderByDescending(k => k.Value.n).Take(25))
            GD.Print($"[black-scan] n={kv.Value.n} {kv.Key} ex={kv.Value.ex}");
        GD.Print($"[black-scan] groupes={groups.Count}");
        root.GetViewport().GetTexture().GetImage().SavePng("/tmp/blackscan.png");
    }

    /// <summary><c>--pick-px=x,y</c> : liste les géométries visibles dont l'AABB
    /// recouvre ce pixel (du plus proche au plus lointain), avec leur matériau.</summary>
    public static void PickPixel(Node root, Vector2 px)
    {
        var cam = root.GetViewport().GetCamera3D();
        if (cam == null) { GD.Print("[pick] pas de caméra"); return; }
        var rows = new List<(float d, string s)>();
        foreach (var n in root.FindChildren("*", "GeometryInstance3D", true, false).Cast<GeometryInstance3D>())
        {
            if (!n.IsVisibleInTree()) continue;
            var aabb = n.GlobalTransform * n.GetAabb();
            var mn = new Vector2(float.MaxValue, float.MaxValue); var mx = new Vector2(float.MinValue, float.MinValue);
            var ok = true;
            for (var i = 0; i < 8; i++)
            {
                var c = aabb.GetEndpoint(i);
                if (cam.IsPositionBehind(c)) { ok = false; break; }
                var p = cam.UnprojectPosition(c);
                mn = new Vector2(Mathf.Min(mn.X, p.X), Mathf.Min(mn.Y, p.Y));
                mx = new Vector2(Mathf.Max(mx.X, p.X), Mathf.Max(mx.Y, p.Y));
            }
            if (!ok || px.X < mn.X || px.X > mx.X || px.Y < mn.Y || px.Y > mx.Y) continue;
            if (mx.X - mn.X > 700) continue; // grands méga-meshes (terrain, ciel)
            var mesh = (n as MeshInstance3D)?.Mesh ?? (n as MultiMeshInstance3D)?.Multimesh?.Mesh;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; mesh != null && i < mesh.GetSurfaceCount(); i++)
            {
                var mat = n.MaterialOverride ?? (n as MeshInstance3D)?.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i);
                var tex = Albedo(mat);
                sb.Append($"[{mat?.GetType().Name ?? "null"} tex={(mat != null && mat.HasMeta("src_tex") ? mat.GetMeta("src_tex") : tex?.ResourcePath ?? "-")}" +
                    (mat is StandardMaterial3D sm ? $" tr={sm.Transparency} vcol={sm.VertexColorUseAsAlbedo} col={sm.AlbedoColor} cull={sm.CullMode}" : "") + "]");
            }
            rows.Add((cam.GlobalPosition.DistanceTo(aabb.GetCenter()), $"{n.GetType().Name} {n.GetPath()} box={(mx - mn)} {sb}"));
        }
        foreach (var r in rows.OrderBy(r => r.d).Take(12)) GD.Print($"[pick] d={r.d:F1} {r.s}");
        GD.Print($"[pick] {rows.Count} candidats à {px}");
    }

    /// <summary>Géométries visibles dont l'AABB monde est énorme (> 25 u) ou non
    /// finie : candidats aux « bandes » plein écran (billboards FX déformés).</summary>
    public static void BigScan(Node root, string tag)
    {
        var n0 = 0;
        foreach (var g in root.FindChildren("*", "GeometryInstance3D", true, false).Cast<GeometryInstance3D>())
        {
            if (!g.IsVisibleInTree()) continue;
            var path = g.GetPath().ToString();
            if (!path.Contains("/Player/") && !path.Contains("/NetEntities/")) continue;
            var a = g.GlobalTransform * g.GetAabb();
            var sz = a.Size;
            var bad = !float.IsFinite(sz.X) || !float.IsFinite(sz.Y) || !float.IsFinite(sz.Z) || !float.IsFinite(a.Position.X);
            if (!bad && Mathf.Max(sz.X, Mathf.Max(sz.Y, sz.Z)) < 2.5f) continue;
            if (g is MultiMeshInstance3D) continue;
            n0++;
            if (n0 <= 6)
            {
                var mat = g.MaterialOverride ?? (g as MeshInstance3D)?.Mesh?.SurfaceGetMaterial(0);
                GD.Print($"[big-scan] {tag} size={sz} bad={bad} {g.GetType().Name} {path} mat={(mat as StandardMaterial3D)?.Transparency}/{(mat as StandardMaterial3D)?.BlendMode} tex={(mat as StandardMaterial3D)?.AlbedoTexture?.ResourcePath}");
            }
        }
        GD.Print($"[big-scan] {tag} total={n0}");
    }

    static Vector3? _rideLast;
    /// <summary>Direction de la monture (pelvis→tête) vs déplacement du joueur.</summary>
    public static void RideDir(Node root, string tag)
    {
        var player = root.GetNodeOrNull<Node3D>("Player");
        var mount = player?.GetNodeOrNull<Node3D>("Model/_mount");
        var sk = mount?.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>().FirstOrDefault();
        if (player == null || sk == null) { GD.Print($"[ride-dir] {tag} pas de monture"); return; }
        int Find(string sub) { for (var i = 0; i < sk.GetBoneCount(); i++) if (sk.GetBoneName(i).Contains(sub, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        var head = Find("Head"); var pel = Find("Pelvis");
        if (head < 0 || pel < 0) { GD.Print($"[ride-dir] {tag} bones absents: " + string.Join(",", Enumerable.Range(0, Math.Min(25, sk.GetBoneCount())).Select(sk.GetBoneName))); return; }
        var h = sk.GlobalTransform * sk.GetBoneGlobalPose(head).Origin;
        var p = sk.GlobalTransform * sk.GetBoneGlobalPose(pel).Origin;
        var face = (h - p); face.Y = 0; face = face.Normalized();
        var pos = player.GlobalPosition;
        string mv = "n/a";
        if (_rideLast is { } last)
        {
            var d = pos - last; d.Y = 0;
            if (d.Length() > 0.05f) mv = $"{(face.Dot(d.Normalized())):F2}";
        }
        _rideLast = pos;
        GD.Print($"[ride-dir] {tag} pos={pos} facing=({face.X:F2},{face.Z:F2}) cos(facing,deplacement)={mv}");
    }

    /// <summary>Liste tous les matériaux sous /Player/ (monture, FX attachés).</summary>
    public static void PlayerMats(Node root)
    {
        foreach (var g in root.FindChildren("*", "GeometryInstance3D", true, false).Cast<GeometryInstance3D>())
        {
            var path = g.GetPath().ToString();
            if (!path.Contains("/Player/") || !g.IsVisibleInTree()) continue;
            var mesh = (g as MeshInstance3D)?.Mesh;
            for (var i = 0; mesh != null && i < mesh.GetSurfaceCount(); i++)
            {
                var mat = g.MaterialOverride ?? (g as MeshInstance3D)?.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i);
                var sm = mat as StandardMaterial3D;
                GD.Print($"[player-mats] {g.Name} ({g.GetType().Name}) {path[(path.IndexOf("/Player/") + 8)..]} | {mat?.GetType().Name} tr={sm?.Transparency} blend={sm?.BlendMode} col={sm?.AlbedoColor} tex={(mat != null && mat.HasMeta("src_tex") ? mat.GetMeta("src_tex") : (sm?.AlbedoTexture?.ResourcePath ?? ""))} unshaded={sm?.ShadingMode}");
            }
        }
    }

    static Texture2D? Albedo(Material? m) => m switch
    {
        StandardMaterial3D sm => sm.AlbedoTexture,
        ShaderMaterial shm => shm.GetShaderParameter("albedo_tex").Obj as Texture2D,
        _ => null,
    };

    static readonly Dictionary<ulong, bool> _dark = new();

    /// <summary>Texture dont les pixels opaques (alpha &gt; 0.5) sont
    /// quasi noirs (moyenne &lt; 0.06) → rendu en silhouette noire.</summary>
    static bool TexDark(Texture2D tex)
    {
        var id = tex.GetInstanceId();
        if (_dark.TryGetValue(id, out var d)) return d;
        var img = tex.GetImage();
        d = false;
        if (img != null)
        {
            img = (Image)img.Duplicate();
            if (img.IsCompressed()) img.Decompress();
            img.Convert(Image.Format.Rgba8);
            var b = img.GetData();
            double sum = 0; long n = 0;
            for (var i = 0; i < b.Length; i += 16)
                if (b[i + 3] > 127) { sum += b[i] + b[i + 1] + b[i + 2]; n++; }
            d = n > 0 && sum / n / 3 / 255 < 0.06;
        }
        return _dark[id] = d;
    }

    static bool VisibleInModel(Node n)
    {
        for (Node? p = n; p != null; p = p.GetParent())
            if (p is Node3D n3 && !n3.Visible) return false;
        return true;
    }

    void AuditOne(string top, string relNo, string e, List<Row> rows,
        SortedDictionary<string, int> cnt, SortedDictionary<string, int> slotCnt,
        Action<SortedDictionary<string, int>, string> bump)
    {
        if (!AssetCatalog.TryGetGlb(relNo, out var glb)) return;
        var node = AssetCatalog.LoadGlbNode(glb);
        if (node == null) { bump(cnt, $"{top}\tNON_CHARGE"); return; }
        foreach (var mi in node.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            var visible = VisibleInModel(mi);
            var nifHidden = false;
            for (Node? p = mi; p != null; p = p.GetParent()) if (p.HasMeta("nif_hidden")) nifHidden = true;
            var cls = "OK";
            var detail = "";
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);
                var tex = Albedo(mat);
                var refName = mat?.HasMeta("nif_tex") == true ? mat.GetMeta("nif_tex").AsString() : "";
                var resolved = mat?.HasMeta("src_tex") == true ? mat.GetMeta("src_tex").AsString() : "";
                string c;
                if (mi.HasMeta("toon_hidden") && !nifHidden && refName.Length > 0)
                { c = "MASQUE_TEXTURE"; detail = $"{mi.GetMeta("toon_hidden")} tex={refName}"; }
                else if (refName.Length == 0) c = visible ? "SANS_TEXTURE_NIF" : "OK";
                else if (resolved.Length == 0) { c = "REF_NON_RESOLUE"; detail = refName; }
                else if (tex == null) { c = "REF_DECODE_ECHEC"; detail = resolved; }
                else if (visible && VcolBlack(mi, i, mat)) { c = "VCOL_NOIR"; detail = resolved; }
                else if (visible && !(mat is StandardMaterial3D { BlendMode: BaseMaterial3D.BlendModeEnum.Add })
                         && !(top == "char" && resolved.EndsWith("b", StringComparison.OrdinalIgnoreCase))
                         && TexDark(tex)) { c = "TEX_NOIRE"; detail = resolved; }
                else c = "OK";
                if (c != "OK" && (cls == "OK" || c.CompareTo(cls) < 0)) cls = c;
            }
            // Un mesh masqué par le NIF lui-même ou sans texture voulue n'est pas un écart.
            if (!visible && cls != "MASQUE_TEXTURE" && cls != "REF_NON_RESOLUE" && cls != "REF_DECODE_ECHEC") cls = "OK";
            bump(cnt, $"{top}\t{cls}");
            rows.Add(new Row { Cat = top, Nif = relNo, Mesh = mi.Name, Cls = cls, Detail = detail });
        }
        node.QueueFree();
        // Slots cités par le NIF mais non rendus (le matériau n'en prend qu'un).
        var nif = AssetCatalog.GetNifCached(e);
        if (nif != null)
            foreach (var s in nif.Shapes)
            {
                var t = s.Texturing;
                if (s.Colors is { Length: > 0 } cs)
                {
                    var mx = 0f;
                    foreach (var c in cs) mx = Math.Max(mx, Math.Max(c.X, Math.Max(c.Y, c.Z)));
                    if (mx < 0.35f)
                    {
                        var mode = s.VertexColor == null ? "absent" : $"src{s.VertexColor.SourceMode}";
                        bump(slotCnt, $"{top}\t[vcol sombre <0.35] prop={mode} texture={(t?.Base != null ? "oui" : "non")}");
                        rows.Add(new Row { Cat = top, Nif = relNo, Mesh = s.Name, Cls = "VCOL_SOMBRE_NIF", Detail = $"prop={mode} max={mx:F2} tex={s.TextureFile}" });
                    }
                }
                if (t == null) continue;
                if (t.Dark != null) bump(slotCnt, $"{top}\t[slot non rendu] dark");
                if (t.Detail != null && t.Base != null) bump(slotCnt, $"{top}\t[slot non rendu] detail");
                if (t.Glow != null) bump(slotCnt, $"{top}\t[slot non rendu] glow");
                if (t.Decals.Length > 0 && t.Base != null) bump(slotCnt, $"{top}\t[slot non rendu] decal");
                if (t.Bump != null || t.Normal != null) bump(slotCnt, $"{top}\t[slot non rendu] bump/normal");
                if (t.Gloss != null) bump(slotCnt, $"{top}\t[slot non rendu] gloss");
                if (t.ShaderMaps.Any(m => m != null)) bump(slotCnt, $"{top}\t[slot non rendu] shader-map");
            }
    }

    /// <summary>Albedo posé mais le matériau multiplie par COLOR_0
    /// quasi noir → le rendu est noir.</summary>
    static bool VcolBlack(MeshInstance3D mi, int surf, Material? mat)
    {
        var consumes = mat is ShaderMaterial ||
                       mat is StandardMaterial3D { VertexColorUseAsAlbedo: true };
        if (!consumes) return false;
        var arr = mi.Mesh!.SurfaceGetArrays(surf)[(int)Mesh.ArrayType.Color];
        if (arr.VariantType != Variant.Type.PackedColorArray) return false;
        var cols = arr.AsColorArray();
        if (cols.Length == 0) return false;
        var max = 0f;
        foreach (var c in cols) max = Mathf.Max(max, Mathf.Max(c.R, Mathf.Max(c.G, c.B)));
        return max <= 0.08f;
    }
}
