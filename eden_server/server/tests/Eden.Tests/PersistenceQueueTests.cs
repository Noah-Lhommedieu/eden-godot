using Eden.Core.Database;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// File de persistance ordonnée par clé — preuve des garanties :
/// ordre strict par clé, parallélisme entre clés, exceptions
/// observées sans tuer le drain, flush = point de durabilité.
/// </summary>
public class PersistenceQueueTests
{
    [Fact]
    public async Task Enqueue_SameKey_ExecutesInOrder()
    {
        await using var q = new PersistenceQueue();
        var order = new List<int>();
        var gate = new object();
        for (var i = 0; i < 100; i++)
        {
            var n = i;
            await q.EnqueueAsync(42, _ =>
            {
                lock (gate) order.Add(n);
                return Task.CompletedTask;
            });
        }
        await q.FlushAsync(42);
        Assert.Equal(Enumerable.Range(0, 100), order);
    }

    [Fact]
    public async Task Enqueue_Exception_IsObserved_DrainSurvives()
    {
        await using var q = new PersistenceQueue();
        var ran = false;
        await q.EnqueueAsync(1, _ =>
            throw new InvalidOperationException("boom"));
        await q.EnqueueAsync(1, _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await q.FlushAsync(1);
        // Une écriture en échec ne bloque pas les suivantes.
        Assert.True(ran);
    }

    [Fact]
    public async Task Flush_WaitsForPendingWork()
    {
        await using var q = new PersistenceQueue();
        var done = 0;
        await q.EnqueueAsync(7, async _ =>
        {
            await Task.Delay(50);
            Interlocked.Increment(ref done);
        });
        await q.FlushAsync(7);
        Assert.Equal(1, done);
    }

    [Fact]
    public async Task Enqueue_DifferentKeys_ParallelDrains()
    {
        await using var q = new PersistenceQueue();
        // Deux clés = deux drains : un travail lent sur la clé 1 ne
        // doit pas retarder la clé 2.
        var tcs1 = new TaskCompletionSource();
        var key2Done = new TaskCompletionSource();
        await q.EnqueueAsync(1, async _ => await tcs1.Task);
        await q.EnqueueAsync(2, _ =>
        {
            key2Done.TrySetResult();
            return Task.CompletedTask;
        });
        await key2Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        tcs1.SetResult();
        await q.FlushAsync(1);
    }
}
