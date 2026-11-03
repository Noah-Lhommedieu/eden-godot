# Rapport de baseline Phase 0 — 2026-10-26

Mesures prises avec la nouvelle instrumentation (`Eden.Server` +
export `/metrics` :9094 zone, :9093 world, :9095 mission, :9096 ticket,
:9091 gateway, :9092 login) et l'essaim `server/tools/Eden.Bots`.

Machine : i5-12450H / 16 Go / RTX 4060 Laptop 8 Go, Wi-Fi, Nobara,
build dev `net10.0`, PostgreSQL 12 local (conteneur), services
`dotnet run` locaux. Bots et serveur sur la même machine (loopback) —
les latences réseau sont donc un plancher optimiste.

Fichiers de preuve dans `docs/baselines/2026-10-26-*.{json,txt}`.

---

## 1. Entrée de session — mesures

| Run | Sessions | Entrées | entryMs min/avg/max | Preuve |
|---|---:|---:|---|---|
| 50-entry froid (JIT) | 50 | 50/50 | 6 / 91 / 337 ms | `bots-50-entry-cold.json` |
| 50-entry chaud | 50 | 50/50 | 7 / 27 / 93 ms | `bots-50-entry-warm.json` |
| 200-entry burst | 200 | 129/200 en 10 s | 25 / 6 203 / 10 006 ms | `bots-200-entry-v2.json` |
| 10-mixed | 10 | 10/10 | 90 / 152 / 205 ms | `bots-10-mixed-v2.json` |

Le run `bots-200-entry.json` (0/200) d'hier était une mesure tronquée :
aucune trame reçue car la file de commandes n'a jamais débité dans la
fenêtre — pas un rejet, un backlog pur (voir §4).

### Coût DB par entrée (`CNC_T_ClientUseTicket`, NCID 0)

| Charge | Ops | Requêtes SQL total | Req/entrée | Durée op |
|---|---:|---:|---:|---:|
| Légère (10-20 sessions) | 50 | 1 820 | **36,4** | — |
| Burst 300 entrées | 300 | 61 599 | **205** | sum 32,1 s (~107 ms/op nominal, **avg handle 7,3 s** sous burst) |

La première mesure de ~600 requêtes/entrée est corrigée : **~36 en
charge légère, ~205 constatées sous contention** (retries/pool — à
creuser en Phase 2). La durée unitaire SQL reste ~0,5 ms ; le problème
est le volume séquentiel + la contention du pool Npgsql (~100 conn.
par défaut) : sous burst 200, `eden_packet_handle_ms{ncid=0}` atteint
**7,3 s en moyenne**.

### Dispatch delays sous burst 200

Les trames d'entrée replayées attendent derrière le backlog :
NCID 26 (EnterNodePlayerData) avg **15,7 s**, NCID 24 (EquipAndSpell)
15,2 s, NCID 53 (Shortcut) 14,6 s, NCID 104 12,0 s. `eden_zone_command_
delay_ms` : avg 526 ms sur 19 617 commandes. File `eden_zone_command_
queue` montée à ~210.

### JIT (preuve froid/chaud)

`dotnet_jit_*` : 13 091 méthodes compilées, 3,04 s de compilation cumulée
sur le boot + premier burst. Effet net sur l'entrée : **avg 91 ms →
27 ms (×3,4), max 337 → 93 ms** entre le 1er et le 2e burst 50. Le
burst à froid ne disparaît donc pas de lui-même — warm-up ou
ReadyToRun reste pertinent (Phase 6 §6 du plan).

### GC sous charge (300 entrées + combat, ~2 min)

gen0=262, gen1=77, **gen2=10** ; pause totale 0,241 s ; alloué 1,13 Go ;
327 exceptions (majoritairement déconnexions de bots). À suivre : gen2
non nul même à cette échelle.

---

## 2. Tick ZoneLoop

| Contexte | Distribution | Preuve |
|---|---|---|
| Idle + bursts 50/200 | p50 ≤5 ms ; 1 tick 100-250, 1 tick 250-500, **2 ticks 500 ms-2,5 s** | `zone-post-200.txt` |
| Combat 10-20 bots (sain) | 97,6 % ≤5 ms ; ~9 ticks 50-100 ms ; max ~500 ms (entrées) | scrape zone saine |

