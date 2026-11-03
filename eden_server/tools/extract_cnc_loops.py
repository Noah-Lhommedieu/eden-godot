#!/usr/bin/env python3
"""Extrait la structure fil exacte des CNC_* (Serialize) — incluant les
champs en boucle.

Pattern natif (lapis::COutBinStream) :
  - scalaires :  mov eax,[this+off]; call lsi/lsEs/lsEh/lsERSs/...
  - boucle    :  u16 count = [this+0x8] >> 2  (ou [this+off] direct)
                 pour i in count : champ = array[[this+off]] [i]
                 (index esi*4 = int/string, esi*2 = short, esi*1 = byte)

Produit docs/generated/cnc_loop_layout.json :
  { "ZoneServer": { "CNC_CZ_ClientMailList":
      {"count_field": "u16", "loop": ["int","string","short",...],
       "scalar_prefix": [...], "scalar_suffix": [...]} } }
"""
import json, re, subprocess, sys
from collections import OrderedDict

BINARIES = {
    'ZoneServer':  'server_us/ZoneServer/ZoneServer',
    'WorldServer': 'server_us/WorldServer/WorldServer',
    'MissionServer': 'server_us/MissionServer/MissionServer',
    'LoginServer': 'server_us/LoginServer/LoginServer',
    'GatewayServer': 'server_us/GatewayServer/GatewayServer',
}

OP = {
    'lsEb': 'byte', 'lsEc': 'sbyte', 'lsEh': 'ubyte',
    'lsEs': 'short', 'lsEt': 'ushort',
    'lsEi': 'int', 'lsEj': 'uint', 'lsEl': 'long',
    'lsEm': 'ulong', 'lsEf': 'float', 'lsEd': 'double',
    'lsERSs': 'string', 'lsERKSs': 'string',
}

def func_disasm(binary, addr, size=0x4000):
    out = subprocess.run(
        ['objdump', '-d', '--start-address=%#x' % addr,
         '--stop-address=%#x' % (addr + size), '-M', 'intel', binary],
        capture_output=True, text=True).stdout
    lines = []
    for m in re.finditer(r'^\s+([0-9a-f]+):\s+(?:[0-9a-f]{2} )+\s*(\S.*)$',
                         out, re.M):
        lines.append((int(m.group(1), 16), m.group(2)))
    # coupe au premier 'ret'
    res = []
    for a, ins in lines:
        res.append((a, ins))
        if ins.startswith('ret'): break
    return res

def analyse(insns):
    """Retourne (scalars_prefix, loop_fields, scalars_suffix).

    Champ indexé = précédé d'un accès [reg*scale] (n'importe quel
    registre de compteur : esi/ecx/ebx/edi) ou d'un 'add reg,[this+off]'
    (base de tableau + offset indexé déjà calculé dans reg).
    """
    fields = []          # (kind, indexed)
    pending_indexed = False
    for addr, ins in insns:
        # indexation: [x*1/2/4] ou base+reg calculé (add reg,[this+off])
        if (re.search(r'\*1\]', ins) or re.search(r'\*2\]', ins)
                or re.search(r'\*4\]', ins)
                or re.search(r'add\s+e\w+,DWORD PTR \[e\w+\+0x[0-9a-f]+\]', ins)):
            pending_indexed = True
        m = re.search(r'call\s+[0-9a-f]+ <_ZN5lapis13COutBinStream(lsE\w+)>', ins)
        if m:
            kind = OP.get(m.group(1))
            if kind:
                fields.append((kind, pending_indexed))
                pending_indexed = False
    # Découpe : le préfixe = champs non-indexés avant le 1er indexé
    prefix, loop, suffix = [], [], []
    seen_idx = False
    for kind, idx in fields:
        if idx:
            seen_idx = True
            loop.append(kind)
        elif not seen_idx:
            prefix.append(kind)
        else:
            suffix.append(kind)
    return prefix, loop, suffix

def main():
    out = {}
    for name, binary in BINARIES.items():
        symfile = f'docs/generated/{name}-symbols.txt'
        try:
            syms = open(symfile).read()
        except FileNotFoundError:
            continue
        cmds = {}
        # CNC_*::Serialize(COutBinStream&) — la méthode virtuelle de data
        for m in re.finditer(
                r'^([0-9a-f]{8}) [WwTt] lapis::(CN[CET][A-Z]*_\w+)::Serialize\(lapis::COutBinStream&\)\s*$',
                syms, re.M):
            addr, cls = int(m.group(1), 16), m.group(2)
            insns = func_disasm(binary, addr)
            prefix, loop, suffix = analyse(insns)
            if loop:
                cmds[cls] = {'scalar_prefix': prefix, 'loop': loop,
                             'scalar_suffix': suffix}
            elif prefix or suffix:
                cmds[cls] = {'scalar_prefix': prefix + suffix,
                             'loop': [], 'scalar_suffix': []}
        out[name] = cmds
        n_loop = sum(1 for v in cmds.values() if v['loop'])
        print(f'{name}: {len(cmds)} Serialize analysés, {n_loop} avec boucle')
    json.dump(out, open('docs/generated/cnc_loop_layout.json', 'w'),
              indent=1, ensure_ascii=False)

if __name__ == '__main__':
    main()
