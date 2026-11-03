#!/usr/bin/env bash
# Capture in-engine (rendu GPU réel) d'une map en solo.
# Usage : tools/capture.sh S001 [nom] [args jeu supplémentaires…]
# Sortie : artifacts/shots/<ts>-<map>[-nom].png — comparer à ref-shots/.
# ⚠ La capture part après ~240 frames : l'eau animée peut manquer selon
#   le timing (observé 2026-11-02) — refaire une capture avant de conclure.
set -euo pipefail
source "$(dirname "$0")/env.sh"
map="${1:?map requise (ex. S001)}"; name="${2:-}"; shift $(( $# >= 2 ? 2 : 1 ))
out="$ARTIFACTS/shots/$(eden_ts)-$map${name:+-$name}.png"
mkdir -p "$(dirname "$out")"
cd "$PROJECT_DIR"
env DISPLAY="${DISPLAY:-:0}" timeout 180 flock "$GODOT_LOCK" "$GODOT_BIN" --path . \
    res://scenes/game.tscn --resolution 1280x720 -- \
    --map="$map" --solo --shot="$out" "$@" > "${out%.png}.log" 2>&1 || true
[ -s "$out" ] || eden_die "pas de capture — voir ${out%.png}.log"
echo "$out"
