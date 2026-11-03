using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Evaluate;

/// <summary>Registre des instances d'évaluation actives —
/// équivalent <c>lapis::CEvaluateFactory</c> (map evalId→CEvaluate
/// zone @0x8084190). Les mutations sont serialisées par l'appelant
/// (ZoneLoop) — le service lui-même n'est pas thread-safe, comme
/// les factories natives.</summary>
public sealed class EvaluateService
{
    private readonly Dictionary<int, EvaluateInstance> _instances =
        new();
    private int _nextId = 1;

    /// <summary><c>CreateEvaluate</c> @0x8084190 — crée l'instance
    /// pour l'équipe ; retourne null si aucune def S_RewardScore ne
    /// couvre le nœud (log natif « no evaluate list node_tid »).</summary>
    public EvaluateInstance? Create(int nodeTid,
        IEnumerable<int> memberCharIds,
        IReadOnlyDictionary<int, RewardScoreParser.EvaluateDef> defs)
    {
        if (!defs.TryGetValue(nodeTid, out var def)) return null;
        var inst = new EvaluateInstance(_nextId++, def, memberCharIds);
        _instances[inst.Id] = inst;
        return inst;
    }

    /// <summary><c>GetEvaluate</c> — null si absent.</summary>
    public EvaluateInstance? Get(int evalId) =>
        _instances.TryGetValue(evalId, out var i) ? i : null;

    /// <summary>Instance d'un perso via son evalId (char+0xbb4
    /// natif porté par l'EntryState, pas ici).</summary>
    public void Remove(int evalId) => _instances.Remove(evalId);

    public IReadOnlyCollection<EvaluateInstance> All => _instances.Values;
}
