using Eden.Core.Database.Repositories;
using Eden.Core.Game.Foundation;
using Npgsql;

namespace Eden.Zone.Features.Modifiers;

/// <summary>
/// Liste en mémoire des comptes VIP, rechargée périodiquement : le gameplay lit un booléen
/// synchrone (<see cref="IsVip"/>), sans accès base. Un VIP accordé ou expiré est pris en compte
/// au plus tard après <see cref="RefreshSeconds"/> secondes.
/// </summary>
public static class VipDirectory
{
    public const int RefreshSeconds = 10;

    private static volatile IReadOnlySet<int> _active = new HashSet<int>();

    /// <summary>Dépôt du statut VIP (posé par <see cref="Start"/>) pour les commandes admin.</summary>
    public static VipRepository? Repo { get; private set; }

    public static bool IsVip(int accountId) => _active.Contains(accountId);

    /// <summary>Remplace la liste (tests, ou rechargement manuel après une commande admin).</summary>
    public static void Set(IEnumerable<int> accountIds) => _active = accountIds.ToHashSet();

    /// <summary>Recharge tout de suite la liste (après un octroi ou un retrait).</summary>
    public static async Task RefreshAsync() =>
        _active = await Repo!.ActiveAccountIdsAsync(GameTime.Clock.Now);

    /// <summary>Lance la boucle de rechargement ; une erreur de base garde la dernière liste connue.</summary>
    public static void Start(NpgsqlDataSource db)
    {
        var repo = Repo = new VipRepository(db);
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try { _active = await repo.ActiveAccountIdsAsync(GameTime.Clock.Now); }
                catch (Exception e) { Eden.Core.Logging.EdenLog.Write($"[Vip] rechargement impossible : {e.Message}"); }
                await Task.Delay(TimeSpan.FromSeconds(RefreshSeconds));
            }
        });
    }
}
