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
    [InlineData(CastShape.Self, true)]     // 50015 PBAoE — cyclone au lanceur
    [InlineData(CastShape.Nearby, true)]
    [InlineData(CastShape.Single, false)]  // 50027 : impact sur la cible
    [InlineData(CastShape.Sector, false)]  // 50663 : cône depuis le lanceur
    [InlineData(CastShape.Ground, false)]
    [InlineData(CastShape.Direction, false)]
    [InlineData(CastShape.Cross, false)]
    public void SelfAnchored_OnlySelfCentered(CastShape s, bool expected) =>
        Assert.Equal(expected, SpellZone.SelfAnchored(s));

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
    public void Plan_GroundWithoutTarget_RefusedUnlessCasterCentered()
    {
        // Règle propriétaire : pas de cast ciblé sans cible. Cercle au sol
        // sans cible ni visée → refusé (la visée passe par GroundTargeter).
        var p = SpellZone.Plan(CastShape.Ground, false, 0, 0, 100, 200, 0f, 54f);
        Assert.Equal(CastRoute.Refused, p.Route);
        // Description « autour de l'invocateur » (Rayonnement de Guérison) :
        // centré sur le lanceur, sans cible.
        var c = SpellZone.Plan(CastShape.Ground, false, 0, 0, 100, 200, 0f, 54f,
            casterCentered: true);
        Assert.Equal(CastRoute.ToLocation, c.Route);
        Assert.Equal((100f, 200f), (c.X, c.Y));
    }

    [Theory]
    [InlineData(CastShape.Direction)]
    [InlineData(CastShape.Cross)]
    [InlineData(CastShape.Sector)]
    public void Plan_Directional_TargetUsesToLife_NoTargetRefusedUnlessCasterCentered(CastShape s)
    {
        Assert.Equal(CastRoute.ToLife, SpellZone.Plan(s, true, 5, 5, 0, 0, 0f, 30f).Route);
        Assert.Equal(CastRoute.Refused,
            SpellZone.Plan(s, false, 0, 0, 0, 0, MathF.PI / 2, 30f).Route);
        // « autour de lui / devant le lanceur » : NCID 16 le long du cap.
        var p = SpellZone.Plan(s, false, 0, 0, 0, 0, MathF.PI / 2, 30f, casterCentered: true);
        Assert.Equal(CastRoute.ToLocation, p.Route);
        var (ax, ay) = SpellZone.AxisTo(0, 0, p.X, p.Y, 0f);
        Assert.Equal(-1f, ax, 4); Assert.Equal(0f, ay, 4);    // ouest, comme le cap
    }

    [Theory]
    // (description FR réelle, abrégée, forme, attendu « se lance sans cible »)
    [InlineData("ATQ PHY +#29# points et frappe tous les ennemis à 3 m. alentour.", CastShape.Self, true)]       // 50015 Cyclone
    [InlineData("PV Max +10 % pour tous les membres du groupe dans un rayon 15 mètres.", CastShape.Self, true)]  // 50107 buff de groupe
    [InlineData("Rend #30# PV à tous les membres du groupe dans un rayon de 8 mètres autour de l'invocateur.", CastShape.Ground, true)] // 50109
    [InlineData("Inflige des DÉG Feu dans un rayon de 6 m.", CastShape.Ground, false)]                            // 50127 : visée au sol
    [InlineData("zone en éventail de 5 mètres autour de lui, les assommant", CastShape.Sector, true)]              // 50398 Onde de Choc
    [InlineData("zone en arc de cercle de 6 m. devant le lanceur.", CastShape.Sector, true)]                       // 58478
    [InlineData("Jeteur de sort téléporté 30 m en avant dans la direction choisie", CastShape.Direction, true)]    // 50385 Flash Gordon
    [InlineData("Inflige des DÉG aux cibles dans un éventail de 6 m. Arme requise : #18#", CastShape.Sector, false)] // 58353 Grille Sonique
    [InlineData("ennemis sur une ligne droite de 6 mètres devant la cible sélectionnée", CastShape.Direction, false)] // 50184 Purgatoire
    [InlineData("zone en croix de 4,5 m. ainsi que Traversombre", CastShape.Cross, false)]                         // 50469
    [InlineData("Ennemis téléportés près de la cible. DÉG à 15 m alentour.", CastShape.Ground, false)]             // « cible » prime sur « alentour »
    [InlineData("Inflige des DÉG de Nature aux cibles sélectionnées dans un rayon de 5 mètres.", CastShape.Single, false)] // 50027 Tornade
    [InlineData("Inflige +#B9$150# dégâts par seconde à un ennemi", CastShape.Single, false)]                      // 50022 Perforation
    [InlineData(null, CastShape.Sector, false)]
    public void CastableWithoutTarget_FollowsShapeAndDescription(string? desc, CastShape shape, bool ok) =>
        Assert.Equal(ok, SpellCastRule.CastableWithoutTarget(shape, desc));

    [Fact]
    public void Plan_Single_TargetToLife_ElseRefused()
    {
        // Forme 0 = cible unique : NCID 9 avec cible ; SANS cible le
        // natif n'a aucun message — CastToNowhere refuse la forme 0
        // (0x5ac→1452) et CastToLocation aussi (0x5ab→1451). Le client
        // bloque donc en local (prompt textindex 2195) au lieu
        // d'émettre un 17 voué au refus 410.
        Assert.Equal(CastRoute.ToLife, SpellZone.Plan(CastShape.Single, true, 5, 5, 0, 0, 0f, 30f).Route);
        Assert.Equal(CastRoute.Refused, SpellZone.Plan(CastShape.Single, false, 0, 0, 0, 0, 0f, 30f).Route);
    }

    [Fact]
    public void RequiresTarget_OnlySingleShape()
    {
        // Seule la forme 0 exige une entité — 1/2/5/6 passent par un
        // point (16), 3 par soi-même (17), 4 par le canal 59.
        foreach (var s in Enum.GetValues<CastShape>())
            Assert.Equal(s == CastShape.Single, SpellZone.RequiresTarget(s));
    }

    [Theory]
    [InlineData(CastShape.Self, true)]
    [InlineData(CastShape.Single, false)]
    [InlineData(CastShape.Ground, false)]
    [InlineData(CastShape.Direction, false)]
    [InlineData(CastShape.Cross, false)]
    [InlineData(CastShape.Sector, false)]
    [InlineData(CastShape.Nearby, false)]
    public void AcceptsNowhere_OnlyShapeThree(CastShape s, bool ok) =>
        Assert.Equal(ok, SpellZone.AcceptsNowhere(s));

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

    private static string? RepoRoot()
    {
        for (var d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
            if (Directory.Exists(Path.Combine(d, "corpus", "fr-full")) &&
                Directory.Exists(Path.Combine(d, "eden_server"))) return d;
        return null;
    }

    /// <summary>La liste serveur des formes directionnelles centrées lanceur
    /// (SpellCastRouting.CasterCenteredSpellIds) doit être EXACTEMENT ce que la
    /// règle de description client donne sur les données fr-full (formes 2/5/6).</summary>
    [Fact]
    public void CasterCenteredIds_MatchServerList()
    {
        var root = RepoRoot();
        if (root == null) return;   // arbre partiel : rien à comparer
        var db = Path.Combine(root, "corpus", "fr-full", "data", "db");
        var latin1 = System.Text.Encoding.Latin1;
        var text = File.ReadAllText(Path.Combine(db, "t_spell.ini"), latin1).Replace("\r", "");
        var desc = new Dictionary<int, string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     text, @"^(\d{5})\|([^|\n]*)\|([^|]*)\|$",
                     System.Text.RegularExpressions.RegexOptions.Multiline))
            desc[int.Parse(m.Groups[1].Value)] = System.Text.RegularExpressions.Regex.Replace(m.Groups[3].Value, @"\$\d+\$", "");
        var expected = new SortedSet<int>();
        foreach (var line in File.ReadLines(Path.Combine(db, "c_spell.ini"), latin1))
        {
            var f = line.Split('|');
            // Un nom Big5 peut contenir 0x7C : recoller les colonnes en trop (44 attendues).
            if (f.Length > 44) f = f[..16].Concat(new[] { string.Join("|", f[16..(17 + f.Length - 44)]) })
                .Concat(f[(17 + f.Length - 44)..]).ToArray();
            if (f.Length != 44 || !int.TryParse(f[0], out var id)) continue;
            int.TryParse(f[20], out var p1); int.TryParse(f[30], out var sub);
            var shape = SpellZone.ShapeOf(p1, sub);
            if (shape is CastShape.Direction or CastShape.Cross or CastShape.Sector &&
                desc.TryGetValue(id, out var d) && SpellCastRule.CasterCentered(d))
                expected.Add(id);
        }
        var src = File.ReadAllText(Path.Combine(root, "eden_server", "server", "src", "Eden.Zone",
            "Combat", "SpellCastRouting.cs"));
        var block = src[src.IndexOf("CasterCenteredSpellIds =", StringComparison.Ordinal)..];
        block = block[block.IndexOf('{', block.IndexOf("new HashSet<int>", StringComparison.Ordinal))..];
        block = block[..block.IndexOf("};", StringComparison.Ordinal)];
        var actual = new SortedSet<int>(System.Text.RegularExpressions.Regex.Matches(block, @"\d{5}")
            .Select(m => int.Parse(m.Value)));
        Assert.Equal(expected, actual);
        Assert.True(expected.Count >= 5, "règle trop stricte : liste vide ?");
    }
}
