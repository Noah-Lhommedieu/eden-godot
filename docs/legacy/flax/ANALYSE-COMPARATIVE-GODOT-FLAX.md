# Analyse comparative Godot / Flax — client du portage Eden Eternal (EDENFR)

> Document autonome destiné à un **avis externe** (autre IA ou humain). Rédigé le 2026-11-01
> par l'agent qui a travaillé sur les deux dépôts. Chaque chiffre est étiqueté :
> **[mesuré]** (relevé réel, commande donnée en annexe), **[doc]** (repris d'un document du dépôt,
> non remesuré), **[estimé]** (jugement de l'auteur, à contester). Les erreurs passées de l'auteur
> sont listées en §10 — le lecteur doit en tenir compte pour pondérer la fiabilité de ce document.

---

## 0. Résumé exécutif (à lire en premier)

**Projet** : recoder le client du MMO *Eden Eternal* (client officiel français « EDENFR », 2010,
Gamebryo/D3D9, formats NIF/KF/KFM/DDS/FSM/SMP/INI/CEGUI) pour qu'il tourne sur un moteur moderne,
avec un serveur C# isolé (PostgreSQL 17 + Redis). Le client Godot (C#) existe et est « une base
à ~70 % bonne » selon le propriétaire. Un second portage vers **Flax Engine 1.13** a été lancé le
2026-10-31.

**Objectifs déclarés du propriétaire** (par priorité) : (1) **performance bien plus élevée**,
(2) **perfection** du rendu/gameplay vs le jeu original, (3) **fichiers natifs EDENFR** chargés
directement (pas de conversion GLB/PNG qui « aggrave la qualité »), (4) **EDENFR uniquement**
(pas de merge EN/Reforged/CN), (5) moteur **gratuit**, développable **à ~100 % par un agent IA
en autonomie**.

**Constats clés**

1. **Les formats natifs ne sont pas une propriété de Flax.** Les loaders (NIF, KF, KFM, DDS, FSM,
   SMP, IFL) sont du **C# pur** (`Eden.Core`, 890 tests verts, hors moteur). Godot 4.7 C# peut les
   utiliser tels quels (multi-cible net8.0). L'argument « Flax permet le natif » est faux ;
   l'argument correct est « Flax permet de faire du runtime-natif *aussi*, sans plus ».
2. **Aujourd'hui le client Godot ne respecte pas « EDENFR seul »** : ses assets convertis (27 Go
   GLB/PNG + 18 Go `dist/`) viennent de `corpus/official` (merge EDENFR + compléments US).
   Atteindre « EDENFR seul » exige soit de reconvertir depuis `fr-full`, soit des loaders natifs —
   donc **le loader natif est requis dans les deux stacks pour l'objectif 4**.
3. **Performance mesurée S001 (Aven) même machine** [mesuré] : Godot ≈ 17,3 ms/frame en régime
   établi (57-59 fps, sans HUD, solo) ; Flax ≈ 3,7-4,3 ms (≈ 230-270 fps, avec HUD, en jeu
   réseau). Ratio **≈ 4×** aujourd'hui, **≈ 2,5×** si l'on prend le meilleur chiffre documenté de
   Godot (10,4 ms [doc]). **Comparaison non équitable** (voir §5) : Flax n'a ni outline toon, ni
   post-process brouillard, ni 263 PNJ animés ; il alloue 2,1 Mo/frame (GC permanent).
4. **Mémoire** [doc/mesuré] : Godot en live ≈ 5,0 Go RSS et 4,5 Go VRAM de textures (RGBA8 non
   compressées pour teintes/char/UI) ; Flax ≈ 2,8 Go RSS (VRAM non mesurée).
