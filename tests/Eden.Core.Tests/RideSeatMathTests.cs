using System.Numerics;
using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

public class RideSeatMathTests
{
    // Rx(-90°) puis Ry(180°) : rotation d'import des GLB (Z-up).
    private static RideSeatMath.Basis3 Import()
    {
        var m = Matrix4x4.CreateRotationX(-MathF.PI / 2) *
                Matrix4x4.CreateRotationY(MathF.PI);
        // System.Numerics est en lignes : colonnes = transposée.
        return new(new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23),
            new(m.M31, m.M32, m.M33));
    }

    [Fact]
    public void IdentitySeat_GivesRiderBasisInverseOfImport()
    {
        // L17 identité dans le repère GLB → monde = import ; le cavalier
        // retombe sur l'identité (son propre import s'applique à ses nœuds).
        var imp = Import();
        var rb = RideSeatMath.RiderBasis(imp, imp);
        Assert.True(Vector3.Distance(rb.X, Vector3.UnitX) < 1e-5);
        Assert.True(Vector3.Distance(rb.Y, Vector3.UnitY) < 1e-5);
        Assert.True(Vector3.Distance(rb.Z, Vector3.UnitZ) < 1e-5);
    }

    [Fact]
    public void RotatedSeat_RotatesRider()
    {
        // L17 tourné de 180° autour de Z GLB (R906) : le cavalier regarde
        // vers l'arrière de la monture.
        var imp = Import();
        var l = Matrix4x4.CreateRotationZ(MathF.PI);
        var lb = new RideSeatMath.Basis3(new(l.M11, l.M12, l.M13),
            new(l.M21, l.M22, l.M23), new(l.M31, l.M32, l.M33));
        var rb = RideSeatMath.RiderBasis(imp.Mul(lb), imp);
        // −Z (avant) du cavalier devient +Z (arrière monde).
        Assert.True(rb.Mul(-Vector3.UnitZ).Z > 0.99f);
    }

    [Fact]
    public void Origin_IsSeatOrigin_MinusDrop()
    {
        Assert.Equal(new Vector3(1, 2, 3),
            RideSeatMath.RiderOrigin(new(1, 2, 3), 0f));
        Assert.Equal(new Vector3(1, 1.9f, 3),
            RideSeatMath.RiderOrigin(new(1, 2, 3), 0.1f));
    }

    [Theory]
    [InlineData(0f, 0.233f, 1.985f, true)]    // r014
    [InlineData(-0.002f, -0.424f, 2.610f, true)] // r021 (mesh AABB faux)
    [InlineData(0f, 35f, 0f, false)]           // JEFF
    [InlineData(0f, 0f, 0f, false)]            // AR979 (origine)
    public void Plausible(float x, float y, float z, bool ok) =>
        Assert.Equal(ok, RideSeatMath.IsPlausibleSeat(new(x, y, z)));
}
