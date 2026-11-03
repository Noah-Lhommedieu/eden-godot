using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Competences de vie (recolte, minage, agriculture).
/// Correspond a CLifeSkill / GatherNode. Utilise S_LifeSkill.ini:
/// nodes de recolte → produits (itemId, valeur).
/// </summary>
public sealed class LifeSkillService
{
    private readonly Dictionary<int, WorldParsers.LifeSkillNode> _nodes;
    private readonly Dictionary<int, int> _skillLevels = new();   // charId → niveau
    private readonly Dictionary<int, GatherJob> _active = new();

    public sealed class GatherJob
    {
        public int CharacterId { get; set; }
        public int NodeId { get; set; }
        public int RemainingSeconds { get; set; }
        public List<int> PendingItems { get; } = new();
    }

    public LifeSkillService(
        IEnumerable<WorldParsers.LifeSkillNode> nodes)
        => _nodes = nodes.ToDictionary(n => n.Id);

    public static LifeSkillService LoadFromDataDir(string decodedDir)
        => new(WorldParsers.ParseLifeSkills(
            Path.Combine(decodedDir, "S_LifeSkill.ini")));

    public int NodeCount => _nodes.Count;

    /// <summary>Demarre une recolte si le joueur a le niveau requis.</summary>
    public GatherJob? StartGather(int charId, int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node)) return null;
        var level = _skillLevels.GetValueOrDefault(charId, 1);
        if (level < node.RequiredLevel) return null;
        var job = new GatherJob
        {
            CharacterId = charId,
            NodeId = nodeId,
            RemainingSeconds = node.TimeSeconds,
        };
        foreach (var (itemId, _, _) in node.Products)
            job.PendingItems.Add(itemId);
        _active[charId] = job;
        return job;
    }

    /// <summary>Tick de progression — retourne les items quand termine.</summary>
    public IReadOnlyList<int> TickGather(int charId, int seconds)
    {
        if (!_active.TryGetValue(charId, out var job))
            return Array.Empty<int>();
        job.RemainingSeconds -= seconds;
        if (job.RemainingSeconds > 0) return Array.Empty<int>();
        _active.Remove(charId);
        return job.PendingItems;
    }

    public void SetSkillLevel(int charId, int level)
        => _skillLevels[charId] = level;

    public WorldParsers.LifeSkillNode? GetNode(int nodeId)
        => _nodes.TryGetValue(nodeId, out var n) ? n : null;
}
