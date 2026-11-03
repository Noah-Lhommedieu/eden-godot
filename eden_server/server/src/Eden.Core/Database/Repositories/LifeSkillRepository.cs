using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Implémentation Dapper de <see cref="ILifeSkillRepository"/> —
/// table native edenfr_db `player_life_skill`.</summary>
public sealed class LifeSkillRepository : ILifeSkillRepository
{
    private readonly NpgsqlDataSource _db;

    public LifeSkillRepository(NpgsqlDataSource db) => _db = db;

    /// <inheritdoc/>
    public async Task<List<PlayerLifeSkillEntity>> GetByPlayerAsync(int playerId)
    {
        await using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<PlayerLifeSkillEntity>(
            @"SELECT player_id AS PlayerId, life_skill_id AS LifeSkillId,
                     exp AS Exp, due_date AS DueDate
                FROM player_life_skill
               WHERE player_id = @playerId", new { playerId });
        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task InsertAsync(int playerId, int lifeSkillId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO player_life_skill (player_id, life_skill_id, exp, due_date)
              VALUES (@playerId, @lifeSkillId, 0, 0)", new { playerId, lifeSkillId });
    }

    /// <inheritdoc/>
    public async Task InsertManyAsync(int playerId, IEnumerable<int> lifeSkillIds)
    {
        // int[] matérialisé : Npgsql le lie comme array PG pour unnest
        // (un IEnumerable paresseux serait ré-énuméré par Dapper).
        var ids = lifeSkillIds as int[] ?? lifeSkillIds.ToArray();
        if (ids.Length == 0) return;
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO player_life_skill (player_id, life_skill_id, exp, due_date)
              SELECT @playerId, s.id, 0, 0
              FROM unnest(@ids) AS s(id)
              ON CONFLICT (player_id, life_skill_id) DO NOTHING",
            new { playerId, ids });
    }

    /// <inheritdoc/>
    public async Task UpdateExpAsync(int playerId, int lifeSkillId, int exp, int dueDate)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE player_life_skill SET exp = @exp, due_date = @dueDate
               WHERE player_id = @playerId AND life_skill_id = @lifeSkillId",
            new { playerId, lifeSkillId, exp, dueDate });
    }

    /// <inheritdoc/>
    public async Task ReplaceAsync(int playerId, int oldId, int newId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE player_life_skill SET life_skill_id = @newId, exp = 0, due_date = 0
               WHERE player_id = @playerId AND life_skill_id = @oldId",
            new { playerId, oldId, newId });
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int playerId, int lifeSkillId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"DELETE FROM player_life_skill
               WHERE player_id = @playerId AND life_skill_id = @lifeSkillId",
            new { playerId, lifeSkillId });
    }
}
