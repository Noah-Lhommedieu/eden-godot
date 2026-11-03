using System.Collections.Concurrent;
using System.Text;
using EdenClient.World;
using Xunit;
using Xunit.Abstractions;

namespace EdenClient.NetTests;

/// <summary>
/// Extension du lecteur <see cref="NifFile"/> (matériaux, arbre de scène,
/// contrôleurs, particules) : parse de TOUS les .nif/.kf de
/// <c>corpus/fr-full</c> — zéro erreur, zéro type de bloc inconnu — et
/// comptage de chaque fonctionnalité. Skippé si le corpus est absent.
/// </summary>
public class NifExtCorpusTests
{
    private readonly ITestOutputHelper _out;
    public NifExtCorpusTests(ITestOutputHelper o) => _out = o;

    private static string? FindFrFull()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_FR_FULL");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "corpus", "fr-full");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    private static bool IsNif(byte[] d)
    {
        var magic = Encoding.Latin1.GetBytes("Gamebryo File Format");
        return d.Length > magic.Length &&
            d.AsSpan(0, magic.Length).SequenceEqual(magic);
    }

    [Fact]
    public void WholeCorpusParsesWithoutErrorOrUnknownBlock()
    {
        var root = FindFrFull();
        if (root == null) return;                       // skip

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".kf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(files.Length > 100, "corpus fr-full incomplet ?");

        var errors = new ConcurrentBag<string>();
        var unknown = new ConcurrentBag<string>();
        var skipped = new ConcurrentDictionary<string, int>();
        var notNif = new ConcurrentBag<string>();
        // Fichiers contenant la fonctionnalité / total de blocs.
        var filesWith = new ConcurrentDictionary<string, int>();
        var blocks = new ConcurrentDictionary<string, int>();
        var coherence = new ConcurrentBag<string>();
        var parsed = 0;

        Parallel.ForEach(files, f =>
        {
            var data = File.ReadAllBytes(f);
            if (!IsNif(data)) { notNif.Add(f); return; }
            NifFile n;
            try { n = NifFile.Parse(data); }
            catch (Exception e) { errors.Add($"{f} : {e.Message}"); return; }
            Interlocked.Increment(ref parsed);
            foreach (var w in n.Errors) errors.Add($"{f} : {w}");
            foreach (var u in n.UnknownBlockTypes) unknown.Add($"{u} ({f})");
            foreach (var s in n.SkippedBlockTypes)
                skipped.AddOrUpdate(s, 1, (_, v) => v + 1);
            foreach (var (t, c) in n.BlockTypeCounts)
            {
                blocks.AddOrUpdate(t, c, (_, v) => v + c);
                filesWith.AddOrUpdate(t, 1, (_, v) => v + 1);
            }
            // Cohérence : chaque bloc typé doit se retrouver dans la liste typée.
            int Cnt(string t) => n.BlockTypeCounts.GetValueOrDefault(t);
            void Same(string what, int got, int want)
            {
                if (got != want) coherence.Add($"{f} : {what} {got} != {want}");
            }
            Same("particules", n.ParticleSystems.Count, Cnt("NiParticleSystem"));
            Same("séquences", n.Sequences.Count, Cnt("NiControllerSequence"));
            Same("lumières", n.Lights.Count,
                Cnt("NiDirectionalLight") + Cnt("NiAmbientLight"));
            Same("portails", n.Portals.Count, Cnt("NiPortal"));
            Same("salles", n.Rooms.Count, Cnt("NiRoom"));
            Same("lignes", n.Lines.Count(l => l.Vertices.Length > 0),
                n.Lines.Count);
            Same("extra", n.ExtraData.Count,
                Cnt("NiStringExtraData") + Cnt("NiBooleanExtraData") +
                Cnt("NiIntegerExtraData") + Cnt("NiIntegersExtraData") +
                Cnt("NiFloatExtraData") + Cnt("NiTextKeyExtraData"));
            Same("contrôleurs", n.Controllers.Count,
                n.BlockTypeCounts.Where(kv => kv.Key.EndsWith("Controller") ||
                    kv.Key.EndsWith("Ctlr")).Sum(kv => kv.Value)
                - Cnt("NiPSysModifierCtlr")); // abstrait : jamais en bloc
            foreach (var ps in n.ParticleSystems)
            {
                if (ps.Data == null) coherence.Add($"{f} : particules sans NiPSysData");
                if (ps.Modifiers.Count != ps.ModifierBlocks.Length)
                    coherence.Add($"{f} : modifiers non résolus");
            }
            // Valeurs plausibles : indices, couleurs, sources, interpolateurs.
            foreach (var sh in n.Shapes)
                if (sh.Indices.Any(i => i < 0 || i >= sh.Vertices.Length))
                    coherence.Add($"{f} : {sh.Name} indice hors sommets");
            foreach (var p in n.Properties.OfType<NifMaterial>())
                if (!float.IsFinite(p.Alpha) || !float.IsFinite(p.Glossiness) ||
                    !float.IsFinite(p.Diffuse.X))
                    coherence.Add($"{f} : matériau {p.Block} alpha {p.Alpha}");
            foreach (var t in n.Properties.OfType<NifTexturingProperty>())
                foreach (var m in t.AllMaps())
                    if (m.SourceBlock >= 0 && m.Source == null)
                        coherence.Add($"{f} : slot texture sans source");
            foreach (var ip in n.Interpolators)
            {
                var need = ip.Type switch
                {
                    "NiFloatInterpolator" => ip.DataBlock < 0 || ip.Floats != null,
                    "NiBoolInterpolator" => ip.DataBlock < 0 || ip.Bools != null,
                    "NiPoint3Interpolator" => ip.DataBlock < 0 || ip.Points != null,
                    "NiTransformInterpolator" => ip.DataBlock < 0 || ip.Transform != null,
                    "NiPathInterpolator" => ip.Points != null && ip.Floats != null,
                    _ => ip.SplineData != null,
                };
                if (!need) coherence.Add($"{f} : {ip.Type} données non résolues");
            }
            foreach (var em in n.ParticleSystems.SelectMany(p => p.Of<NifPSysEmitter>()))
                if (!float.IsFinite(em.LifeSpan) ||
                    !float.IsFinite(em.Speed) || !float.IsFinite(em.InitialRadius))
                    coherence.Add($"{f} : émetteur {em.Name} valeurs aberrantes");
            // Versions sans tailles (20.1.0.3) : tout doit être lu à la suite.
            if (n.Version == 0x14010003 && n.Shapes.Count == 0)
                coherence.Add($"{f} : 20.1.0.3 sans shape (resync raté)");
            foreach (var k in n.Controllers)
                if (k.InterpolatorBlock >= 0 && k.Interpolator == null &&
                    k.Type != "NiGeomMorpherController")
                    coherence.Add($"{f} : {k.Type} sans interpolateur");
        });

        _out.WriteLine($"fichiers: {files.Length}, parsés: {parsed}, " +
            $"non-NIF: {string.Join(',', notNif.Select(Path.GetFileName))}");
        _out.WriteLine("types sautés par conception: " +
            string.Join(", ", skipped.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));

        // Comptes de fonctionnalités (fichiers) vs références attendues.
        var expect = new (string type, int want)[]
        {
            ("NiParticleSystem", 2317), ("NiPSysMeshEmitter", 1832),
            ("NiTextureTransformController", 2344), ("NiAlphaController", 2341),
            ("NiBillboardNode", 1984), ("NiMaterialColorController", 768),
            ("NiFlipController", 164), ("NiPathInterpolator", 108),
            ("NiRoom", 91), ("NiPortal", 91), ("NiControllerSequence", 0),
            ("NiVisController", 0), ("NiTransformController", 0),
            ("NiGeomMorpherController", 0), ("NiLODNode", 0),
            ("NiSwitchNode", 0), ("NiSortAdjustNode", 0),
            ("NiDirectionalLight", 0), ("NiAmbientLight", 0),
            ("NiCollisionData", 0), ("NiLines", 0), ("NiPSysBoxEmitter", 0),
            ("NiPSysSphereEmitter", 0), ("NiPSysColorModifier", 0),
            ("NiPSysGravityModifier", 0), ("NiPSysColliderManager", 0),
            ("NiPSysPlanarCollider", 0), ("NiStringExtraData", 0),
            ("NiTextKeyExtraData", 0),
        };
        foreach (var (type, want) in expect)
            _out.WriteLine($"{type,-34} fichiers={filesWith.GetValueOrDefault(type),5} " +
                $"blocs={blocks.GetValueOrDefault(type),7}" +
                (want > 0 ? $"   (réf. {want})" : ""));
        _out.WriteLine("types de blocs distincts: " + blocks.Count);

        NoneOf("erreurs de parse", errors);
        NoneOf("types de blocs inconnus", unknown.Distinct());
        NoneOf("incohérences", coherence);
        // Quelques invariants forts du corpus.
        Assert.True(filesWith["NiParticleSystem"] >= 2317);
        Assert.True(filesWith["NiTextureTransformController"] >= 2344);
        Assert.True(filesWith["NiAlphaController"] >= 2341);
        Assert.True(filesWith["NiBillboardNode"] >= 1984);
        Assert.True(filesWith["NiMaterialColorController"] >= 768);
        Assert.True(filesWith["NiFlipController"] >= 164);
        Assert.True(filesWith["NiPathInterpolator"] >= 108);
    }

