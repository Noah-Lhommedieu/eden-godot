#!/usr/bin/env python3
"""Murs invisibles : les .smp EDENFR de certaines maps (s005 s007 s008 s014
s021 s027 s037 s213 s303…) bloquent presque tout (99 % des points de spawn
de la scène tombent dans une case obstacle) alors que le .smp US est cohérent.
Pour chaque map dont le .smp du corpus est incohérent avec sa scène (> 8 %
de points de spawn bloqués) ET dont le .smp US est meilleur, copie le .smp
US dans godot_project/gamedata/map/sknodemap/fix/ (lu en priorité par
MapLoader).  Ne touche jamais corpus/ ni raw_assets/ (lecture seule).
Usage : python3 tools/fix_nodemaps.py
"""
import os, struct, shutil, sys
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
C = os.path.join(ROOT, "corpus/official")
S = os.path.join(ROOT, "eden_server/server_us/Data/NodeMap")
OUT = os.path.join(ROOT, "eden_godot/godot_project/gamedata/map/sknodemap/fix")

def load(p):
    d = open(p, "rb").read()
    nl = struct.unpack_from("<H", d, 8)[0]; o = 10 + nl + 4
    w, h, n = struct.unpack_from("<HHI", d, o)
    return w, h, d[o + 8:o + 8 + n]

def blocked(m, x, y):
    w, h, c = m; cx = int(x * 2 // 1); cy = int(y * 2 // 1)
    if cx < 0 or cy < 0 or cx >= w * 2 or cy >= h * 2: return False
    i = cy * w * 2 + cx
    v = (c[i >> 2] >> (6 - 2 * (i & 3))) & 3
    return v > 0 and (v & 1) != 0

def pts(scene):
    sec = None; res = []
    for l in open(scene, "rb").read().decode("latin1").splitlines():
        if l.startswith("["): sec = l.split("]")[0]; continue
        p = l.split(",")
        try:
            if sec == "[npc": res.append((float(p[2]), float(p[3])))
            if sec == "[position": res.append((float(p[1]), float(p[2])))
            if sec == "[reborn_monsters": res.append((float(p[5]), float(p[6])))
        except Exception: pass
    return res

def rate(m, P): return sum(1 for x, y in P if blocked(m, x, y)) / len(P)

shutil.rmtree(OUT, ignore_errors=True); os.makedirs(OUT)
fixed = []
us = {f.lower(): f for f in os.listdir(S) if f.lower().endswith(".smp")}
for f in sorted(os.listdir(C + "/map/sknodemap")):
    if not f.lower().endswith(".smp") or len(f) < 5 or f.lower() not in us: continue
    sc = C + "/data/scene/" + f[:-4].lower() + ".ini"
    if not os.path.exists(sc): continue
    P = pts(sc)
    if len(P) < 5: continue
    a = load(C + "/map/sknodemap/" + f); b = load(S + "/" + us[f.lower()])
    ra, rb = rate(a, P), rate(b, P)
    if ra > 0.08 and rb < ra * 0.5:
        shutil.copyfile(S + "/" + us[f.lower()], OUT + "/" + f.lower())
        fixed.append((f, round(ra, 2), round(rb, 2)))
for x in fixed: print("fix", x)
print(len(fixed), "maps corrigées")
