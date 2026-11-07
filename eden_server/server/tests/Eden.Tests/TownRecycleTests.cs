using Eden.World;
using Xunit;

namespace Eden.Tests;

/// <summary>Règle d'échéance de CEventFamilyInstanceRecycleCheck::Execute
/// (mission @0x081e4040).</summary>
public class TownRecycleTests
{
    [Fact]
    public void TenOnline_StopsCheck()
    {
        var ticks = 5;
        Assert.Equal(TownRecycleService.Step.Stop,
            TownRecycleService.Evaluate(true, 10, ref ticks));
        Assert.Equal(5, ticks);
    }

    [Fact]
    public void FamilyMissing_StopsCheck()
    {
        var ticks = 0;
        Assert.Equal(TownRecycleService.Step.Stop,
            TownRecycleService.Evaluate(false, 0, ref ticks));
    }

    [Fact]
    public void FewOnline_Ticks_ThenRecyclesAt72()
    {
        var ticks = 0;
        for (var i = 1; i < 72; i++)
            Assert.Equal(TownRecycleService.Step.Tick,
                TownRecycleService.Evaluate(true, 9, ref ticks));
        Assert.Equal(TownRecycleService.Step.Recycle,
            TownRecycleService.Evaluate(true, 9, ref ticks));
        Assert.Equal(72, ticks);
    }
}
