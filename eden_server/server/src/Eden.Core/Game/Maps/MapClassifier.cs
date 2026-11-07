namespace Eden.Core.Game.Maps;

/// <summary>Faits d'une map nécessaires au classement (tirés de
/// <c>c_node</c>, <c>t_node</c> et de la scène <c>[reborn_monsters]</c>).</summary>
/// <param name="NodeType"><c>c_node</c> col 3.</param>
/// <param name="NameFr">Nom FR (<c>t_node</c>) ou Big5 (<c>c_node</c>), pour « Illusion Destructrice » / 毀滅幻境.</param>
/// <param name="RespawnSeconds">Délai de réapparition de chaque spawn (champ 4).</param>
public sealed record MapFacts(int NodeId, int NodeType, string NameFr,
    IReadOnlyList<int> RespawnSeconds);

/// <summary>
/// Règle de classement des maps (PLAN-ACTION-3 §4.1) :
/// type 4 → ville de guilde ; type 18 ou « Illusion Destructrice » → épreuve ;
/// types 5 et 7 → donjon (instance privée) ; type 11 → instance partagée ;
/// types spéciaux (8-10, 12-17) → mode spécial ; type 1 dont le délai de
/// respawn médian est de 20 à 90 s (BTS 224, DD 216, Labo Vingot 212) →
/// instance partagée, sinon monde ouvert (le monde normal respawn en ≤ 10 s).
/// </summary>
public static class MapClassifier
{
    public const int SharedRespawnMinSeconds = 20;
    public const int SharedRespawnMaxSeconds = 90;

    /// <summary>Corrections curées (devront migrer vers le contenu, table
    /// <c>map_kind_override</c>) : cartes de type 1 qui sont des modes spéciaux.
    /// 96 = Île aux Cerisiers (Sakura), mêlée générale PvP.</summary>
    private static readonly Dictionary<int, MapKind> Overrides = new()
    {
        [96] = MapKind.SpecialMode,
    };

    private static readonly HashSet<int> SpecialTypes =
        new() { 8, 9, 10, 12, 13, 14, 15, 16, 17 };

    public static MapKind Classify(MapFacts f)
    {
        if (Overrides.TryGetValue(f.NodeId, out var forced)) return forced;
        if (f.NodeType == 4) return MapKind.GuildCity;
        if (f.NodeType == 18 || IsIllusion(f.NameFr)) return MapKind.Trial;
        if (f.NodeType is 5 or 7) return MapKind.Dungeon;
        if (f.NodeType == 11) return MapKind.SharedInstance;
        if (SpecialTypes.Contains(f.NodeType)) return MapKind.SpecialMode;
        if (f.NodeType == 1) return IsFastRespawnShared(f.RespawnSeconds)
            ? MapKind.SharedInstance : MapKind.Field;
        if (f.NodeType == 3) return MapKind.Field;
        return MapKind.Unknown;
    }

    private static bool IsIllusion(string name) =>
        name.Contains("Illusion Destructrice", StringComparison.OrdinalIgnoreCase)
        || name.Contains("毀滅幻境", StringComparison.Ordinal); // nom Big5 de c_node

    private static bool IsFastRespawnShared(IReadOnlyList<int> respawn)
    {
        var positive = respawn.Where(r => r > 0).OrderBy(r => r).ToArray();
        if (positive.Length < 20) return false;
        var median = positive[positive.Length / 2];
        return median is >= SharedRespawnMinSeconds and <= SharedRespawnMaxSeconds;
    }
}

/// <summary>
/// Règles de mort. Décision du propriétaire (2026-11-07) : on perd de l'EXP
/// UNIQUEMENT si la mort a lieu sur une map normale ET a été causée par un
/// monstre (PvE). Jamais en PvP (duel, Sakura, arènes, GvG…), jamais en donjon,
/// instance, ville de guilde ou mode spécial.
/// </summary>
public static class DeathRules
{
    public static bool LosesExp(MapKind map, KillerKind killer) =>
        map == MapKind.Field && killer == KillerKind.Monster;

    /// <summary>Le PvP ne fait JAMAIS perdre d'or ni d'EXP, où que ce soit.</summary>
    public static bool LosesGold(MapKind map, KillerKind killer) => false;
}
