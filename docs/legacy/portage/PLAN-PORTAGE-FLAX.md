# PLAN — Portage du client Eden vers Flax Engine (dépôt isolé)

> **Statut** : plan rédigé, **rien n'est exécuté**. Synthèse de 5 inventaires
> d'agents (monde/rendu, UI, réseau/gameplay, données/VFS/outillage,
> spécification des formats natifs) + le rapport externe
> « Analyse du Portage vers Flax Engine ».
>
> **Décisions directrices (utilisateur)** :
> 1. **Nouveau dépôt git séparé** (sibling de `portage/`) — copie du
>    serveur, des données et de l'outillage. **Zéro impact** sur le projet
>    Godot : `portage/` devient référence en lecture seule.
> 2. **Contenu officiel EDENFR privilégié au maximum** — le projet est un
>    recodage complet du jeu d'après les clients officiels.
> 3. **Formats natifs chargés directement dans Flax** (`.nif`, `.kf`,
>    `.kfm`, `.dds`, `.fsm`, `.smp`, `.area`, `.ini`, `.ifl`, `.layout`,
>    `.imageset`, `.looknfeel`, `.scheme`, `.font`, `.pkg`) — aucune
>    conversion offline. Le corpus converti (GLB/PNG) = oracle de
>    validation croisée + repli par fichier.
> 4. **Zéro code mort** : le code de conversion Godot et tout le code
>    moteur-couplé n'est PAS copié — qualité maximale, on repart propre.
> 5. **Architecture complète** dès le jour 1 : dossiers IA (règles,
>    plans, complétions), règles de code spécifiques Flax, tests.
> 6. **EDENFR pur — FR uniquement** : client ET serveur ne consomment
>    que le contenu du client officiel français (objets, quêtes, textes,
>    tables, scènes). **Pas de corpus `official` (merge EN/Reforged/FR)
>    et pas de bascule EN** — l'utilisateur soupçonne le merge FR/EN
>    d'être mal mélangé ; la valeur sûre = EDENFR seul. L'anglais sera
>    réétudié plus tard, sur données propres.

---

## 0. Cadre et non-objectifs

- **Le serveur change de place mais pas de contenu** : copie intégrale
  `eden_server/` (code, migrations `Database/`, `server_us/` runtime
  oracle 200 M, scripts, docs, `.env` copié en local mais gitignoré).
  Lui aussi est isolé — les expérimentations Flax ne touchent jamais
  `portage/eden_server`.
- **Le client Godot reste intact** dans `portage/` : jouable, référence
  comportementale et source de vérité des specs. Rien n'est modifié
  dedans, jamais.
- **Flax 1.13+** (sorti 21/10/2026, base **.NET 10** = parité serveur).
  EULA : gratuit < 250 k$/trimestre, 4 % au-delà — non commercial → OK.
- **Périmètre = le client complet + audio** (`.ogg`/`.wav` natifs Flax —
  absents du client Godot).

---

## 1. Le nouveau dépôt

### 1.1 Emplacement et structure

