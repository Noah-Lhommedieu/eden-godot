#!/usr/bin/env bash
# Vérifie l'état des DEUX villes de guilde de test (PLAN-ACTION-3, lot A5).
# Lecture seule. Usage : tools/fixtures/vdg-verify.sh [conteneur PG]
# Attendu : 5001 QAForge (node 502) = ferme/mine/autel/alchimie/fonderie niv. 12 ;
#           5000 livetest (node 501) = mare/hôtel/marché niv. 12 ; les deux ouvertes.
set -euo pipefail
C="${1:-edengodot-postgres}"
q() { docker exec "$C" psql -U postgres -d eden_live -At -F ' | ' -c "$1"; }
echo "== Guildes de test"
q "select id, name, lv as niveau, house_open as node_ville, recycled_time from guild.family where id in (5000,5001) order by id"
echo "== Contenu des villes (type d'item 42=contrat PNJ/bâtiment ; tid=bâtiment ou PNJ)"
q "select family_id, item_type, count(*) as nb, min(level) as niv_min, max(level) as niv_max from guild.family_building_container where family_id in (5000,5001) group by 1,2 order by 1,2"
echo "== Bâtiments (loc multiple de 10 = emplacement de bâtiment)"
q "select family_id, loc, tid, level from guild.family_building_container where family_id in (5000,5001) and loc % 10 = 0 order by 1,2"
fail=0
for f in 5000 5001; do
  open=$(q "select house_open from guild.family where id=$f")
  [ -n "$open" ] && [ "$open" != "0" ] || { echo "ECHEC : ville $f fermée"; fail=1; }
  n=$(q "select count(*) from guild.family_building_container where family_id=$f")
  [ "${n:-0}" -ge 5 ] || { echo "ECHEC : ville $f a $n objets (< 5)"; fail=1; }
done
[ $fail -eq 0 ] && echo "OK : les deux villes sont prêtes" || exit 1
