using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Game.Trade;
using Xunit;

namespace Eden.Tests;

internal sealed class InMemoryTradeRepository : ITradeRepository
{
    public readonly List<TradeEntity> Trades = new();
    private int _nextId = 1;

    public Task<TradeEntity?> GetActiveTradeByPlayerAsync(int playerId)
        => Task.FromResult(Trades.FirstOrDefault(t =>
            t.InitiatorId == playerId || t.PartnerId == playerId));

    public Task<TradeEntity?> GetActiveTradeAsync(int a, int b)
        => Task.FromResult(Trades.FirstOrDefault(t =>
            (t.InitiatorId == a && t.PartnerId == b) ||
            (t.InitiatorId == b && t.PartnerId == a)));

    public Task CreateTradeAsync(TradeEntity trade)
    {
        trade.Id = _nextId++;
        Trades.Add(trade);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateTradeAsync(TradeEntity trade) => Task.FromResult(true);

    public Task<bool> DeleteTradeAsync(int tradeId)
        => Task.FromResult(Trades.RemoveAll(t => t.Id == tradeId) > 0);
}

public class TradeServiceTests
{
    private static (InMemoryTradeRepository trades, InMemoryInventoryRepository inv,
        Dictionary<int, long> gold, TradeService svc) Create()
    {
        var trades = new InMemoryTradeRepository();
        var inv = new InMemoryInventoryRepository();
        var invSvc = new InventoryService(inv);
        var gold = new Dictionary<int, long> { [1] = 1000, [2] = 500 };
        var svc = new TradeService(trades, inv, invSvc,
            getGold: id => Task.FromResult(gold.GetValueOrDefault(id)),
            setGold: (id, v) => { gold[id] = v; return Task.CompletedTask; });
        return (trades, inv, gold, svc);
    }

    private static async Task SeedItem(InMemoryInventoryRepository inv,
        int charId, int itemId, int qty)
    {
        await new InventoryService(inv).AddItemAsync(charId, itemId, qty);
    }

    [Fact]
    public async Task RequestTrade_CreatesSession()
    {
        var (trades, _, _, svc) = Create();
        var (success, _) = await svc.RequestTradeAsync(1, 2, 5.0);
        Assert.True(success);
        Assert.Single(trades.Trades);
    }

    [Fact]
    public async Task RequestTrade_FailsWhenTooFar()
    {
        var (_, _, _, svc) = Create();
        var (success, _) = await svc.RequestTradeAsync(1, 2, 50.0);
        Assert.False(success);
    }

    [Fact]
    public async Task AddItem_ReservesFromInventory()
    {
        var (trades, inv, _, svc) = Create();
        await svc.RequestTradeAsync(1, 2, 5.0);
        await SeedItem(inv, 1, 1001, 10);

        Assert.True(await svc.AddItemAsync(1, 2, 0, 0, 6));
        var trade = trades.Trades.Single();
        Assert.Single(trade.InitiatorItems);
        Assert.Equal(6, trade.InitiatorItems[0].Quantity);
        Assert.Equal(4, inv.Items.Single().Quantity); // reste en inventaire
    }

    [Fact]
    public async Task AddItem_FailsOnBoundItem()
    {
        var (_, inv, _, svc) = Create();
        await svc.RequestTradeAsync(1, 2, 5.0);
        var invSvc = new InventoryService(inv);
        await invSvc.AddItemAsync(1, 1001, 5, isBound: true);

        Assert.False(await svc.AddItemAsync(1, 2, 0, 0, 5));
    }

    [Fact]
    public async Task RemoveItem_ReturnsToInventory()
    {
        var (trades, inv, _, svc) = Create();
        await svc.RequestTradeAsync(1, 2, 5.0);
        await SeedItem(inv, 1, 1001, 10);
        await svc.AddItemAsync(1, 2, 0, 0, 6);

        Assert.True(await svc.RemoveItemAsync(1, 2, 0));
        Assert.Empty(trades.Trades.Single().InitiatorItems);
        Assert.Equal(10, inv.Items.Single().Quantity);
    }

    [Fact]
    public async Task Confirm_ExecutesItemAndGoldSwap()
    {
        var (trades, inv, gold, svc) = Create();
        await svc.RequestTradeAsync(1, 2, 5.0);
        await SeedItem(inv, 1, 1001, 10);
        await SeedItem(inv, 2, 2002, 3);

        await svc.AddItemAsync(1, 2, 0, 0, 7);
        await svc.AddItemAsync(2, 1, 0, 0, 3);
        await svc.ModifyGoldAsync(1, 2, 200);

        Assert.True(await svc.ConfirmTradeAsync(1, 2));
        Assert.True(await svc.ConfirmTradeAsync(2, 1));

        Assert.Empty(trades.Trades); // session supprimée
        // Joueur 1 : 3×2002, or 1000-200=800
        Assert.Contains(inv.Items, i => i.CharacterId == 1 && i.ItemId == 2002 && i.Quantity == 3);
        Assert.Equal(800, gold[1]);
        // Joueur 2 : 7×1001, or 500+200=700
        Assert.Contains(inv.Items, i => i.CharacterId == 2 && i.ItemId == 1001 && i.Quantity == 7);
        Assert.Equal(700, gold[2]);
    }

    [Fact]
    public async Task Cancel_ReturnsItemsToOwners()
    {
        var (trades, inv, _, svc) = Create();
        await svc.RequestTradeAsync(1, 2, 5.0);
        await SeedItem(inv, 1, 1001, 10);
        await svc.AddItemAsync(1, 2, 0, 0, 6);

        Assert.True(await svc.CancelTradeAsync(1, 2));
        Assert.Empty(trades.Trades);
        Assert.Equal(10, inv.Items.Single(i => i.CharacterId == 1).Quantity);
    }
}
