using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Eden.Core;
using Eden.Core.Logging;

namespace Eden.Zone;


/// <summary>
/// Boucle acteur de la zone — file de commandes bornée à un seul
/// lecteur, réveillée <b>à l'arrivée</b> d'une commande (l'attente sur
/// le <see cref="Channel"/> est bornée par l'échéance du prochain
/// tick de jeu, pas par un <see cref="PeriodicTimer"/> qui ferait
/// attendre les commandes jusqu'à 100 ms — écho mesuré p50 ~42 ms en
/// baseline 2026-10-26). Objectif du modèle : les sessions ne mutent
/// plus l'état de jeu depuis leur thread de lecture, elles
/// <see cref="Post"/>ent une commande que la boucle exécute en
/// contexte unique — fini les races respawn/chase/cast vs handlers.
/// <para>Migration progressive (catégorie par catégorie) :</para>
/// <para>- cat. 1 (ici) : respawn des mobs + cycle des trésors au sol
/// — remplace le <c>System.Timers.Timer</c> 1 s de Program.cs dont
/// le callback fire-and-forget (<c>_ = TickTreasuresAsync()</c>)
/// perdait les exceptions et pouvait s'empiler sur lui-même ;</para>
/// <para>- cat. 2 : chase des mobs ; cat. 3 : séquences de cast.</para>
/// <para>Cadence : le travail périodique (combat, aggro, respawn)
/// bat à 100 ms ; respawn+trésors restent sur un tick toutes les
/// 10 itérations (= la seconde native). Les commandes elles sont
/// drainées dès réception, par tranches de
/// <see cref="CommandBudget"/> — un flot continu ne peut plus
/// empêcher le tick de jeu de partir à l'heure.</para>
/// </summary>
public sealed class ZoneLoop : IAsyncDisposable
{
    /// <summary>Intervalle de la boucle (native : l'action loop du
    /// zone server tourne en continu ; 100 ms est la maille des
    /// timers de jeu).</summary>
    public static readonly TimeSpan TickInterval =
        TimeSpan.FromMilliseconds(100);

    /// <summary>Budget de temps par tranche de drain de commandes —
    /// au-delà, la boucle revient vérifier l'échéance du tick avant de
    /// reprendre la file. <c>EDEN_ZONE_COMMAND_BUDGET_MS</c>, défaut
    /// 2 ms.</summary>
    public static readonly TimeSpan CommandBudget =
        TimeSpan.FromMilliseconds(EdenConfig.ZoneCommandBudgetMs);

    /// <summary>Période respawn/trésors en ticks — 10 × 100 ms = la
    /// seconde du timer natif qu'il remplace.</summary>
    private const int SlowTickEvery = 10;

