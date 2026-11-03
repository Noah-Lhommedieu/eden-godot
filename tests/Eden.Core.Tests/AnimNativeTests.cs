using EdenClient.Game;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Text keys → événements et noms d'os natifs (corpus fr-full).</summary>
public class AnimNativeTests
{
    // ------------------------------ AnimTextKeys -----------------------

    [Fact]
    public void ClassifiesEveryKeyFamilyFound()
    {
        Assert.Equal(AnimEventKind.Start, AnimTextKeys.Classify("start", out _));
        Assert.Equal(AnimEventKind.End, AnimTextKeys.Classify("end", out _));
        Assert.Equal(AnimEventKind.Hit, AnimTextKeys.Classify("hit", out _));
        Assert.Equal(AnimEventKind.Metadata, AnimTextKeys.Classify(
            " -GlobalRatio 100 -GlobalCompressFloats true -GlobalDontCompress false", out _));
        Assert.Equal(AnimEventKind.Metadata, AnimTextKeys.Classify("morph: unknown", out _));
        Assert.Equal(AnimEventKind.Sound, AnimTextKeys.Classify("s1", out var i1));
        Assert.Equal(1, i1);
        Assert.Equal(AnimEventKind.EffectSpawn, AnimTextKeys.Classify("e2", out var i2));
        Assert.Equal(2, i2);
        Assert.Equal(AnimEventKind.Unknown, AnimTextKeys.Classify("zzz", out _));
        // m# dépend de la famille de séquence
        Assert.Equal(AnimEventKind.Footstep, AnimTextKeys.Classify("move01", "m2", false, out var m));
        Assert.Equal(2, m);
        Assert.Equal(AnimEventKind.Hit, AnimTextKeys.Classify("attack03", "m1", false, out _));
        Assert.Equal(AnimEventKind.Marker, AnimTextKeys.Classify("attack03", "m1", true, out _));
        Assert.Equal(AnimEventKind.Marker, AnimTextKeys.Classify("death01", "m3", false, out _));
        Assert.Equal("rshoot", AnimTextKeys.Family("rshoot64"));
    }

