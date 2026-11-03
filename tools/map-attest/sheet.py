#!/usr/bin/env python3
"""sheet.py out.png code... : planche-contact (3x2, 640x360) des captures de artifacts/map-attest/shots."""
import sys, os
from PIL import Image, ImageDraw
R = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
out, codes = sys.argv[1], sys.argv[2:]
W, H = 640, 360
sh = Image.new('RGB', (W*3, H*((len(codes)+2)//3)), (40, 40, 40))
for i, c in enumerate(codes):
    p = f'{R}/artifacts/map-attest/shots/{c}.png'
    if os.path.exists(p):
        im = Image.open(p).convert('RGB').resize((W, H))
        sh.paste(im, ((i % 3)*W, (i//3)*H))
    d = ImageDraw.Draw(sh); d.rectangle(((i % 3)*W, (i//3)*H, (i % 3)*W+90, (i//3)*H+22), fill=(0, 0, 0))
    d.text(((i % 3)*W+6, (i//3)*H+5), c, fill=(255, 255, 0))
sh.save(out)