Le p95 « 9-12 ms à vide » est corrigé : à vide le tick est ≤5 ms ; les
pics viennent des entrées (et marginalement du slow tick), pas d'un
scan périodique — l'hypothèse « respawn ×1/10 » n'est pas confirmée
comme source dominante.

---

## 3. Chemin chaud NCID

| NCID | Rôle | n | avg handle | avg dispatch-delay |
|---:|---|---:|---:|---:|
| 0 | ClientUseTicket (entrée) | 300 | **7 300 ms** (burst 200) | 0 ms |
| 1 | MoveSelf | 6 925 | 16,3 ms | 930 ms |
| 5 | MeleeAttack | 6 893 | **48,5 ms** | — |
| 9 | SpellCastToLife | 307 | 49,1 ms | — |
| 57 | ZoneServerEcho | 1 206 | 1 828 ms | 2 132 ms |

MeleeAttack ~48 ms/handler : 2 broadcasts `BroadcastAllAsync`
**globaux et séquentiels** par attaque → confirme les items Phase 1
(broadcasts scopés par (node,inst), sérialisation unique, pas d'await
par destinataire).

---

## 4. LIVELOCK — preuve la plus importante du lot

Après le burst 200, la ZoneLoop s'est figée **>6 minutes** (ticks figés
à 855, file ~208, drain ~1 cmd/min), processus tué au SIGKILL.

`dumpasync` (`zoneloop-hang-stack.txt`) montre la boucle garée dans :

```
ZoneLoop.RunAsync → PostAsync cmd → TryHandleOnLoopAsync
 → HandleMigrationDoneAsync → SpawnSceneEntitiesAsync
 → UpdateSightAsync → AnnounceSelfToWatchersAsync
 → NetSession.SendPacketAsync → SendFrameAsync   ← await file pleine
```

Mécanisme : l'entrée d'un perso annonce son existence aux watchers
**séquentiellement** ; ~70 sessions sont mortes en cours d'entrée avec
des files d'envoi pleines (jusqu'à 256 trames de spawn accumulées) ;
chaque `SendFrameAsync` sur une file pleine attend le `SendTimeout`
(5 s) avant de disposer la session. ~70 × 5 s ≈ 6 min pour **une seule
commande** — et ~200 commandes similaires attendaient derrière.

Validation directe de trois items Phase 1 :

- « Gérer le client lent avec `TryWrite` + déconnexion propre,
  jamais d'await de 5 s sur le fil acteur » ;
- « supprimer les awaits séquentiels par destinataire » ;
- « budget de temps par itération » — ici un seul `HandleMigrationDone`
  a monopolisé la boucle ~6 min.

Le serveur est utilisable à 50 joueurs en entrée étalée mais **un burst
de 200 entrées provoque un livelock de plusieurs minutes** — à traiter
en priorité Phase 1.

---

## 5. Combat / kill / loot (scénarios bots)

Bots initiaux (ciblage aléatoire) : attaques émises mais hors portée →
`CanAttack` natif ne produit aucun coup (preuve : `410
CommandFailReturn` ×2968). Après ciblage par proximité (positions 298/
326 suivies, approche du mob le plus proche) :

| Run | Entrées | Dégâts réels | Kills | Loot |
|---|---:|---|---:|---|
| aoe ×20 (proximité) | 20/20 | `272 ClientMeleeAttack` ×539 | **4** (`305 ClientDie` ×4) | `274 PutItemIntoInventory` ×4, `600 Exp` ×4, `284 LevelUp` ×4, `286 GetSpell` ×19 |

Preuve : `bots-20-aoe-proximity.json`.

**Kill = 0 requête SQL synchrone mesurée** (`eden_db_operation_
queries{mob_kill}` = 0, op 2,6 ms) — les écritures passent déjà par la
`PersistenceQueue` (write-behind). L'estimation « 6-10 requêtes
synchrones/kill » du plan est donc fausse pour ce chemin ; Phase 3 se
concentrera surtout sur l'élargissement du write-behind, pas sa création.

