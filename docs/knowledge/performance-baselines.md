# Performances réseau / boucle de zone — lot N (2026-11-07)

Mesures sur la machine de développement (client, serveurs, PostgreSQL et Redis cohabitent : valeurs **relatives**). Méthode : `eden_server/tools/load-test.sh redis <N> <durée> 2 mixed`
(PG17 jetable + Redis, bots `Eden.Bots` serveur-side, scénario `mixed`, 2 actions/s/bot), résumé par `tools/perf/summarize-load.py`.
Percentiles estimés depuis les histogrammes Prometheus de la zone (`eden_zone_tick_ms`, `eden_zone_command_delay_ms`).

| Run | Entrées | Sessions coupées | Entrée zone ms (moy / max) | Tick de zone ms (p50 / p95 / p99) | Attente file de commandes ms (p50 / p95 / p99) | Pause GC cumulée | CPU zone % (moy / max) | RSS max Mo | Sortie Ko/s/session |
|---|---|---|---|---|---|---|---|---|---|
| 50 bots (45 s) | 50/50 | 0 | 608 / 1187 | 2.56 / 4.86 / 8.42 | 3.1 / 24.16 / 859.29 | 1.112 s (20 gen2) | 74.0 / 111.0 | 1012 | 2.4 |
| 150 bots (60 s) | 150/150 | 0 | 2238 / 5009 | 4.0 / 9.91 / 34.5 | 7.26 / 128.7 / 1977.65 | 2.309 s (40 gen2) | 121.2 / 150.0 | 1191 | 5.8 |
| 300 bots (60 s) | 300/300 | 3 | 1533 / 5008 | 8.51 / 24.15 / 41.12 | 145.26 / 469.31 / 4686.78 | 5.555 s (67 gen2) | 152.4 / 234.0 | 1425 | 7.6 |
| 300 bots, journal limité | 300/300 | 1 | 908 / 2688 | 8.63 / 24.42 / 42.93 | 158.3 / 683.06 / 5245.77 | 5.678 s (68 gen2) | 149.8 / 234.0 | 1383 | 7.8 |

## Verdict par rapport aux seuils du plan (§3.8)

| Seuil | 50 | 150 | 300 |
|---|---|---|---|
| Boucle de zone p99 < 50 ms | ✅ 8,4 | ✅ 34,5 | ✅ 41–43 |
| Aucun paquet de gameplay retardé > 100 ms | ❌ p99 859 ms (rafale d'entrées) | ❌ p95 129 ms | ❌ p50 145–158 ms, p95 469–683 ms |
| 0 déconnexion | ✅ | ✅ | ❌ 1 à 3 sessions |
| Budget ≤ 30 Ko/s/joueur | ✅ 2,4 | ✅ 5,8 | ✅ 7,6–7,8 |

**Conclusion honnête : 50 joueurs passent presque tout (hors pointe d'entrée), 150 est limite, 300 NE passe PAS** sur les délais et les déconnexions. Le tick de zone lui-même reste sain.

## Enquête « Broken pipe » (cause établie)

- Les 1 à 3 sessions en erreur sont **fermées par le serveur** : `NetSession` coupe un client dont la file d'envoi (256 trames) déborde (`CloseReason = backlog d'envoi…`, compteur `eden_send_queue_drops_total` = 13 323 à 300 bots, 30 197 à la 2ᵉ passe). Le bot voit ensuite « Broken pipe » à l'écriture.
- Origine du débordement : **l'opcode 326 (`ClientMovePrediction`) représente 94 % des messages reçus** (9,5 M sur 10,1 M en 60 s, soit ≈ 560 messages/s/session). Les 300 bots sont au même endroit : chaque déplacement est rediffusé aux 299 autres (`BroadcastMoveAsync`, O(N) sessions × témoins). Les mobs ajoutent la même diffusion (`BroadcastMobMoveAsync`).
- C'est un cas pire (foule immobile au même point), mais c'est exactement le scénario d'un événement de guilde ou d'une place de ville.

## Correctif appliqué

- `LogThrottle` : le journal « vitesse refusée » écrivait 33 415 lignes en 60 s (E/S sur le chemin chaud, bots qui se téléportent) ; limité à 1 ligne / 5 s / joueur → 1 511 lignes. Effet sur les délais : nul (la cause est ailleurs), effet sur les coupures : 3 → 1 (une seule passe, non concluant).

## Passe 2 : diffusion non bloquante + profilage (2026-11-07, soir)

**Appliqué** : `NetSession.TrySendCosmeticPacket` (jamais d'attente, jamais de fermeture, abandon compté dans `eden_cosmetic_drops_total{reason}`) et `SendCosmeticTo`
(témoins LOINS > 24 unités servis en priorité basse dès 25 % de file, tout le monde abandonné au-delà de 50 %) pour les 326 de joueurs ET de mobs. Un témoin lent ne
bloque plus l'émetteur. Testé : `CosmeticSendTests` (témoin qui ne lit plus : 0 attente, abandons, session intacte).

**Rejeu** (même méthode) : 150 bots → 0 coupure, tick p99 37,6 ms, attente de commandes p50 8 / p95 179 ms ; 300 bots → 1 coupure (3 avant), tick p99 44 ms, attente p50 137 / p95 620 ms.
Les `eden_send_queue_drops` tombent de 13–30 k à 1,3 k ; 32 k mises à jour cosmétiques sont abandonnées à la place (raison `full`).

**Profilage (instrumentation temporaire retirée)** : par déplacement, la diffusion coûte en moyenne 0,30 ms et la mise à jour de vue 0,22 ms ; le reste d'un déplacement et les
commandes de combat portent le total à ≈ 0,8 ms par commande. La boucle de zone est MONO-fil : à 300 bots (≈ 1 400 commandes/s) elle est saturée (somme des drains ≈ 67 s sur 60 s).

**Conclusion** : le 326 n'est pas LE goulot ; il pèse ≈ 40 % du coût d'un déplacement. Le plafond réel est la boucle unique (~1 250 commandes/s). 50 joueurs actifs ≈ 15 % de charge :
confortable ; 300 dans UNE même carte : non. Le cas « 300 sur un seul point » reste un pire cas artificiel (bots sans pathfinding).

## À faire (non fait, décisions de conception)

1. **Boucles de zone par carte/instance** (aujourd'hui une seule pour tout) : seule vraie réponse à 300 joueurs ; gros chantier d'architecture (état partagé de `ZoneEntryFlow`).
2. **Cadence de la vue adaptative** : l'intervalle de 350 ms passe à 700–1 000 ms quand la carte dépasse ~100 joueurs (0,22 ms × 3/s × N aujourd'hui).
3. **Plafond de joueurs visibles** par témoin (ex. 120 plus proches) : décision de jeu.
4. Instrumenter durablement le temps par type de commande (`eden_zone_command_ms{ncid}` hors attente) pour cibler les gros postes sans profiler.
5. Rejouer ce tableau après chaque changement : `tools/perf/summarize-load.py <dossier>`.
