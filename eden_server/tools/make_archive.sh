#!/usr/bin/env bash
# tools/make_archive.sh — archive complète et autonome du dépôt eden_server.
#
# Produit <PREFIX>-<date>.tar.zst (Linux, zstd) et <PREFIX>-<date>.zip
# (Windows, Info-ZIP) dans archives/ à la racine du dépôt.
#
# Usage :
#   tools/make_archive.sh                    # tout (défaut : avec .git, avec reforged/)
#   tools/make_archive.sh --no-reforged      # sans reforged/ (client Reforged + préfixe, -29 Go)
#   tools/make_archive.sh --no-git           # sans .git/
#   tools/make_archive.sh --tar-only|--zip-only
#   tools/make_archive.sh --list|--dry-run   # contenu + taille estimée, rien d'écrit
#   tools/make_archive.sh -o /chemin/sortie  # dossier de sortie
#
# Exclusions (build/transitoire, régénérables ou secrets) :
#   */bin */obj */node_modules */.next */__pycache__ *.pyc
#   NoName_*.zip (dumps de crash client) eden_crash/ eden_d3d_*.log
#   .env* (sauf .env.example) dbdata/ pgdata/ archives/ ghidra_projects/
set -euo pipefail
EDEN_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="$EDEN_ROOT/archives"
PREFIX="eden_server"
STAMP="$(date +%Y%m%d-%H%M)"
DO_TAR=1; DO_ZIP=1; WITH_GIT=1; WITH_REFORGED=1; DRY_RUN=0

while [ $# -gt 0 ]; do
    case "$1" in
        --no-reforged) WITH_REFORGED=0 ;;
        --no-git)      WITH_GIT=0 ;;
        --tar-only)    DO_ZIP=0 ;;
        --zip-only)    DO_TAR=0 ;;
        --list|--dry-run) DRY_RUN=1 ;;
        -o|--out)      OUT_DIR="$(realpath -m "$2")"; shift ;;
        -h|--help)     sed -n '2,22p' "$0"; exit 0 ;;
        *) echo "option inconnue: $1" >&2; exit 2 ;;
    esac
    shift
done

# Entrées de premier niveau toujours exclues (hors --no-* dynamiques).
TOP_EXCLUDE=(dbdata pgdata archives ghidra_projects lost+found .env __pycache__)
[ "$WITH_GIT" = 0 ]      && TOP_EXCLUDE+=(.git)
[ "$WITH_REFORGED" = 0 ] && TOP_EXCLUDE+=(reforged)

# Motifs imbriqués — communs tar/zip/find (glob basename ou chemin).
NESTED=(
    'bin' 'obj' 'node_modules' '.next' '__pycache__' '.venv' 'venv'
    '*.pyc' 'NoName_*.zip' 'eden_crash' 'eden_d3d_*.log'
    '.env' 'winedbg-crash.log'
)

# --- membres de premier niveau retenus ---
members=()
while IFS= read -r e; do
    skip=0
    for x in "${TOP_EXCLUDE[@]}"; do [ "$e" = "$x" ] && skip=1; done
    [ "$skip" = 0 ] && members+=("$e")
done < <(cd "$EDEN_ROOT" && ls -A | sort)

# --- estimateur : somme des tailles après mêmes exclusions (stat seul) ---
estimate() {
    local target="$1" prunes=() p
    for p in "${NESTED[@]}"; do prunes+=( -o -iname "$p" ); done
    find "$EDEN_ROOT/$target" -xdev \( -iname 'x-never' "${prunes[@]}" \) -prune \
        -o -type f -printf '%s\n' 2>/dev/null \
        | awk '{s+=$1} END{printf "%.0f", s+0}'
}
human() { awk -v b="$1" 'BEGIN{s="BKMG";i=1;while(b>=1024&&i<4){b/=1024;i++}printf "%.1f%c",b,substr(s,i,1)}'; }

if [ "$DRY_RUN" = 1 ]; then
    total=0
    echo "== Contenu de l'archive (exclusions appliquées) =="
    printf '%-34s %10s\n' MEMBRE TAILLE
    for m in "${members[@]}"; do
        if [ -f "$EDEN_ROOT/$m" ]; then
            sz=$(stat -c %s "$EDEN_ROOT/$m")
        else
            sz=$(estimate "$m")
        fi
        total=$((total + sz))
        printf '%-34s %10s\n' "$m" "$(human "$sz")"
    done
    printf '%-34s %10s\n' '--- TOTAL estimé ---' "$(human "$total")"
    echo
    echo "Exclus 1er niveau : ${TOP_EXCLUDE[*]}"
    echo "Exclus imbriqués  : ${NESTED[*]}"
    echo "Sorties prévues   : $OUT_DIR/${PREFIX}-${STAMP}.tar.zst + .zip"
    exit 0
fi

mkdir -p "$OUT_DIR"
cd "$EDEN_ROOT"

# --- motifs d'exclusion au format tar ---
tar_ex=()
for p in "${NESTED[@]}"; do
    tar_ex+=(--exclude="$p" --exclude="*/$p" --exclude="*/*/$p" --exclude="*/*/*/$p")
    # couvre basename et profondeurs ; * /$p couvre les contenus de dossiers
    tar_ex+=(--exclude="*/$p/*" --exclude="*/*/$p/*" --exclude="*/*/*/$p/*")
done

if [ "$DO_TAR" = 1 ]; then
    TAR_OUT="$OUT_DIR/${PREFIX}-${STAMP}.tar.zst"
    echo "[tar.zst] -> $TAR_OUT"
    tar --zstd -cf "$TAR_OUT" "${tar_ex[@]}" "${members[@]}"
fi

if [ "$DO_ZIP" = 1 ]; then
    ZIP_OUT="$OUT_DIR/${PREFIX}-${STAMP}.zip"
    echo "[zip] -> $ZIP_OUT"
    zip_ex=()
    for p in "${NESTED[@]}"; do
        zip_ex+=(-x "$p" "*/$p" "*/$p/*" "*/*/$p" "*/*/$p/*" "*/*/*/$p" "*/*/*/$p/*")
    done
    zip -qr "$ZIP_OUT" "${members[@]}" "${zip_ex[@]}"
fi

ls -lh "$OUT_DIR/${PREFIX}-${STAMP}".* 2>/dev/null
echo "Terminé."
