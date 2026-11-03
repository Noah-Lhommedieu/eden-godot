namespace Eden.Core.Game.Services;

/// <summary>
/// File de recrutement d'instance (NCID 220) — portage du board
/// <c>CTeamManager</c> natif : <c>map&lt;node, multimap&lt;role,
/// charId&gt;&gt;</c> + <c>map&lt;charId, vector&lt;node&gt;&gt;</c>
/// (mission @0x080ce5a0 <c>RecruitInstanceAdd</c>).
/// Le matching reproduit <c>CheckMakeTeam</c> mission @0x080c8490 :
/// la taille d'équipe requise (<c>node+0x74</c> = CharLimit) indexe
/// une table de règles hardcodées initialisée par
/// <c>InitRecruitRule</c> @0x080cda70 pour les tailles 3/5/8.
/// Mutations exclusives sur la boucle de zone (comme les EntryState).
/// </summary>
public sealed class RecruitBoard
{
    /// <summary>Entrée de file : paire (rôle, charId) — miroir du
    /// <c>multimap&lt;short,unsigned_int&gt;</c> natif.</summary>
    public readonly record struct Entry(short Role, int CharId);

    /// <summary>Règles de composition indexées par taille d'équipe —
    /// valeurs exactes de <c>InitRecruitRule</c> mission @0x080cda70 :
    /// chaque case i = nombre de membres à prendre par la stratégie i
    /// (0-2 = rôle exact i ; 3-6 = premier rôle dispo dans l'ordre
    /// permuté <c>RandPos({0,1,2}, seed=i-3)</c> — tous des slots
    /// « flex » sur les rôles 0-2).</summary>
    private static readonly IReadOnlyDictionary<int, short[]> Rules =
        new Dictionary<int, short[]>
        {
            [3] = new short[] { 1, 1, 0, 0, 0, 1, 0 },
            [5] = new short[] { 1, 1, 0, 0, 0, 1, 2 },
            [8] = new short[] { 1, 1, 0, 0, 1, 1, 4 },
        };

    /// <summary>Board node → entrées ordonnées par rôle puis par
    /// insertion (ordre du multimap natif — le matching prend le
    /// « premier » d'un rôle = le plus ancien de ce rôle).</summary>
    private readonly Dictionary<int, List<Entry>> _byNode = new();
    /// <summary>charId → nœuds demandés (map native this+0x7c).</summary>
    private readonly Dictionary<int, List<short>> _byChar = new();

    /// <summary>Nombre d'inscrits sur un nœud (champ +0x28 de
    /// l'entrée board native).</summary>
    public int Count(int nodeId) =>
        _byNode.TryGetValue(nodeId, out var l) ? l.Count : 0;

    /// <summary>Nœuds demandés par un perso (null si absent).</summary>
    public IReadOnlyList<short>? NodesOf(int charId) =>
        _byChar.TryGetValue(charId, out var l) ? l : null;

    /// <summary>Inscrit un perso sur chaque nœud demandé —
    /// <c>RecruitInstanceAdd</c> boucle 2 : insertion dans le multimap
    /// du nœud à la position triée par rôle + push dans le vecteur
    /// char→nœuds.</summary>
    public void Add(int charId, short role, IReadOnlyList<short> nodes)
    {
        var mine = _byChar.TryGetValue(charId, out var l)
            ? l : _byChar[charId] = new List<short>();
        foreach (var node in nodes)
        {
            if (!_byNode.TryGetValue(node, out var list))
                _byNode[node] = list = new List<Entry>();
            // multimap.insert : position après les entrées de clé ≤ —
            // ordre stable par (rôle, insertion).
            var idx = list.Count;
            while (idx > 0 && list[idx - 1].Role > role) idx--;
            list.Insert(idx, new Entry(role, charId));
            if (!mine.Contains(node)) mine.Add(node);
        }
    }

