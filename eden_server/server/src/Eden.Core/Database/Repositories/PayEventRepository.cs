using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès lecture à la table native `prestige` pour le
/// système pay-event (zone NCID 253) — seule
/// <c>GetPrestigeExp</c> est nécessaire côté zone : les conditions
/// <c>If_CharReputation</c> de <c>CheckCondition</c> lisent
/// <c>CCharacter::GetPrestigeExp(char, rep_id)</c> natif (la map
/// prestige du perso est chargée depuis cette table). L'écriture
/// reste côté mission (<c>GainExp Prestige</c> DBA →
/// <c>prestige.exp</c> upsert), conforme au découpage natif.</summary>
public sealed class PayEventRepository
{
    private readonly NpgsqlDataSource _db;

    public PayEventRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Exp de prestige courante du perso pour un id de
    /// réputation — 0 si la ligne n'existe pas (natif : la map ne
    /// contient l'entrée qu'après le premier gain).</summary>
    public async Task<int> GetPrestigeExpAsync(int playerId,
        int prestigeId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(exp,0) FROM prestige " +
            "WHERE player_id=@p AND prestige_id=@r",
            new { p = playerId, r = prestigeId });
    }
}
