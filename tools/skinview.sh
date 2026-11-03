#!/usr/bin/env bash
# Visionneuse de skins (race/genre, classe, coiffure, couleurs). Voir docs/tools/skin-viewer.md.
# Interactif : tools/skinview.sh
# Capture    : tools/skinview.sh --body=1 --class=1 --shot=/tmp/k.png --no-ui
set -euo pipefail
source "$(dirname "$0")/env.sh"
[ "${1:-}" = "--" ] && shift
cd "$PROJECT_DIR"
exec env DISPLAY="${DISPLAY:-:0}" flock "$GODOT_LOCK" "$GODOT_BIN" --path . \
    res://scenes/skin_viewer.tscn --resolution 1280x720 -- "$@"
