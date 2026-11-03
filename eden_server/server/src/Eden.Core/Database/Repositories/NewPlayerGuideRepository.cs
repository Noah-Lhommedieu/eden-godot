using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Accès à `new_player_guide` — compteurs du tutoriel
/// par perso : colonnes `id_%02d_trigger` / `id_%02d_closed`
/// (ids 1..20), cibles des `UPDATE new_player_guide SET %s = %d`
/// émis par TriggerNewPlayerGuide/CloseNewPlayerGuide natifs
/// (zone @0x8422fe0/@0x84240b0 → commande DBA
/// "NewPlayerGuide ChID=… MissionID=… Trigger=… Closed=…").</summary>
public sealed class NewPlayerGuideRepository
{
    private readonly NpgsqlDataSource _db;

    public NewPlayerGuideRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Charge les deux maps {id → count} (trigger et
    /// closed) — seules les ids 1..20 ont une colonne native.</summary>
    public async Task<(SortedDictionary<ushort, ushort> Triggers,
        SortedDictionary<ushort, ushort> Closed)> LoadAsync(
        int playerId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM new_player_guide WHERE player_id=@Id",
            new { Id = playerId });
        var triggers = new SortedDictionary<ushort, ushort>();
        var closed = new SortedDictionary<ushort, ushort>();
        if (row != null)
        {
            var d = (IDictionary<string, object?>)row;
            for (ushort id = 1; id <= 20; id++)
            {
                var t = $"id_{id:d2}_trigger";
                var c = $"id_{id:d2}_closed";
                if (d.TryGetValue(t, out var tv) && tv != null
                    && Convert.ToInt32(tv) != 0)
                    triggers[id] = (ushort)Convert.ToInt32(tv);
                if (d.TryGetValue(c, out var cv) && cv != null
                    && Convert.ToInt32(cv) != 0)
                    closed[id] = (ushort)Convert.ToInt32(cv);
            }
        }
        return (triggers, closed);
    }

    /// <summary>Persiste un champ trigger ou closed —
    /// `UPDATE new_player_guide SET id_%02d_[trigger|closed] = %d`
    /// (le nom de colonne est assemblé depuis l'id entière, jamais
    /// d'entrée utilisateur).</summary>
    public async Task SetFieldAsync(int playerId, ushort guideId,
        bool closed, int value)
    {
        if (guideId is < 1 or > 20) return;
        var col = $"id_{guideId:d2}_{(closed ? "closed" : "trigger")}";
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        var n = await connection.ExecuteAsync(
            $"UPDATE new_player_guide SET {col} = @V " +
            "WHERE player_id = @Id",
            new { V = value, Id = playerId });
        if (n == 0)
            await connection.ExecuteAsync(
                $"INSERT INTO new_player_guide (player_id, {col}) " +
                "VALUES (@Id, @V) ON CONFLICT (player_id) DO NOTHING",
                new { V = value, Id = playerId });
    }
}
