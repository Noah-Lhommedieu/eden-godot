using Xunit;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Tests;

/// <summary>
/// Round-trip des classes CNC patchées d'après les boucles natives
/// (patch_cnc_loops.py — layouts vérifiés par désassemblage).
/// </summary>
public class LoopMessageTests
{
    private static T RoundTrip<T>(T msg) where T : NetCommand, new()
    {
        var out_ = new OutBinStream();
        msg.Serialize(out_);
        var back = new T();
        back.Deserialize(new InBinStream(out_.ToArray()));
        return back;
    }

    [Fact]
    public void MailList_ShouldRoundTripEntries()
    {
        var msg = new CNC_CZ_ClientMailList();
        msg.Entries.Add(new CNC_CZ_ClientMailList.Entry
        {
            MailId = 42, SenderName = "Admin", SendTime = 1700000000,
            Subject = "Objet", Content = "Corps", AttachGold = 500,
            AttachItemId = 1001, AttachQty = 3,
        });
        var back = RoundTrip(msg);
        Assert.Single(back.Entries);
        var e = back.Entries[0];
        Assert.Equal(42, e.MailId);
        Assert.Equal("Admin", e.SenderName);
        Assert.Equal("Objet", e.Subject);
        Assert.Equal(500u, e.AttachGold);
        Assert.Equal(1001, e.AttachItemId);
        Assert.Equal(3, e.AttachQty);
    }

    [Fact]
    public void MailList_WireFormat_StartsWithU16Count()
    {
        var msg = new CNC_CZ_ClientMailList();
        msg.Entries.Add(new CNC_CZ_ClientMailList.Entry());
        msg.Entries.Add(new CNC_CZ_ClientMailList.Entry());
        var out_ = new OutBinStream();
        msg.Serialize(out_);
        var bytes = out_.ToArray();
        // u16 count en tête (layout natif 0x8204620)
        Assert.Equal(2, bytes[0] | (bytes[1] << 8));
    }

    [Fact]
    public void PlayerClassList_ShouldRoundTripSCharacterClass()
    {
        var msg = new CNC_CZ_ClientPlayerClassList
        {
            HeaderFlag1 = 1, HeaderFlag2 = 0,
        };
        msg.Entries.Add(new CNC_CZ_ClientPlayerClassList.Entry
        {
            NodeFlag = 1, ClassId = 5, ClassLevel = 60,
            ClassExp = 123456, Flags = 1, U1 = 42,
        });
        var back = RoundTrip(msg);
        Assert.Equal(1, back.HeaderFlag1);
        Assert.Single(back.Entries);
        var e = back.Entries[0];
        Assert.Equal(5, e.ClassId);
        Assert.Equal(60, e.ClassLevel);
        Assert.Equal(123456, e.ClassExp);
        Assert.Equal(42, e.U1);
    }

    [Fact]
    public void TeamAllMembersInfo_ShouldRoundTripWithSuffix()
    {
        var msg = new CNC_CZ_ClientTeamAllMembersInfo { TeamMode = 2 };
        msg.Entries.Add(new CNC_CZ_ClientTeamAllMembersInfo.Entry
        {
            Slot = 0, Flags = 1, CharId = 777, CharName = "Tank",
            Level = 80, GuildName = "LesGens",
        });
        var back = RoundTrip(msg);
        Assert.Equal(2, back.TeamMode);
        Assert.Single(back.Entries);
        Assert.Equal("Tank", back.Entries[0].CharName);
        Assert.Equal(777u, back.Entries[0].CharId);
        Assert.Equal("LesGens", back.Entries[0].GuildName);
    }

    [Fact]
    public void SearchTeamInfo_ShouldRoundTripNestedMembers()
    {
        var msg = new CNC_CZ_ClientSearchTeamInfo { Value = 9 };
        var team = new CNC_CZ_ClientSearchTeamInfo.TeamEntry
        {
            Comment1 = "LFG donjon", Comment2 = "lvl 60+",
            Flags = 3,
        };
        team.Members.Add(new CNC_CZ_ClientSearchTeamInfo.MemberEntry
        {
            CharId = 55, CharName = "Healer",
            Level = 61, ClassId = 7,
        });
        msg.Teams.Add(team);

        var back = RoundTrip(msg);
        Assert.Equal(9, back.Value);
        Assert.Single(back.Teams);
        var t = back.Teams[0];
        Assert.Equal("LFG donjon", t.Comment1);
        Assert.Single(t.Members);
        Assert.Equal("Healer", t.Members[0].CharName);
        Assert.Equal(61, t.Members[0].Level);
    }

    [Fact]
    public void AuctionSellList_ShouldRoundTripEntries()
    {
        var msg = new CNC_CZ_ClientAuctionSellList();
        msg.Entries.Add(new CNC_CZ_ClientAuctionSellList.Entry
        {
            AuctionId = 11, ItemId = 2002, Price = 9999,
            SellerId = 4242, ExpireTime = 1700000000,
        });
        var back = RoundTrip(msg);
        Assert.Single(back.Entries);
        Assert.Equal(11, back.Entries[0].AuctionId);
        Assert.Equal(9999, back.Entries[0].Price);
        Assert.Equal(4242u, back.Entries[0].SellerId);
    }

    [Fact]
    public void EnterNodeInventory_ShouldRoundTrip()
    {
        var msg = new CNC_CZ_ClientEnterNodeInventory { BagType = 1 };
        msg.Entries.Add(new CNC_CZ_ClientEnterNodeInventory.Entry
        {
            SlotValue = 5678,
        });
        var back = RoundTrip(msg);
        Assert.Equal(1, back.BagType);
        Assert.Single(back.Entries);
        Assert.Equal(5678, back.Entries[0].SlotValue);
    }

    [Fact]
    public void EmptyLists_ShouldSerializeCountZero()
    {
        var msg = new CNC_CZ_ClientMailList();
        var out_ = new OutBinStream();
        msg.Serialize(out_);
        // Format natif complet : u16 count + s8 TrailingFlag + u16
        // count TrailingShorts — le client lit ces champs même quand la
        // liste est vide (over-read -> crash si absents, constaté live).
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, out_.ToArray());
    }
}
