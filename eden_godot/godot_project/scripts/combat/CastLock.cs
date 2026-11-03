namespace EdenClient.Combat;

/// <summary>
/// Échéance du verrou de cast client (pure, testable sans Godot).
/// Le verrou est posé à l'envoi du 9/16/17 et normalement levé par
/// 281/287/410 ; le serveur peut ne jamais répondre (cible tuée avant
/// l'impact, TP en plein cast, paquet perdu) — sans échéance plus aucun
/// sort ne partait (« impossible de lancer des sorts après un combat
/// ou un tp »).
/// </summary>
public sealed class CastLock
{
    /// <summary>9/16/17 envoyé, 279 pas encore reçu (ms).</summary>
    public const ulong RequestGraceMs = 3000;
    /// <summary>Marge après 279 + temps de cast : 281 attendu (ms).</summary>
    public const ulong ResolveGraceMs = 2500;

    private ulong _deadline;

    /// <summary>Demande envoyée à <paramref name="nowMs"/>.</summary>
    public void Requested(ulong nowMs) => _deadline = nowMs + RequestGraceMs;

    /// <summary>279 OK reçu : barre de cast <paramref name="castDs"/>
    /// décisecondes (0 = instantané).</summary>
    public void Acknowledged(ulong nowMs, int castDs) =>
        _deadline = nowMs + ResolveGraceMs +
                    (ulong)Math.Max(0, castDs) * 100UL;

    /// <summary>Verrou levé normalement (281/287/410).</summary>
    public void Released() => _deadline = 0;

    /// <summary>Vrai si le verrou est resté posé au-delà de l'échéance.</summary>
    public bool Expired(ulong nowMs) => _deadline != 0 && nowMs >= _deadline;
}

/// <summary>Décision de l'auto-attaque à la fin d'un cast
/// (<see cref="PostCastRules.Decide"/>).</summary>
public enum PostCastAttack
{
    /// <summary>Pas de cible → rien à reprendre.</summary>
    None,
    /// <summary>Cible déjà à portée → reprise normale (swing,
    /// puis poursuite si elle repart — engagement explicite).</summary>
    Resume,
    /// <summary>Cible hors de portée → engagement armé mais AUCUN
    /// déplacement vers elle (natif : l'auto-attaque après un sort
    /// ne court pas ; le swing repart dès qu'elle revient à portée).</summary>
    Hold,
}

/// <summary>
/// Règles d'état cast → auto-attaque du client EE (pure, testable
/// sans Godot — posées ici pour NetTests, consommées par AutoAttack).
///
/// Preuves natives :
///   - <b>Pas d'auto-attaque pendant un cast</b> : une attaque pendant
///     l'incantation CASSE le cast — <c>CPlayerFactory::MeleeAttack</c>
///     zone @0x080f6930 : bit cast de char+0x3b → <c>ISpell::Break</c>
///     (287). Le client retient donc toute émission NCID 5 pendant la
///     fenêtre de cast, alors que le déplacement reste libre
///     (cast mobile — <c>CheckCastBreak</c> désactivé côté serveur).
///   - <b>Reprise seulement à portée</b> : option client
///     <c>AutoReAttackb</c> (client.ini, byte opt+0xe3 bit 7 —
///     FUN_0069e290) ; le re-attack post-action passe par la commande
///     5 du joueur (FUN_007624d0) sans poursuite — le joueur ne se
///     déplace pas vers une cible hors de portée après un sort.
/// </summary>
public static class PostCastRules
{
    /// <summary>L'auto-attaque (swing ET poursuite) est gelée tant
    /// qu'un cast est en vol — <paramref name="casting"/> =
    /// <c>SkillCast.IsCasting</c> (demande émise → 281/287/410/302).</summary>
    public static bool AutoAttackFrozen(bool casting) => casting;

    /// <summary>Décision à la fin d'un cast selon la distance :
    /// à portée → <see cref="PostCastAttack.Resume"/> ; hors de
    /// portée → <see cref="PostCastAttack.Hold"/> (armée, immobile) ;
    /// sans cible → <see cref="PostCastAttack.None"/>.</summary>
    public static PostCastAttack Decide(bool hasTarget, float distance,
        float reach)
    {
        if (!hasTarget) return PostCastAttack.None;
        return distance <= reach
            ? PostCastAttack.Resume : PostCastAttack.Hold;
    }
}
