# Plan d'action — 100 % formats natifs EDENFR, données authentiques

Rédigé le 2026-11-02. Il remplace et étend `LOT2-NATIVE-ASSETS.md`.
Il s'appuie sur les règles de `AGENTS.md` (preuves, statuts, liens physiques).

## Outils de repérage (obligatoires à chaque phase)

- **graphify** : deux graphes, à interroger **ensemble** à chaque tâche.
  - eden-godot (`tools/graph/graph.sh`) : savoir où le code actuel doit
    changer ;
  - portage 4.7 (`tools/graph/graph.sh portage`, miroir en lecture seule) :
    retrouver ce que faisait le chemin GLB d'origine.
  
  Exemples : `query "AssetCatalog GetProto"`, `explain
  "EntityAnimator.Attach()"`, `path "MapLoader" "PropBatcher"`. C'est le
  point de départ des phases B3 (contrat d'arbre GLB), D, E et F (trouver
  chaque consommateur) et G (s'assurer qu'aucun appel au chemin converti
  ne reste). Régénérer le graphe en fin de tâche.
- **reagraph** : graphe du code décompilé (`re/ghidra`, `tools/graph/re-graph.sh`).
  Il sert aux phases A1 (format pkg, CRC du manifeste dans
  `game.bin`/`Launcher.exe`), A5 (chargement RapidXML des layouts) et
  F2 (règles des particules Gamebryo). **À installer** : le paquet n'existe
  que sur GitHub, et son installation attend l'accord du propriétaire
  (`tools/graph/README.md`).
- **ponytail** (skills `ponytail`, `ponytail-review`, `ponytail-audit`,
  `ponytail-debt`, règle `.agents/rules/ponytail.md`) : la plus petite
  modification complète, avec un test pour toute logique non triviale. On
  lance `ponytail-review` sur chaque livraison de phase et `ponytail-audit`
  avant G (code mort du chemin converti).

## Avancement (2026-11-02)

