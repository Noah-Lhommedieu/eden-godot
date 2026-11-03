# Format `.fsm` — scène/terrain par map (Eden Eternal, moteur lapis/HighLand)

> Spécification produite par reversing de `rf_game` (voir provenance en fin de
> fichier) **et validée par hex sur S009.fsm (V20100824) et S001.fsm
> (V20100329)** le 24/10/2026. Fichiers : `raw_assets/map/model/Sxxx/Sxxx.fsm`.

## 1. En-tête (0x58 octets, little-endian)

| Offset | Type | Contenu |
|---|---|---|
| 0x00 | char[4] | `"FSM "` |
| 0x04 | char[9] | `"V20100329"` ou `"V20100824"` (les deux acceptées) |
| 0x0D..0x53 | — | padding zéros |
| 0x54 | u32 | **révision du format** : 2 = V20100329, 3 = V20100824 (vérifié S001/S002/S005/S010=2, S009=3) |

## 2. Chunks `{u32 tag, u32 size}`

`size` = taille totale **depuis le début du header** (les 8 o de tag+size
inclus). Ordre libre, chunks inconnus skippables. Tags observés : `LAY0`,
`GRL `, `GRS `, `OBJ ` (dans cet ordre sur les maps testées).

## 3. `LAY0` — terrain

Header chunk : `{tag, size, gridVtxX u32, gridVtxY u32, cellScale f32,
recordCount u32}` (0x18) puis `recordCount` records de **0x1abf4** o.

**Correction** : `gridVtxX/Y` = dimension de la **grille de vertices**
monde (= tilesPerRow×32 + 1 : 289 = 9×32+1 sur S009, 321 = 10×32+1 sur
S001), PAS un nombre de tuiles. tilesPerRow = (gridVtxX−1)/(gridW−1).
Monde ≈ (gridVtxX−1)×scale = 576 u sur S009 (289×2.0=578 avec le
vertex de bord).

Ex. S009 : grille 289×289 vertices, scale 2.0, 134 records =
81 terrain (9×9) + 53 eau. `size == 8 + 0x10 + count*0x1abf4` (vérifié).

### Record tuile (0x1abf4) — ordre : kind=0 d'abord (x-lent, y-rapide)

**Ordre des records (vérifié)** : tous les records kind=0 (terrain)
d'abord, en ordre **ix lent / iy rapide** (record n ↔ origine
((n/tilesPerRow)×64, (n%tilesPerRow)×64)) ; puis les records kind=1
(eau), mêmes origines que certaines tuiles terrain — les doublons
d'origine sont TOUJOURS de l'eau (53/53 sur S009 : TexSlots
`water_env.dds`/`waterani_normal.ifl`, splatmap entièrement nulle,
hauteurs aplaties à ~9.5 et fond à −30).

| Off | Type | Contenu |
|---|---|---|
| +0x000 | u32 | gridW (33 constaté) |
| +0x004 | u32 | gridH (33) |
| +0x008 | f32 | cellSize (2.0) |
| +0x00C | u32 | **kind** : 0 = terrain, 1 = eau |
| +0x010 | Vertex[33*33] stride 0x28 | `{f32 x,y,z; f32 a,b,c; f32 d,e,f,g}` — xyz = position **monde absolue** (v0 = origine de la tuile, ex. rec1 v0=(0,64,z)) ; abc = **normale** (unitaire vérifié, +z dominant) ; defg = couleur RGBA — HYPOTHESIS (1,1,1,1 constaté, pourrait être un tangent inutilisé) |
| ~0xaa58 | 0x20 | **gap confirmé** (fin vertices réelle 0xaa38 → 0xaa58) |
| +0xaa58 | u8[128*128*4] | splatmap 128×128 RGBA = poids des 4 couches blend (canal dominant, seuil ~0xa1) |
| +0x1aa58 | 0x20 | gap confirmé |
| +0x1aa78 | TexSlot[5] stride 0x4c | couches : slot0 base, 1-4 blend |
| +0x1abf4 | fin | |

### TexSlot (0x4c)

```
+0x00  u8     used (0/1)
+0x01  char   nom .dds NUL-terminé (ex. "mount_04.dds") — résolu sous map/texture/
+0x44  u32    vu = 0 sur S009 (flag/field inutilisé ?)
+0x48  f32    uvScale (tiling ; ratio vs slot0)
```

Indices absents : reconstruits ((W-1)*(H-1) quads → 6 u32/quad,
winding CCW vu depuis +z).
UVs terrain générés : posMonde/tailleTuile × uvScale (relatif) —
convention client, non native.
Noms de couches observés : mount/dust/grass/road ; eau : water_env,
waterani_normal.ifl.

