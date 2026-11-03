using Dapper;
using Npgsql;
using Eden.Core.Game.Mall;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Implémentation PostgreSQL edenfr_account du dépôt item mall natif.
/// Requêtes reprenant mot pour mot les SQL de
/// <c>lapis::CAccountDBO</c> (PGAccountDBO.cc — désasm world
/// re/ghidra/world/decompiled.c) : GetAllItemMallItem @0x808c650,
/// ItemMallReceivableQuery @0x808b0d0, ItemMallReceieveAttempt
/// @0x80…, ItemMallReceieveSuccess @0x808ecd0,
/// ItemMallReceieveFailure @0x8088470, ItemMallReqBuyItem @0x80…,
/// ItemMallCheckLimitAmount @0x8089110, ItemMallAddLimitAmount
/// @0x8088180, ItemMallAnythingReceivable @0x80877d0.
/// </summary>
public sealed class ItemMallRepository : IItemMallRepository
{
    private readonly NpgsqlDataSource _db;

    public ItemMallRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMallEntry>> GetCatalogAsync(
        CancellationToken token = default)
    {
        // SQL natif de GetAllItemMallItem — tri natif (index, group) ;
        // la map interne C++ regroupe ensuite par (group, index).
        const string sql = @"
            SELECT item_id, item_group, item_index, item_num, item_color,
                   money_unit, point, special_price, num_limit,
                   sell_date, not_sell_date, fortune_bag, note, level_limit
            FROM itemmall WHERE sell = 1
            ORDER BY item_index, item_group;";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, cancellationToken: token));
        return rows.Select(r => new ItemMallEntry
        {
            ItemId = r.ItemId,
            Group = r.ItemGroup,
            Index = r.ItemIndex,
            ItemNum = r.ItemNum,
            ItemColor = r.ItemColor,
            MoneyUnit = r.MoneyUnit,
            Point = (uint)r.Point,
            SpecialPrice = (uint)r.SpecialPrice,
            // num_limit n'existe sur le wire que pour le groupe 8 natif
            // (struct +0x10 posé seulement `if (item_group == 8)`).
            NumLimit = r.ItemGroup == 8 ? r.NumLimit : 0,
            SellDate = r.SellDate,
            NotSellDate = r.NotSellDate,
            FortuneBag = r.FortuneBag ?? "",
            Note = r.Note ?? "",
            LevelLimit = r.LevelLimit,
        }).ToList();
    }

    private sealed class Row
    {
        public int ItemId { get; set; }
        public int ItemGroup { get; set; }
        public int ItemIndex { get; set; }
        public int ItemNum { get; set; }
        public long ItemColor { get; set; }
        public int MoneyUnit { get; set; }
        public int Point { get; set; }
        public int SpecialPrice { get; set; }
        public int NumLimit { get; set; }
        public int SellDate { get; set; }
        public int NotSellDate { get; set; }
        public string? FortuneBag { get; set; }
        public string? Note { get; set; }
        public int LevelLimit { get; set; }
    }

