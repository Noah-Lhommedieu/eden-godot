using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>Repository en mémoire pour les tests (pas de PostgreSQL).</summary>
internal sealed class InMemoryInventoryRepository : IInventoryRepository
{
    public readonly List<ItemEntity> Items = new();
    private int _nextId = 1;
    public int BagCapacity = 32;

    public Task<int?> GetAvailableSlotAsync(int characterId, int bagId)
    {
        var cap = bagId == 0 ? BagCapacity
            : BagCapacities.GetValueOrDefault(bagId);
        for (int s = 0; s < cap; s++)
            if (!Items.Any(i => i.CharacterId == characterId &&
                                i.BagId == bagId && i.Slot == s))
                return Task.FromResult<int?>(s);
        return Task.FromResult<int?>(null);
    }

    /// <inheritdoc />
    public async Task<(int BagId, int Slot)?> GetAnyAvailableSlotAsync(
        int characterId)
    {
        for (var bag = 0; bag <= 4; bag++)
        {
            var cap = bag == 0 ? BagCapacity
                : BagCapacities.GetValueOrDefault(bag);
            if (cap <= 0) continue;
            var slot = await GetAvailableSlotAsync(characterId, bag);
            if (slot != null) return (bag, slot.Value);
        }
        return null;
    }

    public Task<ItemEntity?> GetItemAsync(int characterId, int bagId, int slot)
        => Task.FromResult(Items.FirstOrDefault(i =>
            i.CharacterId == characterId && i.BagId == bagId && i.Slot == slot));

    public Task<ItemEntity?> GetEquipmentItemAsync(int characterId,
        int group, int loc)
        => Task.FromResult(Items.FirstOrDefault(i =>
            i.CharacterId == characterId && i.IsEquipped &&
            i.BagId == group && i.Slot == loc));

    public Task<IReadOnlyList<ItemEntity>> GetItemsByCharacterIdAsync(int characterId)
        => Task.FromResult<IReadOnlyList<ItemEntity>>(
            Items.Where(i => i.CharacterId == characterId).ToList());

