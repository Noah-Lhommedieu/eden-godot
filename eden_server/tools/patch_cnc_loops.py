#!/usr/bin/env python3
"""Réécrit les classes CNC à liste variable pour sérialiser un vrai
`List<Entry>` au lieu de champs plats — d'après le désassemblage des
Serialize natifs (cnc_loop_layout.json + vérif manuelle).

Chaque entrée CONFIRMED : classe ->
    (count_type, [(type,name)...prefix], [(type,name)...entry])
"""
import re

# (count_type, champs préfixe scalaires, champs d'entrée répétés)
CONFIRMED = {
    # [byte+0x5, byte+0x4, ushort count] + entrées
    # = byte node + SCharacterClass(byte,byte,int,int,byte,ushort×7)
    'CNC_CZ_ClientPlayerClassList': ('ushort',
        [('byte', 'HeaderFlag1'), ('byte', 'HeaderFlag2')],
        [('byte', 'NodeFlag'), ('byte', 'ClassId'), ('byte', 'Unk1'),
         ('int', 'ClassLevel'), ('int', 'ClassExp'), ('byte', 'Flags'),
         ('ushort', 'U1'), ('ushort', 'U2'), ('ushort', 'U3'),
         ('ushort', 'U4'), ('ushort', 'U5'), ('ushort', 'U6'),
         ('ushort', 'U7')]),

    # LookEquip [int charId?, int?, int?] + tableau ushort = slots équipés
    'CNC_CZ_ClientLookEquip': ('ushort',
        [('int', 'CharId'), ('int', 'U1'), ('int', 'U2')],
        [('ushort', 'ItemUid')]),
    'CNC_CZ_ClientPVPRankLookEquip': ('ushort',
        [('int', 'CharId'), ('string', 'PlayerName'), ('short', 'U1')],
        [('ushort', 'ItemUid')]),
    'CNC_CZ_ClientQueryPlayerCard': ('ushort',
        [('int', 'CharId'), ('string', 'PlayerName'), ('short', 'U1')],
        [('ushort', 'ItemUid')]),

    # EnterNodeShortcut : bitmask+slots conditionnels — patché à la main
    # dans le .g.cs (structure trop complexe pour le template).

    # FamilyStorageLog : count + (action, détail)
    'CNC_CZ_ClientFamilyStorageLog': ('ushort', [],
        [('short', 'Action'), ('string', 'Detail')]),

    # SearchRoomResult : count + salles trouvées
    'CNC_CZ_ClientSearchRoomResult': ('short', [],
        [('int', 'RoomId'), ('string', 'RoomName'), ('short', 'Type'),
         ('short', 'Mode'), ('int', 'OwnerId'), ('int', 'MemberCount'),
         ('int', 'MaxMembers')]),

    # AchievementUpdate [sbyte type?, ushort count] + (id, compteur)
    'CNC_CZ_ClientAchievementUpdate': ('ushort',
        [('sbyte', 'Kind')],
        [('int', 'AchievementId'), ('uint', 'Counter')]),

    # UpdateCollection [short,int,ushort count] + cartes
    'CNC_CZ_ClientUpdateCollection': ('ushort',
        [('short', 'BookId'), ('int', 'U1')],
        [('short', 'CardId')]),

    # UpdateNPCSymbols [short,int,short] + symboles
    'CNC_CZ_ClientUpdateNPCSymbols': ('ushort',
        [('short', 'NpcId'), ('int', 'U1'), ('short', 'U2')],
        [('short', 'SymbolId')]),

    # FamilyBattleKilledRank : count + classement kills
    'CNC_CZ_ClientFamilyBattleKilledRank': ('short', [],
        [('string', 'PlayerName'), ('string', 'FamilyName'), ('short', 'Kills'),
         ('short', 'Rank')]),

    # UpdateFortuneBagCounter : count + compteurs
    'CNC_CZ_ClientUpdateFortuneBagCounter': ('ushort', [],
        [('short', 'BagId'), ('short', 'Count')]),

    # EnterNodeInventory [ubyte,short,sbyte] + ushort par slot
    'CNC_CZ_ClientEnterNodeInventory': ('ushort',
        [('byte', 'BagType'), ('short', 'U1'), ('sbyte', 'U2')],
        [('ushort', 'SlotValue')]),

    # MailList [ushort count] + enregistrement complet par mail
    # (vérifié : boucle SoA unique 0x8204660→0x8204b39).
    'CNC_CZ_ClientMailList': ('ushort', [],
        [('int', 'MailId'), ('string', 'SenderName'), ('int', 'SendTime'),
         ('string', 'Subject'),
         ('short', 'M1'), ('short', 'M2'), ('short', 'M3'),
         ('short', 'M4'), ('short', 'M5'), ('byte', 'Flag'),
         ('short', 'M6'), ('short', 'M7'), ('short', 'M8'),
         ('short', 'M9'), ('short', 'M10'), ('short', 'M11'),
         ('short', 'M12'),
         ('string', 'Content'),
         ('int', 'AttachItemId'), ('int', 'AttachQty'),
         ('short', 'A1'), ('short', 'A2'), ('short', 'A3'),
         ('short', 'A4'), ('short', 'A5'), ('short', 'A6'),
         ('short', 'A7'), ('short', 'A8'), ('short', 'A9'),
         ('short', 'A10'), ('short', 'A11'), ('short', 'A12'),
         ('short', 'A13'), ('short', 'A14'),
         ('long', 'ExpireTime'), ('ushort', 'U1'), ('uint', 'AttachGold'),
         ('ushort', 'U2'), ('ushort', 'U3'), ('ushort', 'U4'),
         ('ushort', 'U5'), ('ushort', 'U6'), ('ushort', 'U7'),
         ('short', 'B1'), ('short', 'B2'), ('ushort', 'U8'),
         ('short', 'C1'), ('short', 'C2'), ('short', 'C3'),
         ('ushort', 'U9'), ('ushort', 'U10'),
         ('short', 'D1'), ('short', 'D2'), ('byte', 'C0'),
         ('ushort', 'U11'), ('short', 'E1'), ('short', 'E2'),
         ('ushort', 'U12')]),

    # AuctionSellList/AuctionBuyList — vérifié :
    # SellList [u16 count] boucle 0x8201990→0x8201d55 ;
    # BuyList [short][u16 count] boucle 0x8201dc0→0x8202188.
    # Entrée = enregistrement objet + champs enchère.
    'CNC_CZ_ClientAuctionSellList': ('ushort', [], [
        ('int', 'AuctionId'), ('short', 'I1'), ('short', 'I2'),
        ('int', 'ItemId'), ('int', 'Price'),
        ('short', 'J1'), ('short', 'J2'), ('short', 'J3'),
        ('short', 'J4'), ('short', 'J5'), ('byte', 'Flag'),
        ('short', 'K1'), ('short', 'K2'), ('short', 'K3'),
        ('short', 'K4'), ('short', 'K5'), ('short', 'K6'),
        ('short', 'K7'), ('string', 'ItemName'),
        ('short', 'L1'), ('short', 'L2'), ('short', 'L3'),
        ('short', 'L4'), ('short', 'L5'), ('short', 'L6'),
        ('short', 'L7'), ('short', 'L8'), ('short', 'L9'),
        ('short', 'L10'), ('long', 'ExpireTime'),
        ('ushort', 'U1'), ('uint', 'SellerId'),
        ('ushort', 'U2'), ('ushort', 'U3'), ('ushort', 'U4'),
        ('ushort', 'U5'), ('ushort', 'U6'), ('ushort', 'U7'),
        ('short', 'V1'), ('short', 'V2'), ('short', 'V3'),
        ('short', 'V4'), ('short', 'V5'),
        ('ushort', 'W1'), ('ushort', 'W2')]),
    'CNC_CZ_ClientAuctionBuyList': ('ushort',
        [('short', 'Category')], [
        ('int', 'AuctionId'), ('short', 'I1'), ('short', 'I2'),
        ('int', 'ItemId'), ('int', 'Price'),
        ('short', 'J1'), ('short', 'J2'), ('short', 'J3'),
        ('short', 'J4'), ('short', 'J5'), ('byte', 'Flag'),
        ('short', 'K1'), ('short', 'K2'), ('short', 'K3'),
        ('short', 'K4'), ('short', 'K5'), ('short', 'K6'),
        ('short', 'K7'), ('string', 'ItemName'),
        ('short', 'L1'), ('short', 'L2'), ('short', 'L3'),
        ('short', 'L4'), ('short', 'L5'), ('short', 'L6'),
        ('short', 'L7'), ('short', 'L8'), ('short', 'L9'),
        ('short', 'L10'), ('long', 'ExpireTime'),
        ('ushort', 'U1'), ('uint', 'SellerId'),
        ('ushort', 'U2'), ('ushort', 'U3'), ('ushort', 'U4'),
        ('ushort', 'U5'), ('ushort', 'U6'), ('ushort', 'U7'),
        ('short', 'V1'), ('short', 'V2'), ('short', 'V3'),
        ('short', 'V4'), ('short', 'V5'),
        ('ushort', 'W1'), ('ushort', 'W2')]),

    # TeamAllMembersInfo — vérifié : [short count] boucle AoS
    # (stride 0x38) 0x8135bb8→0x8135cfc + [short] suffixe.
    'CNC_CZ_ClientTeamAllMembersInfo': ('short', [], [
        ('short', 'Slot'), ('short', 'Flags'), ('uint', 'CharId'),
        ('string', 'CharName'), ('long', 'Exp'), ('long', 'ExpMax'),
        ('int', 'Level'), ('int', 'Hp'),
        ('short', 'M1'), ('short', 'M2'), ('short', 'M3'),
        ('ushort', 'U1'), ('short', 'M4'), ('string', 'GuildName')],
        [('short', 'TeamMode')]),
}

