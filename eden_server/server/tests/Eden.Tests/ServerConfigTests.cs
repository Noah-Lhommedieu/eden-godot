using Eden.Core.Game;
using Xunit;
namespace Eden.Tests;

public class ServerConfigTests
{
    [Fact]
    public void Parse_RatesEventsAndComments()
    {
        var s = ServerConfig.Parse(new[]
        {
            "ExpRate = 5 # commentaire", "WakenClassExpRate=7", "DropRate=2.5",
            "MultiStrokeMaxHits = 9", "MoveSpeedBasePct=150", "CraftSuccessBonusPct=10",
            "Event.Christmas = on", "Event.Christmas.ExpPct=20",
            "Event.Christmas.DropItem=50001:12.5:3", "Event.Halloween=off",
            "ExpRate=abc_ignored_not_here", "bogus line",
        });
        Assert.Equal(5, s.ExpRate);
        Assert.Equal(7, s.WakenClassExpRate);
        Assert.Null(s.ClassExpRate);
        Assert.Equal(2.5, s.DropRate);
        Assert.Equal(2, s.MultiStrokeMaxHits);   // clampé
        Assert.Equal(150, s.MoveSpeedBasePct);
        Assert.Equal(10, s.CraftSuccessBonusPct);
        Assert.True(s.Events["christmas"].Active);
        Assert.False(s.Events["Halloween"].Active);
        Assert.Equal(20, s.Events["Christmas"].ExpPct);
        Assert.Equal((50001, 12.5, 3), s.Events["Christmas"].Drops[0]);
    }
}
