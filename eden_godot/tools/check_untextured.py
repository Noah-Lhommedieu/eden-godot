#!/usr/bin/env python3
"""Controle reproductible : GLB sans texture / textures NIF introuvables.

Deux vues complementaires :

  glb  (rapide)  parcourt godot_project/assets/models/**/*.glb et compte
                 les GLB dont des materiaux n'ont pas de baseColorTexture,
                 et les URI d'image qui ne pointent sur aucun fichier.
  nif  (lent)    parse les NIF sources (RAW) et, pour chaque texture
                 referencee (tous slots NiTexturingProperty + NiTextureEffect),
                 verifie qu'elle se resout via TextureIndex.resolve.
                 Croise avec le GLB : « stale » = le NIF se resout mais le
                 GLB n'a aucune image -> reconversion necessaire.

Usage :
  python3 tools/check_untextured.py glb [--only SUBSTR]
  python3 tools/check_untextured.py nif [--only SUBSTR] [--jobs N] [--tsv OUT]
  python3 tools/check_untextured.py all
Code retour 0 ; ajouter --fail-on-stale pour 1 si des GLB sont stale.
"""
from __future__ import annotations

import argparse
import concurrent.futures as cf
import json
import os
import struct
import sys
from collections import Counter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from nif2gltf import convert as C  # noqa: E402
from nif2gltf.nif_reader import Reader  # noqa: E402

MODELS = C.MODELS
TEXTURES = C.TEXTURES
RAW = C.RAW

SLOTS = ("Base", "Dark", "Detail", "Gloss", "Glow", "Bump Map", "Normal",
         "Parallax", "Decal 0", "Decal 1", "Decal 2", "Decal 3")


def read_glb_json(path):
    with open(path, "rb") as f:
        head = f.read(20)
        if len(head) < 20 or head[:4] != b"glTF":
            return None
        clen, ctype = struct.unpack("<II", head[12:20])
        if ctype != 0x4E4F534A:
            return None
        return json.loads(f.read(clen))


def glb_scan(only=""):
    tot = Counter()
    bad = []
    for dp, _dn, fns in os.walk(MODELS):
        for fn in fns:
            if not fn.endswith(".glb"):
                continue
            p = os.path.join(dp, fn)
            if only and only not in p:
                continue
            j = read_glb_json(p)
            if j is None:
                continue
            tot["glb"] += 1
            mats = j.get("materials", [])
            if not mats:
                continue
            notex = [m for m in mats if "baseColorTexture" not in
                     m.get("pbrMetallicRoughness", {})]
            if len(notex) == len(mats):
                tot["glb_all_mats_untextured"] += 1
            if notex:
                tot["glb_some_mat_untextured"] += 1
                bad.append(os.path.relpath(p, MODELS))
            tot["mats"] += len(mats)
            tot["mats_untextured"] += len(notex)
            for im in j.get("images", []):
                uri = im.get("uri", "")
                if not os.path.exists(os.path.normpath(os.path.join(dp, uri))):
                    tot["dangling_image_uri"] += 1
    return tot, bad


_IDX = None


def _init():
    global _IDX
    _IDX = C.TextureIndex(TEXTURES)


def nif_refs(src):
    """-> (rel_nif, [(slot, name, resolved_rel|None)], parse_error|None)."""
    rel = os.path.relpath(src, RAW).replace(os.sep, "/")
    try:
        nif = Reader().read(src)
    except Exception as e:  # noqa: BLE001
        return rel, [], str(e)[:100]
    pref = C._tex_prefix_for(src)
    out = []
    for b in nif.blocks:
        if b.type == "NiTexturingProperty":
            f = b.fields
            for s in SLOTS:
                if not f.get("Has %s Texture" % s):
                    continue
                td = f.get("%s Texture" % s) or {}
                srcb = nif.block(td.get("Source", -1))
                if srcb is None or srcb.type != "NiSourceTexture":
                    continue
                name = C.source_texture_name(srcb)
                if not name:
                    continue
                out.append((s, name, _IDX.resolve(name, pref)))
        elif b.type == "NiTextureEffect":
            srcb = nif.block(b.fields.get("Source Texture", -1))
            if srcb is not None and srcb.type == "NiSourceTexture":
                name = C.source_texture_name(srcb)
                if name:
                    out.append(("Effect", name, _IDX.resolve(name, pref)))
    return rel, out, None


