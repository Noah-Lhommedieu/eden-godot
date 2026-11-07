using Eden.Core.Game.Maps;

namespace Eden.Core.Game.Modifiers;

/// <summary>Source simple pilotée par une table (config globale, événement,
/// éphéméride du jour) : liste de règles actives, éventuellement filtrées
/// par type de map.</summary>
public sealed class StaticModifierSource : IModifierSource
{
    public sealed record Rule(string KeyId, ModifierMode Mode, double Value,
        MapKind? OnlyMapKind = null);

    private readonly IReadOnlyList<Rule> _rules;

    public StaticModifierSource(string name, IEnumerable<Rule> rules)
    {
        Name = name;
        _rules = rules.ToArray();
    }

    public string Name { get; }

    public void Collect(ModifierKey key, in ModifierContext ctx,
        List<ModifierContribution> sink)
    {
        foreach (var r in _rules)
        {
            if (r.KeyId != key.Id) continue;
            if (r.OnlyMapKind is { } mk && mk != ctx.MapKind) continue;
            sink.Add(new ModifierContribution(Name, r.Mode, r.Value));
        }
    }
}
