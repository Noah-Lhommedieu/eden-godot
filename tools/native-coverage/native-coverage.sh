#!/usr/bin/env bash
# Couverture des formats natifs. Usage : tools/native-coverage/native-coverage.sh --fx [secondes]
# --textures : audit de toutes les références de textures (docs/workflows/TEXTURE-AUDIT.md)
# --fx : tous les .nif de fr-full -> mappage + simulation de particules (Core pur,
#        headless) + inventaire des contrôleurs -> artifacts/fx-sheets/coverage.tsv
set -euo pipefail
source "$(dirname "$0")/../env.sh"
mode="${1:-}"; shift || true
# Le résolveur de textures du port Core (AllCoverage/TextureAudit.cs) doit rester identique à NifMaterials.ResolveCore.
check_resolver_sync() {
    python3 -I - "$EDEN_ROOT/eden_godot/godot_project/scripts/native/NifMaterial.cs" "$(dirname "$0")/AllCoverage/TextureAudit.cs" <<'PY'
import re, sys
def body(path):
    s = open(path, encoding="utf8").read()
    i = s.index("static string? ResolveCore(")
    j = s.index("{", i); d = 0
    for k in range(j, len(s)):
        d += (s[k] == "{") - (s[k] == "}")
        if d == 0: break
    t = re.sub(r"//[^\n]*", "", s[j:k + 1])          # sans commentaires
    return re.sub(r"\s+", "", t).replace("s_byRel!", "s_byRel").replace("s_byBase!", "s_byBase")
a, b = body(sys.argv[1]), body(sys.argv[2])
if a != b:
    sys.exit("ResolveCore a divergé entre NifMaterial.cs et AllCoverage/TextureAudit.cs : resynchroniser le port.")
print("résolveur synchro : OK")
PY
}
case "$mode" in
    --fx)
        out="$ARTIFACTS/fx-sheets/coverage.tsv"
        dotnet run -c Release --project "$(dirname "$0")/FxCoverage" -- "$CORPUS_FR" "$out" "${1:-2}"
        ;;
    --textures)
        check_resolver_sync
        dotnet run -c Release --project "$(dirname "$0")/AllCoverage" -- "$CORPUS_FR" "$ARTIFACTS/coverage/textures.tsv" textures
        ;;
    --all)
        check_resolver_sync
        dotnet run -c Release --project "$(dirname "$0")/AllCoverage" -- "$CORPUS_FR" "$ARTIFACTS/coverage/coverage-all.tsv"
        dotnet run -c Release --project "$(dirname "$0")/AllCoverage" -- "$CORPUS_FR" "$ARTIFACTS/coverage/textures.tsv" textures
        ;;
    *) echo "usage: $0 --fx [secondes] | --textures | --all" >&2; exit 2 ;;
esac
