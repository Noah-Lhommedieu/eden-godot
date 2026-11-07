#!/usr/bin/env bash
# Limitless Studio — build + run. Usage:
#   ./run.sh                 build Debug + lance l'app
#   ./run.sh --page=icons    ouvre directement une page
#   ./run.sh --release       build Release
set -euo pipefail
cd "$(dirname "$0")/Limitless.Studio"

conf=Debug
args=()
for a in "$@"; do
    case "$a" in
        --release) conf=Release ;;
        *) args+=("$a") ;;
    esac
done

dotnet build -c "$conf"
exec dotnet "bin/$conf/net10.0/Limitless.Studio.dll" "${args[@]}"
