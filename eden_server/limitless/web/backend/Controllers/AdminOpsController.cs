using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Limitless.Api.Controllers;

/// <summary>
/// Commandes d'administration du jeu depuis le site (PLAN-ACTION-3 D5) : le site NE réimplémente rien, il signe (HMAC-SHA256) la requête
/// et la transmet au canal interne du serveur de jeu, qui applique les MÊMES commandes que le panneau en jeu (niveau relu en base, audit chaîné,
/// quatre yeux, TOTP). Configuration : <c>Admin:ZoneUrl</c> (défaut http://127.0.0.1:7660/admin/exec) et <c>Admin:Secret</c> (variable <c>Admin__Secret</c>).
/// </summary>
[ApiController]
[Route("api/admin/ops")]
[Authorize(Roles = "Admin")]
public sealed class AdminOpsController : ControllerBase
{
    private readonly IConfiguration _cfg;
    public AdminOpsController(IConfiguration cfg) => _cfg = cfg;

    public record ExecRequest(string Command, Dictionary<string, string>? Args, bool DryRun, string? Totp, string? RequestId);

    /// <summary>POST /api/admin/ops/exec — exécute une commande (give, remove, vip, players, audit, approve, 2fa…).</summary>
    [HttpPost("exec")]
    public async Task<IActionResult> Exec([FromBody] ExecRequest r)
    {
        var secret = _cfg["Admin:Secret"];
        if (string.IsNullOrEmpty(secret)) return StatusCode(503, new { error = "Admin:Secret non configuré" });
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId)) return Forbid();
        var body = JsonSerializer.Serialize(new
        {
            accountId, accountName = User.FindFirstValue(ClaimTypes.Name) ?? "", command = r.Command, args = r.Args ?? new(),
            dryRun = r.DryRun, totp = r.Totp, requestId = r.RequestId ?? "",
        });
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(time + "." + body))).ToLowerInvariant();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var req = new HttpRequestMessage(HttpMethod.Post, _cfg["Admin:ZoneUrl"] ?? "http://127.0.0.1:7660/admin/exec")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Eden-Time", time); req.Headers.Add("X-Eden-Sig", sig);
        try
        {
            using var resp = await http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            return resp.IsSuccessStatusCode ? Content(text, "application/json") : StatusCode((int)resp.StatusCode, new { error = "refusé par le serveur de jeu" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "serveur de jeu injoignable" });
        }
    }
}
