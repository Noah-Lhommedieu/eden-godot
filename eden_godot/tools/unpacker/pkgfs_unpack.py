#!/usr/bin/env python3
"""CLI pour les archives Aeria/Reforged ``pkg/pkg.idx`` + ``pkgNNN.pkg``.

Réécriture propre de ``eden_server/tools/pkg_extract.py`` (même format,
validé) avec sous-commandes argparse :

  list     liste les entrées de l'index
  verify   décompresse chaque entrée et contrôle la taille ;
           avec ``--against DIR`` compare aussi taille+contenu au fichier
           déjà extrait sous DIR
  extract  écrit chaque entrée vers ``--out DIR`` (skip si le fichier
           existe déjà avec la même taille, sauf ``--force``)

Format de l'index : en-tête 292 o, puis records de 592 o :
  +4 u32 offset, +12 u32 zsize, +24 u64 flags, +56 u32 size,
  +60 char[260] name2 (fichier), +320 char[260] name (dossier),
  +584 u32 numéro pkg.  Chemin = name+name2, ``\\`` -> ``/``.
flags&0x40 ou zsize==size : brut ; sinon zlib.
``UIData/`` est remappé en ``uidata/`` (insensible à la casse).
"""
from __future__ import annotations

import argparse
import fnmatch
import os
import struct
import sys
import time
import zlib
from dataclasses import dataclass
from multiprocessing import Pool

HEADER, REC = 292, 592
CHUNK = 1 << 20  # 1 Mio pour les comparaisons de contenu


class UnsafePathError(ValueError):
    pass


@dataclass(frozen=True)
class Entry:
    index: int
    pkg: int
    off: int
    zsize: int
    size: int
    flags: int
    crc: int
    rel: str  # chemin relatif normalisé et sûr


def rel_path(raw_name: str) -> str:
    """Normalise un chemin d'index en chemin relatif sûr.

    ``\\`` -> ``/``, préfixe ``UIData/`` -> ``uidata/``, rejet des
    composants ``..``/absolus/drive letters (anti path-traversal).
    """
    rel = raw_name.replace('\\', '/').lstrip('/')
    parts = [p for p in rel.split('/') if p not in ('', '.')]
    if not parts or any(p == '..' for p in parts):
        raise UnsafePathError(f'chemin dangereux dans l\'index: {raw_name!r}')
    if parts[0].endswith(':') or parts[0].startswith('~'):
        raise UnsafePathError(f'chemin absolu dans l\'index: {raw_name!r}')
    if parts[0].lower() == 'uidata':
        parts[0] = 'uidata'
    return '/'.join(parts)


def parse_index(idx_path: str) -> list[Entry]:
    with open(idx_path, 'rb') as f:
        d = f.read()
    n = (len(d) - HEADER) // REC
    entries: list[Entry] = []
    for i in range(n):
        o = HEADER + i * REC
        off, zsize = struct.unpack_from('<IxxxxI', d, o + 4)
        crc, flags = struct.unpack_from('<QQ', d, o + 16)
        size, = struct.unpack_from('<I', d, o + 56)
        name2 = d[o + 60:o + 320].split(b'\0', 1)[0].decode('latin-1')
        name = d[o + 320:o + 580].split(b'\0', 1)[0].decode('latin-1')
        pkg, = struct.unpack_from('<I', d, o + 584)
        entries.append(Entry(index=i, pkg=pkg, off=off, zsize=zsize,
                             size=size, flags=flags, crc=crc,
                             rel=rel_path(name + name2)))
    return entries


def filter_entries(entries: list[Entry], args: argparse.Namespace) -> list[Entry]:
    if args.only:
        pat = args.only.lower()
        entries = [e for e in entries
                   if fnmatch.fnmatch(e.rel.lower(), pat)]
    if args.limit:
        entries = entries[:args.limit]
    return entries


# --- accès aux pkg (un cache de handles par processus) -------------------

_proc_handles: dict[int, object] = {}


def _pkg_handle(pkgdir: str, pkg_no: int):
    f = _proc_handles.get(pkg_no)
    if f is None:
        p = os.path.join(pkgdir, 'pkg%03d.pkg' % pkg_no)
        try:
            f = open(p, 'rb')
        except OSError:
            f = False
        _proc_handles[pkg_no] = f
    return f or None


