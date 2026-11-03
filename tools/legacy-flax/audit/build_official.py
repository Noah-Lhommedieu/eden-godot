#!/usr/bin/env python3
"""Assemble corpus/official/ = EDENFR (base) + compléments US par id.

Entrées :
  FR = corpus/fr-full (pkg complet) — fallback EDENFR/extracted pour data/db
  US = eden_server/client
  RF = eden_server/reforged/EdenReforged (uidata + fichiers ni-FR-ni-US si listés)
Sortie : corpus/official/ + build_report.json

Règles :
  - c_*/C_* tables : lignes FR conservées ; lignes US ajoutées pour ids absents FR.
  - SCHEMA_US : tables remplacées par la version US entière (dérive de schéma).
  - MISSING_US : fichiers absents FR copiés depuis US.
  - t_* : base = FR officiel transcodé CP1252→UTF-8 ; lang/en/ = US T_* transcodé cp950→UTF-8.
  - Actifs liés (modèles n*/m*, icônes i*) des ids complémentés : copiés depuis US.
"""
import os, re, sys, json, shutil, glob

ROOT = '/home/ster2lune/Bureau/reverseden'
FR_FULL = ROOT + '/portage/corpus/fr-full'
FR_EXTR = '/home/ster2lune/Téléchargements/EDENFR/extracted'
US = ROOT + '/eden_server/client'
RF = ROOT + '/eden_server/reforged/EdenReforged'
OUT = ROOT + '/portage/corpus/official'

FR = FR_FULL if os.path.isdir(FR_FULL + '/data/db') else FR_EXTR

SCHEMA_US = {'c_spell.ini', 'c_enchant.ini', 'c_achievement.ini',
             'c_itemmallenchant.ini'}
# Tables à schéma US mais avec lignes FR-only à préserver : base = lignes
# US (col8 groupe présent), complétées des ids absents US paddés à la
# largeur US (col8=0 quand le format FR 9-col n'a pas de groupe).
US_BASE_MERGE = {'c_collection.ini'}
# Cartes c_collection fantômes à écarter : pointent des ids d'enchant
# (E1023), sans def d'item dans AUCUN corpus (même pas EDENFR) —
# jamais obtenables, affichaient le nom Big5 brut.
_COLLECTION_DROP = {44385, 44386}
MISSING_US = ['C_DailyAward.ini', 'c_collectionpoint.ini', 'T_CharColor.ini',
              't_level.ini']
# tables t_* : le manifeste officiel (34) — on transcode tout t_*.ini FR présent
SCENE_COMPLEMENTS = ['S057', 'S188', 'S192', 'S744', 'S745']

def rd(p):
    try: return open(p, 'rb').read()
    except OSError: return b''

def find_db(root, name):
    for base in (root + '/data/db/', root + '/data/Db/', root + '/data/DB/'):
        if os.path.exists(base + name): return base + name
    try:
        for f in os.listdir(root + '/data/db'):
            if f.lower() == name.lower(): return root + '/data/db/' + f
    except OSError: pass
    return None

def pipe_rows(data):
    """(header_lines, {id: raw_line}) — conserve les lignes brutes."""
    head, rows = [], {}
    for l in data.split(b'\n'):
        m = re.match(rb'^(\d+)\|', l)
        if m: rows[int(m.group(1))] = l.rstrip(b'\r\n')
        elif not rows: head.append(l.rstrip(b'\r\n'))
    return head, rows

def xcode(raw, enc):
    """par ligne : enc strict, sinon latin-1 (préserve chaque octet)."""
    out = []
    for l in raw.split(b'\n'):
        try: out.append(l.decode(enc))
        except UnicodeDecodeError: out.append(l.decode('latin-1'))
    return '\n'.join(out).encode('utf8')

def _cjk(s):
    return sum(1 for c in s if '一' <= c <= '鿿')

_FR_ACC = set('àâäçéèêëîïôöùûüÿœæÀÂÄÇÉÈÊËÎÏÔÖÙÛÜŸŒÆ')

def _fr_line(l):
    # ~1035 lignes CN non traduites vivent dans les t_* FR : cp950 gagne
    # seulement si la lecture cp1252 ressemble à du mojibake (symboles rares).
    if not any(b > 127 for b in l):
        return l.decode('ascii', 'ignore')
    try: s1252 = l.decode('cp1252')
    except UnicodeDecodeError: s1252 = l.decode('latin-1')
    try:
        s950 = l.decode('cp950'); cjk = _cjk(s950)
    except UnicodeDecodeError:
        s950 = ''; cjk = 0
    score_fr = sum(1 for c in s1252 if c in _FR_ACC)
    weird = sum(1 for c in s1252 if '\x80' <= c <= '\xff' and c not in _FR_ACC)
    if cjk >= 3 and weird > score_fr:
        return s950
    return s1252

