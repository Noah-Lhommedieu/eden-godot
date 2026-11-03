# Plan — Fiabilisation Phases 0 / 0-bis / 1 (22 oct. 2026)

Issue de `docs/AUDIT_PROJET_ETAT_ACTUEL.md`. Ordre fixé par le prompt.

## Phase 0 — gains rapides
- [x] `NetDebug.Enabled` static readonly ; remplacer les 8 lectures
      `Environment.GetEnvironmentVariable("EDEN_NET_DEBUG")`
      (NetSession ×3, ProfileStatEmitter, ZoneEntryFlow, Program,
      ZoneMissionLink).
- [x] Frame limit : `len < 2` dans `PacketReceiveBuffer.TryTakePacket`
      est aujourd'hui ignoré → boucle sans consommation (DoS). Rejeter.
      Timeout handshake (RSA/RC4 ≤ 10 s) + idle timeout session
      (`EDEN_IDLE_TIMEOUT_SEC`, défaut 300 s) + log de la cause.
- [x] Ring buffer 32 derniers paquets (dir/ncid/len) par session, dump
      sur déconnexion anormale/exception.
- [x] `devin mcp add eden-re` → reindex-mcp.py ; vérifier tools.

## Phase 0-bis — plateforme
- [x] Inventaire `dotnet list package --outdated|--vulnerable`.
- [x] `Directory.Packages.props` (CPM, versions inchangées).
- [x] Retarget `net10.0` (SDK 10 installé `~/.dotnet`) + warnings.
- [x] Maj packages test.
- [x] `NpgsqlDataSource` unique par service, injecté aux repos.
- [x] `Encoding.RegisterProvider` vérifié partout + test round-trip CP950.

## Phase 1 — harnais wire
- [x] Tests longueur/round-trip codecs ; régression NCID 271/444.
      ⚠️ Découverte : le code envoie `SendPacketAsync(271, 9B)` alors
      que la capture prouve natif 271=`ClientDisplayMonster` (corps
      variable ≥10B) et 444=`ClientGetSelectLifeTarget` (9B). Le corps
      9B émis sous 271 est un désync potentiel — corriger le NCID.
- [x] `tools/gen_golden_tests.py` : `captures/*.bin` (format
      `[ts] E|D ch=… NCID=n len=l hex`) → JSON par NCID → xunit.
- [x] `tools/eden_headless.py` : client RSA+RC4 rejouant un scénario.
- [x] `tools/wire_diff.py` : diff natif-vs-C# NCID/len + masques.
- [x] `.github/workflows/ci.yml` : postgres service + dotnet test.

## Phase 2 — concurrence et flux d'envoi (FAIT)

Commits : `c23fb14` `19147ce` `b880e92` `b65423d` `e96bf3f` `8166c5b`
`560aff9` puis `4648e41` `6240842` (routage acteur + fin des
fire-and-forget).

- [x] File d'envoi bornée `Channel` 256/session, SingleReader ; RC4
      dans le drain (ordre réel du fil) ; backlog > EDEN_SEND_TIMEOUT_MS
      → CloseReason ; SemaphoreSlim send-lock supprimé.
- [x] Zéro `GetAwaiter().GetResult()` dans src/.
- [x] `PersistenceQueue` (ordre par charId, exceptions loguées, flush)
      + `CharacterWriteBehind` (coords/areas dirty, flush 15 s + déco) ;
      14 écritures fire-and-forget migrées.
- [x] `ZoneLoop` : PeriodicTimer 100 ms + file de commandes bornée.
      Cat.1 respawn+trésors, cat.2 chase mobs, cat.3 casts schedulés.
- [x] **Critère strict atteint** : `TryHandleAsync` route le dispatch
      NCID via `RunOnLoopAsync` → toute mutation d'`EntryState` vit
      dans le flux de la boucle (callbacks services, liens
      mission/world, cleanup déconnexion inclus).
- [x] Fin des fire-and-forget mutants : regen/status/wander → steps
      auto-reschedulés ; `*AfterCast`×6 + works + gossip + corpse
      despawn + respawn → `ScheduleOnLoop` ; délais 300 ms du combat
      → phases schedulées (kill broadcast, mob spell hit, spellhit
      aggro→impact, life/nowhere release).
- [x] `_ =` → `await` là où des continuations post-await mutaient
      `st` en concurrence (ResolveWeapon, CastSequence).
- [x] `PostAsync` = sémantique de complétion (résout quand la commande
      a tourné, exception propagée) — requis pour l'ordonnancement
      du cleanup de déconnexion.
- [x] `IsOnLoopThread` via `AsyncLocal` (bug réel trouvé par les
      tests : les continuations de la boucle changent de thread à
      chaque await, un thread-id capturé ne pouvait jamais matcher).
- [x] Test de charge : client lent isolé (socket loopback réel).

Preuve : **655/655 tests verts** (dont 7 nouveaux `ZoneLoopActorTests` :
FIFO, non-concurrence, inline-vs-post, schedule, token annulé,
exception propagée).

### Reste à valider (humain, vrai client)
- Combat complet : chase visible, riposte, sortie propre, retour spawn.
- Cast temporisé : barre → blast à l'échéance → annulation par
  mouvement ; spam de sorts sans freeze.
- AFK >300 s → déco propre loguée ; client lent n'affecte pas les autres.
- Position exacte après déco/reco (write-behind).
