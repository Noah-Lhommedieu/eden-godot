#!/usr/bin/env bash
# Prépare (ou répare) les DEUX villes de guilde de test via les commandes GM,
# de façon rejouable (PLAN-ACTION-3, lot A5). Nécessite un client lancé avec
#   tools/run.sh -- --login=test123:test123 --channel --char --cmd-file=/tmp/gm.cmd
# et le perso admin « ClericQA » en jeu. Les commandes sont ajoutées au fichier GM,
# puis `vdg-verify.sh` valide l'état.
set -euo pipefail
CMD="${EDEN_GM_FILE:-/tmp/gm.cmd}"
[ -f "$CMD" ] || { echo "fichier $CMD absent : lancer le client avec --cmd-file" >&2; exit 1; }
send() { echo "$1" >> "$CMD"; sleep "${2:-2}"; }
# Niveau de guilde 12 pour les deux (le maximum natif).
send "//guild_level 5001 12"
send "//guild_level 5000 12"
# Ville A : ferme, mine, autel, alchimie, fonderie (niv. 12), PNJ de service au cap.
send "//hall_setup 5001 reset rebuild" 4
# Ville B : mare magique, hôtel de brique, marché (niv. 12), PNJ de la mare.
send "//hall_setup 5000 reset rebuild set2" 4
# Kit de test du personnage : outils, plans et matériaux (tables FR, voir vdg-kit.py).
send "//level 90" 2
send "//give_gold 100000" 2
while read -r line; do send "$line" 1; done < <("$(dirname "$0")/vdg-kit.py")
echo "commandes envoyées ; vérifier avec : tools/fixtures/vdg-verify.sh"
