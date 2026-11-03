using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Dépôt des états de quête persistés — tables natives
/// `personal_mission_states` (id=charUid, index=mission, state) et
/// `mission_trace`. Consommées par 348 ClientMissionStates (NCID client
/// 50 QueryMissionStates) dont le Serialize natif @0x8202790 sérialise
/// `map&lt;u16,u16&gt;` en premier — paires index→state.</summary>
public sealed class MissionRepository
{
    private readonly NpgsqlDataSource _db;

    public MissionRepository(NpgsqlDataSource db)
        => _db = db;

    /// <summary>États de quête personnels d'un perso — lignes
    /// (index, state) de `personal_mission_states` où id = charUid.
    /// Ordre croissant comme l'itération du std::map natif.</summary>
    public async Task<List<(int Index, int State)>>
        GetPersonalMissionStatesAsync(int characterId,
            CancellationToken token = default)
    {
        const string sql =
            "SELECT index AS Index, state AS State " +
            "FROM personal_mission_states WHERE id = @Id ORDER BY index";
        await using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<(int Index, int State)>(
            new CommandDefinition(sql, new { Id = characterId },
                cancellationToken: token));
        return rows.AsList();
    }

    /// <summary>Ids des quêtes terminées d'un perso —
    /// `player_mission_complete_num` (une ligne par achèvement,
    /// complete_num = nombre de fois pour les répétables).</summary>
    public async Task<List<int>> GetCompletedMissionsAsync(int characterId,
        CancellationToken token = default)
    {
        const string sql =
            "SELECT mission_id FROM player_mission_complete_num " +
            "WHERE player_id = @Id ORDER BY mission_id";
        await using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<int>(
            new CommandDefinition(sql, new { Id = characterId },
                cancellationToken: token));
        return rows.AsList();
    }

    /// <summary>États de mission natifs (FMS) d'un perso, décodés.
    /// Source : vue `final_mission_state_v2` (UNION des buffers
    /// state1/state2 — le save DBA natif ping-pongue entre les deux).
    /// Chaque ligne = 30 bits utiles : index=(mid-1)/10, slot k=(mid-1)%10,
    /// mid=index*10+1+k, état = (state&gt;&gt;(k*3))&amp;7.
    /// Sémantique (SetFMS désasm mission @0x8095ad0) :
    ///   0 = aucun, 1 = terminée (4→1 : RemoveFinishItem +
    ///       AddMissionCompleteNum), 3 = en cours (0/6→3 : FMSDoAccept),
    ///   4 = objectifs remplis (3→4 : FMSCheckReward),
    ///   5 = marqueur "checked" (la section vec&lt;s16&gt; du 348 —
    ///       266 entrées dans la capture test1), 6 = reprenable,
    ///   7 = suspendue/échouée.</summary>
    public async Task<Dictionary<int, int>> GetFMSAsync(int characterId,
        CancellationToken token = default)
    {
        const string sql =
            "SELECT index AS Index, state AS State " +
            "FROM final_mission_state_v2 WHERE player_id = @Id";
        await using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<(int Index, int State)>(
            new CommandDefinition(sql, new { Id = characterId },
                cancellationToken: token));
        var result = new Dictionary<int, int>();
        foreach (var r in rows)
            for (var k = 0; k < 10; k++)
            {
                var v = (int)((uint)r.State >> (k * 3)) & 7;
                if (v != 0) result[r.Index * 10 + 1 + k] = v;
            }
        return result;
    }

    /// <summary>Les 10 slots de quêtes trackées (HUD de droite) —
    /// `mission_trace.mission_trace01..10` où id = charUid.</summary>
    public async Task<int[]> GetMissionTraceAsync(int characterId,
        CancellationToken token = default)
    {
        const string sql =
            "SELECT mission_trace01 AS T1, mission_trace02 AS T2, " +
            "mission_trace03 AS T3, mission_trace04 AS T4, " +
            "mission_trace05 AS T5, mission_trace06 AS T6, " +
            "mission_trace07 AS T7, mission_trace08 AS T8, " +
            "mission_trace09 AS T9, mission_trace10 AS T10 " +
            "FROM mission_trace WHERE id = @Id";
        await using var conn = _db.CreateConnection();
        var r = await conn.QueryFirstOrDefaultAsync<TraceRow?>(
            new CommandDefinition(sql, new { Id = characterId },
                cancellationToken: token));
        if (r == null) return new int[10];
        return new[]
        {
            r.T1, r.T2, r.T3, r.T4, r.T5,
            r.T6, r.T7, r.T8, r.T9, r.T10,
        };
    }

    /// <summary>Ligne de mission_trace (10 slots i32).</summary>
    private sealed class TraceRow
    {
        public int T1 { get; set; } public int T2 { get; set; }
        public int T3 { get; set; } public int T4 { get; set; }
        public int T5 { get; set; } public int T6 { get; set; }
        public int T7 { get; set; } public int T8 { get; set; }
        public int T9 { get; set; } public int T10 { get; set; }
    }
}
