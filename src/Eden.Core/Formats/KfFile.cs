using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using EdenClient.Vfs;

namespace EdenClient.World;

/// <summary>Type d'interpolation d'une clé Gamebryo (<c>KeyType</c>).</summary>
public enum KfKeyType { Linear = 1, Quadratic = 2, Tbc = 3, XyzRotation = 4, Const = 5 }

/// <summary>Mode de bouclage d'une séquence (<c>CycleType</c>).</summary>
public enum KfCycle { Loop = 0, Reverse = 1, Clamp = 2 }

/// <summary>Événement texte d'une séquence (« start », « hit »…).</summary>
public readonly record struct KfTextKey(float Time, string Value);

/// <summary>Utilitaires d'interpolation partagés par les canaux.</summary>
internal static class KfInterp
{
    /// <summary>Segment contenant <paramref name="t"/> : indice de la clé
    /// de gauche et fraction <paramref name="u"/> (0 avant la première
    /// clé, 1 après la dernière).</summary>
    public static int Seg(float[] times, float t, out float u)
    {
        u = 0;
        if (times.Length < 2 || t <= times[0]) return 0;
        var n = times.Length;
        if (t >= times[n - 1]) { u = 1; return n - 2; }
        var lo = 0; var hi = n - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) >> 1;
            if (times[mid] <= t) lo = mid; else hi = mid;
        }
        var d = times[lo + 1] - times[lo];
        u = d <= 1e-9f ? 0 : (t - times[lo]) / d;
        return lo;
    }

    /// <summary>Hermite : v0, v1 et tangentes sortante de v0 / entrante
    /// de v1 en « valeur par segment » (équivaut au Bézier cubique
    /// P0, P0+out/3, P1−in/3, P1 de Gamebryo).</summary>
    public static float Hermite(float v0, float out0, float v1, float in1, float u)
    {
        var u2 = u * u; var u3 = u2 * u;
        return (2 * u3 - 3 * u2 + 1) * v0 + (u3 - 2 * u2 + u) * out0
             + (-2 * u3 + 3 * u2) * v1 + (u3 - u2) * in1;
    }

    public static Vector3 Hermite(Vector3 v0, Vector3 out0, Vector3 v1,
        Vector3 in1, float u)
    {
        var u2 = u * u; var u3 = u2 * u;
        return (2 * u3 - 3 * u2 + 1) * v0 + (u3 - 2 * u2 + u) * out0
             + (-2 * u3 + 3 * u2) * v1 + (u3 - u2) * in1;
    }

    /// <summary>Kochanek–Bartels : tangentes sortante/entrante par clé
    /// (tension, biais, continuité) en unités « valeur par segment ».</summary>
    public static void TbcTangents(Vector3[] v, Vector3[] tbc,
        Vector3[] outT, Vector3[] inT)
    {
        var n = v.Length;
        for (var i = 0; i < n; i++)
        {
            var t = tbc[i].X; var b = tbc[i].Y; var c = tbc[i].Z;
            var prev = i > 0 ? v[i] - v[i - 1] : v[Math.Min(1, n - 1)] - v[i];
            var next = i < n - 1 ? v[i + 1] - v[i] : v[i] - v[Math.Max(0, i - 1)];
            if (i == 0) prev = next;
            if (i == n - 1) next = prev;
            outT[i] = 0.5f * ((1 - t) * (1 + b) * (1 + c) * prev
                            + (1 - t) * (1 - b) * (1 - c) * next);
            inT[i] = 0.5f * ((1 - t) * (1 + b) * (1 - c) * prev
                           + (1 - t) * (1 - b) * (1 + c) * next);
        }
    }

    /// <summary>Produit de Hamilton (a·b).</summary>
    public static Quaternion Mul(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quaternion Conj(Quaternion q) => new(-q.X, -q.Y, -q.Z, q.W);

    public static Vector3 QLog(Quaternion q)
    {
        var v = new Vector3(q.X, q.Y, q.Z);
        var s = v.Length();
        return s < 1e-8f ? Vector3.Zero : v * (MathF.Atan2(s, q.W) / s);
    }

    public static Quaternion QExp(Vector3 v)
    {
        var a = v.Length();
        if (a < 1e-8f) return Quaternion.Identity;
        var s = MathF.Sin(a) / a;
        return new Quaternion(v.X * s, v.Y * s, v.Z * s, MathF.Cos(a));
    }

    public static Quaternion SlerpShort(Quaternion a, Quaternion b, float u)
    {
        if (Quaternion.Dot(a, b) < 0) b = Quaternion.Negate(b);
        return Quaternion.Normalize(Quaternion.Slerp(a, b, u));
    }
}

/// <summary>
/// B-spline cubique ouverte uniforme (de Boor) sur <c>[Start, Stop]</c> —
/// <c>NiBSpline*Interpolator</c> (+Data/BasisData). Évaluation exacte et
/// sans allocation ; remplace le ré-échantillonnage 15 Hz de l'oracle.
/// </summary>
public sealed class KfBSpline
{
    private const int Deg = 3;
    private readonly float[] _cp;           // ncp × dim, déjà décompressé
    private readonly double[] _knots;
    public int Ncp { get; }
    public int Dim { get; }
    public float Start { get; }
    public float Stop { get; }

    public KfBSpline(float[] cp, int ncp, int dim, float start, float stop)
    {
        _cp = cp; Ncp = ncp; Dim = dim; Start = start; Stop = stop;
        _knots = new double[ncp + Deg + 1];
        var interior = ncp - Deg - 1;
        for (var i = 0; i < interior; i++)
            _knots[Deg + 1 + i] = (i + 1.0) / (interior + 1.0);
        for (var i = ncp; i < _knots.Length; i++) _knots[i] = 1.0;
    }

    /// <summary>Évalue au temps de séquence <paramref name="t"/> (clampé
    /// à [Start, Stop]) ; <paramref name="result"/> a au moins Dim cases.</summary>
    public void Eval(float t, Span<float> result)
    {
        var d = Stop - Start;
        var u = d <= 1e-9f ? 0f : Math.Clamp((t - Start) / d, 0f, 1f);
        EvalU(u, result);
    }

    /// <summary>Évalue au paramètre normalisé u∈[0,1].</summary>
    public void EvalU(float u, Span<float> result)
    {
        double uu = Math.Clamp(u, 0f, 1f);
        int k;
        if (uu >= 1.0) k = Ncp - 1;
        else
        {
            var idx = 0;
            while (idx < _knots.Length && _knots[idx] <= uu) idx++;
            k = Math.Max(Deg, idx - 1);
        }
        Span<double> dd = stackalloc double[(Deg + 1) * 4];
        for (var j = 0; j <= Deg; j++)
            for (var c = 0; c < Dim; c++)
                dd[j * 4 + c] = _cp[(k - Deg + j) * Dim + c];
        for (var r = 1; r <= Deg; r++)
            for (var j = Deg; j >= r; j--)
            {
                var i = k - Deg + j;
                var span = _knots[i + Deg + 1 - r] - _knots[i];
                var a = span == 0 ? 0.0 : (uu - _knots[i]) / span;
                for (var c = 0; c < Dim; c++)
                    dd[j * 4 + c] = (1 - a) * dd[(j - 1) * 4 + c] + a * dd[j * 4 + c];
            }
        for (var c = 0; c < Dim; c++) result[c] = (float)dd[Deg * 4 + c];
    }
}

