#!/usr/bin/env bash
# Rollback « FR seul » (PLAN-ACTION-3 §6.4) : retire TOUTE traduction EN et le glossaire de eden_content, en prouvant que le contenu FR
# n'a pas bougé d'un octet (empreinte des colonnes FR avant / après). Usage : tools/content/rollback-fr-only.sh [--dry-run]
set -euo pipefail
Q() { docker exec -i edengodot-postgres psql -U postgres -d eden_content -At -c "$1"; }
FR_HASH_SQL="select md5(string_agg(x, '|' order by x)) from (
  select 'i'||item_id||name_fr||required_level||buy_price_silver as x from item.item
  union all select 'm'||monster_id||name_fr||level from creature.monster
  union all select 'n'||npc_id||name_fr from npc.npc
  union all select 'q'||quest_id||name_fr from quest.quest
  union all select 'c'||node_id||name_fr from map.map) t"
before=$(Q "$FR_HASH_SQL"); en=$(Q "select count(*) from text.translation where lang='en'")
echo "FR avant : $before ; traductions EN : $en"
[ "${1:-}" = "--dry-run" ] && { echo "(simulation : rien supprimé)"; exit 0; }
Q "begin; delete from text.translation where lang='en'; delete from text.glossary where lang='en'; commit;" >/dev/null
after=$(Q "$FR_HASH_SQL"); left=$(Q "select count(*) from text.translation")
echo "FR après : $after ; traductions restantes : $left"
[ "$before" = "$after" ] && [ "$left" = "0" ] && echo "OK : retour FR seul, contenu FR identique" || { echo "ÉCHEC : le contenu FR a changé"; exit 1; }
