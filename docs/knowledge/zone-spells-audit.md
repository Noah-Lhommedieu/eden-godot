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

## 6. Session live 2026-11-03 (~21h) — ancrage du blast directionnel + floaters

Correctifs client (`combat/SpellFx.cs`, `combat/DamageFloaters.cs`) — **verified live**
(compte test123, map S002, `tools/run.sh -- --login=test123:test123 --channel --char --combat-test …`) :

- **Ancre des effets de blast directionnels** : l'effet 297 des formes 2/5/6 (direction/
  croix/secteur) était posé au point `dst` ; la géométrie native (§1 : sommet du cône,
  centre de la croix, origine de l'onde **au lanceur**) veut l'ancre à `src`. Corrigé :
  `OnGroundBlast` ancre à `src` pour les formes directionnelles (`dst` inchangé pour le
  cercle au sol). Le 288 (lanceur self, formes directionnelles) posait l'effet **sur la
  victime** : remplacé par `DirectionalBlast` ancré au lanceur, orienté vers `(X,Y)` du
  paquet ; le 289 témoin suit le même chemin (src/dst du paquet).
- **Gestuelle distante du 297** : le filtre « entité la plus proche de src » ignorait les
  mobs et pouvait animer un voisin quand le lanceur est le joueur local (absent du
  registre). Désormais : src ≈ joueur local → pas de gestuelle distante (déjà jouée par
  `SkillCast.PlayCastAnim`) ; sinon entité joueur **ou mob** la plus proche (≤ 1,5 u).
- **Floaters** : log `[verify] floater victime=…pos=…` ajouté — prouve un floater par
  victime de chaque 281, posé à +2,2 u sur l'entité victime (déjà correct, désormais
  tracé).

Preuves live (journaux `--combat-test --clone-mob=20678`) :

- Secteur 50663 ciblé (NCID 9 → 288) : `[verify] blast dir #50663 forme=Sector
  src=(360,3,58,8) dst=(364,4,60,1) axe=(0,95,0,32)` — src = position du joueur ; 28
  victimes `s55541` + 28 floaters à leurs positions.
- Secteur 50663 sans cible (NCID 16 → 297) : `[spellfx] zone #50663 forme=Sector
  src=(357,1,60) dst=(357,1,65) ancre=(357,1,60) axe=(0,1)` — ancre = lanceur.
- Cercle 50622 au sol (297) : `ancre=(330,9,62,1)` = dst — chemin inchangé.
- Cyclone 50015 (self, 17) : FX `s10541` joueur + 3 victimes + 3 floaters.
- Tornade 50027 (ciblé, 9 → 288) : `s11741` sur la victime principale + 14 victimes
  281 avec FX + floater chacune (ex. `victime=0xffff90e7 pos=(362,7,2,-61) «237» Crit`).

Divergences serveur constatées (rapportées — serveur en lecture seule) :

- `SendSpellBlastToLocationAsync` écrit `dst` = **point visé par le client** ; le natif
  écrit la **pointe de la forme** (lanceur + axe·L pour secteur/croix/direction). Axe
  identique, mais le `dst` natif porte la portée réelle.
- `SendSpellBlastOK` (288) écrit `X,Y,Z` = position de la victime ; pour les formes
  directionnelles le natif écrit la pointe (`CSpellToSector::NotifyBlastToSelf`…).
- `SendSpellBlastToLifeAsync` (289) est émis aux **témoins** pour toutes les formes y
  compris directionnelles ; le natif n'émet pas de 289 pour les sorts à position
  (secteur/croix/direction passent par `NotifyBlastToLocation` → 297).
- `ClientSpellFlyToLocation` (546, onde visuelle de la forme Direction) jamais émis.
- Le secteur 50663 (cône attendu) touche des victimes **derrière et sur les côtés** du
  lanceur à ~10 u — la résolution serveur du secteur ressemble ici à une zone plus
  large que le cône natif (`SpellAreaGeometry` à re-vérifier sur ce cas).
