using Godot;

namespace EdenClient.World;

/// <summary>
/// Conversion d'axes monde Eden → Godot.
///
/// Le monde .fsm (comme les NIF) est en **Z-up right-handed** : x, y =
/// plan du sol, z = hauteur. Godot est en **Y-up** avec −Z = « avant ».
/// Convention retenue pour le client (cohérente avec la décision de
/// l'équipe nif2gltf « géométrie Z-up brut ») :
///
///   Godot (x, y, z) = (eden.x, eden.z, −eden.y)
///
/// appliquée soit explicitement via <see cref="ToGodot"/> (entités,
/// joueur), soit par une rotation de −90° sur X d'un Node3D racine
/// (contenu fsm placé en coords natives : terrain, OBJ, herbe).
/// Voir docs/world_conventions.md.
/// </summary>
public static class WorldConv
{
    /// <summary>Rotation −90° sur X — transforme un repère Z-up en Y-up
    /// : (x,y,z) → (x, z, −y). À appliquer sur le Node3D racine du
    /// contenu fsm.</summary>
    public static readonly Vector3 EdenRootEulerDeg = new(-90f, 0f, 0f);

    /// <summary>Point Eden (x,y,z) → position Godot.</summary>
    public static Vector3 ToGodot(float ex, float ey, float ez) =>
        new(ex, ez, -ey);

    /// <summary>Point Eden → position Godot.</summary>
    public static Vector3 ToGodot(System.Numerics.Vector3 e) =>
        new(e.X, e.Z, -e.Y);

    /// <summary>Position Godot → coords sol Eden (x, y).</summary>
    public static (float X, float Y) ToEdenGround(Vector3 g) =>
        (g.X, -g.Z);

    /// <summary>Position Godot → point Eden (x, y, z).</summary>
    public static System.Numerics.Vector3 ToEden(Vector3 g) =>
        new(g.X, -g.Z, g.Y);

    /// <summary>Rayon caméra Godot → rayon Eden (pour le raycast
    /// heightfield de <see cref="TerrainSampler"/>).</summary>
    public static void ToEdenRay(Vector3 origin, Vector3 dir,
        out System.Numerics.Vector3 eOrigin,
        out System.Numerics.Vector3 eDir)
    {
        eOrigin = ToEden(origin);
        eDir = ToEden(dir);
    }
}
