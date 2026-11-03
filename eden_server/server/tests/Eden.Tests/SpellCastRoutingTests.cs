using Eden.Core.Data.Parsers;
using Eden.Zone.Combat;
using Xunit;

namespace Eden.Tests;

/// <summary>Tests unitaires de <see cref="SpellCastRouting"/> — la
/// table d'acceptation NCID↔forme et les règles de refus portées du
/// binaire zone (<c>CSpellFactory::CastToLife</c> @0x081545a0,
/// <c>CastToLocation</c> @0x08154210, <c>CastToNowhere</c> @0x08153990,
/// <c>CastToNearby</c> @0x081536d0, <c>CheckCanCast</c> @0x081526e0,
/// <c>CSpellToLife::CastTest</c> @0x084f04d0).</summary>
public class SpellCastRoutingTests
{
    private static SpellParser.SpellDef Def(int p1 = 4, int sub = 0,
        int p2 = 0, int type = 1, int range = 0, int duration = 0) =>
        new()
        {
            Param1 = p1, ShapeSub = sub, Param2 = p2, Type = type,
            Range = range, Duration = duration,
        };

    // ---------- Forme dérivée (LoadAllSpellData @0x08380130) ----------

    [Theory]
    [InlineData(5, 0, SpellCastRouting.ShapeGround)]
    [InlineData(5, 1, SpellCastRouting.ShapeDirection)]
    [InlineData(5, 2, SpellCastRouting.ShapeSector)]
    [InlineData(5, 3, SpellCastRouting.ShapeCross)]
    [InlineData(4, 0, SpellCastRouting.ShapeSingle)]
    [InlineData(4, 1, SpellCastRouting.ShapeDirection)]
    [InlineData(4, 2, SpellCastRouting.ShapeSector)]
    [InlineData(4, 3, SpellCastRouting.ShapeCross)]
    [InlineData(0, 0, SpellCastRouting.ShapeSelf)]
    [InlineData(0, 3, SpellCastRouting.ShapeCross)]
    [InlineData(1, 0, SpellCastRouting.ShapeSelf)]
    [InlineData(2, 0, SpellCastRouting.ShapeSelf)]
    [InlineData(3, 0, SpellCastRouting.ShapeSelf)]
    [InlineData(6, 0, SpellCastRouting.ShapeSingle)]
    [InlineData(9, 0, SpellCastRouting.ShapeSingle)]
    public void ShapeOf_PortsNativeSwitch(int p1, int sub, int expected) =>
        Assert.Equal(expected, SpellCastRouting.ShapeOf(Def(p1, sub)));

    [Fact]
    public void ShapeOf_LiveTableRows()
    {
        // 50015 « Attaque Cyclone » : S_Spell col21=0, col31=vide →
        // forme 3 (PBAoE auto-centrée, rayon = col30 10 × 0.5 = 5 u).
        Assert.Equal(SpellCastRouting.ShapeSelf,
            SpellCastRouting.ShapeOf(Def(p1: 0, sub: 0)));
        // 50663 « Grille Sonique » : col21=4, col31=2 → forme 6
        // (secteur) — pas un CastToNowhere.
        Assert.Equal(SpellCastRouting.ShapeSector,
            SpellCastRouting.ShapeOf(Def(p1: 4, sub: 2)));
        // 50022 « Perforation » : col21=4, col31=0 → forme 0 (cible).
        Assert.Equal(SpellCastRouting.ShapeSingle,
            SpellCastRouting.ShapeOf(Def(p1: 4, sub: 0)));
    }

    // ---------- Routage NCID ↔ forme (CSpellFactory) ----------

    [Theory]
    [InlineData(0, true)]   // cible unique
    [InlineData(1, false)]  // cercle au sol → 0x5aa
    [InlineData(2, true)]   // direction à la cible
    [InlineData(3, false)]  // auto-centrée → 0x5aa
    [InlineData(4, false)]
    [InlineData(5, true)]   // croix à la cible
    [InlineData(6, true)]   // secteur à la cible
    public void AcceptsToLife_Shapes0_2_5_6(int shape, bool ok) =>
        Assert.Equal(ok, SpellCastRouting.AcceptsToLife(shape));

