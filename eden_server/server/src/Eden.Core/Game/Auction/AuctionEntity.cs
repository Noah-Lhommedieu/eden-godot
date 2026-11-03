namespace Eden.Core.Game.Auction;

/// <summary>
/// Vente en cours dans l'hôtel des ventes
/// </summary>
public sealed class AuctionEntity
{
    public int Id { get; set; }
    public int SellerId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public int DurationHours { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "active"; // 'active', 'sold', 'cancelled', 'expired'
}
