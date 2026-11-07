using Eden.Core.Game.Foundation;
using Eden.Core.Game.Gather;

namespace Eden.Tests.Gather;

public class GatherSessionTests
{
    private static GatherSession Make(DateTimeOffset t0) => new(36819, 1, GatherKind.Mining,
        GatherRules.BuildPool(new[] { new GatherProduct(36801, 40, 1), new GatherProduct(36802, 10, 1) }),
        100, 200, t0);

    [Fact]
    public void Kind_FromAreaType()
    {
        Assert.Equal(GatherKind.Mining, GatherKinds.FromAreaType(60));
        Assert.Equal(GatherKind.Farming, GatherKinds.FromAreaType(61));
        Assert.Equal(GatherKind.Crystal, GatherKinds.FromAreaType(62));
        Assert.Null(GatherKinds.FromAreaType(0));
    }

    [Fact]
    public void Movement_BeyondTolerance_IsDetected()
    {
        var s = Make(DateTimeOffset.UnixEpoch);
        Assert.False(s.HasMoved(100.2f, 200.2f));   // jitter
        Assert.True(s.HasMoved(101f, 200f));
        Assert.True(s.HasMoved(100f, 199f));
    }

    [Fact]
    public void ThirtySecondTick_CostsThreeDurability_AndDropsOneResource()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var s = Make(t0);
        var r = s.Tick(t0.AddSeconds(30), new SeededRandom(1), 180, 1.0);
        Assert.NotNull(r.Drop);
        Assert.Equal(3, r.DurabilityCost);
        Assert.Null(r.Stop);
        Assert.Equal(1, s.TicksDone);
    }

    [Fact]
    public void ToolBreaks_WhenDurabilityRunsOut_ButStillAwardsLastDrop()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var s = Make(t0);
        var r = s.Tick(t0.AddSeconds(30), new SeededRandom(1), 3, 1.0);
        Assert.NotNull(r.Drop);
        Assert.Equal(GatherStopReason.ToolBroken, r.Stop);
    }

    [Fact]
    public void FullToolLifetime_180Points_IsSixtyTicks_ThirtyMinutes()
    {
        var t = DateTimeOffset.UnixEpoch; var s = Make(t);
        var left = 180; var rng = new SeededRandom(3); var ticks = 0;
        while (true)
        {
            t = t.AddSeconds(30); ticks++;
            var r = s.Tick(t, rng, left, 1.0);
            left -= r.DurabilityCost;
            if (r.Stop != null) break;
            Assert.True(ticks < 100);
        }
        Assert.Equal(60, ticks);
        Assert.Equal(TimeSpan.FromMinutes(30), t - DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Vip_ToolLastsFortyMinutes()
    {
        var t = DateTimeOffset.UnixEpoch; var s = Make(t);
        var left = 180; var rng = new SeededRandom(3); var ticks = 0;
        while (true)
        {
            t = t.AddSeconds(30); ticks++;
            var r = s.Tick(t, rng, left, 0.75);
            left -= r.DurabilityCost;
            if (r.Stop != null) break;
            Assert.True(ticks < 200);
        }
        Assert.Equal(80, ticks);           // 80 × 30 s = 40 min
    }
}
