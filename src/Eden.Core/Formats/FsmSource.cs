using EdenClient.Vfs;

namespace EdenClient.World;

/// <summary>
/// Choix du <c>.fsm</c> intact parmi plusieurs racines. Les 15 fsm « V7 »
/// de <c>corpus/fr-full</c> (s002, s003, s004, s005, s010, s011, s021,
/// s030, s032, s051, s054, s119, s225, s307, s501) sont des copies
/// corrompues (LAY0 + GRL/GRS/OBJ écrasés → « queue non framée ») ; les
/// originaux EDENFR (<c>~/Téléchargements/EDENFR/extracted</c>) sont
/// entièrement framés (LAY0, GRL, GRS, OBJ, 0 octet de queue). Voir
/// <c>docs/flax/V7-PROPS-SOURCE.md</c>.
/// </summary>
public static class FsmSource
{
    /// <summary>Premier chemin dont le fsm est intact (queue = 0 et au
    /// moins un record OBJ ou GRS) ; à défaut, le premier qui parse
    /// (terrain seul). Null si aucun ne s'ouvre.</summary>
    public static string? PickIntact(IEnumerable<string> candidates)
    {
        string? fallback = null;
        foreach (var path in candidates)
        {
            FsmFile f;
            try { f = FsmFile.Load(path); }
            catch (Exception) { continue; }
            if (f.TrailingBytes == 0 &&
                (f.SceneObjects.Count > 0 || f.GrassInstances.Count > 0))
                return path;
            fallback ??= path;
        }
        return fallback;
    }

    /// <summary>Chemins <c>rel</c> existants sous chaque racine, dans
    /// l'ordre des racines (insensible à la casse).</summary>
    public static IEnumerable<string> Candidates(
        IEnumerable<string> roots, string rel)
    {
        foreach (var r in roots)
            if (EdenVfs.ResolveInsensitive(r, rel) is { } hit)
                yield return hit;
    }
}
