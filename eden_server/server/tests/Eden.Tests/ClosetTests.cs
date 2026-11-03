using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du codec garde-robe — 488
/// <c>CNC_CZ_ClientPlayerClosets</c> : <c>[u8 type][u16 n]
/// [n×{u8 class][s16 count][count×(3×u64)}]</c>
/// (<c>CNC_CZ_ClientPlayerClosets::Serialize</c> +
/// <c>SCharacterCloset::Serialize</c> natifs).
/// </summary>
public class ClosetTests
{
    [Fact]
    public void Build_EmptyMap_ThreeBytes()
    {
        var body = ClosetPackets.Build(0, new());
        // [u8 type=0][u16 0] — map vide, aucun bucket émis.
        Assert.Equal(new byte[] { 0, 0, 0 }, body);
    }

    [Fact]
    public void Build_SingleZeroSlot_MatchesCapture()
    {
        // Capture native zsniff : `00 0100 05 01` + 24×00 (30B) —
        // type 0, 1 entrée classe 5 avec un slot zéro.
        var map = new SortedDictionary<byte,
            List<(ulong Helmet, ulong Clothes, ulong Cloak)>>
        { [5] = [(0UL, 0UL, 0UL)] };
        var body = ClosetPackets.Build(0, map);
        var expected = new byte[30];
        expected[0] = 0;              // u8 type
        expected[1] = 1; expected[2] = 0;   // u16 n = 1
        expected[3] = 5;              // u8 classe
        expected[4] = 1; expected[5] = 0;   // s16 count = 1
        // 24 octets de couleurs à zéro déjà initialisés.
        Assert.Equal(expected, body);
    }

    [Fact]
    public void WriteU64_LittleEndian_Split()
    {
        var map = new SortedDictionary<byte,
            List<(ulong Helmet, ulong Clothes, ulong Cloak)>>
        { [1] = [(0x1122334455667788UL, 0xAABBCCDDEEFF0011UL,
                  0xDEADBEEFUL)] };
        var body = ClosetPackets.Build(0, map);
        // En-tête 6B puis 3 u64 little-endian.
        Assert.Equal(6 + 24, body.Length);
        Assert.Equal(new byte[]
            { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 },
            body[6..14]);
        Assert.Equal(new byte[]
            { 0x11, 0x00, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA },
            body[14..22]);
        Assert.Equal(new byte[]
            { 0xEF, 0xBE, 0xAD, 0xDE, 0, 0, 0, 0 }, body[22..30]);
    }

    [Fact]
    public void Build_MultiClass_OrderedByClass()
    {
        var map = new SortedDictionary<byte,
            List<(ulong Helmet, ulong Clothes, ulong Cloak)>>
        {
            [3] = [(1UL, 2UL, 3UL), (4UL, 5UL, 6UL)],
            [1] = [(7UL, 8UL, 9UL)],
        };
        var body = ClosetPackets.Build(0, map);
        // [u8 0][u16 2] puis classe 1 (1 slot) puis classe 3 (2 slots).
        Assert.Equal(3 + 3 + 24 + 3 + 48, body.Length);
        Assert.Equal(2, BitConverter.ToUInt16(body, 1));
        Assert.Equal(1, body[3]);
        Assert.Equal(1, BitConverter.ToInt16(body, 4));
        Assert.Equal(3, body[3 + 3 + 24]);
        Assert.Equal(2, BitConverter.ToInt16(body, 3 + 3 + 24 + 1));
    }
}
