using Dapper;
using Npgsql;

namespace Eden.Core.Database;

/// <summary>
/// Allocateur d'ids <c>player_mail</c> — séquence PostgreSQL
/// <c>mail_serial</c> (migration <c>Database/012-item-serial-sequence.sql</c>).
/// La colonne n'a ni séquence ni trigger natif ; le serveur natif
/// allouait les ids courriers depuis un compteur global comme celui
/// des serials d'items.
///
/// <c>nextval</c> remplace l'ancien seed max(id)+1 + Interlocked qui
/// ne coordonnait qu'un seul processus — <c>player_mail.id</c> est
/// PRIMARY KEY, une collision = INSERT en échec. Même classe de bug
/// que <see cref="ItemSerialAllocator"/>, corrigée au passage.
/// </summary>
public static class MailIdAllocator
{
    /// <summary>Prochain id de mail — atomique et cross-process.</summary>
    public static async Task<int> NextAsync(NpgsqlDataSource db)
    {
        using var connection = db.CreateConnection();
        return await NextAsync(connection, null);
    }

    /// <summary>Variante pour un appelant déjà dans une connexion /
    /// transaction — miroir de <see cref="ItemSerialAllocator"/> :
    /// un id consommé puis rollback laisse un trou, sans conséquence.
    /// </summary>
    public static async Task<int> NextAsync(NpgsqlConnection connection,
        NpgsqlTransaction? tx = null)
        // nextval rend bigint — cast borné : player_mail.id est int4.
        => checked((int)await connection.ExecuteScalarAsync<long>(
            "SELECT nextval('mail_serial')", transaction: tx));
}
