using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Statistiques battlefield (champ de bataille instancié)
/// — char+0x97c..0x9e0 natifs, 26 entiers : indices 0-11 = mois
/// courant (`battlefield_month`), 12-23 = carrière
/// (`battlefield_career`), 24-25 inutilisés. Requêtes natives
/// (mission DBA) : `SELECT join_count, …, hight_cure FROM
/// battlefield_* WHERE player_id` avec INSERT à zéro si absent.
/// Ordre des colonnes identique dans les deux tables : join, win,
/// lose, mvp, hight_score, kill, die, hight_kill, hight_die,
/// hight_damage, hight_altar_damage, hight_cure.</summary>
public sealed class BattlefieldRepository
{
    private const string Columns =
        "join_count AS J0, win_count AS J1, lose_count AS J2, " +
        "mvp_count AS J3, hight_score AS J4, kill_count AS J5, " +
        "die_count AS J6, hight_kill AS J7, hight_die AS J8, " +
        "hight_damage AS J9, hight_altar_damage AS J10, " +
        "hight_cure AS J11";
    private const string ColNames =
        "join_count, win_count, lose_count, mvp_count, " +
        "hight_score, kill_count, die_count, hight_kill, " +
        "hight_die, hight_damage, hight_altar_damage, hight_cure";

    private readonly NpgsqlDataSource _db;
    public BattlefieldRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Les 26 entiers du bloc char+0x97c. `player_name`/
    /// `player_lv`/`player_class` sont écrits à l'INSERT natif —
    /// identique ici pour garder la ligne cohérente avec les
    /// classements battlefield.</summary>
    public async Task<int[]> LoadStatsAsync(int playerId,
        string playerName, short playerLv, short playerClass)
    {
        var stats = new int[26];
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var month = await ReadRowAsync(connection,
            "battlefield_month", playerId);
        if (month == null)
        {
            await connection.ExecuteAsync(
                $"INSERT INTO battlefield_month (player_id, " +
                $"player_name, player_lv, player_class, {ColNames}, " +
                "mvp_count_weight, kill_count_weight) VALUES (@Id, " +
                "@Name, @Lv, @Class, 0,0,0,0,0,0,0,0,0,0,0,0,0,0) " +
                "ON CONFLICT (player_id) DO NOTHING",
                new { Id = playerId, Name = playerName, Lv = playerLv,
                      Class = playerClass });
            month = new int[12];
        }
        var career = await ReadRowAsync(connection,
            "battlefield_career", playerId);
        if (career == null)
        {
            await connection.ExecuteAsync(
                $"INSERT INTO battlefield_career (player_id, " +
                $"player_name, player_lv, player_class, {ColNames}) " +
                "VALUES (@Id, @Name, @Lv, @Class, 0,0,0,0,0,0,0,0," +
                "0,0,0,0) ON CONFLICT (player_id) DO NOTHING",
                new { Id = playerId, Name = playerName, Lv = playerLv,
                      Class = playerClass });
            career = new int[12];
        }
        Array.Copy(month, stats, 12);
        Array.Copy(career, 0, stats, 12, 12);
        return stats;
    }

    /// <summary>Enregistre un match terminé — incrémente
    /// join_count + win/lose_count + kill/die_count dans les deux
    /// tables (month + career) comme le natif à la fin du match.</summary>
    public async Task RecordMatchAsync(int playerId, bool win,
        int kills = 0, int dies = 0)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "battlefield_month",
            "battlefield_career" })
            await connection.ExecuteAsync(
                $"UPDATE {table} SET join_count = join_count + 1, " +
                $"win_count = win_count + @W, " +
                $"lose_count = lose_count + @L, " +
                $"kill_count = kill_count + @K, " +
                $"die_count = die_count + @D " +
                $"WHERE player_id = @Id",
                new { Id = playerId, W = win ? 1 : 0,
                      L = win ? 0 : 1, K = kills, D = dies });
    }

    /// <summary>Lit les 12 compteurs d'une table battlefield.</summary>
    private static async Task<int[]?> ReadRowAsync(
        NpgsqlConnection connection, string table, int playerId)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            $"SELECT {Columns} FROM {table} WHERE player_id = @Id",
            new { Id = playerId });
        if (row == null) return null;
        var d = (IDictionary<string, object?>)row;
        var values = new int[12];
        for (var i = 0; i < 12; i++)
            values[i] = Convert.ToInt32(d[$"J{i}"]);
        return values;
    }
}
