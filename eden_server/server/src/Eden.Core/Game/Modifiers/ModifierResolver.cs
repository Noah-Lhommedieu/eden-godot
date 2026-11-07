namespace Eden.Core.Game.Modifiers;

/// <summary>Résultat d'une résolution + explication de chaque contribution.</summary>
public sealed record ResolvedModifier(ModifierKey Key, double Value,
    IReadOnlyList<ModifierContribution> Contributions)
{
    /// <summary>Texte lisible pour les journaux / l'admin
    /// (« pourquoi cette valeur ? »).</summary>
    public string Explain() =>
        $"{Key.Id} = {Value:0.####} ← base {Key.Default:0.####}" +
        string.Concat(Contributions.Select(c =>
            $" ; {c.Source}:{c.Mode} {c.Value:0.####}"));
}

/// <summary>
/// Résolveur unique. Composition (documentée et testée) :
/// <code>
/// v = (Set de la dernière source, sinon Default)
/// v = v × Π(Multiply)
/// v = v × (1 + Σ(AddPercent)/100)
/// v = v + Σ(AddFlat)
/// v = clamp(v, Key.Min, min(Key.Max, Cap))
/// </code>
/// </summary>
public sealed class ModifierResolver
{
    private readonly IReadOnlyList<IModifierSource> _sources;

    public ModifierResolver(IEnumerable<IModifierSource> sources) =>
        _sources = sources.ToArray();

    /// <summary>Valeur seule — chemin chaud, sans objet d'explication.</summary>
    public double Resolve(ModifierKey key, in ModifierContext ctx)
    {
        var buffer = new List<ModifierContribution>(8);
        Collect(key, ctx, buffer);
        return Compose(key, buffer);
    }

    /// <summary>Valeur + contributions (diagnostic, admin, tests).</summary>
    public ResolvedModifier ResolveWithExplain(ModifierKey key,
        in ModifierContext ctx)
    {
        var buffer = new List<ModifierContribution>(8);
        Collect(key, ctx, buffer);
        return new ResolvedModifier(key, Compose(key, buffer), buffer);
    }

    private void Collect(ModifierKey key, in ModifierContext ctx,
        List<ModifierContribution> sink)
    {
        foreach (var s in _sources) s.Collect(key, ctx, sink);
    }

    internal static double Compose(ModifierKey key,
        IReadOnlyList<ModifierContribution> contributions)
    {
        var value = key.Default;
        double mult = 1, addPercent = 0, addFlat = 0, cap = double.MaxValue;
        foreach (var c in contributions)
        {
            switch (c.Mode)
            {
                case ModifierMode.Set: value = c.Value; break;
                case ModifierMode.Multiply: mult *= c.Value; break;
                case ModifierMode.AddPercent: addPercent += c.Value; break;
                case ModifierMode.AddFlat: addFlat += c.Value; break;
                case ModifierMode.Cap: cap = Math.Min(cap, c.Value); break;
            }
        }
        value = value * mult * (1 + addPercent / 100.0) + addFlat;
        return Math.Clamp(value, key.Min, Math.Min(key.Max, cap));
    }
}