```
/home/ster2lune/Bureau/reverseden/
├── portage/                      ← EXISTANT, devient lecture seule
└── eden-flax/                    ← NOUVEAU DÉPÔT (git init, remote à créer)
    ├── README.md
    ├── AGENTS.md                  ← règles racine (copie adaptée de portage)
    ├── CLAUDE.md                  ← quick-start
    ├── .gitignore
    ├── .devin/
    │   ├── rules/                 ← ponytail.md + flax-code-quality.md (nouveau)
    │   └── skills/                ← skills projet copiés/utiles
    ├── .claude/
    │   ├── ARCHITECTURE_MAP.md    ← régénéré pour cette arborescence
    │   ├── QUICK_START.md         ← commandes build/test/run du dépôt
    │   ├── COMMON_MISTAKES.md     ← pièges connus (seed : portage/.claude)
    │   ├── completions/           ← docs de complétion par tâche
    │   └── sessions/              ← archives de sessions agents
    ├── docs/
    │   ├── INDEX.md               ← index doc (adapté)
    │   ├── specs/                 ← specs de formats & systèmes copiées :
    │   │   FSM-FORMAT.md, NETWORKING.md, RAW-ASSETS.md,
    │   │   RAW-ASSETS-UI-UIDATA.md, ANIMS-DIRECTIONNELLES.md,
    │   │   MURS-INVISIBLES-PAR-MAP.md, ETAT-SYSTEME-STATS.md,
    │   │   NATIVE-LOADING-ANALYSIS.md, GAMEPLAY-NOTES.md,
    │   │   MONTURES-L17.md, ADMIN-COMMANDES.md, CAPTURE-MATRIX.md,
    │   │   PERF.md, UX-FLOW.md, ref-shots-catalog.md,
    │   │   AUDIT-ANIMS-SORTS.md, TUTO-IA-UI-ET-BUGS.md
    │   ├── ANALYSE-DONNEES-PROVENANCE.md
    │   ├── RAPPORT-CORPUS-OFFICIEL.md, ROLLBACK-CORPUS.md
    │   ├── STACK-FICHIERS-PORTAGE.md
    │   ├── PLAN-PORTAGE-FLAX.md   ← ce document
    │   ├── harvest/               ← analyses gameplay (copié)
    │   ├── flax/                  ← docs du portage (NIF-LOADER.md,
    │   │                            CEGUI-FLAX.md, STATUS.md — parité 1:1)
    │   └── temp-plans/            ← plans d'action datés par lot IA
    ├── futur-feature/             ← 22 docs features manquantes (copié)
    ├── knowledge/                 ← wikis EN/FR + Vendetta (copié complet)
    ├── ref-shots/                 ← 105 captures natives (copié)
    ├── re/                        ← corpus reverse COMPLET : ghidra exports,
    │   │                            reindex, index.sqlite (rebuild),
    │   │                            decompile*.sh, ExportAll.java/py, docs
    ├── eden_server/               ← copie intégrale versionnée
    │   ├── server/                (Eden.sln, src/, tests/ + goldens)
    │   ├── Database/              (migrations SQL + seeds)
    │   ├── server_us/             (runtime oracle : db_decoded, scene,
    │   │                            NodeMap, config — lecture seule)
    │   ├── tools/                 (wire_diff.py, headless_client, lapis.lua,
    │   │                            extract_all_protocols.py, gen_feature_matrix…)
    │   ├── docs/                  (protocole/, generated/, MASTER-IMPL…)
    │   ├── .agents/skills/        (eden-content-editing, eden-portage-csharp,
    │   │                            eden-reverse-serveur — copiés)
    │   ├── AGENTS.md + server/AGENTS.md (copiés, chemins adaptés)
    │   ├── start-native.sh, stop, docker-compose.yml, .env.example
    │   └── .env                   (copié local, gitignoré)
    ├── corpus/
    │   ├── fr-full/               ← corpus EDENFR complet (~9,0 Go,
    │   │                            gitignoré) — SEULE source de données
    │   └── original-fr/           (352 Mo — tables/textes FR extraits,
    │                                copié aussi pour inspection rapide)
    │   (PAS de corpus/official : le merge EN/Reforged est exclu —
    │    voir décision 6 et §8.2)
    ├── client_assets -> corpus/fr-full    ← nom d'oracle lisible (lien ou
    │                                        config ; équivalent raw_assets)
    ├── tools/
    │   ├── edenpack/              (copié — SANS les commandes `bake*` Godot :
    │   │                            build/diff/apply/verify/bake-tables gardés,
    │   │                            bake/bake-tex réécrits pour Flax plus tard)
    │   ├── nif2gltf/              (copié — référence/spec NIF, génère l'oracle
    │   │                            GLB ; plus outil de production)
    │   ├── anim/                  (merge/trim GLB oracle — référence)
    │   ├── textures/              (dds→png oracle — référence)
    │   ├── unpacker/pkgfs_unpack.py
    │   ├── NetTests/              (porté : xUnit .NET pur, hors moteur)
    │   ├── NetSmoke/, HeadlessBot/ (ports C#)
    │   ├── audit/                 (ini_audit.py, build_official.py, gen_fr_supplements.py)
    │   ├── gen_ui_text.py, import_edenfr_overlay.py, fix_nodemaps.py
    │   ├── gen_ncids.py, pack_audit.py, walls_report.py, fsm_seam_dump.py
    │   └── package_dist.sh        (adapté distribution Flax)
    └── eden_flax/                 ← LE PROJET FLAX (voir §2)
```

### 1.2 Manifeste de copie — quoi copier / adapter / laisser

| Élément | Action | Justification |
|---|---|---|
| `eden_server/` intégral (dont `server_us/`, `.env`, `.agents/`) | **copier verbatim** | le serveur tourne en local isolé ; `server_us/` = runtime oracle (db_decoded, scene, NodeMap) lu par `start-native.sh` |
| `corpus/fr-full` (~9,0 Go) + `corpus/original-fr` (~352 Mo) | **copier** (rsync, gitignoré) — `corpus/official` **PAS copié** (merge exclu) | contenu EDENFR pur uniquement (décision 6) |
| `re/` (ghidra exports, reindex, scripts, `re/AGENTS.md`) | **copier verbatim** | spec exécutable ; `index.sqlite` régénéré par `reindex build` |
| `knowledge/`, `ref-shots/`, `futur-feature/` | **copier verbatim** | données d'information des anciens clients (consigne) |
| `godot_project/gamedata/` | **copier verbatim** | données runtime engine-agnostic : client.cfg, settings.cfg, `lang/fr/`, `ui_text_*.tsv`, `levels.tsv`, `t_npc_extra`, `sknodemap/fix`, login_bg |
| `scripts/net,protocol,session,data,vfs` (moins `EdenVfs.Godot.cs`) | **copier → `Eden.Core`** | ~35 k LoC portable ; retirer les `.uid` |
| Parseurs purs monde/data/UI | **copier → `Eden.Core/Formats+Ui`** | FsmFile, NodeMapFile, IflFile, Pathfinder, TerrainSampler, GrassLayout, WorldConv, EdenDir, DirectionalLocomotion, InterpBuffer, RideSeatMath, ServerMoveSpeed, CastLock, CombatFx, CooldownTracker, PickCore, GridResizeMath, SceneBubbleLogic, CinemaTimeline, MonsterActionAnim, AnimPathMap, QuestCatalog/ObjectiveLogic, PlayerBag/Coins/Titles/Achievements/ProfileState/StateLocks, MiscPackets, NativeItemRecord, AppearanceCodec, HeroTraitWire, tables/ (25), TextResolver, UiTextEn, ClientUiSettings, Cegui{Layout,Imageset,Scheme,LookNFeel,Builder}, UiNodeSpec |
| Outils listés §1.1 | **copier/adapté** | toute la chaîne RE/packaging/tests doit vivre dans le nouveau dépôt |
| Code moteur-couplé Godot (`world/` builders, `game/` visuel, `player/` contrôleurs, `hud/` fenêtres, `screens/`, `combat/` nodes, `GameScene`, `AssetCatalog`, `DataPaths` partie Godot, `EdenVfs.Godot.cs`, `EdenGltfMipmaps`, `ToonMaterializer`…) | **PAS copié** | reste dans `portage/` comme référence ; réécriture sur Flax. Élimine le code mort |
| `scenes/*.tscn`, `shaders/*.gdshader`, `ui_textures/`, `.uid`, `.import`, `.gdignore`, `project.godot`, csproj/sln Godot, `export_presets.cfg`, `dist/` | **PAS copié** | spécifique Godot — remplacé par équivalents Flax (`ui_textures/` devient inutile : les PNG sources vivent dans `uidata/` du corpus) |
| `docs/` analyse + `eden_godot/docs/` specs | **copier sélectif** (liste §1.1) | specs moteur-agnostic ; docs de process Godot (MASTER-PLAN, temp-plans archive) laissés |
| `godot-shots/`, `run_*.log`, `switch-corpus.sh`, `.code-review-graph*` | **PAS copié** | artefacts locaux |
| `tools/ini2cs/` | **n'existe pas** (déjà vide) | — |
| Corpus GLB/PNG converti (`godot_project/assets/`, 27 Go) | **régénéré dans le nouveau dépôt seulement si l'oracle A/B en a besoin** (nif2gltf est recopié) — gitignoré | sinon 27 Go pour rien : les loaders natifs le rendent superflu au runtime |

