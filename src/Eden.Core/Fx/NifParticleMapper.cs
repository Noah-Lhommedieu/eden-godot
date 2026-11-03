using System.Numerics;
using EdenClient.World;

namespace EdenClient.Fx;

/// <summary>NiParticleSystem (+ modificateurs, contrôleurs NiPSys*Ctlr) ->
/// <see cref="ParticleSystemDef"/>. Tout est exprimé dans l'espace local du
/// nœud NiParticleSystem (repère NIF Z-up, aucune conversion d'axes).</summary>
public static class NifParticleMapper
{
    public sealed class Result
    {
        public ParticleSystemDef Def = new();
        /// <summary>Particules en coordonnées monde (NiParticleSystem.WorldSpace).</summary>
        public bool WorldSpace;
        /// <summary>Sous-meshes d'émetteur skinnés à re-poser par frame
        /// (<see cref="EmitterSkin.Reskin"/>) ; null si l'émetteur est
        /// rigide — chemin historique : MeshPoints cuits en pose de bind.</summary>
        public EmitterSkin? Skin;
        public List<string> Notes = new();
    }

    static Matrix4x4 Inv(Matrix4x4 m) => Matrix4x4.Invert(m, out var r) ? r : Matrix4x4.Identity;

    /// <summary>Transform de <paramref name="av"/> dans l'espace de <paramref name="ps"/> (row-vector).</summary>
    static Matrix4x4 Rel(NifAv av, NifAv ps) => av.BindWorld * Inv(ps.BindWorld);

