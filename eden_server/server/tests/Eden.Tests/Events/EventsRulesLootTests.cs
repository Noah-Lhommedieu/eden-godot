using Eden.Core.Game.Events;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Loot;
using Eden.Core.Game.Rules;

namespace Eden.Tests.Events;

/// <summary>Bus d'événements (Q5), moteur de règles (Q6) et butin composable (Q7).</summary>
public class EventsRulesLootTests
{
    [Fact]
    public void Bus_DeliversToTypedAndAllSubscribers_AndUnsubscribes()
    {
        var bus = new DomainEventBus(); var typed = 0; var all = 0;
        var sub = bus.Subscribe<MobKilled>(_ => typed++);
        bus.SubscribeAll(_ => all++);
        bus.Publish(new MobKilled(1, 5, 2)); bus.Publish(new LevelUp(1, 10));
        Assert.Equal((1, 2), (typed, all));
        sub.Dispose(); bus.Publish(new MobKilled(1, 5, 2));
        Assert.Equal(1, typed);
    }

    [Fact]
    public void Bus_FailingSubscriberDoesNotBreakOthersNorPublisher()
    {
        var bus = new DomainEventBus(); var ok = 0;
        bus.Subscribe<MobKilled>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<MobKilled>(_ => ok++);
        bus.Publish(new MobKilled(1, 5, 2));
        Assert.Equal(1, ok);
    }

    [Theory]
    [InlineData("eq", "5", 5, true)] [InlineData("eq", "5", 6, false)] [InlineData("ne", "5", 6, true)]
    [InlineData("gt", "5", 6, true)] [InlineData("gt", "5", 5, false)] [InlineData("ge", "5", 5, true)]
    [InlineData("lt", "5", 4, true)] [InlineData("le", "5", 5, true)] [InlineData("le", "5", 6, false)]
    [InlineData("in", "1, 5,9", 5, true)] [InlineData("in", "1,9", 5, false)] [InlineData("zz", "5", 5, false)]
    [InlineData("eq", "abc", 5, false)]
    public void Condition_Operators(string op, string value, long fact, bool expected) =>
        Assert.Equal(expected, new Condition("X", op, value).Matches(new Dictionary<string, long> { ["X"] = fact }));

    [Fact]
    public void Condition_MissingFactIsFalse() =>
        Assert.False(new Condition("Nope", "eq", "1").Matches(new Dictionary<string, long>()));

    private const string Json = """
        [{"id":"r1","on":"MobKilled","if":[{"field":"NodeId","op":"eq","value":"2"},{"field":"MonsterId","op":"in","value":"5,6"}],
          "then":[{"action":"hit","args":{"n":"3"}}]},
         {"id":"r2","on":"LevelUp","then":[{"action":"hit"},{"action":"ghost"}]}]
        """;

    [Fact]
    public void Engine_FiresOnlyMatchingRules_AndPassesArgs()
    {
        var engine = new RuleEngine(); var hits = new List<string>();
        engine.RegisterAction("hit", (e, a) => hits.Add($"{e.Name}:{(a.TryGetValue("n", out var n) ? n : "-")}"));
        engine.SetRules(RuleEngine.ParseJson(Json));
        Assert.Equal(1, engine.Evaluate(new MobKilled(1, 5, 2)));
        Assert.Equal(0, engine.Evaluate(new MobKilled(1, 7, 2)));        // monstre hors liste
        Assert.Equal(0, engine.Evaluate(new MobKilled(1, 5, 3)));        // autre carte
        Assert.Equal(1, engine.Evaluate(new LevelUp(1, 9)));             // action inconnue journalisée, pas de crash
        Assert.Equal(new[] { "MobKilled:3", "LevelUp:-" }, hits);
    }

    [Fact]
    public void Engine_AttachedToBus_Runs()
    {
        var bus = new DomainEventBus(); var engine = new RuleEngine(); var n = 0;
        engine.RegisterAction("hit", (_, _) => n++);
        engine.SetRules(RuleEngine.ParseJson(Json)); engine.Attach(bus);
        bus.Publish(new LevelUp(1, 2));
        Assert.Equal(1, n);
    }

    [Theory]
    [InlineData("[{\"on\":\"X\"}]")] [InlineData("[{\"id\":\"a\"}]")]
    [InlineData("[{\"id\":\"a\",\"on\":\"X\",\"if\":[{\"field\":\"F\",\"op\":\"eq\"}]}]")]
    public void Engine_RejectsInvalidRules(string json) => Assert.Throws<FormatException>(() => RuleEngine.ParseJson(json));

    private static WeightedPool<int> Pool(params (int Id, double W)[] e) =>
        new(e.Select(x => new PoolEntry<int>(x.Id, x.W, 1, 1)));

    [Fact]
    public void Loot_ConditionGatesTheRule()
    {
        var rule = LootRule.Of(Pool((7, 1)));
        var gated = rule with { If = new[] { new Condition("Level", "ge", "50") } };
        var rng = new SeededRandom(1);
        Assert.Empty(gated.Roll(rng, new Dictionary<string, long> { ["Level"] = 49 }));
        Assert.Single(gated.Roll(rng, new Dictionary<string, long> { ["Level"] = 50 }));
    }

    [Fact]
    public void Loot_ChanceZeroNeverDrops_RollsMultiply_NestedAdds()
    {
        var facts = new Dictionary<string, long>(); var rng = new SeededRandom(3);
        Assert.Empty(LootRule.Of(Pool((1, 1)), chancePercent: 0).Roll(new SeededRandom(1), facts));
        Assert.Equal(3, LootRule.Of(Pool((1, 1)), 100, 3).Roll(rng, facts).Count);
        var nested = LootRule.Of(Pool((1, 1))) with { Nested = new[] { LootRule.Of(Pool((2, 1))), LootRule.Of(Pool((3, 1))) } };
        Assert.Equal(new[] { 1, 2, 3 }, nested.Roll(rng, facts).Select(d => d.Item).ToArray());
    }
}