**Règle d'or de l'isolement** : aucun chemin absolu vers `portage/` dans
le nouveau dépôt — tout pointe en relatif (`../corpus/official`,
`../server_us/`, `./client_assets`). `start-native.sh` dépend de
`$EDEN_ROOT/../corpus/official` : la disposition sibling est reproduite
telle quelle, donc le script fonctionne sans retouche.

### 1.3 Initialisation git

- `git init` + premier commit = squelette (arborescence + AGENTS + docs,
  avant tout code). Remote GitHub **déjà fixé** :
  `https://github.com/Noah-Lhommedieu/eden-reverse.git` — push uniquement
  sur demande, même attribution (auteur Noah Lhommedieu,
  Co-Authored-By Devin).
- `.gitignore` : `Binaries/`, `Cache/`, `Content/` cuit, `corpus/`,
  `client_assets`, `Assets.Native`, `*.epk` générés, `.env`, logs,
  shots, `re/index.sqlite` (rebuild).
- **Gros fichiers** : pas de LFS — tout ce qui est volumineux est
  gitignoré (corpus, packs, binaires). Le dépôt reste du code+docs.

---

## 2. Le projet Flax — `eden_flax/`

```
eden_flax/
├── EdenFlax.flaxproj
├── Source/
│   ├── Eden.Core/             module C# PUR (zéro Flax, csproj autonome)
│   │   ├── Net/               TCP, SslStream/TLS1.3+pinning, Rc4,
│   │   │                      BinReader/Writer CP950, VarintCodec,
│   │   │                      ReceiveBuffer, VersionProtocol, ManifestHash
│   │   ├── Protocol/          647 NetMessage + helpers (~26 k LoC)
│   │   ├── Session/           EdenSession (état login→world→zone)
│   │   ├── Data/              IniTable, TableBin, TextResolver, MyCrypt,
│   │   │                      EdenEncoding, tables/ (25), UiTextEn, …
│   │   ├── Vfs/               Epk*, EdenVfs (loose + packs .epk)
│   │   ├── Formats/           Fsm, Smp(NodeMap), Ifl, Area, SceneTable,
│   │   │                      MapEnvIni, WorldConv, EdenDir, InterpBuffer,
│   │   │                      Pathfinder, **Nif*, Kf*, Kfm* (§3.2)**
│   │   └── Ui/                Cegui{Layout,Imageset,Scheme,LookNFeel},
│   │                          CeguiBuilder, UiNodeSpec (modèle neutre)
│   ├── Eden.Native/           module C++ (optionnel, phase perf)
│   │   └── hot paths : RC4, décodeurs NIF/KF, splat, decompression
│   ├── Eden.Game/             module C# couplé Flax
│   │   ├── World/             MapLoader→actors, terrain proc-mesh, sky,
│   │   │                      water, fog, grass instancing, props, colliders
│   │   ├── Entities/          EntityRegistry→AnimatedModel, anims kfm
│   │   ├── Player/            contrôleur, caméra, picking, curseurs
│   │   ├── Combat/            cast lock, auto-attack, FX, cooldowns
│   │   ├── Hud/               ~92 fenêtres → Flax UI
│   │   ├── Ui/                CeguiToFlax (UiNodeSpec→UIControl)
│   │   ├── Screens/           login/channel/select/create/loading
│   │   └── Audio/             bgm ogg par zone, sfx wav
│   ├── *.Build.cs             (Flax.Build : modules, deps, defines)
│   └── Abstractions/          ILog, IFileRoot, IImage, Rgb… (ex-#if GODOT)
├── Content/                   (quasi vide — assets via EdenVfs/runtime)
├── Tests/                     (xUnit : port des 540 NetTests + tests NIF)
├── Tools/                     (NetSmoke-Flax, harnais captures, batchers)
└── docs/ -> lien ../docs/flax/
```

`Eden.Core` compile **hors moteur** (`dotnet build/test` sans Flax) —
c'est ce qui rend les 540 tests directement portables. Les `#if GODOT`
deviennent des abstractions injectées ; **aucun code `#if GODOT` copié
tel quel n'est gardé mort** — on supprime les branches, on garde le pur.

