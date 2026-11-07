using Dapper;
using Npgsql;
using Eden.Core.Game.Auction;

namespace Eden.Core.Database.Repositories;

public sealed class AuctionRepository : IAuctionRepository
{
    private readonly NpgsqlDataSource _db;
    
    public AuctionRepository(NpgsqlDataSource db)
    {
        _db = db;
    }
    
    public async Task<int> CreateAuctionAsync(AuctionEntity auction)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            INSERT INTO active_auctions (seller_id, item_id, quantity, price, duration_hours, created_at, expires_at, status,
                                       item_type, level, class, quality_filter, item_name)
            VALUES (@SellerId, @ItemId, @Quantity, @Price, @DurationHours, @CreatedAt, @ExpiresAt, @Status,
                    @ItemType, @Level, @Class, @QualityFilter, @ItemName)
            RETURNING id";
        
        return await connection.ExecuteScalarAsync<int>(sql, auction);
    }
    
    /// <summary>
    /// Mise en vente en UNE transaction : retrait de la ligne
    /// inventaire du vendeur (inventory1/2 — le slot entier, la clé
    /// (player_id, container_index, loc) fait foi), débit des frais
    /// de dépôt gardé par le solde (<c>gold + @d &gt;= 0</c> — refus
    /// si le solde a bougé entre le check et la tx), puis INSERT de
    /// l'annonce. Avant, les trois écritures étaient éclatées
    /// (ListAsync + AddGoldAsync + RemoveItemAsync sur des
    /// connexions distinctes) : un crash laissait l'annonce active
    /// avec l'item encore en sac — revente en double.
    /// Ordre de verrouillage : inventaire → personnage → annonce
    /// (miroir de <see cref="TradeRepository.SettleStallPurchaseAsync"/>).
    /// Retourne l'id de l'annonce, null si la tx a été refusée.
    /// </summary>
    public async Task<int?> CreateListingAsync(AuctionEntity auction,
        int bagId, int slot, long depositFee)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // 1. Retrait de la ligne vendue — le slot entier part en
            //    vente (stack complet ou pièce d'équipement).
            var removed = 0;
            foreach (var table in new[] { "inventory1", "inventory2" })
            {
                removed = await connection.ExecuteAsync(
                    $"DELETE FROM {table} WHERE player_id = @C " +
                    "AND container_index = @B AND loc = @S",
                    new { C = auction.SellerId, B = bagId, S = slot }, tx);
                if (removed > 0) break;
            }
            if (removed == 0)
            {
                // Slot vidé entre le check handler et la tx.
                await tx.RollbackAsync();
                return null;
            }

            // 2. Frais de dépôt — delta gardé (le check gold hors tx
            //    n'est qu'indicatif).
            if (depositFee > 0)
            {
                var g = await connection.ExecuteScalarAsync<long?>(
                    "UPDATE player_characters " +
                    "SET gold = LEAST(999999999, GREATEST(0, gold + @d)) " +
                    "WHERE id = @id AND gold + @d >= 0 " +
                    "RETURNING gold::bigint",
                    new { id = auction.SellerId, d = -depositFee }, tx);
                if (g == null)
                {
                    await tx.RollbackAsync();
                    return null;
                }
            }

            // 3. Annonce — même INSERT que CreateAuctionAsync.
            var id = await connection.ExecuteScalarAsync<int>(@"
                INSERT INTO active_auctions
                    (seller_id, item_id, quantity, price, duration_hours,
                     created_at, expires_at, status,
                     item_type, level, class, quality_filter, item_name)
                VALUES (@SellerId, @ItemId, @Quantity, @Price,
                        @DurationHours, @CreatedAt, @ExpiresAt, @Status,
                        @ItemType, @Level, @Class, @QualityFilter, @ItemName)
                RETURNING id", auction, tx);
            await tx.CommitAsync();
            return id;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<AuctionEntity?> GetAuctionAsync(int auctionId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT id, seller_id, item_id, quantity, price, duration_hours, created_at, expires_at, status,
                   item_type, level, class, quality_filter, item_name
            FROM active_auctions
            WHERE id = @AuctionId
            LIMIT 1";
        
        return await connection.QueryFirstOrDefaultAsync<AuctionEntity>(sql, new { AuctionId = auctionId });
    }
    
    public async Task<IReadOnlyList<AuctionEntity>> GetActiveAuctionsAsync()
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT id, seller_id, item_id, quantity, price, duration_hours, created_at, expires_at, status,
                   item_type, level, class, quality_filter, item_name
            FROM active_auctions
            WHERE status = 'active' AND expires_at > NOW()
            ORDER BY created_at DESC";
        
        var results = await connection.QueryAsync<AuctionEntity>(sql);
        return results.ToList();
    }
    
    public async Task<bool> UpdateAuctionAsync(AuctionEntity auction)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE active_auctions
            SET status = @Status
            WHERE id = @Id";
        
        var affected = await connection.ExecuteAsync(sql, auction);
        return affected > 0;
    }
    
    public async Task<bool> DeleteAuctionAsync(int auctionId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        
        const string sql = @"
            DELETE FROM active_auctions
            WHERE id = @AuctionId";
        
        var affected = await connection.ExecuteAsync(sql, new { AuctionId = auctionId });
        return affected > 0;
    }
}
