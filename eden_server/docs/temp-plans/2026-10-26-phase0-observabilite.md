# Plan temporaire — Phase 0 observabilité/baseline (2026-10-26)

## Objectif
Rendre la baseline répétable avant toute optimisation : métriques runtime + `Eden.Server`, endpoint Prometheus, healthchecks légers, puis banc d'essai/mesures.

## Étapes
- [x] Aligner `AGENTS.md` serveur/client sur le périmètre Godot-only.
- [x] Ajouter une couche commune `Eden.Core/Observability` :
  - [x] `MeterProvider` OpenTelemetry avec meter `Eden.Server` + runtime .NET ;
  - [x] listener HTTP `/metrics` compatible Prometheus ;
  - [x] listener `/health` JSON (processus + dépendance optionnelle).
- [x] Brancher l'exporter dans Zone, World, Login, Mission, Gateway, Ticket.
- [x] Compléter les instruments `EdenMetrics` :
  - [x] durée par sous-système `ZoneLoop` ;
  - [x] latence file→dispatch et durée par NCID ;
  - [x] compteurs opérations DB logiques par entrée/kill ;
  - [x] jauges files sessions déjà présentes conservées.
- [x] Retirer `DOTNET_EnableDiagnostics=0` des Dockerfiles dev et documenter `dotnet-counters`/`dotnet-trace`/`dotnet-gcdump`.
- [x] Activer/documenter `pg_stat_statements` côté PostgreSQL.
- [x] Build solution + tests serveur.
- [x] Mesure/capture d'un endpoint `/metrics` et `/health` sur au moins un service lancé.
- [x] Réparer la résolution Reforged du layout `portage/eden_server`
      (`eden_server/reforged` en lecture seule) — `MountTalentTests` 5/5.
- [x] Mettre à jour `eden_godot/docs/plan-refonte_architecture-complet.md`.
- [ ] Banc d'essai C# `server/tools/Eden.Bots` :
  - [x] transport client RSA/RC4 + ticket frais + rejeu du burst d'entrée capturé ;
  - [x] résolution DB des `botcharNNN` créés par `Eden.Admin` ;
  - [x] scénarios `entry`, `move`, `combat`, `spell`, `loot`, `aoe`, `mixed` ;
  - [x] exécution multi-sessions séparable du processus serveur ;
  - [x] rapport JSON/console des entrées, paquets, erreurs et durées ;
  - [x] validation live : `botchar001` `move`/`mixed`, 10 sessions `mixed`,
        50 entrées simultanées (`46/50` `473` sous 8 s — backlog d'entrée mesuré) ;
  - [x] validation 200 sessions + scénarios groupés/loot réels
        (`2026-10-26-bots-200-entry-v2.json` : 129/200 entrées en 10 s puis
        **livelock ZoneLoop ~6 min**, preuve `2026-10-26-zoneloop-hang-stack.txt` ;
        `2026-10-26-bots-20-aoe-proximity.json` : kills réels ×4, `274` loot
        inventaire ×4, `600` exp ×4 — trésor au sol non exercé, drops arrivant
        directement en inventaire) ;
  - [x] ciblage combat par proximité (positions NCID 298/326, approche avant
        attaque) — remplace le ciblage aléatoire hors portée.

## Contraintes
- Pas de Generic Host/refactor majeur dans ce lot : les `Program.cs` top-level restent.
- Pas de modification des oracles natifs/Reforged.
- Endpoint métriques/health configurable par env vars, désactivable.
