#!/usr/bin/env python3
"""Génère docs/generated/NCIDS.md — table complète des NCIDs connus du
projet Eden (tous serveurs), avec le statut d'implémentation du client
Reborn et la source de preuve.

Sources :
  - $EDEN/docs/generated/ncid_map.json        — index interne par
    classe dans la table de commandes du binaire (section = processus).
    ⚠ pour les protocoles d'entrée (login/ticket/world), les commandes
    client sont enregistrées APRÈS les autres espaces de noms dans la
    table du binaire : l'index map ≠ NCID wire (ex. ClientHello monde :
    map 93, wire 34). La colonne NCID retient donc l'opcode du .g.cs
    quand la classe y existe (valeur wire vérifiée par captures), et
    marque l'écart.
  - $EDEN/.../Messages/Generated/*.g.cs       — opcode wire par classe.
  - $EDEN/docs/generated/FEATURE-MATRIX.md    — statut de preuve
    serveur (GOLDEN/DESASM/PASSTHRU/INCOMPLET/NON PROUVE).
  - godot_project/scripts/protocol/*.cs       — classes NetMessage du
    client = NCIDs implémentés côté client.

Statuts : implémenté | décodé (preuve serveur sans codec client) |
inconnu.

Usage : python3 tools/gen_ncids.py [--check]
"""
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EDEN = Path(os.environ.get(
    'EDEN_SERVER',
    ROOT.parent.parent / 'eden_server'))
NCID_MAP = EDEN / 'docs' / 'generated' / 'ncid_map.json'
GEN_DIR = (EDEN / 'server' / 'src' / 'Eden.Core' / 'Networking' /
           'Messages' / 'Generated')
MATRIX = EDEN / 'docs' / 'generated' / 'FEATURE-MATRIX.md'
PROTO_DIR = ROOT / 'godot_project' / 'scripts' / 'protocol'
OUT = ROOT / 'docs' / 'generated' / 'NCIDS.md'

# Préfixe de classe → (direction, canal).
# Deux générations de nommage lapis cohabitent :
#   - récente (zone/world) : CNE_C* = requête client→serveur,
#     CNC_C* = notification serveur→client ;
#   - ancienne (login/ticket/gateway) : CNC_(CL|T|G)_Server* = requête
#     client→serveur, CNC_(CL|T|G)_Client* = réponse serveur→client.
DIR_RULES = [
    (r'CNE_ZMis_', ('interne', 'zone→mission')),
    (r'CNC_ZMis_', ('interne', 'mission→zone')),
    (r'CNE_ZM_', ('interne', 'zone→mission')),
    (r'CNC_ZM_', ('interne', 'mission→zone')),
    (r'CNE_ZW_', ('interne', 'zone→world')),
    (r'CNC_ZW_', ('interne', 'world→zone')),
    (r'CNE_MW_', ('interne', 'mission→world')),
    (r'CNC_MW_', ('interne', 'world→mission')),
    (r'CNE_DBA_', ('interne', '*→dba')),
    (r'CNC_DBA_', ('interne', 'dba→*')),
    (r'CN[CE]_(?:CL|T|G)_Server', ('C→S', 'client→serveur')),
    (r'CN[CE]_(?:CL|T|G)_Client', ('S→C', 'serveur→client')),
    (r'CNE_CZ_', ('C→S', 'client→zone')),
    (r'CNC_CZ_', ('S→C', 'zone→client')),
    (r'CNE_CW_', ('C→S', 'client→world')),
    (r'CNC_CW_', ('S→C', 'world→client')),
    (r'CNE_CG_', ('C→S', 'client→gateway')),
    (r'CNC_CG_', ('S→C', 'gateway→client')),
]

