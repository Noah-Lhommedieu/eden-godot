#!/usr/bin/env python3
"""Convertit en lot les .dds de raw_assets vers des .png pour Godot.

Destination : ``<out>/<chemin relatif identique>`` avec l'extension
remplacée par ``.png`` (le chemin et le nom sont conservés tels quels).
Les ``.png`` déjà présents dans les sources (``themes/``,
``uidata/imagesets/`` et quelques autres dossiers UI) sont copiés tels
quels au même emplacement relatif.

Stratégie par fichier : Pillow d'abord (DXT1/3/5, BC5/BC7, RGBA brut) ;
si Pillow refuse, fallback ``magick`` ; sinon échec consigné avec la
cause (FourCC/DXGI lu dans l'en-tête DDS).

Reprise : un .png existant plus récent que le .dds source est sauté.
"""
from __future__ import annotations

import argparse
import fnmatch
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import time
from multiprocessing import Pool

# .png copiés tels quels : spec = themes/ + uidata/imagesets/, étendu à
# tous les .png des sources (une poignée sous UI/, char/, monster/) pour
# une recherche uniforme sous <out>/<rel>.
COMPRESS_LEVEL = 3

# Quelques DXGI_FORMAT (offset 128 si FourCC == 'DX10')
DXGI_NAMES = {
    0: 'UNKNOWN', 28: 'R8G8B8A8_UNORM', 29: 'R8G8B8A8_SNORM',
    71: 'BC1_UNORM', 72: 'BC1_UNORM_SRGB', 74: 'BC2_UNORM',
    75: 'BC2_UNORM_SRGB', 77: 'BC3_UNORM', 78: 'BC3_UNORM_SRGB',
    80: 'BC4_UNORM', 83: 'BC5_UNORM', 87: 'R8G8_B8G8_UNORM',
    95: 'BC6H_UF16', 98: 'BC7_UNORM', 99: 'BC7_UNORM_SRGB',
}


def dds_format_info(path: str) -> str:
    """Cause d'échec lisible : FourCC/DXGI/dimensions du DDS."""
    try:
        with open(path, 'rb') as f:
            h = f.read(148)
        if h[:4] != b'DDS ':
            return 'pas un DDS (magic absent)'
        hgt, w = struct.unpack_from('<II', h, 12)
        pf_flags, fourcc = struct.unpack_from('<I4s', h, 80)
        if fourcc == b'DX10':
            dxgi, = struct.unpack_from('<I', h, 128)
            return f'DX10 dxgi={dxgi}({DXGI_NAMES.get(dxgi, "?")}) {w}x{hgt}'
        if pf_flags & 0x4:
            return f'FourCC={fourcc.decode("latin-1").strip()} {w}x{hgt}'
        bitcount, = struct.unpack_from('<I', h, 88)
        return f'non compressé {bitcount}bpp {w}x{hgt}'
    except Exception as ex:
        return f'en-tête illisible ({ex})'


