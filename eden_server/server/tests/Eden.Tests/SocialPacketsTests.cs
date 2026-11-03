using Eden.Core.Game.Social;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régression byte-level des codecs sociaux — formats natifs
/// (désasm : CNC_CW_ClientUpdateFriendMember::Serialize world
/// @0x80cc120, chunk 10 dans mission QueryFriendData ;
/// CNC_CZ_ClientSearchInfo::Serialize zone @0x8134fc0, header s16=0).
/// </summary>
public class SocialPacketsTests
{
    [Fact]
    public void FriendMember_EmptyList_EmitsSingleZeroCountPacket()
    {
        var packets = SocialPackets
            .BuildFriendMemberPackets(1, []).ToList();
        Assert.Single(packets);
        // [u16 worldId=1][s16 count=0] — jamais vide, le client doit
        // recevoir la réponse même sans amis.
        Assert.Equal(new byte[] { 1, 0, 0, 0 }, packets[0]);
    }

    [Fact]
    public void FriendMember_ChunksAtTen()
    {
        var friends = Enumerable.Range(1, 12)
            .Select(i => new SocialPackets.FriendEntry(
                i, $"P{i}", 3, 60, 100, 1, 2, 5, "Guild",
                1, 1, 0, 1))
            .ToList();
        var packets = SocialPackets
            .BuildFriendMemberPackets(1, friends).ToList();
        Assert.Equal(2, packets.Count);
        // count de chaque chunk : s16 LE à l'offset 2.
        Assert.Equal(10, BitConverter.ToInt16(packets[0], 2));
        Assert.Equal(2, BitConverter.ToInt16(packets[1], 2));
        Assert.Equal(1, BitConverter.ToInt16(packets[0], 0));
    }

    [Fact]
    public void FriendMember_RecordLayoutMatchesNative()
    {
        var body = SocialPackets.BuildFriendMemberPackets(1,
            [new SocialPackets.FriendEntry(
                CharId: 42, Name: "Ab", FriendType: 3, Level: 55,
                NodeId: 12, RaceId: 2, ClassId: 4, ClassLv: 9,
                FamilyName: "G", Online: 1, ServerId: 1, State: 0,
                IsPublic: 1)])
            .Single();
        // [u16 1][s16 1] puis le record.
        Assert.Equal(1, BitConverter.ToInt16(body, 0));
        Assert.Equal(1, BitConverter.ToInt16(body, 2));
        Assert.Equal(42, BitConverter.ToInt32(body, 4));
        // name : u16 len + "Ab".
        Assert.Equal(2, BitConverter.ToInt16(body, 8));
        Assert.Equal((byte)'A', body[10]);
        Assert.Equal((byte)'b', body[11]);
        // 6 s16 : type=3, level=55, node=12, race=2, class=4, classLv=9.
        var o = 12;
        Assert.Equal(3, BitConverter.ToInt16(body, o));
        Assert.Equal(55, BitConverter.ToInt16(body, o + 2));
        Assert.Equal(12, BitConverter.ToInt16(body, o + 4));
        Assert.Equal(2, BitConverter.ToInt16(body, o + 6));
        Assert.Equal(4, BitConverter.ToInt16(body, o + 8));
        Assert.Equal(9, BitConverter.ToInt16(body, o + 10));
        // family str : u16 len + "G" puis online u8, serverId u16,
        // state u8, public u8 — les 13 champs du record natif.
        var f = o + 12;
        Assert.Equal(1, BitConverter.ToInt16(body, f));
        Assert.Equal((byte)'G', body[f + 2]);
        Assert.Equal(1, body[f + 3]);
        Assert.Equal(1, BitConverter.ToInt16(body, f + 4));
        Assert.Equal(0, body[f + 6]);
        Assert.Equal(1, body[f + 7]);
        Assert.Equal(f + 8, body.Length);
    }

    [Fact]
    public void SearchInfo_HeaderZeroThenRecords()
    {
        var body = SocialPackets.BuildSearchInfo(
            [new SocialPackets.SearchEntry(
                CharId: 7, Name: "Zed", Level: 40, ClassId: 3,
                ClassLv: 2, NodeId: 77, IsPublic: 1)]);
        Assert.Equal(0, BitConverter.ToInt16(body, 0));  // header natif = 0
        Assert.Equal(1, BitConverter.ToInt16(body, 2));  // count
        Assert.Equal(7, BitConverter.ToInt32(body, 4));
        Assert.Equal(3, BitConverter.ToInt16(body, 8));
        Assert.Equal((byte)'Z', body[10]);
        // level u16/s16, class u16, classLv s16, node s16, is_public u8.
        var o = 10 + 3;
        Assert.Equal(40, BitConverter.ToInt16(body, o));
        Assert.Equal(3, BitConverter.ToInt16(body, o + 2));
        Assert.Equal(2, BitConverter.ToInt16(body, o + 4));
        Assert.Equal(77, BitConverter.ToInt16(body, o + 6));
        Assert.Equal(1, body[o + 8]);
        Assert.Equal(o + 9, body.Length);
    }
}