| Étape | Statut | Preuve |
|---|---|---|
| A1 format pkg + CRC | `TESTED` | `docs/formats/PKG-FORMAT.md`, `MANIFEST-CRC.md`. CRC = CRC32C ; entrées ≥ 64 Ko = deflate à fenêtre 64 Ko (codes distance 30/31), pas Deflate64. Trouvé par expérience sur `pkg.idx`, sans Ghidra |
| A2 lecteur C# | `TESTED` | `src/Eden.Core/Pkg/*`, `PkgTests` |
| A3 ré-extraction | `COMPLETED` avec exceptions | `tools/edenfr-extract`, `tools/edenfr-verify` : 46 162/47 601 ; 1 439 sons/bgm de taille exacte mais CRC différent, sans autre source (docs/formats/PKG-FORMAT.md) |
| A4 bascule | `TESTED` | `corpus/fr-full` = vérifié ; ancien = `fr-full-pak-2026-10`. `tools/test.sh --no-server` vert. **Reste** : regénérer les GLB dérivés, captures S002 |
| A5 CEGUI | `TESTED` | les 19 layouts se parsent avec les données authentiques ; `KnownMalformedFr` vide. Le parseur RapidXML permissif n'est plus nécessaire |
| A6 tables | `COMPLETED` | absentes du manifeste : fonction absente du jeu d'origine |
| B parité + bascule | `TESTED` | `tools/native-parity` : 199/200 (4 blocs de 50 : 50/50, 50/50, 49/50, 50/50 ; seul écart `char/model/C1716` nodes+tex). `EDEN_NATIVE` absent = **tout natif** depuis 2026-11-02 ; `EDEN_NATIVE=none` ou `derived` = ancien chemin GLB/PNG (A/B conservé jusqu'à G). `tools/test.sh --no-server` vert au 2e passage ; le 1er avait 3 échecs non liés au natif (Dictionary modifié en énumération dans UiTextIsTranslationTests/QuestCatalogTests, TlsConnectionTests) : tests instables, à corriger |
| C textures DDS | `IMPLEMENTED` + captures regardées (S001, S002, S009, S018, S035) | BC1/2/3/5, RGBA, masques 16/24 bits chargés sans décompression. 0 texture absente dans les logs de capture. Écarts à connaître : (1) 98 PNG dérivés (monster 7, char 86, item 2, ride 3) ont une couleur moyenne différente du DDS de `fr-full` (ex. `M08604` : Gardien vert en dérivé, violet en natif sur S009) : le natif suit la source EDENFR, le dérivé semble issu d'une autre source, à enquêter (provenance dit SAME, par nom seulement) ; (2) frange noire sur les bords des feuillages à alpha test (pétales S009) : le DXT1 d'origine a du noir sous l'alpha 0, le dérivé repasse par un encodeur BC7 qui l'atténue. `CompressImage` n'est pas retiré |
| D modèles fixes | `VISUALLY_VALIDATED` sur S001, S002, S009, S018 (intérieur), S035 (intérieur) | Écart de rendu trouvé et corrigé : NiAlphaProperty « blend » donnait `Transparency.Alpha` en natif, alors que l'import glTF de Godot donne `AlphaDepthPrePass` + `depth_draw=OpaqueOnly` (convert.py : blend prioritaire sur test). `ToonMaterializer` ne convertit que `Alpha` en `AlphaHash` : les murs en pierre sortaient avec un dither noir/blanc. Après correction, log MapLoader identique au dérivé : S001 273 GLB, 1102 multimeshes, rejets alphablend×235 ; S002 96 GLB, 709 ; S009 264 GLB, 884. Second écart corrigé : `GrassRenderer` lisait `mi.Mesh` sans l'override de surface posé par `NifScene`, d'où des quads gris opaques (S002) ; il propage maintenant le matériau en `MaterialOverride` (vérifié sur une capture S002 ; d'autres consommateurs de `mi.Mesh` seul restent à auditer) |
| E skin + anim | `IMPLEMENTED` | actif par défaut ; non revalidé visuellement dans cette étape (aucune capture de personnage regardée), parité char/monster 100 % sauf C1716 |
| F fx | `IMPLEMENTED` | actif par défaut ; non revalidé visuellement dans cette étape |
| Bench natif vs `EDEN_NATIVE=none` | `PERFORMANCE_VALIDATED` (frame, RSS) | 3 runs × 20 s, `tools/benchmark.sh` + `compare.sh`, RTX 4060 Laptop : S001 frame p50 11,80 → 11,80 ms (0 %), GPU p50 3,75 → 3,76 ms, RSS p50 2067 → 1939 Mo (-6,2 %), draws 1613 = 1613 ; S002 frame p50 5,10 → 5,10 ms, GPU 1,56 → 1,55 ms, RSS 1861 → 1702 Mo (-8,5 %), draws 587 = 587. Temps de chargement non mesuré. Résultats : `artifacts/benchmarks/20261102-0356*-S001` (natif), `-0357*` (none), `-0359*`/`-0400*` (S002) |
| P EPK strict | `TESTED` (run strict : `VISUALLY_VALIDATED` S001, S002 ; `PERFORMANCE_VALIDATED`) | Clés : paquets bâtis avec les clés dev de release (`~/.config/eden/release/{sign.pem,dev-keyfile.txt}`, celles embarquées dans le client) via `edenpack build --native <dir> --sign-key … --keyfile … --version 1` (2 min, 6,8 Go) ; run : `EDEN_PACKS=<dir> EDEN_ALLOW_LOOSE=0 EDEN_PACK_STRICT=1 EDEN_PACK_B=<champ b= du keyfile>`. Corrigé : `EpkReader` vérifie le sha256 des blobs des paquets en clair (test de falsification) ; `AssetCatalog.TryGetGlb` n'exigeait un `.glb` (tous les objets devenaient des placeholders sans dérivés) ; l'index DDS de `NifMaterials` venait du disque (maintenant EPK) ; `EdenVfs.ListDir` récursif dédupliquait par nom de fichier (perte de textures homonymes) ; atlas CEGUI, fond de carte, polices et textures de dégâts lus via le VFS. Captures strict ≈ loose : S001 273 GLB/1102 multimeshes, S002 96/709, 0 placeholder, UI identique (écart mesuré 0,01–0,3/255). Bench strict 3×20 s : S001 frame p50 11,80 ms (= loose), RSS 1921 Mo (loose 1939), draws 1613 ; S002 5,20 ms, RSS 1692 Mo ; chargement S001 7,9 s (loose natif 7,8 s, dérivés 15 s). Falsification : un octet dans l'index → pack non monté ; dans un blob → lecture refusée (`sha256 du blob ≠ index`). Garde-fou : `VfsReadGuardTests` plafonne les `File./Directory./FileAccess./DirAccess.` par fichier. P6 : `exclude_filter` d'export resserré, `.pck` exporté = 15 Mo, code/scènes/shaders + `eden_default_theme.tres` + `FONT01.ttf`, aucune donnée de jeu. **Non fait** : P4 part B servie par le serveur au login (la part B vient d'`EDEN_PACK_B`) ; audio (aucun lecteur) ; `emblem/`, `cutscene/` sans lecteur confirmé |
| G retrait des formats convertis | `TESTED` | `godot_project/assets/` supprimé (liens physiques ; `portage/` intact), `GltfDocument` et `EdenGltfMipmaps` retirés d'`AssetCatalog`, `GlbAnimFile`/`AnimRetarget`/`AnimPathMap` supprimés (noms d'os natifs, aucun retarget ; `AnimNames` garde les listes de noms), `TexBake`/`ScnBake`/`PocScn`/`NativeParity` supprimés, `NativeMode` toujours actif (plus d'`EDEN_NATIVE`), `ui_textures/themes` (non EDENFR, G8) déplacé dans `eden_godot/tools/legacy/ui_themes_non_edenfr`, `package_dist.sh` et `native-parity` dans `eden_godot/tools/legacy/`. `tools/validate-assets.sh` appelle `native-coverage --all`. Garde-fou : `AucunChargementGltf`. `tools/test.sh --no-server` vert 3 fois de suite (708 + 946 tests). **Reste** : les noms `TryGetGlb`/`LoadGlbNode` désignent maintenant un chemin virtuel vers le `.nif` (renommage à faire) ; `CompressImage` et `edenpack bake-tex/bake` non retirés ; `Reforged` hors périmètre |
| native-coverage `--all` | `TESTED` | `tools/native-coverage/native-coverage.sh --all` : décode 34 927 DDS, 6 420 NIF, 716 KF, 856 KFM, 45 IFL, 63 FSM de fr-full (2 exceptions authentiques documentées) et résout les 7 369 références de fichiers des 483 tables `data/` (5 absentes du jeu d'origine, EDENFR-GAPS G12). Limite : la résolution se fait par nom de fichier ; les références par code (id → modèle) ne sont couvertes que par les captures |
| H validation finale | `IN_PROGRESS` — **le plan n'est PAS terminé** | Voir « Défauts connus » ci-dessous. Fait : captures strict de 17 maps (0 placeholder), 6 corps joueur, 2 monstres, 3 montures, 3 effets ; session live idle de 21 min sur le client exporté (arrêtée, 1 h non atteinte). |

## Objectif et critères de fin

À la fin, le client Godot ne charge **que** des fichiers EDENFR d'origine :
`.nif`, `.kf`, `.kfm`, `.dds`, `.ifl`, `.fsm`, `.smp`, `.ini`, `.layout`…
Aucun `.glb`, `.png` converti ou `.json` dérivé. Toutes les animations, tous
les effets et toutes les particules fonctionnent.

C'est **fini** quand toutes ces conditions sont vraies, chacune prouvée par
un outil du dépôt :

1. `godot_project/assets/` n'existe plus. `GltfDocument` n'est plus appelé
   pour du contenu de jeu. Un garde-fou de test échoue si un `.glb` ou un
   PNG dérivé est chargé.
2. Le corpus est **authentique** : les 47 601 fichiers du manifeste officiel
   `GameDataFileList.txt` ont la bonne taille et le bon CRC
   (`tools/edenfr-verify`).
3. **Couverture native à 100 %** : chaque référence de chaque table EDENFR
   (objets, équipements, armes, montures, familiers, sorts, buffs/états,
   effets, PNJ, monstres, maps) se résout, se charge et s'anime. Rapport :
   `tools/native-coverage`.
4. Les captures de référence de toutes les maps, des classes, des montures
   et de sorts sélectionnés sont comparées à `ref-shots/` et au client
   d'origine.
5. La perf est ≥ à la baseline 4.8 sur S001 (frame P50 ≤ 12,0 ms).
   Mémoire et chargement sont mesurés et documentés.
6. **Le client packagé lit uniquement des EPK** chiffrés et signés
   (ADR-0005), qui contiennent les fichiers natifs tels quels. Chaque
   entrée est reliée au CRC du manifeste officiel. Toutes les preuves
   1 à 5 sont refaites en `EDEN_ALLOW_LOOSE=0` et `EDEN_PACK_STRICT=1`.

## Faits établis (mesurés le 2026-11-02)

| Fait | Conséquence |
|---|---|
| `~/Téléchargements/EDENFR/` = client FR complet : `game.bin`, 448 `pkg/pkgNNN.pkg`, `extracted/` (9,0 Go), `qb_out/` (7,4 Go), `GameDataFileList.txt` | Toutes les sources EDENFR sont sur disque |
| `GameDataFileList.txt` = **manifeste officiel** : `chemin:date:taille:CRC` pour 47 601 fichiers | C'est l'arbitre objectif. Inutile de choisir une source à l'aveugle |
| fr-full et extracted ont **la taille du manifeste** pour 100 % des NIF, KF, KFM, DDS, FSM, INI et layouts, mais leur contenu diffère sur ~1 900 fichiers | Les versions dégradées gardent la taille et mettent des zéros. Une seule copie est la bonne |
| Le CRC du manifeste **n'est pas** un CRC32 zlib (ni adler32, ni ses variantes simples) | L'algorithme est à retrouver dans `Launcher.exe` ou `game.bin` |
| `game.bin` construit les noms de paquets avec `%s\%s%03d.pkg` ; présence de `delFileHistory.txt` et de `packver.txt` | Paquets de patch incrémentaux : l'ordre de lecture du client compte |
| Client d'origine : CEGUI avec **RapidXMLParser** (log `CEGUI.log`) ; les fenêtres « mal formées » n'apparaissent pas dans le log | Il faut un parseur aussi permissif que RapidXML. Certaines des 19 fenêtres sont peut-être inutilisées |
| Blocs NIF/KF de fr-full (7 077 fichiers) : particules `NiParticleSystem` dans **2 317** fichiers ; `NiTextureTransformController` 2 344 ; `NiAlphaController` 2 341 ; `NiBillboardNode` 1 984 ; skin 1 503 ; `NiMaterialColorController` 768 ; B-spline 634 ; morph 209 ; `NiFlipController` 164 ; `NiPathInterpolator` 108 ; `NiLODNode` 5 ; `NiRoom`/`NiPortal` 91 | Liste exacte de ce que le lecteur et le moteur doivent gérer (détail en phase E) |
| Tables à brancher : `effect.ini`, `dyneffect.ini`, `c_spell.ini`, `c_spellreference.ini`, `c_statedependence.ini`, `motion.ini`, `weapon.ini`, `Equip.ini`, `NewEquip.ini`, `SuitEquip.ini`, `ElfEquip.ini`, `4pequip*.ini`, `ridelist.ini`, `c_ridestep.ini`, `c_partner*.ini`, `c_item.ini`, `c_itemmallenchant.ini` | Ce sont les références que `tools/native-coverage` parcourt |

---

## Phase A — Données EDENFR authentiques (corrige les ~1 061 NIF, 37 KF de montures et 19 fenêtres)

**A1. Retrouver le format pkg et le CRC du manifeste**
- Importer `game.bin` et `Launcher.exe` EDENFR dans Ghidra (`re/`, nouveau
  dossier `re/ghidra/edenfr_game`), via l'export existant
  (`re/decompile.sh`).
- Trouver le code de montage des paquets (piste : la chaîne
  `%s\%s%03d.pkg`) : index, compression (Deflate64 vu par 7z),
  chiffrement ou masquage éventuel, ordre de priorité des 448 paquets,
  gestion des suppressions (`delFileHistory.txt`) et des patchs.
- Trouver la fonction qui calcule le CRC des lignes de
  `GameDataFileList.txt` (côté Launcher, à la vérification de mise à jour).
- Livrable : `docs/formats/PKG-FORMAT.md` et `docs/formats/MANIFEST-CRC.md`,
  chacun avec un vecteur de test.

**A2. Lecteur pkg et vérificateur, en C# pur** (`src/Eden.Core/Pkg/`)
- `PkgArchive` : lit un `.pkg` (index, entrées, décompression).
- `PkgFileSystem` : monte les 448 paquets **dans l'ordre du client** et
  applique les remplacements et suppressions.
- `ManifestCrc` : l'algorithme de A1.
- `EdenfrManifest` : parse `GameDataFileList.txt`.
- Tests : vecteurs de A1, un fichier sain (`char/model/b.nif`), un fichier
  dégradé de fr-full (`map/model/object/ub124.nif`, `decorate/model/p330.nif`).
- Rôle limité : **source d'import seulement** (ADR-0005). Le client ne lit
  jamais de `.pkg` ; la distribution se fait en EPK (phase P).

**A3. Ré-extraction vérifiée** (`tools/edenfr-extract`)
- Extraire depuis `pkg/` avec `PkgFileSystem` vers `corpus/fr-v2/`.
- Pour chaque fichier du manifeste : prendre la copie dont la taille et le
  CRC correspondent. Ordre : 1) reconstruction pkg, 2) `extracted/`,
  3) `qb_out/`, 4) `fr-full`.
