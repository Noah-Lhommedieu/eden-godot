using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>L'état d'une route du destin telle que la persiste
/// `player_destiny_road` — équivalent du bloc `SDestinyRoad`
/// (char+0xbc8 natif) + le flag reroll (+0xbe4, non persisté par le
/// natif — flag de session).</summary>
public sealed class DestinyRoadRow
{
    public int PlayerId;
    /// <summary>road_id natif (0 = pas de route active).</summary>
    public int RoadId;
    /// <summary>road_type natif (+0xbca) — backdrop/type de route.</summary>
    public int RoadType;
    /// <summary>step natif (+0xbcc) — case courante (0..38).</summary>
    public int Step;
    /// <summary>dice_time natif (+0xbce) — dés restants (10 à la création).</summary>
    public int RollsLeft;
    /// <summary>exp_buff natif (+0xbd0, i32) — réserve de Destiny Points
    /// dépensée à chaque avancée (coût = paramètre 376).</summary>
    public int DpPool;
    /// <summary>dice_point natif (+0xbd4) — valeur du dernier dé /
    /// points de mouvement en attente.</summary>
    public int MovePoints;
    /// <summary>function_state natif (+0xbd6) — bitmask des fonctions
    /// déjà utilisées (1 &lt;&lt; funcId).</summary>
    public int FunctionState;
    /// <summary>Event ids des 39 cases (slots 1-39).</summary>
    public int[] Slots { get; set; } = new int[39];
}

/// <summary>
/// Dépôt `player_destiny_road` + `player_characters.destiny_vit` —
/// équivalent des `CExecutionerDBO::LoadDestinyRoad` /
/// `UpdateDestinyRoadToDB` / `TC_ClearDestinyRoad` natifs
/// (mission @ 0x08159700 / 0x08147ee0 / 0x081f7cf0). Dans notre
/// architecture la zone persiste directement (le relais texte
/// « Update DestinyRoad » de la mission native n'existe pas —
/// la mission C# ne détient pas de CExecutionerData).
/// </summary>
public sealed class DestinyRoadRepository
{
    private readonly NpgsqlDataSource _db;

    public DestinyRoadRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>SELECT natif des 46 colonnes (road_id, road_type,
    /// step, dice_time, exp_buff, dice_point, function_state, slots 1-39).
    /// Null si aucune route — le natif Reset alors le bloc.</summary>
    public async Task<DestinyRoadRow?> GetAsync(int charId)
    {
        const string sql = @"
            SELECT player_id AS PlayerId, road_id AS RoadId,
                   road_type AS RoadType, step AS Step,
                   dice_time AS RollsLeft, exp_buff AS DpPool,
                   dice_point AS MovePoints,
                   function_state AS FunctionState,
                   slot_1, slot_2, slot_3, slot_4, slot_5, slot_6,
                   slot_7, slot_8, slot_9, slot_10, slot_11, slot_12,
                   slot_13, slot_14, slot_15, slot_16, slot_17, slot_18,
                   slot_19, slot_20, slot_21, slot_22, slot_23, slot_24,
                   slot_25, slot_26, slot_27, slot_28, slot_29, slot_30,
                   slot_31, slot_32, slot_33, slot_34, slot_35, slot_36,
                   slot_37, slot_38, slot_39
            FROM player_destiny_road WHERE player_id = @CharId";
        using var c = _db.CreateConnection();
        var r = await c.QuerySingleOrDefaultAsync<dynamic>(sql,
            new { CharId = charId });
        if (r == null) return null;
        var row = new DestinyRoadRow
        {
            PlayerId = (int)r.player_id, RoadId = (int)r.road_id,
            RoadType = (int)r.road_type, Step = (int)r.step,
            RollsLeft = (int)r.dice_time, DpPool = (int)r.exp_buff,
            MovePoints = (int)r.dice_point,
            FunctionState = (int)r.function_state,
        };
        var slots = (IDictionary<string, object>)r;
        for (var i = 0; i < 39; i++)
            row.Slots[i] = Convert.ToInt32(slots[$"slot_{i + 1}"]);
        return row;
    }

    /// <summary>UPDATE ou INSERT selon l'existence — sémantique de
    /// `UpdateDestinyRoadToDB` natif (GetDestinyRoadUpdateSQLCmd
    /// @0x0832e860 / GetDestinyRoadInsertSQLCmd @0x0832ea40) : la table
    /// native n'a pas de PK, le natif tente l'UPDATE puis INSERT.</summary>
    public async Task UpsertAsync(DestinyRoadRow row)
    {
        var p = new DynamicParameters(row);
        for (var i = 0; i < 39; i++)
            p.Add($"S{i + 1}", (short)row.Slots[i]);
        var upd = $@"
            UPDATE player_destiny_road SET
                road_id = @RoadId, road_type = @RoadType, step = @Step,
                dice_time = @RollsLeft, exp_buff = @DpPool,
                dice_point = @MovePoints,
                function_state = @FunctionState,
                {string.Join(", ", Enumerable.Range(1, 39)
                    .Select(i => $"slot_{i} = @S{i}"))}
            WHERE player_id = @PlayerId";
        using var c = _db.CreateConnection();
        var n = await c.ExecuteAsync(upd, p);
        if (n == 0)
        {
            var slotCols = string.Join(", ",
                Enumerable.Range(1, 39).Select(i => $"slot_{i}"));
            var slotParams = string.Join(", ",
                Enumerable.Range(1, 39).Select(i => $"@S{i}"));
            var ins = $@"
                INSERT INTO player_destiny_road
                    (player_id, road_id, road_type, step, dice_time,
                     exp_buff, dice_point, function_state, {slotCols})
                VALUES (@PlayerId, @RoadId, @RoadType, @Step,
                        @RollsLeft, @DpPool, @MovePoints,
                        @FunctionState, {slotParams})";
            await c.ExecuteAsync(ins, p);
        }
    }

    /// <summary>`Clear DestinyRoad` natif — DELETE de la ligne à la
    /// fin d'une route (TC_ClearDestinyRoad @ 0x081f7cf0).</summary>
    public async Task DeleteAsync(int charId)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "DELETE FROM player_destiny_road WHERE player_id = @CharId",
            new { CharId = charId });
    }

    /// <summary>`player_characters.destiny_vit` — vitalité destin
    /// (exec+0x6b6 natif, cap = 100 × paramètre 569).</summary>
    public async Task<int> GetVitalityAsync(int charId)
    {
        using var c = _db.CreateConnection();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(destiny_vit, 0) FROM player_characters" +
            " WHERE id = @CharId", new { CharId = charId });
    }

    /// <summary>UPDATE destiny_vit — équivalent du champ exec+0x6b6
    /// sauvegardé dans le gros UPDATE player_characters natif.</summary>
    public async Task SetVitalityAsync(int charId, int vit)
    {
        using var c = _db.CreateConnection();
        await c.ExecuteAsync(
            "UPDATE player_characters SET destiny_vit = @Vit" +
            " WHERE id = @CharId", new { CharId = charId, Vit = vit });
    }
}
