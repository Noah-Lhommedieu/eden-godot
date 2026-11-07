using Dapper;
using Npgsql;
using Eden.Core.Game.Inventory;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès inventaire sur le schéma NATIF edenfr_db :
/// vue `inventory` = UNION de `inventory1`/`inventory2` (buffers de
/// save ping-pong du DBA natif). Loc = slot dans le conteneur,
/// container_index = sac (0 = sac principal). La table maison
/// `player_items` n'est plus lue — les persos natifs (ex. test1) y
/// sont vides, leurs items réels vivent dans inventory1.</summary>
public sealed partial class InventoryRepository : IInventoryRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Tables d'instanciation d'items portant une colonne
    /// `id` dans le même espace de serials (vérifié par
    /// information_schema sur edenfr_db — player_closet et player_room n'ont
    /// pas de colonne id et en sont exclus).
    /// Liste documentaire : le seed de la séquence
    /// <c>item_serial</c> est matérialisé dans
    /// <c>Database/012-item-serial-sequence.sql</c> — tenir les deux
    /// en sync. <c>family_building_container</c> y figure : son id est
    /// alloué via <see cref="ItemSerialAllocator"/> (insert bâtiment
    /// de hall de famille).</summary>
    internal static readonly string[] ItemSerialTables =
    {
        "inventory1", "inventory2", "equipment1", "equipment2",
        "storage1", "storage2", "mailitem", "family_storage",
        "player_room_container", "farm_container", "bags",
        "elf_container", "item_mall_items", "player_bank",
        "collection", "family_building_container",
    };

    public InventoryRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>Alloue le prochain serial d'item — voir
    /// <see cref="ItemSerialAllocator"/> (séquence <c>item_serial</c>
    /// partagée, espace global natif).</summary>
    private Task<int> AllocateItemSerialAsync()
        => ItemSerialAllocator.NextAsync(_db);

    /// <summary>Map une ligne de la vue `inventory` vers ItemEntity —
    /// loc→Slot, container_index→BagId, cur_maxdurability→MaxDurability
    /// (repli sur durability), strengthen→EnchantLevel, bind≠0→lié.</summary>
    private static ItemEntity ToEntity(NativeItemRow r) => new()
    {
        Id = r.Id,
        CharacterId = r.PlayerId,
        BagId = r.ContainerIndex,
        Slot = r.Loc,
        ItemId = r.ItemId,
        // La colonne `durability` porte le COUNT du stack pour les
        // consommables (natif : ModifyItemDurability = ajustement de
        // count, CheckAutoStackItem @0x8090410) — Quantity doit la
        // refléter, sinon un stack de 100 est lu comme une unité
        // (bug live : clic droit supprimait toute la pile).
        Quantity = r.Durability > 0 ? r.Durability : 1,
        Durability = r.Durability,
        MaxDurability = r.CurMaxDurability > 0
            ? r.CurMaxDurability : r.Durability,
        EnchantLevel = r.Strengthen,
        IsBound = r.Bind != 0,
        CreateTime = r.CreateTime,
        DueDate = r.DueDate,
        Quality = r.Quality,
        Magic = r.Magic,
        MagicBoxEnchant1 = r.MagicboxEnchant1,
        MagicBoxEnchant2 = r.MagicboxEnchant2,
        MagicBoxEnchant3 = r.MagicboxEnchant3,
        MagicBoxEnergy = r.MagicboxEnergy,
        MagicBoxStarLevel = r.MagicboxStarlevel,
        EmbeddedAmount = r.EmbeddedAmount,
        EmbeddedId1 = r.EmbeddedId1,
        EmbeddedId2 = r.EmbeddedId2,
        EmbeddedId3 = r.EmbeddedId3,
        EmbeddedId4 = r.EmbeddedId4,
        EmbeddedId5 = r.EmbeddedId5,
        EmbeddedId6 = r.EmbeddedId6,
        Combo = r.ComboId,
        Witchcraft = r.Witchcraft,
        Identify = r.Identify,
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
        GoldEnchant = r.GoldEnchant,
        PlusParameter = r.PlusParameter,
        Color = r.Color,
        TrainLevel = r.TrainLevel,
        TrainExp = r.TrainExp,
        TrainSlot1Type = r.TrainSlot1Type,
        TrainSlot1Level = r.TrainSlot1Level,
        TrainSlot2Type = r.TrainSlot2Type,
        TrainSlot2Level = r.TrainSlot2Level,
        TrainSlot3Type = r.TrainSlot3Type,
        TrainSlot3Level = r.TrainSlot3Level,
    };

    private const string LiveSelect = @"
        SELECT id, player_id, loc, item_id, durability,
               cur_maxdurability, strengthen, bind, create_time,
               container_index, due_date, quality, magic,
               magicbox_enchant1, magicbox_enchant2, magicbox_enchant3,
               magicbox_energy, magicbox_starlevel,
               embedded_amount,
               embedded_id1, embedded_id2, embedded_id3,
               embedded_id4, embedded_id5, embedded_id6,
               combo_id, witchcraft, identify,
               extra_combo_id1, extra_combo_id2, extra_combo_id3,
               extra_combo_id4, extra_combo_id5, extra_combo_id6,
               extra_combo_id7, extra_combo_id8, extra_combo_id9,
               extra_combo_id10, extra_combo_id11, gold_enchant,
               plus_parameter, color,
               train_level, train_exp,
               train_slot1_type, train_slot1_level,
               train_slot2_type, train_slot2_level,
               train_slot3_type, train_slot3_level
        FROM inventory";

    /// <summary>Premier slot libre du sac (loc non occupé dans
    /// container_index) — borné par la capacité RÉELLE du sac : le
    /// client ne connaît que `bag_N_slots` emplacements (base 32 +
    /// sacs équipés) ; placer un item au-delà produit un 274/277 avec
    /// loc hors limites → PutIn hors tableau → crash client (bug live
    /// : déséquipement, achat NPC, loot).</summary>
    public async Task<int?> GetAvailableSlotAsync(int characterId, int bagId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var capacity = await GetBagCapacity(characterId, bagId)
            ?? (bagId == 0 ? 32 : 0);
        if (capacity <= 0) return null;

        const string sql = @"
            SELECT s.slot
            FROM generate_series(0, @Cap - 1) AS s(slot)
            WHERE NOT EXISTS (
                SELECT 1 FROM inventory i
                WHERE i.player_id = @CharacterId
                AND i.container_index = @BagId
                AND i.loc = s.slot
            )
            ORDER BY s.slot
            LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<int?>(sql,
            new { CharacterId = characterId, BagId = bagId,
                  Cap = capacity });
    }

    /// <summary>Premier emplacement libre TOUS SACS confondus —
    /// `IItemContainer::GetEmptySlot` itéré sur les conteneurs ouverts
    /// comme le natif (dstIndex=-1 du SwitchItemLocation : le client
    /// demande « n'importe quel sac »). Itère 0→4 par capacité
    /// décroissante d'index, retourne (bagId, slot) ou null si tout
    /// est plein.</summary>
    public async Task<(int BagId, int Slot)?> GetAnyAvailableSlotAsync(
        int characterId)
    {
        var capacities = await GetAllBagCapacitiesAsync(characterId);
        for (var bag = 0; bag <= 4; bag++)
        {
            if (!capacities.TryGetValue(bag, out var cap) || cap <= 0)
                continue;
            var slot = await GetAvailableSlotAsync(characterId, bag);
            if (slot != null) return (bag, slot.Value);
        }
        return null;
    }

    /// <summary>Un item par sac+slot natif.</summary>
    public async Task<ItemEntity?> GetItemAsync(int characterId, int bagId,
        int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<NativeItemRow>(
            LiveSelect + @"
            WHERE player_id = @CharacterId AND container_index = @BagId
              AND loc = @Slot LIMIT 1",
            new { CharacterId = characterId, BagId = bagId, Slot = slot });
        return row == null ? null : ToEntity(row);
    }

    /// <summary>Un item porté — conteneur cible natif type 1 de
    /// `CItemFactory::GetCloneItemFromContainer` (map char+0x4fc). La
    /// vue `equipment` (UNION equipment1/2) est indexée par
    /// (container_index = groupe d'équipement, loc = slot d'équipement).
    /// </summary>
    public async Task<ItemEntity?> GetEquipmentItemAsync(int characterId,
        int group, int loc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory", "FROM equipment") + @"
            WHERE player_id = @CharacterId AND container_index = @G
              AND loc = @L LIMIT 1",
            new { CharacterId = characterId, G = group, L = loc });
        if (row == null) return null;
        var e = ToEntity(row);
        e.IsEquipped = true;
        return e;
    }

    /// <summary>Un item du coffre — conteneur cible natif type 4 de
    /// `CItemFactory::GetCloneItemFromContainer` (map char+0x514 —
    /// le natif permet d'incruster sur un item en banque).</summary>
    public async Task<ItemEntity?> GetStorageItemAsync(int characterId,
        int index, int loc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory", "FROM storage") + @"
            WHERE player_id = @CharacterId AND container_index = @I
              AND loc = @L LIMIT 1",
            new { CharacterId = characterId, I = index, L = loc });
        return row == null ? null : ToEntity(row);
    }

    /// <summary>Persiste les 7 champs de socket d'un item — équivalent
    /// `CItemDBO::ModifyItemEmbedded` natif (UseItemToItemControl::
    /// UseItem case 0x2d : UPDATE embedded_amount + embedded_id1-6 sur
    /// la table du conteneur). Le serial `id` est global sur toutes les
    /// tables items → on balaie inventory1/2, equipment1/2, storage1/2
    /// et on s'arrête à la première ligne touchée.</summary>
    public async Task UpdateEmbeddedAsync(int itemUid, int amount,
        IReadOnlyList<int> ids)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var p = new
        {
            A = amount,
            E1 = ids[0], E2 = ids[1], E3 = ids[2],
            E4 = ids[3], E5 = ids[4], E6 = ids[5],
            Id = itemUid,
        };
        foreach (var table in new[]
                 { "inventory1", "inventory2", "equipment1",
                   "equipment2", "storage1", "storage2" })
        {
            var n = await connection.ExecuteAsync(
                $"UPDATE {table} SET embedded_amount=@A, " +
                "embedded_id1=@E1, embedded_id2=@E2, embedded_id3=@E3, " +
                "embedded_id4=@E4, embedded_id5=@E5, embedded_id6=@E6 " +
                "WHERE id=@Id", p);
            if (n > 0) return;
        }
    }

    /// <summary>Persiste le champ enchant d'un item — équivalent
    /// `CItemDBO::ModifyItemWitchCraft` natif (UseItemToItemControl::
    /// UseItem case 0x38/0x4f : `UPDATE <table> SET witchcraft=…` —
    /// colonne `witchcraft` = itemId du scroll appliqué, clone+0x4c).
    /// Le serial `id` est global → même balayage que
    /// <see cref="UpdateEmbeddedAsync"/>.</summary>
    public async Task UpdateWitchcraftAsync(int itemUid, int witchcraft)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[]
                 { "inventory1", "inventory2", "equipment1",
                   "equipment2", "storage1", "storage2" })
        {
            var n = await connection.ExecuteAsync(
                $"UPDATE {table} SET witchcraft=@W WHERE id=@Id",
                new { W = witchcraft, Id = itemUid });
            if (n > 0) return;
        }
    }

    /// <summary>Persiste les 8 champs Mana Infusion d'un item —
    /// équivalent `CItemDBO::ModifyItemTrain` natif (appelé par
    /// SetItemTrainType @0x8099940 et GainEquipExp @0x841b1d0) :
    /// UPDATE train_level/train_exp/train_slotN_{type,level} sur la
    /// table du conteneur ; le serial est global → même balayage que
    /// <see cref="UpdateEmbeddedAsync"/>.</summary>
    public async Task UpdateItemTrainAsync(int itemUid, int level,
        long exp, int t1, int l1, int t2, int l2, int t3, int l3)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var p = new { Lv = level, Ex = exp, T1 = t1, L1 = l1,
                      T2 = t2, L2 = l2, T3 = t3, L3 = l3, Id = itemUid };
        foreach (var table in new[]
                 { "inventory1", "inventory2", "equipment1",
                   "equipment2", "storage1", "storage2" })
        {
            var n = await connection.ExecuteAsync(
                $"UPDATE {table} SET train_level=@Lv, train_exp=@Ex, " +
                "train_slot1_type=@T1, train_slot1_level=@L1, " +
                "train_slot2_type=@T2, train_slot2_level=@L2, " +
                "train_slot3_type=@T3, train_slot3_level=@L3 " +
                "WHERE id=@Id", p);
            if (n > 0) return;
        }
    }

    /// <summary>`eqtrain_item_id` du perso — serial de l'item choisi
    /// pour la Mana Infusion (`player_characters`, char+0xd54 natif).
    /// </summary>
    public async Task<int> GetEquipTrainItemIdAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(eqtrain_item_id, 0) FROM player_characters " +
            "WHERE id = @C", new { C = characterId });
    }

    /// <summary>Persiste `eqtrain_item_id` — équivalent du
    /// `CCharacterDBO::SavePlayerCharacter(.., "eqtrain_item_id")`
    /// de `SetEquipTrainItemID` @0x840b670.</summary>
    public async Task SetEquipTrainItemIdAsync(int characterId,
        int itemSerial)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_characters SET eqtrain_item_id = @S " +
            "WHERE id = @C", new { S = itemSerial, C = characterId });
    }

    /// <summary>Résout un serial d'item → (entité, conteneur natif,
    /// clé, index dans le vecteur) — équivalent
    /// `CCharacter::FindEquipTrainItemLocation` @0x840b500 :
    /// conteneurs ÉQUIPÉS d'abord (arbre +0x4fc, type 1, clé =
    /// container_index/groupe, index = position triée par loc), puis
    /// les sacs ouverts (arbre +0x4e4, type 0, clé = bagId, index
    /// pareil). Retourne null si le serial est introuvable — le natif
    /// remet alors eqtrain_item_id à 0.</summary>
    public async Task<(ItemEntity Item, int ContainerType,
        int ContainerKey, int VectorIndex)?>
        FindItemBySerialAsync(int characterId, int serial)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // 1) conteneurs d'équipement (type natif 1) — chaque
        // container_index est un conteneur CEquipment ; l'index natif
        // du vecteur = le loc (vecteur dimensionné par
        // SetContainerSize, slots à trous — GainEquipExp gate
        // l'archétype sur `0 < index < 6` = locs d'armure).
        var eq = (await connection.QueryAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory", "FROM equipment") + @"
            WHERE player_id = @C ORDER BY container_index, loc",
            new { C = characterId })).Select(ToEntity).ToList();
        foreach (var g in eq.GroupBy(i => i.BagId).OrderBy(g => g.Key))
        {
            var hit = g.FirstOrDefault(i => i.Id == serial);
            if (hit != null)
            {
                hit.IsEquipped = true;
                return (hit, 1, g.Key, hit.Slot);
            }
        }

        // 2) sacs ouverts (type natif 0 — CInventory) : index = loc
        // (vecteur dimensionné à la capacité du sac, slots à trous).
        var bags = (await connection.QueryAsync<NativeItemRow>(
            LiveSelect + @"
            WHERE player_id = @C AND container_index BETWEEN 0 AND 4
            ORDER BY container_index, loc",
            new { C = characterId })).Select(ToEntity).ToList();
        foreach (var g in bags.GroupBy(i => i.BagId).OrderBy(g => g.Key))
        {
            var hit = g.FirstOrDefault(i => i.Id == serial);
            if (hit != null) return (hit, 0, g.Key, hit.Slot);
        }
        return null;
    }

    /// <summary>Tous les items du perso (toutes les poches).</summary>
    public async Task<IReadOnlyList<ItemEntity>> GetItemsByCharacterIdAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var rows = await connection.QueryAsync<NativeItemRow>(
            LiveSelect + @"
            WHERE player_id = @CharacterId
            ORDER BY container_index, loc",
            new { CharacterId = characterId });
        return rows.Select(ToEntity).ToList();
    }

    /// <summary>Tous les items des conteneurs d'inventaire ouverts —
    /// le NCID 312 sérialise un bloc bitmap par conteneur contigu,
    /// pas uniquement le sac 0 (désasm
    /// `CNC_CZ_ClientEnterNodeInventory::Serialize` @0x8233d80).</summary>
    public async Task<IReadOnlyList<ItemEntity>> GetLiveItemsAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var rows = await connection.QueryAsync<NativeItemRow>(
            LiveSelect + @"
            WHERE player_id = @CharacterId
              AND container_index BETWEEN 0 AND 4
            ORDER BY container_index, loc",
            new { CharacterId = characterId });
        return rows.Select(ToEntity).ToList();
    }

    /// <summary>Insère un item dans le buffer actif inventory1 — le
    /// prochain save DBA le ping-ponguera ; la vue le lit déjà.</summary>
    public async Task AddItemAsync(ItemEntity item)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // id NOT NULL sans séquence côté schéma natif — serial alloué
        // par le serveur via la séquence item_serial partagée
        // (voir AllocateItemSerialAsync / migration 012).
        item.Id = await AllocateItemSerialAsync();

        const string sql = @"
            INSERT INTO inventory1
                (id, player_id, container_index, loc, item_id, durability,
                 cur_maxdurability, strengthen, bind, create_time, due_date,
                 quality, embedded_amount,
                 embedded_id1, embedded_id2, embedded_id3,
                 embedded_id4, embedded_id5, embedded_id6,
                 combo_id, witchcraft, identify,
                 extra_combo_id1, extra_combo_id2, extra_combo_id3,
                 extra_combo_id4, extra_combo_id5, extra_combo_id6,
                 extra_combo_id7, extra_combo_id8, extra_combo_id9)
            VALUES (@Id, @CharacterId, @BagId, @Slot, @ItemId, @Durability,
                 @MaxDurability, @EnchantLevel, @IsBoundI, @CreateTime,
                 @DueDate, @Quality, @EmbeddedAmount,
                 @EmbeddedId1, @EmbeddedId2, @EmbeddedId3,
                 @EmbeddedId4, @EmbeddedId5, @EmbeddedId6,
                 @Combo, @Witchcraft, @Identify,
                 @EC1, @EC2, @EC3, @EC4, @EC5, @EC6, @EC7, @EC8, @EC9)";

        await connection.ExecuteAsync(sql, new
        {
            item.Id,
            item.CharacterId,
            item.BagId,
            item.Slot,
            item.ItemId,
            item.Durability,
            item.MaxDurability,
            item.EnchantLevel,
            IsBoundI = item.IsBound ? 1 : 0,
            CreateTime = item.CreateTime != 0 ? item.CreateTime
                : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            item.DueDate,
            item.Quality,
            item.EmbeddedAmount,
            item.EmbeddedId1,
            item.EmbeddedId2,
            item.EmbeddedId3,
            item.EmbeddedId4,
            item.EmbeddedId5,
            item.EmbeddedId6,
            item.Combo,
            item.Witchcraft,
            item.Identify,
            EC1 = item.ExtraCombos[0], EC2 = item.ExtraCombos[1],
            EC3 = item.ExtraCombos[2], EC4 = item.ExtraCombos[3],
            EC5 = item.ExtraCombos[4], EC6 = item.ExtraCombos[5],
            EC7 = item.ExtraCombos[6], EC8 = item.ExtraCombos[7],
            EC9 = item.ExtraCombos[8],
        });
    }

    /// <summary>Supprime l'item au slot donné — les deux buffers sont
    /// tentés (le save peut en avoir permuté un).</summary>
    public async Task<bool> RemoveItemAsync(int characterId, int bagId,
        int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var affected = 0;
        foreach (var table in new[] { "inventory1", "inventory2" })
            affected += await connection.ExecuteAsync(
                $"DELETE FROM {table} " +
                "WHERE player_id=@C AND container_index=@B AND loc=@S",
                new { C = characterId, B = bagId, S = slot });
        return affected > 0;
    }

    /// <summary>Supprime un item équipé — consommation native d'un item
    /// utilisé depuis le conteneur équipement (`CItemFactory::
    /// UseItemParam` sel=1 → map char+0x4fc ; les items portés ne sont
    /// jamais stackables → DELETE direct, comme RemoveContainerItem
    /// natif quand le count atteint 0). Les deux buffers sont tentés.
    /// </summary>
    public async Task<bool> RemoveEquippedItemAsync(int characterId,
        int group, int loc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var affected = 0;
        foreach (var table in new[] { "equipment1", "equipment2" })
            affected += await connection.ExecuteAsync(
                $"DELETE FROM {table} " +
                "WHERE player_id=@C AND container_index=@G AND loc=@L",
                new { C = characterId, G = group, L = loc });
        return affected > 0;
    }

    /// <summary>Suppression par clé `id` (consommation — Portal Stone).</summary>
    public async Task RemoveLiveItemAsync(int characterId, int itemRowId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
            if (await connection.ExecuteAsync(
                    $"DELETE FROM {table} WHERE id=@Id AND player_id=@C",
                    new { Id = itemRowId, C = characterId }) > 0)
                return;
    }

    /// <summary>Transfert de propriété d'une ligne item — achat
    /// d'étal natif (`CItemFactory::SwitchItemLocation` vendeur→
    /// acheteur) : ré-attribue `player_id` et repositionne la ligne
    /// sans changer le serial `id` (l'historique de l'item suit).</summary>
    public async Task<bool> TransferItemAsync(int itemRowId,
        int newOwnerId, int newBagId, int newSlot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
            if (await connection.ExecuteAsync(
                    $"UPDATE {table} SET player_id=@O, " +
                    "container_index=@B, loc=@S WHERE id=@Id",
                    new { O = newOwnerId, B = newBagId, S = newSlot,
                          Id = itemRowId }) > 0)
                return true;
        return false;
    }

    /// <summary>Met à jour un item par sa clé `id` (déplacement de slot
    /// libre, swap) — les deux buffers.</summary>
    public async Task<bool> UpdateItemAsync(ItemEntity item)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        var affected = 0;
        foreach (var table in new[] { "inventory1", "inventory2" })
            affected += await connection.ExecuteAsync(
                $"UPDATE {table} SET item_id=@ItemId, " +
                "container_index=@BagId, loc=@Slot, " +
                "durability=@Durability, cur_maxdurability=@MaxDurability, " +
                "strengthen=@EnchantLevel, bind=@IsBoundI, " +
                "identify=@Identify " +
                // Les serials `id` ne sont pas uniques entre
                // inventory1 et inventory2 (persos natifs importés) :
                // sans le filtre propriétaire l'UPDATE écrasait la
                // ligne homonyme d'un AUTRE perso (loc tampon du tri
                // jamais restauré, 23505 inventory2_index).
                "WHERE id=@Id AND (@Owner = 0 OR player_id=@Owner)",
                new
                {
                    Owner = item.CharacterId,
                    item.ItemId,
                    item.BagId,
                    item.Slot,
                    item.Durability,
                    item.MaxDurability,
                    item.EnchantLevel,
                    IsBoundI = item.IsBound ? 1 : 0,
                    item.Identify,
                    item.Id,
                });
        return affected > 0;
    }

    /// <summary>`CItemDBO::SetItemBind` natif (BindItem @0x08116870 :
    /// UPDATE bind=1 sur la ligne item). Le serial `id` est global sur
    /// toutes les tables items → on balaie inventory1/2 + equipment1/2
    /// (un item lié ne peut pas être en banque/famille — les conteneurs
    /// de dépôt refusent déjà les items liés).</summary>
    public async Task SetItemBindAsync(int itemUid)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2",
                                      "equipment1", "equipment2",
                                      "bags" })
            if (await connection.ExecuteAsync(
                    $"UPDATE {table} SET bind=1 WHERE id=@Id",
                    new { Id = itemUid }) > 0)
                return;
    }

    /// <summary>Miroir de <see cref="SetItemBindAsync"/> pour le
    /// dé-lier — `CItemDBO::ModifyItemBind` natif appelé par
    /// `CEC_ItemUnBind::Execute` @0x0853f820 (parchemin unbind) :
    /// `bind=0` sur la ligne item.</summary>
    public async Task ClearItemBindAsync(int itemUid)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2",
                                      "equipment1", "equipment2",
                                      "bags" })
            if (await connection.ExecuteAsync(
                    $"UPDATE {table} SET bind=0 WHERE id=@Id",
                    new { Id = itemUid }) > 0)
                return;
    }

    /// <summary>Persiste le vecteur de combos d'un item — équivalent
    /// `CItemDBO::ModifyItemCombo` natif (AddComboStrengthen @0x080a2160 :
    /// les 11 u16 `extra_combo_id1..11` du clone+0x34). Cherche la ligne
    /// dans inventory1 puis inventory2 (ping-pong DBA natif).</summary>
    public async Task UpdateItemCombosAsync(ItemEntity item)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            var n = await connection.ExecuteAsync(
                $"UPDATE {table} SET extra_combo_id1=@E1, " +
                "extra_combo_id2=@E2, extra_combo_id3=@E3, " +
                "extra_combo_id4=@E4, extra_combo_id5=@E5, " +
                "extra_combo_id6=@E6, extra_combo_id7=@E7, " +
                "extra_combo_id8=@E8, extra_combo_id9=@E9, " +
                "extra_combo_id10=@E10, extra_combo_id11=@E11 " +
                "WHERE id=@Id",
                new
                {
                    E1 = item.ExtraCombos[0], E2 = item.ExtraCombos[1],
                    E3 = item.ExtraCombos[2], E4 = item.ExtraCombos[3],
                    E5 = item.ExtraCombos[4], E6 = item.ExtraCombos[5],
                    E7 = item.ExtraCombos[6], E8 = item.ExtraCombos[7],
                    E9 = item.ExtraCombos[8],
                    E10 = item.ExtraComboId10, E11 = item.ExtraComboId11,
                    item.Id,
                });
            if (n > 0) return;
        }
    }

    /// <summary>Persiste les champs boîte magique d'un item —
    /// équivalent `CItemDBO::UpdateMagicBoxData` natif (DoMagicBox
    /// @0x80a8b90 : UPDATE magicbox_enchant1-3, magicbox_energy,
    /// magicbox_starlevel sur la table inventory). Cherche la ligne
    /// dans inventory1 puis inventory2 (ping-pong DBA natif).</summary>
    public async Task UpdateMagicBoxDataAsync(int itemUid, int enchant1,
        int enchant2, int enchant3, int energy, int starLevel)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            var n = await connection.ExecuteAsync(
                $"UPDATE {table} SET magicbox_enchant1=@E1, " +
                "magicbox_enchant2=@E2, magicbox_enchant3=@E3, " +
                "magicbox_energy=@En, magicbox_starlevel=@Sl WHERE id=@Id",
                new { E1 = enchant1, E2 = enchant2, E3 = enchant3,
                      En = energy, Sl = starLevel, Id = itemUid });
            if (n > 0) return;
        }
    }

    /// <summary>Ligne complète d'un item par serial — toutes les
    /// colonnes (utilisé pour construire le 369 ClientModifyItemInfo
    /// natif qui sérialise l'enregistrement entier, DoMagicBox
    /// @0x80a8b90). Le serial est global → recherche dans tous les
    /// conteneurs (sac, équipement, coffre).</summary>
    public async Task<IDictionary<string, object?>?> GetItemFullRowAsync(
        int itemUid)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[]
                 { "inventory1", "inventory2", "equipment1",
                   "equipment2", "storage1", "storage2" })
        {
            var row = await connection.QueryFirstOrDefaultAsync(
                $"SELECT * FROM {table} WHERE id=@Id", new { Id = itemUid });
            if (row != null) return (IDictionary<string, object?>)row;
        }
        return null;
    }

    /// <summary>Déplace un item du sac vers l'équipement — équivalent
    /// natif `CItemFactory::Equip` : la ligne quitte inventory1/2 pour
    /// equipment1 (loc = slot d'équipement, container_index = groupe
    /// d'équipement — 0 = tenue courante). Les colonnes d'instance
    /// (durability, bind, strengthen, serial `id`) sont préservées.</summary>
    public async Task<bool> MoveToEquipmentAsync(int characterId,
        int bagId, int slot, int equipGroup, int equipLoc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            // DELETE ... RETURNING * : lecture+retrait atomique de la
            // ligne complète (toutes les colonnes d'instance).
            var row = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@B AND loc=@S RETURNING *",
                new { C = characterId, B = bagId, S = slot }, tx);
            if (row == null) continue;
            var d = (IDictionary<string, object?>)row;

            // Swap natif : si une pièce occupe déjà le loc d'équipement,
            // elle repart dans le sac au slot libéré par la nouvelle
            // pièce (même transaction — sinon violation de l'unique
            // (player_id, container_index, loc) d'equipment1).
            var old = await connection.QueryFirstOrDefaultAsync(
                "DELETE FROM equipment1 WHERE player_id=@C AND " +
                "container_index=@G AND loc=@L RETURNING *",
                new { C = characterId, G = equipGroup, L = equipLoc },
                tx);
            if (old != null)
            {
                var o = (IDictionary<string, object?>)old;
                await connection.ExecuteAsync(@"
                    INSERT INTO inventory1
                        (id, player_id, container_index, loc, item_id,
                         durability, cur_maxdurability, strengthen, bind,
                         maker, rank, identify, magic, embedded_amount,
                         embedded_id1, embedded_id2, embedded_id3,
                         embedded_id4, embedded_id5, embedded_id6,
                         create_time, due_date, combo_id)
                    VALUES (@id, @pid, @bag, @loc, @iid, @dura, @maxd,
                        @str, @bind, @maker, @rank, @ident, @magic, @embAmt,
                        @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due,
                        @combo)",
                    new
                    {
                        id = o["id"], pid = characterId, bag = bagId,
                        loc = slot, iid = o["item_id"],
                        dura = o["durability"], maxd = o["cur_maxdurability"],
                        str = o["strengthen"], bind = o["bind"],
                        maker = o["maker"], rank = o["rank"],
                        ident = o["identify"], magic = o["magic"],
                        embAmt = o["embedded_amount"],
                        e1 = o["embedded_id1"], e2 = o["embedded_id2"],
                        e3 = o["embedded_id3"], e4 = o["embedded_id4"],
                        e5 = o["embedded_id5"], e6 = o["embedded_id6"],
                        ctime = o["create_time"], due = o["due_date"],
                        combo = o["combo_id"],
                    }, tx);
            }

            await connection.ExecuteAsync(@"
                INSERT INTO equipment1
                    (id, player_id, container_index, loc, item_id,
                     durability, cur_maxdurability, strengthen, bind,
                     maker, rank, identify, magic, embedded_amount,
                     embedded_id1, embedded_id2, embedded_id3,
                     embedded_id4, embedded_id5, embedded_id6,
                     create_time, due_date, combo_id)
                VALUES (@id, @pid, @grp, @loc, @iid, @dura, @maxd,
                    @str, @bind, @maker, @rank, @ident, @magic, @embAmt,
                    @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, @combo)",
                new
                {
                    id = d["id"], pid = characterId, grp = equipGroup,
                    loc = equipLoc, iid = d["item_id"],
                    dura = d["durability"], maxd = d["cur_maxdurability"],
                    str = d["strengthen"], bind = d["bind"],
                    maker = d["maker"], rank = d["rank"],
                    ident = d["identify"], magic = d["magic"],
                    embAmt = d["embedded_amount"],
                    e1 = d["embedded_id1"], e2 = d["embedded_id2"],
                    e3 = d["embedded_id3"], e4 = d["embedded_id4"],
                    e5 = d["embedded_id5"], e6 = d["embedded_id6"],
                    ctime = d["create_time"], due = d["due_date"],
                    combo = d["combo_id"],
                }, tx);
            await tx.CommitAsync();
            return true;
        }
        return false;
    }

    /// <summary>Déséquipe — `CItemFactory::UnEquip` : la ligne repart
    /// de equipment1/2 vers inventory1 au slot demandé. Retourne false
    /// si le slot destination est occupé ou l'item introuvable.</summary>
    public async Task<bool> MoveToInventoryAsync(int characterId,
        int equipGroup, int equipLoc, int bagId, int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        // Slot destination libre ? Les tables réelles sont inventory1/2 —
        // une table « inventory » n'existe pas : la requête levait une
        // NpgsqlException non gérée → aucun ack → freeze client au
        // déséquipement (bug live). Le test, DELETE et INSERT partagent
        // une transaction pour qu'un échec d'insertion ne perde pas l'item.
        var occupied = await connection.ExecuteScalarAsync<int>(
            "SELECT (SELECT COUNT(*) FROM inventory1 WHERE player_id=@C " +
            "AND container_index=@B AND loc=@S) + " +
            "(SELECT COUNT(*) FROM inventory2 WHERE player_id=@C " +
            "AND container_index=@B AND loc=@S)",
            new { C = characterId, B = bagId, S = slot }, tx);
        if (occupied > 0)
        {
            await tx.RollbackAsync();
            return false;
        }
        foreach (var table in new[] { "equipment1", "equipment2" })
        {
            var row = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@G AND loc=@L RETURNING *",
                new { C = characterId, G = equipGroup, L = equipLoc }, tx);
            if (row == null) continue;
            var d = (IDictionary<string, object?>)row;
            await connection.ExecuteAsync(@"
                INSERT INTO inventory1
                    (id, player_id, container_index, loc, item_id,
                     durability, cur_maxdurability, strengthen, bind,
                     maker, rank, identify, magic, embedded_amount,
                     embedded_id1, embedded_id2, embedded_id3,
                     embedded_id4, embedded_id5, embedded_id6,
                     create_time, due_date, combo_id)
                VALUES (@id, @pid, @bag, @slot, @iid, @dura, @maxd,
                    @str, @bind, @maker, @rank, @ident, @magic, @embAmt,
                    @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, @combo)",
                new
                {
                    id = d["id"], pid = characterId, bag = bagId,
                    slot, iid = d["item_id"], dura = d["durability"],
                    maxd = d["cur_maxdurability"], str = d["strengthen"],
                    bind = d["bind"], maker = d["maker"], rank = d["rank"],
                    ident = d["identify"], magic = d["magic"],
                    embAmt = d["embedded_amount"],
                    e1 = d["embedded_id1"], e2 = d["embedded_id2"],
                    e3 = d["embedded_id3"], e4 = d["embedded_id4"],
                    e5 = d["embedded_id5"], e6 = d["embedded_id6"],
                    ctime = d["create_time"], due = d["due_date"],
                    combo = d["combo_id"],
                }, tx);
            await tx.CommitAsync();
            return true;
        }
        await tx.RollbackAsync();
        return false;
    }

    /// <summary>Swap équipement↔équipement — `CItemFactory::SwitchEquip`
    /// natif : échange les locs de deux pièces (ex. 1H de la loc 7
    /// vers la 8 pour libérer la main principale). Transactionnel :
    /// DELETE … RETURNING puis réinsertion aux positions croisées,
    /// dans la table où vivait chaque ligne (equipment1/2).</summary>
    public async Task<bool> SwapEquipmentAsync(int characterId,
        int srcGroup, int srcLoc, int dstGroup, int dstLoc)
    {
        if (srcGroup == dstGroup && srcLoc == dstLoc) return true;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        const string cols =
            "(id, player_id, container_index, loc, item_id, " +
            "durability, cur_maxdurability, strengthen, bind, " +
            "maker, rank, identify, magic, embedded_amount, " +
            "embedded_id1, embedded_id2, embedded_id3, " +
            "embedded_id4, embedded_id5, embedded_id6, " +
            "create_time, due_date, combo_id)";
        IDictionary<string, object?>? src = null;
        string? srcTable = null;
        foreach (var table in new[] { "equipment1", "equipment2" })
        {
            var row = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@G AND loc=@L RETURNING *",
                new { C = characterId, G = srcGroup, L = srcLoc }, tx);
            if (row != null) { src = row; srcTable = table; break; }
        }
        if (src == null) { await tx.RollbackAsync(); return false; }
        IDictionary<string, object?>? dst = null;
        string? dstTable = srcTable;
        foreach (var table in new[] { "equipment1", "equipment2" })
        {
            var row = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@G AND loc=@L RETURNING *",
                new { C = characterId, G = dstGroup, L = dstLoc }, tx);
            if (row != null) { dst = row; dstTable = table; break; }
        }
        Task InsertAsync(string table, IDictionary<string, object?> d,
            int grp, int loc) => connection.ExecuteAsync(
            $"INSERT INTO {table} {cols} VALUES (@id, @pid, @grp, @loc, " +
            "@iid, @dura, @maxd, @str, @bind, @maker, @rank, @ident, " +
            "@magic, @embAmt, @e1, @e2, @e3, @e4, @e5, @e6, @ctime, " +
            "@due, @combo)",
            new
            {
                id = d["id"], pid = characterId, grp, loc,
                iid = d["item_id"], dura = d["durability"],
                maxd = d["cur_maxdurability"], str = d["strengthen"],
                bind = d["bind"], maker = d["maker"], rank = d["rank"],
                ident = d["identify"], magic = d["magic"],
                embAmt = d["embedded_amount"],
                e1 = d["embedded_id1"], e2 = d["embedded_id2"],
                e3 = d["embedded_id3"], e4 = d["embedded_id4"],
                e5 = d["embedded_id5"], e6 = d["embedded_id6"],
                ctime = d["create_time"], due = d["due_date"],
                combo = d["combo_id"],
            }, tx);
        await InsertAsync(srcTable!, src, dstGroup, dstLoc);
        if (dst != null)
            await InsertAsync(dstTable!, dst, srcGroup, srcLoc);
        await tx.CommitAsync();
        return true;
    }

    /// <summary>Items du conteneur d'équipement familier — table native
    /// <c>elf_container</c> (même schéma item que inventory1, plus
    /// <c>elf_id</c>). <c>container_index</c> y porte le <b>loc elf</b>
    /// (index dans le vecteur elf char+0x8ac — désasm
    /// <c>CItemFactory::ElfEquip</c> @0x808cb20 :
    /// <c>GetContainerIndex()</c> du CElfContainer indexe le vecteur
    /// d'elfs) ; <c>loc</c> = slot 0-3 (0-1 = kinds arme 7/19,
    /// 2-3 = accessoires 10/34). Mappé en ItemEntity avec BagId =
    /// loc elf et Slot = slot du conteneur.</summary>
    public async Task<IReadOnlyList<ItemEntity>>
        GetElfContainerItemsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory",
                "FROM elf_container") + @"
            WHERE player_id = @CharacterId
            ORDER BY container_index, loc",
            new { CharacterId = characterId });
        return rows.Select(r =>
        {
            var e = ToEntity(r);
            e.IsEquipped = true;
            return e;
        }).ToList();
    }

    /// <summary>Colonnes communes des INSERT item natifs — mêmes 24
    /// champs que les moves equipment1 (RETURNING * → réinsertion).</summary>
    private const string ElfItemCols =
        "(id, player_id, container_index, loc, item_id, " +
        "durability, cur_maxdurability, strengthen, bind, " +
        "maker, rank, identify, magic, embedded_amount, " +
        "embedded_id1, embedded_id2, embedded_id3, " +
        "embedded_id4, embedded_id5, embedded_id6, " +
        "create_time, due_date, combo_id)";

    private static object ElfItemParams(IDictionary<string, object?> d,
        int characterId, int containerIndex, int loc,
        int elfId = -1) => new
    {
        elfId,
        id = d["id"], pid = characterId, grp = containerIndex, loc,
        iid = d["item_id"], dura = d["durability"],
        maxd = d["cur_maxdurability"], str = d["strengthen"],
        bind = d["bind"], maker = d["maker"], rank = d["rank"],
        ident = d["identify"], magic = d["magic"],
        embAmt = d["embedded_amount"],
        e1 = d["embedded_id1"], e2 = d["embedded_id2"],
        e3 = d["embedded_id3"], e4 = d["embedded_id4"],
        e5 = d["embedded_id5"], e6 = d["embedded_id6"],
        ctime = d["create_time"], due = d["due_date"],
        combo = d["combo_id"],
    };

    /// <summary>Équipe un item du sac sur un familier — miroir de
    /// <c>CItemFactory::ElfEquip</c> @0x808cb20 : la ligne quitte
    /// inventory1/2 vers elf_container ; si le slot dst est occupé
    /// l'occupant revient dans le slot sac libéré (PutIn croisé
    /// natif). Transactionnel — un échec ne perd pas l'item.</summary>
    /// <param name="elfId">elf_id natif de l'elf cible (clé de la
    /// ligne elf_container).</param>
    /// <param name="elfLoc">loc de l'elf (container_index).</param>
    /// <param name="elfSlot">slot 0-3 du conteneur elf.</param>
    public async Task<bool> MoveToElfContainerAsync(int characterId,
        int bagId, int slot, int elfId, int elfLoc, int elfSlot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            var row = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@B AND loc=@S RETURNING *",
                new { C = characterId, B = bagId, S = slot }, tx);
            if (row == null) continue;
            var d = (IDictionary<string, object?>)row;

            // Occupant du slot elf → repart dans le slot sac libéré
            // (swap PutIn natif — même transaction).
            var occ = await connection.QueryFirstOrDefaultAsync(
                "DELETE FROM elf_container WHERE player_id=@C AND " +
                "container_index=@E AND loc=@L RETURNING *",
                new { C = characterId, E = elfLoc, L = elfSlot }, tx);
            if (occ != null)
            {
                var o = (IDictionary<string, object?>)occ;
                await connection.ExecuteAsync(
                    $"INSERT INTO inventory1 {ElfItemCols} VALUES " +
                    "(@id, @pid, @grp, @loc, @iid, @dura, @maxd, @str, " +
                    "@bind, @maker, @rank, @ident, @magic, @embAmt, " +
                    "@e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, @combo)",
                    ElfItemParams(o, characterId, bagId, slot), tx);
            }
            await connection.ExecuteAsync(
                $"INSERT INTO elf_container (elf_id, " +
                ElfItemCols.TrimStart('(').Replace(")", "") +
                ") VALUES (@elfId, @id, @pid, @grp, @loc, @iid, @dura, " +
                "@maxd, @str, @bind, @maker, @rank, @ident, @magic, " +
                "@embAmt, @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, " +
                "@combo)",
                ElfItemParams(d, characterId, elfLoc, elfSlot,
                    (int)elfId), tx);
            await tx.CommitAsync();
            return true;
        }
        await tx.RollbackAsync();
        return false;
    }

    /// <summary>Déséquipe un item de familier vers le sac —
    /// <c>CItemFactory::ElfEquip</c> en sens inverse : l'occupant du
    /// slot sac destination repart dans le slot elf libéré.</summary>
    public async Task<bool> MoveFromElfContainerAsync(int characterId,
        int elfLoc, int elfSlot, int bagId, int bagSlot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "DELETE FROM elf_container WHERE player_id=@C AND " +
            "container_index=@E AND loc=@S RETURNING *",
            new { C = characterId, E = elfLoc, S = elfSlot }, tx);
        if (row == null)
        {
            await tx.RollbackAsync();
            return false;
        }
        var d = (IDictionary<string, object?>)row;
        // Occupant du slot sac dst → slot elf libéré (swap PutIn).
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            var occ = await connection.QueryFirstOrDefaultAsync(
                $"DELETE FROM {table} WHERE player_id=@C AND " +
                "container_index=@B AND loc=@S RETURNING *",
                new { C = characterId, B = bagId, S = bagSlot }, tx);
            if (occ == null) continue;
            var o = (IDictionary<string, object?>)occ;
            var p = ElfItemParams(o, characterId, elfLoc, elfSlot);
            await connection.ExecuteAsync(
                $"INSERT INTO elf_container (elf_id, " +
                ElfItemCols.TrimStart('(').Replace(")", "") +
                ") VALUES (@elfId, @id, @pid, @grp, @loc, @iid, @dura, " +
                "@maxd, @str, @bind, @maker, @rank, @ident, @magic, " +
                "@embAmt, @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, " +
                "@combo)",
                ElfItemParams(o, characterId, elfLoc, elfSlot,
                    (int)o["elf_id"]!), tx);
            break;
        }
        await connection.ExecuteAsync(
            $"INSERT INTO inventory1 {ElfItemCols} VALUES " +
            "(@id, @pid, @grp, @loc, @iid, @dura, @maxd, @str, " +
            "@bind, @maker, @rank, @ident, @magic, @embAmt, " +
            "@e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, @combo)",
            ElfItemParams(d, characterId, bagId, bagSlot), tx);
        await tx.CommitAsync();
        return true;
    }

    /// <summary>Swap de deux slots dans le conteneur elf —
    /// <c>CItemFactory::ElfSwitchEquip</c> @0x808d700 : échange
    /// adjacent dans le MÊME conteneur (natif : conteneur identique
    /// requis, slots voisins 0↔1 ou 2↔3 uniquement).</summary>
    public async Task<bool> SwitchElfContainerSlotsAsync(int characterId,
        int elfLoc, int slotA, int slotB)
    {
        if (slotA == slotB) return true;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var a = await connection.QueryFirstOrDefaultAsync(
            "DELETE FROM elf_container WHERE player_id=@C AND " +
            "container_index=@E AND loc=@S RETURNING *",
            new { C = characterId, E = elfLoc, S = slotA }, tx);
        var b = await connection.QueryFirstOrDefaultAsync(
            "DELETE FROM elf_container WHERE player_id=@C AND " +
            "container_index=@E AND loc=@S RETURNING *",
            new { C = characterId, E = elfLoc, S = slotB }, tx);
        if (a == null && b == null)
        {
            await tx.RollbackAsync();
            return false;
        }
        Task InsAsync(IDictionary<string, object?> d, int loc,
            int elfId) => connection.ExecuteAsync(
            $"INSERT INTO elf_container (elf_id, " +
            ElfItemCols.TrimStart('(').Replace(")", "") +
            ") VALUES (@elfId, @id, @pid, @grp, @loc, @iid, @dura, " +
            "@maxd, @str, @bind, @maker, @rank, @ident, @magic, " +
            "@embAmt, @e1, @e2, @e3, @e4, @e5, @e6, @ctime, @due, " +
            "@combo)",
            ElfItemParams(d, characterId, elfLoc, loc, elfId), tx);
        if (a != null)
            await InsAsync((IDictionary<string, object?>)a, slotB,
                (int)((IDictionary<string, object?>)a)["elf_id"]!);
        if (b != null)
            await InsAsync((IDictionary<string, object?>)b, slotA,
                (int)((IDictionary<string, object?>)b)["elf_id"]!);
        await tx.CommitAsync();
        return true;
    }

    /// <summary>Met à jour due_date d'un item (serial `id` partagé
    /// entre les tables items) — rattrapage des items à durée créés
    /// avant le support de due_date.</summary>
    public async Task UpdateDueDateAsync(int itemSerial, int dueDate)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        foreach (var table in new[] { "inventory1", "inventory2",
            "equipment1", "equipment2" })
            await connection.ExecuteAsync(
                $"UPDATE {table} SET due_date=@D WHERE id=@Id",
                new { D = dueDate, Id = itemSerial });
    }

    /// <summary>Supprime tout le conteneur d'équipement d'un elf —
    /// équivalent natif de la destruction du nœud <c>elf_container</c>
    /// dans <c>CKFElfManager::ReturnElf</c> (@0x80796xx : le nœud de
    /// la map char+0x544 clé=loc est effacé quand l'elf est consommé
    /// par la fusion). CanUpgrade garantit déjà que le conteneur est
    /// vide — le delete est défensif.</summary>
    public async Task DeleteElfContainerAsync(int characterId,
        int elfLoc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM elf_container WHERE player_id=@C AND " +
            "container_index=@L",
            new { C = characterId, L = elfLoc });
    }

    /// <summary>Items portés (équipement) — vue native `equipment`
    /// (UNION equipment1/2) : (loc, item_id). Plusieurs lignes par loc
    /// = buffers de save non permutés ; l'appelant déduplique.</summary>
    public async Task<IReadOnlyList<(int Loc, int ItemId)>>
        GetEquippedItemsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<(int loc, int iid)>(
            "SELECT loc, item_id AS iid FROM equipment " +
            "WHERE player_id=@CharacterId AND container_index = 0 " +
            "ORDER BY loc",
            new { CharacterId = characterId });
        return rows.Select(r => (r.loc, r.iid)).ToList();
    }

    /// <summary>Items portés avec entités complètes (durabilité, bind,
    /// enchant) — alimente le 282 ClientUpdateEquipment. `Slot` porte le
    /// loc d'équipement, `IsEquipped` est vrai.</summary>
    public async Task<IReadOnlyList<ItemEntity>>
        GetEquippedItemEntitiesAsync(int characterId)
    {
        return await GetEquipmentEntitiesAsync(characterId, 0);
    }

    /// <summary>TOUTES les lignes de la vue `equipment` (tous
    /// conteneurs 0..5 confondus) — nécessaire au rebuild complet du
    /// 311 dont le corps itère les 6 clés de la map char+0x4fc.</summary>
    public async Task<IReadOnlyList<ItemEntity>>
        GetAllEquipmentEntitiesAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory", "FROM equipment") + @"
            WHERE player_id = @CharacterId ORDER BY container_index, loc",
            new { CharacterId = characterId });
        return rows.Select(r =>
            {
                var e = ToEntity(r);
                e.IsEquipped = true;
                return e;
            }).ToList();
    }

    /// <summary>Items d'un conteneur d'équipement (`container_index`
    /// dans la vue `equipment`) — 0 = set principal, 1 = 2ᵉ set
    /// (alimente le 571 PVPRankLookEquip, qui émet les deux).</summary>
    public async Task<IReadOnlyList<ItemEntity>>
        GetEquipmentEntitiesAsync(int characterId, int containerIndex)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<NativeItemRow>(
            LiveSelect.Replace("FROM inventory", "FROM equipment") + @"
            WHERE player_id = @CharacterId AND container_index = @Bag
            ORDER BY loc",
            new { CharacterId = characterId, Bag = containerIndex });
        return rows.Select(r =>
            {
                var e = ToEntity(r);
                e.IsEquipped = true;
                return e;
            }).ToList();
    }

    /// <summary>Colonnes des tables items natives (53, ordre
    /// information_schema — identique sur inventory1/2, bags,
    /// storage1/2). Utilisé pour les INSERT ... SELECT inter-tables
    /// avec container_index/loc surchargés dès le SELECT : insérer
    /// `SELECT *` puis UPDATE créerait un état intermédiaire violant
    /// l'index unique (player_id, container_index, loc) quand le
    /// couple (srcBag,srcSlot) existe déjà dans la table cible.</summary>
    private const string ItemColumns =
        "id, item_id, durability, maker, rank, identify, magic, " +
        "embedded_amount, embedded_id1, embedded_id2, embedded_id3, " +
        "embedded_id4, embedded_id5, embedded_id6, create_time, " +
        "due_date, container_index, combo_id, extra_combo_id1, " +
        "extra_combo_id2, extra_combo_id3, extra_combo_id4, " +
        "extra_combo_id5, extra_combo_id6, strengthen, " +
        "cur_maxdurability, bind, player_id, loc, witchcraft, " +
        "extra_combo_id7, extra_combo_id8, extra_combo_id9, quality, " +
        "plus_parameter, color, train_level, train_exp, " +
        "train_slot1_type, train_slot1_level, train_slot2_type, " +
        "train_slot2_level, train_slot3_type, train_slot3_level, " +
        "extra_combo_id10, extra_combo_id11, gold_enchant, " +
        "magicbox_enchant1, magicbox_enchant2, magicbox_enchant3, " +
        "magicbox_energy, magicbox_starlevel, unbind";

    /// <summary>Même liste que <see cref="ItemColumns"/> avec
    /// container_index→@H et loc→@L remplacés par paramètres.</summary>
    private const string ItemSelectWithLoc =
        "id, item_id, durability, maker, rank, identify, magic, " +
        "embedded_amount, embedded_id1, embedded_id2, embedded_id3, " +
        "embedded_id4, embedded_id5, embedded_id6, create_time, " +
        "due_date, @H, combo_id, extra_combo_id1, " +
        "extra_combo_id2, extra_combo_id3, extra_combo_id4, " +
        "extra_combo_id5, extra_combo_id6, strengthen, " +
        "cur_maxdurability, bind, player_id, @L, witchcraft, " +
        "extra_combo_id7, extra_combo_id8, extra_combo_id9, quality, " +
        "plus_parameter, color, train_level, train_exp, " +
        "train_slot1_type, train_slot1_level, train_slot2_type, " +
        "train_slot2_level, train_slot3_type, train_slot3_level, " +
        "extra_combo_id10, extra_combo_id11, gold_enchant, " +
        "magicbox_enchant1, magicbox_enchant2, magicbox_enchant3, " +
        "magicbox_energy, magicbox_starlevel, unbind";










    /// <summary>Ligne brute de la vue `inventory` (schéma natif).</summary>
    private sealed class NativeItemRow
    {
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public int Loc { get; set; }
        public int ItemId { get; set; }
        public int Durability { get; set; }
        public int CurMaxDurability { get; set; }
        public int Strengthen { get; set; }
        public int Bind { get; set; }
        public int CreateTime { get; set; }
        public int ContainerIndex { get; set; }
        public int DueDate { get; set; }
        /// <summary>Colonne `quality` native (clone+0x2c) — couleur
        /// d'instance (0 = qualité de base S_Item col19).</summary>
        public int Quality { get; set; }
        /// <summary>Colonne `magic` (clone+0x6c) — bucket qualité
        /// 0x10 de la boîte magique quand ≠0.</summary>
        public int Magic { get; set; }
        /// <summary>Combos boîte magique (clone+0x70..+0x74 —
        /// magicbox_enchant1-3).</summary>
        public int MagicboxEnchant1 { get; set; }
        public int MagicboxEnchant2 { get; set; }
        public int MagicboxEnchant3 { get; set; }
        /// <summary>Énergie/niveau d'étoile (clone+0x7c/+0x7e).</summary>
        public int MagicboxEnergy { get; set; }
        public int MagicboxStarlevel { get; set; }
        /// <summary>Sockets ouverts (clone+0x24 bits 0-2) + gemmes
        /// incrustées (clone+0x40 — 0 = socket vide, −1 = non ouverte).
        /// </summary>
        public int EmbeddedAmount { get; set; }
        public int EmbeddedId1 { get; set; }
        public int EmbeddedId2 { get; set; }
        public int EmbeddedId3 { get; set; }
        public int EmbeddedId4 { get; set; }
        public int EmbeddedId5 { get; set; }
        public int EmbeddedId6 { get; set; }
        /// <summary>combo_id natif (clone+0x10) — produits player_works.</summary>
        public int ComboId { get; set; }
        /// <summary>Flag maudit (colonne `witchcraft`).</summary>
        public int Witchcraft { get; set; }
        /// <summary>identify — flag d'identification (colonne native,
        /// 1 = identifié).</summary>
        public int Identify { get; set; }
        /// <summary>extra_combo_id1..9 (clone+0x34 — 9 des 11 natifs).</summary>
        public int ExtraComboId1 { get; set; }
        public int ExtraComboId2 { get; set; }
        public int ExtraComboId3 { get; set; }
        public int ExtraComboId4 { get; set; }
        public int ExtraComboId5 { get; set; }
        public int ExtraComboId6 { get; set; }
        public int ExtraComboId7 { get; set; }
        public int ExtraComboId8 { get; set; }
        public int ExtraComboId9 { get; set; }
        /// <summary>extra_combo_id10/11 (queue fixe du record item).</summary>
        public int ExtraComboId10 { get; set; }
        public int ExtraComboId11 { get; set; }
        /// <summary>gold_enchant natif (clone+0x64 queue fixe).</summary>
        public int GoldEnchant { get; set; }
        /// <summary>plus_parameter natif (clone+0x2e).</summary>
        public int PlusParameter { get; set; }
        /// <summary>color natif (clone+0x50 i64 — bit21 du mask).</summary>
        public long Color { get; set; }
        /// <summary>CEquipTrainAttrs natif (clone+0x58 : train_level,
        /// +0x5c : train_exp, +0x60/62/64 : types, +0x66/68/6a :
        /// niveaux de slot).</summary>
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
