using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class TradeViewTests
{
    [Theory]
    [InlineData(true, false, false, "Trade complete!")]
    [InlineData(false, true, false, "Waiting for partner…")]
    [InlineData(false, false, true, "Partner locked — review then Lock/Ok")]
    [InlineData(false, true, true, "")]
    [InlineData(false, false, false, "")]
    public void Status(bool done, bool me, bool them, string key) =>
        Assert.Equal(key, TradeView.StatusKey(done, me, them));

    [Theory]
    [InlineData("1 500", 10_000, 1500u)]
    [InlineData("99999", 100, 100u)]
    [InlineData("abc", 100, 0u)]
    [InlineData("", 100, 0u)]
    [InlineData("99999999999999", long.MaxValue, uint.MaxValue)]
    [InlineData("12", -5, 0u)]
    public void ParseGold(string t, long max, uint expected) =>
        Assert.Equal(expected, TradeView.ParseGold(t, max));

    [Fact]
    public void NextSlot()
    {
        Assert.Equal(3, TradeView.NextSlot(3, 8));
        Assert.Equal(-1, TradeView.NextSlot(8, 8));
    }
}
