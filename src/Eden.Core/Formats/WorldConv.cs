using System.Numerics;

namespace EdenClient.World;

/// <summary>
/// Conversion d'axes monde Eden → repère Y-up moteur.
///
/// Le monde .fsm (comme les NIF) est en **Z-up right-handed** : x, y =
/// plan du sol, z = hauteur. Godot/Flax sont en **Y-up**.
/// Convention retenue (cohérente avec la décision de l'équipe
/// nif2gltf « géométrie Z-up brut ») :
///
///   Yup (x, y, z) = (eden.x, eden.z, −eden.y)
///
/// produit un repère Y-up **right-handed** (−Z = « avant », convention
/// Godot). Flax est left-handed (+Z avant) : la frontière moteur dans
/// Eden.Game applique le flip de handedness ; cette classe reste
/// purement numérique.
/// Voir docs/specs/world_conventions.md.
/// </summary>
public static class WorldConv
{
    /// <summary>Rotation −90° sur X (degrés) — transforme un repère
    /// Z-up en Y-up : (x,y,z) → (x, z, −y).</summary>
    public static readonly Vector3 EdenRootEulerDeg = new(-90f, 0f, 0f);

    /// <summary>Point Eden (x,y,z) → position Y-up right-handed.</summary>
    public static Vector3 ToYup(float ex, float ey, float ez) =>
        new(ex, ez, -ey);

    /// <summary>Point Eden → position Y-up right-handed.</summary>
    public static Vector3 ToYup(Vector3 e) => new(e.X, e.Z, -e.Y);

    /// <summary>Position Y-up → coords sol Eden (x, y).</summary>
    public static (float X, float Y) ToEdenGround(Vector3 g) =>
        (g.X, -g.Z);

    /// <summary>Position Y-up → point Eden (x, y, z).</summary>
    public static Vector3 ToEden(Vector3 g) => new(g.X, -g.Z, g.Y);

    /// <summary>Rayon caméra Y-up → rayon Eden (pour le raycast
    /// heightfield de <see cref="TerrainSampler"/>).</summary>
    public static void ToEdenRay(Vector3 origin, Vector3 dir,
        out Vector3 eOrigin, out Vector3 eDir)
    {
        eOrigin = ToEden(origin);
        eDir = ToEden(dir);
    }
}