- Rapport : `docs/provenance/edenfr-verify-<date>.tsv`, avec le statut de
  chaque fichier (`OK-pkg`, `OK-extracted`, …, `AUCUNE-SOURCE`).
- Si aucune copie ne correspond : examiner les paquets plus anciens (patch
  par delta), sinon garder le fichier tel que le client d'origine le lit.
  Un fichier vraiment cassé côté éditeur est **aussi** cassé dans le jeu
  original. Le reproduire est alors la fidélité, et on le documente.
- Livrable : `tools/edenfr-verify` affiche 47 601/47 601 conformes (ou la
  liste exacte des exceptions prouvées).

**A4. Bascule du corpus**
- `corpus/fr-full` devient la version vérifiée. L'ancienne est renommée
  `corpus/fr-full-pak-2026-10` et reste en lecture seule pour l'A/B.
- Relancer `tools/test.sh`, `tools/validate-assets.sh` et
  `tools/capture.sh S002` : les arbres et structures en bois doivent
  revenir. Comparer avec la capture 4.7 et `ref-shots/`.
- Reconvertir les 38 échecs de conversion G2 (tant que les dérivés
  existent), puis fermer G2 et G3 dans `EDENFR-GAPS.md`.

**A5. Fenêtres CEGUI : parseur au comportement de RapidXML**
- `CeguiLayout` : remplacer `XmlReader` par un parseur permissif,
  équivalent à RapidXML en mode non validant (attributs mal quotés, `<`
  dans les valeurs, balises mal fermées, comme le client d'origine).
- Établir la liste des fenêtres que le client d'origine charge vraiment :
  `CEGUI.log` et les appels `loadWindowLayout` dans `game.bin`.
- Livrable : les 19 fenêtres se construisent, `KnownMalformedFr` est
  vidé, et chaque fenêtre utilisée en jeu est capturée.

**A6. Tables absentes** (`C_DailyAward`, `c_collectionpoint`)
- Après A3, revérifier dans le manifeste. Si la table n'y est pas, elle
  n'existe pas en EDENFR : la fonction correspondante est absente du jeu
  original. Le documenter, ne rien inventer.

---

## Phase B — Socle

**B1. Core unique (Lot 1, ADR-0003).** `src/Eden.Core` est référencé par
le csproj Godot. Les copies de `scripts/{net,protocol,session,data,vfs,world…}`
sont retirées. Divergences à fusionner : `WorldConv`, `ThemePalette`,
`ClientSettings`, `EdenVfs`. Preuve : NetTests et Core verts.

**B2. Projet adaptateur `src/Eden.Godot/`** (seul code de rendu à voir
`Godot.*`), dossier `Native/`. Un fichier par responsabilité :
`DdsTexture`, `NifScene`, `NifMaterial`, `NifSkeleton`, `KfAnimation`,
`KfmStateMachine`, `NiParticleRuntime`, `NiControllerRuntime`,
`NifCollision`.

**B3. Banc de parité** (`tools/native-parity`, headless)
- Pour un `.nif` ou un `.kf` : construire l'arbre via l'ancien chemin GLB
  **et** via le chemin natif, puis comparer noms, types, sommets,
  triangles, AABB, textures, os et poses à t = 0, ½ et fin.
- Échantillon : 200 modèles et 50 animations, puis la totalité.
  Rapport : `artifacts/parity/`.

**B4. Bascule par type** : `EDEN_NATIVE=tex,mesh,skin,anim,fx` dans
`AssetCatalog`, pour faire l'A/B phase par phase.

---

## Phase C — Textures DDS natives

- `DdsTexture` : `DdsInfo` → `Image.CreateFromData(w, h, mips, format,
  octets)` sans décompression :
  - BC1 → `Dxt1`, BC2 → `Dxt3`, BC3 → `Dxt5`, BC5 → `RgtcRg` ;
  - A8R8G8B8 / X8R8G8B8 / R5G6B5 / A4R4G4B4 → `Rgba8` (swizzle) ;
  - L8 / A8 → `L8` / `La8` ;
  - mips incluses.
- DDS tronqué : charger les mips complètes ; si la mip 0 est incomplète,
  passer par `DdsDecode` et le documenter.
- Brancher : `AssetCatalog.GetTexture`, matériaux, UI (imagesets CEGUI
  `.dds`/`.tga`), curseurs, terrain (couches de splat), eau (`.ifl` →
  frames DDS), icônes.
- Supprimer `CompressImage` à l'exécution (la recompression S3TC ne sert
  plus).
