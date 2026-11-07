using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Work;

/// <summary>
/// Règle UNIQUE « quel item est le plan d'une recette » (PLAN-ACTION-3 §4.2),
/// utilisée par le serveur, le client (fenêtre de craft), le wiki et l'admin.
/// <para>Prouvée sur <c>c_familycombine.ini</c> (3 178 recettes) : en catégories
/// 3/4/5, le plan est le <b>premier matériau</b> (col15) dont l'item a un kind
/// 53 (formule d'alchimie), 54 (fonderie), 55 (mare magique / artisan) ou 56
/// (formule d'enchantement). Les catégories 6 (fragments de carte) et 7 n'ont
/// pas de plan. Pour les plans de récolte (<c>c_familycollect</c>) le plan est le
/// catalyseur, de kind 50 (ferme), 51 (mine) ou 52 (invocation).</para>
/// </summary>
public static class CraftPlanRules
{
    public const int KindFarmPlan = 50;
    public const int KindMinePlan = 51;
    public const int KindSummonPlan = 52;
    public const int KindAlchemyFormula = 53;
    public const int KindForgeFormula = 54;
    public const int KindCraftFormula = 55;
    public const int KindEnchantFormula = 56;

    /// <summary>Kind d'un plan de recette de fabrication (53-56).</summary>
    public static bool IsCombinePlanKind(int kind) =>
        kind is >= KindAlchemyFormula and <= KindEnchantFormula;

    /// <summary>Kind d'un plan de récolte (50-52).</summary>
    public static bool IsCollectPlanKind(int kind) =>
        kind is >= KindFarmPlan and <= KindSummonPlan;

    /// <summary>Id de l'item plan d'une recette de fabrication, ou null si la
    /// recette n'a pas de plan (fragments de carte, catégorie 7…).</summary>
    public static int? FindPlanItemId(WorkOrderParser.WorkCombineDef recipe,
        Func<int, int> kindOfItem)
    {
        if (recipe.Materials.Count == 0) return null;
        var first = recipe.Materials[0].ItemId;
        return first > 0 && IsCombinePlanKind(kindOfItem(first)) ? first : null;
    }

    /// <summary>Recettes fabriquées à partir de ce plan (réciproque).</summary>
    public static IEnumerable<WorkOrderParser.WorkCombineDef> RecipesOfPlan(
        IEnumerable<WorkOrderParser.WorkCombineDef> recipes, int planItemId) =>
        recipes.Where(r => r.Materials.Count > 0
            && r.Materials[0].ItemId == planItemId);

    /// <summary>Plans d'un produit : une recette par plan possible.</summary>
    public static IEnumerable<int> PlansProducing(
        IEnumerable<WorkOrderParser.WorkCombineDef> recipes, int productItemId,
        Func<int, int> kindOfItem) =>
        recipes.Where(r => r.ProductItemId == productItemId)
            .Select(r => FindPlanItemId(r, kindOfItem))
            .Where(p => p.HasValue).Select(p => p!.Value).Distinct();
}
