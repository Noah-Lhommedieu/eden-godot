> **Phase G (2026-11-02)** : le chemin dérivé (GLB/PNG/`.kfm.json`) n'existe plus dans eden-godot ; le client lit les .nif/.kf/.kfm/.dds d'origine (corpus/fr-full ou EPK natifs). Le texte ci-dessous décrit l'ancien pipeline du portage 4.7. Statut `deprecated`, Godot 4.8-beta2.

# Pipeline d'assets

Statut : **verified** (actuel) / **hypothesis** (cible). Godot 4.8-beta2, 2026-11-02. Chemin par défaut = natif, voir `native-pipeline.md`.

## Actuel (transitoire, ADR-0004)

```text
corpus/fr-full (natif NIF/KF/KFM/DDS/FSM/INI, lecture seule)
   ├─ runtime direct : INI, FSM, SMP, IFL, layouts CEGUI, DDS d'UI      (EdenVfs, casse insensible)
   └─ dérivés offline : godot_project/assets/{models,anims,textures}  (GLB/PNG/.kfm.json)
         43 321 liens physiques (SAME) + 179 reconvertis + 40 PNG UI (DIFF)
```

- Conversion : `EDEN_RAW_ASSETS=corpus/fr-full python3 eden_godot/tools/nif2gltf/convert.py --list <rel.lst>`
  (sortie dans `godot_project/assets`), puis
  `eden_godot/tools/anim/trim_anim_glb.py <glb…>` pour les `.slim.glb`.
- Contrôle : `tools/validate-assets.sh` (provenance), puis capture.
- Pièges : liens physiques (délier avant d'écrire) ; casse des noms (la
  sortie des reconversions est en minuscules, et `AssetCatalog` la résout
  sans tenir compte de la casse).

## Cible (Lot 2)

Loaders natifs de `src/Eden.Core/Formats` (déjà testés) → adaptateur
Godot : `NifFile` → `ArrayMesh`/`Skin`, `KfFile`/`KfPose` → `Animation`
(TBC, B-spline et transitions `.kfm` conservées), `DdsInfo` → `Image`
compressée avec mips. Gains attendus (**hypothesis**, à mesurer) : disque,
RAM et VRAM, chargement, fidélité des animations. Effet nul attendu sur la
frame time.
