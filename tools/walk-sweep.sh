#!/usr/bin/env bash
# Balayage déplacement : --walk-test=10 solo sur chaque map de la liste.
# Usage : walk-sweep.sh LISTE.txt   (codes Sxxx, un par ligne, header ignoré)
# Sortie : artifacts/walk-sweep/logs/<code>.log + results.tsv (reprend les
# codes déjà présents dans results.tsv — interruptible).
# Tue le client dès la ligne « arrêt » du walk-test (~20 s/map).
# Le lock GPU est pris par l'APPELANT : flock /tmp/godot.lock walk-sweep.sh …
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/.."
OUT="$ROOT/artifacts/walk-sweep"
GODOT="$ROOT/engine/current/godot"
LIST="$1"
mkdir -p "$OUT/logs"
RES="$OUT/results.tsv"
[ -f "$RES" ] || printf 'code\tdist_10s\ty_start\ty_end\terr_count\tstatus\n' > "$RES"
for code in $(cat "$LIST"); do
  [ "$code" = "code" ] && continue
  grep -q -P "^${code}\t" "$RES" && continue
  log="$OUT/logs/$code.log"
  : > "$log"
  "$GODOT" --path "$ROOT/eden_godot/godot_project" \
    res://scenes/game.tscn --resolution 960x540 \
    -- --map="$code" --solo --walk-test=10 >> "$log" 2>&1 &
  pid=$!
  # Attente : ligne « arrêt », ou crash client, ou 60 s max.
  for _ in $(seq 1 60); do
    grep -q 'walk-test] arrêt' "$log" && break
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
  done
  kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  dep=$(grep -oP 'départ \(\K[^)]+' "$log" | head -1)
  arr=$(grep -oP 'arrêt \(\K[^)]+' "$log" | head -1)
  dist=$(grep -oP 'déplacé \K[0-9.,]+' "$log" | head -1)
  ys=$(printf '%s' "$dep" | cut -d, -f2 | tr -d ' ')
  ye=$(printf '%s' "$arr" | cut -d, -f2 | tr -d ' ')
  errc=$(grep -c 'ERROR' "$log")
  st="?"
  [ -z "$dep" ] && st="NO_START"
  [ -n "$dep" ] && [ -z "$arr" ] && st="NO_STOP"
  [ -n "$arr" ] && st="OK"
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$code" "${dist:-0}" "${ys:-?}" "${ye:-?}" "$errc" "$st" >> "$RES"
  echo "$code : ${dist:-?} u, y ${ys:-?}→${ye:-?}, err=$errc [$st]"
done
echo "terminé : $(tail -n +2 "$RES" | wc -l) maps dans $RES"
