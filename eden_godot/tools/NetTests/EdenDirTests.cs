using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Convention dir = 3π/2 − atan2(Δy,Δx) (ChangeFaceDir natif,
/// zone @0x0847a3b0, constante @0x08877088 = 4.71238898).</summary>
public class EdenDirTests
{
    [Theory]
    [InlineData(1f, 0f)]    // vers +X
    [InlineData(0f, 1f)]    // vers +Y
    [InlineData(-1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(1f, 1f)]
    public void FromDelta_ToYaw_FaceLaCible(float dx, float dy)
    {
        var yaw = EdenDir.ToYaw(EdenDir.FromDelta(dx, dy));
        // forward godot = (−sinψ, −cosψ) ; eden = (x, −z).
        var fx = -MathF.Sin(yaw);
        var fy = MathF.Cos(yaw);
        var n = MathF.Sqrt(dx * dx + dy * dy);
        Assert.Equal(dx / n, fx, 3);
        Assert.Equal(dy / n, fy, 3);
    }

    [Fact]
    public void ConstanteNative()
    {
        Assert.Equal(4.71238898f, EdenDir.C, 5);
        // dir 0 = face au sud (−Y).
        Assert.Equal(0f, MathF.Cos(EdenDir.HeadingOf(0f)), 4);
        Assert.Equal(-1f, MathF.Sin(EdenDir.HeadingOf(0f)), 4);
    }

    [Theory]
    [InlineData(0f)] [InlineData(1.2f)] [InlineData(-2.5f)]
    public void Yaw_RoundTrip(float yaw)
    {
        var back = EdenDir.ToYaw(EdenDir.FromYaw(yaw));
        Assert.Equal(MathF.Cos(yaw), MathF.Cos(back), 3);
        Assert.Equal(MathF.Sin(yaw), MathF.Sin(back), 3);
    }
}
