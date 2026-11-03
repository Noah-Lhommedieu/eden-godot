#!/usr/bin/env python3
"""
Remplit les corps vides des classes CNE_* dans les .g.cs avec les
vrais champs extraits par extract_cne_fields.py
(docs/generated/cne_fields.json).
"""
import json, re, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GEN_DIR = os.path.join(ROOT,
    'server/src/Eden.Core/Networking/Messages/Generated')
CAT_MAP = {
    'LoginMessages.g.cs':'LoginServer',
    'GatewayMessages.g.cs':'GatewayServer',
    'TicketMessages.g.cs':'TicketServer',
    'WorldMessages.g.cs':'WorldServer',
    'MissionMessages.g.cs':'MissionServer',
    'ZoneMessages.g.cs':'ZoneServer',
}
CS_WRITE = {
    'byte':('WriteByte','ReadByte'), 'short':('WriteShort','ReadShort'),
    'ushort':('WriteShort','ReadShort'), 'int':('WriteInt','ReadInt'),
    'uint':('WriteInt','ReadInt'), 'long':('WriteLong','ReadLong'),
    'ulong':('WriteLong','ReadLong'), 'float':('WriteFloat','ReadFloat'),
    'double':('WriteDouble','ReadDouble'),
    # x86 32-bit: long C++ = 4 octets
    'unsigned long':('WriteInt','ReadInt'),
    'unsigned long&':('WriteInt','ReadInt'),
    'long&':('WriteInt','ReadInt'),
}
CS_PROP_TYPE = {'std::string&':'string', 'std::string':'string',
                'bool':'bool'}
# Noms C++ -> types C# pour les elements de tableaux
CS_ELEM_TYPE = {'unsigned long&':'uint', 'unsigned long':'uint',
                'long&':'int', 'long':'int',
                'unsigned char':'byte', 'unsigned char&':'byte'}

def cast_read(t, name):
    if t == 'string': return f"{name} = stream.ReadString();"
    if t == 'bool':   return f"{name} = stream.ReadBool();"
    if t == 'ushort': return f"{name} = (ushort)stream.ReadShort();"
    if t == 'uint':   return f"{name} = (uint)stream.ReadInt();"
    if t == 'ulong':  return f"{name} = (ulong)stream.ReadLong();"
    return f"{name} = stream.{CS_WRITE[t][1]}();"

def cast_write(t, name):
    if t == 'string': return f"stream.WriteString({name});"
    if t == 'bool':   return f"stream.WriteBool({name});"
    if t == 'ushort': return f"stream.WriteShort((short){name});"
    if t == 'uint':   return f"stream.WriteInt((int){name});"
    if t == 'ulong':  return f"stream.WriteLong((long){name});"
    return f"stream.{CS_WRITE[t][0]}({name});"

