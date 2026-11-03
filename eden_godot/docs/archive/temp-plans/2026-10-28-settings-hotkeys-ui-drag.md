# Plan — Settings complet + hotkeys souris + drag par entête

## État constaté (déjà en place)
- `settings/` (autoload `SettingsApplier`, `ClientSettings` INI,
  `SettingsWindow` 5 onglets, `InputBindings`) — thème/police/scale/
  audio/graphismes appliquent à chaud en un clic.
- Fenêtres déjà déplaçables par barre de titre (`DragHelper`).
- Capture remap accepte les boutons souris, scroll exclu.

## À faire
- [x] Menu Échap : retirer « Move UI » (6 boutons = ref native).
- [x] `HudLayout` : supprimer le mode (Ctrl+L, poignées overlay) ;
      chaque HUD fixe reçoit une **entête grippe** (bande haute ~8 px,
      toujours active) — glisser déplace, relâcher persiste
      (`Huds`/`clientSettings.json` inchangé).
- [x] Hotkeys souris : convertir les touches codées en dur en actions
      `eden_*` → bindables boutons souris (XButton1/2…) :
      - `HudRoot._UnhandledInput` switch I/C/K/J/N/M/L/Enter → actions
        `eden_pet` (N), `eden_achievements` (L), `eden_social` (O) +
        dispatch SettingsApplier ; le reste existe déjà.
      - `SkillBarWindow` slots 1-9,0,-,= → `eden_slot_1..12`
        (Ctrl = rangée 2, conservé via `e.CtrlPressed`).
      - `SocialWindow` : `TogglePanel()` public, retirer `_UnhandledKeyInput` O.
- [x] `InputBindings` : alias Mouse4/5 → XButton1/2 ; retirer Wheel*
      (« pas les scroll » — capture les ignore déjà).
- [x] `SettingsWindow` : option « Default » police (FallbackFont=null) ;
      hint capture « key or mouse button ».
- [x] Tests NetTests : WheelUp invalide ; defaults nouvelles actions.
- [x] Build 0 err + NetTests verts (420).

## Hors scope (signalé)
- Language = placeholder (aucune loc réelle).
- TextureQuality = stub (documenté).
- Audio : buses créées, pas de sons en jeu de toute façon.
- Fenêtre Settings = `Window` Godot (drag natif), pas refaite.
