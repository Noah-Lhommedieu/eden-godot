#!/usr/bin/env bash
# Lance la chaîne native complète Eden depuis server_us/
# Usage: ./native_stack.sh [start|stop|status]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
US="$ROOT/server_us"
TOOLS="$ROOT/tools"
export LC_ALL=C LANG=C LOCPATH=/nonexistent

stop_all() {
    pkill -x TicketServer 2>/dev/null; pkill -x GatewayServer 2>/dev/null
    pkill -x MissionServer 2>/dev/null; pkill -f 'LoginServer' 2>/dev/null
    pkill -x WorldServer 2>/dev/null; pkill -x ZoneServer 2>/dev/null
    pkill -f 'native_mitm.py' 2>/dev/null
    sleep 1
}

start() {
    stop_all
    echo "[*] TicketServer :2067"
    (cd "$US/TicketServer" && setsid nohup ./TicketServer -p 2067 >/tmp/nat_ticket.out 2>&1 </dev/null &)
    sleep 1
    echo "[*] GatewayServer :5560"
    PGPASS="$(sed -n 's/^POSTGRES_PASSWORD=//p' "$ROOT/.env" 2>/dev/null | tail -n1)"
    PGPASS="${PGPASS:?POSTGRES_PASSWORD absent — renseigner .env}"
    (cd "$US/GatewayServer" && setsid nohup ./GatewayServer -i 127.0.0.1 -d edenfr_member -u postgres -w "$PGPASS" -p 5560 >/tmp/nat_gateway.out 2>&1 </dev/null &)
    sleep 1
    echo "[*] MissionServer :7654"
    (cd "$US/MissionServer" && setsid nohup ./MissionServer -d 127.0.0.1 >/tmp/nat_mission.out 2>&1 </dev/null &)
    sleep 1
    echo "[*] LoginServer :6544 (NSS-patched)"
    (cd "$US/LoginServer" && setsid nohup ./LoginServer.patched >/tmp/nat_login.out 2>&1 </dev/null &)
    sleep 1
    echo "[*] WorldServer :5710/5712 (charge Data ~1-2 min)"
    (cd "$US/WorldServer" && setsid nohup ./WorldServer >/tmp/nat_world.out 2>&1 </dev/null &)
    (
        for i in $(seq 1 180); do
            ss -tln 2>/dev/null | grep -q ':5710 ' && break
            sleep 2
        done
        echo "[*] ZoneServer 1011 :10022 (après World:5710)"
        cd "$US/ZoneServer" && setsid nohup ./ZoneServer >/tmp/nat_zone.out 2>&1 </dev/null
    ) &
    echo "[*] MITM :6543->6544 :5200->5712"
    (cd "$TOOLS" && setsid nohup python3 native_mitm.py >/tmp/mitm.log 2>&1 </dev/null &)
    sleep 2
    status
}

status() {
    echo "--- listeners ---"
    ss -tln | grep -E ':(2067|5200|5560|5710|5712|6543|6544|7654|10022) ' || true
    echo "--- procs ---"
    ps -eo pid,comm | grep -E 'TicketSer|GatewayS|MissionS|LoginSer|WorldSer|ZoneServ' || true
}

case "${1:-start}" in
    start) start ;;
    stop) stop_all ;;
    status) status ;;
esac
