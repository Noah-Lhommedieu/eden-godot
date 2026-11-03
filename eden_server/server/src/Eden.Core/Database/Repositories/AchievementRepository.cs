using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Table native <c>player_characters_achievement</c>
/// (player_id, achievement_id, point, success_num) — la map
/// <c>exec+0x528</c> du mission natif : par succès, le nombre de
/// déclenchements (success_num) et le total de points gagnés.
/// Le succès est « complet » quand success_num &gt;= NeedNum
/// (S_Achievement col1). La mission native écrivait via
/// <c>CExecutionerDBO::AddAchievement/UpdateAchievement</c> — chez nous
/// la zone applique la même sémantique directement.</summary>
public sealed class AchievementRepository
{
    private readonly NpgsqlDataSource _db;

    public AchievementRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Charge la progression complète du perso —
    /// <c>Load Personal Achievement Data</c> natif.</summary>
    public async Task<Dictionary<int, (int SuccessNum, int Point)>>
        LoadAsync(int playerId)
    {
        using var c = _db.CreateConnection();
        var rows = await c.QueryAsync<(int AchId, int Point, int Num)>(
            "SELECT achievement_id AS AchId, point AS Point, " +
            "success_num AS Num FROM player_characters_achievement " +
            "WHERE player_id=@p", new { p = playerId });
        return rows.ToDictionary(r => r.AchId,
            r => (r.Num, r.Point));
    }

    /// <summary>INSERT ou UPDATE la ligne du succès — natif :
    /// AddAchievement à la création, UpdateAchievement ensuite.</summary>
    public async Task UpsertAsync(int playerId, int achievementId,
        int successNum, int point)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "INSERT INTO player_characters_achievement " +
            "(player_id, achievement_id, success_num, point) " +
            "VALUES (@p,@a,@n,@pt) " +
            "ON CONFLICT (player_id, achievement_id) DO UPDATE SET " +
            "success_num=@n, point=@pt",
            new { p = playerId, a = achievementId, n = successNum,
                  pt = point });
    }

    /// <summary>Total de points de succès affiché au classement —
    /// <c>player_achievement_rank.total_point</c> (exec+0x53c natif).
    /// Upsert best-effort : les autres colonnes du rank sont maintenues
    /// par la tâche de classement, ici seul le total est suivi.</summary>
    /// <summary>Màj du classement — UPDATE natif mission
    /// @0x083349e7 : total + 18 colonnes de catégories lues dans la
    /// map cumulative (exec+0x548). Mapping prouvé : monster_kill =
    /// map[type 1], family_instance = map[type 5]. collection_point
    /// (clé 50) et class_point (clé 51 = somme triangulaire des
    /// niveaux de classe) et les colonnes instance/family_* restent
    /// 0 — agrégats spéciaux non portés.</summary>
    public async Task UpdateRankTotalAsync(int playerId,
        string playerName, int level, int totalPoint,
        IReadOnlyDictionary<short, int>? groups = null)
    {
        using var c = _db.CreateConnection();
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var kill = groups?.GetValueOrDefault((short)1) ?? 0;
        var fam = groups?.GetValueOrDefault((short)5) ?? 0;
        await c.ExecuteAsync(
            // Toutes les colonnes NOT NULL doivent être listées — la
            // table locale reprend le CREATE natif (23 colonnes,
            // family_mine/plant/summon/alchemy/casting/enchant sans
            // défaut). total_base_point/gender ont des défauts.
            "INSERT INTO player_achievement_rank " +
            "(player_id, player_name, player_lv, update_time, " +
            " total_point, collection_point, class_point, " +
            " monster_kill, family_instance, lv01_instance_node, " +
            " lv31_instance_node, lv46_instance_node, " +
            " lv61_instance_node, lv76_instance_node, " +
            " lv91_instance_node, family_boss, rare_monster, " +
            " family_mine, family_plant, family_summon, " +
            " family_alchemy, family_casting, family_enchant) " +
            "VALUES (@p,@n,@lv,@now,@pt,0,0,@mk,@fi," +
            "0,0,0,0,0,0,0,0,0,0,0,0,0) " +
            "ON CONFLICT (player_id) DO UPDATE SET " +
            "player_name=@n, player_lv=@lv, update_time=@now, " +
            "total_point=@pt, monster_kill=@mk, family_instance=@fi",
            new { p = playerId, n = playerName, lv = level, now,
                  pt = totalPoint, mk = kill, fi = fam });
    }

    /// <summary>Mail de récompense — <c>SendSystemMail</c> natif
    /// (zone @0x082fd202, déclenché par
    /// <c>CNE_ZMis_ZoneServerAchievementRewardMail</c>) : title/expéd
    /// = T_TextIndex 0x8f5/0x8f6, corps 0x8f7 avec %s = nom du succès,
    /// item joint = S_Achievement col11 ×1. <paramref name="amount"/> =
    /// quantité (colonne durability du mailitem = taille de pile des
    /// consommables/objets de quête), utilisée par le courrier des
    /// mutants.</summary>
    public async Task InsertRewardMailAsync(int playerId, int itemId,
        string sender, string title, string body, int amount = 1)
    {
        await using var c = _db.CreateConnection();
        var mailId = await MailIdAllocator.NextAsync(_db);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await c.ExecuteAsync(
            @"INSERT INTO player_mail
                (id, receiver_id, sender_id, sender_name, send_time,
                 due_date, title, content, item_id, gold, crystal,
                 cod, opened, returned, authoritative)
              VALUES (@mailId, @playerId, 0, @sender, @now,
                 0, @title, @body, @itemId, 0, 0, 0, 0, 0, 0)",
            new { mailId, playerId, sender, now, title, body, itemId });
        var serial = await ItemSerialAllocator.NextAsync(_db);
        await c.ExecuteAsync(
            @"INSERT INTO mailitem
                (id, item_id, durability, player_id, mail_id,
                 create_time)
              VALUES (@serial, @itemId, @amount, @playerId, @mailId, @now)",
            new { serial, itemId, amount = Math.Max(1, amount), playerId,
                  mailId, now });
    }

    // NOTE : l'écriture prestige a migré vers PrestigeRepository
    // (sémantique native complète : gate id, level=1, clamp 75000,
    // notification 60/update_prestige par le monde).
}
