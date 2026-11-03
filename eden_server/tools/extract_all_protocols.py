#!/usr/bin/env python3
"""
Extracteur automatique de protocole Eden pour portage C# (.NET 8).
Scanne les binaires ELF 32-bit (non-strippés avec symboles) pour extraire
la liste complète des classes CNC_*, leurs méthodes Serialize() et l'ordre des champs.
Génère le code source C# correspondant dans Eden.Core.Networking.Messages.
"""

import os
import re
import sys
import json
import subprocess
from pathlib import Path

# Mapping des types C++ vers les types et méthodes C# de OutBinStream / InBinStream
TYPE_MAP = {
    'char': ('byte', 'WriteByte', 'ReadByte'),
    'unsigned char': ('byte', 'WriteByte', 'ReadByte'),
    'short': ('short', 'WriteShort', 'ReadShort'),
    'unsigned short': ('ushort', 'WriteShort', 'ReadShort'),
    'int': ('int', 'WriteInt', 'ReadInt'),
    'unsigned int': ('uint', 'WriteInt', 'ReadInt'),
    'long long': ('long', 'WriteLong', 'ReadLong'),
    'unsigned long long': ('ulong', 'WriteLong', 'ReadLong'),
    'float': ('float', 'WriteFloat', 'ReadFloat'),
    'double': ('double', 'WriteDouble', 'ReadDouble'),
    'std::string&': ('string', 'WriteString', 'ReadString'),
    'std::string': ('string', 'WriteString', 'ReadString'),
}

BINARIES = [
    ('LoginServer', 'server_us/LoginServer/LoginServer', 'Login'),
    ('GatewayServer', 'server_us/GatewayServer/GatewayServer', 'Gateway'),
    ('TicketServer', 'server_us/TicketServer/TicketServer', 'Ticket'),
    ('WorldServer', 'server_us/WorldServer/WorldServer', 'World'),
    ('MissionServer', 'server_us/MissionServer/MissionServer', 'Mission'),
    ('ZoneServer', 'server_us/ZoneServer/ZoneServer', 'Zone'),
]

def analyze_binary(name, bin_path, category):
    if not os.path.exists(bin_path):
        print(f"Skipping {bin_path} (not found)")
        return {}

    print(f"[*] Analyse de {name} ({bin_path})...")
    
    # 1. Extraction des symboles avec nm
    nm_cmd = ['nm', '-C', bin_path]
    try:
        nm_out = subprocess.check_output(nm_cmd).decode('latin1')
    except Exception as e:
        print(f"Erreur nm sur {bin_path}: {e}")
        return {}

    # Extraction des symboles Serialize
    serializes = {}
    ncids = {}
    for line in nm_out.splitlines():
        # Match lapis::CNC_* ou CNE_*::Serialize
        m_ser = re.search(r'([0-9a-fA-F]+)\s+[WwT]\s+lapis::(CN[CE]_[a-zA-Z0-9_]+)::Serialize\(lapis::COutBinStream&\)', line)
        if m_ser:
            addr = int(m_ser.group(1), 16)
            cmd_name = m_ser.group(2)
            serializes[cmd_name] = addr

        # Match NCID variable
        m_ncid = re.search(r'([0-9a-fA-F]+)\s+[VvB]\s+lapis::CNetCommand<lapis::(CNC_[a-zA-Z0-9_]+)>::NCID', line)
        if m_ncid:
            ncids[m_ncid.group(2)] = int(m_ncid.group(1), 16)

    print(f"    -> {len(serializes)} classes CNC_* trouvées")

    # 2. Désassemblage par lot pour extraire les types de champs
    results = {}
    for cmd_name, addr in sorted(serializes.items()):
        obj_cmd = [
            'objdump', '-d', '-C',
            f'--start-address={hex(addr)}',
            f'--stop-address={hex(addr + 450)}',
            bin_path
        ]
        try:
            dump = subprocess.check_output(obj_cmd).decode('latin1')
        except Exception:
            continue

        fields = []
        lines = dump.splitlines()
        for l in lines[3:]:
            if '<' in l and '>:' in l and cmd_name not in l:
                break
            m_call = re.search(r'(call|jmp)\s+([0-9a-fA-F]+)\s+<lapis::COutBinStream::operator<<\((.*)\)>', l)
            if m_call:
                cpp_type = m_call.group(3)
                fields.append(cpp_type)
            m_sub = re.search(r'(call|jmp)\s+([0-9a-fA-F]+)\s+<lapis::([a-zA-Z0-9_]+)::Serialize\(lapis::COutBinStream&\)>', l)
            if m_sub and m_sub.group(3) != cmd_name:
                fields.append(f"struct_{m_sub.group(3)}")

        # Filtrer le dernier 'unsigned short' qui est souvent le trailer NCID de CNetCommand::_Serialize
        clean_fields = []
        for i, f in enumerate(fields):
            # Si le dernier champ est unsigned short, on le garde ou on le traite comme NCID
            clean_fields.append(f)

        results[cmd_name] = {
            'name': cmd_name,
            'address': hex(addr),
            'server': name,
            'category': category,
            'raw_fields': clean_fields
        }

    return results

