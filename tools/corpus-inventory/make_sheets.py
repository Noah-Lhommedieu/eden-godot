#!/usr/bin/env python3
"""Contact sheets 5x5 @256px depuis artifacts/corpus-png/ vers
artifacts/corpus-sheets/<dir>/sheet-NNN.png + sheets-index.tsv
(chemin_source -> png, sheet, ligne, colonne). Resumable."""
import os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PNG = os.path.join(ROOT, "artifacts", "corpus-png")
OUT = os.path.join(ROOT, "artifacts", "corpus-sheets")
LOGD = os.path.join(ROOT, "artifacts", "corpus-inventory")
CELL, COLS, ROWS = 256, 5, 5
PER = COLS * ROWS

def sheets_for(d, files):
    """Retourne [(sheet_path, [fichiers])] a produire (skip existants a jour)."""
    jobs = []
    for i in range(0, len(files), PER):
        chunk = files[i:i + PER]
        sp = os.path.join(OUT, d, f"sheet-{i//PER + 1:03d}.png")
        newest = max(os.path.getmtime(os.path.join(PNG, d, f)) for f in chunk)
        if not (os.path.exists(sp) and os.path.getmtime(sp) >= newest):
            jobs.append((sp, [os.path.join(PNG, d, f) for f in chunk]))
    return jobs

def montage(job):
    sp, files = job
    os.makedirs(os.path.dirname(sp), exist_ok=True)
    try:
        subprocess.run(
            ["montage", "-label", "%f", "-tile", f"{COLS}x{ROWS}",
             "-geometry", f"{CELL}x{CELL}+6+6", "-background", "#202020",
             "-fill", "white", "-pointsize", "13"] + files + [sp],
            check=True, capture_output=True, timeout=300)
        return None
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
        err = e.stderr.decode("utf-8", "replace") if e.stderr else str(e)
        return (sp, err[:160])

def main():
    by_dir = {}
    for dp, _, fns in os.walk(PNG):
        rel_dir = os.path.relpath(dp, PNG)
        if rel_dir == ".":
            rel_dir = ""
        pngs = sorted(f for f in fns if f.lower().endswith(".png"))
        if pngs:
            by_dir[rel_dir] = pngs
    jobs, index = [], []
    for d, pngs in by_dir.items():
        jobs += sheets_for(d, pngs)
        for i, f in enumerate(pngs):
            n, c = i // PER, i % PER
            sp = os.path.join("artifacts/corpus-sheets", d,
                              f"sheet-{n + 1:03d}.png")
            src = os.path.join(d, f) if d else f
            index.append((os.path.splitext(src)[0], src, sp,
                          c // COLS, c % COLS))
    print(f"{sum(len(v) for v in by_dir.values())} png, {len(jobs)} planches a produire")
    fails = []
    with ThreadPoolExecutor(max_workers=4) as ex:
        for i, res in enumerate(ex.map(montage, jobs), 1):
            if res:
                fails.append(res)
            if i % 100 == 0:
                print(f"{i}/{len(jobs)} planches", flush=True)
    with open(os.path.join(LOGD, "sheets-index.tsv"), "w") as f:
        f.write("base\tpng\tplanche\tligne\tcolonne\n")
        for base, src, sp, r, c in index:
            f.write(f"{base}\tartifacts/corpus-png/{src}\t{sp}\t{r}\t{c}\n")
    if fails:
        with open(os.path.join(LOGD, "sheets-failures.tsv"), "w") as f:
            for sp, err in fails:
                f.write(f"{sp}\t{err}\n")
    print(f"{len(fails)} planches en echec")

if __name__ == "__main__":
    main()
