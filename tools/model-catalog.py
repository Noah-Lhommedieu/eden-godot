#!/usr/bin/env python3
"""Catalogue TOUS les modèles natifs (.nif/.kf/.kfm) de corpus/fr-full par catégorie.
Sortie : docs/reference/MODELS-CATALOG.tsv (+ MODELS-CATALOG.md : décompte par catégorie).
Règles = conventions de nommage + liens 4pequip.ini / c_item / t_item."""
import os, re, csv, sys
from collections import Counter
ROOT = os.path.join(os.path.dirname(__file__), '..', 'corpus', 'fr-full')
OUT = os.path.join(os.path.dirname(__file__), '..', 'docs', 'reference')
BODY = {'g': 'Humain (fille)', 'b': 'Humain (garçon)', 'm': 'Zumi (souris)', 'f': 'Anuran (grenouille)',
        'k': 'Ursun (ours)', 'l': 'Halfkin (fillette)', 'c': 'Halfkin (garçon)'}
def rd(p): return open(os.path.join(ROOT, p), 'rb').read().decode('cp1252', errors='replace')
# 4pequip : mesh -> item codes
eq = {}
sec = ''
for l in rd('data/db/4pequip.ini').split('\n'):
    l = l.strip()
    if l.startswith('['): sec = l[1:-1]; continue
    m = re.match(r'DB\d+=(.*)', l)
    if m:
        p = m.group(1).split(',')
        if len(p) > 3: eq.setdefault(p[2].lower(), []).append((sec, p[0], p[3]))
# items code -> id/name
code2id = {}
for l in rd('data/db/c_item.ini').split('\r\n'):
    p = l.split('|')
    if p[0].isdigit() and len(p) > 2: code2id.setdefault(p[2], int(p[0]))
names = {}
for b in re.split(r'\r\n(?=\d+\|)', rd('data/db/t_item.ini')):
    m = re.match(r'(\d+)\|([^|]*)\|', b)
    if m: names[int(m.group(1))] = m.group(2)
def classify(top, fn):
    low = fn.lower(); stem = os.path.splitext(low)[0]
    ext = os.path.splitext(low)[1]
    if ext in ('.kf', '.kfm'): return ('Animation', 'animation', '')
    if top == 'char':
        if re.fullmatch(r'[bgmfklc]', stem): return ('Perso : corps complet (méga-modèle race/genre)', BODY.get(stem, ''), '')
        m = re.fullmatch(r'([bgmfklc])(\d)(\d\d)', stem)   # b101, g204…
        if m: return ('Perso : pièce de tenue/coiffure (dans corps)', BODY.get(m.group(1), ''), '')
        m = re.fullmatch(r'([bgmfklc])(1[0-9]{3})', stem)   # b1001 : coiffure/casque (série 1xxx)
        if m: return ('Perso : casque / coiffure attachée (série 1xxx)', BODY.get(m.group(1), ''), '')
        m = re.fullmatch(r'([bgmfklc])(2[0-9]{3})', stem)   # g2xxx : cape / dos
        if m: return ('Perso : cape / dos attaché (série 2xxx)', BODY.get(m.group(1), ''), '')
        m = re.fullmatch(r'([bgmfklc])(1[0-9]{4})', stem)   # b13001 : accessoire / ailes (série 1xxxx)
        if m: return ('Perso : accessoire / ailes / costume (série 1xxxx)', BODY.get(m.group(1), ''), '')
        if stem.startswith('x'): return ('Perso : racial / spécial (x…)', '', '')
        if stem == 'dressroom': return ('Perso : cabine d’essayage', '', '')
        return ('Perso : autre', '', '')
    if top == 'item':
        if low.startswith('w'): return ('Objet : arme', '', '')
        return ('Objet : autre (g…)', '', '')
    if top == 'ride': return ('Monture', '', '')
    if top == 'monster': return ('Monstre / PNJ', '', '')
    if top == 'effect':
        if re.match(r's\d+', stem): return ('Effet : sort (sNNNNN)', '', '')
        if stem.startswith('indicator'): return ('Effet : indicateur', '', '')
        return ('Effet : autre', '', '')
    if top == 'house': return ('Maison', '', '')
    if top == 'decorate': return ('Décor intérieur', '', '')
    if top == 'map': return ('Carte : décor / terrain', '', '')
    return (top, '', '')
rows = []
for top in ('char', 'item', 'ride', 'monster', 'effect', 'house', 'decorate', 'map'):
    base = os.path.join(ROOT, top)
    for dp, dn, fn in os.walk(base):
        rel = os.path.relpath(dp, ROOT)
        if top == 'map' and '/model' not in rel and not rel.endswith('model'): continue
        for f in fn:
            if os.path.splitext(f)[1].lower() not in ('.nif', '.kf', '.kfm'): continue
            if rel.split(os.sep)[-1] in ('texture',): continue
            cat, sub, _ = classify(top, f)
            link = ''
            lowstem = os.path.splitext(f)[0].lower()
            if top == 'char':
                refs = eq.get(lowstem, [])
                if refs:
                    sec, code, skin = refs[0]
                    iid = code2id.get(code)
                    link = f'{len(refs)} ligne(s) 4pequip ; ex. {code} / skin {skin} / {names.get(iid, "")}'
            rows.append((os.path.join(rel, f).replace(os.sep, '/'), cat, sub, link,
                         os.path.getsize(os.path.join(dp, f))))
rows.sort()
with open(os.path.join(OUT, 'MODELS-CATALOG.tsv'), 'w', encoding='utf-8', newline='') as o:
    w = csv.writer(o, delimiter='\t'); w.writerow(['fichier', 'catégorie', 'race/genre', 'lien 4pequip/item', 'octets']); w.writerows(rows)
c = Counter(r[1] for r in rows)
with open(os.path.join(OUT, 'MODELS-CATALOG.md'), 'w', encoding='utf-8') as o:
    o.write('# Catalogue des modèles natifs (corpus/fr-full)\n\nGénéré par `tools/model-catalog.py` — chaque .nif/.kf/.kfm est classé dans `MODELS-CATALOG.tsv`.\n\n| catégorie | fichiers |\n|---|---|\n')
    for k, v in sorted(c.items(), key=lambda kv: -kv[1]): o.write(f'| {k} | {v} |\n')
    o.write(f'\nTotal : {len(rows)} fichiers.\n')
print(len(rows)); [print(v, k) for k, v in c.most_common()]
