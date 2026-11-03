using EdenClient.Fx;
using EdenClient.World;
using Godot;
using Vector3 = System.Numerics.Vector3;

namespace EdenClient.Native;

/// <summary>Un NiParticleSystem : simulation CPU déterministe (<see cref="ParticleSim"/>) rendue en
/// MultiMesh de billboards avec le matériau NIF (additif / mélange / test alpha).</summary>
internal sealed class NiParticleRuntime : IFxDriver
{
    readonly NativeFxHost _host;
    readonly NifParticleSystem _ps;
    readonly ParticleSim _sim;
    readonly ParticleRenderData _rd = new();
    readonly Node3D _node;
    readonly MultiMeshInstance3D _mmi;
    readonly MultiMesh _mm;
    readonly float[] _buf;
    readonly bool _world, _sort;
    Transform3D _frame, _frameInv;
    bool _frameSet;

    public int Count => _sim.Count;
    public ParticleSim Sim => _sim;

    NiParticleRuntime(NativeFxHost host, NifParticleSystem ps, NifParticleMapper.Result map,
        Node3D node, MultiMeshInstance3D mmi, MultiMesh mm, bool sort)
    {
        _host = host; _ps = ps; _node = node; _mmi = mmi; _mm = mm; _sort = sort;
        _world = !map.Def.LocalSpace;
        _sim = new ParticleSim(map.Def, host.Options.Seed + (uint)ps.Block * 7919u);
        _buf = new float[Math.Max(1, map.Def.MaxCount) * 16];
    }