def generate_csharp_class(cmd_data, opcode):
    name = cmd_data['name']
    fields = cmd_data['raw_fields']

    props = []
    ser_lines = []
    deser_lines = []

    # Exclure le dernier unsigned short s'il s'agit du header/footer NCID automatique
    if fields and fields[-1] in ('unsigned short', 'short'):
        effective_fields = fields[:-1]
    else:
        effective_fields = fields

    for idx, f in enumerate(effective_fields):
        field_name = f"Field{idx + 1}"
        if f in TYPE_MAP:
            cs_type, writer, reader = TYPE_MAP[f]
            # Noms plus parlants si 1 seul champ
            if len(effective_fields) == 1:
                field_name = "Value"
            props.append(f"    public {cs_type} {field_name} {{ get; set; }}")
            if cs_type == 'ushort':
                ser_lines.append(f"        stream.{writer}((short){field_name});")
                deser_lines.append(f"        {field_name} = (ushort)stream.{reader}();")
            elif cs_type == 'uint':
                ser_lines.append(f"        stream.{writer}((int){field_name});")
                deser_lines.append(f"        {field_name} = (uint)stream.{reader}();")
            elif cs_type == 'ulong':
                ser_lines.append(f"        stream.{writer}((long){field_name});")
                deser_lines.append(f"        {field_name} = (ulong)stream.{reader}();")
            else:
                ser_lines.append(f"        stream.{writer}({field_name});")
                deser_lines.append(f"        {field_name} = stream.{reader}();")
        else:
            props.append(f"    // Type non résolu : {f}")
            ser_lines.append(f"        // TODO: stream write for {f}")
            deser_lines.append(f"        // TODO: stream read for {f}")

    prop_block = "\n".join(props) if props else "    // Aucun champ sérialisé"
    ser_block = "\n".join(ser_lines) if ser_lines else "        // Aucun champ"
    deser_block = "\n".join(deser_lines) if deser_lines else "        // Aucun champ"

    return f"""/// <summary>
/// Reversé depuis lapis::{name} ({cmd_data['server']}).
/// Champs C++ originaux : {', '.join(effective_fields)}
/// </summary>
public sealed class {name} : NetCommand
{{
    public override int Opcode => {opcode};
    public override string Name => "{name}";

{prop_block}

    public override void Serialize(OutBinStream stream)
    {{
{ser_block}
    }}

    public override void Deserialize(InBinStream stream)
    {{
{deser_block}
    }}
}}
"""

def main():
    root_dir = Path(__file__).resolve().parent.parent
    os.chdir(root_dir)

    all_commands = {}
    for name, bin_path, cat in BINARIES:
        cmds = analyze_binary(name, bin_path, cat)
        all_commands.update(cmds)

    print(f"\n[+] Total cumulé : {len(all_commands)} commandes uniques reversées !")

    # Sauvegarde JSON de la spec
    spec_path = "docs/protocole/protocol_spec.json"
    os.makedirs(os.path.dirname(spec_path), exist_ok=True)
    with open(spec_path, "w", encoding="utf-8") as f:
        json.dump(all_commands, f, indent=2)
    print(f"[+] Spec JSON sauvegardée dans {spec_path}")

    # Charge les vrais NCIDs extraits du binaire (docs/generated/ncid_map.json)
    # Genere par tools/extract_ncids.py: opcode reel = index dans
    # InitPCCommandTable (mov %ax, <ncid_addr>).
    ncid_map = {}
    ncid_path = "docs/generated/ncid_map.json"
    if os.path.exists(ncid_path):
        raw = json.load(open(ncid_path))
        # Fusionne tous les serveurs (une classe n'est definie que sur un)
        for srv, mapping in raw.items():
            for cls, ncid in mapping.items():
                ncid_map[cls] = ncid
        print(f"[+] {len(ncid_map)} vrais NCIDs charges depuis {ncid_path}")
    else:
        print("[!] ncid_map.json absent — opcodes sequentiels generes")

    # Génération du code C# par catégorie
    by_category = {}
    for k, v in all_commands.items():
        cat = v['category']
        by_category.setdefault(cat, []).append(v)

    out_dir = Path("server/src/Eden.Core/Networking/Messages/Generated")
    out_dir.mkdir(parents=True, exist_ok=True)

    for cat, items in by_category.items():
        file_path = out_dir / f"{cat}Messages.g.cs"
        print(f"[*] Génération de {file_path} ({len(items)} classes)...")
        code_classes = []
        for idx, cmd in enumerate(items):
            # Vrai NCID si connu, sinon index de secours
            code_classes.append(generate_csharp_class(
                cmd, ncid_map.get(cmd['name'], idx)))

        content = f"""// <auto-generated>
// Fichier généré automatiquement par tools/extract_all_protocols.py.
// Ne pas éditer manuellement. Reversé depuis server_us ({cat}Server).
// </auto-generated>

namespace Eden.Core.Networking.Messages.Generated;

{chr(10).join(code_classes)}
"""
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(content)

    print("\n[+] Extraction et génération C# terminées avec succès !")

if __name__ == '__main__':
    main()
