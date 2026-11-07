using Dapper;
using Eden.Core.Game.Admin;
using Npgsql;

namespace Eden.Core.Database.Repositories;

/// <summary>Audit d'administration chaîné (<c>admin.audit</c>) et secrets TOTP (<c>account.accounts.totp_secret</c>).</summary>
public sealed class AdminRepository : IAdminAuditStore, ITotpStore
{
    private const string Columns = "audit_id, at, account_id, account_name, char_id, command, args_json, dry_run, ok, message, request_id, prev_hash, hash";
    private readonly NpgsqlDataSource _db;
    public AdminRepository(NpgsqlDataSource db) => _db = db;

    public async Task<AuditEntry> AppendAsync(DateTimeOffset at, AdminActor a, string command, string argsJson, bool dryRun, bool ok, string message, string requestId)
    {
        await using var c = await _db.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(9001001)", transaction: tx);          // sérialise la chaîne
        var prev = await c.ExecuteScalarAsync<string?>("SELECT hash FROM admin.audit ORDER BY audit_id DESC LIMIT 1", transaction: tx) ?? AuditChain.Genesis;
        var hash = AuditChain.Hash(prev, AuditChain.Content(at, a.AccountId, a.AccountName, a.CharId, command, argsJson, dryRun, ok, message, requestId));
        var id = await c.ExecuteScalarAsync<long?>(
            "INSERT INTO admin.audit(at, account_id, account_name, char_id, command, args_json, dry_run, ok, message, request_id, prev_hash, hash) " +
            "VALUES (@at, @aid, @an, @cid, @command, @argsJson, @dryRun, @ok, @message, @requestId, @prev, @hash) " +
            "ON CONFLICT (request_id) WHERE request_id <> '' DO NOTHING RETURNING audit_id",
            new { at = at.ToUniversalTime(), aid = a.AccountId, an = a.AccountName, cid = a.CharId, command, argsJson, dryRun, ok, message, requestId, prev, hash }, tx);
        await tx.CommitAsync();
        return new AuditEntry(id ?? 0, at, a.AccountId, a.AccountName, a.CharId, command, argsJson, dryRun, ok, message, requestId, prev, hash);
    }

    private sealed record Row(long audit_id, DateTime at, int account_id, string account_name, int char_id, string command, string args_json,
        bool dry_run, bool ok, string message, string request_id, string prev_hash, string hash)
    {
        public AuditEntry ToEntry() => new(audit_id, new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)), account_id, account_name, char_id,
            command, args_json, dry_run, ok, message, request_id, prev_hash, hash);
    }

    public async Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit)
    {
        await using var c = await _db.OpenConnectionAsync();
        var rows = await c.QueryAsync<Row>($"SELECT {Columns} FROM admin.audit ORDER BY audit_id DESC LIMIT @limit", new { limit });
        return rows.Select(r => r.ToEntry()).ToList();
    }

    /// <summary>Vérifie toute la chaîne ; renvoie l'id de la première ligne altérée, ou null.</summary>
    public async Task<long?> VerifyAllAsync()
    {
        await using var c = await _db.OpenConnectionAsync();
        var rows = await c.QueryAsync<Row>($"SELECT {Columns} FROM admin.audit ORDER BY audit_id");
        return AuditChain.FirstBroken(rows.Select(r => r.ToEntry()));
    }

    public async Task<AdminResult?> FindByRequestAsync(string requestId)
    {
        await using var c = await _db.OpenConnectionAsync();
        var r = await c.QuerySingleOrDefaultAsync<(bool Ok, string Message)?>("SELECT ok AS Ok, message AS Message FROM admin.audit WHERE request_id = @requestId", new { requestId });
        return r == null ? null : new AdminResult(r.Value.Ok, r.Value.Message, r.Value.Ok ? "" : "failed");
    }

    public async Task<string?> GetAsync(int accountId)
    {
        await using var c = await _db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<string?>("SELECT totp_secret FROM account.accounts WHERE id = @accountId", new { accountId });
    }

    public async Task SetAsync(int accountId, string secret)
    {
        await using var c = await _db.OpenConnectionAsync();
        await c.ExecuteAsync("UPDATE account.accounts SET totp_secret = @secret WHERE id = @accountId", new { accountId, secret });
    }

    /// <summary>Nom de connexion d'un compte (pour l'audit), vide si inconnu.</summary>
    public async Task<string> AccountNameAsync(int accountId)
    {
        await using var c = await _db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<string?>("SELECT username FROM account.accounts WHERE id = @accountId", new { accountId }) ?? "";
    }

    /// <summary>Niveau d'administrateur d'un compte (0 si inconnu), source du privilège de jeu et des droits du site.</summary>
    public async Task<int> AdminLevelAsync(int accountId)
    {
        await using var c = await _db.OpenConnectionAsync();
        return await c.ExecuteScalarAsync<int?>("SELECT admin_level FROM account.accounts WHERE id = @accountId", new { accountId }) ?? 0;
    }
}
