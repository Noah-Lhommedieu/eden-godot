using System.Numerics;

namespace EdenClient.Fx;

// Paramètres purs d'un système de particules Gamebryo (NiPSys*). Aucune
// dépendance à NifFile ni à Godot : l'adaptateur NIF remplit ces structures.
// Tous les angles sont en radians, les durées en secondes.

public enum EmitterShape { Box, Sphere, Mesh }

public enum GravityType
{
    /// <summary>Accélération constante le long de l'axe (gravité « monde »).</summary>
    World,
    /// <summary>Attraction vers <see cref="GravityDef.Position"/> (force ponctuelle),
    /// atténuée par exp(-Decay × distance).</summary>
    Point,
    /// <summary>Champ de force plan : atténué avec la distance au plan (Decay).</summary>
    ForceField,
}

/// <summary>Émetteur (NiPSysBox/Sphere/MeshEmitter). Valeurs « ± var » :
/// tirage uniforme symétrique dans [x - var, x + var].</summary>
public sealed class EmitterDef
{
    public EmitterShape Shape { get; init; } = EmitterShape.Box;
    /// <summary>Boîte : dimensions complètes (X, Y, Z), centrée sur l'origine.</summary>
    public Vector3 BoxSize { get; init; } = Vector3.Zero;
    public float SphereRadius { get; init; }
    /// <summary>Mesh : points de la surface (sommets), tirés au hasard.</summary>
    public Vector3[]? MeshPoints { get; init; }
    /// <summary>Mesh : normales parallèles aux points (axe d'émission local).
    /// Absent : axe fixe <see cref="Axis"/>.</summary>
    public Vector3[]? MeshNormals { get; init; }
    /// <summary>Axe d'émission local (supposé +X comme Gamebryo).</summary>
    public Vector3 Axis { get; init; } = Vector3.UnitX;
    /// <summary>Repère de l'objet émetteur dans l'espace du système (position
    /// de naissance et direction y sont passées). Identité par défaut.</summary>
    public Matrix4x4 Frame { get; init; } = Matrix4x4.Identity;
    /// <summary>Direction d'émission aléatoire sur la sphère (vitesse « random »).</summary>
    public bool RandomDirection { get; init; }
    /// <summary>Mesh : triangles (3 indices par face) sur <see cref="MeshPoints"/>.
    /// Présents : naissance à un point aléatoire de la surface, axe = normale
    /// de la face. Absents : sommets seuls.</summary>
    public int[]? MeshTriangles { get; init; }

    public float Speed { get; init; } = 1f;
    public float SpeedVar { get; init; }
    public float Declination { get; init; }
    public float DeclinationVar { get; init; }
    public float PlanarAngle { get; init; }
    public float PlanarAngleVar { get; init; }
    /// <summary>« Initial Radius » : taille (rayon) du billboard à la naissance.</summary>
    public float InitialRadius { get; init; } = 1f;
    public float RadiusVar { get; init; }
    public float LifeSpan { get; init; } = 1f;
    public float LifeSpanVar { get; init; }
    public Vector4 InitialColor { get; init; } = Vector4.One;
}

/// <summary>NiPSysSpawnModifier : enfants à la mort (ou à la collision).</summary>
public sealed class SpawnDef
{
    /// <summary>Une particule de génération g engendre si g &lt; Generations.</summary>
    public int Generations { get; init; } = 1;
    /// <summary>Probabilité [0..1] de spawn, tirée par particule.</summary>
    public float Percentage { get; init; } = 1f;
    public int MinNum { get; init; } = 1;
    public int MaxNum { get; init; } = 1;
    /// <summary>Vitesse enfant = vitesse parent × SpeedFactor × (1 ± SpeedChaos).</summary>
    public float SpeedFactor { get; init; } = 1f;
    public float SpeedChaos { get; init; }
    /// <summary>Écart angulaire max de la direction par rapport au parent.</summary>
    public float DirectionChaos { get; init; }
    public float LifeSpan { get; init; } = 1f;
    public float LifeSpanVar { get; init; }
    /// <summary>Spawn à la mort naturelle (fin de vie).</summary>
    public bool OnDeath { get; init; } = true;
}

