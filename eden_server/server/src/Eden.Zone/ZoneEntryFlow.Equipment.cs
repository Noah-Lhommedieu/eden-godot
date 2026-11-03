using Eden.Core.Game.Inventory;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Canonicalisation des anciennes locs d'équipement avant le 311
/// (entrée) et refresh UI — le conteneur doit utiliser les slots calculés
/// par le serveur natif, pas l'EquipType brut enregistré par d'anciennes
/// builds du port.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>
    /// Répare les lignes d'équipement actif dont `loc` n'est pas autorisé
    /// par leur `S_Item.EquipLoc`. Les serials/colonnes sont conservés;
    /// un item non équipable retourne dans un sac ouvert si une place existe.
    /// Aucun item n'est effacé si conflit ou inventaire plein.
    /// </summary>
    private async Task NormalizeEquipmentSlotsAsync(EntryState st,
        CancellationToken token)
    {
        if (st.CharId == 0) return;
        var charId = (int)st.CharId;
        var items = await _services.InventoryRepo
            .GetEquippedItemEntitiesAsync(charId);
        var occupied = items.Where(i => i.BagId == 0)
            .Select(i => i.Slot).ToHashSet();

        foreach (var item in items.Where(i => i.BagId == 0)
                     .OrderBy(i => i.Slot))
        {
            token.ThrowIfCancellationRequested();
            var def = _services.GameData.Items
                .FirstOrDefault(d => d.Id == item.ItemId);
            var allowed = NativeEquipmentSlots.AllowedLocations(
                def?.EquipLoc ?? 0);
            if (allowed.Contains(item.Slot)) continue;

            var target = allowed.FirstOrDefault(slot =>
                !occupied.Contains(slot), -1);
            var moved = false;
            if (target >= 0)
            {
                moved = await _services.InventoryRepo.SwapEquipmentAsync(
                    charId, 0, item.Slot, 0, target);
                if (moved)
                {
                    occupied.Remove(item.Slot);
                    occupied.Add(target);
                }
            }
            else if (await _services.InventoryRepo
                         .GetAnyAvailableSlotAsync(charId) is { } free)
            {
                moved = await _services.InventoryRepo.MoveToInventoryAsync(
                    charId, 0, item.Slot, free.BagId, free.Slot);
                if (moved) occupied.Remove(item.Slot);
            }

            EdenLog.Write(moved
                ? $"[Equipment] char={charId} item={item.ItemId} " +
                  $"loc={item.Slot} -> {(target >= 0 ? target : "inventory")}"
                : $"[Equipment] invalid legacy row retained char={charId} " +
                  $"item={item.ItemId} loc={item.Slot} equipType=" +
                  $"{def?.EquipLoc ?? 0}; no safe destination");
        }
    }
}