- Preuves : captures A/B S001, S002 et HUD ; bench (VRAM, RSS,
  chargement) ; 0 texture absente.

---

## Phase D — Modèles fixes (décors, maps, maisons, objets au sol)

À coder dans `NifFile` (lecture) et `NifScene`/`NifMaterial` (Godot) :

- Géométrie : `NiTriShape`/`NiTriStrips` (désentrelacement des strips),
  `NiLines`, UV multiples, couleurs de sommet (`NiVertexColorProperty`).
- Nœuds : `NiNode` (arbre conservé, pas aplati), `NiBillboardNode`
  (1 984 fichiers ; mode billboard Godot ou shader),
  `NiSortAdjustNode`, `NiLODNode`/`NiRangeLODData` (vers
  `visibility_range`), `NiSwitchNode`.
- Matériaux : `NiMaterialProperty` (ambient, diffuse, specular,
  emissive, glossiness, alpha), `NiTexturingProperty` (base, dark,
  detail, glow, decal ; adressage clamp/wrap ; filtre),
  `NiAlphaProperty` (mélange et test, les deux réglages Gamebryo),
  `NiZBufferProperty`, `NiStencilProperty` (2 faces),
  `NiSpecularProperty`, `NiDitherProperty`, `NiShadeProperty`. Le
  shader toon du projet (`toon_lit`) est repris et alimenté par ces
  propriétés.
