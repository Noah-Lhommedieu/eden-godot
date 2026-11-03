# Plan — contrôles gameplay EE + premières animations (25/10/2026)

Spec : `docs/GAMEPLAY-NOTES.md`. Ownership : `scripts/player/*`,
`scripts/world/EntityRegistry.cs` (visuels/anims/labels), `scripts/game/*`,
`scripts/input/*`, `scripts/combat/*`. GameScene/MapLoader/AssetCatalog/UI :
non touchés.

## Preuves collectées

- **NCID 5 C→S MeleeAttack** `[i32 sightHandle][u8 type]` —
  `ZoneMessages.g.cs CNE_CZ_ZoneServerMeleeAttack` + capture native
  `0500 <mob> 00` (commentaire `ZoneEntryFlow.Combat.Mob.cs:115`) ;
  `handle=0 ou type=2` = stop. Serveur C# : `ZoneEntryFlow.cs:482`.
- **NCID 10 C→S TalkToNPC** `[i32 sightHandle]` — `ZoneMessages.g.cs`
  + `ZoneEntryFlow.Dialogue.cs:29`. Déjà `ZoneTalkToNpc` côté client.
- **NCID 124 C→S TargetUpdate** `[i32 handle]` → 444 lock —
  `ZoneEntryFlow.cs:1022`. Déjà `ZoneTargetUpdate` côté client.
- **NCID 123 C→S TurnSelf** `[f32 dir]` → 441 — `ZoneEntryFlow.cs:737`.
- **S→C 272 ClientMeleeAttack** `[u32 atk][u32 cible][i32 dmg][i32 type]
  [i32 motionKfId]` — `ZoneEntryFlow.Combat.cs:58`.
- **Anims** : GLB méga `assets/anims/char/animation/<L>.glb` (391 anims
  nommées stand01/move01..43/attack01..27/magic/hurt/death/ride…),
  `assets/anims/monster/animation/<stem>.glb` (~45 anims), + fichiers
  unitaires `<stem>_<anim>.glb`. **Noms de nœuds identiques** entre GLB
  anim et GLB modèle (vérifié B/G/M020/M025/am202 : 206/206, 35/35,
  43/43 cibles présentes).
- **Structure modèle** : joints → bones de **1 Skeleton3D**
  (`Scene Root/Bip01/Bip01 NonAccum/Skeleton3D`) ; cibles non-joint
  (Bip01, Nub…) restent Node3D. Track glTF anim = path nom-nœud plat,
  types Position3D/Rotation3D/Scale3D → retarget : nom →
  `<cheminSkeleton>:<bone>` ou chemin nœud.
- `GltfDocument.GenerateScene` spamme des erreurs « quaternion must be
  normalized » (43k lignes, clés dégénérées du .kf) et parse 88–180 Mo
  pour char → **parseur GLB maison** (JSON+BIN) n'extrayant que les
  anims voulues.
- Marqueur : `assets/textures/effect/texture/mark-ground01.png` (anneau
  256², -p = variante). Curseurs : `assets/textures/UI/SystemCursors.png`
  = grille 8×4 de 32 px (14 cellules pleines — mapping cellule→rôle
  HYPOTHESIS).
- Session : `SessionCarry.Session` public (flux écrans) — mode `--live`
  direct crée une session privée non accessible → combat limité au
  flux normal (log WARN sinon).

## Fichiers

- `scripts/game/GlbAnimFile.cs` — parseur GLB pur (JSON chunk +
  accessors float32, channels nom→times/values) — testable NetTests.
- `scripts/game/AnimPathMap.cs` — pure : nom cible → path skeleton:bone
  ou path nœud — testable.
- `scripts/game/AnimRetarget.cs` — Godot : GlbAnimFile + modèle →
  AnimationLibrary retargetée (noms demandés seulement).
- `scripts/game/EntityAnimator.cs` — composant Node : AnimationPlayer +
  auto idle(move01)/stand01 par déplacement, PlayOnce(attack/hurt),
  cache GlbAnimFile par chemin.
- `scripts/game/CharVisual.cs` — `AttachAnimator(body, letter)` +
  PlayIdle via animator.
- `scripts/input/PickCore.cs` — pur : nearest screen-space pick —
  testable.
- `scripts/input/EdenCursors.cs` — curseurs contextuels (atlas cells
  SystemCursors.png, fallback flèche).
- `scripts/combat/CombatMessages.cs` — ZoneMeleeAttack (5) +
  ZoneClientMeleeAttack (272).
- `scripts/combat/AutoAttack.cs` — approche + boucle swing NCID5 +
  anims + stop.
- `scripts/player/MoveMarker.cs` — anneau sol texturé.
- `scripts/player/PlayerController.cs` — réécrit : L-drag orbite /
  R-drag orbite+steer / L-click move+marker+select / R-click
  NPC talk / mob auto-attack / WASD / molette.
- `EntityRegistry.cs` — attache EntityAnimator aux visuels GLB,
  move/stand auto, events hurt/attack depuis 272.

## Contrôles (spec exacte)

- LMB down→drag>6px : orbite seule. Release sans drag : pick entité
  (sélection+TargetUpdate) sinon sol → move + MoveMarker.
- RMB down→drag>6px : orbite + yaw modèle = yaw caméra (steering) +
  TurnSelf au relâchement. Release sans drag : pick entité → NPC
  (approche ≤3 u → TalkToNpc) / mob (AutoAttack). Sol : rien.
- MMB : orbite (existant). Molette : zoom. WASD : déplacement direct,
  annule clic-sol + auto-attack.

## Tests / validation

- NetTests : GlbAnimFile (fixture m486_stand01.glb / m936_hurt02.glb),
  PickCore, CombatMessages round-trip, AnimPathMap.
- `dotnet build` 0/0, `dotnet test tools/NetTests` verts.
- Live : `DISPLAY=:0 godot -- --login=admin:adminmdp --channel --char`
  → clic sol (marqueur), clic droit mob (approche+attaque, wire 5→272
  + 280 HP), drags, captures `../godot-shots/`.

## Résultat (validé live 25/10)

- Build 0 erreur ; NetTests 99/99.
- Live --input-test : clic sol→marker+move ✔, clic droit mob→TargetUpdate
  124→444 lock ✔, NCID 5 ×N→302 stance + **272 dmg réels** (162/9) ✔.
- Anims : 16 char + 12/stem retargetées, 0 erreur is_finite après
  sanitization des clés (positions/scale/quats dégénérés .kf).
- Reste : curseur cellules HYPOTHESIS, kfId→anim name, --live session
  privée (combat via SessionCarry seul), hurt/death anims.
- Fix déblocage fait dans scripts/hud (autre agent) : StretchModeEnum
  .Stretch→.Scale ×2 (enum Godot 4.7).
