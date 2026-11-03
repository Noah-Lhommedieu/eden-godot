#!/usr/bin/env bash
# Capture la fenêtre active toutes les 5 s vers ref-shots/.
# Usage: ./capture.sh [durée_max_secondes]  (défaut 0 = sans fin)
OUT="$(cd "$(dirname "$0")" && pwd)"
LIMIT="${1:-0}"
i=1; t=0
while true; do
    ts=$(date +%H%M%S)
    DISPLAY=:0 spectacle -b -n -a -o "$OUT/shot_$(printf %04d $i)_$ts.png" >/dev/null 2>&1 || true
    i=$((i+1)); t=$((t+5))
    [ "$LIMIT" -gt 0 ] && [ "$t" -ge "$LIMIT" ] && break
    sleep 5
done
echo "capture terminée: $((i-1)) images" >> "$OUT/capture.log"
