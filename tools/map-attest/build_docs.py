#!/usr/bin/env python3
"""Fusionne nodes.json (build_list.py) + results.tsv + notes.tsv -> docs/reference/maps.tsv.
Le MAPS.md est rédigé à la main ; ce script écrit aussi artifacts/map-attest/summary.json."""
import json, os, re, csv, collections
R = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
A = R + '/artifacts/map-attest'
nodes = json.load(open(A + '/nodes.json'))
byid = {x['id']: x for x in nodes}
res = {}
for row in csv.DictReader(open(A + '/results.tsv'), delimiter='\t'):
    res[(row['code'], row['mode'])] = row
notes = {}
if os.path.exists(A + '/notes.tsv'):
    for l in open(A + '/notes.tsv', encoding='utf8'):
        p = l.rstrip('\n').split('\t')
        if len(p) >= 2 and not l.startswith('#'): notes[p[0]] = p[1:]
mis = collections.Counter(int(m) for m in re.findall(r'\bS(\d{3})\b',
      open(R + '/corpus/fr-full/data/db/c_mission.ini', 'rb').read().decode('big5', 'replace')))
KIND = {'1': 'champ', '3': 'ville', '4': 'sanctuaire', '5': 'donjon/instance/divers', '7': 'épreuve héroïque',
        '8': 'guerre de nodes', '9': 'maison perso', '10': 'arène', '11': 'profondeurs (donjon)',
        '12': 'expédition anti-dragon', '13': 'champ de bataille', '14': 'tour', '15': 'survie',
        '16': 'île du mariage', '17': 'temple du chaos', '18': 'illusion destructrice'}
DEV = re.compile(r'測試|暫名|暫用|格子世界|^帳篷')
def core(z): return re.sub(r'【.*?】|\(.*?\)|^.*•', '', z).strip()
def classify(x):
    i = x['id']; own = x['geo_own']; fr = x['fr']; z = x['c_name']
    donor = ''; dnote = ''
    mm = re.match(r'S(\d{3})M', x['minimap'] or '')
    if mm and int(mm.group(1)) != i and not own:
        d = byid.get(int(mm.group(1)))
        if d and d['geo_own']:
            donor = 'S%03d' % d['id']
            dnote = 'confirmé par nom' if core(z) and core(z) == core(d['c_name']) else 'non confirmé'
    if not x['in_c']:
        if own: return 'T_ONLY_GEO', donor, dnote       # nom FR + géométrie mais pas de ligne c_node
        if fr: return 'LABEL_UI', donor, dnote          # libellé t_node sans carte
        return 'VIDE', donor, dnote
    if DEV.search(z) or 'Place Holder' in fr or '(Placeholder' in fr or fr.startswith('Test Cottage'):
        return ('DEV_TEST_GEO' if own else 'DEV_TEST_NOGEO'), donor, dnote
    if own:
        return ('JOUABLE_NOMMEE' if fr else 'JOUABLE_SANS_NOM_FR'), donor, dnote
    # Scène propre déclarant sa géométrie partagée ([globe_data]) :
    # prioritaire sur le donneur minimap — c'est ce que le client charge.
    sc = x.get('geo_scene')
    if sc: return 'SANS_GEO_SCENE_DECLAREE', os.path.splitext(sc)[0].upper(), 'globe_data'
    if donor: return ('SANS_GEO_DONNEUR_CONFIRME' if dnote.startswith('confirmé') else 'SANS_GEO_DONNEUR_INCERTAIN'), donor, dnote
    return ('LACUNE_ARCHIVE' if fr else 'LACUNE_SANS_NOM'), donor, dnote
cols = ['id', 'code_client', 'code_c_node_col5', 'nom_fr', 'nom_zh', 'niveaux', 'kind', 'kind_label', 'geometrie_propre',
        'donneur_minimap', 'donneur_verif', 'classification', 'refs_c_mission', 'charge_loose', 'maploader', 'placeholders',
        'collision_objs', 'colliders_consolides', 'props', 'npc_scene', 'event_areas', 'spawn', 'charge_strict', 'capture', 'note_visuelle']
out = []; summ = collections.Counter()
for x in nodes:
    cls, donor, dn = classify(x)
    code = 'S%03d' % x['id']
    r = res.get((code, 'loose')); s = res.get((code, 'strict'))
    nt = notes.get(code, [''])
    geo = x['geo_own'] or ''
    if geo: geo = 'map/model/%s/%s' % (code.lower() if geo.islower() else code, geo)
    row = [x['id'], code, x['col5'], x['fr'].replace('\n', ' '), x['c_name'], x['lv'], x['kind'], KIND.get(x['kind'], ''),
           geo, donor, dn, cls, mis.get(x['id'], 0)]
    if r and r['status']=='FAIL' and 'introuvable' in r['reasons']:
        row += ['FAIL(map introuvable -> repli S009, rien chargé)'] + ['']*8 + [(s['status'] if s else ''), r['shot']]
    elif r:
        row += [r['status'] + ('(' + r['reasons'] + ')' if r['reasons'] else ''), r['maploader'], r['placeholders'], r['coll_objs'],
                r['consolidated'], r['props'], r['npc'], r['event_areas'], r['spawn'],
                (s['status'] + ('(' + s['reasons'] + ')' if s['reasons'] else '')) if s else '', r['shot']]
    else:
        row += ['non lancé' if geo or x.get('geo_scene') else 'sans géométrie'] + [''] * 8 + [(s['status'] if s else ''), '']
    dflt = ''
    if r and r['maploader']=='nif-unique' and not nt[0]: dflt='NIF unique : aucune collision comptée ; caméra de spawn à vérifier (cf. MAPS.md §Anomalies)'
    row.append(nt[0] or dflt)
    out.append(row); summ[cls] += 1
os.makedirs(R + '/docs/reference', exist_ok=True)
with open(R + '/docs/reference/maps.tsv', 'w', encoding='utf8') as f:
    f.write('\t'.join(cols) + '\n')
    for r in out: f.write('\t'.join(str(c).replace('\t', ' ') for c in r) + '\n')
# géométries orphelines (dossiers sans node)
orph = []
for code in sorted(k[0] for k in res if k[1] == 'loose'):
    if int(code[1:]) not in byid:
        r = res[(code, 'loose')]; orph.append(code)
json.dump(dict(classes=summ, orphans=orph), open(A + '/summary.json', 'w'), ensure_ascii=False, indent=1)
print(dict(summ)); print('orphelins', orph)
