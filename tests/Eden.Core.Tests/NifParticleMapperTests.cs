using System.Numerics;
using EdenClient.Fx;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>NifFile -> ParticleSystemDef (F1/F2) et courbes de contrôleurs.</summary>
public class NifParticleMapperTests
{
    static string? Nif(string rel)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "corpus", "fr-full", rel);
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    [Fact]
    public void MeshEmitterEffect_MapsAndEmits()
    {
        var path = Nif("effect/model/s10121.nif");
        if (path == null) return; // corpus absent
        var nif = NifFile.Load(path);
        var ps = Assert.Single(nif.ParticleSystems);
        var m = NifParticleMapper.Map(nif, ps);
        Assert.True(m.WorldSpace);
        Assert.Equal(EmitterShape.Mesh, m.Def.Emitter.Shape);
        Assert.NotNull(m.Def.Emitter.MeshTriangles);
        Assert.Equal(600f, m.Def.Curves[PsParam.BirthRate].Eval(0.1f)); // NiPSysEmitterCtlr : taux constant
        var sim = new ParticleSim(m.Def, 1);
        for (int i = 0; i < 20; i++) sim.Step(1f / 30f);
        Assert.InRange(sim.Count, 1, m.Def.MaxCount);
    }

    [Fact]
    public void Reskin_TwoBoneBlend_WeightsApplied()
    {
        var skin = new EmitterSkin();
        skin.Parts.Add(new EmitterSkin.Part
        {
            MeshBlock = 7, BaseVertex = 0,
            BoneBlocks = [10, 11],
            BoneSkin = [Matrix4x4.Identity, Matrix4x4.Identity],
            Joints = [0, 1, 0, 0, 0, 1, 0, 0],
            Weights = [0.5f, 0.5f, 0, 0, 0.25f, 0.75f, 0, 0],
            LocalVerts = [Vector3.Zero, new Vector3(4, 0, 0)],
            LocalNormals = [Vector3.UnitZ, Vector3.UnitZ],
        });
        var pts = new Vector3[2];
        var nrm = new Vector3[2];
        // os0 immobile, os1 déplacé de +10 en Y (espace des points).
        Matrix4x4[]?[] bw = [new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 10, 0) }];
        skin.Reskin(bw, pts, nrm);
        Assert.Equal(new Vector3(0, 5, 0), pts[0]);
        Assert.Equal(new Vector3(4, 7.5f, 0), pts[1]);
        // os1 pivoté de 90° autour de X : la normale suit (Z → ±Y).
        Matrix4x4[]?[] bwRot = [new[]
        {
            Matrix4x4.Identity,
            Matrix4x4.CreateRotationX(MathF.PI / 2f) * Matrix4x4.CreateTranslation(0, 10, 0),
        }];
        skin.Reskin(bwRot, pts, nrm);
        Assert.True(MathF.Abs(nrm[1].Y) > 0.7f, $"normale non posée : {nrm[1]}");
        Assert.Equal(0f, nrm[1].X, 4);
        Assert.True(nrm[1].LengthSquared() > 0.99f);
    }

    [Fact]
    public void Reskin_NullPart_KeepsBakedVerts()
    {
        var skin = new EmitterSkin();
        skin.Parts.Add(new EmitterSkin.Part
        {
            MeshBlock = 3, BaseVertex = 1,
            BoneBlocks = [9], BoneSkin = [Matrix4x4.Identity],
            Joints = [0, 0, 0, 0], Weights = [1f, 0, 0, 0],
            LocalVerts = [new Vector3(1, 2, 3)],
        });
        var pts = new[] { new Vector3(9, 9, 9), new Vector3(1, 2, 3) };
        Matrix4x4[]?[] bw = [null];            // sous-mesh non résolu
        skin.Reskin(bw, pts, null);
        Assert.Equal(new Vector3(9, 9, 9), pts[0]);
        Assert.Equal(new Vector3(1, 2, 3), pts[1]);
    }

    [Theory]
    [InlineData("ride/model/r253.nif")]
    [InlineData("ride/model/r014.nif")]
    public void MountEmitter_SkinnedParts_BindPoseMatchesBaked(string rel)
    {
        var path = Nif(rel);
        if (path == null) return; // corpus absent
        var nif = NifFile.Load(path);
        var skinned = 0;
        foreach (var ps in nif.ParticleSystems)
        {
            var m = NifParticleMapper.Map(nif, ps);
            if (m.Skin is not { } es) continue;
            skinned++;
            var invPs = Matrix4x4.Invert(ps.BindWorld, out var ip) ? ip : Matrix4x4.Identity;
            // Pose de bind : monde(os) = BindWorld, exprimé dans l'espace
            // des points → bw = BindWorld × inv(ps.BindWorld). Le re-skin
            // doit retomber sur les sommets cuits (pose de bind).
            var bw = es.Parts.Select(p => p.BoneBlocks.Select(b =>
                    (nif.AvByBlock.TryGetValue(b, out var av) ? av.BindWorld : Matrix4x4.Identity) * invPs)
                    .ToArray()).ToArray();
            var pts = (Vector3[])m.Def.Emitter.MeshPoints!.Clone();
            var orig = pts.ToArray();
            m.Skin.Reskin(bw, pts, m.Def.Emitter.MeshNormals);
            float maxErr = 0;
            for (var v = 0; v < pts.Length; v++)
                maxErr = Math.Max(maxErr, Vector3.Distance(pts[v], orig[v]));
            Console.WriteLine($"[skin-bind] {rel} ps#{ps.Block} écart max {maxErr:F4} m");
            // Résidu attendu : le transform global NiSkinData (ignoré au
            // parse) diffère du monde du géom de quelques mm (arrondis
            // d'authoring) — des ordres de grandeur sous l'écart
            // bind→posé mesuré (0,24–2,2 m). < 2 cm : formule correcte.
            Assert.True(maxErr < 2e-2f, $"{rel} ps#{ps.Block} écart bind {maxErr:F4} m");
        }
        Assert.True(skinned > 0, $"{rel} : aucun émetteur mesh skinné trouvé");
    }

    [Fact]
    public void MountEmitter_SimBirthsFollowReskin()
    {
        var path = Nif("ride/model/r253.nif");
        if (path == null) return;
        var nif = NifFile.Load(path);
        var ps = nif.ParticleSystems.FirstOrDefault(p =>
            NifParticleMapper.Map(nif, p).Skin != null);
        Assert.NotNull(ps);
        var m = NifParticleMapper.Map(nif, ps);
        var def = m.Def;
        var skin = m.Skin!;
        var pts = def.Emitter.MeshPoints!;
        // Déplace tous les os de +100 en Y (espace des points) : les
        // naissances doivent suivre le déplacement posé.
        var bw = skin.Parts.Select(p =>
            p.BoneSkin.Select(_ => Matrix4x4.CreateTranslation(0, 100, 0)).ToArray()).ToArray();
        skin.Reskin(bw, pts, def.Emitter.MeshNormals);
        var sim = new ParticleSim(def, 1);
        sim.Step(1f / 30f);
        Assert.True(sim.Count > 0);
        foreach (var p in sim.Particles)
            Assert.True(p.Position.Y > 50f, $"naissance hors mesh posé : {p.Position}");
    }

    [Fact]
    public void MountEmitter_ReskinCost_PerFrame()
    {
        var path = Nif("ride/model/r253.nif");
        if (path == null) return;
        var nif = NifFile.Load(path);
        double worst = 0;
        foreach (var ps in nif.ParticleSystems)
        {
            var m = NifParticleMapper.Map(nif, ps);
            if (m.Skin is not { } es) continue;
            var invPs = Matrix4x4.Invert(ps.BindWorld, out var ip) ? ip : Matrix4x4.Identity;
            var bw = es.Parts.Select(p => p.BoneBlocks.Select(b =>
                    (nif.AvByBlock.TryGetValue(b, out var av) ? av.BindWorld : Matrix4x4.Identity) * invPs)
                    .ToArray()).ToArray();
            var pts = (Vector3[])m.Def.Emitter.MeshPoints!.Clone();
            var nrm = m.Def.Emitter.MeshNormals is { } n ? (Vector3[])n.Clone() : null;
            const int batch = 50;
            double best = double.MaxValue;
            for (var i = 0; i < 200; i++) es.Reskin(bw, pts, nrm);   // chauffe JIT/allocs
            for (var rep = 0; rep < 50; rep++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < batch; i++) es.Reskin(bw, pts, nrm);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000.0 / batch);
            }
            worst = Math.Max(worst, best);
            Console.WriteLine($"[skin-cost] r253 ps#{ps.Block} verts={es.Parts.Sum(p => p.LocalVerts.Length)} {best:F1} µs/frame");
        }
        // Min par batch (robuste à la charge de la machine de test) :
        // émetteurs par monture ≤ ~8 → budget total < 500 µs par frame.
        Assert.True(worst < 500, $"re-skin trop coûteux : {worst:F1} µs");
    }

    [Fact]
    public void CtlTiming_LoopReverseClamp()
    {
        var loop = new CtlTiming(0, 2, 1, 0, NifCycle.Loop, false);
        Assert.Equal(0.5f, loop.Local(2.5f), 4);
        var rev = new CtlTiming(0, 2, 1, 0, NifCycle.Reverse, false);
        Assert.Equal(1.5f, rev.Local(2.5f), 4);
        var clamp = new CtlTiming(1, 3, 2, 0, NifCycle.Clamp, false);
        Assert.Equal(3f, clamp.Local(10f), 4);
        Assert.False(clamp.Running(10f));
    }

    [Fact]
    public void EmitterActiveCurve_StopsEmission()
    {
        var active = new FxCurve
        {
            Chan = new KfFloatChan { Times = [0f, 1f], V = [1f, 0f], Type = KfKeyType.Const },
            Timing = new CtlTiming(0, 1, 1, 0, NifCycle.Clamp, false),
        };
        var def = new ParticleSystemDef
        {
            Emitter = new EmitterDef { LifeSpan = 10f },
            Curves = new Dictionary<PsParam, FxCurve>
            {
                [PsParam.BirthRate] = new FxCurve { Chan = new KfFloatChan { Times = [0f], V = [100f] }, Timing = default },
                [PsParam.EmitterActive] = active,
            },
        };
        var sim = new ParticleSim(def, 3);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        int after1s = sim.Count;
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        Assert.Equal(after1s, sim.Count); // émetteur éteint après 1 s (clés const)
        Assert.True(after1s > 20);
    }
}

