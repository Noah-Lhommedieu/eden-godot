using System.Numerics;

namespace EdenClient.Fx;

/// <summary>Une particule vivante. Taille et couleur sont les valeurs finales
/// (après grow/fade et keyframes) ; Initial* sont les valeurs de naissance.</summary>
public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Size;
    public Vector4 Color;
    public float Rotation;
    public float RotationSpeed;
    public float Age;
    public float LifeSpan;
    public int Generation;
    public float InitialSize;
    public Vector4 InitialColor;
    public bool Dead;
}

/// <summary>Simulateur CPU déterministe (même graine + mêmes dt = mêmes bits).
/// Ordre par pas : naissance, vieillissement, gravité, position, rotation,
/// collisions, couleur, grow/fade, mort (+ spawn), bornes.</summary>
public sealed class ParticleSim
{
    /// <summary>Pas maximal interne ; un dt plus grand est découpé.</summary>
    public float MaxStep { get; set; } = 0.05f;

    readonly ParticleSystemDef _def;
    readonly uint _seed;
    ulong _rng;
    readonly Particle[] _p;
    readonly Particle[] _pending;
    int _pendingCount;
    float _birthAcc;
    int _stepCount;
    bool _burstDone;
    int _loopIdx = int.MinValue;

    public int Count { get; private set; }
    public float Time { get; private set; }
    public ReadOnlySpan<Particle> Particles => new(_p, 0, Count);
    public ParticleSystemDef Def => _def;
    /// <summary>Transform du nœud système : sert à passer les naissances en monde
    /// (LocalSpace=false) et au rendu local. Convention System.Numerics.</summary>
    public Matrix4x4 EmitterTransform { get; set; } = Matrix4x4.Identity;
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }
    public bool HasBounds { get; private set; }

    public ParticleSim(ParticleSystemDef def, uint seed)
    {
        _def = def;
        _seed = seed;
        int cap = Math.Max(1, def.MaxCount);
        _p = new Particle[cap];
        _pending = new Particle[cap];
        Reset();
    }

    public void Reset()
    {
        _rng = _seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
        Count = 0;
        Time = 0;
        _birthAcc = 0;
        _stepCount = 0;
        _burstDone = false;
        _loopIdx = int.MinValue;
        HasBounds = false;
    }

    /// <summary>Avance rapide (pré-chauffe) en pas fixes.</summary>
    public void Warmup(float seconds, float step = 1f / 30f)
    {
        for (float t = 0; t < seconds; t += step) Step(Math.Min(step, seconds - t));
    }

    public void Step(float dt)
    {
        if (dt <= 0) return;
        while (dt > MaxStep) { SubStep(MaxStep); dt -= MaxStep; }
        SubStep(dt);
    }

    // --- RNG : splitmix64, indépendant de System.Random -------------------
    float Rand()
    {
        _rng += 0x9E3779B97F4A7C15UL;
        ulong z = _rng;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) * (1f / (1 << 24));
    }
    float Sym() => Rand() * 2f - 1f;

    // Valeurs courantes des paramètres d'émetteur (après courbes de contrôleurs).
    float _rate, _speed, _life, _radius, _decl, _declVar, _planVar, _rotSpd, _rotSpdVar;
    bool _active = true;

    float P(PsParam k, float dflt) =>
        _def.Curves.TryGetValue(k, out var c) ? c.Eval(Time) : dflt;

    void EvalParams()
    {
        var e = _def.Emitter;
        _rate = Math.Max(0f, P(PsParam.BirthRate, _def.BirthRate));
        _active = !_def.Curves.TryGetValue(PsParam.EmitterActive, out var a) || a.Eval(Time) >= 0.5f;
        _speed = P(PsParam.Speed, e.Speed);
        _life = P(PsParam.LifeSpan, e.LifeSpan);
        _radius = P(PsParam.InitialRadius, e.InitialRadius);
        _decl = P(PsParam.Declination, e.Declination);
        _declVar = P(PsParam.DeclinationVar, e.DeclinationVar);
        _planVar = P(PsParam.PlanarAngleVar, e.PlanarAngleVar);
        var rot = _def.Rotation;
        _rotSpd = P(PsParam.RotSpeed, rot?.InitialSpeed ?? 0f);
        _rotSpdVar = P(PsParam.RotSpeedVar, rot?.InitialSpeedVar ?? 0f);
    }

    /// <summary>Force de gravité au temps courant (courbe GravityStrength si présente).</summary>
    float GravStrength(GravityDef g) => _def.Curves.TryGetValue(PsParam.GravityStrength, out var c)
        ? c.Eval(Time) : g.Strength;

    void SubStep(float dt)
    {
        var d = _def;
        Time += dt;
        EvalParams();
        if (d.ResetOnLoop && d.Curves.TryGetValue(PsParam.BirthRate, out var lc) &&
            lc.Timing.Cycle == EdenClient.World.NifCycle.Loop && lc.Timing.Duration > 1e-4f)
        {
            int k = (int)MathF.Floor((Time * lc.Timing.Frequency + lc.Timing.Phase - lc.Timing.Start) / lc.Timing.Duration);
            if (_loopIdx != int.MinValue && k != _loopIdx) Count = 0;
            _loopIdx = k;
        }

        if (d.Loop && d.LoopLength > 0 && Time >= d.LoopLength)
        {
            Time -= d.LoopLength;
            _burstDone = false;
            if (d.ResetOnLoop) Count = 0;
        }

        Emit(dt);

        _pendingCount = 0;
        int w = 0;
        for (int i = 0; i < Count; i++)
        {
            var p = _p[i];
            p.Age += dt;
            Vector3 prev = p.Position;

            foreach (var g in d.Gravities) p.Velocity += GravityAccel(g, GravStrength(g), p.Position) * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.RotationSpeed * dt;

            foreach (var c in d.Colliders)
            {
                if (p.Dead) break;
                Collide(c, ref p, prev);
            }

            p.Color = ColorAt(p);
            p.Size = p.InitialSize * GrowFadeScale(p);

            bool natural = p.Age >= p.LifeSpan;
            if (natural && !p.Dead && d.Spawn is { OnDeath: true } sd) SpawnChildren(sd, in p, p.Position, p.Velocity);
            if (natural || p.Dead) continue; // retirée (compaction stable)
            _p[w++] = p;
        }
        Count = w;

        for (int i = 0; i < _pendingCount && Count < _p.Length; i++) _p[Count++] = _pending[i];

        if (d.BoundUpdateSkip >= 0 && _stepCount % (d.BoundUpdateSkip + 1) == 0) UpdateBounds();
        _stepCount++;
    }

    void Emit(float dt)
    {
        var d = _def;
        int n = 0;
        if (!_burstDone && Time >= d.EmitStart)
        {
            _burstDone = true;
            n += d.InitialBurst;
        }
        if (_active && Time >= d.EmitStart && Time - dt < d.EmitStop)
        {
            _birthAcc += _rate * dt;
            int k = (int)_birthAcc;
            _birthAcc -= k;
            n += k;
        }
        for (; n > 0 && Count < _p.Length; n--) _p[Count++] = Birth();
    }

    Particle Birth()
    {
        var e = _def.Emitter;
        Vector3 pos;
        Vector3 axis = e.Axis;
        switch (e.Shape)
        {
            case EmitterShape.Sphere:
            {
                // rejet dans le cube : uniforme dans la boule
                Vector3 v;
                do { v = new Vector3(Sym(), Sym(), Sym()); } while (v.LengthSquared() > 1f);
                pos = v * e.SphereRadius;
                break;
            }
            case EmitterShape.Mesh when e.MeshPoints is { Length: > 0 } pts:
            {
                if (e.MeshTriangles is { Length: >= 3 } tr)
                {
                    int f = Math.Min((int)(Rand() * (tr.Length / 3)), tr.Length / 3 - 1) * 3;
                    int ia = tr[f], ib = tr[f + 1], ic = tr[f + 2];
                    float u = Rand(), v = Rand();
                    if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                    float w = 1f - u - v;
                    pos = pts[ia] * w + pts[ib] * u + pts[ic] * v;
                    Vector3 fn = Vector3.Cross(pts[ib] - pts[ia], pts[ic] - pts[ia]);
                    if (e.MeshNormals is { } mn && ia < mn.Length && ib < mn.Length && ic < mn.Length &&
                        (mn[ia] + mn[ib] + mn[ic]).LengthSquared() > 1e-12f)
                        fn = mn[ia] * w + mn[ib] * u + mn[ic] * v;
                    if (fn.LengthSquared() > 1e-12f) axis = fn;
                }
                else
                {
                    int i = Math.Min((int)(Rand() * pts.Length), pts.Length - 1);
                    pos = pts[i];
                    if (e.MeshNormals is { } ns && i < ns.Length && ns[i].LengthSquared() > 1e-12f) axis = ns[i];
                }
                break;
            }
            default:
                pos = new Vector3(Sym(), Sym(), Sym()) * (e.BoxSize * 0.5f);
                break;
        }
        if (e.Shape != EmitterShape.Mesh) pos = Vector3.Transform(pos, e.Frame);

        float decl = _decl + Sym() * _declVar;
        float plan = e.PlanarAngle + Sym() * _planVar;
        Vector3 an = Vector3.Normalize(axis);
        MakeBasis(an, out var b, out var c);
        Vector3 dir = MathF.Cos(decl) * an
                      + MathF.Sin(decl) * (MathF.Cos(plan) * b + MathF.Sin(plan) * c);
        if (e.RandomDirection)
        {
            Vector3 rv;
            do { rv = new Vector3(Sym(), Sym(), Sym()); } while (rv.LengthSquared() > 1f || rv.LengthSquared() < 1e-6f);
            dir = Vector3.Normalize(rv);
        }
        if (e.Shape != EmitterShape.Mesh) dir = Vector3.Normalize(Vector3.TransformNormal(dir, e.Frame));
        float speed = _speed + Sym() * e.SpeedVar;
        Vector3 vel = dir * speed;

        if (!_def.LocalSpace)
        {
            pos = Vector3.Transform(pos, EmitterTransform);
            vel = Vector3.TransformNormal(vel, EmitterTransform);
        }

        float size = Math.Max(0f, _radius + Sym() * e.RadiusVar);
        float life = Math.Max(1e-4f, _life + Sym() * e.LifeSpanVar);
        var p = new Particle
        {
            Position = pos, Velocity = vel, LifeSpan = life,
            InitialSize = size, Size = size,
            InitialColor = e.InitialColor, Color = e.InitialColor,
        };
        InitRotation(ref p);
        return p;
    }

    void InitRotation(ref Particle p)
    {
        if (_def.Rotation is not { } r) return;
        p.Rotation = r.InitialAngle + Sym() * r.InitialAngleVar;
        float s = _rotSpd + Sym() * _rotSpdVar;
        if (r.RandomSign && Rand() < 0.5f) s = -s;
        p.RotationSpeed = s;
    }

    void SpawnChildren(SpawnDef sd, in Particle parent, Vector3 pos, Vector3 vel)
    {
        if (parent.Generation >= sd.Generations) return;
        if (sd.Percentage < 1f && Rand() >= sd.Percentage) return;
        int n = sd.MinNum >= sd.MaxNum ? sd.MinNum : sd.MinNum + (int)(Rand() * (sd.MaxNum - sd.MinNum + 1));
        float baseSpeed = vel.Length();
        Vector3 baseDir = baseSpeed > 1e-8f ? vel / baseSpeed : Vector3.UnitZ;
        for (int i = 0; i < n && _pendingCount < _pending.Length; i++)
        {
            Vector3 dir = baseDir;
            if (sd.DirectionChaos > 0)
            {
                MakeBasis(baseDir, out var b, out var c);
                float a = Rand() * sd.DirectionChaos, ph = Rand() * MathF.Tau;
                dir = MathF.Cos(a) * baseDir + MathF.Sin(a) * (MathF.Cos(ph) * b + MathF.Sin(ph) * c);
            }
            float sp = baseSpeed * sd.SpeedFactor * (1f + Sym() * sd.SpeedChaos);
            var ch = new Particle
            {
                Position = pos, Velocity = dir * sp, Generation = parent.Generation + 1,
                LifeSpan = Math.Max(1e-4f, sd.LifeSpan + Sym() * sd.LifeSpanVar),
                InitialSize = parent.InitialSize, Size = parent.InitialSize,
                InitialColor = parent.InitialColor, Color = parent.InitialColor,
            };
            InitRotation(ref ch);
            _pending[_pendingCount++] = ch;
        }
    }

    static Vector3 GravityAccel(GravityDef g, float strength, Vector3 pos)
    {
        Vector3 axis = Vector3.Normalize(g.Axis);
        float s = strength;
        if (g.Type == GravityType.Point)
        {
            Vector3 to = g.Position - pos;
            float dist = to.Length();
            if (dist < 1e-6f) return Vector3.Zero;
            if (g.Decay != 0) s *= MathF.Exp(-g.Decay * dist);
            return to / dist * s;
        }
        if (g.Type == GravityType.ForceField && g.Decay != 0)
            s *= MathF.Exp(-g.Decay * MathF.Abs(Vector3.Dot(pos - g.Position, axis)));
        return axis * s;
    }

    void Collide(PlanarColliderDef c, ref Particle p, Vector3 prev)
    {
        Vector3 n = Vector3.Normalize(c.Normal);
        float d0 = Vector3.Dot(prev - c.Point, n);
        float d1 = Vector3.Dot(p.Position - c.Point, n);
        if (!(d0 >= 0 && d1 < 0)) return;
        float t = d0 / (d0 - d1);
        Vector3 hit = prev + (p.Position - prev) * t;
        if (c.Width > 0 || c.Height > 0)
        {
            MakeBasis(n, out var u, out var v);
            Vector3 rel = hit - c.Point;
            if (c.Width > 0 && MathF.Abs(Vector3.Dot(rel, u)) > c.Width * 0.5f) return;
            if (c.Height > 0 && MathF.Abs(Vector3.Dot(rel, v)) > c.Height * 0.5f) return;
        }
        p.Velocity -= (1f + c.Bounce) * Vector3.Dot(p.Velocity, n) * n;
        p.Position = hit;
        if (c.SpawnOnCollide && _def.Spawn is { } sd) SpawnChildren(sd, in p, hit, p.Velocity);
        if (c.DieOnCollide) p.Dead = true;
    }

    Vector4 ColorAt(in Particle p)
    {
        var k = _def.Colors;
        if (k is not { Length: > 0 }) return p.InitialColor;
        float t = p.Age / p.LifeSpan;
        if (t <= k[0].T) return k[0].Color;
        for (int i = 1; i < k.Length; i++)
        {
            if (t <= k[i].T)
            {
                float span = k[i].T - k[i - 1].T;
                float f = span > 1e-8f ? (t - k[i - 1].T) / span : 1f;
                return Vector4.Lerp(k[i - 1].Color, k[i].Color, f);
            }
        }
        return k[^1].Color;
    }

    float GrowFadeScale(in Particle p)
    {
        if (_def.GrowFade is not { } g) return 1f;
        float s = 1f;
        if (g.GrowTime > 0 && p.Generation <= g.GrowGeneration) s = Math.Min(s, p.Age / g.GrowTime);
        if (g.FadeTime > 0 && p.Generation <= g.FadeGeneration) s = Math.Min(s, (p.LifeSpan - p.Age) / g.FadeTime);
        return Math.Clamp(s, 0f, 1f);
    }

    void UpdateBounds()
    {
        if (Count == 0) { HasBounds = false; return; }
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (int i = 0; i < Count; i++)
        {
            var h = new Vector3(_p[i].Size * 0.5f);
            mn = Vector3.Min(mn, _p[i].Position - h);
            mx = Vector3.Max(mx, _p[i].Position + h);
        }
        BoundsMin = mn; BoundsMax = mx; HasBounds = true;
    }

    static void MakeBasis(Vector3 n, out Vector3 b, out Vector3 c)
    {
        Vector3 r = MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        b = Vector3.Normalize(Vector3.Cross(r, n));
        c = Vector3.Cross(n, b);
    }
}
