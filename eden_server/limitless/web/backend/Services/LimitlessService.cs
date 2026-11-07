using Dapper;
using Limitless.Api.Data;

namespace Limitless.Api.Services;

/// <summary>Exp Ultime du personnage — lit les tables chantier 3
/// (`player_limitless`, `limitless_perks`, `player_limitless_perks`,
/// `ll_level_cost`). Repli gracieux si les tables sont absentes
/// (migration non appliquée).</summary>
public sealed class LimitlessService
{
    private readonly Db _db;
    public LimitlessService(Db db) => _db = db;

    public record PerkDto(string Code, string Name, string Description,
        int RequiredLevel, double Value, string ValueKind);
    public record LimitlessState(int CharId, string Name, long Exp,
        int Level, long ExpToNext, IReadOnlyList<PerkDto> Perks);

    /// <summary>État Exp Ultime d'un perso : exp, niveau, coût du
    /// niveau suivant, perks débloqués.</summary>
    public async Task<LimitlessState?> GetAsync(int charId)
    {
        try
        {
            using var db = _db.Game();
            var pl = await db.QuerySingleOrDefaultAsync<
                (long Exp, int Lv, string Name)>(
                "SELECT l.exp AS Exp, l.level AS Lv, c.given_name AS Name " +
                "FROM player_limitless l " +
                "JOIN player_characters c ON c.id = l.player_id " +
                "WHERE l.player_id = @id", new { id = charId });
            if (pl.Name == null) return null;

            // Coût du niveau suivant (ll_level_cost du niveau+1).
            var next = await db.QuerySingleOrDefaultAsync<long?>(
                "SELECT exp_req FROM ll_level_cost WHERE level = @lv",
                new { lv = pl.Lv + 1 }) ?? 0;

            // Perks débloqués (required_level <= niveau du perso).
            var perks = await db.QueryAsync<PerkDto>(
                "SELECT p.code AS Code, p.name AS Name, " +
                "       p.description AS Description, " +
                "       p.required_level AS RequiredLevel, " +
                "       p.value AS Value, p.value_kind AS ValueKind " +
                "FROM limitless_perks p " +
                "WHERE p.required_level <= @lv " +
                "ORDER BY p.sort_order, p.required_level",
                new { lv = pl.Lv });

            return new LimitlessState(charId, pl.Name, pl.Exp, pl.Lv,
                next, perks.ToList());
        }
        catch (Exception ex) when (ex.Message.Contains("does not exist"))
        {
            return null; // tables Limitless absentes — migration non appliquée
        }
    }

    /// <summary>Catalogue complet des perks (wiki — ordre de
    /// palier). Repli gracieux si la table est absente.</summary>
    public async Task<IReadOnlyList<PerkDto>> AllPerksAsync()
    {
        try
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<PerkDto>(
                "SELECT code AS Code, name AS Name, " +
                "description AS Description, " +
                "required_level AS RequiredLevel, value AS Value, " +
                "value_kind AS ValueKind " +
                "FROM limitless_perks " +
                "ORDER BY sort_order, required_level");
            return rows.ToList();
        }
        catch { return Array.Empty<PerkDto>(); }
    }

    /// <summary>Courbe de coût par niveau Exp Ultime
    /// (ll_level_cost — wiki + jauge dashboard).</summary>
    public async Task<IReadOnlyList<object>> LevelCostsAsync()
    {
        try
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync(
                "SELECT level, exp_req FROM ll_level_cost " +
                "ORDER BY level");
            return rows.ToList();
        }
        catch { return Array.Empty<object>(); }
    }

    /// <summary>Ligne du classement Exp Ultime (record — les tuples
    /// ne se sérialisent pas en JSON).</summary>
    public record TopRow(int Rank, string Name, int Lv, long Exp);

    /// <summary>Classement Exp Ultime (niveau puis exp).</summary>
    public async Task<IReadOnlyList<TopRow>> TopAsync(int n = 50)
    {
        try
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<(string Name, int Lv, long Exp)>(
                "SELECT c.given_name AS Name, l.level AS Lv, l.exp AS Exp " +
                "FROM player_limitless l " +
                "JOIN player_characters c ON c.id = l.player_id " +
                "WHERE c.given_name NOT LIKE '[GM]%' " +
                "ORDER BY l.level DESC, l.exp DESC LIMIT @n", new { n });
            return rows.Select((r, i) => new TopRow(i + 1, r.Name, r.Lv,
                r.Exp)).ToList();
        }
        catch { return Array.Empty<TopRow>(); }
    }
}
