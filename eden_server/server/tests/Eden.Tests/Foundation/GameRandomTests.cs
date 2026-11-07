using Eden.Core.Game.Foundation;

namespace Eden.Tests.Foundation;

public class GameRandomTests
{
    [Fact]
    public void SameSeed_SameSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        for (var i = 0; i < 50; i++) Assert.Equal(a.NextDouble(), b.NextDouble());
    }

    [Fact]
    public void Fork_IsDeterministicAndIndependent()
    {
        var root = new SeededRandom(7);
        var x1 = root.Fork("almanax:2026-11-07").NextDouble();
        var x2 = new SeededRandom(7).Fork("almanax:2026-11-07").NextDouble();
        var y = root.Fork("almanax:2026-11-08").NextDouble();
        Assert.Equal(x1, x2);
        Assert.NotEqual(x1, y);
    }

    [Fact]
    public void Chance_MatchesRate()
    {
        var r = new SeededRandom(1);
        var hits = Enumerable.Range(0, 20000).Count(_ => r.Chance(40));
        Assert.InRange(hits / 20000.0, 0.38, 0.42);
        Assert.False(r.Chance(0));
    }

    [Fact]
    public void PickIndex_FollowsWeights()
    {
        var r = new SeededRandom(3);
        var counts = new int[3];
        var w = new double[] { 1, 0, 3 };
        for (var i = 0; i < 20000; i++) counts[r.PickIndex(w)]++;
        Assert.Equal(0, counts[1]);
        Assert.InRange(counts[2] / (double)counts[0], 2.7, 3.3);
    }

    [Fact]
    public void PickIndex_NoPositiveWeight_ReturnsMinusOne() =>
        Assert.Equal(-1, new SeededRandom(1).PickIndex(new double[] { 0, -2 }));

    [Fact]
    public void Next_StaysInRange()
    {
        var r = new SeededRandom(9);
        for (var i = 0; i < 1000; i++) Assert.InRange(r.Next(5, 8), 5, 7);
    }
}
