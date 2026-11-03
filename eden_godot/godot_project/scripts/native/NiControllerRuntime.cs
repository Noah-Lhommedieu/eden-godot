using System.Numerics;
using EdenClient.Fx;
using EdenClient.World;
using Godot;
using GMatrix = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace EdenClient.Native;

internal sealed class ActionDriver : IFxDriver
{
    readonly Action<float, float> _f;
    public ActionDriver(Action<float, float> f) => _f = f;
    public void Update(float t, float dt) => _f(t, dt);
}

/// <summary>Contrôleurs de matériaux et de nœuds (F3) : UV, alpha, couleur, flipbook, visibilité,
/// transform, morph, billboards. Temps propre à chaque contrôleur (fréquence / phase / cycle) ou celui
/// de la NiControllerSequence embarquée qui le pilote.</summary>
internal static class NiControllerRuntime
{
    public static void Build(NativeFxHost h)
    {
        var nif = h.Nif; var rep = h.Report;
        NifControllerSequence? seq = null;
        if (nif.Sequences.Count > 0)
            seq = (h.Options.Sequence != null
                ? nif.Sequences.FirstOrDefault(s => s.Name.Equals(h.Options.Sequence, StringComparison.OrdinalIgnoreCase))
                : null) ?? nif.Sequences[0];

        foreach (var c in nif.Controllers)
        {
            if (c.Type.StartsWith("NiPSys") || c.Type == "NiMultiTargetTransformController") continue;
            var ip = c.Interpolator;
            var tm = CtlTiming.Of(c);
            if (seq != null && (ip == null || c.ManagerControlled))
            {
                var cb = seq.Blocks.FirstOrDefault(b => b.ControllerBlock == c.Block);
                if (cb?.Interpolator != null) { ip = cb.Interpolator; tm = CtlTiming.Of(seq); }
            }
            try { BuildOne(h, c, ip, tm); }
            catch (Exception e) { rep.Note($"driver-error:{c.Type}:{e.GetType().Name}"); }
        }

        if (h.Options.HandleBillboards)
            foreach (var n in nif.Nodes)
                if (n.Type == "NiBillboardNode" && h.NodeOf(n.Block) is { } node)
                { h.Add(new BillboardDriver(h, node, n)); rep.Count("billboard"); }
    }