def read_entry(pkgdir: str, e: Entry) -> bytes | None:
    """Retourne le contenu décompressé, ou None si le pkg manque."""
    f = _pkg_handle(pkgdir, e.pkg)
    if f is None:
        return None
    f.seek(e.off)
    raw = f.read(e.zsize)
    if len(raw) != e.zsize:
        raise IOError(f'lecture courte: {len(raw)}/{e.zsize} o')
    if e.flags & 0x40 or e.zsize == e.size:
        return raw
    try:
        return zlib.decompress(raw)
    except zlib.error:
        pass
    # Variante client FR 2016 : pseudo-en-tête zlib 2 o (CMF 0x88) puis
    # soit deflate standard (petites entrées), soit Deflate64 (fenêtre
    # 64K que zlib ne sait pas décoder — grandes entrées).
    try:
        do = zlib.decompressobj(-15)
        out = do.decompress(raw[2:]) + do.flush()
        if len(out) == e.size:
            return out
    except zlib.error:
        pass
    return _inflate_deflate64(raw[2:], e.size)


def _inflate_deflate64(body: bytes, size: int) -> bytes:
    """Décode un flux Deflate64 en l'enveloppant dans un .zip minimal
    (méthode 9) lu par ``zipfile_deflate64``. Le CRC est inconnu avant
    décodage : le contrôle est neutralisé pendant la lecture, la taille
    attendue servant de validation."""
    import io
    import zipfile as _zf
    from zipfile_deflate64 import ZipFile

    name = b'f'
    lh = struct.pack('<IHHHHHIIIHH', 0x04034B50, 20, 0, 9, 0, 0, 0,
                     len(body), size, len(name), 0)
    local = lh + name + body
    cd = struct.pack('<IHHHHHHIIIHHHHHII', 0x02014B50, 20, 20, 0, 9,
                     0, 0, 0, len(body), size, len(name), 0, 0, 0, 0, 0, 0)
    eocd = struct.pack('<IHHHHIIH', 0x06054B50, 0, 0, 1, 1,
                       len(cd) + len(name), len(local), 0)
    orig = _zf.ZipExtFile._update_crc
    _zf.ZipExtFile._update_crc = lambda self, data: None
    try:
        out = ZipFile(io.BytesIO(local + cd + name + eocd)).read('f')
    finally:
        _zf.ZipExtFile._update_crc = orig
    if len(out) != size:
        raise IOError(f'deflate64: {len(out)}/{size} o')
    return out


def _same_file(dst: str, data: bytes, size: int) -> str | None:
    """Compare ``data`` au fichier ``dst``. Retourne None si identique."""
    try:
        st = os.stat(dst)
    except OSError:
        return 'absent'
    if st.st_size != len(data):
        return f'taille {st.st_size} != {len(data)}'
    with open(dst, 'rb') as f:
        pos = 0
        while True:
            blk = f.read(CHUNK)
            if not blk:
                break
            if blk != data[pos:pos + len(blk)]:
                return 'contenu différent'
            pos += len(blk)
    return None


# --- workers multiprocessing --------------------------------------------

def _verify_one(job) -> tuple:
    pkgdir, against, e = job
    try:
        data = read_entry(pkgdir, e)
    except Exception as ex:
        return ('error', e, f'{type(ex).__name__}: {ex}')
    if data is None:
        return ('missing_pkg', e, f'pkg{e.pkg:03d}.pkg absent')
    if len(data) != e.size and not (e.flags & 0x40):
        return ('error', e, f'taille décompressée {len(data)} != index {e.size}')
    if against:
        why = _same_file(os.path.join(against, e.rel), data, e.size)
        if why:
            return ('diff', e, why)
    return ('ok', e, '')


def _extract_one(job) -> tuple:
    pkgdir, outdir, force, e = job
    dst = os.path.join(outdir, e.rel)
    if not force:
        try:
            if os.stat(dst).st_size == e.size:
                return ('skipped', e, '')
        except OSError:
            pass
    try:
        data = read_entry(pkgdir, e)
    except Exception as ex:
        return ('error', e, f'{type(ex).__name__}: {ex}')
    if data is None:
        return ('missing_pkg', e, f'pkg{e.pkg:03d}.pkg absent')
    if len(data) != e.size and not (e.flags & 0x40):
        return ('error', e, f'taille {len(data)} != index {e.size}')
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp = dst + '.part'
    with open(tmp, 'wb') as w:
        w.write(data)
    os.replace(tmp, dst)
    return ('ok', e, '')


