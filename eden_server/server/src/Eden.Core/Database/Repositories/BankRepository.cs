using Dapper;
using Npgsql;
using Eden.Core.Game.Bank;

namespace Eden.Core.Database.Repositories;

/// <summary>Banque personnelle sur le schéma NATIF edenfr_db : vue
/// `storage` = UNION de `storage1`/`storage2` (buffers de save
/// ping-pong). `loc` = slot banque, `container_index` = onglet
/// (-1/0 = onglet principal). Remplace la table maison `player_bank`
/// — les persos natifs stockent en storage1.</summary>
public sealed class BankRepository : IBankRepository
{
    // Capacité banque par défaut — BankService.DefaultBankSlots (50)
    private const int MaxBankSlots = 50;
    private readonly NpgsqlDataSource _db;

    public BankRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Ligne natif→entité : loc→Slot, cur_maxdurability→
    /// MaxDurability (repli durability), strengthen→EnchantLevel,
    /// bind≠0→lié. Quantity = durability — convention native
    /// (storage1/2 n'a pas de colonne quantity : le count des
    /// stackables vit dans `durability`, comme l'inventaire ;
    /// pour l'équipement le compte logique est 1, voir
    /// BankService.CountOf).</summary>
    private static BankItemEntity ToEntity(Row r) => new()
    {
        Id = r.Id,
        CharacterId = r.PlayerId,
        Slot = r.Loc,
        ItemId = r.ItemId,
        Quantity = r.Durability,
        Durability = r.Durability,
        MaxDurability = r.CurMaxDurability > 0
            ? r.CurMaxDurability : r.Durability,
        ContainerIndex = r.ContainerIndex,
        EnchantLevel = r.Strengthen,
        IsBound = r.Bind != 0,
        CreateTime = r.CreateTime,
    };

    private const string LiveSelect = @"
        SELECT id, player_id, loc, item_id, durability,
               cur_maxdurability, container_index, strengthen,
               bind, create_time
        FROM storage";

