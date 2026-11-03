using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;

namespace Eden.Core.Game.Work;

/// <summary>
/// Station de combinaison (service 10, CCombineOp op 0x27) — portage
/// de <c>CItemFactory::CheckCombineItem</c> @0x809b570 et du roll de
/// <c>DoCombineItem</c> @0x80b0410 (zone/decompiled.c).
///
/// Distincte de <see cref="WorkOrderService"/> : la station ne prend
/// que les recettes S_FamilyCombine de <b>catégorie 6</b> (col1) à
/// exactement 4 matériaux, consomme au terme d'un cast de 40
/// unités×100 ms (enchant CombineItemID) et produit ×1 l'item col10
/// — pas de CWork/player_works.
///
/// Aucun I/O : le flux zone orchestre persistance et paquets.
/// </summary>
public sealed class CombineStationService
{
    /// <summary>Durée du cast natif : 0x28 unités d'enchant ×100 ms
    /// (EffectToLife(CombineItemID, 0x28) + 433 [10][0x28][0]).</summary>
    public const int CastUnits = 0x28;

    /// <summary>Champ kind du booster natif (+0x122 == 0x1f41) —
    /// aucun CChanceItem n'existe dans les données : accepté par id.</summary>
    public const int BoosterKind = 0x1f41;

    /// <summary>Textes natifs émis via ClientTextOutputIndex.</summary>
    public static class Texts
    {
        /// <summary>Recette non-catégorie-6 / kind mismatch (0x249).</summary>
        public const int BadRecipe = 0x249;
        /// <summary>Pas de slot libre alors qu'aucune pile ne se vide
        /// (0x4f9).</summary>
        public const int NoInventorySpace = 0x4f9;
    }

    /// <summary>Échec de validation — textId natif ou abort silencieux
    /// (natif : return 0 → 434 au client).</summary>
    public readonly record struct CombineCheck(bool Ok, int TextId);

    private readonly GameDataService _data;
    private readonly Random _rand;

    /// <summary>Station — tables combine chargées par GameDataService.</summary>
    public CombineStationService(GameDataService data, Random? rand = null)
    {
        _data = data;
        _rand = rand ?? new Random();
    }

    /// <summary>
    /// CheckCombineItem natif — recette catégorie 6 à 4 matériaux,
    /// catalyseur = matériau[0] à l'emplacement exact, booster
    /// optionnel kind 0x1f41, or ≥ col9, ≥1 slot libre sauf si une
    /// pile consommée sera vidée (bVar4).
    /// </summary>
    /// <param name="recipeId">u32 du paquet 170.</param>
    /// <param name="catalyst">item à (catBag, catSlot).</param>
    /// <param name="booster">item à (boostBag, boostSlot) ou null
    /// quand les locs sont -1.</param>
    /// <param name="items">inventaire sacs 0-3 (non équipé).</param>
    /// <param name="gold">or du perso (char+0x4b0).</param>
    /// <param name="freeSlots">slots libres bags 0-3.</param>
    public CombineCheck CheckCombine(int recipeId,
        ItemEntity? catalyst, ItemEntity? booster,
        IReadOnlyList<ItemEntity> items, int gold, int freeSlots)
    {
        // vfunc+0x178 = CCombineQuery — catégorie 6 seulement, et la
        // table exige exactement 4 entrées matériaux (vec +0x40).
        if (!_data.WorkCombineById.TryGetValue(recipeId, out var def)
            || def == null || def.Category != 6
            || def.Materials.Count != 4)
            return new(false, Texts.BadRecipe);
        var bag = items.Where(i => !i.IsEquipped).ToList();
        // CheckItemNum par matériau — agrégat par item_id.
        foreach (var m in def.Materials)
            if (bag.Where(i => i.ItemId == m.ItemId)
                    .Sum(i => i.Quantity) < m.Quantity)
                return new(false, 0);
        // Catalyseur = matériau[0] au loc exact, qté ≥ requise.
        var first = def.Materials[0];
        if (catalyst == null || catalyst.ItemId != first.ItemId
            || catalyst.Quantity < first.Quantity)
            return new(false, 0);
        // bVar4 — vrai tant qu'AUCUNE pile consommée ne sera vidée ;
        // alors le produit exige un slot libre (0x4f9).
        var needsSlot = true;
        foreach (var m in def.Materials)
            if (bag.Any(i => i.ItemId == m.ItemId
                    && i.Quantity <= m.Quantity))
                needsSlot = false;
        if (catalyst.Quantity == first.Quantity) needsSlot = false;
        // Booster CChanceItem kind 0x1f41, qty==1 → pile vidée aussi.
        if (booster != null)
        {
            if (booster.ItemId != BoosterKind) return new(false, 0);
            if (booster.Quantity == 1) needsSlot = false;
        }
        if (gold < def.Gold) return new(false, 0);
        if (needsSlot && freeSlots < 1)
            return new(false, Texts.NoInventorySpace);
        return new(true, 0);
    }

    /// <summary>Roll DoCombineItem natif — succès si rand%100 &lt;
    /// taux recette (col13) + bonus booster (+0x124, non mappé → 0).</summary>
    public bool RollSuccess(WorkOrderParser.WorkCombineDef def,
        int boosterBonus = 0)
        => _rand.Next(100) < def.SuccessRate + boosterBonus + ServerConfig.CraftSuccessBonusPct;

    /// <summary>Recette catégorie-6 valide ou null — même garde que
    /// le re-check de DoCombineItem.</summary>
    public WorkOrderParser.WorkCombineDef? GetRecipe(int recipeId)
        => _data.WorkCombineById.TryGetValue(recipeId, out var def)
            && def != null && def.Category == 6
            && def.Materials.Count == 4
            ? def : null;
}
