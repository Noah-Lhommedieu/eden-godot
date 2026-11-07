#!/usr/bin/env python3
"""Contrôle de la documentation de la base (PLAN-ACTION-3, lot DB-C).

Règle du propriétaire : TOUTES les tables et TOUTES les colonnes sont commentées, aucune table morte.
Affiche les manques ; avec --strict, sort en erreur s'il en reste (et à la baisse seulement, via
--baseline <fichier> : le nombre de manques ne doit pas augmenter).

Usage : tools/db/doc-lint.py [--container edengodot-postgres] [--db eden_live] [--strict] [--baseline f]
"""
import argparse, subprocess, sys

def psql(c, db, sql):
    r = subprocess.run(["docker", "exec", c, "psql", "-U", "postgres", "-d", db, "-At", "-F", "\t", "-c", sql],
                       capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.strip().split("\n") if l]

ap = argparse.ArgumentParser()
ap.add_argument("--container", default="edengodot-postgres"); ap.add_argument("--db", default="eden_live")
ap.add_argument("--strict", action="store_true"); ap.add_argument("--baseline")
a = ap.parse_args()
SCH = "('item','creature','npc','quest','map','text','rule','calendar','history')" if a.db == "eden_content" else "('character','inventory','market','guild','social','progress','ranking','lottery','system','admin','account','member')"
tables = psql(a.container, a.db, f"""select n.nspname||'.'||c.relname, obj_description(c.oid) is not null
  from pg_class c join pg_namespace n on n.oid=c.relnamespace where c.relkind='r' and n.nspname in {SCH} order by 1""")
cols = psql(a.container, a.db, f"""select n.nspname||'.'||c.relname||'.'||at.attname, col_description(c.oid, at.attnum) is not null
  from pg_attribute at join pg_class c on c.oid=at.attrelid join pg_namespace n on n.oid=c.relnamespace
  where c.relkind='r' and at.attnum>0 and not at.attisdropped and n.nspname in {SCH}""")
bad_t = [t for t, ok in tables if ok == "f"]
bad_c = [c for c, ok in cols if ok == "f"]
weak = [c for c, ok in psql(a.container, a.db, f"""select n.nspname||'.'||c.relname||'.'||at.attname, (col_description(c.oid, at.attnum) like '%non documenté%')::text from pg_attribute at join pg_class c on c.oid=at.attrelid join pg_namespace n on n.oid=c.relnamespace where c.relkind='r' and at.attnum>0 and not at.attisdropped and n.nspname in {SCH}""") if ok == 'true']
bad_name = [t for t, _ in tables if t.split(".")[1].endswith(("_old", "_bak", "_tmp", "_copy")) or "test" in t.split(".")[1]]
print(f"tables sans commentaire : {len(bad_t)}/{len(tables)} ; colonnes sans commentaire : {len(bad_c)}/{len(cols)} ; noms suspects : {len(bad_name)} ; colonnes « sens non documenté » (dette) : {len(weak)}")
for t in bad_t[:15]: print("  table  ", t)
if len(bad_t) > 15: print(f"  … {len(bad_t) - 15} autres")
for t in bad_name: print("  nom    ", t)
total = len(bad_t) + len(bad_c) + len(bad_name)
if a.baseline:
    try: base = int(open(a.baseline).read().strip())
    except FileNotFoundError: base = None
    if base is None or total < base: open(a.baseline, "w").write(str(total)); print(f"baseline = {total}")
    elif total > base: print(f"ECHEC : {total} manques > baseline {base}"); sys.exit(1)
elif a.strict and total: sys.exit(1)