# Espaces de noms « client-visibles » par section du map — les classes
# ticket/gateway présentes dans la section LoginServer appartiennent à
# un autre canal et ne doivent PAS être matchées aux codecs login du
# client (numérotations indépendantes par connexion).
CHANNEL_PREFIXES = {
    'LoginServer': ('CNC_CL_', 'CNE_CL_'),
    'TicketServer': ('CNC_T_', 'CNE_T_'),
    'GatewayServer': ('CNC_G_', 'CNE_G_', 'CNC_CG_', 'CNE_CG_'),
    'WorldServer': ('CNC_CW_', 'CNE_CW_'),
    'ZoneServer': ('CNC_CZ_', 'CNE_CZ_'),
}

# Fichier codec client → serveur dont il porte les NCIDs.
FILE_SERVER = {
    'LoginMessages.cs': 'LoginServer',
    'TicketMessages.cs': 'TicketServer',
    'WorldMessages.cs': 'WorldServer',
}
DEFAULT_SERVER = 'ZoneServer'   # Zone*.cs


def load_matrix():
    """FEATURE-MATRIX.md → {class_name: status}."""
    out = {}
    if not MATRIX.exists():
        return out
    row = re.compile(r'^\|\s*\d+\s*\|\s*`(\w+)`\s*\|\s*([A-ZÉ ]+?)\s*\|')
    for line in MATRIX.read_text(encoding='utf-8').splitlines():
        m = row.match(line)
        if m:
            out[m.group(1)] = m.group(2).strip()
    return out


def load_gen_opcodes():
    """*.g.cs → {class_name: opcode wire}."""
    out = {}
    for f in GEN_DIR.glob('*.g.cs'):
        t = f.read_text(encoding='utf-8')
        for m in re.finditer(
                r'public sealed class (\w+) : NetCommand.*?'
                r'public override int Opcode => (\d+)', t, re.S):
            out[m.group(1)] = int(m.group(2))
    return out


def load_client_protocol():
    """Classes NetMessage client → {(server, opcode, dir): (cls, file)}.

    Opcode lu sur `Opcode => N` dans le corps de la classe ; direction
    = dernier marqueur « C→S »/« S→C » du commentaire qui précède la
    déclaration (les <summary> des classes login/ticket ne répètent pas
    le numéro). ZoneEmptyRequest a des instances statiques
    `new(op, "Nom")`."""
    out = {}
    cls_re = re.compile(r'public\s+sealed\s+class\s+(\w+)')
    op_re = re.compile(r'public\s+override\s+int\s+Opcode\s*=>\s*(\d+)')
    dir_re = re.compile(r'([CS])\s*→\s*([CS])')
    for f in sorted(PROTO_DIR.glob('*.cs')):
        text = f.read_text(encoding='utf-8')
        server = FILE_SERVER.get(f.name, DEFAULT_SERVER)
        decls = list(cls_re.finditer(text))
        for i, d in enumerate(decls):
            end = decls[i + 1].start() if i + 1 < len(decls) else len(text)
            body = text[d.start():end]
            op = op_re.search(body)
            if not op:
                continue
            # Direction : PREMIER marqueur « C→S »/« S→C » du <summary>
            # propre à la classe (les références « réponse au C→S n »
            # en fin de commentaire ne doivent pas l'emporter).
            ctx = text[max(0, d.start() - 1200):d.start()]
            sstart = ctx.rfind('<summary>')
            span = ctx[sstart:] if sstart >= 0 else ctx[-200:]
            dirs = dir_re.findall(span)
            arrow = f'{dirs[0][0]}→{dirs[0][1]}' if dirs else '?'
            out.setdefault((server, int(op.group(1)), arrow),
                           (d.group(1), f.name))
        # Instances ZoneEmptyRequest : `new(57, "Zone.Echo")`.
        for m in re.finditer(r'new\((\d+),\s*"([^"]+)"\)', text):
            out.setdefault((server, int(m.group(1)), 'C→S'),
                           (m.group(2).split('.')[-1], f.name))
    return out


