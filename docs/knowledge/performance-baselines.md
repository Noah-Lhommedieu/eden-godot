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
