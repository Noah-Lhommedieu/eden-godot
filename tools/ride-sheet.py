#!/usr/bin/env python3
"""Planche contact des captures ride-lab : python3 tools/ride-sheet.py <dir> <out.png> [cols] [filtre]"""
import sys, glob, os
from PIL import Image, ImageDraw
d, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 4
flt = sys.argv[4] if len(sys.argv) > 4 else ""
fs = [f for f in sorted(glob.glob(os.path.join(d, "*.png"))) if flt in os.path.basename(f)]
tw = 480
ims = []
for f in fs:
    im = Image.open(f).convert("RGB"); h = int(im.height * tw / im.width)
    im = im.resize((tw, h)); ImageDraw.Draw(im).text((6, 4), os.path.basename(f)[:-4], fill=(255, 255, 0))
    ims.append(im)
rows = (len(ims) + cols - 1) // cols
th = ims[0].height
sheet = Image.new("RGB", (cols * tw, rows * th))
for i, im in enumerate(ims): sheet.paste(im, ((i % cols) * tw, (i // cols) * th))
sheet.save(out); print(out, len(ims))
