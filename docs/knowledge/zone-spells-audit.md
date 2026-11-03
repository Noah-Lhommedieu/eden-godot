# Sorts de zone : routage, géométrie, animations de cast (2026-11-03)

Statut : décodage natif **verified** (décompilé zone, adresses citées) ; rendu et
gestuelles **verified en session live** là où le texte le dit, **hypothesis** sinon.
Client : `eden_godot/godot_project/scripts/combat/`. Tests : `eden_godot/tools/NetTests/SpellZoneTests.cs`.

## 1. Ce que le natif fait (zone, `re/ghidra/zone/decompiled.c`)

`SSpellData+0x18` (« forme ») vient de `CSpellDBO::LoadAllSpellData` @0x08380130 :
C_Spell index 20 (`p1`) et index 30 (`sub`) :

| p1 | sub | forme |
|---|---|---|
| 0 | 3 | 5 croix ; sinon 3 |
| 1-3 | - | 3 (centré lanceur) |
| 4 | 0/1/2/3 | 0 cible / 2 direction / 6 secteur / 5 croix |
| 5 | 0/1/2/3 | **1 cercle au sol** / 2 / 6 / 5 |
| 6, autre | - | 0 |

Générateurs (`CSpellFactory::InitSpellTable` @0x08154a10) : 0 ToLife, 1 ToLocation, 2 ToDirection,
3 ToNowhere, 4 ToNearby (jamais produit), 5 ToCross, 6 ToSector. Quel message accepte quelle forme :

- `CastToLocation` @0x08154210 (NCID 16) : formes 1, 2, 5, 6 uniquement. Forme 1 = cercle **centré sur le
  point**. 2/5/6 = axe lanceur → point (vecteur `(px-cx, py-cy)` normalisé).
