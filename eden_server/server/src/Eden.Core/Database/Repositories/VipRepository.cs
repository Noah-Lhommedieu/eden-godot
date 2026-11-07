using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Statut VIP par compte (table <c>account.vip_status</c>) : VIP tant que
/// <c>vip_until</c> est NULL ou futur. Les bonus sont des données du moteur de modificateurs.</summary>
public sealed class VipRepository
{
    private readonly NpgsqlDataSource _db;

    public VipRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Identifiants des comptes VIP à l'instant <paramref name="now"/>.</summary>
    public async Task<IReadOnlySet<int>> ActiveAccountIdsAsync(DateTimeOffset now)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        var ids = await c.QueryAsync<int>(
            "SELECT account_id FROM account.vip_status WHERE vip_until IS NULL OR vip_until > @now",
            new { now });
        return ids.ToHashSet();
    }

    /// <summary>Accorde (ou prolonge) le VIP ; <paramref name="until"/> NULL = permanent.</summary>
    public async Task GrantAsync(int accountId, DateTimeOffset? until, string? by, string? note)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        await c.ExecuteAsync(
            "INSERT INTO account.vip_status(account_id, vip_until, granted_by, note) VALUES (@accountId, @until, @by, @note) " +
            "ON CONFLICT (account_id) DO UPDATE SET vip_until = @until, granted_by = @by, note = @note",
            new { accountId, until, by, note });
    }

    /// <summary>Retire le VIP du compte.</summary>
    public async Task RevokeAsync(int accountId)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        await c.ExecuteAsync("DELETE FROM account.vip_status WHERE account_id = @accountId", new { accountId });
    }

    /// <summary>Identifiant du compte portant ce nom d'utilisateur, ou null.</summary>
    public async Task<int?> FindAccountIdAsync(string username)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int?>(
            "SELECT id FROM account.accounts WHERE lower(username) = lower(@username)", new { username });
    }

    /// <summary>Fin du VIP du compte du personnage : (false,_) non VIP ; (true,null) permanent.</summary>
    public async Task<(bool IsVip, DateTimeOffset? Until)> StatusOfCharAsync(int charId, DateTimeOffset now)
    {
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        var row = await c.QueryFirstOrDefaultAsync<(DateTimeOffset? Until, int Found)>(
            "SELECT v.vip_until AS Until, 1 AS Found FROM character.player_characters p " +
            "JOIN account.accounts a ON a.username = p.account_name " +
            "JOIN account.vip_status v ON v.account_id = a.id " +
            "WHERE p.id = @charId AND (v.vip_until IS NULL OR v.vip_until > @now)", new { charId, now });
        return (row.Found == 1, row.Until);
    }
}
