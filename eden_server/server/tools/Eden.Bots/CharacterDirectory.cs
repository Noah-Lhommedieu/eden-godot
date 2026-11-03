using Npgsql;

namespace Eden.Bots;

/// <summary>Personnage de banc résolu depuis `edenfr_db.player_characters`
/// + son premier spellmaster réel (`player_spells`) pour les casts.</summary>
public sealed record BotCharacter(int Id, string Name, short SpellId);

public static class CharacterDirectory
{
    public static async Task<IReadOnlyList<BotCharacter>> ResolveAsync(
        string connectionString, string namePrefix, int count,
        CancellationToken ct)
    {
        await using var db = NpgsqlDataSource.Create(connectionString);
        await using var cmd = db.CreateCommand("""
            SELECT c.id,
                   c.given_name,
                   COALESCE((
                       SELECT p.spellmaster_id
                       FROM player_spells p
                       WHERE p.player_id = c.id
                       ORDER BY p.spell_order, p.spellmaster_id
                       LIMIT 1), 0) AS spell_id
            FROM player_characters c
            WHERE c.given_name LIKE @prefix
              AND c.deleted_time = 0
            ORDER BY c.given_name, c.id
            LIMIT @count;
            """);
        cmd.Parameters.AddWithValue("prefix", namePrefix + "%");
        cmd.Parameters.AddWithValue("count", count);
        var chars = new List<BotCharacter>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            chars.Add(new BotCharacter(r.GetInt32(0), r.GetString(1),
                r.GetInt16(2)));
        return chars;
    }
}
