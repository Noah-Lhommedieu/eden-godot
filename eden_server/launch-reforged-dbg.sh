#!/usr/bin/env bash
# Version debug : lance game_.exe sous winedbg en mode --auto
# (backtrace+registres imprimés sur la 1re exception non gérée).
# Mêmes chemins que launch-reforged.sh (dépôt local + préfixe local) ;
# surcharge du runner possible via EDEN_WINE_HOME=/chemin/bin.
set -euo pipefail
EDEN_ROOT="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="$EDEN_ROOT/reforged/EdenReforged"
PREFIX="$EDEN_ROOT/reforged/prefix"
WINE_BIN="${EDEN_WINE_HOME:-$HOME/.local/share/lutris/runners/wine/wine-ge-8-26-x86_64/bin}"
cd "$GAME_DIR"
exec env WINEPREFIX="$PREFIX" WINEDEBUG=-all "$WINE_BIN/winedbg" --auto game_.exe LogLog 2>&1 | tee /tmp/winedbg-crash.log