5. **Avancement** : Godot ≈ 111 000 lignes C#, 394 commits (25-31 oct.), jouable. Flax ≈ 70 000
   lignes dont 48 700 de `Eden.Core` (copié du Godot) et **21 700 de code moteur-couplé** — soit
   ≈ 30-35 % du volume engine-coupled de Godot (~62 000 lignes) — avec une parité *vérifiée à
   l'écran* d'environ 20-25 % (le terrain n'est réellement visible que depuis quelques heures).
6. **Autonomie agent** : Godot = excellente (CLI headless, scènes texte, API stable, énorme
   corpus d'entraînement). Flax = moyenne (éditeur requis pour le cook, toolchain Linux bricolée,
   2 bugs moteur contournés dont un **patch binaire** de `libFlaxEngine.so`, matériaux binaires
   générés à la main).
7. **Option tierce sérieuse** : moteur C# maison (Silk.NET/Vulkan) — plafond perf/fidélité
   maximal, autonomie 100 %, mais 4-7 mois [estimé].

**Pas de verdict univoque** : si la priorité 1 (perf) domine, les données penchent pour Flax (ou
moteur maison) ; si l'autonomie agent + risque + temps-à-parité dominent, Godot + loaders natifs.
Le §9 propose une décision à garde-fous chiffrés.

---

## 1. Contexte technique commun

| Élément | Détail |
|---|---|
| Jeu | Eden Eternal EDENFR (client officiel FR 2010), toon/low-poly, ~92 fenêtres UI, MMO |
| Formats | `.nif` (Gamebryo 20.x, ~563 types de blocs, skin 4 influences), `.kf/.kfm` (anims + machine de transitions), `.dds` (BC1-3/5, A8R8G8B8, tronqués possibles), `.fsm` (map, terrain 5 couches), `.smp` (nodemap), `.ifl` (flipbook eau), `.layout/.imageset/.scheme/.looknfeel/.font` (CEGUI), `.ini` (3 formats + chiffrement) |
| Données | `corpus/fr-full` ≈ 9,0 Go (EDENFR pur) ; `corpus/official` (merge EN/FR/CN, **exclu** du portage Flax) |
| Réseau | TCP, 2 transports (TLS 1.3 + pinning / RSA+RC4), 647 messages, ~26 000 lignes |
| Serveur | C# isolé, PG17 (base unique `edenfr`, schémas game/account/member), Redis 7 (tickets, mutations chaudes, flush 15 s). **1429 tests verts** [mesuré 2026-11-01] |
| `Eden.Core` | module C# pur : net, protocole, session, données, VFS/EPK, formats, UI neutre (UiNodeSpec). Flax : net10.0, 890 tests. Godot : net8.0 (**version Core différente à réconcilier**, §6) |
| Machine de test | Linux (Nobara/KDE Wayland), RTX 4060 laptop, Godot 4.7.2 mono, Flax 1.13.7012 |

---

## 2. Stack Godot (client de référence)

### 2.1 Version et architecture
- **Godot 4.7.2 mono**, `Godot.NET.Sdk`, `net8.0`. Chemin `portage/eden_godot/godot_project/`.
- ≈ **111 000 lignes C#** [mesuré] : HUD/fenêtres 40 400, monde 7 000, écrans 4 400, jeu 3 500,
  combat 3 200, joueur 2 200 ; le reste = protocole/net/données/vfs/UI neutre (≈ 48 000, pur,
  déjà copié dans `Eden.Core` de Flax).
- 394 commits, 25→31 oct. 2026 [mesuré `git log`] — développement par agents IA.
- Shaders : `terrain_splat`, `water`, `sky_dome`, `fog_ceiling` (post-process), `toon_lit`,
  `toon_lit_2s` (outline/rim). Rendu **Forward+**/Vulkan par défaut.

### 2.2 Pipeline d'assets (conversion offline)
`corpus/official` → `tools/nif2gltf` (Python, `nif.xml`) → **GLB** ; `.kf` → **GLB d'anims**
(`.slim.glb`, 4 243 fichiers) ; DDS → **PNG**. Résultat : `godot_project/assets` **27 Go**
(anims 14 Go, textures 9,4 Go, modèles 3,5 Go) + `dist/` 18 Go [mesuré `du`].
Pertes/limites documentées dans le dépôt [doc] : interpolations **TBC/Quadratic** réduites à
LINEAR dans le GLB ; **B-spline** (143 k échecs de conversion cités dans le plan) ; **machine de
transitions `.kfm` ignorée** ; variante NIF packed `0x14030202` (27 fichiers) non convertible ;
textures converties puis **recompressées à l'exécution** (`CompressImage` S3TC, rafale CPU
~8 Mo × ~600 images → pic RSS).

### 2.3 Optimisations déjà faites [doc `docs/specs/PERF.md`]
Cache d'`AnimationLibrary` retargetées ; préchargement parallèle des protos GLB ; culling de
distance des props (`VisibilityRangeEnd` par rayon) ; **PropBatcher** (props statiques → MultiMesh
par (modèle × cellule 64 u), −28 % nœuds) ; herbe MultiMesh par cellule ; **compression S3TC des
albedos** (VRAM −3,2 Go, fps ×5 selon le doc) ; streaming des corps NPC (3/frame) ; GC compactant
post-chargement ; parallélisation `objects/env/entities` (12,9 s → ~5 s).

