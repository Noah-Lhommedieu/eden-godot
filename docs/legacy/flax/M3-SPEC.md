# M3 Spec — MapLoader Flax S002 (condensé des 2 audits 2026-11-01)

## Fichiers (via EdenVfs root `client_assets`, ResolveInsensitive)

| Ordre | Fichier | Parseur Eden.Core |
|---|---|---|
| 1 | `data/scene/s002.ini` | `SceneTable.Load` → `FsmFile`="S002.fsm", EventAreas (REVIVE/GATEWAY/BORN) |
| 2 | `map/model/s002/s002.fsm` | `FsmFile.Load` (queue V7 : `TrailingBytes`) |
| 3 | `map/sknodemap/s002.smp` | `NodeMapFile.Load` — PAS de `fix/` (US interdit) |
| 4 | `map/model/s002/s002.ini` | `MapEnvIni.Load` → `GetPhase(Day)` |
| 5 | `map/texture/*.dds` | bytes → Texture (DdsInfo à écrire) |

## FSM (specs/FSM-FORMAT.md)

- Header 0x58 : `"FSM "` + version[9] + rev@0x54. Chunks `{tag,size}`.
- LAY0 header 0x18 : gridVtxX/Y (**vertices**, pas tuiles), cellScale=2, recordCount.
- Record tuile 0x1abf4 : +0x00 gridW=33,gridH=33,cellSize,kind (0=terrain,1=eau) ; +0x10 verts 33²×0x28 `{pos xyz absolue, nrm xyz, rgba f32}` ; +0xaa58 splatmap RGBA 128² ; +0x1aa78 TexSlot[5]×0x4c `{used u8, name[67], u32@44, uvScale f32@48}`.
- Ordre records : kind=0 x-lent/y-rapide puis kind=1 (eau, même origine). TileWorld=64 u. `GetTerrainTile(ix,iy)`, `WeldSplatEdges()` (bords splat moyennés).
- Positions vertex = monde absolu ; mesh = pos locales + acteur à (OriginX,OriginY,0).

## Terrain mesh (ref TerrainBuilder.cs)

- 1 mesh/tuile, indices CCW vu de +z : `v, v+w, v+1 / v+1, v+w, v+w+1`.
- UV = posMonde/64 (fract dans shader → uv local ; tile_uv = pos/64 → splat).
- Normales reprises si |n|∈]0.9,1.1[ sinon recalcul produits croisés.
- Vertex color = éclairage baké RGBA (×albedo ; eau : teinte+alpha rive).

## Splat shader (ref terrain_splat.gdshader)

- 5 couches : layer0 base (w0=1−Σ), R/G/B/A splat→layers1-4 (HYPOTHESIS canal→slot).
- splatmap samplée LINÉAIRE (pas sRGB). Renorm `tot>1→1/tot`.
- `uv_scaleN = scale0²/scaleN` (scale≤0→1) — piège corrigé, FUN_00b02100.
- Texture `map/texture/{slot.Name}` ; slot sans texture → repli slot0.

## Caméra

- Spawn : REVIVE→GATEWAY→Positions[BornIdx−1]→Positions[0]→centre fsm ; Z scène non fiable → `max(z, TerrainHeight)`.
- Rig : pivot +1.6 u, yaw/pitch clamp (−0.6,1.2) pitch init 0.45 rad, zoom molette 3–18, sens 0.008, FOV 75, clamp sol +0.3.

## Env (MapEnvIni — Big5, sections dup, couleurs décimales 0xRRGGBB)

`[Scene]` suffixed Sunrise|Day|Sunset|Night (Sky/Bloom : `SunSet` aussi) :
- LitColor* → ambiant (énergie 0.55) ; LitDifColor* → soleil (1.4, repli LitColor)
- FogColor* → fog+ciel ; FogMax* (~8-36 %) → `fogEnd = FogSightRang*/max(FogMax,0.05)` ; FogSightRang* (sans 'e', défaut 500)
- Soleil : repli dir (0.35,0.8,−0.45). Sans ini : fog 60-600, ambiant (0.45,0.47,0.55)≥0.4.

## Collisions

- `TerrainSampler.TryGetHeight(fsm,x,y,out z)` bilinéaire kind=0 only.
- `NodeMapFile.IsBlocked(x,y)` bit0 bloque, bit1 non, hors carte libre ; `Dilated()` 8-voisins.

## Axes — critique

Eden = Z-up right-handed ; Flax = Y-up **left-handed**. Choix : racine Actor −90°X sous laquelle tout vit en coords Eden natives (miroir Godot) + flip LH à la frontière. `WorldConv.cs` a la note LH — trancher avant le mesh.

## Flax 1.13 API vérifiée

- `Content.CreateVirtualAsset<Model>()` → `model.SetupLODs(new[]{1})` → `model.LODs[0].Meshes[0].UpdateMesh(verts,tris,normals,tangents,uv,colors)` → `new StaticModel{Model=m}` → `Level.SpawnActor`. UV1 via `MeshAccessor` ou `MeshBase.Init` — sinon UV0 seul suffit (uv_scale dans shader).
- DDS : `Texture.FromMemory(bytes, ImageFormat.DDS)` à tester ; sinon `tex.Init(ref InitData)` avec mips BC bruts (`InitData{Format,Width,Height,ArraySize,Mips[]}`, `MipData{Data,RowPitch,SlicePitch}`). Décodeur DDS game-Linux douteux → `DdsInfo` header Eden.Core + upload BC brut.
- Matériau : pas de compile HLSL en C#. Options : asset `.flax` Material (Content.LoadAsync) OU `MaterialBase.CreateVirtualInstance()` + `SetParameterValue(name, obj)` sur un material moteur. Splat → besoin shader custom `.flax` (Content/ d'eden_flax) ou surface+params.
- `Camera` + `Camera.OverrideMainCamera=cam` ; `Input.GetKey/GetMouseButton/MouseDelta/MouseScrollDelta` ; `Script` avec OnUpdate ; `Time.DeltaTime`.
- Scène requise pour SpawnActor : `Level.LoadSceneFromBytes(jsonBytes)` ou Scene.flax + GameSettings.FirstScene ; `new Scene()` non confirmé.
- `GamePlugin.Initialize()` = entry ; `Debug.Log/LogWarning/LogError` ; flags game `-windowed -std -mute`, éditeur `-project -play`.

## Hors scope M3

props OBJ/GRS (queue V7 → M4), eau animée .ifl (rendre tuiles kind=1 translucides suffit), herbe, FogCeiling, météo, dôme NIF (ciel procédural teinté FogColor).
