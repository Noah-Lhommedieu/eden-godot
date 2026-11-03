namespace Eden.Core.Database.Entities;

/// <summary>
/// Serveur de monde de jeu stocké dans 'edenfr_account' (table 'worlds').
/// </summary>
public sealed class WorldServerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 5200;
    public int OnlineUsers { get; set; }
    public int MaxUsers { get; set; } = 1000;
    public int State { get; set; } // 0 = Normal, 1 = Nouveau, 2 = Occupé, 3 = Plein
    public string Version { get; set; } = "1.0";
    public int ShowOrder { get; set; }
}
