#!/usr/bin/env python3
"""Genere docs/generated/FEATURE-MATRIX.md — NCID -> classe -> sites
d'emission -> statut de preuve wire.

Sources :
  - classes NetCommand generees (server/src/Eden.Core/Networking/
    Messages/Generated/*.g.cs) : opcode + nom.
  - sites d'emission : `SendAsync(new X` et `SendPacketAsync(n,`
    dans server/src/.
  - preuve : tests/Eden.Tests/Data/golden/ncid_*.json (captures
    natives) + tests/Eden.Tests/Data/known_incomplete.json.

Statuts :
  GOLDEN   — la classe round-trippe des corps captures.
  INCOMPLET— golden present mais la classe ne round-trippe pas
             (codec dedie utilise en production).
  PASSTHRU — emission brute d'un corps entrant (relais volontaire).
  DESASM   — layout valide par le Serialize natif, pas de capture.
  NON PROUVE — aucune preuve.
"""
import json, os, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'server' / 'src'
GENDIR = SRC / 'Eden.Core' / 'Networking' / 'Messages' / 'Generated'
TESTDATA = ROOT / 'server' / 'tests' / 'Eden.Tests' / 'Data'


def collect_classes():
    classes = {}  # opcode -> (class_name, file)
    name2op = {}
    for f in GENDIR.glob('*.g.cs'):
        text = f.read_text(encoding='utf-8')
        for m in re.finditer(
                r'public sealed class (\w+) : NetCommand.*?'
                r'public override int Opcode => (\d+)', text, re.S):
            name, op = m.group(1), int(m.group(2))
            classes.setdefault(op, (name, f.name))
            name2op[name] = op
    return classes, name2op


def collect_emitters(name2op):
    typed = {}   # opcode -> set(file)
    raw = {}     # opcode -> set(file)
    # Constantes NCID nommées (`public const ushort NcidX = 46`)
    # résolues — les pushes monde (FamilyPackets.Ncid*, …) passent
    # par ces noms au lieu de littéraux.
    const_re = re.compile(
        r'\bconst\s+(?:ushort|short|int|byte)\s+(\w+)\s*=\s*(\d+)\s*;')
    consts = {}
    texts = []
    for f in SRC.rglob('*.cs'):
        text = f.read_text(encoding='utf-8')
        texts.append((str(f.relative_to(SRC)), text))
        for m in const_re.finditer(text):
            consts.setdefault(m.group(1), int(m.group(2)))
    # Emissions typées `XxxAsync(...new CNC_Foo {..}` — la virgule
    # d'arguments avant `new` est tolérée (helpers de broadcast genre
    # NotifyFamilyMembersAsync) — et emissions brutes
    # `XxxAsync(...<ncid|NcidConst>, ...)` (SendPacketAsync & helpers).
    send_re = re.compile(
        r'(?:Send|Notify|Broadcast|Push)\w*Async\('
        r'[^;{}()]*?new\s+(\w+)')
    raw_re = re.compile(
        r'(?:Send|Notify|Broadcast|Push)\w*Async\('
        r'[^;{}]*?(?<![\w])(\d+)\s*,')
    const_arg_re = re.compile(
        r'(?:Send|Notify|Broadcast|Push)\w*Async\('
        r'[^;{}]*?\b((?:\w+\.)?\w*Ncid\w*)\s*,')
    # `new CNC_*` : les classes CNC_* sont exclusivement des
    # commandes serveur→client — toute instanciation hors .g.cs est
    # un site d'emission (couvre `var x = new CNC_…; SendAsync(x)`).
    inst_re = re.compile(r'\bnew\s+(CNC_\w+)')
    for rel, text in texts:
        for m in send_re.finditer(text):
            if m.group(1) in name2op:
                typed.setdefault(name2op[m.group(1)], set()).add(rel)
        for m in inst_re.finditer(text):
            if m.group(1) in name2op:
                typed.setdefault(name2op[m.group(1)], set()).add(rel)
        for m in raw_re.finditer(text):
            raw.setdefault(int(m.group(1)), set()).add(rel)
        for m in const_arg_re.finditer(text):
            name = m.group(1).split('.')[-1]
            if name in consts:
                raw.setdefault(consts[name], set()).add(rel)
    return typed, raw


def main():
    classes, name2op = collect_classes()
    typed, raw = collect_emitters(name2op)
    golden = {int(p.stem.split('_')[1]): len(json.loads(p.read_text()))
              for p in (TESTDATA / 'golden').glob('ncid_*.json')}
    incomplete = set(json.loads(
        (TESTDATA / 'known_incomplete.json').read_text()).keys())
    incomplete = {int(x) for x in incomplete}

    lines = [
        '# Feature-matrix protocole — generee par '
        '`tools/gen_feature_matrix.py`',
        '',
        '> Ne pas editer a la main — relancer le script.',
        '',
        '| NCID | Classe | Statut | Captures | Emission |',
        '|---|---|---|---|---|',
    ]
    stats = {'GOLDEN': 0, 'INCOMPLET': 0, 'PASSTHRU': 0,
             'DESASM': 0, 'NON PROUVE': 0}
    for op in sorted(set(classes) | set(raw) | set(golden)):
        cls = classes.get(op, ('—', ''))[0]
        emit = sorted(typed.get(op, set()))
        rawem = sorted(raw.get(op, set()))
        if op in golden and op not in incomplete:
            status = 'GOLDEN'
        elif op in incomplete:
            status = 'INCOMPLET'
        elif rawem and not emit:
            status = 'PASSTHRU'
        elif emit:
            status = 'DESASM'
        else:
            status = 'NON PROUVE'
        stats[status] += 1
        cap = golden.get(op, 0)
        sites = ', '.join(f'`{os.path.basename(e)}`' for e in
                          (emit or rawem)) or '—'
        lines.append(f'| {op} | `{cls}` | {status} | {cap or "—"} '
                     f'| {sites} |')
    lines += [
        '',
        '## Bilan',
        '',
        '| Statut | NCIDs |',
        '|---|---|',
    ]
    for k, v in stats.items():
        lines.append(f'| {k} | {v} |')
    out = ROOT / 'docs' / 'generated' / 'FEATURE-MATRIX.md'
    out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('sauvegarde', out)
    for k, v in stats.items():
        print(f'  {k}: {v}')


if __name__ == '__main__':
    main()
