using Dapper;
using Limitless.Api.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Limitless.Api.Services;

/// <summary>Classements — Top PvP / PvE / Succès / Exp Ultime
/// depuis les tables du jeu. Requêtes ad-hoc contre fndb1, cache
/// mémoire 30 s (les tops ne changent pas à la seconde). Repli
/// gracieux si une table manque.</summary>
public sealed class RankingsService
{
    private readonly Db _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public RankingsService(Db db, IMemoryCache cache)
    { _db = db; _cache = cache; }

    /// <summary>Une ligne de classement — `Value` porte la métrique
    /// du board (niveau, points PvP, points succès, exp ultime) ;
    /// `ClassId`/`Level` alimentent l'icône de classe et le contexte.</summary>
    public record RankRow(int Rank, string Name, long Value,
        int ClassId, int Level);

    /// <summary>DTO de lecture Dapper (Rank posé après tri).</summary>
    private record RowDto(string Name, long Value, int ClassId,
        int Level);

    private Task<IReadOnlyList<RankRow>> Cached(string key,
        Func<Task<IReadOnlyList<RankRow>>> load) =>
        _cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            return await load();
        })!;

    /// <summary>Top exp Personnage (player_characters : level puis
    /// exp du niveau courant). Les comptes outillage [GM]* sont
    /// exclus du classement public.</summary>
    public Task<IReadOnlyList<RankRow>> TopLevelAsync(int n = 50) =>
        Cached("rank:level:" + n, async () =>
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<RowDto>(
                "SELECT given_name AS Name, exp AS Value, " +
                "class_id AS ClassId, level AS Level " +
                "FROM player_characters " +
                "WHERE given_name NOT LIKE '[GM]%' " +
                "ORDER BY level DESC, exp DESC LIMIT @n", new { n });
            return rows.Select((r, i) =>
                new RankRow(i + 1, r.Name, r.Value, r.ClassId, r.Level))
                .ToList();
        });

    /// <summary>Top exp de Classe (player_classes : class_level puis
    /// class_exp). RankRow.ClassId = la classe classée (icône),
    /// Level = niveau de classe — distinct de l'exp Personnage.</summary>
    public Task<IReadOnlyList<RankRow>> TopClassExpAsync(int n = 50) =>
        Cached("rank:class:" + n, async () =>
        {
            using var db = _db.Game();
            var rows = await db.QueryAsync<RowDto>(
                "SELECT c.given_name AS Name, pc.class_exp AS Value, " +
                "pc.class_id AS ClassId, pc.class_level AS Level " +
                "FROM player_classes pc " +
                "JOIN player_characters c ON c.id = pc.character_id " +
                "WHERE c.given_name NOT LIKE '[GM]%' " +
                "ORDER BY pc.class_level DESC, pc.class_exp DESC " +
                "LIMIT @n", new { n });
            return rows.Select((r, i) =>
                new RankRow(i + 1, r.Name, r.Value, r.ClassId, r.Level))
                .ToList();
        });

    /// <summary>Top Succès — player_achievement_rank.total_point
    /// (table native des succès).</summary>
    public Task<IReadOnlyList<RankRow>> TopAchievementsAsync(int n = 50) =>
        Cached("rank:ach:" + n, async () =>
        {
            try
            {
                using var db = _db.Game();
                var rows = await db.QueryAsync<RowDto>(
                    "SELECT c.given_name AS Name, " +
                    "r.total_point AS Value, c.class_id AS ClassId, " +
                    "c.level AS Level " +
                    "FROM player_achievement_rank r " +
                    "JOIN player_characters c ON c.id = r.player_id " +
                    "WHERE c.given_name NOT LIKE '[GM]%' " +
                    "ORDER BY r.total_point DESC LIMIT @n", new { n });
                return rows.Select((r, i) =>
                    new RankRow(i + 1, r.Name, r.Value, r.ClassId,
                        r.Level)).ToList();
            }
            catch { return Array.Empty<RankRow>(); }
        });

    /// <summary>Top PvP — points de colisée natifs
    /// (`player_characters.colosseum_point`).</summary>
    public Task<IReadOnlyList<RankRow>> TopPvpAsync(int n = 50) =>
        Cached("rank:pvp:" + n, async () =>
        {
            try
            {
                using var db = _db.Game();
                var rows = await db.QueryAsync<RowDto>(
                    "SELECT given_name AS Name, " +
                    "colosseum_point AS Value, class_id AS ClassId, " +
                    "level AS Level " +
                    "FROM player_characters " +
                    "WHERE colosseum_point > 0 " +
                    "AND given_name NOT LIKE '[GM]%' " +
                    "ORDER BY colosseum_point DESC LIMIT @n", new { n });
                return rows.Select((r, i) =>
                    new RankRow(i + 1, r.Name, r.Value, r.ClassId,
                        r.Level)).ToList();
            }
            catch { return Array.Empty<RankRow>(); }
        });

    /// <summary>Top Exp Ultime (chantier 3) — table optionnelle.</summary>
    public Task<IReadOnlyList<RankRow>> TopLimitlessAsync(int n = 50) =>
        Cached("rank:ll:" + n, async () =>
        {
            try
            {
                using var db = _db.Game();
                var rows = await db.QueryAsync<RowDto>(
                    "SELECT c.given_name AS Name, l.exp AS Value, " +
                    "c.class_id AS ClassId, l.level AS Level " +
                    "FROM player_limitless l JOIN player_characters c " +
                    "  ON c.id = l.player_id " +
                    "WHERE c.given_name NOT LIKE '[GM]%' " +
                    "ORDER BY l.level DESC, l.exp DESC LIMIT @n",
                    new { n });
                return rows.Select((r, i) =>
                    new RankRow(i + 1, r.Name, r.Value, r.ClassId,
                        r.Level)).ToList();
            }
            catch { return Array.Empty<RankRow>(); } // table absente
        });
}
