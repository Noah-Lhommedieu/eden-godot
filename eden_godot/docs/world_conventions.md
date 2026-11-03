# Conventions monde — client Eden Reborn

Décisions du lot « map loader + contrôleur » (24/10/2026). Références :
`docs/FSM-FORMAT.md` (format), `scripts/world/` (implémentation).

## Axes

- **Monde Eden (.fsm / NIF / scène ini)** : Z-up right-handed —
  x, y = plan du sol (xy des vertex/OBJ/spawn ini), z = hauteur.
- **Godot** : Y-up, −Z = avant.
- **Conversion retenue** (cohérente avec nif2gltf « Z-up brut ») :

  ```
  godot = ( eden.x,  eden.z,  −eden.y )
  eden  = ( godot.x, −godot.z, godot.y )
  ```

  Vérifiée : la hauteur fsm (+z) devient +Y Godot — le terrain est
  horizontal ; les hauteurs réelles (z ∈ [−48, +128] sur S001/S009)
  tombent sur l'axe vertical.

- **Implémentation** : le contenu fsm (terrain, OBJ, eau) est placé en
  coords natives sous un Node3D `World` tourné `rotation_degrees =
  (-90, 0, 0)` — équivalent exact de la formule ci-dessus, sans toucher
  aux données. Les entités logiques (joueur, NPC placeholders) vivent
  en espace Godot direct via `WorldConv.ToGodot/ToEden`.

## Échelle / repères

- 1 unité fsm = 1 unité Godot (≈ 1 m — taille capsule joueur 1.8 u).
- Tuile terrain : 33×33 vertices, cellSize 2.0 → 64×64 u,
  tileX = floor(x/64).
- Taille monde = (gridVtx−1)×scale : S009 = 576 u, S001 = 640 u.
- **Ne pas** utiliser `Sxxx.area`×96 pour les dimensions (grille
  logique, ≠ géométrie — voir FSM-FORMAT.md §7).
- `record +0x0C` kind : 0 = terrain, 1 = eau (toutes les tuiles en
  double d'origine sont de l'eau — vérifié 53/53 S009).

## Hauteur de terrain

`TerrainSampler.TryGetHeight(fsm, x, y)` = bilinéaire sur les vertices
de la tuile kind=0 (les tuiles bord à bord partagent leurs vertices —
continuité vérifiée < 5 cm en test). Utilisée par le contrôleur
(snap sol), le clic-sol (marche + dichotomie en espace Godot) et le
placement des entités. L'eau n'est PAS prise en compte (kind=1 ignoré).

## Assets runtime (pas d'import Godot)

`assets/.gdignore` actif → tout passe par `AssetCatalog` en mode
fichier : textures `Image.LoadFromFile` (.dds→.png sous
`assets/textures/<rel>`), GLB `GltfDocument` (cache + Duplicate).
Résolution insensible à la casse (S009.fsm vs s009.ini).

## Décisions / HYPOTHESIS documentées

- Vertex attr #3 → `COLOR_0` (**confirmé** — éclairage baké :
  scan exhaustif des .fsm montre des valeurs 0.0-1.0 non blanches sur
  40+ maps, motif ombres portées B≥G≥R ; sur les tuiles eau = teinte
  cyan + alpha 0.3-1.0 fade de rive. Appliqué : `terrain_splat`
  ×COLOR.rgb, `water` ×COLOR.rgba). Objets : `COLOR_0` glTF appliqué
  aussi — les seules formes non-blanches des NIFs objet ont
  SourceVertexMode=AMB_DIF ou pas de propriété (= utilisé) ; 0 shape
  non-blanc en SRC_IGNORE.
- Splatmap : canaux RGBA → slots 1..4, base slot0 = 1−Σ (HYPOTHESIS).
- UV terrain : posMonde/64 × uvScale du slot (convention client).
- Matrice OBJ 3×3 prise row-major comme axes du Basis — transpose à
  vérifier à l'écran (symptôme : objets en miroir/rotation inversée).
- Variantes modèle `name_1.._5.glb` : on charge `name.glb`, fallback
  `name_1.glb` (règle native inconnue).
- Spawn joueur debug : 1re aire REVIVE → GATEWAY → [position] born_area
  → centre map (born_area est vide sur S009 → revive 勇蹄村,
  (479.1, 248.5)). **Attention** : sur S001 le point REVIVE est sous le
  niveau de l'eau/port — privilégier un pivot haut pour les captures.