CS_TYPES = {'byte': 'byte', 'sbyte': 'sbyte', 'ubyte': 'byte',
            'short': 'short', 'ushort': 'ushort', 'int': 'int',
            'uint': 'uint', 'long': 'long', 'ulong': 'ulong',
            'float': 'float', 'double': 'double', 'string': 'string'}
# InBinStream/OutBinStream n'ont pas de méthodes unsigned/sbyte :
# on émet des casts comme le fait le code généré existant.
RD = {'byte': 'ReadByte', 'sbyte': '(sbyte)ReadByte',
      'ubyte': 'ReadByte',
      'short': 'ReadShort', 'ushort': '(ushort)ReadShort',
      'int': 'ReadInt', 'uint': '(uint)ReadInt',
      'long': 'ReadLong', 'ulong': '(ulong)ReadLong',
      'float': 'ReadFloat', 'double': 'ReadDouble',
      'string': 'ReadString'}
WR = {'byte': 'WriteByte', 'sbyte': 'WriteByte',
      'ubyte': 'WriteByte',
      'short': 'WriteShort', 'ushort': 'WriteShort',
      'int': 'WriteInt', 'uint': 'WriteInt',
      'long': 'WriteLong', 'ulong': 'WriteLong',
      'float': 'WriteFloat', 'double': 'WriteDouble',
      'string': 'WriteString'}
