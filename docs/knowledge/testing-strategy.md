# Stratégie de test

Statut : **verified** — 2026-11-02.

| Niveau | Outil | État |
|---|---|---|
| Unitaires client (code pur de `scripts/`) | `eden_godot/tools/NetTests` (xUnit) | 711 verts, 3 skip (EN, `C_DailyAward`, layout Reforged) |
| Smoke moteur | `ZoneLotGTests.Smoke_GameScene_Headless` (S002 headless sur `EDEN_GODOT_BIN`) | vert sur 4.8 |
| Core (formats natifs, VFS, UI neutre) | `tests/Eden.Core.Tests` | 890 verts |
| Serveur | `eden_server/server` (`dotnet test Eden.sln`) | 1429 verts |
| Provenance assets | `tools/validate-assets.sh` | 43 594 dérivés, tous sourcés fr-full |
| Visuel | `tools/capture.sh` + comparaison `ref-shots/` (105) | manuel ; pas de diff automatique |
| Performance | `tools/benchmark.sh` + `tools/compare.sh` | baselines dans `performance-baselines.md` |
| Unitaires Godot (Nodes, scènes) | GdUnit4Net | **non installé** (compatibilité 4.8-beta2 à vérifier) |
| Stabilité longue | session ≥ 1 h | **à faire** |

`tools/test.sh` enchaîne les trois suites .NET. Ordre recommandé avant une
livraison : `setup`, `build`, `test`, `validate-assets`, `capture`, puis
`benchmark` si la perf est touchée.
