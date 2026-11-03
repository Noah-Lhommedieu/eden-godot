# Rapport complet — Stack serveur Eden (C#/.NET 10) & client Godot « Eden Client Reborn »

> Document produit pour être relu/vérifié par une IA externe. Toutes les
> affirmations citent `fichier:ligne` quand possible. Sources :
> `/home/ster2lune/Bureau/reverseden/eden_server/` (original),
> `/home/ster2lune/Bureau/reverseden/eden_server/` et
> `/home/ster2lune/Bureau/reverseden/portage/eden_server/` (copies),
> `/home/ster2lune/Bureau/reverseden/portage/eden_godot/` (client Godot).
> Date : 2026-10-25.

---

## 0. Contexte projet (à connaître avant toute évaluation)

- **Eden Eternal** = MMORPG dont le serveur officiel C++ 32 bits (moteur `lapis`,
  binaires `server_us/` non strippés) sert de **spécification exécutable**.
- Le projet réécrit le serveur en C#/.NET avec une contrainte dure :
  **fidélité wire byte-à-byte** — deux clients natifs (`client/` référence et
  `reforged/EdenReforged/` mod QoL anglais) doivent rester jouables.
- En parallèle, un **client Godot 4 maison** (`portage/eden_godot/`)
  est développé pour remplacer à terme le client natif.
- Conséquence architecturale majeure : **le protocole réseau (TCP + RSA-2048 +
  RC4-40bits + framing `[u16 len][u16 NCID][payload]`) est imposé par les
  clients natifs**. Toute « optimisation » du transport ne peut changer le
  protocole que si le client Godot devient le seul client supporté.

### Inventaire des copies réalisées

| Destination | Contenu | Taille |
|---|---|---|
| `reverseden/eden_server/` | `server/` (sln + 9 csproj, ~490 .cs, hors bin/obj), `server_us/Data` + `config.ini`/`setup.ini` (gamedata runtime 183 M), `Database/` (migrations SQL), `database.out` (dump edenfr_account), `docs/`, `captures/`, `tools/` (sans toolchain 618 M), `docker-compose.yml`, `.env`/`.env.example`, scripts start/stop, `AGENTS.md`, `README.md`, `CLAUDE.md`, `eden_rsa.pem` | ~237 M |
| `reverseden/portage/eden_server/` | **copie identique** (référence serveur à côté du client Godot) | ~237 M |

Non copiés (volontaire) : `server_us/` binaires natifs, `re/` exports Ghidra,
`client/` et `reforged/` (29 G), `tools/toolchain/`, `Server/server.rar`,
clients lourds. `clientmod/` omis. ⚠️ `.env` contient le mot de passe DB de
dev local — copié pour permettre l'exécution, à ne pas publier.

---

# PARTIE 1 — STACK SERVEUR

## 1.1 Vue d'ensemble

Solution `server/Eden.sln` — **9 projets `net10.0`** (SDK `10.0.100`,
`global.json`, LangVersion `latest`, Nullable enable). Un processus par ancien
binaire C++ :

| Projet | Port(s) | Rôle | État |
|---|---|---|---|
| Eden.Core | — (lib) | Transport lapis, crypto, codecs, parsers .ini, repositories, services gameplay, config | bibliothèque partagée |
| Eden.Gateway | 5560 | Passerelle launcher/heartbeat | **stub assumé** (lecture brute sans framing, `Program.cs:62-65`) |
| Eden.Login | 6543 | Auth RSA→RC4, liste mondes, ticket login→world | fonctionnel (~3 NCIDs) |
| Eden.World | 5200 client + 5301 lien ZW | création/sélection perso, amis, famille, item mall, LFG | largement implémenté (~35 NCIDs) |
| Eden.Zone | 10022 (5201 en docker) client | cœur : entités, combat, sorts, inventaire, social, PvP, métiers… | le plus complet (~80 NCIDs interceptés + ~214 registrations) |
| Eden.Mission | 7654 interne | moteur de quêtes, dialogues, mail, classements, battlefield, mariages | implémenté (~25 NCIDs ZM) |
| Eden.Ticket | 2067 interne | émission/validation one-shot tickets | complet, minimal, **sans DB** (mémoire) |
| Eden.Admin | — (CLI) | création comptes/persos hors-jeu | fonctionnel |
| ~~Eden.HttpApi~~ | — | **n'existe pas** dans la solution (le natif HTTPAServer n'est pas porté) |