def cp1252_to_utf8(raw):
    return '\n'.join(_fr_line(l) for l in raw.split(b'\n')).encode('utf8')

def cp950_to_utf8(raw): return xcode(raw, 'cp950')  # US = Big5 (× = 0xA1D1…)

report = {'fr_base': FR, 'merged': {}, 'copied_files': [], 'scenes': {},
          'models_copied': [], 'models_missing': [], 'errors': []}

os.makedirs(OUT + '/data/db', exist_ok=True)
os.makedirs(OUT + '/data/scene', exist_ok=True)

# ---------- 1. toute la base db FR ----------
fr_db_files = {}
for f in os.listdir(FR + '/data/db'):
    p = FR + '/data/db/' + f
    if os.path.isfile(p): fr_db_files[f.lower()] = f

us_db_files = {}
try:
    for f in os.listdir(US + '/data/db'):
        p = US + '/data/db/' + f
        if os.path.isfile(p): us_db_files[f.lower()] = f
except OSError: pass

merged, schema_swapped, fr_only = 0, 0, 0
for low, fname in sorted(fr_db_files.items()):
    src = FR + '/data/db/' + fname
    dst = OUT + '/data/db/' + fname
    if low in SCHEMA_US:
        us_p = find_db(US, fname)
        if us_p:
            shutil.copyfile(us_p, dst); schema_swapped += 1
            report['merged'][fname] = 'US-version-entière (schéma)'
            continue
    if low in US_BASE_MERGE:
        us_p = find_db(US, fname)
        fr_head, fr_rows = pipe_rows(rd(src))
        us_head, us_rows = pipe_rows(rd(us_p)) if us_p else ([], {})
        add = sorted(set(fr_rows) - set(us_rows))
        width = max((len(r.split(b'|')) - 1) for r in us_rows.values())
        body = [us_rows[i] for i in sorted(us_rows)]
        for i in add:
            parts = fr_rows[i].split(b'|')
            parts += [b'0'] * (width + 1 - len(parts))
            body.append(b'|'.join(parts))
        # Cartes fantômes : pointent des ids d'enchant (E1023), sans
        # def d'item nulle part (même pas EDENFR) — jamais obtenables.
        body = [r for r in body
                if int(r.split(b'|')[0]) not in _COLLECTION_DROP]
        open(dst, 'wb').write(b'\n'.join(us_head + body) + b'\n')
        merged += 1
        report['merged'][fname] = f'base US +{len(add)} lignes FR-only'
        continue
    if low.startswith('t_'):
        # texte FR : transcode CP1252 → UTF-8
        open(dst, 'wb').write(cp1252_to_utf8(rd(src)))
        fr_only += 1; continue
    # c_*/autres : fusion par id (lignes US pour ids absents FR)
    us_p = find_db(US, fname)
    if not us_p:
        shutil.copyfile(src, dst); fr_only += 1; continue
    fr_head, fr_rows = pipe_rows(rd(src))
    us_head, us_rows = pipe_rows(rd(us_p))
    if not fr_head and not fr_rows:
        shutil.copyfile(src, dst); fr_only += 1; continue
    add = sorted(set(us_rows) - set(fr_rows))
    head = us_head if len(us_head) >= len(fr_head) else fr_head
    body = [fr_rows[i] for i in sorted(fr_rows)] + [us_rows[i] for i in add]
    open(dst, 'wb').write(b'\n'.join(head + body) + b'\n')
    if add:
        merged += 1
        report['merged'][fname] = f'+{len(add)} lignes US'
    else:
        fr_only += 1

# ---------- 2. fichiers absents FR ----------
for fname in MISSING_US:
    p = find_db(US, fname)
    if p:
        shutil.copyfile(p, OUT + '/data/db/' + fname)
        report['copied_files'].append(fname)
# EquipType → C_EquipType (mismatch nom)
p = find_db(FR, 'EquipType.ini') or find_db(US, 'EquipType.ini')
if p: shutil.copyfile(p, OUT + '/data/db/C_EquipType.ini'); report['copied_files'].append('C_EquipType.ini')

# ---------- 3. lang/en = US T_* UTF-8 ----------
os.makedirs(OUT + '/data/db/lang/en', exist_ok=True)
ten = 0
for low, fname in us_db_files.items():
    if low.startswith('t_'):
        open(OUT + '/data/db/lang/en/' + low, 'wb').write(cp950_to_utf8(rd(US + '/data/db/' + fname)))
        ten += 1
report['lang_en_files'] = ten

