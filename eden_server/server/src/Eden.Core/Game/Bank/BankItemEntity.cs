namespace Eden.Core.Game.Bank;

/// <summary>
/// Item stocké en banque
/// </summary>
public sealed class BankItemEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    /// <summary>Onglet banque (`storage.container_index`, -1/0 = principal).</summary>
    public int ContainerIndex { get; set; }
    /// <summary>Fortification (`storage.strengthen`) — bit 19 du mask item.</summary>
    public int EnchantLevel { get; set; }
    /// <summary>Item lié (`storage.bind` != 0) — flag 0x48 du codec.</summary>
    public bool IsBound { get; set; }
    /// <summary>`storage.create_time` — bit 8 du mask (u32 item+0x18).</summary>
    public int CreateTime { get; set; }
}
