using Eden.Core.Game.Foundation;
using Eden.Core.Game.Gather;

namespace Eden.Tests.Gather;

public class GatherRulesTests
{
    // Pioche de niveau 30 (c_familycollect 245) : 36801 à 40 %, 36802 à 10 %, 36791 à 0,2 %.
    private static readonly GatherProduct[] Pickaxe30 =
    {
        new(36801, 40, 1), new(36802, 10, 1), new(36791, 0.2, 1),
    };

    [Theory]
    [InlineData(40, 4)] [InlineData(25, 4)] [InlineData(24.9, 2)]
    [InlineData(10, 2)] [InlineData(5, 2)] [InlineData(4.9, 1)] [InlineData(0.2, 1)]
    public void MaxQuantity_FollowsRarity(double rate, int max) =>
        Assert.Equal(max, GatherRules.MaxQuantityForRate(rate));

    [Fact]
    public void Tick_DistributionMatchesRates()
    {
        var pool = GatherRules.BuildPool(Pickaxe30);
        var rng = new SeededRandom(2026);
        var counts = new Dictionary<int, int>();
        const int n = 50000;
        for (var i = 0; i < n; i++)
        {
            var id = GatherRules.Tick(pool, rng)!.Value.ItemId;
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        // Poids 40 / 10 / 0,2 → 79,8 % / 19,9 % / 0,4 %.
        Assert.InRange(counts[36801] / (double)n, 0.77, 0.82);
        Assert.InRange(counts[36802] / (double)n, 0.18, 0.22);
        Assert.True(counts.GetValueOrDefault(36791) / (double)n < 0.01);
    }

    [Fact]
    public void Quantities_NeverExceedRarityCap()
    {
        var pool = GatherRules.BuildPool(Pickaxe30);
        var rng = new SeededRandom(5);
        for (var i = 0; i < 5000; i++)
        {
            var d = GatherRules.Tick(pool, rng)!.Value;
            var cap = d.ItemId switch { 36801 => 4, 36802 => 2, _ => 1 };
            Assert.InRange(d.Quantity, 1, cap);
        }
    }

    [Fact]
    public void RareProduct_AlwaysSingle()
    {
        var pool = GatherRules.BuildPool(new[] { new GatherProduct(1, 0.5, 1) });
        var rng = new SeededRandom(1);
        for (var i = 0; i < 200; i++) Assert.Equal(1, GatherRules.Tick(pool, rng)!.Value.Quantity);
    }

    [Fact]
    public void EmptyTool_NoDraw() =>
        Assert.Null(GatherRules.Tick(GatherRules.BuildPool(Array.Empty<GatherProduct>()), new SeededRandom(1)));

    [Fact]
    public void SameSeed_SameDraws()
    {
        var pool = GatherRules.BuildPool(Pickaxe30);
        var a = new SeededRandom(9); var b = new SeededRandom(9);
        for (var i = 0; i < 100; i++) Assert.Equal(GatherRules.Tick(pool, a), GatherRules.Tick(pool, b));
    }

    [Fact]
    public void Durability_OnePointPerTenSeconds_180PointsIsThirtyMinutes()
    {
        double carry = 0; var total = 0;
        for (var tick = 0; tick < 60; tick++) total += GatherRules.DurabilityCost(30, 1.0, ref carry);
        Assert.Equal(180, total);          // 60 ticks × 30 s = 30 min = 180 points
    }

    [Fact]
    public void Durability_VipMinus25Percent_NoRoundingLoss()
    {
        double carry = 0; var total = 0;
        for (var tick = 0; tick < 60; tick++) total += GatherRules.DurabilityCost(30, 0.75, ref carry);
        Assert.Equal(135, total);          // 180 × 0,75 : l'outil dure 40 min
    }

    [Fact]
    public void ErrorCodes_AllHaveFrenchText()
    {
        var cat = ErrorCatalog.LoadEmbedded();
        foreach (var r in Enum.GetValues<GatherStopReason>())
            Assert.True(cat.Has(GatherRules.ErrorCodeFor(r)));
    }

    [Fact]
    public void NoticeIds_AreStableAndDistinct()
    {
        var ids = new[] { GameErrors.GatherOutOfArea, GameErrors.GatherMoved, GameErrors.GatherToolBroken,
            GameErrors.GatherBagFull, GameErrors.GatherAlready }.Select(GatherRules.NoticeIdFor).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, ids);
        Assert.Equal(0, GatherRules.NoticeIdFor("AUTRE"));
    }

    [Fact]
    public void Pool_Probabilities_SumToOne() =>
        Assert.Equal(1.0, GatherRules.BuildPool(Pickaxe30).Probabilities().Sum(p => p.Probability), 6);

    [Fact]
    public void RealCorpus_PickaxeHasANonEmptyPool()
    {
        var f = Path.Combine(TestPaths.RepoRoot, "..", "corpus", "fr-full", "data", "db", "c_familycollect.ini");
        if (!File.Exists(f)) return;
        var rows = Eden.Core.Data.Parsers.WorkOrderParser.ParseCollect(f);
        var tool = rows.First(r => r.CatalystItemId == 36819);
        var pool = GatherRules.BuildPool(tool.Products.Select(p => new GatherProduct(p.ItemId, p.Rate, p.Quantity)));
        Assert.True(pool.Count >= 2);
    }
}
