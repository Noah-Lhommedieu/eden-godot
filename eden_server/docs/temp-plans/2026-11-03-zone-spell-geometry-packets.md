# Plan — sorts de zone : géométrie secteur native + routage blast (P0/P1/P2)

Date : 2026-11-03. Périmètre : `src/Eden.Zone/**`, `tests/Eden.Tests`,
`docs/knowledge/zone-spells-audit.md` §7. Client/scripts et `Eden.Core/Fx`
intouchables (autre agent). Référence : `zone-spells-audit.md` §6 divergences.

## Preuves natives (zone, `re/ghidra/zone/decompiled.c`)

- `CSpellToSector::HitTest` @0x084fb410 → `SectorSearch` @0x084fba60 :
  - **pré-filtre distance = disque de rayon L centré sur la POINTE**
    (tip = lanceur + L·axe), pas sur le lanceur ;
  - test d'angle au SOMMET (lanceur) : `|atan2(v−apex) − θ_axe| ≤
    atan2(R', √(L²−R'²))` avec R' = min(R,L), + tolérance élévation
    `atan2(|dz|, d_h)` (non modélisée : pas de z victime).
  - → portée avant ~2L possible en natif ; derrière le lanceur = miss
    garanti (double rejet distance+angle).
- `NotifyBlastToSelf` : secteur @0x084fb170 (288 pos = tip, victime=0) ;
  direction @0x084f6070 (pos = extrémité balayée lanceur+axe·(L+mod/2),
  clamp mur non modélisé) ; croix @0x084f40c0 (pos = pointe bras 0,
  vecteurs = x/y bras 1-3) ; location @0x084f8c70 (pos = point visé) ;
  nowhere @0x084fa550 (victime=0, pos = point du sort).
- `NotifyBlastToOthers` : formes à position → **297** (src=lanceur,
  dst=tip ; croix : vecteurs bras 1-3). 289 = `CSpellToLife` seulement
  (@0x084f7e40).
- Direction : `NotifyFlyLocaltion` @0x084f6590 émet 546 (position d'onde
  courante) — codec client présent mais **aucun handler** → pas d'émission.
- Double 281 : `ApplyNowhereResultAsync` émet 281 puis `KillMobAsync`
  (spellId≠0) → `KillMobBroadcastAsync` ré-émet un 281 identique. Fix :
  `FinishMobDeathAsync` comme les autres chemins sort.
- Données : 50663 → L=10 (c28=20×0.5), R=10 (c29=20×0.5) → demi-angle 90°.
  59280 → Param1=5/sub=0 → shape 1 cercle, R=10 (c29=20×0.5) — le rayon
  5u codé en dur est infidèle.

## Éditions

1. `SpellAreaGeometry.ContainsSector` → disque-pointe ∩ cône-sommet
   (atan2, cas victime-au-sommet fidèle à atan2f(0,0)=0).
   Helpers `DirectionTip`/`SectorTip`/`CrossTips` pour les paquets.
2. `Resolution.cs ExecuteSpellHitAsync` : formes 2/5/6 → 288 victime=0,
   pos=tip ; témoins 297 (au lieu de 289).
3. `Resolution.cs ApplyAreaSpellAsync` : 288 au lanceur (pos = point
   cercle / tip directionnel) ; 297 dst=tip (+VecA/VecB croix) pour
   témoins ; shape 1 garde le 297 au lanceur (visuel client — le 288
   victime=0 est un no-op client).
4. `Resolution.cs SpellSplashAsync` + `ApplyAreaSpellAsync` : axe
   dégénéré → (1,0) (natif atan2f(0,0)=0), supprime le repli cercle-R.
5. `ApplyNowhereResultAsync` : `FinishMobDeathAsync` (tue le double-281),
   rayon = AreaRadius(rdef)+mod/2, blast 288+297 (self) / 297 (témoins)
   du sort résultat.
6. Tests `SpellAreaGeometryTests` : modèle disque-pointe, cas 50663
   réalistes (16u derrière MISS, 16u devant HIT natif, côté ~10u MISS),
   tips.
7. Doc §7 + build `flock /tmp/eden-build.lock tools/build.sh` +
   `tools/test.sh` + regen graphe.

## Limites assumées

- Pas de rayon d'entité victime (modèle point) ; pas d'élévation z ;
  pas de clamp mur (`CheckLineOfMoveUntilBlock`) ; onde directionnelle
  = union instantanée (pas de 546 — client sans handler).
- 288 codec : vecteurs croix non représentables (counts seuls écrits) —
  les bras vont dans le 297 (codec complet).
