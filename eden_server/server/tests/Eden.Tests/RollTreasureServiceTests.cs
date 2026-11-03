using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// <see cref="RollTreasureService"/> — port du
/// <c>RollitemManager</c> natif (CheckRollItem @0x84bc220,
/// StartRollTreasure @0x84bb560, Rollsort @0x84bb300,
/// UserDisconnect). Couvre : jets dédupliqués, tri need &gt; greed
/// décroissant, force-pass des absents, all-pass → giveup,
/// déconnexion.
/// </summary>
public class RollTreasureServiceTests
{
    private static RollTreasureService NewWithRoll(
        params int[] charIds)
    {
        var svc = new RollTreasureService();
        svc.Create(1000, 42, 0, "TestSword", charIds);
        return svc;
    }

    /// <summary>Réponses partielles → Recorded ; dernière → Complete.
    /// </summary>
    [Fact]
    public void CheckRoll_LastAnswerCompletes()
    {
        var svc = NewWithRoll(1, 2, 3);
        Assert.Equal(RollTreasureService.RollCheckResult.Recorded,
            svc.CheckRoll(1000, 1, 1).Result);
        Assert.Equal(RollTreasureService.RollCheckResult.Recorded,
            svc.CheckRoll(1000, 2, 2).Result);
        Assert.Equal(RollTreasureService.RollCheckResult.Complete,
            svc.CheckRoll(1000, 3, 1).Result);
    }

    /// <summary>Non-participant / roll inconnu → NotParticipant ;
    /// double réponse → AlreadyAnswered (CheckRollItem natif
    /// rejette les deux).</summary>
    [Fact]
    public void CheckRoll_RejectsNonParticipantAndDuplicates()
    {
        var svc = NewWithRoll(1, 2);
        Assert.Equal(RollTreasureService.RollCheckResult.NotParticipant,
            svc.CheckRoll(1000, 99, 1).Result);
        Assert.Equal(RollTreasureService.RollCheckResult.NotParticipant,
            svc.CheckRoll(7777, 1, 1).Result);
        svc.CheckRoll(1000, 1, 1);
        Assert.Equal(
            RollTreasureService.RollCheckResult.AlreadyAnswered,
            svc.CheckRoll(1000, 1, 2).Result);
    }

    /// <summary>Jets tirés dans [0, 99) et dédupliqués entre
    /// participants (re-tirage natif sur collision).</summary>
    [Fact]
    public void CheckRoll_RollsAreUniqueAndInRange()
    {
        var svc = NewWithRoll(1, 2, 3, 4, 5);
        var rolls = new List<short>();
        foreach (var c in new[] { 1, 2, 3, 4, 5 })
            rolls.Add(svc.CheckRoll(1000, c, 1).RollValue);
        Assert.All(rolls, r => Assert.InRange(r, 0, 98));
        Assert.Equal(rolls.Count, rolls.Distinct().Count());
    }

    /// <summary>Le gagnant est le plus haut jet « need » — les greed
    /// ne gagnent que s'il n'y a aucun need (Rollsort natif : need
    /// triés d'abord, puis greed, décroissant).</summary>
    [Fact]
    public void Resolve_NeedBeatsGreed()
    {
        var svc = NewWithRoll(1, 2);
        svc.CheckRoll(1000, 1, 2); // greed
        svc.CheckRoll(1000, 2, 1); // need → gagnant
        Assert.Equal(2, svc.Resolve(1000));
    }

    /// <summary>Need gagne même avec un jet plus faible qu'un greed.
    /// </summary>
    [Fact]
    public void Resolve_NeedPriorityIndependentOfRollValue()
    {
        var svc = NewWithRoll(1, 2, 3);
        svc.CheckRoll(1000, 1, 2);
        svc.CheckRoll(1000, 2, 2);
        svc.CheckRoll(1000, 3, 1);
        Assert.Equal(3, svc.Resolve(1000));
    }

    /// <summary>Entre deux needs, c'est le jet le plus haut qui
    /// gagne — vérifié sur de nombreux tirages (le tri
    /// <c>Rollsort</c> est décroissant).</summary>
    [Fact]
    public void Resolve_HighestNeedRollWins()
    {
        for (var i = 0; i < 200; i++)
        {
            var svc = NewWithRoll(1, 2);
            var r1 = svc.CheckRoll(1000, 1, 1).RollValue;
            var r2 = svc.CheckRoll(1000, 2, 1).RollValue;
            Assert.Equal(r1 > r2 ? 1 : 2, svc.Resolve(1000));
        }
    }

    /// <summary>Timeout → non-répondants passés de force
    /// (UpdateRoll natif) ; un seul « need » gagne.</summary>
    [Fact]
    public void Resolve_UnansweredAreForcedPass()
    {
        var svc = NewWithRoll(1, 2, 3);
        svc.CheckRoll(1000, 2, 1); // seul répondant, need
        Assert.Equal(2, svc.Resolve(1000));
    }

    /// <summary>Tout le monde passe (ou personne ne répond) → null
    /// (SetRollGiveUp natif — le trésor redevient ramassable).</summary>
    [Fact]
    public void Resolve_AllPassReturnsNull()
    {
        var svc = NewWithRoll(1, 2);
        svc.CheckRoll(1000, 1, 0);
        Assert.Null(svc.Resolve(1000));

        var svc2 = NewWithRoll(1, 2);
        svc2.CheckRoll(1000, 1, 0);
        svc2.CheckRoll(1000, 2, 0);
        Assert.Null(svc2.Resolve(1000));
    }

    /// <summary>Déconnexion → le joueur est marqué pass
    /// (UserDisconnect natif) et le roll complété est rapporté.</summary>
    [Fact]
    public void UserDisconnect_ForcesPassAndReportsCompleted()
    {
        var svc = NewWithRoll(1, 2, 3);
        svc.CheckRoll(1000, 1, 1);
        svc.CheckRoll(1000, 2, 1);
        var done = svc.UserDisconnect(3);
        Assert.Equal(new uint[] { 1000 }, done);
        // 3 a passé → gagnant parmi 1 et 2.
        Assert.Contains(svc.Resolve(1000), new int?[] { 1, 2 });
    }

    /// <summary>Déconnexion d'un non-participant (ou roll déjà
    /// répondu) → aucun roll complété.</summary>
    [Fact]
    public void UserDisconnect_NonParticipantNoop()
    {
        var svc = NewWithRoll(1, 2);
        svc.CheckRoll(1000, 1, 1);
        Assert.Empty(svc.UserDisconnect(99));
        // Déjà répondu → pas de changement.
        Assert.Empty(svc.UserDisconnect(1));
    }
}
