using Dapper;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès collection de cartes — table native `collection`
/// (edenfr_db) : même schéma item que `inventory` moins
/// (player_id, container_index, loc) plus `account_id`
/// (= player_id du possesseur, cf. `LoadCollection` mission natif
/// `WHERE account_id = %d`). Les cartes y sont DÉPLACÉES par
/// `PutItemToCollection` — le serial `id` suit l'item, l'espace
/// est le même que les autres tables items.</summary>
public sealed partial class InventoryRepository
{
    /// <summary>Cartes actuellement déposées — paires
    /// (serial, item_id) ordonnées.</summary>
    public async Task<IReadOnlyList<(long RowId, int ItemId)>>
        GetCollectionItemsAsync(int characterId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<(long id, int iid)>(
            "SELECT id, item_id AS iid FROM collection " +
            "WHERE account_id=@C ORDER BY item_id",
            new { C = characterId });
        return rows.ToList();
    }

    /// <summary>Déplace l'item du sac vers `collection` —
    /// `PutItemToCollection` natif (zone @0x80f0000) : retrait du
    /// conteneur + INSERT dans la table collection avec
    /// account_id = player_id. Toutes les colonnes d'instance sont
    /// copiées (DELETE … RETURNING * puis INSERT dynamique —
    /// schéma identique moins les 3 colonnes de position).</summary>
    /// <returns>La ligne déplacée (id, item_id) ou null si le slot
    /// ne portait rien.</returns>
    public async Task<(long RowId, int ItemId)?>
        MoveItemToCollectionAsync(int characterId, int bagId, int slot)
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

            // Schéma `collection` = inventory − (player_id,
            // container_index, loc) + account_id — copie dynamique
            // de toutes les colonnes d'instance (magicbox, train,
            // combos étendus…).
            var p = new DynamicParameters();
            var cols = new List<string>();
            foreach (var kv in d)
            {
                if (kv.Key is "player_id" or "container_index" or "loc")
                    continue;
                cols.Add(kv.Key);
                p.Add(kv.Key, kv.Value);
            }
            cols.Add("account_id");
            p.Add("account_id", characterId);
            await connection.ExecuteAsync(
                $"INSERT INTO collection ({string.Join(", ", cols)}) " +
                $"VALUES ({string.Join(", ", cols.Select(c => "@" + c))})",
                p, tx);
            await tx.CommitAsync();
            return (Convert.ToInt64(d["id"]), Convert.ToInt32(d["item_id"]));
        }
        await tx.RollbackAsync();
        return null;
    }

    /// <summary>Rend une carte de collection au sac — inverse de
    /// <see cref="MoveItemToCollectionAsync"/> : DELETE de la ligne
    /// `collection` + INSERT dans inventory1 à (bagId, slot)
    /// (`GetItemFromCollection` natif — PutItemInInventory avec les
    /// coordonnées du premier emplacement libre résolu côté
    /// conteneur).</summary>
    /// <returns>L'item restauré (id, item_id) ou null si la carte
    /// n'appartient pas à ce perso.</returns>
    public async Task<(long RowId, int ItemId)?>
        MoveItemFromCollectionAsync(int characterId, int itemId,
            int bagId, int slot)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        // Clé native de la map de collection = item_id (une entrée
        // par type de carte — doublons refusés au dépôt, texte 0x680).
        var row = await connection.QueryFirstOrDefaultAsync(
            "DELETE FROM collection WHERE account_id=@C AND " +
            "item_id=@Iid RETURNING *",
            new { C = characterId, Iid = itemId }, tx);
        if (row == null)
        {
            await tx.RollbackAsync();
            return null;
        }
        var d = (IDictionary<string, object?>)row;
        var p = new DynamicParameters();
        var cols = new List<string>();
        foreach (var kv in d)
        {
            if (kv.Key is "account_id") continue;
            cols.Add(kv.Key);
            p.Add(kv.Key, kv.Value);
        }
        cols.Add("player_id"); p.Add("player_id", characterId);
        cols.Add("container_index"); p.Add("container_index", bagId);
        cols.Add("loc"); p.Add("loc", slot);
        await connection.ExecuteAsync(
            $"INSERT INTO inventory1 ({string.Join(", ", cols)}) " +
            $"VALUES ({string.Join(", ", cols.Select(c => "@" + c))})",
            p, tx);
        await tx.CommitAsync();
        return (Convert.ToInt64(d["id"]), Convert.ToInt32(d["item_id"]));
    }

    /// <summary>Persiste le total de points de collection — cible du
    /// <c>SavePlayerCharacter(.., "collection_point")</c> natif :
    /// <c>player_achievement_rank.collection_point</c> (la colonne
    /// n'existe que là ; la ligne est créée par le système
    /// achievement — UPDATE best-effort).</summary>
    public async Task SetCollectionPointAsync(int characterId,
        int points)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE player_achievement_rank SET collection_point=@P " +
            "WHERE player_id=@C",
            new { P = points, C = characterId });
    }
}
