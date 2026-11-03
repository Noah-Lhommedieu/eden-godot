#!/usr/bin/env bash
# Versions et chemins épinglés — source unique de tous les scripts tools/.
# À sourcer : `source "$(dirname "$0")/env.sh"`. Ne rien coder en dur ailleurs.

EDEN_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export EDEN_ROOT

# Moteur : engine/current -> engine/<version> (tools/engine-update.sh).
export GODOT_BIN="$EDEN_ROOT/engine/current/godot"
export GODOT_EXPECTED="4.8.beta2.mono"          # préfixe de `godot --version`
export DOTNET_EXPECTED="10.0"                   # préfixe de `dotnet --version`

# Projets.
export CLIENT_DIR="$EDEN_ROOT/eden_godot"
export PROJECT_DIR="$CLIENT_DIR/godot_project"
export CLIENT_CSPROJ="$PROJECT_DIR/EdenClientReborn.csproj"
export NETTESTS_DIR="$CLIENT_DIR/tools/NetTests"
export CORE_TESTS_DIR="$EDEN_ROOT/tests/Eden.Core.Tests"
export SERVER_SLN="$EDEN_ROOT/eden_server/server/Eden.sln"

# Données : EDENFR pur uniquement (ADR-0002). Lecture seule.
export CORPUS_FR="$EDEN_ROOT/corpus/fr-full"

# Sorties générées (jamais dans les sources).
export ARTIFACTS="$EDEN_ROOT/artifacts"

# Un seul client graphique à la fois.
export GODOT_LOCK="/tmp/godot.lock"

# Les NetTests lancent le moteur épinglé, pas le `godot` du PATH.
export EDEN_GODOT_BIN="$GODOT_BIN"

eden_ts() { date +%Y%m%d-%H%M%S; }
eden_log() { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*" >&2; }
eden_die() { eden_log "ERREUR: $*"; exit 1; }

# Les démons dotnet (nœuds MSBuild, VBCSCompiler) héritent du fd de `flock` et
# gardent le verrou de build après la fin du build : on les désactive.
export MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false DOTNET_CLI_USE_MSBUILD_SERVER=0
