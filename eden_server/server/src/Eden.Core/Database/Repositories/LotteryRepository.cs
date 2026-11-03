using Dapper;
using Eden.Core.Game.Lottery;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès aux tables lottery — port des DBO natifs :
/// <list type="bullet">
/// <item>Tables de récompenses (edenfr_account, base `lapis::data` du
/// world) : `lottery` (autel normal), `ingame_lottery` (Eden Crystal),
/// `new_lottery` (Eden Gem/diamond) —
/// `SELECT round, item_index, item_id, item_amount, probability,
/// bulletin, reset_bonus, cant_gonext WHERE week = tm_wday` +
/// `WHERE round = 0` pour les paliers hebdo.</item>
/// <item>État joueur (edenfr_db, CCharacterDBO) : `lottery`,
/// `in_game_lottery`, `new_lottery` — même préfixe mais tables
/// distinctes. SELECT natif (mission/strings.txt) : type, item_id,
/// durability, counts, plus, update, event_flag, crystal_b[, _c],
/// extra_*, reset_bonus, can_go_next.</item>
/// <item>`player_lottery_point` (edenfr_db) — points/paliers hebdo
/// par compte : `SELECT … WHERE account_id = %u` + UPDATE/INSERT.</item>
/// </list></summary>
public interface ILotteryRepository
{
    /// <summary>Toutes les lignes d'une table de récompenses
    /// (edenfr_account). Le filtre `week` est appliqué côté service —
    /// les paliers round=0 doivent être chargés pour toutes les
    /// semaines.</summary>
    Task<List<LotteryReward>> LoadRewardsAsync(AltarKind altar);

    /// <summary>État persisté d'un perso pour l'autel — null si
    /// aucune ligne (premier tirage → INSERT au stage 2 natif).</summary>
    Task<PlayerLotteryState?> GetStateAsync(int playerId, AltarKind altar);

    /// <summary>InsertCharacterXxxLotteryData — INSERT natif à
    /// counts &lt; 2 (Insert Elf/InGame/DiamondLotteryData).</summary>
    Task InsertStateAsync(int playerId, AltarKind altar,
        PlayerLotteryState s);

    /// <summary>UpdateCharacterXxxLotteryData — les colonnes du
    /// `Update LotteryData` natif : type, item_id, durability, counts,
    /// event_flag, crystal_b[, crystal_c], reset_bonus, can_go_next.
    /// (Pas de colonne input ni extra — DBOs séparés.)</summary>
    Task UpdateStateAsync(int playerId, AltarKind altar,
        PlayerLotteryState s);

    /// <summary>UpdateCharacterXxxLotteryBonus — `SET extra_item_id,
    /// extra_counts, extra_multiple`.</summary>
    Task UpdateBonusAsync(int playerId, AltarKind altar,
        PlayerLotteryState s);

    /// <summary>ResetCharacterXxxLotteryBonus — extra_* à zéro.</summary>
    Task ResetBonusAsync(int playerId, AltarKind altar);

    /// <summary>UpdateCharacterXxxLotteryPlus — `SET lottery_plus`.</summary>
    Task UpdatePlusAsync(int playerId, AltarKind altar,
        PlayerLotteryState s);

    /// <summary>UpdateCharacterXxxLotteryUpdate — `SET lottery_update`
    /// (timestamp de décroissance du plus).</summary>
    Task UpdatePlusTimestampAsync(int playerId, AltarKind altar,
        PlayerLotteryState s);

    /// <summary>DeleteCharacterXxxLotteryData — lose natif.</summary>
    Task DeleteStateAsync(int playerId, AltarKind altar);

    /// <summary>Points/paliers hebdo du compte
    /// (`SELECT … FROM player_lottery_point WHERE account_id`).</summary>
    Task<LotteryWeekPoints?> GetWeekPointsAsync(int accountId);

    /// <summary>INSERT de la ligne `player_lottery_point`.</summary>
    Task InsertWeekPointsAsync(int accountId, LotteryWeekPoints p);

