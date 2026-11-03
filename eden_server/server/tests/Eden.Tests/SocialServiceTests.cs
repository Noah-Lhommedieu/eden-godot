using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class SocialServiceTests
{
    /// <summary>Modèle natif `friends.friend_type` : 1 = j'ai ajouté,
    /// 2 = invitation pendante (l'autre m'a ajouté), 3 = mutuel,
    /// 0 = blacklist — une ligne par direction (TC_AddFriend).</summary>
    [Fact]
    public async Task Friend_RequestAcceptFlow()
    {
        var svc = new FriendService();
        // A envoie la requête : A→B = 1 (ajouté), B→A = 2 (en attente).
        await svc.SetRelationAsync(1, 2, 1);
        await svc.SetRelationAsync(2, 1, 2);
        Assert.Equal(1, svc.GetRelationType(1, 2));
        Assert.Equal(2, svc.GetRelationType(2, 1));
        Assert.False(svc.AreFriends(1, 2));
        // B accepte → les deux directions passent mutuelles.
        await svc.SetRelationAsync(1, 2, 3);
        await svc.SetRelationAsync(2, 1, 3);
        Assert.True(svc.AreFriends(1, 2));
        Assert.True(svc.AreFriends(2, 1));
    }

    [Fact]
    public async Task Friend_DeclineRemoveAndBlacklist()
    {
        var svc = new FriendService();
        // B décline l'invitation de A : la relation disparaît des
        // deux côtés, A peut re-inviter.
        await svc.SetRelationAsync(1, 3, 1);
        await svc.SetRelationAsync(3, 1, 2);
        await svc.RemoveRelationAsync(1, 3);
        await svc.RemoveRelationAsync(3, 1);
        Assert.False(svc.AreFriends(1, 3));
        await svc.SetRelationAsync(1, 3, 3);
        await svc.SetRelationAsync(3, 1, 3);
        Assert.True(svc.AreFriends(1, 3));
        // Blacklist : friend_type 0 — ni ami ni invité.
        await svc.SetRelationAsync(1, 9, 0);
        Assert.True(svc.IsBlocked(1, 9));
        Assert.Contains(9, svc.GetBlocked(1));
        Assert.DoesNotContain(9, svc.GetFriends(1));
        // Retrait d'un ami : les deux directions tombent.
        await svc.RemoveRelationAsync(1, 3);
        await svc.RemoveRelationAsync(3, 1);
        Assert.Equal(-1, svc.GetRelationType(1, 3));
    }

    [Fact]
    public void Friend_CommunitySearch()
    {
        var svc = new FriendService();
        var players = new[]
        {
            (1, "Alpha", 10, 1), (2, "Beta", 50, 5), (3, "Gamma", 80, 7)
        };
        Assert.Equal(2, svc.Search(players, minLevel: 40).Count);
        Assert.Single(svc.Search(players, nameFilter: "bet"));
        Assert.Single(svc.Search(players, classId: 7));
    }

    [Fact]
    public async Task Lover_ProposeMarryBreakup()
    {
        var svc = new LoverService();
        Assert.True(svc.Propose(1, 2));
        var couple = await svc.AcceptAsync(2, 1);
        Assert.NotNull(couple);
        Assert.False(couple!.IsMarried);
        Assert.Equal(2, svc.GetPartnerId(1));
        Assert.True(await svc.MarryAsync(1));
        Assert.True(couple.IsMarried);
        svc.AddAffection(1, 500);
        Assert.Equal(500, couple.Affection);
        Assert.True(await svc.BreakupAsync(2));
        Assert.Null(svc.GetPartnerId(1));
    }

    [Fact]
    public async Task Lover_NoDoubleCouple()
    {
        var svc = new LoverService();
        svc.Propose(1, 2);
        await svc.AcceptAsync(2, 1);
        Assert.False(svc.Propose(1, 3)); // deja en couple
        Assert.False(svc.Propose(3, 1)); // cible deja en couple
    }
}
