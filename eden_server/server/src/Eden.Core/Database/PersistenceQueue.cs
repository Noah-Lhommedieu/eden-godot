using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Eden.Core.Logging;

namespace Eden.Core.Database;

/// <summary>
/// File de persistance ordonnée par clé (charId) — remplace les
/// écritures DB fire-and-forget (<c>_ = repo.XxxAsync()</c>) dont les
/// exceptions étaient perdues et dont l'ordre n'était pas garanti
/// (deux <c>UPDATE</c> lancés en vol pouvaient s'exécuter dans le
/// désordre : un statut obsolète écrasait le nouveau).
/// <para>Une file bornée + un drain unique par clé : les écritures
/// d'un même personnage s'exécutent strictement dans l'ordre
/// d'enqueue. Clés différentes = drains parallèles (pas de
/// serialisation globale).</para>
/// <para>Backpressure : file pleine → <see cref="EnqueueAsync"/>
/// attend, jamais de drop silencieux. Les exceptions de travail sont
/// observées et loguées <c>[Persistence]</c> sans tuer le drain.</para>
/// </summary>
public sealed class PersistenceQueue : IAsyncDisposable
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<int,
        Channel<Func<CancellationToken, Task>>> _channels = new();
    private static volatile int _globalDepth;
    private static ObservableGauge<int>? _depthGauge;
    private readonly ConcurrentDictionary<int, Task> _drainers = new();
    private volatile bool _disposed;

    /// <param name="capacity">Travaux max en attente par clé avant
    /// backpressure sur l'enqueueur (défaut 256).</param>
    public PersistenceQueue(int capacity = 256) => _capacity = capacity;

    /// <summary>
    /// Enfile <paramref name="work"/> pour la clé — exécuté par le
    /// drain dédié, après tous les travaux enfilés avant lui pour la
    /// même clé. Retourne dès l'enqueue (pas d'attente d'exécution) :
    /// utiliser <see cref="FlushAsync"/> pour attendre la persistance
    /// effective.
    /// </summary>
    public ValueTask EnqueueAsync(int key,
        Func<CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        if (_disposed) return ValueTask.CompletedTask;
        _depthGauge ??= EdenMetrics.ObservePersistenceQueue(
            () => _globalDepth);
        var ch = _channels.GetOrAdd(key, CreateWorker);
        if (ch.Writer.TryWrite(work))
        {
            Interlocked.Increment(ref _globalDepth);
            return ValueTask.CompletedTask;
        }
        // File pleine : backpressure — l'appelant attend qu'un drain
        // libère de la place, jamais de drop silencieux.
        return EnqueueSlowAsync(ch, work, ct);
    }

    /// <summary>
    /// Attend que tous les travaux enfilés jusqu'ici pour la clé soient
    /// exécutés (point de synchronisation — déconnexion, migration).
    /// </summary>
    public async Task FlushAsync(int key, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(key,
            _ => { tcs.TrySetResult(); return Task.CompletedTask; }, ct);
        await tcs.Task;
    }

    private static async ValueTask EnqueueSlowAsync(
        Channel<Func<CancellationToken, Task>> ch,
        Func<CancellationToken, Task> work, CancellationToken ct)
    {
        await ch.Writer.WriteAsync(work, ct);
        Interlocked.Increment(ref _globalDepth);
    }

    private Channel<Func<CancellationToken, Task>> CreateWorker(int key)
    {
        var ch = Channel.CreateBounded<Func<CancellationToken, Task>>(
            new BoundedChannelOptions(_capacity)
            { SingleReader = true, SingleWriter = false,
              FullMode = BoundedChannelFullMode.Wait });
        _drainers[key] = Task.Run(() => DrainAsync(key, ch));
        return ch;
    }

    private async Task DrainAsync(int key,
        Channel<Func<CancellationToken, Task>> ch)
    {
        try
        {
            await foreach (var work in ch.Reader.ReadAllAsync()
                               .ConfigureAwait(false))
            {
                Interlocked.Decrement(ref _globalDepth);
                var sw = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    await work(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Exception observée + loguée — le drain continue :
                    // une écriture en échec ne doit pas bloquer les
                    // suivantes ni passer inaperçue.
                    EdenLog.Write(
                        $"[Persistence] key={key} : {ex.Message}");
                }
                EdenMetrics.DbOpMs.Record(
                    System.Diagnostics.Stopwatch.GetElapsedTime(sw)
                        .TotalMilliseconds,
                    new KeyValuePair<string, object?>("operation",
                        "persistence_queue"));
                EdenMetrics.DbOperations.Add(1,
                    new KeyValuePair<string, object?>("operation",
                        "persistence_queue"));
            }
        }
        finally
        {
            _channels.TryRemove(key, out _);
            _drainers.TryRemove(key, out _);
        }
    }

    /// <summary>Complète toutes les files et attend la fin des drains
    /// — les travaux enfilés sont exécutés avant la coupure.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var kv in _channels)
            kv.Value.Writer.TryComplete();
        await Task.WhenAll(_drainers.Values).ConfigureAwait(false);
    }
}
