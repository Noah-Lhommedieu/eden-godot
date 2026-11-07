using Eden.Core.Game.Foundation;
using Eden.Core.Game.Rules;

namespace Eden.Core.Game.Loot;

/// <summary>
/// Règle de butin composable (PLAN-ACTION-3 Q7) : sous une <see cref="Condition"/> optionnelle, elle se déclenche avec
/// <see cref="ChancePercent"/> puis tire dans un <see cref="WeightedPool{T}"/> (<see cref="Rolls"/> fois) et/ou
/// déroule des règles imbriquées. Drops, coffres, récolte, loteries et récompenses d'éphéméride sont des
/// données de ce modèle unique.
/// </summary>
public sealed record LootRule(
    IReadOnlyList<Condition> If, double ChancePercent, int Rolls,
    WeightedPool<int>? Pool, IReadOnlyList<LootRule> Nested)
{
    /// <summary>Règle simple : toujours vraie, 100 %, un tirage dans le pool.</summary>
    public static LootRule Of(WeightedPool<int> pool, double chancePercent = 100, int rolls = 1) =>
        new(Array.Empty<Condition>(), chancePercent, rolls, pool, Array.Empty<LootRule>());

    /// <summary>Tire le butin ; <paramref name="facts"/> alimente les conditions (niveau, type de carte, VIP…).</summary>
    public List<PoolDraw<int>> Roll(IRandom rng, IReadOnlyDictionary<string, long> facts)
    {
        var res = new List<PoolDraw<int>>();
        if (!If.All(c => c.Matches(facts))) return res;
        if (ChancePercent < 100 && rng.NextDouble() * 100 >= ChancePercent) return res;
        for (var i = 0; i < Rolls && Pool != null; i++)
            if (Pool.Draw(rng) is { } d) res.Add(d);
        foreach (var n in Nested) res.AddRange(n.Roll(rng, facts));
        return res;
    }
}
