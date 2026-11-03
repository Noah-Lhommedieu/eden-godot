#!/usr/bin/env python3
"""Genere docs/humain/NCID-ETAT.md — etat d'implementation de CHAQUE
NCID du protocole (Zone/World/Mission/Login/Gateway/Ticket).

Statuts par NCID entrant (CNE_*) :
  FAIT     — traite par le dispatch (switch ZoneEntryFlow ou handler
             PacketDispatcher enregistre).
  REPLAY   — servi par EntryReplayData (corps captures rejoues, valide
             a l'entree mais statique).
  SILENT   — ignore volontairement (le natif ne repond pas non plus).
  PARTIEL  — handler present mais golden incomplet (known_incomplete).
  ABSENT   — aucun traitement : le paquet tombe dans le vide.

Pour les sortants (CNC_*) : FAIT = au moins un site d'emission, sinon
ABSENT. GOLDEN = test byte-a-byte sur capture native existant.
"""
import json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'server/src'
GEN = SRC / 'Eden.Core/Networking/Messages/Generated'
TESTDATA = ROOT / 'server/tests/Eden.Tests/Data'


def collect():
    classes = {}          # (proto,op) -> name
    name2op = {}
    for f in GEN.glob('*.g.cs'):
        proto = f.name.replace('Messages.g.cs', '')
        for m in re.finditer(
                r'public sealed class (\w+) : NetCommand.*?'
                r'public override int Opcode => (\d+)',
                f.read_text(), re.S):
            classes[(proto, int(m.group(2)))] = m.group(1)
            name2op[m.group(1)] = (proto, int(m.group(2)))

    # dispatch zone : switch (opcode) dans ZoneEntryFlow.cs
    text = (SRC / 'Eden.Zone/ZoneEntryFlow.cs').read_text()
    i = text.index('switch (opcode)')
    j = text.index('// Requêtes d\'entrée')
    zone_switch = {int(m.group(1))
                   for m in re.finditer(r'case (\d+):', text[i:j])}
    rep = (SRC / 'Eden.Zone/EntryReplayData.cs').read_text()
    replay = {int(m.group(1)) for m in re.finditer(r'\[(\d+)\]\s*=', rep)}
    silents = {104, 156, 172, 186, 204}   # opcode is ... => true

    # handlers enregistres (Register(() => new X) / RegisterChar)
    regops = {}
    for f in SRC.rglob('*.cs'):
        for m in re.finditer(r'Register(?:Char)?\(\(\) => new (\w+)',
                             f.read_text()):
            if m.group(1) in name2op:
                regops.setdefault(name2op[m.group(1)], set()).add(
                    str(f.relative_to(SRC)).split('/')[-1])

    # dispatchs internes (World/Mission/Ticket/Login) : switch (opcode|ncid)
    internal = {}
    for svc in ('World', 'Mission', 'Ticket', 'Login', 'Gateway'):
        ops = set()
        for f in (SRC / f'Eden.{svc}').rglob('*.cs'):
            t = f.read_text()
            for sm in re.finditer(
                    r'switch\s*\(\s*(?:opcode|ncid)\s*\)', t):
                # fenetre genereuse apres le switch
                seg = t[sm.start():sm.start() + 40000]
                ops |= {int(m.group(1))
                        for m in re.finditer(r'case (\d+):', seg)}
        internal[svc] = ops

    # emissions
    emit = {}
    for f in SRC.rglob('*.cs'):
        t = f.read_text()
        for m in re.finditer(r'SendPacketAsync\(\s*(\d+)', t):
            emit.setdefault(int(m.group(1)), set()).add(
                str(f.relative_to(SRC)).split('/')[-1])
        for m in re.finditer(
                r'(?:SendAsync|Broadcast\w*Async|ReplyAsync|SendAllAsync)'
                r'\(new (\w+)', t):
            if m.group(1) in name2op:
                emit.setdefault(name2op[m.group(1)][1], set()).add(
                    str(f.relative_to(SRC)).split('/')[-1])

    gold = {int(f.stem.split('_')[1])
            for f in (TESTDATA / 'golden').glob('ncid_*.json')}
    ki = json.load(open(TESTDATA / 'known_incomplete.json'))
    return classes, zone_switch, replay, silents, regops, internal, \
        emit, gold, ki


