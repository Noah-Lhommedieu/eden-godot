using Dapper;
using Npgsql;
using Eden.Core.Game.Inventory;

namespace Eden.Core.Database.Repositories;

/// <summary>Stockage partagé de famille/guilde — table native
/// `family_storage` : lignes items complètes (mêmes colonnes que
/// `inventory` moins rank/magic, `family_id` à la place de
/// `player_id`). `container_index` = index du coffre (0-2 natif :
  /// coffre 0 → family lv≥3, 1 → lv≥5, 2 → lv≥6 — CheckPrivilige
/// mission @0x081b08xx, priv ids 11/12/13 = storage01/02/03) ;
/// `loc` = slot 0-39 (cap 0x28 par coffre — désasm zone
/// `CNE_CZ_ZoneServerFamilyStoragePutItem::Execute` @0x08258a00).
/// </summary>
public interface IFamilyBankRepository
{
    /// <summary>Items d'un coffre (`container_index`), triés par
    /// `loc` — le <see cref="ItemEntity.CharacterId"/> porte le
    /// family_id.</summary>
    Task<IReadOnlyList<ItemEntity>> GetItemsAsync(int familyId,
        int storageIndex);
    /// <summary>Un item d'un coffre à un slot donné.</summary>
    Task<ItemEntity?> GetItemAsync(int familyId, int storageIndex,
        int loc);
    /// <summary>Nombre d'items d'un coffre (gate natif &lt; 0x28).</summary>
    Task<int> GetItemCountAsync(int familyId, int storageIndex);
    /// <summary>Premier slot libre 0..39 du coffre (résolution du
    /// `bank_loc = -1` natif — boucle de libre-emplacement de
    /// l'Execute zone).</summary>
    Task<int?> GetFreeLocAsync(int familyId, int storageIndex);
    /// <summary>Insère la ligne complète dans `family_storage`
    /// (`item.CharacterId` = family_id, `BagId` = coffre,
    /// `Slot` = loc) — l'id de ligne est réutilisé tel quel (espace
    /// sérial global inter-tables, comme l'INSERT...SELECT natif).</summary>
    Task InsertItemAsync(ItemEntity item);
    /// <summary>Supprime la ligne par id sérial.</summary>
    Task<bool> RemoveItemAsync(int rowId);
    /// <summary>Append `family_storage_log` — SQL natif
    /// `CFamilyDBO::InsertFamilyStorageLog` (mission @0x817da00) :
    /// log_type 0 "Storage Insert", 1 "Delete", 2 "Recycle".</summary>
    Task AppendStorageLogAsync(int familyId, short logType, string log);
    /// <summary>50 dernières entrées du journal, plus récentes
    /// d'abord.</summary>
    Task<IReadOnlyList<FamilyStorageLogRow>> GetStorageLogAsync(
        int familyId);
}

/// <summary>Ligne `family_storage_log` native — pairs
/// {log_type s16, log string} renvoyées par ClientFamilyStorageLog
/// (506) ; log_type : 0 "Storage Insert", 1 "Delete", 2 "Recycle"
/// (EFamilyStorageState du CLogFactory mission natif).</summary>
public sealed class FamilyStorageLogRow
{
    public short LogType { get; set; }
    public string Log { get; set; } = "";
    public int LogTime { get; set; }
}

/// <summary>Accès DB à `family_storage`/`family_storage_log`.</summary>
public sealed class FamilyBankRepository : IFamilyBankRepository
{
    /// <summary>Capacité native d'un coffre de famille — 0x28 dans les
    /// gates zone (`*(iVar9 + 0x28) &lt; 0x28`) et mission
    /// (`TC_FamilyStorageInsertItemRequest` @0x081f43c0).</summary>
    public const int StorageSlotCapacity = 40;

    /// <summary>Nombre de coffres natifs (container_index 0..2 —
    /// storage01/02/03_privilege).</summary>
    public const int StorageCount = 3;

    private readonly NpgsqlDataSource _db;

    public FamilyBankRepository(NpgsqlDataSource db)
        => _db = db;

