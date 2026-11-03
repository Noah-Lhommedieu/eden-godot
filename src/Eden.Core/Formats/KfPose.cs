using System.Numerics;

namespace EdenClient.World;

/// <summary>
/// Pose d'un squelette <see cref="NifFile"/> animé par des
/// <see cref="KfSequence"/> + skinning CPU (top-4 poids) — pur C#,
/// aucune dépendance moteur. Convention row-vector Gamebryo :
/// <c>monde(enfant) = local(enfant) × monde(parent)</c> et sommet
/// skinné <c>Σ w · v × SkinXf_i × monde(os_i)</c>.
/// Usage : <see cref="ResetBind"/> → <see cref="Apply"/> (1..2 séquences,
/// poids de fondu) → <see cref="Solve"/> → <see cref="Skin"/>.
/// </summary>
public sealed class KfPose
{
    private readonly NifFile _nif;
    private readonly int[] _parent;        // index nœud du parent, −1 racine
    private readonly int[] _order;         // parents avant enfants
    private readonly Dictionary<string, int> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<KfSequence, int[]> _trackNode = new();
    private readonly Vector3[] _t;
    private readonly Quaternion[] _r;
    private readonly float[] _s;

    /// <summary>Matrices monde (row-vector) par nœud, après <see cref="Solve"/>.</summary>
    public Matrix4x4[] World { get; }

    /// <summary>Pistes dont le nœud n'existe pas dans le squelette
    /// (ignorées) — diagnostic.</summary>
    public int UnmatchedTracks { get; private set; }

