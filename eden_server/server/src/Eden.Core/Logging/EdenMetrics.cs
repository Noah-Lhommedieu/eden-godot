using System.Diagnostics.Metrics;

namespace Eden.Core.Logging;

/// <summary>Métriques runtime du serveur — <c>Meter "Eden.Server"</c>
/// consultable via <c>dotnet-counters monitor</c> ou un exporteur
/// OpenTelemetry. Compteurs incrémentés aux points chauds réseau et
/// persistance ; jauges par callbacks (profondeurs de files,
/// sessions actives).</summary>
public static class EdenMetrics
{
    /// <summary>Meter global du serveur.</summary>
    public static readonly Meter Meter = new("Eden.Server", "1.0.0");

    /// <summary>Scope DB actif propagé aux awaits/tasks enfants —
    /// permet de rattacher chaque mesure Npgsql à une opération logique
    /// sans instrumenter les repositories un à un.</summary>
    private static readonly AsyncLocal<DbOperationScope?>
        CurrentDbOperation = new();

    /// <summary>Écoute interne des métriques Npgsql. Elle est active
    /// même sans exporteur HTTP et permet à <see cref="DbOperationScope"/>
    /// de compter les commandes SQL unitaires du scope.</summary>
    private static readonly MeterListener NpgsqlCommandListener =
        CreateNpgsqlCommandListener();

    /// <summary>Paquets entrants (par service).</summary>
    public static readonly Counter<long> PacketsIn =
        Meter.CreateCounter<long>("eden_packets_in",
            description: "Paquets NCID reçus");

    /// <summary>Paquets sortants (par service).</summary>
    public static readonly Counter<long> PacketsOut =
        Meter.CreateCounter<long>("eden_packets_out",
            description: "Trames envoyées");

    /// <summary>Octets sortants.</summary>
    public static readonly Counter<long> BytesOut =
        Meter.CreateCounter<long>("eden_bytes_out",
            description: "Octets écrits sur les sockets");

    /// <summary>Durée des ticks de boucle de zone (ms).</summary>
    public static readonly Histogram<double> ZoneTickMs =
        Meter.CreateHistogram<double>("eden_zone_tick_ms",
            unit: "ms", description: "Durée d'un tick de ZoneLoop");

    /// <summary>Durée par sous-système de tick (tag subsystem).</summary>
    public static readonly Histogram<double> ZoneSubsystemMs =
        Meter.CreateHistogram<double>("eden_zone_subsystem_ms",
            unit: "ms",
            description: "Durée d'une phase de ZoneLoop (tag subsystem)");

    /// <summary>Délai file réseau → début de dispatch (ms).</summary>
    public static readonly Histogram<double> PacketDispatchDelayMs =
        Meter.CreateHistogram<double>("eden_packet_dispatch_delay_ms",
            unit: "ms",
            description: "Délai entre réception d'une trame et son dispatch (tag ncid)");

    /// <summary>Durée de traitement d'un NCID (ms, tag ncid/path).</summary>
    public static readonly Histogram<double> PacketHandleMs =
        Meter.CreateHistogram<double>("eden_packet_handle_ms",
            unit: "ms",
            description: "Durée de traitement d'un NCID (tags ncid, path)");

    /// <summary>Délai d'attente d'une commande dans ZoneLoop (ms).</summary>
    public static readonly Histogram<double> ZoneCommandDelayMs =
        Meter.CreateHistogram<double>("eden_zone_command_delay_ms",
            unit: "ms",
            description: "Délai entre enqueue et exécution d'une commande ZoneLoop");

    /// <summary>Latence des opérations de persistance (ms).</summary>
    public static readonly Histogram<double> DbOpMs =
        Meter.CreateHistogram<double>("eden_db_op_ms",
            unit: "ms", description: "Latence d'une op DB enfilée");

    /// <summary>Durée d'une opération DB nommée (tag operation).</summary>
    public static readonly Histogram<double> DbOperationMs =
        Meter.CreateHistogram<double>("eden_db_operation_ms",
            unit: "ms",
            description: "Durée d'une opération DB logique (tag operation)");

    /// <summary>Nombre d'opérations DB logiques (tag operation).</summary>
    public static readonly Counter<long> DbOperations =
        Meter.CreateCounter<long>("eden_db_operations",
            description: "Opérations DB logiques exécutées (tag operation)");

    /// <summary>Commandes SQL exécutées dans un scope logique.</summary>
    public static readonly Counter<long> DbScopedQueries =
        Meter.CreateCounter<long>("eden_db_queries",
            description: "Commandes SQL mesurées dans un scope DB logique");

    /// <summary>Durée unitaire des commandes SQL dans un scope (ms).</summary>
    public static readonly Histogram<double> DbScopedQueryMs =
        Meter.CreateHistogram<double>("eden_db_query_ms",
            unit: "ms",
            description: "Durée d'une commande SQL dans un scope DB logique");