    private static void NoneOf(string what, IEnumerable<string> items)
    {
        var l = items.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(l.Count == 0,
            $"{l.Count} {what} :\n" + string.Join("\n", l.Take(15)));
    }

    [Fact]
    public void KnownEffectDecodesParticlesAndControllers()
    {
        var root = FindFrFull();
        if (root == null) return;                       // skip
        var hit = Directory.EnumerateFiles(
                Path.Combine(root, "effect"), "*.nif", SearchOption.AllDirectories)
            .Select(p => NifFile.Parse(File.ReadAllBytes(p)))
            .FirstOrDefault(n => n.ParticleSystems.Count > 0 &&
                n.ParticleSystems[0].Of<NifPSysEmitter>().Any() &&
                n.Controllers.Any(k => k.Type == "NiPSysEmitterCtlr"));
        Assert.NotNull(hit);
        var ps = hit!.ParticleSystems[0];
        Assert.NotNull(ps.Data);
        Assert.True(ps.Data!.MaxParticles > 0);
        var em = ps.Of<NifPSysEmitter>().First();
        Assert.True(em.LifeSpan > 0 || em.LifeSpanVariation >= 0);
        Assert.True(ps.Of<NifPSysAgeDeath>().Any());
        Assert.True(ps.Of<NifPSysSpawn>().Any());
        Assert.True(ps.Of<NifPSysBoundUpdate>().Any());
    }
}
