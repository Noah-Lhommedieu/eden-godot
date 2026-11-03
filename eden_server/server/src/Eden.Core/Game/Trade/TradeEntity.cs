namespace Eden.Core.Game.Trade;

/// <summary>
/// Session de trade entre deux joueurs
/// </summary>
public sealed class TradeEntity
{
    public int Id { get; set; }
    public int InitiatorId { get; set; }
    public int PartnerId { get; set; }
    public bool InitiatorConfirmed { get; set; }
    public bool PartnerConfirmed { get; set; }
    public int InitiatorGold { get; set; }
    public int PartnerGold { get; set; }
    public List<TradeItem> InitiatorItems { get; set; } = new();
    public List<TradeItem> PartnerItems { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed class TradeItem
{
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}
