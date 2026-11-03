#!/usr/bin/env bash
# Tests : NetTests client (dont smoke headless sur le moteur épinglé),
# Eden.Core, serveur. Usage : tools/test.sh [--no-server]
set -euo pipefail
source "$(dirname "$0")/env.sh"
mkdir -p "$ARTIFACTS/logs"; log="$ARTIFACTS/logs/test-$(eden_ts).log"

suites=("$NETTESTS_DIR" "$CORE_TESTS_DIR")
[ "${1:-}" = "--no-server" ] || suites+=("$SERVER_SLN")
rc=0
tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
for s in "${suites[@]}"; do
    eden_log "test $(basename "$s")"
    code=0; dotnet test "$s" -nologo > "$tmp" 2>&1 || code=$?
    cat "$tmp" >> "$log"
    grep -E "Réussi!|Échoué!|Passed!|Failed!|error" "$tmp" || true
    [ $code -eq 0 ] || rc=1
done
[ $rc -eq 0 ] && eden_log "ok — $log" || eden_die "échecs — $log"
