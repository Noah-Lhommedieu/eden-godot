using Eden.Core.Game.Bank;
using Eden.Core.Game.Inventory;
using Eden.Core.Database.Repositories;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion de la banque — correspond à CStorage/CStorageOp.
/// Gère les dépôts depuis l'inventaire (auto-stack), retraits et swaps.
/// </summary>
public sealed class BankService
{
    private readonly IBankRepository _bankRepository;
    private readonly Dictionary<int, ItemParser.ItemDef> _itemDefs;
    private const int DefaultBankSlots = 50;

    public BankService(IBankRepository bankRepository)
        : this(bankRepository, null) { }

    public BankService(IBankRepository bankRepository,
        IEnumerable<ItemParser.ItemDef>? itemDefs)
    {
        _bankRepository = bankRepository;
        _itemDefs = itemDefs?.ToDictionary(d => d.Id) ?? new();
    }

    private int MaxStackOf(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var def) && def.MaxStack > 0
            ? def.MaxStack : 99;

    /// <summary>Item stackable (même règle que InventoryService :
    /// MaxStack&gt;1 et non équipable — le natif ne fusionne jamais
    /// une arme/armure).</summary>
    private bool IsStackable(int itemId) =>
        MaxStackOf(itemId) > 1 &&
        !(_itemDefs.TryGetValue(itemId, out var def) && def.EquipLoc > 0);

    /// <summary>Compte d'un enregistrement banque — le natif stocke le
    /// count dans `durability` pour les stackables (pas de colonne
    /// quantity dans storage1/2). Pour l'équipement, durability =
    /// durabilité réelle et le compte logique est 1 (la ligne existe).</summary>
    private int CountOf(BankItemEntity item) =>
        IsStackable(item.ItemId) ? item.Quantity : 1;

    /// <summary>Synchronise durability↔count après mutation d'un stack
    /// (UpdateBankItemAsync persiste `durability`, pas `Quantity`).</summary>
    private void SyncStack(BankItemEntity item)
    {
        if (IsStackable(item.ItemId)) item.Durability = item.Quantity;
    }

    /// <summary>
    /// Dépose un item de l'inventaire en banque — débite le slot inventaire,
    /// auto-stack en banque (CheckAutoStackItem), puis slot libre.
    /// Correspond à CNE_CZ_ZoneServerStorageDeposit.
    /// </summary>
    public async Task<(bool Success, int Deposited)> DepositFromInventoryAsync(
        IInventoryRepository inventory,
        int characterId, int bagId, int invSlot, int quantity)
    {
        var inv = await inventory.GetItemAsync(characterId, bagId, invSlot);
        if (inv == null || inv.IsEquipped || inv.Quantity <= 0)
            return (false, 0);
        var stackable = IsStackable(inv.ItemId);
        var count = stackable
            ? Math.Min(quantity, inv.Quantity)
            : 1; // non-stackable : l'item entier (loc par loc, natif)
        var maxStack = MaxStackOf(inv.ItemId);
        var remaining = count;

        // 1. Auto-stack sur les stacks banque existants (même item —
        // CheckAutoStackItem natif : même template, durabilité/bind/
        // strengthen identiques sinon le stack est disqualifié).
        // PLANIFIÉ en mémoire (pas d'écriture) : l'exécution passe en
        // une transaction quand le repo est SQL.
        var bankItems = await _bankRepository.GetBankItemsByCharacterIdAsync(characterId);
        var merges = new List<(BankItemEntity Stack, int Added)>();
        if (stackable)
            foreach (var stack in bankItems.Where(i =>
                         i.ItemId == inv.ItemId && i.Quantity < maxStack &&
                         i.EnchantLevel == inv.EnchantLevel &&
                         i.IsBound == inv.IsBound))
            {
                var toAdd = Math.Min(remaining, maxStack - stack.Quantity);
                stack.Quantity += toAdd;
                SyncStack(stack);
                merges.Add((stack, toAdd));
                remaining -= toAdd;
                if (remaining == 0) break;
            }

        // 2. Nouvelles lignes planifiées pour le reste — les slots
        //    sont résolus au moment de l'exécution (dans la tx pour
        //    le chemin SQL, à la volée pour le fallback).
        var capacity = await GetStorageCapacityAsync(characterId);
        var chunks = new List<BankItemEntity>();
        var pending = remaining;
        while (pending > 0)
        {
            var stackSize = stackable
                ? Math.Min(pending, maxStack) : 1;
            chunks.Add(new BankItemEntity
            {
                CharacterId = characterId,
                ItemId = inv.ItemId,
                Quantity = stackSize,
                // stackable : durability = count (convention native
                // inventory/storage) ; équipement : durabilité réelle.
                Durability = stackable ? stackSize : inv.Durability,
                MaxDurability = stackable ? maxStack : inv.MaxDurability,
                EnchantLevel = inv.EnchantLevel,
                IsBound = inv.IsBound,
                CreateTime = inv.CreateTime,
            });
            pending -= stackSize;
        }

        // 3. Exécution atomique (repo SQL) : débit inventaire + merges
        //    + inserts en une seule transaction — un crash
        //    intermédiaire ne peut plus dupliquer/perdre l'item.
        if (_bankRepository is BankRepository sqlRepo)
        {
            var done = await sqlRepo.ExecuteDepositAsync(characterId,
                merges, chunks, capacity, inv, stackable);
            return done > 0 ? (true, done) : (false, 0);
        }

        // ---- Fallback non-SQL (tests en mémoire) — même séquence
        //      qu'avant la transactionnalisation. ----
        foreach (var (stack, _) in merges)
            await _bankRepository.UpdateBankItemAsync(stack);
        var inserted = 0;
        foreach (var chunk in chunks)
        {
            var freeSlot = await _bankRepository.GetAvailableSlotAsync(
                characterId, capacity);
            if (freeSlot == null) break; // banque pleine — on dépose ce qu'on peut
            chunk.Slot = freeSlot.Value;
            await _bankRepository.AddBankItemAsync(chunk);
            inserted += chunk.Quantity;
        }

        var deposited = merges.Sum(m => m.Added) + inserted;
        if (deposited == 0) return (false, 0);

        inv.Quantity -= deposited;
        if (inv.Quantity <= 0)
            await inventory.RemoveItemAsync(characterId, bagId, invSlot);
        else
        {
            // durability = count persisté pour les stackables —
            // sans cette synchro l'UPDATE réécrivait l'ancien count.
            if (stackable) inv.Durability = inv.Quantity;
            await inventory.UpdateItemAsync(inv);
        }

        return (true, deposited);
    }

