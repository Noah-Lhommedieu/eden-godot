namespace Eden.Core.Game.Stats;

/// <summary>
/// Stats d'un personnage (base + bonus)
/// </summary>
public sealed class PlayerStatsEntity
{
    public int CharacterId { get; set; }
    
    // Base stats (déterminées par race, classe, niveau)
    public int STR_Base { get; set; }
    public int DEX_Base { get; set; }
    public int CON_Base { get; set; }
    public int INT_Base { get; set; }
    public int PSY_Base { get; set; }
    public int AGI_Base { get; set; }
    
    // Bonus stats (équipement, buffs, talents)
    public int STR_Bonus { get; set; }
    public int DEX_Bonus { get; set; }
    public int CON_Bonus { get; set; }
    public int INT_Bonus { get; set; }
    public int PSY_Bonus { get; set; }
    public int AGI_Bonus { get; set; }
    
    // Stats calculées (readonly)
    public int STR_Total => STR_Base + STR_Bonus;
    public int DEX_Total => DEX_Base + DEX_Bonus;
    public int CON_Total => CON_Base + CON_Bonus;
    public int INT_Total => INT_Base + INT_Bonus;
    public int PSY_Total => PSY_Base + PSY_Bonus;
    public int AGI_Total => AGI_Base + AGI_Bonus;
}
