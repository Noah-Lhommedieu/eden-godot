#!/usr/bin/env python3
"""Visages : le trait vertical au milieu du visage vient du fond NOIR (ou
transparent) de l'atlas *FACES*.png — le filtrage bilinéaire/mip mélange
ce noir aux bords des îlots UV (couture visible).  Étend (dilate) la couleur
des bords sur le fond avant sauvegarde (« edge padding »).  Idempotent.
Usage : python3 tools/pad_face_textures.py
"""
import glob, os, sys
import numpy as np
from PIL import Image
D = os.path.join(os.path.dirname(__file__), "..", "godot_project",
                 "assets", "textures", "char", "texture")
STAMP = b"edenpad1"

def pad(a):
    rgb = a[..., :3].astype(np.float32); al = a[..., 3]
    bg = (rgb.max(axis=2) <= 3)
    # fond relié au bord uniquement : flood simple par itérations
    reach = np.zeros_like(bg); reach[0, :] = bg[0, :]; reach[-1, :] = bg[-1, :]
    reach[:, 0] = bg[:, 0]; reach[:, -1] = bg[:, -1]
    for _ in range(2048):
        n = reach.copy()
        n[1:] |= reach[:-1]; n[:-1] |= reach[1:]
        n[:, 1:] |= reach[:, :-1]; n[:, :-1] |= reach[:, 1:]
        n &= bg
        if (n == reach).all(): break
        reach = n
    bg = reach
    if not bg.any(): return None
    known = ~bg
    out = rgb.copy()
    for _ in range(64):
        if known.all(): break
        acc = np.zeros_like(out); cnt = np.zeros(known.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0: continue
                k = np.roll(np.roll(known, dy, 0), dx, 1)
                v = np.roll(np.roll(out, dy, 0), dx, 1)
                acc += v * k[..., None]; cnt += k
        new = (~known) & (cnt > 0)
        out[new] = acc[new] / cnt[new][:, None]
        known = known | new
    res = a.copy(); res[..., :3] = np.clip(out, 0, 255).astype(np.uint8)
    res[bg, 3] = 255
    return res

n = 0
for p in sorted(glob.glob(os.path.join(D, "*FACES*.png"))):
    im = Image.open(p)
    if im.info.get("eden") == "padded": continue
    a = np.array(im.convert("RGBA"))
    if a[..., 3].min() < 250: continue   # calques alpha : fond voulu transparent
    r = pad(a)
    if r is None: continue
    from PIL.PngImagePlugin import PngInfo
    meta = PngInfo(); meta.add_text("eden", "padded")
    Image.fromarray(r, "RGBA").save(p, pnginfo=meta); n += 1
print(n, "textures visage dilatées")
