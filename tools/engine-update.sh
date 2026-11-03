#!/usr/bin/env bash
# Met à jour le moteur Godot épinglé, puis valide (import + build + tests).
# Rollback automatique si une étape échoue.
#
# Usage : tools/engine-update.sh <Godot_v*_mono_linux_x86_64.zip> [templates.tpz]
#
# Étapes : décompresse dans engine/<dossier> ; lit la version ; bascule
# engine/current ; réécrit le Sdk du csproj (version du nupkg fourni) et
# GODOT_EXPECTED d'env.sh ; installe les templates ; vide .godot/mono ;
# import headless ; build ; tests (sans serveur). Voir ADR-0001.
set -euo pipefail
source "$(dirname "$0")/env.sh"
zip="${1:?zip moteur requis}"; tpz="${2:-}"
[ -f "$zip" ] || eden_die "introuvable : $zip"
mkdir -p "$ARTIFACTS/logs"

prev="$(readlink "$EDEN_ROOT/engine/current")"
prev_sdk="$(grep -o 'Godot.NET.Sdk/[^"]*' "$CLIENT_CSPROJ")"
prev_expected="$GODOT_EXPECTED"

dir="$(unzip -Z1 "$zip" | sed -n '1s#/.*##p')"   # sed lit tout : pas de SIGPIPE sous pipefail
eden_log "décompression $dir"
unzip -q -o "$zip" -d "$EDEN_ROOT/engine"
bin="$(find "$EDEN_ROOT/engine/$dir" -maxdepth 1 -type f -name 'Godot_v*' | head -1)"
[ -x "$bin" ] || eden_die "binaire introuvable dans engine/$dir"
ln -sfn "$(basename "$bin")" "$EDEN_ROOT/engine/$dir/godot"
ver="$("$bin" --version)"                         # ex. 4.8.beta2.mono.official.cafc17a19
expected="$(echo "$ver" | cut -d. -f1-3).mono"     # ex. 4.8.beta2.mono
nupkg="$(ls "$EDEN_ROOT/engine/$dir/GodotSharp/Tools/nupkgs/"Godot.NET.Sdk.*.nupkg | head -1)"
sdk="$(basename "$nupkg" .nupkg | sed 's/^Godot.NET.Sdk.//')"
eden_log "moteur $ver — SDK $sdk (avant : $prev / $prev_sdk)"

rollback() {
    eden_log "ÉCHEC — rollback vers $prev"
    ln -sfn "$prev" "$EDEN_ROOT/engine/current"
    sed -i "s#Godot.NET.Sdk/[^\"]*#$prev_sdk#" "$CLIENT_CSPROJ"
    sed -i "s#^export GODOT_EXPECTED=.*#export GODOT_EXPECTED=\"$prev_expected\"          \# préfixe de \`godot --version\`#" "$EDEN_ROOT/tools/env.sh"
    rm -rf "$PROJECT_DIR/.godot/mono"
    exit 1
}
trap rollback ERR

ln -sfn "$dir" "$EDEN_ROOT/engine/current"
sed -i "s#Godot.NET.Sdk/[^\"]*#Godot.NET.Sdk/$sdk#" "$CLIENT_CSPROJ"
sed -i "s#^export GODOT_EXPECTED=.*#export GODOT_EXPECTED=\"$expected\"          \# préfixe de \`godot --version\`#" "$EDEN_ROOT/tools/env.sh"
if [ -n "$tpz" ]; then
    t="$HOME/.local/share/godot/export_templates/$expected"
    mkdir -p "$t" && unzip -q -o "$tpz" -d "$t"
    [ -d "$t/templates" ] && { mv "$t"/templates/* "$t"/; rmdir "$t/templates"; }
fi
rm -rf "$PROJECT_DIR/.godot/mono"
eden_log "import headless"
(cd "$PROJECT_DIR" && timeout 900 "$EDEN_ROOT/engine/current/godot" --headless --path . --import) \
    > "$ARTIFACTS/logs/engine-import-$(eden_ts).log" 2>&1 || true
"$EDEN_ROOT/tools/build.sh"
"$EDEN_ROOT/tools/test.sh" --no-server
trap - ERR
eden_log "ok : moteur $ver actif. Ancien moteur conservé : engine/$prev"
eden_log "Étapes suivantes : tools/benchmark.sh S001 puis tools/compare.sh, tools/capture.sh S001."