- Extra data : `NiStringExtraData`, `NiTextKeyExtraData`,
  `NiBooleanExtraData`, `NiIntegerExtraData` (indices de collision,
  noms de points d'ancrage).
- Collision : `NiCollisionData`, helpers `true wall` → `CollisionBuilder`.
- Occlusion : `NiRoomGroup`/`NiRoom`/`NiPortal` (91 fichiers) → données
  pour l'occlusion (Lot 4).
- Lumières : `NiDirectionalLight`, `NiAmbientLight` (17 et 11 fichiers).
- `PhysX` (`NiPhysXScene`, 21 fichiers) : relever l'usage. Si c'est
  décoratif, l'ignorer, et le documenter.
- Brancher : `MapLoader`, `PropBatcher` (instancing par
  (nif × matériau)), `TempleBackdrop`, maisons et familles.
- Preuves : parité ≥ 99 % sur toutes les maps ; captures A/B de chaque
  map de référence ; bench.

---

## Phase E — Personnages, monstres, PNJ, montures, familiers, équipements, armes + animations

**E1. Skinning natif.** `NiSkinInstance`/`NiSkinData`/`NiSkinPartition` →
`Skeleton3D` (os = `NifNode`, pose de bind) et `Skin`, 4 influences.
Parties du corps assemblées par `CharModelSpec` (tête, cheveux, corps,
mains, pieds, costume).

**E2. Animations KF → `Animation` Godot.** Tous les interpolateurs
mesurés :
- `NiTransformInterpolator`/`Data` (Linear, Quadratic, TBC, XYZ Euler) ;
- `NiBSplineCompTransformInterpolator` (634 fichiers),
  `NiBSplineCompFloat`/`Point3Interpolator` ;
- `NiFloatInterpolator` ;
- `NiBoolInterpolator` (visibilité) ;
- `NiPoint3Interpolator` ;
- `NiPathInterpolator` (108) ;
- `NiMultiTargetTransformController`.

Échantillonnage dense (30 Hz, ou les clés exactes quand elles sont
linéaires), pour que la courbe Godot reproduise TBC et B-spline à
l'erreur près, mesurée par le banc de parité (tolérance chiffrée).

**E3. `NiTextKeyExtraData` → événements.** Sons, frames d'impact,
apparition d'effets, pas, début et fin de boucle. Branchés sur
`CombatFx`/`SpellFx` et l'audio.

**E4. KFM → machine d'états.** `KfmFile` (animations, transitions,
durées, types de fondu) → `AnimationTree` ou un mixer manuel
(`KfmStateMachine`). Remplace `.kfm.json` et les transitions codées en
dur.