# ---------- 3b. lignes ajoutées par le portage ----------
# Contenu absent des sources FR **et** US mais requis en jeu :
# montures EDENFR sans def serveur US — « Kitsune de l'Aube » (1290,
# enchant 11354 → r253 renard doré) et « Renard Blanc du blizzard »
# (48813, enchant 60419 → r268, l'exclu Aeria 美法特製金品 ; l'id US
# n'est qu'un placeholder « 測試用編號 »).
def _cells(d, width=None):  # {col: valeur} → ligne pipe
    row = [b''] * (width or max(d) + 1)
    for k, v in d.items(): row[k] = v
    return b'|'.join(row)

_kitsune_enchant = _cells({0: b'11354', 1: b'E0247',
    5: b"Kitsune de l'Aube", 7: b'1', 8: b'0',
    9: b'2134', 10: b'253020', 12: b'1', 13: b'2',  # Ride r253 renard du
        # soleil doré (clé native S_Enchant — r532 = créature volante,
        # PAS le renard ; le blanc est r268/48813), seats 1, type combat
    16: b'2051', 17: b'90',                    # VIT DEP +90 %
    23: b'2083', 24: b'2', 25: b'1',           # ATQ+2 % / DEF+1 %
    30: b'2002', 31: b'7',                     # PV +7 %
    47: b'0',
    56: b"VIT DEP +90%, PV +7%, ATQ +2%, DEF +1% sur monture"}, 62)
_kitsune_en = (b"1290|Dawn Kitsune (Legendary)|$7$The mythical sun "
               b"messenger can burn the impure.")
# Enchant du renard blanc — ligne FR c_itemmallenchant copiée à
# l'identique sauf nom/desc traduits (61 cols comme la source).
_whitefox_enchant = _cells({0: b'60419', 1: b'I01681',
    5: b'Renard Blanc du blizzard aux neuf queues',
    6: b'1', 7: b'0', 8: b'2134', 9: b'268010', 11: b'1', 12: b'2',
    15: b'2051', 16: b'90', 22: b'2002', 23: b'7',
    29: b'2083', 30: b'2', 32: b'2', 47: b'0',
    55: b'Monte : vitesse +90%, HP +7%, ATQ et MAG +2%.'}, 61)

# Cartes archive des montures EDENFR sans entrée c_collection
# (cat 29, groupe 5, noms t_item FR / c_item Big5 — Placeholders exclus).
_MOUNT_CARDS = [
    b"1290|Kitsune de l'Aube|0|29|150|Monture|0|5|5|",
    b'1365|\xa4C\xa4\xe9\xb6\xc7\xbb\xa1\xa4\xd1\xa4\xf5\xbb\xf1\xb0\xc4\xbe\xd4\xb0\xab\xaey\xc3M|0|29|151|Monture|0|5|5|',
    b'20579|Cannon Tower|0|29|152|Monture|0|5|5|',
    b'21346|Eternal Flame Thorn Dragon (1-Day)|0|29|153|Monture|0|5|5|',
    b'23765|Shadow Etherworld Beast|0|29|154|Monture|0|5|5|',
    b'24130|Eternal Flame Thorn Dragon (7 Day)|0|29|155|Monture|0|5|5|',
    b'29150|Untamed Grassland Alpaca|0|29|156|Monture|0|5|5|',
    b'29226|Assault Murasame-FOX Walker (Prime) (7-Day)|0|29|157|Monture|0|5|5|',
    b'29227|Assault Murasame-FOX Transformer (3-Day)|0|29|158|Monture|0|5|5|',
    b'39202|Test Battle Flying Dragon|0|29|159|Monture|0|5|5|',
    b'39203|Test Battle Golden Alpaca|0|29|160|Monture|0|5|5|',
    b'39204|Test Battle Black Alpaca|0|29|161|Monture|0|5|5|',
    b'39205|Test Battle Razorback|0|29|162|Monture|0|5|5|',
    b'39206|Test Battle Sabretooth|0|29|163|Monture|0|5|5|',
    b'39207|Test Battle Twin Cow|0|29|164|Monture|0|5|5|',
    b'39280|Test Lion Beast|0|29|165|Monture|0|5|5|',
    b'39281|Test Lion Beast|0|29|166|Monture|0|5|5|',
    b'48813|Renard Blanc du blizzard aux neuf queues|0|29|167|Monture|0|5|5|',
    # Famille renard EDENFR — stubs US I00132 remplacés (r267/269/270).
    b'48799|Renard du blizzard aux neuf queues bleu fonce (Vert)|0|29|168|Monture|0|5|5|',
    b'48815|Renard d\'Or aux neuf queues (Superieur)|0|29|169|Monture|0|5|5|',
    b'48817|Renard aux neuf queues Pourpre (Legendaire)|0|29|170|Monture|0|5|5|',
]

