namespace EdenClient.Game;

/// <summary>Vitesse de déplacement du joueur local : PILOTÉE PAR LE
/// SERVEUR uniquement. Le serveur calcule <c>Movement()</c> (base
/// native × buffs plafonnés × monture, voir
/// <c>NativeCombatFormulas.Movement</c>) et l'envoie dans 296
/// <c>Speeds[0]</c> ; le client ne calcule aucun bonus (monture, buff,
/// AGI…) : il convertit seulement l'unité native en unités monde.
/// Pas de profil reçu → 1 (vitesse de base).</summary>
public static class ServerMoveSpeed
{
    /// <summary>Movement natif à vitesse de base (18.85 × 0.5) =
    /// <c>NativeCombatFormulas.BaseMovement</c> côté serveur.</summary>
    public const float NativeBase = 9.425f;

    /// <summary>Multiplicateur appliqué aux vitesses monde de base du
    /// contrôleur : Speeds[0] / <see cref="NativeBase"/>. Garde-fou de
    /// sanité seulement (0.05..50) — aucun plafond de gameplay ici.</summary>
    public static float Multiplier(float[]? speeds)
    {
        if (speeds == null || speeds.Length == 0 || speeds[0] <= 0f ||
            float.IsNaN(speeds[0]) || float.IsInfinity(speeds[0]))
            return 1f;
        var m = speeds[0] / NativeBase;
        return m < 0.05f ? 0.05f : m > 50f ? 50f : m;
    }
}
