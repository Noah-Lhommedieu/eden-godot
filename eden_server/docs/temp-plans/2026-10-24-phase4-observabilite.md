# Phase 4 — Observabilité et robustesse

## P4.1 — ILogger par catégorie
- `Eden.Core.Logging.EdenLog` : LoggerFactory unique (SimpleConsole,
  horodatage), `ILogger` par catégorie dérivée du préfixe `[Tag]` des
  messages existants (Zone, Combat, Net, Sight, DB, Login…).
- Migration mécanique `Console.WriteLine("[Tag] …")` → `EdenLog.Write`
  (471 sites, 76 fichiers) — les call sites gardent leur texte, le tag
  devient la catégorie. Warning/Error : préfixes `WARN`/`FAIL`/`error`.
- Hors scope : messages REPL/debug déjà sous `NetDebug.Enabled`.

## P4.2 — System.Diagnostics.Metrics
- `EdenMetrics` (Meter "Eden.Server") : packets_in/packets_out,
  send_queue_depth, sessions_active, zone_tick_ms (histogram),
  db_query_ms (histogram), persistence_queue_depth.
- Câblage : NetSession (in/out + profondeur file), NetSession.Accept
  (sessions), ZoneLoop (tick), PersistenceQueue (file), repos (latence
  via wrapper optionnel — pas d'instrumentation par requête individuelle
  si coûteux : compter seulement).
- Consultable via `dotnet-counters monitor --name Eden.Zone`.

## P4.3 — Rate limiting
- Entrant : token bucket par session (ex. 100 paquets/s, burst 200) →
  dépassement = déconnexion propre + log. Configurable env.
- Login : limite par IP sur tentatives (ex. 5/min) dans le service Login.

## P4.4 — Config typée
- `EdenConfig` : ~15 env vars existantes (DB_*, EDEN_*, ports) lues une
  fois, valeurs par défaut documentées, chargée au démarrage des services.

## Critères
- 0 changement de comportement observable (mêmes textes de log).
- Tests verts à chaque commit ; métriques vérifiables en test.

## Bilan final — Phase 4 clôturée (2026-10-24)
| Sous-phase | Statut | Commit |
|---|---|---|
| 4.1 EdenLog (471 sites) | ✅ | c943101 |
| 4.2 EdenMetrics + file bornée | ✅ | 1dca63d |
| 4.3 Rate-limit in + login/IP | ✅ | 8beb7b0 |
| 4.4 EdenConfig (~35 vars, 13 fichiers) | ✅ | 8beb7b0 |

Build 0 warn / 0 err — 687 tests verts à chaque jalon.
Résidu assumé : `GameBalance` conserve `GetEnvironmentVariables()` pour les
clés dynamiques `EDEN_BAL_*`/`EDEN_BAL_PARAM_*` (impossibles à typer) ;
`EdenLog` lit `EDEN_LOG_LEVEL` en interne (bootstrap du logging).
