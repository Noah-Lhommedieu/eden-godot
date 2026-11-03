# ADR-0003 — Source de vérité du Core

- Statut : **proposé** (à trancher au Lot 1) — 2026-11-02

## Constat

Deux copies du domaine pur coexistent :

- `eden_godot/godot_project/scripts/{net,protocol,session,data,vfs,world…}` :
  compilées dans l'assembly Godot et testées par `NetTests` (714).
- `src/Eden.Core` : la version issue du portage Flax, avec 890 tests. Elle
  ajoute les loaders natifs (`NifFile`, `KfFile`, `KfmFile`, `KfPose`,
  `DdsDecode`, `SplatBake`, `WaterBake`) et les correctifs EDENFR (FSM V7,
  CP1252, `NativeOnly`, casse).

## Proposition

- `src/Eden.Core` devient la seule source. Le csproj Godot le référence
  (`ProjectReference`), et ces fichiers sortent de `scripts/`.
- Le travail se fait fichier par fichier, avec `NetTests` et
  `Eden.Core.Tests` verts à chaque pas. Les divergences connues
  (`WorldConv`, `ThemePalette`, `ClientSettings`, `EdenVfs`) se fusionnent
  à la main.
- D'ici là, tout correctif de format ou de donnée se porte **dans les deux
  copies** (règle d'`ARCHITECTURE.md`).

## Risques

Les `#if GODOT` dans les fichiers « purs » (ex. `EdenVfs.Godot.cs`) doivent
rester côté adaptateur. Il faut garder l'API publique stable pour les
scripts Godot.
