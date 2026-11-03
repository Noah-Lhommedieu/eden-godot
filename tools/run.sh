#!/usr/bin/env bash
# Lance le client (fenêtré) sur le moteur épinglé. Arguments après `--`
# transmis au jeu. Ex. : tools/run.sh -- --map=S001 --solo
#                        tools/run.sh -- --login=test123:test123 --channel --char
set -euo pipefail
source "$(dirname "$0")/env.sh"
[ "${1:-}" = "--" ] && shift
cd "$PROJECT_DIR"
# --login=/--channel/--char = pilote des écrans (ScreenManager) : il
# faut boot.tscn (scène main), pas game.tscn qui ne lit que --live…
SCENE="res://scenes/game.tscn"
for a in "$@"; do
    case "$a" in --login=*|--channel|--char|--char=*) SCENE="res://scenes/boot.tscn";; esac
done
exec env DISPLAY="${DISPLAY:-:0}" flock "$GODOT_LOCK" "$GODOT_BIN" --path . \
    "$SCENE" --resolution 1280x720 -- "$@"