    public static NiParticleRuntime? Create(NativeFxHost host, NifParticleSystem ps)
    {
        var map = NifParticleMapper.Map(host.Nif, ps);
        foreach (var n in map.Notes) host.Report.Note(n);
        if (map.Def.Curves.Count == 0 && ps.Of<NifPSysEmitter>().FirstOrDefault() == null) return null;

        var node = host.NodeOf(ps.Block);
        if (node == null)
        {
            node = new Node3D { Name = string.IsNullOrEmpty(ps.Name) ? "PSys" : ps.Name };
            var parent = host.NodeOf(ps.ParentBlock) ?? host.Root;
            parent.AddChild(node);
            node.Transform = NativeFx.ToTransform(ps.BindLocal);
            node.SetMeta("nif_block", ps.Block);
            host.Nodes[ps.Block] = node;
        }
        // Les géométries émettrices ne se dessinent pas.
        foreach (var e in ps.Of<NifPSysEmitter>())
            foreach (var mb in e.MeshBlocks)
                if (host.NodeOf(mb) is { } mn) mn.Visible = false;

        var nm = ps.Properties.OfType<EdenClient.World.NifMaterial>().FirstOrDefault();
        var tex = ps.Properties.OfType<NifTexturingProperty>().FirstOrDefault();
        var al = ps.Properties.OfType<NifAlphaProperty>().FirstOrDefault();
        var zb = ps.Properties.OfType<NifZBufferProperty>().FirstOrDefault();
        var (mat, st) = NativeFx.MakeMaterial(nm, tex, al, zb, true, host.Options, true);
        host.AllMats.Add(st);
        if (System.Environment.GetEnvironmentVariable("EDEN_FX_DIAG") == "1") { Diag(ps, tex, al, st); GD.Print("[psdiag] warn " + string.Join(" | ", host.Nif.Warnings.Where(w => w.Contains("ixel") || w.Contains("21")))); }
        foreach (var p in ps.Properties) host.RegisterMat(p.Block, st);

        var quad = new QuadMesh { Size = new Godot.Vector2(2, 2), Material = mat };
        int max = Math.Max(1, map.Def.MaxCount);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = quad, InstanceCount = max, VisibleInstanceCount = 0,
            CustomAabb = new Aabb(new Godot.Vector3(-200, -200, -200), new Godot.Vector3(400, 400, 400)),
        };
        var mmi = new MultiMeshInstance3D
        {
            Name = "Particles", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = !map.Def.LocalSpace,
        };
        node.AddChild(mmi);
        return new NiParticleRuntime(host, ps, map, node, mmi, mm, NativeFx.BlendMode(al) == 2);
    }

    static void Diag(NifParticleSystem ps, NifTexturingProperty? tex, NifAlphaProperty? al, MatState st)
    {
        string a = "notex";
        if (st.Texture != null)
        {
            var img = st.Texture.GetImage();
            if (img != null)
            {
                if (img.IsCompressed()) img.Decompress();
                img.Convert(Image.Format.Rgba8);
                var d = img.GetData(); int amin = 255, amax = 0, bright = 0, n = d.Length / 4;
                for (int i = 0; i < n; i++) { amin = Math.Min(amin, d[i * 4 + 3]); amax = Math.Max(amax, d[i * 4 + 3]); }
                a = $"{img.GetWidth()}x{img.GetHeight()} a={amin}..{amax} mips={img.HasMipmaps()}";
            }
        }
        GD.Print($"[psdiag] {ps.Name} blend={NativeFx.BlendMode(al)} alpha={(al == null ? "none" : $"b{al.Blend} t{al.Test} src{al.SrcBlend} dst{al.DstBlend}")} tex={tex?.Base?.File} {a} texprop={(tex == null ? "null" : tex.Base == null ? "nobase" : $"src={tex.Base.SourceBlock} ext={tex.Base.Source?.External} px={tex.Base.Source?.Pixels != null} pdb={tex.Base.Source?.PixelDataBlock} fmt={tex.Base.Source?.Pixels?.Format}")} props={string.Join(",", ps.Properties.Select(q => q.Type))}");
    }

    public void Reset() { _sim.Reset(); _frameSet = false; }

    public void SetVisible(bool v) => _mmi.Visible = v;

    public void Update(float t, float dt)
    {
        if (_world)
        {
            if (!_frameSet)
            {
                _frame = _host.Root.IsInsideTree() ? _host.Root.GlobalTransform : _host.Root.Transform;
                _frameInv = _frame.AffineInverse();
                _frameSet = true;
                _mmi.GlobalTransform = _frame;
            }
            var nodeG = _node.IsInsideTree() ? _node.GlobalTransform : _node.Transform;
            _sim.EmitterTransform = NativeFx.ToMatrix(_frameInv * nodeG);
        }
        _sim.Step(dt);
        FillBuffers();
    }

    void FillBuffers()
    {
        var cam = _mmi.IsInsideTree() ? _mmi.GetViewport()?.GetCamera3D() : null;
        var xf = _mmi.IsInsideTree() ? _mmi.GlobalTransform : _mmi.Transform;
        var inv = xf.AffineInverse();
        Vector3 right = Vector3.UnitX, up = Vector3.UnitY, fwd = -Vector3.UnitZ, pos = Vector3.Zero;
        if (cam != null)
        {
            var cb = cam.GlobalTransform.Basis;
            right = N(inv.Basis * cb.X); up = N(inv.Basis * cb.Y); fwd = N(inv.Basis * -cb.Z);
            var p = inv * cam.GlobalPosition; pos = new Vector3(p.X, p.Y, p.Z);
        }
        _rd.Fill(_sim, new BillboardView(right, up, fwd, pos), _sort);
        int n = _rd.InstanceCount;
        for (int k = 0; k < n; k++)
        {
            Array.Copy(_rd.Transforms, k * 12, _buf, k * 16, 12);
            Array.Copy(_rd.Colors, k * 4, _buf, k * 16 + 12, 4);
        }
        _mm.VisibleInstanceCount = n;
        if (n > 0) RenderingServer.MultimeshSetBuffer(_mm.GetRid(), _buf);
    }

    static Vector3 N(Godot.Vector3 v) { v = v.Normalized(); return new Vector3(v.X, v.Y, v.Z); }
}
