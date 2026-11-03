import os, random, sys
root = sys.argv[1]
mdir = os.path.join(root, "eden_godot/godot_project/assets/models")
quota = [("map/model/object", 70), ("map/model", 20), ("char/model", 30), ("monster/model", 35), ("ride/model", 15),
         ("item/model", 20), ("house/model", 5), ("decorate/model", 5)]
random.seed(42); seen = set(); out = []
for sub, n in quota:
    fs = []
    for dp, _, fns in os.walk(os.path.join(mdir, sub)):
        for f in fns:
            if f.endswith(".glb"):
                rel = os.path.relpath(os.path.join(dp, f), mdir)[:-4].replace(os.sep, "/")
                if rel not in seen: fs.append(rel)
    fs.sort(); random.shuffle(fs)
    for r in fs[:n]: seen.add(r); out.append(r)
print("\n".join(out))
