#!/usr/bin/env bash
# Client packagé natif (phase P/H) : paquets EPK signés/chiffrés (edenpack build --native) + export Godot.
# Usage : tools/package.sh <dossier-sortie> <sign-key> <keyfile> [version]
#   → <sortie>/gamedata/*.epk, <sortie>/EdenLimitless.x86_64 (+ .pck). Le .pck ne contient aucune donnée de jeu.
set -euo pipefail
source "$(dirname "$0")/env.sh"
out="${1:?dossier de sortie}"; key="${2:?sign.key/pem}"; kf="${3:?keyfile}"; ver="${4:-1}"
pack="$EDEN_ROOT/artifacts/packs/.edenpack-bin"
[ -f "$pack/edenpack.dll" ] || dotnet build "$EDEN_ROOT/eden_godot/tools/edenpack" -c Release -o "$pack" -nologo -v q
mkdir -p "$out/gamedata"
dotnet "$pack/edenpack.dll" build --native "$out/gamedata" --sign-key "$key" --keyfile "$kf" --version "$ver"
cd "$PROJECT_DIR"
flock "$GODOT_LOCK" "$GODOT_BIN" --headless --path . --export-release "Linux/X11" "$out/EdenLimitless.x86_64"
eden_log "ok — $out"
