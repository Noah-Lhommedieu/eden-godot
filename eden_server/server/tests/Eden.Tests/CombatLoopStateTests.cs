using System.Collections.Concurrent;
using Eden.Zone;

namespace Eden.Tests;

/// <summary>Régressions des générations de combat par monstre:
/// une seule boucle concurrente et invalidation des tâches périmées.</summary>
public sealed class CombatLoopStateTests
{
    /// <summary>Seule une boucle gagne la course; la mort invalide son jeton.</summary>
    [Fact]
    public void Mob_AllowsOneLoop_AndInvalidatesItOnDeath()
    {
        var mob = new ZoneLiveWorld.LiveMob
        {
            Handle = 0xffffa001,
            MobId = 56001,
            NodeId = 1,
            Hp = 100,
            MaxHp = 100,
        };
        mob.BeginCombat(200000004, 10, 20);

        var started = new ConcurrentBag<int>();
        Parallel.For(0, 32, _ =>
        {
            if (mob.TryStartCombatLoop(200000004) is { } generation)
                started.Add(generation);
        });

        Assert.Single(started);
        var generation = started.Single();
        Assert.True(mob.IsCombatLoopCurrent(generation,
            200000004));

        mob.MarkDead();

        Assert.False(mob.IsCombatLoopCurrent(generation,
            200000004));
        Assert.Null(mob.TryStartCombatLoop(200000004));
    }

    /// <summary>Un changement de cible invalide toute continuation de
    /// l'aggro précédente.</summary>
    [Fact]
    public void Mob_NewAggroTarget_InvalidatesPreviousGeneration()
    {
        var mob = new ZoneLiveWorld.LiveMob
        {
            Handle = 0xffffa002,
            MobId = 56002,
            NodeId = 1,
            Hp = 100,
            MaxHp = 100,
        };
        mob.BeginCombat(1, 10, 20);
        var previous = mob.TryStartCombatLoop(1);
        Assert.NotNull(previous);

        mob.BeginCombat(2, 11, 20);

        Assert.False(mob.IsCombatLoopCurrent(previous.Value, 1));
        Assert.Null(mob.TryStartCombatLoop(1));
        Assert.NotNull(mob.TryStartCombatLoop(2));
    }

    /// <summary>Le respawn purge le wind-up d'un sort temporisé — un
    /// blast schedulé ne doit pas rejaillir sur l'engagement suivant
    /// (génération déjà invalidée par CancelCombat).</summary>
    [Fact]
    public void Mob_Respawn_ClearsCastingUntil()
    {
        var mob = new ZoneLiveWorld.LiveMob
        {
            Handle = 0xffffa003,
            MobId = 56003,
            NodeId = 1,
            Hp = 100,
            MaxHp = 100,
        };
        mob.CastingUntil = DateTime.UtcNow.AddSeconds(5);

        mob.ResetCombatForRespawn();

        Assert.Equal(default, mob.CastingUntil);
        Assert.True(mob.Alive);
    }
}
