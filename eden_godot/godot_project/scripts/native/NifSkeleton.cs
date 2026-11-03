using EdenClient.World;

namespace EdenClient.Native;

/// <summary>
/// Groupes de squelette d'un NIF : reproduit la logique de l'import glTF de Godot (un Skeleton3D par
/// ensemble d'os connectés, enraciné sur le plus haut os ; les nœuds non-os intermédiaires deviennent os).
/// Pur (sans Godot.*) : testable hors moteur.
/// </summary>
public sealed class NifSkelGroup
{
    public int RootBlock;
    /// <summary>-1 : le Skeleton3D remplace <see cref="RootBlock"/> (os racine unique). Sinon nœud
    /// non-os (plus petit ancêtre commun des os racines) qui héberge le Skeleton3D.</summary>
    public int HostBlock = -1;
    public HashSet<int> Joints = new();
    public HashSet<int> Bones = new();
    /// <summary>Os en pré-ordre (parents avant enfants).</summary>
    public List<int> Order = new();
}

public static class NifSkeleton
{
    static List<int> AncestorsInclusive(NifFile nif, int b)
    {
        var l = new List<int>();
        var guard = 0;
        while (b >= 0 && nif.AvByBlock.TryGetValue(b, out var av) && guard++ < 4096)
        {
            l.Add(b);
            b = av.ParentBlock;
        }
        return l;
    }

    static bool IsAncestorOrSelf(NifFile nif, int anc, int node) => AncestorsInclusive(nif, node).Contains(anc);

    /// <summary>Racine du squelette d'une peau (null si os introuvables ou arbres disjoints).</summary>
    static int? SkinRoot(NifFile nif, HashSet<int> joints)
    {
        var roots = joints.Where(j => !AncestorsInclusive(nif, j).Skip(1).Any(joints.Contains)).ToList();
        if (roots.Count == 1) return roots[0];
        // plus petit ancêtre commun
        var chains = roots.Select(r => AncestorsInclusive(nif, r)).ToList();
        foreach (var c in chains[0])
            if (chains.All(ch => ch.Contains(c))) return c;
        return null;
    }

    /// <summary>Groupes de squelette + groupe de chaque shape skinné (par BlockIndex).</summary>
    public static List<NifSkelGroup> Analyze(NifFile nif, out Dictionary<int, NifSkelGroup> shapeGroup)
    {
        shapeGroup = new();
        var groups = new List<NifSkelGroup>();
        var perShape = new List<(NifShape S, HashSet<int> J, int Root)>();
        foreach (var s in nif.Shapes)
        {
            if (s.Skin == null || s.Skin.BoneBlocks.Length == 0) continue;
            if (s.Skin.BoneBlocks.Any(b => !nif.AvByBlock.ContainsKey(b))) continue;
            var j = new HashSet<int>(s.Skin.BoneBlocks);
            var r = SkinRoot(nif, j);
            if (r == null) continue;
            perShape.Add((s, j, r.Value));
        }
        foreach (var (s, j, r) in perShape)
        {
            var host = groups.FirstOrDefault(g => IsAncestorOrSelf(nif, g.RootBlock, r));
            if (host == null)
            {
                host = new NifSkelGroup { RootBlock = r };
                foreach (var g in groups.Where(g => IsAncestorOrSelf(nif, r, g.RootBlock)).ToList())
                {
                    host.Joints.UnionWith(g.Joints);
                    groups.Remove(g);
                }
                groups.Add(host);
            }
            host.Joints.UnionWith(j);
        }
        foreach (var g in groups)
        {
            var roots = g.Joints.Where(j => !AncestorsInclusive(nif, j).Skip(1).Any(g.Joints.Contains)).ToList();
            if (roots.Count > 1)
            {
                var chains = roots.Select(r => AncestorsInclusive(nif, r)).ToList();
                g.HostBlock = chains[0].First(c => chains.All(ch => ch.Contains(c)));
                g.RootBlock = g.HostBlock;
            }
            else g.RootBlock = roots[0];
            foreach (var j in g.Joints)
                foreach (var a in AncestorsInclusive(nif, j))
                {
                    if (a == g.HostBlock) break;
                    g.Bones.Add(a);
                    if (a == g.RootBlock) break;
                }
            void Walk(int b)
            {
                g.Order.Add(b);
                if (nif.AvByBlock[b] is NifNode n)
                    foreach (var c in n.Children)
                        if (g.Bones.Contains(c.Block)) Walk(c.Block);
            }
            if (g.HostBlock < 0) Walk(g.RootBlock);
            else if (nif.AvByBlock[g.HostBlock] is NifNode hn)
                foreach (var c in hn.Children)
                    if (g.Bones.Contains(c.Block)) Walk(c.Block);
        }
        foreach (var (s, j, r) in perShape)
        {
            var g = groups.FirstOrDefault(g => j.IsSubsetOf(g.Joints));
            if (g != null) shapeGroup[s.BlockIndex] = g;
        }
        return groups;
    }
}