EXTRA_ROWS = {
    'c_enchant.ini': [_kitsune_enchant, _whitefox_enchant],
    'c_collection.ini': _MOUNT_CARDS,
    'lang/en/t_item.ini': [_kitsune_en],
    # Masques de carnaval — defs perdues côté US ; noms FR +
    # enchants liés (même id) présents. Clone du masque 40759.
    'c_itemmall.ini': [
        b"44047|K10063|K10059|G00003|UI006|||Masque de Carnaval "
        b"Mysterieux fait main|68|4|0|268435584||855|||||0|4||||44047"
        b"||||||1|1|100||38|$7$Masque de carnaval EDENFR.",
        b"44048|K10063|K10059|G00003|UI006|||Masque de Carnaval "
        b"Luxueux fait main|68|4|0|268435584||855|||||0|4||||44048"
        b"||||||1|1|100||38|$7$Masque de carnaval EDENFR."],
}
# Fichiers entiers propres au portage (lus par le serveur via
# ReforgedDataLocator — oracle server_us en lecture seule).
EXTRA_FILES = {
    # header |V.11|78| obligatoire : IniTable.ParseLines lit
    # lines[0] comme déclaration de format, sinon 0 row parsée.
    'S_ItemExtra.ini': (
        b'|V.11|78|\r\n'
        + _cells({0: b'1290', 1: b'I01488', 3: b'G00007',
            4: b'UI067', 7: b"Kitsune de l'Aube (Legendaire)",
            8: b'44', 10: b'0', 11: b'8323', 12: b'2', 13: b'-1',
            18: b'0', 19: b'5', 21: b'100', 22: b'506',
            23: b'11354', 24: b'5', 29: b'1', 33: b'52',
            34: b'$7$Messager mythique du soleil - monture '
                b'legendaire EDENFR.'}) + b'\r\n'
        # chaîne de quête Kitsune (1287-1291 hors 1290) — lignes
        # copiées telles quelles depuis c_item EDENFR.
        + b'1287|I00278||||||\xaa\xb0\xafW\xa7\xc0\xa4\xda|44||0|0||-1|||||0|||||||||||100|1|100||52|$7$\xa1u\xb6\xc7\xbb\xa1\xa4\xa4\xa4E\xa7\xc0\xa7\xaf\xaa\xb0\xbf\xf2\xafd\xa6b\xa6U\xa6a\xaa\xba\xa7\xc0\xa4\xda\xa1A\xaf\xe0\xb7P\xa8\xfc\r\n'
        + b'1288|I00115||||||\xc4\xb2\xb4C\xbe\xaf|44||0|0||-1|||||0|||||||||||100|1|100||52|$7$\xa1u\xa5i\xa5H\xa5\xce\xa8\xd3\xa5[\xb3t\xa6U\xba\xd8\xa6\xb3\xbe\xf7\xaa\xab\xaa\xba\xa6\xa8\xaa\xf8\xb3t\xab\xd7\xa1A\xacO\xa4@\r\n'
        + b'1289|I00384||||||\xaa\xb0\xafW\xa6\xe5|44||0|0||-1|||||0|||||||||||100|1|100||52|$7$\xa1u\xb6\xc7\xbb\xa1\xa4\xa4\xa4E\xa7\xc0\xa7\xaf\xaa\xb0\xaa\xba\xa6\xe5\xb2G\xa1A\xc2A\xac\xf5\xaa\xba\xc3C\xa6\xe2\xa6\xb3\xaa\xd1\xa7l\r\n'
        + b'1291|I01481||||||\xaey\xc3M\xc2\xa7\xb2\xb0|48||0|1|2|-1|||||0|||||||||||100||||52|$7$\xa7\xb9\xa6\xa8\xa7\xa4\xc3M\xa5\xf4\xb0\xc8\xaa\xba\xbc\xfa\xc0y\xc4_\xb2\xb0\xa1C\r\n'
        # Renard blanc EDENFR — remplace le placeholder US 48813
        # (S_ItemExtra est prioritaire depuis le passage en remplacement).
        + b'48813|I01681||G00007|UI067|||Renard Blanc du blizzard aux '
          b'neuf queues|44||0|8323|2|-1|||||0|5||100|506|60419|5|||||1|1'
          b'|100||39|$7$Renard legendaire du blizzard. Monte : vitesse '
          b'+90%, HP +7%, ATQ/MAG +2%.\r\n'
        # Reste de la famille renard EDENFR — mêmes stubs US I00132 à
        # remplacer (lignes FR c_itemmall, nom traduit) : r267 bleu
        # foncé, r269 or, r270 pourpre.
        + b"48799|I01636||G00007|UI067|||Renard du blizzard aux neuf "
          b"queues bleu fonce|44||0|8323|2|-1|||||0|2||100|506|60335|5"
          b"|||||1|1|100||39|$7$Ce renard majestueux a l'air\r\n"
        + b"48815|I01682||G00007|UI067|||Renard d'Or aux neuf queues|"
          b"44||0|8323|2|-1|||||0|2||100|506|60421|5|||||1|1|100||39|"
          b"$7$Ce renard a l'air feroce et motive. Parfait sur le "
          b"champ de bataille !\r\n"
        + b"48817|I01683||G00007|UI067|||Renard aux neuf queues "
          b"Pourpre|44||0|8323|2|-1|||||0|5||100|506|60423|5|||||1|1|"
          b"100||39|$7$Ce renard a l'air feroce et motive. Parfait "
          b"sur le champ de bataille !\r\n"
        # Masques de carnaval — defs perdues côté US ; noms FR +
        # enchants liés (même id) présents. Clone du masque 40759.
        + b"44047|K10063|K10059|G00003|UI006|||Masque de Carnaval "
          b"Mysterieux fait main|68|4|0|268435584||855|||||0|4||||44047"
          b"||||||1|1|100||38|$7$Masque de carnaval EDENFR.\r\n"
        + b"44048|K10063|K10059|G00003|UI006|||Masque de Carnaval "
          b"Luxueux fait main|68|4|0|268435584||855|||||0|4||||44048"
          b"||||||1|1|100||38|$7$Masque de carnaval EDENFR.\r\n"),
}
# Items présents dans le catalogue EDENFR (c_item/c_itemmall FR) mais
# sans def serveur US (S_Item/S_ItemMall) — copiés en bloc : boîtes
# « Surprise », gemmes, cosmétiques... Sans eux //clone_item refuse et
# l'usage tombe sur « hors tables ».
_US_SRV = ROOT + '/eden_server/server_us/Data/db_decoded'
def _srv_ids(name):
    p = _US_SRV + '/' + name
    if not os.path.exists(p): return set()
    return {int(m.group(1)) for l in open(p, 'rb').read().split(b'\n')
            if (m := re.match(rb'^(\d+)\|', l))}
