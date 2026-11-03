using System.Collections.Concurrent;

namespace Eden.Bots;

/// <summary>Dernière position connue d'une entité/trésor vue —
/// alimentée par les spawns 298/329 et les moves 326 pour le ciblage
/// par proximité (attaque uniquement à portée réelle).</summary>
public readonly record struct EntitySpot(float X, float Z);

/// <summary>Résultat agrégé d'une session bot — mesures serveur
/// headless uniquement, pas une preuve visuelle client.</summary>
public sealed class BotResult
{
    public required int CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public bool Entered { get; set; }
    public int Sent { get; set; }
    public int Received { get; set; }
    public int Moves { get; set; }
    public int Attacks { get; set; }
    public int Spells { get; set; }
    public int LootAttempts { get; set; }
    public int Echoes { get; set; }
    public int MobsSeen => Mobs.Count;
    public int TreasuresSeen => Treasures.Count;
    public double EntryMs { get; set; }
    /// <summary>Vrai quand le gate de version a rejeté la session
    /// (0xFFFD) — attendu sous <c>--no-version</c> ou manifest
    /// strict non conforme.</summary>
    public bool VersionRejected { get; set; }
    public string? Error { get; set; }
    public ConcurrentDictionary<uint, EntitySpot> Mobs { get; } = new();
    public ConcurrentDictionary<uint, EntitySpot> Entities { get; } = new();
    public ConcurrentDictionary<uint, EntitySpot> Treasures { get; } = new();
    public ConcurrentDictionary<int, int> InboundNcids { get; } = new();
}