    public static Result Map(NifFile nif, NifParticleSystem ps)
    {
        var r = new Result { WorldSpace = ps.WorldSpace };
        var e = ps.Of<NifPSysEmitter>().FirstOrDefault();
        if (e == null) { r.Notes.Add("no-emitter"); r.Def = new ParticleSystemDef { BirthRate = 0 }; return r; }

        var emitter = BuildEmitter(nif, ps, e, r);
        var curves = new Dictionary<PsParam, FxCurve>();
        foreach (var c in ps.ControllerList)
        {
            PsParam? pm = c.Type switch
            {
                "NiPSysEmitterCtlr" => PsParam.BirthRate,
                "NiPSysEmitterSpeedCtlr" => PsParam.Speed,
                "NiPSysEmitterLifeSpanCtlr" => PsParam.LifeSpan,
                "NiPSysEmitterInitialRadiusCtlr" => PsParam.InitialRadius,
                "NiPSysGravityStrengthCtlr" => PsParam.GravityStrength,
                "NiPSysEmitterDeclinationCtlr" => PsParam.Declination,
                "NiPSysEmitterDeclinationVarCtlr" => PsParam.DeclinationVar,
                "NiPSysEmitterPlanarAngleVarCtlr" => PsParam.PlanarAngleVar,
                "NiPSysInitialRotSpeedCtlr" => PsParam.RotSpeed,
                "NiPSysInitialRotSpeedVarCtlr" => PsParam.RotSpeedVar,
                _ => null,
            };
            if (pm is not { } p) continue;
            var tm = CtlTiming.Of(c);
            var ch = NifCurves.FloatOf(c.Interpolator);
            if (ch != null) curves[p] = new FxCurve { Chan = ch, Timing = tm };
            else if (c.Interpolator != null) r.Notes.Add("curve-unsupported:" + c.Interpolator.Type);
            if (c.Type == "NiPSysEmitterCtlr" && NifCurves.FloatOf(c.VisibilityInterpolator) is { } vis)
                curves[PsParam.EmitterActive] = new FxCurve { Chan = vis, Timing = tm };
        }

        var ad = ps.Of<NifPSysAgeDeath>().FirstOrDefault();
        var sp = ps.Of<NifPSysSpawn>().FirstOrDefault();
        SpawnDef? spawn = sp == null ? null : new SpawnDef
        {
            Generations = sp.NumSpawnGenerations,
            Percentage = sp.PercentageSpawned,
            MinNum = sp.MinNumToSpawn, MaxNum = Math.Max(sp.MinNumToSpawn, sp.MaxNumToSpawn),
            SpeedFactor = 1f, SpeedChaos = sp.SpawnSpeedVariation,
            DirectionChaos = sp.SpawnDirVariation,
            LifeSpan = sp.LifeSpan, LifeSpanVar = sp.LifeSpanVariation,
            OnDeath = ad?.SpawnOnDeath ?? false,
        };
        var gf = ps.Of<NifPSysGrowFade>().FirstOrDefault();
        var rot = ps.Of<NifPSysRotation>().FirstOrDefault();
        var col = ps.Of<NifPSysColor>().FirstOrDefault();
        var bu = ps.Of<NifPSysBoundUpdate>().FirstOrDefault();

        var grav = new List<GravityDef>();
        foreach (var g in ps.Of<NifPSysGravity>())
        {
            var go = nif.AvByBlock.GetValueOrDefault(g.GravityObjectBlock);
            var m = go != null ? Rel(go, ps) : Matrix4x4.Identity;
            var axis = Vector3.TransformNormal(g.Axis, m);
            grav.Add(new GravityDef
            {
                Axis = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : -Vector3.UnitZ,
                Strength = g.Strength, Decay = g.Decay,
                Type = g.ForceType == 1 ? GravityType.Point : GravityType.ForceField,
                Position = m.Translation,
            });
        }

        var cols = new List<PlanarColliderDef>();
        foreach (var cm in ps.Of<NifPSysColliderManager>())
            foreach (var c in cm.Colliders)
            {
                var co = nif.AvByBlock.GetValueOrDefault(c.ColliderObjectBlock);
                var m = co != null ? Rel(co, ps) : Matrix4x4.Identity;
                var n = Vector3.TransformNormal(Vector3.Cross(c.XAxis, c.YAxis), m);
                if (n.LengthSquared() < 1e-12f) continue;
                cols.Add(new PlanarColliderDef
                {
                    Point = m.Translation, Normal = Vector3.Normalize(n), Bounce = c.Bounce,
                    Width = c.Width, Height = c.Height,
                    SpawnOnCollide = c.SpawnOnCollide, DieOnCollide = c.DieOnCollide,
                });
            }

        ColorKey[]? ck = null;
        if (col?.Colors is { Count: > 0 } kc)
            ck = Enumerable.Range(0, kc.Count).Select(i => new ColorKey(kc.Times[i], kc.Values[i])).ToArray();

        int max = Math.Clamp(ps.Data?.MaxParticles ?? 256, 1, 4096);
        r.Def = new ParticleSystemDef
        {
            Emitter = emitter,
            BirthRate = 0f,
            LocalSpace = !ps.WorldSpace,
            MaxCount = max,
            Spawn = spawn,
            GrowFade = gf == null ? null : new GrowFadeDef
            {
                GrowTime = gf.GrowTime, FadeTime = gf.FadeTime,
                GrowGeneration = gf.GrowGeneration, FadeGeneration = gf.FadeGeneration,
            },
            Rotation = rot == null ? null : new RotationDef
            {
                InitialSpeed = rot.Speed, InitialSpeedVar = rot.SpeedVariation,
                InitialAngle = rot.Angle, InitialAngleVar = rot.AngleVariation,
                RandomSign = rot.RandomSpeedSign,
            },
            Colors = ck,
            Gravities = grav.ToArray(),
            Colliders = cols.ToArray(),
            BoundUpdateSkip = bu == null ? -1 : bu.UpdateSkip,
            Curves = curves,
            ResetOnLoop = ps.ControllerList.Any(c => c.Type == "NiPSysResetOnLoopCtlr"),
        };
        if (!curves.ContainsKey(PsParam.BirthRate)) r.Notes.Add("no-birthrate");
        return r;
    }

