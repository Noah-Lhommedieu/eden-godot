using Dapper;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Limitless.Api.Controllers;

/// <summary>Guides / tutos « comment jouer » — mini-CMS stocké dans
/// la table <c>wiki_articles</c> de <b>fnaccount</b> (décision W4 :
/// la base du portail, pas fndb1 qui appartient au game-server ;
/// la table est créée au démarrage par Program.cs — pas de migration
/// externe requise). Lecture publique (articles publiés), écriture
/// réservée au rôle Admin — même convention que AdminController.
/// Le corps est du markdown-lite (titres ##, **gras**, *italique*,
/// `code`, listes -, liens [t](u)) rendu côté frontend.</summary>
[ApiController]
[Route("api/guides")]
public sealed class GuidesController : ControllerBase
{
    private readonly Db _db;
    public GuidesController(Db db) => _db = db;

    public record GuideRow(int Id, string Slug, string Title,
        string Category, string Body, int SortOrder, bool Published,
        string Author, DateTime CreatedAt, DateTime UpdatedAt);

    public record GuideUpsert(string Title, string Category,
        string Body, int SortOrder, bool Published, string? Slug);

    /// <summary>GET /api/guides — liste publique (publiés seulement,
    /// body inclus : pages courtes, pas de pagination à ce volume).
    /// ?all=1 (admin) retourne aussi les brouillons.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool all = false)
    {
        var includeDrafts = all && User.IsInRole("Admin");
        using var db = _db.Account();
        var rows = await db.QueryAsync<GuideRow>(
            "SELECT id AS Id, slug AS Slug, title AS Title, " +
            "category AS Category, body AS Body, " +
            "sort_order AS SortOrder, published AS Published, " +
            "author AS Author, created_at AS CreatedAt, " +
            "updated_at AS UpdatedAt FROM wiki_articles " +
            (includeDrafts ? "" : "WHERE published ") +
            "ORDER BY category, sort_order, id");
        return Ok(rows);
    }

    /// <summary>GET /api/guides/{slug} — un article publié
    /// (ou brouillon pour un admin).</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug)
    {
        using var db = _db.Account();
        var row = await db.QuerySingleOrDefaultAsync<GuideRow>(
            "SELECT id AS Id, slug AS Slug, title AS Title, " +
            "category AS Category, body AS Body, " +
            "sort_order AS SortOrder, published AS Published, " +
            "author AS Author, created_at AS CreatedAt, " +
            "updated_at AS UpdatedAt FROM wiki_articles " +
            "WHERE slug = @s", new { s = slug });
        if (row == null || (!row.Published && !User.IsInRole("Admin")))
            return NotFound(new { error = "guide introuvable" });
        return Ok(row);
    }

    /// <summary>Slug sûr pour l'URL — dérivé du titre si absent.</summary>
    private static string Slugify(string s)
    {
        var t = s.ToLowerInvariant();
        t = Regex.Replace(t, "[^a-z0-9]+", "-").Trim('-');
        return t.Length == 0 ? "guide" : t[..Math.Min(t.Length, 80)];
    }

    /// <summary>POST /api/guides — création (admin). Le slug est
    /// dérivé du titre si non fourni ; suffixé si collision.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(GuideUpsert p)
    {
        if (string.IsNullOrWhiteSpace(p.Title) ||
            string.IsNullOrWhiteSpace(p.Body))
            return BadRequest(new { error = "titre/corps requis" });
        using var db = _db.Account();
        var baseSlug = Slugify(p.Slug ?? p.Title);
        var slug = baseSlug;
        for (var i = 2; await Exists(db, slug); i++)
            slug = $"{baseSlug}-{i}";
        var id = await db.ExecuteScalarAsync<int>(
            "INSERT INTO wiki_articles (slug,title,category,body," +
            "sort_order,published,author) " +
            "VALUES (@s,@t,@c,@b,@o,@p,@a) RETURNING id",
            new { s = slug, t = p.Title.Trim(),
                  c = string.IsNullOrWhiteSpace(p.Category)
                        ? "General" : p.Category.Trim(),
                  b = p.Body, o = p.SortOrder, p = p.Published,
                  a = User.Identity?.Name ?? "admin" });
        return Ok(new { id, slug });
    }

    private static async Task<bool> Exists(
        System.Data.IDbConnection db, string slug) =>
        await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM wiki_articles WHERE slug=@s",
            new { s = slug }) > 0;

    /// <summary>PUT /api/guides/{id} — édition complète (admin).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, GuideUpsert p)
    {
        if (string.IsNullOrWhiteSpace(p.Title) ||
            string.IsNullOrWhiteSpace(p.Body))
            return BadRequest(new { error = "titre/corps requis" });
        using var db = _db.Account();
        var slug = string.IsNullOrWhiteSpace(p.Slug)
            ? null : Slugify(p.Slug);
        var n = await db.ExecuteAsync(
            "UPDATE wiki_articles SET title=@t, category=@c, body=@b," +
            "sort_order=@o, published=@p, " +
            "slug=COALESCE(@s, slug), updated_at=now() WHERE id=@i",
            new { i = id, t = p.Title.Trim(),
                  c = string.IsNullOrWhiteSpace(p.Category)
                        ? "General" : p.Category.Trim(),
                  b = p.Body, o = p.SortOrder, p = p.Published,
                  s = slug });
        return n == 0
            ? NotFound(new { error = "guide introuvable" })
            : Ok(new { ok = true });
    }

    /// <summary>DELETE /api/guides/{id} — suppression (admin).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        using var db = _db.Account();
        var n = await db.ExecuteAsync(
            "DELETE FROM wiki_articles WHERE id=@i", new { i = id });
        return n == 0
            ? NotFound(new { error = "guide introuvable" })
            : Ok(new { ok = true });
    }
}
