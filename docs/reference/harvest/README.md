# Harvest — synthèses gameplay importées du portage 4.7

**Importé de `../portage/docs/harvest/` le 2026-11-03 — copie datée,
pas un lien.** Source amont en lecture seule ; si le portage fait
évoluer ces docs, ré-importer (`cp ../portage/docs/harvest/*.md .`),
ne pas éditer ici et là-bas en parallèle.

Ce sont des synthèses « métier » déjà récoltées (wiki FR/EN +
classeur Vendetta + INI natives + décompilé), avec statuts
CONFIRMED/PARTIEL/MISSING et sources citées — à lire **avant** de
re-extraire le wiki ou de ré-implémenter une mécanique. Périmètre :
lvl ≤ 90.

| Fichier | Contenu |
|---|---|
| `classes-catalog.md` | Catalogue des 61 classes : ids, branches, prérequis EN vs FR, variantes raciales — croisé `c_class`/`t_class` |
| `class-expertise.md` | « Class Ability Bonus » / expertise : mécanique, table `S_ClassRankBuff.ini`, piège de vocabulaire (≠ certificats ≠ talents KP) |
| `expertise-stats.md` | Mêmes paliers, version chiffrée : rang `floor(min(niv,100)×0.2)`, enchant par palier, preuve native `RefreshJPAward` |
| `racial-skills.md` | Modèle racial réel : passif par classe + 10 sorts par branche (lv45/60) — pas un sort unique par race |
| `refshots-classes.md` | Spec détaillée des `ref-shots/` liés aux classes (géométrie UI, tooltips, verbatim) — complète `eden_godot/docs/client-assets/ref-shots-catalog.md` |
| `awakened-refs.md` | Références classes éveillées (majoritairement hors périmètre >90, documenté pour ne pas confondre) |

Chemins cités dans ces docs : ils datent du portage 4.7 mais
l'arborescence est la même ici (`eden_godot/`, `eden_server/`,
`knowledge/`, `re/`). Vérifié à l'import : `eden_server/server_us/
Data/db_decoded/S_ClassRankBuff.ini` et `corpus/fr-full/data/db/
c_classrankbuff.ini` existent.
