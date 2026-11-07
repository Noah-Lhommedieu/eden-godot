using System.Data;
using Npgsql;

namespace Limitless.Api.Data;

/// <summary>Accès PostgreSQL — deux bases : fnaccount (comptes)
/// et fndb1 (persos/jeu). Même DSN que les services du game-server.
/// </summary>
public sealed class Db
{
    private readonly string _account, _game;
    public Db(string account, string game) { _account = account; _game = game; }
    public IDbConnection Account() => new NpgsqlConnection(_account);
    public IDbConnection Game() => new NpgsqlConnection(_game);
}
