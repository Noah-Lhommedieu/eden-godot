#!/usr/bin/env python3
"""
Extraction des vrais opcodes (NCIDs) depuis les binaires server_us.

Methode:
  - Les classes CNC_*/CNE_* ont une variable statique
    `lapis::CNetCommand<T>::NCID` dans .data.
  - Au demarrage, `lapis::InitPCCommandTable` enregistre les commandes
    dans l'ordre: chaque bloc fait `mov %ax, <ncid_addr>` ou ax =
    l'index dans la table.
  - Donc: opcode reel = index d'apparition du `mov %ax, <ncid_addr>`
    dans l'ordre des adresses d'instructions du .text.

Sortie: docs/generated/ncid_map.json
"""
import subprocess, re, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVERS = ['LoginServer','GatewayServer','TicketServer',
           'WorldServer','MissionServer','ZoneServer']

def extract_ncids(binpath):
    nm = subprocess.check_output(['nm','-C',binpath]).decode('latin1')
    addr2name = {}
    for line in nm.splitlines():
        m = re.match(
            r'([0-9a-f]+)\s+[VvBbDd]\s+lapis::CNetCommand<lapis::'
            r'(CN[CE]_[a-zA-Z0-9_]+)>::NCID', line)
        if m: addr2name[int(m.group(1),16)] = m.group(2)

    dis = subprocess.check_output(['objdump','-d',binpath]).decode('latin1')
    assigns = []
    for m in re.finditer(
            r'([0-9a-f]+):\s+66 a3 '
            r'([0-9a-f]{2} [0-9a-f]{2} [0-9a-f]{2} [0-9a-f]{2})\s+mov',
            dis):
        ia = int(m.group(1),16)
        b = m.group(2).split()
        addr = int(b[3]+b[2]+b[1]+b[0], 16)
        if addr in addr2name:
            assigns.append((ia, addr))
    assigns.sort()
    return {addr2name[a]: i for i,(_,a) in enumerate(assigns)}

def main():
    all_ncids = {}
    for srv in SERVERS:
        path = os.path.join(ROOT, 'server_us', srv, srv)
        if not os.path.exists(path):
            print(f"skip {srv} (absent)")
            continue
        n = extract_ncids(path)
        all_ncids[srv] = n
        print(f"{srv}: {len(n)} NCIDs")

    out_dir = os.path.join(ROOT, 'docs', 'generated')
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, 'ncid_map.json')
    with open(out,'w') as f:
        json.dump(all_ncids, f, indent=1)
    print(f"sauvegarde {out}")

if __name__ == '__main__':
    main()