**E5. Morph** : `NiGeomMorpherController`/`NiMorphData` (209 fichiers ;
visages, créatures) → blend shapes Godot.

**E6. Équipements et armes** : `weapon.ini`, `Equip.ini`,
`NewEquip.ini`, `SuitEquip.ini`, `ElfEquip.ini`, `4pequip*.ini` →
modèles natifs attachés aux os (`BoneAttachment3D` sur les noms
`Bip01 R Hand`, dummies `NiNode`), teintes (`c_charcolor`) en shader,
effets d'arme (lueur d'enchantement `c_itemmallenchant`, traînées).

**E7. Montures** : `ridelist.ini`, `c_ridestep.ini` → modèle et
animations natifs de la monture, siège du cavalier (`RideSeatMath`),
animations de cavalier propres à la monture (`ride04-07`), effets
attachés.

**E8. Familiers et partenaires** : `c_partner*.ini` → même chaîne.

**E9. Retarget** : `AnimRetarget` et `GlbAnimFile` sont supprimés ; les
anims ciblent directement les noms d'os natifs.

Preuves : banc de parité des poses ; captures en mouvement (attente,
course, attaque, sort) pour chaque classe, 10 monstres, toutes les
familles de montures, 5 armes ; test des transitions `.kfm`.

---

## Phase F — Effets, particules et contrôleurs animés (tous les objets, buffs/états, montures, armes, sorts)

**F1. Lecture des particules** (`NifFile`) — tous les blocs présents
dans fr-full :
- systèmes : `NiParticleSystem`, `NiPSysData` ;
- émetteurs : `NiPSysBoxEmitter`, `NiPSysMeshEmitter` (1 832 fichiers),
  `NiPSysSphereEmitter` ;
- modificateurs : `AgeDeath`, `Spawn`, `Position`, `BoundUpdate`,
  `GrowFade`, `Rotation`, `Color` (+ `NiColorData`), `Gravity`,
  `ColliderManager`, `PlanarCollider` ;
- contrôleurs : `NiPSysEmitterCtlr` (+ `NiBoolData`), `UpdateCtlr`,
  `EmitterSpeedCtlr`, `EmitterLifeSpanCtlr`, `EmitterInitialRadiusCtlr`,
  `GravityStrengthCtlr`, `EmitterDeclination(Var)Ctlr`,
  `EmitterPlanarAngleVarCtlr`, `InitialRotSpeed(Var)Ctlr`,
  `ResetOnLoopCtlr`.

**F2. Moteur de particules fidèle** (`NiParticleRuntime`)
- Simulation CPU déterministe des règles Gamebryo (naissance, vie,
  vitesse, déclinaison, gravité, taille et fondu, couleur, rotation,
  collisions planes), en coordonnées locales ou monde selon le système.
- Rendu par `MultiMesh` (billboards) avec le matériau NIF (alpha
  additif ou mélangé).
- Les `GPUParticles3D` sont écartés tant qu'ils ne reproduisent pas ces
  règles : la fidélité passe avant tout.
- Tests : simulation pure dans `Eden.Core` (positions et couleurs à t
  donné), puis captures.

**F3. Contrôleurs de matériaux et de nœuds** (`NiControllerRuntime`) :
- `NiTextureTransformController` (défilement d'UV, 2 344 fichiers) ;
- `NiAlphaController` (2 341) ;
- `NiMaterialColorController` (768) ;
- `NiFlipController` + `.ifl` (164 ; eau, feux, flipbooks) ;
- `NiVisController` (visibilité booléenne) ;
- `NiTransformController` hors KF (objets animés de décor) ;
- `NiGeomMorpherController`.

Ils sont pilotés par `NiControllerSequence` intégrées au NIF
(22 982 séquences) : boucle, aller-retour, une seule fois.

**F4. Branchement gameplay** — chaque source d'effet lue depuis les
tables natives :
- sorts : `c_spell.ini`, `c_spellreference.ini` (incantation,
  projectile, impact, zone) ;
- buffs et états : `c_statedependence.ini`/`t_statedependence.ini`,
  effets attachés aux os, en boucle tant que l'état dure ;
- `effect.ini`, `dyneffect.ini` : catalogue et effets dynamiques ;
- objets : lueur d'enchantement, effets d'équipement et de set
  (`c_equipset`), objets au sol ;
- montures et familiers : effets attachés ;
- armes : effets de main et traînées ;
- événements des text keys (E3) : synchronisation avec les animations.

**F5. Couverture** (`tools/native-coverage --fx`)
- Parcourt **chaque** ligne des tables ci-dessus : résout le fichier,
  le charge, simule 2 s en headless et note le nombre de particules,
  la durée et les erreurs.
- Objectif : 100 % des références résolues et animées, ou une exception
  prouvée (fichier absent du manifeste EDENFR).
- Planche de captures automatique : 1 image par sort, buff, monture et
  arme de référence, dans `artifacts/fx-sheets/`.

---

## Phase P — Client packagé EPK, natif, chiffré et signé (ADR-0005)

L'EPK du portage est déjà implémenté (`eden_godot/tools/edenpack`,
`scripts/vfs/`, `docs/packaging/EPK-FORMAT.md`) : blocs zstd, AES-256-GCM,
ECDSA P-256, `current.json` signé, partie B de clé donnée par le serveur,
`diff`/`apply` et marqueurs de suppression. Il reste à l'adapter au mode
natif.

