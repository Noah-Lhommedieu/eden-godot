using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service des shops PNJ — achat/vente/rachat d'items.
/// Correspond à CVendor / shop lapis. La liste de rachat (buyback)
/// conserve les derniers items vendus comme sur l'original.
/// </summary>
public sealed class ShopService
{
    private readonly InventoryService _inventory;
    private readonly Dictionary<int, ShopAndMiscParsers.StoreDef> _stores;
    private readonly Dictionary<int, ItemParser.ItemDef> _itemDefs;
    private readonly ParameterParser.ParameterSet _parameters;
    private readonly Func<int, Task<long>> _getGold;
    private readonly Func<int, long, Task> _setGold;
    /// <summary>Delta d'or atomique (<c>ICharacterRepository.
    /// AddGoldAsync</c> en production) — optionnel ; sans lui
    /// <see cref="ApplyGoldDeltaAsync"/> retombe sur
    /// get→set absolu (tests).</summary>
    private readonly Func<int, long, Task>? _addGold;
    /// <summary>File de rachat par joueur — équivalent de la deque
    /// buyback native (mémoire mission, « Temp UID » libérée à la
    /// déconnexion → scope session, pas de table SQL).</summary>
    private readonly Dictionary<int, List<BuybackEntry>> _buybacks = new();
    /// <summary>Cap native : `while (10 &lt; size) pop_front` dans
    /// l'Execute ZMis ZoneServerBuyBackList — 10 entrées max.</summary>
    private const int MaxBuybackEntries = 10;

    /// <summary>Port d'accès aux monnaies typées des fame shops —
    /// `BuyItems` natif @0x080aca40 : quand le priceType de l'item
    /// (S_Item col30 → GameData::CItemData+0x78) n'est pas 1 (or),
    /// le débit vise une autre bourse — lifelong gold (5), trésor
    /// de famille (0xa), or colisée (0xb), special_coins
    /// (0xc-0x11→types 0-5, 0x15-0x17→types 9-11 via
    /// `CCharacter::GetCoinTypeByPriceType` @0x8409e40), war coin
    /// (0x13), alpaca coin (0x14). Le natif relaie la plupart vers
    /// la mission (TC BuySpecialCoinItem/BuyWarItem/…) ; ici la
    /// zone possède les bourses et débite directement.
    /// <c>GetBalance</c>(charId, priceType) → solde, ou null quand
    /// la monnaie n'est pas gérée (achat refusé — ex. lover 4 sans
    /// stockage). <c>Debit</c>(charId, priceType, montant) débite
    /// après octroi de l'item et émet les updates client.</summary>
    public sealed record ShopCurrencyPort(
        Func<int, int, Task<long?>> GetBalance,
        Func<int, int, long, Task> Debit);

    /// <summary>Entrée de rachat — snapshot complet de l'item vendu
    /// (sockets/combo/enchant conservés pour le rachat, comme le record
    /// SItemData que la mission native garde en mémoire) + prix payé
    /// (`total_price` de la text-command `Sell Item`).</summary>
    public sealed record BuybackEntry(
        Eden.Core.Game.Inventory.ItemEntity Item, int Quantity,
        long PaidPrice)
    {
        public int ItemId => Item.ItemId;
    }

    public ShopService(InventoryService inventory,
        IEnumerable<ShopAndMiscParsers.StoreDef> stores,
        IEnumerable<ItemParser.ItemDef> itemDefs,
        ParameterParser.ParameterSet parameters,
        Func<int, Task<long>> getGold,
        Func<int, long, Task> setGold,
        Func<int, long, Task>? addGold = null)
    {
        _inventory = inventory;
        _stores = stores.ToDictionary(s => s.Id);
        _itemDefs = itemDefs.ToDictionary(d => d.Id);
        _parameters = parameters;
        _getGold = getGold;
        _setGold = setGold;
        _addGold = addGold;
    }

    /// <summary>
    /// Applique un delta d'or — <c>gold = gold + @d</c> atomique via
    /// <see cref="ICharacterRepository.AddGoldAsync"/> quand le
    /// delegate <c>setGold</c> cible un repo caractères (production :
    /// <c>characterRepository.SetGoldAsync</c> → Target = le repo).
    /// Fallback absolu <c>getGold→setGold</c> pour les lambdas de
    /// test. Jamais de régression absolue en production : la paire
    /// get/set y perdait les écritures concurrentes.
    /// </summary>
    private async Task ApplyGoldDeltaAsync(int characterId, long delta)
    {
        if (_addGold != null)
        {
            await _addGold(characterId, delta);
            return;
        }
        if (_setGold.Target is ICharacterRepository characters)
        {
            await characters.AddGoldAsync(characterId, delta);
            return;
        }
        var current = await _getGold(characterId);
        await _setGold(characterId, current + delta);
    }

    /// <summary>Contenu d'un shop (null si le PNJ n'est pas vendeur).</summary>
    public ShopAndMiscParsers.StoreDef? GetStore(int storeId) =>
        _stores.TryGetValue(storeId, out var s) ? s : null;

