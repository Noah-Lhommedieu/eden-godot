using Eden.Core.Game.Services;
using Eden.Core.Game.Social;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régression byte-level du codec classements — formats natifs :
/// `CNC_ZMis_ZoneServerRankInfo::Serialize` mission @0x080b78b0 (763),
/// `CNE_ZMis_ZoneServerRankInfo::Deserialize` zone @0x08331330,
/// `CNC_CZ_ClientRankInfo::Serialize` @0x080b9b40 (521).
/// </summary>
public class RankPacketsTests
{
    /// <summary>Deux rangés avec tous les champs remplis.</summary>
    private static List<RankPackets.RankEntry> TwoRows() =>
    [
        new() { Name = "Alpha",
            F = { [0] = 11, [1] = 60, [2] = 900, [8] = 7 },
            Str2 = "Guild", StrList = { "m1", "m2" } },
        new() { Name = "Beta",
            F = { [0] = 22, [1] = 55, [2] = 800, [8] = 3 },
            Str2 = "", StrList = { } },
    ];

    [Fact]
    public void Build763_LayoutMatchesNative()
    {
        var b = RankPackets.Build763(1, 0x1234, TwoRows());
        var p = 0;
        // vec<str> names : [u16=2]["Alpha"]["Beta"]
        Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
        var nl = BitConverter.ToUInt16(b, p); p += 2;
        Assert.Equal("Alpha",
            Eden.Core.Data.EdenEncoding.Big5.GetString(b, p, nl));
        p += nl;
        nl = BitConverter.ToUInt16(b, p); p += 2;
        Assert.Equal("Beta",
            Eden.Core.Data.EdenEncoding.Big5.GetString(b, p, nl));
        p += nl;
        // 9 vec<i32> — col0 = {11,22}, col8 = {7,3}
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
            var expect = i switch
            { 0 => new[] { 11, 22 }, 1 => new[] { 60, 55 },
              2 => new[] { 900, 800 }, 8 => new[] { 7, 3 },
              _ => new[] { 0, 0 } };
            foreach (var v in expect)
            { Assert.Equal(v, BitConverter.ToInt32(b, p)); p += 4; }
        }
        // [s16 type=1][s16 ver=0x1234]
        Assert.Equal(1, BitConverter.ToInt16(b, p)); p += 2;
        Assert.Equal(0x1234, BitConverter.ToInt16(b, p)); p += 2;
        // vec<str> str2 : {"Guild",""}
        Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
        var s2 = BitConverter.ToUInt16(b, p); p += 2;
        Assert.Equal("Guild",
            Eden.Core.Data.EdenEncoding.Big5.GetString(b, p, s2));
        p += s2;
        Assert.Equal(0, BitConverter.ToUInt16(b, p)); p += 2;
        // vec<vec<str>> : [{"m1","m2"},{}]
        Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
        Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
        var m = BitConverter.ToUInt16(b, p); p += 2;
        Assert.Equal("m1",
            Eden.Core.Data.EdenEncoding.Big5.GetString(b, p, m));
        p += m;
        m = BitConverter.ToUInt16(b, p); p += 2;
        Assert.Equal("m2",
            Eden.Core.Data.EdenEncoding.Big5.GetString(b, p, m));
        p += m;
        Assert.Equal(0, BitConverter.ToUInt16(b, p)); p += 2;
        Assert.Equal(b.Length, p);
    }

    [Fact]
    public void Parse763_RoundTrips()
    {
        var rows = TwoRows();
        var set = RankPackets.Parse763(
            RankPackets.Build763(200, 42, rows), out var type);
        Assert.NotNull(set);
        Assert.Equal(200, type);
        Assert.Equal(42, set.Version);
        Assert.Equal(new[] { "Alpha", "Beta" }, set.Names);
        Assert.Equal(new[] { 11, 22 }, set.Cols[0]);
        Assert.Equal(new[] { 900, 800 }, set.Cols[2]);
        Assert.Equal(new[] { 7, 3 }, set.Cols[8]);
        Assert.Equal(new[] { "Guild", "" }, set.Str2s);
        Assert.Equal(new[] { "m1", "m2" }, set.StrLists[0]);
        Assert.Empty(set.StrLists[1]);
    }

    [Fact]
    public void Parse763_Truncated_ReturnsNull()
    {
        var b = RankPackets.Build763(1, 0, TwoRows());
        Assert.Null(RankPackets.Parse763(
            b[..^3], out _));
        Assert.Null(RankPackets.Parse763(
            Array.Empty<byte>(), out _));
    }

    [Fact]
    public void Build521_EmptySet_AllZeroCounts()
    {
        var b = RankPackets.Build521(null);
        Assert.Equal(18, b.Length); // 9 compteurs u16 = 0
        Assert.All(b, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Build521_MapsCols048AndDropsOthers()
    {
        var set = RankPackets.Parse763(
            RankPackets.Build763(1, 0, TwoRows()), out _)!;
        var b = RankPackets.Build521(set);
        var p = 0;
        Assert.Equal(2, BitConverter.ToUInt16(b, p)); p += 2;
        p += BitConverter.ToUInt16(b, p) + 2;              // "Alpha"
        p += BitConverter.ToUInt16(b, p) + 2;              // "Beta"
        // 8 vec<i32> : cols 0,1,2,3 remplies ; 4-6 vides ; col8 fin
        var expected = new int[][]
        {
            new[] { 11, 22 }, new[] { 60, 55 },
            new[] { 900, 800 }, new[] { 0, 0 },
            Array.Empty<int>(), Array.Empty<int>(),
            Array.Empty<int>(), new[] { 7, 3 },
        };
        foreach (var col in expected)
        {
            Assert.Equal(col.Length, BitConverter.ToUInt16(b, p));
            p += 2;
            foreach (var v in col)
            { Assert.Equal(v, BitConverter.ToInt32(b, p)); p += 4; }
        }
        Assert.Equal(b.Length, p);
    }
}
