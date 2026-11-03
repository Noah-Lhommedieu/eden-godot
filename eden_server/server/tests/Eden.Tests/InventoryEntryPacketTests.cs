using Eden.Core.Database.Repositories;
using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;

namespace Eden.Tests;

/// <summary>Contrôles de régression du NCID 312, notamment les bitmaps
/// et les items répartis sur plusieurs sacs ouverts.</summary>
public sealed class InventoryEntryPacketTests
{
    /// <summary>Champs décodés d'un record item 312 dans les tests.</summary>
    private sealed record ParsedItem(ushort Id, short Durability,
        short MaxDurability, byte Flags, uint Mask, short[] EmbeddedIds,
        short? EmbeddedAmount, short? Combo, uint? CreateTime,
        ushort[] ExtraCombos, short? Enchant, long? DueDate);

    /// <summary>Chaque conteneur annoncé doit embarquer ses propres
    /// items, avec la capacité de base native de 32 slots.</summary>
    [Fact]
    public void EnterNodeInventory_SerializesItemsFromEveryOpenContainer()
    {
        var items = new[]
        {
            new ItemEntity
            {
                BagId = 0, Slot = 0, ItemId = 49004,
                Quantity = 1, Durability = 1, MaxDurability = 1,
            },
            new ItemEntity
            {
                BagId = 1, Slot = 2, ItemId = 33281,
                Quantity = 1, Durability = 1, MaxDurability = 1,
            },
            new ItemEntity
            {
                BagId = 1, Slot = -1, ItemId = 33333,
                Quantity = 1, Durability = 1, MaxDurability = 1,
            },
        };
        var body = NativeItemCodec.BuildEnterNodeInventory(items,
            new Dictionary<int, int> { [1] = 30 },
            Array.Empty<EquippedBag>());

        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        Assert.Equal(2, reader.ReadByte());

        var idsByBag = new List<int>[2];
        var capacities = new ushort[2];
        for (var bag = 0; bag < idsByBag.Length; bag++)
        {
            idsByBag[bag] = new List<int>();
            var capacity = reader.ReadUInt16();
            capacities[bag] = capacity;
            _ = reader.ReadByte(); // Native container flag.
            var bitmapLength = reader.ReadInt16();
            var bitmap = reader.ReadBytes(bitmapLength);
            for (var slot = 0; slot < capacity; slot++)
            {
                if ((bitmap[slot / 8] & (0x80 >> (slot & 7))) == 0)
                    continue;

                idsByBag[bag].Add(reader.ReadUInt16());
                reader.BaseStream.Position += 44; // Remaining item record.
            }
        }

        Assert.Equal(new ushort[] { 32, 30 }, capacities);
        Assert.Equal(new[] { 49004 }, idsByBag[0]);
        Assert.Equal(new[] { 33281 }, idsByBag[1]);
    }

    /// <summary>Conditional fields must stay aligned when real item rows
    /// contain create-time, sockets, combo, strengthen and expiry data.</summary>
    [Fact]
    public void EnterNodeInventory_ParsesFullMetadataRecordsWithoutLosingItems()
    {
        var first = new ItemEntity
        {
            BagId = 0, Slot = 3, ItemId = 34539,
            Durability = 3, MaxDurability = 4, IsBound = true,
            Identify = 0, EmbeddedAmount = 2, EmbeddedId1 = 12001,
            Combo = 45, EnchantLevel = 5, CreateTime = 123456,
            DueDate = 654321,
        };
        first.ExtraCombos[0] = 78;
        var second = new ItemEntity
        {
            BagId = 1, Slot = 29, ItemId = 33281,
            Durability = 1, MaxDurability = 1, Identify = 1,
            CreateTime = 123457,
        };
        var body = NativeItemCodec.BuildEnterNodeInventory(
            new[] { first, second },
            new Dictionary<int, int> { [0] = 32, [1] = 30 },
            Array.Empty<EquippedBag>());

        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        Assert.Equal(2, reader.ReadByte());
        var bag0 = ReadBag(reader, 32);
        var bag1 = ReadBag(reader, 30);
        var parsedFirst = Assert.Single(bag0.Items);
        Assert.Equal((ushort)34539, parsedFirst.Id);
        Assert.Equal((short)3, parsedFirst.Durability);
        Assert.Equal((short)4, parsedFirst.MaxDurability);
        // Flags = bind bit 0x10 (clone+0x24 natif — désasm
        // CPlayerFactory::BindItem / FastDressDoItemBind) | bit 0x02.
        Assert.Equal((byte)0x12, parsedFirst.Flags);
        Assert.Equal(0x2805c1u, parsedFirst.Mask);
        Assert.Equal(new short[] { 12001 }, parsedFirst.EmbeddedIds);
        Assert.Equal((short?)2, parsedFirst.EmbeddedAmount);
        Assert.Equal((short?)45, parsedFirst.Combo);
        Assert.Equal((uint?)123456u, parsedFirst.CreateTime);
        Assert.Equal(new ushort[] { 78 }, parsedFirst.ExtraCombos);
        Assert.Equal((short?)5, parsedFirst.Enchant);
        Assert.Equal((long?)654321L, parsedFirst.DueDate);
        var parsedSecond = Assert.Single(bag1.Items);
        Assert.Equal((ushort)33281, parsedSecond.Id);
        Assert.Equal((uint?)123457u, parsedSecond.CreateTime);
        Assert.Equal(2, reader.ReadByte());
        for (var holder = 0; holder < 2; holder++)
        {
            _ = reader.ReadByte(); // u8 holder index.
            Assert.Equal(5, reader.ReadUInt16());
            for (var slot = 0; slot < 5; slot++)
                Assert.Equal(0, reader.ReadUInt16());
        }
        Assert.Equal(stream.Length, stream.Position);
    }

