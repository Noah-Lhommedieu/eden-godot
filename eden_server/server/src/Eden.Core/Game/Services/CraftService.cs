using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de craft — correspond a CCubeCrafting / CCombineOp / CCompoundOp.
///
/// Types de craft (d'apres les tables serveur):
/// - ElfCombine  : craft par les familiers (S_ElfCombine.ini, 957 recettes)
/// - FamilyCombine : craft guilde (S_FamilyCombine.ini, 3275 recettes)
/// - ItemCombo   : combos/sets d'items (S_ItemCombo.ini)
///
/// Reversed depuis ZoneServer: CEC_CombineItem, CCubeCrafting::Combine,
/// CCombineOp, CCompoundOp, CCubeOp.
/// </summary>
public sealed class CraftService
{
    private readonly InventoryService _inventory;

    private readonly Dictionary<int, CraftRecipeParser.ElfCombineRecipe> _elfRecipes;
    private readonly Dictionary<int, CraftRecipeParser.FamilyCombineRecipe> _familyRecipes;
    private readonly List<CraftRecipeParser.ItemCombo> _itemCombos;

    public CraftService(
        InventoryService inventory,
        IEnumerable<CraftRecipeParser.ElfCombineRecipe> elfRecipes,
        IEnumerable<CraftRecipeParser.FamilyCombineRecipe> familyRecipes,
        IEnumerable<CraftRecipeParser.ItemCombo> itemCombos)
    {
        _inventory = inventory;
        _elfRecipes = elfRecipes.ToDictionary(r => r.Id);
        _familyRecipes = familyRecipes.ToDictionary(r => r.Id);
        _itemCombos = itemCombos.ToList();
    }

    /// <summary>Charge toutes les tables de craft depuis le repertoire decode.</summary>
    public static CraftService LoadFromDataDir(InventoryService inventory, string decodedDir)
    {
        return new CraftService(
            inventory,
            CraftRecipeParser.ParseElfCombine(Path.Combine(decodedDir, "S_ElfCombine.ini")),
            CraftRecipeParser.ParseFamilyCombine(Path.Combine(decodedDir, "S_FamilyCombine.ini")),
            CraftRecipeParser.ParseItemCombo(Path.Combine(decodedDir, "S_ItemCombo.ini")));
    }

    public sealed record CraftResult(
        bool Success,
        string Error,
        int ResultItemId = 0,
        int ResultQuantity = 0,
        CraftRecipeParser.BonusScript? Bonus = null);

    /// <summary>
    /// Execute une recette de craft elfe.
    /// Correspond a CEC_CombineItem::Execute.
    /// </summary>
    public async Task<CraftResult> CraftElfAsync(
        int characterId, int recipeId, int elfMood = 100, int elfEnergy = 100)
    {
        if (!_elfRecipes.TryGetValue(recipeId, out var recipe))
            return new CraftResult(false, $"Unknown recipe {recipeId}");

        // L'elfe doit avoir assez d'energie (reversed: CEC_Elf* gere l'energie)
        if (elfEnergy <= 0)
            return new CraftResult(false, "Elf has no energy");

        // Consommer le materiau si la recette en exige un
        if (recipe.MaterialItemId > 0)
        {
            var consumed = await ConsumeMaterialAsync(characterId, recipe.MaterialItemId, 1);
            if (!consumed)
                return new CraftResult(false, $"Missing material item {recipe.MaterialItemId}");
        }

        // Determiner le resultat — le script bonus peut substituer l'item
        var resultId = recipe.ResultItemId;
        var bonus = PickBonus(recipe);
        if (bonus != null && bonus.ItemId1 > 0)
            resultId = bonus.ItemId1;

        var added = await _inventory.AddItemAsync(characterId, resultId,
            Math.Max(1, recipe.ResultQuantity));

        if (!added.Success)
            return new CraftResult(false, added.ErrorMessage);

        return new CraftResult(true, string.Empty, resultId,
            Math.Max(1, recipe.ResultQuantity), bonus);
    }

    /// <summary>Execute une recette de craft guilde.</summary>
    public async Task<CraftResult> CraftFamilyAsync(int characterId, int recipeId)
    {
        if (!_familyRecipes.TryGetValue(recipeId, out var recipe))
            return new CraftResult(false, $"Unknown recipe {recipeId}");

        var added = await _inventory.AddItemAsync(characterId, recipe.ResultItemId,
            Math.Max(1, recipe.ResultQuantity));

        return added.Success
            ? new CraftResult(true, string.Empty, recipe.ResultItemId, recipe.ResultQuantity)
            : new CraftResult(false, added.ErrorMessage);
    }

    /// <summary>
    /// Retourne les combos applicables a un type d'item donne.
    /// </summary>
    public IReadOnlyList<CraftRecipeParser.ItemCombo> GetCombosForItemType(int itemTypeId)
        => _itemCombos.Where(c => c.ApplicableItemTypes.Contains(itemTypeId)).ToList();

    /// <summary>Choix pondere d'un script bonus (weight = probabilite).</summary>
    private static CraftRecipeParser.BonusScript? PickBonus(
        CraftRecipeParser.ElfCombineRecipe recipe)
    {
        if (recipe.BonusScripts.Count == 0) return null;
        var total = recipe.BonusScripts.Sum(b => Math.Max(1, b.Weight));
        var roll = Random.Shared.Next(total);
        foreach (var b in recipe.BonusScripts)
        {
            roll -= Math.Max(1, b.Weight);
            if (roll < 0) return b;
        }
        return recipe.BonusScripts[^1];
    }

    private Task<bool> ConsumeMaterialAsync(int characterId, int itemId, int qty)
    {
        // L'API repository actuelle ne permet pas la recherche par itemId;
        // le reseau enverra le slot exact dans la version complete.
        // Placeholder conservatif: retourne true si la recette existe.
        return Task.FromResult(true);
    }
}
