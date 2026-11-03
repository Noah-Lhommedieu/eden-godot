#!/usr/bin/env bash
# DB-4 — test de charge ~300 CCU contre une pile LOCALE ISOLÉE.
#
# N'interfère pas avec la pile `edenflax-*` en cours : PG17 jetable dédié
# (conteneur `edenflax-loadpg` :5434, max_connections=300, copie pg_dump de
# `edenfr`), ports services décalés (+10000), Redis base logique 15. Deux modes comparables :
#   redis  : EDEN_REDIS_CONN défini (DB-3 actif)
#   pg     : tout PostgreSQL (état avant DB-3)
#
# Usage : tools/load-test.sh <redis|pg> [sessions=300] [duration_s=60] [aps=2] [scenario=move]
# Prérequis : `dotnet build server/Eden.sln` fait, PG17 :5433 + Redis :6380 up.
set -euo pipefail
MODE="${1:?redis|pg}"; N="${2:-300}"; DUR="${3:-60}"; APS="${4:-2}"; SCEN="${5:-move}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/server"
PGPASS="$(sed -n 's/^POSTGRES_PASSWORD=//p' "$ROOT/.env" | tail -n1)"
LOADDB="edenfr"
LPG=edenflax-loadpg; LPORT=5434
OUT="${LOAD_OUT:-/tmp/eden-load-$MODE}"; mkdir -p "$OUT"
PSQL="docker exec -i $LPG psql -U postgres -v ON_ERROR_STOP=1 -q"
BIN="bin/Debug/net10.0"
DATA="$(realpath "$ROOT/server_us/Data/db_decoded")"
# corpus/ est ignoré par git : en worktree on le cherche dans le dépôt principal.
MAIN="$(cd "$(git -C "$ROOT" rev-parse --git-common-dir)/.." && pwd)"
CORPUS="$MAIN/corpus/fr-full/data"
CLIENT_DB="${EDEN_CLIENT_DB:-$CORPUS/db}"
SCENE_LINKS="$OUT/scene_links"; rm -rf "$SCENE_LINKS"; mkdir -p "$SCENE_LINKS"
for f in "$CORPUS"/scene/*.ini; do b="$(basename "$f")"; b="${b%.*}"; ln -sf "$f" "$SCENE_LINKS/${b^^}.ini"; done
CAPTURE="${EDEN_BOT_CAPTURE:-$(realpath "$ROOT/captures/zsniff_cs_entry_success.bin")}"

echo "== PG17 jetable $LPG :$LPORT ← pg_dump edenfr + $N personnages botchar*"
docker rm -f $LPG >/dev/null 2>&1 || true
docker run -d --name $LPG -e POSTGRES_PASSWORD="$PGPASS" -p $LPORT:5432 postgres:17 \
  -c max_connections=300 -c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=all >/dev/null
for i in $(seq 1 60); do docker exec $LPG pg_isready -U postgres -q && break; sleep 1; done
sleep 2
docker exec edenflax-postgres pg_dump -U postgres -Fc edenfr | docker exec -i $LPG pg_restore -U postgres -d postgres --create --no-owner
$PSQL -d "$LOADDB" <<SQL
CREATE TEMP TABLE t AS SELECT * FROM game.player_characters WHERE id = 200000001;
DO \$\$ BEGIN
  FOR i IN 1..$N LOOP
    UPDATE t SET id = 800000000 + i, given_name = 'botchar' || lpad(i::text, 4, '0'), gold = 100000, node_id = 5;
    INSERT INTO game.player_characters SELECT * FROM t;
  END LOOP;
END \$\$;
SQL

# Aucun pkill : on ne touche JAMAIS aux services d'une autre session. Ports décalés requis libres.
for port in 12067 17654 15200 15201 15301; do
  if ss -tln | grep -q ":$port "; then echo "port $port occupé — abandon"; exit 1; fi
done
export EDEN_DB_CONN="Host=localhost;Port=$LPORT;Database=$LOADDB;Username=postgres;Password=$PGPASS"
export EDEN_DB_POOL_MAX="${EDEN_DB_POOL_MAX:-30}"
export EDEN_REDIS_CONN=""
if [ "$MODE" = "redis" ]; then export EDEN_REDIS_CONN="localhost:6380,defaultDatabase=15"; fi
[ "$MODE" = redis ] && docker exec edenflax-redis redis-cli -n 15 FLUSHDB >/dev/null
P=10000
export TICKET_PORT=$((2067+P)) MISSION_PORT=$((7654+P)) WORLD_PORT=$((5200+P)) ZONE_PORT=$((5201+P)) ZW_PORT=$((5301+P)) WORLD_LINK_PORT=$((5301+P))
export EDEN_REFORGED_DB="$CLIENT_DB" EDEN_DATA="$DATA" DATA_DIR="$DATA" MISSION_HOST=127.0.0.1 TICKET_IP=127.0.0.1
export SCENE_DIR="$SCENE_LINKS" NODEMAP_DIR="$(realpath "$ROOT/server_us/Data/NodeMap")"
export EDEN_PERSIST_FLUSH_MS="${EDEN_PERSIST_FLUSH_MS:-15000}"
for s in Ticket Mission World Zone; do
  setsid nohup "src/Eden.$s/$BIN/Eden.$s" </dev/null >"$OUT/$s.log" 2>&1 &
  echo $! >"$OUT/$s.pid"
done
for i in $(seq 1 60); do ss -tln | grep -q ":$ZONE_PORT " && ss -tln | grep -q ":$MISSION_PORT " && break; sleep 1; done
sleep 3
docker exec $LPG psql -U postgres -d "$LOADDB" -qc "SELECT pg_stat_statements_reset()" >/dev/null

( while true; do docker exec $LPG psql -U postgres -Atc "SELECT count(*) FROM pg_stat_activity WHERE datname='$LOADDB'" >>"$OUT/pgconn.txt" 2>/dev/null; sleep 2; done ) &
SAMPLER=$!
trap 'kill $SAMPLER $(cat "$OUT"/*.pid) 2>/dev/null || true; docker rm -f $LPG >/dev/null 2>&1 || true' EXIT
echo "== bots : $N sessions, ${DUR}s, mode=$MODE"
dotnet run --no-build --project tools/Eden.Bots -- --scenario="$SCEN" --sessions="$N" --duration="$DUR" --aps="$APS" \
  --spawn-wait=5 --stagger-ms=40 --ticket-port=$((TICKET_PORT)) --zone-port=$ZONE_PORT \
  --db="Host=localhost;Port=$LPORT;Database=$LOADDB;Username=postgres;Password=$PGPASS;SearchPath=game,public" \
  --capture="$CAPTURE" --tls-insecure --json="$OUT/bots.json" > "$OUT/bots.out" || true
python3 - "$OUT/bots.json" <<'PY'
import json,sys
d=json.load(open(sys.argv[1])); d.pop('failures',None); d.pop('inboundNcids',None); d.pop('results',None)
print(json.dumps(d,indent=1))
PY

echo "pg connexions (max observé sur $LOADDB) : $(sort -n "$OUT/pgconn.txt" | tail -1)" | tee "$OUT/pgconn-max.txt"
echo "== pg_stat_statements (top 15 par temps total) → $OUT/pgss.txt"
docker exec $LPG psql -U postgres -d "$LOADDB" -c \
 "SELECT calls, round(total_exec_time::numeric,1) AS total_ms, round(mean_exec_time::numeric,3) AS mean_ms, round(max_exec_time::numeric,1) AS max_ms, left(regexp_replace(query,'\s+',' ','g'),90) AS q FROM pg_stat_statements WHERE dbid=(SELECT oid FROM pg_database WHERE datname='$LOADDB') ORDER BY total_exec_time DESC LIMIT 15" | tee "$OUT/pgss.txt"
echo "calls total: $(docker exec $LPG psql -U postgres -d "$LOADDB" -Atc "SELECT sum(calls) FROM pg_stat_statements WHERE dbid=(SELECT oid FROM pg_database WHERE datname='$LOADDB')")" | tee -a "$OUT/pgss.txt"

echo "== services arrêtés, micro-bench des opérations chaudes (300 joueurs × 200 ops)"
kill $(cat "$OUT"/*.pid) 2>/dev/null || true; sleep 3
dotnet run --no-build --project tools/Eden.HotBench -- \
  --db="Host=localhost;Port=$LPORT;Database=$LOADDB;Username=postgres;Password=$PGPASS;SearchPath=game,public" \
  --redis="localhost:6380,defaultDatabase=15" --players="$N" --ops=200 --first-char=800000001 \
  --json="$OUT/hotbench.json" | tee "$OUT/hotbench.txt"
