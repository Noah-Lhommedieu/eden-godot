---
name: godot-rendering-performance
description: Profiler et optimiser le rendu ou le CPU du client Eden sous Godot 4.8 (frame time, draw calls, SceneTree, GPU). À utiliser pour toute tâche « fps », « lag », « optimisation », « draw calls », « RenderingServer », « MultiMesh », « LOD », « occlusion ».
---

# Performance rendu/CPU — Godot 4.8

## Règle
Profiler, puis formuler une hypothèse, faire **un** changement, remesurer
sur le même scénario et comparer. Un gain qui dégrade le rendu est refusé.

## Étapes
1. Baseline : `tools/benchmark.sh S001 3 35` (solo, sans HUD, VSync coupée,
   1280x720). Lire `summary.env` : `frame_p50_ms`, `frame_p95_ms`,
   `gpu_p50_ms`, `draws`, `nodes`.
2. Séparer CPU et GPU : si `gpu_p50_ms` est bien en dessous de
   `frame_p50_ms`, le goulot est CPU (scripts, SceneTree, soumission).
   Baseline 4.8 sur S001 : frame 12,0 ms pour un GPU de 3,7 ms, donc
   **CPU-bound** (`docs/knowledge/performance-baselines.md`).
3. CPU : chercher les `_Process` coûteux (`EntityAnimator`×27, `Label3D`×206,
   `PerfProbe` lui-même chaque seconde), les allocations par frame et les
   conversions Variant. Ventiler avant de toucher au code.
4. GPU : le détail par passe est dans `run*.log` (`GPU PROFILE`).
   RenderDoc ou Nsight si une passe domine.
5. Après : `tools/benchmark.sh` puis
   `tools/compare.sh <avant> <après>`, et `tools/capture.sh S001` pour
   vérifier le rendu. Résultat dans `performance-baselines.md`.

## Leviers (non mesurés, par gain/effort estimé)
Remplacer les `Label3D` ; LOD d'animation (tick réduit hors écran) ;
occlusion (`OccluderInstance3D`) ; étendre le PropBatcher (multi-matériaux) ;
chunks de terrain 4×4 ; cache de pipelines de shaders ; thread de rendu
dédié ; `RenderingServer` direct **seulement** si le profil montre que le
SceneTree est le goulot. `RenderingServer` n'aide pas un goulot GPU.

## Références
- docs.godotengine.org : `tutorials/performance/` (general, cpu, gpu,
  using_servers), `tutorials/3d/occlusion_culling`, `mesh_lod`,
  `visibility_ranges`, `pipeline_compilations`. Vérifier contre la 4.8.
