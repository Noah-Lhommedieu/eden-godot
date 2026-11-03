using Eden.Core.Data.Parsers;
using Eden.Zone.Combat;
using Xunit;

namespace Eden.Tests;

/// <summary>Tests unitaires de <see cref="SpellAreaGeometry"/> — la
/// géométrie portée des HitTest natifs du binaire zone
/// (<c>CSpellToLocation::HitTest</c> @0x084f9300,
/// <c>CSpellToSector::HitTest</c> @0x084fb410,
/// <c>CSpellToDirection::HitTest</c> @0x084f6ad0,
/// <c>CSpellToCross::HitTest</c> @0x084f5020).</summary>
public class SpellAreaGeometryTests
{
    private static SpellParser.SpellDef Def(int duration = 0, int range = 0) =>
        new() { Duration = duration, Range = range };

    // ---------- Paramètres natifs ----------

    [Fact]
    public void AreaRadius_IsCol30HalvedTruncated()
    {
        // (short)(u16 col30 × 0.5f) — LoadAllSpellData @0x08380130.
        Assert.Equal(7f, SpellAreaGeometry.AreaRadius(Def(duration: 15)));
        Assert.Equal(5f, SpellAreaGeometry.AreaRadius(Def(duration: 11)));
        Assert.Equal(0f, SpellAreaGeometry.AreaRadius(Def(duration: 0)));
    }

    [Fact]
    public void AreaLength_IsCol28Halved()
    {
        // (float)(u16 col28 × _DAT_0887b5b0), _DAT_0887b5b0 = double 0.5.
        Assert.Equal(10f, SpellAreaGeometry.AreaLength(Def(range: 20)));
        Assert.Equal(0f, SpellAreaGeometry.AreaLength(Def(range: 0)));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(-4, -2)]
    [InlineData(-5, -2)]
    [InlineData(0, 0)]
    public void RadiusModHalf_TruncatesTowardZero(int mod, int expected)
    {
        // Natif : (short)(((mod>>15)&1) + mod) >> 1 ≡ trunc(mod/2).
        Assert.Equal(expected, SpellAreaGeometry.RadiusModHalf(mod));
    }

    [Fact]
    public void AreaEnumerationEnabled_RejectsSpecMinus91()
    {
        // +0x1a == -0x5b → HitTest cercle saute l'énumération.
        Assert.False(SpellAreaGeometry.AreaEnumerationEnabled(-91));
        Assert.True(SpellAreaGeometry.AreaEnumerationEnabled(-99));
    }

    [Fact]
    public void TryNormalizeAxis_UnitVector()
    {
        Assert.True(SpellAreaGeometry.TryNormalizeAxis(0, 0, 3, 4,
            out var ax, out var ay));
        Assert.Equal(0.6f, ax, 4); Assert.Equal(0.8f, ay, 4);
        Assert.False(SpellAreaGeometry.TryNormalizeAxis(1, 1, 1, 1,
            out _, out _));
    }

    // ---------- Forme 1 : cercle ----------

    [Fact]
    public void Circle_HitsInsideAndRejectsOutside()
    {
        Assert.True(SpellAreaGeometry.ContainsCircle(10, 10, 5, 10, 14));
        Assert.True(SpellAreaGeometry.ContainsCircle(10, 10, 5, 15, 10)); // bord inclus (≤)
        Assert.False(SpellAreaGeometry.ContainsCircle(10, 10, 5, 16, 10));
        Assert.False(SpellAreaGeometry.ContainsCircle(10, 10, 0, 10, 10));
    }

    // ---------- Forme 6 : secteur ----------

