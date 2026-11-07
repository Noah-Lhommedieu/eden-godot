using Eden.Core.Database.Repositories;
using Eden.Core.Game.Auction;

namespace Eden.Core.Game.Services;

/// <summary>
/// Hotel des ventes — correspond a CAuctionShop.
/// Mise en vente, recherche, frais, duree, recuperation.
/// </summary>
public sealed class AuctionService
{
    private readonly List<AuctionListing> _listings = new();
    private int _nextId = 1;
    private IAuctionRepository? _repo;

    /// <summary>Branche la persistance PostgreSQL (write-through).</summary>
    public void Attach(IAuctionRepository repo) => _repo = repo;

    /// <summary>Recharge les annonces actives depuis la base (au boot).</summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        if (_repo == null) return;
        var rows = await _repo.GetActiveAuctionsAsync();
        foreach (var r in rows)
        {
            _listings.Add(new AuctionListing
            {
                Id = r.Id, SellerId = r.SellerId, ItemId = r.ItemId,
                Quantity = r.Quantity, Price = r.Price,
                ExpiresAt = r.ExpiresAt,
                ItemType = r.ItemType, Level = r.Level, Class = r.Class,
                QualityFilter = r.QualityFilter, ItemName = r.ItemName,
            });
            if (r.Id >= _nextId) _nextId = r.Id + 1;
        }
    }

    /// <summary>Frais de depot: % du prix demande.</summary>
    public const double DepositRate = 0.05;
    /// <summary>Commission sur vente reussie: % du prix final.</summary>
    public const double SaleCommissionRate = 0.08;

    public sealed class AuctionListing
    {
        public int Id { get; set; }
        public int SellerId { get; set; }
        public int ItemId { get; set; }
        public int Quantity { get; set; }
        public long Price { get; set; }
        public long? BuyoutPrice { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int? BuyerId { get; set; }
        public bool Sold { get; set; }
        // Champs dénormalisés (miroir natif `auction`, migration 026) —
        // renseignés à la mise en vente depuis l'ItemDef + l'instance.
        /// <summary>Catégorie HV (ItemDef.ItemType, col33→+0x84).</summary>
        public int ItemType { get; set; }
        /// <summary>Niveau requis (ItemDef.RequiredLevel, col14→+0x3e).</summary>
        public int Level { get; set; }
        /// <summary>Masque i64 classes (ItemDef.ClassMask, col18→+0x48).</summary>
        public long Class { get; set; }
        /// <summary><c>1 &lt;&lt; quality</c> d'instance (SaveAuction).</summary>
        public int QualityFilter { get; set; } = 1;
        /// <summary>Nom natif : maker + nom + "+N" (filtre LIKE).</summary>
        public string ItemName { get; set; } = "";
    }

    /// <summary>Champs dénormalisés calculés une fois à la mise en
    /// vente (ItemDef + qualité d'instance) — mêmes colonnes que
    /// l'INSERT natif SaveAuction.</summary>
    public readonly record struct AuctionDenorm(int ItemType, int Level,
        long Class, int QualityFilter, string ItemName);

    /// <summary>Filtre du catalogue (wire 114 = ordre natif des pops
    /// de TC_AuctionBuyQuery, mission). Adaptation client Godot : tout
    /// champ à 0/vide = « pas de filtre » (le client natif envoie
    /// toujours des bornes pleines ; le nôtre envoie des défauts).</summary>
    public readonly record struct AuctionQuery(
        short LevelMin, short LevelMax, int PriceMin, int PriceMax,
        int ClassMask, short QualityMask, short ItemType,
        short Sort, short Desc, string Keyword, short Page);

    /// <summary>
    /// Met un objet en vente. Retourne les frais de depot a payer.
    /// </summary>
    public async Task<AuctionListing> ListAsync(int sellerId, int itemId,
        int quantity, long price, TimeSpan duration,
        long? buyoutPrice = null, AuctionDenorm denorm = default)
    {
        var listing = new AuctionListing
        {
            Id = _nextId++,
            SellerId = sellerId,
            ItemId = itemId,
            Quantity = quantity,
            Price = price,
            BuyoutPrice = buyoutPrice,
            ExpiresAt = DateTime.UtcNow + duration,
            ItemType = denorm.ItemType, Level = denorm.Level,
            Class = denorm.Class,
            QualityFilter = denorm.QualityFilter == 0 ? 1
                : denorm.QualityFilter,
            ItemName = denorm.ItemName ?? "",
        };
        _listings.Add(listing);
        if (_repo != null)
            listing.Id = await _repo.CreateAuctionAsync(new AuctionEntity
            {
                SellerId = sellerId, ItemId = itemId, Quantity = quantity,
                Price = (int)price,
                DurationHours = (int)duration.TotalHours,
                ExpiresAt = listing.ExpiresAt, Status = "active",
                ItemType = denorm.ItemType, Level = denorm.Level,
                Class = denorm.Class,
                QualityFilter = listing.QualityFilter,
                ItemName = listing.ItemName,
            });
        return listing;
    }

    /// <summary>
    /// Mise en vente persistée : sur repo SQL, les frais de dépôt,
    /// le retrait de l'item du sac vendeur et l'INSERT de l'annonce
    /// sont atomiques (<see cref="AuctionRepository.CreateListingAsync"/>)
    /// — <c>Atomic=true</c> et l'appelant ne doit PAS refaire
    /// débit/retrait. Sur repo non-SQL (tests en mémoire), repli sur
    /// <see cref="ListAsync"/> — <c>Atomic=false</c> : le caller
    /// reprend le chemin historique frais+retrait séparés.
    /// </summary>
    /// <param name="depositFee">frais déjà calculés (DepositFee) et
    /// vérifiés solvables par l'appelant — la tx re-garde le solde.</param>
    public async Task<(AuctionListing? Listing, bool Atomic)>
        ListPersistedAsync(int sellerId, int itemId, int quantity,
            long price, TimeSpan duration, long? buyoutPrice,
            int bagId, int slot, long depositFee,
            AuctionDenorm denorm = default)
    {
        if (_repo is not AuctionRepository sql)
        {
            var legacy = await ListAsync(sellerId, itemId, quantity,
                price, duration, buyoutPrice, denorm);
            return (legacy, false);
        }
        var entity = new AuctionEntity
        {
            SellerId = sellerId, ItemId = itemId, Quantity = quantity,
            Price = (int)price, DurationHours = (int)duration.TotalHours,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow + duration, Status = "active",
            ItemType = denorm.ItemType, Level = denorm.Level,
            Class = denorm.Class,
            QualityFilter = denorm.QualityFilter == 0 ? 1
                : denorm.QualityFilter,
            ItemName = denorm.ItemName ?? "",
        };
        var id = await sql.CreateListingAsync(entity, bagId, slot,
            depositFee);
        if (id == null) return (null, true);
        var listing = new AuctionListing
        {
            Id = id.Value, SellerId = sellerId, ItemId = itemId,
            Quantity = quantity, Price = price,
            BuyoutPrice = buyoutPrice, ExpiresAt = entity.ExpiresAt,
            ItemType = entity.ItemType, Level = entity.Level,
            Class = entity.Class, QualityFilter = entity.QualityFilter,
            ItemName = entity.ItemName,
        };
        _listings.Add(listing);
        if (listing.Id >= _nextId) _nextId = listing.Id + 1;
        return (listing, true);
    }

    public long DepositFee(long price) => (long)(price * DepositRate);

    /// <summary>Recherche par itemId, prix max, vendeur exclu.</summary>
    public IReadOnlyList<AuctionListing> Search(int? itemId = null,
        long? maxPrice = null, int? excludeSellerId = null)
        => _listings.Where(l =>
                !l.Sold && l.ExpiresAt > DateTime.UtcNow
                && (itemId == null || l.ItemId == itemId)
                && (maxPrice == null || l.Price <= maxPrice)
                && (excludeSellerId == null || l.SellerId != excludeSellerId))
            .OrderBy(l => l.Price)
            .ToList();

    /// <summary>
    /// Catalogue filtré — port de
    /// <c>CExecutionerDBO::QueryAuctionBuy</c> (mission @0x08160710) :
    /// même WHERE sur les champs dénormalisés, tri natif par vue
    /// (0=level, 1=quality, 2=duedate, 3=price — tout autre code →
    /// level asc comme le fallback natif `auction_view_by_level_up`),
    /// pagination 20/page, et le COUNT renvoyé à côté.
    /// Adaptation wire : notre client envoie 0/vide = « pas de
    /// filtre » (levelMax/priceMax ≤ 0 = sans borne haute, masques 0
    /// = passe-tout — la sentinelle native classe-0 est déjà
    /// équivalente ; itemType 0 = toutes catégories ; keyword vide).
    /// </summary>
    public (IReadOnlyList<AuctionListing> Entries, int Total)
        Search(AuctionQuery q)
    {
        var now = DateTime.UtcNow;
        var kw = q.Keyword ?? "";
        var rows = _listings.Where(l =>
            !l.Sold && l.ExpiresAt > now
            && (q.ItemType == 0 || l.ItemType == q.ItemType)
            && l.Price >= q.PriceMin
            && (q.PriceMax <= 0 || l.Price <= q.PriceMax)
            && l.Level >= q.LevelMin
            && (q.LevelMax <= 0 || l.Level <= q.LevelMax)
            && (q.QualityMask == 0
                || (l.QualityFilter & q.QualityMask) != 0)
            && (q.ClassMask == 0
                || (l.Class & q.ClassMask) != 0)
            && (kw.Length == 0
                || l.ItemName.Contains(kw,
                    StringComparison.OrdinalIgnoreCase)));
        rows = q.Sort switch
        {
            1 => q.Desc != 0
                ? rows.OrderByDescending(l => l.QualityFilter)
                : rows.OrderBy(l => l.QualityFilter),
            2 => q.Desc != 0
                ? rows.OrderByDescending(l => l.ExpiresAt)
                : rows.OrderBy(l => l.ExpiresAt),
            3 => q.Desc != 0
                ? rows.OrderByDescending(l => l.Price)
                : rows.OrderBy(l => l.Price),
            _ => q.Desc != 0
                ? rows.OrderByDescending(l => l.Level)
                : rows.OrderBy(l => l.Level),
        };
        var list = rows.ToList();
        var page = Math.Max(0, (int)q.Page);
        return (list.Skip(page * 20).Take(20).ToList(), list.Count);
    }

    /// <summary>
    /// Achat — retourne (goldPourVendeur, commission).
    /// </summary>
    public async Task<(long ToSeller, long Commission)?> BuyAsync(
        int buyerId, int listingId)
    {
        var l = _listings.FirstOrDefault(x => x.Id == listingId);
        if (l == null || l.Sold || l.ExpiresAt <= DateTime.UtcNow
            || l.SellerId == buyerId)
            return null;
        // Marquage mémoire IMMÉDIAT (réservation — un second achat
        // traité pendant l'await du règlement serait sinon accepté).
        // Sur repo SQL, la transition status='sold' est portée par la
        // tx de règlement (SettleAuctionPurchaseAsync — or+mail+statut
        // atomiques) ; si celle-ci échoue la ligne DB reste 'active'
        // et l'annonce renaît au prochain chargement — état correct
        // puisque rien n'a été réglé.
        l.Sold = true;
        l.BuyerId = buyerId;
        if (_repo != null && _repo is not AuctionRepository)
            await _repo.UpdateAuctionAsync(new AuctionEntity
            {
                Id = l.Id, SellerId = l.SellerId, ItemId = l.ItemId,
                Quantity = l.Quantity, Price = (int)l.Price,
                ExpiresAt = l.ExpiresAt, Status = "sold",
            });
        var commission = (long)(l.Price * SaleCommissionRate);
        return (l.Price - commission, commission);
    }

    /// <summary>Annonce par id (non vendue, non expirée).</summary>
    public AuctionListing? GetListing(int listingId)
        => _listings.FirstOrDefault(l => l.Id == listingId && !l.Sold &&
                                         l.ExpiresAt > DateTime.UtcNow);

    /// <summary>
    /// Annule une annonce du vendeur — retourne l'annonce retirée ou
    /// null si refusée. Quand <paramref name="mail"/> est fourni le
    /// retour d'item au vendeur part par mail transactionnel
    /// (<see cref="MailRepository.SettleAuctionReturnAsync"/> :
    /// status='cancelled' + mail + ligne mailitem atomiques) — sinon
    /// repli sur l'UPDATE d'état historique (repos non-SQL/tests,
    /// l'appelant réinsère alors l'item en inventaire).
    /// </summary>
    /// <param name="durability">durability/count de la pièce jointe
    /// (stack = quantité ; équipement = usure restaurée).</param>
    /// <param name="maxDurability">cur_maxdurability de la pièce
    /// jointe (cap de pile ou usure max du template).</param>
    public async Task<AuctionListing?> CancelAsync(int sellerId,
        int listingId, MailRepository? mail = null,
        int durability = 1, int maxDurability = 1)
    {
        var l = _listings.FirstOrDefault(x => x.Id == listingId);
        if (l == null || l.Sold || l.SellerId != sellerId) return null;
        if (mail != null)
        {
            var ok = await mail.SettleAuctionReturnAsync(l.Id,
                "cancelled", BuildReturnMail(sellerId, l),
                durability, maxDurability);
            // Tx refusée (annonce déjà basculée en DB) — l'annonce
            // reste listée, cohérent avec la base.
            if (!ok) return null;
            _listings.Remove(l);
            return l;
        }
        _listings.Remove(l);
        if (_repo != null)
            await _repo.UpdateAuctionAsync(new AuctionEntity
            {
                Id = l.Id, SellerId = l.SellerId, ItemId = l.ItemId,
                Quantity = l.Quantity, Price = (int)l.Price,
                ExpiresAt = l.ExpiresAt, Status = "cancelled",
            });
        return l;
    }

    /// <summary>
    /// Expire une annonce — item retourné au vendeur par mail en une
    /// transaction (<paramref name="mail"/>, même seam que
    /// <see cref="CancelAsync"/>, statut 'expired'). Sans repo mail,
    /// repli sur retrait mémoire + UPDATE d'état.
    /// </summary>
    public async Task<AuctionListing?> ExpireAsync(int listingId,
        MailRepository? mail = null,
        int durability = 1, int maxDurability = 1)
    {
        var l = _listings.FirstOrDefault(x => x.Id == listingId);
        if (l == null || l.Sold || l.ExpiresAt > DateTime.UtcNow)
            return null;
        if (mail != null)
        {
            var ok = await mail.SettleAuctionReturnAsync(l.Id,
                "expired", BuildReturnMail(l.SellerId, l),
                durability, maxDurability);
            if (!ok) return null;
            _listings.Remove(l);
            return l;
        }
        _listings.Remove(l);
        if (_repo != null)
            await _repo.UpdateAuctionAsync(new AuctionEntity
            {
                Id = l.Id, SellerId = l.SellerId, ItemId = l.ItemId,
                Quantity = l.Quantity, Price = (int)l.Price,
                ExpiresAt = l.ExpiresAt, Status = "expired",
            });
        return l;
    }

    /// <summary>Mail de retour d'une annonce terminée — flux natif
    /// « Auction House » : item joint (ligne mailitem créée par la
    /// tx), expiration 30 j comme le courrier joueur.</summary>
    private static MailRow BuildReturnMail(int sellerId,
        AuctionListing l) => new()
    {
        RecipientId = sellerId, SenderId = 0,
        SenderName = "Auction House",
        Subject = "Auction return",
        AttachedItemId = l.ItemId,
        AttachedItems = $"{l.ItemId}:{l.Quantity}",
        SentAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(30),
    };

    public IReadOnlyList<AuctionListing> ListingsFor(int sellerId)
        => _listings.Where(l => l.SellerId == sellerId).ToList();

    public int ActiveCount =>
        _listings.Count(l => !l.Sold && l.ExpiresAt > DateTime.UtcNow);
}
