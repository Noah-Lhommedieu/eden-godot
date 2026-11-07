using System.Data;
using Npgsql;

namespace Limitless.Studio.Services;

/// <summary>Accès PostgreSQL fndb1 — même DSN que le game-server.
/// Dapper pour l'édition items/monstres.</summary>
public sealed class GameDb
{
    private readonly string _cs;
    public GameDb(string? cs = null) =>
        _cs = cs ?? "Host=localhost;Port=5432;Database=fndb1;" +
                   "Username=postgres;Password=postgres";
    public IDbConnection Open() => new NpgsqlConnection(_cs);
}
