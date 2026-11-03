# CLAUDE.md

Démarrage rapide. Les règles complètes sont dans **`AGENTS.md`** (à lire
d'abord), le plan dans `docs/architecture/MASTER-PLAN.md`.

- Client Eden Eternal **EDENFR pur** sur **Godot 4.8-beta2 .NET**
  (`engine/current/godot`, jamais le `godot` du PATH).
- Données : `corpus/fr-full` uniquement. `portage/`, `corpus/` et `re/`
  sont en lecture seule. `godot_project/assets/` est en liens physiques :
  délier avant d'écrire.
- Preuves : `tools/test.sh`, `tools/capture.sh`, `tools/benchmark.sh`.
  « Fait » sans preuve = non fait.
- Skills du dépôt : `.claude/skills/` (lien vers `.agents/skills/`), dont
  `ponytail` (méthode) et `code-graph`.
- Se repérer avec les graphes avant un grep : `tools/graph/graph.sh
  query "…"` (eden-godot), `tools/graph/graph.sh portage query "…"`
  (portage 4.7, inclut le catalogue ref-shots), `re/reindex`
  pour le décompilé, `--graph knowledge/graphify-out/graph.json` pour le
  wiki. Régénérer en fin de tâche.
- Gameplay/données : `knowledge/README.md` dit où chercher quoi (wikis,
  tables `data/db/*.ini`, `ref-shots/` + spec
  `eden_godot/docs/client-assets/ref-shots-catalog.md`, synthèses
  `docs/reference/harvest/`).
- Ne jamais charger : `corpus/`, `re/`, `knowledge/` en entier,
  `docs/legacy/`, `eden_godot/docs/archive/`, `artifacts/`.
- Commits/push : pas de git pour l'instant (décision du propriétaire).
