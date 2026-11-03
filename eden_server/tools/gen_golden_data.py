#!/usr/bin/env python3
"""Découpe les captures zsniff (captures/*.bin) en cas de test par NCID.

Format des lignes décodées :
    [HH:MM:SS.mmm] E ch=<hex> NCID=<n> len=<l> <hexpayload>
    [HH:MM:SS.mmm] D ch=<hex> NCID=<n> len=<l> <hexpayload>
E = émis par le serveur (CNC_*), D = reçu du client (CNE_*).
Le hexpayload INCLUT le u16 NCID en tête (little-endian).

Produit dans server/tests/Eden.Tests/Data/golden/ :
    ncid_<n>.json : {"ncid": n, "cases": [{"dir": "E|D",
                     "body": "<hex sans NCID>", "ts": ..., "src": ...}]}
Une nouvelle capture rejouée → régénérer ce dossier suffit à produire
de nouveaux cas xunit (GoldenBytesTests, MemberData).

Usage : tools/gen_golden_data.py
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CAPTURES = ROOT / 'captures'
OUT = ROOT / 'server' / 'tests' / 'Eden.Tests' / 'Data' / 'golden'

LINE = re.compile(
    r'^\[[\d:]+\.\d+\]\s+([EDS])\s+(?:ch=[0-9a-f]+\s+)?'
    r'NCID=(\d+)\s+len=(\d+)\s+([0-9a-fA-F]+)\s*$')

# Nombre max de cas retenus par (ncid, direction) — les captures
# contiennent des centaines de trames identiques (mouvement, combat) ;
# on déduplique sur les bytes, puis on borne pour garder la suite
# rapide. Les cas sont triés par longueur pour couvrir les variantes.
MAX_CASES_PER_KIND = 8


def main() -> int:
    cases: dict[int, list[dict]] = {}
    skipped = 0
    for cap in sorted(CAPTURES.glob('*.bin')):
        for raw in cap.read_text(errors='replace').splitlines():
            m = LINE.match(raw.strip())
            if not m:
                continue
            direction, ncid, dlen, hexpl = m.group(1), int(m.group(2)), \
                int(m.group(3)), m.group(4)
            payload = bytes.fromhex(hexpl)
            if len(payload) != dlen or len(payload) < 2:
                skipped += 1
                continue
            # Le u16 NCID doit être cohérent avec l'annonce du décodeur.
            wire_ncid = payload[0] | (payload[1] << 8)
            if wire_ncid != ncid:
                skipped += 1
                continue
            cases.setdefault(ncid, []).append({
                'dir': direction,
                'body': payload[2:].hex(),
                'src': cap.name,
            })

    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob('ncid_*.json'):
        old.unlink()

    n_files = n_cases = 0
    for ncid, items in sorted(cases.items()):
        # Déduplique (dir, body) en gardant la première occurrence,
        # puis borne par direction.
        seen: set[tuple[str, str]] = set()
        kept: list[dict] = []
        per_dir: dict[str, int] = {}
        for it in items:
            key = (it['dir'], it['body'])
            if key in seen:
                continue
            seen.add(key)
            if per_dir.get(it['dir'], 0) >= MAX_CASES_PER_KIND:
                continue
            per_dir[it['dir']] = per_dir.get(it['dir'], 0) + 1
            kept.append(it)
        (OUT / f'ncid_{ncid}.json').write_text(json.dumps(
            {'ncid': ncid, 'cases': kept}, indent=1))
        n_files += 1
        n_cases += len(kept)

    print(f'golden: {n_files} NCID, {n_cases} cas '
          f'({skipped} lignes ignorées) → {OUT}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
