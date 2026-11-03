using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de montures — correspond a CEC_RideSpell + CRideList.
/// Acquisition, vitesses (S_RideStep.ini), buffs de monture.
/// </summary>
public sealed class MountService
{
    private readonly Dictionary<string, DropAndSpeedParsers.SpeedDef> _rides;
    private readonly Dictionary<int, MountInstance> _mounted = new();

    public sealed class MountInstance
    {
        public int CharacterId { get; set; }
        public string RideCode { get; set; } = string.Empty;  // R001
        public int ItemId { get; set; }
        public float SpeedBonus { get; set; }
    }

    public MountService(IEnumerable<DropAndSpeedParsers.SpeedDef> rideSpeeds)
        => _rides = rideSpeeds.ToDictionary(r => r.Code);

    public static MountService LoadFromDataDir(string decodedDir)
        => new(DropAndSpeedParsers.ParseSpeedTable(
            Path.Combine(decodedDir, "S_RideStep.ini")));

    /// <summary>Monte sur une monture — applique le bonus de vitesse.</summary>
    public MountInstance? Mount(int characterId, string rideCode, int itemId)
    {
        if (!_rides.TryGetValue(rideCode, out var ride)) return null;
        var m = new MountInstance
        {
            CharacterId = characterId,
            RideCode = rideCode,
            ItemId = itemId,
            SpeedBonus = ride.RunSpeed,
        };
        _mounted[characterId] = m;
        return m;
    }

    /// <summary>Descend de la monture.</summary>
    public bool Dismount(int characterId) => _mounted.Remove(characterId);

    public bool IsMounted(int characterId) => _mounted.ContainsKey(characterId);

    public MountInstance? GetMount(int characterId)
        => _mounted.TryGetValue(characterId, out var m) ? m : null;

    public DropAndSpeedParsers.SpeedDef? GetRideDef(string rideCode)
        => _rides.TryGetValue(rideCode, out var r) ? r : null;
}
