# Plan — HUD propre + textes EN + icônes (25/10/2026)

Mission : icônes d'items correctes, anglais partout, HUD propre,
char list lisible, police FONT01.

## Diagnostic établi (preuves)

- **Icônes** : les PNG `assets/textures/UI/itemicon/*.png` sont SANS
  (8887 fichiers, contenu réel vérifié par PIL). Le code
  `itemId → c_item.ini col1 (Code) → itemicon/<Code>.png` est correct
  et les icônes se chargent déjà (capture live : cellules remplies de
  contenu réel, pas de boîtes unies).
- **Vrai bug visuel** : `CeguiAnchors.FromUnified` traitait la 3e/4e
  paire de l'UnifiedAreaRect comme **largeur/hauteur** ; CEGUI les
  définit comme **bords droit/bas** (preuve : layouts avec
  `{{1,-72},…,{1,-38}}` = éléments dockés à droite de 34 px ;
  doc CEGUI wiki + `CoordConverter::getBaseXValue` v0-8). Résultat :
  fenêtres démesurées (inventaire rendu 884 px de large au lieu de 511,
  étalé hors écran en bloc blanc). 11229/12182 rects concernés.
- **Textes** : les T_*.ini du Reforged sont déjà EN ASCII
  (t_monster 56001→« Red Cap Mushroid », t_npc 50002→« Village
  Head\nAndrew », t_item, t_mission, t_node, T_Class). Le serveur C#
  envoie les noms Big5 dans SeeMob → prioriser les tables EN.
- **Debug** : `DebugUI/DebugLabel` (game.tscn) affiche fps/pos/aide —
  à masquer derrière `--debug` / F12.
- **Char list** : `CharSelectScreen.FillSlots` affiche « node N race N »
  → remplacer par classe EN (T_Class) + nom de zone (t_node).
- **Font** : `ui_textures/FONT01.ttf` = Sarasa Gothic TC, 46k glyphes
  (latin+Big5) → thème par défaut `gui/theme/custom`.

## Actions

- [x] `scripts/ui/CeguiLayout.cs` : `FromUnified` — edges + décalage
   d'alignement appliqué aux deux bords (sémantique exacte
   `CoordConverter` : `base += (parent - size)/2 | (parent - size)`).
   Maj `tools/NetTests/CeguiTests.cs`.
- [x] `scripts/data/TextResolver.cs` (nouveau) : caches paresseux
   t_npc/t_monster/t_item/t_mission/t_node/T_Class/T_Spell +
   t_itemmall ; strip `$N$` ; NPC « Titre\nNom » ; résolution de
   fichier injectable (`ResolveFile`, défaut raw_assets/ — NetTests
   compatible, pas de dépendance Godot).
- [x] `GameScene.cs` : noms mobs/NPC EN via delegates + hook `Spawned`
   (`RenameEntityEnglish`) ; flag `--debug` + F12 pour DebugUI.
- [x] `MinimapPanel.cs` : nom de zone EN via TextResolver (t_node).
- [x] `InventoryWindow.cs` : noms/descriptions via TextResolver.
- [x] `QuestTracker.cs` / `ProfileWindow.cs` : via TextResolver
   (cache partagé).
- [x] `CharSelectScreen.cs` : « Warrior LvN » + « Limestone Mtn. »
   (édition minimale dans screens/ — requis par la mission).
- [x] `ui_textures/eden_default_theme.tres` + `project.godot`
   `gui/theme/custom` : FONT01 en police UI par défaut.
- [x] `dotnet build` 0/0, NetTests 118/118 verts.
- [x] Captures live : `godot-shots/hud_inventory_icons_en.png`
   (fenêtre 511×351 + icônes réelles), `hud_ingame_labels_en.png`
   (labels latins, pas de texte debug), `charselect_names_en.png`.

