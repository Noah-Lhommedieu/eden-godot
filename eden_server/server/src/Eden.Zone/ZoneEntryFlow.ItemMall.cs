using Eden.Core.Game.Mall;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Item mall côté zone — trois responsabilités natives :
/// <list type="bullet">
/// <item>ZW 24 <c>CNE_ZW_ZoneServerItemMallReceieveAttempt::Execute</c>
/// (@0x82ac7f0) : livraison d'un achat dans l'inventaire du perso
/// (AddCloneItemWorld / PutIntoContainer natif), texte 0x89
/// « %s x %d », puis réponse ZW 12 {result, receipt, char, serial,
/// create_time}.</item>
/// <item>CZ 70 <c>CNE_CZ_ZoneServerItemMallReceivableEnter</c> : pose
/// l'opération <c>CItemMallReceivableOp</c> (IsA = 0x2f) — marqueur
/// exclusif comme les autres ops (un seul pointeur natif).</item>
/// <item>CZ 83 <c>CNE_CZ_ZoneServerItemMallUseItem::Execute</c>
/// (@0x82…) : <c>UseItemParam(sel→conteneur, idx, slot, uiItemId,
/// params, -1,-1,-1)</c> — même pipeline d'usage que l'ItemUse 19,
/// avec sélecteur de conteneur sac(0)/équipement(1).</item>
/// </list>
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Type d'opération natif de <c>CItemMallReceivableOp::IsA</c>
    /// — posé dans <see cref="EntryState.ShopOpType"/> à la réception
    /// du CZ 70 (annulation = succès, pas d'effet).</summary>
    private const int ItemMallReceivableOpType = 0x2f;

    /// <summary>
    /// Livraison item mall — équivalent de
    /// <c>CNE_ZW_ZoneServerItemMallReceieveAttempt::Execute</c> : perso
    /// en zone (sinon 0x15), itemdata présente (sinon 0x18), slot libre
    /// (sinon 0x19), création + texte 0x89 + 274, réponse (serial,
    /// create_time) pour l'insert item_receipt du monde.
    /// </summary>
    public Task<(short Result, uint Serial, uint CreateTime)>
        HandleItemMallDeliveryAsync(uint receiptId, uint charId,
            ushort itemId, ushort itemQuantity, long itemColor,
            CancellationToken token)
        => RunOnLoopAsync(ct => HandleItemMallDeliveryCoreAsync(
            receiptId, charId, itemId, itemQuantity, itemColor, ct),
            token);

    /// <summary>Corps de la livraison — sur le fil de la boucle.</summary>
    private async Task<(short Result, uint Serial, uint CreateTime)>
        HandleItemMallDeliveryCoreAsync(uint receiptId, uint charId,
            ushort itemId, ushort itemQuantity, long itemColor,
            CancellationToken token)
    {
        // GetCharacterZone(charId) natif → NULL = le perso n'est pas
        // sur cette zone (code 0x15).
        var session = _services.Sessions.FindByCharacter((int)charId);
        if (session == null)
            return (ItemMallResult.CharNotInZone, 0, 0);

        // Lookup itemdata (arbre CZoneServer+0x110 natif) → 0x18.
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == itemId);
        if (def == null)
            return (ItemMallResult.ItemDefMissing, 0, 0);

        // GetEmptySlotNum/GetContainerWithEmptySlot natifs → inventaire
        // plein = 0x19 ; autre échec = 99. AddItemAsync couvre à la
        // fois AddCloneItemWorld (non-stackable) et PutIntoContainer
        // (stackable, fusion sur stack existant).
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var qty = Math.Max((int)itemQuantity, 1);
        var (ok, error, bag, slot) = await _services.Inventory
            .AddItemAsync((int)charId, itemId, qty, 0, 0, false, touched);
        if (!ok)
        {
            EdenLog.Write($"[Zone] ItemMall delivery char={charId} " +
                $"item={itemId}×{itemQuantity} : {error}");
            return (error?.Contains("slot", StringComparison
                .OrdinalIgnoreCase) == true
                ? ItemMallResult.InventoryFull
                : ItemMallResult.Failure, 0, 0);
        }

        // Notification merge-aware : 291 sur stack fusionné, 274 sur
        // nouveau slot (le 274 sur un slot occupé crashe le client —
        // même règle que les achats boutique).
        await NativeItemCodec.EmitItemSlotsAsync(session,
            _services.InventoryRepo, (int)charId, touched, token);

        // Relecture de la ligne créée/fusionnée — serial + create_time
        // alimentent item_receipt.unique_id/create_time (clone+4/+0x14).
        var created = bag != null && slot != null
            ? await _services.InventoryRepo.GetItemAsync(
                (int)charId, bag.Value, slot.Value)
            : null;
        // 146 — l'item livré compte pour ItemCollect.
        _services.NotifyItemChanged((int)charId, itemId);

        // Texte natif 0x89 « %s x %d » (vfunc char+0x58,
        // textid 0x89, item name × quantité).
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = 0x89,
            Field2 = $"{def.Name} x {itemQuantity}",
        }, token);

        EdenLog.Write($"[Zone] ItemMall delivery char={charId} " +
            $"item={itemId}×{itemQuantity} receipt={receiptId} " +
            $"-> bag={bag} slot={slot} serial={created?.Id}");
        return (ItemMallResult.Ok, (uint)(created?.Id ?? 0),
            (uint)(created?.CreateTime ?? 0));
    }
}
