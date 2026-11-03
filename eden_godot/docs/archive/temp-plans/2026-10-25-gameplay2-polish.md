# Plan — lot gameplay-2 (agent B) : 272 hurt/death, apparence remote, herbe, eau .ifl, fog plafonné

Symptômes : pas d'anims hurt/death, joueurs distants = capsules,
pas d'herbe (chunks GRL/GRS ignorés), eau statique, fog Godot sans
plafond natif.

## Preuves collectées

- **motion.ini** (`raw_assets/data/db/motion.ini`, 82 075 lignes) :
  `[KEY_INFO] DBn=<model>,<kfId>,<frametime>,<anim>` et `[KEY_Table]
  DBn=<model>,<kfId>,<sec>,<phase>,<link>,<son>,<anim>` — le mapping
  (model,kfId)→anim existe : `m025,1021→attack01`, `g,1421→attack12`.
  → résolution exacte du field5 du 272, pas une HYPOTHESIS.
- **272** : serveur `ZoneEntryFlow.Combat.cs` — type 1/3 hit, **type 5
  = coup fatal** (kfId=-1), twin = type 1 + kfId=-1 (echo dégâts, pas
  d'anim d'attaque), type 8 = miss. Attaquant joueur = charId (sibling
  « Player », pas dans le registre).
- **Mort mob** : 280 attr0→0, puis 272 fatal, puis **305 ClientDie
  `[u32 victime][u32 tueur][u8]`**, cadavre retiré ~5 s après par 269
  (`ZoneEntryFlow.Combat.Death.cs` — le délai natif couvre l'anim).
  269 sert AUSSI au despawn hors-vue → death01 uniquement si l'entité
  est marquée mourante.
- **265 ne porte PAS l'apparence** (codec serveur ClientSeeCharCodec :
  96 o, seulement `modelRev`). Le blob 0x20 n'existe que dans
  **263 ClientDisplayChar** (réponse WhoIs NCID 2 `[i32 charId][u8=1]`,
  `ZoneEntryFlow.DisplayChar.cs` — head fixe jusqu'à l'apparence :
  u32,str,4×f32,2×u8,4B,u16,u8,u16,app32).
- **GRL/GRS** vérifiés : S009 3292 inst (UE704M/UE704MN), S002 21 597,
  S025 31 313 → cap 5000 + cellules 64 u (TileWorldSize) pour le
  frustum culling (MultiMesh par type×cellule + CustomAabb).
  GLBs convertis présents : `models/map/model/object/UE*.glb` (1 mesh,
  alphaMode MASK 0.49, texture map/texture/UExxxx.png). ParamA/B du
  GRS non prouvés → yaw pseudo-aléatoire déterministe (hash index).
- **Eau** : tuiles kind=1 slot1 = `waterani_normal.ifl` (frames
  caust00..29.dds, 64×64 uniformes → `map/texture/caustNN.png`),
  slot0 = base (`water_env.dds`/`magma_01.dds`). .ifl = liste texte
  CRLF, ligne `name.ext [n répétitions]` (5 fichiers avec durée).
- **Fog** : FogMax natif 0.08-0.36 = opacité MAX atteinte à
  FogSightRang. Godot linéaire monte à 100 % → post-process
  CanvasLayer lisant depth texture avec clamp à FogMax (cap dur).

## Points d'édition

- `scripts/combat/CombatMessages.cs` : + `ZoneClientDie` (305),
  `ZoneWhoIs` (C→S 2), `ZoneClientDisplayChar` (263, tête seule).
- `scripts/combat/MotionTable.cs` (NEW, pur) : parse motion.ini →
  (model,kfId)→anim ; `Shared` via résolveur injecté.
- `scripts/combat/CombatFx.cs` (NEW, pur) : hitType→(attaque,blessure)
  + règles fatal/miss/twin.
- `scripts/world/EntityRegistry.cs` : lazy-bind session
  (SessionCarry, découverte tardive comme PlayerController) → 272/305/
  263 via file thread-safe ; Remove honore `Dying` (délai grâce) ;
  SeeChar remote → WhoIs une fois par modelRev → 263 → CharVisual.
- `RemoteEntity` : `Die()`, `Dying`, placeholder humanoïde stylisé
  pour les joueurs + label proéminent.
- `scripts/world/GrassLayout.cs` (NEW, pur) : groupage type×cellule +
  échantillonnage cap.
- `scripts/world/GrassRenderer.cs` (NEW, Godot) : MultiMesh par
  type×cellule, mesh GLB (skip helpers invisibles) ou quads croisés.
- `scripts/world/IflFile.cs` (NEW, pur) : parse .ifl (nom [répét]).
- `scripts/world/WaterAnimator.cs` (NEW) : BuildMaterial → shader
  `water.gdshader` (base + caustiques en sampler2DArray cyclé par
  TIME ; fallback scroll UV).
- `shaders/water.gdshader`, `shaders/fog_ceiling.gdshader` (NEW).
- `scripts/world/FogCeiling.cs` (NEW) : CanvasLayer plein écran,
  clamp fog_max, retrouve Map_* ancêtre + WorldEnvironment « Environment »
  (désactive le fog builtin), inv_proj caméra par frame.
- `scripts/world/TerrainBuilder.cs` (eau uniquement) :
  BuildWaterMaterial → WaterAnimator ; hook fin de Build →
  GrassRenderer + FogCeiling (seul point d'insertion autorisé).
- `tools/NetTests/` : csproj += fichiers purs ; tests IflFile,
  MotionTable, CombatFx, GrassLayout, messages 305/2/263.

## HYPOTHESIS documentées
- kfId→anim résolu via motion.ini (preuve format serveur) ; hurt
  générique « hurt01 » (pas de hurtId dans le 272 — natif picore dans
  le pool hurt du KFM cible : `hurt01` seul).
- WhoIs NCID 2 utilisé pour l'apparence remote (le canal d'apparence
  prouvé du serveur ; le déclencheur natif exact = modelRev → requête
  non identifiée).
- GRS ParamA/B ignorés (non prouvés) ; rotation hashée déterministe.
- Water shader : mix base+caustique à 55 %, dérive UV lente (le
  blending exact natif n'est pas désassemblé).
- FogCeiling remplace le fog builtin par un post-process depth :
  rampe linéaire (déjà la sémantique Godot), cap = FogMax.
- `--live` direct (session privée GameScene) : FX combat absents,
  même limite que l'envoi AutoAttack (SessionCarry seul connu).

## Validation
dotnet build 0/0, dotnet test verts + nouveaux tests, live
--login=admin:adminmdp --channel --char → combat mob, captures
godot-shots/.
