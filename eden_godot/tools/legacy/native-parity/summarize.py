import sys, re, collections
rows = [l.rstrip("\n").split("\t") for l in open(sys.argv[1])][1:]
by = collections.defaultdict(collections.Counter); cls = collections.Counter(); ex = {}
for r in rows:
    kind, st, c = r[1], r[2], r[3]
    by[kind][st] += 1; by["ALL"][st] += 1
    for m in re.findall(r"([a-z-]+)\(", c):
        cls[(kind if kind in ("char","monster","ride","item") else "static", m)] += 1
        ex.setdefault((kind if kind in ("char","monster","ride","item") else "static", m), r[0])
print("%-10s %5s %5s %s" % ("kind", "n", "OK", "rate"))
for k, c in sorted(by.items()):
    n = sum(c.values()); print("%-10s %5d %5d %5.1f%%  %s" % (k, n, c["OK"], 100 * c["OK"] / max(n, 1), dict(c)))
print("\nclasses de divergence (groupe, classe): n  [exemple]")
for (g, m), n in cls.most_common(): print("  %-8s %-16s %4d  [%s]" % (g, m, n, ex[(g, m)]))
