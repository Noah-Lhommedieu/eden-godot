# Plan — lot polish final (CharColor, monture, ifl objets, détails)

## 1. CharColorTable — convention d'index (BUG confirmé)

**Preuve désasm `client_game`** (corps dans re/ghidra, mêmes adresses rf_game) :

- `FUN_0073e970` @0x0073e970 : `body=*(param_1+0x168)` ; body==2 → base
  0x191 (401 Zumi) ; ==4 → 0x1f5 (501 Anuran) ; ==6 → 0x259 (601 Ursun,
  + `FUN_00abc210(sel==10)`) ; sinon 0xc9 (201 Human/Halfkin).
  `id = base + sel` (sel = bits 24-29 du style bitfield = skin_color du
  blob) → `GetData(id)` dans la map CCharColor → RGB packed → dye slot 2.
- `FUN_0073e8d0` : cheveux `sel + (body==6 ? 0x2bd=701 : 0x65=101)` →
  dye slot 1. (0xfffffda8 = -600).
- `FUN_0073ea60`/`FUN_0073eaf0` : yeux `sel + 0x12d=301` → dye slot 3
  (deux sous-couleurs — eyes_color/eyes_color2).
- Palette dye équipement : `FUN_006e6620` remplit `DAT_010c0f60[i]` =
  `GetData(i+1)` pour i=0..63 → les sélecteurs 6 bits du dyeing
  (`FUN_00ab6b30`) indexent les lignes id 1..64 (DYE_*) — cas distinct
  des teintes corps.
- Bonus preuve : `FUN_0073e6c0` → mesh cheveux natif = `#%c1%02d`
  (sel à 2 chiffres), pas 100+sel.

**Bug actuel** : `CharColorTable.Get(s)` retourne la s-ième ligne
(positionnelle) = id s+1 (DYE_*) au lieu de `base+sel` → skin 3+ rend
orange/rouge au lieu de SKIN_TONE_*.

**Fix** : table indexée par **id de ligne** ; API `Skin(bodyCode,sel)`/
`Hair(bodyCode,sel)`/`Eyes(sel)` avec bases natives ; CharVisual passe
`app.BodyCode`. Re-tint dans ApplyEquipment via meta nœud.

## 2. Monture visuelle

Item monture équipé (loc 282/283 mask=2 / InventoryState) → GLB
`ride/model/<stem>.glb` (stem via ridelist.ini — chercher
MissionBridge/RideList existant) attaché sous le perso, selle ~+1.0,
idle anim monture. Vérifier si test1 en a une (logs MountHud) sinon mock.

## 3. Objets .ifl map/effect

Étendre WaterAnimator aux .ifl sous map/model ou effect/ si trivial ;
sinon documenter.

## 4. Détails : labels NPC (déjà EN — vérifier), placeholder distants,
blob shadow (vérifier visibilité).

## Résultats

- [x] CharColorTable : clé = id de ligne (pas position) ; bases
  natives skin 201/401/501/601, hair 101/701, eyes 301 + sélecteur.
  test1 live : skin=0 hair=0 (corps b) → SKIN_TONE_1 (207,153,114) +
  HAIR_LIGHT_BROWN (220,180,150). Re-tint ApplyEquipment via meta
  `eden_tint_*` posées par BuildBody.
- [x] Monture : remote via 263 Rest bit6 rideKey →
  `RemoteEntity.SetRide` (GLB `ride/model/<stem>` −90°X + animator
  `ride/animation/<stem>` — r071 : 4 anims retargetées, selle = 72 %
  hauteur bornée 0.5-1.6, lift réappliqué au re-SetVisual). Self :
  chemin existant MissionBridge 283 mask=2 (non modifié — hors scope).
  test1 non monté en live (aucun [mount]) → preuve mock vis_test
  `--ride=71010`.
- [x] .ifl objets : **documenté — rien à animer**. 48 .ifl dans
  raw_assets ; seuls les 10 `map/texture/*.ifl` (eau) sont référencés
  par les .fsm (déjà via WaterAnimator). `map/model/*.nif` : 0
  référence .ifl (les 908 hits grep étaient du bruit binaire —
  vérifié strings/JSON GLB). effect/texture/*.ifl référencés par des
  NIF d'effets non rendus en jeu (CombatFx n'instancie pas de modèle
  d'effet). char/item .ifl orphelins. Aucun GLB converti ne contient
  de référence .ifl.
- [x] Détails : labels NPC déjà EN (TextResolver via ResolveNpc) ;
  placeholder distant remplacé par le vrai corps à la réception du
  263 ; blob shadow posé sur joueur (PlayerController) et chaque
  RemoteEntity (Decal +0.6 — MapLoader NPC en avait déjà).
- [x] Tests : 275 verts (+3 CharColorTable) ; build 0/0.
- [x] Captures : godot-shots/final_polish_{char,mount,live}.png.