    /// <summary>UPDATE des 4 compteurs `player_lottery_point`.</summary>
    Task UpdateWeekPointsAsync(int accountId, LotteryWeekPoints p);
}

/// <summary>Implémentation Dapper — deux connexions : edenfr_db (état)
/// et edenfr_account (récompenses), comme les bases `edenfr_db`/`lapis::data`
/// des binaires natifs.</summary>
public sealed class LotteryRepository : ILotteryRepository
{
    private readonly NpgsqlDataSource _gameDb;     // edenfr_db
    private readonly NpgsqlDataSource _accountDb;  // edenfr_account

    public LotteryRepository(NpgsqlDataSource gameDb,
        NpgsqlDataSource accountDb)
    {
        _gameDb = gameDb;
        _accountDb = accountDb;
    }

    /// <summary>Table edenfr_db d'état par autel (lottery = normal,
    /// in_game_lottery, new_lottery = diamond).</summary>
    private static string StateTable(AltarKind altar) => altar switch
    {
        AltarKind.Normal => "lottery",
        AltarKind.InGame => "in_game_lottery",
        AltarKind.Diamond => "new_lottery",
        _ => throw new ArgumentOutOfRangeException(nameof(altar)),
    };

    /// <summary>Table edenfr_account de récompenses par autel —
    /// `ingame_lottery` a un underscore contrairement à son état.</summary>
    private static string RewardTable(AltarKind altar) => altar switch
    {
        AltarKind.Normal => "lottery",
        AltarKind.InGame => "ingame_lottery",
        AltarKind.Diamond => "new_lottery",
        _ => throw new ArgumentOutOfRangeException(nameof(altar)),
    };

    /// <summary>`new_lottery` n'a pas de colonne crystal_c (schéma
    /// natif) — le SELECT/INSERT/UPDATE l'exclut pour le diamant.</summary>
    private static bool HasCrystalC(AltarKind altar)
        => altar != AltarKind.Diamond;