- Un même impact produit parfois deux 281 **identiques sur le fil** pour une même
  victime (`NCID=281 …90e7 f690ffff 01c2eb0b bb000000 01000000` ×2, et `fe90ffff` ×2,
  run 2026-11-03 ~21h) — double envoi serveur ; le client affiche les deux, fidèle
  au fil.
- Sort 59280 (AoE) : les 281 portent `caster=0x0bebc201` = **le joueur** — c'est un
  proc/enchant AoE déclenché côté serveur pendant le combat (pas un cast explicite) ;
  il arrive en 281 directs, sans paquet blast (288/289/297) — le visuel de zone
  d'un proc n'est donc jamais diffusé (côté serveur).

## 7. Corrections serveur (2026-11-03, sortie §6)

Statut : **TESTED** (headless — paquets et géométrie prouvés par tests
unitaires ; rendu client non re-validé live). Fichiers :
`Eden.Zone/Combat/SpellAreaGeometry.cs`,
`ZoneEntryFlow.Combat.Spells.Resolution.cs`,
`tests/Eden.Tests/SpellAreaGeometryTests.cs`.
Build `flock /tmp/eden-build.lock tools/build.sh` vert ; `tools/test.sh`
vert (836 NetTests + 966 Core + 1550 Eden.Tests, 0 échec ; 23 tests
`SpellAreaGeometry`).

### 7.1 Géométrie du secteur (P0)