def gen_class(name, fields, opcode, server, names=None):
    props, ser, deser, docs = [], [], [], []
    if not fields:
        docs.append("")
        props.append("    // Aucun champ sérialisé")
    RESERVED = {'Name', 'Opcode', 'Value'}  # membres de NetCommand
    idx = 0  # index dans la liste de noms semantiques
    for i, f in enumerate(fields):
        sem = names[idx] if names and idx < len(names) else None
        if sem in RESERVED:
            sem = f"{sem}Field"
        idx += 1
        marr = re.match(r'^([\w &]+?)\[(\d+)\]$', f)
        if marr and (marr.group(1).strip() in CS_WRITE
                     or marr.group(1).strip() in CS_ELEM_TYPE):
            # Champ en boucle: type[N] -> tableau de N elements
            bt_raw, n = marr.group(1).strip(), int(marr.group(2))
            bt = CS_ELEM_TYPE.get(bt_raw, bt_raw)
            fn = sem or f"Field{i+1}"
            props.append(f"    public {bt}[] {fn} {{ get; set; }} = new {bt}[{n}];")
            if bt == 'byte':
                ser.append(f"        stream.WriteBytes({fn}, {n});")
                deser.append(f"        {fn} = stream.ReadBytes({n});")
            else:
                ser.append(f"        for (var _i = 0; _i < {n}; _i++) "
                           f"{cast_write(bt, fn + '[_i]')}")
                deser.append(f"        for (var _i = 0; _i < {n}; _i++) "
                             f"{cast_read(bt, fn + '[_i]')}")
            docs.append(f)
        elif f in CS_WRITE or f in CS_PROP_TYPE:
            resolved = CS_PROP_TYPE.get(f, CS_ELEM_TYPE.get(f, f))
            count = len([x for x in fields
                         if x in CS_WRITE or x in CS_PROP_TYPE
                         or re.match(r'^[\w &]+?\[\d+\]$', x)])
            fn = sem or (f"Field{i+1}" if count > 1 else "Value")
            props.append(f"    public {resolved} {fn} {{ get; set; }}"
                         + (" = \"\";" if resolved == "string" else ""))
            ser.append(f"        {cast_write(resolved, fn)}")
            deser.append(f"        {cast_read(resolved, fn)}")
            docs.append(f)
        elif f.startswith('struct_'):
            st = f[7:]
            fn = f"{st}{i+1}"
            props.append(f"    public {st} {fn} {{ get; set; }} = new();")
            ser.append(f"        {fn}.Serialize(stream);")
            deser.append(f"        {fn}.Deserialize(stream);")
            docs.append(f)
        else:
            props.append(f"    // Type non résolu: {f}")
    return f"""/// <summary>
/// Reversé depuis lapis::{name} ({server}).
/// Champs C++ originaux : {', '.join(docs)}
/// </summary>
public sealed class {name} : NetCommand
{{
    public override int Opcode => {opcode};
    public override string Name => "{name}";

{chr(10).join(props)}

    public override void Serialize(OutBinStream stream)
    {{
{chr(10).join(ser) if ser else '        // Aucun champ'}
    }}

    public override void Deserialize(InBinStream stream)
    {{
{chr(10).join(deser) if deser else '        // Aucun champ'}
    }}
}}
"""

def collect_known_types():
    """Classes NetCommand deja generees (tous fichiers .g.cs)."""
    known = set()
    for fn in os.listdir(GEN_DIR):
        if fn.endswith('.g.cs'):
            src = open(os.path.join(GEN_DIR, fn)).read()
            known.update(re.findall(r'public sealed class (\w+) : NetCommand', src))
    return known

def main():
    cne_data = json.load(open(os.path.join(ROOT,'docs/generated/cne_fields.json')))
    cnc_data = json.load(open(os.path.join(ROOT,'docs/generated/cnc_fields.json')))
    ncid_map = json.load(open(os.path.join(ROOT,'docs/generated/ncid_map.json')))
    names_path = os.path.join(ROOT,'docs/generated/field_names.json')
    sem_names = json.load(open(names_path)) if os.path.exists(names_path) else {}
    known_types = collect_known_types()

    for fn, srv in CAT_MAP.items():
        path = os.path.join(GEN_DIR, fn)
        if not os.path.exists(path): continue
        src = open(path).read()
        cne = cne_data.get(srv, {})
        cnc = cnc_data.get(srv, {})
        ncids = ncid_map.get(srv, {})
        replaced = 0
        for name in set(list(cne) + list(cnc)):
            # CNE_ = client->serveur : champs dans Deserialize (cne_fields)
            # CNC_ = serveur->client : champs dans Serialize (cnc_fields)
            fl = cne.get(name) if name.startswith('CNE_') else cnc.get(name)
            if fl is None:
                fl = cne.get(name, cnc.get(name, []))
            # Classe maintenue a la main (ex: MoveSelf) — ne pas regénerer
            if name in sem_names and sem_names[name] is None:
                continue
            # Filtre les sous-structs non generes
            fl = [f for f in fl
                  if not f.startswith('struct_') or f[7:] in known_types]
            # Remplace le corps de la classe si elle existe
            pat = re.compile(
                r'/// <summary>\n/// Reversé depuis lapis::' + re.escape(name) +
                r'.*?\n/// </summary>\npublic sealed class ' + re.escape(name) +
                r' : NetCommand\n\{.*?\n\}\n', re.S)
            if pat.search(src):
                src = pat.sub(gen_class(name, fl,
                    ncids.get(name,0), srv,
                    sem_names.get(name)), src)
                replaced += 1
        open(path,'w').write(src)
        print(f"{fn}: {replaced} classes remplies")

if __name__ == '__main__':
    main()