    /// <summary>Retire un perso de toutes les files —
    /// <c>RecruitInstanceLeave</c> mission @0x080c7290 : efface les
    /// entrées (rôle,charId) de chaque nœud enregistré puis l'entrée
    /// char→nœuds. Retourne les nœuds touchés (un résultat 553 [1][x]
    /// natif est émis par nœud retiré).</summary>
    public List<short> Remove(int charId)
    {
        var removed = new List<short>();
        if (!_byChar.Remove(charId, out var nodes)) return removed;
        foreach (var node in nodes)
        {
            if (!_byNode.TryGetValue(node, out var list)) continue;
            if (list.RemoveAll(e => e.CharId == charId) > 0)
                removed.Add(node);
            if (list.Count == 0) _byNode.Remove(node);
        }
        return removed;
    }

    /// <summary>Tente de former une équipe sur le nœud —
    /// <c>CheckMakeTeam</c> mission @0x080c8490. Retourne les charIds
    /// dans l'ordre de sélection (le 1er = leader natif) ou null si
    /// pas de match. En cas de succès les membres sont retirés de
    /// toutes les files (le natif les purge via
    /// <c>RecruitInstanceLeave(role,true)</c>).</summary>
    /// <param name="validate">Pré-filtre par candidat (niveau,
    /// connexion…) — les entrées invalides sont ignorées comme le
    /// retry -1 natif, sans être retirées.</param>
    public List<int>? TryMatch(int nodeId, int charLimit,
        Func<int, bool>? validate = null)
    {
        if (!_byNode.TryGetValue(nodeId, out var board) ||
            board.Count < charLimit ||
            !Rules.TryGetValue(charLimit, out var rule))
            return null;
        // Copie de travail = le _M_copy natif du multimap ; les
        // picks effacent dedans, l'échec ne touche pas le board réel.
        var pool = new List<Entry>(board);
        if (validate != null)
            pool.RemoveAll(e => !validate(e.CharId));
        if (pool.Count < charLimit) return null;
        // Gate natif : ∃ rôle-0 ET ∃ rôle-1 (deux lower_bound <1/<2).
        if (!pool.Any(e => e.Role == 0) || !pool.Any(e => e.Role == 1))
            return null;
        // Pré-checks natifs : count(rôle1) ≥ rule[1] &&
        // count(rôle2) ≥ rule[2].
        if (pool.Count(e => e.Role == 1) < rule[1] ||
            pool.Count(e => e.Role == 2) < rule[2])
            return null;
        var picked = new List<int>(charLimit);
        for (var i = 0; i < 7; i++)
            for (var n = 0; n < rule[i]; n++)
                if (!PickOne(pool, i, picked)) return null;
        if (picked.Count != charLimit) return null;
        // Succès : les membres quittent TOUTES leurs files.
        foreach (var cid in picked) Remove(cid);
        return picked;
    }

    /// <summary>Un pick de la règle — cases du switch natif :
    /// 0-2 = premier membre du rôle exact i ; 3-6 = flex — le premier
    /// rôle disponible dans l'ordre permuté <c>RandPos</c> (rotations
    /// de {0,1,2} par la seed i-3, approximation documentée).</summary>
    private static bool PickOne(List<Entry> pool, int slot,
        List<int> picked)
    {
        if (slot <= 2)
            return TakeRole(pool, (short)slot, picked);
        var seed = slot - 3; // cases 3,4,5 → seeds 0,1,2 ; case 6 → 3
        for (var k = 0; k < 3; k++)
            if (TakeRole(pool, (short)((seed + k) % 3), picked))
                return true;
        return false;
    }

    /// <summary>Prend le premier membre du rôle exact (ordre multimap
    /// = rôle asc puis insertion) et l'efface du pool.</summary>
    private static bool TakeRole(List<Entry> pool, short role,
        List<int> picked)
    {
        var idx = pool.FindIndex(e => e.Role == role);
        if (idx < 0) return false;
        picked.Add(pool[idx].CharId);
        pool.RemoveAt(idx);
        return true;
    }
}
