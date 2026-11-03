namespace Eden.Core.Game.Classes;

/// <summary>
/// Stats d'une classe à un niveau donné
/// Issue de S_Class.ini
/// </summary>
public sealed class ClassStatsEntity
{
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int HpBonus { get; set; }
    public int MpBonus { get; set; }
    public int StrBonus { get; set; }
    public int DexBonus { get; set; }
    public int ConBonus { get; set; }
    public int IntBonus { get; set; }
    public int PsyBonus { get; set; }
    public int AgiBonus { get; set; }
}
