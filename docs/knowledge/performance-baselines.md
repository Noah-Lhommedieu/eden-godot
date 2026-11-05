# Baselines de performance

Protocole : `tools/benchmark.sh <map> 3 35`. Mode solo, `--nohud`,
1280x720, VSync coupée (`gamedata/settings.cfg`), régime établi à
t ≥ 15 s, 3 runs. Les centiles portent sur les moyennes par seconde. GPU =
`--gpu-profile` (2e moitié des échantillons). Machine : i5-12450H, RTX 4060
Laptop (pilote 595.99.02), Nobara KDE Wayland via XWayland.

## S001 (Aven) — 2026-11-02 — statut **verified**

| | Godot 4.7.2 (portage, corpus official) | Godot 4.8-beta2 (eden-godot, EDENFR) | Écart |
|---|---|---|---|
| frame P50 | 18,80 ms (53 fps) | **12,00 ms (83 fps)** | **−36,2 %** |
| frame P95 | 20,50 ms | **12,80 ms** | −37,6 % |
| GPU P50 | 5,14 ms | **3,70 ms** | −28,0 % |
| RSS P50 | 1 980 Mo | 2 064 Mo | +4,3 % |
| draws / prims / nœuds | 1561 / 1 355 527 / 7631 | 1561 / 1 355 527 / 7629 | scène identique |

Dossiers : `artifacts/benchmarks/20261102-baseline-4.7.2-portage-S001/`,
`artifacts/benchmarks/20261102-011202-S001/`.

Lecture :
- La scène rendue est la même (draws et prims identiques ; à 2 nœuds
  près, des entités). L'écart vient donc du moteur, pas du contenu
  EDENFR.
- Les deux versions sont **limitées par le CPU** : en 4.8, 12 ms de frame
  pour 3,7 ms de GPU. La marge suivante est côté scripts et SceneTree
  (skill `godot-rendering-performance`).
- Le `proc=` de PerfProbe (> frame) n'est pas fiable : la sonde parcourt
  la scène chaque seconde.
- N = 3 runs, une seule machine, une seule scène. Avant d'étendre la
  conclusion, ajouter S002 et une scène de combat avec HUD.

## Freeze post-kill (loot 274) — 2026-11-03 — statut **verified**

Scénario : `tools/run.sh -- --login=test123:test123 --channel --char
--kill-loop=6,79` (serveur natif, drop 100 %). La fenêtre « post-kill »
agrège les 2 s suivant chaque mort.

Cause mesurée : un NCID 274 déclenchait **deux** `PlayerBag.RaiseChanged`
(bind `HudExtensions` + bind `InventoryWindow`), chacun notifiant 7-8
abonnés ; les fenêtres cachées reconstruisaient quand même leur grille
(`QueueFree` + cellules + textures + `ItemTooltip.Bind`), et le premier
loot payait le parse paresseux `c_item.ini`/`c_itemmall.ini` (~9 Mo,
~1 s).

Correctifs (`PlayerBag.cs`, `InventoryWindow.cs`, `Bank/Shop/Vending/
FamilyStorage/Neobank/ArchiveWindow.cs`, hook `SlotsChanged` de
`QuestTracker.cs`) :

- `RaiseChanged` coalescé à une levée `Changed` par frame (CallDeferred ;
  synchrone hors moteur pour NetTests) ;
- repaint ignoré fenêtre cachée : les abonnés gardent leurs dicts à jour
  (`PlayerBag.Items` relu) mais ne reconstruisent pas les cellules ; la
  grille est repeinte à la réouverture (`VisibilityChanged` +
  `_repaintPending`) ;
- parse `c_item` pré-chauffé en `Task.Run` au montage du HUD (équivalent
  de `DataWarmup`, hors frame de loot).

Mesures (même machine, même scénario, run propre — sans build/test en
parallèle) :

