using Eden.Core.Game.Trade;
using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service des échanges P2P — correspond à CTrade.
/// Cycle : Request → Accept → AddItem/RemoveItem/ModifyGold → Ready →
/// Confirm → Execute (transfert réel via InventoryService).
/// </summary>
public sealed class TradeService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryService _inventoryService;
    private readonly Func<int, Task<long>>? _getGold;
    private readonly Func<int, long, Task>? _setGold;
    /// <summary>Callback (charId, itemId) invoqué à chaque mutation
    /// d'inventaire liée au trade — la Zone le branche sur le
    /// `update_quest_item` natif (ItemCollect).</summary>
    public Action<int, int>? OnInventoryChanged { get; set; }
    /// <summary>Distance max d'échange — `CloseEnough(cible, 10.0)`
    /// dans `CPlayerFactory::RequestTrade` (désasm zone:138245).</summary>
    public const double MaxTradeDistance = 10.0;

    public TradeService(ITradeRepository tradeRepository,
        IInventoryRepository inventory,
        InventoryService inventoryService,
        Func<int, Task<long>>? getGold = null,
        Func<int, long, Task>? setGold = null)
    {
        _tradeRepository = tradeRepository;
        _inventory = inventory;
        _inventoryService = inventoryService;
        _getGold = getGold;
        _setGold = setGold;
    }

    /// <summary>Demande un échange — CNE_CZ_ZoneServerTradeRequest.</summary>
    public async Task<(bool Success, string ErrorMessage)> RequestTradeAsync(
        int initiatorId, int partnerId, double distance)
    {
        if (distance > MaxTradeDistance)
            return (false, "Partner too far");
        if (await _tradeRepository.GetActiveTradeAsync(initiatorId, partnerId) != null)
            return (false, "Already in trade");

        await _tradeRepository.CreateTradeAsync(new TradeEntity
        {
            InitiatorId = initiatorId,
            PartnerId = partnerId,
            CreatedAt = DateTime.UtcNow,
        });
        return (true, string.Empty);
    }

    /// <summary>Accepte une demande — CNE_CZ_ZoneServerTradeRequestAccept.</summary>
    public async Task<bool> AcceptTradeAsync(int initiatorId, int partnerId)
        => await _tradeRepository.GetActiveTradeAsync(initiatorId, partnerId) != null;

    /// <summary>Trade actif d'un joueur (un joueur n'a qu'un trade à la fois).</summary>
    private async Task<TradeEntity?> GetTrade(int playerId, int _ignored)
        => await _tradeRepository.GetActiveTradeByPlayerAsync(playerId);

    /// <summary>Id du partenaire de trade de ce joueur, null si pas en trade.</summary>
    public async Task<int?> GetTradePartnerAsync(int playerId)
    {
        var t = await _tradeRepository.GetActiveTradeByPlayerAsync(playerId);
        if (t == null) return null;
        return t.InitiatorId == playerId ? t.PartnerId : t.InitiatorId;
    }

    /// <summary>
    /// Ajoute un item à la fenêtre de trade — réserve la quantité de l'inventaire.
    /// CNE_CZ_ZoneServerTradeAddItem (ushort bag, slot, tradeSlot, count).
    /// </summary>
    public async Task<bool> AddItemAsync(int playerId, int partnerId,
        int bagId, int invSlot, int quantity)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null || ConfirmedBy(trade, playerId)) return false;

        var inv = await _inventory.GetItemAsync(playerId, bagId, invSlot);
        if (inv == null || inv.IsEquipped || inv.IsBound || quantity <= 0)
            return false;

        // Stackable natif (non équipable + cap>1) : `durability` porte
        // le count. Un équipement part ENTIÈREMENT dans le trade —
        // sa durability est une usure, pas une quantité (l'ancien code
        // décrémentait Quantity sans toucher durability → dupe).
        var stackable = _inventoryService.IsStackable(inv.ItemId);
        var count = stackable ? quantity : 1;
        if (stackable && inv.Quantity < count) return false;

        // Débiter l'inventaire (l'item est "dans le trade") —
        // débit gardé + UPDATE active_trades en UNE transaction sur
        // repo SQL (un crash entre les deux laissait l'item débité
        // sans trace de réservation).
        var newQuantity = stackable ? inv.Quantity - count : 0;
        if (_tradeRepository is TradeRepository sql)
        {
            // itemId transmis : la réservation est écrite dans
            // trade_items dans la même tx que le débit — le settle
            // recharge l'entité et ne livre que ces lignes.
            var reserved = await sql.ReserveItemAsync(trade, playerId,
                bagId, invSlot, count, newQuantity, inv.ItemId);
            if (!reserved) return false;
        }
        else
        {
            inv.Quantity = newQuantity;
            if (inv.Quantity <= 0)
                await _inventory.RemoveItemAsync(playerId, bagId, invSlot);
            else
            {
                // durability = count persisté pour les stackables.
                if (inv.Durability == inv.Quantity + count)
                    inv.Durability = inv.Quantity;
                await _inventory.UpdateItemAsync(inv);
            }
            if (!await _tradeRepository.UpdateTradeAsync(trade))
                return false;
        }
        OnInventoryChanged?.Invoke(playerId, inv.ItemId);

        ItemsOf(trade, playerId).Add(new TradeItem
        {
            Slot = invSlot, ItemId = inv.ItemId, Quantity = count
        });
        return true;
    }

    /// <summary>Retire un item du trade — le rend à l'inventaire.</summary>
    public async Task<bool> RemoveItemAsync(int playerId, int partnerId, int tradeIndex)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null || ConfirmedBy(trade, playerId)) return false;

        var items = ItemsOf(trade, playerId);
        if (tradeIndex < 0 || tradeIndex >= items.Count) return false;
        var item = items[tradeIndex];

        var add = await _inventoryService.AddItemAsync(
            playerId, item.ItemId, item.Quantity);
        if (!add.Success) return false;
        OnInventoryChanged?.Invoke(playerId, item.ItemId);

        if (_tradeRepository is TradeRepository sql)
            await sql.RemoveTradeItemAsync(
                trade.Id, playerId, tradeIndex);
        items.RemoveAt(tradeIndex);
        return await _tradeRepository.UpdateTradeAsync(trade);
    }

    /// <summary>Modifie l'or proposé — CNE_CZ_ZoneServerTradeModifyGold.</summary>
    public async Task<bool> ModifyGoldAsync(int playerId, int partnerId, long gold)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null || ConfirmedBy(trade, playerId) || gold < 0) return false;
        if (_getGold != null && await _getGold(playerId) < gold) return false;

        if (trade.InitiatorId == playerId) trade.InitiatorGold = (int)gold;
        else trade.PartnerGold = (int)gold;
        return await _tradeRepository.UpdateTradeAsync(trade);
    }

    /// <summary>Reset le contenu du trade — CNE_CZ_ZoneServerTradeReset.</summary>
    public async Task<bool> ResetTradeAsync(int playerId, int partnerId)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null) return false;
        await ReturnItemsAsync(trade, trade.InitiatorId);
        await ReturnItemsAsync(trade, trade.PartnerId);
        if (_tradeRepository is TradeRepository sql)
            await sql.ClearTradeItemsAsync(trade.Id);
        trade.InitiatorGold = trade.PartnerGold = 0;
        trade.InitiatorConfirmed = trade.PartnerConfirmed = false;
        return await _tradeRepository.UpdateTradeAsync(trade);
    }

    /// <summary>Confirme le trade — exécution quand les deux ont confirmé.</summary>
    public async Task<bool> ConfirmTradeAsync(int playerId, int partnerId)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null) return false;

        if (trade.InitiatorId == playerId) trade.InitiatorConfirmed = true;
        else trade.PartnerConfirmed = true;

        if (!trade.InitiatorConfirmed || !trade.PartnerConfirmed)
            return await _tradeRepository.UpdateTradeAsync(trade);

        return await ExecuteTradeAsync(trade);
    }

    /// <summary>Annule — rend les items aux deux joueurs.</summary>
    public async Task<bool> CancelTradeAsync(int playerId, int partnerId)
    {
        var trade = await GetTrade(playerId, partnerId);
        if (trade == null) return false;
        await ReturnItemsAsync(trade, trade.InitiatorId);
        await ReturnItemsAsync(trade, trade.PartnerId);
        return await _tradeRepository.DeleteTradeAsync(trade.Id);
    }

    /// <summary>
    /// Exécute le transfert. Sur repo SQL tout est atomique dans
    /// <see cref="TradeRepository.SettleTradeAsync"/> : deltas d'or
    /// gardés + livraison croisée des items réservés + suppression de
    /// la session en UNE transaction — la fenêtre « or réglé,
    /// livraison partielle » de l'ancien chemin split est fermée ;
    /// un refus (solde, sac plein, session close) laisse le trade
    /// ouvert et les items réservés restituables via
    /// <see cref="CancelTradeAsync"/>. Sur repo non-SQL (tests),
    /// repli sur le chemin historique : or d'abord puis items croisés
    /// puis delete — chaque étape reste annulable par échec tôt.
    /// </summary>
    private async Task<bool> ExecuteTradeAsync(TradeEntity trade)
    {
        if (_tradeRepository is TradeRepository sql)
        {
            var settled = await sql.SettleTradeAsync(trade,
                DeliveriesOf(trade.InitiatorItems),
                DeliveriesOf(trade.PartnerItems));
            if (settled == null) return false;
            foreach (var item in trade.InitiatorItems)
                OnInventoryChanged?.Invoke(trade.PartnerId, item.ItemId);
            foreach (var item in trade.PartnerItems)
                OnInventoryChanged?.Invoke(trade.InitiatorId, item.ItemId);
            return true;
        }

        if (trade.InitiatorGold > 0 || trade.PartnerGold > 0)
        {
            // Sans repo SQL les delegates get/set portent le
            // règlement ; s'ils manquent aussi l'or ne peut pas
            // bouger → fail closed (les items ne sont pas livrés).
            if (!await SettleGoldAsync(trade))
                return false;
        }

        // Items croisés — fallback en mémoire/tests : chaque
        // statement reste atomique mais le lot n'est pas joignable
        // (pas de seam de connexion sur les mocks).
        foreach (var item in trade.InitiatorItems)
        {
            await _inventoryService.AddItemAsync(
                trade.PartnerId, item.ItemId, item.Quantity);
            OnInventoryChanged?.Invoke(trade.PartnerId, item.ItemId);
        }
        foreach (var item in trade.PartnerItems)
        {
            await _inventoryService.AddItemAsync(
                trade.InitiatorId, item.ItemId, item.Quantity);
            OnInventoryChanged?.Invoke(trade.InitiatorId, item.ItemId);
        }

        return await _tradeRepository.DeleteTradeAsync(trade.Id);
    }

    /// <summary>Projette la fenêtre de trade en lots livrables :
    /// gabarit de pile S_Item (<see cref="InventoryService.MaxStackOf"/>
    /// si stackable sinon 1) + usure template pour la livraison SQL.
    /// </summary>
    private List<TradeRepository.TradeDeliveryItem> DeliveriesOf(
        List<TradeItem> items) => items.Select(i =>
        {
            var stackable = _inventoryService.IsStackable(i.ItemId);
            return new TradeRepository.TradeDeliveryItem(i.ItemId,
                i.Quantity,
                stackable ? _inventoryService.MaxStackOf(i.ItemId) : 1,
                stackable ? 0 : _inventoryService.MaxDurabilityOf(
                    i.ItemId));
        }).ToList();

    /// <summary>
    /// Règlement de l'or du trade — delta net par joueur
    /// (initiateur : +PartnerGold −InitiatorGold). Sur repo SQL la
    /// paire de deltas est une transaction unique ; sinon fallback
    /// absolu sur les delegates (tests en mémoire).
    /// </summary>
    private async Task<bool> SettleGoldAsync(TradeEntity trade)
    {
        var initiatorDelta = (long)trade.PartnerGold - trade.InitiatorGold;
        var partnerDelta = (long)trade.InitiatorGold - trade.PartnerGold;

        if (_tradeRepository is TradeRepository sql)
            return await sql.SettleGoldAsync(trade.InitiatorId,
                initiatorDelta, trade.PartnerId, partnerDelta);

        // Fallback non-SQL : garde de solde puis écriture absolue.
        if (_getGold == null || _setGold == null)
            return false;
        var goldA = await _getGold(trade.InitiatorId);
        var goldB = await _getGold(trade.PartnerId);
        if (goldA + initiatorDelta < 0 || goldB + partnerDelta < 0)
            return false;
        await _setGold(trade.InitiatorId, goldA + initiatorDelta);
        await _setGold(trade.PartnerId, goldB + partnerDelta);
        return true;
    }

    /// <summary>
    /// Achat sur étal joueur (VendorBuy) : transfert de la ligne
    /// inventaire + règlement de l'or en une transaction quand le
    /// repo trade est SQL. Retourne null quand le repo n'est pas SQL
    /// — l'appelant conserve alors son chemin legacy.
    /// </summary>
    public async Task<(bool Ok, long BuyerGold, long SellerGold)?>
        TryStallPurchaseAsync(int itemRowId, int buyerId, int sellerId,
            int bagId, int slot, int price)
    {
        if (_tradeRepository is not TradeRepository sql)
            return null;
        return await sql.SettleStallPurchaseAsync(itemRowId, buyerId,
            sellerId, bagId, slot, price);
    }

    /// <summary>Rend les items de la fenêtre de trade à leur propriétaire.</summary>
    private async Task ReturnItemsAsync(TradeEntity trade, int playerId)
    {
        var items = ItemsOf(trade, playerId);
        foreach (var item in items)
        {
            await _inventoryService.AddItemAsync(
                playerId, item.ItemId, item.Quantity);
            OnInventoryChanged?.Invoke(playerId, item.ItemId);
        }
        items.Clear();
    }

    private static List<TradeItem> ItemsOf(TradeEntity t, int playerId) =>
        t.InitiatorId == playerId ? t.InitiatorItems : t.PartnerItems;

    private static bool ConfirmedBy(TradeEntity t, int playerId) =>
        t.InitiatorId == playerId ? t.InitiatorConfirmed : t.PartnerConfirmed;
}
