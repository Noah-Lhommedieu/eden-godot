# Plan H1/H6/H7 — fond temple, curseur contextuel, icônes skillbar

## Preuves collectées

- **S218_room10b.glb extrait** : 11 480 nœuds, 0 multi-parent (json). Sonde
  GlbProbe étendue (--glb=) : charge OK, 6264 surfaces / 6152
  texturées, AABB natif (54..545, 10..622, -77..242) —
  Select_pos (318,613,5) de startsetting.ini est dedans.
- **startsetting.ini** (`data/db/startsetting.ini`) :
  [Select] Select_camera=(327.5,670,7) Select_pos=(318,613,5) ;
  [Create] Human_pos=(317.69,613.5,41.93) camera=(317.64,613,42.9)
  rotate=(317,613,180) + Kuma/Mouse/Frog.
- **Curseurs** : `client_game FUN_007b8950` itère la DDS
  SystemCursors en cells row-major (index = cellule, >>5). Indices
  natifs observés : 0/1 flèche, 2 (type 0x43), 3 = cible attaquable
  (FUN_0078bf30 case 7/0x13), 5 main (flag non-attaque), 6-10
  variantes combat (case 0x33), 11/12 drop, 13/14/15 drag
  valide/invalide, 18 = caché (cellule vide). Cellule 9 = bulle
  (forme vérifiée), 6 = épée rouge.
- **H7 cause** : `SkillBarWindow` icône `OffsetBottom=-8` sous ancre
  TopWide → hauteur ≤ 0 → icône jamais rendue. C_Spell : 4612
  icônes chargées OK, fichiers `UI/skillicon/J001.png` présents.
- Reforged `FunctionFastBar` (main.imageset x4,y466,381×45) =
  transparent → les slots natifs n'ont pas de texture : les
  « anneaux » = cells SkillBarWindow (fond sombre+bord) sans icônes.

## Actions

1. `scripts/screens/TempleBackdrop.cs` (nouveau) : scène S218 +
   caméra fixe (Select/Create shots), env+lights, CharMarker
   persistant partagé entre écrans (Attach(screen)).
2. `ScreenManager._Ready` : préchauffe le proto GLB en tâche de fond.
3. Login/Channel/CharSelect/CharCreate : remplacent gradient/
   SelectStage remplacé par TempleBackdrop ; CharCreate affiche le perso sur
   le marqueur (shot Create, race map → section ini).
4. `scripts/input/EdenCursors.cs` : cells réelles (Attack=3,
   Talk=9, Move=2, Interact=5, Busy=11), cache par mode (plus
   d'AtlasTexture par frame), resize→formes Godot.
5. `LoadingScreen` : curseur Busy pendant le chargement.
6. `SkillBarWindow` : hauteur d'icône corrigée (OffsetBottom=SlotSize).
7. `HotbarPanel` : slots FastBar1 = vrais sorts (PlayerSkills +
   SpellCatalog), refresh sur Changed, plus de DemoIcons.
8. `GlbProbe` : options --glb= + AABB monde (diag, gardé).

## Validation

- `dotnet build` **0/0**, `dotnet test tools/NetTests` **276/276**.
- Captures fenêtrées : `final2-login/channel/chars/create.png` avec
  `temple=True` (chargement `S218_room10b` réellement terminé).
- `hud-inventory-final2.png` + logs : drag barre de titre ET fond,
  positions persistées.
- `world-final3-s002.png` : terrain/sky/alpha corrigés ; plus de voile
  blanc ni poutre bleue `UD501`.
- `entities-final-s002.png` + `--pick-test` : 3/3 entités raycastées
  via `PickArea`.
- Live `--cursor-test` : NPC `kind=8` → `Talk` ;
  `--cursor-test=mob` : mob `kind=12` → `Attack` ;
  `--cursor-test=ui` : `ItemMallBtn` → `Arrow`.
- `--hud-open` différé + `--hud-dump` : toutes les fenêtres principales
  et extensions trouvées ; `HOME` isolé → positions par défaut dans le
  viewport (les x négatifs relevés avant venaient de `ui_layout.cfg`).

Reste non prouvé : curseurs Busy/Forbid/Drag en contexte live, icônes
avec un vrai compte skillé, vidéo locomotion/attaque/clic NPC et audit
de toutes les fenêtres.
