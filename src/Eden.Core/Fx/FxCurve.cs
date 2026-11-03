using System.Numerics;
using EdenClient.World;

namespace EdenClient.Fx;

/// <summary>Minuterie d'un contrôleur Gamebryo : temps global -> temps local
/// (fréquence, phase, [start, stop], cycle boucle / aller-retour / une fois).</summary>
public readonly record struct CtlTiming(float Start, float Stop, float Frequency, float Phase,
    NifCycle Cycle, bool Backwards)
{
    public float Duration => Math.Max(Stop - Start, 0f);

    public static CtlTiming Of(NifController c) =>
        new(c.StartTime, c.StopTime, c.Frequency, c.Phase, c.Cycle, c.PlayBackwards);

    public static CtlTiming Of(NifControllerSequence s) =>
        new(s.StartTime, s.StopTime, s.Frequency, 0f, s.Cycle, false);

    public float Local(float t)
    {
        float d = Duration;
        float tt = t * Frequency + Phase;
        if (d <= 1e-6f) return Start;
        float x = tt - Start;
        switch (Cycle)
        {
            case NifCycle.Loop:
                x %= d; if (x < 0) x += d; break;
            case NifCycle.Reverse:
                x %= 2 * d; if (x < 0) x += 2 * d;
                if (x > d) x = 2 * d - x; break;
            default:
                x = Math.Clamp(x, 0, d); break;
        }
        if (Backwards) x = d - x;
        return Start + x;
    }

    /// <summary>Vrai tant que le contrôleur évolue encore (cycle Clamp fini = faux).</summary>
    public bool Running(float t) => Cycle != NifCycle.Clamp || (t * Frequency + Phase - Start) < Duration;
}

/// <summary>Courbe scalaire pilotée par un contrôleur.</summary>
public sealed class FxCurve
{
    public KfFloatChan Chan { get; init; } = new();
    public CtlTiming Timing { get; init; }
    public float Eval(float t) => Chan.Eval(Timing.Local(t));
}

/// <summary>Conversion des clés NIF résolues en canaux évaluables (KfFile).</summary>
public static class NifCurves
{
    /// <summary>Valeur « invalide » des interpolateurs sans valeur de repos.</summary>
    public static bool IsInvalid(float v) => v <= -3e38f || float.IsNaN(v);

    public static KfFloatChan Float(NifKeys<float>? k)
    {
        var c = new KfFloatChan();
        if (k == null || k.Count == 0) return c;
        c.Times = k.Times; c.V = k.Values; c.Type = (KfKeyType)(int)k.Type;
        if (k.Type == NifKeyType.Quadratic && k.Forward != null && k.Backward != null)
        { c.T0 = k.Forward; c.T1 = k.Backward; }
        else if (k.Type == NifKeyType.Tbc && k.Tbc != null)
        {
            int n = k.Count;
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(k.Values[i], 0, 0);
            var a = new Vector3[n]; var b = new Vector3[n];
            KfInterp.TbcTangents(v, k.Tbc, a, b);
            c.T0 = a.Select(x => x.X).ToArray(); c.T1 = b.Select(x => x.X).ToArray();
        }
        else if (k.Type is NifKeyType.Quadratic or NifKeyType.Tbc) c.Type = KfKeyType.Linear;
        return c;
    }

    public static KfVec3Chan Vec3(NifKeys<Vector3>? k)
    {
        var c = new KfVec3Chan();
        if (k == null || k.Count == 0) return c;
        c.Times = k.Times; c.V = k.Values; c.Type = (KfKeyType)(int)k.Type;
        if (k.Type == NifKeyType.Quadratic && k.Forward != null && k.Backward != null)
        { c.T0 = k.Forward; c.T1 = k.Backward; }
        else if (k.Type == NifKeyType.Tbc && k.Tbc != null)
        {
            int n = k.Count;
            var a = new Vector3[n]; var b = new Vector3[n];
            KfInterp.TbcTangents(k.Values, k.Tbc, a, b);
            c.T0 = a; c.T1 = b;
        }
        else if (k.Type is NifKeyType.Quadratic or NifKeyType.Tbc) c.Type = KfKeyType.Linear;
        return c;
    }

    /// <summary>Canal scalaire d'un interpolateur (float ou bool 0/1). Sans clés :
    /// la valeur de repos. Null : B-spline compacte (non décodée ici).</summary>
    public static KfFloatChan? FloatOf(NifInterpolator? ip)
    {
        if (ip == null) return null;
        if (ip.Bools is { Count: > 0 } bk)
        {
            return new KfFloatChan
            {
                Times = bk.Times, V = bk.Values.Select(b => b ? 1f : 0f).ToArray(),
                Type = KfKeyType.Const,
            };
        }
        if (ip.Type == "NiBoolInterpolator")
            return new KfFloatChan { Times = [0f], V = [ip.BoolValue ? 1f : 0f] };
        if (ip.Floats is { Count: > 0 }) return Float(ip.Floats);
        if (ip.Type == "NiFloatInterpolator")
            return new KfFloatChan { Times = [0f], V = [IsInvalid(ip.FloatValue) ? 0f : ip.FloatValue] };
        return null;
    }

    public static KfVec3Chan? Vec3Of(NifInterpolator? ip)
    {
        if (ip == null) return null;
        if (ip.Points is { Count: > 0 }) return Vec3(ip.Points);
        if (ip.Type == "NiPoint3Interpolator")
            return new KfVec3Chan { Times = [0f], V = [ip.PointValue] };
        return null;
    }

    /// <summary>Piste de transform d'un NiTransformInterpolator (null pour les
    /// B-splines et NiPathInterpolator, non décodés ici).</summary>
    public static KfTrack? Track(NifInterpolator? ip, string node)
    {
        if (ip is not { Type: "NiTransformInterpolator" }) return null;
        var tr = new KfTrack { NodeName = node };
        bool okT = !IsInvalid(ip.Translation.X), okS = !IsInvalid(ip.Scale);
        if (okT) tr.Pos = new KfVec3Chan { Times = [0f], V = [ip.Translation] };
        if (okS) tr.Scl = new KfFloatChan { Times = [0f], V = [ip.Scale] };
        if (ip.Rotation.LengthSquared() > 1e-6f && !IsInvalid(ip.Rotation.X))
            tr.Rot = new KfRotChan { Times = [0f], Q = [ip.Rotation] };
        var d = ip.Transform;
        if (d == null) return tr;
        if (d.Pos.Count > 0) tr.Pos = Vec3(d.Pos);
        if (d.Scale.Count > 0) tr.Scl = Float(d.Scale);
        if (d.RotType == NifKeyType.XyzRotation && d.XyzRot != null)
            tr.Rot = new KfRotChan
            {
                Type = KfKeyType.XyzRotation,
                Xyz = d.XyzRot.Select(Float).ToArray(),
            };
        else if (d.RotTimes.Length > 0)
        {
            var rc = new KfRotChan
            {
                Times = d.RotTimes, Q = d.Rot.ToArray(),
                Type = d.RotType == NifKeyType.Const ? KfKeyType.Const : KfKeyType.Linear,
            };
            if (d.RotType is NifKeyType.Quadratic or NifKeyType.Tbc && d.Rot.Length > 2)
            { rc.Type = KfKeyType.Quadratic; rc.BuildSquad(); }
            tr.Rot = rc;
        }
        return tr;
    }
}
