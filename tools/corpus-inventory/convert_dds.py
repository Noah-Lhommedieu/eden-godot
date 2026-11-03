#!/usr/bin/env python3
"""Convertit corpus/fr-full/*.dds (et copies png/bmp/jpg/tga) en miroir PNG
dans artifacts/corpus-png/<relpath>. Resumable : saute les PNG à jour.
Ecrit convert-failures.tsv et convert.log dans artifacts/corpus-inventory/.
Sécurité : s'arrête si < 3 Go libres sur le volume de sortie."""
import os, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "corpus", "fr-full")
DST = os.path.join(ROOT, "artifacts", "corpus-png")
LOGD = os.path.join(ROOT, "artifacts", "corpus-inventory")
VIEWABLE = {".png", ".bmp", ".jpg", ".jpeg", ".tga"}

def free_gb(path):
    return os.statvfs(path).f_bavail * os.statvfs(path).f_frsize / 2**30

def dst_for(rel, ext):
    return os.path.join(DST, os.path.splitext(rel)[0] + ".png")

def one(job):
    rel, src = job
    out = dst_for(rel, os.path.splitext(rel)[1].lower())
    if os.path.exists(out) and os.path.getmtime(out) >= os.path.getmtime(src):
        return None
    os.makedirs(os.path.dirname(out), exist_ok=True)
    if os.path.splitext(rel)[1].lower() in VIEWABLE - {".tga"}:
        try:
            with open(src, "rb") as fi, open(out, "wb") as fo:
                fo.write(fi.read())
            return None
        except OSError as e:
            return (rel, f"copy: {e}")
    try:
        # [0] = premier niveau mip / première face
        subprocess.run(["convert", src + "[0]", out], check=True,
                       capture_output=True, timeout=120)
        return None
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
        err = getattr(e, "stderr", b"")
        if isinstance(err, bytes):
            err = err.decode("utf-8", "replace")
        return (rel, f"convert: {(err or str(e))[:160]}")

def main():
    jobs = []
    for dp, _, fns in os.walk(SRC):
        for fn in fns:
            ext = os.path.splitext(fn)[1].lower()
            if ext == ".dds" or ext in VIEWABLE:
                p = os.path.join(dp, fn)
                jobs.append((os.path.relpath(p, SRC), p))
    jobs.sort()
    fails, done = [], 0
    t0 = time.time()
    with open(os.path.join(LOGD, "convert.log"), "w") as log, \
         ThreadPoolExecutor(max_workers=6) as ex:
        for res in ex.map(one, jobs):
            done += 1
            if res:
                fails.append(res)
            if done % 500 == 0:
                gb = free_gb(DST)
                el = time.time() - t0
                log.write(f"{done}/{len(jobs)} en {el:.0f}s, {gb:.1f} Go libres, {len(fails)} echecs\n")
                log.flush()
                if gb < 3:
                    log.write("STOP: disque presque plein\n")
                    sys.exit(2)
    with open(os.path.join(LOGD, "convert-failures.tsv"), "w") as f:
        f.write("chemin\terreur\n")
        for rel, err in fails:
            f.write(f"{rel}\t{err}\n")
    print(f"fin: {done} fichiers, {len(fails)} echecs, {time.time()-t0:.0f}s")

if __name__ == "__main__":
    main()
