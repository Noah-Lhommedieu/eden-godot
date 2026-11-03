# DB-4 — test de charge ~300 CCU (2026-11-01)

Mesure du serveur après DB-2 (base unique `edenfr`) et DB-3 (Redis), PG seul vs Redis,
sur la même machine (client, serveurs, PG et Redis cohabitent : valeurs **relatives**).

## Méthode (`eden_server/tools/load-test.sh <pg|redis>`)

Pile **isolée** (n'interfère pas avec `edenflax-*`) : PG17 jetable `edenflax-loadpg` :5434
(`max_connections=300`, copie `pg_dump` de `edenfr`), Ticket/Mission/World/Zone natifs sur ports +10000,
Redis base logique 15, 300 personnages `botchar0001..0300` clonés. Deux étages :

1. **Bots de bout en bout** — `server/tools/Eden.Bots` (client serveur-side du dépôt : ticket → zone → rejeu
   d'entrée → mouvements/attaques/sorts) : 300 sessions, 60 s, 2 actions/s, scénario `mixed`.
   *Note : `tools/HeadlessBot` (racine) ne convient pas — il pilote 1 session `duel-accept`/`mail-check`
   via le noyau client `eden_flax/`, hors périmètre.*
2. **Micro-bench** — `server/tools/Eden.HotBench` : 300 joueurs concurrents × 200 opérations, latence client
   par appel, PG direct vs Redis, mêmes workers.

## Résultats micro-bench (latence ms, 60 000 appels/ligne ; 2 runs, valeurs du run Redis | run PG)

| Opération chaude | Voie | p50 | **p95** | p99 | débit |
|---|---|---|---|---|---|
| Or `AddGoldAsync` | PG direct | 15,8 | **21–22** | 26 | 17,5 k/s |
| Or `AddGoldAsync` | Redis (Lua) | 3,0 | **4,7–4,8** | 9–10 | 90 k/s |
| Position `UpdateCoordinatesAsync` | PG direct | 12,4–12,9 | **16–19** | 18–24 | 22–24 k/s |
| Position | Redis (fire-and-forget) | 0,00 | **0,01–0,05** | 0,1–0,6 | 600–640 k/s |
| Ticket login émission+consommation | PG | 25–26 | **35–40** | 44–50 | 11 k/s |
| Ticket login émission+consommation | Redis | 1,4–1,8 | **3,5–7,8** | 7–12 | 119–178 k/s |
| Flush Redis→PG de 300 persos sales | — | — | — | — | 0,55–0,78 s (1,8–2,6 ms/perso) |

→ or ≈ 4,5× plus rapide au p95, position ≈ 1000×, tickets ≈ 5–10×. Le flush complet des 300 persos coûte < 1 s toutes les 15 s.

## Résultats bout en bout (300 sessions, 60 s)

| | PG seul | Redis |
|---|---|---|
| Entrées réussies | 300/300 | 300/300 |
| Entrée zone (ms) moy / max | 158 / 737 | 171 / 744 |
| Mouvements envoyés | 35 552 | 35 797 |
| Sessions coupées (« Broken pipe ») | 4 | 2 |
| Appels SQL totaux (pg_stat_statements) | 183 390 | 181 261 |
| `UPDATE … SET node_id…` (position) : appels / moy / max | 791 / 1,37 ms / 10 ms | 468+300 / 0,09–0,10 ms / 0,6 ms |

Lecture honnête : la position était **déjà** en write-behind mémoire (15 s) avant DB-3, donc le nombre d'UPDATE
reste du même ordre ; le gain côté PG est la latence par UPDATE (flush batché Redis→PG : 1,4 ms → 0,09 ms moyen,
max 10 → 0,6 ms) et surtout la sortie du hot-path (or, tickets) du moteur relationnel. Les bots ne ramassent pas
d'or (`lootAttempts=0`) : le gain or n'est mesuré que par le micro-bench. Le bout en bout démontre l'**absence de régression**
et le fonctionnement complet (Redis tickets/hot + flush) à 300 CCU.

## Constats à traiter

1. **`max_connections` PG** — à 300 CCU, 3 pools Npgsql par process (game/account/member, défaut 100 chacun)
   × Zone/World/Mission saturent les 100 connexions du PG par défaut : `53300 too many clients` ×59 dans
   le Zone (première exécution, abandon d'entrée de plusieurs sessions). Corrections livrées : `EDEN_DB_POOL_MAX`
   (plafond par pool, 30 dans le test) et `-c max_connections=300` dans `docker-compose.yml` (effectif au
   prochain `up` du conteneur). Pic observé après correction : 105 connexions, 0 erreur 53300.
2. **« Broken pipe » 2–4 sessions/300** dans les deux modes (donc non liée à la DB ni à Redis) : cause non
   investiguée — piste : fermeture serveur d'une session en rafale d'entrée (limites par session/backlog).
3. **Limites DB-3 assumées** : or/position/vitals seulement. Lectures hors-zone (Mission lit `gold`, `node_id`
   en PG) peuvent retarder jusqu'à 15 s ; les écrivains PG directs (courrier, échange, enchères, Mission)
   restent corrects car le delta d'or commute avec `gold = gold + d`. Flush forcé à la déconnexion et avant
   migration de zone (`UpdateCoordinatesDurableAsync`).

Données brutes : `eden_server/docs/load-results/{pg,redis}/` (`bots-summary.json`, `hotbench.{txt,json}`,
`pgss.txt`, `pgconn-max.txt`).
