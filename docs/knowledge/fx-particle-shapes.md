---
title: FX — particules en carrés et bordures de feuilles (texture, clamp, alpha)
status: hypothesis
version: Godot 4.8-beta2 .NET
date: 2026-11-03
---

# Particules/effets rendus en carrés — causes et correctifs

Constat d'origine : les effets de sorts (étoiles, cercles, flammes, runes)
apparaissaient comme des **quads pleins** (carrés). Banc :
`tools/capture-fx.sh effect/model/<n>.nif <nom>` → `artifacts/fx-sheets/`,
diagnostic par PSys : `EDEN_FX_DIAG=1` (lignes `[psdiag]`).

## Cause racine v1 — textures embarquées ignorées (corrigé avant reprise)

`NiParticleRuntime.Create` → `NativeFx.MakeMaterial` ne lisait que
`tex.Base.File` ; pour les NIF dont la texture est un `NiPixelData`
**embarqué** (`Source.Pixels`), `use_tex` restait faux → quad plein.
Correctif (commit 611f5a4) : `NativeFx.PixelTexture` décode les formats
0/1 (RGB24/RGBA32 via `Channels`) et 4/5/6 (DXT1/3/5) + mips.

Preuves (`artifacts/fx-quad/`) : `b-s53921.png` (cartes blanches) →
`a-s53921.png` (étoile) ; `b-s113411.png` (socle carré) → `a-s113411.png`
(disque) ; `before-notex.png` → `after-notex.png` (planche 4 effets :
rune s82211, flamme s81111, sphère s113411, étoile s53921).

## Couverture texture réelle (audit 2026-11-03, `tools/fx-audit.sh`)

Sur `corpus/fr-full/effect/model` : 1520 nif à particules, **2750 PSys** :

| kind | n | blend | n | clamp | n |
|---|---|---|---|---|---|
| ext-file (.dds) | 2681 | 3 additif | 2135 | 3 WRAP | 1738 |
| embedded (NiPixelData) | 69 | 2 mix | 615 | 0 CLAMP_S_T | 1008 |
| no-base-map / src-no-pixels | **0** | | | 1/2 mixte | 4 |
| fichier absent du corpus | **0** | pxfmt | 1=40, 5=12, 6=16, 4=1 | | |

→ Toutes les PSys ont une texture résolvable ; les carrés résiduels après
PixelTexture ne viennent PAS d'un chemin manquant. `artifacts/fx-audit.tsv`.

## Cause racine v2 — ClampMode ignoré (bordures)

Le shader FX (`NativeFx.GetShader`) utilisait `repeat_enable` pour tout.
Or `NifTexMap.ClampMode` : 0 = CLAMP_S_CLAMP_T (37 % des PSys, et ~17 460
`NiTextureTransformController` ciblent des textures clamp0 dans 1506 nif).
Avec repeat, un défilement UV (ou le filtre bilinéaire au bord du quad)
rabat les texels du bord opposé → **liseré / cadre de feuille visible**.

Correctif : `GetShader(..., bool repeat)` — `repeat_disable` quand
`ClampMode == 0` ; modes 1/2 (un axe wrappe) et 3 gardent repeat.
`EDEN_FX_CLAMP=0` restaure l'ancien comportement (preuve A/B).

## Cause racine v3 — alpha non prémultiplié dans les mips

`Image.GenerateMipmaps` (box) moyenne le RGB des texels alpha=0 (souvent
noir) dans les bords → halo/liseré sombre autour des sprites alpha-blendés.
Correctif : `MipmapsPremult` (NativeFx) génère la chaîne de mips en alpha
prémultiplié puis divise par alpha — applique aux textures **embarquées**
uniquement (les .dds externes gardent leurs mips d'origine, authentiques).

## Repli propre

`tex.Base` présent mais texture non résolue (fichier absent, pixels
illisibles) → `use_tex=false` explicite (avant : sampler non lié = blanc).

## Non vérifié / limites

- s00141 : `particles=0` à t≥0,7 s est **authentique** (lifespan 0,3 s,
  birth arrêté à 0,667 s) — capturer avec `--t=0.35`.
- Textures externes DXT5 : frange de bord non prémultipliée = aspect
  authentique du fichier (mips cuites dedans) — pas de correction.
- `NifMaterials.cs` (StandardMaterial3D, géométrie non-FX) : ClampMode
  non honoré (hors scope de ce chantier).
- UI : `CeguiToGodot.GetRegion` + mascot `LoadingScreen` = `FilterClip`
  (commit 611f5a4). Hors scope : `DamageFloaters` RegionRect, `PetFunctionBar`,
  `ItemTooltip`, `ElfWindow`, `EdenCursors` (AtlasTexture sans FilterClip).
- NiFlipController : pas discrets = comportement natif (pas d'interpolation
  entre cadres de flipbook — voulu).
