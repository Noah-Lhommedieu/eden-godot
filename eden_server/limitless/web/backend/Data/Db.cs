using System.Data;
using Npgsql;

namespace Limitless.Api.Data;

/// <summary>Accès PostgreSQL — deux bases : fnaccount (comptes)
/// et fndb1 (persos/jeu). Même DSN que les services du game-server.
/// </summary>
public sealed class Db
{
    private readonly string _account, _game, _content, _live;
    public Db(string account, string game, string content = "", string live = "") { _account = account; _game = game; _content = content; _live = live; }
    /// <summary>Base STATIQUE du jeu (eden_content) : lecture du wiki, édition admin + historique.</summary>
    public IDbConnection Content() => new NpgsqlConnection(_content);
    /// <summary>Base DYNAMIQUE du jeu (eden_live), en LECTURE seule côté site : villes de guilde, classements.</summary>
    public IDbConnection Live() => new NpgsqlConnection(_live);
    public IDbConnection Account() => new NpgsqlConnection(_account);
    public IDbConnection Game() => new NpgsqlConnection(_game);
}
