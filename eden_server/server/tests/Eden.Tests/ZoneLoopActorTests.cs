using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Primitives acteur de <see cref="ZoneLoop"/> — preuve des garanties
/// du modèle : commandes FIFO sérialisées (une à la fois), travail
/// schedulé déclenché sur le fil de la boucle, PostOrRun inline vs
/// post, exceptions qui ne tuent pas le drain.
/// La boucle est instanciée avec des dépendances nulles : les ticks
/// périodiques (TickMobCombats/TickRespawns) avalent leurs propres
/// exceptions et n'interfèrent pas avec les assertions.
/// PostAsync se résout quand la commande a tourné — le sentinel
/// borne en plus tout travail schedulé échu entretemps.
/// </summary>
public class ZoneLoopActorTests
{
    /// <summary>Poste un sentinel qui complète quand la boucle l'a
    /// exécuté — borne tout travail posté/schedulé avant lui.</summary>
    private static Task DrainAsync(ZoneLoop loop)
    {
        var tcs = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loop.PostAsync(_ =>
        {
            tcs.SetResult();
            return Task.CompletedTask;
        });
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PostAsync_CommandsRunSeriallyInOrder()
    {
        await using var loop = new ZoneLoop(null!, null!);
        var order = new List<int>();
        for (var i = 0; i < 50; i++)
        {
            var n = i;
            await loop.PostAsync(_ =>
            {
                order.Add(n);
                return Task.CompletedTask;
            });
        }
        await DrainAsync(loop);
        Assert.Equal(Enumerable.Range(0, 50), order);
    }

    [Fact]
    public async Task PostAsync_CommandsNeverRunConcurrently()
    {
        await using var loop = new ZoneLoop(null!, null!);
        var active = 0;
        var maxActive = 0;
        foreach (var _ in Enumerable.Range(0, 20))
            await loop.PostAsync(async _ =>
            {
                maxActive = Math.Max(maxActive,
                    Interlocked.Increment(ref active));
                await Task.Delay(1);
                Interlocked.Decrement(ref active);
            });
        await DrainAsync(loop);
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task PostOrRunAsync_FromOutside_RunsOnLoopThread()
    {
        await using var loop = new ZoneLoop(null!, null!);
        var onLoop = false;
        await loop.PostOrRunAsync(_ =>
        {
            onLoop = loop.IsOnLoopThread;
            return Task.CompletedTask;
        });
        Assert.True(onLoop);
    }

    [Fact]
    public async Task PostOrRunAsync_FromInside_RunsInlineNoDeadlock()
    {
        await using var loop = new ZoneLoop(null!, null!);
        var done = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // Un post+await depuis la boucle serait un deadlock — le
        // point de l'exécution en ligne via IsOnLoopThread.
        _ = loop.PostAsync(async _ =>
        {
            Assert.True(loop.IsOnLoopThread);
            await loop.PostOrRunAsync(__ =>
            {
                done.TrySetResult(true);
                return Task.CompletedTask;
            });
            done.TrySetResult(true);
        });
        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Schedule_FiresWorkOnLoopThread()
    {
        await using var loop = new ZoneLoop(null!, null!);
        var done = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Schedule(TimeSpan.Zero, _ =>
        {
            done.SetResult(loop.IsOnLoopThread);
            return Task.CompletedTask;
        });
        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Schedule_PassesCancelledTokenToWork()
    {
        await using var loop = new ZoneLoop(null!, null!);
        using var cts = new CancellationTokenSource();
        var done = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Schedule(TimeSpan.Zero, t =>
        {
            done.SetResult(t.IsCancellationRequested);
            return Task.CompletedTask;
        }, cts.Token);
        cts.Cancel();
        // L'événement échu reçoit le token annulé — au travail de
        // décider d'agir ou non (purge d'état côté cast).
        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PostAsync_ThrowingCommand_PropagatesAndDrainSurvives()
    {
        await using var loop = new ZoneLoop(null!, null!);
        // L'exception remonte à l'appelant (complétion) sans tuer la
        // boucle — la commande suivante tourne normalement.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loop.PostAsync(_ => throw new InvalidOperationException("boom")));
        var ran = false;
        await loop.PostAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        Assert.True(ran);
    }
}
