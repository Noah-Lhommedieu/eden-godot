#!/usr/bin/env bash
# Balayage visuel montures : pour chaque ride key de ridelist.ini, pose le
# modèle via /tmp/ride_key.txt (yaw=0 — marche, pas vue de profil bloquée),
# capture --shot. Usage : flock /tmp/godot.lock mount-sweep.sh [map]
# Sortie : artifacts/mount-sweep/<key>.png + results.tsv (repris auto).
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/.."
OUT="$ROOT/artifacts/mount-sweep"
GODOT="$ROOT/engine/current/godot"
MAP="${1:-S401}"
mkdir -p "$OUT"
RES="$OUT/results.tsv"
[ -f "$RES" ] || printf 'key\tstem\tvariant\tshot_ok\terrs\n' > "$RES"
grep -oP 'r\K\d{6}' "$ROOT/corpus/fr-full/data/db/ridelist.ini" | sort -u | \
while read -r key; do
  grep -q -P "^${key}\t" "$RES" && continue
  stem=$((10#$key / 100)); var=$((10#$key % 100 / 10))
  shot="$OUT/$key.png"
  echo "$key 0 8 9 1.2" > /tmp/ride_key.txt
  "$GODOT" --path "$ROOT/eden_godot/godot_project" \
    res://scenes/game.tscn --resolution 960x540 \
    -- --map="$MAP" --solo --shot="$shot" > "$OUT/$key.log" 2>&1 &
  pid=$!
  for _ in $(seq 1 45); do
    [ -s "$shot" ] && break
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
  done
  sleep 1; kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  ok=0; [ -s "$shot" ] && ok=1
  errs=$(grep -c 'ERROR' "$OUT/$key.log")
  printf '%s\t%s\t%s\t%s\t%s\n' "$key" "$stem" "$var" "$ok" "$errs" >> "$RES"
  echo "key $key (r$stem v$var) : shot=$ok err=$errs"
done
rm -f /tmp/ride_key.txt
echo "terminé : $(tail -n +2 "$RES" | wc -l) clés"