    [Fact]
    public void EveryCorpusKeyClassifiesAndFamiliesMatchDocumentation()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var distinct = new HashSet<string>();
        var unknown = new HashSet<string>();
        var perKind = new Dictionary<AnimEventKind, int>();
        foreach (var f in KfCorpus.Files(root, ".kf"))
        {
            var kf = KfFile.Parse(File.ReadAllBytes(f));
            for (var i = 0; i < kf.SequenceCount; i++)
            {
                var s = kf.Get(i)!;
                foreach (var k in s.TextKeys) distinct.Add(k.Value);
                foreach (var e in AnimTextKeys.Build(s))
                {
                    perKind[e.Kind] = perKind.GetValueOrDefault(e.Kind) + 1;
                    if (e.Kind == AnimEventKind.Unknown) unknown.Add(e.Key);
                }
            }
        }
        Assert.Empty(unknown);
        // 11 chaînes distinctes : start end m1 m2 m3 hit s1 e1 e2 + 2 métadonnées
        Assert.Equal(11, distinct.Count);
        Assert.Equal(22945, perKind[AnimEventKind.Start]);
        Assert.Equal(22945, perKind[AnimEventKind.End]);
        Assert.True(perKind[AnimEventKind.Hit] >= 1);
        Assert.True(perKind[AnimEventKind.Footstep] >= 6);
        Assert.Equal(1, perKind[AnimEventKind.Sound]);
        Assert.Equal(2, perKind[AnimEventKind.EffectSpawn]);
        Assert.Equal(15, perKind[AnimEventKind.Metadata]);
    }

    [Fact]
    public void BuildOrdersByTimeAndPromotesMotionKeysByContext()
    {
        var keys = new List<KfTextKey>
        {
            new(0f, "start"), new(1.333f, "end"),
            new(0.315f, "hit"), new(0.301f, "m1"),
        };
        var ev = AnimTextKeys.Build("attack02", keys);
        Assert.Equal(new[] { "start", "m1", "hit", "end" }, ev.Select(e => e.Key));
        Assert.Equal(AnimEventKind.Marker, ev[1].Kind);     // « hit » explicite présent
        Assert.Equal(AnimEventKind.Hit, ev[2].Kind);
        // sans « hit » : m1 devient l'impact
        var ev2 = AnimTextKeys.Build("attack01", keys.Where(k => k.Value != "hit").ToList());
        Assert.Equal(AnimEventKind.Hit, AnimTextKeys.First(ev2, AnimEventKind.Hit)!.Value.Kind);
        Assert.Equal(0.301f, AnimTextKeys.First(ev2, AnimEventKind.Hit)!.Value.Time, 4);
        // Start retranché
        var ev3 = AnimTextKeys.Build("move01", new[] { new KfTextKey(2.5f, "m1") }, 2f);
        Assert.Equal(0.5f, ev3[0].Time, 4);
    }

    [Fact]
    public void CollectFiresEventsInWindowAndRepeatsOnLoops()
    {
        var ev = AnimTextKeys.Build("move01", new[]
        {
            new KfTextKey(0f, "start"), new KfTextKey(0.23f, "m1"),
            new KfTextKey(0.55f, "m2"), new KfTextKey(0.6f, "end"),
        });
        var got = new List<AnimEvent>();
        AnimTextKeys.Collect(ev, 0.6f, true, 0.2f, 0.3f, got);
        Assert.Single(got);
        Assert.Equal(AnimEventKind.Footstep, got[0].Kind);
        got.Clear();
        // fenêtre à cheval sur la boucle : m2 (0,55) puis end+start puis m1 (0,6+0,23)
        AnimTextKeys.Collect(ev, 0.6f, true, 0.5f, 0.85f, got);
        Assert.Equal(new[] { "m2", "end", "start", "m1" }, got.Select(e => e.Key));
        Assert.Equal(0.83f, got[^1].Time, 4);
        // clip non bouclé : rien après la durée
        got.Clear();
        AnimTextKeys.Collect(ev, 0.6f, false, 0.5f, 3f, got);
        Assert.Equal(new[] { "m2", "end" }, got.Select(e => e.Key));
        // start à t=0 : seulement avec includeFrom
        got.Clear();
        AnimTextKeys.Collect(ev, 0.6f, false, 0f, 0.1f, got);
        Assert.Empty(got);
        AnimTextKeys.Collect(ev, 0.6f, false, 0f, 0.1f, got, includeFrom: true);
        Assert.Equal("start", got[0].Key);
    }

    [Fact]
    public void RealMonsterMoveHasTwoFootstepsPerCycle()
    {
        var root = KfCorpus.Root();
        var p = root == null ? null : EdenClient.Vfs.EdenVfs.ResolveInsensitive(
            root, "monster/animation/M014.kf");
        if (p == null) return;
        var s = KfFile.Parse(File.ReadAllBytes(p)).Get("move02")!;
        var ev = AnimTextKeys.Build(s);
        Assert.Equal(2, ev.Count(e => e.Kind == AnimEventKind.Footstep));
        var got = new List<AnimEvent>();
        AnimTextKeys.Collect(ev, s.Duration, true, 0f, s.Duration * 3f, got);
        Assert.Equal(6, got.Count(e => e.Kind == AnimEventKind.Footstep));
    }

    // ---------------------------- NativeBoneNames ----------------------

    [Fact]
    public void EveryTrackTargetResolvesInTheKfmModelNif()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var failures = new List<string>();
        var kfms = 0; var targets = 0; var matched = 0; var ambiguous = 0;
        var missingNif = new List<string>();
        foreach (var f in KfCorpus.Files(root, ".kfm"))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            // décors : dummy.kf (squelette factice), objets de map : voir doc
            if (!(rel.StartsWith("char/") || rel.StartsWith("ride/") ||
                  rel.StartsWith("house/") || rel.StartsWith("monster/")))
                continue;
            var kfm = KfmFile.Parse(File.ReadAllBytes(f));
            var dir = Path.GetDirectoryName(f)!;
            var nifPath = KfCorpus.Resolve(dir, kfm.NifName);
            if (nifPath == null || !File.Exists(nifPath)) { missingNif.Add(rel); continue; }
            kfms++;
            var nif = NifFile.Parse(File.ReadAllBytes(nifPath));
            foreach (var kfRel in kfm.Anims.Select(a => a.KfPath).Distinct())
            {
                var kfPath = KfCorpus.Resolve(dir, kfRel);
                Assert.NotNull(kfPath);
                var r = NativeBoneNames.Resolve(KfFile.Parse(File.ReadAllBytes(kfPath!)), nif);
                targets += r.Targets; matched += r.Matched; ambiguous += r.Ambiguous.Count;
                if (!r.Complete)
                    failures.Add($"{rel}: {string.Join(",", r.Unmatched)}");
            }
        }
        Assert.True(kfms >= 700, "kfm résolus : " + kfms);
        Assert.Equal(new[] { "monster/m264.kfm" }, missingNif);   // nif absent du corpus
        // Seule cible orpheline du corpus : m462.kf vise « Plane431 », nœud
        // absent de m462.nif (octets introuvables dans le fichier : défaut
        // d'export d'origine, sans effet visuel).
        Assert.Equal(new[] { "monster/m462.kfm: Plane431" }, failures);
        Assert.Equal(targets - 1, matched);
        Assert.True(targets > 30_000, "cibles : " + targets);
        _ = ambiguous;      // informatif : noms en double signalés, premier retenu
    }

    [Fact]
    public void GodotBoneNamesKeepNifNamesAndEscapeReservedChars()
    {
        Assert.Equal("Bip01 L Calf", NativeBoneNames.GodotBoneName("Bip01 L Calf"));
        Assert.Equal("a_b_c", NativeBoneNames.GodotBoneName("a:b/c"));
        Assert.True(NativeBoneNames.IsGodotSafe("Bone_L102_Hair_L03"));
        Assert.False(NativeBoneNames.IsGodotSafe("NiPSysMeshEmitter:0"));
        Assert.Equal("Skeleton3D:Bip01 Head",
            NativeBoneNames.TrackPath("Skeleton3D", "Bip01 Head"));
        Assert.Equal("model/B.nif", NativeBoneNames.KfmRelative(".\\model\\B.nif"));
        Assert.Equal("char/animation/B.kf",
            NativeBoneNames.KfmSibling("char", ".\\animation\\B.kf"));
    }

    [Fact]
    public void BoneNamesOfCharMonsterRideNifsAreGodotSafe()
    {
        var root = KfCorpus.Root();
        if (root == null) return;
        var n = 0;
        foreach (var dir in new[] { "char/model", "monster/model", "ride/model" })
        {
            var d = Path.Combine(root, dir);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*.nif").Where((_, i) => i % 5 == 0))
            {
                var nif = NifFile.Parse(File.ReadAllBytes(f));
                foreach (var nd in nif.Nodes)
                    Assert.True(NativeBoneNames.IsGodotSafe(nd.Name), f + " : " + nd.Name);
                n++;
            }
        }
        Assert.True(n > 100);
    }
}
