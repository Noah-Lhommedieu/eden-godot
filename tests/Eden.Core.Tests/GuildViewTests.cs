using EdenClient.Protocol;
using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class GuildViewTests
{
    private static GuildMember Mb(int id, string n, byte on = 1) =>
        new() { CharId = id, Name = n, Online = on, Level = 10 };

    [Fact]
    public void RankLabel_Rules()
    {
        var titles = new[] { "Membre", "Chef", "Cadre A", "", "", "", "", "", "" };
        Assert.Equal("Chef", GuildView.RankLabel(Mb(1, "a"), 9, 1, -1, titles));
        Assert.Equal("Cadre A", GuildView.RankLabel(Mb(9, "s"), 9, 1, 0, titles));
        Assert.Equal("Rank 3", GuildView.RankLabel(Mb(9, "s"), 9, 1, 3, titles));
        Assert.Equal("Membre", GuildView.RankLabel(Mb(5, "m"), 9, 1, 0, titles));
        Assert.Equal("Leader", GuildView.RankLabel(Mb(1, "a"), 9, 1, -1, new string[0]));
    }

    [Fact]
    public void Sorted_OnlineLeaderName_Filter()
    {
        var ms = new[] { Mb(3, "Zed"), Mb(2, "Amy", 0), Mb(1, "Bob"), Mb(4, "Cat") };
        var all = GuildView.Sorted(ms, 4, false).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "Cat", "Bob", "Zed", "Amy" }, all);
        var on = GuildView.Sorted(ms, 4, true).Select(m => m.Name).ToArray();
        Assert.Equal(new[] { "Cat", "Bob", "Zed" }, on);
    }

    [Fact]
    public void Lines()
    {
        var m = Mb(1, "Bob");
        m.Contribution = 5;
        Assert.Equal("● Bob — Lv10 Mage — Chef (+5)", GuildView.MemberLine(m, "Mage", "Chef"));
        Assert.Contains("«hello»", GuildView.MemberTip(
            new GuildMember { Name = "x", SelfComment = "hello" }, "C"));
        Assert.Equal("[01-02] a b (t1 #7)",
            GuildView.LogLine(new GuildLogEntry(1, 7, 86400 + 3600, 0, "a", "b")));
        Assert.Equal("[??] t — w", GuildView.BoardLine(0, "t", "w"));
        Assert.Equal("<G> Lv3 — 12 members — leader L",
            GuildView.RecruitLine(new GuildRecruitEntry(1, "G", "L", 12, 3, 1)));
    }

    [Fact]
    public void Inputs()
    {
        Assert.Null(GuildView.ValidName("  "));
        Assert.Equal("Ab", GuildView.ValidName(" Ab "));
        Assert.Equal(50, GuildView.ParseDonation("50"));
        Assert.Null(GuildView.ParseDonation("0"));
        Assert.Null(GuildView.ParseDonation("x"));
        Assert.Equal((short)12, GuildView.ParseEmblem("12"));
        Assert.Null(GuildView.ParseEmblem("99999"));
        Assert.Equal("—", GuildView.Created(0));
    }

    [Fact]
    public void FamilyOp_Wire()
    {
        var q = WorldFamilyOp.Simple(FamilyOps.Quit);
        Assert.Equal(9, q.Opcode);
        Assert.Equal(FamilyOps.Quit, q.Op);
        var k = WorldFamilyOp.Of(FamilyOps.Kick, 77);
        Assert.Equal(77, k.A);
    }
}
