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

    // Colonnes dénormalisées de filtre (miroir natif `auction`,
    // SaveAuction @0x0815da30, migration 026) :
    /// <summary>Catégorie hôtel des ventes (CItemData+0x84, col33).</summary>
    public int ItemType { get; set; }
    /// <summary>Niveau requis du template (CItemData+0x3e, col14).</summary>
    public int Level { get; set; }
    /// <summary>Masque i64 de classes (CItemData+0x48, col18).</summary>
    public long Class { get; set; }
    /// <summary><c>1 &lt;&lt; quality</c> d'instance — filtre bitmask.</summary>
    public int QualityFilter { get; set; } = 1;
    /// <summary>Nom affiché natif : maker + nom + "+N" de fortif.</summary>
    public string ItemName { get; set; } = "";
}