---

## 3. Stratégie assets natifs — le cœur du chantier

### 3.1 Ordre d'implémentation des loaders

| # | Format | Effort | Débloque | Référence |
|---|---|---|---|---|
| 1 | `.ini` + `.area` + `.tsv`/`.cfg` | trivial | TOUT (tables, scènes, env, motion.ini, ridelist) | `IniTable.cs`/`EdenEncoding`/`MyCrypt` — copier |
| 2 | `.fsm` + `.smp` | moyen | première map marchable | `FSM-FORMAT.md`, `FsmFile.cs`, `NodeMapFile.cs` — copier |
| 3 | `.dds` | trivial→moyen | textures partout | **import natif Flax** ; runtime `Texture` depuis bytes ; tolérer tronqués |
| 4 | `.ifl` | trivial | eau animée | `IflFile.cs` — copier |
| 5 | `.nif` + `.kf` + `.kfm` | **LOURD** | props, persos, mobs, montures, anims | `tools/nif2gltf/` (nif.xml 8 667 l.) → **porter en C# schema-driven** |
| 6 | `.layout`/`.imageset`/`.scheme`/`.looknfeel`/`.font` | moyen | UI complète | `Cegui*.cs` parseurs copiés, matérialiseur réécrit |
| 7 | `.pkg` | trivial | extraction corpus | `pkgfs_unpack.py` |
| — | `.csm`, `.fsmp`, `.pgp`, `.smp2` | inconnu | **hors périmètre** (inutilisés) | reverser seulement si preuve |

### 3.2 Loader NIF/KF/KFM — décision

- **C# schema-driven** (porter `nif_reader.py`+`nif_spec.py`+`nif.xml`)
  produisant `Model`/`SkinnedModel` procéduraux — pas d'importeur
  éditeur (le contenu vit dans EdenVfs). C++ ensuite seulement sur
  hot paths mesurés.