    /// <inheritdoc />
    public async Task<bool> AnythingReceivableAsync(string accountName,
        CancellationToken token = default)
    {
        // ItemMallAnythingReceivable : COUNT(*) des réclamables > 0.
        const string sql = @"
            SELECT COUNT(*) FROM item_receivable
            WHERE account_name = @account AND state = 1;";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        var n = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new { account = accountName },
                cancellationToken: token));
        return n > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemReceivableRow>> GetReceivablesAsync(
        string accountName, int type, string? from, string? to,
        CancellationToken token = default)
    {
        // Trois variantes natives (désasm @0x808b0d0) : type 21 → LIMIT 21
        // sans filtre ; type 0 → purchase_time > from LIMIT 21 ; sinon →
        // purchase_time NOT BETWEEN from AND to LIMIT type.
        var sql = type switch
        {
            0x15 => @"SELECT id, item_id, item_quantity, purchase_time,
                             item_color, point, amount, world_id, player_name, mail_name
                      FROM item_receivable
                      WHERE account_name = @account AND state = 1
                      ORDER BY purchase_time DESC LIMIT 21;",
            0 => @"SELECT id, item_id, item_quantity, purchase_time,
                          item_color, point, amount, world_id, player_name, mail_name
                   FROM item_receivable
                   WHERE account_name = @account AND state = 1
                         AND purchase_time > @from::timestamp
                   ORDER BY purchase_time DESC LIMIT 21;",
            _ => @"SELECT id, item_id, item_quantity, purchase_time,
                          item_color, point, amount, world_id, player_name, mail_name
                   FROM item_receivable
                   WHERE account_name = @account AND state = 1
                         AND purchase_time NOT BETWEEN @from::timestamp AND @to::timestamp
                   ORDER BY purchase_time DESC LIMIT @lim;",
        };
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        var rows = await conn.QueryAsync<ReceivableDto>(new CommandDefinition(
            sql, new { account = accountName, from = from ?? "", to = to ?? "", lim = type },
            cancellationToken: token));
        return rows.Select(Map).ToList();
    }

    private sealed class ReceivableDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int ItemQuantity { get; set; }
        public DateTime PurchaseTime { get; set; }
        public long ItemColor { get; set; }
        public int Point { get; set; }
        public int Amount { get; set; }
        public int WorldId { get; set; }
        public string? PlayerName { get; set; }
        public string? MailName { get; set; }
    }

    private static ItemReceivableRow Map(ReceivableDto r) => new()
    {
        Id = r.Id,
        ItemId = r.ItemId,
        ItemQuantity = r.ItemQuantity,
        PurchaseTime = r.PurchaseTime,
        ItemColor = r.ItemColor,
        Point = r.Point,
        Amount = r.Amount,
        WorldId = r.WorldId,
        PlayerName = r.PlayerName ?? "",
        MailName = r.MailName ?? "",
    };

    /// <inheritdoc />
    public async Task<ItemReceivableRow?> ClaimReceivableAsync(
        int id, string accountName, CancellationToken token = default)
    {
        // ItemMallReceieveAttempt : SELECT … WHERE id AND account AND
        // state = 1 (FOR UPDATE) puis UPDATE state = 2 — atomique dans
        // une transaction pour éviter les doubles réclamations.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        await using var tx = await conn.BeginTransactionAsync(token);
        var row = await conn.QueryFirstOrDefaultAsync<ReceivableDto>(
            new CommandDefinition(@"
                SELECT id, item_id, item_quantity, purchase_time,
                       item_color, point, amount, world_id, player_name, mail_name
                FROM item_receivable
                WHERE id = @id AND account_name = @account AND state = 1
                FOR UPDATE;",
                new { id, account = accountName },
                transaction: tx, cancellationToken: token));
        if (row == null)
        {
            await tx.RollbackAsync(token);
            return null;
        }
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE item_receivable SET state = 2 WHERE id = @id;",
            new { id }, transaction: tx, cancellationToken: token));
        await tx.CommitAsync(token);
        return Map(row);
    }

    /// <inheritdoc />
    public async Task CompleteReceivableAsync(int id, string accountName,
        int uniqueId, int createTime, CancellationToken token = default)
    {
        // ItemMallReceieveSuccess : SELECT (state=2) → INSERT
        // item_receipt(même id + unique_id/create_time) → DELETE.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        await using var tx = await conn.BeginTransactionAsync(token);
        var row = await conn.QueryFirstOrDefaultAsync<ReceivableDto>(
            new CommandDefinition(@"
                SELECT id, item_id, item_quantity, purchase_time,
                       item_color, point, amount, world_id, player_name, mail_name
                FROM item_receivable
                WHERE id = @id AND account_name = @account AND state = 2
                FOR UPDATE;",
                new { id, account = accountName },
                transaction: tx, cancellationToken: token));
        if (row == null)
        {
            await tx.RollbackAsync(token);
            return;
        }
        // SQL natif : INSERT INTO item_receipt (id, purchase_time,
        // account_name, item_id, item_quantity, item_color, point,
        // amount, world_id, player_name, unique_id, create_time).
        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO item_receipt
                (id, purchase_time, account_name, item_id, item_quantity,
                 item_color, point, amount, world_id, player_name,
                 unique_id, create_time)
            VALUES (@id, @ptime, @account, @itemId, @qty, @color,
                    @point, @amount, @world, @player, @unique, @ctime);",
            new
            {
                id, ptime = row.PurchaseTime, account = accountName,
                itemId = row.ItemId, qty = row.ItemQuantity,
                color = row.ItemColor, point = row.Point,
                amount = row.Amount, world = row.WorldId,
                player = row.PlayerName ?? "",
                unique = uniqueId, ctime = createTime,
            }, transaction: tx, cancellationToken: token));
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_receivable WHERE id = @id;",
            new { id }, transaction: tx, cancellationToken: token));
        await tx.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task RequeueReceivableAsync(int id,
        CancellationToken token = default)
    {
        // ItemMallReceieveFailure : remise en file state = 1.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE item_receivable SET state = 1 WHERE id = @id;",
            new { id }, cancellationToken: token));
    }

    /// <inheritdoc />
    public async Task<int> RequeueClaimedReceivablesAsync(
        CancellationToken token = default)
    {
        // Claims orphelins : la ligne est restée à state=2 parce que le
        // verdict ZW 12 n'est jamais revenu (redémarrage world/zone,
        // coupure du lien entre-temps). Sans ce reset elles restent
        // invisibles à la liste (filtre state=1) et impossibles à
        // réclamer — fuite permanente de l'achat.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        return await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE item_receivable SET state = 1 WHERE state = 2;",
            cancellationToken: token));
    }

    /// <inheritdoc />
    public async Task<int> InsertReceivableAsync(string accountName,
        int itemId, int itemQuantity, long itemColor, int point,
        int amount, int worldId, string playerName, string mailName,
        CancellationToken token = default)
    {
        // ItemMallReqBuyItem : INSERT (state, account_name, item_id,
        // item_quantity, item_color, point, amount, world_id,
        // player_name, mail_name). Le natif insère state = -1 en
        // attente du billing puis bascule à 1 ; notre déduction est
        // synchrone → state = 1 directement.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(@"
            INSERT INTO item_receivable
                (state, account_name, item_id, item_quantity, item_color,
                 point, amount, world_id, player_name, mail_name)
            VALUES (1, @account, @itemId, @qty, @color, @point, @amount,
                    @world, @player, @mail)
            RETURNING id;",
            new
            {
                account = accountName, itemId, qty = itemQuantity,
                color = itemColor, point, amount, world = worldId,
                player = playerName, mail = mailName,
            }, cancellationToken: token));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMallLimitRow>> GetLimitAmountsAsync(
        int accountId, CancellationToken token = default)
    {
        // UpdatePlayerItemMallLimit : toutes les lignes du compte.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        var rows = await conn.QueryAsync<ItemMallLimitRow>(new CommandDefinition(@"
            SELECT item_group AS ItemGroup, item_index AS ItemIndex,
                   item_id AS ItemId, item_num AS ItemNum,
                   money_unit AS MoneyUnit,
                   sell_amount AS SellAmount
            FROM itemmall_limit_amount WHERE account_id = @acc;",
            new { acc = accountId }, cancellationToken: token));
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<int> CheckLimitAmountAsync(int accountId, int group,
        int index, int moneyUnit, int itemId, int itemNum,
        CancellationToken token = default)
    {
        // ItemMallCheckLimitAmount : SELECT sell_amount par clé ;
        // INSERT d'une ligne à 0 si absente puis relecture ; -1 erreur.
        const string select = @"
            SELECT sell_amount FROM itemmall_limit_amount
            WHERE account_id = @acc AND item_group = @grp
                  AND item_index = @idx AND money_unit = @mu
                  AND item_id = @item AND item_num = @num;";
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        var args = new
        {
            acc = accountId, grp = group, idx = index, mu = moneyUnit,
            item = itemId, num = itemNum,
        };
        var amount = await conn.QueryFirstOrDefaultAsync<int?>(
            new CommandDefinition(select, args, cancellationToken: token));
        if (amount == null)
        {
            await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO itemmall_limit_amount
                    (account_id, sell_amount, item_id, item_group,
                     item_index, item_num, money_unit)
                VALUES (@acc, 0, @item, @grp, @idx, @num, @mu);",
                args, cancellationToken: token));
            amount = await conn.QueryFirstOrDefaultAsync<int?>(
                new CommandDefinition(select, args, cancellationToken: token));
        }
        return amount ?? -1;
    }

    /// <inheritdoc />
    public async Task AddLimitAmountAsync(int accountId, int group,
        int index, int moneyUnit, int itemId, int itemNum, int amount,
        CancellationToken token = default)
    {
        // ItemMallAddLimitAmount : UPDATE sell_amount = sell_amount + N.
        await using var conn = _db.CreateConnection();
        await conn.OpenAsync(token);
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE itemmall_limit_amount
            SET sell_amount = sell_amount + @add
            WHERE account_id = @acc AND item_group = @grp
                  AND item_index = @idx AND money_unit = @mu
                  AND item_id = @item AND item_num = @num;",
            new
            {
                add = amount, acc = accountId, grp = group, idx = index,
                mu = moneyUnit, item = itemId, num = itemNum,
            }, cancellationToken: token));
    }
}