    static EmitterDef BuildEmitter(NifFile nif, NifParticleSystem ps, NifPSysEmitter e, Result r)
    {
        // Direction d'émission : Gamebryo = axe Z local de l'objet émetteur
        // (déclinaison mesurée depuis Z) — hypothèse, voir notes de validation.
        Matrix4x4 frame = Matrix4x4.Identity;
        if (e.Shape != NifEmitterShape.Mesh &&
            nif.AvByBlock.TryGetValue(e.EmitterObjectBlock, out var obj))
            frame = Rel(obj, ps);

        var def = new EmitterDef
        {
            Shape = (EmitterShape)(int)e.Shape,
            BoxSize = new Vector3(e.Width, e.Height, e.Depth),
            SphereRadius = e.Radius,
            Axis = Vector3.UnitZ,
            Frame = frame,
            Speed = e.Speed, SpeedVar = e.SpeedVariation,
            Declination = e.Declination, DeclinationVar = e.DeclinationVariation,
            PlanarAngle = e.PlanarAngle, PlanarAngleVar = e.PlanarAngleVariation,
            InitialRadius = e.InitialRadius, RadiusVar = e.RadiusVariation,
            LifeSpan = e.LifeSpan, LifeSpanVar = e.LifeSpanVariation,
            InitialColor = e.InitialColor,
        };
        if (e.Shape != NifEmitterShape.Mesh) return def;

        var pts = new List<Vector3>(); var nrm = new List<Vector3>(); var tris = new List<int>();
        var inv = Inv(ps.BindWorld);
        EmitterSkin? skin = null;
        foreach (var mb in e.MeshBlocks)
        {
            var sh = nif.Shapes.FirstOrDefault(s => s.BlockIndex == mb);
            if (sh == null) { r.Notes.Add("mesh-emitter-missing-shape"); continue; }
            int b0 = pts.Count;
            foreach (var v in sh.Vertices) pts.Add(Vector3.Transform(v, inv));
            for (int i = 0; i < sh.Vertices.Length; i++)
                nrm.Add(sh.Normals != null && i < sh.Normals.Length
                    ? SafeNorm(Vector3.TransformNormal(sh.Normals[i], inv)) : Vector3.Zero);
            foreach (var t in sh.Indices) tris.Add(b0 + t);
            // Shape skinné : les verts cuits ci-dessus sont la pose de
            // bind — le runtime re-pose par frame via <see cref="EmitterSkin"/>.
            if (sh.Skin is { } sk && sh.LocalVertices is { } lv && lv.Length == sh.Vertices.Length)
            {
                skin ??= new EmitterSkin();
                skin.Parts.Add(new EmitterSkin.Part
                {
                    MeshBlock = mb, BaseVertex = b0,
                    BoneBlocks = sk.BoneBlocks, BoneSkin = sk.BoneSkin,
                    Joints = sk.Joints, Weights = sk.Weights,
                    LocalVerts = lv, LocalNormals = sh.LocalNormals,
                });
            }
        }
        r.Skin = skin;
        // EmitFrom : 0 sommets, 1 centre de face, 2 centre d'arête, 3 surface de face, 4 surface d'arête.
        // Seuls 0 (sommets) et 3 (surface, 98 % du corpus) sont rendus fidèlement.
        bool surface = e.EmitFrom != 0 && tris.Count >= 3;
        if (e.EmitFrom is 1 or 2 or 4) r.Notes.Add("emitfrom-approx:" + e.EmitFrom);
        return new EmitterDef
        {
            Shape = EmitterShape.Mesh, MeshPoints = pts.ToArray(),
            MeshNormals = nrm.ToArray(),
            MeshTriangles = surface ? tris.ToArray() : null,
            Axis = Vector3.UnitZ,
            RandomDirection = e.VelocityType == 1,
            Speed = e.Speed, SpeedVar = e.SpeedVariation,
            Declination = e.Declination, DeclinationVar = e.DeclinationVariation,
            PlanarAngle = e.PlanarAngle, PlanarAngleVar = e.PlanarAngleVariation,
            InitialRadius = e.InitialRadius, RadiusVar = e.RadiusVariation,
            LifeSpan = e.LifeSpan, LifeSpanVar = e.LifeSpanVariation,
            InitialColor = e.InitialColor,
        };
    }