Topologie docker-compose : `postgres:5432` + ticket:2067, gateway:5560,
login:6543, world:5200, zone:5201, mission:5202 sur réseau bridge `eden`.

## 1.2 Packages NuGet — exhaustif

Gestion centralisée `server/Directory.Packages.props` :

| Package | Version | Usage |
|---|---|---|
| Dapper | 2.1.89 | micro-ORM, tout l'accès DB |
| Npgsql | 10.0.3 | driver PostgreSQL |
| Microsoft.Extensions.Logging.Console | 10.0.0 | logging (SimpleConsole) |
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.5 | tests |
| Microsoft.NET.Test.Sdk | 18.0.1 | tests |
| coverlet.collector | 6.0.4 | couverture |

**C'est tout.** Pas d'ASP.NET, pas de DI container, pas de Serilog, pas de
System.IO.Pipelines, pas de polly/resilience, pas de bibliothèque réseau tierce.
La composition est manuelle dans chaque `Program.cs` (~250 lignes de `new`
dans Zone). Métriques via `System.Diagnostics.Metrics` (BCL), crypto via
`System.Security.Cryptography` (BCL), concurrence via `System.Threading.Channels`.

## 1.3 Couche réseau — détail exact

**API : `TcpListener` + `AcceptTcpClientAsync` + `NetworkStream`, async/await
pur.** Zéro `SocketAsyncEventArgs`, zéro `System.IO.Pipelines`, zéro `Socket`
direct, zéro UDP dans tout `src/`.

| Classe | Fichier | Rôle |
|---|---|---|
| `NetSession` (536 l.) | `Eden.Core/Networking/Session/NetSession.cs` | session joueur : handshake, framing, file d'envoi bornée 256 + drain unique, timeouts, rate-limit |
| `Rc4` (53 l.) | `Networking/Rc4.cs` | RC4 maison compatible OpenSSL, stream continu par direction |
| `EdenCrypto` | `Networking/EdenCrypto.cs` | paire RSA-2048 **e=257** (0x0101) depuis `eden_rsa.pem` — e=65537 changerait la taille du blob et désynchroniserait le client |
| `PacketReceiveBuffer` | `Networking/PacketReceiveBuffer.cs` | buffer réception → trames ; compaction amortie, rejet `len<2` |
| `PacketTrace` | `Networking/Session/PacketTrace.cs` | ring 32 paquets dumpé à la déco anormale |
| `In/OutBinStream` | `Networking/` | lecteur/écrivain LE ; strings Big5 préfixées u16 |
| `NetCommand` | `Networking/NetCommand.cs` | base de ~1 225 messages (`Opcode`/`Serialize`/`Deserialize`) |
| `PacketDispatcher` | `Networking/Handlers/` | dispatch opcode→factory+handler par dictionnaires |

**Protocole wire (désasm prouvé)** :
1. S→C : clé publique RSA-2048 brute `[u32 nlen][u32 elen][n BE][e BE]` (266 o)
2. C→S : 256 o = RSA_PKCS1(clé RC4 **5 octets / 40 bits**)
3. Trames : `[u16 len en clair][RC4(u16 NCID + corps)]` — keystream continu.

**Concurrence** : accept-loop `while(true)` + `Task.Run` par connexion
(thread-pool). Par session : 1 tâche lecture + 1 drain envoi
(`Channel<(byte[]Buf,int,int,int)>` bornée 256, `SingleReader`,
`FullMode.Wait`, timeout 5 s → fermeture propre). RC4 sortant appliqué
**dans le drain** → ordre keystream garanti sous envois concurrents.
Rate-limit 300 pkt/s/session + login 10/min/IP. Handshake 10 s, idle 300 s.

**Allocations hot path** : ~4-5 allocs/paquet entrant (`byte[]` par trame,
`.ToArray()` par payload, `NetCommand` par paquet, `List<byte[]>` par appel) ;
~2-3/sortant (`OutBinStream`+`ArrayBufferWriter`, `ToArray`, puis trame louée
`ArrayPool` rendue par le drain — bon point). Broadcasts : `await` **séquentiel**
par session + re-sérialisation par destinataire (`ZoneSessionManager.cs:50-70`).

