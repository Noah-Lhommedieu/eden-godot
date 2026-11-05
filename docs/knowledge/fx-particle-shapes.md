---
title: FX — particules en carrés et bordures de feuilles (texture, clamp, alpha)
status: verified
version: Godot 4.8-beta2 .NET
date: 2026-11-03
---

# Particules/effets rendus en carrés — causes et correctifs

Constat d'origine : les effets de sorts (étoiles, cercles, flammes, runes)
apparaissaient comme des **quads pleins** (carrés). Banc :
`tools/capture-fx.sh effect/model/<n>.nif <nom>` → `artifacts/fx-sheets/`,
diagnostic par PSys : `EDEN_FX_DIAG=1` (lignes `[psdiag]` : blend, alpha,
clamp, source texture externe/embarquée, taille, plage alpha, mips).

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

Diag par effet (`EDEN_FX_DIAG=1`, headless OK) — chaque PSys résout sa
texture (externe via `effect/texture/*.dds`, ou `px=True` + `pdb=` +
`fmt=` pour l'embarqué) avec plage alpha réelle (`a=0..247` … `0..255`).

## Cause racine v2 — ClampMode ignoré (bordures)

Le shader FX (`NativeFx.GetShader`) utilisait `repeat_enable` pour tout.
Or `NifTexMap.ClampMode` : 0 = CLAMP_S_CLAMP_T (37 % des PSys, et ~17 460
`NiTextureTransformController` ciblent des textures clamp0 dans 1506 nif).
Avec repeat, un défilement UV (ou le filtre bilinéaire au bord du quad)
rabat les texels du bord opposé → **liseré / cadre de feuille visible**.

Correctif : `GetShader(..., bool repeat)` — `repeat_disable` quand
`ClampMode == 0`. Pour les modes mixtes (1 = clamp S, 2 = clamp T), le
sampler garde `repeat_enable` (l'axe wrappé doit continuer à défiler) et
l'uniform `uv_clamp` borne l'autre axe dans le fragment
(`clamp(p, 0, 1)` par axe). `EDEN_FX_CLAMP=0` restaure l'ancien
comportement (preuve A/B).

A/B capturé (`artifacts/fx-sheets/after2/`, clamp0 les deux) :
`a-S01451-t0.4.png` vs `b-S01451-t0.4-rep.png`, `a-S02051-t0.6.png` vs
`b-S02051-t0.6-rep.png` — visuellement identiques aux instants capturés
(le bord des textures est alpha=0, la couture repeat est invisible). Le
correctif reste protecteur pour les UV qui débordent (défilement,
demi-texel de filtre en bord de quad).

## Cause racine v3 — alpha non prémultiplié dans les mips

`Image.GenerateMipmaps` (box) moyenne le RGB des texels alpha=0 (souvent
noir) dans les bords → halo/liseré sombre autour des sprites alpha-blendés.
Correctif : `MipmapsPremult` (NativeFx) génère la chaîne de mips en alpha
prémultiplié puis divise par alpha — applique aux textures **embarquées**
uniquement (les .dds externes gardent leurs mips d'origine, authentiques ;
leur bord non prémultiplié fait partie du fichier).

## Repli propre

`tex.Base` présent mais texture non résolue (fichier absent, pixels
illisibles) → `use_tex=false` explicite (avant : sampler non lié = blanc).

## Captures de régression lues (2026-11-03, `artifacts/fx-sheets/after2/`)

| capture | contenu lu |
|---|---|
| `a-s113411-t0.6.png` | sphère/disque additif, sans cadre |
| `a-s53921-t0.6.png` | ailes/plumes (embarqué fmt6, mips=True) |
| `a-S01451-t0.4.png`, `-t1.2.png` | anneau vu en biais, fondu — clamp0, particles=35 |
| `a-S02051-t0.6.png` | jets colorés + bannière, particles=270 |
| `a-S00141-t0.35.png` | motes en fin de vie, particles=16 |
| `a-s81111-t0.8.png` | flamme rose swirl (embarqué fmt5/6) |
| `a-s82211-t0.8.png` | cercle de runes bleu (embarqué fmt4/5/6, fmt4 couvert) |

Aucune capture ne montre de cadre de quad ni de liseré de feuille.

## Non vérifié / limites

- **S00141 « particles=0 » réfuté** : `tools/fx-audit --sim` (ParticleSim
  Core pur) donne 40 particules à t=0,1 et 31 à 0,35 ; la capture t=0,35
  en affiche 16. L'émetteur est un burst court : `NiPSysEmitterCtlr`
  birthrate 450 (rest value, pas de clés), visibilité `0=True;0,13=False`
  → fenêtre d'émission [0, 0,13 s], lifespan 0,3±0,33. Les captures
  précédentes à 0 particules dataient d'un état antérieur.
- Courbes : `KfFloatChan.Eval` interpole (Linear lerp, Quadratic/TBC
  Hermite, Const = palier voulu par la donnée). `NiFlipController` reste
  discret = comportement natif (pas d'interpolation entre cadres).
- `NifMaterials.cs` (StandardMaterial3D, géométrie non-FX) : ClampMode
  non honoré (hors scope de ce chantier).
- UI : bordures d'atlas couvertes centralement —
  `UiTextureCache.GetRegion` (CeguiToGodot.cs:209, `FilterClip=true`)
  couvre tous les appelants `Region()`/`Tex.GetRegion` ; `ItemTooltip`
  et `LoadingScreen` (mascot) ont leur `FilterClip` propre. Reste hors
  scope : `EdenCursors.cs` (~l.128, AtlasTexture directe sans FilterClip
  — saignement possible du voisinage à échelle fractionnaire) et les
  `TextureRect.RegionRect` (ex. DamageFloaters) — même classe de risque.
  `NativeCursor` fait une copie de pixels (`Image.GetRegion`) → immunisé.
  Captures témoins lues : `artifacts/ui-border/zoom-mascot-0.9.png`,
  `zoom-bar-0.3.png`, `loading-after-*.png` — bords propres.
- Incohérence UV tranchée (verified, 2026-11-04, r253) : `DemoTree.Build`
  inverse le V (`1f - p.Y`), `NifScene` copie les UV NIF bruts. Le flag
  `NativeFxOptions.InvertV` (compensation `-Tv` dans `MatState.Apply`)
  était à `true` par défaut — calibré sur DemoTree — alors que les chemins
  production (NifScene : montures, persos, mobs, SpellFx) ont le V brut.
  Résultat : scroll V inversé partout (flammes r253 pointe→base au lieu de
  base→pointe ; vérifié par données : V=0 à la pointe des rubans, `+Tv`
  déplace le motif vers V=0). Corrigé : `InvertV=false` par défaut, `true`
  uniquement dans NativeFxDemo. Confirmé visuellement in-game. Reste à
  confirmer sur un asset asymétrique si les textures statiques des deux
  chemins diffèrent en miroir vertical.
- `xform-unresolved:NiPathInterpolator` (S01451, S01151) : B-spline /
  chemin non décodé — note existante, quelques drivers absents.
