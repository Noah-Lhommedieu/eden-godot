using Eden.Core.Game.Foundation;
using Eden.Core.Game.Loot;

namespace Eden.Core.Game.Gather;

/// <summary>Type de récolte, déduit du type d'aire de l'outil (<c>c_item</c> col 32).</summary>
public enum GatherKind
{
    /// <summary>Aire 60 : pioche → « En cours de minage ».</summary>
    Mining = 1,
    /// <summary>Aire 61 : gants → « En cours de cueillette ».</summary>
    Farming = 2,
    /// <summary>Aire 62 : boule de cristal → « En cours de divination ».</summary>
    Crystal = 3,
}

public static class GatherKinds
{
    /// <summary>Type d'aire de l'outil (60/61/62) → type de récolte ; null sinon.</summary>
    public static GatherKind? FromAreaType(int areaType) => areaType switch
    {
        60 => GatherKind.Mining,
        61 => GatherKind.Farming,
        62 => GatherKind.Crystal,
        _ => null,
    };
}

/// <summary>Ce qui s'est passé lors d'un tick de récolte.</summary>
/// <param name="Drop">Ressource obtenue (null si l'outil n'a aucun produit).</param>
/// <param name="DurabilityCost">Points de durabilité à retirer à l'outil.</param>
/// <param name="Stop">Raison d'arrêt après ce tick (outil cassé…), sinon null.</param>
public readonly record struct GatherTickResult(GatherDrop? Drop,
    int DurabilityCost, GatherStopReason? Stop);

/// <summary>
/// État PUR d'une récolte continue d'un joueur (aucune dépendance réseau ni
/// base) : position d'origine, horodatage des ticks, report de durabilité.
/// Le service de zone l'anime ; les règles de tirage restent dans
/// <see cref="GatherRules"/>.
/// </summary>
public sealed class GatherSession
{
    /// <summary>Tolérance de déplacement (unités) avant annulation : absorbe le
    /// bruit de position sans laisser le joueur marcher.</summary>
    public const float MoveToleranceUnits = 0.5f;

    private double _durabilityCarry;

    public GatherSession(int toolId, long toolSerial, GatherKind kind,
        WeightedPool<int> pool, float originX, float originZ,
        DateTimeOffset startedAt)
    {
        ToolId = toolId; ToolSerial = toolSerial; Kind = kind; Pool = pool;
        OriginX = originX; OriginZ = originZ;
        StartedAt = LastTickAt = startedAt;
    }

    public int ToolId { get; }
    public long ToolSerial { get; }
    public GatherKind Kind { get; }
    public WeightedPool<int> Pool { get; }
    public float OriginX { get; }
    public float OriginZ { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset LastTickAt { get; private set; }
    public int TicksDone { get; private set; }

    /// <summary>Le joueur s'est-il éloigné de son point d'origine ?</summary>
    public bool HasMoved(float x, float z)
    {
        var dx = x - OriginX; var dz = z - OriginZ;
        return dx * dx + dz * dz > MoveToleranceUnits * MoveToleranceUnits;
    }

    /// <summary>Un tick : une ressource tirée au hasard, la durabilité consommée
    /// depuis le tick précédent (1 point / 10 s × multiplicateur), et l'arrêt si
    /// l'outil est épuisé.</summary>
    /// <param name="durabilityLeft">Durabilité de l'outil avant ce tick.</param>
    /// <param name="durabilityMultiplier">Modificateur <c>gather.durability_cost</c>.</param>
    public GatherTickResult Tick(DateTimeOffset now, IRandom rng,
        int durabilityLeft, double durabilityMultiplier)
    {
        var elapsed = Math.Max(0, (now - LastTickAt).TotalSeconds);
        LastTickAt = now;
        TicksDone++;
        var cost = GatherRules.DurabilityCost(elapsed, durabilityMultiplier,
            ref _durabilityCarry);
        var drop = GatherRules.Tick(Pool, rng);
        GatherStopReason? stop = durabilityLeft - cost <= 0
            ? GatherStopReason.ToolBroken : null;
        return new GatherTickResult(drop, cost, stop);
    }
}