/// <summary>Canal scalaire (échelle, alpha, vitesse d'émetteur, booléen) :
/// clés Linear/Quadratic/TBC/Const ou B-spline, ou valeur constante.</summary>
public sealed class KfFloatChan
{
    public float[] Times = Array.Empty<float>();
    public float[] V = Array.Empty<float>();
    /// <summary>Tangentes sortante / entrante (Quadratic et TBC).</summary>
    public float[]? T0, T1;
    public KfKeyType Type = KfKeyType.Linear;
    public KfBSpline? Spline;

    public bool IsEmpty => Spline == null && V.Length == 0;
    public bool IsConstant => Spline == null && V.Length <= 1;
    /// <summary>Vrai si les clés sont interpolées linéairement (la
    /// cuisson peut alors garder les clés exactes).</summary>
    public bool IsLinearKeys => Spline == null &&
        (Type is KfKeyType.Linear || V.Length <= 1);
    public float[] KeyTimes => Spline == null ? Times : Array.Empty<float>();

    public float Eval(float t)
    {
        if (Spline != null)
        {
            Span<float> r = stackalloc float[1];
            Spline.Eval(t, r);
            return r[0];
        }
        if (V.Length == 0) return 0;
        if (V.Length == 1) return V[0];
        var i = KfInterp.Seg(Times, t, out var u);
        switch (Type)
        {
            case KfKeyType.Const:
                return u >= 1f ? V[i + 1] : V[i];
            case KfKeyType.Quadratic or KfKeyType.Tbc when T0 != null && T1 != null:
                return KfInterp.Hermite(V[i], T0[i], V[i + 1], T1[i + 1], u);
            default:
                return V[i] + (V[i + 1] - V[i]) * u;
        }
    }
}

/// <summary>Canal Vector3 (position, couleur) : clés ou B-spline.</summary>
public sealed class KfVec3Chan
{
    public float[] Times = Array.Empty<float>();
    public Vector3[] V = Array.Empty<Vector3>();
    public Vector3[]? T0, T1;
    public KfKeyType Type = KfKeyType.Linear;
    public KfBSpline? Spline;

    public bool IsEmpty => Spline == null && V.Length == 0;
    public bool IsConstant => Spline == null && V.Length <= 1;
    public bool IsLinearKeys => Spline == null &&
        (Type is KfKeyType.Linear || V.Length <= 1);
    public float[] KeyTimes => Spline == null ? Times : Array.Empty<float>();

    public Vector3 Eval(float t)
    {
        if (Spline != null)
        {
            Span<float> r = stackalloc float[3];
            Spline.Eval(t, r);
            return new Vector3(r[0], r[1], r[2]);
        }
        if (V.Length == 0) return Vector3.Zero;
        if (V.Length == 1) return V[0];
        var i = KfInterp.Seg(Times, t, out var u);
        switch (Type)
        {
            case KfKeyType.Const:
                return u >= 1f ? V[i + 1] : V[i];
            case KfKeyType.Quadratic or KfKeyType.Tbc when T0 != null && T1 != null:
                return KfInterp.Hermite(V[i], T0[i], V[i + 1], T1[i + 1], u);
            default:
                return Vector3.Lerp(V[i], V[i + 1], u);
        }
    }
}

/// <summary>Canal rotation : quaternions (slerp / squad), trois courbes
/// d'Euler XYZ ou B-spline.</summary>
public sealed class KfRotChan
{
    public float[] Times = Array.Empty<float>();
    public Quaternion[] Q = Array.Empty<Quaternion>();
    /// <summary>Points de contrôle de squad (Quadratic / TBC).</summary>
    public Quaternion[]? Ctl;
    public KfKeyType Type = KfKeyType.Linear;
    /// <summary>Courbes d'angles X, Y, Z (rad) si <see cref="Type"/> =
    /// XyzRotation.</summary>
    public KfFloatChan[]? Xyz;
    public KfBSpline? Spline;

    public bool IsEmpty => Spline == null && Xyz == null && Q.Length == 0;
    public bool IsConstant => Spline == null && Xyz == null && Q.Length <= 1;
    public bool IsLinearKeys => Spline == null && Xyz == null &&
        (Type is KfKeyType.Linear || Q.Length <= 1);

    /// <summary>Temps des clés (union des trois axes en XYZ ; vide pour
    /// une spline).</summary>
    public float[] KeyTimes => Spline != null ? Array.Empty<float>()
        : Xyz != null ? Xyz.SelectMany(a => a.Times).Distinct().Order().ToArray()
        : Times;

    public Quaternion Eval(float t)
    {
        if (Spline != null)
        {
            Span<float> r = stackalloc float[4];
            Spline.Eval(t, r);
            var q = new Quaternion(r[1], r[2], r[3], r[0]);   // nif : w,x,y,z
            return q.LengthSquared() > 1e-12f
                ? Quaternion.Normalize(q) : Quaternion.Identity;
        }
        if (Xyz != null)
        {
            var (sx, cx) = MathF.SinCos(Xyz[0].Eval(t) / 2);
            var (sy, cy) = MathF.SinCos(Xyz[1].Eval(t) / 2);
            var (sz, cz) = MathF.SinCos(Xyz[2].Eval(t) / 2);
            // composition qz·qy·qx (vérifiée sur B.kf par l'oracle : 35/35 os)
            return Quaternion.Normalize(KfInterp.Mul(KfInterp.Mul(
                new Quaternion(0, 0, sz, cz), new Quaternion(0, sy, 0, cy)),
                new Quaternion(sx, 0, 0, cx)));
        }
        if (Q.Length == 0) return Quaternion.Identity;
        if (Q.Length == 1) return Q[0];
        var i = KfInterp.Seg(Times, t, out var u);
        if (Type == KfKeyType.Const) return u >= 1f ? Q[i + 1] : Q[i];
        var a = Q[i]; var b = Q[i + 1];
        if (Ctl != null && Type is KfKeyType.Quadratic or KfKeyType.Tbc)
        {
            // squad : slerp(slerp(a,b,u), slerp(sa,sb,u), 2u(1−u))
            var s1 = KfInterp.SlerpShort(a, b, u);
            var s2 = KfInterp.SlerpShort(Ctl[i], Ctl[i + 1], u);
            return KfInterp.SlerpShort(s1, s2, 2 * u * (1 - u));
        }
        return KfInterp.SlerpShort(a, b, u);
    }