    static void BuildOne(NativeFxHost h, NifController c, NifInterpolator? ip, CtlTiming tm)
    {
        var rep = h.Report;
        List<MatState> Mats() => h.MatsByProp.GetValueOrDefault(c.TargetBlock) ?? new();
        switch (c.Type)
        {
            case "NiTextureTransformController":
            {
                var ch = NifCurves.FloatOf(ip); var mats = Mats();
                if (ch == null || mats.Count == 0) { rep.Note("uv-unresolved"); return; }
                if (c.TexSlot != 0) { rep.Note("uv-slot" + c.TexSlot); return; }
                var curve = new FxCurve { Chan = ch, Timing = tm };
                uint op = c.Operation;
                h.Add(new ActionDriver((t, _) =>
                {
                    float v = curve.Eval(t);
                    foreach (var m in mats)
                    {
                        switch (op)
                        {
                            case 0: m.Tu = v; break;
                            case 1: m.Tv = v; break;
                            case 2: m.Rot = v; break;
                            case 3: m.Su = v; break;
                            case 4: m.Sv = v; break;
                        }
                        m.Dirty = true;
                    }
                }));
                rep.Count("uv");
                return;
            }
            case "NiAlphaController":
            {
                var ch = NifCurves.FloatOf(ip); var mats = Mats();
                if (ch == null || mats.Count == 0) { rep.Note("alpha-unresolved"); return; }
                var curve = new FxCurve { Chan = ch, Timing = tm };
                h.Add(new ActionDriver((t, _) =>
                {
                    float v = Math.Clamp(curve.Eval(t), 0f, 1f);
                    foreach (var m in mats) { m.Alpha = v; m.Dirty = true; }
                }));
                rep.Count("alpha");
                return;
            }
            case "NiMaterialColorController":
            {
                var ch = NifCurves.Vec3Of(ip); var mats = Mats();
                if (ch == null || mats.Count == 0) { rep.Note("color-unresolved"); return; }
                ushort which = c.TargetColor;
                if (which is 0 or 2) { rep.Note("color-ambient/specular-ignored"); return; }
                h.Add(new ActionDriver((t, _) =>
                {
                    var v = ch.Eval(tm.Local(t));
                    foreach (var m in mats)
                    {
                        if (which == 1) m.Diffuse = v; else m.Emissive = v;
                        m.Dirty = true;
                    }
                }));
                rep.Count("color");
                return;
            }
            case "NiFlipController":
            {
                var mats = Mats();
                var files = ExpandFlip(h, c);
                if (mats.Count == 0 || files.Count == 0) { rep.Note("flip-unresolved"); return; }
                var tex = new Texture2D?[files.Count];
                h.Add(new ActionDriver((t, _) =>
                {
                    float dur = tm.Duration;
                    int idx = dur <= 1e-6f ? 0 : Math.Clamp((int)((tm.Local(t) - tm.Start) / dur * files.Count), 0, files.Count - 1);
                    tex[idx] ??= LoadTex(h, files[idx]);
                    foreach (var m in mats) { m.Texture = tex[idx] ?? m.Texture; m.Dirty = true; }
                }));
                rep.Count("flip");
                return;
            }
            case "NiVisController":
            {
                var ch = NifCurves.FloatOf(ip); var node = h.NodeOf(c.TargetBlock);
                if (ch == null || node == null) { rep.Note("vis-unresolved"); return; }
                h.Add(new ActionDriver((t, _) => node.Visible = ch.Eval(tm.Local(t)) >= 0.5f));
                rep.Count("vis");
                return;
            }
            case "NiTransformController":
            {
                var node = h.NodeOf(c.TargetBlock);
                var av = h.Nif.AvByBlock.GetValueOrDefault(c.TargetBlock);
                var tr = NifCurves.Track(ip, av?.Name ?? "");
                if (node == null || av == null || tr == null) { rep.Note("xform-unresolved:" + (ip?.Type ?? "null")); return; }
                h.Add(new ActionDriver((t, _) =>
                {
                    var p = av.Translation; var r = av.Rotation; var s = av.Scale;
                    tr.Sample(tm.Local(t), ref p, ref r, ref s);
                    var m = GMatrix.CreateScale(s) * GMatrix.CreateFromQuaternion(r) * GMatrix.CreateTranslation(p);
                    node.Transform = NativeFx.ToTransform(m);
                }));
                rep.Count("xform");
                return;
            }
            case "NiGeomMorpherController":
                MorphDriver.TryBuild(h, c, tm);
                return;
            default:
                rep.Note("ctl-unhandled:" + c.Type);
                return;
        }
    }

    static Texture2D? LoadTex(NativeFxHost h, string file)
    {
        var loader = h.Options.TextureLoader ?? AssetCatalog.GetTexture;
        return loader(h.Options.TexPrefix + file) ?? loader(file);
    }

    /// <summary>Frames d'un flipbook : sources du contrôleur, une .ifl étant dépliée.</summary>
    static List<string> ExpandFlip(NativeFxHost h, NifController c)
    {
        var l = new List<string>();
        foreach (var f in c.FlipFiles)
        {
            if (string.IsNullOrEmpty(f)) continue;
            if (f.EndsWith(".ifl", StringComparison.OrdinalIgnoreCase))
            {
                try { l.AddRange(IflFile.Load(h.Options.TexPrefix + f).Frames); }
                catch (Exception e) when (e is IOException or FileNotFoundException)
                { h.Report.Note("ifl-missing:" + f); }
            }
            else l.Add(f);
        }
        return l;
    }
}

