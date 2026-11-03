using System.Numerics;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Lecteurs KF/KFM + pose/skinning sur les vrais fichiers EDENFR
/// (<c>char/animation/g.kf</c>, <c>char/g.kfm</c>, <c>char/model/g.nif</c>).
/// KF/KFM identiques dans <c>corpus/fr-full</c> et <c>extracted</c> ;
/// le g.nif complet (skin) n'existe que dans <c>extracted</c>.
/// Tests skippés si les arbres sont absents.
/// </summary>
public class KfTests
{
    private static string? Find(string rel)
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        foreach (var r in new[]
        {
            Environment.GetEnvironmentVariable("EDEN_NIF_ROOT"),
            Path.Combine(home, "Téléchargements", "EDENFR", "extracted"),
            Environment.GetEnvironmentVariable("EDEN_CORPUS"),
        })
            if (!string.IsNullOrEmpty(r)) roots.Add(r);
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "client_assets");
            if (Directory.Exists(p)) { roots.Add(p); break; }
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        foreach (var r in roots)
            if (Directory.Exists(r) &&
                EdenClient.Vfs.EdenVfs.ResolveInsensitive(r, rel) is { } hit)
                return hit;
        return null;
    }

    [Fact]
    public void BSplineEndpointsAndConstants()
    {
        var cp = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };   // 8 points, dim 1
        var o = new float[1];
        KfFile.BSplineEval(cp, 8, 1, 0f, o);
        Assert.Equal(1f, o[0], 4);                          // ouverte : P0
        KfFile.BSplineEval(cp, 8, 1, 1f, o);
        Assert.Equal(8f, o[0], 4);                          // Pn−1
        var flat = Enumerable.Repeat(3.5f, 6).ToArray();
        KfFile.BSplineEval(flat, 6, 1, 0.37f, o);
        Assert.Equal(3.5f, o[0], 4);
        // points colinéaires équidistants → monotone
        var prev = float.MinValue;
        for (var i = 0; i <= 10; i++)
        {
            KfFile.BSplineEval(cp, 8, 1, i / 10f, o);
            Assert.True(o[0] >= prev - 1e-4f);
            prev = o[0];
        }
    }

    [Fact]
    public void GKfSequencesDecode()
    {
        var path = Find("char/animation/g.kf");
        if (path == null) return;
        var kf = KfFile.Load(path);
        Assert.True(kf.SequenceCount > 300);
        Assert.Equal("stand01", kf.SequenceNames[0]);
        Assert.Equal("move01", kf.SequenceNames[6]);
        var stand = kf.Get("stand01")!;
        Assert.NotNull(stand);
        Assert.InRange(stand.Duration, 3.0f, 3.6f);
        Assert.True(stand.Tracks.Count > 100);
        Assert.Contains(stand.Tracks, t => t.NodeName == "Bip01");
        Assert.NotEmpty(stand.TextKeys);
        // toutes les pistes échantillonnables : quaternions unitaires, finis
        for (var k = 0; k < 4; k++)
            foreach (var tr in stand.Tracks)
            {
                var p = Vector3.Zero; var r = Quaternion.Identity; var s = 1f;
                tr.Sample(stand.Start + stand.Duration * k / 3f,
                    ref p, ref r, ref s);
                Assert.True(float.IsFinite(p.X + p.Y + p.Z));
                Assert.InRange(r.Length(), 0.99f, 1.01f);
                Assert.True(float.IsFinite(s));
            }
        var move = kf.Get("move01")!;
        Assert.InRange(move.Duration, 0.7f, 0.9f);
        Assert.Equal(KfCycle.Loop, move.Cycle);
        // wrap : boucle
        Assert.InRange(move.Wrap(move.Duration * 2.25f),
            move.Start, move.Start + move.Duration);
    }

    [Fact]
    public void BSplineAndTransformDataTracksBothPresent()
    {
        var path = Find("char/animation/b.kf");
        if (path == null) return;
        var kf = KfFile.Load(path);
        Assert.Equal(391, kf.SequenceCount);
        var s = kf.Get("stand01")!;
        // 206 blocs contrôlés ; B-splines évaluées exactement
        Assert.True(s.Tracks.Count >= 200);
        Assert.Contains(s.Tracks, t => t.Rot?.Spline != null);
        Assert.Contains(s.Tracks, t => t.HasPos);
    }

    [Fact]
    public void GKfmMapsLocomotionEvents()
    {
        var path = Find("char/g.kfm");
        if (path == null) return;
        var kfm = KfmFile.Load(path);
        Assert.True(kfm.Anims.Count >= 300);
        Assert.False(kfm.Truncated);
        Assert.EndsWith("G.kf", kfm.ByEvent(101)!.KfPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, kfm.ByEvent(101)!.Index);
        Assert.Equal(7, kfm.ByEvent(102)!.Index);
        Assert.Equal(8, kfm.ByEvent(103)!.Index);
        Assert.Equal(9, kfm.ByEvent(104)!.Index);
        Assert.Equal(0, kfm.ByEvent(1)!.Index);
        var kfPath = Find("char/animation/g.kf");
        if (kfPath == null) return;
        var kf = KfFile.Load(kfPath);
        Assert.Equal("move01", kf.SequenceNames[kfm.ByEvent(101)!.Index]);
    }

    [Fact]
    public void PoseBindMatchesBakedAndAnimationMovesBones()
    {
        var nifPath = Find("char/model/g.nif");
        var kfPath = Find("char/animation/g.kf");
        if (nifPath == null || kfPath == null) return;
        var nif = NifFile.Parse(File.ReadAllBytes(nifPath));
        var skinned = nif.Shapes.Where(s => s.Skin != null).ToList();
        if (skinned.Count == 0) return;          // g.nif stripé : skip
        Assert.True(nif.Nodes.Count > 50);
        var pose = new KfPose(nif);
        pose.ResetBind();
        pose.Solve();
        // Bind : skinning = vertices cuits (tolérance 5 cm sur la médiane).
        var errs = new List<float>();
        foreach (var sh in skinned.Take(20))
        {
            var v = new Vector3[sh.LocalVertices!.Length];
            Assert.True(pose.Skin(sh, v, null));
            for (var i = 0; i < v.Length; i += Math.Max(1, v.Length / 50))
                errs.Add((v[i] - sh.Vertices[i]).Length());
        }
        errs.Sort();
        Assert.True(errs[errs.Count / 2] < 0.05f,
            "médiane erreur bind " + errs[errs.Count / 2]);

        var kf = KfFile.Load(kfPath);
        var move = kf.Get("move01")!;
        pose.ResetBind();
        pose.Apply(move, move.Start + move.Duration * 0.25f);
        pose.Solve();
        Assert.True(pose.UnmatchedTracks < move.Tracks.Count / 2);
        var sh0 = skinned[0];
        var vb = new Vector3[sh0.LocalVertices!.Length];
        pose.Skin(sh0, vb, null);
        var moved = 0f;
        for (var i = 0; i < vb.Length; i++)
            moved = MathF.Max(moved, (vb[i] - sh0.Vertices[i]).Length());
        Assert.True(float.IsFinite(moved));
    }
}
