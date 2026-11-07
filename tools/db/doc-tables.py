#!/usr/bin/env python3
"""Génère les COMMENT ON TABLE factuels des tables non commentées (PLAN-ACTION-3, DB-C).

Le texte vient de faits vérifiables : fichiers C# du serveur qui citent la table, nombre de lignes, colonnes.
Les tables jamais citées par le code sont listées à part (candidates à la suppression, décision du propriétaire).
Usage : tools/db/doc-tables.py --out eden_server/Database/031-table-comments.sql
"""
import argparse, subprocess, sys, re, os

def psql(sql):
    r = subprocess.run(["docker", "exec", "edengodot-postgres", "psql", "-U", "postgres", "-d", "eden_live", "-At", "-F", "\t", "-c", sql],
                       capture_output=True, text=True)
    if r.returncode: sys.exit(r.stderr)
    return [l.split("\t") for l in r.stdout.strip().split("\n") if l]

ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args()
root = "eden_server/server/src"
tables = [t[0] for t in psql("""select n.nspname||'.'||c.relname from pg_class c join pg_namespace n on n.oid=c.relnamespace
  where c.relkind='r' and n.nspname in ('character','inventory','market','guild','social','progress','ranking','lottery','system','admin','account','member') and obj_description(c.oid) is null order by 1""")]
sources = {}
for d, _, fs in os.walk(root):
    if "/bin" in d or "/obj" in d: continue
    for f in fs:
        if f.endswith(".cs"):
            p = os.path.join(d, f); sources[p] = open(p, encoding="utf-8", errors="ignore").read().lower()
out, unused = [], []
for t in tables:
    sch, name = t.split(".")
    pat = re.compile(r"(?<![a-z_])(?:%s\.)?%s(?![a-z_])" % (sch, re.escape(name)))
    refs = sorted({os.path.basename(p) for p, s in sources.items() if pat.search(s)})
    n = psql(f"select count(*) from {t}")[0][0]
    cols = [c[0] for c in psql(f"select column_name from information_schema.columns where table_schema='{sch}' and table_name='{name}' order by ordinal_position")]
    if not refs: unused.append(f"{t} ({n} lignes)"); text = f"Table non citée par le code du serveur (au 2026-11-07), {n} lignes ; candidate à la suppression ou à la fusion. Colonnes : {', '.join(cols[:8])}."
    else: text = f"Table utilisée par le serveur ({', '.join(refs[:5])}{'…' if len(refs) > 5 else ''}) ; {n} lignes au 2026-11-07. Colonnes : {', '.join(cols[:8])}{'…' if len(cols) > 8 else ''}."
    out.append(f"COMMENT ON TABLE {t} IS '{text.replace(chr(39), chr(39)*2)}';")
open(a.out, "w", encoding="utf-8").write("-- 031 : commentaires factuels des tables (générés par tools/db/doc-tables.py ; usage = fichiers C# qui citent la table).\nBEGIN;\n" + "\n".join(out) + "\nCOMMIT;\n")
print(f"{len(out)} commentaires ; non citées par le code : {len(unused)}")
for u in unused: print("  ", u)
