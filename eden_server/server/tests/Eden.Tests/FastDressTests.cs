using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du sous-système de tenues rapides — table native
/// <c>GameData::FastDressEquipLoc</c> (.rodata zone @0x087c36e0),
/// groupes d'armure de <c>CheckFastDressUseItem</c> @0x080ff560,
/// et layouts wire 593/594
/// (<c>CNC_CZ_ClientEnterNodeFastDress::Serialize</c> @0x08232e70,
/// <c>CNC_CZ_ClientUpdateFastDressData::Serialize</c> @0x08232e90).
/// </summary>
public class FastDressTests
{
    // ------------------------- table native -------------------------

    [Fact]
    public void EquipLocs_NativeOrder24()
    {
        Assert.Equal(24, FastDressSlots.EquipLocs.Length);
        // Table extraite de .rodata — ordre = index de bit du mask.
        Assert.Equal(
            new ushort[]
            {
                7, 8, 14, 15, 11, 12, 13, 6, 10,
                601, 602, 603, 605, 604,
                611, 612, 613, 615, 614,
                621, 622, 623, 625, 624,
            },
            FastDressSlots.EquipLocs);
    }

    [Theory]
    [InlineData(3, 2)] [InlineData(6, 2)] [InlineData(9, 2)]
    [InlineData(12, 2)] [InlineData(15, 2)]
    [InlineData(2, 3)] [InlineData(5, 3)] [InlineData(8, 3)]
    [InlineData(11, 3)] [InlineData(14, 3)]
    [InlineData(1, 4)] [InlineData(4, 4)] [InlineData(7, 4)]
    [InlineData(10, 4)] [InlineData(13, 4)]
    [InlineData(17, 0)] [InlineData(28, 0)] [InlineData(39, 0)]
    [InlineData(0, -1)] [InlineData(40, -1)] [InlineData(43, -1)]
    public void GroupOfKind_NativeMasks(int kind, int expected)
        => Assert.Equal(expected, FastDressSlots.GroupOfKind(kind));

    [Theory]
    [InlineData(7, 0, 7)] [InlineData(8, 0, 8)]
    [InlineData(6, 0, 6)] [InlineData(10, 0, 10)]
    [InlineData(15, 0, 15)]
    [InlineData(601, 2, 1)] [InlineData(605, 2, 5)]
    [InlineData(611, 3, 1)] [InlineData(615, 3, 5)]
    [InlineData(621, 4, 1)] [InlineData(625, 4, 5)]
    public void EquipPosOf_KeyToGroupLoc(int key, int group, int loc)
    {
        var (g, l) = FastDressSlots.EquipPosOf(key);
        Assert.Equal(group, g);
        Assert.Equal(loc, l);
    }

    [Fact]
    public void BitIsSet_BitIndexIsEquipLocOrder()
    {
        // bit 0 = clé 7, bit 1 = clé 8, bit 9 = clé 601.
        var mask = (1 << 0) | (1 << 9);
        Assert.True(FastDressSlots.BitIsSet(mask, 7));
        Assert.True(FastDressSlots.BitIsSet(mask, 601));
        Assert.False(FastDressSlots.BitIsSet(mask, 8));
        Assert.False(FastDressSlots.BitIsSet(mask, 602));
        Assert.False(FastDressSlots.BitIsSet(mask, 999));
    }

    // ------------------------- records -------------------------

    [Fact]
    public void StateCreate_ThreePagesTwoMaps()
    {
        var s = FastDressState.Create();
        Assert.Equal(3, s.Saved.Count);
        Assert.Equal(3, s.Edited.Count);
        Assert.All(s.Saved.Values, m => Assert.Empty(m));
    }

    [Fact]
    public void RecordClear_NativeSeedValues()
    {
        var r = new FastDressRecord
        {
            Uid = 42, ItemId = 100, ContainerType = 0,
            FromLoc = 5, FromIndex = 1, Page = 2, TarLoc = 7,
        };
        r.Clear();
        // RemoveFastDressInfo natif : uid/item/con→0, loc/idx→-1,
        // page/clé conservés.
        Assert.Equal(0, r.Uid);
        Assert.Equal(0, r.ItemId);
        Assert.Equal(-1, r.FromLoc);
        Assert.Equal(-1, r.FromIndex);
        Assert.Equal(2, r.Page);
        Assert.Equal(7, r.TarLoc);
    }

    // ------------------------- wire 593/594 -------------------------

    [Fact]
    public void BuildEnterNode_CountThenSixIntsPerRecord()
    {
        var rec = new FastDressRecord
        {
            Uid = 77, ItemId = 31899, ContainerType = 0,
            FromLoc = 4, FromIndex = 2, Page = 1, TarLoc = 7,
        };
        var body = FastDressPackets.BuildEnterNode(new[] { rec });
        // [s16 n=1][item][con][loc][idx][page][tarloc] = 2+24 octets.
        Assert.Equal(26, body.Length);
        Assert.Equal(1, BitConverter.ToInt16(body, 0));
        Assert.Equal(31899, BitConverter.ToInt32(body, 2));
        Assert.Equal(0, BitConverter.ToInt32(body, 6));
        Assert.Equal(4, BitConverter.ToInt32(body, 10));
        Assert.Equal(2, BitConverter.ToInt32(body, 14));
        Assert.Equal(1, BitConverter.ToInt32(body, 18));
        Assert.Equal(7, BitConverter.ToInt32(body, 22));
    }

    [Fact]
    public void BuildUpdate_ModeWithRecords()
    {
        var rec = new FastDressRecord
        {
            ItemId = 5, ContainerType = 1, FromLoc = 7, FromIndex = 0,
            Page = 0, TarLoc = 7,
        };
        var body = FastDressPackets.BuildUpdate(4, 0, new[] { rec });
        // [s16 mode][s16 n][i32 page][6×i32].
        Assert.Equal(2 + 2 + 4 + 24, body.Length);
        Assert.Equal(4, BitConverter.ToInt16(body, 0));
        Assert.Equal(1, BitConverter.ToInt16(body, 2));
        Assert.Equal(0, BitConverter.ToInt32(body, 4));
        Assert.Equal(5, BitConverter.ToInt32(body, 8));
        Assert.Equal(7, BitConverter.ToInt32(body, 28));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    public void BuildUpdate_ModeOnlyModes(short mode)
    {
        var body = FastDressPackets.BuildUpdate(mode, 2,
            new[] { new FastDressRecord() });
        // modes 1/7/8 → [s16 mode] seul (le natif saute le bloc).
        Assert.Equal(2, body.Length);
        Assert.Equal(mode, BitConverter.ToInt16(body, 0));
    }
}
