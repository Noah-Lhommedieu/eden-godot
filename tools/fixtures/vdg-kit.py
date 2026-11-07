#!/usr/bin/env python3
"""Commandes GM `//give` du kit de test des villes de guilde (PLAN-ACTION-3, lot A5).

Un outil de récolte par type et par palier (pioche / gants / boule : niv. 30, 45, 60), plus,
pour CHAQUE type de service (ferme, mine, invocation, alchimie, fonderie, mare), le plan de la
recette la moins chère et ses matériaux. Tout vient des tables FR (`corpus/fr-full`), rien n'est
inventé. Usage : tools/fixtures/vdg-kit.py > /tmp/kit.cmd
"""
import os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DB = os.path.join(ROOT, "corpus", "fr-full", "data", "db")

def load(f, enc="big5"):
    raw = open(os.path.join(DB, f), "rb").read().decode(enc, errors="replace")
    return [l.rstrip("\r").split("|") for l in raw.split("\n") if l.split("|")[0].isdigit()]

TOOLS = [36819, 36825, 36831, 36821, 36827, 36833, 36823, 36829, 36835]  # niv. 30/45/60 : pioche, gants, boule
out = [f"//give {t} 1" for t in TOOLS]

# Plans de récolte : catégorie 0 ferme, 1 mine, 2 invocation — le plan (kind 50/51/52 = 50 + catégorie)
# de niveau de travail le plus bas. Les lignes dont le catalyseur est un OUTIL (kind 44) sont ignorées.
collect = load("c_familycollect.ini")
items = {int(p[0]): p for p in load("c_item.ini")}
for cat in ("0", "1", "2"):
    rows = [r for r in collect if r[1] == cat and r[2].isdigit() and int(r[2]) > 0
            and len(items.get(int(r[2]), [])) > 8 and items[int(r[2])][8] == str(50 + int(cat))]
    best = min(rows, key=lambda r: (int(r[10] or 0), int(r[13] or 0)))
    out.append(f"//give {best[2]} 1")

# Plans de fabrication : catégories 3 alchimie, 4 fonderie, 5 mare — recette la moins chère.
combine = load("c_familycombine.ini")
for cat in ("3", "4", "5"):
    rows = [r for r in combine if r[1] == cat and r[10].isdigit()]
    best = min(rows, key=lambda r: int(r[9] or 0))
    for i in range(15, len(best) - 2, 3):
        if best[i].isdigit() and int(best[i]) > 0:
            qty = int(best[i + 2] or 1)
            out.append(f"//give {best[i]} {qty}")

print("\n".join(out))
