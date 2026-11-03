# PERF — mesures et optimisations (lot G1)

Outil : `scripts/perf/PerfProbe.cs` — activé par `--perf`, ajouté par
`GameScene._Ready` après la construction du monde. Logue chaque
seconde `fps, frame ms (min-max), RSS process (via /proc), mémoire
statique, VRAM, draw calls, objets/primitifs rendus, nœuds, compte de
la scène par type, caches AssetCatalog` + résumé `perf-done`.
Options : `--perf-seconds=N` (quitte après N s), `--perf-out=<fic>`,
`--perf-nostats` (saute le comptage d'arbre).
`Perf.PerfTrace` (même fichier) chronomètre les phases de chargement
(`[perf] loadmap …` / sous-sections dans `MapLoader`).

Commande de mesure :

```bash
cd godot_project
DISPLAY=:0 godot --path . res://scenes/game.tscn \
    --resolution 1280x720 -- \
    --map=S001 --solo --nohud --perf --perf-seconds=30 \
    --perf-out=/tmp/perf_s001.txt
```

## Mesures (spawn joueur, 1280×720, RTX 4060, preset Medium, solo)

### S001 — ville dense (3769 objets, 263 NPC, 177 tuiles)

| métrique                | avant  | après | gain |
|-------------------------|--------|-------|------|
| chargement map          | 5590 ms| 2040 ms| −63 % |
| dont objects            | 2623 ms| ~860 ms| −67 % |
| dont entities (263 NPC) | 1359 ms| ~780 ms| −43 % |
| fps moyen (fen. 240 fr) | 66     | ~87   | +32 % |
| draw calls / frame      | 3813   | 3252  | −15 % |
| objets rendus / frame   | 8050   | 5673  | −30 % |
| primitives / frame      | 2,78 M | 2,25 M| −19 % |
| nœuds scène             | 36513  | 36513 | =    |
| MeshInstance3D          | 10098  | 10098 | =    |
| RSS process             | ~1,63 GB| ~1,9 GB| +0,25 GB |
| VRAM / textures         | 712 MB / 576 MB | = | |

### S002 — plage (2064 objets, 53 NPC)

| métrique       | avant  | après  |
|----------------|--------|--------|
| chargement map | 2522 ms| ~1200 ms |
| draw calls     | 885    | 789    |
| fps moyen      | 120    | ~110   |
| RSS            | 1,13 GB| ~1,2 GB |

(Le delta fps S002 est dans le bruit vsync/fenêtre courte.)

### Multi-instance (2 clients simultanés, 30 s, 960×540)

S001 + S002 en parallèle : chaque instance ~80 fps, RSS ~1,55-1,6 GB,
aucun crash, sortie propre après `--perf-seconds=30`. Total ~3,2 GB
RAM pour 2 clients — tient largement sur la machine (15 GB).

## Optimisations appliquées

1. **Cache des AnimationLibrary retargetées** (`game/AnimPathMap.cs` +
   `game/AnimRetarget.cs` + `game/EntityAnimator.cs`). Le retarget ne
   produit que des paths relatifs : mêmes modèle+anims ⇒ même
   bibliothèque. Clé = `animRelPath + wanted + AnimPathMap.Signature`.
   Avant : 263 retargets sur S001 (~30× le même « M001 »). Après : un
   par (stem × squelette). Élimine le spam de log « N anims
   retargetées » (1 ligne par combinaison au lieu d'une par NPC).

2. **Préchargement parallèle des protos GLB** (`AssetCatalog.
   PreloadGlbProtos`, appelé par `MapLoader.BuildObjects` et
   `BuildEntities` avant les boucles d'instanciation). Parse +
   `GenerateScene` + matériaux + culling sur des threads (N-2, borné
   2-8) — tout est détaché, sans accès SceneTree ; les Duplicate
   restent séquentiels main-thread. Idem pour les fichiers d'anims
   (`EntityAnimator.PreloadAnimFiles` — `GlbAnimFile` est un parseur
   C# pur). Caches `AssetCatalog`/`EntityAnimator` passés sous verrou.
   **Note** : `Node.Duplicate` en parallèle a été essayé et REVERT —
   blocage mesuré (probable contention interne ObjectDB). Ne pas
   réessayer sans preuve contraire.

3. **Culling à distance des props de map** (`scripts/perf/PerfCulling.
   cs`, appliqué au proto dans `AssetCatalog.LoadGlbNode` — hérité par
   les Duplicate). `VisibilityRangeEnd` par rayon d'AABB : <0,75 u →
   60 u ; <2 u → 120 u ; <6 u → 220 u ; <15 u → 400 u ; ≥15 u → jamais.
   Fade dither `Self` (marge 8 %). Périmètre : `models/map/model/` hors
   `sky/` (dôme) ; entités (`monster/`, `char/`, `ride/`) NON cullées
   (sight-range réseau les borne déjà). Vérif visuelle : capture spawn
   S001 avant/après — diff RMSE < 0,3 %.

4. **GC agressive post-chargement** (`GameScene` après `LoadMapNode`,
   ~35 ms derrière l'écran de chargement) — rend la partie du burst
   d'allocations GLB/PNG récupérable.

5. **Instrumentation** : traces `[perf] loadmap` ventilées dans
   `MapLoader` (fsm / terrain+grass / objects / env / entities) —
   inertes sans `--perf` (`PerfTrace.Report` no-op).

## Constaté déjà en place (pas de travail nécessaire)

- `AssetCatalog` : caches textures (par chemin, échecs compris),
  protos GLB, listings de dossiers insensibles à la casse. Pas de
  double-chargement observé (`cache=42tex/204glb` sur S001 : ~600
  modèles demandés → 204 protos distincts).
- Herbe : déjà `MultiMeshInstance3D` par (type × cellule 64 u) avec
  `CustomAabb` → culling frustum natif ; plafond 5000 instances.
- Labels entités : déjà `Billboard` activé ; budgets NPC bornés par le
  nombre réel de spawns.
- Settings perf (D5) : presets Low/Med/High/Ultra commutent ombres,
  fog, particules, herbe, distance — déjà câblés.

## Lot G2 — batching des props statiques (`world/PropBatcher.cs`)

Props `map/model/object/*` regroupés en `MultiMeshInstance3D` par
(modèle GLB × cellule 64 u × nœud-mesh du proto). Éligibilité scannée
une fois sur le proto partagé : exclus les modèles animés
(AnimationPlayer/Skeleton3D/particules/lumières), alpha **blend**
(tri par instance faux — scissor OK) et multi-matériaux
(MaterialOverride unique). Colliders préservés : les meshes helper
(même prédicat que `CollisionBuilder` — meta `collision_helper` ou
nom helper) reçoivent par instance un `StaticBody3D` porteur (1 nœud
+ N `CollisionShape3D`, shapes trimesh/box **partagées** par mesh,
cap `MaxHelpersPerObject`). `VisibilityRangeEnd` recopié par nœud
(culling distance sur l'AABB de cellule — approximation 64 u) ;
ombres off si rayon < 1,5 u. Flag : `--props-batch` (défaut on),
`--no-props-batch` / `--props-batch=off`.

### Mesures S001 (même commande que ci-dessus, env de test llvmpipe —
les fps absolus sont plus bas que la 4060, comparer en relatif)

| métrique                | off     | on      | gain  |
|-------------------------|---------|---------|-------|
| chargement map          | 5385 ms | 2969 ms | −45 % |
| draw calls / frame      | 2307    | 2441    | +6 %* |
| objets rendus / frame   | 4574    | 3782    | −17 % |
| nœuds scène             | 48092   | 34662   | −28 % |
| MeshInstance3D          | 10098   | 6062    | −40 % |
| frame ms (médiane)      | ~130    | ~80     | −38 % |
| fps moyen 30 s          | 7,9     | 11,8    | +49 % |
| RSS stable              | ~1,8 GB | ~1,5 GB | −0,3 GB |

\* draw calls légèrement en hausse : un MultiMesh d'une cellule 64 u
intersecte plus de splits d'ombre que les petits props individuels —
le vrai goulot (CPU scene/raster sous llvmpipe) chute quand même
d'un tiers. Sur S001 : **1 843 props batchés en 599 multimeshes +
1 825 bodies** de collision porteurs, 1 926 objets restés sur le
chemin instancié (986 avec colliders natifs).

Vérif visuelle : capture vis_test spawn S001 off/on — RMSE 1,6,
0,07 % de pixels à >16/255 (bruit AA). Parité caméra/physique :
player pos identique, 1 825+986 = 2 811 colliders (même total).

**Bug pré-existant découvert** : `CollisionBuilder.Collect` compose
les transforms en incluant le Transform de la racine objet —
`body.Transform` (local sous l'objet) reçoit donc objT·meshRel → les
colliders du chemin instancié sont posés à **objT²** (~2× la position
de l'objet — murs invisibles déportés au loin, jamais au bon endroit).
`PropBatcher` pose les siens correctement (objT·rel) : les props
batchés bloquent réellement (vérifié — le SpringArm de caméra
raccourcit derrière un mur au spawn, cam=(259.4, 0.4, −298.2) vs
pleine extension (259.4, 5.2, −289.5) sans bodies). À corriger côté
`CollisionBuilder.Collect` (exclure le transform racine) — signalé,
non modifié ici (fichier hors périmètre).

## Phase 1 refonte (26 oct. — flux login→jeu live, `--perf`)

Mesures réelles sur le flux `--login=admin:adminmdp --channel --char`
(S001, machine dev, RTX) :

| transition                        | avant      | après      |
|-----------------------------------|------------|------------|
| channel→sélecteur (UI affichée)   | ~20 s      | **0,16 s** |
| └ preview avatar (tâche de fond)  | bloquait UI| ~7-11 s bg |
| char→« EN JEU »                   | ~30 s      | **~14,5 s**|
| dont loadmap S001                 | 13,6 s     | 5,8-6 s    |
| dont objects+env+entities         | 12,9 s seq | ~5 s (parallèle) |
| dont entities (corps 263 NPC)     | ~7,5 s     | streamés post-entrée (~3/frame, proches d'abord) |
| dont glb preload (184 protos)     | —          | ~6 s (dominé par le méga-modèle perso ~5,5 s) |
| VRAM textures                     | 4 421 MB   | **1 173 MB** (−73 %) |
| VRAM totale                       | 4 670 MB   | 1 445 MB |
| fps en jeu (post-stream)          | 11-12      | **55-66**  |
| RSS process                       | ~4,3 GB    | ~5,9 GB* |

\* RSS : le heap mono garde sa marque haute après le burst de
compression (~8 Mo × ~600 images en parallèle) — deux GC compactants
(post-load + post-stream) limitent, mais la RSS réelle dépend aussi
des protos GLB/anim cachés et des pages mappées des 22 Go d'assets.
Le poste VRAM (le facteur fps déterminant) est divisé par ~4.

### Correctifs phase 1

- **`.slim.glb` d'anims** (`tools/anim/trim_anim_glb.py`, 4 243
  fichiers générés — B.glb 88→10,8 Mo pour les 39 anims perso) ;
  `EntityAnimator.ResolveAnimPath` les préfère au méga-GLB.
- **Preview perso asynchrone** (`CharacterPreview.Show`) — le pipeline
  `BuildBody` (proto GLB + dressing + anims) tourne en tâche de fond ;
  `ItemLookup`/`CharColorTable`/`_pequipCache`/`_tintCache` verrouillés.
- **Teintes peau/teintures octet-directes** (`CharVisual.Tinted/Dyed`
  sur `Image.GetData()` — 25,7 s → ~4-5 s) + garde anti re-teinte
  (`_tintProducts`).
- **Path-map par modèle** (`EntityAnimator.Attach modelKey`) — le
  `BuildPathMap` (~15 ms×263) n'est fait qu'une fois par stem.

## Phase v3 (27 oct. — P0.1/P0.3/P0.4 : stabilité migration, dédoublonnage PNJ, rayon de visibilité)

Correctifs P0 :
- **P0.1** : `AssetCatalog.PreloadGlbProtos` et `MapLoader.LoadMap` n'appellent plus `GltfDocument.AppendFromFile`/`GenerateScene` en parallèle multi-threads pendant que le SceneTree tourne (élimine le deadlock ObjectDB/RenderingServer au changement de map). `GameScene.OnNodeData` effectue désormais la migration de façon asynchrone sous `LoadingScreen.EnsureOverlay`, met les paquets d'entités en file d'attente pendant le chargement, libère l'ancienne map avant de charger la nouvelle, puis rejoue la file.
- **P0.3** : en mode live (`SpawnSceneNpcs = false`), `MapLoader` précharge les protos/skins/anims de la table de scène mais délègue l'instanciation des PNJ à `EntityRegistry` (zéro doublon `Npc_*` vs entité serveur kind 8).
- **P0.4** : préchargement des textures de variantes (`npclist` col9) sous l'écran de chargement, construction immédiate des seuls PNJ dans un rayon de 75 m autour du spawn (`VisibilityRangeEnd = 75-85 m`), `SetProcess(false)` sur les `EntityAnimator` des PNJ de scène statiques, file `_pendingVisuals` bornée en ms/frame dans `EntityRegistry`, et suppression du `GC.Collect` bloquant/compactant post-stream.

Mesures S001 (1280×720, RTX 4060, HUD complet) :

| seconde | fps | frame moy (min-max) | proc ms | draws | nodes |
|---------|-----|---------------------|---------|-------|-------|
| t=2s    | 60  | 16,8 ms (8,5-140,2) | 287,0   | 1 850 | 12 498 |
| t=3s    | 89  | 11,2 ms (8,6-29,4)  | 29,0    | 1 850 | 12 498 |
| t=4s    | 94  | 10,6 ms (8,9-17,2)  | 29,7    | 1 850 | 12 498 |
| t=5s    | 92  | 10,8 ms (8,5-32,5)  | 47,2    | 1 850 | 12 498 |
| t=8s    | 97  | 10,3 ms (9,0-12,5)  | 31,2    | 1 848 | 12 498 |
| t=14s   | 96  | 10,4 ms (8,7-14,7)  | 26,9    | 1 848 | 12 498 |
| **moy 15s** | **89,8** | **11,1 ms** | — | **1 848** | **12 498** (vs 36 513 avant) |

- **Parallélisation objects/env/entities** dans `LoadMap`
  (sous-arbres détachés disjoints, `_rootLock` sur les attaches
  au même parent) : 12,9 s → ~5 s.
- **Streaming des corps NPC** post-entrée (`DrainNpcBodies`,
  ~3/frame, triés par distance au spawn) — seuls les holders
  (position/label) sont créés pendant le chargement.
- **Compression S3TC/DXT des albedos** (`AssetCatalog.CompressImage` +
  `CompressEmbeddedTextures` — PNG catalog + textures embarquées GLB ;
  UI/normales exclues) : −3,2 Go VRAM textures, fps ×5. Sorties de
  teinte recompressées ; entrées décompressées si besoin.
- **Second GC compactant** après la fin du stream NPC.

## Mesure RAM « top 5 postes » (2026-11, `--perf` + `tex-postes`)

Tally des octets décodés par source de texture (`AssetCatalog`) +
moniteurs moteur en steady.

| Poste | Solo S001 | Live S001 |
|---|---|---|
| VRAM textures | 620 MB | **4 513 MB** |
| VRAM totale | 791 MB | 4 787 MB |
| RSS process | ~2,9 GB | **~5,0 GB** (peak 5,5) |
| Static mem | ~507 MB | ~580 MB |
| Nœuds scène | 17 830 | 20 146 |
| Cache assets | 172 tex / 274 glb | 514 tex / 278 glb |
| Décodé tracké (`tex-postes`) | 1 534 MB | 3 125 MB |

`tex-postes` (par dossier/fichier, octets décodés) :

1. **`monster/texture/` = 488 MB décodés** — skins variantes NPC
   (npclist col9). Compressés à l'upload → ~76 MB VRAM. C'est le
   plus gros groupe de la longue traîne.
2. **`glb:S218_room10b.glb` = 390 MB décodés** — le hall temple du
   login : textures embarquées massives ; compressées à l'upload
   (~60 MB VRAM) mais la source reste dans le proto caché toute la
   session (levier : libérer le proto après les écrans).
3. Équipements NPC/GLB `glb:UB*/UA*/m*` — 15-33 MB décodés chacun,
   compressés.
4. `char/texture/*` — 8-16 MB décodés par FACES/SB ; **exclus de la
   compression** (relecture pixels pour teintes) — masse live.
5. Atlases UI (`uidata`) via CeguiToGodot — non compressés
   (GetRegion/blit CPU).

**Écart clé live vs solo** : la compression S3TC fonctionne partout
où elle s'applique (620 MB VRAM textures en solo), mais en live les
postes **exclus** (`char/texture`, `char/model`, UI, teintes
générées par avatar/joueur distant — RGBA8 non compressées)
dominent : tex 4 513 MB on vs 5 944 MB off — la compression ne
récupère que ~1,4 GB en live. Levier principal : compresser ou
streamer les teintes/skins par personnage (produire des images
compressées à la teinte, et re-compresser les variantes
`monster/texture`/`char/texture` quand elles ne servent plus de
source pixel).

- **Mémoire** : ~2,9 GB RSS solo / ~5,0 GB live (mesuré ci-dessus) —
  le poste dominant est la VRAM textures non compressées
  (`char/texture`/`char/model`/UI/teintes). ImageTexture garde aussi
  la copie CPU de l'Image — pas d'API de « discard » (passer à des
  .ctex/CompressedTexture2D importés exigerait un pipeline d'import).
- **Chargement synchrone** : `LoadMap` reste bloquant (~2 s S001)
  derrière l'écran de chargement — acceptable ; un stream asynchrone
  par chunks exigerait de revoir l'ajout à l'arbre (duplicate
  parallèle non sûr, voir ci-dessus).
- Terrain : 177 MeshInstance3D pourraient être fusionnés en chunks
  4×4 tuiles — gain de draw calls modéré (3252 → ?), à évaluer.
- Les spikes frame ~140-150 ms au premier affichage = compilation de
  shaders à chaud ; un `Pipeline Pre-Caching`/`--shader-cache` ou
  warmup dédié reste à faire.

## Fuites à la sortie (2026-11 — `--perf-seconds` sortie propre, solo S001)

Rapport ObjectDB/RID Godot à `GetTree().Quit()` :

| Poste | Avant | Après libération caches |
|---|---|---|
| ObjectDB instances | 12 698 | **5 623** (−56 %) |
| Mesh RIDs | 1 178 | 153 |
| Material RIDs | 1 085 | 308 |
| Texture RIDs | 1 358 | 470 |
| render Instances | 1 420 | 242 |
| `GodotBody3D` | 2 090 | 2 090 (teardown order — voir note) |

Correctif : `GameScene._ExitTree` libère les caches statiques de la
partie — `AssetCatalog.ReleaseCaches` (protos GLB `Free()`s, textures
déréférencées), `CharBodyCache.ReleaseCache` (protos corps `Free()`s),
`ToonMaterializer`/`WaterAnimator` (dictionnaires matériaux vidés).
Résidu : corps physiques de map (teardown-order — la transition de
map `QueueFree` bien l'ancien root, pas de fuite de session mesurée)
et ~5,6k instances à attribuer (relancer avec `--verbose` si besoin).

## C5 — mesure `--perf` du flux réel (28/10/2026)

Flux `--login --channel --char --live`, entrée sur Aven (S001) :

| Phase | Durée |
|---|---|
| ZoneTicket | 813 ms |
| `body LoadGlbNode` (1er corps) | 3 356 ms |
| `glb preload` (184 protos) | 7 476 ms |
| `npc protos (84) + skins (126)` | 7 556 ms |
| `npc anims (84)` | 845 ms |
| `objects+env+entities` | 13 581 ms |
| `loadmap S001` | 14 538 ms |
| `player+visual` en jeu | 60 ms |
| Total login → jeu | ~25 s (sous l'écran de chargement) |

RSS à l'entrée en jeu : ~3 810 Mo. Pics > 33 ms hors chargement : à
mesurer (la série `--perf` en jeu n'imprime pas encore de frame time).
Pistes : paralléliser le préchargement des protos PNJ/GLB, convertir
les textures en format GPU, limiter les 184 protos au rayon visible.
