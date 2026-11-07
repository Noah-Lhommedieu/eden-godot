using System.Text;
using System.Text.Json;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Admin;
using Eden.Core.Game.Foundation;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Session;
using Eden.Zone.Handlers;
using Npgsql;

namespace Eden.Zone.Features.Admin;

/// <summary>
/// Point de câblage de l'administration (D5/D6/D8) : crée l'unique <see cref="AdminService"/> du serveur (hôte zone, audit PostgreSQL,
/// TOTP, politique lue dans l'environnement : <c>EDEN_ADMIN_TOTP</c>, <c>EDEN_ADMIN_FOUR_EYES</c>, <c>EDEN_ADMIN_RATE</c>).
/// </summary>
public static class AdminBridge
{
    public static AdminService? Service { get; private set; }
    public static AdminRepository? Repo { get; private set; }
    private static ZoneEntryFlow? _flow;

    public static void Init(ZoneEntryFlow flow, ZoneServices services, NpgsqlDataSource db)
    {
        _flow = flow; Repo = new AdminRepository(db);
        var policy = new AdminPolicy(
            RequireTotp: Environment.GetEnvironmentVariable("EDEN_ADMIN_TOTP") == "on",
            FourEyes: Environment.GetEnvironmentVariable("EDEN_ADMIN_FOUR_EYES") == "on",
            RateMaxPerMinute: int.TryParse(Environment.GetEnvironmentVariable("EDEN_ADMIN_RATE"), out var r) ? r : 30);
        Service = new AdminService(new ZoneAdminHost(flow, services, db), Repo, Repo, GameTime.Clock, policy);
        EdenLog.Write($"[Admin] service prêt : TOTP exigé={policy.RequireTotp}, quatre yeux={policy.FourEyes}, limite={policy.RateMaxPerMinute}/min");
        AdminHttpHost.StartIfConfigured();
    }

    /// <summary>Exécute une requête JSON <c>{requestId, command, args, dryRun, totp}</c> pour un acteur déjà authentifié ; renvoie la réponse JSON.</summary>
    public static async Task<string> ExecuteJsonAsync(string json, AdminActor actor)
    {
        if (Service == null) return Reply(AdminResult.Fail("unavailable", "administration indisponible"));
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var args = root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
                ? a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString()) : new Dictionary<string, string>();
            var totp = root.TryGetProperty("totp", out var t) ? t.GetString() : actor.TotpCode;
            var req = new AdminRequest(root.TryGetProperty("requestId", out var id) ? id.GetString() ?? "" : "",
                root.GetProperty("command").GetString() ?? "", args, actor with { TotpCode = totp },
                root.TryGetProperty("dryRun", out var d) && d.GetBoolean());
            return Reply(await Service.ExecuteAsync(req));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Reply(AdminResult.Fail("bad_args", "requête invalide"));
        }
    }

    /// <summary>Commande texte « //vip compte [jours|off] » : même couche que le panneau (une seule implémentation de la règle).</summary>
    public static async Task<string> RunVipTextAsync(Guid sessionId, string[] args)
    {
        var actor = await ActorOfAsync(sessionId);
        if (actor == null || Service == null) return "administration indisponible";
        var a = new Dictionary<string, string> { ["account"] = args.Length > 0 ? args[0] : "", ["reason"] = "commande //vip" };
        if (args.Length > 1) a["days"] = args[1];
        var r = await Service.ExecuteAsync(new AdminRequest("", "vip", a, actor));
        return r.Message;
    }

    private static string Reply(AdminResult r) => JsonSerializer.Serialize(new { ok = r.Ok, message = r.Message, code = r.Code, replayed = r.Replayed });

    /// <summary>Acteur d'une session de jeu : le privilège (dérivé du compte à l'entrée) donne le niveau.</summary>
    internal static async Task<AdminActor?> ActorOfAsync(Guid sessionId)
    {
        var st = _flow?.AdminStateOf(sessionId);
        if (st == null || Repo == null) return null;
        return new AdminActor(st.AccountId, await Repo.AccountNameAsync(st.AccountId), (int)st.CharId, (AdminLevel)Math.Clamp(st.Privilege, 0, 5));
    }
}

/// <summary>Opcode 9067 C→S (custom Reborn) : requête d'administration JSON <c>[u16 len][UTF-8]</c>.</summary>
public sealed class CNE_CZ_ZoneServerAdminRpc : NetCommand
{
    public override int Opcode => 9067;
    public override string Name => "CNE_CZ_ZoneServerAdminRpc";
    public string Json { get; set; } = "";
    public override void Serialize(OutBinStream stream) { var b = Encoding.UTF8.GetBytes(Json); stream.WriteShort((short)b.Length); stream.WriteBytes(b); }
    public override void Deserialize(InBinStream stream) => Json = Encoding.UTF8.GetString(stream.ReadBytes(stream.ReadUShort()));
}

/// <summary>
/// RPC d'administration en jeu (D6). S→C 9068 : <c>[u16 len][UTF-8 JSON {ok,message,code,replayed}]</c>. Un compte sans niveau reçoit
/// « forbidden » et rien d'autre : le client n'affiche le panneau que si <c>whoami</c> répond avec un niveau.
/// </summary>
internal sealed class AdminRpcFeature : IZoneFeature
{
    public const ushort ResponseNcid = 9068;
    public string Name => "admin-rpc";

    public void Register(PacketDispatcher d, ZoneServices s) =>
        d.RegisterChar(() => new CNE_CZ_ZoneServerAdminRpc(), async (session, msg, charId, token) =>
        {
            var actor = await AdminBridge.ActorOfAsync(session.SessionId);
            var reply = actor == null || actor.Level == AdminLevel.None
                ? JsonSerializer.Serialize(new { ok = false, message = "refusé", code = "forbidden", replayed = false })
                : await AdminBridge.ExecuteJsonAsync(msg.Json, actor);
            var b = Encoding.UTF8.GetBytes(reply);
            var body = new byte[2 + b.Length]; BitConverter.TryWriteBytes(body.AsSpan(0, 2), (ushort)b.Length); b.CopyTo(body, 2);
            await session.SendPacketAsync(ResponseNcid, body, token);
        });
}