    /// <summary>Calcule les points de contrôle squad (Shoemake) de clés
    /// en continuité d'hémisphère.</summary>
    public void BuildSquad()
    {
        var n = Q.Length;
        for (var i = 1; i < n; i++)
            if (Quaternion.Dot(Q[i - 1], Q[i]) < 0) Q[i] = Quaternion.Negate(Q[i]);
        Ctl = new Quaternion[n];
        for (var i = 0; i < n; i++)
        {
            var qi = Q[i];
            var ci = KfInterp.Conj(qi);
            var next = i < n - 1 ? KfInterp.QLog(KfInterp.Mul(ci, Q[i + 1])) : Vector3.Zero;
            var prev = i > 0 ? KfInterp.QLog(KfInterp.Mul(ci, Q[i - 1])) : Vector3.Zero;
            Ctl[i] = Quaternion.Normalize(KfInterp.Mul(qi,
                KfInterp.QExp(-(next + prev) / 4f)));
        }
    }
}

/// <summary>
/// Piste d'un os : canaux position / rotation / échelle (clés exactes,
/// splines B évaluées exactement, rotations XYZ en courbes d'Euler).
/// Canal absent = le bind du nœud est conservé.
/// </summary>
public sealed class KfTrack
{
    public string NodeName = "";
    public KfVec3Chan? Pos;
    public KfRotChan? Rot;
    public KfFloatChan? Scl;

    public bool HasPos => Pos is { IsEmpty: false };
    public bool HasRot => Rot is { IsEmpty: false };
    public bool HasScl => Scl is { IsEmpty: false };

    /// <summary>Échantillonne à <paramref name="t"/> ; les canaux
    /// absents laissent les valeurs passées inchangées.</summary>
    public void Sample(float t, ref Vector3 pos, ref Quaternion rot,
        ref float scale)
    {
        if (HasPos) pos = Pos!.Eval(t);
        if (HasRot) rot = Rot!.Eval(t);
        if (HasScl) scale = Scl!.Eval(t);
    }
}

/// <summary>Types de valeur d'une piste auxiliaire.</summary>
public enum KfAuxKind { Float, Bool, Point3 }

/// <summary>
/// Piste non-transform (<c>NiFloatInterpolator</c>, <c>NiBool…</c>,
/// <c>NiPoint3…</c>, splines compactes Float/Point3) : alpha, translation
/// de texture, couleur de matériau, paramètres d'émetteur de particules,
/// morph. <see cref="Var1"/> / <see cref="Var2"/> désignent la propriété
/// animée (« TT_TRANSLATE_U », « Diffuse »…).
/// </summary>
public sealed class KfAuxTrack
{
    public string NodeName = "";
    public string PropertyType = "";
    public string ControllerType = "";
    public string Var1 = "";
    public string Var2 = "";
    public KfAuxKind Kind;
    public KfFloatChan? Float;      // Float et Bool (valeur 0/1)
    public KfVec3Chan? Vec;         // Point3

    public float SampleFloat(float t) => Float?.Eval(t) ?? 0f;
    public bool SampleBool(float t) => SampleFloat(t) >= 0.5f;
    public Vector3 SampleVec(float t) => Vec?.Eval(t) ?? Vector3.Zero;
}

/// <summary>Piste cuite : tableaux denses prêts pour une
/// <c>Animation</c> Godot ; temps en secondes depuis le début de la
/// séquence. Un canal constant tient en une clé à t=0.</summary>
public sealed class KfBakedTrack
{
    public string NodeName = "";
    public float[] PosTimes = Array.Empty<float>();
    public Vector3[] Pos = Array.Empty<Vector3>();
    public float[] RotTimes = Array.Empty<float>();
    public Quaternion[] Rot = Array.Empty<Quaternion>();
    public float[] SclTimes = Array.Empty<float>();
    public float[] Scl = Array.Empty<float>();
    /// <summary>Erreur max (cuisson interpolée linéairement vs
    /// évaluateur, mesurée à 4× la fréquence de cuisson) : unités de
    /// position, radians de rotation, échelle.</summary>
    public float MaxPosError, MaxRotError, MaxSclError;
}

/// <summary>Séquence cuite (voir <see cref="KfSequence.Bake"/>).</summary>
public sealed class KfBakedSequence
{
    public string Name = "";
    public float Duration;
    public KfCycle Cycle;
    public float Frequency = 1f;
    public float Hz;
    public List<KfBakedTrack> Tracks { get; } = new();
    /// <summary>Text keys, temps relatifs au début de la séquence.</summary>
    public List<KfTextKey> TextKeys { get; } = new();
    public float MaxPosError, MaxRotError, MaxSclError;
}

/// <summary>En-tête d'une séquence, lu sans décoder les pistes (peu
/// coûteux : sert aux machines d'états).</summary>
public readonly record struct KfSequenceInfo(string Name, KfCycle Cycle,
    float Frequency, float Start, float Stop, int ControlledBlocks)
{
    public float Duration => Math.Max(Stop - Start, 0f);
}

/// <summary>Séquence d'animation (<c>NiControllerSequence</c>).</summary>
public sealed class KfSequence
{
    public string Name = "";
    public float Start, Stop, Frequency = 1f, Weight = 1f;
    public KfCycle Cycle;
    public string AccumRoot = "";
    public List<KfTrack> Tracks { get; } = new();
    public List<KfAuxTrack> AuxTracks { get; } = new();
    public List<KfTextKey> TextKeys { get; } = new();

    /// <summary>Durée en secondes.</summary>
    public float Duration => Math.Max(Stop - Start, 0f);

    /// <summary>Ramène un temps de lecture (s depuis le début) dans
    /// [Start, Stop] selon le cycle.</summary>
    public float Wrap(float elapsed)
    {
        var d = Duration;
        if (d <= 1e-6f) return Start;
        var t = elapsed * Frequency;
        switch (Cycle)
        {
            case KfCycle.Loop:
                t %= d; if (t < 0) t += d; break;
            case KfCycle.Reverse:
                t %= 2 * d; if (t < 0) t += 2 * d;
                if (t > d) t = 2 * d - t; break;
            default:
                t = Math.Clamp(t, 0, d); break;
        }
        return Start + t;
    }

    // ----------------------------- cuisson -----------------------------

    /// <summary>Cuit la séquence en tableaux denses par os : clés exactes
    /// quand le canal est linéaire par clés, sinon grille régulière à
    /// <paramref name="hz"/> (TBC, quadratique, B-spline, Euler XYZ).
    /// Rotations en continuité d'hémisphère. Avec
    /// <paramref name="measure"/>, mesure l'erreur max contre
    /// l'évaluateur (à 4× <paramref name="hz"/>).</summary>
    public KfBakedSequence Bake(float hz = 30f, bool measure = true)
    {
        var res = new KfBakedSequence
        {
            Name = Name, Duration = Duration, Cycle = Cycle,
            Frequency = Frequency, Hz = hz,
        };
        foreach (var k in TextKeys)
            res.TextKeys.Add(new KfTextKey(k.Time - Start, k.Value));
        foreach (var tr in Tracks)
        {
            var b = BakeTrack(tr, hz, measure);
            res.Tracks.Add(b);
            res.MaxPosError = MathF.Max(res.MaxPosError, b.MaxPosError);
            res.MaxRotError = MathF.Max(res.MaxRotError, b.MaxRotError);
            res.MaxSclError = MathF.Max(res.MaxSclError, b.MaxSclError);
        }
        return res;
    }

