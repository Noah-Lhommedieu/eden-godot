#!/usr/bin/env python3
"""Construit artifacts/map-attest/nodes.json depuis c_node/t_node/map/ (lecture seule)."""
import re, os, json, sys
R=os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
C=R+'/corpus/fr-full'
def recs(path,enc):
    t=open(path,'rb').read().decode(enc,errors='replace').replace('\r','')
    out=[];cur=None
    for ln in t.split('\n'):
        if re.match(r'^\d+\|',ln): 
            if cur: out.append(cur)
            cur=ln
        elif cur is not None: cur+='\n'+ln
    if cur: out.append(cur)
    return out
cn={}
for r in recs(C+'/data/db/c_node.ini','big5'):
    f=r.split('|'); cn[int(f[0])]=f
tn={}
for r in recs(C+'/data/db/t_node.ini','cp1252'):
    f=r.split('|'); tn[int(f[0])]=f
# dossiers map/model (insensible à la casse) -> fichiers
md={}
for d in os.listdir(C+'/map/model'):
    p=C+'/map/model/'+d
    if os.path.isdir(p): md.setdefault(d.lower(),{})[d]=sorted(os.listdir(p))
sk=set(x.lower() for x in os.listdir(C+'/map/sknodemap'))
# data/scene/sXXX.ini : [globe_data] col0 = géométrie déclarée par la
# scène propre du node (mécanisme EDENFR — S137 → « S055.fsm »). Le
# fichier déclaré doit exister sous map/model/<stem>/ (insensible casse).
def scene_geo(own):
    p=C+'/data/scene/'+own.lower()+'.ini'
    if not os.path.exists(p): return None
    ing=False
    for ln in open(p,encoding='latin-1',errors='replace'):
        ln=ln.strip()
        if ln.startswith('['):
            ing=ln[1:].lower().startswith('globe_data'); continue
        if ing and ln and not ln.startswith(';'):
            stem=ln.split(',')[0].strip()
            if not stem: return None
            files=[]
            for d,fl in md.get(os.path.splitext(stem)[0].lower(),{}).items(): files+=[x for x in fl]
            return stem if any(x.lower()==stem.lower() for x in files) else None
    return None
out=[]
for i in sorted(set(cn)|set(tn)):
    f=cn.get(i); t=tn.get(i)
    code=f[4] if f and len(f)>4 else ''
    own='S%03d'%i
    def geo(c):
        if not c: return None
        files=[]
        for d,fl in md.get(c.lower(),{}).items(): files+= [x for x in fl]
        k=[x for x in files if x.lower().endswith(('.fsm','.nif'))]
        return k[0] if k else None
    out.append(dict(id=i,c_name=f[1] if f else '',lv=f[2] if f else '',kind=f[3] if f else '',
      col5=code,minimap=f[5] if f and len(f)>5 else '',worldmap=f[6] if f and len(f)>6 else '',
      own=own,fr=(t[1].strip() if t and len(t)>1 else ''),in_c=bool(f),in_t=bool(t),
      geo_own=geo(own),geo_col5=geo(code),geo_scene=scene_geo(own),smp_own=(own.lower()+'.smp') in sk))
json.dump(out,open(R+'/artifacts/map-attest/nodes.json','w'),ensure_ascii=False,indent=0)
print(len(out))
