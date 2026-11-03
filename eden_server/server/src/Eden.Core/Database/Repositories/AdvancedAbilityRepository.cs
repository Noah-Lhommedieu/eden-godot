using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Dépôt de la table native <c>advanced_ability</c>
/// (player_id, effect_id, point) — les points d'aptitude avancée
/// dépensés dans les arbres S_Talent. SQL natif :
/// GetAdvancedAbility{Insert,Update,Remove,Add}SQLCmd
/// (@0x863a860-0x863a910 dans server_us).
/// </summary>
public interface IAdvancedAbilityRepository
{
    /// <summary>Map effectId → point investi (chargement login).</summary>
    Task<Dictionary<short, short>> GetEffectsAsync(int characterId);

    /// <summary>INSERT … ON CONFLICT DO UPDATE — équivalent des
    /// GetAdvancedAbilityInsertSQLCmd / GetAdvancedAbilityAddSQLCmd
    /// natifs (pas de PK déclarée dans le schéma live → on supprime
    /// puis insère pour éviter tout doublon).</summary>
    Task SetEffectPointAsync(int characterId, short effectId,
        short point);

    /// <summary>Retire un effet (reset d'arbre —
    /// GetAdvancedAbilityRemoveSQLCmd natif).</summary>
    Task RemoveEffectAsync(int characterId, short effectId);
}

/// <inheritdoc cref="IAdvancedAbilityRepository"/>
public sealed class AdvancedAbilityRepository : IAdvancedAbilityRepository
{
    private readonly NpgsqlDataSource _db;

    public AdvancedAbilityRepository(NpgsqlDataSource db)
        => _db = db;

    public async Task<Dictionary<short, short>> GetEffectsAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<(int EffectId, int Point)>(
            "SELECT effect_id AS EffectId, point AS Point " +
            "FROM advanced_ability WHERE player_id = @Id",
            new { Id = characterId });
        var map = new Dictionary<short, short>();
        foreach (var r in rows)
            map[(short)r.EffectId] = (short)r.Point;
        return map;
    }

    public async Task SetEffectPointAsync(int characterId,
        short effectId, short point)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // Schéma natif sans PK : UPDATE puis INSERT si absent (ordre
        // natif Insert/Update — pas de doublon possible ici).
        var n = await connection.ExecuteAsync(
            "UPDATE advanced_ability SET point = @Pt " +
            "WHERE player_id = @Id AND effect_id = @Eff",
            new { Id = characterId, Eff = (int)effectId,
                  Pt = (int)point });
        if (n == 0)
            await connection.ExecuteAsync(
                "INSERT INTO advanced_ability " +
                "(player_id, effect_id, point) VALUES (@Id, @Eff, @Pt)",
                new { Id = characterId, Eff = (int)effectId,
                      Pt = (int)point });
    }

    public async Task RemoveEffectAsync(int characterId, short effectId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM advanced_ability " +
            "WHERE player_id = @Id AND effect_id = @Eff",
            new { Id = characterId, Eff = (int)effectId });
    }
}