    /// <summary>
    /// Retire un item de la banque vers l'inventaire.
    /// Correspond à CNE_CZ_ZoneServerStorageWithdraw. Retourne les slots
    /// inventaire touchés (merged/nouveau) pour les deltas 274/291.
    /// </summary>
    public async Task<(bool Success, int Withdrawn, int ItemId,
        List<(int BagId, int Slot, bool Merged)> Touched)>
        WithdrawToInventoryAsync(
        InventoryService inventory,
        int characterId, int bankSlot, int quantity)
    {
        var item = await _bankRepository.GetBankItemAsync(characterId, bankSlot);
        var empty = (false, 0, 0,
            new List<(int BagId, int Slot, bool Merged)>());
        if (item == null) return empty;
        var stackable = IsStackable(item.ItemId);
        var available = CountOf(item);
        if (available <= 0) return empty;
        var count = stackable ? Math.Min(quantity, available) : 1;

        // Chemin transactionnel : merge inventaire + nouvelles lignes
        // + débit banque en UNE tx (repo SQL uniquement — le fallback
        // ci-dessous sert les repos en mémoire des tests).
        if (_bankRepository is BankRepository sqlRepo)
        {
            var r = await sqlRepo.ExecuteWithdrawAsync(characterId,
                item, count, stackable, MaxStackOf(item.ItemId));
            if (!r.Ok) return empty;
            return (true, r.Withdrawn, item.ItemId, r.Touched);
        }

        // Ajouter à l'inventaire (auto-stack géré par InventoryService)
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var result = await inventory.AddItemAsync(
            characterId, item.ItemId, count, preferredBagId: 0,
            enchantLevel: item.EnchantLevel, isBound: item.IsBound,
            touched: touched);
        if (!result.Success) return empty;

        // Débiter le slot banque
        item.Quantity = stackable ? item.Quantity - count : 0;
        SyncStack(item);
        if (item.Quantity <= 0)
            await _bankRepository.RemoveBankItemAsync(characterId, bankSlot);
        else
            await _bankRepository.UpdateBankItemAsync(item);

        return (true, count, item.ItemId, touched);
    }