Le loot au sol (`329 SeeTreasure`/`35 TreasurePickup`) n'a pas pu être
exercé : les drops de ces kills arrivent directement en inventaire
(274). Scénario trésor au sol à rejouer quand une zone à trésors sera
accessible aux bots.

---

## 6. Client (Godot, S002, live, sonde `--perf`)

| Métrique | Valeur | Note |
|---|---:|---|
| FPS moyen (75 s en jeu) | **211** | menu ~2000, en jeu 210-220 |
| Frame time en jeu | ~4,6 ms typique | spikes 40-66 ms épisodiques |
| Hitch max au spawn | **134,8 ms** | compilation shader probable |
| RSS pic | 3,74 Go | vs 2,8 Go baseline — fuites RID à la sortie toujours présentes |
| VRAM | **3,22 Go** (textures 3,10 Go) | moniteur `RenderVideoMemUsed` |
| Draw calls / objs / prims | 478 / 1 095 / 365 k | ville de spawn S002 |
| Nœuds scène | 16 993 (16 771 sous GameScene) | 3 020 MultiMeshInstance |
| Chargement S002 | **4 112 ms** | objects 2 186 + entities 1 434 + terrain/grass 409 + env 61 ; +41 ms GC |

Cible « map < 2 s sans gel » : ratée de ~2×. La fenêtre de chargement
bloque aussi le rendu (pas de frames sonde pendant la phase — gel
visuel). `cache=147tex/169glb` : seul le hot-set de la map est chargé.

---

## 7. Synthèse vs budgets cibles

| Cible Phase 5 | Mesuré | Verdict |
|---|---|---|
| Entrée <20 requêtes et <10 ms bloquants | ~36 req / ~107 ms (léger) ; ~205 req / 7,3 s (burst) | ❌ |
| Tick max <20 ms avec 50 entrées | 50/50 en ~93-337 ms ; ticks >500 ms | ❌ |
| Écho mouvement p50 <5 ms | MoveSelf handle 16,3 ms avg | ❌ |
| 50 entrées simultanées | 50/50 OK, entrées ~27-91 ms | ⚠️ ok à froid hors JIT |
| 200 entrées | 129/200 puis **livelock ~6 min** | ❌ critique |
| Kill synchrone | 0 requête, 2,6 ms op | ✅ déjà write-behind |
| Client 60 FPS | 211 FPS en ville | ✅ (marge) |
| Map <2 s | 4,1 s | ❌ |
| VRAM textures ÷4 | 3,1 Go textures | à traiter (DDS) |

## 8. Recommandations ordonnées (à arbitrer)

1. **Anti-livelock** (Phase 1) : jamais d'await de `SendFrameAsync`
   sur la boucle — `TryWrite` + déconnexion sur backlog ; annonces
   watchers sérialisées une fois, fan-out par destinataire hors await.
2. **Entrée** (Phase 2) : charger l'état perso hors boucle, batcher
   les ~36 requêtes (NpgsqlBatch), pool dimensionné au burst.
3. **Broadcasts** : scoper par (node,inst) + sérialisation unique —
   48 ms/attaque devient ~0.
4. **Client** : pipeline DDS/mipmaps (tex ÷4), chargement map asynchrone
   ou chunked (4,1 s → <2 s), shader pre-warm pour le hitch 134 ms.
5. **Warm-up JIT** / ReadyToRun pour le burst à froid (×3,4 mesuré).

## Limites de la mesure

- Bots ≠ clients réels : pas de rendu, actions synthétiques, positions
  simulées ; le chemin réseau est loopback (latence plancher).
- Pas encore de PvP/groupe réel, ni trésor au sol, ni persos HL :
  les botchars sont Lv~5 en ville S002.
- Une seule machine : la contention CPU client+bots+serveur+DB gonfle
  les queues respectives.
- GC/exceptions sous charge réelle client restent à re-mesurer après
  Phase 1.
