namespace Eden.Core.Database.Repositories;

/// <summary>
/// Interface d'accès à la facturation item mall (edenfr_member.tb_user).
/// </summary>
public interface IUserRepository
{
    /// <summary>Soldes item mall du compte (edenfr_member.tb_user réel :
    /// <c>pvalues</c> = monnaie 1, <c>bonus</c> = monnaie 2 — équivalent
    /// des champs CPlayerEntity +0x44/+0x48 du billing natif).</summary>
    Task<(int Pvalues, int Bonus)?> GetItemMallPointsAsync(
        string memberId, CancellationToken token = default);

    /// <summary>Déduction atomique <c>UPDATE tb_user SET col=col-c WHERE
    /// mid=@m AND col&gt;=c</c> — false si solde insuffisant.
    /// <paramref name="currency"/> : 1 = pvalues, 2 = bonus, 3 = temps de
    /// jeu rabattu sur pvalues (pas de billing externe chez nous).</summary>
    Task<bool> TryDeductItemMallPointsAsync(string memberId, int currency,
        int cost, CancellationToken token = default);

    /// <summary>Crédite des points item mall — inverse de la
    /// déduction (même mapping monnaie 1=pvalues / 2=bonus).</summary>
    Task<bool> AddItemMallPointsAsync(string memberId, int currency,
        int amount, CancellationToken token = default);
}