- **P1. Empaquetage natif.** `edenpack build` prend un arbre natif :
  `corpus/fr-full` vérifié (phase A) et `godot_project/gamedata/`.
  - Codec `zstd`, ou `raw` pour les fichiers déjà compressés : DDS BC*
    en texCodec `raw`, sans recompression.
  - Désactiver `bake-tex` (webp, BC7), `ScnBake` (`.scn`), la réécriture
    des URI GLB et `bake-tables` (`.inib`) : ce sont des formats dérivés.
  - Nouveau manifeste de paquets :
    - `bootstrap.epk` : UI de login, en clair mais signé ;
    - `eden-core.epk` : `data/`, `uidata/`, `map/` (fsm, smp, ini, area),
      `ui/` ;
    - `eden-assets.epk` : `char/`, `monster/`, `ride/`, `item/`,
      `effect/`, `decorate/`, `house/`, `emblem/`, `cutscene/` ;
    - `eden-audio.epk` : sons et musiques EDENFR.
- **P2. Traçabilité EDENFR.** Pendant le build, chaque entrée est
  confrontée au manifeste `GameDataFileList.txt` (taille et CRC, via
  `ManifestCrc` de la phase A2). Le rapport `artifacts/packs/<ver>/provenance.tsv`
  relie le SHA-256 EPK au CRC officiel. Le build **échoue** si une entrée
  hors `gamedata/` du projet n'est pas conforme au manifeste.
- **P3. Lecture native depuis les paquets.** Tous les loaders
  (`NifFile`, `KfFile`, `KfmFile`, `DdsInfo`, `IflFile`, `FsmFile`,
  `CeguiLayout`…) passent par `EdenVfs.ReadAllBytes`/`OpenRead`, jamais
  par `File.*`. Garde-fou de test : `grep` des appels `File.` sur des
  chemins de données dans `scripts/` et `src/`.
  - Les textures DDS sont transmises au GPU depuis les octets du paquet,
    sans fichier temporaire (`Materialize` est réservé aux cas où Godot
    exige un chemin).
- **P4. Chiffrement actif.** `eden-core` et `eden-assets` sont chiffrés,
  `bootstrap` reste en clair et signé. La partie B de la clé est donnée
  par le serveur au login (`eden-server-stack`), et la rotation se fait
  par `cryptoRev`. Le build release refuse les paquets non signés (déjà
  codé).
- **P5. Patchs.** `edenpack diff/apply` entre deux versions du corpus ou
  du projet, avec marqueurs de suppression. On vérifie : application,
  rollback, bascule atomique de `current.json`.
- **P6. Export Godot.** Le `.pck` ne contient **que** le code, les scènes,
  les shaders et l'UI du projet, jamais de donnée de jeu (filtre d'export
  à resserrer). Les données ne viennent que des EPK, posés à côté de
  l'exécutable (`<exe>/gamedata/*.epk`).
- **Preuves :**
  - `edenpack verify` sur chaque paquet ;
  - falsification d'un octet → refus au démarrage ;
  - login, puis S001, S002 et combat en `EDEN_ALLOW_LOOSE=0
    EDEN_PACK_STRICT=1` ;
  - captures identiques au mode fichiers libres ;
  - bench : chargement et RSS, paquet contre fichiers libres.

## Phase G — Retrait des formats convertis

- Supprimer `godot_project/assets/` (les liens physiques ; portage n'est
  pas touché).
- Supprimer `GlbAnimFile`, `AnimRetarget`, le chemin `GltfDocument` et
  `EdenGltfMipmaps` d'`AssetCatalog`, ainsi que `TexBake`/`ScnBake` s'ils
  ne servent plus.
- Déplacer les convertisseurs vers `eden_godot/tools/legacy/`
  (`nif2gltf`, `textures`, `anim`).
- Remplacer `tools/validate-assets.sh` par `tools/native-coverage`
  (toutes catégories, exécuté aussi sur les EPK).
- Retirer de `edenpack` et de `package_dist.sh` les étapes dérivées :
  `bake-tex`, `bake` (`.scn`), réécriture des URI GLB, copie de `assets/`. Ajouter un garde-fou de test : un `.glb` ou un
  PNG dérivé chargé fait échouer le test.
