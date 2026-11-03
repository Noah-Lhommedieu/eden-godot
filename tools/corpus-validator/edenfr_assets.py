#!/usr/bin/env python3
"""Classe les assets dérivés (GLB/PNG/JSON) d'un arbre `assets/` selon
la provenance EDENFR de leur source native.

Pour chaque dérivé, la source est cherchée dans le corpus de référence
qui l'a produit (`--built-from`, ex. portage/corpus/official) puis dans
`corpus/fr-full` (insensible à la casse) :

  SAME     source présente dans fr-full, octets identiques → dérivé
           réutilisable tel quel (EDENFR prouvé) ;
  DIFF     source fr-full différente → à reconvertir depuis fr-full ;
  NOFR     source absente de fr-full (complément US/merge) → exclu ;
  NOSRC    source introuvable dans --built-from → à examiner.

Sortie : un TSV `statut<TAB>dérivé<TAB>source_fr` sur stdout + résumé
sur stderr. Lecture seule : ne modifie aucun corpus.

Usage : edenfr_assets.py <assets_dir> --built-from <corpus> --fr <fr-full>
"""
import argparse
import hashlib
import os
import sys

SRC_EXT = {
    "glb": [".nif", ".kf", ".kfm"],
    "png": [".dds", ".tga", ".png", ".bmp", ".jpg"],
    "json": [".kfm", ".kf"],
}


class CiTree:
    """Index insensible à la casse d'un arbre (clé = chemin minuscule)."""

    def __init__(self, root):
        self.root = root
        self.idx = {}
        for d, _, files in os.walk(root, followlinks=True):
            for f in files:
                full = os.path.join(d, f)
                rel = os.path.relpath(full, root).lower()
                self.idx.setdefault(rel, full)

    def get(self, rel):
        return self.idx.get(rel.lower())


def candidates(rel):
    """Chemins source possibles (sans extension dérivée) d'un dérivé."""
    top, _, rest = rel.partition("/")
    name = rest
    for suf in (".slim.glb", ".kfm.json", ".glb", ".png", ".json"):
        if name.lower().endswith(suf):
            stem = name[: -len(suf)]
            kind = suf.rsplit(".", 1)[-1]
            break
    else:
        return []
    exts = SRC_EXT[kind]
    if suf == ".kfm.json":
        exts = [".kfm"]
    elif suf == ".slim.glb":
        exts = [".kf"]
    out = [stem + e for e in exts]
    # textures/<x> peut venir de <x> ou de model/<x> ; UI → ui/
    return out


def digest(path, cache={}):
    if path not in cache:
        h = hashlib.blake2b(digest_size=16)
        with open(path, "rb") as f:
            for chunk in iter(lambda: f.read(1 << 20), b""):
                h.update(chunk)
        cache[path] = h.digest()
    return cache[path]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("assets")
    ap.add_argument("--built-from", required=True)
    ap.add_argument("--fr", required=True)
    a = ap.parse_args()
    built, fr = CiTree(a.built_from), CiTree(a.fr)
    counts = {}
    for d, _, files in os.walk(a.assets):
        for f in files:
            full = os.path.join(d, f)
            rel = os.path.relpath(full, a.assets)
            status, src_fr = "NOSRC", ""
            for c in candidates(rel):
                b = built.get(c)
                if not b:
                    continue
                r = fr.get(c)
                if not r:
                    status = "NOFR"
                elif os.path.getsize(r) == os.path.getsize(b) and \
                        digest(r) == digest(b):
                    status, src_fr = "SAME", os.path.relpath(r, a.fr)
                else:
                    status, src_fr = "DIFF", os.path.relpath(r, a.fr)
                break
            counts[(rel.split("/")[0], status)] = \
                counts.get((rel.split("/")[0], status), 0) + 1
            print(f"{status}\t{rel}\t{src_fr}")
    for k in sorted(counts):
        print(f"{k[0]:10} {k[1]:6} {counts[k]}", file=sys.stderr)


if __name__ == "__main__":
    main()
