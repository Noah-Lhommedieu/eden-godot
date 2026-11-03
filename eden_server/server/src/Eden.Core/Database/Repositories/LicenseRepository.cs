using Dapper;
using Npgsql;
using Eden.Core.Game.Classes;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Implémentation Dapper de <see cref="ILicenseRepository"/> —
/// `player_license` natif (PK composite id+item_id+loc).
/// </summary>
public sealed class LicenseRepository : ILicenseRepository
{
    private readonly NpgsqlDataSource _db;

    public LicenseRepository(NpgsqlDataSource db)
        => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenseEntity>> GetLicensesAsync(
        int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // item_id est un integer : les ids > 32767 (33xxx/34xxx) sont
        // stockés en s16 négatif par AddLicenseAsync — la normalisation
        // CASE rend aussi tolérante la lecture des lignes écrites en
        // positif (migration manuelle, autre writer).
        var rows = await connection.QueryAsync<LicenseEntity>(
            "SELECT id AS PlayerId, " +
            "CASE WHEN item_id > 32767 THEN item_id - 65536 " +
            "ELSE item_id END AS ItemId, loc AS Loc " +
            "FROM player_license WHERE id=@Id",
            new { Id = playerId });
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task AddLicenseAsync(int playerId, short itemId,
        short loc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // INSERT idempotent : GetLicense natif refuse le doublon en
        // amont — ON CONFLICT couvre les courses (récompense quête
        // doublée, level-up rejoué).
        await connection.ExecuteAsync(
            "INSERT INTO player_license (id, item_id, loc) " +
            "VALUES (@Id, @Item, @Loc) " +
            "ON CONFLICT (id, item_id, loc) DO NOTHING",
            new { Id = playerId, Item = itemId, Loc = loc });
    }

    /// <inheritdoc />
    public async Task SetLicenseLocAsync(int playerId, short itemId,
        short loc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // SQL natif : UPDATE player_license SET loc=%hhd
        // WHERE id=%d AND item_id=%hd — le natif filtre aussi sur
        // loc mais notre PK composite exige la ligne courante :
        // on met à jour par (id,item_id) quelle que soit la loc.
        await connection.ExecuteAsync(
            "UPDATE player_license SET loc=@Loc " +
            "WHERE id=@Id AND item_id=@Item",
            new { Id = playerId, Item = itemId, Loc = loc });
    }

    /// <inheritdoc />
    public async Task RemoveLicenseAsync(int playerId, short itemId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM player_license WHERE id=@Id AND item_id=@Item",
            new { Id = playerId, Item = itemId });
    }
}
