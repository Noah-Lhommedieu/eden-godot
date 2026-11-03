---
name: eden-edenfr-data
description: Travailler sur les données et assets du jeu en restant EDENFR pur (corpus/fr-full) — tables INI, layouts CEGUI, dérivés GLB/PNG, reconversion nif2gltf, provenance. À utiliser pour « asset manquant », « table », « texture absente », « reconvertir », « provenance », « corpus ».
---

# Données EDENFR

## Règles (ADR-0002, ADR-0004)
- Source unique : `corpus/fr-full`, en lecture seule. Jamais
  `corpus/official`, le merge US, Reforged ou `../portage/corpus`.
- Lecture via `EdenVfs` / `AssetCatalog` / `DataPaths`, qui ne tiennent
  pas compte de la casse et retirent le préfixe `C_/T_/S_`. Pas de
  `File.*` direct sur une donnée corpus.
- Encodage : `t_*.ini` en CP1252, le reste en Big5
  (`EdenEncoding.TableFallback`). UTF-8 strict est détecté en premier.
- `godot_project/assets/` est en liens physiques : délier avant d'écrire
  (les convertisseurs du dépôt le font).

## Donnée absente
1. Vérifier sous une autre casse ou un autre nom
   (`find corpus/fr-full -iname …`).
2. Si elle est vraiment absente : l'ajouter à
   `docs/workflows/EDENFR-GAPS.md`. Pas de bouche-trou, pas de copie
   depuis official.

## Reconvertir un asset depuis fr-full
```bash
echo "char/model/b1176.nif" > /tmp/lst
EDEN_RAW_ASSETS=$PWD/corpus/fr-full python3 eden_godot/tools/nif2gltf/convert.py --list /tmp/lst --out artifacts/logs/nif_report.txt
python3 eden_godot/tools/anim/trim_anim_glb.py eden_godot/godot_project/assets/anims/<…>.glb   # anims → .slim.glb
tools/validate-assets.sh
```
Les sorties sont en minuscules (casse de fr-full) ; la résolution
insensible à la casse les trouve.

## Pièges connus
- ~1 061 NIF et 37 KF de montures fr-full sont dégradés (G2, G3) :
  `string too long` ou `overrun`, ce qui donne un GLB vide. Ce n'est pas un
  bug du lecteur.
- 19 layouts FR sont en XML mal formé (G4) : liste gelée dans
  `CeguiTests.KnownMalformedFr`.
- Quand le volume d'une table change, recaler le test sur le compte
  **mesuré**, en commentaire avec l'ancien compte.
