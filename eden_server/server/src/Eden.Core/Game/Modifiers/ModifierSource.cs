namespace Eden.Core.Game.Modifiers;

/// <summary>Mode de composition d'une contribution (ordre d'application :
/// <c>Set</c> → <c>Multiply</c> → <c>AddPercent</c> → <c>AddFlat</c> → bornes).</summary>
public enum ModifierMode
{
    /// <summary>Remplace la valeur de base (la dernière source gagne).</summary>
    Set,
    /// <summary>Multiplie (config, événement, éphéméride, mode de map).</summary>
    Multiply,
    /// <summary>Bonus en % additionné aux autres bonus (charmes, VIP).</summary>
    AddPercent,
    /// <summary>Ajoute une quantité fixe (ex. +2 entrées de donjon).</summary>
    AddFlat,
    /// <summary>Plafond supplémentaire appliqué en dernier.</summary>
    Cap,
}

/// <summary>Une contribution d'une source, conservée pour <c>Explain</c>.</summary>
public readonly record struct ModifierContribution(string Source,
    ModifierMode Mode, double Value);

/// <summary>
/// Source de modificateurs : config, type de map, événement, éphéméride, VIP,
/// charmes… Une classe par source. Ajouter une source ne change aucun système
/// de jeu.
/// </summary>
public interface IModifierSource
{
    string Name { get; }

    /// <summary>Ajoute à <paramref name="sink"/> les contributions de cette
    /// source pour la clé et le contexte donnés (rien si non concernée).</summary>
    void Collect(ModifierKey key, in ModifierContext ctx,
        List<ModifierContribution> sink);
}
