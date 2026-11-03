# Animations — `.kf` / `.kfm` Gamebryo

`.kf` = clip d'animation (courbes TBC/Quadratic/B-spline — **perdues
dans les GLB actuels**, c'est un gain attendu du Lot 2).
`.kfm` = graphe de séquences (transitions entre clips).
Parser : `src/Eden.Core/Formats/{KfFile,KfmFile,KfPose}`.

## Répartition

| Dossier | Fichiers | Contenu |
|---|---|---|
| `char/` | 7 (0 kf / 7 kfm) | graphes par corps (b/f/g/k/l/m.kfm) |
| `char/animation/` | 7 (7 kf / 0 kfm) | clips du corps de perso (b.kf ≈ 24 Mo…) |
| `decorate/` | 142 (1 kf / 141 kfm) | graphes déco |
| `house/` | 15 (0 kf / 15 kfm) | graphes maison |
| `house/animation/` | 15 (15 kf / 0 kfm) | maison |
| `map/model/object/` | 4 (2 kf / 2 kfm) |  |
| `monster/` | 532 (0 kf / 532 kfm) | graphes par monstre |
| `monster/animation/` | 532 (532 kf / 0 kfm) | 1 clip par monstre (m###.kf ↔ m###.kfm) |
| `ride/` | 159 (0 kf / 159 kfm) | graphes par monture |
| `ride/animation/` | 159 (159 kf / 0 kfm) | montures |

Chaîne monstre : `monsterlist.ini` `DB<code>,<kfm>,…,<skin dds>`
+ `boundinfo.ini` `<code>,<nif>` — relie animation↔modèle↔skin.
Transitions `.kfm` documentées dans `NATIVE-LOADING-ANALYSIS.md`.
