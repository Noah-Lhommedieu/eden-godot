using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des codecs item mall + neobank (vague Reforged).
/// Formats prouvés côté serveur C# : CNE_CW_* monde
/// (<c>Eden.World/Handlers/ItemMallHandlers.cs</c> —
/// <c>CNE_CW_ServerItemMallReqBuyItem</c>, catalogue
/// <c>ItemMallPackets.SerializeCatalog</c>, limites
/// <c>BuildLimit</c>, reçus <c>BuildReceivableQueryBody</c>) et
/// CNE_CZ_* zone (<c>ZoneMessages.g.cs</c> 70/83).
/// L'audit rf_itemmall/rf_neobank n'a trouvé <b>aucun</b> protocole
/// propre aux DLL — ce sont des patchs client.
/// </summary>
public class ZoneItemMallTests
{
    // ---------------- C→Z zone ----------------

    [Fact]
    public void ReceivableEnter_EmptyBody()
    {
        var w = new BinWriter();
        new ZoneItemMallReceivableEnter().Serialize(w);
        Assert.Empty(w.ToArray());
        Assert.Equal(70, new ZoneItemMallReceivableEnter().Opcode);
    }

    [Fact]
    public void MallUseItem_Wire14B_PlusString()
    {
        // [s16 sel][s16 cont][s16 slot][i32 uiItemId][str params]
        var w = new BinWriter();
        new ZoneItemMallUseItem
        {
            Sel = 1, ContainerIndex = 2, Slot = 7,
            UiItemId = 0x11223344, Params = "x",
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(10 + 3, b.Length); // u16 len + 'x'
        var m = new ZoneItemMallUseItem();
        m.Deserialize(new BinReader(b));
        Assert.Equal(1, m.Sel);
        Assert.Equal(2, m.ContainerIndex);
        Assert.Equal(7, m.Slot);
        Assert.Equal(0x11223344, m.UiItemId);
        Assert.Equal("x", m.Params);
    }

    // ---------------- C→W monde ----------------

    [Fact]
    public void AnythingQuery_And_PointInfoQuery_Empty()
    {
        var w1 = new BinWriter();
        new WorldItemMallAnythingQuery().Serialize(w1);
        Assert.Empty(w1.ToArray());
        var w2 = new BinWriter();
        new WorldItemMallPointInfoQuery().Serialize(w2);
        Assert.Empty(w2.ToArray());
    }

    [Fact]
    public void ReceivableQuery_Wire6B()
    {
        // Format déjà prouvé (mitm ligne 30) : [u16][str][str].
        var w = new BinWriter();
        new WorldItemMallReceivableQuery { Field1 = 21 }
            .Serialize(w);
        var m = new WorldItemMallReceivableQuery();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(21, m.Field1);
    }

    [Fact]
    public void ReceiveAttempt_Wire4B()
    {
        var w = new BinWriter();
        new WorldItemMallReceiveAttempt { ReceiptId = 0x89abcdef }
            .Serialize(w);
        var b = w.ToArray();
        Assert.Equal(4, b.Length);
        var m = new WorldItemMallReceiveAttempt();
        m.Deserialize(new BinReader(b));
        Assert.Equal(0x89abcdefu, m.ReceiptId);
    }

    [Fact]
    public void ListQuery_Wire4B()
    {
        var w = new BinWriter();
        new WorldItemMallListQuery { Field1 = 0, Version = 7 }
            .Serialize(w);
        Assert.Equal(4, w.ToArray().Length);
        var m = new WorldItemMallListQuery();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(7, m.Version);
    }

    [Fact]
    public void BuyRequest_WireRoundTrip()
    {
        // [s16 key(mu*100+grp)][s16 idx][s16 amount][u16 version]
        // [str][str recipient][str]
        var w = new BinWriter();
        new WorldItemMallBuyRequest
        {
            Key = 208, Index = 3, Amount = 2, Version = 42,
            Recipient = "Alice",
        }.Serialize(w);
        var m = new WorldItemMallBuyRequest();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(208, m.Key);
        Assert.Equal(3, m.Index);
        Assert.Equal(2, m.Amount);
        Assert.Equal(42, m.Version);
        Assert.Equal("", m.Field5);
        Assert.Equal("Alice", m.Recipient);
        Assert.Equal("", m.Field7);
    }

    // ---------------- W→C monde ----------------

    [Fact]
    public void Anything_U8()
    {
        var w = new BinWriter();
        w.WriteBool(true);
        var m = new WorldItemMallAnything();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.True(m.Anything);
    }

    [Fact]
    public void ReceivableList_Records()
    {
        var w = new BinWriter();
        w.WriteShort(2);                        // s16 — 2 reçus
        w.WriteInt(1001);                       // u32 id
        w.WriteUShort(51773);                   // u16 item
        w.WriteUShort(3);                       // u16 qty
        w.WriteString("2026-10-26 08:00:00");   // str date
        w.WriteInt(1002);
        w.WriteUShort(40001);
        w.WriteUShort(1);
        w.WriteString("");
        var m = new WorldItemMallReceivableList();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.Receipts.Count);
        Assert.Equal(1001u, m.Receipts[0].Id);
        Assert.Equal(51773, m.Receipts[0].ItemId);
        Assert.Equal(3, m.Receipts[0].Quantity);
        Assert.Equal(40001, m.Receipts[1].ItemId);
    }

