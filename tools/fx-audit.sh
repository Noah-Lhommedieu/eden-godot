#!/usr/bin/env bash
# Audit textures/clamp des particules NIF (Core pur, headless) :
# source de texture par PSys (externe/embarquée/manquante), blend et clamp,
# fichiers .dds absents de effect/texture. Sortie : artifacts/fx-audit.tsv.
# Usage : tools/fx-audit.sh [sous-dossier de corpus/fr-full]   (défaut effect/model)
#         tools/fx-audit.sh --bytex s0014102.dds   # quels nif utilisent cette texture
set -euo pipefail
source "$(dirname "$0")/env.sh"
proj="$EDEN_ROOT/tools/fx-audit/FxAudit.csproj"
if [ "${1:-}" = "--bytex" ]; then
    shift
    flock /tmp/eden-build.lock dotnet run -c Release --project "$proj" -- "$CORPUS_FR/effect/model" --bytex "$@"
    exit 0
fi
sub="${1:-effect/model}"
out="$ARTIFACTS/fx-audit.tsv"
mkdir -p "$ARTIFACTS"
flock /tmp/eden-build.lock dotnet run -c Release --project "$proj" -- "$CORPUS_FR/$sub" "$out"