DOMAINES = [
    ('Combat / sorts', ('Melee', 'Spell', 'Enchant', 'Revive', 'Damage',
                        'Attack', 'Combat', 'Hate', 'Stun')),
    ('Inventaire / equipement',
     ('Item', 'Equip', 'Bag', 'Divide', 'Arrange', 'Drop', 'PickUp',
      'Closet', 'FastDress', 'MagicBox', 'Identify', 'Repair',
      'Fortify', 'Socket', 'Fuse', 'Combine', 'Mosaic', 'Repair',
      'Enhance', 'Corrode', 'Archive')),
    ('Pet / elf', ('Elf', 'Pet')),
    ('Equipe / social', ('Team', 'Friend', 'Family', 'Channel', 'Tell',
                         'Chat', 'Marry', 'Wedding', 'Looking')),
    ('Quetes / missions', ('Mission', 'Quest', 'Trace')),
    ('Commerce / echanges', ('Vendor', 'Trade', 'Auction', 'Booth',
                             'Mail', 'BuyBack', 'Shop', 'ItemMall')),
    ('Instances / PvP / events',
     ('Battlefield', 'Colosseum', 'Territory', 'BattleRoyale', 'Duel',
      'NodeGroup', 'Recruit', 'Evaluate', 'Crusade', 'Ceremony',
      'Gladiator', 'Church')),
    ('Deplacement / monde', ('Move', 'ChangePosition', 'Turn', 'Goto',
                             'Transport', 'Portal', 'Migration', 'Echo',
                             'Node', 'Scene', 'Enter', 'Leave')),
    ('Perso / progression', ('Player', 'Class', 'License', 'Attribute',
                             'Attr', 'Level', 'Exp', 'Point', 'Work',
                             'Lottery', 'Skill')),
    ('Divers', ()),
]


def domain(name):
    for dom, kws in DOMAINES:
        if any(k in name for k in kws):
            return dom
    return 'Divers'


ANALYSE = """\
## Verdict — les gros systèmes sont-ils codés ?

### Combat : codé, mais incomplet

**Ce qui existe** (`ZoneEntryFlow.Combat*.cs`, ~4 100 lignes) :

- Pipeline mêlée natif complet : portée `CanAttack`, rolls hit/miss/
  crit (`NativeCombatFormulas`), swing + kfId par arme équipée, hurt
  du mob (495), mort → XP/XP-classe/loot/respawn, leash 50 u.
- Aggro mob : boucle de combat par mob (phases 302, attaques 271,
  retour au spawn, déaggro par distance à l'ancre).
- Sorts ciblés (9), de zone (16), autour (59), instant (17) :
  CastOK/Blast/Hit nativement ordonnés, enchant procs (85/127/128),
  résurrection (20), régénération HP/MP différenciée combat.
- PvP joueur↔joueur via le même pipeline.

**Ce qui manque ou coince (constats live)** :

- **Aggro passive absente** : `MonsterAiService.ShouldAggro` existe
  (profil `AggroRange` chargé) mais n'est **jamais appelé** — un mob
  n'agresse que s'il est attaqué. Les « noms rouges » ne sautent
  jamais sur le joueur, et l'écart de niveau n'est pas évalué.
- **Mode combat sur auto-attaque** : `st.InCombat` est posé côté
  serveur mais le flag 302 « engagé » joueur n'est émis que hors
  portée — à vérifier contre la capture native pour la bannière.
- **Cast en mouvement** : pas de règle timed-cast-vs-déplacement
  (le natif autorise l'incantation en marche pour certains sorts).
- **AoE partiel** : `HandleCastLocationAsync` applique des dégâts
  fixes (60, rayon 8 u) au lieu de la résolution de sort réelle.
- **Latence skill→dégât** : la séquence de cast est cadencée par des
  `ScheduleOnLoop(300 ms+)` ; une file trop granulaire peut rendre
  le hit perceptiblement tardif.

### Équipement / inventaire : codé, bugs connus

- `NativeItemCodec` (274/291/312/348/571…), `InventoryHandlers`,
  `EquipTrainHandlers`, `NativeElfCodec` existent et passent des
  goldens — mais l'équipement **pet-vs-armure** et le bitmap
  d'inventaire ont des défauts structurels remontés en live
  (objets équipés dans le mauvais slot, inventaire « plein » non
  détecté → drop au sol).

### Quêtes — régression compteur

Le loot appelle bien `SendQuestItemChangedAsync` (146 zone→mission)
mais les objectifs « collecter N items » restent à 0/N en live :
soit le lien mission ne réévalue pas, soit la réponse compteur
n'est pas émise/patchée. À tracer côté service Mission.

---
"""