_srv = _srv_ids('S_Item.ini') | _srv_ids('S_ItemMall.ini')
_extra_ids = {int(l.split(b'|')[0]) for blob in EXTRA_FILES.values()
              for l in blob.split(b'\n') if re.match(rb'^\d+\|', l)}
_fr_items = {}
for _f in ('c_item.ini', 'c_itemmall.ini'):
    _p = find_db(FR, _f)
    if _p:
        _, _r = pipe_rows(rd(_p))
        _fr_items.update(_r)
_fr_only = sorted(set(_fr_items) - _srv - _extra_ids)
EXTRA_FILES['S_ItemExtra.ini'] = EXTRA_FILES['S_ItemExtra.ini'] + b''.join(
    _fr_items[i] + b'\r\n' for i in _fr_only)
print(f'S_ItemExtra: {len(_extra_ids)} curés + {len(_fr_only)} FR-only')
# Lignes EDENFR absentes des tables serveur combo/chest (chargées sans
# overlay par le serveur — les coffres cosmétiques 10001-10090 etc.
# n'existent que côté FR). Overlays propres au portage, headers natifs.
def _pipe_extra(src_name, srv_name, header):
    have = _srv_ids(srv_name)
    p = find_db(FR, src_name)
    rows = [l for _, l in pipe_rows(rd(p))[1].items()
            if int(l.split(b'|')[0]) not in have]
    print(f'{srv_name[:-4]}Extra: {len(rows)} lignes EDENFR')
    return header + b'\r\n' + b'\r\n'.join(rows) + b'\r\n'
EXTRA_FILES['S_ItemChestExtra.ini'] = _pipe_extra(
    'c_itemchest.ini', 'S_ItemChest.ini', b'|V.3|32|')
EXTRA_FILES['S_ItemComboExtra.ini'] = _pipe_extra(
    'c_itemcombo.ini', 'S_ItemCombo.ini', b'|V.4|42|')
# Enchants EDENFR absents de TOUTES les tables résolues par le serveur
# (S_Enchant + S_ItemMallEnchant natifs + overlay c_*) — orbes,
# montures FR-only (scorpions, tigre, renards), anti-break stones.
_ench_have = (_srv_ids('S_Enchant.ini') | _srv_ids('S_ItemMallEnchant.ini'))
for _f in ('c_enchant.ini', 'c_itemmallenchant.ini'):
    _p = OUT + '/data/db/' + _f
    if os.path.exists(_p):
        _ench_have |= {int(m.group(1)) for l in open(_p, 'rb').read().split(b'\n')
                       if (m := re.match(rb'^(\d+)\|', l))}
_ench_rows = {}
for _f in ('c_enchant.ini', 'c_itemmallenchant.ini'):
    _p = find_db(FR, _f)
    if _p:
        _, _r = pipe_rows(rd(_p))
        _ench_rows.update(_r)
