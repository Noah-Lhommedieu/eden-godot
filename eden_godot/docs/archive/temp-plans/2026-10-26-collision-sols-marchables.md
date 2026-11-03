# 2026-10-26 — Collisions : sols marchables (pont S002)

Plan-racine : `eden_server/docs/temp-plans/2026-10-26-phase5-client-gameplay.md` §2.
Problème : helpers `floor*`/`UB*`/primitives/`Editable*` → blockers purs ;
pont S002 (UB107/UB108, x≈263-327 y≈283-330) infranchissable. Fait clé de
l'audit GLB : le helper du pont UB107 s'appelle **`true wall`** mais son
AABB est une dalle plate (~7×5,7×1,1) → classification nom + géométrie.

## Décisions

- [x] `ToonMaterializer` : split regex `WallHelperName` (`true ?wall`,
      `blo(c)?`, `knock`, `nocamera`, `camera`) vs `FloorHelperName`
      (`box*`, `floor*`, `plane*`, `object*`, `cylinder*`, `light*`,
      `U[A-Z]\d+(_\d+)?`, `.*-[sn]`) + `Editable*` → floor.
      `ClassifyHelper(name)` : wall si wall-regex, sinon floor.
- [x] Promotion géométrique (CollisionBuilder) : helper classé wall dont
      l'AABB locale est plate (Y ≤ 0,6×max(X,Z)) → **floor** + warn
      `class=wall?` (pont UB107 `true wall` = dalle → floor). Floor
      classé à AABB verticale → warn `class=wall?` (reste floor).
- [x] Layers : walls `CollisionLayer=1`, floors `CollisionLayer=2`,
      `CollisionMask=0` (corps statiques ne sondent rien). Joueur
      mask=1 → `MoveAndCollide` ignore les floors (capsule 1,7 u sous
      un tablier à +8 u ne bloquait de toute façon pas).
- [x] `WalkableGround.cs` (nouveau) : constantes couches + requête
      `Sample(ctx,x,z,…)` = `max(terrain, raycastDown(mask=2))`,
      origine `refY+HeadClearance(2,3)` → `refY−MaxDrop(24)`, état
      « sticky » (_pos/_y) : continu (≤3 u) → ref = max(terrain,
      lastY) (suivi du tablier) ; discontinu (téléport/spawn) → bande
      basse terrain+2,3 (pas de pop sur les toits). `SampleAt` =
      variante y-aware (seam documentée).
- [x] Seam joueur SANS toucher PlayerController.cs/GameScene.cs :
      `MapLoader.TryGetGodotHeight` (déjà branché par
      `GameScene.HookPlayerGround:1415` sur `_player.GroundHeight`)
      ajoute le raycast **uniquement en `Engine.IsInPhysicsFrame()`**
      + `WorldRoot` dans l'arbre (le loader n'est jamais parenté —
      `GetWorld3D` via `WorldRoot`). Appelants idle (snap entités
      distantes `EntityRegistry.GroundPos`, clic-sol `TryPickGround`,
      spawn) → heightfield pur, 0 raycast (perf + stabilité sticky).
- [x] `MapLoader.TryGetWalkableHeight(pos,out y)` publique y-aware
      (seam si un futur délégué transporte y).
- [x] Fallback AABB : floor >10 k tris → trimesh forcée + warn (jamais
      de boîte géante) ; wall >10 k → box conservée + warn.
      `BuildShape` partagé avec PropBatcher.
- [x] PropBatcher : `ColliderSpec` + Cls/Name ; corps split wall(1)/
      floor(2) par instance ; shape cache par (mesh,cls) ; même policy.
- [x] Diag : `EDEN_COLLISION_DIAG=1` (dump tous helpers classés) et
      `--collision-dump=ex,ey[,r]` (r=30 défaut) — nom/classe/tris/
      shape par objet dans le rayon, appelé par CollisionBuilder.Apply
      ET PropBatcher.Flush.
- [x] Build 0/0, NetTests vert (TerrainSampler reste pur).

## Risques / suspects à vérifier

- Helpers `.*-[sn]` verticaux (LOD ombre de palissades) → floor +
  warn `class=wall?` : un sommet de palissade à +1 u devient « sol »
  possible au contact (sticky ne le snap que s'il est ≤2,3 u au-dessus
  du sol courant) — surveiller le dump.
- `true wall` plats >2,3 u au-dessus du sol : inaccessibles (le rayon
  part de la tête) — normal.
- Tablier à <2,3 u du sol : impossible de passer dessous (snap dessus)
  — acceptable (capsule 1,7 u ne passerait pas).