    [Fact]
    public void ReceiveResult_S16_U32()
    {
        var w = new BinWriter();
        w.WriteShort(0x19);                     // s16 — inventaire plein
        w.WriteInt(1001);                       // u32 receipt
        var m = new WorldItemMallReceiveResult();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x19, m.Result);
        Assert.Equal(1001u, m.ReceiptId);
        Assert.Equal("Inventaire plein.",
            ItemMallResults.Text(m.Result));
    }

    [Fact]
    public void CatalogList_RoundTrip()
    {
        var src = new WorldItemMallList { Version = 9 };
        src.Entries.Add(new ItemMallEntry
        {
            Key = 208, Index = 3, ItemId = 51773, ItemNum = 5,
            Point = 300, SpecialPrice = 150, NumLimit = 2,
            ItemColor = 0x1122334455667788, NotSellDate = 1700000000,
            FortuneBag = "fb_01", Note = "promo", LevelLimit = 20,
            SellDate = 1600000000,
        });
        var w = new BinWriter();
        src.Serialize(w);
        var m = new WorldItemMallList();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(9, m.Version);
        var e = Assert.Single(m.Entries);
        Assert.Equal(208, e.Key);
        Assert.Equal(2, e.MoneyUnit);    // key/100
        Assert.Equal(8, e.Group);        // key%100
        Assert.Equal(51773, e.ItemId);
        Assert.Equal(5, e.ItemNum);
        Assert.Equal(300u, e.Point);
        Assert.Equal(150u, e.SpecialPrice);
        Assert.Equal(150u, e.EffectivePrice); // promo < plein
        Assert.Equal(2, e.NumLimit);
        Assert.Equal(0x1122334455667788, e.ItemColor);
        Assert.Equal("fb_01", e.FortuneBag);
        Assert.Equal("promo", e.Note);
        Assert.Equal(20, e.LevelLimit);
    }

    [Fact]
    public void BuyResult_S16_I32_I32()
    {
        var w = new BinWriter();
        w.WriteShort(0);                        // ok
        w.WriteInt(950);                        // pvalues restants
        w.WriteInt(120);                        // bonus restants
        var m = new WorldItemMallBuyResult();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(ItemMallResults.Ok, m.Result);
        Assert.Equal(950, m.Pvalues);
        Assert.Equal(120, m.Bonus);
    }

    [Fact]
    public void Limit_Rows()
    {
        var w = new BinWriter();
        w.WriteUShort(9);                       // u16 version
        w.WriteUShort(1);                       // u16 1 ligne
        w.WriteShort(208);                      // s16 key
        w.WriteShort(3);                        // s16 index
        w.WriteUShort(51773);                   // u16 item
        w.WriteUShort(5);                       // u16 num
        w.WriteUShort(4);                       // u16 déjà acheté
        var m = new WorldItemMallLimit();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(9, m.Version);
        var r = Assert.Single(m.Rows);
        Assert.Equal(208, r.Key);
        Assert.Equal(3, r.Index);
        Assert.Equal(4, r.Sold);
    }

    // ---------------- état agrégé ----------------

    [Fact]
    public void State_CatalogLimitPointsFlow()
    {
        ItemMallState.Reset();
        var list = new WorldItemMallList { Version = 3 };
        list.Entries.Add(new ItemMallEntry
        { Key = 108, Index = 1, ItemId = 51773, Point = 10 });
        ItemMallState.ApplyListData(list);
        Assert.Equal(3, ItemMallState.Version);
        Assert.Single(ItemMallState.Entries);

        var lim = new WorldItemMallLimit { Version = 3 };
        lim.Rows.Add(new ItemMallLimit(108, 1, 51773, 1, 2));
        ItemMallState.ApplyLimitData(lim);
        Assert.Equal(2,
            ItemMallState.Entries[(108 << 16) | 1].Sold);

        ItemMallState.ApplyPointsData(500, 60);
        Assert.Equal(500, ItemMallState.Points);
        Assert.Equal(60, ItemMallState.Bonus);

        var rl = new WorldItemMallReceivableList();
        rl.Receipts.Add(new ItemMallReceipt(7, 51773, 1, "t"));
        ItemMallState.ApplyReceiptsData(rl);
        Assert.True(ItemMallState.AnythingReceivable);
        Assert.Single(ItemMallState.Receipts);

        var rr = new WorldItemMallReceiveResult
        { Result = 0, ReceiptId = 7 };
        ItemMallState.ApplyReceiveResultData(rr);
        Assert.False(ItemMallState.AnythingReceivable);
        Assert.Empty(ItemMallState.Receipts);

        ItemMallState.ApplyBuyResultData(new WorldItemMallBuyResult
        { Result = 0, Pvalues = 490, Bonus = 60 });
        Assert.Equal(490, ItemMallState.Points);
        Assert.Equal("OK", ItemMallState.LastStatus);
        ItemMallState.Reset();
    }

    [Fact]
    public void ResultTexts_Map()
    {
        Assert.Equal("OK", ItemMallResults.Text(0));
        Assert.Equal("Points insuffisants (AP).",
            ItemMallResults.Text(1));
        Assert.Equal("Inventaire plein.", ItemMallResults.Text(0x19));
        Assert.Equal("Erreur 99", ItemMallResults.Text(99));
    }

    // ---------------- neobank (client-side only) ----------------

    [Fact]
    public void NeobankSettings_Cycles()
    {
        var cols = NeobankSettings.Columns;
        try
        {
            NeobankSettings.Columns = 32;
            Assert.Equal(16, NeobankSettings.CycleColumns());
            Assert.Equal(20, NeobankSettings.CycleColumns());
            NeobankSettings.Rows = 20;
            Assert.Equal(1, NeobankSettings.CycleRows());
            var mode = NeobankSettings.ScrollMode;
            Assert.Equal(1 - mode, NeobankSettings.ToggleMode());
        }
        finally
        {
            NeobankSettings.Columns = cols;
            NeobankSettings.Rows = 8;
            NeobankSettings.ScrollMode = 1;
        }
    }

    [Fact]
    public void NeobankLocks_TogglePrune()
    {
        NeobankLocks.Wipe();
        Assert.False(NeobankLocks.IsLocked(0, 3));
        Assert.True(NeobankLocks.Toggle(0, 3));
        Assert.True(NeobankLocks.IsLocked(0, 3));
        Assert.False(NeobankLocks.IsLocked(1, 3));
        NeobankLocks.Toggle(0, 5);
        NeobankLocks.Prune(0, 4); // retire le verrou du slot 5
        Assert.True(NeobankLocks.IsLocked(0, 3));
        Assert.False(NeobankLocks.IsLocked(0, 5));
        Assert.False(NeobankLocks.Toggle(0, 3)); // retire
        NeobankLocks.Wipe();
    }
}