**⚠️ Faiblesses mesurées pour un MMORPG** :
1. **`NoDelay` jamais activé** — Nagle + delayed ACK = +40-200 ms potentiels
   sur petits paquets interactifs (0 match `NoDelay|SocketOption|KeepAlive`
   dans `src/`).
2. **Latence acteur +50 ms moyenne** : tout paquet zone est posté sur
   `ZoneLoop` (tick 100 ms) avant dispatch — mouvement/combat inclus.
3. **Client lent = boucle gelée** : les handlers sur le fil acteur `await`
   une file pleine jusqu'à 5 s.
4. Pas de TCP keep-alive (NAT morte détectée à 300 s), pas de borne globale
   de sessions ni de handshakes RSA concurrents (DoS surface), map `_attempts`
   login non bornée.
5. `List<byte>`+`RemoveRange` O(n²) dans `ZoneWorldLink`/`ZoneMissionLink`
   (le défaut corrigé ailleurs par `PacketReceiveBuffer`).
6. Crypto héritée = **sécurité nulle par design** (RC4 cassé, 40 bits,
   PKCS#1 v1.5, pas de MAC/TLS) — imposée par les clients natifs.
7. Pas d'arrêt gracieux des listeners (pas de CancellationToken).

## 1.4 Persistance & données

**PostgreSQL 12** (docker-compose ; ⚠️ EOL nov 2024 — la CI utilise déjà
postgres:16). 3 bases : `edenfr_db` (jeu, ~156 objets), `edenfr_account` (comptes/item
mall/lottery, 24 tables), `edenfr_member` (billing `tb_user`).

- **Accès : Dapper + SQL inline paramétré partout**, aucun ORM. Repositories
  (~35 classes) injectés avec un `NpgsqlDataSource` par base/process, pool par
  défaut (MaxPoolSize=100, aucun tuning, pas de CommandTimeout).
  `MatchNamesWithUnderscores` activé par module-init `DapperBootstrap`.
- ~18 transactions localisées (création perso multi-tables, bags, item mall) ;
  beaucoup de multi-statements sans tx (dissolution guilde).
- **`PersistenceQueue`** (`Eden.Core/Database/PersistenceQueue.cs`) : channel
  borné 256 **par charId**, drain par clé → ordre strict par perso,
  parallélisme inter-persos, métriques. **N'existe que dans Zone.**
- **`CharacterWriteBehind`** (zone) : dirty-flags coords/revive, flush 15 s
  (`EDEN_PERSIST_FLUSH_MS`) + flush à la déco. Autosave complet ~1 min.
- **`ItemSerialAllocator`** : `max(id)+1` en mémoire sur 15 tables items —
  reproduit l'absence de séquence native ; **mono-process only**.
- **Gamedata 100 % en mémoire** : `GameDataService.LoadAll()` charge ~110
  fichiers `.ini` de `server_us/Data/db_decoded` au boot (S_Item ~17k lignes,
  S_Monster ~12.6k, S_Enchant ~19.7k, S_Mission ~79k pipes) — zéro lookup DB
  pour les defs. Parser `IniTable` 3 formats, `MyCrypt` ZipCrypto.
  Overlay Reforged fusionné (`ReforgedDataLocator`).

**⚠️ Faiblesses** :
1. **Modèle « DB-as-truth »** : pas de working-set inventaire en mémoire —
   `GetItemsByCharacterIdAsync` re-sélectionne ~50 colonnes à chaque op ;
   **~6-10 requêtes synchrones par kill de mob** (exp, classe, or, quêtes,
   scans inventaire) sur le chemin handler.
2. **Race or/ressources** : `SetGoldAsync` absolu (zone) vs
   `gold = gold + n` atomique (mission) sur la même ligne → lost update
   possible entre services.
3. Migrations SQL **sans `\connect`** → tables créées dans la base `postgres`
   sur volume frais (`check_and_migrate_db.py` pallie — script d'ailleurs
   cassé : nom de conteneur obsolète).
4. Schéma natif `edenfr_db` non versionné (dump absent du dépôt) ; tables maison
   doublons (`characters`, `player_items`, `guilds`) + repo mort
   `Eden.Core/Data/Repositories`.
5. Multi-table sweeps : chaque update de champ item boucle jusqu'à 6 tables
   séquentiellement.
6. `ItemSerialAllocator` corrompt l'espace d'ids en multi-process.

## 1.5 Architecture gameplay (Eden.Zone)

- **`ZoneLoop`** = modèle acteur : `Channel` borné 1024 commandes +
  `PeriodicTimer` **100 ms** (drain commandes → events schedulés → ticks
  combat/aggro → tick lent respawn ×1/10). `AsyncLocal` `IsOnLoopThread` +
  `PostOrRunAsync` anti-deadlock. **Toute mutation d'`EntryState` passe par la
  boucle** — races éliminées par construction.
- **`ZoneEntryFlow`** : orchestrateur en **~95 fichiers partials**
  (`ZoneEntryFlow.Combat.*`, `.Sight`, `.Migration`, `.Mission`, `.Elf`,
  `.Territory`, `.Vending`, …). `TryHandleAsync` route tout sur la boucle ;
  `TryHandleOnLoopAsync` = switch de ~80 NCIDs (700 lignes) puis fallback
  `PacketDispatcher`.
- **`EntryState`** : miroir plat documenté du `CCharacter` natif (~1100 l. de
  champs annotés par offset `char+0x4e4`…).
- **`EntryReplayData`** : burst d'entrée **rejoué depuis captures natives**
  (payloads hex patchés charId/nom) — fidélité parfaite mais fragile hors
  node 2/capture.
- **Protocole** : ~1 225 classes `NetCommand` dont ~1 200 générées
  (`Messages/Generated/*.g.cs`, champs `FieldN` positionnels — sortie de
  `tools/extract_all_protocols.py` ; `ZoneMessages.g.cs` = 873 classes, 31k
  lignes). Codecs dédiés pour les formats variables (18 fichiers :
  `NativeItemCodec` bitmask, `VendorPackets`, `WeddingPackets`…).
  `known_incomplete.json` liste 61 NCIDs aplatis par le générateur.
- **Liens inter-services** : T_ (:2067, RC4), ZW Zone→World (:5301, RC4),
  ZM Zone→Mission (:7654, handshake fait mais **trafic en clair**, fidèle au
  natif) + partage implicite via PostgreSQL (pas de bus).
- **Couverture** : FEATURE-MATRIX = **68 GOLDEN / 170 DESASM / 120 PASSTHRU /
  61 INCOMPLET / 471 NON PROUVE** sur 890 NCIDs.

**⚠️ Faiblesses** : `ZoneEntryFlow` god-class assumée (~80 partials,
plusieurs fichiers > 40 ko) ; double chemin de dispatch subtil ; mono-zone
par construction (`ActiveZoneSession` unique, `WorldServerId=1` en dur) ;
boucle unique = plafond de scalabilité (un `await` DB lent bloque toute la
zone) ; code mort (`CombatEngine`, `SpatialGrid`, `WorldPacketHandlers`,
`MissionPacketHandlers`).

## 1.6 Tests, tooling, déploiement, observabilité

- **Tests** : ~97 fichiers, **769 `[Fact]/[Theory]`** xunit. **401 cas golden
  byte-à-byte** (`Data/golden/ncid_*.json`, 124 NCIDs) : `Deserialize→Serialize`
  doit reproduire les octets natifs ; ratchet anti-régression sur les 61
  incomplets. Tests de charge backpressure, acteur, persistance, parsers.
  CI GitHub Actions : postgres:16 + toutes migrations + tests contre vraie DB.
- **Toolchain de preuve unique** : `native_mitm.py` (déchiffre avec la clé
  privée), `eden_headless.py`/`eden_session.py` (client RSA+RC4 rejoueur),
  `wire_diff.py` (diff NCID-par-NCID + masques), `lapis.lua` (dissector),
  `ptrace_sniff.py`, `zsniff_decode.py`, `extract_all_protocols.py`
  (générateur des `.g.cs` via `nm -C`), `gen_feature_matrix.py`.
- **Dockerfiles** : 6 × multi-stage `sdk:10.0`→`runtime:10.0`, mais `COPY . .`
  avant restore (pas de cache NuGet), pas de healthcheck,
  `DOTNET_EnableDiagnostics=0` (bloque dotnet-counters).
- **Observabilité** : `EdenLog` (SimpleConsole par catégorie inférée `[Tag]`),
  `EdenMetrics` (meter `Eden.Server` : packets in/out, bytes, décos taggées,
  tick zone, db_op, jauges queue/sessions) — **aucun exporteur OTel/Prometheus
  configuré**, zéro tracing distribué (`ActivitySource` absent).
- **Scripts fragiles** : `start`/`stop` legacy tronquent `/var/log/*` ;
  secrets DB en clair dans `start-native.sh`/`start-local.sh` ;
  `patchIp.sh` = sed binaire ; `check_and_migrate_db.py` cassé.

## 1.7 Recommandations d'optimisation serveur (pour évaluation externe)

Priorisées, sans casser la fidélité wire :

1. **Quick wins réseau** : `tcpClient.NoDelay = true` à l'accept (+ keepalive) ;
   mutualiser le plaintext sérialisé des broadcasts ; `PacketReceiveBuffer`
   dans les liens ZW/ZM ; `TryWrite` ciblé pour que les clients lents ne
   gèlent pas la boucle ; borner sessions/handshakes RSA.
2. **Latence acteur** : traiter mouvement/combat hors file 100 ms ou tick
   adaptatif ; ne jamais `await` de DB sur le fil de la boucle.
3. **Persistance « memory-first »** : working-set par personnage chargé à
   l'entrée (inventaire inclus), mutations en mémoire sur la boucle, tout
   write via `PersistenceQueue` avec dirty-flags étendus (exp/or/items).
   Élimine N+1 et l'amplification par kill. Or : deltas atomiques partout.
4. **Schéma** : postgres 16+, `\connect` dans les migrations, séquence/table
   de compteurs pour les ids items, purge des tables maison doublons.
5. **Si les clients natifs sont abandonnés** (Godot only) : tout devient
   possible — TLS/QUIC, canal UDP/ENet/KCP pour le mouvement, sérialisation
   moderne. Tant qu'ils sont supportés, le wire reste figé.
6. **Stack alternative envisageable en .NET** : `System.IO.Pipelines` +
   `Socket` direct + `MemoryPool` (zéro-alloc), `Microsoft.Extensions.Hosting`
   (DI + IHostedService + graceful shutdown), OpenTelemetry, healthchecks.
   Dapper+Npgsql restent un bon choix. Aucun besoin d'Acteurs/Orleans à cette
   échelle — le Channel-acteur maison est plus simple et suffisant.

---

# PARTIE 2 — STACK CLIENT GODOT « Eden Client Reborn »

## 2.1 Moteur & versions

| Élément | Valeur | Source |
|---|---|---|
| Moteur | **Godot 4.7.2 (.NET/mono)** | `EdenClientReborn.csproj` (`Godot.NET.Sdk/4.7.2`) |
| Langage | **C# uniquement — 156 fichiers .cs, 0 GDScript** | `find scripts` |
| Runtime | **.NET 8.0**, `EnableDynamicLoading`, Nullable+ImplicitUsings | csproj |
| Packages | `System.Text.Encoding.CodePages` 8.0.0 (Big5 CP950) — **unique dep** | csproj + `NuGet.config` (source locale GodotSharp hors-ligne) |
| Scènes | 14 `.tscn` quasi triviales (1 nœud+script) ; **toute l'UI et le monde sont construits programmatiquement** | `scenes/` |
| Entrée | `run/main_scene="res://scenes/boot.tscn"` → `ScreenManager` ; autoloads `SettingsApplier` + `HudExtensions` | `project.godot` |
| Thème | `ui_textures/eden_default_theme.tres` (⚠️ dossier gitignoré, régénéré) | `project.godot` |
| Exports | presets Linux/X11 + Windows x86_64, C# compilé (`script_export_mode=2`), pck non chiffré | `export_presets.cfg` |

## 2.2 Code client — arborescence & réseau

```
scripts/
  Main.cs (orphelin), GameScene.cs (~1520 l. — god-object), AssetCatalog.cs,
  ClientConfig.cs, DataPaths.cs
  net/        EdenConnection.cs (TcpClient NoDelay + RSA→RC4 + framing +
              Channel<Frame>), Rc4.cs, BinReader/BinWriter, NetMessage.cs
  protocol/   ~161 messages NCID (Login, World, Zone*, codecs items/apparence)
  session/    EdenSession.cs (orchestrateur login→world→zone, dispatch OnZone)
  screens/    ScreenManager + Login/Channel/CharSelect/CharCreate/Loading +
              TempleBackdrop (hall 3D S218 partagé)
  world/      MapLoader, FsmFile, TerrainBuilder/Sampler, PropBatcher
              (MultiMesh), CollisionBuilder, EntityRegistry, SkyDome,
              FogCeiling, WeatherEffects, GrassRenderer, WaterAnimator…
  game/       CharVisual (méga-modèle GLB #BFACE/#B2xx), EntityAnimator,
              AnimRetarget, GlbAnimFile (parseur GLB maison)
  player/     PlayerController (CharacterBody3D kinématique WASD+clic-sol,
              SpringArm caméra), InterpBuffer (150 ms render delay),
              NetworkInterpolator, MoveMarker
  combat/     AutoAttack, SkillCast, CombatFx, DamageFloaters, CastBar…
  hud/        ~33 classes — fenêtres CEGUI natives portées (inventaire,
              profil, shop, banque, craft, quêtes, skills, social, carte,
              chat, minimap, hotbar…)
  ui/         CeguiToGodot (layouts .layout → Controls), parseurs CEGUI XML
  input/      EdenCursors (atlas natif), PickCore
  data/       IniTable, MyCrypt (ZipCrypto), TextResolver (T_*.ini EN), tables/
  settings/   presets Low→Ultra, bindings
  perf/       PerfProbe, PerfCulling
  debug/      harnais de test visuels
```

**Réseau client exact** :
- `System.Net.Sockets.TcpClient { NoDelay = true }` + `NetworkStream` —
  **pas** `StreamPeerTCP` Godot (`net/EdenConnection.cs:21-43`).
- Handshake RSA→RC4 identique au serveur (clé RC4 5 o en PKCS#1 v1.5).
- Réception : `Task.Run(ReadLoopAsync)` → `List<byte> pending` +
  `RemoveRange(0,n)` (**O(n) par trame**) → `Channel<Frame>` unbounded.
- Dispatch : `EdenSession.OnZone(ncid, handler)` dict, invoqué sur le thread
  réseau → repost sur main thread via `ConcurrentQueue` drainée en `_Process`.
- **3 connexions TCP séparées** login:6543 → world:5200 → zone:5201 ;
  ticket:2067 codé mais inutilisé dans le flux standard.
- Mouvement : **client-authoritative**, `MoveSelf` ~10 Hz, écho 264 ne
  corrige que si drift > 5 u. Entités distantes : interpolation 150 ms +
  extrapolation bornée 250 ms. **Pas de heartbeat ni timeout applicatif**
  (half-open TCP non détecté).

## 2.3 Assets, chargement & pipeline

**Inventaire `godot_project/assets/` (~22 G, gitignoré, mesuré) :**

| Sous-dossier | Taille | Contenu | Source |
|---|---|---|---|
| `textures/` | **9,3 G** | **58 057 PNG** convertis depuis DDS (DXT1/3/5, BC5/BC7) | `tools/textures/convert_dds_to_png.py` (Pillow→ImageMagick fallback) |
| `models/` | **3,3 G** | GLB convertis depuis 11 547 `.nif` Gamebryo 20.2/20.3 | `tools/nif2gltf/` |
| `anims/` | **9,0 G** | GLB anims (4 266 `.kf`) + `.kfm.json` manifests ; **fichiers de 88-180 M** | `tools/nif2gltf/` |

0 `.nif` résiduel. 124 PNG natifs copiés tels quels. `ui_textures/` (42 M) :
48 PNG CEGUI + `FONT01.ttf` + theme `.tres`, hors `assets/` pour import Godot.

**Pipeline `tools/nif2gltf/` — Python 3 + numpy seul** (ni Blender ni pyffi) :
lecteur NIF générique piloté par `nif.xml` (schéma niftools), writer GLB 2.0
minimal, **skinning complet** (JOINTS/WEIGHTS, IBM, SkinPartition), DAG→tree,
matériaux→pbrMetallicRoughness, B-splines resamplées 15 Hz, textures par URI
relative. Qualité prouvée (5555/5573 bones, 35/35 rotations). `unpacker/` =
extracteur `.pkg` Reforged (stdlib, 78 233 entrées vérifiées).

**Chargement runtime — hors pipeline d'import Godot** (`assets/` sous
`.gdignore`, rien n'est importé) :
- `AssetCatalog` : GLB via `GltfDocument.AppendFromFile` → proto caché →
  `Node.Duplicate()` par instanciation ; textures `Image.LoadFromFile` →
  `ImageTexture` (RGBA8 non compressé, **copie CPU conservée** — ~576 M VRAM +
  ~1,9 G RSS/instance mesurés).
- Préchargement GLB `Parallel.ForEach` borné ; Duplicate séquentiel (le
  parallèle casse l'ObjectDB — revert documenté).
- `gamedata/` : `client.cfg` (endpoints) + `settings.cfg` ; en package :
  `uidata/` CEGUI, `data/{db,scene}/*.ini` lus **en clair au runtime**,
  `map/model/Sxxx/*.{fsm,ini,area}` (sans .nif).
- `MapLoader.LoadMap` : `.fsm` (15-20 M, vues paresseuses) → terrain
  1 ArrayMesh/tuile + splat RGBA + eau + herbe MultiMesh → objets GLB +
  PropBatcher (modèle × cellule 64 u) → env `Sxxx.ini` (soleil 4-splits, fog,
  SkyDome, météo) → entités NPC. **Chargement synchrone ~2 s** hors écran ;
  le reload à la migration (`OnNodeData`) gèle le main thread.
- `LoadingScreen` : phases réelles (ticket → connexion → parse `.fsm` en
  `Task.Run` → précharge par lots de 48), overlay survivant au
  `ChangeSceneToFile`.
- **5 shaders maison** : `terrain_splat` (5 couches + vertex-color baké),
  `water` (frames .ifl 12 fps), `toon_lit` (look anime + rim), `sky_dome`,
  `fog_ceiling` (post-process plafonnant le fog).
- `tools/` : `NetTests` (xunit, **275 tests**), `NetSmoke` (headless
  RSA+RC4 rejouant login→zone), `package_dist.sh`, `gen_ncids.py`.
- **Distribution** : package autonome ~1,3 G hors assets (binaire 105 M +
  uidata 83 M + data 67 M + maps 968 M), **~23 G avec `--full`**.

## 2.4 État d'avancement client

Fonctionnel live : login→channel→sélection→loading→**S002 jouable** ;
entités 298/265/326 spawn/move ; dialogue NPC→shop réel ; transport ;
banque ; auto-attack + cast → dégâts/mort prouvés ; HUD CEGUI avec drag
persisté. WIP : création perso à éprouver, ~1 200 NCIDs zone hors entrée
non implémentés (guildes, enchères, PvP, mariage…), aucun retry/retour
arrière, canal auxiliaire absent, heartbeat absent.

## 2.5 Faiblesses client & pistes d'optimisation

1. **9,3 G de PNG décompressés en VRAM RGBA8** — les DDS sources étaient déjà
   en BC1/3/7 : la conversion a décompressé puis re-gonflé. Pistes :
   (a) `Image.LoadDdsFromFile` sur les BC d'origine (saute la conversion) ;
   (b) KTX2/Basis Universal (`.ctex`/glTF `KHR_texture_basisu`, ÷4-6 VRAM) ;
   (c) pipeline d'import Godot en VRAM-compressed pour release.
2. **GLB d'anims 88-180 M** (float32 non quantifié) : `KHR_mesh_quantization`,
   meshopt/Draco, dédup de clés, ou bake en `AnimationLibrary` `.res`.
3. **SessionCarry statique + binds paresseux dupliqués** (≥4 classes) → en
   mode `--live` direct, combat/fenêtres/sac perdent leurs binds.
4. **Reload de map synchrone** à la migration (gel multi-secondes) — le
   chemin async existe déjà (`LoadWorldAsync`).
5. **Bug connu** : `CollisionBuilder.Collect` double le transform racine →
   colliders instanciés déportés (`PERF.md:151-161`).
6. Hot path : `List<byte>.RemoveRange` par trame réseau ; picking raycast +
   `UnprojectPosition` sur **toutes** les entités chaque frame (à throttler
   10-15 Hz) ; `[wire]` hex-dump chaque trame en live (à flaguer).
7. `GameScene.cs` monolithe ~1520 l. (chargement + dispatch + HUD + pilotes
   QA en production) ; `async void` fire-and-forget ; fuites RID à la sortie
   forcée ; pas de heartbeat → half-open indétecté.
8. Spikes ~150 ms compilation shaders à chaud → pre-caching pipeline Godot ;
   terrain reconstruit à chaud depuis `.fsm` (177 MeshInstance3D sur S001) →
   bake offline possible ; streaming par chunks 64 u (données déjà découpées).
9. `ui_textures/` + `.tres` gitignorés (fragile) ; `.ttf` non embarqués dans
   le pck ; `nif_report.txt` écrasé à chaque run incrémental.
10. Sécurité héritée : mdp = `md5(mdp)`, déplacement client-authoritative —
    conforme au natif.

## 2.6 Recommandations d'optimisation client (pour évaluation externe)

- **Godot 4 + C# est un choix cohérent** pour ce portage (licence libre,
  .NET partagé avec le serveur, export Win/Linux). Les alternatives (Unity,
  Unreal, custom) n'apporteraient rien à ce stade — le goulot est les
  **assets**, pas le moteur.
- Priorités : compression textures GPU (KTX2/Basis ou DDS natif) >
  quantization anims > streaming chunks > correction colliders > heartbeat +
  timeouts > refactor `GameScene`/session.
- Réseau : garder `TcpClient` (correct), mais remplacer le buffer par un ring
  buffer/`Span`, ajouter watchdog. Si le serveur devient Godot-only un jour :
  canal UDP/ENet Godot (`ENetConnection` intégré) envisageable pour le
  mouvement — aujourd'hui impossible, les clients natifs exigent TCP.
- Le partage de code serveur↔client (BinStream, Rc4, codecs NCID) est déjà
  miroir manuel — une lib `.csproj` commune éliminerait la divergence.

---

# PARTIE 3 — SYNTHÈSE « recoder le même jeu, stack optimisée »

| Couche | Actuel | Verdict | Piste réaliste |
|---|---|---|---|
| Runtime serveur | .NET 10, async/await, Channels | **bon** — garder | System.IO.Pipelines, hosting/OTel |
| Transport | TCP+RC4 fidèle natif | **imposé** tant que clients natifs | NoDelay immédiat ; UDP seulement si Godot-only |
| Concurrence | acteur maison + files bornées | **bon**, couvre le besoin | sortir la DB du fil acteur |
| Persistance | Dapper/Npgsql/PG12, DB-as-truth | **limite MMO** | memory-first + write-behind généralisé ; PG16 |
| Moteur client | Godot 4.7.2 C# | **bon** | garder |
| Assets client | PNG 9 G + GLB anims 9 G non compressés | **principal goulot** | KTX2/Basis, quantization, streaming |
| Protocole client | ~161/1200 NCIDs, pas de heartbeat | WIP normal | ring buffer, watchdog |
| Déploiement | compose simple, PG12, secrets en clair | dev-grade | healthchecks, OTel, migrations `\connect` |

**Points d'attention pour l'IA externe** : la fidélité wire aux clients
natifs **contraint** toute refonte réseau ; le modèle « DB-as-truth » est le
vrai plafond de charge côté serveur ; le poids des assets est le vrai goulot
côté client. Le reste de la stack est sain et bien au-dessus du niveau
« serveur privé » habituel (preuves byte-à-byte, acteur, backpressure, CI).

*Fin du rapport — généré par analyse statique des sources citées.*
