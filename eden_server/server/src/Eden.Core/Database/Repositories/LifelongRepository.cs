using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Compteurs « lifelong » d'une classe — miroir du noeud
/// <c>SClassLifelong</c> natif (map char+0xc58 côté zone /
/// exec+0x748 côté mission). La clé n'est PAS le class_id mais le
/// <b>groupe waken</b> : <c>CGameData::GetWakenGroup(classId, raceId)</c>
/// (IGameData vtable +0x1d8) retourne <c>CClassData+8</c> = S_Class
/// col2 (par ex. classe 21/22 → groupe 1, 59-61 → groupe 59).</summary>
public sealed class LifelongCounters
{
    /// <summary>Type 0 — kills en guerre de territoire
    /// (<c>CTerritory::LifelongKillCheck</c> @0x84c45c0, dedup par
    /// victime). <c>SClassLifelong+0x14</c> / col territory_kill.</summary>
    public int TerritoryKill;
    /// <summary>Type 1 — victoires colosseum par membre de l'équipe
    /// gagnante (<c>CColosseum::AllGotoTeamPreparePos</c> @0x8404d00).
    /// <c>SClassLifelong+0x18</c> / col colosseum_win.</summary>
    public int ColosseumWin;
    /// <summary>Type 2 — kills de monstres rares, += Point de
    /// l'achievement du mob (<c>S_Achievement.Type==1</c>,
    /// <c>CMonster::SetMonsterKillAchievement</c> @0x8499170).
    /// <c>SClassLifelong+0x1c</c> / col rare_monster_kill.</summary>
    public int RareMonsterKill;
    /// <summary>Type 3 — kills quotidiens (tout kill à exp>0,
    /// <c>CCharacter::AddLifelongPoint</c> @0x84198c0) — remis à 0 au
    /// changement de jour. <c>SClassLifelong+0x20</c> /
    /// col daily_monster_kill.</summary>
    public int DailyMonsterKill;
    /// <summary>Compteur journalier non encore flushé vers la table —
    /// <c>SClassLifelong+0x24</c> natif (s16) : flush à &gt;9 et à la
    /// sortie du perso.</summary>
    public short PendingDaily;
}

