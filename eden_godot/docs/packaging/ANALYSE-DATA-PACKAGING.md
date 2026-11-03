# Analyse — chargement des données, packaging chiffré, cible ≤ 10 Go

Date : 2026-10-29. Analyse statique (lecture seule) — aucune
modification. Objectifs : cartographier le chargement, évaluer un
format packagé type pkg chiffré, vérifier les gains perf et la cible
5-10 Go de stockage.

## 1. Comment les données sont chargées aujourd'hui

### Architecture : deux résolveurs, un goulot unique

Tout l'accès fichiers passe par deux classes statiques :

- **`scripts/DataPaths.cs`** — racines de données brutes, par priorité :
  `$EDEN_DATA_DIR` → `<exe>/gamedata` → `raw_assets/` (dev, symlink) →
  `res://gamedata`. Sert `uidata/`, `data/`, `map/`, `client.cfg`.
- **`scripts/AssetCatalog.cs`** — deux racines : `AssetsRoot`
  (`assets/` converti : textures/models/anims) et `RawAssetsRoot`
  (fsm/ini/smp). `ResolveFile` = fast path `File.Exists` + fallback
  **scan de dossier insensible à la casse** mis en cache (`_dirCache`)
  — obligatoire : les données natives mélangent les casses
  (`S009.fsm` vs `s009.ini`). Caches : `_texCache` (échecs inclus),
  `_glbCache` (protos), `_dirCache`.

C'est la bonne nouvelle du projet : **toutes les lectures convergent
vers ~8 API publiques** (`ResolveFile`, `ResolveRawAsset`,
`ResolveAssetFile`, `GetTexture`, `TryGetGlb`, `LoadGlbNode`,
`FindSubdir`, `FindDataRoot`). Un backend archive se branche là.

### Formats lus et APIs consommatrices

| Format | Racine | API consommatrice |
|---|---|---|
| `.ini` tables (~156 fichiers, 54 Mo db) | RawAssets `data/db` | `File.ReadAllBytes`→MyCrypt→`IniTable` (sniff UTF-8/Big5), lazy au premier usage |
| `.ini` scène/env | RawAssets `data/scene`, `map/model/Sxxx` | `File.ReadAllLines` Big5 |
| `.fsm` (~15-20 Mo/map) | `map/model/Sxxx` | `File.ReadAllBytes` (vues lazy) |
| `.smp` | `map/sknodemap` | `File.ReadAllBytes` |
| `.glb` modèles (8 486) | `assets/models/` | `GltfDocument.AppendFromFile` |
| `.glb`/`.slim.glb` anims (4 243 paires) | `assets/anims/` | `FileStream`/`BinaryReader` maison (`GlbAnimFile`) |
| `.png` (58 230) | `assets/textures/` | `Image.LoadFromFile` + `GenerateMipmaps` + `CompressImage` S3TC CPU |
| `.imageset/.scheme/.looknfeel/.font/.layout` CEGUI | `uidata/` | `XDocument.Load/Parse`, `Directory.EnumerateFiles` |
| `.ttf`, atlases UI | `uidata/fonts`, `res://ui_textures` | `FontFile.LoadDynamicFont`, `GD.Load`, `Image.LoadFromFile` |
| `.tsv` (levels, ui_text) | `gamedata/` | `File.ReadLines` |

### Phases de chargement

- **Boot** : `client.cfg` sync ; `CeguiContext.Load` (~1 s de XML
  imagesets/fonts) en `Task.Run` ; proto GLB temple en fond.
- **Login** : `login_bg.jpg` sync ; `PreGameWarmup` construit les corps
  en tâches de fond.
- **Sélecteur** : `CharVisual.BuildBody` en `Task.Run` par avatar.
- **Chargement zone** : `FsmFile.Load` + `PreloadGlbProtos` par
  batches, puis `MapLoader.LoadMap` **sur UN worker** (le parallélisme
  GLB a été revert pour deadlock ObjectDB — `P0.1`, PERF.md).
