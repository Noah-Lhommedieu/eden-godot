using System.Data;
using Dapper;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Limitless.Api.Controllers;

/// <summary>Panel admin — news + statut serveur, restreint au rôle
/// Admin (JWT claim rôle lu depuis la colonne accounts.role).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly Db _db;
    public AdminController(Db db) => _db = db;

    public record News(string Title, string Body, string Author);

    /// <summary>Statut du serveur — joueurs connectés, uptime.
    /// Public : la page d'accueil affiche le statut sans connexion.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status()
    {
        // online_user vit dans fnaccount.worlds (population mise à
        // jour par le world-server) ; les persos sont dans fndb1.
        using var acc = _db.Account();
        var online = await acc.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(online_user),0) FROM worlds");
        using var db = _db.Game();
        var total = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM player_characters");
        return Ok(new { online, total,
            now = DateTimeOffset.UtcNow });
    }

    /// <summary>News — table `news` (créée à la demande si absente).
    /// POST admin pour publier sur la page d'accueil.</summary>
    public record NewsPost(string Title, string Body);

    [HttpGet("news")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNews()
    {
        using var db = _db.Account();
        try
        {
            var rows = await db.QueryAsync(
                "SELECT id,title,body,created_at FROM news " +
                "ORDER BY created_at DESC LIMIT 50");
            return Ok(rows);
        }
        catch { return Ok(Array.Empty<object>()); }
    }

    private static async Task EnsureNewsTable(IDbConnection db) =>
        await db.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS news (id SERIAL PRIMARY KEY," +
            "title VARCHAR(200), body TEXT, author VARCHAR(60)," +
            "created_at TIMESTAMPTZ DEFAULT now(), " +
            "updated_at TIMESTAMPTZ DEFAULT now())");

    [HttpPost("news")]
    public async Task<IActionResult> PostNews(NewsPost p)
    {
        using var db = _db.Account();
        await EnsureNewsTable(db);
        var id = await db.ExecuteScalarAsync<int>(
            "INSERT INTO news (title,body,author) VALUES(@t,@b,@a) " +
            "RETURNING id",
            new { t = p.Title, b = p.Body,
                  a = User.Identity?.Name ?? "admin" });
        return Ok(new { id });
    }

    [HttpPut("news/{id:int}")]
    public async Task<IActionResult> PutNews(int id, NewsPost p)
    {
        using var db = _db.Account();
        await EnsureNewsTable(db);
        var n = await db.ExecuteAsync(
            "UPDATE news SET title=@t, body=@b, " +
            "updated_at=now() WHERE id=@id",
            new { t = p.Title, b = p.Body, id });
        return n == 0 ? NotFound() : Ok(new { ok = true });
    }

    [HttpDelete("news/{id:int}")]
    public async Task<IActionResult> DeleteNews(int id)
    {
        using var db = _db.Account();
        var n = await db.ExecuteAsync(
            "DELETE FROM news WHERE id=@id", new { id });
        return n == 0 ? NotFound() : Ok(new { ok = true });
    }

    // ---------------- Joueurs ----------------

    /// <summary>Liste des comptes + persos (admin). q = username ou
    /// nom de perso (contient).</summary>
    [HttpGet("players")]
    public async Task<IActionResult> Players([FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        using var acc = _db.Account();
        var accounts = (await acc.QueryAsync<(int Id,
            string Username, int State)>(
            "SELECT id AS Id, username AS Username, state AS State " +
            "FROM accounts " +
            (string.IsNullOrWhiteSpace(q)
                ? ""
                : "WHERE username ILIKE @q OR realname ILIKE @q ") +
            "ORDER BY id LIMIT @s OFFSET @o",
            new { q = $"%{q}%", s = Math.Clamp(size, 1, 200),
                  o = (Math.Max(page, 1) - 1) * size })).ToList();
        if (accounts.Count == 0) return Ok(Array.Empty<object>());
        using var db = _db.Game();
        var ids = accounts.Select(a => a.Id).ToArray();
        var chars = await db.QueryAsync<(int AccountId, int Id,
            string Name, int Level, int ClassId)>(
            "SELECT account_id AS AccountId, id AS Id, " +
            "given_name AS Name, level AS Level, class_id AS ClassId " +
            "FROM player_characters WHERE account_id = ANY(@ids)",
            new { ids });
        var byAcc = chars.GroupBy(c => c.AccountId)
            .ToDictionary(g => g.Key, g => (object)g.Select(
                c => new { c.Id, c.Name, c.Level,
                    c.ClassId }).ToList());
        return Ok(accounts.Select(a => new
        {
            a.Id, a.Username, state = a.State,
            characters = byAcc.TryGetValue(a.Id, out var ch)
                ? ch : new List<object>(),
        }));
    }

    /// <summary>Ban / unban d'un compte — accounts.state (le login
    /// natif lit state : ≠0 = compte refusé à la connexion).
    /// Aussi une queue web_admin_commands pour kick/mute en jeu —
    /// voir POST /api/admin/command.</summary>
    [HttpPost("players/{accountId:int}/ban")]
    public async Task<IActionResult> Ban(int accountId,
        [FromBody] BanReq? req)
    {
        using var acc = _db.Account();
        await acc.ExecuteAsync(
            "UPDATE accounts SET state=@s WHERE id=@id",
            new { s = req?.Banned == false ? 0 : -1, id = accountId });
        return Ok(new { ok = true, banned = req?.Banned != false });
    }

    public record BanReq(bool Banned = true);

    // ---------------- Terminal de commandes distant ----------------

    /// <summary>File de commandes admin — table
    /// fndb1.web_admin_commands (créée à la demande). Le serveur
    /// zone/world la dépilie et applique les commandes GM
    /// (RootCmds/GmCommandService côté serveur — TODO branchement
    /// côté game-server ; l'endpoint est stable pour le front).
    /// kind = 'gm' (commande console GM exécutée en jeu au nom du
    /// perso cible), 'kick', 'message' (broadcast).</summary>
    public record RemoteCmd(string Kind, string? Target,
        string Command);

    [HttpPost("command")]
    public async Task<IActionResult> PostCommand(RemoteCmd c)
    {
        if (string.IsNullOrWhiteSpace(c.Command))
            return BadRequest(new { error = "command requis" });
        var kind = string.IsNullOrWhiteSpace(c.Kind)
            ? "gm" : c.Kind;
        if (kind is not ("gm" or "kick" or "message"))
            return BadRequest(new { error =
                "kind ∈ gm|kick|message" });
        using var db = _db.Game();
        await db.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS web_admin_commands (" +
            "id SERIAL PRIMARY KEY, kind VARCHAR(20) NOT NULL, " +
            "target VARCHAR(60), command TEXT NOT NULL, " +
            "author VARCHAR(60), status VARCHAR(20) DEFAULT 'pending'," +
            "created_at TIMESTAMPTZ DEFAULT now(), " +
            "done_at TIMESTAMPTZ)");
        var id = await db.ExecuteScalarAsync<int>(
            "INSERT INTO web_admin_commands (kind,target,command," +
            "author) VALUES(@k,@t,@c,@a) RETURNING id",
            new { k = kind, t = c.Target, c = c.Command,
                  a = User.Identity?.Name ?? "admin" });
        return Ok(new { id, status = "pending",
            note = "consommé par le game-server quand le poller " +
                   "sera branché (TODO server-side)" });
    }

    [HttpGet("command")]
    public async Task<IActionResult> ListCommands(
        [FromQuery] string? status)
    {
        using var db = _db.Game();
        try
        {
            var rows = await db.QueryAsync(
                "SELECT id,kind,target,command,author,status," +
                "created_at,done_at FROM web_admin_commands " +
                (status == null ? "" : "WHERE status=@s ") +
                "ORDER BY id DESC LIMIT 100",
                new { s = status });
            return Ok(rows);
        }
        catch { return Ok(Array.Empty<object>()); }
    }
}
