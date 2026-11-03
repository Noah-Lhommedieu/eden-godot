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
