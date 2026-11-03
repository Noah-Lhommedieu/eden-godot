using Eden.Core.Game.Classes;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Zone;
using Eden.Zone.Combat;
using Xunit;

namespace Eden.Tests;

/// <summary>Vitesse de monture : calculée serveur UNIQUEMENT (296
/// Speeds[0]) — bonus de ride compté une seule fois.</summary>
public class RideSpeedTests
{
    private static readonly EnchantService.UseCommand[] RideEnchant =
    {
        new(2134, new double[] { 151010, 1 }),   // ride
        new(2051, new double[] { 75 }),          // +75 % vitesse
    };

    [Fact]
    public void PipelineCommands_DropsMovementCommand()
    {
        var kept = RideSpeedRules.PipelineCommands(RideEnchant);
        Assert.DoesNotContain(kept, c => c.CommandId == 2051);
        Assert.Contains(kept, c => c.CommandId == 2134);
    }

    [Fact]
    public void RideStage_DoesNotAddMovementToPipeline()
    {
        var c = new StatContribution();
        LicenseService.ApplyEnchantCommands(c,
            RideSpeedRules.PipelineCommands(RideEnchant));
        Assert.Equal(0, c.MoveSpeedPercent);
        // Contrôle : sans filtre le bonus passait dans le pipeline.
        var raw = new StatContribution();
        LicenseService.ApplyEnchantCommands(raw, RideEnchant);
        Assert.Equal(75, raw.MoveSpeedPercent);
    }

    [Fact]
    public void Movement_WithRide_IsBaseTimesOnePlusBonus()
    {
        var foot = new CombatantSnapshot();
        var mounted = new CombatantSnapshot { MoveSpeedBonusPct = 75 };
        var ratio = NativeCombatFormulas.Movement(mounted)
            / NativeCombatFormulas.Movement(foot);
        Assert.Equal(1.75f, ratio, 3);
        // MovementRate affiché = +75 %.
        Assert.Equal(75f, NativeCombatFormulas.MovementRate(mounted), 2);
    }

    [Fact]
    public void Movement_RideStageAndSnapshot_NotDoubleCounted()
    {
        // Snapshot = bonus RideStates (75) + contribution pipeline
        // (étape ride filtrée → 0) : multiplicateur 1.75 et non 2.5.
        var c = new StatContribution();
        LicenseService.ApplyEnchantCommands(c,
            RideSpeedRules.PipelineCommands(RideEnchant));
        var snap = new CombatantSnapshot
        {
            MoveSpeedBonusPct = 75,
            BonusMoveSpeedPct = c.MoveSpeedPercent,
        };
        var ratio = NativeCombatFormulas.Movement(snap)
            / NativeCombatFormulas.Movement(new CombatantSnapshot());
        Assert.Equal(1.75f, ratio, 3);
    }

    [Fact]
    public void Movement_BuffsCappedByMoveSpeedCap()
    {
        var snap = new CombatantSnapshot { BonusMoveSpeedPct = 5000 };
        var cap = Eden.Core.Game.GameBalance.MoveSpeedCapPct / 100f;
        var ratio = NativeCombatFormulas.Movement(snap)
            / NativeCombatFormulas.Movement(new CombatantSnapshot());
        Assert.Equal(cap, ratio, 3);
    }

    [Fact]
    public void Movement_RideStacksOnTopOfSaturatedBuffs()
    {
        // Buff dev +200 % (satur le plafond) + monture +75 % : la monture
        // multiplie encore (×1.75 vs sans monture) au lieu d'être absorbée.
        var buffed = new CombatantSnapshot { BonusMoveSpeedPct = 5000 };
        var buffedRide = new CombatantSnapshot
        { BonusMoveSpeedPct = 5000, MoveSpeedBonusPct = 75 };
        var ratio = NativeCombatFormulas.Movement(buffedRide)
            / NativeCombatFormulas.Movement(buffed);
        Assert.Equal(1.75f, ratio, 3);
    }
}
