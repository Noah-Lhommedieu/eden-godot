#!/usr/bin/env python3
"""Inventaire de traduction EN (PLAN-ACTION-3 lot E) : couverture par type (vue text.v_translation_status, périmètre ≤ Lv90) et
extraction de ce qui reste à traduire dans content/lang/en/to_translate.tsv (entity_type, id, fr, contexte). Usage : tools/content/lang-inventory.py [--no-file]"""
import subprocess, sys, os

def psql(sql):
    r = subprocess.run(["docker", "exec", "edengodot-postgres", "psql", "-U", "postgres", "-d", "eden_content", "-At", "-F", "\t", "-c", sql], capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.splitlines() if l]

cov = {}
for t, s, n in psql("select entity_type, status, count(*) from text.v_translation_status group by 1,2 order by 1,2"):
    cov.setdefault(t, {})[s] = int(n)
print(f"{'type':9} {'total':>6} {'missing':>8} {'outdated':>9} {'auto':>6} {'relu':>6} {'validé':>7}   couverture")
for t, d in cov.items():
    tot = sum(d.values()); done = tot - d.get("missing", 0)
    print(f"{t:9} {tot:6} {d.get('missing',0):8} {d.get('outdated',0):9} {d.get('auto',0):6} {d.get('reviewed',0):6} {d.get('validated',0):7}   {100*done/tot:5.1f} %")
if "--no-file" not in sys.argv:
    rows = psql("""select s.entity_type, s.entity_id, s.name_fr,
        case s.entity_type when 'item' then 'niv ' || i.required_level || ' kind ' || i.item_kind when 'monster' then 'niv ' || m.level when 'quest' then 'niv ' || q.min_level else '' end
      from text.v_translation_status s
      left join item.item i on s.entity_type='item' and i.item_id=s.entity_id
      left join creature.monster m on s.entity_type='monster' and m.monster_id=s.entity_id
      left join quest.quest q on s.entity_type='quest' and q.quest_id=s.entity_id
      where s.status in ('missing','outdated') order by 1,2""")
    os.makedirs("content/lang/en", exist_ok=True)
    with open("content/lang/en/to_translate.tsv", "w", encoding="utf-8") as f:
        f.write("entity_type\tentity_id\tfr\tcontext\n")
        for r in rows: f.write("\t".join((r + [""] * 4)[:4]) + "\n")
    print(f"{len(rows)} lignes à traduire -> content/lang/en/to_translate.tsv")