    public Task AddItemAsync(ItemEntity item)
    {
        item.Id = _nextId++;
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveItemAsync(int characterId, int bagId, int slot)
        => Task.FromResult(Items.RemoveAll(i =>
            i.CharacterId == characterId && i.BagId == bagId && i.Slot == slot) > 0);

    public Task<bool> UpdateItemAsync(ItemEntity item) => Task.FromResult(true);

    public Task<int?> GetBagCapacity(int characterId, int bagId)
        => Task.FromResult<int?>(BagCapacity);

    public readonly Dictionary<int, int> BagCapacities = new();
    public readonly List<EquippedBag> EquippedBags = new();

    public Task<IReadOnlyDictionary<int, int>> GetAllBagCapacitiesAsync(
        int characterId)
    {
        // Miroir du fallback du vrai repo : sac 0 toujours ouvert.
        var caps = BagCapacities.ToDictionary(kv => kv.Key,
            kv => kv.Value);
        if (!caps.ContainsKey(0) || caps[0] <= 0)
            caps[0] = BagCapacity;
        return Task.FromResult<IReadOnlyDictionary<int, int>>(caps);
    }

    public Task SetBagCapacityAsync(int characterId, int bagId, int slots)
    {
        BagCapacities[bagId] = slots;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EquippedBag>> GetEquippedBagsAsync(
        int characterId)
        => Task.FromResult<IReadOnlyList<EquippedBag>>(EquippedBags);

    public Task<EquippedBag?> GetEquippedBagAsync(int characterId,
        int holderIndex, int holderLoc)
        => Task.FromResult(EquippedBags.FirstOrDefault(b =>
            b.HolderIndex == holderIndex && b.Loc == holderLoc));

    public Task<bool> MoveItemToBagHolderAsync(int characterId,
        int srcBagId, int srcSlot, int holderIndex, int holderLoc)
    {
        var item = Items.FirstOrDefault(i =>
            i.CharacterId == characterId && i.BagId == srcBagId
            && i.Slot == srcSlot);
        if (item == null) return Task.FromResult(false);
        Items.Remove(item);
        EquippedBags.Add(new EquippedBag
        {
            SerialId = item.Id, ItemId = item.ItemId,
            HolderIndex = holderIndex, Loc = holderLoc,
        });
        return Task.FromResult(true);
    }

    public Task<bool> MoveBagToInventoryAsync(int characterId,
        int holderIndex, int holderLoc, int dstBagId, int dstSlot)
    {
        var bag = EquippedBags.FirstOrDefault(b =>
            b.HolderIndex == holderIndex && b.Loc == holderLoc);
        if (bag == null) return Task.FromResult(false);
        EquippedBags.Remove(bag);
        Items.Add(new ItemEntity
        {
            Id = bag.SerialId, CharacterId = characterId,
            BagId = dstBagId, Slot = dstSlot, ItemId = bag.ItemId,
            Quantity = 1,
        });
        return Task.FromResult(true);
    }

    public Task SwapEquippedBagLocsAsync(int characterId,
        int holderIndex, int locA, int locB)
    {
        var a = EquippedBags.FirstOrDefault(b =>
            b.HolderIndex == holderIndex && b.Loc == locA);
        var c = EquippedBags.FirstOrDefault(b =>
            b.HolderIndex == holderIndex && b.Loc == locB);
        if (a != null) a.Loc = locB;
        if (c != null) c.Loc = locA;
        return Task.CompletedTask;
    }

    public Task SwapInventoryBagsAsync(int characterId, int bagA,
        int bagB)
    {
        foreach (var i in Items.Where(i =>
            i.CharacterId == characterId && i.BagId == bagA))
            i.BagId = -1;
        foreach (var i in Items.Where(i =>
            i.CharacterId == characterId && i.BagId == bagB))
            i.BagId = bagA;
        foreach (var i in Items.Where(i =>
            i.CharacterId == characterId && i.BagId == -1))
            i.BagId = bagB;
        (BagCapacities[bagA], BagCapacities[bagB]) =
            (BagCapacities.GetValueOrDefault(bagB),
             BagCapacities.GetValueOrDefault(bagA));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(int Loc, int ItemId)>> GetEquippedItemsAsync(int characterId)
        => Task.FromResult<IReadOnlyList<(int, int)>>(Array.Empty<(int, int)>());

    public Task<IReadOnlyList<ItemEntity>> GetLiveItemsAsync(int characterId)
        => Task.FromResult<IReadOnlyList<ItemEntity>>(
            Items.Where(i => i.CharacterId == characterId).ToList());

    public Task RemoveLiveItemAsync(int characterId, int itemRowId)
    {
        Items.RemoveAll(i => i.CharacterId == characterId && i.Id == itemRowId);
        return Task.CompletedTask;
    }

    public Task<bool> TransferItemAsync(int itemRowId, int newOwnerId,
        int newBagId, int newSlot)
    {
        var item = Items.FirstOrDefault(i => i.Id == itemRowId);
        if (item == null) return Task.FromResult(false);
        item.CharacterId = newOwnerId;
        item.BagId = newBagId;
        item.Slot = newSlot;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task UpdateItemTrainAsync(int itemUid, int level, long exp,
        int t1, int l1, int t2, int l2, int t3, int l3)
    {
        var item = Items.FirstOrDefault(i => i.Id == itemUid);
        if (item == null) return Task.CompletedTask;
        item.TrainLevel = level; item.TrainExp = exp;
        item.TrainSlot1Type = t1; item.TrainSlot1Level = l1;
        item.TrainSlot2Type = t2; item.TrainSlot2Level = l2;
        item.TrainSlot3Type = t3; item.TrainSlot3Level = l3;
        return Task.CompletedTask;
    }

    /// <summary>eqtrain_item_id simulé par perso (tests).</summary>
    public readonly Dictionary<int, int> EquipTrainItemIds = new();

    public Task<int> GetEquipTrainItemIdAsync(int characterId)
        => Task.FromResult(EquipTrainItemIds.GetValueOrDefault(characterId));

    public Task SetEquipTrainItemIdAsync(int characterId, int itemSerial)
    {
        EquipTrainItemIds[characterId] = itemSerial;
        return Task.CompletedTask;
    }

    public Task<(ItemEntity Item, int ContainerType, int ContainerKey,
        int VectorIndex)?> FindItemBySerialAsync(int characterId,
        int serial)
    {
        var item = Items.FirstOrDefault(i =>
            i.CharacterId == characterId && i.Id == serial);
        if (item == null)
            return Task.FromResult<(ItemEntity, int, int, int)?>(null);
        var vec = Items.Where(i => i.CharacterId == characterId &&
            i.BagId == item.BagId &&
            i.IsEquipped == item.IsEquipped)
            .OrderBy(i => i.Slot).ToList();
        return Task.FromResult<(ItemEntity, int, int, int)?>(
            (item, item.IsEquipped ? 1 : 0, item.BagId,
             vec.IndexOf(item)));
    }
}

public class InventoryServiceTests
{
    private static InventoryService CreateService(InMemoryInventoryRepository repo) =>
        new(repo, new[]
        {
            new ItemParser.ItemDef { Id = 1001, MaxStack = 10 },
            new ItemParser.ItemDef { Id = 1002, MaxStack = 1 },
        });

    [Fact]
    public async Task AddItem_FillsFreeSlot()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        var (success, _, _, _) = await svc.AddItemAsync(1, 1001, 5);
        Assert.True(success);
        var item = Assert.Single(repo.Items);
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public async Task AddItem_AutoStacksOnExisting()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 8);          // stack 8/10 slot 0
        await svc.AddItemAsync(1, 1001, 5);          // 2 -> slot 0, 3 -> slot 1

        Assert.Equal(10, repo.Items[0].Quantity);
        Assert.Equal(3, repo.Items[1].Quantity);
    }

    [Fact]
    public async Task AddItem_EnchantedDoesNotStackOnNormal()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 5);
        await svc.AddItemAsync(1, 1001, 5, enchantLevel: 3);
        Assert.Equal(2, repo.Items.Count);
    }

