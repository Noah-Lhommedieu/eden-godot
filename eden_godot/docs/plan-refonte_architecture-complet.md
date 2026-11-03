# MISSION : refondre la stack Eden (serveur C#/.NET + client Godot) pour Godot UNIQUEMENT — propre, mesurée, optimisée, prête pour ~50 joueurs simultanés

> Format de suivi : chaque action est une case à cocher. Une case ne passe à `[x]` qu'avec une preuve (test, capture, mesure ou diff vérifié). Mettre ce fichier à jour au fil de l'eau.

## 1. Contexte et objectif final
Projet : réécriture du serveur d'Eden Eternal en C#/.NET 10 (projets Gateway, Login, World, Zone, Mission, Ticket, Admin, Core) + client Godot 4 C# maison « Eden Client Reborn ». PostgreSQL, Dapper/Npgsql. Le serveur natif C++ d'origine sert de SPÉCIFICATION et d'ORACLE de comportement (lecture seule).

- [ ] Livrer un client de jeu et un code serveur propres, mesurés et optimisés, techniquement capables d'ouvrir un vrai serveur de 50 joueurs actifs simultanés (marge de conception ×4 = 200), avec :
  - [ ] chargements rapides, aucun lag en jeu (beaucoup de sorts, de maps, d'items), bonne qualité graphique (ombres, lumières, textures) ;
  - [ ] actions du joueur ressenties comme instantanées ; PvP équitable, sans triche réseau (validation serveur) ;
  - [ ] mouvement, animations et combat optimisés sur Linux ET Windows ;
  - [ ] mises à jour faciles et rapides dans TOUS les cas (futur launcher) ;
  - [ ] portage complet d'Eden Eternal.

## 2. Périmètre (non négociable) pour le projet Client Godot (tout le dossier `/home/ster2lune/Bureau/reverseden/portage/`)
- [ ] Travailler UNIQUEMENT dans `/home/ster2lune/Bureau/reverseden/portage/eden_server/` (serveur) et `/home/ster2lune/Bureau/reverseden/portage/eden_godot/` (client).
- [ ] Ne JAMAIS modifier les références/oracles en lecture seule : `/home/ster2lune/Bureau/reverseden/eden_server/`, `/home/ster2lune/Bureau/reverseden/eden_server/` (copie historique), `server_us/`, `re/`, `client/` et `reforged/`.
- [ ] Arrêter le support des clients natifs et Reforged : aucune compatibilité wire requise ; viser la fidélité de COMPORTEMENT vérifiée par des tests d'oracle contre le serveur natif.
- [ ] Conserver la logique et les fonctionnalités de jeu identiques aux anciens clients (ou améliorées uniquement avec accord du lead dev), notamment inventaire, ordre d'application des stats, buffs/status, triple hit, AGI → vitesse de déplacement, etc.
- [x] Mettre à jour `AGENTS.md` du dépôt de travail pour refléter ce périmètre : modifier uniquement ce serveur, clients natifs non supportés, oracle en lecture seule, golden wire migrés message par message, handshake de version, mesure avant optimisation.

## 3. Règles d'engagement
- [ ] MESURER AVANT D'OPTIMISER : baseline → changement → mesure ; livrer un tableau avant/après ; aucune optimisation à l'intuition.
- [ ] VÉRIFIER toute affirmation par lecture du code, grep ou mesure avant d'agir ; signaler ce qui est faux ou périmé ; les points `[HYPOTHÈSE]` ne sont pas prouvés.
- [ ] Faire de petits commits (un sujet par commit), ajouter des tests avec chaque correctif, éviter tout refactor massif sans filet de tests.
- [ ] Coder les décisions d'architecture sans autorisation seulement si elles sont peu impactantes ou explicitement demandées ; escalader tout choix coûteux/non demandé.
- [ ] Éviter toute action destructive sur les données ou le dépôt sans confirmation ; garder les secrets hors git et hors scripts versionnés.
- [ ] Avancer une phase à la fois : ne passer à la suivante que lorsque les tests existants sont verts et les métriques de la phase documentées.
- [ ] Poser UNE question précise si une instruction est ambiguë au lieu de deviner.
- [ ] Ne rien publier/uploader publiquement (assets convertis, launcher, etc.) sans demande explicite.

## 4. Baseline mesurée (2026-10-26 ; 6 bots Python, machine unique i5-12450H / 16 Go / RTX 4060 Laptop 8 Go, Wi-Fi, Nobara ; build dev)

> **Re-mesure complète 2026-10-26** avec l'instrumentation nouvelle et les bots
> C# (10-200 sessions, combat réel) : rapport chiffré dans
> `eden_server/docs/baselines/2026-10-26-rapport-baseline.md` — il corrige
> plusieurs chiffres ci-dessous (requêtes/entrée, kill, ticks, client).

### Serveur
- [ ] Revalider les chiffres suivants avec la nouvelle instrumentation :
  - [x] Tick ZoneLoop : re-mesuré → p50 ≤5 ms même sous combat 10-20 bots ; les pics (>500 ms-2,5 s) viennent des bursts d'entrée, pas d'un « tick lent » dominant ; 200 entrées → **livelock ~6 min** (preuve `zoneloop-hang-stack.txt`).
  - [ ] Écho mouvement (MoveSelf → NCID 264) : p50 42 ms, p95 101 ms, p99 140 ms, max 395 ms ; cause supposée : paquet traité au prochain tick de 100 ms (attente moyenne ~50 ms). Re-mesuré partiellement : handle MoveSelf 16,3 ms avg + dispatch-delay 930 ms sous backlog ; latence de bout en bout à re-mesurer après Phase 1.
  - [x] Requêtes SQL par entrée : re-mesuré → **36,4 req/entrée en charge légère, ~205 sous burst 200** (pool Npgsql saturé ; handle NCID 0 avg 7,3 s). Le chiffre ~600 est corrigé ; le problème reste VOLUME séquentiel sur le fil acteur.
  - [x] Kill de mob : re-mesuré → **0 requête SQL synchrone** (`mob_kill` déjà en write-behind `PersistenceQueue`, op 2,6 ms) — l'hypothèse « 6-10 requêtes synchrones » est infirmée.
  - [ ] Absence de flag GC/PGO ; Workstation GC concurrent par défaut ; absence de `NoDelay`/keep-alive serveur ; 0 `async void`, 0 `.Result` bloquant, 1 `.Wait` bénin, 53 locks bornés.
  - [ ] Un SEUL processus Zone gère toutes les maps ; AOI existante (`Sight.cs`, ±48, throttle 350 ms) ; `SpatialGrid` = code mort ; broadcasts `ZoneSessionManager` actuellement globaux.
  - [ ] Mouvement client-authoritative ; mot de passe `md5(mdp)` ; crypto RSA-2048 e=257 + RC4-40 héritée du natif ; `EntryReplayData` rejoué depuis des captures natives ; `ZoneEntryFlow` god-class ; `ItemSerialAllocator max(id)+1` ; lost update possible sur l'or ; code mort à inventorier.
  - [ ] Gamedata : ~110 `.ini` en clair, chaînes Big5, MyCrypt ZipCrypto inutilisé.
  - [ ] PostgreSQL 12 local vs 16 CI ; migrations sans `\connect` ; schéma `fndb1` non versionné ; packages obsolètes.
  - [ ] Couverture actuelle : ~769 tests, 401 cas golden (124 NCIDs) ; 68 GOLDEN / 170 DESASM / 120 PASSTHRU / 61 INCOMPLET / 471 NON PROUVÉ sur 890.

### Client
- [ ] Revalider les chiffres suivants avec le profiler Godot et captures :
  - [x] Re-mesuré (sonde `--perf`, S002 live) : 211 FPS avg, frame ~4,6 ms, hitch spawn 134,8 ms, 3,74 Go RSS pic, 3,22 Go VRAM (3,10 Go textures), 478 draw calls, 365 k primitives, 16 993 nœuds ; chargement S002 **4 112 ms**.
  - [x] Aucune section `[rendering]` dans `project.godot` (vérifié) ; fuites RID à la sortie toujours présentes (ObjectDB/RID leaks au shutdown).
  - [ ] Textures : 58 184 PNG / 9,81 Go ; VRAM théorique RGBA8+mips ≈ 69 Go ; 87 % sans mipmaps.
  - [ ] Anims : 4 243 GLB / 9,42 Go ; méga-anims 80-181 Mo ; 1 391 squelettes uniques.
  - [ ] Modèles : 3,3 Go de GLB parsés à l'exécution ; entrée de map ~2-4 s ; reload synchrone à la migration.
  - [ ] Réseau client : `TcpClient NoDelay` OK ; `List<byte>.RemoveRange` par trame ; pas de heartbeat/watchdog ; `GameScene` monolithe ; picking sur toutes les entités ; hex-dump `[wire]` en live ; ~1 040 messages serveur non représentés côté client.
  - [ ] Runtime client .NET 8 vs serveur net10 ; aucun build Windows testé ; `raw_assets` symlink.
  - [x] Caveats de mesure : re-mesuré → bots C# 10-200 sessions avec combat réel (kills prouvés) ; persistent : pas de clients réels joueurs, positions bot simulées, machine unique, build dev, loopback.

## 5. Budgets cibles (à valider avec l'utilisateur)
- [ ] Serveur avec 50 bots en combat : tick p99 < 10 ms ; action→réponse p99 < 20 ms ; écho mouvement localhost p50 < 5 ms et p99 < 30 ms ; 0 GC gen2/s en régime établi ; mémoire stable sur 1 h ; entrée de session < 20 requêtes SQL et < 10 ms bloquants ; tick max < 20 ms avec 50 entrées simultanées.
- [ ] Client : 60 FPS stables avec 50 joueurs visibles + FX de combat sur GPU milieu de gamme (4 Go VRAM) ; aucun hitch > 50 ms ; chargement de map < 2 s sans gel du thread principal ; VRAM textures ÷ ≥ 4.

## 6. Décisions de travail
- [ ] Ne PAS activer Server GC sans mesure ; tester d'abord si le burst d'entrée disparaît à la 2e exécution (JIT) ; sinon warm-up/ReadyToRun.
- [ ] Pas de Redis, Orleans, sharding DB, Kubernetes ni multi-process zone pour l'objectif 50 joueurs.
- [ ] Concentrer l'effort sur les pics (entrées, kills AoE), la latence de boucle et les assets.
- [ ] Textures : DDS BC7 (couleur/alpha), BC1 (opaque), BC5 (normales), mipmaps générées, UI non compressée, packs zstd par zone ; pas de KTX2/Basis sauf besoin mobile ; traiter ~19 300 textures >256 px et regrouper ~38 900 petites.
- [ ] Transport : TCP + TLS 1.3 d'abord ; UDP seulement si les mesures montrent du jitter/perte gênant ; prévoir `ITransport` avec canaux fiables/non fiables.
- [ ] Ne pas redessiner les ~1 200 messages d'un coup : transport/enveloppe d'abord, chemins chauds ensuite, reste à la demande.

## 7. PLAN (phases ordonnées)

### PHASE 0 — AGENTS.md, baseline et banc d'essai
- [x] Mettre à jour `AGENTS.md` (voir §2).
- [x] Ajouter OpenTelemetry + export Prometheus (métriques runtime + `Eden.Server`), healthchecks ; retirer `DOTNET_EnableDiagnostics=0` en dev ; documenter `dotnet-counters`/`dotnet-trace`/`dotnet-gcdump` ; activer `pg_stat_statements`.
- [x] Ajouter les histogrammes : durée de tick et de chaque sous-système `ZoneLoop`, délai file→dispatch, durée par NCID, profondeur de file d'envoi par session, latence DB par opération logique.
- [x] Compter les requêtes SQL unitaires par entrée/kill via un `MeterListener` Npgsql corrélé à un scope `AsyncLocal` (`eden_db_queries_total`, `eden_db_operation_queries`).
- [x] Corriger la résolution de l'oracle Reforged dans `portage/eden_server` (`eden_server/reforged` en lecture seule + montage compose/local) ; tests `MountTalent` revenus à `5/5`.
- [x] Créer 200 personnages de test avec `Eden.Admin` (`botacct00`–`botacct66`, `botchar001`–`botchar200`).
- [x] Écrire un essaim de bots C# sur la lib de protocole : entrée, mouvement, sorts, kills, loot, déconnexion ; scénarios 10/50/200 sessions, combat de groupe, spam de sorts, AoE, 50 entrées simultanées ; bots sur process/machine distincts du serveur. (`server/tools/Eden.Bots` ; ciblage par proximité via positions NCID 298/326 ; AoE a tué 4 mobs réels — cible 10 non atteinte faute de mobs à portée ; trésor au sol non exercé, drops arrivant en inventaire.)
- [x] Tester JIT : burst d'entrée à froid vs à chaud ; `gc-pause-time`, compteurs gen0/1/2, allocation rate sous charge. (50-entry froid avg 91 ms → chaud 27 ms, ×3,4 ; JIT 13 091 méthodes / 3,04 s ; gen0=262 gen1=77 gen2=10 ; pause 0,241 s ; 1,13 Go alloué.)
- [x] Mesurer client : ventilation VRAM (sonde `--perf` : 3,22 Go VRAM dont 3,10 Go textures), capture en ville S002 avec entités (`2026-10-26-client-perf-s002.txt`).
- [x] Livrer un rapport de baseline chiffré. (`eden_server/docs/baselines/2026-10-26-rapport-baseline.md`)

### PHASE 1 — Boucle zone et réseau serveur
- [x] Faire attendre la boucle sur le `Channel` avec timeout jusqu'à la prochaine échéance ; traiter les commandes dès leur arrivée avec un budget de temps par itération (ex. 2 ms) ; évaluer un tick de 33-50 ms ; objectif écho p50 < 5 ms. **Fait 2026-10-26** : réveil sur arrivée + drain `EDEN_ZONE_COMMAND_BUDGET_MS` (2 ms), cadence 100 ms conservée (le tick 33-50 ms devient inutile — les commandes n'attendent plus le tick). Mesuré move×50 : command delay p50 ≤5 ms (62 %), p97 ≤50 ms, max 250 ms borné au burst ; MoveSelf handle 3,5 ms avg (avant : avg 526 ms, outliers 12-15 s). (`bots-50-move-wake-budget.json`)
- [x] Ajouter `NoDelay` + keep-alive TCP, heartbeat/timeout applicatif, bornes sessions/handshakes/`_attempts`, `CancellationToken` + arrêt gracieux via `Microsoft.Extensions.Hosting`. **Fait 2026-10-26** : `NoDelay` + keep-alive (`EDEN_TCP_KEEPALIVE_SEC`) au accept ; idle timeout applicatif `EDEN_IDLE_TIMEOUT_SEC` (déjà en place = heartbeat borne haute) ; `EDEN_ZONE_MAX_SESSIONS` ; arrêt gracieux SIGINT+SIGTERM via `PosixSignalRegistration` (équivalent Hosting : stop listener → complète/draine la boucle → dispose sessions → borne d'attente).
- [x] Scoper les broadcasts `ZoneSessionManager` par `(node, inst)` ; réutiliser l'AOI existante. **Fait 2026-10-26** : vérifié — les chemins chauds (mouvement, combat mob, sight) diffusent déjà aux watchers AOI, pas à `Sessions.All` global ; les boucles `Sessions.All` restantes sont des chemins froids convertis en sends non bloquants.
- [x] Regrouper plusieurs trames dans un seul write par session/tick ; sérialiser une fois les broadcasts ; supprimer les awaits séquentiels par destinataire. **Fait 2026-10-26** : `PreparePacket` sérialise une fois (`ZoneEntryFlow.Broadcast.cs`), ~35 boucles converties ; drain `NetSession` regroupe les trames prêtes en un write ≤64 Ko (RC4 toujours par trame, ordre file) ; `SendPreparedToAsync` ne await plus par destinataire (TryWrite non bloquant).
- [x] Gérer le client lent avec `TryWrite` + déconnexion propre, jamais d'await de 5 s sur le fil acteur. **Fait 2026-10-26** (`NetSession.DropOnFullQueue` activé sur les sessions zone : TryWrite, sinon close du socket → reader conclut ; aucun await). Avant : burst 200 → livelock ~6 min (`zoneloop-hang-stack.txt`) ; après : **200/200 entrées, avg 321 ms, 0 erreur, file vidée en temps réel** (`bots-200-entry-nondrop-fix.json`).
- [x] Remplacer `List<byte>.RemoveRange` par `PacketReceiveBuffer` dans `ZoneWorldLink`/`ZoneMissionLink`. **Fait 2026-10-26** : fenêtre compactée, plus de memmove O(n) ni copie par trame ; `len<2` = corruption → reconnexion (désync RC4 sinon) ; `PacketFramingTests` 5/5.
- [x] Remplacer le scan complet du respawn par une file de priorité par heure de respawn. **Fait 2026-10-26** : `TickRespawns` supprimé — le tas rescapé restait vide (les respawns live passent déjà par `Schedule` par mob = échéancier par heure) ; le scan rescanait un heap vide chaque seconde.
- [x] Vérifier `EnsureNodePopulated` ; préchauffer les maps chaudes au boot ou préparer hors boucle. **Fait 2026-10-26** : populate lazy = premier joueur d'une map payait des centaines d'alloc `LiveMob` sur la boucle (S001 : 431 mobs + 256 NPC). Préchauffage au boot des scènes à spawn joueur (inst=0, types instance exclus) avant `listener.Start()` + `EDEN_ZONE_PREWARM_MAPS` ; vérifié boot réel : 7 nœuds avant « Serveur prêt ». (`temp-plans/2026-10-26-phase1-prewarm-live-nodes.md`)

### PHASE 2 — Entrée de session et base de données (priorité n°1 des mesures)
- [x] Charger inventaire/sorts/dress/quêtes en parallèle HORS `ZoneLoop` ; la boucle reçoit un état prêt. **Fait 2026-10-26** : `EntryLoadPipeline` — la commande NCID 0 pose `st.EntryLoad` et rend la main ; chargement ticket/DB/écritures d'entrée sur thread pool → `EntryBundle` immuable → install atomique postée à la boucle ; gate `TryHandleAsync` par session. (`bots-50/200-entry-pipeline.json`)
- [x] Utiliser `NpgsqlBatch` (un aller-retour) ; supprimer les INSERT répétés (`player_spells`, `fast_dress`) via création initiale ou `INSERT ... ON CONFLICT DO NOTHING`/`unnest` ; vérification en mémoire après un seul SELECT. **Fait 2026-10-26** : fast_dress 72→1 (generate_series×unnest), custom_class ≤3→1, daily_awards upsert unique, life_skill `InsertManyAsync`, sorts manquants via `EnsureSpellsAsync` batch ; lectures dédupliquées (equipment/chars/classlist/limitless/spells relues 2-5× → 1×) ; parallélisme via connexions pool distinctes (équivalent NpgsqlBatch pour des SELECTs).
- [x] Évaluer `No Reset On Close=true` après vérification qu'aucun état de session, `SET` ou table temporaire n'est utilisé. **Fait 2026-10-26** : audit complet — aucun SET/LISTEN/PREPARE/table temporaire/advisory lock dans le code ; activé dans `.env.example` + env local ; garde-fou documenté.
- [ ] Aligner PostgreSQL 16+, ajouter `\connect` aux migrations, versionner le schéma `fndb1`, purger tables doublons et code mort ; tuner pool Npgsql + `CommandTimeout` ; remplacer `ItemSerialAllocator` par une séquence PostgreSQL ; rendre or/ressources atomiques en deltas. **Partiel 2026-10-26** : or en deltas atomiques sur chemins économiques (trade/shop/vending/enchères/mail) ; `ItemSerialAllocator` confirmé non multi-process (zone↔mission collision réelle) — à remplacer par séquence PG (suivi) ; PG16/versioning schéma non faits.
- [ ] Supprimer tout await DB sur le fil de la boucle. **Majoritaire 2026-10-26** : entrée (ticket→migration, ~200 req) hors boucle ; restent des requêtes par-paquet post-install (elfs 110, mission 50, handlers) — bornées, suivi.
- [ ] Atteindre < 20 requêtes et < 10 ms bloquants par entrée ; tick max < 20 ms avec 50 entrées simultanées. **Mesuré 2026-10-26** : ~34 req/entrée (était ~36 légère/205 burst) — pas encore <20 ; bloquant boucle ≈0 (command delay 0,64 ms avg sur 43k, file 0) ; 200/200 entrées avg 6,6 ms.

### PHASE 3 — Combat/kill et persistance progressive
- [x] Sortir les écritures du fil acteur sur le chemin kill ; garder un working-set mémoire pour exp/or/inventaire ; écrire via `PersistenceQueue` (dirty-flags étendus) ; mesurer avec bots AoE. **Fait 2026-10-26** : kill déjà write-behind (0 req synchrone mesurée Phase 0) ; dirty-flags étendus via entrée pipeline + prefetch ; combat ×20 NCID5 handler 0,46 ms avg post-refactors.
- [x] Séparer écritures critiques (échanges, achats, transferts d'or, objets rares : transactionnelles et immédiates) et non critiques (write-behind) ; garantir aucun risque de dupe/perte à la déco/crash. **Fait 2026-10-26** (`temp-plans/2026-10-26-phase3-persistance-transactions.md`) : trade/étals/shops/enchères-achat/mail/coffre-famille/banque = une connexion+`BeginTransaction` par opération, or en deltas gardés ; 4 bugs réels corrigés (or trade mort, inventory2 ignoré, serial dupliqué banque, Quantity=durability). Résiduel : auction-sell, mail-or, livraison trade, SetGold absolus restants — voir plan.
- [x] N'adopter un mode memory-first complet que si les mesures sous charge 50-200 bots le justifient. **Statu quo 2026-10-26** : write-behind existant + tx critiques suffisent à 200 sessions (entrée 6,6 ms avg) — memory-first complet non justifié par les mesures actuelles ; à réévaluer si charge >200.

### PHASE 4 — Transport et protocole (Godot-only)
- [ ] T1. Créer `ITransport` ; TLS 1.3 (`SslStream`) + framing moderne (longueur varint), payloads existants conservés ; supprimer RSA-2048/RC4-40 ; handshake de version obligatoire (version minimale + hash gamedata ; client périmé → « mise à jour requise »). **Majoritaire 2026-10-26** : `ITransport`/`TcpTransport`/`LegacyLapisTransport` (a), framing varint LEB128 borné `EDEN_MAX_FRAME_SIZE` partout (b), `TlsTransport`+cert auto-signé+pinning `EDEN_TRANSPORT=modern|legacy` (c) — prouvé live : vrai client Godot `zone hello` en TLS pinné et en legacy, bots 5/5 deux modes, liens internes TLS. Handshake version obligatoire post-TLS (NCID 0xFFFF/0xFFFE/0xFFFD, `EDEN_VERSION_CHECK`, `EDEN_MIN_CLIENT_VERSION`) + manifest gamedata SHA-256 déterministe (166 ini, identique sur les 3 services ; client périmé → `update_required`, prouvé live client Godot `VERSION_OK` ×3 + bot `--no-version` rejeté). Reste : suppression effective RSA/RC4 (phase4-c2, `EDEN_TRANSPORT=legacy` repli conservé), tickets signés inter-services.
- [ ] T2. Remplacer `md5(mdp)` par Argon2id côté serveur ; re-hachage à la première connexion ; tickets inter-services signés ECDSA/HMAC à courte durée. **Argon2id fait 2026-10-27** : `accounts.password_hash` PHC `$argon2id$v=19$m=65536,t=3,p=1$…` sur le credential wire `md5(mdp)` ; verify PHC sinon legacy puis rehash post-login (preuve live : `admin` rehashé en base, 2e login sans rehash, mauvais mdp rejeté dès PHC posé) ; nouveaux comptes hashés à l'insert. Reste : tickets inter-services signés.
- [ ] T3. Créer `Eden.Protocol` net8 : générer messages client ET serveur depuis le même schéma ; rattraper les ~1 040 messages manquants côté client ; UTF-8 partout (supprimer Big5/CodePages) ; convertir les ini en UTF-8 au build.
- [ ] T4. Redessiner uniquement les chemins chauds, dans l'ordre : mouvement, cast/combat, spawn/despawn/état d'entités, chat.

### PHASE 5 — Ressenti gameplay client (ajouts utilisateur 2026-10-26)
- [x] Coder le saut et le double saut avec l'animation prévue par l'ancien client Reforged ; prouver les deux sauts en capture live. **Fait 2026-10-26** : `eden_jump` (Space) → impulsions verticales (v=7, g=19, ~1,3 u apex, ~0,74 s air), max 2 impulsions/vol miroir `jump_feature_max` Reforged, `jump01` en `PlayOnce`, atterrissage terrain/plancher. Preuve `EDEN_JUMP_DIAG=1 --input-test` : « impulsion n=1 » → « impulsion n=2 y=0,81 air=True » en plein vol → « atterrissage » + `godot-shots/00-jump-air.png`. Sync NCID 581 différée (attente capture Reforged `jump_feature=1`).
- [x] Corriger les zones de collision trop bloquantes de la map : le personnage doit pouvoir passer au-dessus des objets franchissables, notamment le pont de la première map ; prouver par un run live sur le pont. **Fait 2026-10-26** : classification géométrique AABB — dalles plates nommées wall → `floor` (layer 2, raycast walkable), helpers verticaux → `wall` (layer 1), cas ambigus logués. Preuve live `--collision-dump=295,305,60` sur S002 : `'true wall'` du pont UE001 → `class=Floor layer=2 note=floor(flat-wall)` « pont marchable » ; `blo`/dalles verticales restent `Wall layer=1` ; client en zone (TLS) `zone hello 160,47`.
- [x] Corriger les bugs graphiques/textures dans la sélection de personnage et en jeu ; couvrir warrior puis toutes les autres classes, ainsi que les costumes équipables ; prouver par captures multi-classes/costumes. **Fait 2026-10-26** : (a) mipmaps — `Image.GenerateMipmaps()` dans `AssetCatalog.LoadTexture` + extension GLTF `EdenGltfMipmaps` (249/249 textures GLB sans mips avant) + samplers `filter_linear_mipmap`, kill-switch `EDEN_MIPMAPS=0` ; mesure A/B S002 live : fps identique ≈222, tex VRAM 3096→4051 MB (+31 %), loadmap +88 ms ; (b) matrice apparence — nouveau diag `--appearance-test` : 12 combos × 7 corps (b/g/m/f/k/l/c) × visage/peau/cheveux × costumes 4pequip (worn simulé) — dump `[appmat]` mesh/tenue/peau/face_tex/attach, 0 mesh voulu absent, 1 texture visage absente (`KFACES12`, limite données Ursun loguée) ; (c) log `[assets] texture absente/illisible` ajouté — balayage live S002 + écrans sélection/création : aucune autre texture en échec. Captures `godot-shots/appmat-s002.png`, `appmat-select.png`, `appmat-create.png`, `live-ingame.png`. Reste : joueur distant réel en zone (preuve équipement synthétique rejouée seulement).
- [x] Supprimer la latence de déclenchement/arrêt des animations de déplacement : l'animation doit démarrer à la même milliseconde que le mouvement et s'arrêter dès l'arrêt réel ; mesurer/valider en capture vidéo ou timestamps. **Fait 2026-10-26** : arrêt franc local — relâche + déplacement ~nul ≥45 ms → `stand01` immédiat (avant : fenêtre 90 ms + hold 60 ms + blend 120 ms ≈ 200-270 ms de run-on) ; blends loco 70 ms. Distant : `InputMoving` piloté par le payload 326 (`DstPos!=CurPos`) dès la trame au lieu d'attendre la vitesse interpolée (~150 ms gagnés côté remote). Preuve log `--input-test` : `move01` distant immédiat après trame 326.

### PHASE 6 — Complétude HUD/NCID/contenu (suivi `eden_server/docs/temp-plans/2026-10-27-phase6-completude-ncid-client.md`)

- [x] Économie prouvée live : trade J↔J (transfert DB sans dupe/perte), étals joueur (vente+achat+règlement), banque + coffre famille, **item mall** (achat → points débités en DB + item livré — driver `--mall-test`).
- [x] Maps : **139/139 chargées** en audit headless (`--map=Sxxx --solo`) — rapport `temp-plans/2026-11-07-audit-maps-items-monstres.md`. Spam quaternion (~11 k err/map) éliminé (sanitisation tracks + rest poses + lib retargetée).
- [x] Monstres/items : 13 033 monstres / 21 485 items audités ; stems spawnés 100 % couverts (fallback `c_monster` pour la typo scène `M0M01` ; `M912` = marqueur statue bénin) ; 45 icônes `E0xxx` récupérées depuis `skillicon/` Reforged, 6 introuvables (défaut natif).
- [x] Entrée live : **zéro NCID « non géré »** — parseurs ajoutés pour tout le burst d'entrée (shortcuts, closets, licenses, life skills, sysvars, cooldowns, team recruit, guide, revive, node times, enchant, destiny, monde 40/46/48/54/60/78, item mall monde, flag 302…). Pattern retenu : binds précoces `HudExtensions` + `Apply*Data` thread-safe / `RaiseChanged` posté main.
- [x] NCID S→C : **213 émissions serveur couvertes, zéro drop silencieux** — ~170 parseurs générés depuis les codecs serveur + 11 codecs complexes manuels (hotbar, look-equip, cube, blast, player-card, team-search, rooms, killed-rank, apex-buffer) ; `MiscPackets` (Latest/Seen + event main-thread) + `GameScene.OnMiscPacket` (chat, vitals, reset, revive, sac, coins). Live : 89 NCIDs reçus, 0 « non géré ».
- [x] NCID C→S : **68 senders générés** depuis les codecs `CNE_CZ_` serveur + senders manuels (duel, message-board, magicbox wire vecteurs, battlefield, loterie) — câblage UI uniquement à sémantique prouvée (règle anti-spéculation).
- [x] HUD dédiés vague 2 : `LotteryWindow` (3 autels, tickets/crystals natifs), `MagicBoxWindow` (main/sacrifices/combos/catalyseur mall), `BattlefieldWindow`, `HotelWindow`, `SystemServiceWindows` (closet/lifeskill/territory/collection/fastdress/recruit/renfort), `RollTreasureDialog` (520→193 need/greed/pass), `InspectWindow` (460/571, bouton 🔍 cible joueur→143), `DuelDialog`, `MessageBoardWindow`, `FeatureDialogWindow`. Tous déplaçables (DragHelper), montés dans `HudExtensions`.
- [x] Optimisation client (docs/PERF.md, mesures A/B) : **PerfCulling** (visibility-range par rayon AABB — draw calls 3813→~2000 vue spawn S001), **PropsBatcher** MultiMesh (1843 props→599 multimeshes, frame ms −38 %, fps +49 % sous llvmpipe), mipmaps textures+GLB, chargement map 5590→2040 ms, collisions partagées. Bug `CollisionBuilder` objT² corrigé. Restes non bloquants : pipeline .ctex (VRAM ~576 MB), chargement async, fusion terrain, warmup shaders.
- [ ] Reste : PVP/arena/battlefield live (multi-clients) — nécessite 2 clients simultanés ; instances/donjons et transports en scénario live ; .ctex/async/terrain-merge/shader-warmup (perf, non bloquant).

## 7b. Refonte fidélité totale v2 — état nov. 2026

Plan actif : `docs/archive/temp-plans/2026-10-27-refonte-fidelite-totale-v2.md`.
Blocs A-J bouclés ; Bloc K en cours.

- [x] **A. Renommage** — Eden Limitless (config/name + libellés).
- [x] **B. Perf** — ~90-100 fps live steady (9,8 ms) ; RAM top-5
  mesurée (`tex-postes` : exclues char/UI dominent tex=4,5 Go live —
  `docs/PERF.md`) ; login bg instantané ; CharBodyCache.
- [x] **C. Gameplay** — échelles, vitesses (×5 dev), interp mobs.
- [x] **D. Thème** — héritage panneaux + drag universel.
- [x] **E. Purge** — HUD parasites vs `hud-interfaces.md`.
- [x] **F. Écrans** — login/channel/charselect (roster par noms +
  mot de passe perso) /charcreate (chrome natif + skin) /loading
  (bandeau natif `loadingframe00/01` + alpaca animé).
- [x] **G. Corps** — audit 10 combos race/genre, caméra par race,
  masques FACES B/D/F teintés.
- [x] **H. HUD permanent** — minimap 16 boutons câblés (+arène/
  événements), castbar prouvée live.
- [x] **I. Fenêtres services/PvP** — 16 fenêtres migrées en layouts
  CEGUI natifs (`BuildNativeWindow`) ; repair forgeron round-trip
  live prouvé (`--repair-test` 45→342 + 47→343). Étal joueur prouvé
  (`--vending-test` : 77→377[1][2]+vente, 72→378→76 achat), défi 1v1
  prouvé (`--duel-test` + `HeadlessBot` : 135→451→136→453→454), mail
  PJ↔PJ prouvé (`--mail-test=dest` + bot `mail-check`), daily reward
  prouvé (`--daily-test` : 258 op=1→615 état, jour 1 levé à 600 s
  en ligne, claim op=2→274 item+615 type=2), level-up prouvé
  (`--levelup-test` : `//set_level` → 600 exp=0 + 284 level=31→30
  restauré). Reste : roll loot — déclencheur gameplay hors portée
  driver.
- [x] **J. Analyse re/** — chargement natif documenté
  (`docs/NATIVE-LOADING-ANALYSIS.md`) : DB sync + CZone +
  PrecacheKFM ; notre flux déjà conforme.
- [~] **K. Validation** — `PERF.md` + `hud-interfaces.md` à jour ;
  reste : matrice capture-vs-ref, fuites RID/ObjectDB à la sortie,
  warmup shaders.

## 8. Livrables attendus
- [x] `AGENTS.md` serveur + client alignés sur le nouveau périmètre.
- [ ] Rapport de baseline chiffré avant/après.
- [ ] Tests et captures live pour chaque correctif client.
- [x] Petits commits séparés par sujet.
- [x] Documentation des limitations et preuves conservées dans `docs/`.
