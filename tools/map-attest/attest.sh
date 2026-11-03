#!/usr/bin/env bash
# Attestation de chargement des maps : un lancement client natif par map.
# Usage : tools/map-attest/attest.sh [-m loose|strict] [-b BATCH_S] CODE...
#         (CODE = S### ; --map reçoit le code tel quel, cf. GameScene)
# Reprenable : une map déjà présente dans results.tsv (même mode) est sautée.
# Sortie : artifacts/map-attest/results.tsv (+ shots/<code>[-strict].png|.log).
# Passe par tools/capture.sh (flock /tmp/godot.lock) — jamais deux clients.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
source "$HERE/../env.sh"
OUT="$ARTIFACTS/map-attest"; mkdir -p "$OUT/shots"
RES="$OUT/results.tsv"; mode=loose; budget=420
while getopts "m:b:" o; do case $o in m) mode=$OPTARG;; b) budget=$OPTARG;; esac; done
shift $((OPTIND-1))
[ -s "$RES" ] || printf 'code\tstatus\tmode\tmaploader\tglb\tplaceholders\tcoll_objs\tconsolidated\tprops\tnpc\tevent_areas\tspawn\tlum\tstd\tcolors\ttex_absent\tn_err\tfirst_err\treasons\tshot\tsecs\n' > "$RES"
t_start=$(date +%s)
for code in "$@"; do
  grep -q -P "^${code}\t[^\t]*\t${mode}\t" "$RES" && { echo "skip $code ($mode)"; continue; }
  [ $(( $(date +%s) - t_start )) -gt "$budget" ] && { echo "budget $budget s atteint — reprendre plus tard"; break; }
  t0=$(date +%s)
  sfx=""; [ "$mode" = strict ] && sfx="-strict"
  for try in 1 2 3; do
    if [ "$mode" = strict ]; then
      # Packagé strict (epk-native-notes §7) : clés dev de release, B lue du keyfile
      png=$(EDEN_PACKS="$ARTIFACTS/packs/test-native" EDEN_ALLOW_LOOSE=0 EDEN_PACK_STRICT=1 \
            EDEN_PACK_B="$(sed -n 's/^b=//p' "$HOME/.config/eden/release/dev-keyfile.txt")" \
            "$HERE/../capture.sh" "$code" "attest$sfx" 2>/dev/null | tail -1)
    else
      png=$("$HERE/../capture.sh" "$code" "attest$sfx" 2>/dev/null | tail -1)
    fi
    # timeout 180 de capture.sh inclut l'attente du flock : si le moteur n'a
    # jamais démarré (pas de ligne [vfs]), c'est la file d'attente, on retente.
    lg=$(ls -t "$ARTIFACTS"/shots/*-"$code"-attest$sfx.log 2>/dev/null | head -1)
    [ -n "$png" ] || { [ -n "$lg" ] && ! grep -q '^\[vfs\]' "$lg"; } || break
    [ -n "$png" ] && break
    [ -n "$lg" ] && grep -q '^\[vfs\]' "$lg" && break
  done
  secs=$(( $(date +%s) - t0 ))
  dst="$OUT/shots/${code}${sfx}"
  if [ -n "$png" ] && [ -s "$png" ]; then
    mv -f "$png" "$dst.png"; mv -f "${png%.png}.log" "$dst.log"
  else
    # capture.sh a échoué : récupérer le dernier log de ce code
    lg=$(ls -t "$ARTIFACTS"/shots/*-"$code"-attest$sfx.log 2>/dev/null | head -1)
    [ -n "$lg" ] && mv -f "$lg" "$dst.log"; : > "$dst.png"; rm -f "$dst.png"
  fi
  [ -e "$dst.log" ] || : > "$dst.log"
  EDEN_ROOT="$EDEN_ROOT" python3 -I "$HERE/parse_log.py" "$code" "$dst.log" "$dst.png" "$mode" "$secs" | tee -a "$RES" | cut -f1-3,4,6,7,10,19
done
