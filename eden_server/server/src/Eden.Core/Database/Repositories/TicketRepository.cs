using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

public interface ITicketRepository
{
    Task IssueAsync(uint key1, uint key2, int characterId,
        uint clientIp, uint zoneIp, ushort zonePort);
    /// <summary>Consomme le ticket (usage unique) — retourne le charId.</summary>
    Task<int?> ConsumeAsync(uint key1, uint key2, uint zoneIp, ushort zonePort);
    /// <summary>Émet un ticket de connexion Login→World (subject = compte).</summary>
    Task IssueLoginAsync(uint key1, int accountId, string accountName,
        uint clientIp, uint worldIp, ushort worldPort);
    /// <summary>Consomme le ticket login — retourne (accountId, accountName).</summary>
    Task<(int accountId, string accountName)?> ConsumeLoginAsync(
        uint key1, string accountName);
    /// <summary>Idem sans vérif de nom (le 2e champ natif = MAC client).</summary>
    Task<(int accountId, string accountName)?> ConsumeLoginByKeyAsync(uint key1);
    Task<int> PurgeExpiredAsync();
}

/// <summary>
/// Registre de tickets de migration — fidèle à la map native
/// <c>0x89b9890</c> de ZoneServer (clé → CTicket) sauf qu'elle est
/// partagée World→Zone via PostgreSQL.
/// </summary>
public sealed class TicketRepository : ITicketRepository
{
    private readonly NpgsqlDataSource _db;
    private static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(5);

    public TicketRepository(NpgsqlDataSource db)
        => _db = db;

    public async Task IssueAsync(uint key1, uint key2, int characterId,
        uint clientIp, uint zoneIp, ushort zonePort)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            INSERT INTO zone_tickets (key1, key2, character_id, client_ip, zone_ip, zone_port, issued_at)
            VALUES (@K1, @K2, @CharId, @ClientIp, @ZoneIp, @ZonePort, NOW())
            ON CONFLICT (key1, key2) DO UPDATE
            SET character_id = @CharId, issued_at = NOW(), used = FALSE";
        await c.ExecuteAsync(sql, new
        {
            K1 = (long)key1, K2 = (long)key2, CharId = characterId,
            ClientIp = (long)clientIp, ZoneIp = (long)zoneIp, ZonePort = (int)zonePort,
        });
    }

    public async Task<int?> ConsumeAsync(uint key1, uint key2, uint zoneIp, ushort zonePort)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            UPDATE zone_tickets SET used = TRUE
            WHERE key1 = @K1 AND key2 = @K2 AND used = FALSE
              AND issued_at > NOW() - INTERVAL '5 minutes'
              AND (zone_ip = 0 OR zone_ip = @ZoneIp)
              AND (zone_port = 0 OR zone_port = @ZonePort)
            RETURNING character_id";
        return await c.QueryFirstOrDefaultAsync<int?>(sql, new
        {
            K1 = (long)key1, K2 = (long)key2,
            ZoneIp = (long)zoneIp, ZonePort = (int)zonePort,
        });
    }

    public async Task<int> PurgeExpiredAsync()
    {
        using var c = _db.CreateConnection();
        return await c.ExecuteAsync(
            "DELETE FROM zone_tickets WHERE issued_at < NOW() - INTERVAL '1 hour'");
    }

    public async Task IssueLoginAsync(uint key1, int accountId, string accountName,
        uint clientIp, uint worldIp, ushort worldPort)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            INSERT INTO login_tickets (key1, account_id, account_name, client_ip, world_ip, world_port, issued_at)
            VALUES (@K1, @AccId, @Name, @ClientIp, @WorldIp, @WorldPort, NOW())
            ON CONFLICT (key1) DO UPDATE
            SET account_id = @AccId, account_name = @Name, issued_at = NOW(), used = FALSE";
        await c.ExecuteAsync(sql, new
        {
            K1 = (long)key1, AccId = accountId, Name = accountName,
            ClientIp = (long)clientIp, WorldIp = (long)worldIp, WorldPort = (int)worldPort,
        });
    }

    public async Task<(int accountId, string accountName)?> ConsumeLoginAsync(
        uint key1, string accountName)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            UPDATE login_tickets SET used = TRUE
            WHERE key1 = @K1 AND used = FALSE
              AND issued_at > NOW() - INTERVAL '5 minutes'
              AND account_name = @Name
            RETURNING account_id, account_name";
        var row = await c.QueryFirstOrDefaultAsync<(int, string)?>(sql, new
        {
            K1 = (long)key1, Name = accountName,
        });
        return row;
    }

    public async Task<(int accountId, string accountName)?> ConsumeLoginByKeyAsync(uint key1)
    {
        using var c = _db.CreateConnection();
        const string sql = @"
            UPDATE login_tickets SET used = TRUE
            WHERE key1 = @K1 AND used = FALSE
              AND issued_at > NOW() - INTERVAL '5 minutes'
            RETURNING account_id, account_name";
        return await c.QueryFirstOrDefaultAsync<(int, string)?>(sql, new
        {
            K1 = (long)key1,
        });
    }
}
