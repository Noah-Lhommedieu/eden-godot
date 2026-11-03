namespace EdenClient.Protocol;

/// <summary>Logique pure (testée par NetTests) : quelles quêtes du
/// registre 348 / des updates 391 sont « en cours » pour le tracker.
/// Le serveur ne met dans la map « en cours » que les FMS 3/4 ; un slot
/// mission_trace peut traîner après validation (DB test123 : trace=1009
/// alors que FMS 1009 = 0 / 1010 = 1) et ne doit PAS faire réapparaître
/// la quête.</summary>
public static class QuestStateLogic
{
    /// <summary>FMS 3 = acceptée, 4 = objectifs remplis (à rendre).</summary>
    public static bool IsOngoing(int fms) => fms is 3 or 4;

    /// <summary>Quête affichable en cours : FMS 3/4 dans le registre, ou
    /// (registre sans entrée) présente dans la map « en cours » du 348.
    /// FMS 0 (jamais prise / reset), 1 (faite), 5 (checked), 2/6/7 :
    /// jamais affichée, même si son slot de suivi est non nul.</summary>
    public static bool IsLive(int mid, IReadOnlyDictionary<int, int> fms,
        ISet<int> inProgress) =>
        fms.TryGetValue(mid, out var st) ? IsOngoing(st)
            : inProgress.Contains(mid);

    /// <summary>Quêtes à afficher : slots de suivi vivants (prioritaires),
    /// sinon toutes les quêtes en cours.</summary>
    public static List<int> Visible(IEnumerable<int> trace,
        IEnumerable<int> inProgress, IReadOnlyDictionary<int, int> fms)
    {
        var prog = new HashSet<int>(inProgress);
        var res = trace.Where(m => m > 0 && IsLive(m, fms, prog))
            .Distinct().ToList();
        if (res.Count == 0)
            res = prog.Where(m => IsLive(m, fms, prog)).ToList();
        return res;
    }
}
