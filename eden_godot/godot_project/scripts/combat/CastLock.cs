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
