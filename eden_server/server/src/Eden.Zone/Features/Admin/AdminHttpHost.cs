using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eden.Core.Game.Admin;
using Eden.Core.Logging;

namespace Eden.Zone.Features.Admin;

/// <summary>
/// Canal interne du site vers le serveur de jeu (loopback, signé HMAC-SHA256) : <c>POST /admin/exec</c> avec les en-têtes
/// <c>X-Eden-Time</c> (secondes Unix, tolérance 60 s) et <c>X-Eden-Sig</c> = HMAC(secret, time + "." + corps). Corps :
/// <c>{accountId, accountName, level, totp, requestId, command, args, dryRun}</c>. Désactivé tant que <c>EDEN_ADMIN_SECRET</c> n'est pas posé.
/// Le niveau est RELU en base : le site ne peut pas s'attribuer plus que <c>accounts.admin_level</c>.
/// </summary>
internal static class AdminHttpHost
{
    public static void StartIfConfigured()
    {
        var secret = Environment.GetEnvironmentVariable("EDEN_ADMIN_SECRET");
        if (string.IsNullOrEmpty(secret)) { EdenLog.Write("[Admin] canal HTTP interne désactivé (EDEN_ADMIN_SECRET absent)"); return; }
        var port = int.TryParse(Environment.GetEnvironmentVariable("EDEN_ADMIN_HTTP_PORT"), out var p) ? p : 7660;
        var l = new HttpListener(); l.Prefixes.Add($"http://127.0.0.1:{port}/admin/");
        l.Start(); EdenLog.Write($"[Admin] canal HTTP interne sur 127.0.0.1:{port}");
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var ctx = await l.GetContextAsync();
                _ = Task.Run(() => Handle(ctx, secret));
            }
        });
    }

    internal static string Sign(string secret, string time, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(time + "." + body))).ToLowerInvariant();

    internal static bool Verify(string secret, string time, string body, string sig, DateTimeOffset now) =>
        long.TryParse(time, out var t) && Math.Abs(now.ToUnixTimeSeconds() - t) <= 60
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(secret, time, body)), Encoding.ASCII.GetBytes(sig));

    private static async Task Handle(HttpListenerContext ctx, string secret)
    {
        try
        {
            using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8); var body = await sr.ReadToEndAsync();
            if (ctx.Request.HttpMethod != "POST" || !Verify(secret, ctx.Request.Headers["X-Eden-Time"] ?? "", body, ctx.Request.Headers["X-Eden-Sig"] ?? "", Eden.Core.Game.Foundation.GameTime.Clock.Now))
            { ctx.Response.StatusCode = 401; ctx.Response.Close(); return; }
            using var doc = JsonDocument.Parse(body); var r = doc.RootElement;
            var accountId = r.GetProperty("accountId").GetInt32();
            var level = AdminBridge.Repo == null ? 0 : await AdminBridge.Repo.AdminLevelAsync(accountId);     // niveau relu en base
            var actor = new AdminActor(accountId, r.TryGetProperty("accountName", out var n) ? n.GetString() ?? "" : "", 0, (AdminLevel)Math.Clamp(level, 0, 5));
            var reply = Encoding.UTF8.GetBytes(await AdminBridge.ExecuteJsonAsync(body, actor));
            ctx.Response.ContentType = "application/json"; ctx.Response.OutputStream.Write(reply); ctx.Response.Close();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or HttpListenerException or IOException)
        {
            EdenLog.Write($"[Admin] requête HTTP rejetée : {e.GetType().Name}");
            try { ctx.Response.StatusCode = 400; ctx.Response.Close(); } catch (Exception) { /* connexion déjà fermée */ }
        }
    }
}
