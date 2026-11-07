using Xunit;
using Eden.Core.Game.Family;

namespace Eden.Tests;

/// <summary>
/// Tests des règles pures du cycle de vie des bâtiments de production
/// (lot E4) — chaque comportement renvoie à sa preuve native :
/// <list type="bullet">
/// <item>Usure : ToCombine/ToCollect (`rand &lt; chance` au start,
/// recettes non instantanées) vs EndCombine/EndCollect/EndSummon
/// (`rand &lt;= chance` à la fin, instantanées).</item>
/// <item>EXP bâtiment : <c>EndCombine</c> zone, formule
/// `(int)(rate × exp × 0.01) + exp` (DAT_087cc690 = 0.01).</item>
/// <item>Level-up : <c>CFamily::UpdateBuildingExp</c>/<c>
/// CheckBuildingLevelUp</c> mission (un seuil, template suivant,
/// level &lt; family.Level).</item>
/// <item>Taxe : <c>CheckToCombine</c> —
/// `gold + (int)(rate × gold × 0.01 + 0.5)`.</item>
/// <item>due_date : <c>CFamily::InsertItemToBuilding</c> mission +
/// check `OpenShop` (expiré → 0xc56).</item>
/// </list>
/// </summary>
public class FamilyBuildingWorkRulesTests
{
    /// <summary>Random dont Next(max) rend toujours la même valeur —
    /// simule le CRandom::GetShortRand(100) natif.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _v;
        public FixedRandom(int v) { _v = v; }
        public override int Next(int maxValue) => _v;
    }

    [Theory]
    [InlineData(0x18, true)]  // ferme
    [InlineData(0x1d, true)]  // mare magique
    [InlineData(0x17, false)]
    [InlineData(0x1e, false)]
    [InlineData(0, false)]
    public void StationType_Range(int type, bool expected) =>
        Assert.Equal(expected,
            FamilyBuildingWorkRules.IsStationType(type));

    [Theory]
    [InlineData(0x18, 0)]
    [InlineData(0x1d, 5)]
    public void StationIndex_Mapping(int type, int index) =>
        Assert.Equal(index,
            FamilyBuildingWorkRules.StationIndex(type));

    /// <summary>Forme de fin d'ordre (End* natif) : `rand &lt;= chance` —
    /// rand == chance usure quand même (le natif saute seulement si
    /// `chance &lt; rand`).</summary>
    [Fact]
    public void Wear_InclusiveAtBoundary()
    {
        Assert.True(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(50), 50, inclusive: true));
        Assert.False(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(51), 50, inclusive: true));
    }

    /// <summary>Forme de démarrage (ToCombine/ToCollect natif) :
    /// `rand &lt; chance` — rand == chance n'use pas.</summary>
    [Fact]
    public void Wear_StartExcludesBoundary()
    {
        Assert.False(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(50), 50, inclusive: false));
        Assert.True(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(49), 50, inclusive: false));
        Assert.True(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(99), 100, inclusive: false));
        Assert.False(FamilyBuildingWorkRules.RollWear(
            new FixedRandom(0), 0, inclusive: false));
    }

    /// <summary>Gain natif : `(int)(rate × exp × 0.01) + exp` —
    /// rate 0 (cas EDENFR courant, boosters bâtiment absents) rend
    /// l'EXP brute de la recette.</summary>
    [Fact]
    public void BuildingExpGain_Formula()
    {
        Assert.Equal(120,
            FamilyBuildingWorkRules.BuildingExpGain(0, 120));
        Assert.Equal(180,
            FamilyBuildingWorkRules.BuildingExpGain(50, 120));
        Assert.Equal(0, FamilyBuildingWorkRules.BuildingExpGain(0, 0));
    }

    /// <summary>Taxe native : `tax = floor(rate × gold × 0.01 + 0.5)` —
    /// arrondi au plus proche, total = gold + tax.</summary>
    [Fact]
    public void TaxedGold_Rounding()
    {
        Assert.Equal(1050,
            FamilyBuildingWorkRules.TaxedGold(1000, 5));
        Assert.Equal(366,
            FamilyBuildingWorkRules.TaxedGold(333, 10)); // 33.8 → 33
        Assert.Equal(345,
            FamilyBuildingWorkRules.TaxedGold(329, 5));  // 16.45 → 16
        Assert.Equal(1000, FamilyBuildingWorkRules.TaxedGold(1000, 0));
        Assert.Equal(0, FamilyBuildingWorkRules.TaxedGold(0, 99));
    }

    /// <summary>due_date natif : seuls les NPC 0x2a/0x52 du conteneur 0
    /// avec une durée d'item &gt; 0 deviennent temporaires — les
    /// bâtiments 0x29 et les NPC permanents restent à 0.</summary>
    [Fact]
    public void DueDate_TemporaryNpcOnly()
    {
        const long now = 1_700_000_000;
        Assert.Equal(now + 3600,
            FamilyBuildingWorkRules.DueDateFor(0x2a, 3600, now));
        Assert.Equal(now + 300,
            FamilyBuildingWorkRules.DueDateFor(0x52, 300, now));
        Assert.Equal(0,
            FamilyBuildingWorkRules.DueDateFor(0x29, 3600, now));
        Assert.Equal(0,
            FamilyBuildingWorkRules.DueDateFor(0x2a, 0, now));
        Assert.Equal(0,
            FamilyBuildingWorkRules.DueDateFor(0x52, -5, now));
    }

    /// <summary>Check OpenShop natif : expiré = due_date ≠ 0 ET &lt;
    /// now ; 0 (permanent) n'expire jamais.</summary>
    [Fact]
    public void NpcExpiry_Check()
    {
        const long now = 1_700_000_000;
        Assert.False(FamilyBuildingWorkRules.IsNpcExpired(0, now));
        Assert.False(
            FamilyBuildingWorkRules.IsNpcExpired((int)now + 60, now));
        Assert.True(
            FamilyBuildingWorkRules.IsNpcExpired((int)now - 1, now));
    }

    /// <summary>UpdateBuildingExp : seuil 0 → le natif retourne sans
    /// rien écrire (ni l'EXP ni le changement).</summary>
    [Fact]
    public void ApplyExp_ZeroThresholdInert()
    {
        var r = FamilyBuildingWorkRules.ApplyBuildingExp(
            exp: 10, tid: 30000, level: 1, familyLevel: 5,
            threshold: 0, gain: 50, nextTid: 30001, nextLevel: 2);
        Assert.False(r.Changed);
        Assert.Equal(10, r.Exp);
        Assert.Equal(30000, r.Tid);
        Assert.Equal(1, r.Level);
    }

    /// <summary>Gain simple sans level-up : l'EXP se cumule.</summary>
    [Fact]
    public void ApplyExp_Accumulates()
    {
        var r = FamilyBuildingWorkRules.ApplyBuildingExp(
            exp: 10, tid: 30000, level: 1, familyLevel: 5,
            threshold: 100, gain: 50, nextTid: 30001, nextLevel: 2);
        Assert.True(r.Changed);
        Assert.False(r.LeveledUp);
        Assert.Equal(60, r.Exp);
        Assert.Equal(30000, r.Tid);
    }

    /// <summary>Level-up natif : un seul seuil soustrait, tid = template
    /// suivant, level = next.Level (pas level+1).</summary>
    [Fact]
    public void ApplyExp_LevelUpOneThreshold()
    {
        var r = FamilyBuildingWorkRules.ApplyBuildingExp(
            exp: 90, tid: 30000, level: 1, familyLevel: 5,
            threshold: 100, gain: 50, nextTid: 30001, nextLevel: 2);
        Assert.True(r.Changed);
        Assert.True(r.LeveledUp);
        Assert.Equal(40, r.Exp); // 140 - 100, une seule fois
        Assert.Equal(30001, r.Tid);
        Assert.Equal(2, r.Level);
    }

    /// <summary>Seuil atteint mais level == familyLevel → le natif
    /// banque l'EXP sans monter (level &lt; family.Level requis).</summary>
    [Fact]
    public void ApplyExp_CappedByFamilyLevel()
    {
        var r = FamilyBuildingWorkRules.ApplyBuildingExp(
            exp: 90, tid: 30000, level: 3, familyLevel: 3,
            threshold: 100, gain: 50, nextTid: 30001, nextLevel: 4);
        Assert.True(r.Changed);
        Assert.False(r.LeveledUp);
        Assert.Equal(140, r.Exp); // bankée
        Assert.Equal(30000, r.Tid);
        Assert.Equal(3, r.Level);
    }

    /// <summary>Pas de template suivant → pas de level-up (la fin de
    /// chaîne S_Buliding stocke building+0x30 = 0).</summary>
    [Fact]
    public void ApplyExp_NoNextTemplate()
    {
        var r = FamilyBuildingWorkRules.ApplyBuildingExp(
            exp: 90, tid: 30000, level: 1, familyLevel: 5,
            threshold: 100, gain: 50, nextTid: 0, nextLevel: 0);
        Assert.False(r.LeveledUp);
        Assert.Equal(140, r.Exp);
        Assert.Equal(30000, r.Tid);
    }

    /// <summary>CheckBuildingLevelUp : la ligne c_level[level+1]
    /// existe et l'EXP bankée atteint le seuil → rejoue
    /// UpdateBuildingExp avec gain 0 (level-up différé possible après
    /// la hausse de niveau famille).</summary>
    [Fact]
    public void Evaluate_RowExistsBankedExp()
    {
        var r = FamilyBuildingWorkRules.EvaluateLevelUp(
            exp: 140, tid: 30000, level: 3, familyLevel: 4,
            thresholdRowExists: true, threshold: 100,
            nextTid: 30001, nextLevel: 4);
        Assert.True(r.Changed);
        Assert.True(r.LeveledUp);
        Assert.Equal(40, r.Exp);
        Assert.Equal(30001, r.Tid);
        Assert.Equal(4, r.Level);
    }

    /// <summary>Ligne présente, EXP insuffisante → rien.</summary>
    [Fact]
    public void Evaluate_RowExistsBelowThreshold()
    {
        var r = FamilyBuildingWorkRules.EvaluateLevelUp(
            exp: 40, tid: 30000, level: 1, familyLevel: 5,
            thresholdRowExists: true, threshold: 100,
            nextTid: 30001, nextLevel: 2);
        Assert.False(r.Changed);
    }

    /// <summary>Ligne level+1 absente (plafond de table) : le natif
    /// synchronise tid/level sur le template suivant si le tid traîne.</summary>
    [Fact]
    public void Evaluate_MissingRowSyncsTemplate()
    {
        var r = FamilyBuildingWorkRules.EvaluateLevelUp(
            exp: 80, tid: 30000, level: 1, familyLevel: 5,
            thresholdRowExists: false, threshold: 0,
            nextTid: 30001, nextLevel: 2);
        Assert.True(r.Changed);
        Assert.False(r.LeveledUp); // sync, pas un level-up
        Assert.Equal(30001, r.Tid);
        Assert.Equal(2, r.Level);
        Assert.Equal(80, r.Exp);
    }

    /// <summary>Ligne absente ET pas de template suivant → rien
    /// (fin de chaîne en tête de table).</summary>
    [Fact]
    public void Evaluate_MissingRowNoNext()
    {
        var r = FamilyBuildingWorkRules.EvaluateLevelUp(
            exp: 80, tid: 30099, level: 13, familyLevel: 20,
            thresholdRowExists: false, threshold: 0,
            nextTid: 0, nextLevel: 0);
        Assert.False(r.Changed);
    }
}
