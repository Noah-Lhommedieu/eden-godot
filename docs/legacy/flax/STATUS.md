# STATUS — parité eden-flax vs client de référence

Matrice vivante. Légende : `[ ]` non commencé, `[~]` en cours/partiel,
`[x]` prouvé (capture/trace vs référence). Référence Godot : dépôt
`../portage/` (commit de réf. à noter par jalon).

## Jalons (PLAN-PORTAGE-FLAX §10)

| Jalon | État | Preuve |
|---|---|---|
| M0 — Dépôt + DB-1 (PG17/Redis, bases edenfr, serveur isolé vert) | [x] | stack compose converge + 1416/1416 tests |
| M1 — Flax installé, eden_flax compile, Eden.Core autonome, 540 tests verts | [x] | Flax 1.13.7012 ; `flax-build.sh -build` Done ; 714/714 tests |
| M2 — Réseau login→zone headless | [x] | NetSmoke (Eden.Core) login→world→zone vs stack locale : trace NCIDs conforme, entrée zone char #200000001, keepalives 57/9057 — `eden_server/captures/netsmoke-m2-trace.txt` |
| M3 — Première map (ini/fsm/smp/dds/area, terrain, caméra, S002) | [x] | **2026-11-01 soir : terrain réellement visible** (winding inversé corrigé) + dôme ciel NIF + splat/eau ; rendu prouvé sur s001/s002/s003/s005/s009/s011 (captures in-engine `shots/map_n*.png`, `shots/ui3.png`, non versionnées). | S002 chargée en ~70 ms : 49 tuiles, 7/7 DDS **texturées** (EdenTerrainMaterial unlit Color+Emissive), fog EDENFR calibré, caméra orbitale au spawn REVIVE. Splat 5 couches = couche 0 (shortcut), eau **translucide** (EdenWaterMaterial, `gen_mat.py --water`, texture 1x1 teintée alpha 0.65 ; drift/flipbook/onde non faits) — M4-SPEC.md. Diagnostic 2026-11-01 : « caméra noyée » = eau opaque qui bouchait la vue (pas la caméra) ; capture `shots/water-s002-crop.png` |
| M4 — Entités (NIF/KF subset, joueur, spawn/move/interp) | [~] | **2026-11-01 soir : joueur textures + PNJ/monstres visibles** (racine causale : `MaterialSlotIndex` jamais posé → tous les meshes sur le slot 0 ; racines Eden sans recalcul de transform monde). Reste : visage joueur, animations vues en live, props « drum/bannières » aux feuillets blancs au spawn. | NifFile + NifObjectLoader : S009 870 acteurs props, joueur g.nif ; KF/KFM joués via pipeline skinned GPU (`SkinnedRigFactory` : SkinnedModel+AnimatedModel, offset=BoneSkin — vérifié vs source Flax 1.13 ; poses poussées/déformées confirmées par probe). **Rendu non confirmé** : caméra noyée dans terrain sur tous les spots testés (bug préexistant, identique en statique). Manque : alpha-scissor, props des 15 maps V7 |
| M5 — HUD cœur + écrans (CeguiToFlax, P0, login→loading) | [~] | `Eden.Game/Ui/` : UiAtlas (PNG imagesets, brosse 9-slice UV), CeguiToFlax (UiNodeSpec→Control), UiHost (`EDEN_UI=<layout>`) ; build Flax 0 err, 800/800 tests ; rendu vérifié `EDEN_MAP=none EDEN_UI=login|characterwnd` : 34 imagesets, 30/8 contrôles, 0 type inconnu, **atlas manquants 0** — chrome natif (titlebar gradient + caption, cadres métal, editbox, ornements) texturé via `Texture.FromMemory`+`WaitForLoaded` ; **FONT01.TTF (Sarasa ~24 Mo posée dans uidata/fonts/, stub corpus 367 Ko rejeté par sanity cmap) rasterisé MSDF** = Latin+CJK nets, police par défaut des layouts et des écrans ScreenKit ; 9-slice marges réduites si rect < frame, composite background→cellule centrale. Reste : tiling vrai des bords non fait (étirement), login_bg non intégré au mode direct, fidélité globale vs ref-shots non rejouée |
| M6 — Gameplay (combat, quêtes, dialogues, boutiques, portails, montures) | [~] | Live login→zone OK vs serveur local (`test123`, `EDEN_TLS_INSECURE=1`) : handshake+ticket, entrée s002, 3 bridges (Entities/Hud/Audio) attachés, HUD live monté (« Monts Calcair »), entités NCID reçues, `ZoneMoveSelf` 10 Hz. **Corruption tas RÉSOLUE** : bug Flax `StringAnsi::Set` (buffer dimensionné en chars UTF-16 puis `ConvertUTF162ANSI` y écrit 1-4 o/cp UTF-8) — tout log non-ASCII piétinait le heap via `Logger::Write -std` ; fix = `EdenLog` translittère ASCII, run live 2min55 sortie propre 0 crash (commit cb34048). Rigs figés bind pose par défaut (`EDEN_RIG_ANIM=1` debug — UpdateMesh/frame reste suspect UAF draw-jobs). **Entités LIVE RÉSOLU** : le serveur n'émettait rien car `player_limitless` absente → `PrefetchEntryAsync` avortait TOUTE l'entrée (InstallEntry jamais appelé) — migration 021 + PrefetchOrNull → burst complet (313 appliqué au spawn) + **vrais spawns sight reçus** (PNJs FR à l'écran). `EDEN_FAKE_ENT` reste utile en debug. ⚠️ Gel ~17 s post-entités : suspect = `DebugLogWindow` O(n²) (re-parse RichTextBox par ligne de log spawn — `EDEN_ENT_SPAWNLOG=0` kill-switch ajouté) — à confirmer. **Gameplay livré (commités, 798/798 tests)** : combat `f0b4d3d` (picking cible, clic D engage, R attaque, auto-attaque, cadre cible 444, floaters dégâts, mort/réz 305/306), dialogues/quêtes/portails `5fd46e8` (285/388 talk+fiches, journal J 348/391/345/293, marqueurs !/?, GATEWAY→15), monture `bda8ca1` (T, 283 rideKey, visuel selle L17, vitesse serveur, sac 312/274/275/291). Sorts/cast `a1b678a` (envois 9/13/16/17 + break 13, binds 279/281/286/287/302/311/313/368/410, barre castingbarwnd native, hotbar icônes+cd sweep). Fidélité UI `144d859` (FONT01 MSDF, WaitForLoaded → fin panneaux blancs, titlebar/chrome natifs). **E2E live prouvé (run 10:20)** : login→channel→perso→s002, **9 bridges attachés**, 19×298 reçus → PNJ FR réels spawnés (Cynthia, Marchand d'Armes Corne, Contremaitre Laz…), minimap « Monts Calcaires » X/Y live, tracker « Votre première Quête I » (348 serveur), chat FR, spawn intérieur village via pos 313. Boutiques : shopwnd natif + wire 304/22/23/63/342-343 — **rendu live prouvé** (onglets Achat/Vente, 10 articles FR réels avec prix). Journal quêtes ouvert live (`EDEN_HUD_OPEN=questlog` — quête 1009 réelle). Minimap X:309,Y:315 = pos 313 serveur. **M6 fonctionnellement complet.** Reste : input clavier injoignable en -play éditeur sous XWayland (XTEST ok, `Input.GetKeyDown` muet — à trancher via FlaxGame cooké ; clics souris non tranchés non plus), chase-cast + ground-target UI, nom GM CJK « ?? », monture non vue faute d'item ride dans le sac test, teardown-NRE atténué. **Standalone cooké (player8) + EdenKeys : boucle quête complète prouvée** — `//tele` (NCID 3 GM) → reposition 313 (fix `PlayerController.Teleport` : EnterNode replace l'acteur, avant = désync) → F→TalkToNpc→285→`opt`→ReplyDialog→388 op=2→MissionListOp→388 op=4 → quête 1010 rendue (+38 EXP chat, marqueurs 4→3). Bug picking : famille NPC = 0x88xxxxxx entière (6a90993). Clic OS Wayland débloqué via uinput REL-only (ReplyDialog idx=1 live). Reste : chase-cast, ground-target, monture sans item, freeze Render2D intermittent (UiAtlas innocent — 3,3k sommets/frame sain). **Combat + boutique live prouvés (player8)** : `tele`→`r`→engage Lv1 Champignoïde → swings→kills ×6 (+500 EXP classe, skill « Metal Bluster ») ; marchand 50172 → F→304→shopwnd 10 articles FR → `buy`→NCID 22→**BuyUp 308** ×2 ; **sorts live** `alpha1-3`→casts #50011/#50038/#50150→279 OK (ciblé 0x0bebc201) ; **transport+migration node prouvés** — Passage 50360→F→393→40 routes→`route`→NCID 88→`M3 s001 chargée`/map Aven rechargée (TransportWindow+ShopBridge bind, teardown map+MapUnloading, fix VFS WalkInsensitive DFS s001/S001 ; EdenKeys `route@t:N`) |
| M7 — UI complète + i18n + audio | [~] | Lots P1/P2/audio/i18n fusionnés sur master (2026-11-01), 890 tests verts ; rendu live non rejoué fenêtre par fenêtre — aucune preuve visuelle M7 |
| M8 — Packaging (.epk/.inib) + perf | [~] | edenpack/boot-check/PerfProbe fusionnés ; cook player reproductible réparé (audio Options, DLLs framework) — boot packs seuls non rejoué |
| M9 — Parité visuelle (≥6 maps, multi-joueurs) | [ ] | |

