using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class GuildServiceTests
{
    [Fact]
    public async Task CreateGuild_AndManageMembers()
    {
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(1, "TestGuild", 50);
        Assert.Equal(1, g.LeaderCharacterId);
        Assert.Equal(1, g.Level);
        Assert.True(await svc.AddMemberAsync(g.Id, 2, "P2"));
        Assert.True(svc.SetRank(1, 2, GuildService.GuildRank.Officer));
        Assert.False(svc.SetRank(2, 1, GuildService.GuildRank.Member)); // pas leader
    }

    [Fact]
    public async Task Guild_LevelUpAndTaxes()
    {
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(1, "G", 50);
        await svc.AddMemberAsync(g.Id, 2, "P2");
        svc.AddExperience(g.Id, 25000);
        Assert.True(g.Level >= 2);
        var tax = svc.LevyTax(g.Id, 10, 1000);
        Assert.Equal(100, tax);
        Assert.Equal(100, g.Treasury);
        var share = svc.DistributeTreasury(g.Id, 1);
        Assert.Equal(50, share); // 100 / 2 membres
    }

    [Fact]
    public async Task Guild_LeaderQuit_Transfers()
    {
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(1, "G", 50);
        await svc.AddMemberAsync(g.Id, 2, "P2");
        await svc.LeaveGuildAsync(1);
        Assert.Equal(2, g.LeaderCharacterId);
    }
}

public class MailServiceTests
{
    [Fact]
    public void SendMail_WithAttachments()
    {
        var svc = new MailService();
        var m = svc.Send(1, "Sender", 2, "Hello", "body", 1000, new[] { 33747, 44025 });
        Assert.Equal(2, m.AttachedItemIds.Count);
        var inbox = svc.GetInbox(2);
        Assert.Single(inbox);
        var claim = svc.ClaimAttachments(2, m.Id);
        Assert.NotNull(claim);
        Assert.Equal(1000, claim!.Value.Gold);
        Assert.Null(svc.ClaimAttachments(2, m.Id)); // deja reclame
    }

    [Fact]
    public void DeleteMail_ReturnsAttachmentsToSender()
    {
        var svc = new MailService();
        var m = svc.Send(1, "S", 2, "gift", "b", 500, new[] { 123 });
        Assert.True(svc.Delete(2, m.Id));
        // l'expediteur recoit un mail "Returned"
        var senderInbox = svc.GetInbox(1);
        Assert.Single(senderInbox);
        Assert.Equal(500, senderInbox[0].AttachedGold);
    }

    [Fact]
    public void ExpiredMail_Purged()
    {
        var svc = new MailService();
        var m = svc.Send(1, "S", 2, "old", "b");
        m.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        Assert.Empty(svc.GetInbox(2));
        Assert.Equal(1, svc.PurgeExpired());
    }
}
