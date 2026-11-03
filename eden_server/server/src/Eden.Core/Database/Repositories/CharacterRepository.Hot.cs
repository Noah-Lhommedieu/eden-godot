using Dapper;
using Eden.Core.Logging;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Flush Redis → PostgreSQL des champs chauds (DB-3) : or (delta), position,
/// vitals. Appelé périodiquement par <c>CharacterWriteBehind</c> (~15 s) et
/// à la déconnexion. Un lease <c>SET NX PX</c> par perso garantit un seul
/// flusheur à la fois même avec plusieurs zones.
/// </summary>
public sealed partial class CharacterRepository
{
    private static readonly TimeSpan FlushLeaseTtl = TimeSpan.FromSeconds(20);

    /// <summary>Écrit en PG ce qui est sale en Redis pour ce perso. No-op sans Redis.
    /// Si PG échoue, le snapshot est réinjecté dans Redis puis l'exception remonte.</summary>
    public async Task FlushHotAsync(int characterId)
    {
        if (Hot is null) return;
        var owner = Guid.NewGuid().ToString("N");
        var lease = $"flush:{characterId}";
        var got = false;
        for (var i = 0; i < 5 && !(got = await Hot.TryAcquireAsync(lease, owner, FlushLeaseTtl)); i++)
            await Task.Delay(40);
        if (!got) return; // un autre flusheur travaille déjà sur ce perso
        try
        {
            var snap = await Hot.TakeAsync(characterId);
            if (snap.IsEmpty) { await Hot.AckAsync(characterId, null); return; }
            try
            {
                long? goldPg = await WriteSnapshotAsync(characterId, snap);
                await Hot.AckAsync(characterId, goldPg);
            }
            catch
            {
                await Hot.RestoreAsync(characterId, snap);
                throw;
            }
        }
        finally { await Hot.ReleaseAsync(lease, owner); }
    }

    /// <summary>Flush de tous les persos sales (boucle périodique). Retourne le nombre flushé.</summary>
    public async Task<int> FlushAllHotAsync()
    {
        if (Hot is null) return 0;
        var n = 0;
        foreach (var id in await Hot.DirtyIdsAsync())
        {
            try { await FlushHotAsync(id); n++; }
            catch (Exception ex)
            { EdenLog.Write($"[Hot] flush char={id} échoué (réinjecté) : {ex.Message}"); }
        }
        return n;
    }

    /// <summary>UN UPDATE : position/vitals présents + or en delta borné ; retourne le gold PG résultant.</summary>
    private async Task<long?> WriteSnapshotAsync(int id, Hot.HotSnapshot s)
    {
        var sets = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);
        if (s.Position is { } pos)
        {
            sets.Add("node_id = @n, x = @x, y = @y, z = @z, face_dir = @d");
            p.Add("n", pos.NodeId); p.Add("x", pos.X); p.Add("y", pos.Y);
            p.Add("z", pos.Z); p.Add("d", pos.Dir);
        }
        if (s.Vitals is { } v)
        {
            sets.Add("hp = @hp, mp = @mp");
            p.Add("hp", v.Hp); p.Add("mp", v.Mp);
        }
        if (s.GoldDelta != 0)
        {
            // Même clamp que les écrivains PG directs (courrier/échange/enchères).
            sets.Add("gold = LEAST(999999999, GREATEST(0, gold + @g))");
            p.Add("g", s.GoldDelta);
        }
        using var c = _db.CreateConnection();
        await c.OpenAsync();
        var sql = "UPDATE player_characters SET " + string.Join(", ", sets) +
                  " WHERE id = @id RETURNING gold::bigint";
        var gold = await c.ExecuteScalarAsync<long?>(sql, p);
        return s.GoldDelta != 0 ? gold : null;
    }
}
