#!/usr/bin/env python3
"""Export de secours de eden_content (PLAN-ACTION-3 B0) : content/export/<schéma>.<table>.jsonl, trié et déterministe.
Permet de relire le contenu sans base et de voir un diff lisible. Usage : tools/content/export.py [--out content/export]"""
import argparse, json, os, subprocess, sys

TABLES = {"item.item": "item_id", "creature.monster": "monster_id", "npc.npc": "npc_id", "quest.quest": "quest_id", "map.map": "node_id",
          "craft.recipe": "recipe_id", "craft.recipe_material": "recipe_id, position", "creature.monster_drop": "monster_id, pool, position",
          "npc.shop_item": "store_id, position", "quest.quest_reward": "quest_id, position",
          "text.translation": "entity_type, entity_id, field, lang", "text.glossary": "fr_term, lang"}
ap = argparse.ArgumentParser(); ap.add_argument("--out", default="content/export"); a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
for t, pk in TABLES.items():
    sql = f"select row_to_json(x) from (select * from {t} order by {pk}) x"
    r = subprocess.run(["docker", "exec", "edengodot-postgres", "psql", "-U", "postgres", "-d", "eden_content", "-At", "-c", sql], capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    lines = [json.dumps(json.loads(l), ensure_ascii=False, sort_keys=True) for l in r.stdout.splitlines() if l]
    open(os.path.join(a.out, f"{t}.jsonl"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print(f"{t}: {len(lines)} lignes")
