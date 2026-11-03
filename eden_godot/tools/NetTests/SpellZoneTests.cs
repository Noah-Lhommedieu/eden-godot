using EdenClient.Combat;
using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Géométrie et routage des sorts de zone (SpellZone) — centre, rayon, axe, message émis.
/// Références natives : LoadAllSpellData @0x08380130, CastToLocation @0x08154210, CastToLife @0x081545a0,
/// CastToNowhere @0x08153990, CSpellToLocation::HitTest @0x084f9300.</summary>
public class SpellZoneTests
{
    [Theory]
    [InlineData(5, 0, CastShape.Ground)]    // 50622 Meteor-like : cercle au sol
    [InlineData(5, 1, CastShape.Direction)]
    [InlineData(5, 2, CastShape.Sector)]
    [InlineData(5, 3, CastShape.Cross)]
    [InlineData(4, 0, CastShape.Single)]    // 50027 : cible unique (+ splash serveur)
    [InlineData(4, 1, CastShape.Direction)] // 50646
    [InlineData(4, 2, CastShape.Sector)]    // 50663
    [InlineData(4, 3, CastShape.Cross)]     // 50652
    [InlineData(0, 0, CastShape.Self)]      // 50015 PBAoE centré lanceur
    [InlineData(0, 3, CastShape.Cross)]
    [InlineData(1, 0, CastShape.Self)]
    [InlineData(3, 0, CastShape.Self)]
    [InlineData(6, 0, CastShape.Single)]
    public void ShapeOf_PortsNativeSwitch(int p1, int sub, CastShape expected) =>
        Assert.Equal(expected, SpellZone.ShapeOf(p1, sub));

    [Theory]
    [InlineData(20, 10f)]
    [InlineData(25, 12f)]   // 12.5 tronqué (short)
    [InlineData(5, 2f)]     // 2.5 → 2
    [InlineData(1, 0f)]     // 0.5 → 0 : pas de zone
    [InlineData(0, 0f)]
    public void AreaRadius_IsHalfTruncated(int col29, float expected) =>
        Assert.Equal(expected, SpellZone.AreaRadius(col29));

    [Fact]
    public void Axis_MatchesEdenDirConvention()
    {
        // dir 0 = face au sud (−Y) ; dir π/2 (sens horaire vu du dessus) = ouest (−X).
        var (x0, y0) = SpellZone.Axis(0f);
        Assert.Equal(0f, x0, 4); Assert.Equal(-1f, y0, 4);
        var (x1, y1) = SpellZone.Axis(MathF.PI / 2);
        Assert.Equal(-1f, x1, 4); Assert.Equal(0f, y1, 4);
        // aller-retour avec FromDelta
        var (ax, ay) = SpellZone.Axis(1.234f);
        Assert.Equal(1.234f, EdenDir.FromDelta(ax, ay), 4);
    }

    [Fact]
    public void AxisTo_NormalizesCasterToPoint_AndFallsBackOnCoincidence()
    {
        var (x, y) = SpellZone.AxisTo(10, 20, 13, 24, 0f);
        Assert.Equal(0.6f, x, 4); Assert.Equal(0.8f, y, 4);
        var (fx, fy) = SpellZone.AxisTo(5, 5, 5, 5, 0f);   // confondu → cap
        Assert.Equal(0f, fx, 4); Assert.Equal(-1f, fy, 4);
    }

    [Fact]
    public void InCircle_CenterOnPoint_NativeInclusiveRadius()
    {
        Assert.True(SpellZone.InCircle(100, 50, 10, 110, 50));   // pile sur le rayon
        Assert.False(SpellZone.InCircle(100, 50, 10, 110.01f, 50));
        Assert.True(SpellZone.InCircle(100, 50, 10, 100, 40));
    }

    [Fact]
    public void Plan_SelfAlwaysNowhere_EvenWithTarget()
    {
        var p = SpellZone.Plan(CastShape.Self, true, 50, 60, 1, 2, 0f, 30f);
        Assert.Equal(CastRoute.ToNowhere, p.Route);
        Assert.Equal((1f, 2f), (p.X, p.Y));
    }

    [Fact]
    public void Plan_GroundWithTarget_CentersOnTarget()
    {
        var p = SpellZone.Plan(CastShape.Ground, true, 50, 60, 1, 2, 0f, 30f);
        Assert.Equal(CastRoute.ToLocation, p.Route);
        Assert.Equal((50f, 60f), (p.X, p.Y));
    }

    [Fact]
    public void Plan_GroundWithoutTarget_AheadOfCasterOnFacingAxis_ClampedByReach()
    {
        // face au sud (dir 0) : devant = y décroissant
        var p = SpellZone.Plan(CastShape.Ground, false, 0, 0, 100, 200, 0f, 54f);
        Assert.Equal(CastRoute.ToLocation, p.Route);
        Assert.Equal(100f, p.X, 3);
        Assert.Equal(200f - SpellZone.GroundFallbackMax, p.Y, 3);
        var near = SpellZone.Plan(CastShape.Ground, false, 0, 0, 100, 200, 0f, 4f);
        Assert.Equal(196f, near.Y, 3);   // portée 4 < 8
    }

    [Theory]
    [InlineData(CastShape.Direction)]
    [InlineData(CastShape.Cross)]
    [InlineData(CastShape.Sector)]
    public void Plan_Directional_TargetUsesToLife_ElseLocationAlongFacing(CastShape s)
    {
        Assert.Equal(CastRoute.ToLife, SpellZone.Plan(s, true, 5, 5, 0, 0, 0f, 30f).Route);
        var p = SpellZone.Plan(s, false, 0, 0, 0, 0, MathF.PI / 2, 30f);
        Assert.Equal(CastRoute.ToLocation, p.Route);
        var (ax, ay) = SpellZone.AxisTo(0, 0, p.X, p.Y, 0f);
        Assert.Equal(-1f, ax, 4); Assert.Equal(0f, ay, 4);    // ouest, comme le cap
    }

    [Fact]
    public void Plan_Single_TargetToLife_ElseNowhere()
    {
        Assert.Equal(CastRoute.ToLife, SpellZone.Plan(CastShape.Single, true, 5, 5, 0, 0, 0f, 30f).Route);
        Assert.Equal(CastRoute.ToNowhere, SpellZone.Plan(CastShape.Single, false, 0, 0, 0, 0, 0f, 30f).Route);
    }

    [Theory]
    [InlineData(CastShape.Ground, true)]
    [InlineData(CastShape.Direction, true)]
    [InlineData(CastShape.Cross, true)]
    [InlineData(CastShape.Sector, true)]
    [InlineData(CastShape.Self, false)]
    [InlineData(CastShape.Single, false)]
    public void AcceptsLocation_OnlyAreaShapes(CastShape s, bool ok) =>
        Assert.Equal(ok, SpellZone.AcceptsLocation(s));

    [Fact]
    public void ClampToReach_KeepsInsideKeepsDirectionAndFlagsClamp()
    {
        var inside = SpellZone.ClampToReach(0, 0, 3, 4, 10);
        Assert.Equal((3f, 4f, false), inside);
        var (x, y, clamped) = SpellZone.ClampToReach(0, 0, 30, 40, 10);   // distance 50 → bornée à 10
        Assert.True(clamped);
        Assert.Equal(6f, x, 4); Assert.Equal(8f, y, 4);
    }
}
