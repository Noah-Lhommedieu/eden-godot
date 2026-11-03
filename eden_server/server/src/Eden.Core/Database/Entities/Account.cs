namespace Eden.Core.Database.Entities;

/// <summary>
/// Compte utilisateur principal stocké dans la base PostgreSQL 'edenfr_account' (table 'accounts').
/// </summary>
public sealed class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Empreinte Argon2id au format PHC du credential wire
    /// (<see cref="Eden.Core.Security.PasswordHasher"/>) — NULL tant que
    /// le compte n'a pas été migré par le rehash-at-login.</summary>
    public string? PasswordHash { get; set; }
    public string RealName { get; set; } = string.Empty;
    public int WorldServerId { get; set; }
    public int State { get; set; }
    public int HealthOnlineTime { get; set; }
    public int HealthOfflineTime { get; set; }
    public int LastSaveHealthTime { get; set; }
    public string ItemMallPassword { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
}