### 2.4 Mesures Godot

| Mesure | Valeur | Source |
|---|---|---|
| S001 solo, `--nohud`, régime établi (t=3-29 s) | **frame 17,0-17,7 ms, 57-59 fps**, draws 1 561, objets 1 835, nœuds 7 631, VRAM 940 Mo (tex 863 Mo), RSS 2,5 Go | **[mesuré 2026-11-01]** |
| Même scène, moyenne 30 s incluant chargement | 56,4 fps (min 8,9 / max 83,4) | [mesuré] |
| S001 avec HUD complet, après optimisations v3 | **10,4-11,1 ms, ~90-97 fps**, draws 1 848, nœuds 12 498 | [doc] |
| Live S001 | RSS ≈ 5,0 Go (pic 5,5), VRAM textures **4,5 Go** (teintes/char/UI non compressés) | [doc] |
| Chargement login → jeu | ~25 s (loadmap S001 14,5 s en live ; ~2-3 s en solo) | [doc] |

> ⚠️ Écart 17 ms (mesuré aujourd'hui) vs 10-11 ms ([doc]) non expliqué : GPU partagé avec
> l'environnement de bureau pendant la mesure, ou régression. À remesurer proprement.

### 2.5 Forces / faiblesses Godot
**Forces** : client **déjà jouable** (~70 %) ; énorme documentation et corpus d'entraînement IA ;
CLI headless (`godot --path … -- --shot=…`), scènes `.tscn` texte, build `dotnet build` en
secondes ; zéro patch moteur nécessaire ; export mature ; communauté.
**Faiblesses** : coût **SceneTree** (12-36 k nœuds, 124 `AnimationPlayer`, 206 `Label3D`) ; peu de
latitude pour le batching/instancing au-delà de MultiMesh ; pas d'API de « discard » CPU pour
`ImageTexture` ; compilations de shaders à chaud (pics 140 ms) ; `Node.Duplicate` parallèle
non sûr (essayé, annulé) ; plafond de perf « raisonnable » mais modeste sans passer par
`RenderingServer` direct.

---

## 3. Stack Flax (portage en cours)

### 3.1 Version et architecture
- **Flax 1.13.7012** (.NET 10), dépôt isolé `eden-flax/`, remote prévu `Noah-Lhommedieu/eden-reverse`.
- `eden_flax/Source/Eden.Core` (pur, net10.0, 48 700 lignes, **890 tests**) + `Eden.Game`
  (couplé moteur, **21 700 lignes** : World 2 300, Entities 1 300, Anim 700, Combat 1 800, Hud 1 000,
  Windows 2 100, Dialogs 1 200, Ui 1 100, Quests 900, Shop 900…).
- **Loaders natifs runtime en C# pur** : `NifFile`, `KfFile`, `KfmFile`, `KfPose`, `DdsInfo/DdsDecode`
  (BC1-3/5, BC2 CPU), `FsmFile`, `NodeMapFile`, `IflFile`, `SplatBake`, `WaterBake`.
  Les modèles sont des `Model`/`SkinnedModel` **virtuels** créés au runtime (`UpdateMesh`), aucun
  import éditeur du corpus.
- 116 commits en 2 jours (31 oct.-1 nov.) par sessions d'agents parallèles.

### 3.2 Pipeline
Corpus EDENFR (`fr-full`) → VFS (`EdenVfs`, loose-first, packs `.epk`) → loaders C# → meshes/
textures Flax au runtime. **Aucune conversion offline.** Cook du player : `tools/flax-cook.sh`
(éditeur Flax lancé via un plugin, ~4 min) ; itération rapide : `tools/player-sync.sh` (~1 min).

### 3.3 Fait / pas fait (preuves par capture in-engine, 2026-11-01)
| Domaine | État |
|---|---|
| Réseau, login → zone | ✔ (live vs serveur local, tickets Redis) |
| Terrain, ciel NIF (dôme), eau, herbe, props | ✔ visibles sur s001/s002/s003/s005/s009/s011 |
| Joueur texturé, PNJ/monstres | ✔ **depuis quelques heures** (cf. §10 : 3 bugs racines corrigés aujourd'hui) |
| HUD | partiel : cadres natifs, panneaux parasites masqués ; slots/tracker/chat non fidèles |
| Fenêtres P1/P2 | code mergé (banque, groupe, guilde, mail, trade, vending, profil, commerce…), **non vérifié visuellement fenêtre par fenêtre** |
| Gameplay live | quête, combat, boutique, transport prouvés (avant les corrections visuelles) |
| Audio | catalogue + banque ; 1397/2448 `.wav` du corpus sont **nuls** (magic ≠ RIFF) |
| Animations vues en live | non démontrées visuellement |
| Outline toon, post-process brouillard, particules/effets | **non faits** |
| Splat terrain | **bake CPU** 512 px/tuile (pas de vrai shader splat) |
| A/B pixel vs Godot, ≥ 6 maps validées, multi-joueurs | non faits |

### 3.4 Mesures Flax [mesuré 2026-11-01]
S001, player cooké, flux réseau complet, HUD actif, `Graphics.UseVSync=false` + `Time.*FPS=0` :
frame **P50 3,7-4,3 ms, P95 6,1-6,4 ms, P99 7,2-7,8 ms** (≈ 230-270 fps) ; RAM 2,8 Go ;
allocations **2,1-2,4 Mo/frame** (budget interne 2 Ko → GC gen0 ~+1 000 /12 s, gen2 en hausse) ;
chargement map s001 ≈ 5,0 s, 1 054 props fusionnés par cellule (3 769 instances) + 5 000 instances
d'herbe. **Piège** : par défaut Flax plafonne la boucle à 60 Hz (`Time.UpdateFPS`) — sans les
réglages ci-dessus on lit « 16,7 ms » quelle que soit la charge.

### 3.5 Bugs / pièges rencontrés avec Flax (journal)
**Bugs moteur réels (2)** :
1. `StringAnsi::Set/Append/ctor(StringView)` : buffer dimensionné en caractères UTF-16 puis
   `ConvertUTF162ANSI` écrit 1-4 octets/cp → **corruption de tas** dès un log non-ASCII.
   Contourné par **patch binaire** de `libFlaxEngine.so` (éditeur *et* player) — non reproductible
   après réinstallation ; contournement logiciel : translittération ASCII dans `EdenLog`.
2. `Screenshot.Capture` **segfault** dans le player cooké ; remplacé par rendu hors-écran + readback
   GPU (`SceneRenderTask` + staging, `DownloadData` retourne `true` en cas d'**échec**).

**Pièges d'API / comportements non documentés (erreurs de l'équipe, mais coûteuses)** :
winding **main gauche** (faces arrière cullées → terrain visible seulement par en-dessous) ;
`Mesh.MaterialSlotIndex` jamais posé sur modèles virtuels (**tous les meshes sur le slot 0**) ;
matrice monde non recalculée pour un acteur spawné sous un parent tourné tant que son transform
local reste à la valeur par défaut ; `AnimatedModel` virtuel + `UpdateMesh` par frame (UAF) →
pipeline skinned GPU requis ; `ProfilerCPU.BeginEvent/EndEvent` non exposés en C# ; import WAV
via `AudioTool.Options` zéro-initialisé → crash `ConvertBitDepth` ; `Texture.FromMemory(DDS)` en
build cuite = modal bloquant par texture ; `Engine.MainWindow` vs `Screen.MainWindow` ;
`-play` éditeur sans scène si `ProjectCache.dat` n'a pas `LastScene` ; clavier OS non livré au
player sous Wayland (XTEST/uinput) ; focus perdu = jeu gelé.

### 3.6 Outillage / autonomie agent
- Toolchain : pas de `clang` système → clang 20 ROCm via **bind-mount userns** (`flax-build.sh`).
- Matériaux custom : **binaires Visject générés par script Python** (`tools/gen_mat.py`) — pas de
  shader splat/toon réel côté projet (bakes CPU en contournement).
- Erreurs de compilation **non affichées** par `flax-build` (il faut relancer `csc` à la main).
- Cook = lancer l'**éditeur** Flax (plugin) ; DLLs runtime manquantes à copier dans le framework
  embarqué ; assemblies .NET tronquées (Parallel, Numerics…).
- Captures : flameshot/spectacle **inutilisables** (bureau Wayland vole le focus) → capture
  in-engine (`tools/player-shot.sh`, `EDEN_SHOT_UI=1` inclut le HUD en redessinant `GameRoot`).
- Points positifs : build C# en ~1 min, `dotnet test` du Core hors moteur, hot path en C#,
  moteur **source disponible** (corrigeable en théorie), jobs système, rendu moderne.

---

## 4. Comparaison à objectifs égaux

| Critère | Godot 4.7 C# | Flax 1.13 | Commentaire |
|---|---|---|---|
| Perf mesurée S001 | 17,3 ms (aujourd'hui) / 10-11 ms [doc] | 3,7-4,3 ms | non équitable (§5) |
| Plafond perf théorique | modeste sans `RenderingServer` | élevé (instancing, jobs, pipeline moderne) | non démontré par la mesure |
| RAM live | ~5,0 Go | ~2,8 Go (S001) | Godot : textures RGBA8 teintées |
| VRAM | 0,9 Go (solo) / 4,5 Go (live) | non mesurée | à mesurer |
| Formats natifs | faisable (loaders C# purs) | **fait** | §6 |
| EDENFR seul | **non aujourd'hui** (assets `official`) | oui | §2/§3 |
| Rendu (shaders toon/outline/splat) | **faits** (shaders gdshader) | **à faire**, contraint par matériaux binaires | avantage Godot aujourd'hui |
| Qualité plafond visuel | suffisante (assets 2010) | plus haut (PBR, post) mais assets toon limitants | peu décisif |
| Avancement | ≈ 70 %, jouable | ≈ 20-25 % vérifié | |
| Autonomie agent | **excellente** | moyenne | décisif pour le risque |
| Maturité / docs | très forte | faible (1.13, petite équipe) | |
| Bugs moteur bloquants | 0 connu ici | 2 (contournés) | |
| Licence | MIT | gratuit < 250 k$/trim., 4 % au-delà | non commercial → OK |
| Build / itération | `dotnet build` secondes | ~1 min (sync) / 4 min (cook) | |
| Tests visuels autonomes | `--shot` natif | capture maison | |

---

## 5. Validité des mesures de performance (lecture critique)

Pourquoi **ne pas conclure « Flax = 4× Godot »** :
1. **Contenu inégal** : Flax n'a pas l'outline toon (2ᵉ passe/hull), le post-process brouillard
   (`FogCeiling`), les `Label3D`/décals ; il anime moins de PNJ (28 squelettes Godot vs entités
   envoyées par le serveur dans le rayon de vue). Godot : sans HUD (`--nohud`), solo.
2. **Régime** : Godot 17 ms aujourd'hui vs 10-11 ms documenté — incertitude sur l'état de la
   machine (GPU partagé). Il faut une **mesure à armes égales** (même cam, mêmes PNJ, mêmes effets,
   HUD des deux côtés, même résolution, charge machine contrôlée).
3. **Qualité de l'échantillon** : une seule scène (S001), une seule machine.
4. Flax a un **défaut d'allocation** (2,1 Mo/frame) qui, une fois corrigé, ne peut qu'améliorer
   les P99 ; Godot a des allocs C# similaires non mesurées.
5. **Estimation** : Flax complet (outline + post + 263 PNJ) ≈ 120-170 fps → **2-3×** Godot.

---

## 6. Scénario « on retourne sur Godot » (analyse)

### 6.1 Duplication du projet (plan)
- `cp -a portage/eden_godot eden-godot-native/` **sans** `godot_project/assets` (27 Go) ni
  `dist/` (18 Go) : ces dossiers sont des **dérivés** ; pointer le client sur `corpus/fr-full`
  (lien) et ne plus regénérer de GLB.
- `portage/` reste **lecture seule** (référence comportementale). Nouveau dépôt git, remote
  séparé ; tags de parité.
- **Réconciliation `Eden.Core`** : Godot 4.7 SDK cible **net8.0**, le Core Flax est **net10.0**
  (et a évolué : `NifFile`, `KfPose`, `DdsDecode`, loaders…). Il faut **multi-cibler** le Core
  (`net8.0;net10.0`) ou rétroporter ; risque faible (code pur) mais à valider par la suite de
  tests (890). Une seule source de vérité pour le Core, consommée par Godot *et* le serveur.
- Garder les 540 NetTests + les 890 tests du Core.

### 6.2 Loader natif sous Godot — gains attendus et coûts

**Mécanisme** : `NifFile` → `ArrayMesh` (vertices/normales/UV/indices, skin 4 influences) via
`RenderingServer`/`ArrayMesh.AddSurfaceFromArrays` ; squelette NIF → `Skeleton3D` + `Skin` ;
`KfFile` → `Animation` (échantillonnage dense des clés TBC/Quadratic/B-spline → LINEAR haute
fréquence, ou pose par frame via `KfPose`) ; DDS natif → `Image.LoadDdsFromBuffer` /
`ImageTexture` compressée directement (BC1/BC3/BC5 déjà prêts, mips inclus) ; `FsmFile` →
terrain existant (`TerrainBuilder` réutilisable).

| Gain potentiel | Nature | Magnitude | Statut |
|---|---|---|---|
| **EDENFR seul** (suppression de la dépendance `official`) | données | qualitatif, exigence n°4 | certain |
| **Disque** : 27 Go GLB/PNG + 18 Go dist → ≈ 9 Go corpus (moins en `.epk`) | stockage/distribution | −60 à −80 % | [estimé] |
| **VRAM/RAM textures** : DDS déjà compressés + mips, plus de `CompressImage` CPU ni de PNG décodés | mémoire/CPU | VRAM live 4,5 Go → ~1-1,5 Go ; RSS live 5 Go → ~2,5-3 Go | [estimé] |
| **Teintes/dye en shader** (masque `B` + 3 couleurs en paramètres) au lieu d'une texture RGBA8 par avatar/teinte | mémoire/CPU | supprime la masse « textures générées par joueur distant » | [estimé, très probable] |
| **Temps de chargement** : plus de `GltfDocument` + `GenerateScene` + PNG → direct vers buffers GPU | CPU | glb preload 6-7,5 s → ×2-4 plus rapide | [estimé] |
| **Fidélité animation** : TBC/Quadratic/B-spline réels, **transitions `.kfm`**, variantes packed | qualité | corrige des pertes connues du GLB | partiel (à auditer) |
| **Montures / familiers / équipements** : mêmes clips natifs (`ride04-07`, motion tables) ; partage de squelette (`Skeleton3D`) ; armes via `BoneAttachment3D` | fonctionnel | **ne vient pas du loader seul** : dépend de la logique déjà en Core (RideSeatMath, DirectionalLocomotion, CostumeVisual) ; le gain est de **retrouver les animations qui échouent à la conversion** | à quantifier par audit (comparer le nombre de séquences KF décodables natif vs GLB) |
| **Cartes « propres »** | données | les scènes/spawns viennent de `fr-full` (pas de merge) | certain (dû aux *données*, pas au loader) |

**Ce que le loader NE change PAS** : le coût de rendu (draw calls, nœuds, ombres), le coût
SceneTree, le GC C#. Il réduit mémoire, chargement et dette de conversion, **pas** la frame time.

**Coûts / risques** [estimé] : les appelants sont couplés aux arbres de nœuds issus des GLB
(`ToonMaterializer`, `CollisionBuilder` — meta `collision_helper`, `PropBatcher` — éligibilité
par nœud de proto, `EntityAnimator`/`AnimRetarget`/`GlbAnimFile`, `CharVisual`/`CostumeVisual`,
`AssetCatalog`). Il faut produire des structures équivalentes depuis le NIF ou réécrire ces
consommateurs. Effort : **3-5 semaines** d'agents pour le remplacement complet + 1-2 semaines
de vérification visuelle (même méthode de preuve que §8). Un **spike** (1 prop + le joueur +
une anim TBC) tient en **2-4 jours** et lève l'incertitude.

### 6.3 Optimisations de performance possibles sous Godot (sans perdre le gameplay)

Classées par gain/effort [estimé ; **aucune n'est mesurée dans ce document**] :

| # | Levier | Effet attendu | Effort | Risque qualité |
|---|---|---|---|---|
| 1 | **Textures DDS natives compressées + mips**, suppression du `CompressImage` runtime | VRAM/RAM, bande passante, chargement | moyen (loader) | nul |
| 2 | **Teinte/dye en shader** | supprime des textures par joueur ; RAM live | moyen | nul |
| 3 | **Remplacer `Label3D` (206)** par un atlas/texte batché ou overlay Control | CPU/draw calls, nœuds | faible-moyen | nul |
| 4 | **LOD d'animation** (tick réduit hors écran / hors rayon), `AnimationMixer` manuel, `VisibleOnScreenNotifier` | CPU animation (124 `AnimationPlayer`) | moyen | faible |
| 5 | **Occlusion culling** (`OccluderInstance3D` baké par map) — surtout villes denses (Aven) | draw calls/objets rendus | moyen | faible |
| 6 | **Étendre le PropBatcher** aux multi-matériaux et à l'alpha-blend trié | draw calls | moyen | faible |
| 7 | **Terrain** : fusionner 177 `MeshInstance3D` en chunks 4×4 tuiles | draw calls | faible | nul |
| 8 | **Rendu** : tester le renderer *Mobile*/*Compatibility* (toon unlit n'a pas besoin de tout Forward+), ombres directionnelles allégées (CSM 2 cascades, blob shadows déjà présents) | frame GPU | faible | à valider visuellement |
| 9 | **Threading** : thread de rendu dédié (`rendering/driver/threads`), `WorkerThreadPool` pour décodage/skin | frame CPU/pics | moyen | faible |
| 10 | **Contournement du SceneTree** pour les objets massifs (props, herbe, entités distantes) via `RenderingServer` direct / MultiMesh partout | nœuds 12 k → quelques centaines | élevé | faible (si testé) |
| 11 | **Pré-compilation/pipeline cache** des shaders (pics 140 ms) | stutter | faible | nul |
| 12 | **Allocations C#** : pooling, pas d'alloc par frame dans HUD/entités | P99/GC | moyen | nul |

**Plafond réaliste Godot** [estimé] : 1,5-2,5× la frame time actuelle (soit ~100-150 fps à
contenu égal sur cette machine) avec un effort **élevé** car on lutte contre le SceneTree. Atteindre
« bien plus élevé » (> 3×) pousse vers le contournement du moteur (levier 10), au prix de la
maintenabilité.

---

## 7. Autres moteurs gratuits envisagés (pour un agent IA autonome)

| Moteur | Licence | Langage | Réutilise `Eden.Core` | Perf potentielle | Autonomie agent | Verdict |
|---|---|---|---|---|---|---|
| **Godot 4.7** | MIT | C#/GDScript | oui (net8.0) | moyenne | **très haute** | candidat A |
| **Flax 1.13** | gratuit < 250 k$/trim. | C# | oui | haute | moyenne | candidat B |
| **Moteur C# maison** (Silk.NET + Vulkan/GL, SDL, OpenAL) | libre | C# | **100 %** | **maximale** | **totale** (build secondes, offscreen trivial) | candidat C, **4-7 mois** [estimé] : renderer, toolkit UI + saisie texte/IME, audio, effets/particules |
| Unity Personal | gratuit < 200 k$ | C# | oui | haute | moyenne (éditeur, activation de licence, source fermée) | écarté |
| Stride | MIT | C# | oui | bonne | faible-moyenne (Game Studio Windows) | écarté |
| Bevy | MIT/Apache | Rust | **non** (réécriture ≈ 70 k lignes) | haute | haute | écarté |
| MonoGame/FNA | libre | C# | oui | dépend du code maison | haute | ≈ moteur maison sans bénéfice |
| Unreal / O3DE | redevance / Apache | C++ | non | très haute | faible | écartés (poids, C++) |

---

## 8. Méthode de preuve recommandée (valable quel que soit le moteur)

Leçon principale du projet : **ne jamais déclarer « fait » sans image**. Procédure :
1. Capture **in-engine** (readback GPU, indépendante du bureau) à cadrage fixé (`EDEN_CAM_AT/YAW/PITCH/DIST`).
2. Comparaison à `ref-shots/` (105 captures natives) et à la capture Godot **même cadrage**.
3. Trois niveaux rapportés séparément : build/tests, headless/réseau, rendu.
4. Bench perf **à armes égales** : mêmes PNJ, mêmes effets, HUD, résolution, vsync coupé, charge machine contrôlée, N≥3 runs.

---

## 9. Recommandation et garde-fous

Il n'existe pas de choix dominant :

- **Godot + loaders natifs** : risque minimal, parité atteinte vite (client ~70 %), autonomie agent
  maximale, EDENFR seul rétabli. Plafond perf plus bas (≈ 1,5-2,5× l'existant avec effort).
- **Flax** : meilleure frame time mesurée (2,5-4× selon la référence), RAM moitié, plafond plus
  haut ; mais ≈ 65-70 % du chantier restant, shaders/matériaux à reconstruire, 2 bugs moteur,
  autonomie moyenne, maturité faible.
- **Moteur maison** : plafond maximal et autonomie totale, coût/volume le plus élevé.

**Décision proposée (dépend de la priorité n°1 du propriétaire = performance)** :
1. Faire d'abord **deux tests d'une journée** : (a) **bench à armes égales S001** Godot vs Flax ;
   (b) **spike loader natif sous Godot** (1 prop + joueur + 1 anim TBC) — prouve que le gain
   « natif » est indépendant du moteur et mesure le gain mémoire/chargement réel.
2. Si (a) donne ≥ 2× pour Flax **à contenu égal** *et* que le propriétaire accepte 3-8 semaines
   [estimé] de chantier avec revue à 4 semaines → **continuer Flax**, avec Godot conservé comme
   client jouable de référence.
3. Sinon → **Godot + loaders natifs** (EDENFR seul), puis les leviers §6.3 (1-9) ; considérer le
   levier 10 seulement si la perf reste insuffisante.
4. Moteur maison uniquement si le propriétaire vise le plafond absolu et accepte 4-7 mois.
5. Critères d'abandon Flax (revue à 4 semaines) : < 60 % de parité visuelle **prouvée par capture**,
   ou > 3 nouveaux blocages moteur sans contournement, ou impossibilité de reproduire le patch moteur.

**Estimation du reste à faire Flax** : l'auteur avait annoncé « 6-10 semaines ». La vélocité
observée (≈ 11 000 lignes/jour d'agents en parallèle, 116 commits/2 jours) suggère que le code
restant (~40-60 k lignes) pourrait s'écrire en **~1-2 semaines**, mais la **dette de vérification
visuelle** (chaque système doit être prouvé à l'image, et les « fait » antérieurs se sont révélés
faux) rend **3-8 semaines** plus réaliste. Fourchette large, à contester.

---

## 10. Erreurs passées de l'auteur (pour pondérer la confiance)

- Le STATUS du dépôt marquait **M3 « fait » alors que le terrain n'était visible que par
  en-dessous** (winding). Les jalons M3-M6 étaient validés sur logs/compilation, pas sur image.
- Une première synthèse affirmait que « la performance ne départage pas les moteurs » **avant toute
  mesure** ; la mesure donne 2,5-4× pour Flax.
- Une mesure initiale Godot « 56 fps » incluait le chargement (moyenne 30 s) ; le régime établi
  est 57-59 fps (aujourd'hui) / 90-97 fps ([doc]).
- Les « pistes écartées » pour le joueur invisible (textures, alpha, winding) étaient fausses ; la
  cause réelle était `MaterialSlotIndex`.
- Estimations de durée : non fondées sur une vélocité mesurée (corrigé ici).

---

## 11. Questions ouvertes pour le relecteur externe

1. Le ratio de perf survit-il à un contenu égal (outline toon, brouillard, 263 PNJ, HUD) ?
2. Godot : un renderer *Mobile/Compatibility* + occlusion culling + batching étendu atteint-il
   ≥ 2× sans contourner le SceneTree ?
3. Le loader natif sous Godot (`ArrayMesh`/`Skeleton3D`/`Animation` rééchantillonnée) préserve-t-il
   la fidélité des animations TBC/B-spline ? Combien de séquences KF sont décodables en natif mais
   absentes des GLB ?
4. Flax : le patch `StringAnsi` peut-il être remplacé par un build du moteur depuis les sources ?
   Un shader splat/toon réel est-il réalisable sans l'éditeur (génération de matériaux) ?
5. Le moteur maison est-il réaliste (4-7 mois) vu la surface UI (92 fenêtres) + saisie texte CJK/IME ?
6. Risque légal/éthique : projet de recodage d'un jeu commercial — hors périmètre de ce document.

---

## Annexe A — Commandes de mesure (reproductibles)

```bash
# Godot S001 (régime établi à lire dans le fichier : lignes t=N)
cd portage/eden_godot/godot_project
DISPLAY=:0 godot --path . res://scenes/game.tscn --resolution 1280x720 -- \
  --map=S001 --solo --nohud --perf --perf-seconds=30 --perf-out=/tmp/perf_godot_s001.txt

# Flax S001 (non bridé ; transport vers le node 1 à t=14 s via commande GM scriptée)
cd /tmp/claude-1000/player   # player cooké : tools/flax-cook.sh <dir>
DISPLAY=:0 EDEN_NOVSYNC=1 EDEN_TLS_INSECURE=1 EDEN_AUTO=test123:test123 EDEN_AUTO_PAUSE_MS=2000 \
  EDEN_PERF=1 EDEN_PERF_EVERY=5 EDEN_KEYS="cmd@14://transport_node 1" ./FlaxGame -std -mute

# Capture 3D (+HUD) in-engine
EDEN_SHOT_UI=1 tools/player-shot.sh <player_dir> shots/x.png 30
```

## Annexe B — Fichiers de référence dans le dépôt
`docs/flax/STATUS.md` (matrice de parité), `docs/flax/PARITY-2026-11-01.md`,
`docs/flax/BENCH-S001-2026-11-01.md`, `docs/specs/PERF.md` (Godot), `docs/specs/NATIVE-LOADING-ANALYSIS.md`,
`docs/PLAN-PORTAGE-FLAX.md` (plan d'origine, `portage/docs/`), `.claude/completions/2026-11-01-*.md`
(journal par lot), `eden_flax/Source/Eden.Core/Formats/*` (loaders natifs).
