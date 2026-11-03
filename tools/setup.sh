#!/usr/bin/env bash
# Vérifie l'environnement (versions épinglées, corpus, liens) — ne modifie rien.
# Code de sortie non nul si un prérequis manque.
set -euo pipefail
source "$(dirname "$0")/env.sh"

fail=0
check() { if eval "$2" >/dev/null 2>&1; then echo "ok   $1"; else echo "KO   $1"; fail=1; fi; }

gv="$("$GODOT_BIN" --version 2>/dev/null || true)"
dv="$(dotnet --version 2>/dev/null || true)"
echo "godot  : ${gv:-absent}   (attendu $GODOT_EXPECTED*)"
echo "dotnet : ${dv:-absent}   (attendu $DOTNET_EXPECTED*)"
check "moteur épinglé"            "[[ '$gv' == $GODOT_EXPECTED* ]]"
check ".NET SDK"                  "[[ '$dv' == $DOTNET_EXPECTED* ]]"
check "templates d'export"        "[ -f ~/.local/share/godot/export_templates/${GODOT_EXPECTED}/version.txt ]"
check "corpus fr-full"            "[ -d '$CORPUS_FR/data/db' ]"
check "raw_assets -> fr-full"     "[ \"\$(readlink -f '$CLIENT_DIR/raw_assets')\" = \"\$(readlink -f '$CORPUS_FR')\" ]"
check "assets dérivés"            "[ -d '$PROJECT_DIR/assets/models' ]"
check "pas de corpus/official"    "[ ! -e '$EDEN_ROOT/corpus/official' ]"
check "docker (PG17/Redis)"       "docker ps --format '{{.Names}}' | grep -q edengodot-postgres"
exit $fail