def _run_pool(worker, jobs_iter, jobs: int, total: int, label: str):
    """Itère les résultats, affiche la progression, cumule les statuts."""
    counts: dict[str, int] = {}
    errors: list[tuple[Entry, str]] = []
    done = 0
    t0 = time.time()
    with Pool(processes=jobs) as pool:
        for status, e, detail in pool.imap_unordered(worker, jobs_iter,
                                                   chunksize=64):
            counts[status] = counts.get(status, 0) + 1
            if status not in ('ok', 'skipped'):
                errors.append((e, f'{status}: {detail}'))
            done += 1
            if done % 5000 == 0 or done == total:
                dt = max(time.time() - t0, 1e-9)
                print(f'{label}: {done}/{total} '
                      f'({done / dt:.0f}/s)', file=sys.stderr)
    return counts, errors


# --- sous-commandes ------------------------------------------------------

def cmd_list(args) -> int:
    entries = filter_entries(parse_index(args.index), args)
    for e in entries:
        print(e.pkg, e.off, e.zsize, e.size, hex(e.flags), e.rel)
    print(f'# {len(entries)} entrée(s)', file=sys.stderr)
    return 0


def _index_path(pkgdir: str) -> str:
    return os.path.join(pkgdir, 'pkg.idx')


def cmd_verify(args) -> int:
    entries = filter_entries(parse_index(_index_path(args.pkgdir)), args)
    total = len(entries)
    jobs_iter = ((args.pkgdir, args.against, e) for e in entries)
    counts, errors = _run_pool(_verify_one, jobs_iter, args.jobs, total,
                               'verify')
    print(f'entrées={total} ' + ' '.join(f'{k}={v}'
                                         for k, v in sorted(counts.items())))
    for e, msg in errors:
        print(f'ERR [{e.index}] {e.rel} -> {msg}', file=sys.stderr)
    bad = total - counts.get('ok', 0)
    return 1 if bad else 0


def cmd_extract(args) -> int:
    entries = filter_entries(parse_index(_index_path(args.pkgdir)), args)
    total = len(entries)
    jobs_iter = ((args.pkgdir, args.out, args.force, e) for e in entries)
    counts, errors = _run_pool(_extract_one, jobs_iter, args.jobs, total,
                               'extract')
    print(f'entrées={total} ' + ' '.join(f'{k}={v}'
                                         for k, v in sorted(counts.items())))
    for e, msg in errors:
        print(f'ERR [{e.index}] {e.rel} -> {msg}', file=sys.stderr)
    bad = total - counts.get('ok', 0) - counts.get('skipped', 0)
    return 1 if bad else 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(
        description='Inspecte/vérifie/extrait pkg.idx + pkgNNN.pkg.')
    sub = ap.add_subparsers(dest='cmd', required=True)

    def common(p):
        p.add_argument('pkgdir', help='dossier contenant pkg.idx et pkgNNN.pkg')
        p.add_argument('--limit', type=int, default=0,
                       help='ne traiter que les N premières entrées')
        p.add_argument('--only', metavar='GLOB',
                       help='filtrer les chemins (ex: "*.dds", insensible '
                            'à la casse)')
        p.add_argument('--jobs', '-j', type=int, default=os.cpu_count() or 4)

    p = sub.add_parser('list', help='liste les entrées de l\'index')
    p.add_argument('index', help='chemin de pkg.idx')
    p.add_argument('--limit', type=int, default=0)
    p.add_argument('--only', metavar='GLOB')
    p.set_defaults(func=cmd_list, jobs=1)

    p = sub.add_parser('verify', help='décompresse et contrôle les entrées')
    common(p)
    p.add_argument('--against', metavar='DIR',
                   help='compare aussi au fichier déjà extrait sous DIR')
    p.set_defaults(func=cmd_verify)

    p = sub.add_parser('extract', help='écrit les entrées vers --out')
    common(p)
    p.add_argument('--out', required=True, metavar='DIR')
    p.add_argument('--force', action='store_true',
                   help='réécrit même si le fichier existe (même taille)')
    p.set_defaults(func=cmd_extract)

    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == '__main__':
    sys.exit(main())
