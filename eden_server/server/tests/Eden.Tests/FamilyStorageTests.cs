using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Codecs du coffre de famille — formats du désassemblage zone
/// (`CNC_CZ_ClientFamilyStorage*::Serialize` + emit
/// `CFamilyStorageShop::OpenShop` @0x08579440). Voir
/// docs/temp-plans/2026-10-23-family-storage-natif.md.</summary>
public class FamilyStorageTests
{
    private static ItemEntity SampleItem() => new()
    {
        Id = 42, CharacterId = 7, BagId = 1, Slot = 3,
        ItemId = 994, Maker = "acme",
        Durability = 75, MaxDurability = 75,
        Identify = 1, IsBound = false,
        CreateTime = 1_700_000_000, DueDate = 0,
        EmbeddedAmount = 0,
        EmbeddedId1 = -1, EmbeddedId2 = -1, EmbeddedId3 = -1,
        EmbeddedId4 = -1, EmbeddedId5 = -1, EmbeddedId6 = -1,
        Combo = 0, EnchantLevel = 0, PlusParameter = 0,
    };

    [Fact]
    public void DeleteItem_body_is_index_plus_loc()
    {
        var body = FamilyStoragePackets.BuildDeleteItem(2, 7);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x07, 0x00 }, body);
    }

    [Fact]
    public void StorageData_empty_storage_is_header_plus_trailer()
    {
        // [s16 0][s16 index][u16 0][s16 0] — 6 octets, aucun item.
        var body = FamilyStoragePackets.BuildStorageData(2, new List<ItemEntity>());
        Assert.Equal(new byte[] { 0, 0, 0x02, 0x00, 0, 0, 0, 0 }, body);
    }

    [Fact]
    public void StorageData_header_fields_match_native_layout()
    {
        var body = FamilyStoragePackets.BuildStorageData(1,
            new List<ItemEntity> { SampleItem() });

        Assert.Equal((short)0, BitConverter.ToInt16(body, 0));   // +4 — toujours 0
        Assert.Equal((short)1, BitConverter.ToInt16(body, 2));   // +6 — index coffre
        Assert.Equal((ushort)1, BitConverter.ToUInt16(body, 4));  // +0x1c — count
        Assert.Equal((short)0, BitConverter.ToInt16(body, body.Length - 2)); // trailing +4
    }

    [Fact]
    public void StorageItem_record_is_fixed_layout_with_maker_and_fields()
    {
        var body = FamilyStoragePackets.BuildPutItem(1, SampleItem());
        // [s16 index][s16 loc][u16 tid][s16 dur][s16 maxdur][u16 len][maker]
        Assert.Equal((short)1, BitConverter.ToInt16(body, 0));         // index
        Assert.Equal((short)3, BitConverter.ToInt16(body, 2));         // loc = 3
        Assert.Equal((ushort)994, BitConverter.ToUInt16(body, 4));      // tid
        Assert.Equal((short)75, BitConverter.ToInt16(body, 6));        // dur
        Assert.Equal((short)75, BitConverter.ToInt16(body, 8));        // maxdur
        var makerLen = BitConverter.ToUInt16(body, 10);
        Assert.Equal((ushort)4, makerLen);
        Assert.Equal("acme", System.Text.Encoding.ASCII.GetString(body, 12, 4));
        // flags octet après maker : identify bit3 set → 0x08
        Assert.Equal(0x08, body[12 + 4]);
        var off = 12 + 4 + 1;
        for (var i = 0; i < 6; i++)                            // embedded = -1
            Assert.Equal((short)-1, BitConverter.ToInt16(body, off + i * 2));
        off += 12;
        Assert.Equal((short)0, BitConverter.ToInt16(body, off));      // emb_amount
        Assert.Equal(1_700_000_000u, BitConverter.ToUInt32(body, off + 2)); // create_time
    }

    [Fact]
    public void Bound_item_sets_bind_flag_bit6()
    {
        var item = SampleItem();
        item.IsBound = true;
        var body = FamilyStoragePackets.BuildPutItem(0, item);
        // flags octet = after index/loc (4) + tid (2) + dur/maxdur (4) + u16 len (2) + "acme" (4) = 16
        Assert.Equal(0x48, body[16]);
    }

    [Fact]
    public void StorageLog_pairs_type_and_text()
    {
        var body = FamilyStoragePackets.BuildStorageLog(
            new (short, string)[] { (0, "Insert") });
        Assert.Equal((ushort)1, BitConverter.ToUInt16(body, 0));        // count
        Assert.Equal((short)0, BitConverter.ToInt16(body, 2));         // log_type 0
        var len = BitConverter.ToUInt16(body, 4);
        Assert.Equal((ushort)6, len);
        Assert.Equal("Insert", System.Text.Encoding.ASCII.GetString(body, 6, 6));
    }
}
