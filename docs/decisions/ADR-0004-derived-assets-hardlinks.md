# ADR-0004 — Assets dérivés : liens physiques filtrés EDENFR

- Statut : **clos** — 2026-11-02 (phase G : `godot_project/assets/` supprimé, plus aucun dérivé ; voir PLAN-ACTION-NATIF-EDENFR.md)

## Contexte

Le client 4.7 charge des dérivés (GLB, PNG, `.kfm.json`) produits par
`tools/nif2gltf` et `tools/textures`, depuis `godot_project/assets/` (27 Go
dans portage). Il reste 46 Go libres sur le disque (ext4, sans reflink, sans
casefold utilisable sans root).

## Décision

- Les dérivés de portage sont classés par `tools/corpus-validator/edenfr_assets.py`
  (résultat : `docs/provenance/assets-2026-11-02.tsv`) :
  - `SAME` : source fr-full identique octet pour octet à la source
    d'origine. **43 321 fichiers** repris en **liens physiques** (0 octet
    de plus) ;
  - `DIFF` : source fr-full différente. **356 fichiers** reconvertis depuis
    fr-full. 179 modèles ou animations convertis, 38 échecs (voir
    `EDENFR-GAPS`), 40 PNG d'UI recopiés ;
  - `NOFR` / `NOSRC` : source absente de fr-full (merge US, Reforged).
    **37 390 fichiers exclus**.
- Les convertisseurs (`gltf_writer`, `convert.py`, `convert_dds_to_png`,
  `copy_ui_png`, `trim/merge_anim_glb`) **délient** la cible avant
  d'écrire. Sans cela, l'écriture traverserait le lien et modifierait
  `portage/`.
- Garde-fou : `tools/validate-assets.sh` échoue si un dérivé n'a pas de
  source fr-full.

## Sortie

Transitoire, jusqu'au Lot 2 (loaders natifs du Core câblés dans Godot :
NIF → `ArrayMesh`, KF → `Animation`, DDS → `ImageTexture` compressée).
Ensuite, `assets/` disparaît.
