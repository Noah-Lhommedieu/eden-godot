using System;

namespace EdenClient.Game;

/// <summary>
/// Convention de « direction » (dir, entité+0x58) du protocole Eden —
/// prouvée dans le serveur natif : <c>ILifeEntity::ChangeFaceDir</c>
/// (zone @0x0847a3b0) écrit <c>dir = C − atan2(Δy, Δx)</c> avec
/// <c>C = 3π/2</c> (double @0x08877088, lu dans le binaire), et les
/// transformations locales→monde utilisent
/// <c>sincosf(C − dir)</c> (@0x087xxxx, l.790323 / 999705 du décompilé).
/// Le cap monde (angle depuis +X, sens trigo) est donc
/// <c>α = 3π/2 − dir</c> : dir 0 = face au sud (−Y), croît dans le
/// sens horaire vu du dessus. Un modèle à rotation nulle regarde +Y
/// Eden (= −Z Godot), cf. SkinTest.
/// </summary>
public static class EdenDir
{
    public const float C = 3f * MathF.PI / 2f;

    /// <summary>dir Eden pour un déplacement (Δx, Δy) — équivalent de
    /// <c>ChangeFaceDir</c>.</summary>
    public static float FromDelta(float dx, float dy) =>
        Wrap(C - MathF.Atan2(dy, dx));

    /// <summary>Cap monde α (angle depuis +X) d'un dir.</summary>
    public static float HeadingOf(float dir) => C - dir;

    /// <summary>Yaw Godot (rotation Y du nœud, modèle regardant −Z à
    /// yaw 0) pour un dir Eden.</summary>
    public static float ToYaw(float dir)
    {
        var a = HeadingOf(dir);
        var dx = MathF.Cos(a);          // eden x = godot x
        var dz = -MathF.Sin(a);         // godot z = −eden y
        return MathF.Atan2(-dx, -dz);
    }

    /// <summary>dir Eden pour un yaw Godot (inverse de
    /// <see cref="ToYaw"/>) — émis dans MoveSelf/TurnSelf.</summary>
    public static float FromYaw(float yaw)
    {
        // forward godot = (−sinψ, −cosψ) → eden (x, y) = (−sinψ, cosψ).
        return FromDelta(-MathF.Sin(yaw), MathF.Cos(yaw));
    }

    private static float Wrap(float a)
    {
        var t = MathF.PI * 2f;
        a %= t;
        return a < 0 ? a + t : a;
    }
}
