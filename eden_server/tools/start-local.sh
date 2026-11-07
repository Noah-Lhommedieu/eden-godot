#!/bin/bash
# POC local : lance la stack complète (Gateway/Ticket/Login/World/
# Zone/Mission) contre le PostgreSQL du host (127.0.0.1:5433).
# Le client connect.ini pointe vers 127.0.0.1:6543 (le client tourne
# sous Wine sur la même machine — l'IP LAN DHCP a déjà changé :
# 172.29.x -> 192.168.1.30).
# Prérequis : dotnet build -c Release (binaires bin/Release/net10.0).
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -f "$ROOT/.env" ]; then
  PGPASS="$(sed -n 's/^POSTGRES_PASSWORD=//p' "$ROOT/.env" | tail -n1)"
fi
PGPASS="${PGPASS:-postgres}"
DB="Host=127.0.0.1;Port=5433;Database=eden_live;Username=postgres;Password=$PGPASS"
ACC="Host=127.0.0.1;Port=5433;Database=eden_live;Username=postgres;Password=$PGPASS"
MEM="Host=127.0.0.1;Port=5433;Database=eden_live;Username=postgres;Password=$PGPASS"

# DB-2 : base unique `edenfr` (schémas game/account/member, search_path par
# rôle via DbConnectionFactory). EDEN_DB_CONN prime sur DB_CONN/ACCOUNT_DB_CONN/
# MEMBER_DB_CONN ci-dessus ; EDEN_LEGACY_DB=1 = rollback vers les 3 anciennes bases.
if [ "${EDEN_LEGACY_DB:-0}" != "1" ]; then
    export EDEN_DB_CONN="Host=127.0.0.1;Port=5433;Database=eden_live;Username=postgres;Password=$PGPASS"
fi
# DB-3 : tickets + mutations chaudes (or/position/vitals) sur Redis 7 (port 6380),
# flush Redis→PG ~15 s. EDEN_REDIS=off ou EDEN_LEGACY_DB=1 = tout PostgreSQL.
if [ "${EDEN_REDIS:-on}" != "off" ] && [ "${EDEN_LEGACY_DB:-0}" != "1" ]; then
    export EDEN_REDIS_CONN="${EDEN_REDIS_CONN:-localhost:${EDEN_REDIS_PORT:-6380}}"
fi
DATA="$ROOT/server_us/Data/db_decoded"
REFORGED_DB="$ROOT/../../eden_server/reforged/EdenReforged/data/db"
[ -d "$REFORGED_DB" ] || REFORGED_DB=""
HOST_IP="${EDEN_HOST_IP:-127.0.0.1}"

cd "$ROOT/server"
setsid nohup dotnet src/Eden.Gateway/bin/Release/net10.0/Eden.Gateway.dll > /tmp/eden-gateway.log 2>&1 &
setsid nohup dotnet src/Eden.Ticket/bin/Release/net10.0/Eden.Ticket.dll > /tmp/eden-ticket.log 2>&1 &
EDEN_NET_DEBUG=1 ACCOUNT_DB_CONN="$ACC" MEMBER_DB_CONN="$MEM" GAME_DB_CONN="$DB" \
  POC_ADVERTISE_IP="$HOST_IP" POC_ADVERTISE_PORT=5200 LOGIN_PORT=6543 \
  setsid nohup dotnet src/Eden.Login/bin/Release/net10.0/Eden.Login.dll > /tmp/eden-login.log 2>&1 &
EDEN_NET_DEBUG=1 DB_CONN="$DB" ACCOUNT_DB_CONN="$ACC" WORLD_PORT=5200 ZONE_IP="$HOST_IP" ZONE_PORT=5201 \
  setsid nohup dotnet src/Eden.World/bin/Release/net10.0/Eden.World.dll > /tmp/eden-world.log 2>&1 &
EDEN_NET_DEBUG=1 DB_CONN="$DB" ACCOUNT_DB_CONN="$ACC" ZONE_PORT=5201 NODE_ID=5 DATA_DIR="$DATA" \
  EDEN_REFORGED_DB="$REFORGED_DB" TICKET_IP=127.0.0.1 MISSION_HOST=127.0.0.1 \
  setsid nohup dotnet src/Eden.Zone/bin/Release/net10.0/Eden.Zone.dll > /tmp/eden-zone.log 2>&1 &
EDEN_NET_DEBUG=1 DB_CONN="$DB" DATA_DIR="$DATA" EDEN_REFORGED_DB="$REFORGED_DB" \
  setsid nohup dotnet src/Eden.Mission/bin/Release/net10.0/Eden.Mission.dll > /tmp/eden-mission.log 2>&1 &
sleep 4
ss -tln | grep -E "5560|2067|6543|5200|5201|7654" \
  && echo "[OK] Services Eden démarrés (logs: /tmp/eden-*.log)"
