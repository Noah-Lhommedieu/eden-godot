#!/usr/bin/env bash
# Capture in-engine d'un effet NIF natif (NativeFx) sur fond neutre.
# Usage : tools/capture-fx.sh effect/model/s10121.nif [nom] [args démo : --t=1.5 --dist=4 --yaw= --pitch= --target=x,y,z --scale=]
# Sortie : artifacts/fx-sheets/<nom>.png (+ .log)
set -euo pipefail
source "$(dirname "$0")/env.sh"
nif="${1:?nif relatif à corpus/fr-full}"; name="${2:-$(basename "${nif%.nif}")}"; shift $(( $# >= 2 ? 2 : 1 ))
out="$ARTIFACTS/fx-sheets/$name.png"
mkdir -p "$(dirname "$out")"
cd "$PROJECT_DIR"
lock=(flock "$GODOT_LOCK"); [ -z "${FX_NOLOCK:-}" ] || lock=()   # FX_NOLOCK=1 : ne pas attendre le verrou (autre client headless)
env DISPLAY="${DISPLAY:-:0}" timeout 120 "${lock[@]}" "$GODOT_BIN" --path . \
    res://scenes/fx_demo.tscn --resolution 960x720 -- \
    --fx-demo="$nif" --shot="$out" "$@" > "${out%.png}.log" 2>&1 || true
[ -s "$out" ] || eden_die "pas de capture — voir ${out%.png}.log"
echo "$out"