_ench_miss = sorted(set(_ench_rows) - _ench_have)
EXTRA_ROWS.setdefault('c_enchant.ini', []).extend(
    _ench_rows[i] for i in _ench_miss)
print(f'enchants EDENFR manquants ajoutés: {len(_ench_miss)}')
# Noms localisés présents dans EDENFR mais perdus à la fusion.
_names_have = set()
for _f in ('t_item.ini', 't_itemmall.ini', 't_itemchest.ini',
           't_itemcombo.ini'):
    _p = OUT + '/data/db/' + _f
    if os.path.exists(_p):
        _names_have |= {int(m.group(1)) for l in open(_p, 'rb').read().split(b'\n')
                        if (m := re.match(rb'^(\d+)\|', l))}
_name_rows = {}
for _f in ('t_item.ini', 't_itemmall.ini', 't_itemchest.ini',
           't_itemcombo.ini'):
    _p = find_db(FR, _f)
    if _p:
        _, _r = pipe_rows(rd(_p))
        for _i, _l in _r.items():
            _name_rows.setdefault(_i, (_f, _l))
_name_miss = [i for i in sorted(set(_fr_items) | set(_name_rows))
              if i in _fr_items and i in _name_rows and i not in _names_have]
for i in _name_miss:
    EXTRA_ROWS.setdefault(_name_rows[i][0], []).append(_name_rows[i][1])
print(f'noms localisés rattrapés: {len(_name_miss)}')
for rel, lines in EXTRA_ROWS.items():
    dst = OUT + '/data/db/' + rel
    if not os.path.exists(dst): continue
    cur = open(dst, 'rb').read()
    have = {m.group(1) for l in cur.split(b'\n')
            if (m := re.match(rb'^(\d+)\|', l))}
    add = []
    for l in lines:
        i = int(l.split(b'|')[0])
        if i in have: continue
        have.add(i)
        add.append(l)
    if add:
        if not cur.endswith(b'\n'): cur += b'\r\n'
        open(dst, 'wb').write(cur + b'\r\n'.join(add) + b'\r\n')
        report['merged'][rel] = report['merged'].get(rel, '') + \
            f' +{len(add)} ligne(s) portage'
for rel, blob in EXTRA_FILES.items():
    open(OUT + '/data/db/' + rel, 'wb').write(blob)
    report['copied_files'].append(rel)

# ---------- 4. scènes complémentaires ----------
for sid in SCENE_COMPLEMENTS:
    got = []
    for sub in ('data/scene/', 'data/Scene/'):
        p = US + '/' + sub + sid + '.ini'
        if os.path.exists(p):
            shutil.copyfile(p, OUT + '/data/scene/' + sid + '.ini'); got.append('scene-ini')
    for pat in (US + '/map/model/' + sid, US + '/map/Model/' + sid):
        if os.path.isdir(pat):
            dst = OUT + '/map/model/' + sid
            shutil.copytree(pat, dst, dirs_exist_ok=True); got.append('map-model')
    for pat in glob.glob(US + '/map/sknodemap/' + sid + '.*'):
        os.makedirs(OUT + '/map/sknodemap', exist_ok=True)
        shutil.copyfile(pat, OUT + '/map/sknodemap/' + os.path.basename(pat)); got.append('smp')
    report['scenes'][sid] = got or 'ABSENT US'

# ---------- 5. le reste de l'arbre FR (hors data/db déjà traité) ----------
for top in ('map', 'model', 'char', 'monster', 'ride', 'effect', 'house',
            'emblem', 'decorate', 'cutscene', 'CutScene', 'ui', 'UI', 'item',
            'movies', 'Movies', 'data'):
    src = FR + '/' + top
    if not os.path.isdir(src): continue
    dst = OUT + '/' + top
    # data/ : copier tout sauf db déjà gérée (scene, etc.)
    if top == 'data':
        for f in os.listdir(src):
            if f == 'db': continue
            shutil.copytree(src + '/' + f, dst + '/' + f, dirs_exist_ok=True) \
                if os.path.isdir(src + '/' + f) else shutil.copyfile(src + '/' + f, dst + '/' + f)
        continue
    shutil.copytree(src, dst, dirs_exist_ok=True)

# ---------- 6. uidata = Reforged (layouts identiques + extras) ----------
for cand in (RF + '/uidata', RF + '/UIData', US + '/UIData'):
    if os.path.isdir(cand):
        shutil.copytree(cand, OUT + '/uidata', dirs_exist_ok=True)
        report['uidata_source'] = cand
        break
# arial.ttf bonus depuis EDENFR
for cand in glob.glob(FR + '/uidata/**/arial.ttf', recursive=True) + \
            glob.glob(FR_EXTR + '/uidata/**/arial.ttf', recursive=True):
    os.makedirs(OUT + '/uidata', exist_ok=True)
    shutil.copyfile(cand, OUT + '/uidata/arial.ttf')

