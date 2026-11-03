#!/usr/bin/env python3
"""Compare deux traces zsniff (native vs C#) NCID par NCID.

Entrées : fichiers au format zsniff (lignes
    [ts] E ch=.. NCID=n len=l <hex>   — serveur → client
    [ts] D ch=.. NCID=n len=l <hex>   — client → serveur
ou les R/E/D du MITM).

Usage :
    tools/wire_diff.py captures/native.bin captures/cs.bin \\
        [--masks tools/wire_masks.json]

--masks : JSON {"<ncid>": [[off, len], ...]} — plages d'octets du body
    (hors NCID) à ignorer pour le contenu (handles de session,
    timestamps, ids générés, positions). Exemple :
    {"301": [[0, 4]], "326": [[0, 4], [10, 4]]}

Alignement : difflib.SequenceMatcher sur la séquence (dir, ncid) — les
flux serveur entrelacent les pushes, une comparaison positionnelle
stricte produirait des faux écarts.

Sortie : tableau des écarts — missing/extra (présence), len!= (taille),
bytes!= (contenu hors masques). Code retour 1 si écart, 0 sinon.
"""
import difflib
import json
import re
import sys
from pathlib import Path

LINE = re.compile(
    r'^\[[\d:]+\.\d+\]\s+([EDS])\s+(?:ch=[0-9a-f]+\s+)?'
    r'NCID=(\d+)\s+len=(\d+)\s+([0-9a-fA-F]+)\s*$')


def load(path: str) -> list[tuple[str, int, bytes]]:
    out = []
    for raw in Path(path).read_text(errors='replace').splitlines():
        m = LINE.match(raw.strip())
        if not m:
            continue
        d, ncid, dlen, hexpl = m.group(1), int(m.group(2)), \
            int(m.group(3)), m.group(4)
        payload = bytes.fromhex(hexpl)
        if len(payload) != dlen or len(payload) < 2:
            continue
        if payload[0] | (payload[1] << 8) != ncid:
            continue
        if d == 'R':  # trames brutes chiffrées — ignorées
            continue
        out.append((d, ncid, payload[2:]))
    return out


def masked(body: bytes, spans: list[list[int]]) -> bytes:
    b = bytearray(body)
    for off, ln in spans:
        b[off:off + ln] = b'\x00' * ln
    return bytes(b)


def main(argv: list[str]) -> int:
    if len(argv) < 3:
        print(__doc__)
        return 2
    fa, fb = argv[1], argv[2]
    masks: dict[int, list[list[int]]] = {}
    if '--masks' in argv:
        mp = Path(argv[argv.index('--masks') + 1])
        masks = {int(k): v for k, v in
                 json.loads(mp.read_text()).items()}

    ta, tb = load(fa), load(fb)
    sa = [(d, n) for d, n, _ in ta]
    sb = [(d, n) for d, n, _ in tb]
    print(f'{fa}: {len(sa)} trames | {fb}: {len(sb)} trames')

    diffs = 0
    sm = difflib.SequenceMatcher(None, sa, sb, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == 'equal':
            for (d, ncid_a, body_a), (_, ncid_b, body_b) in zip(
                    ta[i1:i2], tb[j1:j2]):
                span = masks.get(ncid_a, [])
                if len(body_a) != len(body_b):
                    diffs += 1
                    print(f'len!= {d} NCID={ncid_a} '
                          f'{len(body_a)}B vs {len(body_b)}B')
                elif masked(body_a, span) != masked(body_b, span):
                    diffs += 1
                    shown = min(len(body_a), 24)
                    print(f'bytes!= {d} NCID={ncid_a} '
                          f'{body_a[:shown].hex()} vs '
                          f'{body_b[:shown].hex()}'
                          + (f' (masqué {span})' if span else ''))
        else:
            for d, ncid in sa[i1:i2]:
                diffs += 1
                print(f'missing {d} NCID={ncid} (absent côté B)')
            for d, ncid in sb[j1:j2]:
                diffs += 1
                print(f'extra   {d} NCID={ncid} (absent côté A)')
    print(f'\n{diffs} écart(s)')
    return 1 if diffs else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