    [Fact]
    public void Sector_ConeApexAtCaster()
    {
        // cône +x : sommet (0,0), axe (1,0), L=10, R=5 → demi-angle 30°.
        Assert.True(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5, 8, 0));
        // sur l'arc à L
        Assert.True(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5, 10, 0));
        // au-delà de L
        Assert.False(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5, 11, 0));
        // derrière le lanceur
        Assert.False(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5, -1, 0));
    }

    [Fact]
    public void Sector_HalfAngleIsAsinOfROverL()
    {
        // L=10, R=5 → demi-angle asin(0.5)=30°. Point à 29° (≈8.75, 5.04)
        // dedans ; à 31° (≈8.57,5.15) dehors.
        var d = 10f;
        var rad29 = 29 * Math.PI / 180;
        var rad31 = 31 * Math.PI / 180;
        Assert.True(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5,
            (float)(d * Math.Cos(rad29)), (float)(d * Math.Sin(rad29))));
        Assert.False(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 5,
            (float)(d * Math.Cos(rad31)), (float)(d * Math.Sin(rad31))));
    }

    [Fact]
    public void Sector_RadiusCappedAtLength()
    {
        // R > L → R' = L → demi-angle 90° (asin(1)).
        Assert.True(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 20, 0, 9));
        Assert.False(SpellAreaGeometry.ContainsSector(0, 0, 1, 0, 10, 20, -1, 9));
    }

    [Fact]
    public void Sector_AxisRotated()
    {
        // cône vers -y : sommet (0,0), axe (0,-1), L=10, R=5.
        Assert.True(SpellAreaGeometry.ContainsSector(0, 0, 0, -1, 10, 5, 0, -9));
        Assert.False(SpellAreaGeometry.ContainsSector(0, 0, 0, -1, 10, 5, 0, 9));
    }

    // ---------- Forme 2 : direction ----------

    [Fact]
    public void Direction_BandFromCaster()
    {
        // bande le long de +x : origine (0,0), axe (1,0), len 20, demi-largeur 4.
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, 10, 0));
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, 10, 4));
        // le lanceur est inclus (bord 0)
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, 0, 0));
        // trop loin de l'axe
        Assert.False(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, 10, 4.1f));
        // avant le lanceur (l'onde part du lanceur, pas en arrière)
        Assert.False(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, -0.1f, 0));
        // après l'extrémité
        Assert.False(SpellAreaGeometry.ContainsDirection(0, 0, 1, 0, 20, 4, 21, 0));
    }

    [Fact]
    public void Direction_AxisRotated()
    {
        // bande le long de +y
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, 0, 1, 20, 4, 0, 15));
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, 0, 1, 20, 4, -4, 15));
        Assert.False(SpellAreaGeometry.ContainsDirection(0, 0, 0, 1, 20, 4, 4.5f, 15));
        // diagonale normalisée
        var s = MathF.Sqrt(0.5f);
        Assert.True(SpellAreaGeometry.ContainsDirection(0, 0, s, s, 10, 2, 5, 5));
    }

    // ---------- Forme 5 : croix ----------

    [Fact]
    public void Cross_TwoBandsCenteredOnCaster()
    {
        // croix centrée (0,0), axe (1,0), demi-longueur L=10, largeur R=6.
        // bras le long de l'axe
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 9, 0));
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, -9, 0));
        // bras perpendiculaire
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 0, 9));
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 0, -9));
        // coin de la croix — dans les deux bandes
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 2, 2));
        // diagonale hors des bandes
        Assert.False(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 8, 8));
        // au-delà de la demi-longueur sur un bras
        Assert.False(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 11, 0));
        Assert.False(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 0, -11));
        // hors demi-largeur (R/2 = 3)
        Assert.False(SpellAreaGeometry.ContainsCross(0, 0, 1, 0, 10, 6, 9, 3.5f));
    }

    [Fact]
    public void Cross_CentresOnCasterNotAimPoint()
    {
        // l'axe ne sert qu'à l'orientation — la croix est toujours
        // centrée sur le lanceur (ox,oy), pas sur le point visé.
        Assert.True(SpellAreaGeometry.ContainsCross(5, 5, 1, 0, 10, 6, 5, 14));
        Assert.False(SpellAreaGeometry.ContainsCross(5, 5, 1, 0, 10, 6, 16, 5));
    }

    [Fact]
    public void Cross_AxisRotated()
    {
        // croix orientée 45° : axe (√½,√½) — bras le long de la diagonale.
        var s = MathF.Sqrt(0.5f);
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, s, s, 10, 4, 7, 7));
        Assert.True(SpellAreaGeometry.ContainsCross(0, 0, s, s, 10, 4, -7, 7));
        Assert.False(SpellAreaGeometry.ContainsCross(0, 0, s, s, 10, 4, 9, 0));
    }
}
