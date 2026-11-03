using Dapper;
using Npgsql;
using Eden.Core.Game.Trade;

namespace Eden.Core.Database.Repositories;

public sealed class TradeRepository : ITradeRepository
{
    private readonly NpgsqlDataSource _db;
    
    public TradeRepository(NpgsqlDataSource db)
    {
        _db = db;
    }
    
    public async Task<TradeEntity?> GetActiveTradeByPlayerAsync(int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
            SELECT * FROM active_trades
            WHERE initiator_id = @PlayerId OR partner_id = @PlayerId
            ORDER BY id DESC
            LIMIT 1";
        var trade = await connection.QueryFirstOrDefaultAsync<TradeEntity>(sql, new { PlayerId = playerId });
        if (trade != null)
            await LoadItemsAsync(connection, trade);
        return trade;
    }

    public async Task<TradeEntity?> GetActiveTradeAsync(int initiatorId, int partnerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT id, initiator_id, partner_id, initiator_confirmed, partner_confirmed,
                   initiator_gold, partner_gold, created_at
            FROM active_trades
            WHERE initiator_id = @InitiatorId AND partner_id = @PartnerId
            OR (initiator_id = @PartnerId AND partner_id = @InitiatorId)
            LIMIT 1";
        
        var trade = await connection.QueryFirstOrDefaultAsync<TradeEntity>(sql, 
            new { InitiatorId = initiatorId, PartnerId = partnerId });
        if (trade != null)
            await LoadItemsAsync(connection, trade);
        return trade;
    }

    /// <summary>Recharge les items réservés du trade depuis
    /// <c>trade_items</c> (migration 015) dans les listes
    /// <see cref="TradeEntity.InitiatorItems"/> /
    /// <see cref="TradeEntity.PartnerItems"/> — sans elle chaque entité
    /// rechargée avait des listes vides et le settle ne livrait rien
    /// (items débités puis perdus, constat live).</summary>
    private static async Task LoadItemsAsync(
        System.Data.Common.DbConnection connection, TradeEntity trade)
    {
        var rows = (await connection.QueryAsync(
            "SELECT player_id, item_id, quantity, slot " +
            "FROM trade_items WHERE trade_id = @T ORDER BY slot",
            new { T = trade.Id })).ToList();
        foreach (var r in rows)
        {
            var item = new TradeItem
            {
                Slot = (int)r.slot,
                ItemId = (int)r.item_id,
                Quantity = (int)r.quantity,
            };
            if ((int)r.player_id == trade.InitiatorId)
                trade.InitiatorItems.Add(item);
            else
                trade.PartnerItems.Add(item);
        }
    }
    
    public async Task CreateTradeAsync(TradeEntity trade)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            INSERT INTO active_trades (initiator_id, partner_id, initiator_confirmed, partner_confirmed,
                                       initiator_gold, partner_gold, created_at)
            VALUES (@InitiatorId, @PartnerId, @InitiatorConfirmed, @PartnerConfirmed,
                    @InitiatorGold, @PartnerGold, @CreatedAt)";
        
