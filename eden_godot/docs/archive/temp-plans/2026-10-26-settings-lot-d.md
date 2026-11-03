# Plan d'action — Lot D Paramètres (26/10/2026)

Objectif : paramètres complets + persistants (`gamedata/settings.cfg`),
fenêtre déplaçable à onglets (Affichage/Graphismes/Interface/Touches/
Audio), ouverte depuis le menu HUD bas-droite (SysBtn → « system »)
et depuis l'écran login (engrenage). MASTER-PLAN §D1–D5.

## Ownership (strict)
- Nouveaux : `scripts/settings/*`, `scenes/settings*.tscn`,
  `gamedata/settings.cfg`, test `tools/NetTests/SettingsTests.cs`.
- Modifs minimales : `project.godot` (autoload), `HudRoot.cs`
  (hook `ApplyUiScale` UNIQUEMENT), `LoginScreen.cs` (bouton ⚙),
  `tools/NetTests/NetTests.csproj` (Compile Include).
- Intouchable : `world/`, `hud/` (hors hook), `player/`, `combat/`.

## Fichiers
- [x] `scripts/settings/ClientSettings.cs` — modèle pur (sans Godot,
      `#if GODOT` pour la résolution de chemins) : sections
      display/graphics/interface/keys/audio, Parse/Serialize,
      Load/Save (`<exe>/gamedata` → `res://gamedata` via DataPaths),
      presets LOW/MED/HIGH/ULTRA, clamp UiScale 0.5–2.0.
- [x] `scripts/settings/InputBindings.cs` — plan pur (testable) +
      `#if GODOT` Apply → InputMap (actions `move_*` existantes +
      `eden_*`).
- [x] `scripts/settings/SettingsApplier.cs` — autoload
      (scenes/settings_applier.tscn) : applique au boot, ré-applique
      au changement de scène, dispatch `eden_*` en `_Input`
      (SetInputAsHandled → pas de double-toggle avec les touches
      codées en dur du HUD), bus audio, fenêtre settings.
- [x] `scripts/settings/SettingsWindow.cs` — Window embarquée
      (draggable natif) + TabContainer 5 onglets, widgets codés,
      apply à chaud + save à chaque changement, Appliquer/Reset/
      Fermer, capture remap (clic → touche).
- [x] `scenes/settings.tscn` + `scenes/settings_applier.tscn` +
      `[autoload]` dans project.godot.
- [x] `gamedata/settings.cfg` — template défauts.
- [x] `HudRoot.ApplyUiScale(float)` (CanvasLayer.Scale).
- [x] `LoginScreen` : bouton ⚙ haut-droite → ToggleSettings.
- [x] Branchement SysBtn : SettingsApplier s'abonne à
      `MenuIconsPanel.MenuAction` (via HudReady) — « system » ouvre
      la fenêtre (hud/ non modifié).
- [x] Tests NetTests : roundtrip load/save, clamp scale, plan
      InputBindings, preset.
- [x] Captures : fenêtre ouverte en jeu + UI scale 1.5
      (args `--settings-open`, `--uiscale=`).

## Limitations connues
- Touches codées en dur dans HudRoot (I/C/Enter) restent actives si
  remapées ailleurs (hud/ hors périmètre — documenté).
- Qualité textures : stockée, application = stub (rechargement assets
  requis).
- DOF : best-effort via CameraAttributes si présentes.
