# ADR-0001 — Moteur Godot 4.8-beta2 .NET épinglé

- Statut : **accepté (expérimental)** — 2026-11-02
- Contexte : le portage 4.7.2 (`../portage/`) est le client de référence.
  eden-godot sert à tester la 4.8-beta2 (perf, stabilité, coût des mises à
  jour). Le build .NET de la 4.8 exige .NET 10.

## Décision

- Binaire : `engine/Godot_v4.8-beta2_mono_linux_x86_64/`, actif via le lien
  `engine/current` (`4.8.beta2.mono.official.cafc17a19`).
- SDK C# : `Godot.NET.Sdk/4.8.0-beta.2`, `net10.0`. La source NuGet
  `godot-local` pointe sur `engine/current/GodotSharp/Tools/nupkgs`, ce qui
  permet de travailler hors ligne avec un SDK identique au binaire.
- Templates d'export : `~/.local/share/godot/export_templates/4.8.beta2.mono/`.
- Aucune mise à jour automatique : un changement de moteur passe uniquement
  par `tools/engine-update.sh`, qui fait l'import, le build et les tests,
  avec un rollback automatique en cas d'échec.
- Le `godot` du PATH (4.7.2) reste réservé à `../portage/`.

## Mesures (voir `knowledge/performance-baselines.md`)

- Migration 4.7.2 → 4.8-beta2 : csproj (Sdk, TFM) et NuGet.config ;
  suppression de `System.Text.Encoding.CodePages` (inclus dans net10,
  NU1510). Aucune ligne de C# à changer pour l'API moteur.
- `tools/engine-update.sh` va du zip à « build et 1601 tests verts » en
  **71 s**.
- S001, même contenu (1561 draws, 1,355 M prims) : 4.8 plus rapide que
  4.7.2 (chiffres dans la note de baselines).

## Conséquences et sortie

- Préversion : régressions possibles. Les addons (GdUnit4, MCP) doivent
  être testés sur ce binaire exact avant adoption.
- Bascule vers 4.8 stable dès sa sortie : `tools/engine-update.sh <zip>`,
  puis `benchmark` et `compare` contre la baseline beta2.
