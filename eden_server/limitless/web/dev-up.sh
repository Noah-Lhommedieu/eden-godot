#!/bin/bash
# dev-up.sh — lance le portail Limitless (API :5154 + front :3000)
# contre le PostgreSQL local. Les DSN sont dans
# backend/appsettings.json (mêmes identifiants que tools/start-local.sh).
# Usage : limitless/web/dev-up.sh   (Ctrl-C ne tue pas les setsid —
#   ./dev-up.sh --stop pour tout arrêter)
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"

if [ "$1" = "--stop" ]; then
  pkill -f "Limitless.Api.dll" || true
  pkill -f "next dev" || true
  exit 0
fi

cd "$ROOT/backend"
dotnet build -c Debug >/dev/null
ASPNETCORE_URLS="http://localhost:5154" \
  setsid nohup dotnet bin/Debug/net10.0/Limitless.Api.dll \
  > /tmp/limitless-api.log 2>&1 &

cd "$ROOT/frontend"
setsid nohup npm run dev > /tmp/limitless-web.log 2>&1 &

sleep 5
ss -tln | grep -E "5154|3000" \
  && echo "[OK] API :5154 + front :3000 (logs /tmp/limitless-*.log)"
