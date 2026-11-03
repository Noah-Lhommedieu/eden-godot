using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Combos de certificats (C_EquipSet.ini réelle via
/// raw_assets — sets 79-93 = 5 branches × tailles 4/3/2 ;
/// skip si les assets sont absents).</summary>
public class EquipSetTests
{
    private static bool Loaded()
    {
        try { return EquipSetTable.QueryItem(
            new ushort[] { 33272, 33273 }) == 38167; }
        catch { return false; }
    }

    /// <summary>Set 89 (défense, 2 certs) → « STR specialization »
    /// 38167 — wiki FR : 2 certs Défense = FOR +2 %.</summary>
    [Fact]
    public void TwoDefenseCertsResolve()
    {
        if (!Loaded()) return;
        Assert.Equal(38167,
            EquipSetTable.QueryItem(new ushort[] { 33272, 33280 }));
    }

    /// <summary>Set 79 (défense, 4 certs) → « Shield Master » 38157
    /// (« Shield DEF +50%, and Max HP »).</summary>
    [Fact]
    public void FourDefenseCertsResolve()
    {
        if (!Loaded()) return;
        Assert.Equal(38157, EquipSetTable.QueryItem(
            new ushort[] { 33272, 33273, 33274, 33275 }));
    }

    /// <summary>Une moitié mélangeant branches → le plus grand
    /// sous-ensemble de la branche prioritaire (ordre des lignes :
    /// défense avant soin) — 2 défense + 2 soin = combo défense.</summary>
    [Fact]
    public void BestComboPicksBranchPriority()
    {
        if (!Loaded()) return;
        var combo = EquipSetTable.BestCombo(
            new ushort[] { 33272, 33273, 33308, 33309 });
        Assert.NotNull(combo);
        Assert.Equal(new ushort[] { 33272, 33273 }, combo);
        Assert.Equal(38167, EquipSetTable.QueryItem(combo));
    }

    /// <summary>3 certs même branche → combo 3 pièces (38162 pour la
    /// défense — « Deep Impact & Combat Cyclone DMG +10% ») et non
    /// le bonus 2 pièces (non cumulatif).</summary>
    [Fact]
    public void ThreeCertsBeatTwo()
    {
        if (!Loaded()) return;
        var combo = EquipSetTable.BestCombo(
            new ushort[] { 33272, 33273, 33274, 33308 });
        Assert.NotNull(combo);
        Assert.Equal(3, combo.Count);
        Assert.Equal(38162, EquipSetTable.QueryItem(combo));
    }

    /// <summary>Quatre certificats de branches distinctes → aucun
    /// combo.</summary>
    [Fact]
    public void MixedSinglesGiveNothing()
    {
        if (!Loaded()) return;
        Assert.Null(EquipSetTable.BestCombo(
            new ushort[] { 33272, 33281, 33290, 33308 }));
    }
}
