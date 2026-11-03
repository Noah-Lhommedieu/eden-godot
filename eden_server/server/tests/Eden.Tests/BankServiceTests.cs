using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Bank;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

internal sealed class InMemoryBankRepository : IBankRepository
{
    public readonly List<BankItemEntity> Items = new();
    public int BankCapacity = 10;

    public Task<int?> GetAvailableSlotAsync(int characterId,
        int maxSlots = 50)
    {
        var cap = Math.Min(BankCapacity, maxSlots);
        for (int s = 0; s < cap; s++)
            if (!Items.Any(i => i.CharacterId == characterId && i.Slot == s))
                return Task.FromResult<int?>(s);
        return Task.FromResult<int?>(null);
    }

    public Task<BankItemEntity?> GetBankItemAsync(int characterId, int slot)
        => Task.FromResult(Items.FirstOrDefault(i =>
            i.CharacterId == characterId && i.Slot == slot));

    public Task<IReadOnlyList<BankItemEntity>> GetBankItemsByCharacterIdAsync(int characterId)
        => Task.FromResult<IReadOnlyList<BankItemEntity>>(
            Items.Where(i => i.CharacterId == characterId).ToList());

    public Task AddBankItemAsync(BankItemEntity item)
    {
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveBankItemAsync(int characterId, int slot)
        => Task.FromResult(Items.RemoveAll(i =>
            i.CharacterId == characterId && i.Slot == slot) > 0);

    public Task<bool> UpdateBankItemAsync(BankItemEntity item) => Task.FromResult(true);

    public int ExpandCount;

    public Task<int> GetStorageExpandCountAsync(int characterId)
        => Task.FromResult(ExpandCount);

    public Task SetStorageExpandCountAsync(int characterId, int count)
    {
        ExpandCount = count;
        return Task.CompletedTask;
    }
}

public class BankServiceTests
{
    private static (InMemoryInventoryRepository inv, InMemoryBankRepository bank,
        InventoryService invSvc, BankService bankSvc) Create()
    {
        var inv = new InMemoryInventoryRepository();
        var bank = new InMemoryBankRepository();
        var defs = new[] { new ItemParser.ItemDef { Id = 1001, MaxStack = 10 } };
        var invSvc = new InventoryService(inv, defs);
        var bankSvc = new BankService(bank, defs);
        return (inv, bank, invSvc, bankSvc);
    }

    [Fact]
    public async Task Deposit_MovesItemFromInventoryToBank()
    {
        var (inv, bank, invSvc, bankSvc) = Create();
        await invSvc.AddItemAsync(1, 1001, 8);

        var (success, deposited) = await bankSvc.DepositFromInventoryAsync(inv, 1, 0, 0, 5);
        Assert.True(success);
        Assert.Equal(5, deposited);
        Assert.Equal(5, bank.Items.Single().Quantity);
        Assert.Equal(3, (await inv.GetItemAsync(1, 0, 0))!.Quantity);
    }

    [Fact]
    public async Task Deposit_RemovesInventoryStackWhenFullyDeposited()
    {
        var (inv, bank, invSvc, bankSvc) = Create();
        await invSvc.AddItemAsync(1, 1001, 8);
        var (success, _) = await bankSvc.DepositFromInventoryAsync(inv, 1, 0, 0, 8);
        Assert.True(success);
        Assert.Empty(inv.Items);
        Assert.Equal(8, bank.Items.Single().Quantity);
    }

    [Fact]
    public async Task Deposit_AutoStacksOnExistingBankStack()
    {
        var (inv, bank, invSvc, bankSvc) = Create();
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 0, ItemId = 1001, Quantity = 7 });
        await invSvc.AddItemAsync(1, 1001, 6);

        var (success, deposited) = await bankSvc.DepositFromInventoryAsync(inv, 1, 0, 0, 6);
        Assert.True(success);
        Assert.Equal(10, bank.Items[0].Quantity); // 7+3
        Assert.Equal(3, bank.Items[1].Quantity);  // reste
    }

    [Fact]
    public async Task Deposit_FailsOnEquippedItem()
    {
        var (inv, bank, invSvc, bankSvc) = Create();
        await invSvc.AddItemAsync(1, 1001, 5);
        await invSvc.SetEquippedAsync(1, 0, 0, true);
        var (success, _) = await bankSvc.DepositFromInventoryAsync(inv, 1, 0, 0, 5);
        Assert.False(success);
    }

    [Fact]
    public async Task Withdraw_MovesItemBackToInventory()
    {
        var (inv, bank, invSvc, bankSvc) = Create();
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 0, ItemId = 1001, Quantity = 8 });

        var (success, withdrawn, _, _) = await bankSvc.WithdrawToInventoryAsync(invSvc, 1, 0, 6);
        Assert.True(success);
        Assert.Equal(6, withdrawn);
        Assert.Equal(2, bank.Items.Single().Quantity);
        Assert.Equal(6, inv.Items.Single().Quantity);
    }

    [Fact]
    public async Task SwitchBankItems_SwapsSlots()
    {
        var (_, bank, _, bankSvc) = Create();
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 0, ItemId = 1001, Quantity = 5 });
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 3, ItemId = 2002, Quantity = 1 });

        Assert.True(await bankSvc.SwitchBankItemsAsync(1, 0, 3));
        Assert.Equal(2002, (await bank.GetBankItemAsync(1, 0))!.ItemId);
        Assert.Equal(1001, (await bank.GetBankItemAsync(1, 3))!.ItemId);
    }

    /// <summary>Drop natif type=4 (CPlayerFactory::DropItem →
    /// EradicateItem sur char+0x514) : le loc banque entier est
    /// détruit, stack compris — pas de décrément par count.</summary>
    [Fact]
    public async Task RemoveBankItem_DestroysWholeLoc()
    {
        var (_, bank, _, bankSvc) = Create();
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 0, ItemId = 1001, Quantity = 8 });
        bank.Items.Add(new BankItemEntity { CharacterId = 1, Slot = 2, ItemId = 2002, Quantity = 1 });

        Assert.True(await bankSvc.RemoveBankItemAsync(1, 0));
        Assert.Single(bank.Items);
        Assert.Equal(2, bank.Items.Single().Slot);
        Assert.False(await bankSvc.RemoveBankItemAsync(1, 0));
    }

    /// <summary>Décision native `CPlayerFactory::ExpandStorage` :
    /// prix indexé par `expand_storage_cnt` (S_Parameter 409/410 =
    /// 1500/3500), cap à 2 expansions, or insuffisant → 2.</summary>
    [Fact]
    public void EvaluateExpandStorage_NativeDecision()
    {
        var prices = new[] { 1500, 3500 };
        // 1re expansion payable → 0 avec le coût de la rangée 1
        Assert.Equal((0, 1500),
            BankService.EvaluateExpandStorage(0, 2000, prices));
        // or insuffisant → 2 (le natif émet le manquant via 0x5aae)
        Assert.Equal((2, 1500),
            BankService.EvaluateExpandStorage(0, 1499, prices));
        // 2e expansion au prix de la rangée 2
        Assert.Equal((0, 3500),
            BankService.EvaluateExpandStorage(1, 3500, prices));
        // cap natif expand_storage_cnt < 2 → 1
        Assert.Equal((1, 0),
            BankService.EvaluateExpandStorage(2, 99999, prices));
        // compteur négatif / hors table → 1
        Assert.Equal((1, 0),
            BankService.EvaluateExpandStorage(-1, 99999, prices));
        Assert.Equal((1, 0),
            BankService.EvaluateExpandStorage(0, 99999, Array.Empty<int>()));
    }
}
