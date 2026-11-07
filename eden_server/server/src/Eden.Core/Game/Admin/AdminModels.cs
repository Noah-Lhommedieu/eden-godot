namespace Eden.Core.Game.Admin;

/// <summary>Niveaux d'administration d'un COMPTE (PLAN-ACTION-3 D0) : 1 lecture, 2 support, 3 contenu, 4 publication, 5 root.</summary>
public enum AdminLevel { None = 0, Viewer = 1, Support = 2, Content = 3, Publisher = 4, Root = 5 }

/// <summary>Qui exécute : compte, personnage actif (0 hors jeu) et code TOTP éventuel.</summary>
public sealed record AdminActor(int AccountId, string AccountName, int CharId, AdminLevel Level, string? TotpCode = null);

/// <summary>Requête d'administration, identique pour le panneau en jeu, le site et la console. <c>RequestId</c> rend la requête idempotente.</summary>
public sealed record AdminRequest(string RequestId, string Command, IReadOnlyDictionary<string, string> Args, AdminActor Actor, bool DryRun = false)
{
    public string Arg(string name, string fallback = "") => Args.TryGetValue(name, out var v) ? v : fallback;
    public int IntArg(string name, int fallback = 0) => int.TryParse(Arg(name), out var n) ? n : fallback;
}

/// <summary>Résultat : <c>Code</c> vide si OK, sinon un code stable (forbidden, rate_limited, totp_required, bad_args, unknown_command, pending_approval…).</summary>
public sealed record AdminResult(bool Ok, string Message, string Code = "", bool Replayed = false)
{
    public static AdminResult Success(string message) => new(true, message);
    public static AdminResult Fail(string code, string message) => new(false, message, code);
}

/// <summary>Ligne du journal d'audit chaîné (append-only) : chaque empreinte couvre la précédente.</summary>
public sealed record AuditEntry(long AuditId, DateTimeOffset At, int AccountId, string AccountName, int CharId, string Command,
    string ArgsJson, bool DryRun, bool Ok, string Message, string RequestId, string PrevHash, string Hash);

/// <summary>Opérations de jeu dont les commandes ont besoin ; implémentées par la zone (joueur connecté) et la base (hors ligne).</summary>
public interface IAdminHost
{
    /// <summary>Identifiant du personnage portant ce nom (connecté ou non), null si inconnu.</summary>
    Task<int?> ResolveCharAsync(string name);
    Task<bool> GiveAsync(int charId, int itemId, int quantity);
    Task<bool> RemoveAsync(int charId, int itemId, int quantity);
    /// <summary>Personnages actuellement connectés (id, nom).</summary>
    IReadOnlyList<(int CharId, string Name)> Online();
    Task<string> GrantVipAsync(string account, DateTimeOffset? until);
    Task<string> RevokeVipAsync(string account);
    /// <summary>Sauvegarde les cibles d'une action de masse AVANT de l'exécuter ; renvoie le chemin ou l'identifiant de sauvegarde.</summary>
    Task<string> BackupMassActionAsync(string description, IReadOnlyList<int> charIds);
}

/// <summary>Magasin d'audit chaîné.</summary>
public interface IAdminAuditStore
{
    Task<AuditEntry> AppendAsync(DateTimeOffset at, AdminActor actor, string command, string argsJson, bool dryRun, bool ok, string message, string requestId);
    Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit);
    Task<AdminResult?> FindByRequestAsync(string requestId);
}
