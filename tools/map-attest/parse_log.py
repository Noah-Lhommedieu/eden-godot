#!/usr/bin/env python3
"""parse_log.py <code> <log> <png> <mode> <secs> -> ligne TSV (stdout).
Ne lit le log que jusqu'à la ligne [shot] : les ERROR après sont le bruit de
sortie du moteur (fuites RID à l'arrêt), pas la map."""
import sys, re, os
code, log, png, mode, secs = sys.argv[1:6]
L = open(log, errors='replace').read().split('\n')
pre = []
for l in L:
    if l.startswith('[shot]'): pre.append(l); break
    pre.append(l)
shot_taken = any(l.startswith('[shot]') for l in pre)
txt = '\n'.join(pre)
def g(rx, s=txt, d=''):
    m = re.search(rx, s); return m.group(1) if m else d
ml = ''
if '[MapLoader]' in txt:
    if 'modèle unique' in txt: ml = 'nif-unique'
    elif re.search(r'\[MapLoader\] OBJ', txt): ml = 'fsm'
    elif 'ni .fsm ni' in txt: ml = 'FAIL-nogeo'
fallback = 'introuvable' in txt
ph = g(r'(\d+) placeholders')
col = g(r'(\d+) objets avec collision')
cons = g(r'colliders consolidés : (\d+) bodies \+ (\d+) shapes')
mm = re.search(r'colliders consolidés : (\d+) bodies \+ (\d+) shapes', txt)
cons = f'{mm.group(1)}b+{mm.group(2)}s' if mm else ''
npc = g(r'\[MapLoader\] (\d+) NPC')
ev = g(r'(\d+) event areas')
props = g(r'(\d+) props batchés')
glb = g(r'OBJ : (\d+) GLB')
sp = g(r'spawn=(\([^)]*\))')
spok = ''
if sp:
    pass
    spok = 'nan' if re.search(r'NaN|Infinity|∞', sp, re.I) else 'ok'
bad = [l.strip() for l in pre if re.search(
    r'ERROR|texture absente|\babsent|[Ee]xception|introuvable|ni \.fsm ni|Échec|SCRIPT ERROR|Unhandled|WARNING:', l)
    and 'i18n-miss' not in l]
# lignes [collision] informatives (classification dalle/mur) ne sont pas des erreurs
bad = [b for b in bad if not b.startswith('[collision]')]
nabs = len([l for l in pre if 'texture absente' in l])
# statistiques image
lum = std = ''
dark = ''
if os.path.exists(png) and os.path.getsize(png) > 0:
    from PIL import Image
    import numpy as np
    im = Image.open(png).convert('RGB')
    a = np.asarray(im).astype(float)
    # zone de jeu : retire 12 % haut/bas (HUD)
    h = a.shape[0]
    c = a[int(h*.12):int(h*.88)]
    y = c.mean(axis=2)
    lum = f'{y.mean():.0f}'; std = f'{y.std():.0f}'
    # nb de teintes quantifiées (16 niveaux/canal) -> scène « plate »
    q = (c // 16).astype(int)
    key = q[..., 0]*256 + q[..., 1]*16 + q[..., 2]
    dark = str(len(np.unique(key)))
status = 'OK'
reasons = []
if not shot_taken: status = 'FAIL'; reasons.append('pas de [shot]')
if fallback: status = 'FAIL'; reasons.append('map introuvable -> repli')
if ml.startswith('FAIL') or ml == '': status = 'FAIL'; reasons.append('pas de MapLoader')
if ph not in ('', '0'): status = 'WARN' if status == 'OK' else status; reasons.append(f'{ph} placeholders')
if nabs: status = 'WARN' if status == 'OK' else status; reasons.append(f'{nabs} texture absente')
if bad: status = 'WARN' if status == 'OK' else status; reasons.append(f'{len(bad)} ligne(s) ERROR/absent')
if lum and (float(lum) < 25 or float(std) < 12 or (dark and int(dark) < 25)):
    status = 'WARN' if status == 'OK' else status; reasons.append('image sombre/plate (auto)')
if spok == 'nan': status = 'FAIL'; reasons.append('spawn NaN')
first = (bad[0][:140] if bad else '')
f = [code, status, mode, ml, glb, ph, col, cons, props, npc, ev, sp, lum, std, dark, str(nabs), str(len(bad)), first, '; '.join(reasons), os.path.relpath(png, os.environ.get('EDEN_ROOT', '.')) if png else '', secs]
print('\t'.join(x.replace('\t', ' ') for x in f))