    /// <summary>Colonnes lues → <see cref="ItemEntity"/>
    /// (family_id→CharacterId, container_index→BagId, loc→Slot).</summary>
    private const string ItemSelect = @"
        SELECT id, family_id AS character_id,
               container_index AS bag_id, loc AS slot, item_id, maker,
               durability, cur_maxdurability AS max_durability,
               strengthen AS enchant_level,
               CASE WHEN bind != 0 THEN 1 ELSE 0 END AS is_bound,
               identify, create_time, due_date, quality,
               magicbox_enchant1, magicbox_enchant2, magicbox_enchant3,
               magicbox_energy, magicbox_starlevel,
               embedded_amount,
               embedded_id1, embedded_id2, embedded_id3,
               embedded_id4, embedded_id5, embedded_id6,
               combo_id AS combo, witchcraft,
               extra_combo_id1, extra_combo_id2, extra_combo_id3,
               extra_combo_id4, extra_combo_id5, extra_combo_id6,
               extra_combo_id7, extra_combo_id8, extra_combo_id9,
               extra_combo_id10, extra_combo_id11,
               gold_enchant, plus_parameter, color,
               train_level, train_exp,
               train_slot1_type, train_slot1_level,
               train_slot2_type, train_slot2_level,
               train_slot3_type, train_slot3_level
        FROM family_storage";

    /// <summary>Materialise la ligne Dapper en entité — colonnes de
    /// type array (`extra_combo_idN`) remises dans le tableau
    /// <see cref="ItemEntity.ExtraCombos"/>.</summary>
    private static ItemEntity ToEntity(FamilyStorageRow r) => new()
    {
        Id = r.Id, CharacterId = r.CharacterId, BagId = r.BagId,
        Slot = r.Slot, ItemId = r.ItemId, Maker = r.Maker,
        Durability = r.Durability,
        MaxDurability = r.MaxDurability, EnchantLevel = r.EnchantLevel,
        IsBound = r.IsBound != 0, Identify = r.Identify,
        CreateTime = r.CreateTime, DueDate = r.DueDate,
        Quality = r.Quality,
        MagicBoxEnchant1 = r.MagicboxEnchant1,
        MagicBoxEnchant2 = r.MagicboxEnchant2,
        MagicBoxEnchant3 = r.MagicboxEnchant3,
        MagicBoxEnergy = r.MagicboxEnergy,
        MagicBoxStarLevel = r.MagicboxStarlevel,
        EmbeddedAmount = r.EmbeddedAmount,
        EmbeddedId1 = r.EmbeddedId1, EmbeddedId2 = r.EmbeddedId2,
        EmbeddedId3 = r.EmbeddedId3, EmbeddedId4 = r.EmbeddedId4,
        EmbeddedId5 = r.EmbeddedId5, EmbeddedId6 = r.EmbeddedId6,
        Combo = r.Combo,
        ExtraCombos =
        {
            [0] = r.ExtraComboId1, [1] = r.ExtraComboId2,
            [2] = r.ExtraComboId3, [3] = r.ExtraComboId4,
            [4] = r.ExtraComboId5, [5] = r.ExtraComboId6,
            [6] = r.ExtraComboId7, [7] = r.ExtraComboId8,
            [8] = r.ExtraComboId9,
        },
        ExtraComboId10 = r.ExtraComboId10,
        ExtraComboId11 = r.ExtraComboId11,
        GoldEnchant = r.GoldEnchant, PlusParameter = r.PlusParameter,
        Color = r.Color, TrainLevel = r.TrainLevel,
        TrainExp = r.TrainExp,
        TrainSlot1Type = r.TrainSlot1Type,
        TrainSlot1Level = r.TrainSlot1Level,
        TrainSlot2Type = r.TrainSlot2Type,
        TrainSlot2Level = r.TrainSlot2Level,
        TrainSlot3Type = r.TrainSlot3Type,
        TrainSlot3Level = r.TrainSlot3Level,
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemEntity>> GetItemsAsync(
        int familyId, int storageIndex)
    {
        using var connection = _db.CreateConnection();
        var rows = await connection.QueryAsync<FamilyStorageRow>(
            ItemSelect + @"
            WHERE family_id = @FamilyId AND container_index = @Index
            ORDER BY loc",
            new { FamilyId = familyId, Index = storageIndex });
        return rows.Select(ToEntity).ToList();
    }

    /// <inheritdoc />
    public async Task<ItemEntity?> GetItemAsync(int familyId,
        int storageIndex, int loc)
    {
        using var connection = _db.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<
            FamilyStorageRow>(ItemSelect + @"
            WHERE family_id = @FamilyId AND container_index = @Index
              AND loc = @Loc LIMIT 1",
            new { FamilyId = familyId, Index = storageIndex, Loc = loc });
        return row == null ? null : ToEntity(row);
    }

    /// <inheritdoc />
    public async Task<int> GetItemCountAsync(int familyId,
        int storageIndex)
    {
        using var connection = _db.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM family_storage
            WHERE family_id = @FamilyId AND container_index = @Index",
            new { FamilyId = familyId, Index = storageIndex });
    }