/// <summary>Morph (NiGeomMorpherController) : blend shapes ajoutées à la surface 0 de la géométrie.</summary>
internal static class MorphDriver
{
    public static void TryBuild(NativeFxHost h, NifController c, CtlTiming tm)
    {
        var rep = h.Report;
        var md = c.MorphData;
        if (md == null || md.Morphs.Length < 2 || h.NodeOf(c.TargetBlock) is not MeshInstance3D mi || mi.Mesh is not ArrayMesh src)
        { rep.MorphsSkipped++; rep.Note("morph-unresolved"); return; }
        var arrays = src.SurfaceGetArrays(0);
        var verts = (Godot.Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        int nm = md.Morphs.Length;
        if (md.Morphs[0].Vectors.Length != verts.Length)
        { rep.MorphsSkipped++; rep.Note("morph-vertex-mismatch"); return; }

        var mesh = new ArrayMesh { BlendShapeMode = Mesh.BlendShapeMode.Relative };
        for (int i = 1; i < nm; i++) mesh.AddBlendShape("m" + i);
        var shapes = new Godot.Collections.Array<Godot.Collections.Array>();
        var baseV = md.Morphs[0].Vectors;
        for (int i = 1; i < nm; i++)
        {
            var v = md.Morphs[i].Vectors;
            var d = new Godot.Vector3[verts.Length];
            for (int k = 0; k < d.Length && k < v.Length; k++)
            {
                var delta = md.Relative ? v[k] : v[k] - baseV[k];
                d[k] = new Godot.Vector3(delta.X, delta.Y, delta.Z);
            }
            var a = new Godot.Collections.Array(); a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = d;
            // le format des blend shapes doit égaler celui du tableau principal
            if (arrays[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
                a[(int)Mesh.ArrayType.Normal] = new Godot.Vector3[verts.Length];
            if (arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
                a[(int)Mesh.ArrayType.Tangent] = new float[verts.Length * 4];
            shapes.Add(a);
        }
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, shapes);
        mesh.SurfaceSetMaterial(0, src.SurfaceGetMaterial(0));
        mi.Mesh = mesh;

        var chans = new KfFloatChan?[nm];
        for (int i = 1; i < nm; i++)
            chans[i] = i < c.MorphInterpolatorList.Length ? NifCurves.FloatOf(c.MorphInterpolatorList[i]) : null;
        h.Add(new ActionDriver((t, _) =>
        {
            float lt = tm.Local(t);
            for (int i = 1; i < nm; i++)
                mi.SetBlendShapeValue(i - 1, chans[i]?.Eval(lt) ?? (i < c.MorphWeights.Length ? c.MorphWeights[i] : 0f));
        }));
        rep.MorphsBuilt++; rep.Count("morph");
    }
}

/// <summary>NiBillboardNode : +X local vers la caméra, +Z vers le haut (hypothèse Gamebryo, à valider).</summary>
internal sealed class BillboardDriver : IFxDriver
{
    readonly NativeFxHost _h; readonly Node3D _node; readonly int _mode;
    // Échelle locale d'ORIGINE : relire _node.Scale à chaque image y réinjecte le
    // pb.Inverse() déjà appliqué à la frame précédente → l'échelle se multiplie
    // (parent d'échelle ≠ 1) et le plan enfle jusqu'à couvrir l'écran.
    readonly Godot.Vector3 _baseScale;
    public BillboardDriver(NativeFxHost h, Node3D node, NifNode n) { _h = h; _node = node; _mode = n.BillboardMode & 7; _baseScale = node.Scale; }

    public void Update(float t, float dt)
    {
        if (!_node.IsInsideTree()) return;
        var cam = _node.GetViewport()?.GetCamera3D();
        if (cam == null) return;
        var rootBasis = _h.Root.GlobalTransform.Basis;
        // Racine/parent FX à échelle ~0 ou scale animé non fini :
        // Inverse() produit du NaN — les gardes intermédiaires ne
        // suffisent pas (NaN.Compare = false) → transform NaN. On
        // vérifie la finitude de la base FINALE avant l'assignation.
        if (Mathf.Abs(rootBasis.Determinant()) < 1e-12f) return;
        var rootInv = rootBasis.Inverse();
        var toCam = rootInv * (cam.GlobalPosition - _node.GlobalPosition);
        var up = rootInv * cam.GlobalTransform.Basis.Y;
        if (_mode == 1 || _mode == 5) { toCam.Z = 0; up = Godot.Vector3.Back; }
        if (!toCam.IsFinite() || !up.IsFinite() ||
            toCam.LengthSquared() < 1e-10f) return;
        var x = toCam.Normalized();
        var z = (up - x * up.Dot(x));
        if (z.LengthSquared() < 1e-10f) return;
        z = z.Normalized();
        var y = z.Cross(x);
        var want = new Godot.Basis(x, y, z);
        var parent = _node.GetParent() as Node3D;
        var pb = parent != null ? rootInv * parent.GlobalTransform.Basis : Godot.Basis.Identity;
        var basis = pb.Inverse() * want * Godot.Basis.FromScale(_baseScale);
        if (!basis.IsFinite()) return;
        _node.Basis = basis;
    }
}