/// <summary>Table native <c>class_lifelong</c> (edenfr_db) —
/// <c>(player_id, class, territory_kill, colosseum_win,
/// rare_monster_kill, daily_monster_kill)</c>, PK (player_id, class).
/// La mission native maintenait une copie mémoire alimentée par les
/// commandes <c>ClassLifelongAdd</c>/<c>ClassLifelongUpdate</c> puis
/// INSERT/UPDATE à la sauvegarde ; chez nous la zone applique les
/// deltas directement (UPSERT <c>col = col + delta</c>), ce qui est
/// aussi plus sûr en multi-zone.</summary>
public sealed class LifelongRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Colonne de compteur par type natif 0-3.</summary>
    private static readonly string[] Columns =
    {
        "territory_kill",      // type 0
        "colosseum_win",       // type 1
        "rare_monster_kill",   // type 2
        "daily_monster_kill",  // type 3
    };

    public LifelongRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Charge toutes les lignes du perso —
    /// <c>CExecutionerDBO::LoadClassLifelong</c> mission
    /// @0x08158840 (SELECT class + 4 compteurs).</summary>
    public async Task<Dictionary<short, LifelongCounters>> LoadAsync(
        int playerId)
    {
        using var c = _db.CreateConnection();
        var rows = await c.QueryAsync<(short Class, int Tk, int Cw,
            int Rk, int Dk)>(
            "SELECT class AS Class, territory_kill AS Tk, " +
            "colosseum_win AS Cw, rare_monster_kill AS Rk, " +
            "daily_monster_kill AS Dk FROM class_lifelong " +
            "WHERE player_id=@p", new { p = playerId });
        var map = new Dictionary<short, LifelongCounters>();
        foreach (var r in rows)
            map[r.Class] = new LifelongCounters
            {
                TerritoryKill = r.Tk, ColosseumWin = r.Cw,
                RareMonsterKill = r.Rk, DailyMonsterKill = r.Dk,
            };
        return map;
    }

    /// <summary>Applique un delta sur un compteur — équivalent de la
    /// commande <c>ClassLifelongAdd class=K type=T add_point=P</c>
    /// (zone→mission @0x8421720, mission <c>TC_ClassLifelongAdd</c>
    /// @0x081f8220 : <c>entry[col] += add_point</c> + dirty).
    /// type ∈ {0 territory, 1 colosseum, 2 rare, 3 daily}.</summary>
    public async Task AddPointAsync(int playerId, short classGroup,
        int type, int delta)
    {
        if (type < 0 || type > 3 || delta == 0) return;
        var col = Columns[type];
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            $"INSERT INTO class_lifelong (player_id, class, {col}) " +
            "VALUES (@p,@g,@d) ON CONFLICT (player_id, class) " +
            $"DO UPDATE SET {col} = class_lifelong.{col} + @d",
            new { p = playerId, g = classGroup, d = delta });
    }

    /// <summary>Or « lifelong » courant (médailles Crystal Cross) —
    /// <c>player_characters.lifelong_gold</c>, chargé dans
    /// <c>exec+0x75c</c> puis copié en <c>char+0xc54</c> à l'attach
    /// (zone @0x8434xxx).</summary>
    public async Task<int> GetGoldAsync(int playerId)
    {
        using var c = _db.CreateConnection();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(lifelong_gold,0) FROM player_characters " +
            "WHERE id=@p", new { p = playerId });
    }

    /// <summary>Persiste l'or lifelong absolu — conservé pour les
    /// commandes GM qui posent une valeur exacte ; les chemins métier
    /// utilisent <see cref="AddGoldAsync"/> (delta atomique).</summary>
    public async Task SetGoldAsync(int playerId, int gold)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE player_characters SET lifelong_gold=@g WHERE id=@p",
            new { p = playerId, g = gold });
    }

    /// <summary>Delta atomique sur <c>player_characters.lifelong_gold</c>
    /// borné [0, 9999999] (cap natif exec+0x75c) — miroir de
    /// <c>MissionDb.AddLifelongGoldAsync</c> ; rend le solde résultant
    /// pour resynchroniser <c>st.LifelongGold</c> en une seule
    /// écriture (l'absolu précédent perdait les écritures
    /// concurrentes).</summary>
    public async Task<int> AddGoldAsync(int playerId, int delta)
    {
        using var c = _db.CreateConnection();
        return await c.ExecuteScalarAsync<int>(
            "UPDATE player_characters SET lifelong_gold = " +
            "LEAST(9999999, GREATEST(0, " +
            "COALESCE(lifelong_gold, 0) + @d)) " +
            "WHERE id = @p RETURNING lifelong_gold",
            new { p = playerId, d = delta });
    }

    /// <summary>Reset quotidien du compteur type 3 d'un perso —
    /// mission <c>Reset DailyMonsterKill</c> (exec+0x760 jour stocké,
    /// dirty→UPDATE) + broadcast zone <c>ZoneServerResetDailyMonsterKill</c>.
    /// Chez nous : reset par perso (lazy au premier accès d'un nouveau
    /// jour) — équivalent puisque la map est privée au perso.</summary>
    public async Task ResetDailyAsync(int playerId)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE class_lifelong SET daily_monster_kill=0 " +
            "WHERE player_id=@p AND daily_monster_kill<>0",
            new { p = playerId });
    }

    /// <summary>Jour du dernier reset daily — le natif le persiste
    /// dans le blob d'attributs exécutant sérialisé
    /// <c>%zX:LastDailyLifelongResetDay</c> (exec+0x760 = tm_mday,
    /// comparé au jour courant au chargement ET sur le timer
    /// quotidien). Notre blob <c>player_characters.flags</c> n'est
    /// pas utilisé par ailleurs — on y loge le même marqueur
    /// <c>;LastDailyLifelongResetDay=N</c>. -1 si absent.</summary>
    public async Task<int> GetResetDayAsync(int playerId)
    {
        using var c = _db.CreateConnection();
        var flags = await c.ExecuteScalarAsync<string>(
            "SELECT COALESCE(flags,'') FROM player_characters " +
            "WHERE id=@p", new { p = playerId });
        const string key = "LastDailyLifelongResetDay=";
        var i = flags?.IndexOf(key, StringComparison.Ordinal) ?? -1;
        if (i < 0) return -1;
        i += key.Length;
        var j = i;
        while (j < flags!.Length && char.IsDigit(flags[j])) j++;
        return int.TryParse(flags.AsSpan(i, j - i), out var d) ? d : -1;
    }

    /// <summary>Persiste le jour du reset dans le blob
    /// <c>flags</c> sans toucher au reste du contenu
    /// (remplace le marqueur s'il existe, sinon l'ajoute).</summary>
    public async Task SetResetDayAsync(int playerId, int day)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE player_characters SET flags = CASE " +
            "WHEN flags LIKE '%LastDailyLifelongResetDay=%' " +
            "THEN regexp_replace(flags, " +
            "'LastDailyLifelongResetDay=[0-9]+', " +
            "'LastDailyLifelongResetDay=' || @d::text) " +
            "ELSE flags || ';LastDailyLifelongResetDay=' || @d::text " +
            "END WHERE id=@p",
            new { p = playerId, d = day });
    }
}
