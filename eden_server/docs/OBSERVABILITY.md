# Observabilité Eden — Phase 0

## Endpoint applicatif commun

Chaque service expose, quand `EDEN_OBSERVABILITY_PORT > 0` :

- `GET /metrics` — export Prometheus OpenTelemetry ;
- `GET /health` — JSON `{ service, status, uptimeSeconds, checks }`, code `503`
  si une dépendance obligatoire (PostgreSQL quand le service en a une) échoue.

Variables :

- `EDEN_OBSERVABILITY_PORT=0` — désactivé par défaut ;
- `EDEN_OBSERVABILITY_BIND=http://127.0.0.1` — bind local par défaut ; utiliser
  `http://0.0.0.0` dans un conteneur ou pour Prometheus distant.

Ports docker-compose actuels :

| Service | Jeu | Observabilité |
|---|---:|---:|
| Gateway | 5560 | 9091 |
| Login | 6543 | 9092 |
| World | 5200 | 9093 |
| Zone | 5201 | 9094 |
| Mission | 5202 | 9095 |
| Ticket | 2067 | 9096 |

Exemple local sans compose :

```bash
GATEWAY_PORT=15560 EDEN_OBSERVABILITY_PORT=19091 \
  dotnet run --project server/src/Eden.Gateway/Eden.Gateway.csproj
curl http://127.0.0.1:19091/health
curl http://127.0.0.1:19091/metrics
```

## Métriques `Eden.Server`

Instruments customs branchés :

- `eden_packets_in_total`, `eden_packets_out_total`, `eden_bytes_out_total` ;
- `eden_disconnects_total{reason=...}` ;
- `eden_send_queue_depth`, `eden_sessions_active`,
  `eden_persistence_queue`, `eden_zone_command_queue` ;
- `eden_zone_tick_ms` ;
- `eden_zone_subsystem_ms{subsystem=commands|scheduled|mob_combat|passive_aggro|slow_tick}` ;
- `eden_zone_command_delay_ms` — attente file → exécution `ZoneLoop` ;
- `eden_packet_dispatch_delay_ms{ncid=...}` — réception → début handler ;
- `eden_packet_handle_ms{ncid=...,path=zone_flow|dispatcher}` ;
- `eden_db_op_ms{operation=...}` ;
- `eden_db_operations_total{operation=...}` ;
- `eden_db_operation_ms{operation=zone_entry_ticket|zone_entry_burst|mob_kill|persistence_queue}` ;
- `eden_db_queries_total{operation=...}` — commandes SQL unitaires dans un scope ;
- `eden_db_query_ms{operation=...}` — durée unitaire des commandes scoped ;
- `eden_db_operation_queries{operation=...}` — distribution du nombre de requêtes
  par entrée/kill ;
- `eden_db_operation_query_ms{operation=...}` — temps SQL cumulé par scope.

L'instrumentation runtime .NET est également exportée (`dotnet_gc_*`,
`dotnet_jit_*`, `dotnet_process_*`, pool de threads). `Npgsql` est inclus
dans les meters écoutés.

## Outils runtime .NET

Les `DOTNET_EnableDiagnostics=0` ont été retirés des Dockerfiles : les outils
de diagnostic fonctionnent en dev.

```bash
# Compteurs live (processus en cours)
dotnet-counters monitor --process-id <pid> \
  --counters Eden.Server,System.Runtime

# Trace événements + compteurs
dotnet-trace collect --process-id <pid> \
  --providers Microsoft-DotNETCore-SampleProfiler,Eden.Server

# Dump GC sous charge
dotnet-gcdump collect --process-id <pid> -o /tmp/eden.gcdump
```

## PostgreSQL — `pg_stat_statements`

`docker-compose.yml` précharge `pg_stat_statements` et monte
`Database/020-pg-stat-statements.sql`, qui crée l'extension dans `postgres`,
`fndb1`, `fnaccount` et `fnmember`.

Requêtes utiles :

```sql
SELECT calls, mean_exec_time, max_exec_time, rows, query
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 20;
```

## Preuve du lot

- `dotnet build server/Eden.sln` : 0 erreur / 0 warning ;
- Gateway lancé avec `GATEWAY_PORT=15560 EDEN_OBSERVABILITY_PORT=19091` ;
- `/health` : `{"service":"gateway","status":"healthy","checks":{"process":"ok"}}` ;
- `/metrics` : métriques runtime .NET + `eden_disconnects_total` et
  `eden_send_queue_depth` après une connexion TCP de test.