    [Fact]
    public async Task AddItem_RespectsMaxStack()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 25); // 10 + 10 + 5
        Assert.Equal(3, repo.Items.Count);
        Assert.Equal(10, repo.Items[0].Quantity);
        Assert.Equal(10, repo.Items[1].Quantity);
        Assert.Equal(5, repo.Items[2].Quantity);
    }

    [Fact]
    public async Task AddItem_FailsWhenBagFull()
    {
        var repo = new InMemoryInventoryRepository { BagCapacity = 2 };
        var svc = CreateService(repo);
        var r1 = await svc.AddItemAsync(1, 1001, 20); // remplit 2 slots
        var r2 = await svc.AddItemAsync(1, 1002, 1);  // maxStack=1, plus de place
        Assert.True(r1.Success);
        Assert.False(r2.Success);
    }

    [Fact]
    public async Task SwitchItem_SwapsOccupiedSlots()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 5);
        await svc.AddItemAsync(1, 1002, 1);
        Assert.True(await svc.SwitchItemLocationAsync(1, 0, 0, 0, 1));
        Assert.Equal(1002, (await repo.GetItemAsync(1, 0, 0))!.ItemId);
        Assert.Equal(1001, (await repo.GetItemAsync(1, 0, 1))!.ItemId);
    }

    [Fact]
    public async Task SwitchItem_MergesSameStackable()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        repo.Items.Add(new ItemEntity { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1001, Quantity = 8 });
        repo.Items.Add(new ItemEntity { CharacterId = 1, BagId = 0, Slot = 1, ItemId = 1001, Quantity = 8 });

        Assert.True(await svc.SwitchItemLocationAsync(1, 0, 0, 0, 1));
        Assert.Equal(10, (await repo.GetItemAsync(1, 0, 1))!.Quantity);
        Assert.Equal(6, (await repo.GetItemAsync(1, 0, 0))!.Quantity);
    }

    [Fact]
    public async Task RemoveItem_ReducesStack()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 10);
        Assert.True(await svc.RemoveItemAsync(1, 0, 0, 4));
        Assert.Equal(6, (await repo.GetItemAsync(1, 0, 0))!.Quantity);
    }

    [Fact]
    public async Task SplitItem_CreatesNewStack()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 10);
        Assert.True(await svc.SplitItemAsync(1, 0, 0, 3, 0, 5));
        Assert.Equal(7, (await repo.GetItemAsync(1, 0, 0))!.Quantity);
        Assert.Equal(3, (await repo.GetItemAsync(1, 0, 5))!.Quantity);
    }

    [Fact]
    public async Task SplitItem_FailsOnInsufficientQuantity()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 10);
        Assert.False(await svc.SplitItemAsync(1, 0, 0, 10, 0, 5));
    }

    [Fact]
    public async Task AddItems_MergesAndPlacesLikeSingleAdds()
    {
        var repo = new InMemoryInventoryRepository();
        var svc = CreateService(repo);
        await svc.AddItemAsync(1, 1001, 8);          // stack 8/10 slot 0

        var results = await svc.AddItemsAsync(1, new[]
        {
            (1001, 5, 0, false),   // 2 → merge slot 0, 3 → nouveau slot
            (1002, 1, 0, false),   // non stackable → nouveau slot
        });

        Assert.All(results, r => Assert.True(r.Ok));
        Assert.Equal(10, repo.Items[0].Quantity);        // merge
        Assert.Equal(3, repo.Items[1].Quantity);         // reste stack
        Assert.Equal(1002, repo.Items[2].ItemId);        // nouveau
        Assert.Contains(results[0].Touched, t => t.Merged);
        Assert.Equal(2, results[0].Touched.Count);
        Assert.Single(results[1].Touched);
    }

    [Fact]
    public async Task AddItems_SecondRequestSeesFirstInsert()
    {
        var repo = new InMemoryInventoryRepository { BagCapacity = 2 };
        var svc = CreateService(repo);

        var results = await svc.AddItemsAsync(1, new[]
        {
            (1001, 20, 0, false),  // remplit les 2 slots (10+10)
            (1002, 1, 0, false),   // plus de place → refus all-or-nothing
        });

        Assert.True(results[0].Ok);
        Assert.False(results[1].Ok);
        Assert.Equal(2, repo.Items.Count);               // rien d'inséré pour le 2e
        Assert.Empty(results[1].Touched);
    }
}
