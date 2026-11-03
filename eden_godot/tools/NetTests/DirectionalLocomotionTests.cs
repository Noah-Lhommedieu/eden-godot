using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Anims/facing directionnels natifs (preuves :
/// docs/ANIMS-DIRECTIONNELLES.md).</summary>
public class DirectionalLocomotionTests
{
    [Theory]
    [InlineData(0f, 1f, 7)]    // W
    [InlineData(0f, -1f, 1)]   // S
    [InlineData(-1f, 0f, 3)]   // A
    [InlineData(1f, 0f, 5)]    // D
    [InlineData(-1f, 1f, 6)]   // W+A
    [InlineData(1f, 1f, 8)]    // W+D
    [InlineData(-1f, -1f, 0)]  // S+A
    [InlineData(1f, -1f, 2)]   // S+D
    [InlineData(0f, 0f, 4)]
    [InlineData(0.05f, -0.05f, 4)]
    public void Record_FromAxes(float lr, float fwd, int rec) =>
        Assert.Equal(rec, DirectionalLocomotion.Record(lr, fwd));

    [Theory]
    [InlineData(7, "move01")]
    [InlineData(6, "move01")]
    [InlineData(8, "move01")]
    [InlineData(3, "move02")]   // gauche = 102
    [InlineData(5, "move03")]   // droite = 103
    [InlineData(1, "move04")]   // arrière = 104
    [InlineData(0, "move04")]
    [InlineData(2, "move04")]
    [InlineData(4, "move01")]
    public void AnimFor_Record(int rec, string anim) =>
        Assert.Equal(anim, DirectionalLocomotion.AnimFor(rec));

    [Fact]
    public void KfmActions_Are101To104()
    {
        Assert.Equal(101, DirectionalLocomotion.KfmAction(7));
        Assert.Equal(102, DirectionalLocomotion.KfmAction(3));
        Assert.Equal(103, DirectionalLocomotion.KfmAction(5));
        Assert.Equal(104, DirectionalLocomotion.KfmAction(1));
    }

    [Fact]
    public void PureStrafeAndBack_KeepHeadingOnFoot()
    {
        foreach (var r in new[] { 1, 3, 5, 7 })
            Assert.Equal(0f, DirectionalLocomotion.FaceOffset(r, false));
    }

    [Fact]
    public void Diagonals_TurnQuarterPi_MovingStraightRelativeToBody()
    {
        const float q = MathF.PI / 4f;
        Assert.Equal(q, DirectionalLocomotion.FaceOffset(6, false), 4);
        Assert.Equal(-q, DirectionalLocomotion.FaceOffset(8, false), 4);
        Assert.Equal(-q, DirectionalLocomotion.FaceOffset(0, false), 4);
        Assert.Equal(q, DirectionalLocomotion.FaceOffset(2, false), 4);
    }

    [Fact]
    public void Mounted_PureStrafe_TurnsNinety_AndKeepsSideAnim()
    {
        Assert.Equal(MathF.PI / 2f,
            DirectionalLocomotion.FaceOffset(3, true), 4);
        Assert.Equal(-MathF.PI / 2f,
            DirectionalLocomotion.FaceOffset(5, true), 4);
        Assert.Equal("move02", DirectionalLocomotion.AnimFor(3, true));
        Assert.Equal("move03", DirectionalLocomotion.AnimFor(5, true));
    }

    [Theory]
    [InlineData(7, "move01")]
    [InlineData(3, "move02")]
    [InlineData(5, "move03")]
    [InlineData(1, "move04")]
    [InlineData(0x6a, "move01")]  // flag de ruée de mob
    [InlineData(255, "move01")]
    public void Remote_AnimFromFlag(int flag, string anim) =>
        Assert.Equal(anim, DirectionalLocomotion.AnimForFlag(flag));
}
