#!/usr/bin/env python3
"""Inventaire des tables de la base de jeu (PLAN-ACTION-3, lot DB-A).

Pour chaque table : lignes, taille, commentaires, références dans le code
serveur, et DÉCISION proposée (politique 2026-11-07 : une table morte est
supprimée ; une table utile mais peu rentable est fusionnée/migrée ; sinon
garder). Lecture seule : n'écrit rien en base.

Usage : tools/db/inventory.py [--container edengodot-postgres] [--db eden_live]
        [--out docs/reference/DB-INVENTORY.md]
"""
import argparse, os, re, subprocess, sys

def psql(container, db, sql):
    r = subprocess.run(["docker", "exec", container, "psql", "-U", "postgres", "-d", db,
                        "-At", "-F", "\t", "-c", sql], capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.strip().split("\n") if l]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--container", default="edengodot-postgres")
    ap.add_argument("--db", default="eden_live")
    ap.add_argument("--out", default="docs/reference/DB-INVENTORY.md")
    a = ap.parse_args()
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    src = os.path.join(root, "eden_server", "server")
    blob = []
    for base in ("src", "tools"):
        for dp, dn, fn in os.walk(os.path.join(src, base)):
            if "/obj" in dp or "/bin" in dp: continue
            for f in fn:
                if f.endswith((".cs", ".sql")):
                    blob.append(open(os.path.join(dp, f), encoding="utf-8", errors="ignore").read())
    for f in os.listdir(os.path.join(root, "eden_server", "Database")):
        pass
    code = "\n".join(blob)
    tables = psql(a.container, a.db, """
      select n.nspname, c.relname, coalesce(s.n_live_tup,0), pg_total_relation_size(c.oid),
             coalesce(obj_description(c.oid),''),
             (select count(*) from pg_attribute at where at.attrelid=c.oid and at.attnum>0 and not at.attisdropped),
             (select count(*) from pg_attribute at where at.attrelid=c.oid and at.attnum>0 and not at.attisdropped
                and col_description(c.oid, at.attnum) is not null)
      from pg_class c join pg_namespace n on n.oid=c.relnamespace
      left join pg_stat_user_tables s on s.relid=c.oid
      where c.relkind='r' and n.nspname not in ('pg_catalog','information_schema')
      order by 1,2""")
    # Tables dont dépend une vue ou une clé étrangère : jamais « mortes » (pg_depend).
    dep = {r[0] for r in psql(a.container, a.db, """
      select distinct src.relnamespace::regnamespace||'.'||src.relname
      from pg_depend d join pg_rewrite r on r.oid=d.objid join pg_class dep on dep.oid=r.ev_class
      join pg_class src on src.oid=d.refobjid where src.relkind='r' and dep.oid<>src.oid
      union select c.confrelid::regclass::text from pg_constraint c where c.contype='f'""")}
    rows = []
    for sch, t, n, size, com, cols, ccols in tables:
        exact = psql(a.container, a.db, f'select count(*) from "{sch}"."{t}"')[0][0]
        refs = len(re.findall(r"\b" + re.escape(t) + r"\b", code))
        if f"{sch}.{t}" in dep or t in dep: refs += 1   # vue / clé étrangère dépendante
        n = int(exact)
        if refs == 0 and n == 0: dec = "SUPPRIMER (morte : 0 ligne, 0 référence)"
        elif refs == 0: dec = f"A EXAMINER ({n} lignes, 0 référence) : migrer ou supprimer"
        elif n == 0 and refs <= 2: dec = "A EXAMINER (vide, peu référencée) : fusionner ou supprimer"
        else: dec = "GARDER (renommer/commenter)"
        rows.append((sch, t, n, int(size), refs, bool(com), int(cols), int(ccols), dec))
    out = os.path.join(root, a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        f.write("# Inventaire des tables (généré par tools/db/inventory.py)\n\n")
        f.write(f"Base `{a.db}` — {len(rows)} tables.\n\n")
        kill = sum(1 for r in rows if r[8].startswith("SUPPRIMER"))
        look = sum(1 for r in rows if r[8].startswith("A EXAMINER"))
        f.write(f"- à supprimer : **{kill}** ; à examiner : **{look}** ; à garder : **{len(rows)-kill-look}**\n")
        f.write(f"- tables commentées : {sum(1 for r in rows if r[5])}/{len(rows)} ; colonnes commentées : "
                f"{sum(r[7] for r in rows)}/{sum(r[6] for r in rows)}\n\n")
        f.write("| schéma | table | lignes | octets | réf. code | commentée | col. comm. | décision |\n|---|---|---|---|---|---|---|---|\n")
        for r in rows:
            f.write(f"| {r[0]} | {r[1]} | {r[2]} | {r[3]} | {r[4]} | {'oui' if r[5] else 'non'} | {r[7]}/{r[6]} | {r[8]} |\n")
    print(f"écrit {out} : {len(rows)} tables, {kill} à supprimer, {look} à examiner")

main()
