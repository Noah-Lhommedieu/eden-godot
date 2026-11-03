using Dapper;
using Eden.Core.Database;
using Npgsql;

namespace Eden.Tests;

/// <summary>DB-2 : search_path par rôle et FKs compte↔personnage de la base
/// fusionnée <c>edenfr</c>. Les tests live s'auto-skippent si la base est absente.</summary>
public class DbConnectionFactoryTests
{
    [Theory]
    [InlineData(DbRole.Game, "game,public")]
    [InlineData(DbRole.Account, "account,public")]
    [InlineData(DbRole.Member, "member,public")]
    public void WithSearchPath_AppliesRoleSchema(DbRole role, string expected)
    {
        var s = DbConnectionFactory.WithSearchPath(
            "Host=h;Database=edenfr;Username=u;Password=p", role);
        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(s).SearchPath);
    }

    private static async Task<NpgsqlDataSource?> Open(DbRole role)
    {
        var ds = NpgsqlDataSource.Create(DbConnectionFactory.WithSearchPath(
            TestDb.ConnStr("edenfr_db"), role));
        try
        {
            await using var c = await ds.OpenConnectionAsync();
            var ok = await c.ExecuteScalarAsync<bool>(
                "select exists(select 1 from pg_namespace where nspname='game')");
            return ok ? ds : null;
        }
        catch (NpgsqlException) { return null; }
    }

    [Fact]
    public async Task SameTableName_ResolvesToRoleSchema()
    {
        var game = await Open(DbRole.Game);
        var acc = await Open(DbRole.Account);
        if (game is null || acc is null) return;
        // `worlds` existe dans game ET account : le search_path les isole.
        Assert.Equal("game", await game.CreateConnection().ExecuteScalarAsync<string>(
            "select schemaname from pg_tables where tablename='worlds' and schemaname = any(current_schemas(false))"));
        Assert.Equal("account", await acc.CreateConnection().ExecuteScalarAsync<string>(
            "select schemaname from pg_tables where tablename='worlds' and schemaname = any(current_schemas(false))"));
    }

    [Fact]
    public async Task CharacterWithUnknownAccount_IsRejectedByFk()
    {
        var game = await Open(DbRole.Game);
        if (game is null) return;
        await using var c = await game.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => c.ExecuteAsync(
            "insert into game.player_characters (id, account_id, account_name) values (-1, -999, 'fk-test')", transaction: tx));
        Assert.Equal("23503", ex.SqlState); // foreign_key_violation
    }
}
