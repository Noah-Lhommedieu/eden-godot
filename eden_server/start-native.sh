#!/usr/bin/env bash
# Lance les services C# en NATIF (hors docker) avec la bonne connexion DB.
#
# ATTENTION — ne PAS utiliser `source .env` : la valeur DB_CONN contient des
# ';' qui tronquent le sourcing. Et surtout : `Host=postgres` ne résout QUE
# dans le réseau docker — en natif il faut Host=localhost (ou 127.0.0.1)
# sinon TOUTES les écritures DB échouent en silence (quêtes, shortcuts,
# vitals, inventaire — bug live constaté plusieurs sessions).
#
# Chaîne complète : Gateway + Login + Ticket (front) puis Mission,
# World, Zone (jeu). Tout écoute sur 0.0.0.0 — le client se connecte
# au Gateway (5560) ou au Login (6543) selon son launcher.
set -u
EDEN_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$EDEN_ROOT/server"

PGPASS="$(sed -n 's/^POSTGRES_PASSWORD=//p' "$EDEN_ROOT/.env" 2>/dev/null | tail -n1)"
PGPASS="${PGPASS:?POSTGRES_PASSWORD absent — renseigner .env (voir .env.example)}"
PGPORT="${EDEN_PG_PORT:-5433}"
DBC="Host=localhost;Port=$PGPORT;Database=edenfr_db;Username=postgres;Password=$PGPASS"
ACC="Host=localhost;Port=$PGPORT;Database=edenfr_account;Username=postgres;Password=$PGPASS"
MEM="Host=localhost;Port=$PGPORT;Database=edenfr_member;Username=postgres;Password=$PGPASS"

# DB-2 : base unique `edenfr` (schémas game/account/member, search_path par
# rôle via DbConnectionFactory). EDEN_DB_CONN prime sur DB_CONN/ACCOUNT_DB_CONN/
# MEMBER_DB_CONN ci-dessus ; EDEN_LEGACY_DB=1 = rollback vers les 3 anciennes bases.
if [ "${EDEN_LEGACY_DB:-0}" != "1" ]; then
    export EDEN_DB_CONN="Host=localhost;Port=$PGPORT;Database=edenfr;Username=postgres;Password=$PGPASS"
fi
# DB-3 : tickets + mutations chaudes (or/position/vitals) sur Redis 7 (port 6380),
# flush Redis→PG ~15 s. EDEN_REDIS=off ou EDEN_LEGACY_DB=1 = tout PostgreSQL.
if [ "${EDEN_REDIS:-on}" != "off" ] && [ "${EDEN_LEGACY_DB:-0}" != "1" ]; then
    export EDEN_REDIS_CONN="${EDEN_REDIS_CONN:-localhost:${EDEN_REDIS_PORT:-6380}}"
fi
DATA="$(realpath ../server_us/Data/db_decoded)"
# Tables canoniques côté client joué : corpus EDENFR pur
# (corpus/fr-full) — décision « EDENFR only », pas de merge EN/US.
# Rollback : EDEN_CLIENT_DB=off (natives S_*) ou un autre chemin db.
CLIENT_DB="${EDEN_CLIENT_DB:-$(realpath "$EDEN_ROOT/../corpus/fr-full/data/db")}"

# Scènes (spawns/PNJ/portails) : EDENFR officiel uniquement
# (corpus/fr-full/data/scene) — plus de merge avec les scènes US.
# fr-full nomme les fichiers en minuscules (s002.ini) alors que le
# serveur déduit le MapCode du nom (S002) : on monte un dossier de
# liens normalisés ${b^^}.ini — 100 % EDENFR, sans données US.
# Rollback : EDEN_SCENE_DIR=off.
SCENE_DIR_SEL="${EDEN_SCENE_DIR:-/tmp/edenfr_scene_links}"
if [ "$SCENE_DIR_SEL" = "off" ]; then
    SCENE_DIR_SEL=""
elif [ "$SCENE_DIR_SEL" = "/tmp/edenfr_scene_links" ]; then
    rm -rf "$SCENE_DIR_SEL"; mkdir -p "$SCENE_DIR_SEL"
    for f in "$(realpath "$EDEN_ROOT/../corpus/fr-full/data/scene")"/*.ini; do
        [ -e "$f" ] || continue
        b="$(basename "$f")"; b="${b%.*}"; ln -sf "$f" "$SCENE_DIR_SEL/${b^^}.ini"
    done
fi

BIN="bin/Debug/net10.0"

pkill -f "net10.0/Eden." 2>/dev/null
pkill -f "net8.0/Eden." 2>/dev/null
sleep 1

# --- Front (stateless-DB ou account) ---
setsid nohup env GATEWAY_PORT=5560 \
    "src/Eden.Gateway/$BIN/Eden.Gateway" \
    </dev/null >/tmp/gateway.log 2>&1 &

setsid nohup env LOGIN_PORT=6543 ACCOUNT_DB_CONN="$ACC" DB_CONN="$DBC" \
    POC_ADVERTISE_IP=127.0.0.1 \
    "src/Eden.Login/$BIN/Eden.Login" \
    </dev/null >/tmp/login.log 2>&1 &

setsid nohup env TICKET_PORT=2067 \
    "src/Eden.Ticket/$BIN/Eden.Ticket" \
    </dev/null >/tmp/ticket.log 2>&1 &

# --- Jeu ---
setsid nohup env DB_CONN="$DBC" MISSION_PORT=7654 EDEN_DATA="$DATA" \
    EDEN_REFORGED_DB="$CLIENT_DB" \
    "src/Eden.Mission/$BIN/Eden.Mission" \
    </dev/null >/tmp/mission.log 2>&1 &

# ZONE_PORT=5201 est aussi lu par le World : il l'annonce aux clients
# comme adresse de la zone (EdenConfig.ZonePort, défaut 10022).
setsid nohup env DB_CONN="$DBC" ACCOUNT_DB_CONN="$ACC" MEMBER_DB_CONN="$MEM" \
    EDEN_REFORGED_DB="$CLIENT_DB" \
    WORLD_PORT=5200 ZW_PORT=5301 ZONE_PORT=5201 \
    "src/Eden.World/$BIN/Eden.World" \
    </dev/null >/tmp/world.log 2>&1 &

# MISSION_PORT explicité : l'env shell peut exporter une valeur
# différente (vu 5202 → lien ZM refusé en boucle).
setsid nohup env DB_CONN="$DBC" ACCOUNT_DB_CONN="$ACC" \
    EDEN_REFORGED_DB="$CLIENT_DB" \
    ZONE_PORT=5201 DATA_DIR="$DATA" MISSION_PORT=7654 \
    ${SCENE_DIR_SEL:+SCENE_DIR="$SCENE_DIR_SEL"} \
    ${SCENE_DIR_SEL:+NODEMAP_DIR="$(realpath ../server_us/Data/NodeMap)"} \
    "src/Eden.Zone/$BIN/Eden.Zone" \
    </dev/null >/tmp/zone.log 2>&1 &

sleep 6
ss -tln | grep -E "2067|5200|5201|5301|5560|6543|7654"
tail -n 3 /tmp/gateway.log /tmp/login.log /tmp/ticket.log \
    /tmp/mission.log /tmp/world.log /tmp/zone.log
