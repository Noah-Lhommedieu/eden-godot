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
  @0x084f9300 : distance ≤ rayon + mod AttrSpellRadius/2).
- Secteur (`CSpellToSector::HitTest` @0x084fb410) : sommet au lanceur, longueur `+0x40`, demi-angle
  `asin(R/L)`. Direction (@0x084f6ad0) : rectangle balayé, largeur `2R`. Croix (@0x084f5020) : deux bandes
  de longueur `2·(+0x40)`, largeur `R`. **Non portés côté serveur** (voir §4).
- `+0x40 = (float)(u16 col28 × _DAT_0887b5b0)`. La constante n'est pas lisible dans l'export ; ses autres
  usages (`x·c + c'`, `d ≤ r + c`) pointent vers 0,5 : **hypothèse**.

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

- Géométrie serveur des formes 2/5/6 : approximation circulaire conservée (`SpellSplashAsync`,
  `ApplyAreaSpellAsync`). Le secteur/croix/rectangle natifs dépendent de la constante `_DAT_0887b5b0`.
- Pose exacte pendant l'incantation : le natif ne la fait pas apparaître dans les données (le client natif
  n'a pas de symboles). Choix actuel : stance de locomotion + glyphe de cast + barre ; **hypothèse**.
- Rotation du glyphe de cast selon l'axe pour les formes directionnelles : non faite.