- **En jeu** : tout est lazy — tables ini au premier usage (c_item
  ~9 Mo, t_dialogue ~80k lignes, motion ~82k), layouts CEGUI à
  l'ouverture des fenêtres, icônes à la demande, protos GLB PNJ/mobs
  drainés ~4 ms/frame (`EntityRegistry`), murs invisibles `.smp`.
- **Seuls scans récursifs** : `QuestObjectiveMarks` (tous les
  `data/scene/*.ini`), `ManifestHash` (`data/db/**/*.ini` SHA-256 à
  la connexion), `SettingsWindow` (`*.ttf`), `CeguiContext`
  (`*.imageset`/`*.font`).
- **Écritures** : `settings.cfg` (gamedata/), `user://*`
  (clientSettings.json, cfg, last_char). **Aucune écriture sous les
  racines de données** — une archive peut être strictement
  en lecture seule.

## 2. Format d'origine (référence)

`tools/unpacker/pkgfs_unpack.py` : `pkg.idx` (header 292 o + records
592 o : offset, zsize, crc, flags, size, name[260]+name2[260],
numéro pkg) + `pkgNNN.pkg` — compression **zlib ou Deflate64**,
flag 0x40 = brut. **Aucun chiffrement** : le format d'origine ne
protégeait rien, c'est pourquoi le corpus s'extrait avec ~350 lignes
de Python.

## 3. Chiffrement — réponse honnête

**« Incassable de façon absolue » n'existe pas pour des assets qu'un
client doit décoder à runtime.** La clé vit forcément dans le client ;
un attaquant déterminé l'extrait du binaire, hooke la fonction de
décodage, ou dump la VRAM/la RAM. C'est physique, pas cryptographique.

Ce qui **est** atteignable — et déjà excellent vs le statu quo
(données en clair + PNG loose) :

- **AES-256-GCM** (ou XChaCha20-Poly1305) par blob — mathématiquement
  incassable ; le tag GCM donne l'intégrité gratis (anti-tampering).
- **Clé dérivée en plusieurs parts** : part obfusquée embarquée
  (KDF style whitebox) + part délivrée par le serveur au login →
  l'archive seule, sans compte/serveur, ne décode rien.
- Index chiffré + signé (Ed25519) — le listing lui-même reste
  indisponible sans boot légitime.

La menace restante (dump GPU/RAM, hook RenderDoc) est irréductible —
même AAA. Recommandation : AES-256-GCM + clé en deux parts, et
considérer la protection comme « dissuasion forte », pas « absolue ».

## 4. Design minimal du pack

Un seul fichier `eden.epk` (ou 2-3 shards par namespace) :

```
header : magic, version, indexOffset, indexSize
index  : hash(path.lower()) → {offset, zsize, size, flags, nonce}
         — chiffré AES-256-GCM, chargé au boot (~3-5 Mo pour ~80k entrées)
blobs  : données, zstd level ~19 par blob (ou store pour WebP/ASTC)
         + AES-256-GCM par blob ou par chunk de 4 Mo
```

Branchement : backend dans `AssetCatalog`/`DataPaths` qui sert des
`byte[]`/`Stream` par chemin — loose files conservés en fallback
(dev inchangé, override `$EDEN_DATA_DIR` prioritaire). APIs à
rerouter : `AppendFromFile` → `AppendFromBuffer`, `Image.LoadFromFile`
→ `Load{Png,Webp,Jpg}FromBuffer` (déjà utilisé par `EdenGltfMipmaps`),
`File.*` → `IVfs`, `XDocument.Load(path)` → `Load(Stream)`,
`Directory.Enumerate*` → index de dossier de l'archive. Estimation :
un `EdenVfs` ~300-400 lignes + reroutage des ~8 API.

## 5. Gains de performance attendus

Le container seul gagne peu ; les vrais leviers :

