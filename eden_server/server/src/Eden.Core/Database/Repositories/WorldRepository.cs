using Dapper;
using Npgsql;
using Eden.Core.Database.Entities;
using Eden.Core.Networking.Messages.Login;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Implémentation PostgreSQL du dépôt des serveurs de monde via Dapper.
/// Reversé depuis lapis::CWorldServerStates.
/// </summary>
public sealed class WorldRepository : IWorldRepository
{
    private readonly NpgsqlDataSource _db;

    public WorldRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WorldServerEntity>> GetAllWorldsAsync()
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   name AS Name,
                   ip AS IpAddress,
                   port AS Port,
                   online_user AS OnlineUsers,
                   maxnum_user AS MaxUsers,
                   state AS State,
                   version AS Version,
                   show_order AS ShowOrder
            FROM worlds
            ORDER BY show_order ASC, id ASC;";

        var worlds = await connection.QueryAsync<WorldServerEntity>(sql);
        return worlds.ToList();
    }

    public async Task<WorldServerEntity?> GetWorldByIdAsync(int worldId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   name AS Name,
                   ip AS IpAddress,
                   port AS Port,
                   online_user AS OnlineUsers,
                   maxnum_user AS MaxUsers,
                   state AS State,
                   version AS Version,
                   show_order AS ShowOrder
            FROM worlds
            WHERE id = @WorldId
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<WorldServerEntity>(sql, new { WorldId = worldId });
    }

    public async Task<List<WorldServerEntry>> GetWorldStatesForAccountAsync(int accountId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT w.id AS WorldId,
                   w.name AS WorldName,
                   w.ip AS IpAddress,
                   w.port AS Port,
                   w.online_user AS Population,
                   w.maxnum_user AS MaxNum,
                   COALESCE(cn.""character"", 0) AS CharacterCount,
                   w.state AS State,
                   w.show_order AS ShowOrder,
                   0 AS Ping
            FROM worlds w
            LEFT JOIN character_number cn ON cn.world_id = w.id AND cn.account_id = @AccountId
            ORDER BY w.show_order ASC, w.id ASC;";

        var results = await connection.QueryAsync<WorldServerEntry>(sql, new { AccountId = accountId });
        return results.ToList();
    }

    public async Task UpdateWorldPopulationAsync(int worldId, int onlineUsers)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"UPDATE worlds SET online_user = @OnlineUsers WHERE id = @WorldId;";
        await connection.ExecuteAsync(sql, new { OnlineUsers = onlineUsers, WorldId = worldId });
    }
}