**Splatmap → slots (HYPOTHESIS)** : les 4 canaux pondèrent les slots
**1..4** ; la base (slot0) = `1 − Σ canaux` — sommes de canaux < 255
observées, pixels (0,0,0,0) → base pure. Alternative non écartée :
R = slot0.

## 4. `GRL ` — types d'herbe

`{tag,size,count u32}` + records **44 o** : `u32 typeId ; char
modelName[40]` NUL-terminé — **correction** : ce n'est PAS « 8o handle
+ 32o params », c'est un nom de modèle herbe (« UE704M.nif »,
« UE704MN.nif » sur S009 ; « UE735M.nif », « UE739M.nif », « UE735MN.nif »
sur S001). Validé : count=2,3.

## 5. `GRS ` — instances d'herbe

`{tag,size,count u32}` + records **32 o** : `u32 typeId ; f32 x,y,z ;
f32 scale (~1.0-1.7) ; u32 0 ; f32 a ; f32 b` — a,b plausibles
rotation/direction, sémantique exacte non prouvée.
Validé : 3292, 5160 records.

## 6. `OBJ ` — objets de scène (validé 2321/3769 records, 92 o)

```
+0x00  f32 pos.x, pos.y, pos.z        (12o)
+0x0C  f32 rot[9]  matrice 3x3        (36o)
+0x30  f32 scale   (0→1.0)
+0x34  char name[40]  code modèle (ex. "UE013", "UB124") → map/model/object/<name>.nif
```

Variantes constatées : des codes ont plusieurs fichiers `UB124_1.._5.nif`
(alternatives ou LOD — règle exacte inconnue ; le record ne stocke que le
code de base).

## 7. Fichiers voisins (hors .fsm)

- `Sxxx.area` : ligne1 `w h` → grille logique w*96 × h*96 unités
  (PROUVÉ `rf_game FUN_006eb940`, const 96.0f). **Attention** : ce
  n'est PAS la taille du terrain rendu — S009 « 9 9 » → 864 u ≠ fsm
  576 u ; le .area sert de bornage logique (gameplay/minimap), les
  dimensions géométriques viennent du fsm. Lignes 2-3 : rect de tuiles
  (hypothèse).
- Maps sans .fsm : ~35 maps (S018, S035, S039, S095, S097, S100+…) ne
  contiennent qu'un `Sxxx.nif` unique — le monde entier est un modèle,
  pas un terrain tuilé.
- `Sxxx.ini` (même dossier) : environnement (heures, couleurs lumière/fog).
- `map/texture/*.dds` : textures de terrain.
- `map/sknodemap/Sxxx.{smp,fsmp,pgp}` : navmesh/collision (non traité ici).
- `Dark/NNNDarkMap.dds` : darkmaps par tuile, index implicite tuile#n.
- `treeobject.ini` : objets supplémentaires éventuels.
- `data/scene/sXXX.ini` : spawns/monstres/NPC/portails (côté gameplay, déjà
  parsé côté client : `scripts/data/tables/SceneTable`).

## 8. Inconnus restants

- Contenu exact des gaps 0x20 (sous-headers ? checksums ? — zéros sur
  les maps vues).
- Sémantique des champs GRS scale/a/b (rotation ? variation ?).
- Vertex attr #3 : **résolu** — éclairage baké RGBA (scan complet
  25/10 : valeurs 0.0-1.0 non blanches sur ~40 maps, ombres portées
  B≥G≥R ; tuiles eau : teinte + alpha de rive). Appliqué en rendu.
- Correspondance canal splat → slot (HYPOTHESIS §3).
- Matrice 3×3 OBJ : row-major avec `world = rot * local` — la colonne 0
  est l'axe local +x. Validé sur la série `UB107/UB108` de S002 (les
  parapets suivent leur chemin à |dot|≈0,997 contre ≈0,456 si transposée).
- Variante `_1.._5.nif` des codes objet (LOD/alternative inconnue).
- Éventuels chunks non observés (fog, sons) — aucun vu sur les maps testées.

## Provenance

`rf_game` : `FUN_00b07450 @0x00b07450` (CHighLand::Load), fichiers
`FUN_00b12ea0/00b130b0/00b13060` (open/find-chunk/read), tiles
`FUN_00b032b0/00b02100/00b008b0/00b0d030/00b0de80/00b0e4f0`, grass
`FUN_00b10230/00b032d0`. `client_game` partage les mêmes adresses.
