using Eden.Core.Data.Parsers;
using Eden.Core.Game.Work;

namespace Eden.Tests;

public class CraftPlanRulesTests
{
    private static WorkOrderParser.WorkCombineDef Recipe(int product, params int[] mats)
    {
        var r = new WorkOrderParser.WorkCombineDef { Id = 1, ProductItemId = product };
        foreach (var m in mats)
            r.Materials.Add(new WorkOrderParser.WorkCombineDef.Material { ItemId = m });
        return r;
    }

    private static int Kind(int id) => id switch { 100 => 54, 101 => 44, 102 => 53, 103 => 49, _ => 0 };

    [Fact]
    public void FirstMaterialOfPlanKind_IsThePlan() =>
        Assert.Equal(100, CraftPlanRules.FindPlanItemId(Recipe(9, 100, 101), Kind));

    [Fact]
    public void SecondPlanKind_IsJustAMaterial() =>
        Assert.Equal(100, CraftPlanRules.FindPlanItemId(Recipe(9, 100, 102), Kind));

    [Fact]
    public void NoPlan_WhenFirstMaterialIsPlain() =>
        Assert.Null(CraftPlanRules.FindPlanItemId(Recipe(9, 101, 100), Kind));

    [Fact]
    public void FragmentRecipe_HasNoPlan() =>
        Assert.Null(CraftPlanRules.FindPlanItemId(Recipe(9, 103, 101), Kind));

    [Fact]
    public void EmptyMaterials_HasNoPlan() =>
        Assert.Null(CraftPlanRules.FindPlanItemId(Recipe(9), Kind));

    [Fact]
    public void KindRanges()
    {
        Assert.All(new[] { 53, 54, 55, 56 }, k => Assert.True(CraftPlanRules.IsCombinePlanKind(k)));
        Assert.All(new[] { 44, 49, 57 }, k => Assert.False(CraftPlanRules.IsCombinePlanKind(k)));
        Assert.All(new[] { 50, 51, 52 }, k => Assert.True(CraftPlanRules.IsCollectPlanKind(k)));
    }

    [Fact]
    public void Reciprocal_FindsRecipesAndPlans()
    {
        var rs = new[] { Recipe(7, 100, 101), Recipe(8, 100), Recipe(7, 102, 101) };
        Assert.Equal(2, CraftPlanRules.RecipesOfPlan(rs, 100).Count());
        Assert.Equal(new[] { 100, 102 }, CraftPlanRules.PlansProducing(rs, 7, Kind).OrderBy(x => x));
    }

    /// <summary>Preuve sur les données réelles (ignoré si le corpus est absent) :
    /// 2 569 recettes ont exactement un matériau de kind 53-56 et c'est le 1ᵉʳ.</summary>
    [Fact]
    public void RealCorpus_PlanIsFirstMaterial_ForAllSinglePlanRecipes()
    {
        var db = Path.Combine(TestPaths.RepoRoot, "..", "corpus", "fr-full", "data", "db");
        var combine = Path.Combine(db, "c_familycombine.ini");
        var itemIni = Path.Combine(db, "c_item.ini");
        if (!File.Exists(combine) || !File.Exists(itemIni)) return;
        var kinds = ItemParser.Parse(itemIni).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Kind);
        int Kd(int id) => kinds.TryGetValue(id, out var k) ? k : 0;
        var recipes = WorkOrderParser.ParseCombine(combine);
        Assert.True(recipes.Count > 3000);
        var withOne = recipes.Where(r => r.Materials.Count(m => CraftPlanRules.IsCombinePlanKind(Kd(m.ItemId))) == 1).ToList();
        Assert.True(withOne.Count > 2000, $"seulement {withOne.Count} recettes à plan unique");
        Assert.All(withOne, r => Assert.NotNull(CraftPlanRules.FindPlanItemId(r, Kd)));
    }
}