# Cast à appliquer autour de la valeur avant l'écriture.
WCAST = {'sbyte': '(byte)', 'ushort': '(short)', 'uint': '(int)',
         'ulong': '(long)'}


def fields_cs(fields, indent='        '):
    out = []
    for t, n in fields:
        init = ' = "";' if t == 'string' else ''
        out.append(
            f'{indent}public {CS_TYPES[t]} {n} {{ get; set; }}{init}')
    return '\n'.join(out)


def patch_class(src, cls, count_type, prefix, entry_fields,
                suffix=()):
    pat = re.compile(
        r'(public sealed class %s : NetCommand\n.*?\n)(\})' % cls, re.S)
    m = pat.search(src)
    if not m:
        return src, False
    head = m.group(1)
    m2 = re.search(r'public override string Name => "[^"]*";\s*\n', head)
    if not m2:
        return src, False
    keep = head[:m2.end()]
    def wr(t, expr):
        c = WCAST.get(t, '')
        return f'{WR[t]}({c}{expr})'

    def rd(t):
        base = RD[t]
        if base.startswith('('):
            cast, method = base.split(')')
            return f'{cast})input.{method}()'
        return f'input.{base}()'

    # cast du count pour la méthode d'écriture réelle (int → param type)
    wparam = {'byte': 'byte', 'sbyte': 'byte', 'ubyte': 'byte',
              'short': 'short', 'ushort': 'short', 'int': 'int',
              'uint': 'int', 'long': 'long', 'ulong': 'long'}
    ct = wparam[count_type]
    pre_decl = fields_cs(prefix, '    ') + '\n\n' if prefix else ''
    pre_ser = ''.join(f'        output.{wr(t, n)};\n' for t, n in prefix)
    pre_des = ''.join(f'        {n} = {rd(t)};\n' for t, n in prefix)
    ser = ''.join(f'            output.{wr(t, "e." + n)};\n'
                  for t, n in entry_fields)
    deser = ''.join(f'            e.{n} = {rd(t)};\n'
                    for t, n in entry_fields)
    suf_decl = ('\n\n' + fields_cs(suffix, '    ')) if suffix else ''
    suf_ser = ''.join(f'        output.{wr(t, n)};\n' for t, n in suffix)
    suf_des = ''.join(f'        {n} = {rd(t)};\n' for t, n in suffix)
    body = f'''
{pre_decl}    /// <summary>Entrées répétées — boucle du Serialize natif.</summary>
    public System.Collections.Generic.List<Entry> Entries {{ get; }}
        = new();

    public sealed class Entry
    {{
{fields_cs(entry_fields)}
    }}{suf_decl}

    public override void Serialize(OutBinStream output)
    {{
{pre_ser}        output.{WR[count_type]}(({ct})Entries.Count);
        foreach (var e in Entries)
        {{
{ser}        }}
{suf_ser}    }}

    public override void Deserialize(InBinStream input)
    {{
{pre_des}        var count = {rd(count_type)};
        Entries.Clear();
        for (var i = 0; i < count; i++)
        {{
            var e = new Entry();
{deser}            Entries.Add(e);
        }}
{suf_des}    }}
'''
    return src[:m.start(1)] + keep + body + '}' + src[m.end(2):], True


def main():
    gen = ('server/src/Eden.Core/Networking/Messages/Generated/'
           'ZoneMessages.g.cs')
    src = open(gen).read()
    n = 0
    for cls, spec in CONFIRMED.items():
        ct, pre, fields = spec[0], spec[1], spec[2]
        suf = spec[3] if len(spec) > 3 else ()
        src, ok = patch_class(src, cls, ct, pre, fields, suf)
        if ok:
            n += 1
        else:
            print('  ! non trouvée:', cls)
    open(gen, 'w').write(src)
    print(gen, '->', n, 'classes patchées')


if __name__ == '__main__':
    main()
