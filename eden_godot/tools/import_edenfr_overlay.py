#!/usr/bin/env python3
"""Overlay EDENFR (client officiel, pkg extraits) -> arbre « raw » delta.

Selectionne (symlinks, lecture seule) : fichiers map/ride/monster/data-scene
ABSENTS de raw_assets (Reforged) + surcharges map/ (textures, objets, NIF de
map) dont la taille differe. Puis :
  python3 tools/textures/convert_dds_to_png.py --root OVERLAY --out godot_project/assets/textures
  EDEN_RAW_ASSETS=OVERLAY python3 tools/nif2gltf/convert.py --no-resume
Usage : import_edenfr_overlay.py FR_EXTRACTED RAW_ASSETS OUT
(FR_EXTRACTED = dossier issu de tools/unpacker/pkgfs_unpack.py extract).
"""
import sys
import os,json,re,shutil
FR,RF,OUT=sys.argv[1:4]
SK=('cutscene','movies','emblem','uidata','ui','data')
def idx(root):
    d={}
    for dp,_,fns in os.walk(root):
        for f in fns:
            p=os.path.join(dp,f); d[os.path.relpath(p,root).lower()]=(p,os.path.getsize(p))
    return d
a=idx(FR); b=idx(RF)
# case-correct path in RF
rfcase={}
for dp,_,fns in os.walk(RF):
    for f in fns:
        rfcase[os.path.relpath(os.path.join(dp,f),RF).lower()]=os.path.relpath(os.path.join(dp,f),RF)
def newcase(k,orig):
    p=orig
    m=re.match(r'map/model/(s\d+)/(.*)$',k)
    if m:
        s=m.group(1).upper(); rest=m.group(2)
        rest=re.sub(r'^s(\d+)',lambda x:'S'+x.group(1),rest)
        rest=re.sub(r'^dark/(\d+)darkmap',lambda x:'Dark/%sDarkMap'%x.group(1),rest)
        return 'map/model/%s/%s'%(s,rest)
    if k.startswith('map/model/object/') or k.startswith('ride/model/r') or k.startswith('ride/animation/r') or re.match(r'ride/r\d+\.kfm',k):
        dn,fn=os.path.split(orig); return dn+'/'+fn[0].upper()+fn[1:]
    if k.startswith('map/sknodemap/'):
        dn,fn=os.path.split(orig); return dn+'/'+fn[0].upper()+fn[1:]
    return orig
sel=[]
for k,(p,sz) in a.items():
    top=k.split('/')[0]
    if top in SK and not k.startswith('data/scene'): continue
    if k.endswith(('.bmp','.txt','_old.nif')) or k.endswith('.csm') and top=='map': continue
    if k not in b:
        if top in('map','ride','monster','data'): sel.append((k,p,'new'))
    elif sz!=b[k][1]:
        if top=='map' : sel.append((k,p,'override'))
shutil.rmtree(OUT,ignore_errors=True)
log=[]
for k,p,kind in sel:
    rel = rfcase[k] if k in rfcase else newcase(k,os.path.relpath(p,FR))
    dst=os.path.join(OUT,rel); os.makedirs(os.path.dirname(dst),exist_ok=True)
    os.symlink(p,dst); log.append((kind,rel))
print(len(log)); 
import collections; print(collections.Counter(k for k,_ in log))