    /// <summary>Reads one 312 bag and checks every set bitmap bit's full
    /// conditional record before returning its capacity.</summary>
    private static (ushort Capacity, List<ParsedItem> Items)
        ReadBag(BinaryReader reader, ushort expectedCapacity)
    {
        var capacity = reader.ReadUInt16();      // u16 slot capacity.
        Assert.Equal(expectedCapacity, capacity);
        _ = reader.ReadByte();                   // u8 container flags.
        var bitmapLength = reader.ReadInt16();   // s16 bitmap byte count.
        var bitmap = reader.ReadBytes(bitmapLength);
        var items = new List<ParsedItem>();
        for (var slot = 0; slot < capacity; slot++)
        {
            if ((bitmap[slot / 8] & (0x80 >> (slot & 7))) == 0) continue;
            var id = reader.ReadUInt16();        // u16 item template id.
            var durability = reader.ReadInt16(); // s16 current durability.
            var maxDurability = reader.ReadInt16();// s16 maximum durability.
            var makerLength = reader.ReadUInt16();// u16 maker byte length.
            _ = reader.ReadBytes(makerLength);   // maker bytes.
            var flags = reader.ReadByte();        // u8 clone flags.
            var mask = (uint)(reader.ReadByte() |
                reader.ReadByte() << 8 |
                reader.ReadByte() << 16);        // u24 optional-field mask.
            var embeddedIds = new List<short>();
            for (var bit = 0; bit < 6; bit++)
                if ((mask & (1u << bit)) != 0)
                    embeddedIds.Add(reader.ReadInt16()); // s16 gem id.
            short? embeddedAmount = (mask & 0x40) != 0
                ? reader.ReadInt16() : null;       // s16 open socket count.
            short? combo = (mask & 0x80) != 0
                ? reader.ReadInt16() : null;       // s16 item combo id.
            uint? createTime = (mask & 0x100) != 0
                ? reader.ReadUInt32() : null;      // u32 creation timestamp.
            if ((mask & 0x200) != 0)
                _ = reader.ReadUInt16();          // u16 optional clone value.
            var extraCombos = new List<ushort>();
            for (var bit = 10; bit <= 18; bit++)
                if ((mask & (1u << bit)) != 0)
                    extraCombos.Add(reader.ReadUInt16()); // u16 extra combo.
            short? enchant = (mask & 0x80000) != 0
                ? reader.ReadInt16() : null;      // s16 strengthen level.
            if ((mask & 0x100000) != 0)
                _ = reader.ReadInt16();           // s16 optional clone value.
            long? dueDate = (mask & 0x200000) != 0
                ? reader.ReadInt64() : null;       // i64 expiry timestamp.
            Assert.Equal(34, reader.ReadBytes(34).Length); // fixed tail.
            items.Add(new ParsedItem(id, durability, maxDurability, flags,
                mask, embeddedIds.ToArray(), embeddedAmount, combo,
                createTime, extraCombos.ToArray(), enchant, dueDate));
        }
        return (capacity, items);
    }
}
