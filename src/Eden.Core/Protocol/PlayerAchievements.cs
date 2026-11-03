namespace EdenClient.Protocol;

/// <summary>
/// État partagé des succès du joueur — rempli par le NCID 480
/// (<see cref="ZoneAchievementUpdate"/>) et consommé par
/// <c>AchievementWindow</c>.
///
/// Sémantique serveur (ZoneEntryFlow.Achievement.cs, désasm zone
/// @0x08209080) : la trame kind 0 emporte les succès COMPLÉTÉS à
/// l'entrée de zone (par paquets de 315) — insert-if-absent ; kind 1
/// est un upsert {id → points cumulés} ; kinds 2/3 portent les totaux
/// catégoriels « points de chasse » (groupes 101-108/201-202, carte
/// cumulative clé 1) et « points de ville » (groupes 501-506, clé 5) ;
/// kinds 4/5 = listes d'ids (town mensuels / divers — non émis par le
/// serveur C# actuel mais parsés pour complétude wire).
///
/// Mutations sur le thread principal uniquement (même convention que
/// <see cref="PlayerBag"/>) — lecture sans lock.
/// </summary>
public static class PlayerAchievements
{
    /// <summary>Émis à chaque trame 480 traitée.</summary>
    public static event Action? Changed;

    /// <summary>achievementId → points cumulés (kinds 0/1).</summary>
    public static IReadOnlyDictionary<int, uint> Points => _points;
    private static readonly Dictionary<int, uint> _points = new();

    /// <summary>achievementId → compteur success_num des succès NON
    /// complétés (extension Eden 480 kind 6 ; 0 = aucune donnée).
    /// </summary>
    public static IReadOnlyDictionary<int, uint> Counters => _counters;
    private static readonly Dictionary<int, uint> _counters = new();

    /// <summary>Compteur courant d'un succès : lorsqu'il est complété
    /// la valeur requise est connue de l'appelant (compteur effacé).
    /// </summary>
    public static uint CounterOf(int achievementId) =>
        _counters.GetValueOrDefault(achievementId);

    /// <summary>Total « points de chasse » (kind 2 — groupes
    /// 101-108/201-202 ; libellé natif « 怪物討伐積分 »).</summary>
    public static uint HuntPoints { get; private set; }

    /// <summary>Total « points de ville » (kind 3 — groupes 501-506 ;
    /// libellé natif « 城鎮成就積分 »).</summary>
    public static uint TownPoints { get; private set; }

    /// <summary>Somme des points par succès — le « x總成就 » du layout
    /// natif (total affiché en haut du panneau gauche).</summary>
    public static long TotalPoints
    {
        get
        {
            long t = 0;
            foreach (var kv in _points) t += kv.Value;
            return t;
        }
    }

    /// <summary>Ids des dernières trames kind 4/5 (succès « town »
    /// mensuels / liste diverse — conservés à titre de télémétrie,
    /// non affichés : le serveur C# ne les émet pas).</summary>
    public static IReadOnlyList<int> LastKind4Ids => _kind4Ids;
    public static IReadOnlyList<int> LastKind5Ids => _kind5Ids;
    private static readonly List<int> _kind4Ids = new();
    private static readonly List<int> _kind5Ids = new();

    /// <summary>Applique une trame 480 (déjà désérialisée).
    /// kind 0 = insert-if-absent (synchro initiale), 1 = upsert,
    /// 2/3 = totaux, 4/5 = listes d'ids.</summary>
    public static void Apply(ZoneAchievementUpdate m)
    {
        switch (m.Kind)
        {
            case 0:
                foreach (var (id, pts) in m.Entries)
                {
                    _points.TryAdd(id, pts);
                    _counters.Remove(id);
                }
                break;
            case 1:
                foreach (var (id, pts) in m.Entries)
                {
                    _points[id] = pts;
                    _counters.Remove(id);
                }
                break;
            case 2:
                HuntPoints = m.Total;
                break;
            case 3:
                TownPoints = m.Total;
                break;
            case 6:
                foreach (var (id, n) in m.Entries)
                    _counters[id] = n;
                break;
            case 4:
                _kind4Ids.Clear();
                _kind4Ids.AddRange(m.Ids);
                break;
            case 5:
                _kind5Ids.Clear();
                _kind5Ids.AddRange(m.Ids);
                break;
            default:
                return;   // kind inconnu : ignorer sans invalider
        }
        Changed?.Invoke();
    }

    /// <summary>Vrai si le succès est connu/complété (présent dans la
    /// map — le serveur n'émet que les complétés et les upserts).</summary>
    public static bool IsCompleted(int achievementId) =>
        _points.ContainsKey(achievementId);

    /// <summary>Points cumulés d'un succès (0 si inconnu).</summary>
    public static uint PointsOf(int achievementId) =>
        _points.GetValueOrDefault(achievementId);

    /// <summary>Reset session (déconnexion / sortie de zone).</summary>
    public static void Clear()
    {
        _points.Clear();
        _counters.Clear();
        HuntPoints = TownPoints = 0;
        _kind4Ids.Clear();
        _kind5Ids.Clear();
        Changed?.Invoke();
    }
}
