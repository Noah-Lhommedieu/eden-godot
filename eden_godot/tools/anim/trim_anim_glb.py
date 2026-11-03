#!/usr/bin/env python3
"""Découpe un GLB d'animations Eden au sous-ensemble réellement joué.

Pourquoi : les GLB d'anims convertis depuis .kf font 80-180 Mo
(~391 anims). Le client n'en joue qu'une quarantaine par fichier —
GlbAnimFile lit pourtant tout le BIN + le JSON géant, puis les
accessors float32 un à un (~27 s mesurées sur char/animation/B.glb).

Ce tool écrit <fichier>.slim.glb à côté de l'original :
  - animations : seulement celles du wanted-set (CharWanted ∪
    EntityWanted ∪ noms de motion.ini pour le stem du fichier) ;
  - accessors : uniquement ceux référencés par les samplers des anims
    gardées + meshes/skins (si présents) ;
  - bufferViews : uniquement ceux référencés (+ images) — le BIN est
    reconstruit en concaténant leurs octets ;
  - nodes/scenes/etc : inchangés (les channels ciblent par index de
    nœud — on conserve la numérotation).

Usage :
  trim_anim_glb.py <glb> [<glb>...] [--motion <motion.ini>] [--all]
"""

from __future__ import annotations

import json
import os
import re
import struct
import sys

# ---- wanted sets (miroir de scripts/game/AnimRetarget.cs) -----------
CHAR_WANTED = {
    "stand01", "stand02", "move01", "move02", "move03", "move04",
    "hurt01", "death01",
    "jump01", "sit01", "collect01",
} | {f"attack{i:02d}" for i in range(1, 28)} \
  | {f"shoot{i:02d}" for i in range(1, 80)} \
  | {f"magic{i:02d}" for i in range(1, 21)} \
  | {f"battle{i:02d}" for i in range(1, 14)} \
  | {f"ride{i:02d}" for i in range(1, 9)} \
  | {f"rattack{i:02d}" for i in range(1, 28)} \
  | {f"rmagic{i:02d}" for i in range(1, 21)} \
  | {f"rbattle{i:02d}" for i in range(1, 14)} \
  | {f"rshoot{i:02d}" for i in range(1, 80)}

ENTITY_WANTED = {
    "stand01", "stand02", "move01", "move02", "move03", "move04",
    "attack01", "attack02", "attack03", "attack04",
    "battle01", "hurt01", "hurt02",
    "death01", "death02", "die01", "magic01",
}

WANTED = CHAR_WANTED | ENTITY_WANTED


def motion_names_by_stem(path: str) -> dict[str, set[str]]:
    """motion.ini → {stem → noms d'anims} (col 0 = modèle, dernière
    col = nom d'anim)."""
    out: dict[str, set[str]] = {}
    try:
        with open(path, "r", encoding="latin-1") as fh:
            for raw in fh:
                line = raw.strip()
                if not line.startswith("DB"):
                    continue
                cols = line.split("=", 1)[-1].split(",")
                if len(cols) < 4:
                    continue
                name = cols[-1].strip()
                stem = cols[0].strip().lower()
                if name and name.upper() != "NULL":
                    out.setdefault(stem, set()).add(name)
    except OSError:
        pass
    return out


def read_glb(path: str) -> tuple[dict, bytes]:
    with open(path, "rb") as fh:
        head = fh.read(20)
        if len(head) < 20 or head[:4] != b"glTF":
            raise ValueError("pas un GLB")
        json_len = struct.unpack("<I", head[12:16])[0]
        json_chunk = fh.read(json_len)
        bin_len = struct.unpack("<I", fh.read(4))[0]
        if fh.read(4) != b"BIN\x00":
            raise ValueError("chunk BIN absent")
        return json.loads(json_chunk), fh.read(bin_len)


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