    /// <summary>Premier slot banque libre (loc non occupé) dans la
    /// capacité réelle — 40 + 20×`expand_storage_cnt` (natif
    /// CPlayerFactory::ExpandStorage @0x80f2670).</summary>
    public async Task<int?> GetAvailableSlotAsync(int characterId,
        int maxSlots = MaxBankSlots)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT s.slot
            FROM generate_series(0, @MaxSlots - 1) AS s(slot)
            WHERE NOT EXISTS (
                SELECT 1 FROM storage i
                WHERE i.player_id = @CharacterId
                AND i.loc = s.slot
            )
            ORDER BY s.slot
            LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<int?>(sql,
            new { CharacterId = characterId, MaxSlots = maxSlots });
    }

    /// <summary>Item au slot banque donné.</summary>
    public async Task<BankItemEntity?> GetBankItemAsync(int characterId,
        int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<Row>(
            LiveSelect + @"
            WHERE player_id = @CharacterId AND loc = @Slot LIMIT 1",
            new { CharacterId = characterId, Slot = slot });
        return row == null ? null : ToEntity(row);
    }

    /// <summary>Tous les items en banque (tous onglets).</summary>
    public async Task<IReadOnlyList<BankItemEntity>>
        GetBankItemsByCharacterIdAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var rows = await connection.QueryAsync<Row>(LiveSelect + @"
            WHERE player_id = @CharacterId
            ORDER BY loc",
            new { CharacterId = characterId });
        return rows.Select(ToEntity).ToList();
    }

    /// <summary>Insère dans le buffer actif storage1 — la vue `storage`
    /// le lit immédiatement.</summary>
    public async Task AddBankItemAsync(BankItemEntity item)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // id NOT NULL sans séquence propre — serial global natif
        // (même espace que l'inventaire) via la séquence item_serial,
        // voir ItemSerialAllocator / migration 012.
        var serial = await ItemSerialAllocator.NextAsync(_db);

        const string sql = @"
            INSERT INTO storage1
                (id, player_id, container_index, loc, item_id, durability,
                 cur_maxdurability, strengthen, bind, create_time)
            VALUES (@Serial, @CharacterId, 0, @Slot, @ItemId, @Durability,
                 @MaxDurability, @Strengthen, @Bind, @Now)";

        await connection.ExecuteAsync(sql, new
        {
            Serial = serial,
            item.CharacterId,
            item.Slot,
            item.ItemId,
            item.Durability,
            item.MaxDurability,
            Strengthen = item.EnchantLevel,
            Bind = item.IsBound ? 1 : 0,
            // create_time : celui de l'item d'origine s'il est connu
            // (bit 8 du mask item — affiché au client), sinon now.
            Now = item.CreateTime > 0 ? item.CreateTime
                : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
    }

    /// <summary>Supprime l'item au slot — les deux buffers tentés.</summary>
    public async Task<bool> RemoveBankItemAsync(int characterId, int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var affected = 0;
        foreach (var table in new[] { "storage1", "storage2" })
            affected += await connection.ExecuteAsync(
                $"DELETE FROM {table} " +
                "WHERE player_id=@C AND loc=@S",
                new { C = characterId, S = slot });
        return affected > 0;
    }

    /// <summary>Met à jour par clé `id` — les deux buffers.</summary>
    public async Task<bool> UpdateBankItemAsync(BankItemEntity item)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var affected = 0;
        foreach (var table in new[] { "storage1", "storage2" })
            affected += await connection.ExecuteAsync(
                $"UPDATE {table} SET item_id=@ItemId, loc=@Slot, " +
                "durability=@Durability, " +
                "cur_maxdurability=@MaxDurability, " +
                "strengthen=@Strengthen, bind=@Bind WHERE id=@Id",
                new
                {
                    item.ItemId,
                    item.Slot,
                    item.Durability,
                    item.MaxDurability,
                    Strengthen = item.EnchantLevel,
                    Bind = item.IsBound ? 1 : 0,
                    item.Id,
                });
        return affected > 0;
    }

    /// <summary>Nombre d'expansions payées — `player_characters.
    /// expand_storage_cnt` (natif : CCharacter+0x77c, max 2 dans
    /// CPlayerFactory::ExpandStorage @0x80f2670).</summary>
    public async Task<int> GetStorageExpandCountAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<int>(
            "SELECT expand_storage_cnt FROM player_characters " +
            "WHERE id = @CharacterId",
            new { CharacterId = characterId });
    }

    /// <summary>Persiste `expand_storage_cnt` — écriture native
    /// `SavePlayerCharacter "gold expand_storage_cnt"` après
    /// `ExpandStorage` (désasm zone:138155).</summary>
    public async Task SetStorageExpandCountAsync(int characterId,
        int count)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_characters SET expand_storage_cnt = @Count " +
            "WHERE id = @CharacterId",
            new { CharacterId = characterId, Count = count });
    }

    /// <summary>
    /// Dépôt banque atomique — UNE transaction : débit de la ligne
    /// inventaire (durability −@deposited pour un stack, DELETE sinon),
    /// merges storage (durability absolue planifiée), inserts storage1
    /// (slots libres résolus DANS la tx — un slot pris entre-temps fait
    /// échouer l'INSERT puis rollback propre). Ordre de verrouillage :
    /// <c>inventory1/2</c> puis <c>storage1/2</c> — le même que le
    /// retrait (anti-deadlock dépôt↔retrait).
    /// Retourne le nombre réellement déposé (&lt; demandé si la banque
    /// sature), -1 si la ligne inventaire a disparu (rollback).
    /// </summary>
    /// <param name="merges">stacks banque à compléter — Quantity porte
    /// déjà la valeur finale.</param>
    /// <param name="chunks">nouvelles lignes à insérer — Slot résolu
    /// ici dans la tx.</param>
    /// <param name="capacity">capacité réelle du coffre
    /// (40 + 20×expand_storage_cnt).</param>
    /// <param name="inv">entité inventaire source lue par le service
    /// (id de ligne, Quantity/Durability courants).</param>
    /// <param name="invStackable">convention native : le count vit dans
    /// <c>durability</c> pour les stackables.</param>
    public async Task<int> ExecuteDepositAsync(int characterId,
        IReadOnlyList<(BankItemEntity Stack, int Added)> merges,
        IReadOnlyList<BankItemEntity> chunks, int capacity,
        Eden.Core.Game.Inventory.ItemEntity inv, bool invStackable)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // Slots libres du coffre — réservés dans la tx pour que le
            // débit inventaire connaisse le montant réel.
            var freeLocs = (await connection.QueryAsync<int>(@"
                SELECT s.slot FROM generate_series(0, @Cap - 1) AS s(slot)
                WHERE NOT EXISTS (
                    SELECT 1 FROM storage i
                    WHERE i.player_id = @CharacterId AND i.loc = s.slot)
                ORDER BY s.slot",
                new { CharacterId = characterId, Cap = capacity }, tx))
                .ToList();

            var deposited = merges.Sum(m => m.Added);
            var placed = new List<(BankItemEntity Chunk, int Loc)>();
            foreach (var chunk in chunks)
            {
                if (freeLocs.Count == 0) break; // banque pleine
                var loc = freeLocs[0];
                freeLocs.RemoveAt(0);
                placed.Add((chunk, loc));
                deposited += chunk.Quantity;
            }
            if (deposited <= 0)
            {
                await tx.RollbackAsync();
                return 0;
            }

            // 1. Inventaire — débit de la source.
            var debited = 0;
            foreach (var table in new[] { "inventory1", "inventory2" })
            {
                if (!invStackable || inv.Quantity - deposited <= 0)
                    debited = await connection.ExecuteAsync(
                        $"DELETE FROM {table} " +
                        "WHERE id=@Id AND player_id=@C",
                        new { Id = inv.Id, C = characterId }, tx);
                else
                    // durability = count du stack (convention native).
                    debited = await connection.ExecuteAsync(
                        $"UPDATE {table} " +
                        "SET durability = durability - @Dep " +
                        "WHERE id=@Id AND player_id=@C " +
                        "AND durability >= @Dep",
                        new { Id = inv.Id, C = characterId,
                              Dep = deposited }, tx);
                if (debited > 0) break;
            }
            if (debited == 0)
            {
                await tx.RollbackAsync();
                return -1;
            }

            // 2. Storage — merges (même forme d'UPDATE que
            //    UpdateBankItemAsync, les deux buffers tentés).
            foreach (var (stack, _) in merges)
                foreach (var table in new[] { "storage1", "storage2" })
                    if (await connection.ExecuteAsync(
                        $"UPDATE {table} SET item_id=@ItemId, loc=@Slot, " +
                        "durability=@Durability, " +
                        "cur_maxdurability=@MaxDurability, " +
                        "strengthen=@Strengthen, bind=@Bind " +
                        "WHERE id=@Id",
                        new
                        {
                            stack.ItemId, stack.Slot, stack.Durability,
                            stack.MaxDurability,
                            Strengthen = stack.EnchantLevel,
                            Bind = stack.IsBound ? 1 : 0, stack.Id,
                        }, tx) > 0)
                        break;

            // 3. Storage — nouvelles lignes (buffer actif storage1,
            //    serial alloué par la séquence item_serial).
            foreach (var (chunk, loc) in placed)
            {
                var serial = await ItemSerialAllocator.NextAsync(_db);
                await connection.ExecuteAsync(@"
                    INSERT INTO storage1
                        (id, player_id, container_index, loc, item_id,
                         durability, cur_maxdurability, strengthen, bind,
                         create_time)
                    VALUES (@Serial, @CharacterId, 0, @Slot, @ItemId,
                        @Durability, @MaxDurability, @Strengthen, @Bind,
                        @Now)",
                    new
                    {
                        Serial = serial, CharacterId = characterId,
                        Slot = loc, chunk.ItemId, chunk.Durability,
                        chunk.MaxDurability,
                        Strengthen = chunk.EnchantLevel,
                        Bind = chunk.IsBound ? 1 : 0,
                        Now = chunk.CreateTime > 0 ? chunk.CreateTime
                            : (int)DateTimeOffset.UtcNow
                                .ToUnixTimeSeconds(),
                    }, tx);
            }

            await tx.CommitAsync();
            return deposited;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Retrait banque atomique — UNE transaction : merges dans les
    /// stacks inventaire compatibles (durability +@add gardée par
    /// <c>≤ maxStack</c>), nouvelles lignes inventory1 copiées depuis
    /// la ligne storage (métadonnées strengthen/bind/create_time
    /// préservées, serial conservé — miroir du déplacement natif),
    /// puis débit du slot banque. Ordre : <c>inventory1/2</c> puis
    /// <c>storage1/2</c> (identique au dépôt). Tout-ou-rien : si
    /// l'inventaire ne peut pas tout prendre → rollback, banque
    /// intacte (sémantique historique).
    /// </summary>
    /// <returns>Touched = slots inventaire modifiés pour les deltas
    /// 274/291 côté client.</returns>
    public async Task<(bool Ok, int Withdrawn,
        List<(int BagId, int Slot, bool Merged)> Touched)>
        ExecuteWithdrawAsync(int characterId, BankItemEntity item,
            int count, bool stackable, int maxStack)
    {
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            var remaining = count;

            // 1. Inventaire — merge sur les stacks compatibles (même
            //    prédicat que CheckAutoStackItem : item_id + strengthen
            //    + bind, non plein, sacs 0-4). Update RELATIF gardé par
            //    le cap — un writer concurrent ne peut pas faire
            //    déborder le stack.
            if (stackable)
            {
                var stacks = await connection.QueryAsync<InvStack>(
                    @"SELECT id, container_index AS Bag, loc AS Loc,
                             durability AS Dur
                      FROM inventory
                      WHERE player_id = @C AND item_id = @Iid
                        AND strengthen = @St AND (bind <> 0) = @Bound
                        AND container_index BETWEEN 0 AND 4
                        AND durability < @Max
                      ORDER BY container_index, loc",
                    new { C = characterId, Iid = item.ItemId,
                          St = item.EnchantLevel, Bound = item.IsBound,
                          Max = maxStack }, tx);
                foreach (var s in stacks)
                {
                    var add = Math.Min(remaining, maxStack - s.Dur);
                    if (add <= 0) continue;
                    var wrote = 0;
                    foreach (var table in new[] { "inventory1",
                                                  "inventory2" })
                    {
                        wrote = await connection.ExecuteAsync(
                            $"UPDATE {table} " +
                            "SET durability = durability + @A " +
                            "WHERE id=@Id AND durability + @A <= @Max",
                            new { A = add, s.Id, Max = maxStack }, tx);
                        if (wrote > 0) break;
                    }
                    if (wrote > 0)
                    {
                        touched.Add((s.Bag, s.Loc, true));
                        remaining -= add;
                    }
                    if (remaining == 0) break;
                }
            }

            // 2. Inventaire — nouvelles lignes aux premiers slots
            //    libres (capacités inventory_slots, sac 0 = 32 par
            //    défaut — même règle que GetAvailableSlotAsync).
            if (remaining > 0)
            {
                var caps = await LoadBagCapacitiesAsync(connection, tx,
                    characterId);
                var serialReused = false;
                for (var bag = 0; bag < caps.Length && remaining > 0;
                     bag++)
                {
                    var freeLocs = (await connection.QueryAsync<int>(@"
                        SELECT s.loc
                        FROM generate_series(0, @Cap - 1) AS s(loc)
                        WHERE NOT EXISTS (
                            SELECT 1 FROM inventory i
                            WHERE i.player_id = @C
                              AND i.container_index = @Bag
                              AND i.loc = s.loc)
                        ORDER BY s.loc",
                        new { C = characterId, Bag = bag,
                              Cap = caps[bag] }, tx)).ToList();
                    foreach (var loc in freeLocs)
                    {
                        if (remaining == 0) break;
                        var stackSize = stackable
                            ? Math.Min(remaining, maxStack) : 1;
                        // Serial : la ligne source pour le 1er chunk
                        // (miroir du déplacement natif — l'id est
                        // libéré par le DELETE du pas 3), un serial
                        // neuf ensuite — re-insérer le même id
                        // violerait l'unicité inter-tables.
                        var serial = !serialReused ? item.Id
                            : await ItemSerialAllocator.NextAsync(_db);
                        // Copie de la ligne storage → inventory1 :
                        // métadonnées conservées (le natif déplace le
                        // record), durability = taille du chunk pour
                        // un stack, usure réelle sinon.
                        var inserted = 0;
                        foreach (var table in new[] { "storage1",
                                                      "storage2" })
                        {
                            inserted = await connection.ExecuteAsync(
                                $@"INSERT INTO inventory1
                                    (id, player_id, container_index,
                                     loc, item_id, durability,
                                     cur_maxdurability, strengthen,
                                     bind, create_time, identify)
                                SELECT @Serial, @C, @Bag, @Loc, item_id,
                                    @Dur, cur_maxdurability, strengthen,
                                    bind, create_time, 1
                                FROM {table} WHERE id = @Id",
                                new
                                {
                                    Serial = serial, C = characterId,
                                    Bag = bag, Loc = loc,
                                    Dur = stackable ? stackSize
                                        : item.Durability,
                                    Id = item.Id,
                                }, tx);
                            if (inserted > 0) break;
                        }
                        if (inserted == 0)
                        {
                            await tx.RollbackAsync();
                            return (false, 0, touched);
                        }
                        serialReused = true;
                        touched.Add((bag, loc, false));
                        remaining -= stackSize;
                    }
                }
            }
            if (remaining > 0)
            {
                // Inventaire plein — tout-ou-rien (sémantique
                // historique : AddItemAsync échouait → banque intacte).
                await tx.RollbackAsync();
                return (false, 0, touched);
            }

            // 3. Storage — débit de la source (durability = count pour
            //    les stackables, DELETE sinon — les deux buffers).
            var leftOver = stackable ? item.Quantity - count : 0;
            var wrote2 = 0;
            foreach (var table in new[] { "storage1", "storage2" })
            {
                wrote2 = leftOver > 0
                    ? await connection.ExecuteAsync(
                        $"UPDATE {table} SET durability = @Left " +
                        "WHERE id=@Id",
                        new { Left = leftOver, item.Id }, tx)
                    : await connection.ExecuteAsync(
                        $"DELETE FROM {table} WHERE id=@Id",
                        new { item.Id }, tx);
                if (wrote2 > 0) break;
            }
            if (wrote2 == 0)
            {
                await tx.RollbackAsync();
                return (false, 0, touched);
            }

            await tx.CommitAsync();
            return (true, count, touched);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Swap/merge banque atomique — UNE transaction : persiste les
    /// lignes <paramref name="from"/> et <paramref name="to"/> déjà
    /// mutées par le service (loc échangés, durability = count final
    /// pour une fusion), ou DELETE la source vidée par une fusion
    /// complète (<paramref name="deleteFrom"/>). Avant, chaque UPDATE
    /// partait sur sa propre connexion — un crash après le merge de
    /// la cible dupliquait le count. Écritures ordonnées par id
    /// croissant (déterministe). Retourne false si une ligne a
    /// disparu ou si le slot cible a été pris entre-temps.
    /// </summary>
    public async Task<bool> ExecuteSwapAsync(int characterId,
        BankItemEntity from, BankItemEntity? to, bool deleteFrom)
    {
        await using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            // Ordre déterministe des verrous : id de ligne croissant.
            var writes = new List<(BankItemEntity Item, bool Delete)>
                    { (from, deleteFrom) };
            if (to != null) writes.Add((to, false));
            foreach (var (w, del) in writes.OrderBy(x => x.Item.Id))
            {
                var wrote = 0;
                foreach (var table in new[] { "storage1", "storage2" })
                {
                    wrote = del
                        ? await connection.ExecuteAsync(
                            $"DELETE FROM {table} " +
                            "WHERE id=@Id AND player_id=@C",
                            new { w.Id, C = characterId }, tx)
                        : await connection.ExecuteAsync(
                            $"UPDATE {table} SET loc=@Slot, " +
                            "durability=@Durability " +
                            "WHERE id=@Id AND player_id=@C",
                            new
                            {
                                w.Slot, w.Durability,
                                w.Id, C = characterId,
                            }, tx);
                    if (wrote > 0) break;
                }
                if (wrote == 0)
                {
                    await tx.RollbackAsync();
                    return false;
                }
            }
            await tx.CommitAsync();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Slot cible pris entre la lecture du service et la tx
            // (index unique player_id+loc) — rejet propre.
            await tx.RollbackAsync();
            return false;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Capacités des sacs 0-4 — `inventory_slots.bag_N_slots`,
    /// sac 0 = 32 par défaut si la ligne manque (même repli que
    /// <c>InventoryRepository.GetAvailableSlotAsync</c>).</summary>
    private static async Task<int[]> LoadBagCapacitiesAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, int characterId)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT bag_0_slots, bag_1_slots, bag_2_slots, " +
            "bag_3_slots, bag_4_slots FROM inventory_slots " +
            "WHERE character_id=@CharacterId",
            new { CharacterId = characterId }, tx);
        var caps = new int[5];
        if (row == null)
        {
            caps[0] = 32; // repli natif sac de base
            return caps;
        }
        caps[0] = (int)row.bag_0_slots;
        caps[1] = (int)row.bag_1_slots;
        caps[2] = (int)row.bag_2_slots;
        caps[3] = (int)row.bag_3_slots;
        caps[4] = (int)row.bag_4_slots;
        if (caps[0] <= 0) caps[0] = 32;
        return caps;
    }

    /// <summary>Stack inventaire candidat au merge (projection vue
    /// `inventory`).</summary>
    private sealed class InvStack
    {
        public int Id { get; set; }
        public int Bag { get; set; }
        public int Loc { get; set; }
        public int Dur { get; set; }
    }

    /// <summary>Ligne brute de la vue `storage` (schéma natif).</summary>
    private sealed class Row
    {
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public int Loc { get; set; }
        public int ItemId { get; set; }
        public int Durability { get; set; }
        public int CurMaxDurability { get; set; }
        public int ContainerIndex { get; set; }
        public int Strengthen { get; set; }
        public int Bind { get; set; }
        public int CreateTime { get; set; }
    }
}
