using System.Text;
using Npgsql;
using Xunit;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Mall;

namespace Eden.Tests;

/// <summary>
/// Tests du flux item mall natif : formats wire (62 catalogue, 79
/// limites, ZW 24/12 — vérifiés par désassemblage des Serialize
/// natifs dans re/ghidra/world/decompiled.c), filtres du catalogue à
/// la <c>CAccountDBO::GetAllItemMallItem</c> (@0x808c650) et machine
/// d'état des receivables <c>item_receivable → item_receipt</c>.
/// </summary>
public class ItemMallTests
{
    // ── Helpers de lecture LE du wire ────────────────────────────

    private static ushort U16(byte[] b, int off)
        => (ushort)(b[off] | (b[off + 1] << 8));

    private static uint U32(byte[] b, int off)
        => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16)
                  | (b[off + 3] << 24));

    private static short S16(byte[] b, int off) => (short)U16(b, off);

    private static int I32(byte[] b, int off) => (int)U32(b, off);

    private static long I64(byte[] b, int off)
        => BitConverter.ToInt64(b, off);

    /// <summary>Lit une string wire (s16 len puis bytes).</summary>
    private static (string Value, int Next) Str(byte[] b, int off)
    {
        int len = S16(b, off);
        var s = Encoding.ASCII.GetString(b, off + 2, len);
        return (s, off + 2 + len);
    }

    /// <summary>Entrée catalogue brute (dates au format natif
    /// YYYYMMDDHH en int, avant conversion epoch par Load).</summary>
    private static ItemMallEntry Row(
        int group = 1, int index = 0, int itemId = 30000,
        int num = 1, int unit = 1, uint point = 100, uint special = 0,
        int sellDate = 0, int notSellDate = 0,
        int numLimit = 0, int levelLimit = 0, string note = "",
        string fortuneBag = "", long color = 0)
        => new()
        {
            ItemId = itemId, Group = group, Index = index,
            ItemNum = num, MoneyUnit = unit, Point = point,
            SpecialPrice = special, SellDate = sellDate,
            NotSellDate = notSellDate, NumLimit = numLimit,
            LevelLimit = levelLimit, Note = note,
            FortuneBag = fortuneBag, ItemColor = color,
        };

    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(1_800_000_000); // ~2027

    // ── Catalogue : filtres natifs GetAllItemMallItem ────────────

    [Fact]
    public void Load_FiltersPointZeroSpecialAndMoneyUnit()
    {
        var cat = ItemMallCatalog.Load(
        [
            Row(point: 0),                    // point=0 hors grp 99 → skip
            Row(group: 99, point: 0),         // groupe 99 → gardé
            Row(point: 100, special: 100),    // special>=point → skip
            Row(unit: 4),                     // money_unit ∉ {1,2,3} → skip
            Row(group: 99, unit: 4),          // groupe 99 exempté → gardé
            Row(group: 2, index: 1, unit: 2), // OK
        ], itemExists: _ => 1, now: Now);
        Assert.Equal(3, cat.Entries.Count);
        Assert.Equal(99, cat.Entries[0].Group);
        Assert.Equal(99, cat.Entries[1].Group);
        Assert.Equal(2, cat.Entries[2].Group);
    }

    [Fact]
    public void Load_ItemNum_CoerceAndMaxStack()
    {
        var cat = ItemMallCatalog.Load(
        [
            Row(itemId: 30001, num: -1),  // négatif → skip
            Row(itemId: 30002, num: 0),   // 0 → coerci à 1
            Row(itemId: 30003, num: 5),   // > maxstack 2 → skip
        ], itemExists: _ => 2, now: Now);
        Assert.Single(cat.Entries);
        Assert.Equal(30002, cat.Entries[0].ItemId);
        Assert.Equal(1, cat.Entries[0].ItemNum);
    }

    [Fact]
    public void Load_MissingItem_SkippedExceptGroup99()
    {
        var cat = ItemMallCatalog.Load(
        [
            Row(itemId: 40001),
            Row(itemId: 40002, group: 99),
        ], itemExists: _ => 0, now: Now);
        Assert.Single(cat.Entries);
        Assert.Equal(99, cat.Entries[0].Group);
    }

    [Fact]
    public void Load_NotSellDatePast_Excludes()
    {
        var cat = ItemMallCatalog.Load(
        [
            Row(notSellDate: 2000010100),  // retiré en 2000 → skip
            Row(notSellDate: 2030010100),  // futur → gardé (epoch>0)
            Row(notSellDate: 0),           // pas de fin → gardé
        ], itemExists: _ => 1, now: Now);
        Assert.Equal(2, cat.Entries.Count);
        Assert.True(cat.Entries[0].NotSellDate > 0);
        Assert.Equal(0, cat.Entries[1].NotSellDate);
    }

    [Fact]
    public void Load_SellDatePast_GivesZeroEpoch()
    {
        var cat = ItemMallCatalog.Load(
        [
            Row(sellDate: 2000010100),   // déjà en vente → 0
            Row(sellDate: 2030010100),   // vente future → epoch>0
        ], itemExists: _ => 1, now: Now);
        Assert.Equal(0, cat.Entries[0].SellDate);
        Assert.True(cat.Entries[1].SellDate > 0);
    }

    // ── Achat : coût / quantité livrée / validations ─────────────

    [Fact]
    public void TotalCost_UsesSpecialPriceWhenSet()
    {
        var item = new ItemMallEntry { Point = 100, SpecialPrice = 40 };
        Assert.Equal(400, ItemMallCatalog.TotalCost(item, 10));
        var plain = new ItemMallEntry { Point = 100, SpecialPrice = 0 };
        Assert.Equal(300, ItemMallCatalog.TotalCost(plain, 3));
    }

    [Fact]
    public void DeliveredQuantity_MultipliesByItemNum()
    {
        var item = new ItemMallEntry { ItemNum = 5 };
        Assert.Equal(15, ItemMallCatalog.DeliveredQuantity(item, 3));
        Assert.Equal(5, ItemMallCatalog.DeliveredQuantity(item, 1));
    }

    [Fact]
    public void ValidateBuyAmount_MultiRequiresSingleUnit()
    {
        var single = new ItemMallEntry { ItemNum = 1 };
        var pack = new ItemMallEntry { ItemNum = 5 };
        Assert.True(ItemMallCatalog.ValidateBuyAmount(single, 10));
        Assert.True(ItemMallCatalog.ValidateBuyAmount(pack, 1));
        Assert.False(ItemMallCatalog.ValidateBuyAmount(pack, 2));
    }

    [Fact]
    public void Find_MatchesGroupIndexMoneyUnit()
    {
        var cat = new ItemMallCatalog(
        [
            Row(group: 3, index: 7, unit: 2),
            Row(group: 99, index: 7, unit: 1),
        ]);
        Assert.NotNull(cat.Find(3, 7, 2));
        Assert.Null(cat.Find(3, 7, 1));    // money_unit différent
        Assert.NotNull(cat.Find(99, 7, 3)); // grp 99 accepte tout unit
        Assert.Null(cat.Find(3, 8, 2));    // mauvais index
    }

    [Fact]
    public void DateToEpoch_NativeLayout()
    {
        // 2000-01-01 00:00 local = 946684800 + décalage — le point
        // fixe est que l'epoch est cohérent avec timelocal natif.
        var e = ItemMallCatalog.DateToEpoch(2000010100);
        Assert.True(e > 0);
        Assert.Equal(0, ItemMallCatalog.DateToEpoch(0));
        Assert.Equal(0, ItemMallCatalog.DateToEpoch(99999999)); // invalide
    }

    // ── Wire : paquets monde ──────────────────────────────────────

    [Fact]
    public void BuildCatalog_VersionCountThenNativeRecordOrder()
    {
        var cat = new ItemMallCatalog(
        [
            new ItemMallEntry
            {
                Group = 3, Index = 2, ItemId = 30042,
                ItemNum = 5, MoneyUnit = 1, Point = 300,
                SpecialPrice = 200, NumLimit = 0, ItemColor = 0x1122,
                NotSellDate = 777, FortuneBag = "bag", Note = "note",
                LevelLimit = 40, SellDate = 0,
            },
        ]);
        var batch = ItemMallPackets.BuildCatalogBatches(cat).Single();
        int o = 0;
        Assert.Equal(1, U16(batch, o)); o += 2;      // ItemMallVersion
        Assert.Equal(1, U16(batch, o)); o += 2;      // count
        // clé wire native = money_unit*100 + item_group
        // (GetAllItemMallItem : sVar10 = local_1c * 100 + local_16[0])
        Assert.Equal(103, S16(batch, o)); o += 2;    // 1*100 + group 3
        Assert.Equal(2, S16(batch, o)); o += 2;      // item_index
        Assert.Equal(30042, U16(batch, o)); o += 2;  // item_id
        Assert.Equal(5, U16(batch, o)); o += 2;      // item_num
        Assert.Equal(300u, U32(batch, o)); o += 4;   // point
        Assert.Equal(200u, U32(batch, o)); o += 4;   // special_price
        Assert.Equal(0, U16(batch, o)); o += 2;      // num_limit
        Assert.Equal(0x1122, I64(batch, o)); o += 8; // item_color
        Assert.Equal(777, I32(batch, o)); o += 4;    // not_sell epoch
        var (bag, n1) = Str(batch, o); o = n1;       // fortune_bag
        var (note, n2) = Str(batch, o); o = n2;      // note
        Assert.Equal("bag", bag);
        Assert.Equal("note", note);
        Assert.Equal(40, U16(batch, o)); o += 2;     // level_limit
        Assert.Equal(0, I32(batch, o)); o += 4;      // sell epoch
        Assert.Equal(batch.Length, o);               // pas d'octet en trop
    }

    [Fact]
    public void BuildCatalog_SplitsAtNativeBatchLimit()
    {
        // Batching natif : nouveau paquet quand count > 19 (les deux
        // premiers paquets pleins, le 3e porte le reste).
        var items = Enumerable.Range(0, 50)
            .Select(i => new ItemMallEntry
            {
                Group = 1, Index = i, ItemId = 30000 + i,
                ItemNum = 1, MoneyUnit = 1, Point = 1,
            }).ToList();
        var batches = ItemMallPackets.BuildCatalogBatches(
            new ItemMallCatalog(items)).ToList();
        Assert.Equal(3, batches.Count);
        Assert.Equal(20, U16(batches[0], 2));
        Assert.Equal(20, U16(batches[1], 2));
        Assert.Equal(10, U16(batches[2], 2));
        // Même version dans chaque paquet.
        Assert.Equal(1, U16(batches[2], 0));
    }

    [Fact]
    public void BuildLimit_VersionCountThenRecords()
    {
        var bytes = ItemMallPackets.BuildLimit(1,
        [
            new ItemMallLimitRow
            {
                ItemGroup = 8, ItemIndex = 2, ItemId = 30001,
                ItemNum = 1, MoneyUnit = 2, SellAmount = 7,
            },
        ]);
        int o = 0;
        Assert.Equal(1, U16(bytes, o)); o += 2;      // version
        Assert.Equal(1, U16(bytes, o)); o += 2;      // count
        // clé wire native = money_unit*100 + item_group (79 = même
        // encodage que le 62, GetAllItemMallLimit natif)
        Assert.Equal(208, S16(bytes, o)); o += 2;    // 2*100 + group 8
        Assert.Equal(2, S16(bytes, o)); o += 2;      // item_index
        Assert.Equal(30001, U16(bytes, o)); o += 2;  // item_id
        Assert.Equal(1, U16(bytes, o)); o += 2;      // item_num
        Assert.Equal(7, U16(bytes, o)); o += 2;      // sell_amount
        Assert.Equal(bytes.Length, o);
    }

    // ── Wire : lien ZW ────────────────────────────────────────────

    [Fact]
    public void BuildZwDelivery_NativeFieldOrder()
    {
        var b = ItemMallPackets.BuildZwDelivery(99, 1234, 30001, 3,
            0xAABB);
        Assert.Equal(20, b.Length); // u32+u32+u16+u16+i64
        Assert.Equal(99u, U32(b, 0));    // receipt id
        Assert.Equal(1234u, U32(b, 4));  // char id
        Assert.Equal(30001, U16(b, 8));  // item id
        Assert.Equal(3, U16(b, 10));     // quantité
        Assert.Equal(0xAABB, I64(b, 12));// item_color
    }

    [Fact]
    public void ParseZwDeliveryResult_SuccessAndFailures()
    {
        // [s16 result][u32 receipt][u32 char][u32 serial][u32 ctime]
        var body = new byte[18];
        BitConverter.GetBytes((short)0).CopyTo(body, 0);
        BitConverter.GetBytes(99u).CopyTo(body, 2);
        BitConverter.GetBytes(1234u).CopyTo(body, 6);
        BitConverter.GetBytes(555u).CopyTo(body, 10);
        BitConverter.GetBytes(1700000000u).CopyTo(body, 14);
        var r = ItemMallPackets.ParseZwDeliveryResult(body);
        Assert.NotNull(r);
        Assert.Equal((0, 99u, 1234u, 555u, 1700000000u), r!.Value);

        // Codes natifs d'échec préservés (0x19 = inventaire plein).
        BitConverter.GetBytes((short)0x19).CopyTo(body, 0);
        Assert.Equal(0x19,
            ItemMallPackets.ParseZwDeliveryResult(body)!.Value.Result);

        // Body tronqué → null (jamais de parse partiel).
        Assert.Null(ItemMallPackets.ParseZwDeliveryResult(
            body.AsSpan(0, 10)));
    }

    // ── DB : machine d'état receivable → receipt ─────────────────

    private static readonly string AccountConn =
        TestDb.ConnStr("edenfr_account");

    private static readonly string MemberConn =
        TestDb.ConnStr("edenfr_member");

    /// <summary>edenfr_account joignable ? Sinon les tests DB sont skippés.</summary>
    private static async Task<bool> DbUp(string conn)
    {
        try
        {
            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Insère un receivable state=1 via l'API publique puis
    /// retourne l'id. Le nettoyage supprime receivable + receipt.</summary>
    private static async Task<int> SeedReceivable(
        ItemMallRepository repo, string account)
        => await repo.InsertReceivableAsync(
            account, itemId: 30001, itemQuantity: 2, itemColor: 0,
            point: 100, amount: 1, worldId: 1, playerName: "tester",
            mailName: "");

    private static async Task CleanupReceivable(int id)
    {
        await using var c = new NpgsqlConnection(AccountConn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM item_receivable WHERE id=@id; " +
            "DELETE FROM item_receipt WHERE id=@id", c);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Receivable_ClaimThenComplete_MovesToReceipt()
    {
        if (!await DbUp(AccountConn)) return;
        var repo = new ItemMallRepository(Npgsql.NpgsqlDataSource.Create(AccountConn));
        var account = "__mall_" + Guid.NewGuid().ToString("N")[..8];
        var id = await SeedReceivable(repo, account);
        try
        {
            // 15 : témoin « à réclamer ».
            Assert.True(await repo.AnythingReceivableAsync(account));

            // 17 : liste des réclamables (type 0x15 = toutes).
            var list = await repo.GetReceivablesAsync(
                account, 0x15, "", "");
            Assert.Contains(list, r => r.Id == id && r.ItemId == 30001);

            // Claim : state 1→2 — un autre compte ne peut pas voler
            // la ligne, un second claim échoue.
            Assert.Null(await repo.ClaimReceivableAsync(id, "autre"));
            var row = await repo.ClaimReceivableAsync(id, account);
            Assert.NotNull(row);
            Assert.Null(await repo.ClaimReceivableAsync(id, account));

            // Succès zone → insert item_receipt + delete receivable.
            await repo.CompleteReceivableAsync(
                id, account, uniqueId: 4242, createTime: 1700000000);
            Assert.False(await repo.AnythingReceivableAsync(account));

            await using var c = new NpgsqlConnection(AccountConn);
            await c.OpenAsync();
            await using var q = new NpgsqlCommand(
                "SELECT count(*) FROM item_receipt WHERE id=@id " +
                "AND unique_id=4242 AND create_time=1700000000 " +
                "AND account_name=@a", c);
            q.Parameters.AddWithValue("id", id);
            q.Parameters.AddWithValue("a", account);
            Assert.Equal(1L, await q.ExecuteScalarAsync());
        }
        finally { await CleanupReceivable(id); }
    }

    [Fact]
    public async Task Receivable_Requeue_RestoresState1()
    {
        if (!await DbUp(AccountConn)) return;
        var repo = new ItemMallRepository(Npgsql.NpgsqlDataSource.Create(AccountConn));
        var account = "__mall_" + Guid.NewGuid().ToString("N")[..8];
        var id = await SeedReceivable(repo, account);
        try
        {
            Assert.NotNull(await repo.ClaimReceivableAsync(id, account));
            // Échec de livraison zone (ZW12≠0) → remise en file.
            await repo.RequeueReceivableAsync(id);
            Assert.NotNull(await repo.ClaimReceivableAsync(id, account));
            await repo.RequeueReceivableAsync(id); // nettoyage état
        }
        finally { await CleanupReceivable(id); }
    }

    [Fact]
    public async Task LimitAmount_CheckSeedsThenAdds()
    {
        if (!await DbUp(AccountConn)) return;
        var repo = new ItemMallRepository(Npgsql.NpgsqlDataSource.Create(AccountConn));
        int accountId = -new Random().Next(100000, 999999); // jetable
        try
        {
            // Check natif : INSERT à 0 si absent puis relecture.
            Assert.Equal(0, await repo.CheckLimitAmountAsync(
                accountId, group: 8, index: 2, moneyUnit: 1,
                itemId: 30001, itemNum: 1));
            await repo.AddLimitAmountAsync(
                accountId, group: 8, index: 2, moneyUnit: 1,
                itemId: 30001, itemNum: 1, amount: 3);
            Assert.Equal(3, await repo.CheckLimitAmountAsync(
                accountId, group: 8, index: 2, moneyUnit: 1,
                itemId: 30001, itemNum: 1));
            var rows = await repo.GetLimitAmountsAsync(accountId);
            Assert.Single(rows);
            Assert.Equal(3, rows[0].SellAmount);
        }
        finally
        {
            await using var c = new NpgsqlConnection(AccountConn);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM itemmall_limit_amount WHERE account_id=@a",
                c);
            cmd.Parameters.AddWithValue("a", accountId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Points_ReadAndAtomicDeduct()
    {
        if (!await DbUp(MemberConn)) return;
        var repo = new UserRepository(Npgsql.NpgsqlDataSource.Create(MemberConn));
        var mid = "__mall_" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            await using (var c = new NpgsqlConnection(MemberConn))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO tb_user (mid, pvalues, bonus) " +
                    "VALUES (@m, 500, 60)", c);
                cmd.Parameters.AddWithValue("m", mid);
                await cmd.ExecuteNonQueryAsync();
            }
            var pts = await repo.GetItemMallPointsAsync(mid);
            Assert.Equal((500, 60), pts);
            // Débit atomique (UPDATE … WHERE solde >= coût) : OK
            // jusqu'à épuisement, refus au-delà — jamais de négatif.
            Assert.True(await repo.TryDeductItemMallPointsAsync(
                mid, currency: 1, cost: 500));
            Assert.False(await repo.TryDeductItemMallPointsAsync(
                mid, currency: 1, cost: 1));
            Assert.True(await repo.TryDeductItemMallPointsAsync(
                mid, currency: 2, cost: 60));
            pts = await repo.GetItemMallPointsAsync(mid);
            Assert.Equal((0, 0), pts);
        }
        finally
        {
            await using var c = new NpgsqlConnection(MemberConn);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM tb_user WHERE mid=@m", c);
            cmd.Parameters.AddWithValue("m", mid);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
