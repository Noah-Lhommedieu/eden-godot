using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

public class ShopServiceTests
{
    private static (InMemoryInventoryRepository inv, Dictionary<int, long> gold,
        ShopService shop) Create()
    {
        var inv = new InMemoryInventoryRepository();
        // SellPrice (col31) = prix système natif : achat plein tarif,
        // revente = ×param346 (0.05) × durabilité.
        var defs = new[]
        {
            new ItemParser.ItemDef { Id = 1001, BuyPrice = 100, SellPrice = 20, MaxStack = 99 },
            new ItemParser.ItemDef { Id = 1002, BuyPrice = 500, SellPrice = 400, MaxStack = 1, EquipLoc = 1 },
            new ItemParser.ItemDef { Id = 1003, BuyPrice = 50, SellPrice = 10 },
        };
        var invSvc = new InventoryService(inv, defs);
        var gold = new Dictionary<int, long> { [1] = 1000 };
        var stores = new[]
        {
            new ShopAndMiscParsers.StoreDef { Id = 10 }
        };
        stores[0].Items.Add((1001, "a", 1));
        stores[0].Items.Add((1002, "b", 1));
        stores[0].Items.Add((1003, "c", 0));
        var parameters = new ParameterParser.ParameterSet(
            new Dictionary<int, float> { [ParameterParser.SellBackFactor] = 0.05f },
            new Dictionary<int, string>());
        var shop = new ShopService(invSvc, stores, defs, parameters,
            getGold: id => Task.FromResult(gold.GetValueOrDefault(id)),
            setGold: (id, v) => { gold[id] = v; return Task.CompletedTask; });
        return (inv, gold, shop);
    }

    [Fact]
    public async Task BuyItem_DeductsGoldAndAddsItem()
    {
        var (inv, gold, shop) = Create();
        var (success, _, _, _, _, _) = await shop.BuyItemAsync(1, 10, 0, 3);
        Assert.True(success);
        Assert.Equal(940, gold[1]); // 1000 - 3×20 (col31 = sys price)
        Assert.Contains(inv.Items, i => i.ItemId == 1001 && i.Quantity == 3);
    }

    [Fact]
    public async Task BuyItem_FailsOnInsufficientGold()
    {
        var (inv, gold, shop) = Create();
        var (success, _, _, _, _, _) = await shop.BuyItemAsync(1, 10, 1, 3); // 3×400 > 1000
        Assert.False(success);
        Assert.Empty(inv.Items);
    }

    [Fact]
    public async Task BuyItem_FailsOnHiddenItem()
    {
        var (_, _, shop) = Create();
        var (success, _, _, _, _, _) = await shop.BuyItemAsync(1, 10, 2, 1);
        Assert.False(success);
    }

