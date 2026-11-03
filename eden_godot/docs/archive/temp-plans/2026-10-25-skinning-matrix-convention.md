# 2026-10-25 — Fix skinning : convention matrice NIF (A1-A2) + euler anims

## Cause exacte (prouvée)

**Le convertisseur interprétait `Matrix33` NIF dans le mauvais sens.**
Gamebryo stocke les matrices en convention *row-vector* (les triplets
stockés = lignes de la matrice ligne = vecteurs de base), soit la
**transposée** de la matrice colonne attendue par glTF/Godot.

Preuve (char/model/B.nif, 5573 os sur 250 skins) :
- avec la convention transposée, `BoneData.SkinTransform == inv(Wb)·Wm`
  pour **5555/5573** os (les 18 restants = pose stockée ≠ bind, normal) ;
- `#GFACE` a S = NiSkinData.SkinTransform = `inv(Wm)` non trivial →
  discrimine `B == inv(Wb)·Wm` (0.001) de `B·S` (1.657) : B est bien
  l'IBM correcte telle quelle ;
- les quaternions .kf des os = `quat(R_stored^T)` exactement (d≈0.000)
  → les tracks quat étaient déjà bonnes ;
- les clés euler XYZ : 35/35 cas décisifs → quat `qz·qy·qx`
  (la composition fichier Rx·Ry·Rz se renverse en passant en colonne).

Conséquence avant fix : joints + IBM avec rotations inversées →
jointMatrix incohérente → vertices skinnés en amas coloré.

## Fichiers touchés

- `tools/nif2gltf/convert.py` :
  - `nitransform_to_mat4` : `mat3_cols(...).T`
  - `trs_from_block` : `mat3_cols(r).T` (node TRS + fallback matrix)
  - `xyz_to_quat_keys` : produit `qz·qy·qx` au lieu de `qx·qy·qz`
  - docstring `mat3_cols` corrigée (elle stocke les lignes, pas colonnes)
- Nouveaux : `scenes/skin_test.tscn`, `scripts/debug/SkinTest.cs`
  (sonde visuelle skinning : `--model= --anim= --animfile= --shot=
  --probe --yaw= --dist=`)

## Régénéré (convert.py --no-resume --only)

- `char/model/` 2565/2567 (2 échecs pré-existants : ver 0x14030202 packed)
- `monster/model/` 1449/1449
- `char/animation/` 17/17
- `monster/animation/` 2919/2940 (21 échecs m962*, même variante packed)
- `ride/` 2022/2024 (2 échecs idem)

## Validation

- Numérique : B.glb bind pose #B202 z∈[0,1.65] humain ; stand01/move01/
  attack01 bbox finies humain ; M025 cohérent.
- Moteur (probe os) : move01 bouge les membres (main delta 0.36 m,
  pied 0.23 m), head à 1.68 m.
- In-game : GameScene S002, corps « g » construit (5 meshes visibles),
  mobs M025/M024 + NPC M001/M012 chargés avec anims.
- Captures : `godot-shots/skinfix/`.

## Reste / notes pour la suite

- **Tous les autres répertoires de modèles (map/, item/, effect/,
  house/, decorate/) contiennent encore l'ancienne convention** —
  les rotations non-identité y étaient déjà inversées avant ; une
  régénération complète est à prévoir (hors scope de cette mission).
- Les headers NIF `0x14030202` (packed, ~25 fichiers) restent non
  supportés par le reader — pré-existant.
- `bspline_failed` (interpolateurs B-spline sans data complète) =
  fallback pose statique existant, non régressé.
