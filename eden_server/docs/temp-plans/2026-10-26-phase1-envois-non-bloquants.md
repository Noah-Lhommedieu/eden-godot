# Phase 1 — lot « envois non bloquants sur la boucle acteur » (anti-livelock)

Date : 2026-10-26. Suit `docs/MASTER-IMPLEMENTATION-PLAN.md` et le plan de
refonte `eden_godot/docs/plan-refonte_architecture-complet.md` §Phase 1
(items « client lent TryWrite + déconnexion propre » et « supprimer les awaits
séquentiels par destinataire »).

## Symptômes mesurés (baseline 2026-10-26)

- Burst de 200 entrées : ZoneLoop **figée ~6 min**, file ~208 commandes,
  drain ~1 cmd/min, processus tué au SIGKILL.
- `dumpasync` (`docs/baselines/2026-10-26-zoneloop-hang-stack.txt`) : la boucle
  est garée dans `HandleMigrationDoneAsync → SpawnSceneEntitiesAsync →
  UpdateSightAsync → AnnounceSelfToWatchersAsync → NetSession.SendFrameAsync`.
- Mécanisme : ~70 sessions dont le client bot est mort en cours d'entrée
  restent dans `ZoneSessionManager._sessions` (leur reader est parqué en
  `await` d'une commande postée sur la boucle → jamais de `ReadAsync` → le
  FIN n'est pas vu → session jamais retirée). Chaque `SendFrameAsync` vers
  leur file pleine attend `SendTimeout` (5 s) sur le fil acteur :
  ~70 × 5 s ≈ 6 min **pour une seule commande**, et ~200 autres attendent.

## Changement

1. `NetSession` : nouveau mode `DropOnFullQueue` (volatile, défaut `false`).
   Dans `SendFrameAsync`, quand le mode est actif :
   - `TryWrite` non bloquant ; succès → `_globalQueued++` comme avant ;
   - échec → rend le buffer `ArrayPool`, `CloseReason ??= "backlog d'envoi…"`,
     `eden_send_queue_drops++`, puis `AbortSendSide()` :
     `_sendQueue.Writer.TryComplete()` + `_sendCts.Cancel()` +
     `Socket.Shutdown(Both)` → le reader reçoit EOF → sortie par le chemin
     normal (`finally` → `OnDisconnectAsync` + `Sessions.Remove` +
     `DisposeAsync`). Aucun `await`, aucun dispose inline.
   - Le chemin par défaut (`DropOnFullQueue=false`, attente bornée
     `SendTimeout`) reste inchangé : handshake/accept des autres services.
2. `EdenConfig` : `EDEN_SEND_QUEUE_CAPACITY` (défaut 256) — la capacité
   devient un `static readonly` au lieu d'un `const` (tuning sans rebuild).
3. `Program.cs` (zone) : `session.DropOnFullQueue = true` à
   `Sessions.Add` — toutes les sessions zone passent en envoi non bloquant.
   La boucle acteur ne peut plus attendre un destinataire.
4. `EdenMetrics` : compteur `eden_send_queue_drops` (trames perdues par
   backpressure — complète `eden_disconnects{reason}` déjà en place).

## Points d'édition

- `server/src/Eden.Core/Networking/Session/NetSession.cs`
- `server/src/Eden.Core/EdenConfig.cs`
- `server/src/Eden.Core/Logging/EdenMetrics.cs`
- `server/src/Eden.Zone/Program.cs` (`Sessions.Add`)
- `server/tests/Eden.Tests/SendQueueTests.cs` (nouveaux cas)

## Tests

- [x] Queue pleine (send buffer socket minuscule + peer qui ne lit pas) :
      `SendPacketAsync` revient instantanément, `CloseReason` renseigné,
      envois suivants no-op (<500 ms). `SendQueue_DropOnFullQueue_
      ClosesWithoutBlocking` vert.
- [x] Mode par défaut inchangé : tests existants verts (suite complète
      1112/1112).
- [x] Preuve live — même burst 200-entry que le livelock :
      **200/200 entrées, 0 erreur, entry avg 321 ms / max 438 ms**
      (`2026-10-26-bots-200-entry-nondrop-fix.json`), file de commandes
      vidée en temps réel (0 résiduel vs ~208 figés), ticks p99 ≤5 ms,
      200 déconnexions « normale », 0 trame droppée nécessaire en
      loopback. Avant le fix : 129/200 en 10 s puis boucle figée ~6 min.

## Hors scope de ce lot

- Budget de temps par itération / tick 33-50 ms, `NoDelay`/heartbeat,
  broadcasts scopés par (node,inst), sérialisation unique des broadcasts,
  `PacketReceiveBuffer` sur les liens, file de priorité respawn,
  chargement d'entrée hors boucle (Phase 2).
