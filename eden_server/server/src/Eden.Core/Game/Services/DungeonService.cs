using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de donjons/instances — correspond a CBattlefield + CBattlefieldFactory.
/// Entrees solo/multi, file d'attente, progression, recompenses de completion.
/// </summary>
public sealed class DungeonService
{
    private readonly Dictionary<int, BattlefieldParser.BattlefieldDef> _defs;
    private readonly Dictionary<int, DungeonInstance> _instances = new();
    private int _nextInstanceId = 1;

    public sealed class DungeonInstance
    {
        public int InstanceId { get; set; }
        public BattlefieldParser.BattlefieldDef Definition { get; set; } = null!;
        public List<int> PlayerIds { get; } = new();
        public DungeonState State { get; set; } = DungeonState.Preparing;
        public DateTime StartedAt { get; set; }
    }

    public enum DungeonState { Preparing, InProgress, Completed, Failed }

    public DungeonService(IEnumerable<BattlefieldParser.BattlefieldDef> defs)
        => _defs = defs.ToDictionary(d => d.Id);

    public static DungeonService LoadFromDataDir(string decodedDir)
        => new(BattlefieldParser.Parse(Path.Combine(decodedDir, "S_Battlefield.ini")));

    public BattlefieldParser.BattlefieldDef? GetDefinition(int battlefieldId)
        => _defs.TryGetValue(battlefieldId, out var d) ? d : null;

    /// <summary>Verifie si un joueur peut entrer (niveau + taille de groupe).</summary>
    public bool CanEnter(int battlefieldId, int playerLevel, int partySize)
    {
        var d = GetDefinition(battlefieldId);
        if (d == null) return false;
        if (playerLevel < d.MinLevel) return false;
        if (partySize > Math.Max(1, d.RequiredPlayers)) return false;
        return true;
    }

    /// <summary>
    /// Cree une instance de donjon pour un groupe.
    /// </summary>
    public DungeonInstance? Enter(int battlefieldId, IEnumerable<int> playerIds,
        int minPlayerLevel)
    {
        var d = GetDefinition(battlefieldId);
        var players = playerIds.ToList();
        if (d == null || players.Count == 0) return null;
        if (!CanEnter(battlefieldId, minPlayerLevel, players.Count)) return null;

        var inst = new DungeonInstance
        {
            InstanceId = _nextInstanceId++,
            Definition = d,
            State = DungeonState.InProgress,
            StartedAt = DateTime.UtcNow,
        };
        inst.PlayerIds.AddRange(players);
        _instances[inst.InstanceId] = inst;
        return inst;
    }

    /// <summary>
    /// Complete le donjon — retourne les recompenses par palier.
    /// </summary>
    public IReadOnlyList<int> Complete(int instanceId, int tierIndex = 0)
    {
        if (!_instances.TryGetValue(instanceId, out var inst)) return Array.Empty<int>();
        inst.State = DungeonState.Completed;
        var d = inst.Definition;
        var tier = Math.Clamp(tierIndex, 0, 8);
        return new[] { d.RewardTiers1[tier], d.RewardTiers2[tier], d.RewardTiers3[tier] };
    }

    public void Abandon(int instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var inst))
            inst.State = DungeonState.Failed;
        _instances.Remove(instanceId);
    }

    public DungeonInstance? Get(int instanceId)
        => _instances.TryGetValue(instanceId, out var i) ? i : null;

    public DungeonInstance? GetInstanceOf(int characterId)
        => _instances.Values.FirstOrDefault(i => i.PlayerIds.Contains(characterId));
}
