#!/usr/bin/env bash
# package_dist.sh — assemble dist/package-{linux,windows}/ autonomes.
#
# Chaque package contient l'export Godot (binaire + .pck + data_*) et
# un dossier gamedata/ avec TOUTES les données runtime lues par le
# client (voir scripts/DataPaths.cs) :
#
#   gamedata/uidata/          CEGUI natif (layouts, imagesets,
#                             looknfeel, schemes, fonts, *.ttf)
#   gamedata/data/{db,scene}/ tables ini + scènes de maps
#   gamedata/map/model/Sxxx/  Sxxx.{fsm,ini,area} + Dark/ (PAS les .nif
#                             — les modèles convertis sont dans assets)
#   gamedata/map/sknodemap/   Sxxx.smp — murs invisibles lus par
#                             MapLoader (les .fsmp/.pgp ne sont pas
#                             utilisés par le client)
#   gamedata/assets -> symlink ../../.. /godot_project/assets (dev)
#                      ou copie réelle avec --full (22 Go)
#   gamedata/client.cfg       config connexion éditable
#   + overlay gamedata/ du projet : traductions FR/EN
#     (data/db/lang/, ui_text_*.tsv), levels.tsv, login_bg.jpg,
#     settings.cfg — ajoutés en dev au-dessus du corpus raw_assets,
#     requis par le client packagé (sinon anglais forcé + trous).
#
# Mode natif (ADR-0005) : tools/package_dist.sh --native-epk <outdir> --sign-key F
#   [--keyfile F] [--version N]  -> bootstrap/eden-core/eden-assets/eden-audio
#   .epk + provenance.tsv depuis corpus/fr-full (aucun dérivé), via
#   `edenpack build --native`. Ne touche pas à dist/.
#
# Usage : tools/package_dist.sh [--full] [--target linux|windows]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
RAW="$ROOT/raw_assets"           # symlink → client Reforged extrait
PROJ="$ROOT/godot_project"
FULL=0
TARGETS=(linux windows)

if [ "${1:-}" = "--native-epk" ]; then
    shift
    [ $# -ge 1 ] || { echo "--native-epk <outdir> [options edenpack]" >&2; exit 2; }
    out="$1"; shift
    bin="$(mktemp -d)"
    dotnet build "$ROOT/tools/edenpack/edenpack.csproj" -c Release \
        -o "$bin" -v q >/dev/null
    dotnet "$bin/edenpack.dll" build --native "$out" "$@"
    rc=$?; rm -rf "$bin"; exit $rc
fi

while [ $# -gt 0 ]; do
    case "$1" in
        --full) FULL=1 ;;
        --target) shift; TARGETS=("$1") ;;
        -h|--help)
            sed -n '2,20p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "option inconnue : $1" >&2; exit 2 ;;
    esac
    shift
done

[ -d "$RAW/uidata" ] || { echo "raw_assets/uidata absent — $RAW" >&2; exit 1; }

copy_map_dir() {
    # $1 = dossier source Sxxx, $2 = dossier gamedata/map/model
    local src="$1" dst="$2" name
    name="$(basename "$src")"
    mkdir -p "$dst/$name"
    # Tous les .fsm/.ini/.area de premier niveau (la casse varie :
    # S009.fsm vs s009.ini ; des sous-cartes type S018/S101.ini aussi).
    find "$src" -maxdepth 1 -type f \
        \( -iname '*.fsm' -o -iname '*.ini' -o -iname '*.area' \) \
        -exec cp -a -t "$dst/$name" {} +
    # Dark/ = minimaps d'ombre (74 Mo total) — pas de .nif ici.
    if [ -d "$src/Dark" ]; then cp -a "$src/Dark" "$dst/$name/"; fi
    if [ -d "$src/dark" ]; then cp -a "$src/dark" "$dst/$name/"; fi
    return 0
}