    static Vector3 SafeNorm(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
}

/// <summary>Skinning d'un émetteur mesh (NiPSysMeshEmitter sur géométries
/// skinnées — pattes/queue/corps des montures r014/r253) : sommets et
/// normales en espace skin + top-4 poids d'os, pour re-poser
/// <c>EmitterDef.MeshPoints/MeshNormals</c> à chaque frame au lieu de
/// rester en pose de bind. Pur C# : les matrices d'os sont fournies par
/// l'appelant (Skeleton3D côté moteur, <see cref="KfPose"/> en test).</summary>
public sealed class EmitterSkin
{
    /// <summary>Un bloc de <c>NifPSysEmitter.MeshBlocks</c> skinné.</summary>
    public sealed class Part
    {
        /// <summary>Index de bloc NIF du shape émetteur (résolution du
        /// Skeleton3D côté moteur).</summary>
        public int MeshBlock;
        /// <summary>Premier sommet du sous-mesh dans MeshPoints/MeshNormals.</summary>
        public int BaseVertex;
        /// <summary>Blocs NIF des os (NiSkinInstance) — diagnostic + repli par nom.</summary>
        public int[] BoneBlocks = Array.Empty<int>();
        /// <summary>Transform espace-skin → espace-os par os (row-vector, NiSkinData).</summary>
        public Matrix4x4[] BoneSkin = Array.Empty<Matrix4x4>();
        /// <summary>4 indices d'os (dans BoneSkin) par sommet.</summary>
        public ushort[] Joints = Array.Empty<ushort>();
        /// <summary>4 poids par sommet (renormalisés à 1).</summary>
        public float[] Weights = Array.Empty<float>();
        /// <summary>Sommets / normales en espace skin (NifShape.Local*).</summary>
        public Vector3[] LocalVerts = Array.Empty<Vector3>();
        public Vector3[]? LocalNormals;
        internal Matrix4x4[]? _work;
    }

    public List<Part> Parts = new();

    /// <summary>Re-pose <paramref name="pts"/>/<paramref name="nrms"/> à la
    /// pose courante. <paramref name="boneWorld"/>[p][k] = transform monde
    /// de l'os k du sous-mesh p, exprimé dans l'espace des points
    /// (row-vector) ; le produit <c>BoneSkin × boneWorld</c> est fait ici,
    /// même formule que <see cref="KfPose.Skin"/>. Entrée null = sous-mesh
    /// non résolu côté moteur : ses sommets gardent la pose de bind cuite
    /// au mapping (comportement historique).</summary>
    public void Reskin(Matrix4x4[]?[] boneWorld, Vector3[] pts, Vector3[]? nrms)
    {
        for (var pi = 0; pi < Parts.Count; pi++)
        {
            var bw = boneWorld[pi];
            if (bw == null) continue;
            var p = Parts[pi];
            var nb = Math.Min(bw.Length, p.BoneSkin.Length);
            var m = p._work ??= new Matrix4x4[p.BoneSkin.Length];
            for (var k = 0; k < nb; k++) m[k] = p.BoneSkin[k] * bw[k];
            var lv = p.LocalVerts;
            var ln = p.LocalNormals;
            var nv = Math.Min(lv.Length, p.Weights.Length / 4);
            for (var v = 0; v < nv && p.BaseVertex + v < pts.Length; v++)
            {
                var acc = Vector3.Zero;
                var nacc = Vector3.Zero;
                for (var q = 0; q < 4; q++)
                {
                    var w = p.Weights[v * 4 + q];
                    if (w <= 0) continue;
                    var j = p.Joints[v * 4 + q];
                    if (j >= nb) continue;
                    acc += w * Vector3.Transform(lv[v], m[j]);
                    if (ln != null)
                        nacc += w * Vector3.TransformNormal(ln[v], m[j]);
                }
                pts[p.BaseVertex + v] = acc;
                if (nrms != null && p.BaseVertex + v < nrms.Length)
                    nrms[p.BaseVertex + v] = ln == null || nacc.LengthSquared() < 1e-10f
                        ? Vector3.Zero : Vector3.Normalize(nacc);
            }
        }
    }
}
