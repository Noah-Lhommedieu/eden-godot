#!/usr/bin/env python3
"""Dump headless des verts de bord entre tuiles terrain voisines d'un .fsm.
usage: fsm_seam_dump.py S003.fsm [coordMonde=192] [axe=y]"""
import struct, sys
REC=0x1abf4
def load(p):
    d=open(p,'rb').read(); o=0x58; recs=[]
    while o<len(d):
        tag,size=struct.unpack_from('<4sI',d,o)
        if tag==b'LAY0':
            gx,gy,sc,n=struct.unpack_from('<IIfI',d,o+8)
            for i in range(n):
                b=o+0x18+i*REC
                w,h,cs,kind=struct.unpack_from('<IIfI',d,b)
                if kind: continue
                vs=[struct.unpack_from('<10f',d,b+0x10+j*0x28) for j in range(w*h)]
                recs.append((w,h,vs))
            return recs
        o+=size
    return recs
def main():
    recs=load(sys.argv[1]); c=float(sys.argv[2]) if len(sys.argv)>2 else 192.0
    ax=1 if (sys.argv[3:] or ['y'])[0]=='y' else 0
    # vertices sur la ligne axe==c, regroupés par (x) -> liste de (tuile, vert)
    pts={}
    for ti,(w,h,vs) in enumerate(recs):
        for v in vs:
            if abs(v[ax]-c)<1e-3: pts.setdefault(round(v[1-ax],2),[]).append((ti,v))
    nd=0;dc=0;dn=0;dz=0;mx=0;n2=0
    for k in sorted(pts):
        l=pts[k]
        if len(l)<2: continue
        n2+=1
        a,b=l[0][1],l[1][1]
        cd=max(abs(a[6+i]-b[6+i]) for i in range(4))
        nn=max(abs(a[3+i]-b[3+i]) for i in range(3))
        zz=abs(a[2]-b[2])
        dc+=cd>1e-3; dn+=nn>1e-3; dz+=zz>1e-3; mx=max(mx,cd)
        if n2<=6 or cd>0.05 and n2<40:
            print(f"pos={k} tiles={l[0][0]},{l[1][0]} z={a[2]:.3f}/{b[2]:.3f} n={tuple(round(x,3) for x in a[3:6])}/{tuple(round(x,3) for x in b[3:6])} col={tuple(round(x,3) for x in a[6:]) }/{tuple(round(x,3) for x in b[6:])}")
    print(f"paires={n2} colDiff={dc} normDiff={dn} zDiff={dz} maxColDiff={mx:.3f}; tuiles terrain={len(recs)}")
main()
