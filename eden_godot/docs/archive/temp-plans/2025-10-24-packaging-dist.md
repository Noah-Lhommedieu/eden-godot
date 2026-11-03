# Plan — packaging autonome (dist/package-*)

## Cible
Export Godot autonome : toutes les données runtime sous
`<exe>/gamedata/` (ou `$EDEN_DATA_DIR`), dev-mode inchangé.

## Étapes
- [x] Inventaire accès fichiers : ScreenManager(uidata), UiDemo(uidata),
  CeguiToGodot(imagesets fallback), AssetCatalog(assets+raw),
  ClientConfig(client.cfg), MapLoader/GameScene/CharCreateScreen via
  AssetCatalog.ResolveRawAsset. Tables = chemins absolus déjà résolus.
- [x] DataPaths.cs : CandidateDataRoots / FindSubdir / ResolveFile /
  ExeDir.
- [x] Recâblage call sites.
- [x] tools/package_dist.sh (+ --full pour copie assets 22 Go).
- [x] build + tests + exports + run headless + doc DISTRIBUTION.md.
