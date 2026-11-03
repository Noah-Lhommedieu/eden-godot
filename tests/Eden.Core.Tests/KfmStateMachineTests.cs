using EdenClient.Game;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Machine d'états KFM : clips synthétiques et KFM réels.</summary>
public class KfmStateMachineTests
{
    private static KfmClip Loop(int ev, string n, float dur = 1f) =>
        new(ev, n, 0f, dur, KfCycle.Loop);
    private static KfmClip Once(int ev, string n, float dur = 0.5f) =>
        new(ev, n, 0f, dur, KfCycle.Clamp);

    private static KfmStateMachine Synthetic(Func<int, int, KfmBlend>? r = null) =>
        new(new[] { Loop(1, "stand01"), Loop(101, "move01", 0.8f),
                    Once(1021, "attack01"), Once(201, "death01", 2f) },
            new KfmBlend(KfmTransitionType.Blend, 0.1f), r);

    [Fact]
    public void FirstRequestStartsWithoutBlend()
    {
        var m = Synthetic();
        Assert.Equal(int.MinValue, m.Current);
        Assert.True(m.Request(1));
        Assert.Equal(1, m.Current);
        Assert.False(m.Blending);
        var l = Assert.Single(m.Layers);
        Assert.Equal(1f, l.Weight);
        Assert.Contains(m.Events, e => e is { Kind: KfmEventKind.Started, Event: 1 });
    }

    [Fact]
    public void UnknownEventIsRejectedAndChangesNothing()
    {
        var m = Synthetic();
        m.Request(1);
        Assert.False(m.Request(9999));
        Assert.Equal(1, m.Current);
        Assert.Contains(m.Events, e => e is { Kind: KfmEventKind.Rejected, Event: 9999 });
        Assert.False(m.Queue(9999));
    }

    [Fact]
    public void SameClipRequestIsIgnoredUnlessRestart()
    {
        var m = Synthetic();
        m.Request(101);
        m.Update(0.3f);
        var t = m.CurrentElapsed;
        m.Request(101);
        Assert.Equal(t, m.CurrentElapsed);
        Assert.False(m.Blending);
        m.Request(101, restart: true);
        Assert.True(m.Blending);                   // redémarre avec fondu
    }

    [Fact]
    public void CrossFadeWeightsSumToOneAndEndAtTarget()
    {
        var m = Synthetic();
        m.Request(1); m.Update(0.2f);
        m.Request(101);
        Assert.True(m.Blending);
        Assert.Equal(2, m.Layers.Count);
        m.Update(0.05f);
        var ls = m.Layers;
        Assert.Equal(1, ls[0].Event); Assert.Equal(101, ls[1].Event);
        Assert.Equal(0.5f, ls[1].Weight, 3);
        Assert.Equal(1f, ls[0].Weight + ls[1].Weight, 4);
        Assert.Equal(1f, ls[0].ApplyWeight);       // 1re couche remplace
        Assert.Equal(0.5f, ls[1].ApplyWeight, 3);  // 2e : mélange incrémental
        Assert.Equal(0.5f, m.BlendProgress, 3);
        m.Update(0.06f);
        Assert.False(m.Blending);
        Assert.Single(m.Layers);
        Assert.Equal(1f, m.Layers[0].Weight);
        var ev = new List<KfmEvent>(); m.DrainEvents(ev);
        Assert.Contains(ev, e => e.Kind == KfmEventKind.BlendDone && e.Event == 101);
        Assert.Empty(m.Events);
    }

    [Fact]
    public void ReRequestMidBlendKeepsWeightsContinuous()
    {
        var m = Synthetic();
        m.Request(1); m.Request(101); m.Update(0.05f);   // 50 % move
        var wMove = m.Layers.First(l => l.Event == 101).Weight;
        m.Request(1021);
        Assert.Equal(3, m.Layers.Count);
        // pas de saut au moment de la demande
        Assert.Equal(wMove, m.Layers.First(l => l.Event == 101).Weight, 4);
        Assert.Equal(0f, m.Layers.First(l => l.Event == 1021).Weight, 4);
        m.Update(0.05f);
        Assert.Equal(1f, m.Layers.Sum(l => l.Weight), 4);
        var cum = 0f;
        foreach (var l in m.Layers)                      // poids incrémentaux cohérents
        {
            cum += l.Weight;
            Assert.Equal(l.Weight / cum, l.ApplyWeight, 4);
        }
        m.Update(0.2f);
        Assert.Single(m.Layers);
        Assert.Equal(1021, m.Current);
    }

