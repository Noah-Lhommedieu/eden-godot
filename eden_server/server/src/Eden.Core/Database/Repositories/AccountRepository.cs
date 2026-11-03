using Dapper;
using Npgsql;
using Eden.Core.Database.Entities;
using Eden.Core.Security;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Implémentation PostgreSQL du dépôt des comptes joueurs via Dapper.
/// Reversé depuis lapis::CAccountDBO.
/// </summary>
public sealed partial class AccountRepository : IAccountRepository
{
    private readonly NpgsqlDataSource _db;

    public AccountRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    public async Task<Account?> GetAccountByUsernameAsync(string username)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   username AS Username,
                   password AS Password,
                   password_hash AS PasswordHash,
                   realname AS RealName,
                   worldserver AS WorldServerId,
                   state AS State,
                   health_online_time AS HealthOnlineTime,
                   health_offline_time AS HealthOfflineTime,
                   last_save_health_time AS LastSaveHealthTime,
                   itemmall_password AS ItemMallPassword,
                   id_card AS IdCard
            FROM accounts
            WHERE LOWER(username) = LOWER(@Username)
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<Account>(sql, new { Username = username });
    }

    public async Task<Account?> GetAccountByIdAsync(int accountId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
            SELECT id AS Id,
                   username AS Username,
                   password AS Password,
                   password_hash AS PasswordHash,
                   realname AS RealName,
                   worldserver AS WorldServerId,
                   state AS State,
                   health_online_time AS HealthOnlineTime,
                   health_offline_time AS HealthOfflineTime,
                   last_save_health_time AS LastSaveHealthTime,
                   itemmall_password AS ItemMallPassword,
                   id_card AS IdCard
            FROM accounts
            WHERE id = @AccountId
            LIMIT 1;";

        return await connection.QueryFirstOrDefaultAsync<Account>(sql, new { AccountId = accountId });
    }

    public async Task<int> CreateAccountAsync(string username, string password, string realName = "")
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        // Pas de séquence sur accounts.id — le natif alloue via
        // CUIDAllocator (AIDAllocator, plage MinAID..MaxAID pré-allouée
        // sur les ids existants). max(id)+1 reproduit la même plage.
        //
        // `password` garde le plaintext (colonne legacy de transition,
        // parité gm_tool/tb_user) ; `password_hash` reçoit le PHC
        // argon2id du credential wire = md5(password) — c'est la forme
        // qu'enverra le client Godot au login (Field2 = md5(mdp)).
        const string sql = @"
            INSERT INTO accounts (id, username, password, password_hash, realname, worldserver, state)
            VALUES ((SELECT COALESCE(MAX(id), 0) + 1 FROM accounts),
                    @Username, @Password, @PasswordHash, @RealName, 0, 0)
            RETURNING id;";

        return await connection.ExecuteScalarAsync<int>(sql, new
        {
            Username = username,
            Password = password,
            PasswordHash = PasswordHasher.HashPlaintextPassword(password),
            RealName = realName
        });
    }

    public async Task SaveLastWorldAsync(int accountId, short worldId)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"UPDATE accounts SET worldserver = @WorldId WHERE id = @AccountId;";
        await connection.ExecuteAsync(sql, new { WorldId = (int)worldId, AccountId = accountId });
    }

    public async Task UpdateAccountStateAsync(int accountId, int state)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"UPDATE accounts SET state = @State WHERE id = @AccountId;";
        await connection.ExecuteAsync(sql, new { State = state, AccountId = accountId });
    }

    /// <summary>UPDATE du PHC Argon2id — appelé par le rehash-at-login
    /// après une authentification legacy réussie (et disponible pour un
    /// futur changement de mot de passe).</summary>
    public async Task UpdatePasswordHashAsync(int accountId, string passwordHash)
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"UPDATE accounts SET password_hash = @PasswordHash WHERE id = @AccountId;";
        await connection.ExecuteAsync(sql, new { PasswordHash = passwordHash, AccountId = accountId });
    }
}

public sealed partial class AccountRepository
{
    /// <summary>Ligne de la table <c>worlds</c> de edenfr_account
    /// (<c>CAccountDBO::GetWorldServerStates</c> : SELECT id, name,
    /// ip, port, online_user, maxnum_user, state, show_order).</summary>
    public sealed class WorldServerRow
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? ip { get; set; }
        public int port { get; set; }
        public int online_user { get; set; }
        public int maxnum_user { get; set; }
        public int state { get; set; }
        public int show_order { get; set; }
    }

    /// <summary>Liste des mondes pour
    /// <c>CNC_CW_ClientGetWorldServers</c> (requête native 29 —
    /// refresh de la liste des mondes côté client).</summary>
    public async Task<IReadOnlyList<WorldServerRow>> GetWorldServersAsync()
    {
        using var connection = _db.CreateConnection();
        await connection.OpenAsync();
        return (await connection.QueryAsync<WorldServerRow>(
            "SELECT id, name, ip, port, online_user, maxnum_user, " +
            "state, show_order FROM worlds ORDER BY show_order")).AsList();
    }
}