    /// <summary>Nombre de commandes SQL par opération logique.</summary>
    public static readonly Histogram<long> DbOperationQueries =
        Meter.CreateHistogram<long>("eden_db_operation_queries",
            unit: "{query}",
            description: "Nombre de commandes SQL d'une opération DB logique");

    /// <summary>Temps SQL cumulé par opération logique (ms).</summary>
    public static readonly Histogram<double> DbOperationQueryMs =
        Meter.CreateHistogram<double>("eden_db_operation_query_ms",
            unit: "ms",
            description: "Temps SQL cumulé d'une opération DB logique");

    /// <summary>Déconnexions anormales (par cause).</summary>
    public static readonly Counter<long> AbnormalDisconnects =
        Meter.CreateCounter<long>("eden_disconnects",
            description: "Sessions closes (tag = cause)");

    /// <summary>Trames perdues par file d'envoi pleine en mode
    /// non bloquant (client lent → session fermée ensuite).</summary>
    public static readonly Counter<long> SendQueueDrops =
        Meter.CreateCounter<long>("eden_send_queue_drops",
            description: "Trames abandonnées sur file d'envoi pleine");

    /// <summary>Enregistre une jauge de profondeur de file d'envoi.</summary>
    public static ObservableGauge<int> ObserveSendQueue(Func<int> sample) =>
        Meter.CreateObservableGauge("eden_send_queue_depth", sample,
            description: "Trames en attente dans la file d'envoi");

    /// <summary>Enregistre une jauge de sessions actives.</summary>
    public static ObservableGauge<int> ObserveSessions(Func<int> sample) =>
        Meter.CreateObservableGauge("eden_sessions_active", sample,
            description: "Sessions TCP actives");

    /// <summary>Enregistre une jauge de profondeur de persistance.</summary>
    public static ObservableGauge<int> ObservePersistenceQueue(
        Func<int> sample) =>
        Meter.CreateObservableGauge("eden_persistence_queue", sample,
            description: "Opérations DB en attente");

    /// <summary>Enregistre une jauge de commandes en attente dans ZoneLoop.</summary>
    public static ObservableGauge<int> ObserveZoneCommandQueue(
        Func<int> sample) =>
        Meter.CreateObservableGauge("eden_zone_command_queue", sample,
            description: "Commandes en attente dans ZoneLoop");

    /// <summary>Mesure une opération DB logique et la compte par nom.</summary>
    public static DbOperationScope BeginDbOperation(string operation) =>
        new(operation);

    /// <summary>Active la remontée interne des durées de commandes
    /// Npgsql vers le scope AsyncLocal. Npgsql 10 émet
    /// <c>db.client.operation.duration</c> ; l'ancien nom
    /// <c>db.client.commands.duration</c> est conservé pour les builds
    /// antérieurs.</summary>
    private static MeterListener CreateNpgsqlCommandListener()
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Npgsql" &&
                (instrument.Name == "db.client.operation.duration" ||
                 instrument.Name == "db.client.commands.duration"))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
                CurrentDbOperation.Value?.NoteQuery(measurement));
        listener.Start();
        return listener;
    }

    /// <summary>Scope RAII d'une opération DB logique (entry, kill…).</summary>
    public sealed class DbOperationScope : IDisposable
    {
        private readonly string _operation;
        private readonly DbOperationScope? _parent;
        private readonly object _gate = new();
        private readonly long _started;
        private long _queries;
        private double _queryMs;

        internal DbOperationScope(string operation)
        {
            _operation = operation;
            _parent = CurrentDbOperation.Value;
            CurrentDbOperation.Value = this;
            _started = System.Diagnostics.Stopwatch.GetTimestamp();
            DbOperations.Add(1,
                new KeyValuePair<string, object?>("operation", operation));
        }

        /// <summary>Compte une commande SQL Npgsql mesurée dans ce
        /// scope. Les tags Npgsql (pool/serveur) ne sont pas copiés pour
        /// éviter une cardinalité inutile.</summary>
        internal void NoteQuery(double seconds)
        {
            var ms = seconds * 1000d;
            var tag = new KeyValuePair<string, object?>(
                "operation", _operation);
            DbScopedQueries.Add(1, tag);
            DbScopedQueryMs.Record(ms, tag);
            lock (_gate)
            {
                _queries++;
                _queryMs += ms;
            }
        }

        public void Dispose()
        {
            if (ReferenceEquals(CurrentDbOperation.Value, this))
                CurrentDbOperation.Value = _parent;
            var tag = new KeyValuePair<string, object?>(
                "operation", _operation);
            long queries;
            double queryMs;
            lock (_gate)
            {
                queries = _queries;
                queryMs = _queryMs;
            }
            DbOperationQueries.Record(queries, tag);
            DbOperationQueryMs.Record(queryMs, tag);
            DbOperationMs.Record(
                System.Diagnostics.Stopwatch.GetElapsedTime(_started)
                    .TotalMilliseconds,
                tag);
        }
    }
}
