#!/usr/bin/env python3
"""copy_ui_png.py — copie les PNG/fontes d'UI CEGUI vers
godot_project/ui_textures/ (hors assets/, donc importés par Godot).

Les atlas référencés par les .imageset CEGUI vivent dans
raw_assets/uidata/imagesets/*.png ; godot_project/assets/ porte un
.gdignore (pipeline nif2gltf) donc on ne peut pas s'y appuyer. On
copie donc :

  raw_assets/uidata/imagesets/*.png        → godot_project/ui_textures/imagesets/
  raw_assets/uidata/imagesets/<Theme>/*.png → godot_project/ui_textures/imagesets/<Theme>/
  raw_assets/uidata/*.png                  → godot_project/ui_textures/
  raw_assets/themes/<Theme>/*.png          → godot_project/ui_textures/themes/<Theme>/
  raw_assets/uidata/FONT01.ttf             → godot_project/ui_textures/  (police UI)

Thèmes : les deux sources sont copiées dans leurs arbres respectifs
(imagesets/<Theme>/ et themes/<Theme>/ — la « collision » du
docstring précédent était une duplication de contenu identique :
même md5 pour les fichiers homonymes, pas un conflit). Union des
noms = Blue, Coffee, Grapes, Green, Lemon, Orange, purple, Red,
Strawberry, Water, Yellow.

Usage : copy_ui_png.py [--dry-run] [--verbose]
"""

import argparse
import os
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / "raw_assets"
DEST = ROOT / "godot_project" / "ui_textures"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dry-run", action="store_true",
                    help="liste sans copier")
    ap.add_argument("--verbose", "-v", action="store_true")
    args = ap.parse_args()

    jobs = []  # (src, dst_rel)
    src = RAW / "uidata" / "imagesets"
    jobs += [(p, Path("imagesets") / p.name)
             for p in sorted(src.glob("*.png"))]
    jobs += [(p, Path(p.name))
             for p in sorted((RAW / "uidata").glob("*.png"))]
    # Variantes de thème — les deux arbres sources (imagesets/<T>/
    # et themes/<T>/) vers leurs équivalents ui_textures ; les
    # fichiers homonymes ont un contenu identique (même md5 vérifié :
    # Blue/Green/Red/Purple) — pas de collision réelle.
    for base, dest_root in ((src, "imagesets"),
                            (RAW / "themes", "themes")):
        if not base.is_dir():
            continue
        for d in sorted(p for p in base.iterdir() if p.is_dir()):
            jobs += [(p, Path(dest_root) / d.name / p.name)
                     for p in sorted(d.glob("*.png"))]
    # Police UI principale référencée par fonts/F001-*.font.
    font = RAW / "uidata" / "FONT01.ttf"
    if font.is_file():
        jobs.append((font, Path(font.name)))

    copied = skipped = missing = 0
    for src_p, dst_rel in jobs:
        dst = DEST / dst_rel
        if not src_p.is_file():
            missing += 1
            print(f"MISSING {src_p}", file=sys.stderr)
            continue
        if dst.exists() and dst.stat().st_mtime >= src_p.stat().st_mtime \
                and dst.stat().st_size == src_p.stat().st_size:
            skipped += 1
            if args.verbose:
                print(f"skip    {dst_rel}")
            continue
        if args.verbose or args.dry_run:
            print(f"copy    {src_p.relative_to(RAW)} -> ui_textures/{dst_rel}")
        if not args.dry_run:
            dst.parent.mkdir(parents=True, exist_ok=True)
            # assets/ = liens physiques vers portage : délier avant d'écrire
            if os.path.lexists(dst):
                os.unlink(dst)
            shutil.copy2(src_p, dst)
        copied += 1

    print(f"{copied} copiés, {skipped} à jour, {missing} absents "
          f"({'dry-run' if args.dry_run else 'écrit'})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
