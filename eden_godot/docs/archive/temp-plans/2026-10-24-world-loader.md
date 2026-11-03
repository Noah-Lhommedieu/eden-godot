# Plan — chargeur de map + contrôleur 3D (lot monde)

## Découvertes hex au-delà de FSM-FORMAT.md (à reporter dans le doc)

- `u32 @0x54` du header = révision du format : **2 = V20100329, 3 = V20100824**
  (vérifié S001/S002/S005/S010 = 2, S009 = 3).
- Record LAY0 `+0x0C` = **kind de tuile** : 0 = terrain, 1 = eau.
  Les records kind=0 (81 = 9² pour S009, 100 = 10² pour S001) viennent en
  premier, ordre **x-lent / y-rapide** (record = ix*tilesPerRow + iy) ; les
  kind=1 suivent, même grille d'origines — les doublons (origine,x,y)
  sont TOUS des tuiles d'eau (TexSlots `water_env.dds`,
  `waterani_normal.ifl`, splat entièrement nulle, hauteurs aplaties).
- `tilesX/tilesY` du header LAY0 = **dimension de la grille de vertices**
  (= tilesPerRow*(gridW-1)+1 : 289=9*32+1, 321=10*32+1), pas un nb de tuiles.
- Positions des vertices = **coordonnées monde absolues** (v0 = origine
  de la tuile, ex. rec1 v0=(0,64,z)).
- Attr #2 vertex = **normale unitaire** (|n|≈1, +z dominant) — confirmé.
- Attr #3 = **couleur RGBA** (1,1,1,1 partout sur S009/S001) — HYPOTHESIS
  (pourrait être un tangent inutilisé, valeur constante).
- GRL record 44 o = `u32 typeId` + `char modelName[40]` NUL («UE704M.nif»,
  «UE704MN.nif», «UE735M.nif»…) — PAS «8o handle + 32o params».
- GRS record 32 o = `u32 typeId ; f32 x,y,z ; f32 scale ; u32 0 ;
  f32 a ; f32 b` (a,b plausibles rotation/direction — sémantique non prouvée).
- Splatmap : canaux = poids des **couches 1-4** ; base (slot0) implicite =
  `1 - Σ` (sommes observées < 255, pixels à 0 → base pure) — HYPOTHESIS.
- `Sxxx.area` « w h » ≠ taille rendue : S009 « 9 9 » → 9×96 = 864 ≠ fsm
  578 ; le .area est une grille logique (bornage), les dimensions réelles
  viennent du fsm `(tilesX-1)*scale` = 576 u. bbox OBJ : 565×567 max.

## Fichiers

- `scripts/world/FsmFile.cs` — parseur pur (byte[] + vues lazy)
- `scripts/world/TerrainSampler.cs` — TryGetHeight bilinéaire (pur)
- `scripts/world/WorldConv.cs` — (ex,ey,ez) → Godot (x,ez,-ey)
- `scripts/world/TerrainBuilder.cs` + `shaders/terrain_splat.gdshader`
- `scripts/world/MapLoader.cs` — map root + terrain + OBJ + env + NPC
- `scripts/AssetCatalog.cs` — textures/GLB runtime hors import
- `scripts/player/{PlayerController,NetworkInterpolator,InterpBuffer}.cs`
- `scenes/game.tscn` + `scripts/GameScene.cs` + input actions runtime
- `tools/NetTests/FsmTests.cs` (+ InterpTests), csproj glob world/player
- `docs/world_conventions.md`

## Convention retenue

Racine `World` sous `rotation_degrees=(-90,0,0)` : tout le contenu fsm
(terrain, OBJ, herbe) placé en coords natives Z-up. Entités jouables et
NPC en espace Godot via `WorldConv.ToGodot(ex,ey,ez)=(ex,ez,-ey)`.
Cohérent avec nif2gltf (« Z-up brut, Godot applique la rotation »).
