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

/// <summary>Partiel sacs — capacités de bags, sacs équipés,
/// mouvements bag↔inventaire et swaps d'emplacements de sacs
/// (tables character_bags / items natives).</summary>
public sealed partial class InventoryRepository
{
    /// <inheritdoc />
    public async Task<int?> GetBagCapacity(int characterId, int bagId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        if (bagId is < 0 or > 4) return null;
        var col = $"bag_{bagId}_slots";
        return await connection.QueryFirstOrDefaultAsync<int?>(
            $"SELECT {col} FROM inventory_slots WHERE character_id=@CharacterId",
            new { CharacterId = characterId });
    }
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> GetAllBagCapacitiesAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT bag_0_slots, bag_1_slots, bag_2_slots, " +
            "bag_3_slots, bag_4_slots FROM inventory_slots " +
            "WHERE character_id=@CharacterId",
            new { CharacterId = characterId });
        var caps = new Dictionary<int, int>();
        if (row != null)
        {
            var r = (IDictionary<string, object?>)row;
            for (var i = 0; i <= 4; i++)
                caps[i] = Convert.ToInt32(r[$"bag_{i}_slots"] ?? 0);
        }
        if (!caps.ContainsKey(0) || caps[0] <= 0)
            caps[0] = 32; // sac de base toujours ouvert (capture native 312)
        return caps;
    }
    /// <inheritdoc />
    public async Task SetBagCapacityAsync(int characterId, int bagId,
        int slots)
    {
        if (bagId is < 0 or > 4) return;
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var col = $"bag_{bagId}_slots";
        await connection.ExecuteAsync(
            $"INSERT INTO inventory_slots (character_id, {col}) " +
            $"VALUES (@C, @S) ON CONFLICT (character_id) " +
            $"DO UPDATE SET {col}=@S",
            new { C = characterId, S = slots });
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<EquippedBag>> GetEquippedBagsAsync(
        int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<EquippedBag>(
            "SELECT id AS SerialId, item_id AS ItemId, " +
            "container_index AS HolderIndex, loc AS Loc, " +
            "COALESCE(maker,'') AS Maker, durability AS Durability, " +
            "identify AS Identify, bind AS Bind, due_date AS DueDate " +
            "FROM bags WHERE player_id=@CharacterId ORDER BY " +
            "container_index, loc",
            new { CharacterId = characterId });
        return rows.ToList();
    }
    /// <inheritdoc />
    public async Task<EquippedBag?> GetEquippedBagAsync(int characterId,
        int holderIndex, int holderLoc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<EquippedBag>(
            "SELECT id AS SerialId, item_id AS ItemId, " +
            "container_index AS HolderIndex, loc AS Loc, " +
            "COALESCE(maker,'') AS Maker, durability AS Durability, " +
            "identify AS Identify, bind AS Bind, due_date AS DueDate " +
            "FROM bags WHERE player_id=@C AND container_index=@H " +
            "AND loc=@L",
            new { C = characterId, H = holderIndex, L = holderLoc });
    }
    /// <inheritdoc />
    public async Task<bool> MoveItemToBagHolderAsync(int characterId,
        int srcBagId, int srcSlot, int holderIndex, int holderLoc)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = await connection.BeginTransactionAsync();
        // Serial + buffer source de l'item (ping-pong natif
        // inventory1/2 — le serial est conservé entre tables).
        var src = await connection.QueryFirstOrDefaultAsync<(int id,
            string t)?>($@"
                SELECT id, 'inventory1' AS t FROM inventory1
                WHERE player_id=@C AND container_index=@B AND loc=@S
                UNION ALL
                SELECT id, 'inventory2' AS t FROM inventory2
                WHERE player_id=@C AND container_index=@B AND loc=@S
                LIMIT 1",
            new { C = characterId, B = srcBagId, S = srcSlot }, tx);
        if (src == null)
        {
            await tx.RollbackAsync();
            return false;
        }
        // INSERT avec container_index/loc surchargés dès le SELECT —
        // un INSERT * suivi d'un UPDATE créerait un état
        // (container_index=srcBag, loc=srcSlot) qui peut violer
        // l'index unique bags(player_id,container_index,loc) si un sac
        // équipé occupe déjà ce couple (holder 0 = mêmes index qu'inv).
        await connection.ExecuteAsync(
            $"INSERT INTO bags ({ItemColumns}) " +
            $"SELECT {ItemSelectWithLoc} FROM {src.Value.t} " +
            "WHERE id=@Id ON CONFLICT (id) DO NOTHING",
            new { Id = src.Value.id, H = holderIndex, L = holderLoc },
            tx);
        await connection.ExecuteAsync(
            $"DELETE FROM {src.Value.t} WHERE id=@Id",
            new { Id = src.Value.id }, tx);
        await tx.CommitAsync();
        return true;
    }
    /// <inheritdoc />
    public async Task<bool> MoveBagToInventoryAsync(int characterId,
        int holderIndex, int holderLoc, int dstBagId, int dstSlot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = await connection.BeginTransactionAsync();
        var serial = await connection.QueryFirstOrDefaultAsync<int?>(
            "SELECT id FROM bags WHERE player_id=@C " +
            "AND container_index=@H AND loc=@L",
            new { C = characterId, H = holderIndex, L = holderLoc }, tx);
        if (serial == null)
        {
            await tx.RollbackAsync();
            return false;
        }
        // Même principe : container_index/loc surchargés dès l'INSERT
        // (index unique (player_id,container_index,loc) sur inventory1).
        await connection.ExecuteAsync(
            $"INSERT INTO inventory1 ({ItemColumns}) " +
            $"SELECT {ItemSelectWithLoc} FROM bags WHERE id=@Id " +
            "ON CONFLICT (id) DO NOTHING",
            new { Id = serial.Value, H = dstBagId, L = dstSlot }, tx);
        await connection.ExecuteAsync(
            "DELETE FROM bags WHERE id=@Id", new { Id = serial.Value },
            tx);
        await tx.CommitAsync();
        return true;
    }
    /// <inheritdoc />
    public async Task SwapInventoryBagsAsync(int characterId, int bagA,
        int bagB)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = await connection.BeginTransactionAsync();
        // index unique (player_id,container_index,loc) → via un
        // container temporaire -1.
        foreach (var table in new[] { "inventory1", "inventory2" })
        {
            await connection.ExecuteAsync(
                $"UPDATE {table} SET container_index=-1 " +
                "WHERE player_id=@C AND container_index=@A",
                new { C = characterId, A = bagA }, tx);
            await connection.ExecuteAsync(
                $"UPDATE {table} SET container_index=@A " +
                "WHERE player_id=@C AND container_index=@B",
                new { C = characterId, A = bagA, B = bagB }, tx);
            await connection.ExecuteAsync(
                $"UPDATE {table} SET container_index=@B " +
                "WHERE player_id=@C AND container_index=-1",
                new { C = characterId, B = bagB }, tx);
        }
        await tx.CommitAsync();
    }
    /// <inheritdoc />
    public async Task SwapEquippedBagLocsAsync(int characterId,
        int holderIndex, int locA, int locB)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "UPDATE bags SET loc=-1 WHERE player_id=@C " +
            "AND container_index=@H AND loc=@A",
            new { C = characterId, H = holderIndex, A = locA }, tx);
        await connection.ExecuteAsync(
            "UPDATE bags SET loc=@A WHERE player_id=@C " +
            "AND container_index=@H AND loc=@B",
            new { C = characterId, H = holderIndex, A = locA,
                B = locB }, tx);
        await connection.ExecuteAsync(
            "UPDATE bags SET loc=@B WHERE player_id=@C " +
            "AND container_index=@H AND loc=-1",
            new { C = characterId, H = holderIndex, B = locB }, tx);
        await tx.CommitAsync();
    }
}
