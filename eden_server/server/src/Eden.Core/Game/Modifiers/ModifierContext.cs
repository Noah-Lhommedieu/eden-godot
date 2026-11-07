using Eden.Core.Game.Maps;

namespace Eden.Core.Game.Modifiers;

/// <summary>Type de classe : normale (1-20) ou éveillée (21-60).</summary>
public enum ClassKind { None, Normal, Awakened }

/// <summary>
/// Contexte d'une résolution. Struct immuable : aucune allocation sur le
/// chemin chaud (kill → EXP).
/// </summary>
public readonly record struct ModifierContext(
    int AccountId = 0,
    int CharId = 0,
    int MapId = 0,
    MapKind MapKind = MapKind.Unknown,
    ClassKind ClassKind = ClassKind.None,
    int BuildingType = 0,
    int CraftType = 0,
    bool IsVip = false,
    int CharmExpPercent = 0,
    int CharmClassPercent = 0,
    int CharmAwakenedPercent = 0);
