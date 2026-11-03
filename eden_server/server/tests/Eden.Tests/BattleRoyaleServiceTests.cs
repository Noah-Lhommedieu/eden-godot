using Eden.Core.Data;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// File Battle Royale — <c>CBattleRoyaleFactory::JoinWait</c>
/// mission natif @0x0804ad70 : FIFO, groupe de 30 (factory+4=0x1e),
/// node arène 309 (factory+8=0x135), sched 502.
/// </summary>
public class BattleRoyaleServiceTests
{
    /// <summary>30 joins → BR formé ; la file est vidée.</summary>
    [Fact]
    public void Join_FormsBattleRoyaleAtThirty()
    {
        var svc = new BattleRoyaleService();
        (int Id, IReadOnlyList<int> Members)? br = null;
        for (var i = 1; i <= 29; i++)
            Assert.Null(svc.Join(i));
        br = svc.Join(30);
        Assert.NotNull(br);
        Assert.Equal(30, br!.Value.Members.Count);
        foreach (var c in br.Value.Members)
            Assert.Equal(br.Value.Id, svc.InstanceOf(c));
        Assert.Null(svc.Join(31));
        Assert.True(svc.InQueue(31));
    }

    /// <summary>Déjà inscrit → Join no-op (gate 0x18c3 natif est
    /// côté zone ; le service ignore).</summary>
    [Fact]
    public void Join_AlreadyQueued_NoOp()
    {
        var svc = new BattleRoyaleService();
        svc.Join(7);
        Assert.Null(svc.Join(7));
        Assert.True(svc.InQueue(7));
    }

    /// <summary>LeaveWait : retrait de file silencieux.</summary>
    [Fact]
    public void Leave_RemovesFromQueue()
    {
        var svc = new BattleRoyaleService();
        svc.Join(9);
        Assert.True(svc.Leave(9));
        Assert.False(svc.InQueue(9));
    }

    /// <summary>LeaveInstance : le BR se dissout sous 2 joueurs
    /// (plus de combat possible).</summary>
    [Fact]
    public void LeaveInstance_DissolvesBelowTwo()
    {
        var svc = new BattleRoyaleService();
        (int Id, IReadOnlyList<int> Members)? br = null;
        for (var i = 1; i <= 30; i++) br = svc.Join(i);
        var id = br!.Value.Id;
        var members = br.Value.Members.ToList();   // snapshot — le
        // service mute la liste live à chaque sortie.
        // 29 sorties : la 29e ramène à 1 membre → dissolution ; les
        // charIds des membres restants sont purgés du lookup.
        foreach (var c in members.Take(29))
            svc.LeaveInstance(c);
        // Instance dissoute : le dernier n'a plus d'instance.
        Assert.Equal(0, svc.LeaveInstance(members.Last()));
        foreach (var c in members)
            Assert.Equal(0, svc.InstanceOf(c));
        Assert.Empty(svc.MembersOf(id));
    }

    /// <summary>Sched 502 : dim 21h31-22h59 (créneaux natifs).</summary>
    [Fact]
    public void IsOpen_Schedule502Window()
    {
        var schedules = new Dictionary<int,
            List<GameDataService.ScheduleEntry>>
        {
            [502] = new()
            {
                new GameDataService.ScheduleEntry(7, 2131, 2259),
            },
        };
        // 2026-11-08 = dimanche.
        var inWindow = new DateTimeOffset(2026, 11, 8, 22, 0, 0,
            TimeSpan.Zero);
        var outWindow = new DateTimeOffset(2026, 11, 8, 23, 30, 0,
            TimeSpan.Zero);
        Assert.True(BattleRoyaleService.IsOpen(schedules, inWindow));
        Assert.False(BattleRoyaleService.IsOpen(schedules, outWindow));
    }
}