- Eau rendue en StandardMaterial translucide (pas de shader animé —
  waterani_normal.ifl ignoré).

## Environnement (25/10)

- **S001 = Aven** (聖徽城, « ivoire et pierre bleue » — pavés brick_*),
  **S009 = volcan** (阿維拉火山). Correspondance node↔map :
  `data/db/c_node.ini` (qui duplique aussi les paramètres env par
  node — incl. des codes sky parfois différents de l'ini, ex. S001
  jour : A201 dans la table vs N201 dans l'ini ; on garde l'ini).
- Couleurs ini = entiers **0xRRGGBB** (preuve : LitColorNight S009
  0x17285B = bleu nuit — BGR donnerait du marron).
- `LitColor*` → ambient Color (énergie `MapLoader.AmbientEnergy`,
  ~0,55 — sinon la scène est lavée) ; `LitDifColor*` → soleil
  (`SunEnergy` ~1,4 — les valeurs ini sont sombres, la lumière douce
  vient de l'ambiant) ; `FogColor*` → fog ; `FogSightRang*`/`FogMax*`
  → `FogDepthEnd = SightRang/max(FogMax,0,05)` (rampe exacte jusqu'à
  SightRang ; le fog Godot ne peut pas plafonner à FogMax comme le
  natif — approximation documentée).
- Ciel : le code `Sky*` (N201, C203…) → `map/model/sky/<code>.glb`
  (dôme panoramique ~2,2 km, coords natives, suit la caméra) via
  `SkyDome` + `shaders/sky_dome.gdshader` (unshaded, **fog_disabled**,
  cull_disabled). Le nœud « sun » du glTF donne la direction du
  soleil pour le DirectionalLight.
- Objets : `ToonMaterializer` (appliqué dans
  `AssetCatalog.LoadGlbNode`) masque les meshes helper — murs de
  collision « true wall », « blo », « floor1 », « Box01 », variantes
  « -s/-n » shadow LOD (toutes surfaces sans texture + nom helper ou
  « Editable * », uniquement si le modèle a d'autres parties texturées)
  — et convertit l'opaque en toon (`shaders/toon_lit.gdshader`,
  3 paliers + rim), l'alpha reste StandardMaterial3D déspecularisé.
  Modèles 100 % non-texturés (UD002…) : conservés tels quels
  (déco couleur pleine probable).
- Ombres : DirectionalLight shadow 4-splits 160 m ; `BlobShadow` =
  Decal radial sombre sous les entités (appliqué aux placeholders
  NPC — réutilisable sur le joueur).
- Minimap : `assets/textures/UI/map/<S###>s.png` (1024²) et
  `<S###>m.png` (512²) existent — lot HUD, non consommé ici.
- Phase env : `--phase=Day|Sunrise|Sunset|Night` (parsé dans
  `MapLoader`, propagé via meta `env_phase` — FogCeiling suit) ;
  défaut Day. Nuit vérifiée : SkyNight + LitColorNight + émissif.
- Météo (`WeatherEffects`) : [Weather]/[WeatherParticle] ini —
  `WeatherSwitch=0` sur les 5 seules maps concernées (S143, S406,
  s1102, s504, s863 — toutes des maps modèle unique) → dormante en
  données. `--weather` force pour la vérif ; textures Rain.dds/Fog.dds
  absentes des assets → replis documentés.
- Émissif (B5) : aucune Glow Texture NIF (0/1159 objets map) ;
  `emissiveFactor` (NiMaterialProperty Emissive Color) déjà exporté
  par le converter — recopié dans le shader toon ; les darkmaps (×96)
  ne sont pas exportées (assombrissement nuit — lot futur).

## TODO lots suivants

- `// TODO: EdenSession` — GameScene pas encore branchée réseau ;
  NetworkInterpolator prêt (PushState/_Process) pour les RemoteEntity.
- Cycle jour/nuit complet automatique (Hour* ini + interpolation
  entre phases) — `--phase=` couvre la sélection manuelle.
- Grass : instancer les GRS via leurs GLB UE7xxM (chunk GRL/GRS parsé
  mais non rendu).
- Physics : HeightMapShape ou colliders OBJ quand le gameplay le
  demandera (MVP = snap hauteur).
- UI : scripts/ui/ (autre agent) consommera AssetCatalog pour les
  imagesets CEGUI.
