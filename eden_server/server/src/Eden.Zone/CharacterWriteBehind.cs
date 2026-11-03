using Eden.Core;
using System.Collections.Concurrent;
using Eden.Core.Database;
using Eden.Core.Database.Repositories;

namespace Eden.Zone;

/// <summary>
/// Write-behind des champs perso à écriture fréquente (position à
/// chaque <c>MoveSelf</c>, revive/last-normal-area à chaque
/// traversée d'aire) — un <c>UPDATE</c> DB par paquet de mouvement
/// saturerait la base pour un champ qui n'a besoin d'être durable
/// qu'au flush.
/// <para>Flags dirty par charId ; le dernier état gagne
/// (<c>player_characters</c> n'a qu'une valeur courante — les
/// positions intermédiaires sont sans intérêt). Flush périodique
/// (<c>EDEN_PERSIST_FLUSH_MS</c>, défaut 15 s) et flush forcé à la
/// déconnexion, routé par la <see cref="PersistenceQueue"/> de la
/// clé — ordre garanti avec les autres écritures du perso.</para>
/// <para>DB-3 : avec Redis (<c>CharacterRepository.Hot</c>) la position
/// n'est plus gardée dans ce dictionnaire mais dans Redis (avec l'or et les
/// vitals) ; le flush périodique/forcé appelle
/// <c>CharacterRepository.FlushHotAsync</c> — Redis→PG. Sans Redis le
/// comportement mémoire d'origine est inchangé.</para>
/// </summary>
public sealed class CharacterWriteBehind : IAsyncDisposable
{
    /// <summary>Champs fréquents en attente — dernier état gagne.</summary>
    private sealed class Dirty
    {
        public (int NodeId, double X, double Y, double Z, double Dir)?
            Coords;
        public int? ReviveAreaId;
        public int? LastNormalAreaId;
    }

    private readonly CharacterRepository _repo;
    private readonly PersistenceQueue _queue;
    private readonly ConcurrentDictionary<int, Dirty> _dirty = new();
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushLoop;

    public CharacterWriteBehind(CharacterRepository repo,
        PersistenceQueue queue, TimeSpan? period = null)
    {
        _repo = repo;
        _queue = queue;
        period ??= TimeSpan.FromMilliseconds(
            EdenConfig.PersistFlushMs);
        _timer = new PeriodicTimer(period.Value);
        _flushLoop = Task.Run(FlushLoopAsync);
    }

    /// <summary>Position sale — équivalent des écritures
    /// <c>UpdateCoordinatesAsync</c> par paquet de mouvement.</summary>
    public void MarkCoordinates(int charId, int nodeId, double x,
        double y, double z, double dir)
    {
        if (_repo.Hot is not null)
        {
            // Redis : le repo applique la garde de plausibilité, écriture
            // FireAndForget (non bloquante pour la boucle de zone).
            _ = _repo.UpdateCoordinatesAsync(charId, nodeId, x, y, z, dir);
            return;
        }
        _dirty.GetOrAdd(charId, _ => new Dirty()).Coords =
            (nodeId, x, y, z, dir);
    }

    /// <summary><c>revive_area_id</c> sale.</summary>
    public void MarkReviveAreaId(int charId, int reviveAreaId)
        => _dirty.GetOrAdd(charId, _ => new Dirty()).ReviveAreaId =
            reviveAreaId;

    /// <summary><c>last_normal_area_id</c> sale.</summary>
    public void MarkLastNormalAreaId(int charId, int areaId)
        => _dirty.GetOrAdd(charId, _ => new Dirty()).LastNormalAreaId =
            areaId;

    /// <summary>
    /// Flush forcé des champs sales du perso, enfilé sur sa file de
    /// persistance puis attendu — à appeler à la déconnexion avant la
    /// sauvegarde complète (le dernier état sale ne doit pas écraser
    /// le <c>SaveFullStateAsync</c>).
    /// </summary>
    public async Task FlushAsync(int charId)
    {
        EnqueueDirty(charId);
        EnqueueHotFlush(charId);
        await _queue.FlushAsync(charId);
    }

    private async Task FlushLoopAsync()
    {
        while (await _timer.WaitForNextTickAsync(_cts.Token)
                   .ConfigureAwait(false))
        {
            foreach (var key in _dirty.Keys)
                EnqueueDirty(key);
            if (_repo.Hot is not null)
                await _repo.FlushAllHotAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Redis→PG pour ce perso, sur sa file ordonnée.</summary>
    private void EnqueueHotFlush(int charId)
    {
        if (_repo.Hot is null) return;
        _ = _queue.EnqueueAsync(charId, _ => _repo.FlushHotAsync(charId));
    }

    /// <summary>Snapshot des champs sales → un travail ordonné sur la
    /// file du perso ; le dirty est vidé dès l'enqueue (les écritures
    /// postérieures re-marquent).</summary>
    private void EnqueueDirty(int charId)
    {
        if (!_dirty.TryGetValue(charId, out var d)) return;
        var coords = d.Coords; d.Coords = null;
        var revive = d.ReviveAreaId; d.ReviveAreaId = null;
        var lastNormal = d.LastNormalAreaId; d.LastNormalAreaId = null;
        if (coords == null && revive == null && lastNormal == null)
            return;
        _ = _queue.EnqueueAsync(charId, async ct =>
        {
            if (coords is { } c)
                await _repo.UpdateCoordinatesAsync(charId, c.NodeId,
                    c.X, c.Y, c.Z, c.Dir);
            if (revive is { } r)
                await _repo.SetReviveAreaIdAsync(charId, r);
            if (lastNormal is { } l)
                await _repo.SetLastNormalAreaIdAsync(charId, l);
        });
    }

    /// <summary>Flush final des champs sales puis arrêt de la
    /// boucle périodique.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var key in _dirty.Keys)
            EnqueueDirty(key);
        if (_repo.Hot is not null)
            await _repo.FlushAllHotAsync().ConfigureAwait(false);
        _cts.Cancel();
        _timer.Dispose();
        try { await _flushLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