def _expected_dds_bytes(path: str) -> int | None:
    """Taille attendue (header + mip0) pour détecter un DDS tronqué."""
    try:
        with open(path, 'rb') as f:
            h = f.read(148)
        if h[:4] != b'DDS ':
            return None
        hgt, w = struct.unpack_from('<II', h, 12)
        pf_flags, fourcc = struct.unpack_from('<I4s', h, 80)
        if fourcc == b'DX10':
            dxgi, = struct.unpack_from('<I', h, 128)
            if dxgi in (71, 72):       # BC1
                return 148 + ((w + 3) // 4) * ((hgt + 3) // 4) * 8
            if 74 <= dxgi <= 99:       # BC2..BC7
                return 148 + ((w + 3) // 4) * ((hgt + 3) // 4) * 16
            return None
        if pf_flags & 0x4:
            fcc = fourcc.strip(b'\0').decode('latin-1')
            blk = 8 if fcc in ('DXT1', 'BC1', 'ATI1') else 16
            return 128 + ((w + 3) // 4) * ((hgt + 3) // 4) * blk
        bpp, = struct.unpack_from('<I', h, 88)
        if bpp and bpp % 8 == 0:
            return 128 + w * hgt * (bpp // 8)
    except Exception:
        pass
    return None


def _magick(src: str, dst: str) -> tuple[bool, str]:
    try:
        r = subprocess.run(['magick', src + '[0]', dst],
                           capture_output=True, text=True, timeout=120)
    except Exception as ex:
        return False, f'magick: {ex}'
    if r.returncode == 0 and os.path.exists(dst):
        return True, ''
    return False, 'magick: ' + (r.stderr.strip() or f'rc={r.returncode}')


def _pillow(src: str, dst: str) -> tuple[bool, str]:
    import io
    from PIL import Image
    try:
        with Image.open(src) as im:
            im.load()
            # assets/ = liens physiques vers portage : délier avant d'écrire
            if os.path.lexists(dst):
                os.unlink(dst)
            im.save(dst, compress_level=COMPRESS_LEVEL)
        return True, ''
    except Exception as ex:
        err1 = f'Pillow: {ex}'
    # DDS tronqué dans la source : pad jusqu'à la taille attendue
    exp = _expected_dds_bytes(src)
    if exp:
        try:
            data = open(src, 'rb').read()
        except OSError:
            data = b''
        if data and len(data) < exp:
            try:
                with Image.open(
                        io.BytesIO(data + b'\0' * (exp - len(data)))) as im:
                    im.load()
                    # assets/ = liens physiques vers portage : délier avant d'écrire
                    if os.path.lexists(dst):
                        os.unlink(dst)
                    im.save(dst, compress_level=COMPRESS_LEVEL)
                return True, ''
            except Exception as ex:
                err1 += f' | Pillow(pad): {ex}'
    return False, err1


def _convert_one(job) -> tuple:
    src, dst = job
    try:
        if (os.path.exists(dst)
                and os.stat(dst).st_mtime >= os.stat(src).st_mtime):
            return ('skipped', src, 0, 0, '')
    except OSError:
        pass
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    ok, err1 = _pillow(src, dst)
    if not ok:
        ok, err2 = _magick(src, dst)
        if not ok:
            return ('error', src, 0, 0,
                    f'{dds_format_info(src)} | {err1} | {err2}')
    sz_src = os.path.getsize(src)
    return ('ok', src, sz_src, os.path.getsize(dst), '')


def _copy_one(job) -> tuple:
    src, dst = job
    try:
        if (os.path.exists(dst)
                and os.stat(dst).st_mtime >= os.stat(src).st_mtime):
            return ('skipped', src, 0, 0, '')
    except OSError:
        pass
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    return ('ok', src, 0, 0, '')


def collect_jobs(root: str, out: str, only: str | None):
    """Retourne (jobs_dds, jobs_png, total_dds_bytes)."""
    dds, pngs, total = [], [], 0
    pat = only.lower() if only else None
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            rel = os.path.relpath(os.path.join(dirpath, fn), root)
            rel = rel.replace(os.sep, '/')
            low = rel.lower()
            src = os.path.join(dirpath, fn)
            if low.endswith('.dds'):
                if pat and not fnmatch.fnmatch(low, pat):
                    continue
                total += os.path.getsize(src)
                dst = os.path.join(out, rel[:-4] + '.png')
                dds.append((src, dst))
            elif low.endswith('.png'):
                if pat and not fnmatch.fnmatch(low, pat):
                    continue
                pngs.append((src, os.path.join(out, rel)))
    dds.sort(key=lambda j: j[0])
    return dds, pngs, total


def run_pool(worker, jobs_list, njobs: int, label: str):
    counts: dict[str, int] = {}
    fails: list[tuple[str, str]] = []
    src_bytes = dst_bytes = 0
    done, total = 0, len(jobs_list)
    t0 = time.time()
    with Pool(processes=njobs) as pool:
        for status, src, s_in, s_out, err in pool.imap_unordered(
                worker, jobs_list, chunksize=16):
            counts[status] = counts.get(status, 0) + 1
            src_bytes += s_in
            dst_bytes += s_out
            if status == 'error':
                fails.append((src, err))
            done += 1
            if done % 1000 == 0 or done == total:
                dt = max(time.time() - t0, 1e-9)
                print(f'{label}: {done}/{total} ({done / dt:.0f}/s, '
                      f'{src_bytes / 2**30:.1f}G->{dst_bytes / 2**30:.1f}G)',
                      file=sys.stderr)
    return counts, fails, src_bytes, dst_bytes


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--root', default='raw_assets',
                    help='racine des assets extraits')
    ap.add_argument('--out', default='godot_project/assets/textures')
    ap.add_argument('--failures', default='docs/texture_failures.txt')
    ap.add_argument('--jobs', '-j', type=int, default=os.cpu_count() or 4)
    ap.add_argument('--limit', type=int, default=0)
    ap.add_argument('--only', metavar='GLOB',
                    help='filtre sur le chemin relatif (insensible casse)')
    ap.add_argument('--estimate', type=int, metavar='N',
                    help='convertit N fichiers échantillonnés dans un '
                         'dossier temporaire, affiche le ratio PNG/DDS '
                         'et la projection, puis quitte')
    ap.add_argument('--no-png-copy', action='store_true',
                    help='ne pas copier les .png de themes/uidata')
    args = ap.parse_args(argv)

    dds, pngs, total_src = collect_jobs(args.root, args.out, args.only)
    print(f'{len(dds)} .dds ({total_src / 2**30:.2f} Gio), '
          f'{len(pngs)} .png à copier', file=sys.stderr)

    if args.estimate:
        n = min(args.estimate, len(dds))
        step = max(1, len(dds) // n)
        sample = dds[::step][:n]
        tmp_parent = (os.path.dirname(os.path.abspath(args.out)) or '.')
        os.makedirs(tmp_parent, exist_ok=True)
        with tempfile.TemporaryDirectory(
                dir=tmp_parent, prefix='.texsample-') as tmp:
            jobs = [(s, os.path.join(tmp, str(i) + '.png'))
                    for i, (s, _d) in enumerate(sample)]
            counts, fails, s_in, s_out = run_pool(_convert_one, jobs,
                                                  args.jobs, 'estimate')
        if s_in:
            ratio = s_out / s_in
            print(f'échantillon: n={len(sample)} ok={counts.get("ok", 0)} '
                  f'échecs={counts.get("error", 0)}')
            print(f'ratio PNG/DDS = {ratio:.2f} '
                  f'({s_in / 2**20:.1f} Mio -> {s_out / 2**20:.1f} Mio)')
            print(f'projection totale: {total_src * ratio / 2**30:.1f} Gio')
            for src, err in fails[:20]:
                print('FAIL', src, '->', err)
        return 1 if fails else 0

    if args.limit:
        dds = dds[:args.limit]

    all_fails: list[tuple[str, str]] = []
    if pngs and not args.no_png_copy:
        c, f, _si, _so = run_pool(_copy_one, pngs, min(4, args.jobs),
                                  'png-copy')
        print('png copiés:', c, file=sys.stderr)
        all_fails += f
    counts, fails, s_in, s_out = run_pool(_convert_one, dds, args.jobs,
                                          'dds')
    all_fails += fails

    print(f'résultat: {counts} | {s_in / 2**30:.2f} Gio DDS -> '
          f'{s_out / 2**30:.2f} Gio PNG')
    if all_fails:
        os.makedirs(os.path.dirname(args.failures) or '.', exist_ok=True)
        with open(args.failures, 'w') as f:
            for src, err in sorted(all_fails):
                f.write(f'{src}\t{err}\n')
        print(f'{len(all_fails)} échecs -> {args.failures}',
              file=sys.stderr)
    return 1 if all_fails else 0


if __name__ == '__main__':
    sys.exit(main())
