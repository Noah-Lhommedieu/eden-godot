using Eden.Core.Game.Modifiers;
using Eden.Zone.Features.Modifiers;

namespace Eden.Zone.Features.Craft;

/// <summary>
/// Durées de craft passées par le moteur de modificateurs (VIP −25 %, config, événements) :
/// une seule règle pour la durée des ordres de travail et pour la barre de cast de la combine.
/// </summary>
internal static class CraftTimes
{
    /// <summary>Durée d'un ordre (unités de 100 ms) : moteur <c>craft.duration</c>, puis bonus Limitless (−x %).</summary>
    public static int WorkUnits(ZoneServices s, ZoneEntryFlow.EntryState st, int baseUnits, double limitlessPct)
    {
        var f = s.Modifiers().Resolve(ModifierKeys.CraftDuration, PlayerModifierContext.For(st));
        return Math.Max(0, (int)(baseUnits * f * (1 + limitlessPct)));
    }

    /// <summary>Durée de la barre de cast de la combine (unités de 100 ms) : moteur <c>craft.cast_seconds</c>.</summary>
    public static int CastUnits(ZoneServices s, ZoneEntryFlow.EntryState st, int baseUnits) =>
        Math.Max(1, (int)Math.Round(baseUnits * s.Modifiers().Resolve(ModifierKeys.CraftCastSeconds, PlayerModifierContext.For(st))));
}