    /// <inheritdoc />
    public async Task<int?> GetFreeLocAsync(int familyId,
        int storageIndex)
    {
        using var connection = _db.CreateConnection();
        const string sql = @"
            SELECT s.loc
            FROM generate_series(0, @Cap - 1) AS s(loc)
            WHERE NOT EXISTS (
                SELECT 1 FROM family_storage i
                WHERE i.family_id = @FamilyId
                AND i.container_index = @Index AND i.loc = s.loc)
            ORDER BY s.loc
            LIMIT 1";
        return await connection.QueryFirstOrDefaultAsync<int?>(sql,
            new { FamilyId = familyId, Index = storageIndex,
                  Cap = StorageSlotCapacity });
    }

    /// <inheritdoc />
    public async Task InsertItemAsync(ItemEntity item)
    {
        using var connection = _db.CreateConnection();
        const string sql = @"
            INSERT INTO family_storage
                (id, item_id, durability, maker, identify,
                 embedded_amount, embedded_id1, embedded_id2,
                 embedded_id3, embedded_id4, embedded_id5, embedded_id6,
                 loc, create_time, due_date, combo_id, strengthen,
                 cur_maxdurability, bind, family_id, container_index,
                 extra_combo_id1, extra_combo_id2, extra_combo_id3,
                 extra_combo_id4, extra_combo_id5, extra_combo_id6,
                 extra_combo_id7, extra_combo_id8, extra_combo_id9,
                 witchcraft, quality, plus_parameter, color,
                 train_level, train_exp,
                 train_slot1_type, train_slot1_level,
                 train_slot2_type, train_slot2_level,
                 train_slot3_type, train_slot3_level,
                 extra_combo_id10, extra_combo_id11, gold_enchant,
                 magicbox_enchant1, magicbox_enchant2, magicbox_enchant3,
                 magicbox_energy, magicbox_starlevel, unbind)
            VALUES
                (@Id, @ItemId, @Durability, @Maker, @Identify,
                 @EmbeddedAmount, @EmbeddedId1, @EmbeddedId2,
                 @EmbeddedId3, @EmbeddedId4, @EmbeddedId5, @EmbeddedId6,
                 @Slot, @CreateTime, @DueDateInt, @Combo, @EnchantLevel,
                 @MaxDurability, @BindInt, @CharacterId, @BagId,
                 @Ec1, @Ec2, @Ec3, @Ec4, @Ec5, @Ec6, @Ec7, @Ec8, @Ec9,
                 @Witchcraft, @Quality, @PlusParameter, @Color,
                 @TrainLevel, @TrainExp,
                 @TrainSlot1Type, @TrainSlot1Level,
                 @TrainSlot2Type, @TrainSlot2Level,
                 @TrainSlot3Type, @TrainSlot3Level,
                 @Ec10, @Ec11, @GoldEnchant,
                 @MagicBoxEnchant1, @MagicBoxEnchant2, @MagicBoxEnchant3,
                 @MagicBoxEnergy, @MagicBoxStarLevel, 0)";
        await connection.ExecuteAsync(sql, new
        {
            item.Id, item.ItemId, item.Durability, item.Maker,
            item.Identify,
            item.EmbeddedAmount,
            item.EmbeddedId1, item.EmbeddedId2, item.EmbeddedId3,
            item.EmbeddedId4, item.EmbeddedId5, item.EmbeddedId6,
            item.Slot, item.CreateTime, item.DueDate,
            item.Combo, item.EnchantLevel, item.MaxDurability,
            BindInt = item.IsBound ? 1 : 0,
            item.CharacterId, item.BagId,
            Ec1 = item.ExtraCombos[0], Ec2 = item.ExtraCombos[1],
            Ec3 = item.ExtraCombos[2], Ec4 = item.ExtraCombos[3],
            Ec5 = item.ExtraCombos[4], Ec6 = item.ExtraCombos[5],
            Ec7 = item.ExtraCombos[6], Ec8 = item.ExtraCombos[7],
            Ec9 = item.ExtraCombos[8],
            item.Witchcraft, item.Quality, item.PlusParameter,
            item.Color, item.TrainLevel, item.TrainExp,
            item.TrainSlot1Type, item.TrainSlot1Level,
            item.TrainSlot2Type, item.TrainSlot2Level,
            item.TrainSlot3Type, item.TrainSlot3Level,
            Ec10 = item.ExtraComboId10, Ec11 = item.ExtraComboId11,
            item.GoldEnchant,
            item.MagicBoxEnchant1, item.MagicBoxEnchant2,
            item.MagicBoxEnchant3,
            item.MagicBoxEnergy, item.MagicBoxStarLevel,
        });
    }

