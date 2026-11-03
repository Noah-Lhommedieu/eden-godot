namespace Eden.Core.Game.Mall;

/// <summary>
/// Points du joueur (AP et EP)
/// </summary>
public sealed class PlayerCoinsEntity
{
    public int CharacterId { get; set; }
    public int APPoints { get; set; }
    public int EPPoints { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Item dans l'item mall
/// </summary>
public sealed class ItemMallItemEntity
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? APPrice { get; set; }
    public int? EPPrice { get; set; }
    public bool IsLimited { get; set; }
    public DateTime? LimitStart { get; set; }
    public DateTime? LimitEnd { get; set; }
    public int? StockQuantity { get; set; }
}
