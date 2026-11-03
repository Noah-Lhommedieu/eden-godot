namespace Eden.Core.Game.Services;

/// <summary>
/// Boutiques AP (Aeria Points) / EP (Eden Points) — correspond a
/// CItemShop + S_ItemMall.ini (meme schema 78 colonnes que S_Item.ini).
/// Gere les soldes de monnaie premium, l'achat et les promos.
/// </summary>
public sealed class ItemMallService
{
    public enum Currency { AP, EP }

    public sealed class MallItem
    {
        public int ItemId { get; set; }
        public long PriceAP { get; set; }
        public long PriceEP { get; set; }
        public int Category { get; set; }
        public DateTime? SaleEndsAt { get; set; }
        public double Discount { get; set; } = 1.0;
        public int? PurchaseLimit { get; set; }
    }

    private readonly Dictionary<int, MallItem> _catalog = new();
    private readonly Dictionary<int, Dictionary<Currency, long>> _wallets = new();
    private readonly Dictionary<int, Dictionary<int, int>> _purchaseCounts = new();

    public void AddToCatalog(MallItem item) => _catalog[item.ItemId] = item;

    public int CatalogCount => _catalog.Count;

    public long GetBalance(int charId, Currency currency)
        => _wallets.TryGetValue(charId, out var w)
            ? w.GetValueOrDefault(currency) : 0;

    public void Credit(int charId, Currency currency, long amount)
    {
        var w = _wallets.TryGetValue(charId, out var existing)
            ? existing : _wallets[charId] = new Dictionary<Currency, long>();
        w[currency] = w.GetValueOrDefault(currency) + amount;
    }

    /// <summary>
    /// Prix effectif (avec discount si promo active).
    /// </summary>
    public long EffectivePrice(int itemId, Currency currency)
    {
        if (!_catalog.TryGetValue(itemId, out var item)) return 0;
        var basePrice = currency == Currency.AP ? item.PriceAP : item.PriceEP;
        var onSale = item.SaleEndsAt == null || item.SaleEndsAt > DateTime.UtcNow;
        return (long)(basePrice * (onSale ? item.Discount : 1.0));
    }

    /// <summary>
    /// Achat — debite le wallet, applique la limite d'achat.
    /// </summary>
    public bool Purchase(int charId, int itemId, Currency currency)
    {
        if (!_catalog.TryGetValue(itemId, out var item)) return false;

        var counts = _purchaseCounts.TryGetValue(charId, out var c)
            ? c : _purchaseCounts[charId] = new Dictionary<int, int>();
        if (item.PurchaseLimit.HasValue &&
            counts.GetValueOrDefault(itemId) >= item.PurchaseLimit.Value)
            return false;

        var price = EffectivePrice(itemId, currency);
        if (GetBalance(charId, currency) < price) return false;

        Credit(charId, currency, -price);
        counts[itemId] = counts.GetValueOrDefault(itemId) + 1;
        return true;
    }
}
