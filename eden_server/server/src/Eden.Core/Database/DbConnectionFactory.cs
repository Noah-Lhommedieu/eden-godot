using Npgsql;

namespace Eden.Core.Database;

/// <summary>Rôle logique d'une connexion : chaque rôle voit « sa »
/// partie de la base unique <c>edenfr</c> via le <c>search_path</c>.</summary>
public enum DbRole
{
    /// <summary>Données de jeu — schéma <c>game</c> (ex-<c>edenfr_db</c>).</summary>
    Game,
    /// <summary>Comptes, item mall — schéma <c>account</c> (ex-<c>edenfr_account</c>).</summary>
    Account,
    /// <summary>Points/billing — schéma <c>member</c> (ex-<c>edenfr_member</c>).</summary>
    Member,
}

/// <summary>
/// Point unique de création des <see cref="NpgsqlDataSource"/> (DB-2 :
/// fusion des 3 bases en <c>edenfr</c> à schémas <c>game</c>/<c>account</c>/<c>member</c>).
/// <para>Une SEULE chaîne : <c>EDEN_DB_CONN</c>. Chaque rôle la reprend
/// et y ajoute son <c>SearchPath</c>, de sorte que les ~545 requêtes
/// Dapper (tables non qualifiées) résolvent vers le bon schéma sans
/// réécriture, et que les collisions de noms (<c>worlds</c>,
/// <c>lottery</c>, <c>configuration</c>…) restent isolées. Les requêtes
/// inter-schémas (FK compte↔personnage) sont qualifiées explicitement.</para>
/// <para>Rollback : si <c>EDEN_DB_CONN</c> est absente, les anciennes
/// variables <c>DB_CONN</c>/<c>ACCOUNT_DB_CONN</c>/<c>MEMBER_DB_CONN</c>
/// pointant les 3 bases d'origine sont honorées telles quelles.</para>
/// </summary>
public static class DbConnectionFactory
{
    /// <summary>Chaîne par défaut (stack locale PG17 :5433, base unique).</summary>
    public const string DefaultConn =
        "Host=localhost;Port=5433;Database=edenfr;Username=postgres;Password=postgres";

    /// <summary>Valeur de <c>SearchPath</c> par rôle.</summary>
    public static string SearchPath(DbRole role) => role switch
    {
        DbRole.Game => "game,public",
        DbRole.Account => "account,public",
        DbRole.Member => "member,public",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Chaîne de connexion effective du rôle (voir résumé de la classe).</summary>
    public static string ConnectionString(DbRole role)
    {
        var unified = EdenConfig.UnifiedDbConn;
        if (!string.IsNullOrWhiteSpace(unified))
            return WithSearchPath(unified, role);

        var legacy = role switch
        {
            DbRole.Game => EdenConfig.DbConn ?? EdenConfig.GameDbConn,
            DbRole.Account => EdenConfig.AccountDbConn,
            _ => EdenConfig.MemberDbConn,
        };
        return legacy ?? WithSearchPath(DefaultConn, role);
    }

    /// <summary>Ajoute le <c>SearchPath</c> du rôle à une chaîne unifiée.</summary>
    public static string WithSearchPath(string conn, DbRole role)
    {
        var b = new NpgsqlConnectionStringBuilder(conn) { SearchPath = SearchPath(role) };
        // Chaque rôle a son pool : 3 pools × Maximum Pool Size (défaut 100) par
        // process dépassent vite max_connections de PG (constaté à ~300 CCU, DB-4).
        if (EdenConfig.DbPoolMax is { } max) b.MaxPoolSize = max;
        return b.ConnectionString;
    }

    /// <summary>Crée le pool propriétaire du rôle (à conserver pour la vie du process).</summary>
    public static NpgsqlDataSource Create(DbRole role)
        => NpgsqlDataSource.Create(ConnectionString(role));

    /// <summary>Description loggable (sans mot de passe) : <c>edenfr[game,public]</c>.</summary>
    public static string Describe(DbRole role)
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionString(role));
        return string.IsNullOrEmpty(b.SearchPath)
            ? b.Database ?? "?"
            : $"{b.Database}[{b.SearchPath}]";
    }
}