## Flax — installation (pinnée)

- **Version** : Flax **1.13.7012** (`1.13+f5a6aacc`, archives fournies).
- **Racine** : `~/Applications/Flax` (hors dépôt — binaires non
  versionnés). `FLAX_ROOT` env pour override.
- **Éditeur** : `Binaries/Editor/Linux/{Release,Development}/FlaxEditor`.
- **Game cooker** : `Binaries/Game/x64/{Debug,Development,Release}` —
  lié sous `Source/Platforms/Linux/Binaries/Game` (chemin attendu par
  Flax.Build).
- **Toolchain** : pas de clang système → clang 20 ROCm
  (`/usr/lib64/rocm/llvm/bin`) exposé via bind-mount userns du
  resource-dir dans `tools/flax-build.sh` (pas de sudo, pas d'écriture
  `/usr`). Si un vrai paquet `clang` est installé plus tard, le wrapper
  reste fonctionnel.
- **Build** : `tools/flax-build.sh -build -buildtargets=EdenGameTarget
  -platform=Linux -arch=x64 -configuration=Development` (et
  `EdenEditorTarget`). Génération projet : `tools/flax-build.sh
  -genproject` (produit `eden_flax.slnx`, non versionné).
- **Prérequis -build** : `dotnet build eden_flax/Source/Eden.Core` —
  `Eden.Game` référence la DLL `net10.0` via
  `ScriptingAPI.FileReferences` (Flax ne prend pas les csproj en
  FileReferences).