1. **Format textures GPU-ready** — aujourd'hui chaque PNG paie :
   décodage PNG + `GenerateMipmaps` CPU + `CompressImage` S3TC CPU.
   Stocker des DDS/ASTC déjà compressés + mips embarqués élimine les
   trois étapes (zéro decode) et la VRAM. C'est le plus gros gain
   chargement dispo. Alternative runtime-loadable : WebP (supporté
   nativement par `Image.LoadFromFile`, ~1,5-2× plus rapide à décoder
   que PNG).
2. **Index O(1)** — remplace les scans `_dirCache` par un hash lookup ;
   supprime les 3 scans récursifs (`data/scene`, manifest `data/db`,
   imagesets/fonts).
3. **I/O regroupées** — un fichier mmap'able vs ~75 000 inodes :
   prefetch séquentiel, page cache OS unique, fin des opens par
   milliers. zstd décompresse ~10× plus vite que le Deflate64/zlib
   d'origine.
4. **Ne change pas** : le parse GLB (`GltfDocument` séquentiel,
   ~7,5 s de preload sur S001 live) — poste dominant du chargement
   map, orthogonal au packaging.
5. Bonus possible : tables `.ini` lazy → index binaire précompilé au
   build du pack (élimine les hoquets première-ouverture).

## 6. Budget poids — cible 5-10 Go

Mesures réelles (`du`) :

| Poste | Actuel | Après levier | Levier |
|---|---|---|---|
| anims | 14,0 Go | **5,16 Go** | ship `.slim.glb` seuls (fat = poids mort — le runtime préfère déjà slim ; `ResolveAnimPath`) |
| models | 3,3 Go | ~2,2 Go | zstd dans le pack (float32 GLB compressent ~30-40 %) ; quantization meshopt optionnelle → ~1,5 Go |
| textures | 9,3 Go | ~3,0 Go | WebP q80-90 (runtime-ready) ; ASTC ~2-2,5 Go mais exige pipeline custom |
| uidata+data+map meta+pck+binaire | ~1,3 Go | ~1,3 Go | incl. élagage ~0,5 Go de morts (5 700 `Dark/*.png` non référencés, 2 009 `*.kfm.json`, backups) |
| **Total** | **~27 Go** | **~11,6 → ~9,9 Go** | + zstd global → borne basse ~9 Go |

Détail anims (mesuré) : monster slim 3,28 G + char slim 1,20 G +
ride slim 0,68 G ≈ **5,16 Go** — plus que l'estimation du ratio B.glb
(les slim monstres gardent plus de pistes). Sous 8 Go exige soit la
quantization anims+models, soit le **slicing par map + téléchargement
à la demande** (install de base ~4-6 Go, zones fetch à l'approche).

## 7. Recommandation

1. `EdenVfs` + `eden.epk` (zstd + AES-256-GCM + index chiffré) —
   branchement `AssetCatalog`/`DataPaths`, loose files en fallback dev.
2. Élagage slim-only + fichiers morts → ~19 Go de corpus utile.
3. Textures → WebP (ou ASTC si on accepte un mini-parseur) → ~12 Go.
4. zstd pack → **~9-10 Go** ; slicing par map si la cible basse
   (5 Go) est ferme.

Le tout est atteignable sans toucher au gameplay : le modèle de
chargement actuel (résolveurs centralisés + caches) est fait pour ça.

---
Non mesuré : couverture slim 1:1 exacte (vérifier les `.glb` sans
jumeau avant suppression — commande `find` dans le rapport source) ;
support DDS à runtime sous Godot 4.7.2 (`Image.LoadFromFile` ne lit
pas .dds — prévoir parseur mip0 ~100 lignes ou rester sur WebP) ;
perf réelle zstd+AES sur ARM (négligeable sur desktop, à mesurer
mobile). Risque principal : la cible 5-6 Go n'est tenable qu'avec
slicing par map — à valider contre la promesse « tout le jeu offline ».
