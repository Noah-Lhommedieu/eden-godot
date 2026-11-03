# Phase 5 — Ressenti gameplay client Godot

Plan-racine : `eden_godot/docs/plan-refonte_architecture-complet.md` (Phase 5).
Audit : rapport subagent 2026-10-26 (fichiers:lignes ci-dessous).

## 1. Saut + double saut

- `eden_jump` = binding inerte (`SettingsApplier.cs:495-499` no-op).
- `jump01` déjà chargée (`AnimRetarget.cs:27-33`) → `Animator.PlayOnce("jump01")` dispo.
- `PlayerController._PhysicsProcess` (`PlayerController.cs:510-636`) : snap `pos.Y=GroundHeight` permanent, pas de vy/air-state.
- Oracle : Reforged `jump_feature` (mempatch zone, SetFloat/NCID 581, off par défaut) ; natif de base non prouvé.
- [x] Saut LOCAL : `vy`+gravité, pas de snap Y airborne, atterrissage à
      `y≤GroundHeight`, 2 impulsions max/vol (miroir `jump_feature_max=1→2`),
      `jump01` en PlayOnce, retour `move01`/`stand01`. Client-seul, aucun
      paquet spéculatif (sync NCID 581 = suivi, nécessite capture Reforged).
      → `PlayerController.UpdateJump` (:698-752), exports
      `JumpSpeed=7 / JumpGravity=19 / MaxJumps=2` (:64-70) —
      hauteur ≈ v²/2g ≈ 1,3 u.
- [x] Preuve : run live `--live --input-test` — `[jump] impulsion n=1`
      → `n=2` en l'air (y=0,81) → `atterrissage` ; capture
      `godot-shots/00-jump-air.png`. Diag derrière EDEN_JUMP_DIAG=1.

## 2. Collisions (pont première map)

- Helpers `floor*`/`plane*`/`object*`/`U[A-Z]\d+`/primitives → blockers purs
  (`CollisionBuilder.MakeShape:122-146` ; AABB-box si >10k tris = mur
  invisible géant). `GroundHeight` = heightfield terrain SEUL → pas de sol
  marchable hors terrain.
- [ ] Split classes : `true wall`/`blo(c)`/`knock`/`nocamera`/`camera` →
      blockers ; `floor*`/`plane*`/`object*`/`U[A-Z]\d+`/primitives → sols
      marchables sur une LAYER dédiée.
- [ ] Mask joueur exclut la layer sol du `MoveAndCollide` ; `GroundHeight` =
      `max(terrain, raycastDown(layer sol))` (rayon court depuis la tête).
- [ ] Pas de fallback AABB-box pour les helpers classe sol (trimesh >10k ok
      ou skip).
- [ ] Preuve : traversée du pont aller-retour live, murs toujours bloquants.

## 3. Textures/apparence

- [x] GAP prouvé : joueurs distants toujours en `#B202` —
      `EntityRegistry.HandleDisplayChar` (:743-767) ne reçoit jamais
      `ApplyEquipment` (seul appelant = `EquipSlotsPanel` local).