## Player standalone (cooké) — NOUVEAU

- **Cook** : `EdenCookPlugin` (EditorPlugin, env `EDEN_COOK=<out>` →
  `GameCooker.Build(LinuxX64, Development)`) — 102 fichiers produits.
  ⚠️ L'éditeur doit être lancé dans le namespace `unshare` du wrapper
  (SDK check Linux hérite du bind-mount clang).
- **Bug moteur RÉSOLU (patch binaire local)** : `StringAnsi`
  (`Set`/`Append`/ctor `StringView`) alloue `len+1` octets puis
  `ConvertUTF162ANSI` écrit jusqu'à 4 o/cp → corruption heap dès tout
  log UTF-8 (crashs `free(): invalid size` pendant cook ET runtime).
  Patch `.so` : allocation `4*len+1` sur les 3 sites — appliqué à
  l'éditeur ET au `.so` copié dans l'output. **Non reproductible** :
  à refaire après toute réinstall Flax (ou fix upstream).
- **DLLs runtime manquantes** dans l'output cooker : `ZstdSharp.dll`
  (réf. `Eden.Core`), `System.Numerics.Vectors.dll`,
  `System.Text.Encoding.CodePages.dll` (implémentations du runtime
  pack, pas les ref-assemblies) — copiées à la main ; la voie officielle
  deploy doit être revérifiée.
- **DDS runtime** : `Texture.FromMemory(DDS)` en build cookée passe
  par l'importeur → zenity modal **par texture** ; `DdsTextureLoader`
  utilise le chemin upload-mips hors éditeur.
- **Résultat** : player standalone tourne — login→channel→perso→
  `EN JEU sur s002`, 10 bridges, monde+perso rendus, HUD complet,
  tracker quêtes réelles. Terrain encore gris (diffuse DDS à revoir).
- **Input** : pointeur uinput OK en jeu (drag → rotation caméra,
  clics UI). Clavier OS (XTEST **et** uinput kernel) **jamais livré**
  sous Wayland — limitation compositor, pas bug client. Repli diag :
  `EdenKeys` (`EDEN_KEYS="j@8,t@11"` = queue scriptée fusionnée à
  `Input`) — **prouvé : T→`[ride] pas d'item monture`, handlers réels**.
