#!/usr/bin/env bash
# Reconstruit dist/packs-test/cfgfix.epk = overlay (dernier pack monté gagne)
# contenant client.cfg + data/ corpus + tout fichier loose (assets/ ou gamedata/) plus récent
# que eden-core.epk. À lancer après toute modif d'assets/gamedata quand on joue
# avec EDEN_ALLOW_LOOSE=0 EDEN_PACKS=…:cfgfix.epk (sinon le jeu garde les
# anciennes données des gros packs).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PK="$ROOT/dist/packs-test"
GP="$ROOT/godot_project"
STAGE="$PK/cfgfix"
EP="$ROOT/tools/edenpack/bin/Debug/net8.0/edenpack"
[ -x "$EP" ] || dotnet build "$ROOT/tools/edenpack/edenpack.csproj" >/dev/null
cp -n "$STAGE/client.cfg" /tmp/_client.cfg.keep 2>/dev/null || true
find "$STAGE" -mindepth 1 ! -name client.cfg -delete
# Données corpus (scene, db, sknodemap…) : eden-core.epk date d'avant
# l'import EDENFR — sans ça le client garde les anciennes scènes (portails
# US : Aven -> Temple Spirenfer) face à un serveur/corpus à jour.
CO="$ROOT/../corpus/official"
mkdir -p "$STAGE/data" "$STAGE/map/sknodemap"
cp -r "$CO/data/." "$STAGE/data/"
cp "$CO"/map/sknodemap/*.smp "$STAGE/map/sknodemap/"
cd "$GP"
declare -A SEEN
while IFS= read -r f; do
  case "$f" in
    assets/*) dst="$STAGE/$f" ;;
    gamedata/*) dst="$STAGE/${f#gamedata/}" ;;
    *) continue ;;
  esac
  case "$f" in *.import|gamedata/settings.cfg) continue ;; esac
  # Doublons de casse (r281/R281…) : edenpack refuse la collision.
  lc="${dst,,}"; [ -n "${SEEN[$lc]:-}" ] && continue; SEEN[$lc]=1
  mkdir -p "$(dirname "$dst")"; cp "$f" "$dst"
done < <(find assets gamedata -type f -newer "$PK/eden-core.epk")
"$EP" build "$STAGE" "$PK/cfgfix.new" --level 3
mv -f "$PK/cfgfix.new" "$PK/cfgfix.epk"
