using Dapper;
using Npgsql;
using Eden.Core.Game.Work;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Accès Dapper à <c>player_works</c> (table native — miroir de
/// <c>lapis::CWork</c>). Le natif INSERT 11 lignes work_num 0-10 à la
/// création du personnage puis ne fait que des UPDATE
/// (CWork::SaveWork / ResetWork). <see cref="EnsureRowsAsync"/>
/// reproduit l'initialisation native à la demande pour les
/// personnages déjà existants.
/// </summary>
public sealed class WorkRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Connexion edenfr_db standard (même base que inventory1 —
    /// le schéma natif regroupe tout dans edenfr_db).</summary>
    public WorkRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>
    /// SELECT natif au chargement (LoadPlayerWork) : les lignes
    /// player_works ordonnées par work_num. Les work_num absents
    /// (personnage sans init native) sont matérialisés en slots
    /// vides — <see cref="EnsureRowsAsync"/> crée les lignes manquantes.
    /// </summary>
    public async Task<WorkSlot[]> GetWorksAsync(int playerId)
    {
        await using var conn = _db.CreateConnection();
        // Dapper row dynamique : 100+ colonnes natives groupées
        // (itemN/amountN/socket_amountN/curseN/comboN/extra_comboN_E)
        // — l'accès par nom évite un POCO plat illisible.
        var rows = (await conn.QueryAsync(
            @"SELECT * FROM player_works
               WHERE player_id = @playerId
               ORDER BY work_num",
            new { playerId }))
            .Cast<IDictionary<string, object>>().ToList();
        var slots = new WorkSlot[WorkSlot.MaxSlots];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = new WorkSlot { WorkNum = i };
            var row = rows.FirstOrDefault(r => Int(r, "work_num") == i);
            if (row != null) CopyRow(row, slots[i]);
        }
        return slots;
    }

    /// <summary>Initialisation native : INSERT de 11 lignes work_num
    /// 0-10 tout à 0 (world server à la création du personnage).</summary>
    public async Task EnsureRowsAsync(int playerId)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO player_works (player_id, work_num)
              SELECT @playerId, n FROM generate_series(0, 10) AS n
              ON CONFLICT (player_id, work_num) DO NOTHING",
            new { playerId });
    }

    /// <summary>
    /// UPDATE complet du slot — mêmes colonnes que le SaveWork natif
    /// (tout sauf player_id/work_num), + finish_at (extension C#).
    /// </summary>
    public async Task SaveAsync(int playerId, WorkSlot work)
    {
        var p = BuildParams(playerId, work);
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(UpdateSql, p);
    }

    /// <summary>ResetWork natif : UPDATE avec tout à 0.</summary>
    public Task ResetAsync(int playerId, WorkSlot work)
    {
        work.Reset();
        return SaveAsync(playerId, work);
    }

    /// <summary>INSERT la ligne work_num si absente puis UPDATE —
    /// utilisé quand EnsureRowsAsync n'a pas encore tourné pour ce
    /// personnage (sécurité sur l'UPDATE silencieusement ignorée).</summary>
    public async Task EnsureAndSaveAsync(int playerId, WorkSlot work)
    {
        await using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            @"INSERT INTO player_works (player_id, work_num)
              VALUES (@playerId, @workNum)
              ON CONFLICT (player_id, work_num) DO NOTHING",
            new { playerId, workNum = work.WorkNum });
        await conn.ExecuteAsync(UpdateSql, BuildParams(playerId, work));
    }

    /// <summary>UPDATE SaveWork natif — toutes les colonnes hors PK.</summary>
    private const string UpdateSql =
        @"UPDATE player_works SET
              work_type = @WorkType, work_id = @WorkId,
              work_event = @WorkEvent,
              failure_rate_of_decline = @Decline,
              bouns_item_amount = @Bonus,
              building_item_id = @BuildingItemId,
              fm_name = @FmName, fm_id = @FmId,
              ext_success_rate = @ExtSuccess,
              ext_building_rate = @ExtBuilding,
              finish_at = @FinishAt,
              item1 = @Item1, amount1 = @Amount1,
              socket_amount1 = @Socket1, combo1 = @Combo1, curse1 = @Curse1,
              extra_combo1_1 = @EC1_1, extra_combo1_2 = @EC1_2,
              extra_combo1_3 = @EC1_3, extra_combo1_4 = @EC1_4,
              extra_combo1_5 = @EC1_5, extra_combo1_6 = @EC1_6,
              extra_combo1_7 = @EC1_7, extra_combo1_8 = @EC1_8,
              extra_combo1_9 = @EC1_9,
              item2 = @Item2, amount2 = @Amount2,
              socket_amount2 = @Socket2, combo2 = @Combo2, curse2 = @Curse2,
              extra_combo2_1 = @EC2_1, extra_combo2_2 = @EC2_2,
              extra_combo2_3 = @EC2_3, extra_combo2_4 = @EC2_4,
              extra_combo2_5 = @EC2_5, extra_combo2_6 = @EC2_6,
              extra_combo2_7 = @EC2_7, extra_combo2_8 = @EC2_8,
              extra_combo2_9 = @EC2_9,
              item3 = @Item3, amount3 = @Amount3,
              socket_amount3 = @Socket3, combo3 = @Combo3, curse3 = @Curse3,
              extra_combo3_1 = @EC3_1, extra_combo3_2 = @EC3_2,
              extra_combo3_3 = @EC3_3, extra_combo3_4 = @EC3_4,
              extra_combo3_5 = @EC3_5, extra_combo3_6 = @EC3_6,
              extra_combo3_7 = @EC3_7, extra_combo3_8 = @EC3_8,
              extra_combo3_9 = @EC3_9,
              item4 = @Item4, amount4 = @Amount4,
              socket_amount4 = @Socket4, combo4 = @Combo4, curse4 = @Curse4,
              extra_combo4_1 = @EC4_1, extra_combo4_2 = @EC4_2,
              extra_combo4_3 = @EC4_3, extra_combo4_4 = @EC4_4,
              extra_combo4_5 = @EC4_5, extra_combo4_6 = @EC4_6,
              extra_combo4_7 = @EC4_7, extra_combo4_8 = @EC4_8,
              extra_combo4_9 = @EC4_9
            WHERE player_id = @PlayerId AND work_num = @WorkNum";

    /// <summary>Construit les paramètres de l'UPDATE — les 4
    /// produits SWorkItem éclatés en colonnes itemN/amountN/…</summary>
    private static DynamicParameters BuildParams(int playerId, WorkSlot work)
    {
        var p = new DynamicParameters();
        p.Add("PlayerId", playerId);
        p.Add("WorkNum", work.WorkNum);
        p.Add("WorkType", work.WorkType);
        p.Add("WorkId", work.WorkId);
        p.Add("WorkEvent", work.WorkEvent);
        p.Add("Decline", work.FailureRateOfDecline);
        p.Add("Bonus", work.BonusItemAmount);
        p.Add("BuildingItemId", work.BuildingItemId);
        p.Add("FmName", work.FmName ?? string.Empty);
        p.Add("FmId", work.FmId);
        p.Add("ExtSuccess", (short)work.ExtSuccessRate);
        p.Add("ExtBuilding", (short)work.ExtBuildingRate);
        p.Add("FinishAt", work.FinishAtMs);
        for (var i = 0; i < 4; i++)
        {
            var prod = work.Products[i];
            var n = i + 1;
            p.Add($"Item{n}", prod.ItemId);
            p.Add($"Amount{n}", prod.Amount);
            p.Add($"Socket{n}", prod.SocketAmount);
            p.Add($"Combo{n}", prod.Combo);
            p.Add($"Curse{n}", prod.Curse ? 1 : 0);
            for (var e = 0; e < WorkProduct.ExtraComboCount; e++)
                p.Add($"EC{n}_{e + 1}", prod.ExtraCombos[e]);
        }
        return p;
    }

    /// <summary>Copie une ligne DB vers le modèle — work_event est
    /// l'enchant armé natif ; la deadline vient de finish_at
    /// (extension C# qui remplace l'enchant volatil).</summary>
    private static void CopyRow(IDictionary<string, object> r, WorkSlot w)
    {
        w.WorkNum = Int(r, "work_num");
        w.WorkType = Int(r, "work_type");
        w.WorkId = Int(r, "work_id");
        w.WorkEvent = Int(r, "work_event");
        w.FailureRateOfDecline = Int(r, "failure_rate_of_decline");
        w.BonusItemAmount = Int(r, "bouns_item_amount"); // typo native (sic)
        w.BuildingItemId = Int(r, "building_item_id");
        w.FmName = r.TryGetValue("fm_name", out var fm) && fm != null
            ? fm.ToString() ?? string.Empty : string.Empty;
        w.FmId = Int(r, "fm_id");
        w.ExtSuccessRate = Int(r, "ext_success_rate");
        w.ExtBuildingRate = Int(r, "ext_building_rate");
        w.FinishAtMs = Long(r, "finish_at");
        // En cours = deadline armée non nulle (finish_at remplace
        // l'enchant volatil natif — work_event est l'id de cmd
        // d'événement, sans rapport avec l'état courant).
        w.Running = w.FinishAtMs > 0;
        for (var i = 0; i < 4; i++)
        {
            var n = i + 1;
            var prod = w.Products[i];
            prod.ItemId = Int(r, $"item{n}");
            prod.Amount = Int(r, $"amount{n}");
            prod.SocketAmount = Int(r, $"socket_amount{n}");
            prod.Combo = Int(r, $"combo{n}");
            prod.Curse = Int(r, $"curse{n}") != 0;
            for (var e = 0; e < WorkProduct.ExtraComboCount; e++)
                prod.ExtraCombos[e] = Int(r, $"extra_combo{n}_{e + 1}");
        }
    }

    /// <summary>Lecture int défensive d'une colonne dynamique.</summary>
    private static int Int(IDictionary<string, object> r, string name)
        => r.TryGetValue(name, out var v) && v != null
            ? Convert.ToInt32(v) : 0;

    /// <summary>Lecture long défensive (finish_at BIGINT).</summary>
    private static long Long(IDictionary<string, object> r, string name)
        => r.TryGetValue(name, out var v) && v != null
            ? Convert.ToInt64(v) : 0;
}