- ⚠️ **Perte de focus = jeu gelé** (temps figé ~21 s, frames non
  rafraîchies) : garder la fenêtre au premier plan pendant les tests.

## Eden.Core (module pur — 714/714 tests verts)

| Système | Source | État | Preuve |
|---|---|---|---|
| Net (TCP/TLS/RC4/BinReader/Varint/ReceiveBuffer/Version/Manifest) | `scripts/net/` | [x] copié | build net10.0 + tests |
| Protocol (647 NetMessage + helpers) | `scripts/protocol/` | [x] copié | |
| Session (EdenSession login→world→zone) | `scripts/session/` | [x] copié | |
| Data (IniTable, TableBin, TextResolver, MyCrypt, EdenEncoding, 25 tables, UiTextEn…) | `scripts/data/` | [x] copié | encodage FR : `t_*` Latin-1, `c_*` Big5 |
| Vfs (Epk*, EdenVfs loose+packs, `ResolveInsensitive` casse+préfixe) | `scripts/vfs/` | [x] copié (branches GODOT retirées) | |
| Formats (Fsm, NodeMap/smp, Ifl, Pathfinder, TerrainSampler, GrassLayout, WorldConv `System.Numerics`) | `scripts/world/` | [x] copié | |
| Game pur (EdenDir, DirectionalLocomotion, InterpBuffer, RideSeatMath, ServerMoveSpeed, CastLock, CombatFx, CooldownTracker, PickCore, GridResizeMath→Ui, SceneBubbleLogic, CinemaTimeline, MonsterActionAnim, AnimPathMap, CharModelSpec, CostumeVisual, GlbAnimFile, PetClientRules, MotionTable, CombatMessages) | `scripts/{game,player,input,combat,scene}/` | [x] copié | |
| Ui (Cegui{Layout,Imageset,Scheme,LookNFeel,Builder}, UiNodeSpec) | `scripts/ui/` | [x] copié | 19 layouts EDENFR corrompus source = liste connue, non fabriqués |
| Formats NIF/KF/KFM (loader C# schema-driven) | à porter depuis `tools/nif2gltf` | [ ] | |

## Serveur (track DB §8.3)

| Phase | État | Sortie |
|---|---|---|
| DB-1 : compose PG17+redis, restore, `dotnet test` vert | [x] | PG17.11+Redis7 healthy 5433/6380 ; 1416/1416 ; zone↔world↔mission établis |
| DB-2 : fusion 3→1 base à schémas + FKs | [x] | base `edenfr` (schémas game/account/member, `Database/022`+`023`), `DbConnectionFactory` (`EDEN_DB_CONN` + search_path/rôle), FK `player_characters.account_id→account.accounts` ; sources `edenfr_*` intactes (rollback) ; 1421/1421 |
| DB-3 : tickets + mutations chaudes → Redis, WriteBehind 15 s | [x] | `RedisTicketStore`, `HotCharacterStore` (or/position/vitals), flush `CharacterRepository.FlushHotAsync` ; `EDEN_REDIS_CONN` (absent = PG) ; 1429/1429 |
| DB-4 : charge ~300 CCU | [x] | 300/300 entrées ; p95 or 21→4,8 ms, position 19→0,01 ms, tickets 35→3,5–7,8 ms ; `eden_server/docs/DB-4-LOAD-TEST.md` |

Adaptations dump PG17 faites dans `eden_server/` (copie, source
`portage/` intacte) : `WITH OIDS`/`SET default_with_oids` retirés de
`database.out`, `\connect edenfr_db` préfixé aux 16 migrations sans
connect explicite, montages `004`/`008` ajoutés au compose,
`ALTER player_works ADD finish_at` rendu idempotent dans `004`,
migration `017` : colonnes `appearance_data`/`create_flag` du dump
legacy (requis par la liste persos).

## Décisions actives

- **EDENFR pur** : `corpus/fr-full` seul ; `corpus/official` exclu ;
  compléments depuis `original-fr` si manque.
- **Zéro code mort** : branches `#if GODOT` supprimées à la copie ;
  `WorldConv` réécrit `System.Numerics`.
- Formats natifs chargés directement ; corpus GLB/PNG = oracle A/B
  (régénéré seulement si besoin).
- `limitless_*`/`player_limitless` absents d'EDENFR (Reforged lvl95+
  hors périmètre) — `LimitlessService` tolère l'absence par design.
- `database.out` non versionné (même convention que portage — dump
  local requis pour l'init DB).
