namespace EdenClient.Game;

/// <summary>Vitesse de lecture des anims de déplacement, proportionnelle
/// à la vitesse de déplacement du joueur (spec propriétaire) :
/// monté, 100 % de move speed = anim normale (×1), 150 % = ×1.5 ;
/// à pied même règle mais la référence est 20 % (×1 à 20 %, ×2 à 40 %).
/// « Move speed » = bonus affiché (<c>MovementRate</c> serveur =
/// (Speeds[0]/<see cref="ServerMoveSpeed.NativeBase"/> − 1) × 100).
/// Planché à ×1 (une anim de marche ralentie sous la référence n'existe
/// pas dans la spec) et plafonné à <see cref="MaxScale"/>.</summary>
public static class MoveAnimSpeed
{
    public const float RefMountedPct = 100f;
    public const float RefFootPct = 20f;
    public const float MaxScale = 3f;

    /// <summary>Bonus de move speed en % depuis le multiplicateur serveur
    /// (<see cref="ServerMoveSpeed.Multiplier"/>).</summary>
    public static float BonusPct(float serverMul) => (serverMul - 1f) * 100f;

    public static float Scale(float bonusPct, bool mounted)
    {
        if (float.IsNaN(bonusPct)) return 1f;
        var s = bonusPct / (mounted ? RefMountedPct : RefFootPct);
        return s < 1f ? 1f : s > MaxScale ? MaxScale : s;
    }
}
