using EdenClient.Combat;

namespace EdenClient.Scene;

/// <summary>
/// Résolution PURE kfId → nom d'animation pour le NCID 495
/// <c>ClientPlayMonsterAction [u32 handle][s16 kfId]</c> (broadcast de
/// <c>CAI::SetEventAction</c> zone @0x084d5120 : joue la motion KF
/// <c>kfId</c> du KFM du monstre). Source exacte = <see cref="MotionTable"/>
/// (motion.ini : <c>(modèle, kfId) → nom</c>) ; quand le modèle n'est pas
/// listé on retombe sur la convention de nommage vérifiée sur les lignes
/// motion.ini (1011 hurt01, 1021 attack01, 301 magic01, 401 shoot01,
/// 501 condition01, 51 trigger01, 101 move01, 151 move51, 201 death01,
/// 3 stand03). kfId ≤ 0 = pas de motion.
/// </summary>
public static class MonsterActionAnim
{
    /// <summary>Nom d'anim par convention de plage de kfId — null si
    /// la plage est inconnue.</summary>
    public static string? NameFromKfId(int kf)
    {
        static string N(string p, int n) => $"{p}{n:D2}";
        return kf switch
        {
            >= 1 and <= 9 => N("stand", kf),
            >= 51 and <= 59 => N("trigger", kf - 50),
            >= 101 and <= 110 => N("move", kf - 100),
            >= 151 and <= 159 => N("move", 50 + (kf - 150)),
            >= 201 and <= 204 => N("death", kf - 200),
            >= 301 and <= 309 => N("magic", kf - 300),
            >= 401 and <= 410 => N("shoot", kf - 400),
            >= 501 and <= 506 => N("condition", kf - 500),
            >= 1011 and <= 1015 => N("hurt", kf - 1010),
            >= 1021 and <= 1024 => N("attack", kf - 1020),
            _ => null,
        };
    }

    /// <summary>Candidats d'anim dans l'ordre de préférence : motion.ini
    /// exact, puis convention. Vide si kfId ≤ 0 ou inconnu.</summary>
    public static IReadOnlyList<string> Candidates(MotionTable? motions,
        string? modelStem, int kfId)
    {
        if (kfId <= 0) return Array.Empty<string>();
        var list = new List<string>(2);
        if (motions?.Lookup(modelStem, kfId) is { Length: > 0 } exact)
            list.Add(exact);
        if (NameFromKfId(kfId) is { } conv && !list.Contains(conv))
            list.Add(conv);
        return list;
    }
}
