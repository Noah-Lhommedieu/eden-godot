---
name: godot-engine-upgrade
description: Changer la version du moteur Godot du projet eden-godot (nouvelle beta, RC, stable) ou revenir en arrière. À utiliser pour « mettre à jour Godot », « passer en 4.8 stable », « tester une nouvelle version du moteur ».
---

# Mise à jour du moteur

## Prérequis
- Le zip **mono** Linux du moteur (`Godot_v<ver>_mono_linux_x86_64.zip`)
  et, si un export est prévu, les templates (`.tpz`) de la **même** version.
- Une baseline perf de la version actuelle
  (`artifacts/benchmarks/*/summary.env`).

## Étapes
1. `tools/engine-update.sh <zip> [tpz]`. Le script décompresse, bascule
   `engine/current`, réécrit le Sdk du csproj et `GODOT_EXPECTED`, vide
   `.godot/mono`, importe, builde et teste. En cas d'échec, il remet
   l'ancien moteur tout seul.
2. `tools/benchmark.sh S001 3 35`, puis
   `tools/compare.sh <baseline> <nouveau>`. Les champs `draws`, `prims` et
   `nodes` doivent être identiques, sinon la comparaison n'est pas
   équitable.
3. `tools/capture.sh S001`, à regarder et comparer à la capture de
   l'ancienne version.
4. Lire le changelog officiel de la version, en particulier les
   changements C#/.NET (TFM minimal) et la compatibilité des nupkgs.
5. Mettre à jour `docs/knowledge/godot-version-matrix.md` et
   `performance-baselines.md`, et compléter l'ADR-0001 (ou écrire un
   nouvel ADR).

## Rollback manuel
`ln -sfn <ancien dossier> engine/current`, remettre l'ancien
`Godot.NET.Sdk/<ver>` dans le csproj et `GODOT_EXPECTED` dans
`tools/env.sh`, puis `rm -rf eden_godot/godot_project/.godot/mono`.

## Mesuré
4.8-beta2, en réinstallant la même version : du zip à « 1601 tests verts »
en 71 s.
