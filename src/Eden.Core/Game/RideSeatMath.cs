using System.Numerics;

namespace EdenClient.Game;

/// <summary>Maths pures du placement cavalier (testées xUnit, sans
/// Godot). Natif : le cavalier devient enfant du nœud L17 de la monture
/// avec un transform local identité (<c>CRide::Attach</c>
/// FUN_0073fc60) → sa racine prend le transform complet de L17.
/// Repère : colonnes de <see cref="Matrix4x4"/> non utilisées ; les
/// bases sont passées en 3 vecteurs colonnes (X, Y, Z).</summary>
public static class RideSeatMath
{
    public readonly record struct Basis3(Vector3 X, Vector3 Y, Vector3 Z)
    {
        public static Basis3 Identity => new(Vector3.UnitX, Vector3.UnitY,
            Vector3.UnitZ);

        public Vector3 Mul(Vector3 v) => X * v.X + Y * v.Y + Z * v.Z;

        public Basis3 Transposed() => new(
            new Vector3(X.X, Y.X, Z.X), new Vector3(X.Y, Y.Y, Z.Y),
            new Vector3(X.Z, Y.Z, Z.Z));

        /// <summary>this × o (appliquer o puis this).</summary>
        public Basis3 Mul(Basis3 o) => new(Mul(o.X), Mul(o.Y), Mul(o.Z));
    }

    /// <summary>Base de la racine cavalier dans le repère monde : celle
    /// de L17 (orthonormale) débarrassée de la rotation d'import GLB de
    /// la monture (le cavalier a la même convention d'import).</summary>
    public static Basis3 RiderBasis(Basis3 seatWorld, Basis3 mountImport) =>
        seatWorld.Mul(mountImport.Transposed());

    /// <summary>Origine monde de la racine cavalier : celle de L17
    /// (transform local identité natif), abaissée de <paramref name=
    /// "drop"/> (axe monde Y, réglage de calage éventuel).</summary>
    public static Vector3 RiderOrigin(Vector3 seatOrigin, float drop) =>
        seatOrigin - new Vector3(0, drop, 0);

    /// <summary>L17 exploitable : écarte les valeurs aberrantes de la
    /// table (JEFF y=35, AR979 à l'origine). Pas de comparaison à la
    /// hauteur du mesh : l'AABB des meshes skinnés est faux au repos
    /// (R021 : top 1,35 pour un L17 à 2,61 valide).</summary>
    public static bool IsPlausibleSeat(Vector3 l17Glb) =>
        l17Glb.Z is > 0.3f and < 30f &&
        MathF.Abs(l17Glb.Y) < 10f && MathF.Abs(l17Glb.X) < 10f;
}