    /// <inheritdoc/>
    public async Task<List<LotteryReward>> LoadRewardsAsync(
        AltarKind altar)
    {
        await using var conn = _accountDb.CreateConnection();
        var rows = await conn.QueryAsync<LotteryRewardRow>(
            $@"SELECT round AS Round, week AS Week, item_index AS ItemIndex,
                      item_id AS ItemId, item_amount AS ItemAmount,
                      probability AS Probability, bulletin AS Bulletin,
                      reset_bonus AS ResetBonus, cant_gonext AS CantGoNext
                 FROM {RewardTable(altar)}
                ORDER BY week, round, item_index");
        return rows.Select(r => new LotteryReward
        {
            Round = r.Round, Week = r.Week, ItemIndex = r.ItemIndex,
            ItemId = r.ItemId, ItemAmount = r.ItemAmount,
            Probability = r.Probability,
            Bulletin = r.Bulletin != 0,
            ResetBonus = r.ResetBonus != 0,
            CantGoNext = r.CantGoNext != 0,
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<PlayerLotteryState?> GetStateAsync(int playerId,
        AltarKind altar)
    {
        var crysC = HasCrystalC(altar) ? ", crystal_c" : ", 0";
        await using var conn = _gameDb.CreateConnection();
        var r = await conn.QueryFirstOrDefaultAsync<StateRow>(
            $@"SELECT lottery_type AS LotteryType, item_id AS ItemId,
                      durability AS ItemAmount, counts AS Counts,
                      lottery_plus AS Plus, lottery_update AS PlusUpdate,
                      event_flag AS EventFlag, crystal_b AS CrystalB{crysC} AS CrystalC,
                      extra_item_id AS ExtraItemId,
                      extra_counts AS ExtraCounts,
                      extra_multiple AS ExtraMultiple,
                      reset_bonus AS ResetBonus, can_go_next AS CanGoNext
                 FROM {StateTable(altar)} WHERE player_id = @playerId",
            new { playerId });
        if (r == null) return null;
        return new PlayerLotteryState
        {
            LotteryType = (short)r.LotteryType,
            ItemId = (short)r.ItemId,
            ItemAmount = (short)r.ItemAmount,
            Counts = (short)r.Counts,
            Plus = r.Plus, PlusUpdate = r.PlusUpdate,
            EventFlag = r.EventFlag,
            CrystalB = (short)r.CrystalB, CrystalC = (short)r.CrystalC,
            ExtraItemId = (short)r.ExtraItemId,
            ExtraCounts = (short)r.ExtraCounts,
            ExtraMultiple = (short)r.ExtraMultiple,
            ResetBonus = r.ResetBonus != 0,
            CanGoNext = r.CanGoNext != 0,
            InputSlot = -1, InputBag = -1,
            Persisted = true,
        };
    }

    /// <inheritdoc/>
    public async Task InsertStateAsync(int playerId, AltarKind altar,
        PlayerLotteryState s)
    {
        var cols = HasCrystalC(altar)
            ? ", crystal_b, crystal_c" : ", crystal_b";
        var vals = HasCrystalC(altar)
            ? ", @crysB, @crysC" : ", @crysB";
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"INSERT INTO {StateTable(altar)}
                 (player_id, lottery_type, item_id, durability, counts,
                  lottery_plus, lottery_update, event_flag{cols},
                  extra_item_id, extra_counts, extra_multiple, extra_reset,
                  reset_bonus, can_go_next)
               VALUES
                 (@playerId, @ltype, @itemId, @amount, @counts,
                  @plus, @plusUpd, @eventFlag{vals},
                  @exItem, @exCounts, @exMult, 0,
                  @resetBonus, @canGoNext)",
            StateParams(playerId, s));
        s.Persisted = true;
    }

    /// <inheritdoc/>
    public async Task UpdateStateAsync(int playerId, AltarKind altar,
        PlayerLotteryState s)
    {
        var crysC = HasCrystalC(altar)
            ? ", crystal_c = @crysC" : "";
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"UPDATE {StateTable(altar)}
                  SET lottery_type = @ltype, item_id = @itemId,
                      durability = @amount, counts = @counts,
                      event_flag = @eventFlag, crystal_b = @crysB{crysC},
                      reset_bonus = @resetBonus, can_go_next = @canGoNext
                WHERE player_id = @playerId",
            StateParams(playerId, s));
    }

    /// <inheritdoc/>
    public async Task UpdateBonusAsync(int playerId, AltarKind altar,
        PlayerLotteryState s)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"UPDATE {StateTable(altar)}
                  SET extra_item_id = @exItem, extra_counts = @exCounts,
                      extra_multiple = @exMult
                WHERE player_id = @playerId",
            new
            {
                playerId, exItem = (int)s.ExtraItemId,
                exCounts = (int)s.ExtraCounts, exMult = (int)s.ExtraMultiple,
            });
    }

