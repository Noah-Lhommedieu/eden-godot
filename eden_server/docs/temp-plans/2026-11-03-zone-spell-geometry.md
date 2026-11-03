# Plan — géométrie native des sorts de zone (formes 1/2/5/6) côté serveur

Date : 2026-11-03. Source : `docs/knowledge/zone-spells-audit.md` §1/§4 +
décompilé `re/ghidra/zone/decompiled.c` (adresses citées par fonction).

## Objectif

Remplacer l'approximation circulaire de `SpellSplashAsync` /
`ApplyAreaSpellAsync` par les HitTest natifs exacts :

- **1 cercle** (`CSpellToLocation::HitTest` @0x084f9300) : centre = point,
  rayon = `R(+0x48) + trunc(AttrSpellRadius/2)` ; énumération seulement si
  spec(+0x1a) != -91 ET rayon > 0.
- **6 secteur** (`CSpellToSector::HitTest` @0x084fb410) : cône sommet =
  lanceur, angle = atan2(axe.y, axe.x), longueur `L = +0x40`,
  demi-angle `atan2(R', sqrt(L²−R'²))` avec `R' = min(R, L)` — PAS de mod.
- **2 direction** (`CSpellToDirection::HitTest` @0x084f6ad0 +
  `NotifyBlastToSelf` @0x084f6070) : onde balayée ; union = bande
  largeur 2R, du LANCEUR vers l'axe, longueur = `L + trunc(mod/2)`
  (nativement plafonnée par `CheckLineOfMoveUntilBlock` — non modélisé).
- **5 croix** (`CSpellToCross::HitTest` @0x084f5020 +
  `NotifyBlastToSelf` @0x084f40c0) : 4 bras = axe tourné 0/90/180/270°,
  centrés sur le LANCEUR, largeur R, longueur de bras
  `L + trunc(mod/2)` (plafonnée par obstacles en natif — non modélisé).

## Champs résolus dans cette session

| Champ | Valeur | Preuve |
|---|---|---|
| `SSpellData+0x40` (L) | `u16 col28 × 0.5` | `LoadAllSpellData` @0x08380130 ; `_DAT_0887b5b0` = double **0.5** (ZoneServer @ .rodata 0x0887b5b0) |
| `SSpellData+0xb0` (onde) | `flySpeed(+0xa8) × clamp(+0xac/+0xa8, 0.1, 10)` | `SSpellData::Init` @0x084eecf0 ; `DAT_088774d0` = 0.1f |
| `MovingHitUpdateFlySpeed` | 4.0 (1 × 40 × 0.1) | `nm` : `DefaultFlySpeed`=40, `MovingHitUpdateTime`=1 |
| `+0x68` dir / `+0x9c` croix | `distance / longueurOnde` (nb de ticks) | NotifyBlastToSelf @0x084f6070 / @0x084f40c0 |
| `+0x88..0x94` croix | distance lanceur→pointe après `CheckLineOfMoveUntilBlock` | NotifyBlastToSelf @0x084f40c0 |
| `DAT_08894104` | **-0.5f** → centre du rect = milieu du segment couvert | .rodata |
| `_DAT_087b2754` | **1.0f** → 1 onde/tick | .rodata |
| `DAT_08896ac8` | **π/2** (HALF_PI) → bras 0/90/180/270° | .rodata |
| mod rayon (kind 1) | `trunc(mod/2)` — `(mod + signBit)>>1` ≡ `mod/2` C# | mêmes NotifyBlastToSelf |

## Points d'édition

- Nouveau `src/Eden.Zone/Combat/SpellAreaGeometry.cs` : helpers statiques
  `ContainsCircle/ContainsSector/ContainsDirection/ContainsCross` +
  `AreaRadius/AreaLength/RadiusModHalf` + `NormalizeAxis`.
- `ZoneEntryFlow.Combat.Spells.Resolution.cs` : `SpellSplashAsync`
  (CastToLife 2/5/6, point=position cible, axe=lanceur→cible) et
  `ApplyAreaSpellAsync` (CastToLocation, axe=lanceur→point) dispatch par
  `DerivedShape` ; cercle : gate spec -91 conservée.
- Tests : `server/tests/Eden.Tests/SpellAreaGeometryTests.cs`
  (centre/rayon/axe/cas limites pour les 4 formes).

## Limites documentées

- Plafonds d'obstacles (`CheckLineOfMoveUntilBlock`, croix+direction) non
  portés — pas de carte de collision côté serveur ; zone effective
  légèrement plus grande qu'en natif quand un mur bloque.
- Paquet témoin `ClientSpellFlyToLocation` (NotifyFlyLocaltion) non émis —
  visuel uniquement ; blast serial/position déjà envoyé par 297.
- Approximation temporelle : l'onde balayée est remplacée par l'union
  statique (victimes dédoublonnées en natif par +0x74/+0xa8 — notre union
  statique a le même ensemble final).
