using Dapper;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Une entrée de prestige perso — <c>SPrestigeData</c> natif
/// <c>{ s16 level ; i32 exp }</c> (mission @0x0807cb90).</summary>
public readonly record struct PrestigeEntry(short Level, int Exp);

/// <summary>Accès à la table native
/// <c>prestige(player_id, prestige_id, level, exp)</c> — modèle et
/// sémantique reversés sur le binaire mission :
/// <c>CExecutionerDBO::LoadPrestige</c> @0x08158630
/// (<c>SELECT prestige_id, level, exp FROM prestige WHERE player_id=%d</c>),
/// <c>CGlobalInfo::AddPrestige</c> @0x0807cb90
/// (<c>INSERT INTO prestige (player_id, prestige_id, level, exp)</c>
/// avec level=1, exp=0),
/// <c>CGlobalInfo::GainPrestigeExp</c> @0x0808c1b0 (clamp + UPDATE),
/// <c>CGlobalInfo::SetPrestigeLevel</c> @0x08087130.</summary>
public sealed class PrestigeRepository
{
    private readonly NpgsqlDataSource _db;

    /// <summary>Cap d'exp prestige — <c>CFormula::AllFormulaParas+0x5a4</c>
    /// natif = S_Parameter #361 (valeur 75000 dans les données).</summary>
    public const int MaxExp = 75000;

    /// <summary>Le natif ne maintient qu'un niveau : tout gain remet
    /// <c>level</c> à 1 (<c>GainPrestigeExp</c> → <c>SetPrestigeLevel
    /// (…,1)</c> si level != 1, INSERT initial level=1).</summary>
    public const short NativeLevel = 1;

    public PrestigeRepository(NpgsqlDataSource db) => _db = db;

    /// <summary>Ids de prestige acceptés par <c>AddPrestige</c> natif :
    /// 1..0x26 (1-38), 0x33..0x3b (51-59), 0x51..0x55 (81-85).</summary>
    public static bool IsValidPrestigeId(int id) =>
        id is >= 1 and <= 38 or >= 51 and <= 59 or >= 81 and <= 85;

    /// <summary>Clamp natif du gain d'exp
    /// (<c>GainPrestigeExp @0x0808c1b0</c>) : exp += delta, plancher 0
    /// (et reset level via psVar[2]=psVar[3]=0 côté négatif), cap
    /// <see cref="MaxExp"/>. Retourne la nouvelle exp.</summary>
    public static int ApplyExpDelta(int current, int delta)
    {
        var next = current + delta;
        if (next < 0) return 0;
        return next > MaxExp ? MaxExp : next;
    }

    /// <summary>Toutes les entrées prestige du perso, triées par id —
    /// miroir de la map <c>exec+0x344</c> chargée par
    /// <c>LoadPrestige</c> natif (ordre std::map).</summary>
    public async Task<SortedDictionary<short, PrestigeEntry>>
        LoadAllAsync(int playerId)
    {
        await using var c = await _db.OpenConnectionAsync();
        var rows = await c.QueryAsync<(short Id, short Level, int Exp)>(
            "SELECT prestige_id AS Id, level AS Level, exp AS Exp " +
            "FROM prestige WHERE player_id = @p ORDER BY prestige_id",
            new { p = playerId });
        var map = new SortedDictionary<short, PrestigeEntry>();
        foreach (var r in rows) map[r.Id] = new PrestigeEntry(r.Level, r.Exp);
        return map;
    }

    /// <summary><c>GainPrestigeExp</c> natif : crée la ligne
    /// (level=1, exp=0) si absente, applique le delta clampé
    /// [0,<see cref="MaxExp"/>], remet level=1 et persiste.
    /// Retourne l'entrée résultante, ou null pour un id hors plage
    /// native (aucune écriture dans ce cas).</summary>
    public async Task<PrestigeEntry?> GainExpAsync(int playerId,
        short prestigeId, int delta)
    {
        if (!IsValidPrestigeId(prestigeId)) return null;
        await using var c = await _db.OpenConnectionAsync();
        var cur = await c.QueryFirstOrDefaultAsync<(short L, int E)?>(
            "SELECT level AS L, exp AS E FROM prestige " +
            "WHERE player_id = @p AND prestige_id = @r",
            new { p = playerId, r = prestigeId });
        var exp = ApplyExpDelta(cur?.E ?? 0, delta);
        await c.ExecuteAsync(
            "INSERT INTO prestige (player_id, prestige_id, level, exp) " +
            "VALUES (@p,@r,1,@e) " +
            "ON CONFLICT (player_id, prestige_id) " +
            "DO UPDATE SET level = 1, exp = @e",
            new { p = playerId, r = prestigeId, e = exp });
        return new PrestigeEntry(NativeLevel, exp);
    }

    /// <summary><c>SetPrestigeLevel</c> natif : crée la ligne si
    /// absente (level=1, exp=0) puis écrit <c>level</c>. Retourne
    /// l'entrée résultante, ou null pour un id hors plage.</summary>
    public async Task<PrestigeEntry?> SetLevelAsync(int playerId,
        short prestigeId, short level)
    {
        if (!IsValidPrestigeId(prestigeId)) return null;
        await using var c = await _db.OpenConnectionAsync();
        await c.ExecuteAsync(
            "INSERT INTO prestige (player_id, prestige_id, level, exp) " +
            "VALUES (@p,@r,@l,0) " +
            "ON CONFLICT (player_id, prestige_id) " +
            "DO UPDATE SET level = @l",
            new { p = playerId, r = prestigeId, l = level });
        var exp = await c.ExecuteScalarAsync<int>(
            "SELECT exp FROM prestige " +
            "WHERE player_id = @p AND prestige_id = @r",
            new { p = playerId, r = prestigeId });
        return new PrestigeEntry(level, exp);
    }
}