    /// <inheritdoc/>
    public async Task ResetBonusAsync(int playerId, AltarKind altar)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"UPDATE {StateTable(altar)}
                  SET extra_item_id = 0, extra_counts = 0,
                      extra_multiple = 0
                WHERE player_id = @playerId", new { playerId });
    }

    /// <inheritdoc/>
    public async Task UpdatePlusAsync(int playerId, AltarKind altar,
        PlayerLotteryState s)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"UPDATE {StateTable(altar)} SET lottery_plus = @plus
                WHERE player_id = @playerId",
            new { playerId, plus = s.Plus });
    }

    /// <inheritdoc/>
    public async Task UpdatePlusTimestampAsync(int playerId,
        AltarKind altar, PlayerLotteryState s)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"UPDATE {StateTable(altar)} SET lottery_update = @plusUpd
                WHERE player_id = @playerId",
            new { playerId, plusUpd = s.PlusUpdate });
    }

    /// <inheritdoc/>
    public async Task DeleteStateAsync(int playerId, AltarKind altar)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            $@"DELETE FROM {StateTable(altar)} WHERE player_id = @playerId",
            new { playerId });
    }

    /// <inheritdoc/>
    public async Task<LotteryWeekPoints?> GetWeekPointsAsync(
        int accountId)
    {
        await using var conn = _gameDb.CreateConnection();
        var r = await conn.QueryFirstOrDefaultAsync<WeekRow>(
            @"SELECT normal_point AS NormalPoint,
                     normal_stage AS NormalStage,
                     diamond_point AS DiamondPoint,
                     diamond_stage AS DiamondStage
                FROM player_lottery_point WHERE account_id = @accountId",
            new { accountId });
        if (r == null) return null;
        return new LotteryWeekPoints
        {
            NormalPoint = r.NormalPoint, NormalStage = r.NormalStage,
            DiamondPoint = r.DiamondPoint, DiamondStage = r.DiamondStage,
            Persisted = true,
        };
    }

    /// <inheritdoc/>
    public async Task InsertWeekPointsAsync(int accountId,
        LotteryWeekPoints p)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO player_lottery_point
                (account_id, normal_point, normal_stage,
                 diamond_point, diamond_stage)
              VALUES (@accountId, @nP, @nS, @dP, @dS)",
            new
            {
                accountId, nP = p.NormalPoint, nS = p.NormalStage,
                dP = p.DiamondPoint, dS = p.DiamondStage,
            });
        p.Persisted = true;
    }

    /// <inheritdoc/>
    public async Task UpdateWeekPointsAsync(int accountId,
        LotteryWeekPoints p)
    {
        await using var conn = _gameDb.CreateConnection();
        await conn.ExecuteAsync(
            @"UPDATE player_lottery_point
                 SET normal_point = @nP, normal_stage = @nS,
                     diamond_point = @dP, diamond_stage = @dS
               WHERE account_id = @accountId",
            new
            {
                accountId, nP = p.NormalPoint, nS = p.NormalStage,
                dP = p.DiamondPoint, dS = p.DiamondStage,
            });
    }

    /// <summary>Paramètres nommés partagés INSERT/UPDATE d'état —
    /// les colonnes NOT NULL reçoivent toujours une valeur.</summary>
    private static object StateParams(int playerId,
        PlayerLotteryState s) => new
    {
        playerId,
        ltype = (int)s.LotteryType, itemId = (int)s.ItemId,
        amount = (int)s.ItemAmount, counts = (int)s.Counts,
        plus = s.Plus, plusUpd = s.PlusUpdate,
        eventFlag = s.EventFlag,
        crysB = (int)s.CrystalB, crysC = (int)s.CrystalC,
        exItem = (int)s.ExtraItemId, exCounts = (int)s.ExtraCounts,
        exMult = (int)s.ExtraMultiple,
        resetBonus = s.ResetBonus ? 1 : 0,
        canGoNext = s.CanGoNext ? 1 : 0,
    };

    /// <summary>Projection Dapper d'une ligne de récompenses —
    /// les colonnes NOT NULL empêchent les nulls.</summary>
    private sealed class LotteryRewardRow
    {
        public int Round { get; set; }
        public int Week { get; set; }
        public int ItemIndex { get; set; }
        public int ItemId { get; set; }
        public int ItemAmount { get; set; }
        public float Probability { get; set; }
        public int Bulletin { get; set; }
        public int ResetBonus { get; set; }
        public int CantGoNext { get; set; }
    }

    /// <summary>Projection Dapper de l'état joueur (snake_case →
    /// Pascal via alias AS explicites).</summary>
    private sealed class StateRow
    {
        public int LotteryType { get; set; }
        public int ItemId { get; set; }
        public int ItemAmount { get; set; }
        public int Counts { get; set; }
        public int Plus { get; set; }
        public int PlusUpdate { get; set; }
        public int EventFlag { get; set; }
        public int CrystalB { get; set; }
        public int CrystalC { get; set; }
        public int ExtraItemId { get; set; }
        public int ExtraCounts { get; set; }
        public int ExtraMultiple { get; set; }
        public int ResetBonus { get; set; }
        public int CanGoNext { get; set; }
    }

    /// <summary>Projection de `player_lottery_point`.</summary>
    private sealed class WeekRow
    {
        public int NormalPoint { get; set; }
        public int NormalStage { get; set; }
        public int DiamondPoint { get; set; }
        public int DiamondStage { get; set; }
    }
}
