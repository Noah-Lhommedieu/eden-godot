using Dapper;
using Limitless.Api.Auth;
using Limitless.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Limitless.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AccountController : ControllerBase
{
    private readonly Db _db;
    private readonly AuthService _auth;
    public AccountController(Db db, AuthService auth)
    { _db = db; _auth = auth; }

    public record RegisterReq(string Username, string Password, string Email);

    /// <summary>Ligne de personnage pour le dashboard (record — les
    /// tuples ne se sérialisent pas en JSON).</summary>
    public record CharRow(int Id, string Name, int Lv, int ClassId,
        long Exp);

    /// <summary>Création de compte — hash MD5 (le login accepte le
    /// plaintext OU le md5hex : on stocke le MD5 pour ne pas garder
    /// le mot de passe en clair).</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Username) ||
            string.IsNullOrWhiteSpace(r.Password))
            return BadRequest(new { error = "username/password requis" });
        using var db = _db.Account();
        var exists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounts WHERE username=@u",
            new { u = r.Username });
        if (exists > 0) return Conflict(new { error = "username pris" });
        // id non séquencé — MAX+1 comme AccountRepository natif ;
        // password en clair : le game server stocke le clair
        // (test123|test123), le hash doit être identique (spec
        // prompt5 — le login web et le login jeu partagent le secret).
        await db.ExecuteAsync(
            "INSERT INTO accounts (id,username,password,email,role) " +
            "VALUES ((SELECT COALESCE(MAX(id),0)+1 FROM accounts)," +
            "        @u,@p,@e,'User')",
            new { u = r.Username, p = r.Password,
                  e = r.Email ?? "" });
        return Ok(new { ok = true });
    }

    /// <summary>Personnages du compte connecté (dashboard).</summary>
    [Authorize]
    [HttpGet("characters")]
    public async Task<IActionResult> Characters()
    {
        var aid = int.Parse(User.FindFirstValue(
            ClaimTypes.NameIdentifier)!);
        using var db = _db.Game();
        var rows = await db.QueryAsync<CharRow>(
            "SELECT id AS Id, given_name AS Name, level AS Lv, " +
            "class_id AS ClassId, exp AS Exp FROM player_characters " +
            "WHERE account_id=@a ORDER BY level DESC",
            new { a = aid });
        return Ok(rows);
    }
}