    public KfPose(NifFile nif)
    {
        _nif = nif;
        var n = nif.Nodes.Count;
        _parent = new int[n];
        var blockToIdx = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            blockToIdx[nif.Nodes[i].Block] = i;
            _byName.TryAdd(nif.Nodes[i].Name, i);
        }
        for (var i = 0; i < n; i++)
            _parent[i] = blockToIdx.TryGetValue(
                nif.Nodes[i].ParentBlock, out var pi) ? pi : -1;
        // Ordre topologique (les blocs enfants peuvent précéder le parent).
        var seen = new bool[n];
        var order = new List<int>(n);
        void Visit(int i)
        {
            if (seen[i]) return;
            seen[i] = true;
            if (_parent[i] >= 0) Visit(_parent[i]);
            order.Add(i);
        }
        for (var i = 0; i < n; i++) Visit(i);
        _order = order.ToArray();
        _t = new Vector3[n];
        _r = new Quaternion[n];
        _s = new float[n];
        World = new Matrix4x4[n];
        ResetBind();
        Solve();
    }

    public int NodeCount => _nif.Nodes.Count;

    /// <summary>Ordre topologique des nœuds (parents avant enfants) —
    /// indices dans <see cref="NifFile.Nodes"/>. Utilisé par le
    /// squelette <c>SkinnedModel</c> (racine d'abord).</summary>
    public int[] NodeOrder => _order;

    /// <summary>Index nœud du parent de <paramref name="i"/>, −1 racine.</summary>
    public int ParentOf(int i) => _parent[i];

    /// <summary>Index nœud par bloc nif, −1 si absent.</summary>
    public int NodeIndexOfBlock(int block) =>
        _indexByBlock.TryGetValue(block, out var i) ? i : -1;

    /// <summary>Matrice monde de bind du nœud (diagnostic).</summary>
    public Matrix4x4 BindWorldOf(int i) => _nif.Nodes[i].BindWorld;

    /// <summary>Index nœud par nom d'os, −1 si absent.</summary>
    public int Find(string name) =>
        _byName.TryGetValue(name, out var i) ? i : -1;

    /// <summary>Revient aux TRS locaux de bind du nif.</summary>
    public void ResetBind()
    {
        for (var i = 0; i < _t.Length; i++)
        {
            var nd = _nif.Nodes[i];
            _t[i] = nd.Translation;
            _r[i] = nd.Rotation;
            _s[i] = nd.Scale;
        }
    }

    /// <summary>Mélange la séquence à <paramref name="time"/> (secondes de
    /// la séquence) dans la pose courante avec <paramref name="weight"/>
    /// (1 = remplace, 0 = inchangé).</summary>
    public void Apply(KfSequence seq, float time, float weight = 1f)
    {
        if (weight <= 0f) return;
        if (!_trackNode.TryGetValue(seq, out var map))
        {
            map = new int[seq.Tracks.Count];
            for (var i = 0; i < map.Length; i++)
            {
                map[i] = Find(seq.Tracks[i].NodeName);
                if (map[i] < 0) UnmatchedTracks++;
            }
            _trackNode[seq] = map;
        }
        for (var i = 0; i < map.Length; i++)
        {
            var ni = map[i];
            if (ni < 0) continue;
            var t = _t[ni]; var r = _r[ni]; var s = _s[ni];
            seq.Tracks[i].Sample(time, ref t, ref r, ref s);
            if (weight >= 1f)
            {
                _t[ni] = t; _r[ni] = r; _s[ni] = s;
            }
            else
            {
                if (Quaternion.Dot(_r[ni], r) < 0) r = Quaternion.Negate(r);
                _t[ni] = Vector3.Lerp(_t[ni], t, weight);
                _r[ni] = Quaternion.Normalize(
                    Quaternion.Slerp(_r[ni], r, weight));
                _s[ni] += (s - _s[ni]) * weight;
            }
        }
    }

    /// <summary>Recalcule les matrices monde depuis les TRS locaux.</summary>
    public void Solve()
    {
        foreach (var i in _order)
        {
            var local = Matrix4x4.CreateScale(_s[i])
                * Matrix4x4.CreateFromQuaternion(_r[i])
                * Matrix4x4.CreateTranslation(_t[i]);
            World[i] = _parent[i] >= 0 ? local * World[_parent[i]] : local;
        }
    }

    /// <summary>Applique le skin d'un shape à la pose courante :
    /// <paramref name="outV"/>/<paramref name="outN"/> (taille = vertices).
    /// Les shapes rigides sont ignorés (retourne false).</summary>
    public bool Skin(NifShape sh, Vector3[] outV, Vector3[]? outN)
    {
        var sk = sh.Skin;
        var lv = sh.LocalVertices;
        if (sk == null || lv == null) return false;
        var nb = sk.BoneBlocks.Length;
        var m = new Matrix4x4[nb];
        for (var i = 0; i < nb; i++)
        {
            m[i] = _nif.NodeByBlock.TryGetValue(sk.BoneBlocks[i], out var nd)
                ? sk.BoneSkin[i] * World[IndexOf(nd)]
                : sk.BoneSkin[i];
        }
        var ln = sh.LocalNormals;
        for (var v = 0; v < lv.Length; v++)
        {
            var acc = Vector3.Zero;
            var nacc = Vector3.Zero;
            for (var q = 0; q < 4; q++)
            {
                var w = sk.Weights[v * 4 + q];
                if (w <= 0) continue;
                var mm = m[sk.Joints[v * 4 + q]];
                acc += w * Vector3.Transform(lv[v], mm);
                if (outN != null && ln != null)
                    nacc += w * Vector3.TransformNormal(ln[v], mm);
            }
            outV[v] = acc;
            if (outN != null)
                outN[v] = ln == null ? Vector3.UnitZ
                    : nacc.LengthSquared() > 1e-10f
                        ? Vector3.Normalize(nacc) : Vector3.UnitZ;
        }
        return true;
    }

    private int IndexOf(NifNode nd) =>
        _indexByBlock.TryGetValue(nd.Block, out var i) ? i : 0;

    private Dictionary<int, int>? _ibb;
    private Dictionary<int, int> _indexByBlock =>
        _ibb ??= _nif.Nodes.Select((n, i) => (n.Block, i))
            .ToDictionary(x => x.Block, x => x.i);
}
