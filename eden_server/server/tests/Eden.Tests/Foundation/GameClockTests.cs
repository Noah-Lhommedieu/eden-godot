using Eden.Core.Game.Foundation;

namespace Eden.Tests.Foundation;

public class GameClockTests
{
    private static FakeGameClock At(string iso) =>
        new(DateTimeOffset.Parse(iso), new GameClockOptions(6, TimeZoneInfo.Utc));

    [Fact]
    public void ResetDay_ChangesAtSixOClock()
    {
        var c = At("2026-11-07T05:59:59Z");
        var before = c.ResetDayNow();
        c.Set(DateTimeOffset.Parse("2026-11-07T06:00:00Z"));
        Assert.Equal(before + 1, c.ResetDayNow());
        c.Set(DateTimeOffset.Parse("2026-11-07T06:00:01Z"));
        Assert.Equal(before + 1, c.ResetDayNow());
    }

    [Fact]
    public void ResetDay_SameDayFromSixToNextSix()
    {
        var c = At("2026-11-07T06:00:00Z");
        var d = c.ResetDayNow();
        c.Set(DateTimeOffset.Parse("2026-11-08T05:59:59Z"));
        Assert.Equal(d, c.ResetDayNow());
    }

    [Fact]
    public void NextReset_IsStrictlyAfter()
    {
        var c = At("2026-11-07T06:00:00Z");
        Assert.Equal(DateTimeOffset.Parse("2026-11-08T06:00:00Z"), c.NextResetNow());
        c.Set(DateTimeOffset.Parse("2026-11-07T03:00:00Z"));
        Assert.Equal(DateTimeOffset.Parse("2026-11-07T06:00:00Z"), c.NextResetNow());
    }

    [Fact]
    public void Advance_MovesTimeOnlyOnDemand()
    {
        var c = At("2026-11-07T10:00:00Z");
        var t = c.Now;
        Assert.Equal(t, c.Now);
        c.Advance(TimeSpan.FromHours(20));
        Assert.Equal(t.AddHours(20), c.Now);
    }

    [Fact]
    public void RealDays_AreIndependentFromReset()
    {
        var a = DateTimeOffset.Parse("2026-11-07T05:00:00Z");
        var b = DateTimeOffset.Parse("2026-11-08T07:00:00Z");
        Assert.Equal(1, GameClockExtensions.RealDaysBetween(a, b));
        var c = At("2026-11-07T05:00:00Z");
        Assert.NotEqual(c.ResetDay(a), c.ResetDay(a.AddHours(2)));
    }

    [Fact]
    public void ResetDay_HonoursTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(1), "t", "t");
        var c = new FakeGameClock(DateTimeOffset.Parse("2026-11-07T04:59:00Z"),
            new GameClockOptions(6, zone));
        var d = c.ResetDayNow();               // 05:59 locale
        c.Set(DateTimeOffset.Parse("2026-11-07T05:00:00Z")); // 06:00 locale
        Assert.Equal(d + 1, c.ResetDayNow());
    }
}