    /// <summary>
    /// Échange deux slots de banque — fusion si même item stackable.
    /// </summary>
    public async Task<bool> SwitchBankItemsAsync(int characterId,
        int fromSlot, int toSlot)
    {
        var from = await _bankRepository.GetBankItemAsync(characterId, fromSlot);
        if (from == null) return false;

        var to = await _bankRepository.GetBankItemAsync(characterId, toSlot);
        if (to == null)
        {
            from.Slot = toSlot;
            if (_bankRepository is BankRepository sqlMove)
                return await sqlMove.ExecuteSwapAsync(characterId, from,
                    null, deleteFrom: false);
            return await _bankRepository.UpdateBankItemAsync(from);
        }

        // Même item stackable → fusion
        if (to.ItemId == from.ItemId && IsStackable(from.ItemId))
        {
            var maxStack = MaxStackOf(from.ItemId);
            var merged = Math.Min(from.Quantity, maxStack - to.Quantity);
            if (merged > 0)
            {
                to.Quantity += merged;
                from.Quantity -= merged;
                SyncStack(to);
                SyncStack(from);
                // UNE transaction (UPDATE cible + UPDATE/DELETE
                // source) quand le repo est SQL — avant, un crash
                // entre les deux écritures dupliquait le count.
                if (_bankRepository is BankRepository sqlMerge)
                    return await sqlMerge.ExecuteSwapAsync(characterId,
                        from, to, deleteFrom: from.Quantity == 0);
                await _bankRepository.UpdateBankItemAsync(to);
                if (from.Quantity == 0)
                {
                    await _bankRepository.RemoveBankItemAsync(characterId, fromSlot);
                    return true;
                }
                return await _bankRepository.UpdateBankItemAsync(from);
            }
        }

        (from.Slot, to.Slot) = (to.Slot, from.Slot);
        if (_bankRepository is BankRepository sqlSwap)
            return await sqlSwap.ExecuteSwapAsync(characterId, from, to,
                deleteFrom: false);
        await _bankRepository.UpdateBankItemAsync(from);
        return await _bankRepository.UpdateBankItemAsync(to);
    }

    /// <summary>Contenu complet de la banque d'un personnage.</summary>
    public Task<IReadOnlyList<BankItemEntity>> GetBankItemsAsync(int characterId) =>
        _bankRepository.GetBankItemsByCharacterIdAsync(characterId);

    /// <summary>Nombre max d'expansions payées — `expand_storage_cnt`
    /// natif (char+0x77c) plafonné à 2, désasm
    /// `CPlayerFactory::ExpandStorage` @0x80f2670.</summary>
    public const int MaxStorageExpansions = 2;

    /// <summary>Décision pure de `CPlayerFactory::ExpandStorage` :
    /// refuse (1) si le compteur atteint le cap natif ou la table de
    /// prix ; refuse (2) si l'or est insuffisant ; sinon succès (0) —
    /// le prix = `prices[expandCnt]` (S_Parameter 409/410, rangée
    /// « 小屋倉庫第N排擴充金額 »). Retourne aussi le coût applicable.</summary>
    public static (short Result, int Cost) EvaluateExpandStorage(
        int expandCnt, long gold, IReadOnlyList<int> prices)
    {
        if (expandCnt < 0 || expandCnt >= MaxStorageExpansions ||
            expandCnt >= prices.Count)
            return (1, 0);
        return prices[expandCnt] <= gold
            ? ((short)0, prices[expandCnt]) : ((short)2, prices[expandCnt]);
    }

    /// <summary>Suppression du loc banque complet — drop natif type=4
    /// (`CPlayerFactory::DropItem` → `EradicateItem` sur le conteneur
    /// char+0x514 : l'item entier est détruit, stack compris —
    /// désasm zone/decompiled.c:141580).</summary>
    public Task<bool> RemoveBankItemAsync(int characterId, int slot) =>
        _bankRepository.RemoveBankItemAsync(characterId, slot);

    /// <summary>Capacité de la banque perso : base 40 + 20 par
    /// expansion payée (`expand_storage_cnt`, natif : CCharacter+0x77c,
    /// max 2 — CPlayerFactory::ExpandStorage @0x80f2670).
    /// HYPOTHESIS : pas de capture de banque — base/pas à confirmer.</summary>
    public async Task<int> GetStorageCapacityAsync(int characterId)
    {
        var expand = await _bankRepository
            .GetStorageExpandCountAsync(characterId);
        return 40 + expand * 20;
    }

    /// <summary>`expand_storage_cnt` courant (char+0x77c natif).</summary>
    public Task<int> GetStorageExpandCountAsync(int characterId) =>
        _bankRepository.GetStorageExpandCountAsync(characterId);

    /// <summary>Persiste `expand_storage_cnt` après un `ExpandStorage`
    /// réussi — le natif sauvegarde `"gold expand_storage_cnt"`.</summary>
    public Task SetStorageExpandCountAsync(int characterId, int count) =>
        _bankRepository.SetStorageExpandCountAsync(characterId, count);
}
