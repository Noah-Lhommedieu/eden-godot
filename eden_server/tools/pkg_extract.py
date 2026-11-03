#!/usr/bin/env python3
"""Extracteur des archives Aeria/Reforged `pkg/pkg.idx` + `pkgNNN.pkg`.

Format de l'index (script QuickBMS `quickbms/aeriagames.bms` 0.2.2, validé
sur l'index Reforged) :
  en-tête 292 octets (260 + 32), puis des enregistrements de 592 octets :
    +0   u32 dummy
    +4   u32 offset dans pkgNNN.pkg
    +8   u32 dummy
    +12  u32 zsize (taille compressée)
    +16  u64 crc ; +24 u64 flags ; +32/+40/+48 u64 timestamps
    +56  u32 size (taille décompressée)
    +60  char[260] name2 (nom de fichier) ; +320 char[260] name (dossier) ;
         chemin final = name + name2 (comme `string NAME + NAME2` du .bms)
    +580 u32 dummy ; +584 u32 pkg (numéro de pkgNNN) ; +588 u32 dummy
  flags & 0x40 : entrée non supportée (stockée brute, size = zsize).
  zsize == size : stocké brut ; sinon bloc zlib.

Usage : pkg_extract.py <dossier_pkg> <dossier_sortie> [--list] [--verify N]
Les chemins sont écrits tels quels (séparateurs `\\` -> `/`).
"""
import os
import struct
import sys
import zlib

HEADER, REC = 292, 592


def parse_index(path):
    d = open(path, 'rb').read()
    n = (len(d) - HEADER) // REC
    for i in range(n):
        o = HEADER + i * REC
        _, off, _, zsize = struct.unpack_from('<IIII', d, o)
        crc, flags = struct.unpack_from('<QQ', d, o + 16)
        size, = struct.unpack_from('<I', d, o + 56)
        name2 = d[o + 60:o + 320].split(b'\0', 1)[0].decode('latin-1')
        name = d[o + 320:o + 580].split(b'\0', 1)[0].decode('latin-1')
        pkg, = struct.unpack_from('<I', d, o + 584)
        yield dict(off=off, zsize=zsize, size=size, flags=flags, crc=crc,
                   pkg=pkg, name=(name + name2).replace('\\', '/').lstrip('/'))


def read_entry(pkgdir, e, handles):
    f = handles.get(e['pkg'])
    if f is None:
        p = os.path.join(pkgdir, 'pkg%03d.pkg' % e['pkg'])
        f = handles[e['pkg']] = open(p, 'rb') if os.path.exists(p) else False
    if not f:
        return None
    f.seek(e['off'])
    raw = f.read(e['zsize'])
    if e['flags'] & 0x40 or e['zsize'] == e['size']:
        return raw
    return zlib.decompress(raw)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    pkgdir, out = args[0], args[1] if len(args) > 1 else None
    entries = list(parse_index(os.path.join(pkgdir, 'pkg.idx')))
    if '--list' in sys.argv:
        for e in entries:
            print(e['pkg'], e['off'], e['zsize'], e['size'], hex(e['flags']), e['name'])
        return
    limit = None
    if '--verify' in sys.argv:
        limit = int(sys.argv[sys.argv.index('--verify') + 1])
    handles, ok, bad, missing = {}, 0, 0, 0
    for e in entries[:limit]:
        try:
            data = read_entry(pkgdir, e, handles)
        except zlib.error as ex:
            bad += 1
            print('ERR zlib', e['name'], ex, file=sys.stderr)
            continue
        if data is None:
            missing += 1
            continue
        if len(data) != e['size'] and not e['flags'] & 0x40:
            bad += 1
            print('ERR size', e['name'], len(data), e['size'], file=sys.stderr)
            continue
        if limit is None and out:
            rel = e['name']
            if rel.startswith('UIData/'):
                rel = 'uidata/' + rel[len('UIData/'):]
            dst = os.path.join(out, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, 'wb') as w:
                w.write(data)
        ok += 1
    print(f'entrées={len(entries)} ok={ok} erreurs={bad} pkg_manquants={missing}')


if __name__ == '__main__':
    main()
