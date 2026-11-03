#!/usr/bin/env bash
# Build client + Core + serveur. Usage : tools/build.sh [Debug|Release]
set -euo pipefail
source "$(dirname "$0")/env.sh"
cfg="${1:-Debug}"
mkdir -p "$ARTIFACTS/logs"; log="$ARTIFACTS/logs/build-$(eden_ts).log"

eden_log "godot $("$GODOT_BIN" --version) — dotnet $(dotnet --version) — $cfg"
tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
for proj in "$CLIENT_CSPROJ" "$EDEN_ROOT/src/Eden.Core/Eden.Core.csproj" "$SERVER_SLN"; do
    eden_log "build $(basename "$proj")"
    code=0; dotnet build "$proj" -c "$cfg" -nologo > "$tmp" 2>&1 || code=$?
    cat "$tmp" >> "$log"
    grep -E " error |Erreur\(s\)|Avertissement\(s\)|Error\(s\)|Warning\(s\)" "$tmp" | sort -u || true
    [ $code -eq 0 ] || eden_die "build $(basename "$proj") — voir $log"
done
eden_log "ok — $log"
