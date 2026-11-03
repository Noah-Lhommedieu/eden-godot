using System.Numerics;
using EdenClient.Fx;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Simulateur de particules Gamebryo : règles pures, déterministes.</summary>
public class ParticleSimTests
{
    static EmitterDef Point(float speed = 0, float life = 1f) =>
        new() { Speed = speed, LifeSpan = life, InitialRadius = 2f, Axis = Vector3.UnitX };

    static ParticleSystemDef Burst(int n, EmitterDef e) =>
        new() { Emitter = e, BirthRate = 0, InitialBurst = n };

    [Fact]
    public void AgeDeath_KillsAtLifespan()
    {
        var s = new ParticleSim(Burst(3, Point(life: 1f)), 1);
        s.Step(0.25f);
        Assert.Equal(3, s.Count);
        s.Step(0.25f); s.Step(0.25f);
        Assert.Equal(3, s.Count);
        s.Step(0.25f); // âge 1.0 >= durée de vie
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Gravity_MatchesSemiImplicitFreeFall()
    {
        var def = new ParticleSystemDef
        {
            Emitter = Point(life: 100f), BirthRate = 0, InitialBurst = 1,
            Gravities = [new GravityDef { Axis = -Vector3.UnitZ, Strength = 10f }],
        };
        var s = new ParticleSim(def, 7);
        const float dt = 1f / 64f; // exact en binaire
        const int n = 64;
        for (int i = 0; i < n; i++) s.Step(dt);
        // v += g dt puis p += v dt : z = -g dt² n(n+1)/2
        float expZ = -10f * dt * dt * n * (n + 1) / 2f;
        Assert.Equal(expZ, s.Particles[0].Position.Z, 3);
        Assert.Equal(-10f * n * dt, s.Particles[0].Velocity.Z, 3);
        // ~ -½ g t² pour t = 1 s (-5), à l'erreur d'Euler près
        Assert.InRange(s.Particles[0].Position.Z, -5.2f, -5f);
    }

    [Fact]
    public void GrowFade_Envelope()
    {
        var def = new ParticleSystemDef
        {
            Emitter = Point(life: 2f), BirthRate = 0, InitialBurst = 1,
            GrowFade = new GrowFadeDef { GrowTime = 0.5f, FadeTime = 0.5f },
        };
        var s = new ParticleSim(def, 1);
        s.Step(0.25f);
        Assert.Equal(1f, s.Particles[0].Size, 4);   // 2 * (0.25/0.5)
        s.Step(0.25f);
        Assert.Equal(2f, s.Particles[0].Size, 4);   // plein
        for (int i = 0; i < 4; i++) s.Step(0.25f);  // âge 1.5 : reste 0.5 -> plein
        Assert.Equal(2f, s.Particles[0].Size, 4);
        s.Step(0.25f);                              // âge 1.75 : reste 0.25
        Assert.Equal(1f, s.Particles[0].Size, 4);
    }

    static void Near(Vector4 e, Vector4 a) => Assert.True((e - a).Length() < 1e-5f, $"{e} != {a}");

    [Fact]
    public void ColorKeys_InterpolateOnNormalizedAge()
    {
        var def = new ParticleSystemDef
        {
            Emitter = Point(life: 1f), BirthRate = 0, InitialBurst = 1,
            Colors = [new(0f, new Vector4(1, 0, 0, 1)), new(0.5f, new Vector4(0, 1, 0, 1)), new(1f, new Vector4(0, 0, 1, 0))],
        };
        var s = new ParticleSim(def, 1);
        s.Step(0.25f);
        Near(new Vector4(0.5f, 0.5f, 0, 1), s.Particles[0].Color);
        s.Step(0.25f);
        Near(new Vector4(0, 1, 0, 1), s.Particles[0].Color);
        s.Step(0.25f);
        Near(new Vector4(0, 0.5f, 0.5f, 0.5f), s.Particles[0].Color);
    }

    static float[] Dump(ParticleSim s)
    {
        var l = new List<float>();
        foreach (var p in s.Particles)
            l.AddRange([p.Position.X, p.Position.Y, p.Position.Z, p.Size, p.Color.W, p.Rotation, p.Age]);
        return l.ToArray();
    }

    ParticleSystemDef Rich() => new()
    {
        Emitter = new EmitterDef
        {
            Shape = EmitterShape.Sphere, SphereRadius = 1f, Speed = 2f, SpeedVar = 1f,
            Declination = 0.5f, DeclinationVar = 0.3f, PlanarAngleVar = 3f, LifeSpan = 1f, LifeSpanVar = 0.4f,
        },
        BirthRate = 50f,
        Rotation = new RotationDef { InitialSpeed = 1f, InitialSpeedVar = 0.5f, RandomSign = true },
        Spawn = new SpawnDef { Generations = 2, Percentage = 0.5f, MaxNum = 3, SpeedChaos = 0.5f, DirectionChaos = 1f },
        Gravities = [new GravityDef()],
    };

    [Fact]
    public void Determinism_SameSeedSameOutput_DifferentSeedDiffers()
    {
        var a = new ParticleSim(Rich(), 42);
        var b = new ParticleSim(Rich(), 42);
        var c = new ParticleSim(Rich(), 43);
        for (int i = 0; i < 100; i++) { a.Step(1f / 30); b.Step(1f / 30); c.Step(1f / 30); }
        Assert.True(a.Count > 0);
        Assert.Equal(Dump(a), Dump(b));
        Assert.NotEqual(Dump(a), Dump(c));
        a.Reset();
        for (int i = 0; i < 100; i++) a.Step(1f / 30);
        Assert.Equal(Dump(b), Dump(a)); // Reset rejoue la même séquence
    }

    [Fact]
    public void PlanarCollider_Bounces_AndDies()
    {
        ParticleSystemDef Make(float bounce, bool die) => new()
        {
            Emitter = new EmitterDef { Speed = 4f, Axis = -Vector3.UnitZ, LifeSpan = 100f },
            BirthRate = 0, InitialBurst = 1,
            Colliders = [new PlanarColliderDef { Point = new Vector3(0, 0, -1), Normal = Vector3.UnitZ, Bounce = bounce, DieOnCollide = die }],
        };
        var s = new ParticleSim(Make(0.5f, false), 1);
        for (int i = 0; i < 8; i++) s.Step(0.0625f); // touche le plan à t = 0.25
        var p = s.Particles[0];
        Assert.True(p.Velocity.Z > 0);
        Assert.Equal(2f, p.Velocity.Z, 3); // 4 * 0.5
        Assert.True(p.Position.Z >= -1f);

        var d = new ParticleSim(Make(1f, true), 1);
        for (int i = 0; i < 8; i++) d.Step(0.0625f);
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void SpawnGenerations_ChildrenOnDeath_StopAtLimit()
    {
        var def = new ParticleSystemDef
        {
            Emitter = Point(life: 0.5f), BirthRate = 0, InitialBurst = 1,
            Spawn = new SpawnDef { Generations = 2, MinNum = 2, MaxNum = 2, LifeSpan = 0.5f },
        };
        var s = new ParticleSim(def, 1);
        s.Step(0.5f);                        // parent meurt -> 2 enfants g1
        Assert.Equal(2, s.Count);
        Assert.All(s.Particles.ToArray(), p => Assert.Equal(1, p.Generation));
        s.Step(0.5f);                        // g1 meurent -> 4 enfants g2
        Assert.Equal(4, s.Count);
        Assert.All(s.Particles.ToArray(), p => Assert.Equal(2, p.Generation));
        s.Step(0.5f);                        // g2 : limite atteinte
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void SpawnOnCollide_EmitsChildren()
    {
        var def = new ParticleSystemDef
        {
            Emitter = new EmitterDef { Speed = 4f, Axis = -Vector3.UnitZ, LifeSpan = 100f },
            BirthRate = 0, InitialBurst = 1,
            Spawn = new SpawnDef { Generations = 1, MinNum = 3, MaxNum = 3, OnDeath = false, LifeSpan = 10f },
            Colliders = [new PlanarColliderDef { Point = new Vector3(0, 0, -1), Bounce = 0, SpawnOnCollide = true, DieOnCollide = true }],
        };
        var s = new ParticleSim(def, 1);
        for (int i = 0; i < 8; i++) s.Step(0.0625f);
        Assert.Equal(3, s.Count);
    }

    [Fact]
    public void LocalVsWorldSpace()
    {
        ParticleSystemDef Make(bool local) => new()
        {
            Emitter = Point(speed: 1f, life: 10f), BirthRate = 0, InitialBurst = 1, LocalSpace = local,
        };
        var move = Matrix4x4.CreateTranslation(10, 0, 0);
        var loc = new ParticleSim(Make(true), 1) { EmitterTransform = move };
        var wld = new ParticleSim(Make(false), 1) { EmitterTransform = move };
        loc.Step(0.5f); wld.Step(0.5f);
        Assert.Equal(0.5f, loc.Particles[0].Position.X, 4);   // local : origine du nœud
        Assert.Equal(10.5f, wld.Particles[0].Position.X, 4);  // monde : transform à la naissance
        wld.EmitterTransform = Matrix4x4.CreateTranslation(50, 0, 0);
        wld.Step(0.5f);
        Assert.Equal(11f, wld.Particles[0].Position.X, 4);    // ne suit plus le nœud

        // Rendu : le local est amené en monde par la transform du nœud.
        var view = new BillboardView(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Zero);
        var rd = new ParticleRenderData();
        loc.EmitterTransform = Matrix4x4.CreateTranslation(100, 0, 0);
        rd.Fill(loc, view, false);
        Assert.Equal(1, rd.InstanceCount);
        Assert.Equal(100.5f, rd.Transforms[3], 4);
    }

    [Fact]
    public void RenderData_SortsBackToFront()
    {
        var def = new ParticleSystemDef
        {
            Emitter = new EmitterDef { Shape = EmitterShape.Mesh, MeshPoints = [new(0, 0, 1), new(0, 0, 5), new(0, 0, 3)], LifeSpan = 9f },
            BirthRate = 0, InitialBurst = 3,
        };
        var s = new ParticleSim(def, 3);
        s.Step(0.1f);
        var rd = new ParticleRenderData();
        rd.Fill(s, new BillboardView(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Zero), true);
        Assert.Equal(3, rd.InstanceCount);
        Assert.True(rd.Transforms[11] >= rd.Transforms[23] && rd.Transforms[23] >= rd.Transforms[35]);
    }
}
