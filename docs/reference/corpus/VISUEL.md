# Accès visuel au corpus — PNG + contact sheets

Complément visuel de `files.tsv` et des docs par type. **Aucune image n'a été
« décrite » par un modèle** : ce sont les pixels réels, à regarder soi-même.

## Les trois couches

| Couche | Chemin | Contenu |
|---|---|---|
| Miroir PNG | `artifacts/corpus-png/<même chemin que fr-full>` | les 35 019 images en PNG, même arborescence/noms que `corpus/fr-full` (`.dds` → `.png`). À ouvrir/`read` directement pour le détail. |
| Contact sheets | `artifacts/corpus-sheets/<dir>/sheet-NNN.png` | planches 5×5, cellules 256 px, nom du fichier sous chaque case — pour balayer un dossier d'un coup d'œil. |
| Index | `artifacts/corpus-inventory/sheets-index.tsv` | `base → png, planche, ligne, colonne` : d'un fichier au sheet qui le contient. |

## Utilisation type

- « À quoi ressemble `ui/itemicon/UI012.dds` ? » → `read artifacts/corpus-png/ui/itemicon/UI012.png`.
- « Parcourir les icônes de sorts » → feuilleter `artifacts/corpus-sheets/UI/skillicon/sheet-*.png`.
- « Quel sheet contient `char/texture/G202AS01A` ? » → `grep G202AS01A artifacts/corpus-inventory/sheets-index.tsv`.

## Décodeur de secours

29 DDS **RGB555 16 bits** (icônes `J###` de classes dans `UI/uiicon/`) ne
passent pas dans ImageMagick — décodés en Python (header 128 o, pixels 5-5-5).
Ils sont inclus au miroir. Si de nouvelles conversions échouent : format à
vérifier dans `convert-failures.tsv`.

## Regénération

```bash
python3 tools/corpus-inventory/convert_dds.py   # miroir (resumable, stop < 3 Go libres)
python3 tools/corpus-inventory/make_sheets.py   # planches + index (incrémental)
```

⚠️ `make_sheets.py` ne régénère que les planches plus vieilles que les PNG :
si un dossier a été modifié entre deux runs, supprimer ses `sheet-*.png` avant
de relancer.

## Description par l'agent (pas de VLM)

Pour une description sémantique d'un fichier précis, l'agent ouvre le PNG
avec `read` (cellules de planche lisibles à 25/sheet, détail = PNG unitaire).

**Descriptions déjà rédigées** (`artifacts/corpus-inventory/`) — à lire avant
de re-décrire :

| Fichier | Couverture |
|---|---|
| `visual-notes-outfits-{1,2,3,boy,other}.md` | tenues de classes, **les 7 corps** (2 960 entrées `class-outfits.tsv`) |
| `visual-notes-class-icons.md` | icônes classes `J###` |
| `visual-notes-skill-icons.md` | 4 742 icônes de sorts (join `c_spell.ini` champ 2) |
| `visual-notes-item-icons.md` | **6 069/6 069** icônes d'items (jointes `items.tsv`) |
| `visual-notes-item-textures.md` | skins d'armes `item/texture/` |
| `visual-notes-monsters.md` | **1 362/1 362** skins monstres référencés |
| `visual-notes-rides.md` | 450 textures montures |
| `visual-notes-ui-atlas.md` | les 34 atlas CEGUI par région |
| `visual-notes-ui-maps.md` | cartes région/minimap par map |
| `visual-notes-char-misc.md` | visages, cheveux, accessoires, costumes |
| `visual-notes-fx-misc.md` | familles effect/, loadingframe, deco |

Jointures dérivées : `class-outfits.tsv`, `equip-visuals.tsv` (11 099 lignes
K-codes), `class-rankbuff-expected.tsv`, `equip-visuals-notes.md`,
`class-rankbuff-notes.md`.