    private static float[] Grid(float dur, float hz)
    {
        if (dur <= 1e-6f) return new[] { 0f };
        var n = Math.Max(1, (int)MathF.Ceiling(dur * hz - 1e-3f));
        var g = new float[n + 1];
        for (var i = 0; i < n; i++) g[i] = i / hz;
        g[n] = dur;
        return g;
    }

    /// <summary>Temps relatifs des clés exactes dans [0, dur], bornes
    /// incluses.</summary>
    private static float[] Exact(float[] keyTimes, float start, float dur)
    {
        var l = new List<float>(keyTimes.Length + 2) { 0f };
        foreach (var kt in keyTimes)
        {
            var r = kt - start;
            if (r > l[^1] + 1e-6f && r < dur - 1e-6f) l.Add(r);
        }
        if (dur > 1e-6f) l.Add(dur);
        return l.ToArray();
    }

    /// <summary>Grille régulière fusionnée avec les temps de clés (les
    /// discontinuités restent à leur place exacte).</summary>
    private float[] GridWithKeys(float[] grid, float[] keyTimes)
    {
        if (keyTimes.Length == 0) return grid;
        var dur = Duration;
        var l = new List<float>(grid);
        foreach (var kt in keyTimes)
        {
            var r = kt - Start;
            if (r > 1e-5f && r < dur - 1e-5f) l.Add(r);
        }
        l.Sort();
        var o = new List<float>(l.Count) { l[0] };
        foreach (var t in l)
            if (t - o[^1] > 1e-5f) o.Add(t);
        if (dur - o[^1] > 1e-5f) o.Add(dur);
        else o[^1] = dur;
        return o.ToArray();
    }

    /// <summary>Tolérances de raffinement adaptatif (cuisson des canaux
    /// non linéaires) : position (unités monde), rotation (rad), échelle.
    /// Un segment dont le milieu s'écarte plus que la tolérance reçoit un
    /// échantillon supplémentaire (jusqu'à 1/240 s).</summary>
    public const float PosTol = 0.005f, RotTol = 0.005f, SclTol = 0.002f;
    private const float MinStep = 1f / 240f;

    /// <summary>Insère les milieux de segments mal approchés par
    /// l'interpolation linéaire des échantillons voisins.</summary>
    private static void Refine<T>(List<float> ts, List<T> vs, Func<float, T> truth,
        Func<T, T, float, T> lerp, Func<T, T, float> dist, float tol)
    {
        for (var i = 0; i + 1 < ts.Count;)
        {
            var dt = ts[i + 1] - ts[i];
            if (dt > MinStep * 1.5f)
            {
                var tm = ts[i] + dt * 0.5f;
                var vm = truth(tm);
                if (dist(vm, lerp(vs[i], vs[i + 1], 0.5f)) > tol)
                {
                    ts.Insert(i + 1, tm); vs.Insert(i + 1, vm);
                    continue;                // ré-examine la moitié gauche
                }
            }
            i++;
        }
    }

    private KfBakedTrack BakeTrack(KfTrack tr, float hz, bool measure)
    {
        var dur = Duration;
        var b = new KfBakedTrack { NodeName = tr.NodeName };
        var grid = Grid(dur, hz);
        const float eps = 1e-6f;

        if (tr.HasPos)
        {
            var c = tr.Pos!;
            Func<float, Vector3> truth = t => c.Eval(Start + t);
            var tl = (c.IsConstant ? new[] { 0f }
                : c.IsLinearKeys ? Exact(c.Times, Start, dur)
                : GridWithKeys(grid, c.KeyTimes)).ToList();
            var vl = tl.Select(truth).ToList();
            if (!c.IsConstant && !c.IsLinearKeys)
                Refine(tl, vl, truth, Vector3.Lerp, (x, y) => (x - y).Length(), PosTol);
            if (vl.All(v => (v - vl[0]).LengthSquared() < eps * eps))
            { tl = new List<float> { 0f }; vl = new List<Vector3> { vl[0] }; }
            var ts = tl.ToArray(); var vs = vl.ToArray();
            b.PosTimes = ts; b.Pos = vs;
            if (measure && ts.Length > 1)
                b.MaxPosError = Measure(dur, hz, truth,
                    t => LerpAt(ts, vs, t), (x, y) => (x - y).Length());
        }
        if (tr.HasRot)
        {
            var c = tr.Rot!;
            Func<float, Quaternion> truth = t => c.Eval(Start + t);
            var tl = (c.IsConstant ? new[] { 0f }
                : c.IsLinearKeys ? Exact(c.Times, Start, dur)
                : GridWithKeys(grid, c.KeyTimes)).ToList();
            var vl = tl.Select(truth).ToList();
            if (!c.IsConstant && !c.IsLinearKeys)
                Refine(tl, vl, truth, KfInterp.SlerpShort, AngleBetween, RotTol);
            for (var i = 1; i < vl.Count; i++)
                if (Quaternion.Dot(vl[i - 1], vl[i]) < 0)
                    vl[i] = Quaternion.Negate(vl[i]);
            if (vl.All(v => 1f - MathF.Abs(Quaternion.Dot(v, vl[0])) < 1e-9f))
            { tl = new List<float> { 0f }; vl = new List<Quaternion> { vl[0] }; }
            var ts = tl.ToArray(); var vs = vl.ToArray();
            b.RotTimes = ts; b.Rot = vs;
            if (measure && ts.Length > 1)
                b.MaxRotError = Measure(dur, hz, truth,
                    t => SlerpAt(ts, vs, t), AngleBetween);
        }
        if (tr.HasScl)
        {
            var c = tr.Scl!;
            Func<float, float> truth = t => c.Eval(Start + t);
            var tl = (c.IsConstant ? new[] { 0f }
                : c.IsLinearKeys ? Exact(c.Times, Start, dur)
                : GridWithKeys(grid, c.KeyTimes)).ToList();
            var vl = tl.Select(truth).ToList();
            if (!c.IsConstant && !c.IsLinearKeys)
                Refine(tl, vl, truth, (x, y, u) => x + (y - x) * u,
                    (x, y) => MathF.Abs(x - y), SclTol);
            if (vl.All(v => MathF.Abs(v - vl[0]) < eps))
            { tl = new List<float> { 0f }; vl = new List<float> { vl[0] }; }
            var ts = tl.ToArray(); var vs = vl.ToArray();
            b.SclTimes = ts; b.Scl = vs;
            if (measure && ts.Length > 1)
                b.MaxSclError = Measure(dur, hz, truth,
                    t => { var i = KfInterp.Seg(ts, t, out var u);
                           return vs[i] + (vs[i + 1] - vs[i]) * u; },
                    (x, y) => MathF.Abs(x - y));
        }
        return b;
    }

