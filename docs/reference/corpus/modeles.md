# Modèles 3D — `.nif` Gamebryo (6 421 fichiers)

NetImmerse/Gamebryo (header `…File Format, Version 20.2.0.8`).
Parser : `src/Eden.Core/Formats/NifFile` ; spec `tools/nif2gltf/nif.xml` ;
classement par fichier : `MODELS-CATALOG.tsv` (7 991 nif/kf/kfm).

## Répartition

| Dossier | Fichiers | Contenu | Nommage |
|---|---|---|---|
| `char/model/` | ~2 096 | corps/tenues perso (peau, cheveux, classes) | lettre corps `B F G K L M` + `b/f/g/k/l/m/x…` |
| `effect/model/` | ~1 692 | sorts/FX (`sNNNNN`) | `sNNNNN.nif` |
| `map/model/` | ~1 320 | terrain/props par map | `map/model/s###/` |
| `monster/model/` | 531 | monstres/PNJ | `m###`, `M###`, `n###`, `N###` |
| `item/model/` | 465 | armes/objets portés | `w#####` |
| `ride/model/` | 159 | montures | `r###` |
| `decorate/model/` | 141 | décorations guilde/villa | `p###` |
| `house/model/` | 17 | bâtiments maison | `h###` |

Liens : `monster/model/m###.nif` ↔ codes `monsterlist`/`boundinfo`
(kfm + skin) ; tenues de classe `A0xxxx` (`c_class.ini`) → meshes
`#F…` via `4pequip.ini` ; skins d'arme `W#####` (`weapon.ini`).

## Fichiers notables

- `map/map.nif` (12,3 Mo) : **exécutable Windows PE**, pas un
  modèle — authentique mais inutilisable (G12).
- 7 « corps complets » méga-modèles race/genre (MODELS-CATALOG).
- 1 « cabine d'essayage », 107 modèles raciaux/spéciaux `x…`.

Liste unitaire : `artifacts/corpus-inventory/files.tsv`
(`ext=nif`, jointe au catalogue).