| | pire image par kill | fenêtre post-kill |
|---|---|---|
| Avant | 150, 129, 140, 131, 84 ms (5 kills) | p99 ≈ 84-150 ms |
| Après | 19, 22, 21, 14, 15, 26 ms (6 kills) | n=1340, p50=8,5, p95=11,1, **p99=14,3**, max=25,7 ms |

Limites :

- Un run intermédiaire sous charge (builds/tests parallèles) affichait
  p99=34 ms / max=119 ms — bruit de machine, pas du scénario ;
- kill #1 de ce même run chargeait encore un proto natif paresseux
  (`g00004.nif`, FX de mort — hors scope, côté `scripts/native`/fx) ;
- `TradeWindow` reste abonné à `Changed` sans garde de visibilité
  (hors périmètre de la tâche ; coalescing le borne à 1×/frame).

## Lag kill mobs — 2e vague (drains réseau, GLB froids, callbacks fantômes) — 2026-11-04 — statut **verified**

Après le fix loot-274 ci-dessus, il restait des pics. Scénario :
`tools/run.sh -- --login=test123:test123 --channel --char --hitch --perf
"--kill-loop=20863,20864,20865"` (S005, serveur C# live).

Causes mesurées et correctifs :

1. **Drains réseau non bornés.** `GameScene._Process` vidait toute la
   file `_pending` en une frame (dispatch maître : SeeMob, inventaire,
   trésors, HUD) — pic `proc=1204 ms` à l'entrée de zone (263 entités ×
   ~4 ms de création). Idem `_deferredDuringMigration` (drain inline en
   fin de migration) et `EntityRegistry._netPending` (burst kill :
   `reg.net 37,7 ms`). → budgets 8 ms (`_pending` + reliquat migration,
   priorité au reliquat pour préserver l'ordre 298→326) et 6 ms
   (`reg.net`) ; scope `gs.pending` ajouté pour l'attribution.
2. **GLB mobs à froid.** Chaque nouveau stem payait 18-37 ms de parse
   dans `SetVisual` en pleine frame (`reg.visual`). → warm asynchrone
   (`s_mobWarm` : proto GLB + texture skin + .kf via `PreloadAnimFiles`,
   thread-safe) lancé dès le 298 ; `DrainPendingVisuals` repousse les
   stems froids (filet 1,5 s). `MapLoader` précharge les 10 familles
   G00001-10 en plus des NPC.
3. **256 callbacks fantômes.** `e.Interp.SetProcess(false)` était posé
   **avant** l'entrée dans l'arbre et ne tenait pas : `proc-dump`
   comptait `NetworkInterpolator×256` en process (~256 callbacks
   natifs→C#/frame en plus du tick mutualisé). → appel déplacé après
   `AddChild(e)` ; proc-dump suivant : `NetworkInterpolator` absent.

Mesures (même machine, même scénario kill-loop) :

| | pire image par kill | entrée de zone |
|---|---|---|
| Avant | 11-41 ms (15 kills) | `proc=1204 ms` en une frame |
| Après | 8-26 ms (17 kills), p50 fenêtre ~6 ms | pic ~140 ms étalé, plus de seconde entière figée |

Restes mesurés (hors fenêtre kill — pistes pour plus tard) :

- actions unitaires du burst login ~40-100 ms (1re construction
  inventaire 47 items / 214 sorts / fenêtres) — un budget de drain ne
  peut pas couper une action, il faut étaler la construction elle-même ;
- `CooldownOverlay×450` en process (pull `Source?.Invoke`/frame sur
  toutes les cellules) ;
- `reg.tick` ~11 ms occasionnel (probable GC, non corrélé au nombre
  d'entités) ;
- trésors au sol : TTL serveur 300 s (270 proprio + 30 public) → en
  grind sans ramassage, draws/labels/nœuds croissent en continu
  (mesuré +580 draws / +230 Label3D / +2800 nœuds sur 4 min).
