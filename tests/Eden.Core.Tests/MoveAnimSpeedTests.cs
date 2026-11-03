using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

public class MoveAnimSpeedTests
{
    [Theory]
    [InlineData(100f, 1f)]
    [InlineData(150f, 1.5f)]
    [InlineData(200f, 2f)]
    [InlineData(35f, 1f)]      // plancher
    [InlineData(900f, 3f)]     // plafond
    public void Mounted_ProportionalToPct(float pct, float expected)
        => Assert.Equal(expected, MoveAnimSpeed.Scale(pct, true), 3);

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(20f, 1f)]
    [InlineData(30f, 1.5f)]
    [InlineData(40f, 2f)]
    public void Foot_BaseAt20Pct(float pct, float expected)
        => Assert.Equal(expected, MoveAnimSpeed.Scale(pct, false), 3);

    [Fact]
    public void BonusFromServerMul()
    {
        Assert.Equal(75f, MoveAnimSpeed.BonusPct(1.75f), 3);
        Assert.Equal(1f, MoveAnimSpeed.Scale(float.NaN, true));
    }
}
