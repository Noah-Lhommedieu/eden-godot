namespace Eden.Core.Game.Admin;

/// <summary>Commandes d'administration livrées. Ajouter une commande = une classe ici + une ligne dans <see cref="Default"/>.</summary>
public static class AdminCommands
{
    public static IReadOnlyList<IAdminCommand> Default { get; } = new IAdminCommand[]
    {
        new WhoAmI(), new HelpCmd(), new Players(), new Give(), new Remove(), new Vip(), new AuditLog(), new Approve(), new TwoFactor(),
    };

    /// <summary>Plafond de quantité par niveau (garde-fou contre une faute de frappe).</summary>
    public static int MaxQuantity(AdminLevel level) => level switch
    {
        AdminLevel.Support => 100, AdminLevel.Content => 1000, _ => 9999,
    };

    private sealed class WhoAmI : IAdminCommand
    {
        public string Name => "whoami"; public AdminLevel MinLevel => AdminLevel.Viewer; public string Help => "whoami — votre compte et votre niveau";
        public Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c) =>
            Task.FromResult(AdminResult.Success($"{r.Actor.AccountName} niveau {(int)r.Actor.Level}"));
    }

    private sealed class HelpCmd : IAdminCommand
    {
        public string Name => "help"; public AdminLevel MinLevel => AdminLevel.Viewer; public string Help => "help — commandes disponibles pour votre niveau";
        public Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c) => Task.FromResult(AdminResult.Success(string.Join("\n",
            c.Service.Commands.Where(x => x.MinLevel <= r.Actor.Level).OrderBy(x => x.MinLevel).ThenBy(x => x.Name).Select(x => x.Help))));
    }

    private sealed class Players : IAdminCommand
    {
        public string Name => "players"; public AdminLevel MinLevel => AdminLevel.Viewer; public string Help => "players — joueurs connectés";
        public Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var on = c.Host.Online();
            return Task.FromResult(AdminResult.Success($"{on.Count} connecté(s)" + (on.Count > 0 ? ": " + string.Join(", ", on.Select(o => $"{o.Name}#{o.CharId}")) : "")));
        }
    }

    /// <summary>Lecture commune des arguments give/remove ; renvoie une erreur ou les cibles.</summary>
    private static async Task<(AdminResult? Error, List<int> Targets)> Targets(AdminRequest r, AdminContext c)
    {
        var item = r.IntArg("item"); var qty = r.IntArg("qty", 1);
        if (item <= 0 || qty <= 0) return (AdminResult.Fail("bad_args", "item et qty (>0) requis"), new());
        if (qty > MaxQuantity(r.Actor.Level)) return (AdminResult.Fail("bad_args", $"quantité maximale pour votre niveau : {MaxQuantity(r.Actor.Level)}"), new());
        if (string.IsNullOrWhiteSpace(r.Arg("reason"))) return (AdminResult.Fail("bad_args", "reason requis (journalisé)"), new());
        var t = r.Arg("target", "me");
        if (t == "all")
            return r.Actor.Level < AdminLevel.Publisher ? (AdminResult.Fail("forbidden", "target=all : niveau 4 requis"), new())
                : (null, c.Host.Online().Select(o => o.CharId).ToList());
        if (t == "me") return r.Actor.CharId > 0 ? (null, new List<int> { r.Actor.CharId }) : (AdminResult.Fail("bad_args", "aucun personnage actif : indiquez target=<nom>"), new());
        var id = await c.Host.ResolveCharAsync(t);
        return id == null ? (AdminResult.Fail("bad_args", $"personnage inconnu : {t}"), new()) : (null, new List<int> { id.Value });
    }

    private sealed class Give : IAdminCommand
    {
        public string Name => "give"; public AdminLevel MinLevel => AdminLevel.Support;
        public string Help => "give target=<nom|me|all> item=<id> qty=<n> reason=<texte> [dry-run] — donne un objet";
        public bool IsMass(AdminRequest r) => r.Arg("target") == "all";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var (err, ids) = await Targets(r, c); if (err != null) return err;
            var item = r.IntArg("item"); var qty = r.IntArg("qty", 1);
            if (r.DryRun) return AdminResult.Success($"[simulation] {ids.Count} joueur(s) recevraient {qty}× objet {item}");
            if (IsMass(r)) await c.Host.BackupMassActionAsync($"give {item}x{qty} à tous par {r.Actor.AccountName}", ids);
            var done = 0; foreach (var id in ids) if (await c.Host.GiveAsync(id, item, qty)) done++;
            return done == ids.Count ? AdminResult.Success($"{qty}× objet {item} donné à {done} joueur(s)") : AdminResult.Fail("partial", $"{done}/{ids.Count} joueur(s) servis");
        }
    }

    private sealed class Remove : IAdminCommand
    {
        public string Name => "remove"; public AdminLevel MinLevel => AdminLevel.Support;
        public string Help => "remove target=<nom|me> item=<id> qty=<n> reason=<texte> [dry-run] — retire un objet";
        public bool IsMass(AdminRequest r) => r.Arg("target") == "all";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var (err, ids) = await Targets(r, c); if (err != null) return err;
            var item = r.IntArg("item"); var qty = r.IntArg("qty", 1);
            if (r.DryRun) return AdminResult.Success($"[simulation] {ids.Count} joueur(s) perdraient {qty}× objet {item}");
            if (IsMass(r)) await c.Host.BackupMassActionAsync($"remove {item}x{qty} à tous par {r.Actor.AccountName}", ids);
            var done = 0; foreach (var id in ids) if (await c.Host.RemoveAsync(id, item, qty)) done++;
            return done == ids.Count ? AdminResult.Success($"{qty}× objet {item} retiré à {done} joueur(s)") : AdminResult.Fail("partial", $"{done}/{ids.Count} joueur(s) servis (quantité insuffisante ?)");
        }
    }

    private sealed class Vip : IAdminCommand
    {
        public string Name => "vip"; public AdminLevel MinLevel => AdminLevel.Content;
        public string Help => "vip account=<compte> [days=<n>|off] reason=<texte> — accorde ou retire le VIP";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var acc = r.Arg("account"); if (acc.Length == 0) return AdminResult.Fail("bad_args", "account requis");
            if (string.IsNullOrWhiteSpace(r.Arg("reason"))) return AdminResult.Fail("bad_args", "reason requis (journalisé)");
            var days = r.Arg("days");
            if (r.DryRun) return AdminResult.Success($"[simulation] vip {acc} {(days.Length == 0 ? "permanent" : days)}");
            if (days == "off") return AdminResult.Success(await c.Host.RevokeVipAsync(acc));
            if (days.Length > 0 && !(int.TryParse(days, out var d) && d > 0)) return AdminResult.Fail("bad_args", "days doit être un entier > 0 ou off");
            return AdminResult.Success(await c.Host.GrantVipAsync(acc, days.Length == 0 ? null : c.Clock.Now.AddDays(int.Parse(days))));
        }
    }

    private sealed class AuditLog : IAdminCommand
    {
        public string Name => "audit"; public AdminLevel MinLevel => AdminLevel.Viewer; public string Help => "audit [n=<20>] — dernières actions d'administration";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var rows = await c.Audit.RecentAsync(Math.Clamp(r.IntArg("n", 20), 1, 100));
            var broken = AuditChain.FirstBroken(rows.OrderBy(x => x.AuditId));
            return AdminResult.Success(string.Join("\n", rows.Select(x => $"#{x.AuditId} {x.At:HH:mm:ss} {x.AccountName} {x.Command} {(x.Ok ? "ok" : "refusé")} {x.Message}"))
                + (broken is { } b && rows.Count > 0 && rows.Min(x => x.AuditId) == 1 ? $"\n!! chaîne rompue à #{b}" : ""));
        }
    }

    private sealed class Approve : IAdminCommand
    {
        public string Name => "approve"; public AdminLevel MinLevel => AdminLevel.Publisher; public string Help => "approve id=<n> — approuve une action de masse demandée par un autre compte (quatre yeux)";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            var original = c.Service.TakePending(r.IntArg("id"), r.Actor.AccountId, out var err);
            if (original == null) return AdminResult.Fail("bad_args", err);
            var res = await c.Service.RunApprovedAsync(original, r.Actor);
            return res with { Message = $"approuvé par {r.Actor.AccountName} : {res.Message}" };
        }
    }

    private sealed class TwoFactor : IAdminCommand
    {
        public string Name => "2fa"; public AdminLevel MinLevel => AdminLevel.Viewer; public string Help => "2fa — configure la double authentification (renvoie l'URI à scanner)";
        public async Task<AdminResult> ExecuteAsync(AdminRequest r, AdminContext c)
        {
            if (await c.Totp.GetAsync(r.Actor.AccountId) != null) return AdminResult.Fail("bad_args", "double authentification déjà configurée");
            var secret = Totp.GenerateSecret(); await c.Totp.SetAsync(r.Actor.AccountId, secret);
            return AdminResult.Success(Totp.OtpAuthUri(r.Actor.AccountName, secret));
        }
    }
}
