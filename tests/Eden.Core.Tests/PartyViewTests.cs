using EdenClient.Protocol;
using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class PartyViewTests
{
    [Fact]
    public void Hp_Ratio_Text()
    {
        Assert.Equal(0.5f, PartyView.HpRatio(50, 100));
        Assert.Equal(1f, PartyView.HpRatio(150, 100));
        Assert.Equal(0f, PartyView.HpRatio(10, 0));
        Assert.Equal("50/100", PartyView.HpText(50, 100));
        Assert.Equal("", PartyView.HpText(5, 0));
    }

    [Fact]
    public void Menu_ByRole()
    {
        Assert.Equal(new[] { PartyMenu.InviteTarget, PartyMenu.Leave },
            PartyView.MenuFor(7, 7, 7));
        Assert.Equal(new[] { PartyMenu.Whisper },
            PartyView.MenuFor(7, 9, 5));
        Assert.Equal(new[] { PartyMenu.Whisper, PartyMenu.Promote, PartyMenu.Kick },
            PartyView.MenuFor(7, 9, 7));
        // soi inconnu (0) : jamais chef
        Assert.Equal(new[] { PartyMenu.Whisper }, PartyView.MenuFor(0, 9, 0));
    }

    [Fact]
    public void Order_And_Tooltip()
    {
        var a = new TeamMember { Slot = 2, CharId = 1, CharName = "A", MapId = 2, Flags = 1 };
        var b = new TeamMember { Slot = 1, CharId = 2, CharName = "B", GuildName = "G", MapId = 3 };
        var o = PartyView.Ordered(new[] { a, b });
        Assert.Equal("B", o[0].CharName);
        Assert.Equal("B  <G>  map S003", PartyView.Tooltip(b));
        Assert.True(PartyView.IsLeader(a));
        Assert.False(PartyView.IsLeader(b));
    }

    [Fact]
    public void PartyState_Roster_FromWire()
    {
        PartyState.Clear();
        var m = new ZoneTeamAllMembersInfo();
        m.Members.Add(new TeamMember { Slot = 0, CharId = 5, CharName = "Me", Hp = 80, Flags = 1 });
        PartyState.ApplyAll(m);
        Assert.True(PartyState.InTeam);
        Assert.Equal(5u, PartyState.LeaderId);
        PartyState.ApplyLeaved();
        Assert.False(PartyState.InTeam);
    }
}
