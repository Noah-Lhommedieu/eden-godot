using EdenClient.World;

namespace EdenClient.Game;

/// <summary>Résultat de la résolution des cibles d'un .kf sur les nœuds
/// d'un NIF.</summary>
public sealed record BoneResolution(int Targets, int Matched,
    IReadOnlyList<string> Unmatched, IReadOnlyList<string> Ambiguous)
{
    public bool Complete => Unmatched.Count == 0;
}

/// <summary>
/// Noms d'os natifs : les pistes d'un .kf visent les nœuds du NIF
/// <b>par leur nom exact</b> (<c>NiControllerSequence</c> → bloc contrôlé
/// → « Node Name »), c'est la résolution de Gamebryo
/// (<c>NiAVObject::GetObjectByName</c>, insensible à la casse chez nous
/// comme dans <see cref="KfPose"/>). Aucun renommage ni retarget : les
/// anciens <c>AnimRetarget</c> / <c>GlbAnimFile</c> (qui passaient par
/// les noms nettoyés d'un GLB) deviennent inutiles — le <c>Skeleton3D</c>
/// natif garde les noms du NIF et l'<c>Animation</c> Godot s'y rattache
/// par <c>Skeleton3D:&lt;nom&gt;</c> (voir <see cref="TrackPath"/>).
///
/// Mesuré sur fr-full (<c>KfmNativeBoneTests</c>) : pour chaque KFM de
/// char/, ride/, house/ et monster/ (hors m264, nif absent du corpus),
/// 100 % des cibles de tous les .kf résolvent dans le NIF du KFM. Les
/// décorations (<c>decorate/*.kfm</c>) pointent toutes <c>dummy.kf</c>
/// (squelette Bip01 factice absent des props) : sans effet. Noms avec
/// doublons dans 40 NIF (surtout effets/accessoires) : on retient le
/// premier nœud (ordre des blocs) et on les signale en
/// <see cref="BoneResolution.Ambiguous"/>.
/// </summary>
public static class NativeBoneNames
{
    /// <summary>Index nom → nœud (premier rencontré gagne, insensible à
    /// la casse).</summary>
    public static Dictionary<string, int> Index(NifFile nif)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nif.Nodes.Count; i++)
            d.TryAdd(nif.Nodes[i].Name, i);
        return d;
    }

    /// <summary>Noms de cibles distincts de toutes les pistes
    /// transform d'un .kf (toutes séquences).</summary>
    public static HashSet<string> Targets(KfFile kf)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < kf.SequenceCount; i++)
            if (kf.Get(i) is { } s)
                foreach (var t in s.Tracks) set.Add(t.NodeName);
        return set;
    }

    /// <summary>Résout une liste de noms de cibles sur les nœuds du NIF.</summary>
    public static BoneResolution Resolve(IEnumerable<string> targets, NifFile nif)
    {
        var idx = Index(nif);
        var dup = new HashSet<string>(
            nif.Nodes.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);
        var un = new List<string>(); var amb = new List<string>();
        var n = 0; var ok = 0;
        foreach (var t in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            n++;
            if (idx.ContainsKey(t))
            {
                ok++;
                if (dup.Contains(t)) amb.Add(t);
            }
            else un.Add(t);
        }
        return new BoneResolution(n, ok, un, amb);
    }

    public static BoneResolution Resolve(KfFile kf, NifFile nif) =>
        Resolve(Targets(kf), nif);

    /// <summary>Un nom de nœud NIF est utilisable tel quel comme nom
    /// d'os Godot (<c>Skeleton3D.add_bone</c> refuse « : » et « / »).</summary>
    public static bool IsGodotSafe(string nifName) =>
        nifName.Length > 0 && nifName.IndexOfAny(UnsafeChars) < 0;

    private static readonly char[] UnsafeChars = { ':', '/', '\\' };

    /// <summary>Nom d'os Godot pour un nœud NIF : identique si sûr (cas de
    /// 100 % des nœuds de char/monster/ride), sinon « : / \ » → « _ ».
    /// À appliquer <b>aux deux côtés</b> (os et piste) pour rester
    /// cohérent.</summary>
    public static string GodotBoneName(string nifName)
    {
        if (IsGodotSafe(nifName)) return nifName;
        var a = nifName.ToCharArray();
        for (var i = 0; i < a.Length; i++)
            if (Array.IndexOf(UnsafeChars, a[i]) >= 0) a[i] = '_';
        return a.Length == 0 ? "_" : new string(a);
    }

    /// <summary>Chemin de piste <c>Animation</c> Godot d'un os :
    /// <c>"Skeleton3D:Bip01 L Calf"</c> ; ajouter <c>:position</c>,
    /// <c>:rotation</c> ou <c>:scale</c> n'est pas nécessaire pour les
    /// pistes POSITION_3D/ROTATION_3D/SCALE_3D (le chemin désigne l'os).</summary>
    public static string TrackPath(string skeletonPath, string nifName) =>
        skeletonPath + ":" + GodotBoneName(nifName);

    /// <summary>Chemin relatif d'un fichier cité par un KFM (« .\animation\B.kf »)
    /// ramené à un chemin relatif au dossier du KFM, séparateur « / ».
    /// La casse reste celle du KFM : résoudre ensuite avec
    /// <c>EdenVfs.ResolveInsensitive</c>.</summary>
    public static string KfmRelative(string kfmReferencedPath)
    {
        var p = kfmReferencedPath.Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p;
    }

    /// <summary>Combine le dossier d'un KFM et un chemin cité par lui.</summary>
    public static string KfmSibling(string kfmDirectory, string referenced) =>
        kfmDirectory.TrimEnd('/', '\\') + "/" + KfmRelative(referenced);
}