    [Fact]
    public void LoopClipRaisesLoopedAndOnceClipEndsExactlyOnce()
    {
        var m = Synthetic();
        m.Request(101);
        m.Update(0.5f);
        Assert.DoesNotContain(m.Events, e => e.Kind == KfmEventKind.Looped);
        m.Update(0.5f);                                  // 1,0 s > 0,8 s
        Assert.Contains(m.Events, e => e is { Kind: KfmEventKind.Looped, Event: 101 });
        Assert.True(m.Steps[0].Wrapped);
        Assert.Equal(0.2f, m.Layers[0].SequenceTime, 3);

        var o = Synthetic();
        o.Request(1021);
        o.Update(0.4f);
        Assert.False(o.CurrentFinished);
        o.Update(0.4f);
        Assert.True(o.CurrentFinished);
        Assert.Single(o.Events, e => e.Kind == KfmEventKind.Ended);
        o.Update(0.4f);
        Assert.Single(o.Events, e => e.Kind == KfmEventKind.Ended);   // pas de 2e fin
        Assert.Equal(0.5f, o.Layers[0].SequenceTime, 4);              // reste en fin de clip
    }

    [Fact]
    public void ReturnEventAndQueueChainAfterOnceClip()
    {
        var m = Synthetic();
        m.ReturnEvent = 1;
        m.Request(1021);
        m.Update(0.6f);                                   // fin → retour au stand
        Assert.Equal(1, m.Current);
        Assert.True(m.Blending);

        var q = Synthetic();
        q.Request(1021);
        Assert.True(q.Queue(101));                        // attendra la fin
        Assert.Equal(1021, q.Current);
        q.Update(0.6f);
        Assert.Equal(101, q.Current);
        // clip bouclant : Queue = immédiat
        var r = Synthetic(); r.Request(1); r.Queue(101);
        Assert.Equal(101, r.Current);
    }

    [Fact]
    public void ZeroDurationTransitionCutsAndMorphFreezesOutgoing()
    {
        var cut = Synthetic((_, _) => new KfmBlend(KfmTransitionType.Blend, 0f));
        cut.Request(1); cut.Request(101);
        Assert.False(cut.Blending);
        Assert.Single(cut.Layers);

        var morph = Synthetic((_, _) => new KfmBlend(KfmTransitionType.Morph, 0.2f));
        morph.Request(101); morph.Update(0.3f);
        morph.Request(1);
        var t0 = morph.Layers[0].SequenceTime;
        morph.Update(0.1f);
        Assert.Equal(t0, morph.Layers[0].SequenceTime, 5);            // sortant figé
        Assert.Equal(0.5f, morph.Layers[1].Weight, 3);

        var blend = Synthetic();
        blend.Request(101); blend.Update(0.3f); blend.Request(1);
        var b0 = blend.Layers[0].SequenceTime;
        blend.Update(0.05f);
        Assert.NotEqual(b0, blend.Layers[0].SequenceTime);            // sortant avance
    }

    [Fact]
    public void SpeedStartFractionAndReverseCycle()
    {
        var m = Synthetic();
        m.Speed = 2f;
        m.Request(101, startFraction: 0.5f);
        Assert.Equal(0.4f, m.Layers[0].SequenceTime, 4);
        m.Update(0.1f);                                   // +0,2 s
        Assert.Equal(0.6f, m.Layers[0].SequenceTime, 4);

        var rev = new KfmClip(5, "pp", 0f, 1f, KfCycle.Reverse);
        Assert.Equal(0.25f, rev.SequenceTime(1.75f), 4);   // ping-pong
        Assert.True(rev.Loops);
        Assert.False(Once(1, "x").Loops);
    }

