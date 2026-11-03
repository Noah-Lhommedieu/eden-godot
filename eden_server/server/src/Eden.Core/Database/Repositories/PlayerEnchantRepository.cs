using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Implémentation Dapper de <see cref="IPlayerEnchantRepository"/>
/// sur la table native `player_enchants`.</summary>
public sealed class PlayerEnchantRepository : IPlayerEnchantRepository
{
    private readonly NpgsqlDataSource _db;

    public PlayerEnchantRepository(NpgsqlDataSource db)
        => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerEnchantEntity>> GetEnchantsAsync(
        int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<PlayerEnchantEntity>(
            "SELECT player_id AS PlayerId, effect_id AS EffectId, " +
            "caster_id AS CasterId, rest_time AS RestTime, " +
            "factor AS Factor, counter AS Counter, " +
            "COALESCE(sets, '') AS Sets, stack_count AS StackCount, " +
            "level AS Level " +
            "FROM player_enchants WHERE player_id=@Id",
            new { Id = playerId });
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task UpsertEnchantAsync(PlayerEnchantEntity e)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // Pas de contrainte unique sur (player_id,effect_id) dans le
        // schéma natif — upsert applicatif : delete puis insert reste
        // atomique pour un même effect (le natif fait
        // « Update Enchant » si présent, « Insert Enchant » sinon).
        await connection.ExecuteAsync(
            "DELETE FROM player_enchants " +
            "WHERE player_id=@PlayerId AND effect_id=@EffectId",
            new { e.PlayerId, e.EffectId });
        await connection.ExecuteAsync(
            "INSERT INTO player_enchants (player_id, effect_id, " +
            "caster_id, rest_time, factor, counter, sets, " +
            "stack_count, level) VALUES (@PlayerId, @EffectId, " +
            "@CasterId, @RestTime, @Factor, @Counter, @Sets, " +
            "@StackCount, @Level)", e);
    }

    /// <inheritdoc />
    public async Task DeleteEnchantAsync(int playerId, int effectId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM player_enchants " +
            "WHERE player_id=@Id AND effect_id=@Effect",
            new { Id = playerId, Effect = effectId });
    }

    /// <inheritdoc />
    public async Task DeleteAllAsync(int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM player_enchants WHERE player_id=@Id",
            new { Id = playerId });
    }
}