# ---------- 7. actifs liés aux compléments ----------
# Les codes n######/m###### ne sont pas des fichiers : ils passent par les
# fichiers d'index (npclist/monsterlist/boundinfo/ridelist) → m*.kfm/dds.

def load_list(root, name):
    """{code: ligne} depuis un fichier d'index `DBxx=code,fichier,...`."""
    p = find_db(root, name)
    out = {}
    if not p: return out
    for l in rd(p).split(b'\n'):
        m = re.match(rb'[^=]+=([a-zA-Z][0-9]+),(.*)', l)
        if m: out[m.group(1).decode('ascii', 'ignore').lower()] = l.rstrip(b'\r\n')
    return out

# index basename→chemins et stem→chemins de l'arbre US (un seul walk)
us_files = {}
us_stems = {}
for dp, _, fns in os.walk(US):
    for fn in fns:
        us_files.setdefault(fn.lower(), []).append(dp + '/' + fn)
        us_stems.setdefault(os.path.splitext(fn)[0].lower(), []).append(dp + '/' + fn)
# basenames des textures monstres/partenaires (préfixe stem)
us_tex = sorted(os.path.basename(f).lower()
                for ps in us_files.values() for f in ps
                if '/monster/texture/' in f.replace('\\', '/'))

copied = missing = 0
want_files = set()
list_adds = {}   # listfile -> {code: ligne}

def add_list_entry(listname, code):
    code = code.lower()
    lf = listname + '.ini'
    if code in fr_lists.get(listname, {}): return
    line = us_lists.get(listname, {}).get(code)
    if line is None:
        report['models_missing'].append(f'{listname}:{code}'); return
    list_adds.setdefault(lf, {})[code] = line
    for g in re.finditer(rb'([A-Za-z0-9_]+\.(?:kfm|nif|dds|kf|bmp|tga))', line, re.I):
        fn = g.group(1).decode().lower()
        want_files.add(fn)
        stem = os.path.splitext(fn)[0]
        want_files.add(stem + '.kfm')
        # textures associées par préfixe (m001 → m00101.dds) dans monster/texture
        for t in us_tex:
            if t.startswith(stem): want_files.add(t)

fr_lists = {n[:-4]: load_list(FR, n) for n in
            ('npclist.ini', 'monsterlist.ini', 'boundinfo.ini', 'ridelist.ini', 'elflist.ini')}
# codes déjà présents dans OUT (idempotence en cas de relance)
for n in list(fr_lists):
    p = OUT + '/data/db/' + n + '.ini'
    if os.path.exists(p):
        extra = {}
        for l in rd(p).split(b'\n'):
            m = re.match(rb'[^=]+=([a-zA-Z][0-9]+),(.*)', l)
            if m: extra[m.group(1).decode('ascii', 'ignore').lower()] = l
        fr_lists[n] = {**fr_lists[n], **extra}
us_lists = {n[:-4]: load_list(US, n) for n in
            ('npclist.ini', 'monsterlist.ini', 'boundinfo.ini', 'ridelist.ini', 'elflist.ini')}

# ids complémentés par table → codes modèles + fichiers
for fname, how in report['merged'].items():
    if not isinstance(how, str) or '+0 ' in how: continue
    fr_p = find_db(FR, fname); us_p = find_db(US, fname)
    if not fr_p or not us_p: continue
    _, fr_rows = pipe_rows(rd(fr_p)); _, us_rows = pipe_rows(rd(us_p))
    new_ids = set(us_rows) - set(fr_rows)
    for i in new_ids:
        f = us_rows[i].split(b'|')
        code = f[1].decode('ascii', 'ignore').lower() if len(f) > 1 else ''
        if fname == 'c_npc.ini' and code.startswith('n'):
            add_list_entry('npclist', code); add_list_entry('boundinfo', code)
        elif fname == 'c_monster.ini' and code.startswith('m'):
            add_list_entry('boundinfo', code)
        elif fname == 'c_partner.ini':
            pcode = f[2].decode('ascii', 'ignore').lower() if len(f) > 2 else ''
            if re.match(rb'[a-z][0-9]+$', f[2] or b'', re.I):
                add_list_entry('boundinfo', pcode)
        # icônes/modèles directs i#####, b*, w*, r*
        for g in re.finditer(rb'([ibwr][0-9]{2,6})\.(kfm|nif|dds|kf|bmp|tga)', us_rows[i], re.I):
            want_files.add((g.group(1) + b'.' + g.group(2)).decode().lower())
        for g in re.finditer(rb'[|,](i[0-9]{4,6})[|,]', us_rows[i], re.I):
            want_files.add(g.group(1).decode().lower() + '.dds')

