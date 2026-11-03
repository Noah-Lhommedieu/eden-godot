using System.Numerics;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Accès au corpus EDENFR pour les tests d'animation native.</summary>
internal static class KfCorpus
{
    public static string? Root()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_CORPUS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "corpus", "fr-full");
            if (Directory.Exists(Path.Combine(p, "char"))) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    public static IEnumerable<string> Files(string root, string ext) =>
        Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories)
            .Where(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Résout un chemin cité par un KFM (« .\animation\B.kf »)
    /// relativement au dossier du KFM, insensible à la casse.</summary>
    public static string? Resolve(string kfmDir, string referenced)
    {
        var cur = kfmDir;
        foreach (var part in referenced.Replace('\\', '/')
                     .Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { cur = Path.GetDirectoryName(cur)!; continue; }
            if (!Directory.Exists(cur)) return null;
            var hit = Directory.EnumerateFileSystemEntries(cur).FirstOrDefault(
                x => string.Equals(Path.GetFileName(x), part,
                    StringComparison.OrdinalIgnoreCase));
            if (hit == null) return null;
            cur = hit;
        }
        return cur;
    }
}

/// <summary>
/// Audit de KfFile / KfmFile sur la totalité des .kf et .kfm de
/// <c>corpus/fr-full</c> : parse sans erreur ni écart de taille de bloc,
/// échantillonnage fini, couverture de tous les types de blocs, cuisson
/// dense avec erreur mesurée. Skippé si le corpus est absent.
/// </summary>
public class KfCorpusTests
{
    // Tous les types de blocs présents dans les .kf de fr-full : tout
    // nouveau type doit être décodé explicitement.
    private static readonly HashSet<string> KnownBlocks = new()
    {
        "NiControllerSequence", "NiTextKeyExtraData",
        "NiTransformInterpolator", "NiTransformData",
        "NiBSplineCompTransformInterpolator", "NiBSplineData", "NiBSplineBasisData",
        "NiFloatInterpolator", "NiFloatData",
        "NiBSplineCompFloatInterpolator", "NiBSplineCompPoint3Interpolator",
        "NiBoolInterpolator", "NiBoolData",
        "NiPoint3Interpolator", "NiPosData", "NiPathInterpolator",
    };