- **Sous-ensemble corpus** : NiNode/Billboard/Switch/SortAdjust/LOD,
  NiTriShape/Strips/Lines + *Data, NiTexturing/Material/Alpha/
  VertexColor/StencilProperty, NiSourceTexture, NiSkinInstance/Data/
  Partition (4 influences), NiTransformController/Keyframe/MultiTarget →
  NiTransformData (**5 modes : LINEAR/QUADRATIC/TBC/XYZ_EULER/CONST** —
  le GLB n'a que LINEAR), NiBSpline(Comp)TransformInterpolator,
  NiControllerSequence + NiTextKeyExtraData, NiFlipController, extras.
- **Fidélité anim = la raison d'être** : TBC/QUADRATIC/B-spline réels +
  machine de transitions `.kfm` (ignorée aujourd'hui).
- **Tolérances** : skip par taille des blocs inconnus, hash-DJB1, DAG
  multi-parents, resync kfm, variante packed `0x14030202` (27 fichiers)
  → erreur explicite + repli GLB.
- **Skinning multi-meshes** → `AnimatedModel` + `SetMasterPoseModel`.
- **Matrices** = vecteurs-lignes Gamebryo (piège n°1 — test `B.nif`
  skin data en premier). Z-up conservé, conversion à la frontière.
- **Validation croisée** : même modèle via NIF vs GLB → sommets/tris,
  AABB (`nif_report*.txt`), os/bind/poids, clips, durées, poses clés.

### 3.3 `.dds` natives et `.ifl`

Flax importe `.dds` (BC1-7) → lecture runtime `Texture` depuis bytes
pour cohérence VFS ; pièges : tronqués, flip V, sRGB, BC5 normal maps,
mips, A8R8G8B8. `.ifl` → texture array flipbook (eau, effets).

### 3.4 CEGUI

Parseurs copiés (`UiNodeSpec` neutre à 25 kinds) ; `CeguiToFlax` =
matérialiseur → `UIControl`/`UICanvas`, `Sprite9Slice` nine-patch,
`SpriteHandle` atlas, états via matériaux UI, `AARRGGBB`, FONT01.ttf +
fallback CJK natif. Tolérances reconduites (double `<?xml?>`, 2
`<GUILayout>`, Big5, props inconnues → compteur).

---

## 4. Réseau et session

Copie quasi intégrale : TCP `TcpClient{NoDelay}`, **deux transports**
(modern TLS 1.3+pinning+VersionHello `EDN1`+manifestHash ; legacy
RSA-2048+RC4-40+varint LEB128), `ReceiveBuffer` 8 Kio, `Rc4` ×2,
`BinReader/Writer` CP950, 647 `NetMessage`, `MiscPackets.BindZone`,
`EdenSession` (10 étapes login→zone, keepalives Echo 57/5 s + pong
9057), dispatch `Dictionary<ncid,List<Action<BinReader>>>`.
**Seule adaptation** : pompe d'événements `ConcurrentQueue` drainée en
`_Process` → équivalent Flax (`Scripting.ExecuteOnUpdate`/file maison).
Perf : deserialize+RC4 sur **Job System**, pooling buffers (`ArrayPool`).

Checklist NCID par priorité : entrée (473/313/310-312/296/351/368),
entités (298/265/326/264/267/269/293/294/280/283/271/329/330/380),
combat (272/278/279/281/287/288/289/305/306/410/495), HUD
(285/316/327/348/388/391/444/474), services (304/357/393/399/430).

---

## 5. Monde et rendu

Chaîne `MapLoader` portée : `.fsm`/`.ini`/`.area`/`.smp`/`sXXX.ini` via
résolution insensible à la casse ; terrain LAY0 → mesh procédural
(`GPUVertexLayout` custom : pos + normale compressée + RGBA baké + UV,
1 mesh/tuile, indices reconstruits) ; shader HLSL splat (5 couches +
splatmap **linéaire** + vertex-color baké) ; eau kind=1 (drift UV +
flipbook + onde + fade rive) ; `.smp` 2 bits + dilatation 8-voisins +
glissement par axe ; helpers NIF → colliders consolidés (walls/floors) +
`TerrainSampler`/`WalkableGround` ; props OBJ (mat3×3 row-major) +
herbe GRL/GRS → instancing par cellule 64 u ; `MapEnvIni` → lumière/
fog/sky (`sun` node)/météo ; `FogCeiling` post-process depth-based ;
`SceneTable` → spawns ; migration 313 (unload/reload/rejoue différés).
Shaders HLSL : terrain_splat, water, sky_dome, fog_ceiling,
toon_lit/_2s (3 paliers+rim → custom shading model), outline.
Inconnues à ne pas « corriger » sans preuve : canal splat→slot
(HYPOTHESIS), caustiques ×1.7, `.area` lignes 2-3, météo 6→3.

---

## 6. Entités, joueur, combat

EntityRegistry : modèle de données portable (handles sight, masked
298, WhoIs différé, ×16+half-float 326, Goto <20/≥20, corpse grace,
visual budget 4 ms) ; rendu → `AnimatedModel`+`SetMasterPoseModel`.
Anims : `NifAnimFile` (KF), retarget (quats nulles→identité),
`DirectionalLocomotion` (records 0-8, KFM 101-104, ±45°/±90°), ride
4004-4007→ride04-07. PlayerController : WASD, clic-sol (raycast+
dichotomie), orbite 6 px, steering RMB+TurnSelf, saut ×2, glissement
.smp, A*+AutoRoute, locks. Combat : SkillCast verrou
(279/281/287/410/302/313/échéance CastLock), AutoAttack (NCID 5 @1.2 s),
CombatFx 272 HitType→anim, CooldownTracker 368, PlayerStateLocks
280/1002-1006. Caméra : yaw/pitch, zoom 3-18, occlusion murs ;
curseurs SystemCursors → CustomCursor.

---

## 7. Interface utilisateur (~92 fenêtres + écrans)

Runtime-translator d'abord (voie prouvée), bake offline optionnel (M6+).
`CeguiToFlax` + repli `EeSkin` (nine-patch parchemin) + `ThemePalette`
(11 thèmes, dérive des pixels d'atlas — logique portable).
Checklist familles (inventaire complet en annexe A) :
- **P0** : Chat, Vitals, TargetFrame, Hotbar, SkillBar, Buff/Nerf,
  Inventory, QuestTracker, Minimap, ZoneBanner + drag/tooltips/curseurs
  + écrans login→loading.
- **P1** : Quests, Profile, Bank/Neobank/Family, Shop, Trade, Vending,
  Mail, Party, Guild, NpcDialogue, Maps×3, Death, Duel, MessageBoard,
  Elf×4, Archive.
- **P2** : ItemMall, Auction, Craft, Destiny, Achievement, Daily,
  Couple, Social, Battlefield, Hotel, Costume, Lottery/Altar, Inspect,
  Services×5, Stats, Class/Expertise/HeroTrait, Preview, Roll, Portal,
  UltimateBar.
Transverse : DragHelper/persist JSON, HudLayout (grab 22 px, fractions
viewport), drag&drop items, ItemTooltip (750 l., `$N$`/BBCode/`#id`),
GridResizeMath, cooldown radial, thèmes, ui_scale, UiTextEn+TSV,
InputBindings (B/M/Échap/WASD/F1-F10), ClientUiStore.
Écrans : Boot→Login→Channel→Select→Create(32/4/5/7)→Loading(5 phases)
→game ; `SessionCarry`→service ; `PreGameWarmup`.

---

## 8. i18n — FR uniquement — VFS, distribution

### 8.1 Texte : EDENFR seul, pas de merge EN

- **Source unique** : les `t_*.ini`/`c_*.ini` de `corpus/fr-full`
  (tables natives FR). Pas d'overlay `lang/fr` sur base EN : la
  base EST le FR — le chemin `TextResolver` merge-overlay devient un
  no-op (gardé mais désactivé, utile le jour où l'EN reviendra propre).
- `TextResolver` copié quand même (priorité mall, `Clean` `$N$`,
  résolution par ID) — c'est le seul resolveur de texte data.
- `UiTextEn`+TSV : **conservé** pour nos propres chaînes codées (EN→FR
  dans nos UI), mais l'exposition utilisateur vise le FR d'abord.
- CP950 via `CodePagesEncodingProvider` (repli `b2u.950`) ; fonts
  `FONT01.ttf` + fallback CJK natif.
- **Suspicion documentée** : le merge `corpus/official` (EN + overlay FR
  + compléments CN/Reforged) est suspecté de mal mixer les contenus —
  EDENFR pur élimine cette variable. Si une donnée manque dans fr-full,
  la compléter DEPUIS `original-fr`/tables FR, jamais depuis le merge.

### 8.2 Données & base serveur — adaptées EDENFR

- `start-native.sh` (copie) : `EDEN_CLIENT_DB` →
  `corpus/fr-full/data/db` ; `EDEN_SCENE_DIR` → `corpus/fr-full/data/
  scene` — les scènes/spawns/portails = EDENFR officiel (plus de merge
  `/tmp/eden_scene_merged` ni scènes US complémentaires).
- **Bases dédiées** : nouvelles bases PostgreSQL pour ce dépôt (p.ex.
  `edenfr_db`/`edenfr_account`/`edenfr_member` via `.env` local) —
  isolation réseau ET données : le serveur Flax n'écrit jamais dans les
  bases du serveur portage.
- **Seeds régénérés depuis les tables FR** : items/quêtes/PNJ/skills
  = `c_*`/`t_*` EDENFR (via `Database/` + outils serveur existants de
  génération depuis les `.ini`) ; les entrées issues uniquement de
  Reforged/CN absentes d'EDENFR sont **exclues** (pas de stub EN).
- `itemmall`, `sknodemap/fix`, `t_npc_extra` : uniquement si la donnée
  existe en EDENFR ; sinon omise.
- Vérification à M0 : inventaire des tables fr-full vs official (ce qui
  manque réellement) → doc `docs/flax/CORPUS-FR-DELTA.md`.
- Périmètre lvl ≤ 90 inchangé ; lvl 95+ hors scope même si présent.

### 8.3 Stack SGBD — PostgreSQL 17 + Redis (décision utilisateur)

Cible ~300 CCU. Le goulot n'est pas le moteur relationnel : c'est
l'héritage Aeria — **PG 12 EOL**, 3 bases physiques (`fndb1`,
`fnaccount`, `fnmember`) sans FK croisées, et le modèle DB-as-truth
(6-10 requêtes disque synchrones par kill). Reste sur l'écosystème
PostgreSQL : les ~545 requêtes Dapper (`RETURNING`, `nextval`,
`unnest`, `ON CONFLICT`) restent compatibles — l'effort va dans le
cache, pas dans une traduction de dialecte. SQL Server Express (10 Go
max) est écarté.

**Architecture décidée pour le serveur copié** (portage reste en PG 12) :

- **PostgreSQL 17 — socle de vérité** : `docker-compose` copié passe à
  `postgres:17`, restauration des dumps existants, puis **fusion des
  3 bases en une seule** `edenfr` à schémas logiques (`account.`,
  `member.`, `game.`). Débloque les FKs moteur entre comptes et
  personnages → suppression des rustines C# d'intégrité.
- **Redis — système nerveux en RAM** : extraction des **tickets
  inter-services** (`TicketRepository` : `login_tickets`,
  `zone_tickets` — TTL atomique, single-writer par session) et des
  **mutations chaudes** (`SetGoldAsync`, position joueur, vitals tick)
  vers ops atomiques Redis. Postgres ne reçoit plus que les flushs du
  **`CharacterWriteBehind` existant** (~15 s) — pattern conservé, sa
  source devient Redis au lieu d'une queue mémoire.
- **Impact code** : `DbConnectionFactory` → une seule chaîne + requêtes
  requalifiées par schéma ; référentiels tickets/chauds réécrits
  (StackExchange.Redis) ; Dapper/Npgsql conservés partout ailleurs.

**Phases DB (track parallèle, démarre après M0, indépendant du client)** :

| Phase | Contenu | Sortie |
|---|---|---|
| **DB-1** | compose `postgres:17` + `redis:7`, restore dumps, `dotnet test` vert | serveur isolé sur PG17 |
| **DB-2** | fusion 3→1 base à schémas + FKs compte↔perso + requalification des requêtes | intégrité référentielle réelle, tests green |
| **DB-3** | tickets + mutations chaudes → Redis ; WriteBehind flush Redis→PG 15 s | charge hot-path en RAM |
| **DB-4** | test de charge ~300 CCU simulés (HeadlessBot ×N) | p95 query/hot-op mesuré |

Points à vérifier en exécution (non préjugés) : chemin de migration
dumps PG12→PG17 (`pg_dump` restore, pas `pg_upgrade` en conteneur),
liste bornée des champs « chauds » (or/position/vitals — pas tout),
atomicité ticket (un seul writer par session, `SET NX PX`).

### 8.4 VFS, distribution

VFS : `EdenVfs` copié (loose-first, ordre inverse montage, tombstones,
`EDEN_VFS`/`EDEN_ALLOW_LOOSE`) ; point moteur = matérialisation
bytes→`Texture`/`Model` (`EdenVfs.Flax`) ; `.inib` préféré à `.ini`.
Distribution : `package_dist.sh` adapté + `edenpack` (build/diff/apply/
verify/bake-tables — `bake*` Godot-headless réécrits en Flax plus tard),
`current.json` signé, multi-packs, `version_check`/ManifestHash.

---

## 9. Axes transverses

### 9.1 Qualité de code — `eden_flax` (règle dédiée `.devin/rules/flax-code-quality.md`)

Héritées du dépôt : 1 classe = 1 responsabilité ; méthode < ~80 l. ;
fichier < ~1 000 l. ; octets uniquement dans codecs ; aucun paquet
spéculatif (preuve = capture/désasm/golden) ; `// HYPOTHESIS:` marqués ;
XML doc publiques + commentaire par champ de payload ; parse réseau
off-thread, mutations main-thread ; `shortcut: <limite>, <upgrade>` ;
encodage jamais implicite ; pas de `.GetAwaiter().GetResult()`.

**Spécifiques Flax à rédiger** :
- Séparation stricte : `Eden.Core` ne référence jamais `FlaxEngine.*` ;
  `Eden.Game` ne contient aucune logique wire/donnée (délègue à Core).
- Aucun `Script` pour la donnée : les Scripts Flax = colle moteur
  (attach/update), jamais de parseur/état métier dedans.
- Assets jamais en `Content/` pour le corpus : loaders runtime via
  EdenVfs ; `Content/` réservé aux assets du shell (icônes propres…).
- Pas d'allocation dans `OnUpdate` : buffers poolés (`ArrayPool`),
  structs réutilisées ; `Mesh`/`Model` créés hors boucle.
- Threading : mutations d'`Actor`/`Control` uniquement main-thread ;
  IO/décode sur Job System/`Task`, join via `Scripting.ExecuteOnUpdate`.
- Ressources natives Flax : `FlaxObject` disposés explicitement
  (`Object.Destroy`/`Content.Unload`), jamais d'oubli de handle GPU.
- Shaders : un fichier par effet, paramètres via `META_CB_BEGIN`,
  permutations via `META_PERMUTATION_*` (pas de duplication).
- Tests : toute logique dans `Eden.Core` = testable xUnit hors moteur ;
  un système sans test de parse = non livré.

### 9.2 Tests

- `Tests/` xUnit .NET pur : porter les **540 NetTests** (goldens RC4/
  RSA/framing/login, IniTable/TableBin/EPK/CEGUI/parseurs/direction/
  cast-lock). `Eden.Core` csproj autonome → `dotnet test` sans Flax.
- Tests loaders natifs (nouveaux) : NIF synthétiques (miroir
  `test_nif.py`), cross-validation GLB, fichiers pathologiques (kfm
  corrompus, dds tronqués, 2×GUILayout, ini CRLF, packed → erreur).
- `NetSmoke-Flax` (replay login→zone) + `HeadlessBot` (2ᵉ client).
- In-game : `-headless -null -mute -std` (logique) ; captures
  **Flameshot + XTEST/Xlib** (identique à l'existant : fenêtré, input
  réel, `flameshot full`) vs `ref-shots/` et A/B Godot même cadrage.

### 9.3 Réseau avec le serveur (process)

Preuve avant code : `reindex opcode <n>` + `Messages/Generated/*.g.cs`
+ goldens serveur (254 cas). `tools/wire_diff.py` pour comparer les
streams. Comptes `admin`/`adminmdp`, `test123`/`test123`. Serveur :
`eden_server/start-native.sh` — **copie locale isolée**, relançable
librement, jamais le `portage/eden_server`.

### 9.4 Git / versionnement

- `git init`, commits petits par sujet, auteur Noah Lhommedieu +
  Co-Authored-By Devin (convention reprise). Remote :
  `https://github.com/Noah-Lhommedieu/eden-reverse.git`, push uniquement
  sur demande.
- Tag de parité à chaque jalon : commit Godot de référence noté dans
  `docs/flax/STATUS.md`.
- https://github.com/Noah-Lhommedieu/eden-reverse.git pour le dépot distant.

### 9.5 Dev & test par agents IA

- **Découpage en lots** : 1 lot = 1 système/famille + critère de
  preuve ; `docs/temp-plans/YYYY-MM-DD-flax-<sujet>.md` avant code.
- **Boucle standard** : spec → code → `Flax.Build` (0 err) →
  `dotnet test` (Core) → `-headless -null -std` (réseau) → fenêtré +
  Flameshot (visuel) → cocher.
- **Preuve obligatoire** : « fait » = capture/trace, pas compilation ;
  3 niveaux rapportés séparément (build/tests, headless, rendu).
- **Parallélisation** : lots Core/formats indépendants parallélisables ;
  1 seul client graphique à la fois (`flock /tmp/flax.lock`).
- Hot-reload `AssemblyLoadContext` pour itérer UI ; subagents
  illimités côté analyse/code (règle maison : 1 write par zone pour
  éviter les collisions, séparation des dossiers de travail par lot).

### 9.6 Performance

- **Budgets** (figés à M2, mesurés à chaque jalon) : chargement map,
  RAM, frame time P95, allocs/frame ≈ 0 en boucle stable.
- **Mesure** : Tracy Profiler + `ProfilerMemory.Dump` + `PerfProbe`
  porté + temps par phase de chargement.
- **Techniques** : instancing cellule 64 u ; `GPUVertexLayout`
  quantifié ; streaming textures (eq. `CharacterTexture`/`SceneTexture`
  natifs) ; preload pendant loading (eq. `PrecacheKFM`) ; Job System
  réseau ; pooling ; visual budget 4 ms/spawn ; culling par cellule ;
  Arena Allocators si hot path.
- **Régressions connues** : RAM textures résidentes ; parse `.ini`
  lazy → `DataWarmup` porté.

### 9.7 Documentation IA (obligatoire dès M0)

- `AGENTS.md` racine + `eden_flax/AGENTS.md` + `eden_server/AGENTS.md`
  (copiés/adaptés) : périmètre, validation, harnais, attribution.
- `.devin/rules/` : `ponytail.md` (copié) + **`flax-code-quality.md`**
  (nouveau — §9.1 spécifiques).
- `.claude/` : ARCHITECTURE_MAP/QUICK_START/COMMON_MISTAKES régénérés
  pour ce dépôt ; `completions/YYYY-MM-DD-<tâche>.md` à chaque lot
  terminé ; `sessions/` archives.
- `docs/flax/STATUS.md` : matrice de parité 1:1 vivante (chaque
  système/fenêtre : [ ] [~] [x] + preuve).
- `docs/temp-plans/` : un plan par lot IA avant de coder.

---

## 10. Jalons

| Jalon | Contenu | Sortie |
|---|---|---|
| **M0 — Dépôt** | arborescence §1.1, copies du manifeste (corpus **fr-full**, pas official), `git init` + remote `eden-reverse`, AGENTS/règles/`.claude` en place, **DB-1 : compose `postgres:17`+`redis:7`, bases `edenfr` créées + seeds FR**, serveur copié qui démarre (`start-native.sh` pointant fr-full) et `dotnet test` serveur vert, `CORPUS-FR-DELTA.md` rédigé | repo propre, serveur isolé fonctionnel sur données FR pures + PG17 |
| **M1 — Flax + Eden.Core** | Flax 1.13 installé, `eden_flax` compile, `Eden.Core` csproj autonome, 540 tests portés verts | `dotnet test` vert |
| **M2 — Réseau** | NetSmoke-Flax login→zone headless vs serveur local | trace NCIDs conforme |
| **M3 — Première map** | loaders ini/fsm/smp/dds/area, terrain splat+collisions+caméra (S002) | capture vs ref-shot + A/B Godot ; `.smp` bloque |
| **M4 — Entités** | NIF/KF subset, props OBJ, joueur, spawn/move/interp, locomotion | entités live animées ; cross-check NIF/GLB |
| **M5 — HUD cœur + écrans** | CeguiToFlax, P0 + login→loading | flux jouable end-to-end |
| **M6 — Gameplay** | combat, quêtes, dialogues, boutiques, portails, montures, familiers | scénarios live (kill, quête, map→map) |
| **M7 — UI complète + i18n** | P1/P2, thèmes, FR/EN, audio | checklist 1:1, bascule FR propre |
| **M8 — Packaging + perf** | `.epk`, `.inib`, packs, budget perf | boot packs seuls ; Tracy OK |
| **M9 — Parité** | captures vs ref-shots, ≥6 maps, multi-joueurs | parité visuelle validée, STATUS final |

Dans chaque jalon : **le pur d'abord** (compile+tests hors moteur),
puis le couplé moteur, puis la preuve visuelle.

---

## 11. Risques

| Risque | Gravité | Mitigation |
|---|---|---|
| Loader NIF Gamebryo 20.x (DAG, skin, B-spline, TBC, hash/packed) | **élevé** | spec `nif2gltf` à porter ; oracle GLB A/B ; sous-ensemble corpus ; repli GLB par fichier |
| Fidélité anims (B-spline 143 k échecs, TBC perdu en GLB) | élevé | motif du natif : 5 interpolations + transitions `.kfm` ; tests poses clés |
| Gap CEGUI→Flax UI (440 layouts, Falagard partiel) | élevé | parseurs purs réutilisés ; UiNodeSpec neutre ; repli `EeSkin` ; bake offline en secours |
| `.csm`/`.fsmp`/`.pgp`/`.smp2` non documentés | faible | inutilisés — hors périmètre |
| Flax 1.13 récent, docs minces, petite équipe | moyen | version pinnée ; spike M0-M3 avant engagement ; Godot intact en secours |
| VFS/EPK vs pipeline de contenu | moyen | runtime-loader (pas d'import base) ; matérialisation bytes→assets |
| Big5 partout | moyen | `CodePagesEncodingProvider` + tests encodage |
| Divergence silencieuse portage/ vs eden-flax (code copié) | moyen | Godot = référence figée ; resync manuel documenté par jalon si bugfix amont |
| EULA royalty > 250 k$/trimestre | faible | non commercial ; revalider avant monétisation |
| Refonte SGBD (3→1 base, Redis hot-path) — régression persistance | moyen | track parallèle post-M0 ; tests serveur + goldens intacts à chaque phase ; WriteBehind déjà existant = base du flush |

---

## 12. Annexes — inventaires des 5 agents

Résumés utilisés (rapports complets en session ; régénérables en docs
`docs/flax/inventaires/` à l'exécution de M0) :

- **Monde/rendu** : 23 fichiers `scripts/world/` (7 071 l.) + 6 shaders
  spécifiés + chaîne de chargement + conventions Z-up + inconnues FSM.
- **UI** : 92 fenêtres par famille (layout CEGUI vs repli code) +
  pipeline `CeguiBuilder`/`UiNodeSpec` (25 kinds) + écrans + transverse.
- **Réseau/gameplay** : stack TLS/RSA+RC4/varint, 647 messages,
  machine 10 étapes, systèmes (move 10 Hz, ×16/half-float, cast lock,
  sight handles) + liste des fichiers 100 % portables.
- **Données/VFS/outillage** : IniTable 3 formats+MyCrypt, ITB1,
  TextResolver, 25 tables, EPK, outils, 540 tests.
- **Formats natifs** : table par format + spec NIF/KF/KFM (header,
  563 niobjects, rigs B/F/G/K/m/rNNN, motion.ini, directionnelles
  101-104) + ordre de déblocage + risques.

*Plan v3 rédigé le 2026-10-31 — dépôt isolé `eden-flax/` (remote
`Noah-Lhommedieu/eden-reverse`), **contenu EDENFR pur FR-only** (corpus
`fr-full`, merge EN exclu), **stack DB PostgreSQL 17 + Redis** (base
unique à schémas, tickets/hot-path en RAM, WriteBehind 15 s — §8.3),
formats natifs dans Flax, zéro code mort. Aucune action exécutée.
Première action proposée si go : M0.*
