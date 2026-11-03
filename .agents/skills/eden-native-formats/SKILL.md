---
name: eden-native-formats
description: Lire ou corriger les formats natifs Gamebryo/Eden (NIF, KF, KFM, DDS, FSM, SMP, IFL, INI chiffrés, EPK) et les brancher dans Godot. À utiliser pour « parser », « NIF », « KF », « animation native », « terrain FSM », « DDS », « loader natif », « Lot 2 ».
---

# Formats natifs

## Où
- Loaders purs testés : `src/Eden.Core/Formats/` (`NifFile`, `KfFile`,
  `KfmFile`, `KfPose`, `DdsInfo`, `DdsDecode`, `FsmFile`, `IflFile`,
  `NodeMapFile`, `SplatBake`, `WaterBake`) et leurs tests dans
  `tests/Eden.Core.Tests`.
- Copie compilée par Godot : `eden_godot/godot_project/scripts/world/*`
  (FSM, IFL, NodeMap…). **Double source** (ADR-0003) : tout correctif se
  porte dans les deux, puis on vérifie avec
  `diff <(sed 's/^\s*//' A) <(sed 's/^\s*//' B)`.
- Spécifications : `eden_godot/docs/FSM-FORMAT.md`,
  `eden_godot/docs/NATIVE-LOADING-ANALYSIS.md`,
  `docs/legacy/flax/{NIF-FR-VARIANT,V7-TAIL-NOTES,M4-SPEC}.md`,
  `eden_godot/tools/nif2gltf/nif.xml`, `re/` (Ghidra, lecture seule).

## Méthode
1. Parser vers un modèle intermédiaire neutre, sans `Godot.*`, et le
   tester sur des fichiers fr-full réels (tests ignorés si le corpus est
   absent), avec des cas corrompus ou tronqués.
2. Un parseur défensif : bornes, tailles déclarées, erreurs typées, jamais
   de `catch {}` muet. Les variantes EDENFR (FSM V7, NIF dégradés) se
   tolèrent explicitement, avec un commentaire qui renvoie à la note.
3. L'adaptateur Godot ne fait que la conversion (`ArrayMesh`, `Skin`,
   `Animation`, `ImageTexture`), dans une classe séparée du parsing.
4. Preuve : test unitaire, puis `tools/capture.sh`, puis
   `tools/benchmark.sh` si le chargement est touché.

## Pièges vérifiés
- fr-full a des dossiers de map jumeaux (`s002/` pour le `.fsm`,
  `S002/` pour le `.area`/`.ini`).
- FSM V7 : queue non framée et blocs non-tuile dans LAY0. Une grille
  régulière se lit en séquentiel, sinon on fouille (`FsmFile.IsRegularGrid`).
- Interpolations KF TBC, Quadratic et B-spline, plus transitions `.kfm` :
  perdues dans les GLB actuels. C'est le gain principal attendu du Lot 2.