public class FxCatalogTests
{
    static string? Db()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "corpus", "fr-full");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    [Fact]
    public void Resolve_DynThenEffect_NoGuessing()
    {
        var c = FxCatalog.Parse("DB0=s10121,s10121.nif,NULL,0,2.50,1\nDB1=s90113,s90112.nif,NULL,0,1,1\n",
            "DB0=E10121,x.wav,s10121\nDB1=E9,x.wav,s_absent\n");
        Assert.Equal("s10121", c.Resolve("E10121")!.Value.Nif);
        Assert.Equal(2.5f, c.Resolve("e10121")!.Value.Scale);
        Assert.Equal("s90112", c.Resolve("s90113")!.Value.Nif);   // nif ≠ clé
        Assert.Null(c.Resolve("E9"));
        Assert.Null(c.Resolve("NULL"));
    }

    [Fact]
    public void Corpus_SpellAndEnchantReferencesResolveToExistingNif()
    {
        var root = Db();
        if (root == null) return;
        var db = Path.Combine(root, "data", "db");
        var cat = FxCatalog.Parse(File.ReadAllText(Path.Combine(db, "effect.ini"), System.Text.Encoding.Latin1),
            File.ReadAllText(Path.Combine(db, "dyneffect.ini"), System.Text.Encoding.Latin1));
        Assert.True(cat.EffectCount > 1800);
        var files = new HashSet<string>(Directory.GetFiles(Path.Combine(root, "effect", "model"), "*.nif")
            .Select(f => Path.GetFileName(f).ToLowerInvariant()));
        int refs = 0, ok = 0;
        foreach (var (table, col, _) in FxCatalog.ReferenceColumns)
        {
            var path = Path.Combine(db, table);
            if (!File.Exists(path)) continue;
            foreach (var l in File.ReadAllText(path, System.Text.Encoding.Latin1).Split('\n'))
            {
                var line = l.TrimEnd('\r');
                int eq = line.StartsWith("DB") ? line.IndexOf('=') : -1;
                if (eq >= 0) line = line[(eq + 1)..];
                var f = line.Split(line.Contains('|') ? '|' : ',');
                if (col >= f.Length || !cat.Knows(f[col])) continue;
                refs++;
                if (cat.Resolve(f[col]) is { } e && files.Contains(e.Nif + ".nif")) ok++;
            }
        }
        Assert.True(refs > 5000, $"refs={refs}");
        Assert.True(ok >= refs * 0.99, $"{ok}/{refs} références d'effets résolues");
    }
}
