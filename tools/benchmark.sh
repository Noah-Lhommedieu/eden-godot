#!/usr/bin/env bash
# Bench reproductible d'une map (solo, sans HUD, VSync coupée par
# gamedata/settings.cfg, 1280x720) — N runs, profil GPU inclus.
# Usage : tools/benchmark.sh S001 [runs=3] [secondes=35] [args jeu…]
# Sortie : artifacts/benchmarks/<ts>-<map>/{run*.perf,run*.log,summary.env}
set -euo pipefail
source "$(dirname "$0")/env.sh"
map="${1:?map requise (ex. S001)}"; runs="${2:-3}"; secs="${3:-35}"
shift $(( $# >= 3 ? 3 : $# ))
out="$ARTIFACTS/benchmarks/$(eden_ts)-$map"; mkdir -p "$out"
cd "$PROJECT_DIR"
files=()
for i in $(seq 1 "$runs"); do
    eden_log "run $i/$runs ($secs s)"
    env DISPLAY="${DISPLAY:-:0}" timeout $((secs + 90)) flock "$GODOT_LOCK" \
        "$GODOT_BIN" --gpu-profile --path . res://scenes/game.tscn \
        --resolution 1280x720 -- --map="$map" --solo --nohud --perf \
        --perf-seconds="$secs" --perf-out="$out/run$i.perf" "$@" \
        > "$out/run$i.log" 2>&1 || eden_log "run $i : code non nul (voir run$i.log)"
    files+=("$out/run$i.perf" "$out/run$i.log")
done
{
    echo "map=$map"
    echo "date=$(date -Iseconds)"
    echo "godot=$("$GODOT_BIN" --version)"
    echo "dotnet=$(dotnet --version)"
    echo "gpu=$(nvidia-smi --query-gpu=name,driver_version --format=csv,noheader 2>/dev/null || echo inconnu)"
    echo "cpu=$(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)"
    echo "resolution=1280x720"
    echo "vsync=$(grep -m1 '^vsync=' "$PROJECT_DIR/gamedata/settings.cfg" | cut -d= -f2)"
    python3 -I "$EDEN_ROOT/tools/benchmark/summarize.py" "${files[@]}"
} > "$out/summary.env"
cat "$out/summary.env"
echo "$out"
