using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Game.Work;

namespace Eden.Tests;

/// <summary>Tests du roll `RandItemCombo` natif (zone @0x080a5480) :
/// pools par tier (col4), tirages par qualité, sans-remise flag==0,
/// pool General q6, groupe dominant → combo principal, vecteur 11.</summary>
public class ItemComboRollTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static ItemComboRollTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static List<CraftRecipeParser.ItemCombo> LoadCombos()
        => CraftRecipeParser.ParseItemCombo(
            Path.Combine(DbDir, "S_ItemCombo.ini"));

    private static CraftRecipeParser.ItemCombo C(int id, int attr,
        int group, int weight, int flag = 0,
        int minLv = 1, int maxLv = 99, params int[] kinds) =>
        new()
        {
            Id = id, AttrType = attr, Tier = group, Value = weight,
            Flag = flag, MinLevel = minLv, MaxLevel = maxLv,
            ApplicableItemTypes = kinds.ToList(),
        };

    // ---------------- Sélection counts ----------------

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 6)]
    [InlineData(5, 1)]
    [InlineData(6, 6)] // pas de tier2 → pool General vide, 3+3
    public void Roll_NbTirages_SelonQualite(int quality, int totalPicks)
    {
        // Assez de candidats dans les deux pools pour tous les tirages.
        var combos = Enumerable.Range(0, 12)
            .Select(i => C(100 + i, i % 2, 10 + i, 10, 0, 1, 99, 17))
            .ToList();
        var seq = new Queue<int>(Enumerable.Repeat(0, 64));
        var (main, extras) = ItemComboRollService.Roll(
            quality, 17, 10, combos, next: _ => seq.Dequeue());
        Assert.Equal(totalPicks, extras.Count(e => e != 0));
        if (totalPicks == 0) Assert.Equal(0, main);
    }

    [Fact]
    public void Roll_PoolEtNiveau_Filtres()
    {
        // kind 18 ne matche pas ; lv hors [5,7] exclu.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 0, 1, 99, 18),   // mauvais kind
            C(2, 0, 10, 10, 0, 5, 7, 17),    // hors niveau
            C(3, 0, 10, 10, 0, 1, 99, 17),   // OK pool0
            C(4, 1, 20, 10, 0, 1, 99, 17),   // OK pool1
        };
        var (_, extras) = ItemComboRollService.Roll(
            0, 17, 10, combos, next: _ => 0);
        Assert.Equal(3, extras[0]);
        Assert.Equal(4, extras[1]);
        Assert.Equal(0, extras[2]);
    }

    [Fact]
    public void Roll_Pondere_PremierCumulSup()
    {
        // pool0 : a(cum 5) b(cum 15) ; roll 4→a, 5→b, 14→b.
        // q3 → (1,0) : pool1 présente mais non tirée (obligatoire :
        // pool1 vide → sortie sans combo).
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 5, kinds: new[] { 17 }),
            C(2, 0, 10, 10, kinds: new[] { 17 }),
            C(9, 1, 20, 10, kinds: new[] { 17 }),
        };
        var (m1, e1) = ItemComboRollService.Roll(
            3, 17, 10, combos, next: _ => 4);
        Assert.Equal(1, e1[0]); Assert.Equal(10, m1);
        var (_, e2) = ItemComboRollService.Roll(
            3, 17, 10, combos, next: _ => 5);
        Assert.Equal(2, e2[0]);
    }

    [Fact]
    public void Roll_SansRemise_Flag0()
    {
        // q2 → 2 tirages pool0 + 2 pool1. flag0 → retrait : ids
        // distincts dans chaque pool.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 0, 1, 99, 17),
            C(2, 0, 10, 10, 0, 1, 99, 17),
            C(9, 1, 20, 10, 0, 1, 99, 17),
        };
        var (_, extras) = ItemComboRollService.Roll(
            2, 17, 10, combos, next: _ => 0);
        Assert.Equal(1, extras[0]);
        Assert.Equal(2, extras[1]); // 1 retiré → le suivant gagne
        Assert.Equal(9, extras[2]); // pool1 : seul candidat
        Assert.Equal(0, extras[3]); // pool1 épuisée après 1 retrait
    }

    [Fact]
    public void Roll_Flag1_Rejouable()
    {
        // flag≠0 : tirage consommé mais nœud conservé → le même combo
        // peut ressortir sur le tirage suivant.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 1, 1, 99, 17),
            C(9, 1, 20, 10, 0, 1, 99, 17),
        };
        var (_, extras) = ItemComboRollService.Roll(
            2, 17, 10, combos, next: _ => 0);
        Assert.Equal(1, extras[0]);
        Assert.Equal(1, extras[1]);
    }

    [Fact]
    public void Roll_Q6_PoolGeneral()
    {
        // tier2 ignoré hors q6 ; en q6 → tirage unique dans la pool
        // tier2 après les tirages General/PartKnock.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 0, 1, 99, 17),
            C(9, 1, 20, 10, 0, 1, 99, 17),   // PartKnock
            C(2, 2, 30, 10, 0, 1, 99, 17),
            C(3, 2, 30, 10, 0, 1, 99, 17),
        };
        var (_, e5) = ItemComboRollService.Roll(
            5, 17, 10, combos, next: _ => 0);
        Assert.Equal(1, e5.Count(e => e != 0));
        Assert.DoesNotContain(2, e5);
        var (_, e6) = ItemComboRollService.Roll(
            6, 17, 10, combos, next: _ => 0);
        // q6 → (3,3) + 1 tier2 ; pools 0/1 n'ont qu'1 candidat chacune
        // (flag0 → épuisées après 1 pick) → extras = [1, 9, 2|3].
        Assert.Equal(1, e6[0]);
        Assert.Equal(9, e6[1]);
        Assert.True(e6[2] is 2 or 3);
        Assert.Equal(3, e6.Count(e => e != 0));
    }

    [Fact]
    public void Roll_GroupeDominant_DevientMain()
    {
        // q4 → (3,3) : pool0 donne deux groupe 7 + un groupe 9 ;
        // pool1 donne un groupe 20 → main = 7 (majoritaire).
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 7, 10, 0, 1, 99, 17),
            C(2, 0, 9, 10, 0, 1, 99, 17),
            C(3, 0, 7, 10, 0, 1, 99, 17),
            C(9, 1, 20, 10, 0, 1, 99, 17),
        };
        var (main, extras) = ItemComboRollService.Roll(
            4, 17, 10, combos, next: _ => 0);
        Assert.Equal(7, main);
        Assert.Equal(4, extras.Count(e => e != 0));
    }

    [Fact]
    public void Roll_AucunCandidat_ToutZero()
    {
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 0, 1, 99, 50), // autre kind
        };
        var (main, extras) = ItemComboRollService.Roll(
            0, 17, 10, combos, next: _ => 0);
        Assert.Equal(0, main);
        Assert.All(extras, e => Assert.Equal(0, e));
    }

    [Fact]
    public void Roll_Pool1Vide_SortieSansCombo()
    {
        // Règle native : pool1 (« PartKnock ») vide → sortie
        // anticipée même si pool0 a des candidats.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            C(1, 0, 10, 10, 0, 1, 99, 17),
        };
        var (main, extras) = ItemComboRollService.Roll(
            0, 17, 10, combos, next: _ => 0);
        Assert.Equal(0, main);
        Assert.All(extras, e => Assert.Equal(0, e));
    }

    [Fact]
    public void Roll_VraiTable_Produit11()
    {
        var combos = LoadCombos();
        var (main, extras) = ItemComboRollService.Roll(
            0, 17, 3, combos, next: _ => 0);
        Assert.Equal(11, extras.Length);
        Assert.Contains(extras, e => e != 0);
        Assert.True(main > 0);
    }

    // ---------------- Éligibilité / intégration ----------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(0x10, false)] // exclu par EndCombine natif
    [InlineData(0x22, true)]
    [InlineData(0x23, false)]
    public void IsWorkComboEligible_Bornes(int kind, bool expected)
        => Assert.Equal(expected,
            ItemComboRollService.IsWorkComboEligible(kind));
}