    [Theory]
    [InlineData(0, false)]  // cible unique → 0x5ab
    [InlineData(1, true)]   // cercle au sol
    [InlineData(2, true)]
    [InlineData(3, false)]  // auto-centrée → 0x5ab
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void AcceptsToLocation_Shapes1_2_5_6(int shape, bool ok) =>
        Assert.Equal(ok, SpellCastRouting.AcceptsToLocation(shape));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]   // seule la forme 3 passe le 17
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void AcceptsToNowhere_OnlyShape3(int shape, bool ok) =>
        Assert.Equal(ok, SpellCastRouting.AcceptsToNowhere(shape));

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]   // seule la forme 4 passe le 59
    [InlineData(6, false)]
    public void AcceptsToNearby_OnlyShape4(int shape, bool ok) =>
        Assert.Equal(ok, SpellCastRouting.AcceptsToNearby(shape));

    [Fact]
    public void Accepts_FullNcidMatrix()
    {
        // 50015-like (forme 3) : NCID 17 accepté, 9/16 refusés.
        Assert.True(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToNowhere, SpellCastRouting.ShapeSelf));
        Assert.False(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLife, SpellCastRouting.ShapeSelf));
        Assert.False(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLocation, SpellCastRouting.ShapeSelf));
        // 50663-like (forme 6 secteur) : 9 ET 16 acceptés, 17 refusé —
        // le cast « sans cible » se fait via un point (NCID 16).
        Assert.True(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLife, SpellCastRouting.ShapeSector));
        Assert.True(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLocation, SpellCastRouting.ShapeSector));
        Assert.False(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToNowhere, SpellCastRouting.ShapeSector));
        // Forme 0 : seul le 9 — jamais de cast sans cible.
        Assert.True(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLife, SpellCastRouting.ShapeSingle));
        Assert.False(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToNowhere, SpellCastRouting.ShapeSingle));
        Assert.False(SpellCastRouting.Accepts(
            SpellCastRouting.NcidToLocation, SpellCastRouting.ShapeSingle));
        // NCID inconnu → toujours refusé.
        Assert.False(SpellCastRouting.Accepts(999, 0));
    }

    [Fact]
    public void RequiresLifeTarget_OnlyShape0()
    {
        for (var s = 0; s <= 6; s++)
            Assert.Equal(s == 0, SpellCastRouting.RequiresLifeTarget(s));
    }

    // ---------- Refus natifs (TextOutputIndex + 410) ----------

    [Fact]
    public void RefusalTextIndex_MapsNativeErrorCodes()
    {
        // 0x5aa / 0x5ab / 0x5ac vus dans les corps CastTo*.
        Assert.Equal(1450, SpellCastRouting.RefusalTextIndex(9));
        Assert.Equal(1451, SpellCastRouting.RefusalTextIndex(16));
        Assert.Equal(1452, SpellCastRouting.RefusalTextIndex(17));
        Assert.Equal(1452, SpellCastRouting.RefusalTextIndex(59));
    }

    // ---------- Cible joueur : spec +0x1a (CanCastTarget) ----------

    [Theory]
    [InlineData(-99, 1, false)]   // ennemi requis → joueur refusé
    [InlineData(-99, 2, false)]
    [InlineData(-95, 1, false)]   // conditionnel : hostile → ennemi
    [InlineData(-95, 2, true)]    // sinon ami → joueur admis
    [InlineData(-94, 1, false)]
    [InlineData(-94, 4, true)]
    [InlineData(-98, 1, true)]    // ami — joueur admis
    [InlineData(-96, 1, true)]    // équipe (+ soi) — contrôlé à part
    [InlineData(-97, 1, true)]
    [InlineData(-93, 1, true)]    // toute entité — default → 1
    [InlineData(-91, 1, true)]
    [InlineData(0, 1, true)]
    public void SpecAllowsPlayerTarget_CanCastTargetSemantics(
        int spec, int type, bool ok) =>
        Assert.Equal(ok,
            SpellCastRouting.SpecAllowsPlayerTarget(spec, type));

    [Theory]
    [InlineData(-96, true)]
    [InlineData(-97, true)]
    [InlineData(-98, false)]
    [InlineData(-99, false)]
    [InlineData(-91, false)]
    public void SpecNeedsTeam_Minus96Minus97(int spec, bool ok) =>
        Assert.Equal(ok, SpellCastRouting.SpecNeedsTeam(spec));

    // ---------- Portée CastTest (HowClose) ----------

    [Fact]
    public void LifeCastOuterRange_RangePlusModPlusNativeConstants()
    {
        // col28=20 → +0x40 = 10 u ; + attrMod(0) + 2.5 (DAT_08896c68)
        // + 1.0 de rayons d'entités (borne forfaitaire documentée).
        Assert.Equal(13.5f, SpellCastRouting.LifeCastOuterRange(
            Def(range: 20), 0));
        Assert.Equal(21.5f, SpellCastRouting.LifeCastOuterRange(
            Def(range: 20), 8));   // 10 + 8 + 2.5 + 1
        // col28 vide → portée 0 → il ne reste que les constantes.
        Assert.Equal(3.5f, SpellCastRouting.LifeCastOuterRange(
            Def(range: 0), 0));
    }
}