def trim(path: str, wanted: set[str]) -> tuple[int, int, int]:
    doc, bin_ = read_glb(path)
    anims = doc.get("animations", [])
    if not anims:
        return os.path.getsize(path), 0, 0

    kept = [a for a in anims if a.get("name") in wanted]
    if not kept:
        kept = anims  # rien du wanted-set → on conserve tout (fichier non anim ?)

    # Accessors utilisés par les anims gardées (sampler input/output).
    used_acc: set[int] = set()
    for a in kept:
        for s in a.get("samplers", []):
            for k in ("input", "output"):
                i = s.get(k)
                if isinstance(i, int):
                    used_acc.add(i)

    # Accessors des meshes/skins/images éventuels (un GLB d'anims peut
    # embarquer la géométrie — ex. monstre + sa bibliothèque).
    for m in doc.get("meshes", []):
        for p in m.get("primitives", []):
            idx = p.get("indices")
            if isinstance(idx, int):
                used_acc.add(idx)
            for v in p.get("attributes", {}).values():
                used_acc.add(v)
    for s in doc.get("skins", []):
        i = s.get("inverseBindMatrices")
        if isinstance(i, int):
            used_acc.add(i)

    accessors = doc.get("accessors", [])
    acc_map: dict[int, int] = {}
    new_accessors = []
    for i, acc in enumerate(accessors):
        if i in used_acc:
            acc_map[i] = len(new_accessors)
            new_accessors.append(acc)

    # BufferViews utilisés par les accessors gardés + les images.
    used_bv: set[int] = set()
    for acc in new_accessors:
        bv = acc.get("bufferView")
        if isinstance(bv, int):
            used_bv.add(bv)
    for img in doc.get("images", []):
        bv = img.get("bufferView")
        if isinstance(bv, int):
            used_bv.add(bv)

    views = doc.get("bufferViews", [])
    bv_map: dict[int, int] = {}
    new_views = []
    new_bin = bytearray()
    for i, v in enumerate(views):
        if i not in used_bv:
            continue
        off = v.get("byteOffset", 0)
        ln = v.get("byteLength", 0)
        pad = -len(new_bin) % 4
        new_bin.extend(b"\x00" * pad)
        v = dict(v)
        v["byteOffset"] = len(new_bin)
        new_bin.extend(bin_[off:off + ln])
        bv_map[i] = len(new_views)
        new_views.append(v)

    # Réécriture des index.
    for acc in new_accessors:
        if "bufferView" in acc:
            acc["bufferView"] = bv_map[acc["bufferView"]]
    for img in doc.get("images", []):
        if "bufferView" in img:
            img["bufferView"] = bv_map[img["bufferView"]]
    for a in kept:
        for s in a.get("samplers", []):
            for k in ("input", "output"):
                if k in s:
                    s[k] = acc_map[s[k]]

    doc["animations"] = kept
    doc["accessors"] = new_accessors
    doc["bufferViews"] = new_views
    if "buffers" in doc:
        doc["buffers"] = [{"byteLength": len(new_bin)}]

    out = re.sub(r"\.glb$", ".slim.glb", path)
    write_glb(out, doc, bytes(new_bin))
    return os.path.getsize(path), len(kept), os.path.getsize(out)


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    motion = next(
        (a.split("=", 1)[1] for a in sys.argv[1:]
         if a.startswith("--motion=")), None)
    if not args:
        print(__doc__)
        return 2

    wanted = set(WANTED)
    if motion:
        per_stem = motion_names_by_stem(motion)
        print(f"[trim] motion.ini : {sum(len(v) for v in per_stem.values())}"
              f" noms / {len(per_stem)} stems")
    else:
        per_stem = {}

    for path in args:
        stem = os.path.splitext(os.path.basename(path))[0].lower()
        # char/animation/<L> = méga-GLB (~80-180 Mo, ~400 anims dont
        # toutes les skills/emotes) → wanted-set strict = ce que le
        # client retargete réellement (CharWanted). Autres stems
        # (monster/ride/…) : wanted ∪ noms motion.ini du stem — le
        # pool d'attaque natif complet reste jouable.
        if "/char/animation/" in path.replace("\\", "/"):
            w = set(CHAR_WANTED)
        else:
            w = wanted | per_stem.get(stem, set())
        try:
            src_size, n_kept, out_size = trim(path, w)
        except Exception as exc:  # noqa: BLE001 — outil de batch
            print(f"[trim] {path} : ÉCHEC {exc}")
            continue
        print(f"[trim] {path} : {src_size >> 20} Mo → {out_size >> 10} Ko"
              f" ({n_kept} anims)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
