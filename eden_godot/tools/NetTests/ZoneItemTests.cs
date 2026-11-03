using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages d'inventaire/équipement du lot S2
/// (<see cref="ZoneItemMessages"/>). Wire prouvé côté serveur :
/// ZoneMessages.g.cs (CNE_CZ_*/CNC_CZ_*) + désasm cité dans
/// InventoryHandlers.cs / ItemActionHandlers.cs / NativeItemCodec.cs.
/// </summary>
public class ZoneItemTests
{
    private static T RoundTrip<T>(T msg) where T : NetMessage, new()
    {
        var w = new BinWriter();
        msg.Serialize(w);
        var back = new T();
        back.Deserialize(new BinReader(w.ToArray()));
        return back;
    }

    // ---------- C→S ----------

    [Fact]
    public void SwitchItemLocation_Wire12B()
    {
        // [s16 type][s16 slot][s16 idx] ×2 — équipe (0,s,0)→(1,7,0).
        var w = new BinWriter();
        new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = 5, SrcIndex = 0,
            DstType = 1, DstSlot = 7, DstIndex = 0,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(12, b.Length);
        var m = RoundTrip(new ZoneSwitchItemLocation
        {
            SrcType = 0, SrcSlot = 5, SrcIndex = 2,
            DstType = 1, DstSlot = -1, DstIndex = -1,
        });
        Assert.Equal(5, m.SrcSlot);
        Assert.Equal(2, m.SrcIndex);
        Assert.Equal(-1, m.DstSlot);
        Assert.Equal(-1, m.DstIndex);
    }

