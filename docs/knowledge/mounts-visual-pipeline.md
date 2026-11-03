# Montures — pipeline visuel, combat, vitesse (EDENFR)

Version testée : Godot 4.8.beta2.mono, client `eden_godot`, corpus
`corpus/fr-full` uniquement. Date : 2026-11-03. Graphe + tests
NetTests/Eden.Core à jour au moment de la rédaction.

## Statut par point

| Point | Statut | Preuve |
|---|---|---|
| Modèles/stem : `rideKey/1000` → `r{key:000}` via `RideList.Stem`, repli natif `param_2/1000` (`FUN_00773d90`, client_game) | verified | `CharVisual.RideStem` ; NetTests `RideTable*` 55 verts |
| Attache cavalier = nœud `L17` animé (natif `CRide::Attach`, rf_game `FUN_0073fc60`) + RideSeat orthonormé + repli RideFit si L17 absent/aberrant | verified | `eden_godot/docs/MONTURES-L17.md` ; `RideSeatMathTests` |
| Textures manquantes = surfaces référençant `nif_tex` sans albedo ; meshes cachés/aides sans texture ≠ trou de données. **Analyse du 2026-11-03 : les 29 FAIL du sweep ont TOUS `notex>0`, aucun ne manque `stand01`** — faux positifs de l'ancien critère (toute surface sans AlbedoTexture, ex. 21 surfaces sur r253) | verified (code + tsv) | `RideLab.Finish` — `notex` ne compte que les références non résolues (`nif_tex` meta copiée par `ToonMaterializer` :414,515), `untx` = info |
| `move01` non requis pour le statut OK (ex. r711 « Tour à Canon » sans marche, natif) | verified (code) | `RideLab.Finish`, note `noMove` |
| Anims directionnelles montées : A→`move02`, D→`move03`, pivots ±90° (`DirectionalLocomotion`, `MountSideAnims`) | verified (tests) | `DirectionalLocomotionTests` 53 verts ; captures à refaire |
| Vitesse d'anim : monté 100 %→×1.0, 150 %→×1.5, pied 20 %→×1.0, bornes 1–3 | verified | `MoveAnimSpeedTests` (InlineData 100/150/200/35/900, 0/20/30/40) |
| Vitesse distante : dérivée du déplacement snapshot (`EntityRegistry.ObserveSpeed`, base pied 6 u/s, lissage 0.4) — pas de champ protocole ajouté (pas de preuve native) | verified (code) | `ServerMoveSpeedTests` |
| Combat monté : routage `attack01-04`, `magic01/09/19/20`, `shoot*` (natif `FUN_00778660`/`FUN_007785d0`) ; `magic03`/`magic02` NON routés nativement → repli pose cavalier | verified (tests + désasm) | `MountCombatRules.RoutedAction` + NetTests `MountRulesTests` |
| Stats monture combat : enchant 11354 → `2134|253020||1|2` (type 2 = combat), `2051` vitesse native 90 → tiers 90 ; pipeline retire 2051 (`RideSpeedRules.PipelineCommands`) pour éviter le double comptage | verified (données + tests) | `c_enchant.ini` 11354 ; `RideSpeedRulesTests` |
| Badge épée « monture de combat » sur l'icône d'item : `AttackIcon` de `uidata/imagesets/main.imageset` (177,288,18×18), affiché si `UseEnchantId` → 2134 type 2 (`MountCombatRules.IsCombatRide`) | implemented | `InventoryWindow.ItemCell` — capture HUD à faire |
| FX monture (nuage noir R260–262, auras couleur par rareté) | hypothesis | NativeFxHost compté par RideLab ; validation visuelle en attente |
| Sweep complet ~90 montures hors archive | en cours | `artifacts/mount-sweep-lab/results.tsv` — critères corrigés, re-run bloqué par le godot lock (agents parallèles) |

## Points non prouvés / limites

- Captures RideLab (A/D sens, WA/WD, jump, ATK/CAST/CAST3, placement
  L17, FX) : non refaites à cette date — le godot lock était pris en
  continu par d'autres agents.
- Logo épée : implémenté sur `ItemCell` (inventaire) ; non vérifié en
  capture. L'usage natif exact (coin de l'icône ? icône de buff ?) reste
  hypothesis — la source d'image (`AttackIcon`, atlas main) est
  verified.
- `RideTypeOf` lit var3 du 2134 ; valeurs ≠ 2 possibles dans le corpus
  (sièges var2) — audit `c_enchant` non exhaustif.
