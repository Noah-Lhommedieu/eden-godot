#!/usr/bin/env bash
# Compare deux résumés de bench (summary.env ou dossier qui le contient).
# Usage : tools/compare.sh <baseline> <candidat>
set -euo pipefail
a="${1:?baseline}"; b="${2:?candidat}"
[ -d "$a" ] && a="$a/summary.env"; [ -d "$b" ] && b="$b/summary.env"
python3 -I - "$a" "$b" <<'EOF'
import sys
def load(p):
    return dict(l.rstrip("\n").split("=", 1) for l in open(p) if "=" in l)
a, b = load(sys.argv[1]), load(sys.argv[2])
for k in ("godot", "map", "runs", "draws", "prims", "nodes"):
    flag = "" if a.get(k) == b.get(k) else "   ⚠ diffère"
    print(f"{k:14} {a.get(k,'-'):>28} {b.get(k,'-'):>28}{flag}")
for k in ("frame_p50_ms", "frame_p95_ms", "gpu_p50_ms", "rss_mb_p50"):
    try:
        x, y = float(a[k]), float(b[k])
        print(f"{k:14} {x:>28.2f} {y:>28.2f}   {100 * (y - x) / x:+.1f} %")
    except (KeyError, ValueError):
        print(f"{k:14} {a.get(k,'-'):>28} {b.get(k,'-'):>28}")
EOF