    [Fact]
    public void ItemUse_Wire17B_CorpsCapture()
    {
        // Corps live capturé : 3×s16 + i32 + u8len(str vide) + 3×s16
        // = 17 octets — le préfixe u8 est le point critique.
        var w = new BinWriter();
        new ZoneItemUse
        {
            ContainerType = 0, ContainerIndex = 0, Slot = 3,
            Target = -1,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(17, b.Length);
        Assert.Equal(0, b[10]);            // u8 len du str param vide
        var m = RoundTrip(new ZoneItemUse
        {
            ContainerType = 0, ContainerIndex = 1, Slot = 9,
            Target = -1, Param = "",
            TargetContainerType = -1, TargetContainerIndex = -1,
            TargetLoc = -1,
        });
        Assert.Equal(9, m.Slot);
        Assert.Equal(-1, m.Target);
        Assert.Equal("", m.Param);
        Assert.Equal(-1, m.TargetLoc);
    }

    [Fact]
    public void ItemUse_ParamStr_U8Prefixe()
    {
        var m = RoundTrip(new ZoneItemUse
        {
            Slot = 1, Param = "abc",
        });
        Assert.Equal("abc", m.Param);
    }

    [Fact]
    public void DivideItem_Wire12B()
    {
        var m = RoundTrip(new ZoneDivideItem
        {
            SrcSlot = 4, SrcBag = 0, DstSlot = 12, DstBag = 0,
            Count = 25, Kind = 0,
        });
        Assert.Equal(25, m.Count);
        Assert.Equal(12, m.DstSlot);
    }

    // ---------- S→C ----------

    [Fact]
    public void SwitchItemLocationOk_Wire13B()
    {
        var w = new BinWriter();
        new ZoneSwitchItemLocationOk
        {
            DstSlot = 14, DstIndex = 3, Result = 1,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(13, b.Length);
        var m = RoundTrip(new ZoneSwitchItemLocationOk
        {
            DstSlot = -1, Result = 1,
        });
        Assert.True(m.Ok);
        Assert.Equal(-1, m.DstSlot);
    }

    [Fact]
    public void ClientItemUse_Wire16B()
    {
        var w = new BinWriter();
        new ZoneClientItemUse
        {
            CharId = 0x100, ItemId = 514, Result = 3,
        }.Serialize(w);
        // 2×i32 + u16 + 4×s16 = 18 octets (layout .g.cs serveur).
        Assert.Equal(18, w.Length);
        var m = RoundTrip(new ZoneClientItemUse
        {
            CharId = 1, ItemId = 514, Result = 3,
        });
        Assert.Equal((ushort)514, m.ItemId);
        Assert.True(m.Accepted);
        Assert.Equal(3, m.Result);
    }

    [Fact]
    public void PutItem_Parse274_CorpsNatif()
    {
        // Corps façon BuildPutItem sans méta :
        // [u16 id][s16 sac][s16 loc][s16 dur][s16 maxDur][u16 0]
        // [u8 flags][u24 mask=0x100][u32 create][34B].
        var w = new BinWriter();
        w.WriteUShort(12045);
        w.WriteShort(0);                  // sac 0
        w.WriteShort(7);                  // loc 7
        w.WriteShort(3);                  // count/dur
        w.WriteShort(75);                 // maxDur
        w.WriteShort(0);                  // str vide
        w.WriteByte(0x08);                // flags
        w.WriteBytes([0x00, 0x01, 0x00]); // mask bit8
        w.WriteInt(0x01020304);           // create_time
        w.WriteBytes(new byte[34]);
        var r = new BinReader(w.ToArray());
        Assert.True(NativeItemRecord.TryReadPutItem(r, out var it));
        Assert.Equal(12045, it.ItemId);
        Assert.Equal(0, it.Bag);
        Assert.Equal(7, it.Slot);
        Assert.Equal(3, it.Durability);
        Assert.Equal(75, it.MaxDurability);
        Assert.Equal(0x08, it.Flags);
    }

    [Fact]
    public void PutItem_Parse274_MaskAmount()
    {
        // mask = bit8 (create_time) + bit6 (amount s16) : les champs
        // conditionnels s'écrivent dans l'ordre bits0-5, bit6, bit7,
        // bit8, … — amount PUIS create_time.
        var w = new BinWriter();
        w.WriteUShort(11); w.WriteShort(1); w.WriteShort(2);
        w.WriteShort(40); w.WriteShort(40);
        w.WriteShort(0); w.WriteByte(0);
        w.WriteBytes([0x40, 0x01, 0x00]); // mask = bit6|bit8
        w.WriteShort(33);                 // bit6 : amount 33
        w.WriteInt(42);                   // bit8 : create_time
        w.WriteBytes(new byte[34]);
        var r = new BinReader(w.ToArray());
        Assert.True(NativeItemRecord.TryReadPutItem(r, out var it));
        Assert.Equal(33, it.Amount);
    }

    // ---------- sémantique des locs (miroir NativeEquipmentSlots) ---

    [Theory]
    [InlineData(7, new[] { 7, 8 })]     // 1H / dual
    [InlineData(19, new[] { 7, 8 })]
    [InlineData(8, new[] { 8 })]        // bouclier
    [InlineData(9, new[] { 7 })]        // 2H → main
    [InlineData(35, new[] { 10 })]      // arc
    [InlineData(10, new[] { 11 })]
    [InlineData(11, new[] { 12, 13 })]
    [InlineData(12, new[] { 14, 15 })]
    [InlineData(99, new int[0])]
    public void AllowedLocations_MiroirServeur(int type, int[] locs)
    {
        Assert.Equal(locs, ZoneItemActions.AllowedLocations(type));
    }

    [Theory]
    [InlineData(4, true)]   // armure
    [InlineData(17, true)]  // arme 1H
    [InlineData(34, true)]  // bouclier
    [InlineData(37, true)]  // collier
    [InlineData(40, true)]  // licence
    [InlineData(45, false)] // gemme (socket)
    [InlineData(60, false)] // œuf familier
    [InlineData(78, false)] // glyphe
    public void IsPlayerEquippableKind_MiroirServeur(int kind, bool ok)
    {
        Assert.Equal(ok, ZoneItemActions.IsPlayerEquippableKind(kind));
    }

    [Fact]
    public void PredictEquipLoc_PremierLibre()
    {
        var worn = new Dictionary<int, int> { [7] = 1009 };
        // 1H avec loc 7 occupée → loc 8 (dual).
        Assert.Equal(8, ZoneItemActions.PredictEquipLoc(7, worn));
        // Rien porté → loc canonique 7.
        Assert.Equal(7, ZoneItemActions.PredictEquipLoc(7, null));
        // Non équipable → −1.
        Assert.Equal(-1, ZoneItemActions.PredictEquipLoc(99, null));
    }

    [Fact]
    public void EquipRequest_DstLocResolue()
    {
        var req = ZoneItemActions.EquipRequest(0, 5, 7, null);
        Assert.Equal(0, req.SrcType);
        Assert.Equal(5, req.SrcSlot);
        Assert.Equal(1, req.DstType);
        Assert.Equal(7, req.DstSlot);
        Assert.Equal(0, req.DstIndex);
        var un = ZoneItemActions.UnequipRequest(7);
        Assert.Equal(1, un.SrcType);
        Assert.Equal(7, un.SrcSlot);
        Assert.Equal(-1, un.DstSlot);
        Assert.Equal(-1, un.DstIndex);
    }

    [Fact]
    public void LocMask_TousLocsCandidats()
    {
        // type 7 (1H) → bits 7+8 ; type 9 (2H) → bit 7 seul.
        Assert.Equal((1u << 7) | (1u << 8), ZoneItemActions.LocMask(7));
        Assert.Equal(1u << 7, ZoneItemActions.LocMask(9));
    }
}