# enchant ride-keys complémentés → ridelist
_, fr_e = pipe_rows(rd(find_db(FR, 'c_enchant.ini') or b''))
_, us_e = pipe_rows(rd(find_db(US, 'c_enchant.ini') or b''))
for i in set(us_e) - set(fr_e):
    for g in re.finditer(rb'2134[|,]([0-9]+)', us_e[i]):
        add_list_entry('ridelist', 'r%06d' % int(g.group(1)))

# écrire les lignes d'index ajoutées
for lf, adds in list_adds.items():
    dst = OUT + '/data/db/' + lf
    if os.path.exists(dst) and adds:
        with open(dst, 'ab') as fh:
            fh.write(b''.join(adds[c] + b'\n' for c in sorted(adds)))
        report.setdefault('list_entries', {})[lf] = len(adds)

for fn in sorted(want_files):
    found = us_files.get(fn, [])
    if not found:
        missing += 1; report['models_missing'].append('file:' + fn); continue
    for f in found:
        rel = os.path.relpath(f, US)
        dst = OUT + '/' + rel
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(f, dst); copied += 1
        report['models_copied'].append(rel)

# ---------- 7b. fusion des collisions de casse ----------
# l'union pkg/extracted crée des paires s003/ + S003/ (le client Windows
# les voyait comme un seul dossier) — fusion récursive, doublons → .casealt.
def _norm_dir(d):
    try: names = os.listdir(d)
    except OSError: return
    groups = {}
    for n in names: groups.setdefault(n.lower(), []).append(n)
    for low, variants in groups.items():
        if len(variants) < 2: continue
        dirs = [v for v in variants if os.path.isdir(d + '/' + v)]
        if len(dirs) >= 2:
            canon = max(dirs, key=lambda v: len(os.listdir(d + '/' + v)))
            for v in dirs:
                if v == canon: continue
                for f in os.listdir(d + '/' + v):
                    src, dst = d + '/' + v + '/' + f, d + '/' + canon + '/' + f
                    if os.path.exists(dst): dst += '.casealt'
                    shutil.move(src, dst)
                os.rmdir(d + '/' + v)
        files = [v for v in variants if not os.path.isdir(d + '/' + v)]
        if len(files) >= 2:
            keep = max(files, key=lambda v: os.path.getsize(d + '/' + v))
            for v in files:
                if v != keep: os.rename(d + '/' + v, d + '/' + keep + '.casealt')
    for n in os.listdir(d):
        if os.path.isdir(d + '/' + n): _norm_dir(d + '/' + n)
_norm_dir(OUT)
import filecmp
for f in glob.glob(OUT + '/**/*.casealt', recursive=True):
    base = f[:-8]
    if os.path.isfile(base) and filecmp.cmp(f, base, shallow=False):
        os.remove(f)          # doublon exact
    elif os.path.isdir(f):
        for x in os.listdir(f):  # dossier-vari → réintégrer dans le canon
            shutil.move(f + '/' + x, base + '/' + x if not os.path.exists(base + '/' + x) else base + '/' + x + '.casealt')
        os.rmdir(f)
    elif not os.path.exists(base):
        os.rename(f, base)

# ---------- 8. remplissage EN des ids sans texte FR ----------
# les t_* base = FR ∪ EN-pour-ids-absents → un id complété affiche de l'EN
# plutôt que Big5/#id ; les traductions FR prioritaires passent par lang/fr.
filled = {}
for f in sorted(os.listdir(OUT + '/data/db')):
    if not f.startswith('t_'): continue
    en_p, base_p = OUT + '/data/db/lang/en/' + f, OUT + '/data/db/' + f
    if not (os.path.exists(en_p) and os.path.exists(base_p)): continue
    def _rows(p):
        out = {}
        for l in open(p, encoding='utf8').read().split('\n'):
            m = re.match(r'^(\d+)\|', l)
            if m: out[int(m.group(1))] = l
        return out
    base, en = _rows(base_p), _rows(en_p)
    add = [en[i] for i in sorted(set(en) - set(base))]
    if add:
        with open(base_p, 'a', encoding='utf8') as fh:
            fh.write('\n'.join(add) + '\n')
        filled[f] = len(add)
report['en_fill'] = filled

report['stats'] = {'merged_tables': merged, 'schema_swapped': schema_swapped,
                   'fr_only': fr_only, 'want_files': len(want_files),
                   'list_entries': sum(len(v) for v in list_adds.values()),
                   'models_copied': copied, 'models_missing': missing}
json.dump(report, open(OUT + '/build_report.json', 'w'), indent=1)
print(json.dumps(report['stats'], indent=1))
print('missing ex:', report['models_missing'][:20])
