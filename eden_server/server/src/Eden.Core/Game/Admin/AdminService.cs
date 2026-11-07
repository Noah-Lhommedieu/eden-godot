using System.Text.Json;
using Eden.Core.Game.Foundation;

namespace Eden.Core.Game.Admin;

/// <summary>Réglages de sécurité de l'administration (D8). Défauts permissifs en développement ; production : tout à true.</summary>
public sealed record AdminPolicy(bool RequireTotp = false, bool FourEyes = false, int RateMaxPerMinute = 30);

/// <summary>Secret TOTP par compte (colonne <c>account.accounts.totp_secret</c>).</summary>
public interface ITotpStore
{
    Task<string?> GetAsync(int accountId);
    Task SetAsync(int accountId, string secret);
}

/// <summary>Une commande d'administration : une classe, un niveau minimal, aucune logique d'interface.</summary>
public interface IAdminCommand
{
    string Name { get; }
    AdminLevel MinLevel { get; }
    string Help { get; }
    /// <summary>Action de masse (plusieurs joueurs) : règle des quatre yeux et sauvegarde préalable.</summary>
    bool IsMass(AdminRequest r) => false;
    Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext ctx);
}

/// <summary>Services passés aux commandes.</summary>
public sealed record AdminContext(IAdminHost Host, IAdminAuditStore Audit, ITotpStore Totp, IGameClock Clock, AdminService Service);

/// <summary>
/// Couche unique des commandes d'administration (PLAN-ACTION-3 D5) : le panneau en jeu, le site et la console passent par
/// <see cref="ExecuteAsync"/>. Ordre des garde-fous : commande connue → niveau → limite de débit → idempotence → TOTP →
/// quatre yeux (actions de masse) → sauvegarde de masse → exécution → audit chaîné.
/// </summary>
public sealed class AdminService
{
    private readonly Dictionary<string, IAdminCommand> _commands;
    private readonly AdminContext _ctx;
    private readonly AdminPolicy _policy;
    private readonly AdminRateLimiter _limiter;
    private readonly Dictionary<long, (AdminRequest Request, DateTimeOffset At)> _pending = new();
    private long _nextApproval = 1;
    private readonly object _lock = new();

    public AdminService(IAdminHost host, IAdminAuditStore audit, ITotpStore totp, IGameClock clock, AdminPolicy policy,
        IEnumerable<IAdminCommand>? commands = null)
    {
        _ctx = new AdminContext(host, audit, totp, clock, this);
        _policy = policy; _limiter = new AdminRateLimiter(policy.RateMaxPerMinute);
        _commands = (commands ?? AdminCommands.Default).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAdminCommand> Commands => _commands.Values;
    public AdminPolicy Policy => _policy;

    public async Task<AdminResult> ExecuteAsync(AdminRequest r)
    {
        var now = _ctx.Clock.Now;
        if (!_commands.TryGetValue(r.Command, out var cmd))
            return await Finish(r, AdminResult.Fail("unknown_command", $"commande inconnue : {r.Command}"), now);
        if (r.Actor.Level < cmd.MinLevel)
            return await Finish(r, AdminResult.Fail("forbidden", $"niveau {(int)cmd.MinLevel} requis (vous : {(int)r.Actor.Level})"), now);
        if (!_limiter.TryAcquire(r.Actor.AccountId, now))
            return await Finish(r, AdminResult.Fail("rate_limited", "trop de commandes, réessayez dans une minute"), now);
        if (r.RequestId.Length > 0 && await _ctx.Audit.FindByRequestAsync(r.RequestId) is { } previous)
            return previous with { Replayed = true };

        var mass = cmd.IsMass(r);
        if (cmd.MinLevel >= AdminLevel.Content || mass)
        {
            var secret = await _ctx.Totp.GetAsync(r.Actor.AccountId);
            if (secret == null ? _policy.RequireTotp : !Totp.Verify(secret, r.Actor.TotpCode, now))
                return await Finish(r, AdminResult.Fail("totp_required", secret == null ? "double authentification non configurée (commande 2fa)" : "code TOTP invalide ou absent"), now);
        }
        if (mass && !r.DryRun && _policy.FourEyes && cmd.Name != "approve")
        {
            long id; lock (_lock) { id = _nextApproval++; _pending[id] = (r, now); }
            return await Finish(r, AdminResult.Fail("pending_approval", $"action de masse en attente : un autre administrateur (niveau 4) doit faire « approve id={id} »"), now);
        }
        AdminResult res;
        try { res = await cmd.ExecuteAsync(r, _ctx); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or KeyNotFoundException)
        { res = AdminResult.Fail("failed", e.Message); }
        return await Finish(r, res, now);
    }

    /// <summary>Retire et renvoie une demande en attente (utilisé par <c>approve</c>).</summary>
    internal AdminRequest? TakePending(long id, int approverAccountId, out string error)
    {
        error = "";
        lock (_lock)
        {
            if (!_pending.TryGetValue(id, out var p)) { error = $"demande {id} inconnue"; return null; }
            if (p.Request.Actor.AccountId == approverAccountId) { error = "règle des quatre yeux : un autre compte doit approuver"; return null; }
            _pending.Remove(id); return p.Request;
        }
    }

    internal Task<AdminResult> RunApprovedAsync(AdminRequest original, AdminActor approver)
    {
        var cmd = _commands[original.Command];
        return cmd.ExecuteAsync(original with { Actor = original.Actor with { Level = original.Actor.Level } }, _ctx);
    }

    private async Task<AdminResult> Finish(AdminRequest r, AdminResult res, DateTimeOffset now)
    {
        var args = JsonSerializer.Serialize(r.Args.Where(a => a.Key != "totp").ToDictionary(a => a.Key, a => a.Value));
        await _ctx.Audit.AppendAsync(now, r.Actor, r.Command, args, r.DryRun, res.Ok, res.Message, r.RequestId);
        return res;
    }
}
