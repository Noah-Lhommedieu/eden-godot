# Inventaire du corpus EDENFR — `corpus/fr-full/`

**47639 fichiers** extraits des paquets EDENFR
(`~/Téléchargements/EDENFR`, 448 `pkgNNN.pkg` + `pkg.idx`),
**lecture seule**. Ancienne extraction : `corpus/fr-full-pak-2026-10`.

Un document par type ; le détail unitaire de chaque fichier est dans
`artifacts/corpus-inventory/files.tsv` (1 ligne/fichier : chemin,
catégorie, taille, métadonnées, description).

| Document | Contenu | Fichiers |
|---|---|---|
| [tables-donnees.md](tables-donnees.md) | Tables de données `data/db/` (141 INI, rôle de chacune) | 141 |
| [scenes-et-cartes.md](scenes-et-cartes.md) | Scènes/cartes par map (fsm, area, sknodemap, csm, minimaps) | 12059 |
| [modeles.md](modeles.md) | Modèles 3D .nif par domaine (6 421) | 6421 |
| [animations.md](animations.md) | Animations .kf/.kfm Gamebryo (1 572) | 1572 |
| [textures.md](textures.md) | Textures .dds par domaine + formats + .ifl (34 972) | 34972 |
| [ui-et-cegui.md](ui-et-cegui.md) | Interface CEGUI + textures UI (uidata/, ui/, UI/) | 16628 |
| [audio-video.md](audio-video.md) | Sons WAV, musiques OGG, cinématiques SWF/OGV | 2552 |
| [systeme-et-divers.md](systeme-et-divers.md) | Racine launcher + formats orphelins | 36 |
| [VISUEL.md](VISUEL.md) | Accès aux images : miroir PNG + contact sheets + index | — |

## Avant de lire un fichier

- **Encodages** : tables `c_*`/`C_*`/`S_*` en **Big5**, `t_*` (textes FR)
  en **CP1252** — jamais de `File.*` direct, passer par `EdenVfs`/
  `AssetCatalog`/`TextResolver` (casse insensible).
- **Pas de `.glb` dans ce dépôt** : le client 4.8 lit les natifs
  (Lot 2). Les dérivés du portage 4.7 vivent dans `../portage/`.
- **Absences authentiques** : des tables citent des fichiers jamais
  livrés → `docs/workflows/EDENFR-GAPS.md` avant de « réparer ».
- Compléments : `MODELS-CATALOG` (modèles), `items.tsv` (items),
  `maps.tsv` (maps), `INVENTAIRE-QUETES-MONTURES.md`,
  `reference/harvest/` (synthèses gameplay), `knowledge/` (wiki).

## Régénération

```bash
python3 tools/corpus-inventory/inventory.py    # → artifacts/corpus-inventory/files.tsv
python3 tools/corpus-inventory/render_docs.py  # → docs/reference/corpus/*.md
python3 tools/corpus-inventory/convert_dds.py  # → artifacts/corpus-png/ (miroir PNG)
python3 tools/corpus-inventory/make_sheets.py  # → artifacts/corpus-sheets/ + sheets-index.tsv
python3 tools/corpus-inventory/class_outfits.py # → class-outfits.tsv (skins de classes)
```
