#!/usr/bin/env python3
"""
Extraction des champs des commandes CNC_* (serveur→client).

Miroir de extract_cne_fields.py : pour les CNC_*, le payload est
construit dans `T::Serialize(COutBinStream&)` via
`lapis::COutBinStream::operator<<(type)`.

Sortie: docs/generated/cnc_fields.json
"""
import subprocess, re, json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVERS = ['LoginServer','GatewayServer','TicketServer',
           'WorldServer','MissionServer','ZoneServer']

TYPE_MAP = {
    'char': 'byte',
    'unsigned char': 'byte',
    'short': 'short',
    'unsigned short': 'ushort',
    'int': 'int',
    'unsigned int': 'uint',
    'long long': 'long',
    'unsigned long long': 'ulong',
    'float': 'float',
    'double': 'double',
    'std::string const&': 'std::string',
    'std::string&': 'std::string',
    'bool': 'bool',
}

def extract(binpath):
    nm = subprocess.check_output(['nm','-C',binpath]).decode('latin1')
    serializes = {}
    for line in nm.splitlines():
        m = re.search(
            r'([0-9a-fA-F]+)\s+[WwT]\s+lapis::(CN[CE]_[a-zA-Z0-9_]+)'
            r'::Serialize\(lapis::COutBinStream&\)', line)
        if m:
            serializes[m.group(2)] = int(m.group(1), 16)

    results = {}
    for name, addr in sorted(serializes.items()):
        try:
            dump = subprocess.check_output([
                'objdump','-d','-C',
                f'--start-address={hex(addr)}',
                f'--stop-address={hex(addr + 1200)}',
                binpath]).decode('latin1')
        except Exception:
            continue
        in_func = False
        lines = []
        for l in dump.splitlines():
            mlabel = re.match(r'^[0-9a-f]+ <(.+)>:', l)
            if mlabel:
                label = mlabel.group(1)
                if '+0x' in label:
                    continue
                if in_func:
                    break
                if '::Serialize' in label and name in label:
                    in_func = True
                continue
            if not in_func:
                continue
            lines.append(l)

        fields = []
        for l in lines:
            m = re.search(
                r'(call|jmp)\s+[0-9a-fA-F]+\s+'
                r'<lapis::COutBinStream::operator<<\((.*?)\)>', l)
            if m:
                fields.append(TYPE_MAP.get(m.group(2), m.group(2)))
        results[name] = fields
    return results

def main():
    all_fields = {}
    for srv in SERVERS:
        path = os.path.join(ROOT, 'server_us', srv, srv)
        if not os.path.exists(path):
            continue
        r = extract(path)
        all_fields[srv] = r
        nonempty = sum(1 for f in r.values() if f)
        print(f"{srv}: {len(r)} Serialize, {nonempty} avec champs")

    out = os.path.join(ROOT, 'docs', 'generated', 'cnc_fields.json')
    with open(out,'w') as f:
        json.dump(all_fields, f, indent=1)
    print(f"sauvegarde {out}")

if __name__ == '__main__':
    main()