    [Fact]
    public void EveryKfParsesWithoutWarningAndSamplesFinite()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var files = 0; var seqs = 0; var tracks = 0; var aux = 0;
        var defectFiles = new SortedSet<string>();
        var blocks = new Dictionary<string, int>();
        foreach (var f in KfCorpus.Files(root, ".kf"))
        {
            var kf = KfFile.Parse(File.ReadAllBytes(f));
            files++;
            foreach (var (t, n) in kf.BlockTypeCounts())
                blocks[t] = blocks.GetValueOrDefault(t) + n;
            for (var i = 0; i < kf.SequenceCount; i++)
            {
                var s = kf.Get(i);
                Assert.True(s != null, $"{f} séquence {i} illisible");
                seqs++;
                var ts = new[] { s!.Start, (s.Start + s.Stop) / 2, s.Stop };
                foreach (var tr in s.Tracks)
                {
                    tracks++;
                    var p = Vector3.Zero; var r = Quaternion.Identity; var sc = 1f;
                    foreach (var t in ts)
                    {
                        tr.Sample(t, ref p, ref r, ref sc);
                        Assert.True(float.IsFinite(p.X + p.Y + p.Z + sc),
                            $"{f} {s.Name} {tr.NodeName} pos/scale NaN à {t}");
                        Assert.InRange(r.Length(), 0.99f, 1.01f);
                    }
                }
                foreach (var a in s.AuxTracks)
                {
                    aux++;
                    foreach (var t in ts)
                    {
                        Assert.True(float.IsFinite(a.SampleFloat(t)), $"{f} {a.ControllerType}");
                        var v = a.SampleVec(t);
                        Assert.True(float.IsFinite(v.X + v.Y + v.Z));
                    }
                }
                Assert.True(float.IsFinite(s.Duration) && s.Duration >= 0);
            }
            Assert.Empty(kf.Warnings);
            if (kf.DataDefects.Count > 0)
                defectFiles.Add(Path.GetFileName(f).ToLowerInvariant());
        }
        Assert.True(files >= 716, "fichiers kf : " + files);
        Assert.True(seqs >= 22945, "séquences : " + seqs);
        Assert.True(tracks > 1_500_000, "pistes transform : " + tracks);
        Assert.True(aux >= 114_900, "pistes auxiliaires : " + aux);
        // Aucun type de bloc inconnu ; les types rares sont bien présents.
        Assert.Empty(blocks.Keys.Where(k => !KnownBlocks.Contains(k)));
        foreach (var k in KnownBlocks) Assert.True(blocks.ContainsKey(k), k + " absent");
        // NaN d'origine : uniquement les anims de char g.kf et l.kf.
        Assert.Subset(new HashSet<string> { "g.kf", "l.kf" }, defectFiles);
    }

    [Fact]
    public void EveryKfmParsesCompletelyAndPointsToExistingSequences()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var n = 0; long trans = 0;
        foreach (var f in KfCorpus.Files(root, ".kfm"))
        {
            var k = KfmFile.Parse(File.ReadAllBytes(f));
            n++;
            Assert.False(k.Truncated, f);
            Assert.Empty(k.Warnings);
            Assert.NotEmpty(k.Anims);
            Assert.Equal(1, k.DefaultSyncType);
            Assert.True(k.DefaultNonSyncType is 0 or 2);
            Assert.Equal(0.1f, k.DefaultSyncDuration, 3);
            var dir = Path.GetDirectoryName(f)!;
            foreach (var g in k.Anims.GroupBy(a => a.KfPath))
            {
                var kfPath = KfCorpus.Resolve(dir, g.Key);
                Assert.True(kfPath != null, $"{f} → {g.Key} introuvable");
                var kf = KfFile.Parse(File.ReadAllBytes(kfPath!));
                foreach (var a in g)
                    Assert.True(a.Index < kf.SequenceCount, $"{f} événement {a.Event}");
            }
            var evs = k.Anims.Select(a => a.Event).ToHashSet();
            foreach (var a in k.Anims)
                foreach (var t in a.Transitions)
                {
                    trans++;
                    Assert.Equal(5, t.Type);            // fr-full : défaut seulement
                    Assert.Contains(t.Target, evs);     // cible = id d'événement
                }
        }
        Assert.True(n >= 856, "kfm : " + n);
        Assert.True(trans > 1_800_000, "transitions : " + trans);
    }

    [Fact]
    public void BakeIsDenseOrderedAndMeasuredOnCorpusSample()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var tracks = 0; var badPos = 0; var badRot = 0; var badScl = 0;
        float maxPosLinear = 0;
        foreach (var f in KfCorpus.Files(root, ".kf").Where((_, i) => i % 6 == 0))
        {
            var kf = KfFile.Parse(File.ReadAllBytes(f));
            for (var i = 0; i < kf.SequenceCount; i += 17)
            {
                var s = kf.Get(i)!;
                var b = s.Bake(30f);
                Assert.Equal(s.Tracks.Count, b.Tracks.Count);
                Assert.Equal(s.Duration, b.Duration);
                for (var k = 0; k < b.Tracks.Count; k++)
                {
                    var bt = b.Tracks[k]; var st = s.Tracks[k];
                    tracks++;
                    AssertSorted(bt.PosTimes, s.Duration); Assert.Equal(bt.PosTimes.Length, bt.Pos.Length);
                    AssertSorted(bt.RotTimes, s.Duration); Assert.Equal(bt.RotTimes.Length, bt.Rot.Length);
                    AssertSorted(bt.SclTimes, s.Duration); Assert.Equal(bt.SclTimes.Length, bt.Scl.Length);
                    for (var q = 1; q < bt.Rot.Length; q++)
                        Assert.True(Quaternion.Dot(bt.Rot[q - 1], bt.Rot[q]) >= 0);
                    foreach (var q in bt.Rot) Assert.InRange(q.Length(), 0.99f, 1.01f);
                    Assert.True(st.HasPos == (bt.Pos.Length > 0));
                    if (bt.MaxPosError > 0.02f) badPos++;
                    if (bt.MaxRotError > 0.05f) badRot++;
                    if (bt.MaxSclError > 0.02f) badScl++;
                    if (st.Pos is { IsLinearKeys: true })
                        maxPosLinear = MathF.Max(maxPosLinear, bt.MaxPosError);
                }
            }
        }
        Assert.True(tracks > 10_000, "pistes cuites : " + tracks);
        // Clés linéaires = clés exactes : erreur de cuisson nulle.
        Assert.True(maxPosLinear < 1e-3f, "erreur canal linéaire : " + maxPosLinear);
        // Restant : discontinuités des données (sauts entre deux clés
        // quasi simultanées), < 0,5 % des pistes.
        Assert.True(badPos <= tracks * 0.005, $"pos hors tolérance : {badPos}/{tracks}");
        Assert.True(badRot <= tracks * 0.005, $"rot hors tolérance : {badRot}/{tracks}");
        Assert.True(badScl <= tracks * 0.005, $"échelle hors tolérance : {badScl}/{tracks}");
    }

    private static void AssertSorted(float[] t, float dur)
    {
        if (t.Length == 0) return;
        Assert.Equal(0f, t[0], 5);
        for (var i = 1; i < t.Length; i++) Assert.True(t[i] > t[i - 1]);
        Assert.True(t[^1] <= dur + 1e-4f);
    }

    [Fact]
    public void BSplineDirectEvaluationStartsAtFirstControlPoint()
    {
        var path = KfCorpus.Root() is { } r
            ? EdenClient.Vfs.EdenVfs.ResolveInsensitive(r, "char/animation/b.kf") : null;
        if (path == null) return;
        var kf = KfFile.Parse(File.ReadAllBytes(path));
        var s = kf.Get("move01")!;
        var tr = s.Tracks.First(t => t.Rot?.Spline != null);
        var sp = tr.Rot!.Spline!;
        // spline ouverte : u=0 vaut le 1er point de contrôle
        var o = new float[4];
        sp.EvalU(0f, o);
        var expect = Quaternion.Normalize(new Quaternion(o[1], o[2], o[3], o[0]));
        Assert.True(MathF.Abs(Quaternion.Dot(expect, tr.Rot.Eval(s.Start))) > 0.9999f);
        Assert.InRange(tr.Rot.Eval(s.Start + s.Duration * 0.5f).Length(), 0.99f, 1.01f);
    }

    // ------------------------ canaux synthétiques ----------------------

    [Fact]
    public void HermiteQuadraticChannelHitsKeysAndFollowsTangents()
    {
        var c = new KfFloatChan
        {
            Type = KfKeyType.Quadratic,
            Times = new[] { 0f, 1f }, V = new[] { 0f, 1f },
            T0 = new[] { 3f, 0f }, T1 = new[] { 0f, 0f },
        };
        Assert.Equal(0f, c.Eval(0f), 5);
        Assert.Equal(1f, c.Eval(1f), 5);
        Assert.Equal(1f, c.Eval(5f), 5);                    // clamp après
        Assert.True(c.Eval(0.1f) > 0.1f);                   // tangente sortante forte
        // tangentes = delta ⇒ droite
        c.T0 = new[] { 1f, 0f }; c.T1 = new[] { 0f, 1f };
        Assert.Equal(0.25f, c.Eval(0.25f), 4);
    }

    [Fact]
    public void XyzRotationComposesZYXAndConstChannelIsStep()
    {
        var rot = new KfRotChan
        {
            Type = KfKeyType.XyzRotation,
            Xyz = new[]
            {
                new KfFloatChan(),
                new KfFloatChan(),
                new KfFloatChan { Times = new[] { 0f, 1f }, V = new[] { 0f, MathF.PI } },
            },
        };
        var q = rot.Eval(1f);                               // 180° autour de Z
        Assert.Equal(1f, MathF.Abs(q.Z), 4);
        Assert.Equal(0f, q.W, 4);
        Assert.Equal(0f, rot.Eval(0f).Z, 4);
        var step = new KfFloatChan { Type = KfKeyType.Const, Times = new[] { 0f, 1f }, V = new[] { 0f, 1f } };
        Assert.Equal(0f, step.Eval(0.99f));
        Assert.Equal(1f, step.Eval(1f));
    }

    [Fact]
    public void SquadPassesThroughKeysAndStaysUnit()
    {
        var qs = new[]
        {
            Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 2f),
        };
        var c = new KfRotChan { Type = KfKeyType.Quadratic, Times = new[] { 0f, 1f, 2f, 3f }, Q = qs };
        c.BuildSquad();
        for (var i = 0; i < 4; i++)
            Assert.True(MathF.Abs(Quaternion.Dot(c.Eval(i), qs[i])) > 0.9999f);
        for (var t = 0f; t <= 3f; t += 0.07f)
            Assert.InRange(c.Eval(t).Length(), 0.999f, 1.001f);
    }

    [Fact]
    public void BakeKeepsExactKeysWhenLinearAndCollapsesConstants()
    {
        var seq = new KfSequence { Name = "t", Start = 0, Stop = 2 };
        var tr = new KfTrack
        {
            NodeName = "n",
            Pos = new KfVec3Chan
            {
                Times = new[] { 0f, 0.5f, 2f },
                V = new[] { Vector3.Zero, Vector3.UnitX, new Vector3(1, 2, 0) },
            },
            Scl = new KfFloatChan { Times = new[] { 0f }, V = new[] { 1f } },
        };
        seq.Tracks.Add(tr);
        var b = seq.Bake(30f).Tracks[0];
        Assert.Equal(new[] { 0f, 0.5f, 2f }, b.PosTimes);
        Assert.Equal(0f, b.MaxPosError, 6);
        Assert.Single(b.Scl);
        Assert.Empty(b.Rot);
    }

    [Fact]
    public void BakeRefinesCurvedQuadraticChannelBelowTolerance()
    {
        var seq = new KfSequence { Name = "t", Start = 0, Stop = 1 };
        // courbe très tendue : un échantillon à 30 Hz ne suffirait pas
        seq.Tracks.Add(new KfTrack
        {
            NodeName = "n",
            Scl = new KfFloatChan
            {
                Type = KfKeyType.Quadratic, Times = new[] { 0f, 1f },
                V = new[] { 0f, 0f }, T0 = new[] { 40f, 0f }, T1 = new[] { 0f, -40f },
            },
        });
        var b = seq.Bake(30f).Tracks[0];
        Assert.True(b.Scl.Length > 31);
        Assert.True(b.MaxSclError < KfSequence.SclTol * 2, "erreur " + b.MaxSclError);
    }
}