build_pkg() {
    local target="$1" src pkg gd
    src="$DIST/$target"
    pkg="$DIST/package-$target"
    gd="$pkg/gamedata"
    [ -d "$src" ] || { echo "!! $src absent — export manquant" >&2; return 1; }

    echo "== package-$target =="
    rm -rf "$pkg"
    mkdir -p "$gd/map/model"
    cp -a "$src"/. "$pkg"/

    # uidata + data : vrais fichiers (déréférence le symlink racine).
    cp -aL "$RAW/uidata" "$gd/uidata"
    cp -aL "$RAW/data"   "$gd/data"

    # map/model/Sxxx : .fsm/.ini/.area + Dark/ uniquement.
    local n=0 d
    for d in "$RAW"/map/model/[Ss][0-9]*/; do
        [ -d "$d" ] || continue
        copy_map_dir "$d" "$gd/map/model"
        n=$((n + 1))
    done
    echo "   $n maps copiées"

    # map/sknodemap : murs invisibles (.smp uniquement — fsmp/pgp
    # jamais lus par le client, ~60 Mo économisés).
    mkdir -p "$gd/map/sknodemap"
    find "$RAW/map/sknodemap" -maxdepth 1 -type f -iname '*.smp' \
        -exec cp -a -t "$gd/map/sknodemap" {} + 2>/dev/null || true

    # assets convertis : symlink dev-local par défaut, copie avec --full.
    if [ "$FULL" -eq 1 ]; then
        cp -aL "$PROJ/assets" "$gd/assets"
    else
        ln -s ../../../godot_project/assets "$gd/assets"
    fi

    # Config serveur éditable.
    cp -a "$PROJ/gamedata/client.cfg" "$gd/client.cfg"

    # Couche dev gamedata/ : traductions (data/db/lang/, ui_text_*.tsv),
    # levels.tsv, login_bg.jpg, settings.cfg — le client les lit en
    # loose en dev ; sans eux le packagé tombe en anglais + trous.
    cp -a "$PROJ/gamedata/." "$gd/"

    # Tables binaires .inib à côté des .ini (le client les préfère,
    # repli .ini sinon). Perdus à chaque rebuild sans ça.
    local epk="$ROOT/tools/edenpack/bin/Release/net8.0/edenpack"
    if [ -x "$epk" ]; then
        "$epk" bake-tables "$gd" >/dev/null \
            && echo "   .inib bakés" \
            || echo "   !! bake-tables a échoué (repli .ini)" >&2
    else
        echo "   .. edenpack Release absent — .inib non bakés" \
             "(repli .ini)" >&2
    fi

    # --- vérification : toutes les racines lues à l'exécution -------
    local missing=0
    for sub in uidata/layouts uidata/imagesets uidata/schemes \
               uidata/looknfeel data/db data/scene client.cfg \
               map/sknodemap data/db/lang ui_text_en_fr.tsv \
               levels.tsv; do
        if [ ! -e "$gd/$sub" ]; then
            echo "   !! MANQUANT gamedata/$sub" >&2
            missing=1
        fi
    done
    if [ "$FULL" -eq 1 ]; then
        for sub in assets/textures assets/models assets/anims; do
            [ -e "$gd/$sub" ] || { echo "   !! MANQUANT gamedata/$sub" >&2; missing=1; }
        done
    else
        if [ -L "$gd/assets" ]; then
            echo "   .. assets = symlink dev-local -> " \
                "$(readlink "$gd/assets") (package final : --full)"
        elif [ ! -e "$gd/assets" ]; then
            echo "   !! MANQUANT gamedata/assets" >&2
            missing=1
        fi
    fi
    [ "$missing" -eq 0 ] || return 1

    du -sh "$pkg" | awk -v t="$target" '{print "   taille "t" : "$1}'
}

for t in "${TARGETS[@]}"; do
    build_pkg "$t"
done
echo "OK — packages dans $DIST/package-{linux,windows}"