    private static float Measure<T>(float dur, float hz, Func<float, T> truth,
        Func<float, T> baked, Func<T, T, float> dist)
    {
        var n = Math.Max(1, (int)MathF.Ceiling(dur * hz * 4f));
        var m = 0f;
        for (var i = 0; i <= n; i++)
        {
            var t = dur * i / n;
            m = MathF.Max(m, dist(truth(t), baked(t)));
        }
        return m;
    }

    private static Vector3 LerpAt(float[] ts, Vector3[] vs, float t)
    {
        if (vs.Length == 1) return vs[0];
        var i = KfInterp.Seg(ts, t, out var u);
        return Vector3.Lerp(vs[i], vs[i + 1], u);
    }

    private static Quaternion SlerpAt(float[] ts, Quaternion[] vs, float t)
    {
        if (vs.Length == 1) return vs[0];
        var i = KfInterp.Seg(ts, t, out var u);
        return KfInterp.SlerpShort(vs[i], vs[i + 1], u);
    }

    private static float AngleBetween(Quaternion a, Quaternion b) =>
        2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));
}

/// <summary>
/// Lecteur <c>.kf</c> Gamebryo 20.x (<c>NiControllerSequence</c>) —
/// header + table des blocs (tailles déclarées) puis décodage
/// <b>paresseux</b> d'une séquence à la demande (g.kf = 40 Mo / 383
/// séquences). Interpolateurs : <c>NiTransformInterpolator</c> +
/// <c>NiTransformData</c> (5 types de clés, rotations quaternion/XYZ),
/// <c>NiBSplineComp/TransformInterpolator</c> (+Data/BasisData),
/// <c>NiPathInterpolator</c> (cuit en position), et en pistes auxiliaires
/// <c>NiFloat/Bool/Point3Interpolator</c> et
/// <c>NiBSplineCompFloat/Point3Interpolator</c>. Spec : nif.xml +
/// <c>tools/nif2gltf/convert.py</c>. Jamais d'exception sur bloc
/// inconnu : ignoré (warning). Chaque bloc décodé est recoupé avec sa
/// taille déclarée (warning si l'écart).
/// </summary>
public sealed class KfFile
{
    private byte[] _d = Array.Empty<byte>();
    private string[] _strings = Array.Empty<string>();
    private string[] _types = Array.Empty<string>();
    private int[] _typeOf = Array.Empty<int>();
    private int[] _off = Array.Empty<int>();
    private int[] _size = Array.Empty<int>();
    private readonly List<int> _seqBlocks = new();

    /// <summary>Version encodée (0x14030100…).</summary>
    public uint Version { get; private set; }
    /// <summary>Avertissements de décodage.</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>Défauts constatés dans les données elles-mêmes (valeurs
    /// NaN d'origine, neutralisées au décodage) — pas des erreurs de
    /// lecture.</summary>
    public List<string> DataDefects { get; } = new();
    /// <summary>Noms des séquences, dans l'ordre fichier (index KFM).</summary>
    public List<string> SequenceNames { get; } = new();

    public int SequenceCount => _seqBlocks.Count;

    /// <summary>Comptes de blocs par type (diagnostic, couverture).</summary>
    public Dictionary<string, int> BlockTypeCounts()
    {
        var r = new Dictionary<string, int>();
        for (var i = 0; i < _typeOf.Length; i++)
        {
            var t = TypeOf(i);
            r[t] = r.GetValueOrDefault(t) + 1;
        }
        return r;
    }

    public static KfFile Load(string path) => Parse(
        EdenVfs.ReadAllBytes(path) ?? throw new FileNotFoundException(path));

    public static KfFile Parse(byte[] data)
    {
        var f = new KfFile { _d = data };
        var nl = Array.IndexOf<byte>(data, (byte)'\n');
        if (nl < 0 || !Encoding.Latin1.GetString(data, 0, nl)
                .Contains("File Format"))
            throw new InvalidDataException("Pas un NIF/KF");
        var p = nl + 1;
        f.Version = U32(data, ref p);
        if (data[p++] != 1)
            throw new InvalidDataException("big-endian non supporté");
        U32(data, ref p);                   // user version
        var nb = (int)U32(data, ref p);
        var nt = U16(data, ref p);
        f._types = new string[nt];
        for (var i = 0; i < nt; i++)
        {
            var len = (int)U32(data, ref p);
            f._types[i] = Encoding.Latin1.GetString(data, p, len);
            p += len;
        }
        f._typeOf = new int[nb];
        for (var i = 0; i < nb; i++) f._typeOf[i] = U16(data, ref p);
        f._size = new int[nb];
        for (var i = 0; i < nb; i++) f._size[i] = (int)U32(data, ref p);
        var ns = (int)U32(data, ref p);
        U32(data, ref p);                   // max string len
        f._strings = new string[ns];
        for (var i = 0; i < ns; i++)
        {
            var len = (int)U32(data, ref p);
            f._strings[i] = Encoding.Latin1.GetString(data, p, len);
            p += len;
        }
        var ng = (int)U32(data, ref p);
        p += 4 * ng;
        f._off = new int[nb];
        for (var i = 0; i < nb; i++)
        {
            f._off[i] = p;
            p += f._size[i];
        }
        if (p > data.Length)
            f.Warnings.Add($"fichier tronqué : blocs jusqu'à {p}, taille {data.Length}");
        for (var i = 0; i < nb; i++)
            if (f.TypeOf(i) == "NiControllerSequence")
            {
                f._seqBlocks.Add(i);
                var q = f._off[i];
                f.SequenceNames.Add(f.Str(U32(data, ref q)));
            }
        return f;
    }

