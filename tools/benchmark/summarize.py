#!/usr/bin/env python3
"""Résume un ou plusieurs runs de bench (sortie PerfProbe + log --gpu-profile).

Régime établi = échantillons t >= --warmup secondes. Chaque ligne PerfProbe
`[perf] t=Ns … frame=X,Yms(min-max) …` donne la frame moyenne de la seconde ;
les centiles portent sur ces moyennes par seconde (pas sur chaque frame).
Le log --gpu-profile donne `GPU PROFILE (total X ms)` par frame échantillonnée.

Usage : summarize.py --warmup 15 run1.perf run1.log [run2.perf run2.log …]
Sortie : clé=valeur sur stdout (consommé par tools/compare.sh).
"""
import argparse
import re
import statistics

FRAME = re.compile(r"t=(\d+)s .*?frame=([\d,\.]+)ms")
DRAWS = re.compile(r"draws=(\d+) objs=(\d+) prims=(\d+) nodes=(\d+)")
RSS = re.compile(r"rss=(\d+)MB")
GPU = re.compile(r"GPU PROFILE \(total ([\d\.]+)ms\)")


def pct(values, p):
    if not values:
        return float("nan")
    s = sorted(values)
    return s[min(len(s) - 1, int(round(p / 100 * (len(s) - 1))))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--warmup", type=int, default=15)
    ap.add_argument("files", nargs="+")
    a = ap.parse_args()
    frames, gpus, rss, scene = [], [], [], None
    for i in range(0, len(a.files), 2):
        with open(a.files[i], encoding="utf-8", errors="replace") as f:
            for line in f:
                m = FRAME.search(line)
                if not m or int(m.group(1)) < a.warmup:
                    continue
                frames.append(float(m.group(2).replace(",", ".")))
                if (r := RSS.search(line)):
                    rss.append(int(r.group(1)))
                if (d := DRAWS.search(line)):
                    scene = d.groups()
        if i + 1 < len(a.files):
            with open(a.files[i + 1], encoding="utf-8", errors="replace") as f:
                gpus += [float(g) for g in GPU.findall(f.read())]
    # Les premières frames GPU incluent le chargement : on garde la 2e moitié.
    gpus = gpus[len(gpus) // 2:]
    print(f"runs={len(a.files) // 2}")
    print(f"samples={len(frames)}")
    print(f"frame_p50_ms={pct(frames, 50):.2f}")
    print(f"frame_p95_ms={pct(frames, 95):.2f}")
    print(f"frame_max_ms={max(frames) if frames else float('nan'):.2f}")
    print(f"fps_p50={1000 / pct(frames, 50):.1f}" if frames else "fps_p50=nan")
    print(f"gpu_p50_ms={pct(gpus, 50):.2f}")
    print(f"rss_mb_p50={statistics.median(rss) if rss else 'nan'}")
    if scene:
        print("draws={}\nobjs={}\nprims={}\nnodes={}".format(*scene))


if __name__ == "__main__":
    main()
