#!/usr/bin/env bash
# Pilote : enchaîne des lots attest.sh de 420 s jusqu'à épuisement de la liste.
# Usage : run-all.sh [-m loose|strict] LISTE_CODES.txt
HERE="$(cd "$(dirname "$0")" && pwd)"
mode=loose; [ "${1:-}" = "-m" ] && { mode=$2; shift 2; }
list=$1
while true; do
  left=0
  for c in $(cat "$list"); do
    grep -q -P "^${c}\t[^\t]*\t${mode}\t" "$HERE/../../artifacts/map-attest/results.tsv" || left=$((left+1))
  done
  [ "$left" -eq 0 ] && { echo "terminé"; break; }
  echo "lot : $left restantes"
  "$HERE/attest.sh" -m "$mode" -b 420 $(cat "$list")
done