- `CastToLife` @0x081545a0 (NCID 9) : formes 0, 2, 5, 6. 2/5/6 prennent la **position de la cible** comme
  point. **La forme 1 est refusée** (0x5aa « can't cast to life »).
- `CastToNowhere` @0x08153990 (NCID 17) : forme 3 uniquement, centre = lanceur. Toute autre forme est
  refusée (0x5ac).
- Rayon : `+0x48 = (short)(u16 col30 × 0.5f)` (index 29), **tronqué** (`CSpellToLocation::HitTest`
  @0x084f9300 : distance ≤ rayon + mod AttrSpellRadius/2 ; le +0x48 stocke déjà la valeur divisée).
- Cercle au sol (`CSpellToLocation::HitTest` @0x084f9300) : `GridRangeSearch(centre=*this+0x2c,
  rayon=R(+0x48)+modAttr(1)/2)` — R′ = `(short)((mod + R) / 2) + R` ; l'énumération n'a lieu que si
  `R′ > 0`.
- Secteur (`CSpellToSector::HitTest` @0x084fb410) : sommet = position lanceur (`spell+0x24 → +0x28/+0x2c`),
  extrémité = lanceur + axe·L, `L = +0x40` ; `SectorSearch(extrémité, L, lanceur, angle=atan2(axe.y,axe.x),
  demi-angle=atan2(R′, √(L²−R′²)))` avec `R′ = min(R(+0x48), L)` — soit un cône de longueur L et
  demi-angle `asin(R/L)` (R≤L). Deux grilles fouillées (`+0x3c` et `+0x50` — mobs et joueurs).
- Direction (`CSpellToDirection::HitTest` @0x084f6ad0) : **onde balayée** — à chaque tick
  `RectangularRangeSearch(centre=lanceur+axe·(distParcourue+len/2), longueur=len(+0xb0), largeur=2·R(+0x48),
  rotation=axe)` ; `distParcourue(+0x64)` avance de `len×DAT_08894104` par tick, restant `+0x68` décrémenté ;
  `NotifyFlyLocaltion` @0x084f6590 émet `ClientSpellFlyToLocation` (position de l'onde, paquet visuel).
  Union temporelle ≈ bande de largeur 2R partant du lanceur sur l'axe, longueur totale ≈ portée.
  Les victimes déjà touchées sont dédoublonnées par la liste `+0x74`.
- Croix (`CSpellToCross::HitTest` @0x084f5020) : **croix grandissante** — `CrossSearch(centre=lanceur
  (+0x4c), largeur=R(+0x48), envergure=2·(+0x40), rotation=axe + perpendiculaire (composante y
  sign-flippée), parcouru(+0x98), plafonds 4 bras +0x88..0x94)`. Union ≈ deux bandes perpendiculaires
  de largeur R et demi-longueur L centrées sur le **lanceur** (pas le point visé : le point ne sert
  qu'à l'orientation). `+0xb0`/initialisation de `+0x68`/`+0x88..0x94` : champ SSpellData non résolu
  (**hypothesis** : portée de vol / bras max).
- `+0x40 = (float)(u16 col28 × _DAT_0887b5b0)`. **Vérifié en .rodata** (binaire
  `server_us/ZoneServer/ZoneServer` @0x0887b5b0) : c'est un **double** — valeur **0,5**.
- Champs restants résolus le 2026-11-03 (constantes lues en .rodata/.data du binaire, rodata
  vaddr 0x087af0c0) : `DAT_08894104` = **-0,5f** (centre du rect d'onde = milieu du segment couvert) ;
  `_DAT_087b2754` = **1,0f** (une onde par tick) ; `DAT_088774d0` = **0,1f** ; `DAT_08896ac8` = **π/2**
  (rotation des bras) ; `DefaultFlySpeed` = 40, `MovingHitUpdateTime` = 1
  → `MovingHitUpdateFlySpeed` = **4,0**.
- Direction : `SSpellData+0xb0` = `flySpeed(+0xa8) × clamp(+0xac/+0xa8, 0,1, 10)` (`SSpellData::Init`
  @0x084eecf0) = longueur d'onde ; `+0x68` = `distance / +0xb0` (ticks restants) initialisé par
  `NotifyBlastToSelf` @0x084f6070 — la distance est `L + trunc(mod/2)` **plafonnée par
  `CheckLineOfMoveUntilBlock`** (obstacles, pas 0,4).
- Croix : `+0x9c` = même formule `(L + trunc(mod/2)) / +0xa0` ; `+0xa0` = `MovingHitUpdateFlySpeed` ;
  `+0x88..0x94` = distance lanceur→pointe de chaque bras après `CheckLineOfMoveUntilBlock`
  (NotifyBlastToSelf @0x084f40c0) ; `+0x58..0x84` = pointes 3D des 4 bras (axe tourné 0/90/180/270°).
- Gate spécifique cercle (`HitTest` @0x084f9300) : l'énumération est **sautée quand +0x1a == -91**
  (spec « toute entité ») — zéro victime d'AoE malgré la spec permissive.
- Rayon cercle natif exact : `R(+0x48) + trunc(mod/2)` où `trunc(mod/2)` est écrit
  `(mod + ((mod>>15)&1)) >> 1` — équivalent à `mod/2` entier ; le cercle PBAoE
  (`CSpellToNowhere::HitTest` @0x084fab70) utilise au contraire le mod **entier** + R.

## 2. Bugs client corrigés

| # | Avant | Après |
|---|---|---|
| Z1 | Forme 3 envoyée en NCID 9 si cible non hostile ; formes 2/5/6 sans cible en NCID 17 (refus natif) ; forme 1 sans cible en NCID 17 (refus natif) | `SpellZone.Plan` : routage natif par forme (tests unitaires) |
| Z2 | Forme 1 : cast immédiat sous la cible, sans visée ni portée | Visée au sol `GroundTargeter` : anneau (Decal) au curseur, rayon `AreaRadius`, borné à la portée, clic gauche = lancer, droit/Échap = annuler (ref-shot « cercle preparation… ») |
| Z3 | 297 (blast au sol) ignoré : aucun effet au centre du cercle | `SpellFx.OnGroundBlast` : effet d'impact posé au sol (hauteur = sol marchable), orienté sur l'axe lanceur → point |
| Z4 | Gestuelle jouée au **279** avec la col3 brute, rejouée en boucle pendant l'incantation | Gestuelle du `BlastId` **résolu** (288 / 297 / 289 champ 11) jouée **au blast**, pose tenue pendant l'incantation (ref-shot TimedCastSkill, délai de swing serveur = temps `hit` à partir du blast) |
| Z5 | 289 : le client lisait le **lanceur** (champ 3) comme victime → effet d'impact sur le lanceur chez les témoins | Alias `Caster` / `TargetHandle` / `BlastIdx` sur `ZoneClientSpellBlastToLife` (layout natif réel) |
| Z6 | Lanceur distant : gestuelle brute au 278 même pour un sort temporisé | Instantané : au 278 ; temporisé : au 289/297 (kfId résolu) |

## 3. Preuves

Voir le rapport de session (captures `artifacts/` et journaux) ; commandes :
`--combat-test --clone-mob=20678 --cast-spell=50622 --ground-auto --zshot-dir=… --zshot-times=…`,
`--ground-demo=50622 --zshot-dir=… --zshot-times=…` (anneau de visée puis lancer au centre).

## 4. Non fait / à vérifier

- ~~Géométrie serveur des formes 2/5/6~~ : **PORTÉE — verified (2026-11-03)**.
  `Eden.Zone/Combat/SpellAreaGeometry.cs` + dispatch dans `SpellSplashAsync`/
  `ApplyAreaSpellAsync` : cercle exact (R + trunc(mod/2), gate spec -91), secteur = cône
  (sommet lanceur, L = col28×0,5 vérifié, demi-angle asin(min(R,L)/L), sans mod), direction =
  bande 2R du lanceur (longueur L + trunc(mod/2)), croix = 4 bras largeur R, demi-longueur
  L + trunc(mod/2) **centrés sur le lanceur**. Tous les champs §1 résolus (rodata).
  Tests : `Eden.Tests/SpellAreaGeometryTests.cs` — 19 cas verts. Limites documentées :
  plafonds d'obstacles (`CheckLineOfMoveUntilBlock`) non portés ; onde temporelle remplacée
  par l'union statique ; paquet visuel `ClientSpellFlyToLocation` (546) non émis aux témoins.
- Pose exacte pendant l'incantation : le natif ne la fait pas apparaître dans les données (le client natif
  n'a pas de symboles). Choix actuel : stance de locomotion + glyphe de cast + barre ; **hypothèse**.
- Rotation du glyphe de **visée** selon l'axe pour les formes directionnelles : non faite (le glyph
  d'impact 297, lui, est orienté — `SpellFx.OnGroundBlast`).

## 5. Session live 2026-11-03 (~19h) — compléments vérifiés

- Visée au sol 50622 (live, compte test123, map S002) : anneau rayon 10, portée 22 (clamp à `+0x40`),
  hauteur = sol marchable ; clic → NCID 16 → 279 (castDs 60) → 297. Anim résolue `shoot01`
  (blastKf 401, stem b) ; effet `s35541` posé au centre, axe lanceur→point `=(0,84,−0,55)`.
  2e cast refusé NCID 410 (cooldown) — conforme.
- Sort 50015 (Self, blast 3004) : NCID 17 → 279 → 281 victimes ; anim `shoot54`.
- Kill-freeze : voir rapport — les ~130 ms post-kill viennent du chemin **loot 274** (PutItem →
  PlayerBag/InventoryWindow/QuestTracker), pas du spawn du trésor au sol (mob 79 = dépôt direct en
  sac) ni de la mort elle-même (mob sans drop : p99 post-kill 9,4 ms ≤ baseline). Hors périmètre
  des fichiers `combat/` : correctif à router (fusionner les 3 `RefreshCells` par 274 + tracer).
