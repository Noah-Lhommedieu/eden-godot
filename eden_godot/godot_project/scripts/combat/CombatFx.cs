namespace EdenClient.Combat;

/// <summary>
/// Traduction pure d'un 272 ClientMeleeAttack en noms d'anims —
/// séparée du routing Godot pour être testable dans NetTests.
///
/// Sémantique wire (serveur <c>ZoneEntryFlow.Combat.cs</c>) :
///   - <c>HitType</c> : 1 = hit, 3 = premier coup, 5 = coup fatal
///     (toujours émis avec kfId −1), 8 = miss ; +0x10 block /
///     +0x100 parry vus en capture.
///   - <c>MotionKfId</c> = kfId d'attaque pioché dans le KFM de
///     l'attaquant (<c>CKfm::GetAttackID</c>) ; <b>−1 = pas de motion
///     d'attaque</b> (twin/dégâts echo OU coup fatal) — on ne joue
///     alors aucune anim d'attaque (comportement natif documenté
///     serveur : « y mettre des HP supprimait toute anim » en miroir).
///
/// Choix des anims :
///   - attaque : <see cref="MotionTable.Lookup"/> sur le stem KFM de
///     l'attaquant (« m025 », lettre corps « g »…) → nom exact
///     (« attack01 », « attack12 »…). Inconnu → « attack01 ».
///   - blessure : « hurt01 » générique (HYPOTHESIS — le natif pioche
///     dans le pool hurtNN du KFM de la CIBLE, non transmis par le
///     wire ; hurt01 couvre tous les modèles).
///   - fatal : « death01 » (la victime marque Dying ; le despawn
///     cadavre arrive ~5 s après via 269).
///   - miss (8) : pas d'anim de blessure (l'attaque swinge quand même).
/// </summary>
public static class CombatFx
{
    /// <summary>Type de coup : miss (aucune blessure jouée).</summary>
    public const int HitMiss = 8;
    /// <summary>Type de coup : fatal (la cible meurt — anim death).</summary>
    public const int HitFatal = 5;

    /// <summary>Vrai si le coup tue la cible (type 5 — le serveur
    /// l'émet avec kfId=-1 juste avant le 305 ClientDie).</summary>
    public static bool IsFatal(int hitType) => (hitType & 0xFF) == HitFatal;

    /// <summary>Vrai si le coup est un miss (type 8 — la cible
    /// n'esquive visuellement que par l'absence de hurt).</summary>
    public static bool IsMiss(int hitType) => (hitType & 0xFF) == HitMiss;

    /// <summary>Nom d'anim d'attaque pour l'attaquant :
    /// kfId résolu via <paramref name="motions"/> sur
    /// <paramref name="modelStem"/> ; -1/inconnu → fallback
    /// « attack01 » SAUF si kfId&lt;0 (pas de motion → null).</summary>
    public static string? AttackAnim(MotionTable? motions,
        string? modelStem, int kfId)
    {
        if (kfId < 0) return null;              // twin/fatal : aucune motion
        var n = motions?.Lookup(modelStem, kfId);
        if (n != null) return n;
        // kfId connu de la table native mais stem non couvert (perso
        // distant sans lettre corps, modèle non listé) : « attack01 »
        // est l'anim générique présente sur tous les KFM.
        return "attack01";
    }

    /// <summary>Nom d'anim de réaction pour la cible :
    /// fatal → « death01 » ; miss → null ; sinon « hurt01 ».</summary>
    public static string? HitAnim(int hitType)
    {
        if (IsFatal(hitType)) return "death01";
        if (IsMiss(hitType)) return null;
        return "hurt01";
    }
}
