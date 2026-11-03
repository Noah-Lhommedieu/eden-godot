using Eden.Core.Data;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// File et matchmaking versus — <c>CVersusInstanceFactory::JoinWait</c>
/// mission natif @0x080d8ad0 : FIFO, groupe de 10 (factory+4),
/// répartition 2 camps, sortie de file/instance, sched 602.
/// </summary>
public class VersusServiceTests
{
    /// <summary>10 joins → match formé au 10e, camps 5/5 alternés.</summary>
    [Fact]
    public void Join_FormsMatchAtGroupSize()
    {
        var svc = new VersusService();
        VersusService.VersusMatch? match = null;
        for (var i = 1; i <= 9; i++)
            Assert.Null(svc.Join(1000 + i));
        match = svc.Join(1010);
        Assert.NotNull(match);
        Assert.Equal(5, match!.CampA.Count);
        Assert.Equal(5, match.CampB.Count);
        // Alternance native : membres pairs → camp A, impairs → B.
        Assert.Equal(new[] { 1001, 1003, 1005, 1007, 1009 },
            match.CampA);
        Assert.Equal(new[] { 1002, 1004, 1006, 1008, 1010 },
            match.CampB);
        // Les 10 membres pointent vers l'instance.
        foreach (var (_, c) in match.Members())
            Assert.Equal(match.Id, svc.MatchOf(c)!.Id);
        // La file est vidée — un 11e attend un nouveau groupe.
        Assert.Null(svc.Join(1011));
        Assert.True(svc.InQueue(1011));
    }

    /// <summary>Node arène alterne 406/407 entre instances.</summary>
    [Fact]
    public void Join_ArenaNodesAlternate()
    {
        var svc = new VersusService();
        var nodes = new List<int>();
        for (var inst = 0; inst < 3; inst++)
        {
            VersusService.VersusMatch? m = null;
            for (var i = 0; i < VersusService.GroupSize; i++)
                m = svc.Join(2000 + inst * 100 + i);
            nodes.Add(m!.NodeId);
        }
        Assert.Equal(406, nodes[0]);
        Assert.Equal(407, nodes[1]);
        Assert.Equal(406, nodes[2]);
    }

    /// <summary>Déjà en file / en match → Join no-op.</summary>
    [Fact]
    public void Join_AlreadyQueuedOrMatched_NoOp()
    {
        var svc = new VersusService();
        Assert.Null(svc.Join(1));
        Assert.Null(svc.Join(1));   // déjà en file — pas de doublon
        Assert.True(svc.InQueue(1));
        // Le doublon n'a pas gonflé la file : 9 autres suffisent.
        VersusService.VersusMatch? m = null;
        for (var i = 2; i <= 10; i++) m = svc.Join(i);
        Assert.NotNull(m);
        Assert.False(svc.InQueue(1));   // consommé par le match
        Assert.NotNull(svc.MatchOf(1));
    }

    /// <summary>LeaveWait : retrait de file silencieux.</summary>
    [Fact]
    public void Leave_RemovesFromQueue()
    {
        var svc = new VersusService();
        svc.Join(42);
        Assert.True(svc.Leave(42));
        Assert.False(svc.InQueue(42));
        Assert.False(svc.Leave(42));
    }

    /// <summary>LeaveInstance : retrait du membre ; la dernière
    /// sortie dissout l'instance (CloseVersusInstance).</summary>
    [Fact]
    public void LeaveInstance_DissolvesWhenEmpty()
    {
        var svc = new VersusService();
        VersusService.VersusMatch? match = null;
        for (var i = 1; i <= 10; i++)
            match = svc.Join(i);
        Assert.NotNull(match);
        foreach (var (_, c) in match!.Members())
            Assert.NotNull(svc.LeaveInstance(c));
        foreach (var (_, c) in match.Members())
            Assert.Null(svc.MatchOf(c));
    }

    /// <summary>Sched 602 : lun 19h-20h (entre autres créneaux).</summary>
    [Fact]
    public void IsOpen_Schedule602Window()
    {
        var schedules = new Dictionary<int,
            List<GameDataService.ScheduleEntry>>
        {
            [602] = new()
            {
                new GameDataService.ScheduleEntry(1, 1900, 2000),
            },
        };
        // Lundi 19h30 → ouvert ; lundi 20h30 → fermé.
        var monday = new DateTimeOffset(2026, 11, 2, 19, 30, 0,
            TimeSpan.Zero); // 2026-11-02 = lundi
        var late = new DateTimeOffset(2026, 11, 2, 20, 30, 0,
            TimeSpan.Zero);
        Assert.True(VersusService.IsOpen(schedules, monday));
        Assert.False(VersusService.IsOpen(schedules, late));
    }
}