- [x] Router l'équipement vu vers `CharVisual.ApplyEquipment` pour les
      entités distantes : queue 263 parsée (`ParseDisplayTail`,
      EntityRegistry.cs:850-880 — set0 mask u32 + records
      [u16 id][EquipView] via `NativeItemRecord.TryReadEquipView`) →
      `RemoteEntity.WornItems` → `ApplyWornEquipment` (:93) ; deltas
      276 → `HandleChangeEquip` (:902). Preuves runtime `--input-test`
      (factice 265+263 rejoué) : `équip=3` parsés, `attach=2` armes
      sur bones Prop, `tenue=#B230` visible (plus le repli #B202).
      Deux bugs pré-existants corrigés en route : `FindChildren` par
      nom de classe C# ne matche jamais l'`EntityAnimator` embarqué
      → filtre `OfType<>` (bug documenté `DragHelper:52`), et
      `SetVisual` `QueueFree` différé → collision de nom « Visual »
      → `RemoveChild` synchrone. + fix `ResolveEquippedMeshes`
      `Mesh[1..]→[2..]` (suffixes tenue digit-only, CharVisual.cs:496)
      — sinon aucune tenue #B2xx ne s'affichait, local inclus.
      Reste non prouvé : 2e joueur réel en zone (démo synthétique
      rejouée en chemin de code réel).
- [x] Mipmaps : `AssetCatalog.GetTexture` sans mipmaps → shimmer distance.
      **Fait 2026-10-26** : génération à la volée des deux côtés —
      `Image.GenerateMipmaps()` dans `AssetCatalog.LoadTexture` (PNG
      directs) + extension `EdenGltfMipmaps` enregistrée sur
      `GltfDocument` (`_ParseImageData` → mips pendant le décodage des
      images GLB, embarquées ou URI externes — vérifié 249/249 sans
      mips avant). Samplers shaders passés en
      `filter_linear_mipmap` (sky/terrain splat/toon/water).
      `EDEN_MIPMAPS=0` = kill-switch A/B. Mesures S002 live
      steady-state (post-stall spawn ~50 s, identique dans les deux
      runs) : `EDEN_MIPMAPS=0` fps≈222 (215-228), VRAM 3214 MB,
      tex 3096 MB ; mips on fps≈222 (211-228), VRAM 4204 MB,
      tex 4051 MB, RSS ~identique (3,6 GB), loadmap 4196→4284 ms —
      **aucune régression FPS**, coût = +955 MB tex (+31 % ≈ chaîne
      de mips théorique).
- [x] Validation multi-classe/costumes : captures `--shot` par classe×corps
      (warrior d'abord) sélection + en jeu.
      **Fait 2026-10-26** : nouveau diag `--appearance-test`
      (vis_test) — lineup de 12 combos sur les 7 corps (b/g/m/f/k/l/c)
      × variantes visage/peau/cheveux/couleurs × costumes 4pequip
      (worn simulé codes réels : #_208, casque attaché B1102/G1107,
      arme W13407 bone Prop1). Dump `[appmat]` par combo : mesh
      cheveux, tenues visibles, peaux, texture visage, suffixes voulus
      absents, attachements bone. Résultats : 12/12 corps+meshes
      résolus, `voulu_absent` vide partout, 1 seule texture visage
      manquante = `KFACES12` (Ursun n'a que FACES01-02 + 70-76 —
      limite données, loguée par le nouveau log `[assets] texture
      absente`). Limites constatées : corps F sans cheveux (Anuran —
      normal), `#B208` sans peau `SB208` appariée (costume intégral —
      130 #B2xx vs 105 SB2xx dans B.glb). Captures :
      `godot-shots/appmat-s002.png` (lineup 12 corps),
      `appmat-select.png` (sélecteur), `appmat-create.png`
      (création), `live-ingame.png` (jeu S002 live, zone hello ok).

## 4. Latence animations déplacement

- Départ ≈ ok (fix H10) ; arrêt = window 90 ms + hold 60 ms + blend 120 ms ≈
  200-270 ms de run-on ; remote : RenderDelay 150 ms + même chemin ≈ 400 ms.
- [x] Arrêt : `InputMoving` relâché + déplacement ~nul → `_locoMoving=false`
      immédiat (symétrie du départ), sans attendre window+hold —
      `EntityAnimator` :344-388 (`StillStopSeconds=45 ms`,
      `LocoBlend=0,07 s` pour les bascules stand↔move).
- [x] Remote : « moving » dérivé du payload 326 (`DstPos != CurPos`) au lieu
      du déplacement mesuré — `EntityRegistry.HandleMovePrediction`
      posé sur `Animator.InputMoving` (:678-680). Preuve :
      `remote anim=move01` dès la trame synthétique.
      RenderDelay conservé à 0,15 s (doc `NetworkInterpolator.cs:16-23`).
- [ ] `move02`/`move03` quand vitesse > marche (`RunSpeed`), si prouvé que
      ce sont les variantes course.
- [ ] Preuve : `EDEN_ANIM_DIAG=1` + timestamps input→move01/release→stand01.

## Hors scope

- Sync saut serveur (NCID 581 — attendre capture Reforged `jump_feature=1`).
- Hypothèses face-variant/dye/hair-index (preuve native requise d'abord).
