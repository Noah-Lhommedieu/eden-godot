# tools/graph — graphes de code pour agents (économie de tokens)

Venv local `tools/graph/.venv/` (non versionné) :

- `graphifyy` (PyPI) — installé le 2026-11-02 ;
- `reagraph` 0.1.0 (source `Graphify-Labs/REAgraph`, absent de PyPI) —
  installé le 2026-11-02 sur accord du propriétaire (cœur stdlib, MIT) :
  `git clone https://github.com/Graphify-Labs/REAgraph <dir> &&
  tools/graph/.venv/bin/pip install "<dir>[live]"`. Pas de `query` sans
  graphify dans le PATH : utiliser `graphify query … --graph tools/graph/reagraph.json`.
- `knowledge/` : `graphify update knowledge` (AST/Markdown, sans LLM),
  sortie `knowledge/graphify-out/` (2 139 nœuds). `knowledge/raw` (HTML) exclu
  par `knowledge/.graphifyignore`.

| Graphe | Commande | Sortie |
|---|---|---|
| eden-godot | `tools/graph/graph.sh` | `graphify-out/` (47 745 nœuds au 2026-11-02) |
| portage 4.7 (miroir en liens physiques, lecture seule) | `tools/graph/graph.sh portage` | `tools/graph/portage/src/graphify-out/` (38 604 nœuds) |
| Décompilé `re/ghidra` | `tools/graph/re-graph.sh` | `tools/graph/re-src/graphify-out/`, `tools/graph/reagraph.json` (68 749 nœuds, 316 014 arêtes) |

Mode d'emploi : skill `.agents/skills/code-graph`. Exclusions d'eden-godot :
`.graphifyignore`.
