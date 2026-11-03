using Eden.Core.Game.Mall;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Accès aux tables item mall natives de edenfr_account —
/// <c>CAccountDBO</c> lapis (PGAccountDBO.cc) : <c>itemmall</c>
/// (catalogue), <c>item_receivable</c> (achats en attente de
/// réclamation), <c>item_receipt</c> (reçus livrés),
/// <c>itemmall_limit_amount</c> (compteurs d'achats limités).
/// Le billing natif passe par LP_DBillingServerConnection ; ici la
/// déduction est synchrone via <see cref="IUserRepository"/>.
/// </summary>
public interface IItemMallRepository
{
    /// <summary>Catalogue actif (sell=1) trié (item_index, item_group)
    /// — <c>GetAllItemMallItem</c>. Les validations natives de chargement
    /// sont faites par l'appelant (<see cref="ItemMallCatalog"/>).</summary>
    Task<IReadOnlyList<ItemMallEntry>> GetCatalogAsync(
        CancellationToken token = default);

    /// <summary>true si le compte a au moins une ligne state=1 —
    /// <c>ItemMallAnythingReceivable</c> (réponse booléenne du 56).</summary>
    Task<bool> AnythingReceivableAsync(string accountName,
        CancellationToken token = default);

    /// <summary>Liste paginée des réclamables — les trois variantes SQL
    /// natives de <c>ItemMallReceivableQuery</c> :
    /// type 21 → LIMIT 21 sans filtre temps ; type 0 →
    /// <c>purchase_time &gt; from</c> LIMIT 21 ; sinon →
    /// <c>purchase_time NOT BETWEEN from AND to</c> LIMIT type.</summary>
    Task<IReadOnlyList<ItemReceivableRow>> GetReceivablesAsync(
        string accountName, int type, string? from, string? to,
        CancellationToken token = default);

    /// <summary>Claim : SELECT state=1 FOR UPDATE → UPDATE state=2.
    /// Retourne la ligne si elle était réclamable, null sinon —
    /// <c>ItemMallReceieveAttempt</c> (résultat 2 natif = succès).</summary>
    Task<ItemReceivableRow?> ClaimReceivableAsync(int id, string accountName,
        CancellationToken token = default);

    /// <summary>Livraison réussie : INSERT item_receipt (même id) →
    /// DELETE item_receivable — <c>ItemMallReceieveSuccess</c>.</summary>
    Task CompleteReceivableAsync(int id, string accountName,
        int uniqueId, int createTime, CancellationToken token = default);

    /// <summary>Livraison échouée : state 2→1 —
    /// <c>ItemMallReceieveFailure</c> (remise en file).</summary>
    Task RequeueReceivableAsync(int id, CancellationToken token = default);

    /// <summary>Remet en file (state 2→1) toutes les lignes claimées
    /// jamais finalisées — orphelines quand le world/zone a redémarré
    /// entre le claim (CW 18) et la réponse ZW 12, ou que la zone a
    /// répondu après la coupure du lien. Appelé au boot du world et à
    /// chaque ré-enregistrement de zone.</summary>
    Task<int> RequeueClaimedReceivablesAsync(
        CancellationToken token = default);

    /// <summary>Insert d'un achat — <c>ItemMallReqBuyItem</c>. Chez nous
    /// pas de billing externe : state=1 directement réclamable.</summary>
    Task<int> InsertReceivableAsync(string accountName, int itemId,
        int itemQuantity, long itemColor, int point, int amount,
        int worldId, string playerName, string mailName,
        CancellationToken token = default);

    /// <summary>Compteurs d'achats limités du compte — remplit le 79
    /// (<c>UpdatePlayerItemMallLimit</c>).</summary>
    Task<IReadOnlyList<ItemMallLimitRow>> GetLimitAmountsAsync(
        int accountId, CancellationToken token = default);

    /// <summary><c>ItemMallCheckLimitAmount</c> : lit sell_amount pour
    /// la clé (insère une ligne à 0 si absente) ; -1 en cas d'erreur.</summary>
    Task<int> CheckLimitAmountAsync(int accountId, int group, int index,
        int moneyUnit, int itemId, int itemNum,
        CancellationToken token = default);

    /// <summary><c>ItemMallAddLimitAmount</c> : sell_amount += amount
    /// pour la clé (créée à 0 par CheckLimitAmount au préalable).</summary>
    Task AddLimitAmountAsync(int accountId, int group, int index,
        int moneyUnit, int itemId, int itemNum, int amount,
        CancellationToken token = default);
}
