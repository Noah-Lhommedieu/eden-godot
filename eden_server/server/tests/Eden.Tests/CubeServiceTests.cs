using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Cube;

namespace Eden.Tests;

/// <summary>Tests du cube Horadric — parseur S_ItemChest.ini (format
/// natif CHoradricCubeQuery) et simulation CheckCubeCosts/ExecuteCubeOp
/// (désasm @0x80ba380/@0x80bceb0). Valeurs de référence = lignes
/// réelles du S_ItemChest décodé.</summary>
public class CubeServiceTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static CubeServiceTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static CubeService BuildService()
    {
        // S_ItemMall partage le schéma 78 cols — merge natif dans la
        // même map d'items (mats cash 40xxx requis par les recettes).
        var items = ItemParser.Parse(Path.Combine(DbDir, "S_Item.ini"))
            .Concat(ItemParser.Parse(
                Path.Combine(DbDir, "S_ItemMall.ini")))
            .GroupBy(i => i.Id).Select(g => g.First()).ToList();
        var enchants = EnchantParser.Parse(
            Path.Combine(DbDir, "S_Enchant.ini"));
        var svc = new CubeService(items, enchants);
        svc.LoadRecipes(ShopAndMiscParsers.ParseItemChests(
            Path.Combine(DbDir, "S_ItemChest.ini")));
        return svc;
    }

    /// <summary>Item 22154 — équipement 71-75 du groupe cube 122154
    /// (col75 de S_Item).</summary>
    private const int GroupWeapon = 22154;

    [Fact]
    public void ParseItemChests_LoadsNativeRecipes()
    {
        var recipes = ShopAndMiscParsers.ParseItemChests(
            Path.Combine(DbDir, "S_ItemChest.ini"));
        Assert.Equal(4671, recipes.Count);

        // Recette 52 réelle : main groupe 122154×1 + 22538×23 + 40473×1,
        // qualité aux 6, or 1754, taux 50, produit=aux (0), lv71, qté 1,
        // poids qualité {6:100}.
        var r = recipes.First(x => x.Id == 52);
        Assert.Equal(122154, r.ItemId);
        Assert.Equal(6, r.AuxQualityReq);
        Assert.Equal(1, r.MainAmount);
        Assert.Equal(1754, r.Gold);
        Assert.Equal(50, r.SuccessRate);
        Assert.Equal(0, r.ProductKey);
        Assert.Equal(71, r.RequiredLevel);
        Assert.Equal(1, r.ProductAmount);
        Assert.Equal(2, r.Subs.Count);
        Assert.Contains(r.Subs, s => s.ItemId == 22538 && s.Amount == 23);
        Assert.Contains(r.Subs, s => s.ItemId == 40473 && s.Amount == 1);
        Assert.Equal(100f, r.QualityWeights[6]);
    }

    [Fact]
    public void CubeGroups_IndexedFromItemColumn75()
    {
        var svc = BuildService();
        var field = svc.GetType().GetField("_groupToItems",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!;
        var map = (Dictionary<int, HashSet<int>>)field.GetValue(svc)!;
        Assert.True(map.Count > 0);
        Assert.Contains(GroupWeapon, map[122154]);
    }

    [Fact]
    public void CheckCosts_FailsWhenIngredientsMissing()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;
        // Un item hors requis, qualité aux respectée mais requis
        // non couverts → fail silencieux natif.
        var aux = new CubeService.PlacedItem(22537, 1, 6, false,
            svc.ItemDef(22537));
        var r = svc.CheckCosts(recipe, 100000, 80, 30,
            new List<CubeService.PlacedItem> { aux }, null);
        Assert.False(r.Ok);
    }

    [Fact]
    public void CheckCosts_SucceedsWithGroupMainAndAux()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;
        var placed = new List<CubeService.PlacedItem>
        {
            // Main satisfait par groupe : 22154 ∈ groupe 122154.
            new(GroupWeapon, 1, 0, false, svc.ItemDef(GroupWeapon)),
            new(22538, 23, 0, false, svc.ItemDef(22538)),
            new(40473, 1, 0, false, svc.ItemDef(40473)),
            // Aux qualité 6 requise (col5) — enregistré, pas consommé.
            new(22166, 1, 6, false, svc.ItemDef(22166)),
        };
        var r = svc.CheckCosts(recipe, 100000, 80, 30, placed, null);
        Assert.True(r.Ok);
        Assert.Equal(new[] { 1, 23, 1, 0 }, r.Consumed);
        Assert.Equal(30 + 3, r.FreeAfterConsume);
        Assert.Equal(new List<int> { 22166 }, r.AuxItemIds);
        Assert.False(r.ProductBound);   // inputs libres → produit libre
    }

    [Fact]
    public void CheckCosts_RejectsWrongAuxQuality()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;   // exige aux qualité 6
        var placed = new List<CubeService.PlacedItem>
        {
            new(GroupWeapon, 1, 0, false, svc.ItemDef(GroupWeapon)),
            new(22538, 23, 0, false, svc.ItemDef(22538)),
            new(40473, 1, 0, false, svc.ItemDef(40473)),
            new(22166, 1, 3, false, svc.ItemDef(22166)), // qualité 3 ≠ 6
        };
        Assert.False(svc.CheckCosts(recipe, 100000, 80, 30,
            placed, null).Ok);
    }

    [Fact]
    public void CheckCosts_FailsOnGoldAndLevel()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;
        var placed = new List<CubeService.PlacedItem>
        {
            new(GroupWeapon, 1, 0, false, svc.ItemDef(GroupWeapon)),
            new(22538, 23, 0, false, svc.ItemDef(22538)),
            new(40473, 1, 0, false, svc.ItemDef(40473)),
        };
        Assert.Equal(0x232, svc.CheckCosts(recipe, 1753, 80, 30,
            placed, null).FailTextId);          // or insuffisant
        Assert.False(svc.CheckCosts(recipe, 1754, 70, 30,
            placed, null).Ok);                   // niveau 70 < 71 requis
    }

    [Fact]
    public void CheckCosts_PartialStackKeepsSlot()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;
        var placed = new List<CubeService.PlacedItem>
        {
            new(GroupWeapon, 1, 0, false, svc.ItemDef(GroupWeapon)),
            new(22538, 100, 0, false, svc.ItemDef(22538)), // stack 100>23
            new(40473, 1, 0, false, svc.ItemDef(40473)),
        };
        var r = svc.CheckCosts(recipe, 100000, 80, 30, placed, null);
        Assert.True(r.Ok);
        Assert.Equal(23, r.Consumed[1]);
        Assert.Equal(30 + 2, r.FreeAfterConsume); // le stack reste
    }

    [Fact]
    public void ProductPool_AuxIdsWhenProductZero()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;   // ProductKey == 0
        var pool = svc.ProductPool(recipe, new List<int> { 22166, 22171 });
        Assert.Equal(new HashSet<int> { 22166, 22171 }, pool);
    }

    [Fact]
    public void ProductPool_DirectProductIsSingleton()
    {
        var svc = BuildService();
        // Recette 1019 : produit direct 23215 (<100000 → singleton).
        var recipe = svc.Recipe(1019)!;
        Assert.Equal(23215, recipe.ProductKey);
        var pool = svc.ProductPool(recipe, new List<int> { 1, 2 });
        Assert.Equal(new HashSet<int> { 23215 }, pool);
    }

    [Fact]
    public void ProductPool_GroupKeyExpandsViaColumn75()
    {
        var svc = BuildService();
        // Recette 1 : produit 200033 = id de GROUPE (≥100000) et
        // == mainId → pool = groupe − aux (désasm branche uVar26==key).
        var recipe = svc.Recipe(1)!;
        var field = svc.GetType().GetField("_groupToItems",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!;
        var map = (Dictionary<int, HashSet<int>>)field.GetValue(svc)!;
        var pool = svc.ProductPool(recipe, new List<int> { 1, 2 });
        Assert.True(pool.Count > 10);
        Assert.True(pool.IsSubsetOf(map[200033]));
    }

    [Fact]
    public void PickQuality_WeightedKey6Returns6()
    {
        var svc = BuildService();
        var recipe = svc.Recipe(52)!;   // poids {6:100} → toujours 6
        Assert.Equal(6, svc.PickQuality(recipe,
            new List<int> { 0 }, 22166));
    }

    [Fact]
    public void SplitStackAmount_PartsSumToTotal()
    {
        var svc = BuildService();
        for (var i = 0; i < 50; i++)
        {
            var parts = svc.SplitStackAmount(10, 9);
            Assert.Equal(10, parts.Sum());
            Assert.True(parts.Count <= 3);
            Assert.All(parts, p => Assert.True(p > 0));
        }
    }
}
