namespace Eden.Core.Game.Events;

/// <summary>
/// Événement de domaine (PLAN-ACTION-3 Q5) : un fait survenu en jeu, publié par le système qui le produit et lu
/// par ceux qui s'y abonnent (éphéméride, succès, audit, déclencheurs). Les <see cref="Facts"/> numériques
/// alimentent les conditions des règles de données (<see cref="Rules.RuleEngine"/>).
/// </summary>
public interface IDomainEvent
{
    string Name { get; }
    IReadOnlyDictionary<string, long> Facts { get; }
}

public sealed record MobKilled(int CharId, int MonsterId, int NodeId) : IDomainEvent
{
    public string Name => nameof(MobKilled);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["MonsterId"] = MonsterId, ["NodeId"] = NodeId };
}

public sealed record ItemCrafted(int CharId, int ItemId, int Quantity) : IDomainEvent
{
    public string Name => nameof(ItemCrafted);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["ItemId"] = ItemId, ["Quantity"] = Quantity };
}

public sealed record GatherTick(int CharId, int ItemId, int Quantity) : IDomainEvent
{
    public string Name => nameof(GatherTick);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["ItemId"] = ItemId, ["Quantity"] = Quantity };
}

public sealed record DungeonEntered(int CharId, int NodeId, bool Multi) : IDomainEvent
{
    public string Name => nameof(DungeonEntered);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["NodeId"] = NodeId, ["Multi"] = Multi ? 1 : 0 };
}

public sealed record PlayerDied(int CharId, int NodeId, bool ByPlayer) : IDomainEvent
{
    public string Name => nameof(PlayerDied);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["NodeId"] = NodeId, ["ByPlayer"] = ByPlayer ? 1 : 0 };
}

public sealed record LevelUp(int CharId, int NewLevel) : IDomainEvent
{
    public string Name => nameof(LevelUp);
    public IReadOnlyDictionary<string, long> Facts => new Dictionary<string, long>
        { ["CharId"] = CharId, ["NewLevel"] = NewLevel };
}