def nif_scan(only="", jobs=0, tsv=None):
    srcs = [s for s in C.scan_sources() if s.lower().endswith(".nif")
            and (not only or only in s)]
    jobs = jobs or max(1, (os.cpu_count() or 2) - 1)
    tot = Counter()
    missing = Counter()      # nom texture -> nb NIF
    missing_nifs = {}
    stale = []
    rows = []
    with cf.ProcessPoolExecutor(max_workers=jobs, initializer=_init) as ex:
        for rel, refs, err in ex.map(nif_refs, srcs, chunksize=32):
            tot["nif"] += 1
            if err:
                tot["parse_error"] += 1
                continue
            base = [r for r in refs if r[0] == "Base"]
            if refs:
                tot["nif_with_tex"] += 1
            unres = [r for r in refs if r[2] is None]
            ubase = [r for r in base if r[2] is None]
            if unres:
                tot["nif_unresolved_any_slot"] += 1
            if ubase:
                tot["nif_unresolved_base"] += 1
                if len(ubase) == len(base):
                    tot["nif_all_base_unresolved"] += 1
                for _s, name, _ in ubase:
                    k = os.path.basename(name.replace("\\", "/")).lower()
                    missing[k] += 1
                    missing_nifs.setdefault(k, []).append(rel)
            for s, name, r in unres:
                rows.append((rel, s, name))
            glb = os.path.join(MODELS, os.path.splitext(rel)[0] + ".glb")
            if base and any(r[2] for r in base) and os.path.exists(glb):
                j = read_glb_json(glb)
                if j is not None:
                    have = {os.path.basename(i.get("uri", "")).lower()
                            for i in j.get("images", [])}
                    want = {os.path.basename(r[2]).lower()
                            for r in base if r[2]}
                    notex = any("baseColorTexture" not in
                                m.get("pbrMetallicRoughness", {})
                                for m in j.get("materials", []))
                    if notex and not want <= have:
                        stale.append(rel)
    if tsv:
        with open(tsv, "w") as f:
            f.write("nif\tslot\ttexture\n")
            for r in rows:
                f.write("\t".join(r) + "\n")
    return tot, missing, missing_nifs, stale


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=("glb", "nif", "all"))
    ap.add_argument("--only", default="")
    ap.add_argument("--jobs", type=int, default=0)
    ap.add_argument("--tsv", default="")
    ap.add_argument("--top", type=int, default=15)
    ap.add_argument("--stale-out", default="",
                    help="ecrit la liste des NIF stale (chemins relatifs)")
    ap.add_argument("--fail-on-stale", action="store_true")
    a = ap.parse_args()
    rc = 0
    if a.mode in ("glb", "all"):
        tot, bad = glb_scan(a.only)
        print("== GLB ==")
        for k, v in sorted(tot.items()):
            print("  %-28s %d" % (k, v))
        by_dir = Counter(os.path.dirname(b) for b in bad)
        for d, n in by_dir.most_common(a.top):
            print("    %5d  %s" % (n, d))
    if a.mode in ("nif", "all"):
        tot, missing, mn, stale = nif_scan(a.only, a.jobs, a.tsv or None)
        print("== NIF (resolution) ==")
        for k, v in sorted(tot.items()):
            print("  %-28s %d" % (k, v))
        print("  stale_glb (texture de base resolvable absente du GLB) %d" % len(stale))
        for s in stale[:a.top]:
            print("    stale:", s)
        print("  textures de base introuvables (distinctes): %d" % len(missing))
        for k, n in missing.most_common(a.top):
            print("    %5d  %s  e.g. %s" % (n, k, mn[k][0]))
        if a.stale_out:
            with open(a.stale_out, "w") as f:
                f.write("\n".join(stale) + "\n")
        if a.fail_on_stale and stale:
            rc = 1
    return rc


if __name__ == "__main__":
    sys.exit(main())