    /// <inheritdoc />
    public async Task<bool> RemoveItemAsync(int rowId)
    {
        using var connection = _db.CreateConnection();
        return await connection.ExecuteAsync(
            "DELETE FROM family_storage WHERE id = @Id",
            new { Id = rowId }) > 0;
    }

    /// <inheritdoc />
    public async Task AppendStorageLogAsync(int familyId, short logType,
        string log)
    {
        using var connection = _db.CreateConnection();
        const string sql = @"
            INSERT INTO family_storage_log (family_id, log_type, log, log_time)
            VALUES (@FamilyId, @LogType, @Log, @LogTime)";
        await connection.ExecuteAsync(sql, new
        {
            FamilyId = familyId, LogType = logType, Log = log,
            LogTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FamilyStorageLogRow>>
        GetStorageLogAsync(int familyId)
    {
        using var connection = _db.CreateConnection();
        const string sql = @"
            SELECT log_type AS LogType, log AS Log, log_time AS LogTime
            FROM family_storage_log WHERE family_id = @FamilyId
            ORDER BY log_time DESC LIMIT 50";
        var rows = await connection.QueryAsync<FamilyStorageLogRow>(sql,
            new { FamilyId = familyId });
        return rows.ToList();
    }

    /// <summary>Ligne brute de `family_storage` (types DB natifs).</summary>
    private sealed class FamilyStorageRow
    {
        public int Id { get; set; }
        public int CharacterId { get; set; }
        public int BagId { get; set; }
        public int Slot { get; set; }
        public int ItemId { get; set; }
        public string Maker { get; set; } = "";
        public int Durability { get; set; }
        public int MaxDurability { get; set; }
        public int EnchantLevel { get; set; }
        public int IsBound { get; set; }
        public int Identify { get; set; }
        public int CreateTime { get; set; }
        public int DueDate { get; set; }
        public int Quality { get; set; }
        public int MagicboxEnchant1 { get; set; }
        public int MagicboxEnchant2 { get; set; }
        public int MagicboxEnchant3 { get; set; }
        public int MagicboxEnergy { get; set; }
        public int MagicboxStarlevel { get; set; }
        public int EmbeddedAmount { get; set; }
        public int EmbeddedId1 { get; set; }
        public int EmbeddedId2 { get; set; }
        public int EmbeddedId3 { get; set; }
        public int EmbeddedId4 { get; set; }
        public int EmbeddedId5 { get; set; }
        public int EmbeddedId6 { get; set; }
        public int Combo { get; set; }
        public int Witchcraft { get; set; }
        public int ExtraComboId1 { get; set; }
        public int ExtraComboId2 { get; set; }
        public int ExtraComboId3 { get; set; }
        public int ExtraComboId4 { get; set; }
        public int ExtraComboId5 { get; set; }
        public int ExtraComboId6 { get; set; }
        public int ExtraComboId7 { get; set; }
        public int ExtraComboId8 { get; set; }
        public int ExtraComboId9 { get; set; }
        public int ExtraComboId10 { get; set; }
        public int ExtraComboId11 { get; set; }
        public int GoldEnchant { get; set; }
        public int PlusParameter { get; set; }
        public long Color { get; set; }
        public int TrainLevel { get; set; }
        public long TrainExp { get; set; }
        public int TrainSlot1Type { get; set; }
        public int TrainSlot1Level { get; set; }
        public int TrainSlot2Type { get; set; }
        public int TrainSlot2Level { get; set; }
        public int TrainSlot3Type { get; set; }
        public int TrainSlot3Level { get; set; }
    }
}