/// <summary>NiPSysGrowFadeModifier : la taille monte pendant GrowTime après la
/// naissance puis retombe à 0 pendant FadeTime avant la mort.</summary>
public sealed class GrowFadeDef
{
    public float GrowTime { get; init; }
    public float FadeTime { get; init; }
    /// <summary>Générations ≤ à cette valeur subissent le grow / fade.</summary>
    public int GrowGeneration { get; init; } = int.MaxValue;
    public int FadeGeneration { get; init; } = int.MaxValue;
}

/// <summary>NiPSysRotationModifier.</summary>
public sealed class RotationDef
{
    public float InitialSpeed { get; init; }
    public float InitialSpeedVar { get; init; }
    public float InitialAngle { get; init; }
    public float InitialAngleVar { get; init; }
    public bool RandomSign { get; init; }
}

/// <summary>Clé de couleur sur l'âge normalisé (age / durée de vie).</summary>
public readonly record struct ColorKey(float T, Vector4 Color);

/// <summary>NiPSysGravityModifier.</summary>
public sealed class GravityDef
{
    public Vector3 Axis { get; init; } = -Vector3.UnitZ;
    public float Strength { get; init; } = 9.81f;
    public GravityType Type { get; init; } = GravityType.World;
    /// <summary>ForceField : force × exp(-Decay × distance au plan).</summary>
    public float Decay { get; init; }
    /// <summary>ForceField : point du plan, dans l'espace de la simulation.</summary>
    public Vector3 Position { get; init; }
}

/// <summary>NiPSysPlanarCollider.</summary>
public sealed class PlanarColliderDef
{
    public Vector3 Point { get; init; }
    public Vector3 Normal { get; init; } = Vector3.UnitZ;
    /// <summary>Restitution : 1 = rebond parfait, 0 = glisse.</summary>
    public float Bounce { get; init; } = 1f;
    /// <summary>Largeur / hauteur du plan (0 = infini).</summary>
    public float Width { get; init; }
    public float Height { get; init; }
    public bool SpawnOnCollide { get; init; }
    public bool DieOnCollide { get; init; }
}

/// <summary>Paramètres d'émetteur pilotables par contrôleur (NiPSys*Ctlr).</summary>
public enum PsParam
{
    BirthRate, EmitterActive, Speed, LifeSpan, InitialRadius, GravityStrength,
    Declination, DeclinationVar, PlanarAngleVar, RotSpeed, RotSpeedVar,
}

public sealed class ParticleSystemDef
{
    /// <summary>Courbes de contrôleurs, évaluées au temps de simulation.
    /// Un paramètre sans courbe garde la valeur de l'émetteur / de BirthRate.</summary>
    public IReadOnlyDictionary<PsParam, FxCurve> Curves { get; init; } =
        new Dictionary<PsParam, FxCurve>();
    public EmitterDef Emitter { get; init; } = new();
    /// <summary>Particules/seconde (NiPSysEmitterCtlr « birth rate »).</summary>
    public float BirthRate { get; init; } = 10f;
    /// <summary>Particules émises d'un coup au temps 0 (et à chaque boucle).</summary>
    public int InitialBurst { get; init; }
    public float EmitStart { get; init; }
    public float EmitStop { get; init; } = float.PositiveInfinity;
    public bool Loop { get; init; }
    /// <summary>Durée du cycle si Loop (typiquement la fin du contrôleur).</summary>
    public float LoopLength { get; init; }
    /// <summary>NiPSysResetOnLoopCtlr : vide les particules au rebouclage.</summary>
    public bool ResetOnLoop { get; init; }
    /// <summary>true : coordonnées du système (la sortie suit le nœud) ;
    /// false : les particules naissent en monde et ne suivent plus l'émetteur.</summary>
    public bool LocalSpace { get; init; } = true;
    public int MaxCount { get; init; } = 256;

    public SpawnDef? Spawn { get; init; }
    public GrowFadeDef? GrowFade { get; init; }
    public RotationDef? Rotation { get; init; }
    public ColorKey[]? Colors { get; init; }
    public GravityDef[] Gravities { get; init; } = [];
    public PlanarColliderDef[] Colliders { get; init; } = [];
    /// <summary>NiPSysBoundUpdateModifier : recalcul de la boîte toutes les
    /// (n+1) étapes ; -1 = jamais.</summary>
    public int BoundUpdateSkip { get; init; } = -1;
}