Preuve native complète : `CSpellToSector::HitTest` @0x084fb410 →
`CGridCoordMap::SectorSearch` @0x084fba60. Le modèle réel est
l'**intersection d'un disque de rayon L centré sur la POINTE**
(`tip = lanceur + L·axe`, param_1/param_2 de l'appel — pré-filtre
`dist(entité, tip) < rayon_entité + L`) **et d'un cône de sommet le
lanceur** (`|atan2(v−lanceur) − θ_axe| ≤ atan2(R', √(L²−R'²))` =
asin(R'/L), R' = min(R,L) ; + tolérance d'élévation `atan2(|dz|, d)`
non modélisée — pas de z victime). `ContainsSector` réécrit en
conséquence (wrap d'angle identique, `atan2f(0,0)=0` pour la victime
au sommet). Conséquences natives :

- portée avant jusqu'à **~2L** sur l'axe (le hit « 16u devant » de
  50663, L=10, était donc fidèle — pas un bug) ;
- tout point **derrière** le lanceur est rejeté deux fois (distance
  à la pointe > L et angle 180°) — le « ~10u derrière » du live ne
  venait pas du cône mais du **repli `!axisOk` → disque R** quand la
  cible se tenait au pas du lanceur (axe nul) : supprimé, le natif
  garde `atan2f(0,0)=0` → axe (1,0) (`SpellSplashAsync` +
  `ApplyAreaSpellAsync`).

Tests ajoutés : `Sector_BehindCasterAlwaysMisses_50663` (16u derrière
MISS, 10u derrière MISS, côté pur ~10u MISS hors disque-pointe,
(5,8) HIT, 16u devant HIT natif), `Sector_TipDiscAndApexCone`,
`Sector_ApexVictimFollowsAtan2OfZero`.

### 7.2 Positions des paquets (P1)

- **288** `ExecuteSpellHitAsync` (NCID 9) : shapes 2/5/6 →
  `VictimHandle=0`, pos = **pointe de la forme** (secteur
  `lanceur+axe·L` @0x084fb170 ; direction `lanceur+axe·(L+trunc(mod/2))`
  @0x084f6070 ; croix : pointe du bras 0 @0x084f40c0). Shape 0 inchangé
  (victime + sa position, @0x084f7820).
- **288** `ApplyAreaSpellAsync` (NCID 16) : désormais émis au lanceur
  pour toute forme (natif `NotifyBlastToSelf`), victime=0, pos =
  pointe/point visé (cercle : point brut @0x084f8c70).
- **297** : `dst` = **pointe** pour les formes directionnelles (avant :
  point visé brut — divergence §6 corrigée) ; `src` = lanceur ; la
  croix remplit `VecA`/`VecB` = x/y des bras 1-3 (codec complet).
  Le point visé brut est conservé pour le cercle au sol (shape 1).

### 7.3 Routage (P2)

- **297 aux témoins** pour les formes à position — remplace le 289
  dans `ExecuteSpellHitAsync` (natif `CSpellTo{Sector,Direction,
  Cross,Location}::NotifyBlastToOthers` → ClientSpellBlastToLocation ;
  289 = `CSpellToLife` seulement @0x084f7e40). 289 conservé pour
  shape 0.
- **297 au lanceur** : gardé pour le cercle (son 288 victime=0 est un
  no-op `Impact` client → sans lui le visuel au sol disparaît) ;
  retiré pour 2/5/6 (le 288 directionnel suffit — un 297 self
  doublait l'effet).
- **546** `ClientSpellFlyToLocation` : natif `CSpellToDirection::
  NotifyFlyLocaltion` @0x084f6590 (position courante de l'onde,
  4 vecteurs 1-float). Codec existant côté client ET serveur, mais
  **aucun handler client** → non émis ; l'onde est déjà résolue en
  union instantanée (documenté §1).
- **Double 281** : `ApplyNowhereResultAsync` émettait 281 puis
  `KillMobAsync(spellId)` → `KillMobBroadcastAsync` ré-émettait un
  281 `flags=Hit` identique pour les victimes tuées (correspond aux
  paires `59280 …01000000 ×2` du fil). Corrigé :
  `FinishMobDeathAsync` comme tous les autres chemins de mort par
  sort.
- **59280** : blast ajouté (288+297 au lanceur, 297 aux témoins,
  `dst`=lanceur — cercle auto-centré) ; rayon désormais lu depuis la
  donnée (`AreaRadius` = col30×0,5 = 10u + trunc(mod/2)) au lieu du
  5u codé en dur.

### 7.4 Limites connues

- Victimes modélisées en points : `rayon_entité` du pré-filtre natif
  non ajouté (le natif touche à `dist(tip) < r_entité + L` — écart
  ≤ ~1-2u en limite avant) ; tolérance d'élévation `atan2(|dz|,d)`
  absente (z victime non modélisé) ; clamp mur
  `CheckLineOfMoveUntilBlock` absent (pas de mur live).
- Le 288 généré ne peut pas porter les vecteurs croix (counts sans
  floats) → bras 1-3 émis dans le 297 seul ; le client n'en a pas
  besoin pour son rendu (X/Y = bras 0).
- `CSpellToNowhere` 288 : victime=self conservée (natif 0) pour le
  visuel d'impact du client Godot — déviation assumée.
- Preuve headless seule : ordre/contenu des paquets et géométrie
  couverts par tests ; la validation visuelle live reste à refaire.

## 8. Gates de routage cast : « pas de sort sans cible » (2026-11-03)

Statut : **TESTED + VISUALLY_VALIDATED** (live `tools/run.sh --
--login=test123:test123 --channel --char --combat-test --cast-nowhere
--cast-spell=<id>` ; détail §8.4). Fichiers :
`eden_server/server/src/Eden.Zone/Combat/SpellCastRouting.cs` (NEUF —
règle pure partagée), `ZoneEntryFlow.Combat.Spells.cs`,
`ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.cs`,
`godot_project/scripts/combat/SpellZone.cs`, `SkillCast.cs`,
tests `NetTests/SpellZoneTests.cs` + `Eden.Tests/SpellCastRoutingTests.cs`
(neuf, 62 cas).

### 8.1 Table de routage native (vérifiée, zone @ adresses)

`SSpellData+0x18` (forme) ← `LoadAllSpellData` @0x08380130
(col21 Param1 × col31 ShapeSub). `CastTo*` exige la forme adéquate
et émet sinon `TextOutputIndex` (316 `[s16 idx][str arg]`, arg = chaîne
%s1 du fmt — vide sauf « %hu » pour 1449) PUIS 410
`ClientCommandFailReturn [i32 ncid][i32 spell]` quand le CastTo*
retourne 0 :

| NCID | Fonction | Formes acceptées | Refus (textindex) |
|---|---|---|---|
| 9 | `CastToLife` @0x081545a0 | 0, 2, 5, 6 | 0x5aa → **1450** « Inutilisable sur les gens » ; cible NULL → 0x5af → **1455** |
| 16 | `CastToLocation` @0x08154210 | 1, 2, 5, 6 | 0x5ab → **1451** « Inutilisable au sol » |
| 17 | `CastToNowhere` @0x08153990 | 3 | 0x5ac → **1452** « Inutilisable sur vous-même » |
| 59 | `CastToNearby` @0x081536d0 | 4 (jamais produit) | 0x5ac → **1452** |
| — | `CheckCanCast` @0x081526e0 | sort hors table | 0x5a9 → **1449** « La Compétence (%s1) n'existe pas. » |

Conséquence : la forme 0 (`CSpellToLife`, cible unique) n'a **aucun**
canal de repli — le client natif n'a pas de message « cast dans le
vide » pour elle : sans cible il bloque en local (prompt textindex
**2195** « Veuillez sélectionner une cible pour utiliser cette
compétence. »). `CastToNowhere` sur forme 0 et `CastToLocation` sur
forme 0/3 sont des paquets forgés que le serveur doit refuser.

### 8.2 Correctifs client (SpellZone / SkillCast)

- `CastRoute.Refused` ajouté ; `Plan(CastShape.Single, hasTarget:false)`
  retourne `Refused` (était : `ToNowhere` — envoyait un 17 voué au
  refus 410 ; c'était la faille « cast sans cible »).
- `SpellZone.RequiresTarget(s)` / `AcceptsNowhere(s)` — la forme 0 est
  la SEULE qui exige une entité ; 1/2/5/6 passent par un point (16),
  3 par soi-même (17), 4 par 59.
- `SkillCast.SendCast` : `Refused` → `SysMsg(TextResolver.TextIndex
  (2195))` + log, **aucun paquet émis**, `CastingSpell` non posé.
- Repli combat-test en zone urbaine : préfère un sort de forme ≠ 0
  pour garder la preuve du cast hors cible.

### 8.3 Correctifs serveur (Eden.Zone)

- `SpellCastRouting` (pur, Eden.Zone/Combat) : `ShapeOf(def)` (port du
  switch), `Accepts{ToLife,ToLocation,ToNowhere,ToNearby}(shape)` +
  `Accepts(ncid,shape)`, `RefusalTextIndex(ncid)`,
  `SpecAllowsPlayerTarget`, `SpecNeedsTeam`, `LifeCastOuterRange`.
- NCID 9 : sort inconnu → 316[1449 arg=id]+410 ; forme ∉{0,2,5,6} →
  316[1450]+410 ; `handle==0` → 316[1455]+410 (GetLifeEntity(0)→NULL
  natif — avant : `ResolveLifeTarget` retombait sur soi).
- Cible joueur (`CastTest` @0x084f04d0) : spec -99 (ennemi requis) et
  -95/-94 avec Type==1 → refus **silencieux** (410 seul —
  `CanCastTarget(param_5=0)` natif) ; -96/-97 → équipe requise
  (`SameTeam`, le lanceur reste admis) ; portée
  `r_c+r_t+range(+0x40)+attrMod(0)+2.5` (DAT_08896c68=2.5, .rodata)
  → 316[819 « trop éloignée »]+410.
- NCID 16 : inconnu → 316[1449]+410 ; forme ∉{1,2,5,6} → 316[1451]+410.
- NCID 17/59 : inconnu → 316[1449]+410 ; forme ≠3 (17) / ≠4 (59) →
  316[1452]+410 — avant : un 17 forgé avec un sort forme-0 retombait
  sur le chemin buff/résultat (résolu quand même !). Le `case 59` du
  dispatch porte désormais son propre ncid (410 Field1=59).
- Ordre : gates après les checks `CheckCanCast` (silence/down-time),
  avant `CastTest` (dismount/MP/279-en-cours) — comme le natif qui
  dispatche la forme dans la factory avant de créer l'ISpell.
- `DerivedShape` (privé, doublon) supprimé → `SpellCastRouting.ShapeOf`.

### 8.4 Preuves live (run.sh, compte test123)

- `--cast-spell=50022` (Perforation, Param1=4/sub=0 → **forme 0**) :
  `cast #50022 refusé : cible requise (textindex 2195, forme 0)` —
  **aucun NCID émis** (fil wire vérifié). Idem 50026/50038 (forme 0)
  en casts suivants.
- `--cast-spell=50015` (Attaque Cyclone, Param1=0 → **forme 3**,
  rayon col30=10→5u) : `cast #50015 → sans cible` → **NCID 17** →
  `279 OK` → gestuelle + blast 59280/PBAoE.
- `--cast-spell=50663` (Grille Sonique, Param1=4/sub=2 → **forme 6
  secteur**) : `cast #50663 → sans cible` → **[wire] NCID 16** vers un
  point devant le lanceur → `279 OK` → `281 impact` sur un mob dans
  le secteur. (La forme 6 n'accepte pas le 17 : le « sans cible »
  natif passe par un point, pas par CastToNowhere.)
- NCID 9 ciblé (mob, en combat) : comportement inchangé.

### 8.5 Limites / hypothèses

- Rayons d'entités (`vfunc+0x18`) de la borne `HowClose` non résolus
  dans le décompilé → `LifeCastOuterRange` ajoute **1,0 u** forfaitaire
  + la constante native 2,5 (hypothesis, documentée).
- `CanSee` (invisibilité, 0x5b0→1456) non modélisé — pas de furtivité
  serveur ; spec -95/-94 : la condition « Type==1 » est dérivée du
  param_4 de CastTest (`SSpellData+0xc`), pas re-désassemblée au-delà.
- Le check d'équipe -97 sur soi est admis (IsMyTeam(self) natif non
  désassemblé jusqu'au fond) ; PvP/duel non modélisés → spec -99 est
  refusée sur TOUT joueur (le natif les traiterait ennemi).
- Échec `NifParticleMapperTests.MountEmitter_ReskinCost_PerFrame`
  (3169 µs > seuil) au run de suite — domaine montures/FX, hors lot.
- `CastCircle.cs` (fichier SpellFx non commité d'un autre agent)
  cassait le build — fix mécanique `Godot.Mesh.PrimitiveType` posé.

## 9. FX natifs : rotation racine, phases cast, corps self --live (2026-11-04)

Session de vérification live (`--live --account=test123 --combat-test`,
caméras `--cast-cam=80,9` top-down et `55,9` diagonale, captures
`--zshot-*` inspectées). Tous les effets sont les .nif EDENFR natifs
(`corpus/fr-full/effect/model`).

### 9.1 Bugs corrigés

- **Rotation racine manquante** (`SpellFx.SpawnNative`,
  `scripts/combat/SpellFx.cs:793`) : les scènes NIF de production
  n'appliquaient pas `WorldConv.EdenRootEulerDeg` (−90°/X, repère Z-up
  → Y-up) contrairement au banc `--fx-native` et aux modèles/map.
  Effets plats dressés à la verticale, entonnoirs couchés —
  « Cyclone totalement inversé ». (verified, images A/B)
- **Corps self absent en `--live`** (`GameScene.EnsureSelfVisual`,
  `scripts/GameScene.cs:930`) : la session créée dans la scène
  n'alimentait ni `SessionCarry.Character` ni `_attached` → joueur
  capsule sans `EntityAnimator` → chant `magic01` et geste `shootNN`
  jamais rendus en test live. Le corps natif est construit après
  `EnterZoneAsync` quand le perso sélectionné est connu ;
  `[live] corps self construit (animator=True)`. (verified)
- **Release dupliquée par victime** : l'effet de zone/release
  (C_Spell col 6) est posé une seule fois par blast
  (`ReleaseForBlast`/`ReleaseAt`, `SpellFx.cs:421/439`, dédoublonné
  sort+hôte 0,4 s). Tornade 50027 : `s11722` UNE fois sur la cible
  primaire ; `s11741` (col 10) reste par victime — fidèle.
- **Hit des formes auto-centrées** (`Impact`, `SpellFx.cs:546`) :
  Self/Nearby posent l'effet AoE une fois au lanceur du blast
  (dédoublonné), plus une ancre par victime — Cyclone n'apparaît plus
  « à l'envers » sur les cibles.
- **Cercle d'incantation procédural supprimé** : `CastCircle` (mesh
  inventé) retiré du cast ; `SpellFx.BeginCastFx` pose les effets
  natifs col 4/5 (`E11611`/`E11621` → `s11611`/`s11621`) tenus pendant
  `castDs`, retirés par `EndCastFx` au blast (288/289/297), au break
  (287 → `SkillCast.StopCastVisuals`) et aux changements de nœud.
  Repli natif `s11611` pour un sort temporisé sans col 4/5.
- **Chant distant** (`OnOtherCast`, `SpellFx.cs:532`) : lanceur distant
  temporisé tient `HoldAnim=magicNN` jusqu'au blast
  (`RemoteBlastAnim` relâche avant le one-shot, + filet timer).

### 9.2 Preuves live (compte test123, S002, `//god`, `//change_class 4`)

- **Cyclone 50015** (Self, NCID 17 `sans cible`) : `s10521`+`s10541`
  x1 sur Player, `s10541` x1 sur la victime. Vortex cyan spiralé
  centré joueur (~71k px cyan, rayon ~185 px écran) à 0,3-0,6 s,
  fondu à ~0,9 s (timing natif). Captures :
  `artifacts/shots/cyclone_live2/cast_0_0,3s.png`,
  `cast_1_0,6s.png`, `cast_2_0,9s.png`, `cast_3_1,5s.png`.
- **Tornade 50027** (Single) : `s11711`/`s11721` sur Player,
  `s11722` x1 sur la cible primaire, `s11741` x1 par victime.
  Entonnoir cyan dressé sur la cible. Captures :
  `artifacts/shots/tornado_live/cast_*.png`, `tornado_live_final.png`.
- **Séisme 50026** (Single, castDs=15) : `s11611`/`s11621` sur Player
  pendant TOUT le cast (24-26k px or à 0,5/1,0/1,4 s), cercle+barre
  retirés au blast, `s11641` explosion dorée+roches sur la cible,
  `chant magic01 tenu (kf=301)`, `cast anim : shoot01 (blastKf=401)`.
  Captures : `artifacts/shots/timed_live2/cast_*.png`,
  `timed_live2_final.png`.
- **Interruption** (`--cast-break=800` → NCID 13 → 287) : cercle
  visible à 0,5 s (24,7k px or), disparu à 1,0 s (1,5k = bruit de
  fond), gestuelle relâchée, `verrou de cast #50026 libéré`.
  Captures : `artifacts/shots/timed_break/cast_*.png`.

### 9.3 Limites / restes

- Cyclone : visibilité courte (~0,9 s) et nappe fine — conforme aux
  clés alpha UV natives (verified placement/forme ; fidélité
  temporelle hypothesis, ref-shot réel du jeu absent).
- Erreurs HUD `Cannot get path of node as it is not in a scene tree`
  (OverheadLayout/RemoteEntity) au spawn distant — cycle de vie
  overhead, hors lot.
- Fuites RID/ObjectDB à la fermeture Godot — hors lot.
- `EDEN_FX_DIAG=3` : dump opt-in des contrôleurs/shapes NIF
  (`NiControllerRuntime.cs`) conservé pour diagnostic.

## 9.4 Suite 2026-11-04 — lot « FX résiduels / level-up / loot »

- **FX vert persistant après soin (50028/50026) — CAUSE TROUVÉE,
  déjà corrigée dans l'arbre** : `s11811` (cercle vert col 4
  `E11811`, posé par `BeginCastFx`) est l'UNIQUE modèle de la chaîne à
  émetteur `cyc=Loop` (dump NIF : 34/35 contrôleurs Loop — fait pour
  tourner en continu). `s11821` (col 5) et `s11841` (col 10, hit) sont
  100 % Clamp, fins ≤0,833 s. `NativeFxSeconds` retourne null sur le
  modèle bouclé → l'ancien plancher de 3 s gardait le cercle ~1,8 s
  après un soin instantané. `SpellFx.cs:845-854` : durée = `hold`
  quand tenu (pas de plancher 3 s) ; `hold=0` → fin native du dernier
  contrôleur clampé +0,5 s. FxCoverage confirme : `s11841`
  `particles_at_end=0` à 4 s, émission stoppée à 0,33 s (vis-bools
  `[0=T;0,33=F;0,5=F]`). Le nœud vit 1,33 s — fidèle. Statut :
  verified par dumps NIF ; re-validation live recommandée.
- **Rectangle noir derrière les drops (halo)** : `SetTreasureVisual`
  ajoutait le quad `Halo` APRÈS la passe `BlobShadow.ExemptVisuals` de
  `SetVisual` → halo resté couche 1, le Decal blob sous l'entité
  (3 m, ajouté à toutes les entités `EntityRegistry.cs:1873`) se
  projetait sur le billboard face-caméra → tache/fond sombre selon
  l'angle de vue — même famille de bug que le « capot noir des
  champignoïdes » (fix D4 déjà prouvé). Correctif : exempt posé après
  `AddChild(halo)` (`EntityRegistry.cs:706-712`). Note : le nif
  `g00006` porte en natif deux quads `Plane` additifs (blend src=6
  dst=0 → `BlendMode==3`, gérés par `NifMaterial.cs:220-228` et la
  chaîne FX) — piste additive vérifiée bout en bout, non fautive.
  Statut : hypothesis forte, capture live à refaire pour valider.
- **Cast « à l'envers » (rotation 90°)** : la convention racine NIF
  `-90°/X` (`WorldConv.EdenRootEulerDeg`, `SpawnNative`,
  `SpellFx.cs:829-835`) est appliquée uniformément — cyclone à plat
  et tornade dressée (captures §9.2). Les ancres directionnelles
  utilisent `Atan2(-ax,ay)` = `EdenDir.ToYaw` (`SpellFx.cs:345`).
  AUCUN offset +90° famille-spécifique n'a de preuve native ; tout
  correctif restant exigerait une capture directionnelle live
  (`--fx-show` sur une forme Direction/Cross/Sector) vs référence.
- **Level-up jaune (NCID 284)** : serveur émet 284 perso (flag 0)
  + classe (flag 1, broadcast) — déjà implémenté côté serveur
  (`ZoneEntryFlow.Combat.Rewards.cs`). Client : `SpellFx` écoute
  `MiscPackets.Received` (`ZoneClientLevelUp`, bindée par
  `MiscPackets` depuis `ZoneMiscBinds.cs`) → `Spawn` de l'effet natif
  sur le perso local (flag 0) ou l'entité `Entities[Field1]` sinon.
  **HYPOTHÈSE clé : `s99211`** — famille `s99*` système
  (E99211 dans dynEffect, sprays en éventail + marqueur « ~>UP< ») ;
  la clé exacte du handler 284 natif n'a pas été retrouvée dans
  client_game (dispatch en table de sauts [FAILED]). À corriger si la
  capture montre un autre visuel que le pilier doré attendu — le
  câblage (284 → spawn sur l'entité flaggée) est, lui, conforme au
  wire. `SpellFx.cs:44-95`.