- Mettre à jour les ADR-0004 (clos) et ADR-0002, `asset-pipeline.md` et
  les skills `eden-native-formats` et `eden-edenfr-data`.

## Phase H — Validation finale

- `tools/test.sh` vert ; `tools/native-coverage` à 100 % ;
  `tools/edenfr-verify` conforme.
- Captures de toutes les maps de référence, de toutes les classes et des
  planches d'effets, comparées à `ref-shots/` (et au client d'origine sous
  Wine si possible : `_wine-artifacts/`).
- Bench S001, S002 et une scène de combat avec HUD (3 runs) ; mémoire et
  chargement comparés à la baseline par dérivés.
- Session live d'au moins 1 h **sur le client exporté et packagé**
  (EPK chiffrés et signés) : serveur, login, combat, montures, sorts.
  Ni crash ni fuite.

---

## Ordre et parallélisme

```text
A1 → A2 → A3 → A4        (données : bloquant pour les captures fidèles)
A5, A6                   (en parallèle de A2-A3)
B1 → B2 → B3 → B4        (en parallèle de A)
C  → D → E → F → P → G → H
P1-P3 peuvent démarrer dès que A4 et C sont faits (format des paquets = fichiers natifs)
```

Après B3, les agents peuvent travailler en parallèle sur C, D, E1-E2 et
F1-F2, car ils possèdent des fichiers `Native/*` distincts. Un seul agent
édite `AssetCatalog` et les csproj. F4 et F5 attendent D, E et F1-F3.

## Risques

- **A1 (RE pkg et CRC)** est le chemin critique. Si les paquets sont des
  deltas, la reconstruction demande l'ordre exact du client. Le
  manifeste permet de tout vérifier.
- **Fidélité des particules** : les règles Gamebryo doivent se déduire de
  nif.xml, du comportement observé et de `re/`. On compare aux captures
  du client d'origine.
- **Volume** : 2 317 NIF à particules et 22 982 séquences. La couverture
  automatique (F5) est la seule façon de garantir « tout ».


## Défauts connus (2026-11-02, signalés par le propriétaire, non résolus sauf mention)

Ce plan n'est pas terminé : « fini » exige les captures comparées à `ref-shots/`, ce qui n'a pas été fait pour l'UI.

| Défaut | Statut | Constat |
|---|---|---|
| Blocs noirs (chiffres de dégâts, logos, feuillages) | **corrigé** `IMPLEMENTED` | Godot 4 charge `Dxt1` sans alpha ; 4 643/8 192 blocs de `algorism01.dds` utilisent l'alpha 1 bit. `DdsTexture` décode maintenant ces BC1 en RGBA8. Non revérifié en combat réel |
| Visages des races en rouge (masque non teinté) | **corrigé** `VISUALLY_VALIDATED` (B, M, L, C ; F et K vus de dos) | `CharVisual.WeldFaceNormals` reconstruisait la surface et perdait le matériau posé en override par le natif ; il est reporté sur le mesh. Les textures `?FACES01.dds` des corps M/F/K/L/C n'existent pas sans lettre : le visage vient de la variante `a-f` |
| Latence à l'action | `HYPOTHESIS` | Chaque sort relisait et décodait le `.nif` sur le thread principal (`LoadNativeScene`) : parse mis en cache. Non mesuré en jeu |
| Clic droit sans attaque | **non résolu** | Pistes : le tracker de quêtes (panneau opaque 1210-1600 × 300-700 px) fait que `OverHud()` ignore le clic ; un déplacement ≥ `DragThresholdPx` pendant le clic est pris pour un pilotage caméra. À reproduire en jeu |
| Panneaux (Classes, TopLeftVitals, chat, QuestTracker…) différents de `ref-shots/` | **non traité** | Aucune comparaison systématique n'a été faite ; seuls l'atlas de thème (Purple) et les captures S001/S002 ont été comparés au mode fichiers libres |
| Animations « pas animées » (montures, tout le jeu) | **non résolu** | Les pistes KF se résolvent (r001 : 66/66, 0 perdue ; boucle Linear, 0,67 s) et la pose varie entre les images en banc d'essai, mais le cavalier n'est pas assis correctement sur la monture (jambes, buste décalés) et le comportement en jeu (machine d'états KFM, locomotion, vitesse) n'a pas été observé. Cible de test suggérée : un kitsune |
| Montures et objets manquants | **non audité** | `native-coverage --all` ne couvre que les références par nom de fichier ; les références par code (id objet → monture → modèle) ne sont pas vérifiées. Le client EDENFR ne contient que 29 284/29 782 montures (G6) mais aucun inventaire « table ↔ archive » n'a été fait |
| Quêtes | **non audité** | `c_mission.ini` est lu (`MissionTable`), mais aucune vérification du nombre de quêtes ni de leur jouabilité n'a été faite |
| Maps « 100 % fonctionnelles » | **non prouvé** | 17 maps chargent sans erreur de ressource ; ni collisions, ni PNJ, ni intérieurs n'ont été vérifiés |
| Session live d'1 h avec combat/monture/sorts | **non faite** | 21 min de présence passive ; pas d'outil de saisie sur la machine |