        await connection.ExecuteAsync(sql, trade);
    }
    
    public async Task<bool> UpdateTradeAsync(TradeEntity trade)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE active_trades
            SET initiator_confirmed = @InitiatorConfirmed, partner_confirmed = @PartnerConfirmed,
                initiator_gold = @InitiatorGold, partner_gold = @PartnerGold
            WHERE id = @Id";
        
        var affected = await connection.ExecuteAsync(sql, trade);
        return affected > 0;
    }
    
    public async Task<bool> DeleteTradeAsync(int tradeId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // Les réservations trade_items partent avec la session — sans
        // FK les deux doivent être supprimés explicitement.
        await connection.ExecuteAsync(
            "DELETE FROM trade_items WHERE trade_id = @TradeId",
            new { TradeId = tradeId });
        const string sql = @"
            DELETE FROM active_trades
            WHERE id = @TradeId";

        var affected = await connection.ExecuteAsync(sql, new { TradeId = tradeId });
        return affected > 0;
    }

    /// <summary>
    /// Applique un delta d'or atomique sur <c>player_characters</c> dans
    /// la transaction courante — <c>gold = LEAST(999999999,
    /// GREATEST(0, gold + @d))</c> (clamp natif, miroir de
    /// <c>ICharacterRepository.AddGoldAsync</c>). Un delta négatif est
    /// gardé par <c>gold + @d &gt;= 0</c> : solde insuffisant → 0 ligne
    /// → l'appelant rollback. Retourne le nouveau solde, null si la
    /// garde a refusé l'écriture.
    /// </summary>
    private static async Task<long?> ApplyGoldDeltaAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        int characterId, long delta)
    {
        var sql =
            "UPDATE player_characters " +
            "SET gold = LEAST(999999999, GREATEST(0, gold + @d)) " +
            "WHERE id = @id" +
            (delta < 0 ? " AND gold + @d >= 0" : "") +
            " RETURNING gold::bigint";
        return await connection.ExecuteScalarAsync<long?>(sql,
            new { id = characterId, d = delta }, tx);
    }

    /// <summary>
    /// Règle l'or d'un trade confirmé en UNE transaction : les deux
    /// deltas (initiateur/partenaire) sont appliqués dans l'ordre des
    /// charId croissants (verrouillage déterministe anti-deadlock sur
    /// <c>player_characters</c>). Un delta nul n'écrit pas. Retourne
    /// false — sans rien écrire — si un débit est refusé par la garde
    /// de solde.
    /// </summary>
    public async Task<bool> SettleGoldAsync(int initiatorId,
        long initiatorDelta, int partnerId, long partnerDelta)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            foreach (var (id, delta) in new[]
                     {
                         (initiatorId, initiatorDelta),
                         (partnerId, partnerDelta),
                     }.OrderBy(x => x.Item1))
            {
                if (delta == 0) continue;
                if (await ApplyGoldDeltaAsync(connection, tx, id, delta)
                        == null)
                {
                    await tx.RollbackAsync();
                    return false;
                }
            }
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Réservation d'un item de trade en UNE transaction : débit de la
    /// ligne inventaire (durability −@q gardée par <c>durability &gt;=
    /// @q</c> pour un stack — un débit concurrent ne peut pas passer
    /// en négatif ; DELETE sinon, la clé <c>id</c> fait foi), puis
    /// UPDATE de la ligne <c>active_trades</c> (mêmes colonnes que
    /// <see cref="UpdateTradeAsync"/>). Avant, les deux écritures
    /// partaient sur des connexions distinctes — un crash laissait un
    /// item débité sans réservation persistée. Ordre : inventory1/2
    /// puis active_trades. Retourne false — rollback — si le slot a
    /// été vidé/débité entre-temps.
    /// </summary>
    /// <param name="newQuantity">quantité restante après réservation
    /// (≤0 → DELETE de la ligne).</param>
    /// <param name="itemId">item réservé — écrit dans
    /// <c>trade_items</c> dans la même transaction (sans elle le
    /// settle recharge une entité aux listes vides et l'item débité
    /// n'est jamais livré).</param>
    public async Task<bool> ReserveItemAsync(TradeEntity trade,
        int playerId, int bagId, int invSlot, int quantity,
        int newQuantity, int itemId = 0)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // 1. Inventaire — débit gardé ou suppression.
            var debited = 0;
            foreach (var table in new[] { "inventory1", "inventory2" })
            {
                debited = newQuantity > 0
                    ? await connection.ExecuteAsync(
                        $"UPDATE {table} " +
                        "SET durability = durability - @Q " +
                        "WHERE player_id=@C AND container_index=@B " +
                        "AND loc=@S AND durability >= @Q",
                        new { Q = quantity, C = playerId, B = bagId,
                              S = invSlot }, tx)
                    : await connection.ExecuteAsync(
                        $"DELETE FROM {table} " +
                        "WHERE player_id=@C AND container_index=@B " +
                        "AND loc=@S",
                        new { C = playerId, B = bagId, S = invSlot }, tx);
                if (debited > 0) break;
            }
            if (debited == 0)
            {
                await tx.RollbackAsync();
                return false;
            }

            // 2. Réservation persistée — indispensable au settle qui
            //    recharge l'entité depuis la base.
            if (itemId > 0)
                await connection.ExecuteAsync(
                    "INSERT INTO trade_items " +
                    "(trade_id, player_id, item_id, quantity, slot) " +
                    "VALUES (@T, @P, @I, @Q, @S)",
                    new { T = trade.Id, P = playerId, I = itemId,
                          Q = quantity, S = invSlot }, tx);

            // 3. Session de trade — même UPDATE que UpdateTradeAsync.
            var updated = await connection.ExecuteAsync(@"
                UPDATE active_trades
                SET initiator_confirmed = @InitiatorConfirmed,
                    partner_confirmed = @PartnerConfirmed,
                    initiator_gold = @InitiatorGold,
                    partner_gold = @PartnerGold
                WHERE id = @Id", trade, tx);
            if (updated == 0)
            {
                await tx.RollbackAsync();
                return false;
            }
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Supprime une ligne de réservation du trade
    /// (TradeRemoveItem : l'index est la position dans la liste du
    /// joueur, miroir de <c>RemoveItemAsync</c> du service).</summary>
    public async Task RemoveTradeItemAsync(int tradeId, int playerId,
        int tradeIndex)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        // DELETE par rang : sous-requête sur la ligne n°tradeIndex du
        // joueur dans ce trade (ORDER BY slot, même ordre que
        // LoadItemsAsync).
        await connection.ExecuteAsync(@"
            DELETE FROM trade_items
            WHERE ctid IN (
                SELECT ctid FROM trade_items
                WHERE trade_id = @T AND player_id = @P
                ORDER BY slot LIMIT 1 OFFSET @I)",
            new { T = tradeId, P = playerId, I = tradeIndex });
    }

    /// <summary>Purge toutes les réservations du trade (reset, cancel,
    /// delete) — les items ont déjà été restitués par l'appelant.</summary>
    public async Task ClearTradeItemsAsync(int tradeId)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM trade_items WHERE trade_id = @T",
            new { T = tradeId });
    }

    /// <summary>
    /// Achat d'étal joueur (VendorBuy natif) en UNE transaction :
    /// transfert de la ligne inventaire au nouveau propriétaire
    /// (serial <c>id</c> préservé, miroir du SwitchItemLocation
    /// vendeur→acheteur) puis les deux deltas d'or — ordre des
    /// tables : <c>inventory1/2</c> puis <c>player_characters</c>
    /// (charId croissant). Retourne les soldes réels post-commit ;
    /// <c>Ok=false</c> si la ligne a disparu ou si le débit de
    /// l'acheteur est refusé (rollback intégral).
    /// </summary>
    public async Task<(bool Ok, long BuyerGold, long SellerGold)>
        SettleStallPurchaseAsync(int itemRowId, int buyerId,
            int sellerId, int bagId, int slot, int price)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // 1. Inventaire : ré-attribution de la ligne (les deux
            //    buffers ping-pong sont tentés — un seul porte l'id).
            var moved = 0;
            foreach (var table in new[] { "inventory1", "inventory2" })
            {
                moved = await connection.ExecuteAsync(
                    $"UPDATE {table} SET player_id=@O, " +
                    "container_index=@B, loc=@S WHERE id=@Id",
                    new { O = buyerId, B = bagId, S = slot,
                          Id = itemRowId }, tx);
                if (moved > 0) break;
            }
            if (moved == 0)
            {
                await tx.RollbackAsync();
                return (false, 0, 0);
            }

            // 2. Or : débit acheteur (gardé par le solde) / crédit
            //    vendeur (clamp cap natif) — charId croissant.
            long buyerGold = 0, sellerGold = 0;
            foreach (var (id, delta) in new[]
                     {
                         (buyerId, -(long)price),
                         (sellerId, (long)price),
                     }.OrderBy(x => x.Item1))
            {
                var g = await ApplyGoldDeltaAsync(connection, tx, id,
                    delta);
                if (g == null)
                {
                    await tx.RollbackAsync();
                    return (false, 0, 0);
                }
                if (id == buyerId) buyerGold = g.Value;
                else sellerGold = g.Value;
            }

            await tx.CommitAsync();
            return (true, buyerGold, sellerGold);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Slot acheteur pris entre-temps (index unique
            // player_id+container_index+loc) — rejet propre.
            await tx.RollbackAsync();
            return (false, 0, 0);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Item livré par le trade — quantité réservée + gabarit
    /// de pile du service (<see cref="TradeDeliveryItem.MaxStack"/> &gt; 1
    /// = stackable, count dans durability ; sinon usure
    /// <see cref="TradeDeliveryItem.MaxDurability"/>).</summary>
    public sealed record TradeDeliveryItem(int ItemId, int Quantity,
        int MaxStack, int MaxDurability);

    /// <summary>Soldes post-règlement pour resync des miroirs de
    /// session (st.Gold) et l'attr 0x02 du 280.</summary>
    public sealed record TradeSettlement(long InitiatorGold,
        long PartnerGold);

    /// <summary>Slots libres des sacs 0-4 — capacités
    /// <c>inventory_slots.bag_N_slots</c> (sac 0 = 32 par défaut,
    /// miroir de GetAllBagCapacitiesAsync) moins les locs occupés dans
    /// les deux buffers. Exécuté DANS la transaction de règlement.</summary>
    private const string FreeSlotsSql = @"
        WITH caps AS (
            SELECT bag_0_slots c0, bag_1_slots c1, bag_2_slots c2,
                   bag_3_slots c3, bag_4_slots c4
            FROM inventory_slots WHERE character_id = @C
        ),
        slots AS (
            SELECT 0 bag, generate_series(0,
                COALESCE((SELECT c0 FROM caps), 32) - 1) loc
            UNION ALL
            SELECT 1, generate_series(0,
                COALESCE((SELECT c1 FROM caps), 0) - 1)
            UNION ALL
            SELECT 2, generate_series(0,
                COALESCE((SELECT c2 FROM caps), 0) - 1)
            UNION ALL
            SELECT 3, generate_series(0,
                COALESCE((SELECT c3 FROM caps), 0) - 1)
            UNION ALL
            SELECT 4, generate_series(0,
                COALESCE((SELECT c4 FROM caps), 0) - 1)
        )
        SELECT bag, loc FROM slots
        WHERE NOT EXISTS (
            SELECT 1 FROM inventory1 i WHERE i.player_id = @C
            AND i.container_index = slots.bag AND i.loc = slots.loc)
          AND NOT EXISTS (
            SELECT 1 FROM inventory2 i WHERE i.player_id = @C
            AND i.container_index = slots.bag AND i.loc = slots.loc)
        ORDER BY bag, loc LIMIT @N";

    /// <summary>
    /// Exécution complète d'un trade confirmé en UNE transaction :
    /// les deux deltas d'or (gardés par le solde, charId croissant),
    /// la livraison croisée des items réservés (fusion dans les
    /// stacks existants puis lignes fraîches en slots libres — serial
    /// <c>item_serial</c> alloué dans la tx), puis suppression de la
    /// session <c>active_trades</c>. Avant, l'or partait en tx
    /// séparée puis chaque item sur sa propre connexion — un crash
    /// laissait or réglé + livraison partielle (dupe/perte).
    /// Retourne les soldes réels post-commit, ou null si la tx a
    /// été refusée (solde insuffisant, sac plein, session déjà
    /// close) — le trade reste alors ouvert et les items réservés
    /// restituables via <c>TradeService.CancelTradeAsync</c>.
    /// </summary>
    public async Task<TradeSettlement?> SettleTradeAsync(
        TradeEntity trade,
        IReadOnlyList<TradeDeliveryItem> initiatorDelivers,
        IReadOnlyList<TradeDeliveryItem> partnerDelivers)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // 1. Or — deltas nets (initiateur : +PartnerGold
            //    −InitiatorGold), charId croissant.
            long initiatorGold = 0, partnerGold = 0;
            foreach (var (id, delta) in new[]
                     {
                         (trade.InitiatorId,
                          (long)trade.PartnerGold - trade.InitiatorGold),
                         (trade.PartnerId,
                          (long)trade.InitiatorGold - trade.PartnerGold),
                     }.OrderBy(x => x.Item1))
            {
                var g = await ApplyGoldDeltaAsync(connection, tx, id,
                    delta);
                if (g == null)
                {
                    await tx.RollbackAsync();
                    return null;
                }
                if (id == trade.InitiatorId) initiatorGold = g.Value;
                else partnerGold = g.Value;
            }

            // 2. Livraisons croisées.
            if (!await DeliverItemsAsync(connection, tx,
                    trade.PartnerId, initiatorDelivers) ||
                !await DeliverItemsAsync(connection, tx,
                    trade.InitiatorId, partnerDelivers))
            {
                await tx.RollbackAsync();
                return null;
            }

            // 3. Session — supprimée dans la même tx (0 ligne = déjà
            //    close : rollback, la livraison est annulée aussi).
            //    Les réservations trade_items suivent la session.
            await connection.ExecuteAsync(
                "DELETE FROM trade_items WHERE trade_id = @Id",
                new { trade.Id }, tx);
            var deleted = await connection.ExecuteAsync(
                "DELETE FROM active_trades WHERE id = @Id",
                new { trade.Id }, tx);
            if (deleted == 0)
            {
                await tx.RollbackAsync();
                return null;
            }
            await tx.CommitAsync();
            return new TradeSettlement(initiatorGold, partnerGold);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Slot libre revendiqué par une écriture concurrente —
            // l'index unique (player_id, container_index, loc) est
            // le dernier rempart : rejet propre, rien n'a bougé.
            await tx.RollbackAsync();
            return null;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Livre les items réservés d'un trade à leur
    /// destinataire dans la tx : stackables fusionnés dans les stacks
    /// existants (delta durability — miroir CheckAutoStackItem), puis
    /// lignes fraîches aux premiers slots libres. Retourne false si
    /// l'inventaire ne peut pas tout recevoir.</summary>
    private static async Task<bool> DeliverItemsAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        int recipientId, IReadOnlyList<TradeDeliveryItem> items)
    {
        foreach (var d in items)
        {
            var remaining = d.Quantity;
            if (d.MaxStack > 1)
                remaining = await MergeIntoStacksAsync(connection, tx,
                    recipientId, d, remaining);

            // Nombre de lignes fraîches à insérer.
            var rowsNeeded = d.MaxStack > 1
                ? (int)Math.Ceiling(remaining / (double)d.MaxStack)
                : remaining;
            if (rowsNeeded == 0) continue;

            var slots = (await connection.QueryAsync(FreeSlotsSql,
                new { C = recipientId, N = rowsNeeded }, tx)).ToList();
            if (slots.Count < rowsNeeded) return false;

            foreach (var row in slots)
            {
                var bag = (int)row.bag;
                var loc = (int)row.loc;
                var stackSize = d.MaxStack > 1
                    ? Math.Min(remaining, d.MaxStack) : 1;
                // durability : count du stack ; équipement = usure
                // pleine (la réservation ne porte que itemId+count —
                // même perte d'attributs que le chemin AddItemAsync).
                var dur = d.MaxStack > 1 ? stackSize
                    : Math.Max(d.MaxDurability, 1);
                var max = d.MaxStack > 1 ? d.MaxStack
                    : Math.Max(d.MaxDurability, 1);
                var serial = await ItemSerialAllocator.NextAsync(
                    connection, tx);
                await connection.ExecuteAsync(@"
                    INSERT INTO inventory1
                        (id, player_id, container_index, loc, item_id,
                         durability, cur_maxdurability, strengthen, bind,
                         create_time, due_date, quality, embedded_amount,
                         embedded_id1, embedded_id2, embedded_id3,
                         embedded_id4, embedded_id5, embedded_id6,
                         combo_id, witchcraft, identify,
                         extra_combo_id1, extra_combo_id2,
                         extra_combo_id3, extra_combo_id4,
                         extra_combo_id5, extra_combo_id6,
                         extra_combo_id7, extra_combo_id8,
                         extra_combo_id9)
                    VALUES (@Id, @Pid, @Bag, @Loc, @Iid, @Dur, @Max,
                         0, 0, @Now, 0, 0, 0,
                         -1, -1, -1, -1, -1, -1,
                         0, 0, 1,
                         0, 0, 0, 0, 0, 0, 0, 0, 0)",
                    new
                    {
                        Id = serial, Pid = recipientId, Bag = bag,
                        Loc = loc, Iid = d.ItemId, Dur = dur, Max = max,
                        Now = (int)DateTimeOffset.UtcNow
                            .ToUnixTimeSeconds(),
                    }, tx);
                remaining -= stackSize;
            }
            if (remaining != 0) return false;
        }
        return true;
    }

    /// <summary>Fusionne <paramref name="remaining"/> dans les stacks
    /// non pleins du même itemId (durability en delta — les conteneurs
    /// et les locs sont parcourus dans l'ordre natif) ; retourne le
    /// reliquat à poser en lignes fraîches.</summary>
    private static async Task<int> MergeIntoStacksAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        int recipientId, TradeDeliveryItem d, int remaining)
    {
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            if (remaining == 0) break;
            var stacks = (await connection.QueryAsync(
                $"SELECT id, durability FROM {table} " +
                "WHERE player_id = @C AND item_id = @I " +
                "AND durability > 0 AND durability < @Max " +
                "ORDER BY container_index, loc",
                new { C = recipientId, I = d.ItemId,
                      Max = d.MaxStack }, tx)).ToList();
            foreach (var st in stacks)
            {
                var add = Math.Min(remaining,
                    d.MaxStack - (int)st.durability);
                if (add <= 0) continue;
                await connection.ExecuteAsync(
                    $"UPDATE {table} SET durability = durability + @A " +
                    "WHERE id = @Id",
                    new { A = add, Id = (int)st.id }, tx);
                remaining -= add;
                if (remaining == 0) break;
            }
        }
        return remaining;
    }
}
