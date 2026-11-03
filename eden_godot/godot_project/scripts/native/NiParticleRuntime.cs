using EdenClient.Fx;
using EdenClient.World;
using Godot;
using Matrix4x4 = System.Numerics.Matrix4x4;
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
    // Émetteur mesh skinné : re-posé par frame depuis les Skeleton3D
    // (r014/r253 — sinon les particules restent en pose de bind).
    readonly EmitterSkin? _skin;
    readonly SkinBind[]? _bind;
    readonly Matrix4x4[]?[]? _bw;

    /// <summary>Skeleton3D + indices d'os résolus pour un sous-mesh skinné.</summary>
    sealed class SkinBind
    {
        public Skeleton3D Sk = null!;
        /// <summary>Index d'os dans <see cref="Sk"/>, parallèle à Part.BoneSkin (−1 = non trouvé).</summary>
        public int[] Bones = Array.Empty<int>();
        /// <summary>BindWorld NIF par os : repli quand Bones[k] < 0 (sommet gardé en bind).</summary>
        public Matrix4x4[] Fallback = Array.Empty<Matrix4x4>();
    }

    /// <summary>EDEN_FX_SKIN=0 : émetteurs cuits en bind (comportement avant fix, preuve A/B).</summary>
    static readonly bool s_skinOn =
        System.Environment.GetEnvironmentVariable("EDEN_FX_SKIN") != "0";

    public int Count => _sim.Count;
    public ParticleSim Sim => _sim;

    NiParticleRuntime(NativeFxHost host, NifParticleSystem ps, NifParticleMapper.Result map,
        Node3D node, MultiMeshInstance3D mmi, MultiMesh mm, bool sort,
        EmitterSkin? skin, SkinBind[]? bind, Matrix4x4[]?[]? bw)
    {
        _host = host; _ps = ps; _node = node; _mmi = mmi; _mm = mm; _sort = sort;
        _skin = skin; _bind = bind; _bw = bw;
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
        if (System.Environment.GetEnvironmentVariable("EDEN_FX_DIAG") == "1")
        {
            Diag(ps, tex, al, st);
            var ws = host.Nif.Warnings.Where(w => w.Contains("ixel") || w.Contains("21")).ToList();
            if (ws.Count > 0) GD.Print("[psdiag] warn " + string.Join(" | ", ws));
        }
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
        var (skin, bind, bw) = BindSkin(host, ps, map);
        return new NiParticleRuntime(host, ps, map, node, mmi, mm, NativeFx.BlendMode(al) == 2,
            skin, bind, bw);
    }

    /// <summary>Résout les sous-meshes d'émetteur skinnés vers le Skeleton3D de l'arbre.
    /// La Skin du MeshInstance3D caché énumère les os dans l'ordre de Part.BoneBlocks
    /// (binds posés par NifScene) — exact même sous des noms NIF dupliqués ; repli :
    /// os par nom dans les Skeleton3D de la racine.</summary>
    static (EmitterSkin? Skin, SkinBind[]? Bind, Matrix4x4[]?[]? Bw) BindSkin(
        NativeFxHost host, NifParticleSystem ps, NifParticleMapper.Result map)
    {
        if (!s_skinOn || map.Skin is not { } es || es.Parts.Count == 0 ||
            map.Def.Emitter.MeshPoints == null)
            return (null, null, null);
        var bind = new SkinBind[es.Parts.Count];
        var bw = new Matrix4x4[es.Parts.Count][];
        var invPs = Matrix4x4.Invert(ps.BindWorld, out var ips) ? ips : Matrix4x4.Identity;
        var verts = 0;
        for (var i = 0; i < es.Parts.Count; i++)
        {
            var b = ResolveSkin(host, es.Parts[i], invPs);
            if (b == null) { host.Report.Note("mesh-emitter-skin-unresolved"); continue; }
            bind[i] = b;
            bw[i] = new Matrix4x4[b.Bones.Length];
            verts += es.Parts[i].LocalVerts.Length;
        }
        if (verts == 0) return (null, null, null);
        if (System.Environment.GetEnvironmentVariable("EDEN_FX_DIAG") == "1")
            GD.Print($"[psdiag] émetteur skinné : {verts} sommets, {bind.Count(x => x != null)}/{bind.Length} sous-meshes");
        return (es, bind, bw);
    }

    static SkinBind? ResolveSkin(NativeFxHost host, EmitterSkin.Part p, Matrix4x4 invPs)
    {
        Skeleton3D? sk = null;
        int[]? bones = null;
        if (host.NodeOf(p.MeshBlock) is MeshInstance3D mi && mi.Skin is { } res &&
            ((sk = mi.GetParent() as Skeleton3D) != null ||
             (sk = mi.GetNodeOrNull(mi.Skeleton) as Skeleton3D) != null))
        {
            bones = new int[p.BoneBlocks.Length];
            var nb = res.GetBindCount();
            for (var k = 0; k < bones.Length; k++)
                bones[k] = k < nb ? sk!.FindBone(res.GetBindName(k)) : -1;
        }
        if (bones == null || bones.All(b => b < 0))
        {
            bones = new int[p.BoneBlocks.Length];
            foreach (var s in host.Root.FindChildren("*", "Skeleton3D", true, false).Cast<Skeleton3D>())
            {
                var any = false;
                for (var k = 0; k < p.BoneBlocks.Length; k++)
                {
                    var nm = host.Nif.AvByBlock.TryGetValue(p.BoneBlocks[k], out var av) ? av.Name : "";
                    bones[k] = nm.Length > 0 ? s.FindBone(nm) : -1;
                    any |= bones[k] >= 0;
                }
                if (any) { sk = s; break; }
            }
        }
        if (sk == null || bones.All(b => b < 0)) return null;
        // Os non résolu : bw = BindWorld × inv(ps.BindWorld) — le sommet
        // garde exactement la pose de bind cuite au mapping (chemin historique).
        var fb = new Matrix4x4[p.BoneBlocks.Length];
        for (var k = 0; k < fb.Length; k++)
            fb[k] = (host.Nif.AvByBlock.TryGetValue(p.BoneBlocks[k], out var av)
                ? av.BindWorld : Matrix4x4.Identity) * invPs;
        return new SkinBind { Sk = sk, Bones = bones, Fallback = fb };
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
                var d = img.GetData(); int amin = 255, amax = 0, n = d.Length / 4;
                for (int i = 0; i < n; i++) { amin = Math.Min(amin, d[i * 4 + 3]); amax = Math.Max(amax, d[i * 4 + 3]); }
                a = $"{img.GetWidth()}x{img.GetHeight()} a={amin}..{amax} mips={img.HasMipmaps()}";
            }
        }
        else if (tex?.Base?.File != null) a = "file-unresolved";
        else if (tex?.Base != null) a = "src-unresolved";
        var em = ps.Of<NifPSysEmitter>().FirstOrDefault();
        var nm = ps.Properties.OfType<EdenClient.World.NifMaterial>().FirstOrDefault();
        var emit = em == null ? "no-emitter" :
            $"shape={em.Shape} ir={em.InitialRadius:0.###}±{em.RadiusVariation:0.###} spd={em.Speed:0.##}±{em.SpeedVariation:0.##} " +
            $"life={em.LifeSpan:0.##}±{em.LifeSpanVariation:0.##} col={em.InitialColor.X:0.##},{em.InitialColor.Y:0.##},{em.InitialColor.Z:0.##},{em.InitialColor.W:0.##} " +
            $"meshes={em.MeshBlocks.Length} veltype={em.VelocityType} emitfrom={em.EmitFrom}";
        var nmat = nm == null ? "nomat" :
            $"dif={nm.Diffuse.X:0.##},{nm.Diffuse.Y:0.##},{nm.Diffuse.Z:0.##} emi={nm.Emissive.X:0.##},{nm.Emissive.Y:0.##},{nm.Emissive.Z:0.##} a={nm.Alpha:0.##}";
        GD.Print($"[psdiag] {ps.Name} blend={NativeFx.BlendMode(al)} alpha={(al == null ? "none" : $"b{al.Blend} t{al.Test} src{al.SrcBlend} dst{al.DstBlend}")} ws={ps.WorldSpace} max={ps.Data?.MaxParticles} {emit} {nmat} clamp={tex?.Base?.ClampMode.ToString() ?? "-"} tex={tex?.Base?.File} {a} texprop={(tex == null ? "null" : tex.Base == null ? "nobase" : $"src={tex.Base.SourceBlock} ext={tex.Base.Source?.External} px={tex.Base.Source?.Pixels != null} pdb={tex.Base.Source?.PixelDataBlock} fmt={tex.Base.Source?.Pixels?.Format}")} props={string.Join(",", ps.Properties.Select(q => q.Type))} mods={string.Join(",", ps.Modifiers.Select(m => m.Type))}");
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
        Reskin();
        _sim.Step(dt);
        FillBuffers();
    }

    /// <summary>Re-pose les sous-meshes d'émetteur skinnés dans l'espace du
    /// nœud psys : v = lv × BoneSkin × monde(os) × inv(monde psys) — les
    /// naissances suivent la pose animée (flammes aux pattes/queue r014/r253).
    /// bw[k] = Rm(monde psys⁻¹ × monde os) ; en ligne : BoneSkin × bw.</summary>
    void Reskin()
    {
        if (_skin == null || _bind == null || _bw == null) return;
        var e = _sim.Def.Emitter;
        if (e.MeshPoints is not { } pts) return;
        var nodeG = _node.IsInsideTree() ? _node.GlobalTransform : _node.Transform;
        var inv = NativeFx.ToMatrix(nodeG.AffineInverse());
        for (var i = 0; i < _bind.Length; i++)
        {
            var b = _bind[i];
            if (b == null) { _bw[i] = null; continue; }
            var bw = _bw[i]!;
            var sg = b.Sk.IsInsideTree() ? b.Sk.GlobalTransform : b.Sk.Transform;
            for (var k = 0; k < b.Bones.Length; k++)
            {
                var bi = b.Bones[k];
                bw[k] = bi >= 0
                    ? NativeFx.ToMatrix(sg * b.Sk.GetBoneGlobalPose(bi)) * inv
                    : b.Fallback[k];
            }
        }
        _skin.Reskin(_bw, pts, e.MeshNormals);
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