    private readonly Channel<Func<CancellationToken, Task>> _commands =
        Channel.CreateBounded<Func<CancellationToken, Task>>(
            new BoundedChannelOptions(1024)
            { SingleReader = true, SingleWriter = false,
              FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cts = new();
    private readonly ZoneServices _services;
    private readonly ZoneEntryFlow _flow;
    private readonly ObservableGauge<int> _queueGauge;
    private readonly Task _run;
    private int _tickCount;
    /// <summary>Marqueur « sur la boucle » propagé par AsyncLocal —
    /// <see cref="IsOnLoopThread"/>. Un thread-id capturé ne marche
    /// PAS : chaque await de RunAsync reprend sur un thread pool
    /// différent ; AsyncLocal suit le flux logique, pas le thread.</summary>
    private static readonly AsyncLocal<bool> _onLoop = new();

    /// <summary>Événements différés (casts, twins de combat, retours
    /// de mob) — déclenchés au premier tick ≥ l'échéance, sur le fil
    /// de la boucle. Remplace les <c>Task.Delay</c> fire-and-forget
    /// qui mutaient l'état de jeu hors boucle.</summary>
    private readonly List<(DateTime Due, Func<CancellationToken, Task> Work,
        CancellationToken Token)> _scheduled = new();

    public ZoneLoop(ZoneServices services, ZoneEntryFlow flow)
    {
        _services = services;
        _flow = flow;
        _queueGauge = EdenMetrics.ObserveZoneCommandQueue(
            () => _commands.Reader.Count);
        _run = Task.Run(RunAsync);
    }

    /// <summary>Vrai si l'appelant tourne dans le flux de la boucle
    /// (commande, tick, événement schedulé) — <see cref="PostOrRunAsync"/>
    /// exécute alors en ligne (un post+await depuis la boucle serait
    /// un deadlock : la file ne serait plus drainée pendant l'attente).
    /// Basé sur AsyncLocal : les continuations async de la boucle
    /// reprennent sur des threads pool différents à chaque await.</summary>
    public bool IsOnLoopThread => _onLoop.Value;

    /// <summary>Poste une commande vers la boucle de zone — exécutée
    /// sur le fil unique entre deux ticks. La tâche retournée se
    /// résout quand la commande a TOURNÉ (pas à l'enqueue) : un
    /// await ordonne donc « exécuté avant de continuer » — requis
    /// pour les cleanups de déconnexion. Backpressure bornée : file
    /// pleine → l'appelant attend avant même de poster.</summary>
    public async Task PostAsync(Func<CancellationToken, Task> command,
        CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueuedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        await _commands.Writer.WriteAsync(async token =>
        {
            EdenMetrics.ZoneCommandDelayMs.Record(
                System.Diagnostics.Stopwatch.GetElapsedTime(enqueuedAt)
                    .TotalMilliseconds);
            try { await command(token); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }, ct);
        await tcs.Task;
    }

    /// <summary>Exécute <paramref name="work"/> sur le fil de la boucle
    /// — inline si déjà dessus, sinon via la file de commandes. C'est
    /// le point d'entrée « actor » : les mutations d'état de jeu ne
    /// tournent que là.</summary>
    public Task PostOrRunAsync(Func<CancellationToken, Task> work,
        CancellationToken ct = default)
        => IsOnLoopThread ? work(ct) : PostAsync(work, ct);

    /// <summary>Planifie <paramref name="work"/> dans
    /// <paramref name="delay"/> — exécuté sur le fil de la boucle au
    /// premier tick ≥ l'échéance (granularité 100 ms, comme les
    /// CEvent* natifs). Le <paramref name="token"/> est transmis au
    /// travail, qui décide comment réagir à l'annulation (purge
    /// d'état côté cast, abandon côté twin).</summary>
    public void Schedule(TimeSpan delay,
        Func<CancellationToken, Task> work,
        CancellationToken token = default)
    {
        lock (_scheduled)
            _scheduled.Add((DateTime.UtcNow + delay, work, token));
    }

    /// <summary>Déclenche les événements échus — retirés de la liste
    /// avant exécution (un travail peut s'en replanifier un autre).</summary>
    private async Task FireScheduledAsync()
    {
        List<(DateTime Due, Func<CancellationToken, Task> Work,
            CancellationToken Token)> due;
        lock (_scheduled)
        {
            if (_scheduled.Count == 0) return;
            var now = DateTime.UtcNow;
            due = _scheduled.Where(s => s.Due <= now).ToList();
            _scheduled.RemoveAll(s => s.Due <= now);
        }
        foreach (var s in due)
            try { await s.Work(s.Token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                EdenLog.Write(
                    $"[ZoneLoop] scheduled : {ex.Message}");
            }
    }

    /// <summary>
    /// Corps de la boucle : dort sur la file de commandes jusqu'à la
    /// prochaine échéance de tick ; une commande entrante la réveille
    /// immédiatement (latence d'écho quasi nulle), puis le drain est
    /// borné par <see cref="CommandBudget"/> pour que le travail
    /// périodique parte toujours à l'heure. Les ticks manqués ne sont
    /// pas rattrapés (même coalescence qu'un <see cref="PeriodicTimer"/>).
    /// </summary>
    private async Task RunAsync()
    {
        _onLoop.Value = true;
        var nextTick = DateTime.UtcNow + TickInterval;
        try
        {
            while (true)
            {
                // 1. Attente réactive : une commande postée réveille
                // la boucle tout de suite (WaitToReadAsync se résout
                // dès qu'un item arrive, sans attendre le timeout) ;
                // sinon on se rendort jusqu'à l'échéance du tick.
                var wait = nextTick - DateTime.UtcNow;
                if (wait > TimeSpan.Zero &&
                    !await WaitCommandsOrDeadlineAsync(wait)
                        .ConfigureAwait(false))
                    break; // file complétée ou arrêt demandé

                // 2. Commandes — tranche bornée : un flot soutenu ne
                // repousse plus le tick de jeu au-delà du budget.
                await DrainCommandsAsync().ConfigureAwait(false);

                // 3. Travail périodique quand l'échéance est atteinte.
                var now = DateTime.UtcNow;
                if (now >= nextTick)
                {
                    var tickStart = System.Diagnostics.Stopwatch
                        .GetTimestamp();
                    await TickWorkAsync().ConfigureAwait(false);
                    EdenMetrics.ZoneTickMs.Record(
                        System.Diagnostics.Stopwatch
                            .GetElapsedTime(tickStart).TotalMilliseconds);
                    nextTick += TickInterval;
                    if (nextTick <= now)
                        // En retard (tick long ou pause process) : on
                        // saute l'échéance ratée plutôt que rafaler.
                        nextTick = now + TickInterval;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Attend une commande jusqu'à <paramref name="wait"/>
    /// max — retourne false si la file est complétée ou l'arrêt
    /// demandé ; le dépassement de délai (échéance du tick) retourne
    /// true pour que la boucle enchaîne sur son travail périodique.
    /// </summary>
    private async Task<bool> WaitCommandsOrDeadlineAsync(TimeSpan wait)
    {
        using var cts = CancellationTokenSource
            .CreateLinkedTokenSource(_cts.Token);
        cts.CancelAfter(wait);
        try
        {
            return await _commands.Reader
                .WaitToReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Distinguer échéance du tick (→ continuer) et arrêt
            // demandé (→ sortir de boucle).
            return !_cts.IsCancellationRequested;
        }
    }

    /// <summary>Draine la file de commandes par tranches de
    /// <see cref="CommandBudget"/> — un seul post peut dépasser le
    /// budget (pas de préemption), mais la boucle revient alors
    /// vérifier l'échéance avant de poursuivre le reste.</summary>
    private async Task DrainCommandsAsync()
    {
        var phaseStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var budgetEnd = phaseStart + (long)(CommandBudget.TotalSeconds
            * System.Diagnostics.Stopwatch.Frequency);
        while (_commands.Reader.TryRead(out var cmd))
        {
            try { await cmd(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                EdenLog.Write($"[ZoneLoop] commande : {ex.Message}");
            }
            if (System.Diagnostics.Stopwatch.GetTimestamp() >= budgetEnd)
                break;
        }
        RecordSubsystem("commands", phaseStart);
    }

    /// <summary>Travail périodique du tick de 100 ms — événements
    /// différés échus, combat des mobs, aggro passif, et toutes les
    /// 10 itérations le tick lent (trésors au sol).</summary>
    private async Task TickWorkAsync()
    {
        // 1. Événements différés échus — casts arrivant à
        // terme, twins 300 ms, pas de retour des mobs.
        await MeasureSubsystemAsync("scheduled",
            FireScheduledAsync);

        // 2. Tick combat (100 ms) — catégorie 2 : poursuite
        // et riposte des mobs, un pas par mob en combat. Les
        // mutations (mob.X/Y, HP) tournent sur le fil unique.
        await MeasureSubsystemAsync("mob_combat", async () =>
        {
            try { await _flow.TickMobCombatsAsync(); }
            catch (Exception ex)
            {
                EdenLog.Write(
                    $"[ZoneLoop] TickMobCombats : {ex.Message}");
            }
        });
        // 2b. Détection passive (CAI::DoSeekEnemy natif) —
        // les mobs agressifs cherchent une cible toutes les
        // ~2 s (timer par mob, le tick ne fait que scanner).
        await MeasureSubsystemAsync("passive_aggro", async () =>
        {
            try { await _flow.TickPassiveAggroAsync(); }
            catch (Exception ex)
            {
                EdenLog.Write(
                    $"[ZoneLoop] TickPassiveAggro : {ex.Message}");
            }
        });

        // 3. Tick lent (1 s) — catégorie 1 : respawn des mobs
        // tués + cycle des trésors au sol (CEventTreasure
        // natif : fenêtre propriétaire → public → despawn).
        if (++_tickCount % SlowTickEvery == 0)
            await MeasureSubsystemAsync("slow_tick", SlowTickAsync);
    }

    private static async Task MeasureSubsystemAsync(
        string subsystem, Func<Task> work)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { await work().ConfigureAwait(false); }
        finally { RecordSubsystem(subsystem, started); }
    }

    private static void RecordSubsystem(string subsystem, long started) =>
        EdenMetrics.ZoneSubsystemMs.Record(
            System.Diagnostics.Stopwatch.GetElapsedTime(started)
                .TotalMilliseconds,
            new KeyValuePair<string, object?>("subsystem", subsystem));

    /// <summary>Tick à la seconde — trésors au sol. Chaque bloc est
    /// borné : une exception ne doit ni tuer la boucle ni remonter
    /// sur un thread pool (l'ancien Elapsed arrêtait le process).
    /// Le respawn des mobs ne passe PAS par un scan : chaque kill
    /// planifie son propre événement <see cref="Schedule"/> — le
    /// `Monsters.TickRespawns` historique (scan de toutes les
    /// instances MonsterEntity dont aucune ne meurt jamais en live,
    /// ~17k entrées/seconde gaspillées) a été retiré 2026-10-26.</summary>
    private async Task SlowTickAsync()
    {
        try { await _flow.TickTreasuresAsync(); }
        catch (Exception ex)
        { EdenLog.Write($"[ZoneLoop] TickTreasures : {ex.Message}"); }
        // World boss / mutants (plan 12) : planificateur + expirations.
        try { await _flow.TickWorldBossAsync(); }
        catch (Exception ex)
        { EdenLog.Write($"[ZoneLoop] TickWorldBoss : {ex.Message}"); }
        // Registre d'enchants mob — même cadence 1 s que les statuts
        // joueur (DoT, verrous, expiration → 300 aux témoins).
        try { await _flow.TickMobEnchantsAsync(); }
        catch (Exception ex)
        { EdenLog.Write($"[ZoneLoop] TickMobEnchants : {ex.Message}"); }
    }

    /// <summary>Complète la file et arrête la boucle après les
    /// commandes en attente.</summary>
    public async ValueTask DisposeAsync()
    {
        _commands.Writer.TryComplete();
        _cts.Cancel();
        try { await _run.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
