using Eden.Core.Game.Inventory;
using Eden.Core.Database.Repositories;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de l'inventaire — correspond à CInventory.
/// Gère les stacks (S_Item.ini col 19), les swaps de slots et l'auto-stack
/// (CItemFactory::CheckAutoStackItem).
/// </summary>
public sealed class InventoryService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly Dictionary<int, ItemParser.ItemDef> _itemDefs;
    /// <summary>Roll natif des sockets à la création
    /// (ChangeEmbeddedAmount @0x809baf0).</summary>
    private readonly Inlay.InlayService _inlay = new();
    private const int DefaultBagSlots = 20;

    /// <summary>Constructeur sans table d'items (tests, stacks = 99).</summary>
    public InventoryService(IInventoryRepository inventoryRepository)
        : this(inventoryRepository, null) { }

    public InventoryService(IInventoryRepository inventoryRepository,
        IEnumerable<ItemParser.ItemDef>? itemDefs)
    {
        _inventoryRepository = inventoryRepository;
        _itemDefs = itemDefs?.ToDictionary(d => d.Id) ?? new();
    }

    /// <summary>Stack max d'un item (99 par défaut si table absente).</summary>
    public int MaxStackOf(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var def) && def.MaxStack > 0
            ? def.MaxStack : 99;

    /// <summary>Stackable natif : non équipable (S_Item EquipLoc ≤ 0)
    /// et cap de pile &gt; 1 — la <c>durability</c> d'une arme est une
    /// usure, pas un count ; la règle doit rester identique à
    /// <see cref="RemoveItemAsync(int,int,int,int)"/>.</summary>
    public bool IsStackable(int itemId) =>
        MaxStackOf(itemId) > 1 &&
        (!_itemDefs.TryGetValue(itemId, out var d) || d.EquipLoc <= 0);

    /// <summary>Usure max S_Item d'un équipement — 0 si la définition
    /// est absente (miroir du <c>def?.MaxDurability ?? 0</c> utilisé
    /// à la création d'item dans <see cref="AddItemAsync"/>).</summary>
    public int MaxDurabilityOf(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var def) ? def.MaxDurability : 0;

    /// <summary>
    /// Ajoute un item à l'inventaire — auto-stack sur les stacks existants
    /// non pleins du même itemId, puis place le reste dans un slot libre.
    /// Correspond à CInventory::AddItem + CheckAutoStackItem.
    /// </summary>
    public async Task<(bool Success, string ErrorMessage, int? BagId, int? Slot)> AddItemAsync(
        int characterId,
        int itemId,
        int quantity,
        int preferredBagId = 0,
        int enchantLevel = 0,
        bool isBound = false,
        List<(int BagId, int Slot, bool Merged)>? touched = null)
    {
        var def = _itemDefs.TryGetValue(itemId, out var idef) ? idef : null;
        // Certificats de classe (kind 40) : jamais d'instance sac —
        // ils vivent dans player_license (+0xaf0 natif). Un cert en
        // inventory1 était sérialisé comme item normal → affiché en
        // anneau et crash client à la sérialisation (bug live).
        if (def?.Kind == 40)
            return (false,
                "certificat de classe — destination player_license, " +
                "pas inventory", null, null);
        var maxStack = MaxStackOf(itemId);
        // Seuls les non-équipables stackent (natif CheckAutoStackItem :
        // flag stack + cap — une arme a aussi une "durabilité">1 mais
        // ne fusionne jamais).
        var stackable = maxStack > 1 && (def?.EquipLoc ?? 0) <= 0;
        var remaining = quantity;

        // 1. Auto-stack : compléter les stacks existants du même item
        //    (même enchant/binding — un enchanté ne stack pas sur un
        //    normal) — dans TOUS les sacs ouverts, pas seulement le
        //    préféré (natif CheckAutoStackItem itère les conteneurs).
        var items = await _inventoryRepository.GetItemsByCharacterIdAsync(characterId);
        foreach (var stack in items.Where(i =>
                     i.ItemId == itemId &&
                     stackable &&
                     !i.IsEquipped && i.EnchantLevel == enchantLevel &&
                     i.IsBound == isBound && i.Quantity < maxStack))
        {
            var toAdd = Math.Min(remaining, maxStack - stack.Quantity);
            stack.Quantity += toAdd;
            // durability = count persisté (UpdateItemAsync n'écrit
            // pas Quantity) — sans ça le merge visuel ne survivait
            // pas au relog.
            stack.Durability = stack.Quantity;
            remaining -= toAdd;
            await _inventoryRepository.UpdateItemAsync(stack);
            touched?.Add((stack.BagId, stack.Slot, true));
            if (remaining == 0)
                return (true, string.Empty, stack.BagId, stack.Slot);
        }

        // 2. Slots libres pour le reste — sac préféré d'abord, puis
        //    débordement sur les autres sacs ouverts (natif
        //    GetContainerWithEmptySlot — un slot au-delà de la
        //    capacité affichée crashait le client, bug live).
        //    On retourne le DERNIER slot alloué : le 274
        //    ClientPutItemIntoInventory l'annonce au client qui place
        //    l'icône à cet emplacement.
        int? lastSlot = null;
        int? lastBag = null;
        var firstPass = true;
        while (remaining > 0)
        {
            // Sac préféré au premier passage, puis n'importe quel sac
            // ouvert (natif : les conteneurs sont itérés dans l'ordre).
            (int BagId, int Slot)? found = null;
            if (firstPass)
            {
                var s0 = await _inventoryRepository.GetAvailableSlotAsync(
                    characterId, preferredBagId);
                if (s0 != null) found = (preferredBagId, s0.Value);
            }
            firstPass = false;
            found ??= await _inventoryRepository
                .GetAnyAvailableSlotAsync(characterId);
            if (found == null)
                return (false, "Inventory full", null, null);
            var (bag, slot) = found.Value;

            var stackSize = stackable
                ? Math.Min(remaining, maxStack) : 1;
            var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Armement due_date à la création : natif
            // `CItemFactory::AddItem*` ne l'arme que si l'unité de
            // durée ∈ {4,5,6} (`itemdata+0x6c - 4 < 3`) — les montures
            // « N日/入手限時 » (col26=4-6) partent en compte à rebours
            // dès l'obtention ; les unités {1,2,3} s'arment au
            // premier usage/déplacement (ItemFactory @0x80901b2).
            var durSec = (def?.DurationUnit ?? 0) is >= 4 and <= 6
                ? def!.DurationSeconds : 0;
            // `durability` = count du stack pour les consommables,
            // durabilité courante (pleine) pour l'équipement — le
            // "100" forfaitaire historique affichait ×100 sur tous les
            // items créés (bug live).
            var initDur = stackable ? stackSize
                : Math.Max(def?.MaxDurability ?? 0, 1);
            var initMax = stackable ? maxStack
                : Math.Max(def?.MaxDurability ?? 0, 1);
            // Ouverture des sockets à la création — natif
            // ChangeEmbeddedAmount (CItemFactory::Clone → roll par
            // socket, stop au 1er échec) : armes toujours, armures
            // kinds 1-16 ; les slots non ouvertes restent −1.
            var newItem = new ItemEntity
            {
                CharacterId = characterId,
                BagId = bag,
                Slot = slot,
                ItemId = itemId,
                Quantity = stackable ? stackSize : 1,
                Durability = initDur,
                MaxDurability = initMax,
                IsEquipped = false,
                EnchantLevel = enchantLevel,
                IsBound = isBound,
                CreateTime = now,
                DueDate = durSec > 0 ? now + durSec : 0,
            };
            if (!stackable && def != null)
            {
                var (sockCount, sockIds) = _inlay.RollSockets(def);
                if (sockCount > 0)
                {
                    newItem.EmbeddedAmount = sockCount;
                    newItem.EmbeddedId1 = sockIds[0];
                    newItem.EmbeddedId2 = sockIds[1];
                    newItem.EmbeddedId3 = sockIds[2];
                    newItem.EmbeddedId4 = sockIds[3];
                    newItem.EmbeddedId5 = sockIds[4];
                    newItem.EmbeddedId6 = sockIds[5];
                }
            }
            await _inventoryRepository.AddItemAsync(newItem);
            lastBag = bag;
            lastSlot = slot;
            touched?.Add((bag, slot, false));
            remaining -= stackSize;
        }

        return (true, string.Empty, lastBag, lastSlot);
    }

    /// <summary>
    /// Réinsère un item à partir d'un snapshot — équivalent du
    /// `CItemDBO::GetBuyBackItem` natif : l'item racheté retrouve ses
    /// attributs exacts (sockets, combo, enchant, dates, liaison)
    /// au lieu du re-roll de <see cref="AddItemAsync"/>. Un seul
    /// stack — les stacks vendus partiels sont restaurés tels quels.
    /// </summary>
    /// <param name="quantity">quantité effectivement vendue
    /// (≤ stack du snapshot).</param>
    /// <returns>succès + emplacement d'insertion pour le 274.</returns>
    public async Task<(bool Success, string ErrorMessage, int? BagId,
        int? Slot)> RestoreItemAsync(int characterId,
        ItemEntity snapshot, int quantity)
    {
        var slot = await _inventoryRepository.GetAvailableSlotAsync(
            characterId, 0);
        if (slot == null)
            return (false, "Inventory full", null, null);

        // Stackable natif (non équipable + cap>1) : durability = count
        // restauré (vente partielle d'un stack). Équipement : l'usure
        // réelle du snapshot.
        var stackable = MaxStackOf(snapshot.ItemId) > 1 &&
            (!_itemDefs.TryGetValue(snapshot.ItemId, out var d) ||
             d.EquipLoc <= 0);
        var restored = new ItemEntity
        {
            CharacterId = characterId,
            BagId = 0,
            Slot = slot.Value,
            ItemId = snapshot.ItemId,
            Quantity = quantity,
            // durability porte le count sur les stacks (modèle natif),
            // l'usure réelle sur l'équipement.
            Durability = stackable ? quantity : snapshot.Durability,
            MaxDurability = snapshot.MaxDurability,
            IsEquipped = false,
            EnchantLevel = snapshot.EnchantLevel,
            IsBound = snapshot.IsBound,
            Identify = snapshot.Identify,
            CreateTime = snapshot.CreateTime,
            DueDate = snapshot.DueDate,
            Quality = snapshot.Quality,
            Magic = snapshot.Magic,
            MagicBoxEnchant1 = snapshot.MagicBoxEnchant1,
            MagicBoxEnchant2 = snapshot.MagicBoxEnchant2,
            MagicBoxEnchant3 = snapshot.MagicBoxEnchant3,
            MagicBoxEnergy = snapshot.MagicBoxEnergy,
            MagicBoxStarLevel = snapshot.MagicBoxStarLevel,
            EmbeddedAmount = snapshot.EmbeddedAmount,
            EmbeddedId1 = snapshot.EmbeddedId1,
            EmbeddedId2 = snapshot.EmbeddedId2,
            EmbeddedId3 = snapshot.EmbeddedId3,
            EmbeddedId4 = snapshot.EmbeddedId4,
            EmbeddedId5 = snapshot.EmbeddedId5,
            EmbeddedId6 = snapshot.EmbeddedId6,
            Combo = snapshot.Combo,
            Witchcraft = snapshot.Witchcraft,
        };
        for (var i = 0; i < snapshot.ExtraCombos.Length; i++)
            restored.ExtraCombos[i] = snapshot.ExtraCombos[i];

        await _inventoryRepository.AddItemAsync(restored);
        return (true, string.Empty, 0, slot.Value);
    }

    /// <summary>
    /// Supprime un item de l'inventaire.
    /// Correspond à CInventory::RemoveItem.
    /// </summary>
    public async Task<bool> RemoveItemAsync(int characterId, int bagId, int slot, int quantity)
    {
        var item = await _inventoryRepository.GetItemAsync(characterId, bagId, slot);
        if (item == null) return false;

        // Stackable natif = non équipable + cap>1 (S_Item col29) —
        // la durabilité d'une arme (>1 aussi) ne doit PAS être
        // traitée comme un count : vendre/jeter une arme supprime
        // la ligne entière.
        var stackable = !item.IsEquipped && MaxStackOf(item.ItemId) > 1 &&
            (!_itemDefs.TryGetValue(item.ItemId, out var d) ||
             d.EquipLoc <= 0);
        if (!stackable || item.Durability <= quantity)
            return await _inventoryRepository.RemoveItemAsync(characterId, bagId, slot);

        // Le count vit dans `durability` (natif : ModifyItemDurability
        // ajuste le count du stack — désasm CheckAutoStackItem).
        item.Durability -= quantity;
        item.Quantity = item.Durability;
        return await _inventoryRepository.UpdateItemAsync(item);
    }

    /// <summary>
    /// Échange deux slots — si l'item destination est le même et stackable,
    /// fusionne jusqu'au stack max (CheckAutoStackItem).
    /// Correspond à CPlayerFactory::SwitchItemLocation.
    /// </summary>
    public async Task<bool> SwitchItemLocationAsync(int characterId,
        int fromBagId, int fromSlot, int toBagId, int toSlot)
    {
        var from = await _inventoryRepository.GetItemAsync(characterId, fromBagId, fromSlot);
        if (from == null || from.IsEquipped) return false;

        var to = await _inventoryRepository.GetItemAsync(characterId, toBagId, toSlot);

        if (to == null)
        {
            // Simple déplacement dans un slot vide
            from.BagId = toBagId;
            from.Slot = toSlot;
            return await _inventoryRepository.UpdateItemAsync(from);
        }

        // Même item stackable → fusion (non-équipable + cap>1 —
        // une arme au même id/enchant/bind ne fusionne jamais).
        if (to.ItemId == from.ItemId && !to.IsEquipped &&
            to.EnchantLevel == from.EnchantLevel &&
            to.IsBound == from.IsBound &&
            MaxStackOf(from.ItemId) > 1 &&
            (!_itemDefs.TryGetValue(from.ItemId, out var fd) ||
             fd.EquipLoc <= 0))
        {
            var maxStack = MaxStackOf(from.ItemId);
            var merged = Math.Min(from.Quantity, maxStack - to.Quantity);
            if (merged > 0)
            {
                to.Quantity += merged;
                to.Durability = to.Quantity;
                from.Quantity -= merged;
                from.Durability = from.Quantity;
                await _inventoryRepository.UpdateItemAsync(to);
                if (from.Quantity == 0)
                {
                    await _inventoryRepository.RemoveItemAsync(characterId, fromBagId, fromSlot);
                    return true;
                }
                return await _inventoryRepository.UpdateItemAsync(from);
            }
        }

        // Swap complet des deux slots — passe par un loc tampon :
        // l'index unique natif (player_id, container_index, loc)
        // refuse l'UPDATE de `from` sur le slot encore occupé par `to`
        // (erreur live : 23505 inventory1_index → échec du swap).
        var (srcBag, srcSlot) = (fromBagId, fromSlot);
        from.BagId = toBagId; from.Slot = (short)(toSlot + 10000);
        await _inventoryRepository.UpdateItemAsync(from);
        to.BagId = srcBag; to.Slot = srcSlot;
        await _inventoryRepository.UpdateItemAsync(to);
        from.Slot = toSlot;
        return await _inventoryRepository.UpdateItemAsync(from);
    }

    /// <summary>
    /// Split un stack d'item — crée un nouveau stack dans un slot libre.
    /// Correspond à CInventory::SplitItem.
    /// </summary>
    public async Task<bool> SplitItemAsync(int characterId, int bagId, int slot,
        int quantity, int toBagId, int toSlot)
    {
        var item = await _inventoryRepository.GetItemAsync(characterId, bagId, slot);
        if (item == null || item.Quantity <= quantity || quantity <= 0)
            return false;

        var toItem = await _inventoryRepository.GetItemAsync(characterId, toBagId, toSlot);
        if (toItem != null) return false;

        await _inventoryRepository.AddItemAsync(new ItemEntity
        {
            CharacterId = characterId,
            BagId = toBagId,
            Slot = toSlot,
            ItemId = item.ItemId,
            Quantity = quantity,
            // durability = count — le nouveau stack porte la part
            // retirée, pas le count total (durability=durability
            // dupliquait le stack entier, bug live).
            Durability = quantity,
            MaxDurability = item.MaxDurability,
            IsEquipped = false,
            EnchantLevel = item.EnchantLevel,
            IsBound = item.IsBound,
        });

        item.Quantity -= quantity;
        item.Durability = item.Quantity;
        return await _inventoryRepository.UpdateItemAsync(item);
    }

    /// <summary>Item vendable à un slot (non équipé, non lié).</summary>
    public async Task<ItemEntity?> GetItemForSaleAsync(int characterId, int bagId, int slot)
    {
        var item = await _inventoryRepository.GetItemAsync(characterId, bagId, slot);
        return item is { IsEquipped: false, IsBound: false } ? item : null;
    }

    /// <summary>Équipe/déséquipe un item (CNE_CZ_ZoneServerUpdateEquipment).</summary>
    public async Task<bool> SetEquippedAsync(int characterId, int bagId, int slot, bool equipped)
    {
        var item = await _inventoryRepository.GetItemAsync(characterId, bagId, slot);
        if (item == null) return false;
        item.IsEquipped = equipped;
        return await _inventoryRepository.UpdateItemAsync(item);
    }
}