    /// <summary>Prix d'achat d'un item = prix système S_Item col 31
    /// (lapis CItem+0x10). `BuyItems` natif @0x80aca40 :
    /// `arrondi(SysPrice × remise)` — remise prestige absente → 1.0.</summary>
    public long BuyPriceOf(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var d) ? d.SellPrice : 0;

    /// <summary>Prix de vente unitaire natif
    /// (`CNE_CZ_ZoneServerSellItems::Execute`) :
    /// `floor(SysPrice × param346 × curDur/maxDur)` — le facteur
    /// durabilité ne vaut que pour l'équipement (EquipLoc>0) ; sur un
    /// stack la « durabilité » porte le compte (modèle natif), pas
    /// l'usure → facteur 1. Retourne 0 si `SysPrice × facteur < 1.0`
    /// (vente refusée par le natif, texte 0x6e0).</summary>
    public long SellPriceOf(Eden.Core.Game.Inventory.ItemEntity item)
    {
        if (!_itemDefs.TryGetValue(item.ItemId, out var d)
            || d.SellPrice <= 0)
            return 0;
        var basePrice = d.SellPrice *
            _parameters.Get(ParameterParser.SellBackFactor);
        if (basePrice < 1.0f) return 0;
        var durFactor = d.EquipLoc > 0 && item.MaxDurability > 0
            ? (double)Math.Max(item.Durability, 0) / item.MaxDurability
            : 1.0;
        return (long)(basePrice * durFactor);
    }

    /// <summary>
    /// Achat d'un item au shop — CNE_CZ_ZoneServerBuyItems.
    /// Vérifie le solde, débite l'or, ajoute l'item.
    /// <c>Touched</c> = slots modifiés (Merged=true → le natif émet
    /// 291 ModifyItemDurability, pas un 274 — un 274 sur un slot
    /// occupé fait matérialiser un clone sur un emplacement déjà
    /// rempli côté client → crash live constaté).
    /// </summary>
    public async Task<(bool Success, string Error, int ItemId,
        int? BagId, int? Slot,
        List<(int BagId, int Slot, bool Merged)> Touched)> BuyItemAsync(
        int characterId, int storeId, int storeSlot, int count,
        ShopCurrencyPort? currency = null)
    {
        List<(int BagId, int Slot, bool Merged)> touched = new();
        var store = GetStore(storeId);
        if (store == null || count <= 0)
            return (false, "Invalid store", 0, null, null, touched);
        if (storeSlot < 0 || storeSlot >= store.Items.Count)
            return (false, "Invalid slot", 0, null, null, touched);
        var (itemId, _, flag) = store.Items[storeSlot];
        if (flag == 0) return (false, "Not for sale", 0, null, null,
            touched);

        var price = BuyPriceOf(itemId) * count;
        // priceType (col30 → +0x78) ≠ 1 → bourse typée
        // (BuyItems @0x080aca40). priceType 0/absent → refus natif.
        var priceType = _itemDefs.TryGetValue(itemId, out var pdef)
            ? pdef.PriceType : (byte)1;
        if (priceType != 1)
        {
            var balance = currency == null ? (long?)null
                : await currency.GetBalance(characterId, priceType);
            if (balance == null || currency == null)
                return (false, $"Currency {priceType} unsupported",
                    0, null, null, touched);
            if (balance.Value < price)
                return (false, "Not enough currency", 0, null, null,
                    touched);
            var curAdd = await _inventory.AddItemAsync(characterId,
                itemId, count, 0, 0, false, touched);
            if (!curAdd.Success)
                return (false, "Inventory full", 0, null, null,
                    touched);
            await currency.Debit(characterId, priceType, price);
            return (true, string.Empty, itemId, curAdd.BagId,
                curAdd.Slot, touched);
        }

        var gold = await _getGold(characterId);
        if (gold < price)
        {
            Eden.Core.Logging.EdenLog.Write(
                $"[Shop] BuyItem diag char={characterId} item={itemId} " +
                $"price={price} gold={gold} sellPrice={BuyPriceOf(itemId)}");
            return (false, "Not enough gold", 0, null, null, touched);
        }

        var add = await _inventory.AddItemAsync(characterId, itemId,
            count, 0, 0, false, touched);
        if (!add.Success) return (false, "Inventory full", 0, null,
            null, touched);

        // Débit en delta atomique (gold + @d) — l'ancien
        // get→set absolu perdait les écritures concurrentes.
        await ApplyGoldDeltaAsync(characterId, -price);
        return (true, string.Empty, itemId, add.BagId, add.Slot,
            touched);
    }

