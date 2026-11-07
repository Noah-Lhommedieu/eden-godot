using System.Collections.Concurrent;

namespace Eden.Core.Logging;

/// <summary>
/// Limiteur de journaux pour les chemins chauds (PLAN-ACTION-3 lot N) : une même clé n'écrit qu'une fois par période.
/// Mesuré : 33 415 lignes « vitesse refusée » en 60 s avec 300 bots, soit du temps de boucle perdu en E/S.
/// </summary>
public static class LogThrottle
{
    private static readonly ConcurrentDictionary<string, long> Last = new();

    /// <summary>Vrai si <paramref name="key"/> n'a pas écrit depuis <paramref name="seconds"/> s (le premier appel écrit toujours).</summary>
    public static bool Allow(string key, double seconds)
    {
        var now = Environment.TickCount64; var period = (long)(seconds * 1000);
        if (Last.Count > 10_000) Last.Clear();                                   // borne mémoire
        var last = Last.GetOrAdd(key, now - period - 1);
        if (now - last < period) return false;
        Last[key] = now; return true;
    }
}
