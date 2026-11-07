#!/usr/bin/env bash
# Graphes de code (graphify : AST local, 0 LLM) pour eden-godot ET le
# portage 4.7 (référence lecture seule).
#
#   tools/graph/graph.sh                       # MAJ graphe eden-godot → graphify-out/
#   tools/graph/graph.sh portage               # MAJ graphe portage → tools/graph/portage/graphify-out/
#   tools/graph/graph.sh query "…"             # interroge eden-godot (aussi : path "A" "B", explain "X")
#   tools/graph/graph.sh portage query "…"     # interroge le portage
#
# Le portage n'est jamais modifié : son code est mis en miroir par liens
# physiques dans tools/graph/portage/src (graphify écrit à côté du miroir).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
G="$ROOT/tools/graph/.venv/bin/graphify"
[ -x "$G" ] || { echo "graphify absent : python3 -m venv tools/graph/.venv && tools/graph/.venv/bin/pip install graphifyy" >&2; exit 1; }

if [ "${1:-}" = "portage" ]; then
    shift
    M="$ROOT/tools/graph/portage/src"
    if [ $# -eq 0 ]; then
        # Le dossier ../portage a été supprimé (2026-11-07) : le graphe de référence
        # est figé dans tools/graph/portage ; seules les requêtes restent possibles.
        P="$ROOT/../portage"
        [ -d "$P" ] || { echo "portage supprimé : graphe figé, mise à jour impossible (requêtes OK)" >&2; exit 1; }
        P="$(cd "$P" && pwd)"
        # Miroir : code et docs du client + serveur, sans données ni sorties.
        mkdir -p "$M"
        rsync -a --delete --link-dest="$P/" \
            --exclude='/corpus/' --exclude='/re/' --exclude='/knowledge/' --exclude='.git/' \
            --exclude='assets/' --exclude='dist/' --exclude='.godot/' --exclude='bin/' \
            --exclude='obj/' --exclude='godot-shots/' --exclude='ref-shots/' --exclude='server_us/' \
            --exclude='raw_assets' --exclude='docs/archive/' --exclude='client-assets/' \
            --exclude='graphify-out/' --exclude='.code-review-graph/' \
            --include='*/' --include='*.cs' --include='*.gd' --include='*.gdshader' \
            --include='*.py' --include='*.sh' --include='*.csproj' --include='*.md' \
            --exclude='*' --prune-empty-dirs \
            "$P/" "$M/"
        cd "$M" && exec "$G" update .
    fi
    cd "$M" && exec "$G" "$@" --graph "$M/graphify-out/graph.json"
fi

cd "$ROOT"
[ $# -eq 0 ] && exec "$G" update .
exec "$G" "$@"
