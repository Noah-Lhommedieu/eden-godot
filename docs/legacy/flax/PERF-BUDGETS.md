# Budgets de performance (PLAN §9.6) — M8

Définis dans `Eden.Core/Game/PerfStats.cs` (`PerfBudget.M8`), mesurés par
`Eden.Game/Perf/PerfProbe.cs` (`EDEN_PERF=1`, rapport toutes les
`EDEN_PERF_EVERY` s dans le log `[Eden][perf]`).

| Budget | Valeur | Mesure |
|---|---|---|
| Frame time P95 | ≤ 16,7 ms (60 fps) | `FrameStats.P95` sur fenêtre 600 frames |
| Allocations managées / frame (boucle stable) | ≤ 2 Kio (cible ≈ 0) | `GC.GetAllocatedBytesForCurrentThread` delta |
| Chargement carte (S002) | ≤ 3 000 ms | `[Eden][perf] carte prête` + phases (`PerfProbe.Scope`) |
| RAM (working set) | ≤ 2 048 Mio | `Environment.WorkingSet` |

Outils : `PerfProbe.Scope("nom")` ouvre un événement `ProfilerCPU`
(visible Flax Profiler / Tracy si le build l'active) et alimente
`PhaseTimes` ; `DataWarmup` parse les tables .ini en arrière-plan dès
l'attachement des ponts (régression connue « parse lazy »). Pooling :
`Audio.Sfx` (16 `AudioSource` réutilisées, 0 alloc par son).

Mesures : voir `.claude/completions/2026-11-01-m7-m8-p2-audio-i18n-packaging.md`.
