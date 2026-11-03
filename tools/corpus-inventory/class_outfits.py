#!/usr/bin/env python3
"""Jointure skins de classe : c_class.ini (code tenue par classe/palier)
x 4pequip.ini (code -> mesh+texture par corps) x char/texture/*.dds
x t_class.ini (nom FR). Sortie : artifacts/corpus-inventory/class-outfits.tsv"""
import os, re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DB = os.path.join(ROOT, "corpus", "fr-full", "data", "db")
TEX = os.path.join(ROOT, "corpus", "fr-full", "char", "texture")

def read_ini(path, enc):
    with open(path, "rb") as f:
        return f.read().decode(enc, "replace")

# noms FR des classes
names = {}
for l in read_ini(os.path.join(DB, "t_class.ini"), "cp1252").splitlines():
    p = l.split("|")
    if p and p[0].isdigit() and len(p) > 1:
        names[int(p[0])] = p[1]

# 4pequip : corps -> {code -> (mesh, texture)}
bodies = {}
cur = None
for l in read_ini(os.path.join(DB, "4pequip.ini"), "big5").splitlines():
    m = re.match(r"\[Table(\w+)\]", l)
    if m:
        cur = m.group(1); bodies[cur] = {}
    elif cur and "=" in l:
        v = l.split("=", 1)[1].split(",")
        if len(v) >= 4 and re.match(r"A\d{5}$", v[0]):
            bodies[cur][v[0]] = (v[2], v[3])

# char/texture : nom de base -> fichiers réels
tex_files = {}
for fn in os.listdir(TEX):
    if fn.lower().endswith(".dds"):
        tex_files[fn.lower()[:-4]] = fn

def tex_match(name):
    """G202AS01 -> tous les fichiers g202as01*.dds"""
    base = name.lower().lstrip("#")
    hits = sorted(fn for k, fn in tex_files.items()
                  if k == base or k.startswith(base))
    return ";".join(hits) if hits else ""

# c_class : enregistrements multi-lignes (champs avec \n interne, ex. "◎…"
# dans la condition). Un record commence par "id|nom|id|J###|" ; les lignes
# suivantes sont des continuations jusqu'au prochain record.
text = read_ini(os.path.join(DB, "c_class.ini"), "big5")
rec_re = re.compile(r"^(\d+)\|[^|\n]*\|\d+\|J\w+\|")
records, cur = [], ""
for l in text.splitlines():
    if rec_re.match(l):
        if cur:
            records.append(cur)
        cur = l
    elif cur:
        cur += "\n" + l
if cur:
    records.append(cur)

rows = []
cls_re = re.compile(r"(\d+)\s+(A\d{5})\s+(A\d{5})\s+0")
for rec in records:
    cid = int(rec_re.match(rec).group(1))
    for lvl, c1, c2 in cls_re.findall(rec):
        for code in (c1, c2):
            rows.append((cid, names.get(cid, "?"), int(lvl), code))

out = os.path.join(ROOT, "artifacts", "corpus-inventory", "class-outfits.tsv")
with open(out, "w") as f:
    f.write("classe_id\tclasse_fr\tniveau\tcode\tcorps\tmesh\ttexture\tfichiers_dds\n")
    n_hit = n_tot = 0
    for cid, nom, lvl, code in sorted(set(rows)):
        for body in ("Girl", "Boy", "Mouse", "Frog", "Kuma", "Loli", "Shota"):
            mesh, tex = bodies.get(body, {}).get(code, ("", ""))
            files = tex_match(tex) if tex else ""
            n_tot += 1
            if files:
                n_hit += 1
            f.write(f"{cid}\t{nom}\t{lvl}\t{code}\t{body}\t{mesh}\t{tex}\t{files}\n")
print(f"{len(set(rows))} entrees classe/palier, {n_hit}/{n_tot} resolues en dds -> {out}")