    /// <summary>
    /// Vente d'un item au shop — CNE_CZ_ZoneServerSellItems.
    /// Débite l'item, crédite l'or, enregistre en buyback.
    /// Retourne aussi la quantité restante sur le slot (vente partielle
    /// de stack), la quantité réellement vendue et le nom pour le
    /// feedback client.
    /// </summary>
    public async Task<(bool Success, string Error, int ItemId,
        int Left, int Sold, string Name, long Paid, int MaxDur)>
        SellItemAsync(int characterId, int bagId, int slot, int count)
    {
        if (count <= 0) return (false, "Invalid count", 0, 0, 0, "", 0, 0);
        var item = await _inventory.GetItemForSaleAsync(characterId, bagId, slot);
        if (item == null) return (false, "No item", 0, 0, 0, "", 0, 0);
        // Garde native (SellItems::Execute) : les ids 40001-53000
        // (objets de quête/spéciaux — `(id+0x63bf) < 13000`) ne sont
        // pas vendables, erreur 0x25d.
        if (item.ItemId is >= 40001 and <= 53000)
            return (false, "Not sellable", 0, 0, 0, "", 0, 0);

        var unitPrice = SellPriceOf(item);
        if (unitPrice <= 0) return (false, "Not sellable", 0, 0, 0, "", 0, 0);
        // Stackable natif = non-équipable + cap>1 (même règle que
        // RemoveItemAsync) : `durability` porte le COUNT du stack.
        // Pour un équipement, `durability` = durabilité — vendre 1
        // supprime la ligne (275), pas décrémenter à 99 (le client
        // gardait l'item affiché — bug live).
        var stackable = _inventory.MaxStackOf(item.ItemId) > 1 &&
            (!_itemDefs.TryGetValue(item.ItemId, out var sd) ||
             sd.EquipLoc <= 0);
        var count2 = Math.Min(count, stackable ? item.Quantity : 1);
        var price = unitPrice * count2;

        if (!await _inventory.RemoveItemAsync(characterId, bagId, slot, count2))
            return (false, "Remove failed", 0, 0, 0, "", 0, 0);

        // Crédit en delta atomique — même raison que l'achat.
        await ApplyGoldDeltaAsync(characterId, price);

        // Enregistrer pour rachat — snapshot de l'item vendu
        // (le natif garde le record SItemData en mémoire mission).
        // Ordre insertion = ordre de la deque native (push_back,
        // pop_front au-delà de 10 — plus ancien en tête).
        if (!_buybacks.TryGetValue(characterId, out var list))
            _buybacks[characterId] = list = new List<BuybackEntry>();
        list.Add(new BuybackEntry(item, count2, price));
        while (list.Count > MaxBuybackEntries) list.RemoveAt(0);

        // Nom pour le texte natif 0xaa "%s,%d" du Execute.
        var name = _itemDefs.TryGetValue(item.ItemId, out var d)
            ? d.Name : item.ItemId.ToString();
        // MaxDur = clone+0xe natif (cap du stack) — requis par le
        // 291 de vente partielle (ItemConsume @0x0808f460).
        // `left` = count restant : 0 pour un équipement (275 natif),
        // count-sold pour un stack (291).
        var left = stackable ? item.Quantity - count2 : 0;
        return (true, string.Empty, item.ItemId,
            left, count2, name, price, item.MaxDurability);
    }

    /// <summary>Liste de rachat dans l'ordre wire natif (front→back
    /// de la deque = plus ancien → plus récent).</summary>
    public IReadOnlyList<BuybackEntry> GetBuybackList(int characterId) =>
        _buybacks.TryGetValue(characterId, out var l)
            ? l : (IReadOnlyList<BuybackEntry>)Array.Empty<BuybackEntry>();

    /// <summary>
    /// Rachat par index — `CItemFactory::BuyBackItems(char, u16 index)`
    /// @0x8092cd0 : index dans la deque → serial →
    /// `CItemDBO::GetBuyBackItem` restaure l'item et débite
    /// `total_price`. Échecs natifs : index hors bornes (texte 0x5f5),
    /// or insuffisant, inventaire plein.
    /// </summary>
    public async Task<(bool Success, string Error, int ItemId,
        int Quantity, int? BagId, int? Slot)> BuyBackItemAsync(
        int characterId, int index)
    {
        if (!_buybacks.TryGetValue(characterId, out var list)
            || index < 0 || index >= list.Count)
            return (false, "Invalid buyback index", 0, 0, null, null);
        var entry = list[index];
        // Retrait AVANT toute opération awaitée — le natif locke le
        // perso pendant le rachat (flag char+0x604 posé dans
        // BuyBackItems @0x8092cd0, relâché dans l'Execute ZMis) ; chez
        // nous l'équivalent anti-spam est de sortir l'entrée de la
        // file de façon synchrone puis de la réinsérer à sa position
        // si une étape échoue — un second clic sur la même row tombe
        // alors sur « Invalid buyback index », jamais sur un dupe.
        list.RemoveAt(index);

        var gold = await _getGold(characterId);
        if (gold < entry.PaidPrice)
        {
            list.Insert(index, entry);
            return (false, "Not enough gold", 0, 0, null, null);
        }

        var add = await _inventory.RestoreItemAsync(characterId,
            entry.Item, entry.Quantity);
        if (!add.Success)
        {
            list.Insert(index, entry);
            return (false, add.ErrorMessage, 0, 0, null, null);
        }

        // Débit en delta atomique (gold + @d).
        await ApplyGoldDeltaAsync(characterId, -entry.PaidPrice);
        return (true, string.Empty, entry.ItemId, entry.Quantity,
            add.BagId, add.Slot);
    }
}
