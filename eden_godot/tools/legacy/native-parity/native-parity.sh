#!/usr/bin/env bash
# B3 — banc de parité GLB (ancien chemin) vs arbre natif (NifFile → NifScene), headless.
# Usage : tools/native-parity/native-parity.sh [liste.txt|rel] [--dump]
#   liste : un rel sans extension par ligne (ex. map/model/object/UE013). Défaut : échantillon
#   de 200 modèles (tools/native-parity/sample200.txt, régénéré par `gen`).
#   tools/native-parity/native-parity.sh gen   → réécrit sample200.txt (déterministe)
# Sortie : artifacts/parity/<ts>/report.tsv + summary.txt
set -euo pipefail
source "$(dirname "$0")/../env.sh"
here="$EDEN_ROOT/tools/native-parity"
if [ "${1:-}" = "gen" ]; then
    python3 -I "$here/gen_sample.py" "$EDEN_ROOT" > "$here/sample200.txt"; wc -l "$here/sample200.txt"; exit 0
fi
list="$(realpath "${1:-$here/sample200.txt}")"; shift $(( $# >= 1 ? 1 : 0 )) || true
out="$ARTIFACTS/parity/$(eden_ts)"; mkdir -p "$out"
cd "$PROJECT_DIR"
timeout 900 flock "$GODOT_LOCK" "$GODOT_BIN" --headless --path . -- \
    --native-parity="$list,$out" "$@" > "$out/run.log" 2>&1 || true
[ -s "$out/report.tsv" ] || eden_die "pas de rapport — voir $out/run.log"
python3 -I "$here/summarize.py" "$out/report.tsv" | tee "$out/summary.txt"
echo "$out"