def main():
    (classes, zone_switch, replay, silents, regops, internal, emit,
     gold, ki) = collect()

    lines = []
    zrows = [((p, o), n) for (p, o), n in classes.items()
             if p == 'Zone']
    zrows.sort(key=lambda x: x[0][1])

    def statut_in(op, name):
        s = []
        if op in zone_switch:
            s.append('FAIT(flow)')
        if ('Zone', op) in regops:
            s.append('FAIT(handler)')
        if op in replay:
            s.append('REPLAY')
        if op in silents:
            s.append('SILENT')
        if not s:
            s.append('ABSENT')
        if str(op) in ki:
            s.append('partiel')
        return ' / '.join(s)

    def statut_out(op):
        if op in emit:
            return 'EMIS'
        return 'ABSENT'

    in_rows = [((p, o), n) for (p, o), n in zrows if n.startswith('CNE_')]
    out_rows = [((p, o), n) for (p, o), n in zrows if n.startswith('CNC_')]

    lines.append('# Etat des NCIDs — protocole client <-> zone')
    lines.append('')
    lines.append('> Regenere par `tools/gen_ncid_etat.py`.')
    lines.append('')
    n_in_done = sum(1 for (_, o), n in in_rows
                    if 'FAIT' in statut_in(o, n)
                    or 'REPLAY' in statut_in(o, n))
    n_out_done = sum(1 for (_, o), _n in out_rows if o in emit)
    lines.append(f'- Entrants C>Z (CNE_CZ_) : **{len(in_rows)}** connus, '
                 f'**{n_in_done}** traites (flow/handler/replay).')
    lines.append(f'- Sortants S>C (CNC_CZ_) : **{len(out_rows)}** connus, '
                 f'**{n_out_done}** emis quelque part.')
    lines.append(f'- Golden byte-a-byte : **{len(gold)}** NCIDs.')
    lines.append('')
    lines.append('Legende : `FAIT(flow)` = case du dispatch principal ; '
                 '`FAIT(handler)` = handler PacketDispatcher ; '
                 '`REPLAY` = corps capture natif rejoue ; `SILENT` = '
                 'ignore comme le natif ; `ABSENT` = rien.')
    lines.append('')
    lines.append(ANALYSE)

    # table entrants par domaine
    lines.append('## Paquets entrants (C>Z)')
    lines.append('')
    for dom, _kws in DOMAINES:
        rows = [(o, n) for (_, o), n in in_rows if domain(n) == dom]
        if not rows:
            continue
        done = sum(1 for o, n in rows
                   if 'ABSENT' not in statut_in(o, n))
        lines.append(f'### {dom} — {done}/{len(rows)} traites')
        lines.append('')
        lines.append('| NCID | Classe | Statut | Golden |')
        lines.append('|---|---|---|---|')
        for o, n in rows:
            lines.append(f'| {o} | `{n}` | {statut_in(o, n)} |'
                         f' {"O" if o in gold else "—"} |')
        lines.append('')

    # sortants emis seulement (464 lignes sinon illisible : on liste
    # les emis + les absents groupes)
    lines.append('## Paquets sortants (S>C) — emis')
    lines.append('')
    lines.append('| NCID | Classe | Golden |')
    lines.append('|---|---|---|')
    for (_, o), n in out_rows:
        if o in emit:
            lines.append(f'| {o} | `{n}` | {"O" if o in gold else "—"} |')
    lines.append('')
    absent = [(o, n) for (_, o), n in out_rows if o not in emit]
    lines.append(f'## Sortants jamais emis ({len(absent)})')
    lines.append('')
    lines.append('| NCID | Classe |')
    lines.append('|---|---|')
    for o, n in absent:
        lines.append(f'| {o} | `{n}` |')
    lines.append('')

    # protocoles internes
    lines.append('## Protocoles internes (serveur<->serveur)')
    lines.append('')
    for proto in ('World', 'Mission', 'Login', 'Gateway', 'Ticket'):
        rows = [(o, n) for (p, o), n in classes.items() if p == proto]
        if not rows:
            continue
        lines.append(f'### {proto} — {len(rows)} classes')
        lines.append('')
        lines.append('| NCID | Classe |')
        lines.append('|---|---|')
        for o, n in sorted(rows):
            lines.append(f'| {o} | `{n}` |')
        lines.append('')

    out = ROOT / 'docs/humain/NCID-ETAT.md'
    out.write_text('\n'.join(lines), encoding='utf-8')
    print(f'{out} ecrit : {len(lines)} lignes, '
          f'{len(in_rows)} entrants / {len(out_rows)} sortants')


if __name__ == '__main__':
    main()
