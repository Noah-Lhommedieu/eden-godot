#!/usr/bin/env bash
# Banc monture+cavalier sur fond neutre (scène ride_lab.tscn), touches simulées.
# Usage : tools/ride-lab.sh <sortie-dir> <args RideLab…>
# Ex.   : tools/ride-lab.sh artifacts/ride-lab/a --rides=14010 --script=STAND:1,W:1.2,A:1.2 --cam=90,10,4
#         tools/ride-lab.sh artifacts/mount-sweep2 --rides-file=docs/reports/FIX-F-montures-hors-archive.tsv --sweep=artifacts/mount-sweep2/results.tsv --script=STAND:0.4,W:0.8,J:0.3
set -euo pipefail
source "$(dirname "$0")/env.sh"
out="$(realpath -m "${1:?dossier de sortie requis}")"; shift
mkdir -p "$out"; args=()
for a in "$@"; do
    case "$a" in --rides-file=*|--sweep=*) a="${a%%=*}=$(realpath -m "${a#*=}")";; esac
    args+=("$a")
done
cd "$PROJECT_DIR"
env DISPLAY="${DISPLAY:-:0}" timeout "${RIDE_LAB_TIMEOUT:-600}" flock "$GODOT_LOCK" "$GODOT_BIN" --path . \
    res://scenes/ride_lab.tscn --resolution 960x540 -- --shots="$out" "${args[@]}" > "$out/run.log" 2>&1 || true
grep -E '^\[ride-lab\]' "$out/run.log" || eden_die "pas de sortie — voir $out/run.log"
