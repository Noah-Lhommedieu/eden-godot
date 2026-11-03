using Eden.Core.Database.Entities;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Interface d'accès aux données des comptes joueurs.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetAccountByUsernameAsync(string username);
    Task<Account?> GetAccountByIdAsync(int accountId);
    Task<int> CreateAccountAsync(string username, string password, string realName = "");
    Task SaveLastWorldAsync(int accountId, short worldId);
    Task UpdateAccountStateAsync(int accountId, int state);
    /// <summary>Écrit le PHC Argon2id de <paramref name="passwordHash"/>
    /// dans <c>accounts.password_hash</c> (rehash-at-login et création de
    /// compte — voir <c>PasswordHasher</c>).</summary>
    Task UpdatePasswordHashAsync(int accountId, string passwordHash);
}
