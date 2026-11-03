#!/usr/bin/env bash
# DB-2 — fusion des 3 bases (edenfr_db / edenfr_account / edenfr_member)
# en UNE base `edenfr` à schémas logiques : game / account / member
# (+ game_archive / account_archive pour les anciens schémas `archive`).
#
# Non destructif pour les sources : les 3 anciennes bases restent
# intactes (rollback = repointer DB_CONN/ACCOUNT_DB_CONN/MEMBER_DB_CONN).
# Rejouable : `edenfr` est DROPée puis recréée à chaque exécution.
#
# Méthode : pg_dump -n public -n archive de chaque source, restauré
# dans le schéma `public` vide de `edenfr`, puis renommé (public→game…).
# Les OID suivent le renommage : séquences, défauts nextval(), vues et
# FKs internes restent valides sans réécriture de DDL.
#
# Usage : ./022-merge-schemas.sh            (via docker exec edenflax-postgres)
#         EDEN_PG_EXEC="" ./022-merge-schemas.sh   (psql/pg_dump locaux, PG* en env)
set -euo pipefail
cd "$(dirname "$0")"

EXEC="${EDEN_PG_EXEC-docker exec -i edenflax-postgres}"
PSQL="$EXEC psql -U postgres -v ON_ERROR_STOP=1 -q"
DUMP="$EXEC pg_dump -U postgres --no-owner --no-acl"
TARGET="${EDEN_DB_NAME:-edenfr}"

$PSQL -d postgres -c "DROP DATABASE IF EXISTS $TARGET WITH (FORCE)"
$PSQL -d postgres -c "CREATE DATABASE $TARGET"

# <base source> <schéma public cible> <schéma archive cible | ->
merge() {
    local src="$1" pub="$2" arc="$3"
    echo "== $src → $pub${arc:+ / $arc}"
    # le dump contient CREATE SCHEMA public/archive : les cibles doivent être absentes
    $PSQL -d "$TARGET" -c "DROP SCHEMA IF EXISTS archive CASCADE; DROP SCHEMA IF EXISTS public CASCADE"
    $DUMP -d "$src" -n public -n archive | $PSQL -d "$TARGET" -o /dev/null
    $PSQL -d "$TARGET" -c "ALTER SCHEMA public RENAME TO $pub"
    if [ "$arc" != "-" ]; then
        $PSQL -d "$TARGET" -c "ALTER SCHEMA archive RENAME TO $arc" 2>/dev/null || true
    fi
}

merge edenfr_db      game    game_archive
merge edenfr_account account account_archive
merge edenfr_member  member  -

$PSQL -d "$TARGET" -c "CREATE SCHEMA IF NOT EXISTS public"
$PSQL -d "$TARGET" -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements"
$PSQL -d "$TARGET" -f - < 023-cross-schema-fks.sql
echo "OK — base $TARGET : schémas game / account / member"
