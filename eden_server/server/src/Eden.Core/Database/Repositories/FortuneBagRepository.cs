using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Dépôt `player_fortune_bag` — compteurs d'ouverture par compte et
/// par sac (la map `char+0x574` native : map&lt;item_id(sac),
/// s16 compteur&gt;). Équivalent de
/// `CExecutionerDBO::LoadFortuneBagCounter` mission @0x081522xx
/// (`SELECT item_id, counter FROM player_fortune_bag WHERE
/// account_id = %d`) et du TC `FortuneBag Item= %hd Counter= %hd`
/// @0x081f8fxx (UPDATE, sinon INSERT).
/// Clé : `account_id` (compte, pas perso) + `item_id` = l'id du sac.
/// La zone charge directement (notre architecture fait les lectures
/// char par dépôt) ; l'écriture passe par la commande DBA texte
/// native relayée à la mission.
/// </summary>
public sealed class FortuneBagRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Source de données du monde de jeu (edenfr_db —
    /// `player_fortune_bag` y vit avec `player_characters`).</summary>
    public FortuneBagRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Compteurs du compte — SELECT natif par account_id.
    /// Retour : map item_id(sac) → compteur (s16 natif).</summary>
    public async Task<Dictionary<ushort, short>> GetCountersAsync(
        int accountId)
    {
        const string sql = @"
            SELECT item_id AS ItemId, counter AS Counter
            FROM player_fortune_bag
            WHERE account_id = @accountId";
        await using var conn = await _db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(int ItemId, int Counter)>(
            sql, new { accountId });
        var map = new Dictionary<ushort, short>(rows.Count());
        foreach (var r in rows)
            map[(ushort)r.ItemId] = (short)r.Counter;
        return map;
    }

    /// <summary>UPSERT du compteur — UPDATE natif si la ligne
    /// (account_id,item_id) existe, INSERT sinon (unique index
    /// `player_fortune_bag_index`).</summary>
    public async Task SetCounterAsync(int accountId, int itemId,
        int counter)
    {
        const string sql = @"
            INSERT INTO player_fortune_bag (account_id, item_id, counter)
            VALUES (@accountId, @itemId, @counter)
            ON CONFLICT (account_id, item_id)
            DO UPDATE SET counter = @counter";
        await using var conn = await _db.OpenConnectionAsync();
        await conn.ExecuteAsync(sql, new { accountId, itemId, counter });
    }
}
