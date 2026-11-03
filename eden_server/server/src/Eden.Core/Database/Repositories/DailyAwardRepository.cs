using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès au calendrier de connexion quotidien — tables
/// natives `daily_awards` (état par perso, `id` = player_id :
/// day_index s16, login_time/cooldown_time i32, award_days et
/// award_records u64, login_status) et `daily_awards_set` (flags
/// globaux, ligne tag=-2 → zone +0x638/+0x639 natifs). SQL natif
/// mission : `SELECT … FROM daily_awards WHERE id = %d`, INSERT
/// à zéro au premier login, UPDATE complet à chaque
/// `UpdateDailyAwardToDB`.</summary>
public sealed class DailyAwardRepository
{
    private readonly NpgsqlDataSource _db;

    public DailyAwardRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>État persistant du calendrier pour un perso.
    /// <c>DayIndex</c> est <see cref="int"/> : la colonne native
    /// <c>day_index</c> est un integer PG (pas un smallint) — un
    /// record à ctor <c>short</c> cassait la matérialisation Dapper
    /// ("no matching constructor") ce qui faisait avorter TOUT le
    /// handler ticket en amont (InstanceGroup non résolu).</summary>
    public sealed record DailyAwardRow(
        int DayIndex, int LoginTime, int CooldownTime,
        long AwardDays, long AwardRecords, int LoginStatus);

    /// <summary>Flags globaux (`daily_awards_set` tag=-2) —
    /// le système est actif quand les deux bits bas sont posés
    /// (flags=3, zone +0x638/+0x639). Absent → considéré actif
    /// (GetDailyAwardSetting native réinsère la ligne).</summary>
    public async Task<bool> IsEnabledAsync()
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var flags = await connection.ExecuteScalarAsync<int?>(
            "SELECT flags FROM daily_awards_set WHERE tag=-2 " +
            "LIMIT 1");
        return flags == null || (flags & 3) == 3;
    }

    /// <summary>État du perso — INSERT à zéro si absent
    /// (`INSERT INTO daily_awards(id, day_index, login_time,
    /// cooldown_time, award_days, award_records, login_status)
    /// VALUES (%d, 0,0,0,0,0,0)` natif au premier login).</summary>
    public async Task<DailyAwardRow> GetAsync(int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync<
            DailyAwardRow>(
            "SELECT day_index AS DayIndex, login_time AS LoginTime, " +
            "cooldown_time AS CooldownTime, award_days AS AwardDays, " +
            "award_records AS AwardRecords, login_status AS LoginStatus " +
            "FROM daily_awards WHERE id=@Id", new { Id = playerId });
        if (row != null) return row;
        await connection.ExecuteAsync(
            "INSERT INTO daily_awards (id, day_index, login_time, " +
            "cooldown_time, award_days, award_records, login_status) " +
            "VALUES (@Id, 0, 0, 0, 0, 0, 0) ON CONFLICT (id) DO NOTHING",
            new { Id = playerId });
        return new DailyAwardRow(0, 0, 0, 0, 0, 0);
    }

    /// <summary>Persiste l'état complet — équivalent du `UPDATE
    /// daily_awards SET day_index=%hd, login_time=%d,
    /// cooldown_time=%d, award_days=%lld, award_records=%lld,
    /// login_status=%d WHERE id=%lu` natif, exprimé en UPSERT unique
    /// (INSERT … ON CONFLICT (id) DO UPDATE, PK `id`) : même effet,
    /// un seul aller-retour au lieu de UPDATE+INSERT conditionnel.</summary>
    public async Task SaveAsync(int playerId, short dayIndex,
        int loginTime, int cooldownTime, long awardDays,
        long awardRecords, int loginStatus)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO daily_awards (id, day_index, login_time, " +
            "cooldown_time, award_days, award_records, login_status) " +
            "VALUES (@Id, @Di, @Lt, @Cd, @Ad, @Ar, @Ls) " +
            "ON CONFLICT (id) DO UPDATE SET " +
            "day_index = EXCLUDED.day_index, " +
            "login_time = EXCLUDED.login_time, " +
            "cooldown_time = EXCLUDED.cooldown_time, " +
            "award_days = EXCLUDED.award_days, " +
            "award_records = EXCLUDED.award_records, " +
            "login_status = EXCLUDED.login_status",
            new { Di = dayIndex, Lt = loginTime, Cd = cooldownTime,
                  Ad = awardDays, Ar = awardRecords, Ls = loginStatus,
                  Id = playerId });
    }
}