    /// <summary>Séquence par nom (insensible à la casse) ou null.</summary>
    public KfSequence? Get(string name)
    {
        var i = SequenceNames.FindIndex(
            n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : Get(i);
    }

    /// <summary>Séquence par index fichier (= index d'anim KFM) ; null
    /// (avec un warning) si le bloc est corrompu.</summary>
    public KfSequence? Get(int index)
    {
        if (index < 0 || index >= _seqBlocks.Count) return null;
        try { return DecodeSequence(_seqBlocks[index]); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException
                                      or IndexOutOfRangeException)
        {
            Warnings.Add($"séquence {index} : {e.Message}");
            return null;
        }
    }

    /// <summary>En-tête de la séquence <paramref name="index"/> sans
    /// décoder ses pistes ; null si l'index est hors borne.</summary>
    public KfSequenceInfo? Info(int index)
    {
        if (index < 0 || index >= _seqBlocks.Count) return null;
        var d = _d;
        var p = _off[_seqBlocks[index]];
        var name = Str(U32(d, ref p));
        var ncb = (int)U32(d, ref p);
        p += 4 + 28 * ncb;                  // array grow by + blocs
        p += 4;                             // weight
        p += 4;                             // text keys
        var cycle = (KfCycle)U32(d, ref p);
        var freq = F32(d, ref p);
        var start = F32(d, ref p);
        var stop = F32(d, ref p);
        return new KfSequenceInfo(name, cycle, freq, start, stop, ncb);
    }

    // ------------------------------------------------------------------

    private string TypeOf(int block) => _types[_typeOf[block]];

    private string Str(uint i) =>
        i == 0xFFFFFFFF || i >= _strings.Length ? "" : _strings[i];

    private static uint U32(byte[] d, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p, 4));
        p += 4; return v;
    }

    private static int I32(byte[] d, ref int p) => (int)U32(d, ref p);

    private static ushort U16(byte[] d, ref int p)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p, 2));
        p += 2; return v;
    }

    private static float F32(byte[] d, ref int p)
    {
        var v = BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(p, 4));
        p += 4; return v;
    }

    /// <summary>Recoupe la fin de lecture avec la taille déclarée.</summary>
    private void CheckEnd(int block, int p)
    {
        var end = _off[block] + _size[block];
        if (p != end)
            Warnings.Add($"bloc {block} {TypeOf(block)} : lu {p - _off[block]} octets, déclaré {_size[block]}");
    }

    private bool IsType(int block, string type) =>
        block >= 0 && block < _off.Length && TypeOf(block) == type;

    private KfSequence DecodeSequence(int block)
    {
        var d = _d;
        var p = _off[block];
        var seq = new KfSequence { Name = Str(U32(d, ref p)) };
        var ncb = (int)U32(d, ref p);
        U32(d, ref p);                      // array grow by
        var cbs = new (int interp, string node, string prop, string ctype,
            string v1, string v2)[ncb];
        for (var i = 0; i < ncb; i++)
        {
            var interp = I32(d, ref p);
            I32(d, ref p);                  // controller (absent en .kf)
            var node = Str(U32(d, ref p));
            var prop = Str(U32(d, ref p));  // property type
            var ctype = Str(U32(d, ref p));
            var v1 = Str(U32(d, ref p));    // controller id
            var v2 = Str(U32(d, ref p));    // interpolator id
            cbs[i] = (interp, node, prop, ctype, v1, v2);
        }
        seq.Weight = F32(d, ref p);
        var textKeys = I32(d, ref p);
        seq.Cycle = (KfCycle)U32(d, ref p);
        seq.Frequency = F32(d, ref p);
        seq.Start = F32(d, ref p);
        seq.Stop = F32(d, ref p);
        I32(d, ref p);                      // manager ptr
        seq.AccumRoot = Str(U32(d, ref p));
        if (Version >= 0x14030000)
            U32(d, ref p);                  // champ 20.3 (0 ou 64), sans effet
        CheckEnd(block, p);

        if (IsType(textKeys, "NiTextKeyExtraData"))
            DecodeTextKeys(textKeys, seq);

        foreach (var cb in cbs)
        {
            if (cb.interp < 0 || cb.interp >= _off.Length) continue;
            var itype = TypeOf(cb.interp);
            if (itype is "NiTransformInterpolator"
                or "NiBSplineCompTransformInterpolator"
                or "NiBSplineTransformInterpolator"
                or "NiPathInterpolator")
            {
                var tr = DecodeTransform(cb.interp, cb.node, seq);
                if (tr != null) seq.Tracks.Add(tr);
                continue;
            }
            var aux = DecodeAux(cb.interp, itype, cb.node);
            if (aux == null)
            {
                Warnings.Add("interpolateur ignoré : " + itype);
                continue;
            }
            aux.PropertyType = cb.prop; aux.ControllerType = cb.ctype;
            aux.Var1 = cb.v1; aux.Var2 = cb.v2;
            seq.AuxTracks.Add(aux);
        }
        return seq;
    }

    private void DecodeTextKeys(int block, KfSequence seq)
    {
        var p = _off[block];
        U32(_d, ref p);                     // name
        var n = (int)U32(_d, ref p);
        for (var i = 0; i < n; i++)
        {
            var t = F32(_d, ref p);
            seq.TextKeys.Add(new KfTextKey(t, Str(U32(_d, ref p))));
        }
        CheckEnd(block, p);
    }

    // ------------------------- pistes transform ------------------------

    private KfTrack? DecodeTransform(int block, string node, KfSequence seq)
    {
        var type = TypeOf(block);
        var d = _d;
        var p = _off[block];
        var tr = new KfTrack { NodeName = node };
        if (type == "NiTransformInterpolator")
        {
            var (bt, bq, bs) = QuatTransform(ref p);
            var data = I32(d, ref p);
            CheckEnd(block, p);
            // Les canaux sans clés gardent la valeur constante de
            // l'interpolateur (si valide), les autres sont écrasés.
            SetConst(tr, bt, bq, bs);
            if (IsType(data, "NiTransformData"))
                DecodeTransformData(data, tr);
            return tr;
        }
        if (type == "NiPathInterpolator")
        {
            DecodePath(block, tr, seq);
            return tr;
        }
        DecodeSpline(block, type == "NiBSplineCompTransformInterpolator", tr);
        return tr;
    }

    private (Vector3 t, Quaternion q, float s) QuatTransform(ref int p)
    {
        var d = _d;
        var t = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
        var w = F32(d, ref p); var x = F32(d, ref p);
        var y = F32(d, ref p); var z = F32(d, ref p);
        var s = F32(d, ref p);
        return (t, new Quaternion(x, y, z, w), s);
    }

    private static void SetConst(KfTrack tr, Vector3 t, Quaternion q, float s)
    {
        const float Invalid = 1e30f;
        if (MathF.Abs(t.X) < Invalid && MathF.Abs(t.Y) < Invalid &&
            MathF.Abs(t.Z) < Invalid)
            tr.Pos = new KfVec3Chan { Times = new[] { 0f }, V = new[] { t } };
        if (MathF.Abs(q.X) < Invalid && q.LengthSquared() > 0.5f)
            tr.Rot = new KfRotChan
            {
                Times = new[] { 0f }, Q = new[] { Quaternion.Normalize(q) },
            };
        if (MathF.Abs(s) < Invalid)
            tr.Scl = new KfFloatChan { Times = new[] { 0f }, V = new[] { s } };
    }

    private void DecodeTransformData(int block, KfTrack tr)
    {
        var d = _d;
        var p = _off[block];
        var nrot = (int)U32(d, ref p);
        if (nrot > 0)
        {
            var rtype = (KfKeyType)U32(d, ref p);
            if (rtype == KfKeyType.XyzRotation)
            {
                var axes = new KfFloatChan[3];
                for (var a = 0; a < 3; a++) axes[a] = ReadFloatGroup(ref p);
                if (axes.Any(a => a.V.Length > 0))
                    tr.Rot = new KfRotChan { Type = rtype, Xyz = axes };
            }
            else
            {
                var rc = new KfRotChan
                {
                    Type = rtype,
                    Times = new float[nrot], Q = new Quaternion[nrot],
                };
                for (var i = 0; i < nrot; i++)
                {
                    rc.Times[i] = F32(d, ref p);
                    var w = F32(d, ref p); var x = F32(d, ref p);
                    var y = F32(d, ref p); var z = F32(d, ref p);
                    if (rtype == KfKeyType.Tbc) p += 12;
                    var q = new Quaternion(x, y, z, w);
                    rc.Q[i] = q.LengthSquared() > 1e-12f
                        ? Quaternion.Normalize(q) : Quaternion.Identity;
                }
                if (rtype is KfKeyType.Quadratic or KfKeyType.Tbc)
                    rc.BuildSquad();
                tr.Rot = rc;
            }
        }
        var pc = ReadVec3Group(ref p);
        if (pc != null) tr.Pos = pc;
        var sc = ReadFloatGroup(ref p);
        if (sc.V.Length > 0) tr.Scl = sc;
        CheckEnd(block, p);
    }

    /// <summary>KeyGroup&lt;float&gt; (n, type, clés) avec tangentes
    /// (Quadratic) ou TBC converti en tangentes.</summary>
    private KfFloatChan ReadFloatGroup(ref int p)
    {
        var d = _d;
        var n = (int)U32(d, ref p);
        var c = new KfFloatChan();
        if (n == 0) return c;
        c.Type = (KfKeyType)U32(d, ref p);
        c.Times = new float[n]; c.V = new float[n];
        if (c.Type is KfKeyType.Quadratic or KfKeyType.Tbc)
        { c.T0 = new float[n]; c.T1 = new float[n]; }
        var tbc = c.Type == KfKeyType.Tbc ? new Vector3[n] : null;
        for (var i = 0; i < n; i++)
        {
            c.Times[i] = F32(d, ref p);
            c.V[i] = F32(d, ref p);
            if (c.Type == KfKeyType.Quadratic)
            {
                c.T0![i] = F32(d, ref p);      // Forward = sortante
                c.T1![i] = F32(d, ref p);      // Backward = entrante
            }
            else if (tbc != null)
                tbc[i] = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
        }
        if (tbc != null)
        {
            var v = c.V.Select(x => new Vector3(x)).ToArray();
            var a = new Vector3[n]; var b = new Vector3[n];
            KfInterp.TbcTangents(v, tbc, a, b);
            c.T0 = a.Select(x => x.X).ToArray();
            c.T1 = b.Select(x => x.X).ToArray();
        }
        return Sanitize(c);
    }

    /// <summary>Neutralise les NaN/∞ présents dans les données d'origine
    /// (clé = dernière valeur finie, tangente = 0 ; canal entièrement
    /// invalide = vide) et les consigne dans <see cref="DataDefects"/>.</summary>
    private KfFloatChan Sanitize(KfFloatChan c)
    {
        var bad = 0;
        var first = Array.FindIndex(c.V, float.IsFinite);
        for (var i = 0; i < c.V.Length; i++)
        {
            if (!float.IsFinite(c.V[i]))
            {
                bad++;
                if (first >= 0)
                    c.V[i] = i == 0 || !float.IsFinite(c.V[i - 1])
                        ? c.V[first] : c.V[i - 1];
            }
            if (c.T0 != null && !float.IsFinite(c.T0[i])) { c.T0[i] = 0; bad++; }
            if (c.T1 != null && !float.IsFinite(c.T1[i])) { c.T1[i] = 0; bad++; }
        }
        if (bad == 0) return c;
        if (first < 0)                          // aucune valeur finie
        {
            c.V = Array.Empty<float>(); c.Times = Array.Empty<float>();
            c.T0 = c.T1 = null;
        }
        DataDefects.Add($"{bad} valeur(s) non finie(s) dans un canal scalaire");
        return c;
    }

    /// <summary>KeyGroup&lt;Vector3&gt; (NiPosData, translations) ; null
    /// si vide.</summary>
    private KfVec3Chan? ReadVec3Group(ref int p)
    {
        var d = _d;
        var n = (int)U32(d, ref p);
        if (n == 0) return null;
        var c = new KfVec3Chan { Type = (KfKeyType)U32(d, ref p) };
        c.Times = new float[n]; c.V = new Vector3[n];
        if (c.Type is KfKeyType.Quadratic or KfKeyType.Tbc)
        { c.T0 = new Vector3[n]; c.T1 = new Vector3[n]; }
        var tbc = c.Type == KfKeyType.Tbc ? new Vector3[n] : null;
        for (var i = 0; i < n; i++)
        {
            c.Times[i] = F32(d, ref p);
            c.V[i] = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
            if (c.Type == KfKeyType.Quadratic)
            {
                c.T0![i] = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
                c.T1![i] = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
            }
            else if (tbc != null)
                tbc[i] = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
        }
        if (tbc != null) KfInterp.TbcTangents(c.V, tbc, c.T0!, c.T1!);
        return c;
    }

    // ------------------------------ B-splines --------------------------

    /// <summary>Lit les points de contrôle du canal (offset de handle
    /// <paramref name="h"/>) ; null si le canal est absent ou invalide.</summary>
    private KfBSpline? SplineChan(int spline, int basis, uint h, int dim,
        float off, float half, bool compact, float start, float stop)
    {
        if (h == 0xFFFFFFFF) return null;
        if (spline < 0 || basis < 0 || spline >= _off.Length ||
            basis >= _off.Length) return null;
        var d = _d;
        var bp = _off[basis];
        var ncp = (int)U32(d, ref bp);
        if (ncp < 4) return null;
        var sp = _off[spline];
        var nf = (int)U32(d, ref sp);
        var fOff = sp;
        sp += 4 * nf;
        var nc = (int)U32(d, ref sp);
        var cOff = sp;
        var need = (long)h + (long)ncp * dim;
        var cp = new float[ncp * dim];
        if (compact)
        {
            if (need > nc || !float.IsFinite(half) || half >= 3.0e38f ||
                !float.IsFinite(off))
                return null;
            for (var i = 0; i < cp.Length; i++)
                cp[i] = off + BinaryPrimitives.ReadInt16LittleEndian(
                    d.AsSpan(cOff + 2 * ((int)h + i), 2)) * half / 32767f;
        }
        else
        {
            if (need > nf) return null;
            for (var i = 0; i < cp.Length; i++)
                cp[i] = BinaryPrimitives.ReadSingleLittleEndian(
                    d.AsSpan(fOff + 4 * ((int)h + i), 4));
        }
        return new KfBSpline(cp, ncp, dim, start, stop);
    }

    private void DecodeSpline(int block, bool compact, KfTrack tr)
    {
        var d = _d;
        var p = _off[block];
        var start = F32(d, ref p);
        var stop = F32(d, ref p);
        var spline = I32(d, ref p);
        var basis = I32(d, ref p);
        var (bt, bq, bs) = QuatTransform(ref p);
        var th = U32(d, ref p); var rh = U32(d, ref p); var sh = U32(d, ref p);
        float to = 0, thr = 0, ro = 0, rhr = 0, so = 0, shr = 0;
        if (compact)
        {
            to = F32(d, ref p); thr = F32(d, ref p);
            ro = F32(d, ref p); rhr = F32(d, ref p);
            so = F32(d, ref p); shr = F32(d, ref p);
        }
        CheckEnd(block, p);
        SetConst(tr, bt, bq, bs);
        var pos = SplineChan(spline, basis, th, 3, to, thr, compact, start, stop);
        var rot = SplineChan(spline, basis, rh, 4, ro, rhr, compact, start, stop);
        var scl = SplineChan(spline, basis, sh, 1, so, shr, compact, start, stop);
        if (pos != null) tr.Pos = new KfVec3Chan { Spline = pos };
        if (rot != null) tr.Rot = new KfRotChan { Type = KfKeyType.Linear, Spline = rot };
        if (scl != null) tr.Scl = new KfFloatChan { Spline = scl };
    }

    /// <summary>B-spline cubique ouverte uniforme (de Boor), u∈[0,1] —
    /// port de <c>bspline_eval</c> de l'oracle (allouant ; voir
    /// <see cref="KfBSpline"/>).</summary>
    internal static void BSplineEval(float[] cp, int n, int dim, float u,
        float[] result) =>
        new KfBSpline(cp, n, dim, 0, 1).EvalU(u, result);

    // --------------------------- NiPathInterpolator --------------------

    /// <summary>Chemin : position = NiPosData(pourcentage(t)) cuite à
    /// 30 Hz en clés de position ; pas de rotation (suivi d'axe non
    /// appliqué : 6 occurrences dans le corpus).</summary>
    private void DecodePath(int block, KfTrack tr, KfSequence seq)
    {
        var d = _d;
        var p = _off[block];
        U16(d, ref p);                      // flags
        I32(d, ref p);                      // bank dir
        F32(d, ref p); F32(d, ref p);       // max bank angle, smoothing
        U16(d, ref p);                      // follow axis
        var pathData = I32(d, ref p);
        var pctData = I32(d, ref p);
        CheckEnd(block, p);
        if (!IsType(pathData, "NiPosData")) return;
        var q = _off[pathData];
        var path = ReadVec3Group(ref q);
        if (path == null) return;
        KfFloatChan? pct = null;
        if (IsType(pctData, "NiFloatData"))
        {
            q = _off[pctData];
            pct = ReadFloatGroup(ref q);
        }
        var end = path.Times[^1];
        var dur = Math.Max(seq.Stop - seq.Start, 1e-6f);
        var n = Math.Max(2, (int)MathF.Ceiling(dur * 30f) + 1);
        var ts = new float[n]; var vs = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            ts[i] = seq.Start + dur * i / (n - 1);
            var f = pct == null || pct.V.Length == 0
                ? (ts[i] - seq.Start) / dur : pct.Eval(ts[i]);
            vs[i] = path.Eval(Math.Clamp(f, 0f, 1f) * end);
        }
        tr.Pos = new KfVec3Chan { Times = ts, V = vs };
    }

    // --------------------------- pistes auxiliaires --------------------

    private KfAuxTrack? DecodeAux(int block, string type, string node)
    {
        var d = _d;
        var p = _off[block];
        var a = new KfAuxTrack { NodeName = node };
        switch (type)
        {
            case "NiFloatInterpolator":
            {
                var v = F32(d, ref p);
                var data = I32(d, ref p);
                CheckEnd(block, p);
                a.Kind = KfAuxKind.Float;
                a.Float = ConstOrFloatData(v, data);
                return a;
            }
            case "NiBoolInterpolator":
            {
                var v = d[p++] != 0 ? 1f : 0f;
                var data = I32(d, ref p);
                CheckEnd(block, p);
                a.Kind = KfAuxKind.Bool;
                a.Float = ConstOrBoolData(v, data);
                return a;
            }
            case "NiPoint3Interpolator":
            {
                var v = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
                var data = I32(d, ref p);
                CheckEnd(block, p);
                a.Kind = KfAuxKind.Point3;
                a.Vec = new KfVec3Chan { Times = new[] { 0f }, V = new[] { v } };
                if (IsType(data, "NiPosData"))
                {
                    var q = _off[data];
                    var c = ReadVec3Group(ref q);
                    CheckEnd(data, q);
                    if (c != null) a.Vec = c;
                }
                return a;
            }
            case "NiBSplineCompFloatInterpolator":
            {
                var start = F32(d, ref p); var stop = F32(d, ref p);
                var spline = I32(d, ref p); var basis = I32(d, ref p);
                var v = F32(d, ref p);
                var h = U32(d, ref p);
                var off = F32(d, ref p); var half = F32(d, ref p);
                CheckEnd(block, p);
                a.Kind = KfAuxKind.Float;
                var sp = SplineChan(spline, basis, h, 1, off, half, true, start, stop);
                a.Float = sp != null ? new KfFloatChan { Spline = sp }
                    : new KfFloatChan { Times = new[] { 0f }, V = new[] { v } };
                return a;
            }
            case "NiBSplineCompPoint3Interpolator":
            {
                var start = F32(d, ref p); var stop = F32(d, ref p);
                var spline = I32(d, ref p); var basis = I32(d, ref p);
                var v = new Vector3(F32(d, ref p), F32(d, ref p), F32(d, ref p));
                var h = U32(d, ref p);
                var off = F32(d, ref p); var half = F32(d, ref p);
                CheckEnd(block, p);
                a.Kind = KfAuxKind.Point3;
                var sp = SplineChan(spline, basis, h, 3, off, half, true, start, stop);
                a.Vec = sp != null ? new KfVec3Chan { Spline = sp }
                    : new KfVec3Chan { Times = new[] { 0f }, V = new[] { v } };
                return a;
            }
            default:
                return null;
        }
    }

    private KfFloatChan ConstOrFloatData(float v, int data)
    {
        if (IsType(data, "NiFloatData"))
        {
            var q = _off[data];
            var c = ReadFloatGroup(ref q);
            CheckEnd(data, q);
            if (c.V.Length > 0) return c;
        }
        return new KfFloatChan { Times = new[] { 0f }, V = new[] { v } };
    }

    private KfFloatChan ConstOrBoolData(float v, int data)
    {
        if (IsType(data, "NiBoolData"))
        {
            var q = _off[data];
            var n = (int)U32(_d, ref q);
            if (n > 0)
            {
                var c = new KfFloatChan
                {
                    Type = KfKeyType.Const, // pas d'interpolation d'un booléen
                    Times = new float[n], V = new float[n],
                };
                var kt = (KfKeyType)U32(_d, ref q);
                for (var i = 0; i < n; i++)
                {
                    c.Times[i] = F32(_d, ref q);
                    c.V[i] = _d[q++] != 0 ? 1f : 0f;
                    if (kt == KfKeyType.Quadratic) q += 2;
                    else if (kt == KfKeyType.Tbc) q += 12;
                }
                CheckEnd(data, q);
                return c;
            }
            CheckEnd(data, q);
        }
        return new KfFloatChan { Times = new[] { 0f }, V = new[] { v } };
    }
}
