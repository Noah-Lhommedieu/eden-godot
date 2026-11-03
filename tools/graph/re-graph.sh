#!/usr/bin/env bash
# Graphe du code décompilé (re/ghidra/*/decompiled.c) : miroir en liens
# physiques dans tools/graph/re-src (re/ reste intouché), graphify AST,
# puis import reagraph. Lent (~200 Mo de C) : lancer en arrière-plan.
# Nécessite reagraph dans le venv (Graphify-Labs/REAgraph, pas sur PyPI).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
V="$ROOT/tools/graph/.venv/bin"
[ -x "$V/reagraph" ] || { echo "reagraph absent du venv — voir tools/graph/README.md" >&2; exit 1; }
D="$ROOT/tools/graph/re-src"
mkdir -p "$D"
for f in "$ROOT"/re/ghidra/*/decompiled.c; do
  n="$(basename "$(dirname "$f")")"
  [ -e "$D/$n.c" ] || ln "$f" "$D/$n.c" 2>/dev/null || cp "$f" "$D/$n.c"
done
"$V/graphify" update "$D"
"$V/reagraph" add "$D/graphify-out/graph.json" --graph "$ROOT/tools/graph/reagraph.json"
