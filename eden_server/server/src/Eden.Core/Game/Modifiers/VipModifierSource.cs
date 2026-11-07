namespace Eden.Core.Game.Modifiers;

/// <summary>Une règle de bonus VIP (donnée, pas du code).</summary>
public sealed record VipRule(string KeyId, ModifierMode Mode, double Value);

/// <summary>
/// Bonus VIP : un simple statut (VIP ou non). Répond uniquement si le
/// contexte est VIP : quand le VIP s'arrête, la source se tait — rien à
/// retirer ni à nettoyer. Seules les clés <c>VipOverridable</c> sont honorées.
/// </summary>
public sealed class VipModifierSource : IModifierSource
{
    private readonly IReadOnlyList<VipRule> _rules;

    public VipModifierSource(IEnumerable<VipRule> rules) =>
        _rules = rules.ToArray();

    public string Name => "vip";

    /// <summary>Valeurs initiales décidées par le propriétaire (2026-11-07) :
    /// −25 % sur les temps de craft / plans de récolte / durabilité des outils
    /// de récolte, et +10 % d'EXP personnage, classe et classe éveillée.</summary>
    public static IReadOnlyList<VipRule> DefaultRules { get; } = new[]
    {
        new VipRule("exp.character", ModifierMode.AddPercent, 10),
        new VipRule("exp.class", ModifierMode.AddPercent, 10),
        new VipRule("exp.awakened_class", ModifierMode.AddPercent, 10),
        new VipRule("craft.cast_seconds", ModifierMode.Multiply, 0.75),
        new VipRule("craft.duration", ModifierMode.Multiply, 0.75),
        new VipRule("gather.tick_seconds", ModifierMode.Multiply, 0.75),
        new VipRule("gather.durability_cost", ModifierMode.Multiply, 0.75),
    };

    public void Collect(ModifierKey key, in ModifierContext ctx,
        List<ModifierContribution> sink)
    {
        if (!ctx.IsVip || !key.VipOverridable) return;
        foreach (var r in _rules)
            if (r.KeyId == key.Id)
                sink.Add(new ModifierContribution(Name, r.Mode, r.Value));
    }
}
