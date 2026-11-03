using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Implémentation PostgreSQL du dépôt de facturation item mall
/// (edenfr_member.tb_user — <c>mid</c>/<c>pvalues</c>/<c>bonus</c>).
/// Reversé depuis lapis::CUserDBO.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly NpgsqlDataSource _db;

    public UserRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<(int Pvalues, int Bonus)?> GetItemMallPointsAsync(
        string memberId, CancellationToken token = default)
    {
        // Schéma réel edenfr_member.tb_user : mid / pvalues / bonus.
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync(token);
        var row = await connection.QueryFirstOrDefaultAsync<(int, int)?>(
            new CommandDefinition(
                "SELECT pvalues, bonus FROM tb_user WHERE mid = @mid LIMIT 1;",
                new { mid = memberId }, cancellationToken: token));
        return row;
    }

    /// <inheritdoc />
    public async Task<bool> TryDeductItemMallPointsAsync(string memberId,
        int currency, int cost, CancellationToken token = default)
    {
        // Monnaie 2 = bonus ; 1 (et 3 = temps de jeu, pas de billing
        // externe chez nous — HYPOTHESIS: rabattu sur pvalues).
        var column = currency == 2 ? "bonus" : "pvalues";
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync(token);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE tb_user SET {column} = {column} - @cost " +
            "WHERE mid = @mid AND " + column + " >= @cost;",
            new { mid = memberId, cost }, cancellationToken: token));
        return affected > 0;
    }

    /// <summary>Crédite des points item mall (commande GM /
    /// compensation) — même mapping monnaie que la déduction :
    /// 2 = bonus (EP), autre = pvalues (AP).</summary>
    public async Task<bool> AddItemMallPointsAsync(string memberId,
        int currency, int amount, CancellationToken token = default)
    {
        var column = currency == 2 ? "bonus" : "pvalues";
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync(token);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE tb_user SET {column} = {column} + @amount " +
            "WHERE mid = @mid;",
            new { mid = memberId, amount }, cancellationToken: token));
        return affected > 0;
    }
}
