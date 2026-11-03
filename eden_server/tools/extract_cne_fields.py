#!/usr/bin/env python3
"""
Extraction des champs des commandes CNE_* (client→serveur).

Pour les CNE_*, les champs du payload sont dans
`T::Deserialize(CInBinStream&)` qui appelle
`lapis::CInBinStream::operator>>(type&)`.

Methode:
  1. nm: recuperer l'adresse de T::Deserialize pour chaque CNE_*
  2. objdump: desassembler ~600 octets et collecter les
     `call <CInBinStream::operator>>(TYPE&)>` dans l'ordre
  3. Sortie: docs/generated/cne_fields.json
"""
import subprocess, re, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVERS = ['LoginServer','GatewayServer','TicketServer',
           'WorldServer','MissionServer','ZoneServer']

TYPE_MAP = {
    'char&': 'byte',
    'unsigned char&': 'byte',
    'short&': 'short',
    'unsigned short&': 'ushort',
    'int&': 'int',
    'unsigned int&': 'uint',
    'long long&': 'long',
    'unsigned long long&': 'ulong',
    'float&': 'float',
    'double&': 'double',
}

def extract(binpath):
    nm = subprocess.check_output(['nm','-C',binpath]).decode('latin1')
    deserializes = {}
    for line in nm.splitlines():
        m = re.search(
            r'([0-9a-fA-F]+)\s+[WwT]\s+lapis::(CN[CE]_[a-zA-Z0-9_]+)'
            r'::Deserialize\(lapis::CInBinStream&\)', line)
        if m:
            deserializes[m.group(2)] = int(m.group(1), 16)

    results = {}
    for name, addr in sorted(deserializes.items()):
        try:
            dump = subprocess.check_output([
                'objdump','-d','-C',
                f'--start-address={hex(addr)}',
                f'--stop-address={hex(addr + 800)}',
                binpath]).decode('latin1')
        except Exception:
            continue
        fields = []
        # On s'arrete a la prochaine etiquette de fonction CNetCommand
        # qu'elle ait des champs ou non.
        in_func = False
        lines = []
        for l in dump.splitlines():
            mlabel = re.match(r'^[0-9a-f]+ <(.+)>:', l)
            if mlabel:
                label = mlabel.group(1)
                # labels internes ont un offset (+0x) — on continue
                if '+0x' in label:
                    continue
                if in_func:
                    break
                if '::Deserialize' in label and name in label:
                    in_func = True
                continue
            if not in_func:
                continue
            lines.append(l)

        # Detecte les boucles de lecture: `call operator>>(T&)` suivi de
        # `cmp $0xN, %e?x` + `jne <addr arriere>` => champ repete N fois.
        i = 0
        while i < len(lines):
            l = lines[i]
            m = re.search(
                r'(call|jmp)\s+[0-9a-fA-F]+\s+'
                r'<lapis::CInBinStream::operator>>\((.*?)\)>', l)
            if m:
                t = TYPE_MAP.get(m.group(2), m.group(2))
                # cherche une boucle dans les ~8 instructions suivantes:
                # `cmp $0xN, %eXX` puis un saut conditionnel arriere.
                repeat = 1
                for j in range(i+1, min(i+8, len(lines))):
                    mc = re.search(r'cmp\s+\$0x([0-9a-fA-F]+),%e[a-z]x', lines[j])
                    if not mc:
                        continue
                    for k in range(j+1, min(j+4, len(lines))):
                        if re.search(r'\tj(ne|b|l|ae|a|be|g|ge|le)\s', lines[k]):
                            repeat = int(mc.group(1), 16)
                            break
                    if repeat > 1:
                        break
                if repeat > 1:
                    fields.append(f'{t}[{repeat}]')
                else:
                    fields.append(t)
                i += 1
                continue
            # appel de Deserialize d'un sous-struct
            m2 = re.search(
                r'(call|jmp)\s+[0-9a-fA-F]+\s+'
                r'<lapis::([a-zA-Z0-9_]+)::Deserialize\(lapis::CInBinStream&\)>', l)
            if m2 and m2.group(2) != name:
                fields.append(f"struct_{m2.group(2)}")
            i += 1
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
        print(f"{srv}: {len(r)} Deserialize, {nonempty} avec champs")

    out_dir = os.path.join(ROOT, 'docs', 'generated')
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, 'cne_fields.json')
    with open(out,'w') as f:
        json.dump(all_fields, f, indent=1)
    print(f"sauvegarde {out}")

if __name__ == '__main__':
    main()
