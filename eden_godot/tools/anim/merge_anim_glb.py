#!/usr/bin/env python3
"""Assemble les GLB d'anims par-action (<stem>_<action>.glb) en un
<stem>.slim.glb combiné.

Pourquoi : EntityRegistry/EntityAnimator ne charge qu'UN fichier
d'anims par stem (`anims/ride/animation/<stem>.slim.glb` puis
`<stem>.glb`). Les stems convertis uniquement en per-action (~15
montures : AR106, R600-R605, TR*, r976/977…) restaient statiques.

Méthode : le squelette Bip01 est partagé entre les fichiers du stem —
base = tableau de nœuds du premier fichier ; chaque channel entrant
est réindexé par NOM de nœud cible (sûr même si l'ordre diffère).
Accessors/bufferViews/images sont concaténés avec décalage d'index,
le BIN est concaténé avec padding 4.

Usage : merge_anim_glb.py <dir>            (tous les stems orphelins)
        merge_anim_glb.py <dir> <stem>…    (stems choisis)
Écrit <dir>/<stem>.slim.glb — saute si un combiné existe déjà.
"""

from __future__ import annotations

import json
import os
import re
import struct
import sys


def read_glb(path: str) -> tuple[dict, bytes]:
    with open(path, "rb") as fh:
        head = fh.read(20)
        if len(head) < 20 or head[:4] != b"glTF":
            raise ValueError("pas un GLB")
        json_len = struct.unpack("<I", head[12:16])[0]
        doc = json.loads(fh.read(json_len))
        bin_len = struct.unpack("<I", fh.read(4))[0]
        if fh.read(4) != b"BIN\x00":
            raise ValueError("chunk BIN absent")
        return doc, fh.read(bin_len)


def write_glb(path: str, doc: dict, bin_data: bytes) -> None:
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    bin_data += b"\x00" * (-len(bin_data) % 4)
    total = 12 + 8 + len(js) + 8 + len(bin_data)
    # assets/ = liens physiques vers portage : délier avant d'écrire
    if os.path.lexists(path):
        os.unlink(path)
    with open(path, "wb") as fh:
        fh.write(b"glTF" + struct.pack("<II", 2, total))
        fh.write(struct.pack("<I", len(js)) + b"JSON" + js)
        fh.write(struct.pack("<I", len(bin_data)) + b"BIN\x00" + bin_data)


def merge_stem(directory: str, stem: str) -> str:
    """Fusionne <stem>_*.glb de `directory` → <stem>.slim.glb.
    Retourne un statut texte."""
    low = stem.lower()
    for f in (f"{stem}.slim.glb", f"{low}.slim.glb",
              f"{stem}.glb", f"{low}.glb"):
        if os.path.exists(os.path.join(directory, f)):
            return "combiné déjà présent — sauté"
    parts = sorted(
        f for f in os.listdir(directory)
        if f.lower().startswith(low + "_")
        and f.lower().endswith(".glb") and ".slim." not in f)
    if not parts:
        return "aucun fichier par-action — sauté"

    base_doc, base_bin = read_glb(os.path.join(directory, parts[0]))
    base_nodes = base_doc.get("nodes", [])
    name_to_idx = {n.get("name"): i for i, n in enumerate(base_nodes)}
    base_doc["animations"] = []
    bin_out = bytearray(base_bin)
    n_anims = 0

    for part in parts:
        doc, bin_ = read_glb(os.path.join(directory, part))
        acc_off = len(base_doc.get("accessors", []))
        bv_off = len(base_doc.get("bufferViews", []))

        # bufferViews : concaténation physique du BIN.
        for v in doc.get("bufferViews", []):
            off = v.get("byteOffset", 0)
            ln = v.get("byteLength", 0)
            pad = -len(bin_out) % 4
            bin_out.extend(b"\x00" * pad)
            v2 = dict(v)
            v2["byteOffset"] = len(bin_out)
            bin_out.extend(bin_[off:off + ln])
            base_doc.setdefault("bufferViews", []).append(v2)
        for a in doc.get("accessors", []):
            a2 = dict(a)
            if isinstance(a2.get("bufferView"), int):
                a2["bufferView"] += bv_off
            base_doc.setdefault("accessors", []).append(a2)
        for im in doc.get("images", []):
            im2 = dict(im)
            if isinstance(im2.get("bufferView"), int):
                im2["bufferView"] += bv_off
            base_doc.setdefault("images", []).append(im2)

        # Les indices de sampler sont locaux à CHAQUE animation
        # (glTF : channel.sampler → animation.samplers[]) — seuls les
        # accessors et les nœuds cibles demandent une réindexation.
        for anim in doc.get("animations", []):
            a2 = dict(anim)
            a2["samplers"] = [
                {**s,
                 "input": s["input"] + acc_off,
                 "output": s["output"] + acc_off}
                for s in anim.get("samplers", [])
                if isinstance(s.get("input"), int)
                and isinstance(s.get("output"), int)]
            chans = []
            for c in anim.get("channels", []):
                tgt = c.get("target", {})
                node_i = tgt.get("node")
                if not isinstance(node_i, int) or \
                        node_i >= len(doc.get("nodes", [])):
                    continue
                name = doc["nodes"][node_i].get("name")
                if name not in name_to_idx:
                    continue  # os absent du squelette de base — improbable
                chans.append({**c,
                              "target": {**tgt,
                                         "node": name_to_idx[name]}})
            a2["channels"] = chans
            base_doc["animations"].append(a2)
            n_anims += 1

    if "buffers" in base_doc:
        base_doc["buffers"] = [{"byteLength": len(bin_out)}]
    out = os.path.join(directory, f"{stem}.slim.glb")
    write_glb(out, base_doc, bytes(bin_out))
    return f"{len(parts)} fichiers → {n_anims} anims " \
           f"({os.path.getsize(out) >> 10} Ko)"


def orphan_stems(directory: str) -> list[str]:
    """Stems ayant des <stem>_*.glb mais pas de <stem>.(slim.)glb."""
    files = os.listdir(directory)
    combined = {os.path.splitext(f)[0].lower() for f in files
                if f.endswith(".glb") and "_" not in f}
    stems = set()
    for f in files:
        m = re.match(r"(.+)_.+\.glb$", f, re.IGNORECASE)
        if m and ".slim." not in f:
            stems.add(m.group(1))
    return sorted(s for s in stems if s.lower() not in combined)


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    directory = sys.argv[1]
    stems = sys.argv[2:] or orphan_stems(directory)
    print(f"[merge] {len(stems)} stem(s) à assembler dans {directory}")
    for stem in stems:
        try:
            print(f"[merge] {stem} : {merge_stem(directory, stem)}")
        except Exception as exc:  # noqa: BLE001 — outil de batch
            print(f"[merge] {stem} : ÉCHEC {exc}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
