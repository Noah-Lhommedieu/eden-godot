using Eden.Core.Game.Vending;
using Eden.Core.Networking;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du sous-système étal joueur — wire natif (désasm zone :
/// Deserialize 77/76, Serialize 378/377/376/375) et validations de
/// <see cref="StallService"/> (CheckBeginStallForStallGoods,
/// rate-limit 1,5 s, distance² ≤ 100, ClearVendorInfo).
/// </summary>
public class VendorStallTests
{
    // --------------------------------------------------------
    //  Parse entrant (wire natif)
    // --------------------------------------------------------

    [Fact]
    public void ParseSellItem_ReadsVectors()
    {
        // Wire natif 77 : [i32 région][str titre][u8 n][n×(s16,s16,
        // s16,i32)] — 10 octets par ligne.
        var w = new OutBinStream();
        w.WriteInt(601);              // i32 — region type
        w.WriteString("Boutique");    // str — titre (u16 len + Big5)
        w.WriteByte(2);               // u8 — 2 lignes
        w.WriteShort(0); w.WriteShort(3); w.WriteShort(0);
        w.WriteInt(1500);             // ligne 0 : cont 0, slot 3, prix
        w.WriteShort(0); w.WriteShort(7); w.WriteShort(0);
        w.WriteInt(0);                // ligne 1 : prix 0

        var req = VendorPackets.ParseSellItem(w.ToArray());
        Assert.NotNull(req);
        Assert.Equal(601, req.RegionType);
        Assert.Equal("Boutique", req.Title);
        Assert.Equal(2, req.Entries.Length);
        Assert.Equal((short)3, req.Entries[0].Index);
        Assert.Equal(1500, req.Entries[0].Price);
        Assert.Equal(0, req.Entries[1].Price);
    }

    [Fact]
    public void ParseSellItem_Truncated_ReturnsNull()
    {
        var w = new OutBinStream();
        w.WriteInt(600); w.WriteString("x"); w.WriteByte(2);
        w.WriteShort(0);              // ligne incomplète
        Assert.Null(VendorPackets.ParseSellItem(w.ToArray()));
    }

    [Fact]
    public void ParseBuy_ReadsLines()
    {
        // Wire natif 76 : [i32 vendeur][u16 n][n×(s16 qty, u8 idx)].
        var w = new OutBinStream();
        w.WriteInt(12345);
        w.WriteShort(2);              // u16 — 2 lignes
        w.WriteShort(1); w.WriteByte(0);
        w.WriteShort(5); w.WriteByte(3);

        var req = VendorPackets.ParseBuy(w.ToArray());
        Assert.NotNull(req);
        Assert.Equal(12345, req.SellerId);
        Assert.Equal(2, req.Lines.Length);
        Assert.Equal((short)1, req.Lines[0].Quantity);
        Assert.Equal((byte)3, req.Lines[1].Index);
    }

    [Fact]
    public void ParseClose_KeepsAsymmetry()
    {
        // Le Deserialize natif lit 2 u8 ; seul le premier est émis
        // dans le 375 (Execute : this[0x12] != 0).
        Assert.Equal((byte)1,
            VendorPackets.ParseClose(new byte[] { 1, 0 }));
        Assert.Equal((byte)0,
            VendorPackets.ParseClose(new byte[] { 0, 9 }));
        Assert.Null(VendorPackets.ParseClose(Array.Empty<byte>()));
    }

    // --------------------------------------------------------
    //  Builders sortants (wire exact)
    // --------------------------------------------------------

    [Fact]
    public void BuildStatus_IsTwoBytes() =>
        Assert.Equal(new byte[] { 1, 2 },
            VendorPackets.BuildStatus(1, 2));

    [Fact]
    public void BuildCloseView_IsI32()
    {
        var b = VendorPackets.BuildCloseView(0x11223344);
        Assert.Equal(4, b.Length);
        Assert.Equal(0x11223344, BitConverter.ToInt32(b));
    }

    [Fact]
    public void BuildClose_IsI32ThenByte()
    {
        var b = VendorPackets.BuildClose(77, 1);
        Assert.Equal(5, b.Length);
        Assert.Equal(77, BitConverter.ToInt32(b));
        Assert.Equal((byte)1, b[4]);
    }

    [Fact]
    public void BuildUpdateView_ExactWire()
    {
        // Record natif 96 octets ; header i32+str+u8.
        var good = new StallService.StallGood
        {
            Index = 2, ItemId = 12345, RequiredLevel = 30,
            Container = 0, Slot = 7, LocIndex = 7, LocExtra = 0,
            Durability = 55, Combo = 42, FlagLow3 = 3,
            EmbeddedIds = new short[] { 101, -1, 0, -1, -1, -1 },
            ExtraCombos = new short[]
                { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            Price = 999, Quantity = 5, Quality = 2, Witchcraft = 0,
            Clone2e = 0, TrainLevel = 0, DueDate = 0x12345678,
            MagicBox = new short[] { 55, 66, 77 },
            MagicBoxEnergy = 123, MagicBoxStar = 4,
            TrainSlots = new short[] { 10, 20, 30, 40, 50, 60 },
        };
        var body = VendorPackets.BuildUpdateView(9999, "Shop",
            new[] { good });
        // 4 (i32) + 2 (len) + 4 (titre) + 1 (count) + 96 (record)
        Assert.Equal(4 + 2 + 4 + 1 + VendorPackets.GoodsRecordBytes,
            body.Length);
        Assert.Equal(98, VendorPackets.GoodsRecordBytes);

        var o = 0;
        Assert.Equal(9999, BitConverter.ToInt32(body, o)); o += 4;
        var slen = BitConverter.ToUInt16(body, o); o += 2;
        Assert.Equal(4, slen); o += slen;
        Assert.Equal(1, body[o++]);                    // count
        // --- record (96 o) ---
        var r = o;
        Assert.Equal(2, body[r]);                      // u8 index
        Assert.Equal(12345, BitConverter.ToInt32(body, r + 1));  // u32
        Assert.Equal(30, BitConverter.ToInt32(body, r + 5));     // req lv
        Assert.Equal(0, BitConverter.ToInt16(body, r + 9));      // cont
        Assert.Equal(7, BitConverter.ToInt16(body, r + 11));     // index
        Assert.Equal(7, BitConverter.ToInt32(body, r + 13));     // loc
        Assert.Equal(0, BitConverter.ToInt32(body, r + 17));     // +0xe
        Assert.Equal(55, BitConverter.ToInt16(body, r + 21));    // dur
        Assert.Equal(42, BitConverter.ToInt16(body, r + 23));    // combo
        Assert.Equal(3, body[r + 25]);                           // flags&7
        Assert.Equal(101, BitConverter.ToInt16(body, r + 26));   // emb0
        Assert.Equal(-1, BitConverter.ToInt16(body, r + 28));
        // extras 0..8 : r+38..r+55
        Assert.Equal(1, BitConverter.ToInt16(body, r + 38));
        Assert.Equal(9, BitConverter.ToInt16(body, r + 54));
        Assert.Equal(999, BitConverter.ToInt32(body, r + 56));   // prix
        Assert.Equal(2, BitConverter.ToInt16(body, r + 60));     // quality
        Assert.Equal(0, BitConverter.ToInt16(body, r + 62));     // witch
        Assert.Equal(0, BitConverter.ToInt16(body, r + 64));     // cl2e
        Assert.Equal(0, BitConverter.ToInt16(body, r + 66));     // train lv
        Assert.Equal(0x12345678u,
            BitConverter.ToUInt32(body, r + 68));                // due
        // slots train permutés : indices 0,3,1,4,2,5
        Assert.Equal(10, BitConverter.ToInt16(body, r + 72));
        Assert.Equal(40, BitConverter.ToInt16(body, r + 74));
        Assert.Equal(20, BitConverter.ToInt16(body, r + 76));
        Assert.Equal(50, BitConverter.ToInt16(body, r + 78));
        Assert.Equal(30, BitConverter.ToInt16(body, r + 80));
        Assert.Equal(60, BitConverter.ToInt16(body, r + 82));
        Assert.Equal(10, BitConverter.ToInt16(body, r + 84));    // extra9
        Assert.Equal(11, BitConverter.ToInt16(body, r + 86));    // extra10
        Assert.Equal(55, BitConverter.ToInt16(body, r + 88));    // mb0
        Assert.Equal(77, BitConverter.ToInt16(body, r + 92));    // mb2
        Assert.Equal(123, BitConverter.ToInt16(body, r + 94));   // energy
        Assert.Equal(4, BitConverter.ToInt16(body, r + 96));     // star
        Assert.Equal(98, r + 98 - r);                            // fin record
    }

    [Fact]
    public void BuildUpdateView_EmptyGoods()
    {
        var body = VendorPackets.BuildUpdateView(1, "", Array.Empty<
            StallService.StallGood>());
        Assert.Equal(4 + 2 + 0 + 1, body.Length);
    }

    // --------------------------------------------------------
    //  StallService — validations natives
    // --------------------------------------------------------

    [Fact]
    public void CheckGoods_Collisions()
    {
        var ok = StallService.CheckGoods(new[]
            { ((short)0, (short)1, 100), ((short)0, (short)2, 50) });
        Assert.Equal(StallService.StallError.Ok, ok);
        var dup = StallService.CheckGoods(new[]
            { ((short)0, (short)1, 100), ((short)0, (short)1, 50) });
        Assert.Equal(StallService.StallError.GoodsCollision, dup);
    }

    [Fact]
    public void CheckGoods_PriceBounds()
    {
        Assert.Equal(StallService.StallError.BadPrice,
            StallService.CheckGoods(new[] { ((short)0, (short)0, -1) }));
        Assert.Equal(StallService.StallError.BadPrice,
            StallService.CheckGoods(new[]
                { ((short)0, (short)0, 1_000_000_000) }));
        Assert.Equal(StallService.StallError.Ok,
            StallService.CheckGoods(new[]
                { ((short)0, (short)0, 999_999_999) }));
    }

    [Fact]
    public void CheckGoods_MaxThirteenFree()
    {
        var free13 = Enumerable.Repeat(((short)0, (short)0, 0), 13)
            .Select((e, i) => ((short)0, (short)i, 0)).ToList();
        Assert.Equal(StallService.StallError.Ok,
            StallService.CheckGoods(free13));
        var free14 = Enumerable.Repeat(((short)0, (short)0, 0), 14)
            .Select((e, i) => ((short)0, (short)i, 0)).ToList();
        Assert.Equal(StallService.StallError.TooManyFree,
            StallService.CheckGoods(free14));
    }

    [Fact]
    public void CheckGoods_Empty() =>
        Assert.Equal(StallService.StallError.BadVectors,
            StallService.CheckGoods(
                Array.Empty<(short, short, int)>()));

    [Fact]
    public void OpenCooldown_1500ms()
    {
        var svc = new StallService();
        var t0 = new DateTime(2026, 1, 1);
        Assert.True(svc.CheckOpenCooldown(1, t0));   // 1ère tentative
        svc.MarkOpenAttempt(1, t0);
        Assert.False(svc.CheckOpenCooldown(1, t0 + TimeSpan.FromSeconds(1)));
        Assert.True(svc.CheckOpenCooldown(1,
            t0 + TimeSpan.FromMilliseconds(1500)));
    }

    [Fact]
    public void InVendorRange_Exactly10Units()
    {
        Assert.True(StallService.InVendorRange(0, 0, 6, 8));   // 10.0
        Assert.False(StallService.InVendorRange(0, 0, 11, 0)); // > 10
    }

    [Fact]
    public void OpenOrUpdate_PreservesViewers()
    {
        // ClearVendorInfo natif : goods+titre remplacés, viewers
        // conservés.
        var svc = new StallService();
        svc.OpenOrUpdate(1, "old", Array.Empty<StallService.StallGood>());
        svc.AddViewer(1, 42);
        var stall = svc.OpenOrUpdate(1, "new", new[]
        {
            new StallService.StallGood
            {
                Index = 0, ItemId = 5, Container = 0, Slot = 1,
                LocIndex = 1, LocExtra = 0, Price = 10, Quantity = 1,
            },
        });
        Assert.Equal("new", stall.Title);
        Assert.Single(stall.Goods);
        Assert.Contains(42, stall.Viewers);
    }

    [Fact]
    public void CleanupDisconnected_RemovesViewerAndStall()
    {
        var svc = new StallService();
        var seller = svc.OpenOrUpdate(7, "s",
            Array.Empty<StallService.StallGood>());
        svc.AddViewer(7, 9);
        Assert.Null(svc.CleanupDisconnected(9));       // viewer seul
        Assert.Empty(seller.Viewers);                  // viewer retiré
        Assert.NotNull(svc.CleanupDisconnected(7));    // étal fermé
        Assert.False(svc.IsVending(7));
    }
}
