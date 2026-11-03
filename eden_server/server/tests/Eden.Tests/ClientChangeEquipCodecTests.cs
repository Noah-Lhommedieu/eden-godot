using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Tests wire du NCID 276 <c>CNC_CZ_ClientChangeEquip</c> —
/// delta d'un slot d'équipement (<c>NotifyChangeEquipment</c> natif
/// @0x843df10, Serialize @0x843exxx) : [i32 char][u16 loc][u16 tid]
/// [s16 cont][i8 rev][u8 flags][str][u24 mask chaîné][34B tail].</summary>
public class ClientChangeEquipCodecTests
{
    private static ItemEntity FullItem() => new()
    {
        ItemId = 15001, Slot = 3, EmbeddedAmount = 5, Combo = 12,
        EnchantLevel = 7, DueDate = 123456789,
    };

    [Fact]
    public void Build_ItemPlein_PrefixeEtMask()
    {
        var it = FullItem();
        it.SetEmbeddedId(0, 111);
        it.SetEmbeddedId(2, 222);
        var b = NativeItemCodec.BuildChangeEquip(7777, 3, 0, 42, it);
        var r = new BinaryReader(new MemoryStream(b));
        Assert.Equal(7777, r.ReadInt32());     // i32 — char+0x34
        Assert.Equal(3, r.ReadUInt16());       // u16 — loc (param_1)
        Assert.Equal(15001, r.ReadUInt16());   // u16 — tid (proto+4)
        Assert.Equal(0, r.ReadInt16());        // s16 — conteneur
        Assert.Equal(42, r.ReadSByte());       // i8  — rev char+0x4dc
        Assert.Equal(5, r.ReadByte());         // u8  — flags &7
        Assert.Equal(0, r.ReadUInt16());       // u16 — len str vide
        // mask : bits0+2 (embedded 0,2), bit6 (amount), bit7 (combo),
        // bit18 (enchant), bit20 (due) = 0x1400C5
        var mask = r.ReadByte() | r.ReadByte() << 8 | r.ReadByte() << 16;
        Assert.Equal(0x1400C5u, (uint)mask);
        Assert.Equal(111, r.ReadInt16());      // embedded0
        Assert.Equal(222, r.ReadInt16());      // embedded2
        Assert.Equal(5, r.ReadInt16());        // embedded_amount
        Assert.Equal(12, r.ReadInt16());       // combo
        Assert.Equal(7, r.ReadUInt16());       // strengthen
        Assert.Equal(123456789L, r.ReadInt64());// due_date
        // tail 34B — reste exactement 34 octets
        Assert.Equal(34, b.Length - (int)r.BaseStream.Position);
    }

    [Fact]
    public void Build_SlotVide_BrancheNative()
    {
        var b = NativeItemCodec.BuildChangeEquip(7777, 5, 2, 1, null);
        Assert.Equal(4 + 2 + 2 + 2 + 1 + 1 + 2 + 3 + 34, b.Length);
        var r = new BinaryReader(new MemoryStream(b));
        Assert.Equal(7777, r.ReadInt32());
        Assert.Equal(5, r.ReadUInt16());
        Assert.Equal(0, r.ReadUInt16());       // tid 0 — slot vidé
        Assert.Equal(2, r.ReadInt16());        // conteneur
        Assert.Equal(1, r.ReadSByte());        // rev
        Assert.Equal(0xff, r.ReadByte());      // flags 0xff — branche vide
        Assert.Equal(0, r.ReadUInt16());       // str vide
        Assert.Equal(0, r.ReadByte());         // mask 0 (3 octets)
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        for (var i = 0; i < 34; i++)           // tail à 0
            Assert.Equal(0, r.ReadByte());
        Assert.Equal(b.Length, (int)r.BaseStream.Position);
    }

    [Fact]
    public void Build_SansChampsConditionnels_MaskZero()
    {
        var it = new ItemEntity { ItemId = 25001, Slot = 0 };
        var b = NativeItemCodec.BuildChangeEquip(1, 0, 0, 9, it);
        var r = new BinaryReader(new MemoryStream(b));
        r.ReadBytes(4 + 2 + 2 + 2 + 1);
        Assert.Equal(0, r.ReadByte());         // flags &7 = 0
        r.ReadUInt16();                        // str len
        Assert.Equal(0, r.ReadByte());         // mask u24 = 0
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(34, b.Length - (int)r.BaseStream.Position);
    }
}
