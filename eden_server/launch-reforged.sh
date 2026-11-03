#!/usr/bin/env bash
# Lance le client Eden Reforged SANS le launcher Electron, DEPUIS le
# dossier du dépôt (reforged/EdenReforged/) avec le préfixe Wine local
# (reforged/prefix/ — copie du préfixe Lutris sans le jeu).
#
# Preuve (rf_game/decompiled.c ~514100) : le client fait
# strstr(GetCommandLineA(), "LogLog") — sans cet argument il affiche une
# MessageBox et quitte. Le launcher ne fait que `spawn(game_.exe, ['LogLog'])`
# (app.asar/main.js:728) — aucun jeton de session n'est transmis, le login
# se fait dans le jeu (écran de connexion classique).
#
# connect.ini pointé sur notre serveur : Server=127.0.0.1,6543.
#
# IMPORTANT — ALAudio.dll normalise son chemin en \\?\Z:\... puis exige
# pour chaque composant GetFileAttributesW & 0x410 == 0x10 (dossier NON
# reparse point) — Wine rapporte les symlinks Unix comme reparse → échec
# « Place clal.xmf beside alaudio.dll » si un composant du chemin du jeu
# est un symlink (c'était le cas de users/ster2lune → steamuser dans le
# préfixe Lutris). Ici le jeu est lancé via Z:\ (mapping Unix de Wine) :
# le chemin du dépôt ne doit contenir AUCUN symlink — vérifié ci-dessous.
set -euo pipefail

EDEN_ROOT="$(cd "$(dirname "$0")" && pwd -P)"
GAME_DIR="$EDEN_ROOT/reforged/EdenReforged"
PREFIX="$EDEN_ROOT/reforged/prefix"

# Runner Wine : wine-ge 8.26 (celui du préfixe Lutris, toujours présent
# hors dépôt) en priorité — Proton-CachyOS crashe au lancement sur
# win32u.NtGdiCreateColorSpace (unimplemented) depuis l'upgrade du
# préfixe. Surcharge possible via EDEN_WINE=/chemin/wine.
LUTRIS_WINE="${EDEN_WINE:-$HOME/.local/share/lutris/runners/wine/wine-ge-8-26-x86_64/bin/wine}"

# Garde-fou ALAudio : aucun composant du chemin du jeu ne doit être un
# symlink (sinon « Place clal.xmf beside alaudio.dll » au lancement).
if [ "$(readlink -f "$GAME_DIR")" != "$GAME_DIR" ]; then
    echo "ERREUR: le chemin du jeu contient un symlink — ALAudio le refusera." >&2
    echo "  résolu : $(readlink -f "$GAME_DIR")" >&2
    exit 1
fi
[ -d "$PREFIX/drive_c" ] || {
    echo "ERREUR: préfixe local absent : $PREFIX" >&2
    echo "  le recréer : rsync -a --exclude 'drive_c/users/steamuser/AppData/Local/EdenReforged' ~/Games/reforgeddecompiled/ reforged/prefix/" >&2
    exit 1
}

cd "$GAME_DIR"

# Le DXVK du préfixe (syswow64/d3d9.dll) = dxvk-sarek v1.10.3 : les dxvk
# récents (2.6.2/3.1.1) exigent VK_KHR_load_store_op_none que le driver
# NVIDIA 595 n'expose pas → « No adapters found ». Backup de l'ancienne
# dll : syswow64/d3d9.dll.bak-dxvk262.
if [[ -x "$LUTRIS_WINE" ]]; then
    exec env WINEPREFIX="$PREFIX" WINEDEBUG=-all \
        WINEDLLOVERRIDES="d3d9=native,builtin" "$LUTRIS_WINE" \
        game_.exe LogLog
elif command -v wine >/dev/null; then
    exec env WINEPREFIX="$PREFIX" WINEDEBUG=-all \
        WINEDLLOVERRIDES="d3d9=native,builtin" wine game_.exe LogLog
else
    echo "wine introuvable — EDEN_WINE=/chemin/wine ou installe wine-ge" >&2
    exit 1
fi
