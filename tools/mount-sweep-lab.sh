#!/usr/bin/env bash
# Sweep « Hors archive » : monte CHAQUE rideKey du TSV (défaut :
# docs/reports/FIX-F-montures-hors-archive.tsv) dans le banc ride_lab,
# vérifie mesh + textures + anims (stand/move/jump) + cavalier (ride01/04)
# + FX, capture « marche » et écrit results.tsv. Code retour 1 si un FAIL.
# Usage : tools/mount-sweep-lab.sh [sortie-dir] [tsv-des-clés]
set -euo pipefail
source "$(dirname "$0")/env.sh"
out="${1:-$ARTIFACTS/mount-sweep-lab}"
tsv="${2:-$EDEN_ROOT/docs/reports/FIX-F-montures-hors-archive.tsv}"
mkdir -p "$out"
RIDE_LAB_TIMEOUT="${RIDE_LAB_TIMEOUT:-1500}" "$EDEN_ROOT/tools/ride-lab.sh" "$out" \
    --rides-file="$tsv" --sweep="$out/results.tsv" \
    --script=STAND:0.4,W:0.8,A:0.3,J:0.3 --cam=100,10,9 | tail -3
python3 - "$out/results.tsv" <<'PY'
import csv, sys
rows = list(csv.DictReader(open(sys.argv[1]), delimiter="\t"))
bad = [r for r in rows if r["status"] != "OK"]
print(f"sweep : {len(rows)} montures, {len(rows)-len(bad)} OK, {len(bad)} FAIL")
for r in bad: print("  FAIL", r["key"], r["stem"], r["notes"], "mesh=%s notex=%s anim=%s rider=%s" % (r["mesh"], r["notex"], r["anim"], r["rider"]))
sys.exit(1 if bad else 0)
PY
