using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;

namespace Eden.Tests;

/// <summary>Tests du roll `AddComboStrengthen` natif (zone @0x080a2160) :
/// premier slot libre 6..10, filtre kind/niveau/groupe/tier≤3/flag,
/// pick pondéré — contre la vraie table EDENFR S_ItemCombo.ini.</summary>
public class StrengthenComboTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static StrengthenComboTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static List<CraftRecipeParser.ItemCombo> LoadCombos()
        => CraftRecipeParser.ParseItemCombo(
            Path.Combine(DbDir, "S_ItemCombo.ini"));

    private static ItemEntity NewItem()
    {
        var e = new ItemEntity { Id = 1, ItemId = 100, CharacterId = 7 };
        for (var i = 0; i < 11; i++) e.SetExtraCombo(i, 0);
        return e;
    }

    [Fact]
    public void Roll_EcritPremierSlotLibre()
    {
        var combos = LoadCombos();
        var item = NewItem();
        // kind 17 (arme) dans la liste des lignes tier 0/1, reqLv 3-4.
        var id = StrengthenComboService.Roll(item, 17, 3, combos);
        Assert.True(id > 0);
        Assert.Equal(id, item.GetExtraCombo(6));
    }

    [Fact]
    public void Roll_SlotsPleins_RetourneZero()
    {
        var combos = LoadCombos();
        var item = NewItem();
        for (var i = 6; i <= 10; i++) item.SetExtraCombo(i, 1);
        Assert.Equal(0,
            StrengthenComboService.Roll(item, 17, 3, combos));
    }

    [Fact]
    public void Roll_SlotSuivant_SiPremierPris()
    {
        var combos = LoadCombos();
        var item = NewItem();
        item.SetExtraCombo(6, 9999); // id inexistant → groupe ignoré
        var id = StrengthenComboService.Roll(item, 17, 3, combos);
        Assert.True(id > 0);
        Assert.Equal(id, item.GetExtraCombo(7));
        Assert.Equal(9999, item.GetExtraCombo(6));
    }

    [Fact]
    public void Roll_ExcluIdDejaPose()
    {
        var combos = LoadCombos();
        var item = NewItem();
        var first = StrengthenComboService.Roll(item, 17, 3, combos);
        var second = StrengthenComboService.Roll(item, 17, 3, combos);
        Assert.True(first > 0 && second > 0);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Roll_ExcluGroupeDejaPose()
    {
        var combos = LoadCombos();
        var item = NewItem();
        // Poser un combo du groupe 1 en slot 6 → tous les combos du
        // groupe 1 deviennent exclus pour le slot 7.
        var g1 = combos.First(c => c.Flag == 0 && c.Tier == 1
            && c.AttrType <= 3 && c.ApplicableItemTypes.Contains(17));
        item.SetExtraCombo(6, g1.Id);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var probe = NewItem();
            probe.SetExtraCombo(6, g1.Id);
            var id = StrengthenComboService.Roll(probe, 17, 3, combos);
            Assert.True(id > 0);
            var rec = combos.First(c => c.Id == id);
            Assert.NotEqual(1, rec.Tier);
        }
    }

    [Fact]
    public void Roll_HorsPlageNiveau_RetourneZero()
    {
        var combos = LoadCombos();
        var item = NewItem();
        // reqLv très élevé : aucun combo tier≤3 ne couvre lv 999.
        Assert.Equal(0,
            StrengthenComboService.Roll(item, 17, 999, combos));
    }

    [Fact]
    public void Roll_KindHorsListe_RetourneZero()
    {
        var combos = LoadCombos();
        var item = NewItem();
        // kind 8000 : aucune ligne EDENFR ne le couvre.
        Assert.Equal(0,
            StrengthenComboService.Roll(item, 8000, 3, combos));
    }

    [Fact]
    public void Roll_TierSuperieur3_Exclus()
    {
        // Tous les combos AttrType 4-6 (magicbox) doivent être exclus.
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            new() { Id = 10, ApplicableItemTypes = { 17 },
                    AttrType = 5, MinLevel = 0, MaxLevel = 100,
                    Tier = 1, Value = 50, Flag = 0 },
            new() { Id = 11, ApplicableItemTypes = { 17 },
                    AttrType = 2, MinLevel = 0, MaxLevel = 100,
                    Tier = 3, Value = 50, Flag = 0 },
        };
        var item = NewItem();
        var id = StrengthenComboService.Roll(item, 17, 10, combos,
            _ => 0);
        Assert.Equal(11, id);           // seul le tier≤3 est éligible
        Assert.Equal(11, item.GetExtraCombo(6));
    }

    [Fact]
    public void Roll_FlagNonZero_SauteExclusions()
    {
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            // Même groupe, AttrType>3, id déjà posé — éligible car
            // flag ≠ 0 (désasm : la porte +0x1b saute tout le bloc).
            new() { Id = 20, ApplicableItemTypes = { 17 },
                    AttrType = 6, MinLevel = 0, MaxLevel = 100,
                    Tier = 9, Value = 10, Flag = 1 },
        };
        var item = NewItem();
        item.SetExtraCombo(6, 20);      // id déjà posé
        var id = StrengthenComboService.Roll(item, 17, 10, combos,
            _ => 0);
        Assert.Equal(20, id);
        Assert.Equal(20, item.GetExtraCombo(7));
    }

    [Fact]
    public void Roll_PickPondere_PremierCumul()
    {
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            new() { Id = 30, ApplicableItemTypes = { 17 },
                    AttrType = 0, MinLevel = 0, MaxLevel = 100,
                    Tier = 1, Value = 20, Flag = 0 },
            new() { Id = 31, ApplicableItemTypes = { 17 },
                    AttrType = 0, MinLevel = 0, MaxLevel = 100,
                    Tier = 2, Value = 5, Flag = 0 },
        };
        var item = NewItem();
        // total = 25 : roll < 20 → id30 ; roll ≥ 20 → id31.
        Assert.Equal(30, StrengthenComboService.Roll(item, 17, 10,
            combos, _ => 19));
        var item2 = NewItem();
        Assert.Equal(31, StrengthenComboService.Roll(item2, 17, 10,
            combos, _ => 20));
    }

    [Fact]
    public void Roll_GroupeZero_Exclu()
    {
        // Natif : le vecteur de groupes est complété par des 0 → un
        // combo groupe 0 ne peut JAMAIS être posé (flag==0).
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            new() { Id = 40, ApplicableItemTypes = { 17 },
                    AttrType = 0, MinLevel = 0, MaxLevel = 100,
                    Tier = 0, Value = 10, Flag = 0 },
        };
        var item = NewItem();
        Assert.Equal(0,
            StrengthenComboService.Roll(item, 17, 10, combos));
    }
}