    [Fact]
    public void SyntheticKfmTransitionsResolveDefaultsAndExplicitEntries()
    {
        var kfm = new KfmFile
        {
            DefaultSyncType = 1, DefaultNonSyncType = 0,
            DefaultSyncDuration = 0.25f, DefaultNonSyncDuration = 0.1f,
        };
        var a = new KfmAnim { Event = 1, KfPath = ".\\a.kf", Index = 0 };
        a.Transitions.Add(new KfmTransition(101, 5, 0f));
        a.Transitions.Add(new KfmTransition(201, 4, 0f));
        a.Transitions.Add(new KfmTransition(301, 2, 0.4f));
        kfm.Add(a);
        kfm.Add(new KfmAnim { Event = 101, KfPath = ".\\a.kf", Index = 1 });
        kfm.Add(new KfmAnim { Event = 201, KfPath = ".\\a.kf", Index = 2 });
        kfm.Add(new KfmAnim { Event = 301, KfPath = ".\\a.kf", Index = 3 });
        // pas de .kf : le chargeur renvoie null → aucun clip, mais Resolve marche
        var m = new KfmStateMachine(kfm, _ => null);
        Assert.Empty(m.Clips);
        Assert.Equal(new KfmBlend(KfmTransitionType.Blend, 0.1f), m.Resolve(1, 101));
        Assert.Equal(new KfmBlend(KfmTransitionType.Morph, 0.25f), m.Resolve(1, 201));
        Assert.Equal(new KfmBlend(KfmTransitionType.Crossfade, 0.4f), m.Resolve(1, 301));
        Assert.Equal(new KfmBlend(KfmTransitionType.Blend, 0.1f), m.Resolve(101, 1)); // absente
    }

    // ------------------------------ KFM réels --------------------------

    private static KfmStateMachine? Real(string rel)
    {
        var root = KfCorpus.Root();
        if (root == null) return null;
        var path = EdenClient.Vfs.EdenVfs.ResolveInsensitive(root, rel);
        if (path == null) return null;
        var dir = Path.GetDirectoryName(path)!;
        var kfm = KfmFile.Parse(File.ReadAllBytes(path));
        return new KfmStateMachine(kfm, kp =>
            KfCorpus.Resolve(dir, kp) is { } p ? KfFile.Parse(File.ReadAllBytes(p)) : null);
    }

    [Theory]
    [InlineData("char/b.kfm")]
    [InlineData("char/g.kfm")]
    [InlineData("monster/M014.kfm")]
    [InlineData("ride/R001.kfm")]
    public void RealKfmBuildsClipsAndAppliesDefaultBlend(string rel)
    {
        var m = Real(rel);
        if (m == null) return;
        Assert.True(m.Has(1));                            // stand01
        Assert.Equal("stand01", m.Clip(1)!.Name);
        Assert.True(m.Has(101));
        Assert.Equal("move01", m.Clip(101)!.Name);
        Assert.True(m.Clip(101)!.Loops);
        Assert.InRange(m.Clip(101)!.Duration, 0.3f, 1.2f);
        // matrice complète de type 5 → défaut non-sync du header (0,1 s)
        var b = m.Resolve(1, 101);
        Assert.Equal(KfmTransitionType.Blend, b.Type);
        Assert.Equal(0.1f, b.Duration, 3);
        m.Request(1); m.Update(1f);
        m.Request(101);
        m.Update(0.05f);
        Assert.Equal(2, m.Layers.Count);
        Assert.Equal(0.5f, m.Layers[1].Weight, 3);
        m.Update(0.1f);
        Assert.Single(m.Layers);
    }

    [Fact]
    public void RealAttackPlaysOnceThenReturnsToStand()
    {
        var m = Real("monster/M014.kfm");
        if (m == null) return;
        var atk = m.Clips.First(c => c.Name == "attack01");
        Assert.False(atk.Loops);
        Assert.Equal(1.333f, atk.Duration, 2);
        m.ReturnEvent = 1;
        m.Request(1);
        m.Request(atk.Event);
        for (var i = 0; i < 100; i++) m.Update(1f / 30f);  // 3,3 s
        Assert.Equal(1, m.Current);
        Assert.False(m.Blending);
    }
}