def direction_of(name):
    for pattern, d in DIR_RULES:
        if re.match(pattern, name):
            return d
    return ('?', 'inconnu')


def main():
    check = '--check' in sys.argv
    nmap = json.loads(NCID_MAP.read_text(encoding='utf-8'))
    gen = load_gen_opcodes()
    matrix = load_matrix()
    client = load_client_protocol()

    lines = [
        '# NCIDs Eden — table complète', '',
        '> Régénéré par `tools/gen_ncids.py` depuis '
        '`eden_server/docs/generated/ncid_map.json`, les `*.g.cs` '
        '(opcode wire), `FEATURE-MATRIX.md` et '
        '`godot_project/scripts/protocol/`. Ne pas éditer à la main.',
        '',
        '**NCID** = valeur wire quand la classe existe dans les `.g.cs` '
        'du serveur (vérifiée par captures) ; sinon l\'index de la table '
        'de commandes du binaire (marqué `†`). Les deux diffèrent pour '
        'les protocoles d\'entrée — la table du binaire enregistre les '
        'commandes client après les autres espaces de noms.',
        '',
        'Statuts : **implémenté** = codec client (`scripts/protocol/`) ; '
        '**décodé** = layout prouvé côté serveur sans codec client ; '
        '**inconnu** = aucune preuve.', '',
    ]

    impl_counts = {}
    for server, entries in nmap.items():
        lines.append(f'## {server} ({len(entries)} NCIDs)')
        lines.append('')
        lines.append('| NCID | Classe serveur | Direction | Statut | '
                     'Preuve |')
        lines.append('|---|---|---|---|---|')
        # Trie par NCID wire quand connu (ordre du protocole réel).
        def sort_key(kv):
            name, ncid = kv
            return (gen.get(name, ncid), name)
        for name, map_ncid in sorted(entries.items(), key=sort_key):
            wire = gen.get(name)
            ncid = wire if wire is not None else map_ncid
            note = f' †`{map_ncid}`' if wire is not None and wire != map_ncid \
                else (' †' if wire is None else '')
            arrow, chan = direction_of(name)
            # Codec client matché seulement dans le canal de la section
            # (les classes ticket/gateway de la section LoginServer ne
            # sont PAS couvertes par les codecs login du client).
            impl = (client.get((server, ncid, arrow))
                    if name.startswith(CHANNEL_PREFIXES.get(server, ()))
                    else None)
            mstatus = matrix.get(name)
            if impl:
                status = f'implémenté `{impl[0]}`'
                proof = f'`{impl[1]}`'
                impl_counts[server] = impl_counts.get(server, 0) + 1
            elif mstatus and mstatus != 'NON PROUVE':
                status = f'décodé ({mstatus.lower()})'
                proof = mstatus
            else:
                status = 'inconnu'
                proof = '—'
            lines.append(f'| {ncid}{note} | `{name}` | {arrow} {chan} | '
                         f'{status} | {proof} |')
        lines.append('')

    summary = 'Implémentés côté client : ' + ', '.join(
        f'**{n}** {srv.replace("Server", "").lower()}'
        for srv, n in sorted(impl_counts.items())) + \
        f' — {len(client)} classes NetMessage recensées.\n\n'
    body = '\n'.join(lines)
    body = body.replace('**inconnu** = aucune preuve.\n\n',
                        '**inconnu** = aucune preuve.\n\n' + summary, 1)

    if check:
        ok = OUT.exists() and OUT.read_text(encoding='utf-8') == body
        print('NCIDS.md', 'à jour' if ok else 'PÉRIMÉ — relancer gen_ncids.py')
        return 0 if ok else 1
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(body, encoding='utf-8')
    total = sum(len(m) for m in nmap.values())
    print(f'{OUT} écrit — {total} entrées, '
          f'{sum(impl_counts.values())} implémentés client '
          f'({impl_counts})')
    return 0


if __name__ == '__main__':
    sys.exit(main())
