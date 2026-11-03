#!/usr/bin/env bash
# Banc locomotion monture (touches injectées, 2 captures par pas).
# Usage : tools/ride-script.sh <rideKey> [script] [yaw pitch dist h] [map]
# Ex.   : tools/ride-script.sh 14010 "STAND:1,W:1.4,A:1.4,D:1.4,S:1.4,J:1.2"
# Sortie : artifacts/ride-script/<key>/NN-<touches>-{a,b}.png + run.log
set -euo pipefail
source "$(dirname "$0")/env.sh"
key="${1:?rideKey requis (ex. 14010)}"
spec="${2:-STAND:1,W:1.4,A:1.4,D:1.4,S:1.4,J:1.2}"
cam="${3:-120 12 7 1.6}"; map="${4:-S401}"
out="$ARTIFACTS/ride-script/$key"; mkdir -p "$out"
rm -f "$out"/*.png
echo "$key $cam" > /tmp/ride_key.txt
cd "$PROJECT_DIR"
env DISPLAY="${DISPLAY:-:0}" timeout 120 flock "$GODOT_LOCK" "$GODOT_BIN" --path . \
    res://scenes/game.tscn --resolution 1280x720 -- --map="$map" --solo \
    --ride-script="$spec" --ride-shots="$out" > "$out/run.log" 2>&1 || true
rm -f /tmp/ride_key.txt
grep -E '^\[ride-script\]' "$out/run.log" || eden_die "pas de sortie — voir $out/run.log"
