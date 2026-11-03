#!/usr/bin/env python3
"""Recapitulatif « murs invisibles » de TOUTES les maps.

Deux familles de murs invisibles (cf. godot_project/scripts/world) :
  1. grille native `map/sknodemap/Sxxx.smp` (NodeMapFile.cs : 2w x 2h
     cases de 0,5 u, bit0 = obstacle) -> PlayerController.ClipByNodeMap
     (client) + Eden.Zone.NodeMap (serveur) ;
  2. meshes « helper » (true wall / block / camera / blo / knock / Line)
     poses dans les NIF (objets du .fsm + NIF de map entiere) ->
     CollisionBuilder (StaticBody3D layer 1).

Usage : python3 tools/walls_report.py [--raw DIR ...] [--md OUT.md]
Sources = EDEN_RAW_ASSETS (defaut raw_assets) puis l'overlay EDENFR
(--raw supplementaire, ex. ../../edenfr_overlay).  Sans dependance Godot.
"""
from __future__ import annotations
import argparse, os, re, struct, sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from nif2gltf.nif_reader import Reader  # noqa: E402

WALL = re.compile(r"^(true ?wall\d*|blo(c)?|knock|.*block\d*|.*camera\d*|"
                  r"line\d*)([_:]\d+)?$", re.I)


def ci_find(root, rel):
    cur = root
    for part in rel.split("/"):
        if not os.path.isdir(cur):
            return None
        hit = next((e for e in os.listdir(cur) if e.lower() == part.lower()), None)
        if hit is None:
            return None
        cur = os.path.join(cur, hit)
    return cur


def find(roots, rel):
    for r in roots:
        p = ci_find(r, rel)
        if p and os.path.exists(p):
            return p
    return None


def parse_smp(path):
    d = open(path, "rb").read()
    if len(d) < 12 or struct.unpack_from("<H", d)[0] != 6 or d[2:8] != b"SMP2.0":
        return None
    nl = struct.unpack_from("<H", d, 8)[0]
    o = 10 + nl + 4
    w, h, n = struct.unpack_from("<HHI", d, o)
    if n != w * h or w <= 0 or h <= 0:
        return None
    return w, h, d[o + 8:o + 8 + n]


def components(cells, cols, rows):
    """-> (cases bloquees, cases bloquees isolees (piliers/arbres), cases libres)"""
    import numpy as np
    arr = np.frombuffer(cells, dtype=np.uint8)
    v = np.stack([(arr >> 6) & 3, (arr >> 4) & 3, (arr >> 2) & 3, arr & 3], 1).reshape(-1)
    blocked = ((v & 1) != 0)[:cols * rows].reshape(rows, cols)
    nb = int(blocked.sum())
    p = np.pad(blocked, 1).astype(np.uint8)
    nbr = sum(p[1 + dy:1 + dy + rows, 1 + dx:1 + dx + cols]
              for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dy, dx) != (0, 0))
    iso = int((blocked & (nbr == 0)).sum())
    return nb, iso, cols * rows - nb


def fsm_objects(path):
    d = open(path, "rb").read(); pos = 0x58; out = []
    while pos + 8 <= len(d):
        tag, size = struct.unpack_from("<II", d, pos)
        if size < 8 or pos + size > len(d):
            break
        if tag == 0x204A424F:
            n = struct.unpack_from("<I", d, pos + 8)[0]; b = pos + 12
            for i in range(n):
                r = d[b + i * 92:b + (i + 1) * 92]
                if len(r) == 92:
                    out.append(r[0x34:0x5C].split(b"\0")[0].decode("latin1"))
        pos += size
    return out


_wall_cache = {}


def wall_meshes(path):
    if path in _wall_cache:
        return _wall_cache[path]
    try:
        nif = Reader().read(path)
        n = sum(1 for b in nif.blocks if b.type in ("NiTriShape", "NiTriStrips")
                and WALL.match(str(b.fields.get("Name") or "")))
    except Exception:  # noqa: BLE001
        n = -1
    _wall_cache[path] = n
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", action="append", default=[])
    ap.add_argument("--md", default="")
    a = ap.parse_args()
    portage = os.path.abspath(os.path.join(HERE, "..", ".."))
    roots = [os.environ.get("EDEN_RAW_ASSETS") or os.path.join(portage, "eden_godot", "raw_assets")]
    roots += a.raw
    names = {}
    tt = find(roots, "data/db/T_Transport.ini")
    if tt:
        for l in open(tt, encoding="utf-8", errors="replace"):
            m = re.match(r"^(\d+)\|([^|]+)\|", l)
            if m:
                names[int(m.group(1))] = m.group(2)
    maps = set()
    for r in roots:
        d = ci_find(r, "map/sknodemap")
        if d:
            maps |= {f[:-4].upper() for f in os.listdir(d) if re.fullmatch(r"[sS]\d+\.smp", f, re.I)}
        d = ci_find(r, "map/model")
        if d:
            maps |= {f.upper() for f in os.listdir(d) if re.fullmatch(r"[sS]\d+", f)}
    rows = []
    for m in sorted(maps):
        row = dict(map=m, name=names.get(int(m[1:]), ""))
        p = find(roots, f"map/sknodemap/{m}.smp")
        if p and (s := parse_smp(p)):
            w, h, cells = s
            nb, iso, free = components(cells, w * 2, h * 2)
            row.update(w=w, h=h, blocked=nb, pct=100.0 * nb / (4 * w * h), iso=iso, free=free)
        fsm = find(roots, f"map/model/{m}/{m}.fsm")
        objs = Counter(); walls_inst = 0; walls_models = 0
        if fsm:
            objs = Counter(fsm_objects(fsm))
            for name, cnt in objs.items():
                p2 = find(roots, f"map/model/object/{name}.nif")
                if p2 and wall_meshes(p2) > 0:
                    walls_models += 1; walls_inst += cnt
            row["kind"] = "fsm"
        else:
            p2 = find(roots, f"map/model/{m}/{m}.nif")
            if p2:
                k = wall_meshes(p2)
                row["kind"] = "nif-map"; walls_inst = max(k, 0); walls_models = 1 if k > 0 else 0
        row.update(helper_models=walls_models, helper_inst=walls_inst)
        rows.append(row)
    out = ["| Map | Nom | Type | Grille m | Cases bloquees | % | Cases isolees | Cases libres | Modeles helper | Instances helper |",
           "|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        out.append("| {map} | {name} | {kind} | {g} | {b} | {p} | {iso} | {free} | {hm} | {hi} |".format(
            map=r["map"], name=r["name"], kind=r.get("kind", "-"),
            g=f"{r['w']}x{r['h']}" if "w" in r else "-", b=r.get("blocked", "-"),
            p=f"{r['pct']:.1f}" if "pct" in r else "-", iso=r.get("iso", "-"), free=r.get("free", "-"), hm=r["helper_models"], hi=r["helper_inst"]))
    txt = "\n".join(out)
    print(txt)
    if a.md:
        open(a.md, "w").write(txt + "\n")


if __name__ == "__main__":
    main()