    [Fact]
    public async Task SellItem_AddsGoldAndRegistersBuyback()
    {
        var (inv, gold, shop) = Create();
        // Le count d'un stack vit dans Durability (modèle natif).
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1001,
          Quantity = 10, Durability = 10, MaxDurability = 10 });

        var (success, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 4);
        Assert.True(success);
        Assert.Equal(1004, gold[1]); // 1000 + 4×floor(20×0.05)=4
        Assert.Equal(6, inv.Items.Single().Quantity);

        var buyback = shop.GetBuybackList(1);
        Assert.Single(buyback);
        Assert.Equal(1001, buyback[0].ItemId);
        Assert.Equal(4, buyback[0].PaidPrice);
    }

    [Fact]
    public async Task SellItem_ScalesByDurability()
    {
        var (inv, gold, shop) = Create();
        // 75% de durabilité → floor(400×0.05×0.75)=15 par unité.
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1002,
          Quantity = 1, Durability = 75, MaxDurability = 100 });
        var (success, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 1);
        Assert.True(success);
        Assert.Equal(1015, gold[1]);
    }

    [Fact]
    public async Task SellItem_RejectsBelowNativeThreshold()
    {
        var (inv, _, shop) = Create();
        // SysPrice 10 × 0.05 = 0.5 < 1.0 → vente refusée (texte 0x6e0).
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1003, Quantity = 5 });
        var (success, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 1);
        Assert.False(success);
        Assert.Equal(5, inv.Items.Single().Quantity);
    }

    [Fact]
    public async Task SellItem_FailsOnEquippedItem()
    {
        var (inv, _, shop) = Create();
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1001, Quantity = 1, IsEquipped = true });
        var (success, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 1);
        Assert.False(success);
    }

    [Fact]
    public async Task BuyBack_RestoresItemWithSnapshotAndDebitsPrice()
    {
        var (inv, gold, shop) = Create();
        // Équipement socketé + enchanté — le snapshot doit survivre
        // au rachat (record SItemData natif conservé par la mission).
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1002,
          Quantity = 1, Durability = 80, MaxDurability = 100,
          EnchantLevel = 5, EmbeddedAmount = 2,
          EmbeddedId1 = 3001, EmbeddedId2 = 3002, Combo = 7,
          Witchcraft = 42 });

        var (sold, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 1);
        Assert.True(sold);
        // Prix de vente : floor(400×0.05×80/100) = 16.
        Assert.Equal(1016, gold[1]);
        Assert.Empty(inv.Items);

        var (ok, err, itemId, qty, bag, slot) =
            await shop.BuyBackItemAsync(1, 0);
        Assert.True(ok, err);
        Assert.Equal(1002, itemId);
        Assert.Equal(1, qty);
        Assert.Equal(0, bag);
        Assert.Equal(1000, gold[1]); // rachat = prix payé (16)

        var restored = Assert.Single(inv.Items);
        Assert.Equal(80, restored.Durability);
        Assert.Equal(5, restored.EnchantLevel);
        Assert.Equal(3001, restored.EmbeddedId1);
        Assert.Equal(3002, restored.EmbeddedId2);
        Assert.Equal(7, restored.Combo);
        Assert.Equal(42, restored.Witchcraft);
        Assert.Empty(shop.GetBuybackList(1));
    }

    [Fact]
    public async Task BuyBack_RestoresPartialStackQuantity()
    {
        var (inv, gold, shop) = Create();
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1001,
          Quantity = 10, Durability = 10, MaxDurability = 99 });

        var (sold, _, _, _, _, _, _, _) = await shop.SellItemAsync(1, 0, 0, 4);
        Assert.True(sold);
        var (ok, _, _, qty, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.True(ok);
        Assert.Equal(4, qty);
        // Le rachat crée un nouveau stack de 4 (durability = count).
        Assert.Equal(10, inv.Items.Sum(i => i.Durability));
    }

    [Fact]
    public async Task BuyBack_FailsOnInvalidIndex()
    {
        var (_, _, shop) = Create();
        var (ok, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.False(ok);
        var (ok2, _, _, _, _, _) = await shop.BuyBackItemAsync(1, -1);
        Assert.False(ok2);
    }

    [Fact]
    public async Task BuyBack_FailsOnInsufficientGold()
    {
        var (inv, gold, shop) = Create();
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1002,
          Quantity = 1, Durability = 100, MaxDurability = 100 });
        await shop.SellItemAsync(1, 0, 0, 1);
        gold[1] = 0;

        var (ok, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.False(ok);
        // Entrée conservée — l'échec ne consomme pas la file.
        Assert.Single(shop.GetBuybackList(1));
    }

    [Fact]
    public async Task BuyBack_CappedAtTenEntries()
    {
        var (inv, _, shop) = Create();
        for (var i = 0; i < 12; i++)
        {
            inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
            { CharacterId = 1, BagId = 0, Slot = i, ItemId = 1001,
              Quantity = 1, Durability = 1 });
            await shop.SellItemAsync(1, 0, i, 1);
        }
        // Cap natif = 10 (trim pop_front de la deque mission).
        var list = shop.GetBuybackList(1);
        Assert.Equal(10, list.Count);
    }

    [Fact]
    public void ClientBuyBackList_EmptyWireIsFiveBytes()
    {
        var body = Eden.Zone.Codecs.BuybackPackets.BuildClientBuyBackList(
            3, Array.Empty<ShopService.BuybackEntry>());
        // Natif : [u8 flag][u16 0][s16 -1] — 5 octets.
        Assert.Equal(new byte[] { 0x03, 0x00, 0x00, 0xFF, 0xFF }, body);
    }

    [Fact]
    public void ClientBuyBackRemove_WireIsFlag2AndIndex()
    {
        // Natif CostMoney @0x830b630 : [u8 2][u16 0][s16 index].
        var body = Eden.Zone.Codecs.BuybackPackets
            .BuildClientBuyBackRemove(4);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x04, 0x00 }, body);
    }

    [Fact]
    public async Task BuyBack_EntryRemovedBeforeCreditBlocksRespam()
    {
        var (inv, gold, shop) = Create();
        inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, BagId = 0, Slot = 0, ItemId = 1002,
          Quantity = 1, Durability = 100, MaxDurability = 100 });
        await shop.SellItemAsync(1, 0, 0, 1);

        var (ok, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.True(ok);
        // Second rachat du même index = row fantôme → refus natif,
        // aucun item dupliqué, or inchangé.
        var goldAfter = gold[1];
        var (ok2, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.False(ok2);
        Assert.Single(inv.Items);
        Assert.Equal(goldAfter, gold[1]);
    }

    [Fact]
    public async Task BuyBack_FailedRestoreReinsertsEntryAtSameIndex()
    {
        var (inv, gold, shop) = Create();
        // Deux ventes → deux entrées ; on vide l'or avant le rachat
        // de l'index 1 → l'entrée doit revenir à sa position.
        for (var i = 0; i < 2; i++)
            inv.Items.Add(new Eden.Core.Game.Inventory.ItemEntity
            { CharacterId = 1, BagId = 0, Slot = i, ItemId = 1002,
              Quantity = 1, Durability = 100, MaxDurability = 100 });
        await shop.SellItemAsync(1, 0, 0, 1);
        await shop.SellItemAsync(1, 0, 1, 1);
        gold[1] = 0;

        var (ok, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 1);
        Assert.False(ok);
        Assert.Equal(2, shop.GetBuybackList(1).Count);
        // L'entrée 0 reste rachetable une fois l'or revenu.
        gold[1] = 1000;
        var (ok2, _, _, _, _, _) = await shop.BuyBackItemAsync(1, 0);
        Assert.True(ok2);
        Assert.Single(shop.GetBuybackList(1));
    }

    [Fact]
    public void ClientBuyBackList_OneEntryLayout()
    {
        var item = new Eden.Core.Game.Inventory.ItemEntity
        { CharacterId = 1, ItemId = 1002, Durability = 80,
          MaxDurability = 100, EnchantLevel = 5, IsBound = true,
          EmbeddedAmount = 1, EmbeddedId1 = 3001 };
        var body = Eden.Zone.Codecs.BuybackPackets.BuildClientBuyBackList(
            3, new[] { new ShopService.BuybackEntry(item, 1, 16) });

        using var ms = new System.IO.MemoryStream(body);
        using var r = new System.IO.BinaryReader(ms);
        Assert.Equal(3, r.ReadByte());          // flag
        Assert.Equal(1, r.ReadUInt16());        // count
        Assert.Equal(1002, r.ReadUInt16());     // item_id
        Assert.Equal(80, r.ReadInt16());        // durability
        Assert.Equal(0, r.ReadInt16());         // identify
        Assert.Equal(0, r.ReadInt16());         // f3
        Assert.Equal(1, r.ReadInt16());         // bind
        Assert.Equal(1, r.ReadByte());          // embedded_amount
        Assert.Equal(3001, r.ReadInt16());      // embedded_id1
        for (var i = 0; i < 5; i++) Assert.Equal(-1, r.ReadInt16()); // id2-6
        Assert.Equal(0, r.ReadInt16());         // witchcraft
        Assert.Equal(0, r.ReadUInt16());        // string len (vide)
        for (var i = 0; i < 11; i++) Assert.Equal(0, r.ReadInt16()); // extra
        Assert.Equal(5, r.ReadInt16());         // strengthen
        Assert.Equal(100, r.ReadInt16());       // cur_maxdurability
        Assert.Equal(0L, r.ReadInt64());        // due_date
        for (var i = 0; i < 18; i++) Assert.Equal(0, r.ReadByte());  // train
        Assert.Equal(0, r.ReadInt16());         // f30
        Assert.Equal(16u, r.ReadUInt32());      // prix de rachat
        for (var i = 0; i < 5; i++) Assert.Equal(0, r.ReadInt16());  // queue
        Assert.Equal(-1, r.ReadInt16());        // trailing
        Assert.Equal(ms.Length, ms.Position);   // rien de plus
    }

    [Fact]
    public void StoreParser_LoadsRealStore()
    {
        var store = LoadStore();
        Assert.Equal(1, store.Id);
        Assert.True(store.Items.Count >= 10);
        Assert.Equal(31900, store.Items[0].ItemId);
    }

    // --- Monnaies typées (priceType ≠ 1 — BuyItems natif
    //     @0x080aca40 : col30 S_Item → CItemData+0x78) ---

    private static (InMemoryInventoryRepository inv,
        List<(int Pt, long Amt)> debits, ShopService shop,
        ShopService.ShopCurrencyPort port) CreateTyped(
            byte priceType, long? balance)
    {
        var inv = new InMemoryInventoryRepository();
        var defs = new[]
        {
            new ItemParser.ItemDef { Id = 1001, SellPrice = 20,
                MaxStack = 99, PriceType = priceType },
        };
        var invSvc = new InventoryService(inv, defs);
        var stores = new[] { new ShopAndMiscParsers.StoreDef { Id = 10 } };
        stores[0].Items.Add((1001, "a", 1));
        var parameters = new ParameterParser.ParameterSet(
            new Dictionary<int, float>(), new Dictionary<int, string>());
        var gold = new Dictionary<int, long> { [1] = 1000 };
        var shop = new ShopService(invSvc, stores, defs, parameters,
            getGold: id => Task.FromResult(gold.GetValueOrDefault(id)),
            setGold: (id, v) => { gold[id] = v; return Task.CompletedTask; });
        var debits = new List<(int, long)>();
        var port = new ShopService.ShopCurrencyPort(
            (cid, pt) => Task.FromResult(
                pt == priceType ? balance : (long?)null),
            (cid, pt, amt) =>
            {
                debits.Add((pt, amt));
                return Task.CompletedTask;
            });
        return (inv, debits, shop, port);
    }

    [Fact]
    public async Task BuyItem_TypedCurrency_DebitsAndGrants()
    {
        var (inv, debits, shop, port) = CreateTyped(5, 500);
        var (success, err, itemId, _, _, _) =
            await shop.BuyItemAsync(1, 10, 0, 2, port);
        Assert.True(success, err);
        Assert.Equal(1001, itemId);
        Assert.Single(debits);
        Assert.Equal((5, 40L), debits[0]); // 2 × SysPrice 20
        Assert.Contains(inv.Items, i => i.ItemId == 1001
            && i.Quantity == 2);
    }

    [Fact]
    public async Task BuyItem_TypedCurrency_InsufficientBalance()
    {
        var (inv, debits, shop, port) = CreateTyped(0xb, 39);
        var (success, _, _, _, _, _) =
            await shop.BuyItemAsync(1, 10, 0, 2, port);
        Assert.False(success);
        Assert.Empty(debits);
        Assert.Empty(inv.Items);
    }

    [Fact]
    public async Task BuyItem_TypedCurrency_UnsupportedRefuses()
    {
        // priceType 4 (lover) sans stockage → GetBalance null.
        var (inv, debits, shop, port) = CreateTyped(4, null);
        var (success, _, _, _, _, _) =
            await shop.BuyItemAsync(1, 10, 0, 1, port);
        Assert.False(success);
        Assert.Empty(debits);
        Assert.Empty(inv.Items);
    }

    [Fact]
    public async Task BuyItem_TypedCurrency_NullPortRefuses()
    {
        var (inv, _, shop, _) = CreateTyped(0x13, 999);
        var (success, _, _, _, _, _) = await shop.BuyItemAsync(1, 10, 0, 1);
        Assert.False(success);
        Assert.Empty(inv.Items);
    }

    [Fact]
    public async Task BuyItem_TypedCurrency_DoesNotTouchGold()
    {
        var gold = 0L;
        var inv = new InMemoryInventoryRepository();
        var defs = new[]
        {
            new ItemParser.ItemDef { Id = 1001, SellPrice = 20,
                MaxStack = 99, PriceType = 0x14 },
        };
        var invSvc = new InventoryService(inv, defs);
        var stores = new[] { new ShopAndMiscParsers.StoreDef { Id = 10 } };
        stores[0].Items.Add((1001, "a", 1));
        var parameters = new ParameterParser.ParameterSet(
            new Dictionary<int, float>(), new Dictionary<int, string>());
        var shop = new ShopService(invSvc, stores, defs, parameters,
            getGold: _ => Task.FromResult(0L),
            setGold: (_, v) => { gold = v; return Task.CompletedTask; });
        var port = new ShopService.ShopCurrencyPort(
            (_, _) => Task.FromResult<long?>(100),
            (_, _, _) => Task.CompletedTask);

        var (success, _, _, _, _, _) =
            await shop.BuyItemAsync(1, 10, 0, 1, port);
        Assert.True(success);
        Assert.Equal(0, gold); // l'or n'est jamais consulté/débité
    }

    private static ShopAndMiscParsers.StoreDef LoadStore()
    {
        var dir = TestPaths.RepoRoot + "/server_us/Data/db_decoded";
        var stores = ShopAndMiscParsers.ParseStores(System.IO.Path.Combine(dir, "S_Store.ini"));
        Assert.NotEmpty(stores);
        return stores[0];
    }
}
