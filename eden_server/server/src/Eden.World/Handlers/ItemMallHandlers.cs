using Eden.Core.Database.Repositories;
using Eden.Core.Game.Mall;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.World.Handlers;

/// <summary>
/// Item mall natif côté monde — <c>CNE_CW_ServerItemMall*</c>
/// (désasm re/ghidra/world/decompiled.c) :
///   15 AnythingReceivable → 56 [u8 bool]
///   17 ReceivableQuery    → 57 [s16 n][n×{u32 id,u16 item,u16 qty,str time}]
///   18 ReceieveAttempt    → claim state 1→2, ZW 24 vers la zone,
///                         puis 58 {s16 result, u32 receipt} (via ZW 12)
///   23 ReqBuyItem         → 63 {s16 result, i32 pvalues, i32 bonus}
///   24 ListQuery          → 62 batches + 79 limits
///   25 PointInfo          → 64 {i32 pvalues, i32 bonus}
/// Le billing externe natif (LP_DBillingServerConnection) est remplacé
/// par une déduction synchrone sur edenfr_member.tb_user (pvalues/bonus).
/// </summary>
public static class ItemMallHandlers
{
    /// <summary>Dépendances injectées par Program.cs.</summary>
    public sealed class Services
    {
        public required IItemMallRepository Mall { get; init; }
        public required IUserRepository Users { get; init; }
        public required ICharacterRepository Characters { get; init; }
        /// <summary>Catalogue validé (rechargé à la demande).</summary>
        public required ItemMallCatalog Catalog { get; set; }
        /// <summary>Id monde écrit dans item_receivable.world_id
        /// (CGlobalInfo::WorldServerID natif).</summary>
        public int WorldServerId { get; init; } = 1;
    }

    /// <summary>Enregistre les six commandes CW item mall.</summary>
    public static void Register(PacketDispatcher d, Services svc)
    {
        // ---- NCID 15 : quelque chose à réclamer ? → 56 [u8] ----
        d.Register(() => new CNE_CW_ServerItemMallAnythingReceivable(),
            async (session, msg, token) =>
            {
                var account = session.Username;
                if (account == null) return;
                var any = await svc.Mall.AnythingReceivableAsync(account, token);
                await session.SendAsync(
                    new CNC_CW_ClientItemMallAnythingReceivable
                    { Value = any ? (byte)1 : (byte)0 }, token);
            });

        // ---- NCID 17 : liste des réclamables → 57 records ----
        d.Register(() => new CNE_CW_ServerItemMallReceivableQuery(),
            async (session, msg, token) =>
            {
                var account = session.Username;
                if (account == null) return;
                var rows = await svc.Mall.GetReceivablesAsync(
                    account, msg.Field1, msg.Field2, msg.Field3, token);
                await session.SendPacketAsync(57,
                    BuildReceivableQueryBody(rows), token);
            });

        // ---- NCID 18 : tentative de réception [u32 receipt] ----
        d.Register(() => new CNE_CW_ServerItemMallReceieveAttempt(),
            async (session, msg, token) =>
            {
                var account = session.Username;
                if (account == null || session.CharacterId == null) return;
                var receiptId = (int)msg.Value;
                // ItemMallReceieveAttempt natif : state 1→2 + lecture.
                var row = await svc.Mall.ClaimReceivableAsync(
                    receiptId, account, token);
                if (row == null)
                {
                    // != 2 natif → 58 immédiat (0xb record introuvable).
                    await session.SendAsync(
                        new CNC_CW_ClientItemMallReceieveAttempt
                        { Field1 = ItemMallResult.RecordNotFound,
                          Field2 = (uint)receiptId }, token);
                    return;
                }
                var zone = ZoneLinkServer.ActiveZoneSession;
                if (zone == null)
                {
                    // Aucune zone connectée : remise en file + 58 {99}.
                    await svc.Mall.RequeueReceivableAsync(receiptId, token);
                    await session.SendAsync(
                        new CNC_CW_ClientItemMallReceieveAttempt
                        { Field1 = ItemMallResult.Failure,
                          Field2 = (uint)receiptId }, token);
                    return;
                }
                // ZW 24 : ordre de livraison à la zone (désasm natif
                // @0x80dcef0 — envoyé seulement si le perso a un node).
                await zone.SendPacketAsync(24,
                    ItemMallPackets.BuildZwDelivery(
                        (uint)receiptId, (uint)session.CharacterId.Value,
                        row.ItemId, row.ItemQuantity, row.ItemColor), token);
                // Le 58 final est émis à la réception du ZW 12
                // (ZoneLinkServer.HandleItemMallDeliveryResultAsync).
            });

        // ---- NCID 23 : achat → 63 {result, pvalues, bonus} ----
        d.Register(() => new CNE_CW_ServerItemMallReqBuyItem(),
            async (session, msg, token) =>
            {
                await HandleBuyAsync(session, msg, svc, token);
            });

        // ---- NCID 24 : catalogue + limites ----
        d.Register(() => new CNE_CW_ServerItemMallListQuery(),
            async (session, msg, token) =>
            {
                await SendCatalogAsync(session, svc, token);
            });

        // ---- NCID 25 : soldes → 64 {pvalues, bonus} ----
        d.Register(() => new CNE_CW_ServerItemMallPointInfo(),
            async (session, msg, token) =>
            {
                var (p1, p2) = await GetPointsAsync(session, svc, token);
                await session.SendAsync(new CNC_CW_ClientItemMallPointInfo
                { Field1 = p1, Field2 = p2 }, token);
            });
    }

    /// <summary>Flux d'achat natif (désasm @0x80dff00) :
    /// version → entrée → amount → coût → solde → niveau → limite →
    /// déduction → insert receivable → 63{0} + 79 + 56.</summary>
    private static async Task HandleBuyAsync(NetSession session,
        CNE_CW_ServerItemMallReqBuyItem msg, Services svc,
        CancellationToken token)
    {
        var account = session.Username;
        if (account == null || session.AccountId == null) return;

        // Field1 = money_unit*100 + item_group (sVar6 = /100, grp = %100).
        var moneyUnit = msg.Field1 / 100;
        var group = msg.Field1 % 100;
        var index = msg.Field2;
        var amount = msg.Field3;
        var (p1, p2) = await GetPointsAsync(session, svc, token);
        Task Reply(short code) => session.SendAsync(
            new CNC_CW_ClientItemMallReqBuyItem
            { Field1 = code, Field2 = p1, Field3 = p2 }, token);

        // Version périmée → code 2 + renvoi du catalogue (natif appelle
        // UpdatePlayerItemMallList + UpdatePlayerItemMallLimit).
        if (msg.Field4 != svc.Catalog.Version)
        {
            await Reply(ItemMallResult.VersionMismatch);
            await SendCatalogAsync(session, svc, token);
            return;
        }

        var entry = svc.Catalog.Find(group, index, moneyUnit);
        var character = session.CharacterId is { } cid
            ? await svc.Characters.GetCharacterByIdAsync(cid)
            : null;
        if (amount < 1) amount = 1;
        if (entry == null ||
            !ItemMallCatalog.ValidateBuyAmount(entry, amount))
        {
            await Reply(ItemMallResult.Failure);
            return;
        }
        if (character == null)
        {
            await Reply(ItemMallResult.Failure);
            return;
        }

        var cost = ItemMallCatalog.TotalCost(entry, amount);
        // Solde : unit 1 → pvalues (code 1), unit 2 → bonus (code 3) ;
        // unit 3 = temps de jeu — rabattu sur pvalues (pas de billing).
        var balance = moneyUnit == 2 ? p2 : p1;
        if (balance < cost)
        {
            await Reply(moneyUnit == 2
                ? ItemMallResult.InsufficientBonus
                : ItemMallResult.InsufficientPoints);
            return;
        }
        // Niveau : level_limit != 0 et perso sous la limite → code 7.
        if (entry.LevelLimit != 0 && character.Level < entry.LevelLimit)
        {
            await Reply(ItemMallResult.LevelTooLow);
            return;
        }
        // Limite par compte (groupe 8 natif) : CheckLimitAmount →
        // -1 → code 5 ; sell_amount + amount > num_limit → code 6.
        var newSellAmount = 0;
        if (entry.NumLimit != 0)
        {
            var current = await svc.Mall.CheckLimitAmountAsync(
                session.AccountId.Value, group, index, moneyUnit,
                entry.ItemId, entry.ItemNum, token);
            if (current < 0)
            {
                await Reply(ItemMallResult.LimitCheckFailed);
                return;
            }
            newSellAmount = current + amount;
            if (newSellAmount > entry.NumLimit && entry.NumLimit > 0)
            {
                await Reply(ItemMallResult.LimitExceeded);
                return;
            }
        }
        // Déduction synchrone (remplace le billing LP_ natif).
        if (!await svc.Users.TryDeductItemMallPointsAsync(
                account, moneyUnit, cost, token))
        {
            await Reply(moneyUnit == 2
                ? ItemMallResult.InsufficientBonus
                : ItemMallResult.InsufficientPoints);
            return;
        }
        // Achat : insert receivable state=1 (réclamable direct).
        var receiptId = await svc.Mall.InsertReceivableAsync(
            account, entry.ItemId,
            ItemMallCatalog.DeliveredQuantity(entry, amount),
            entry.ItemColor, cost, amount, svc.WorldServerId,
            character.GivenName, msg.Field6, token);
        if (entry.NumLimit != 0)
            await svc.Mall.AddLimitAmountAsync(
                session.AccountId.Value, group, index, moneyUnit,
                entry.ItemId, entry.ItemNum, amount, token);

        // 63 {0, soldes frais} puis push des nouvelles limites (79) et
        // du témoin « à réclamer » (56) — comportement natif.
        (p1, p2) = await GetPointsAsync(session, svc, token);
        await Reply(ItemMallResult.Ok);
        if (entry.NumLimit != 0)
            await session.SendPacketAsync(79, ItemMallPackets.BuildLimit(
                svc.Catalog.Version,
                await svc.Mall.GetLimitAmountsAsync(
                    session.AccountId.Value, token)), token);
        await session.SendAsync(
            new CNC_CW_ClientItemMallAnythingReceivable { Value = 1 },
            token);
        EdenLog.Write($"[World] ItemMall buy : {account} " +
            $"grp={group} idx={index} item={entry.ItemId}×{amount} " +
            $"cost={cost} mu={moneyUnit} → receipt #{receiptId}");
    }

    /// <summary>Envoie le catalogue (62 batches) puis les limites (79)
    /// — équivalent UpdatePlayerItemMallList + UpdatePlayerItemMallLimit.</summary>
    private static async Task SendCatalogAsync(NetSession session,
        Services svc, CancellationToken token)
    {
        foreach (var body in ItemMallPackets.BuildCatalogBatches(svc.Catalog))
            await session.SendPacketAsync(62, body, token);
        if (session.AccountId is { } acc)
            await session.SendPacketAsync(79, ItemMallPackets.BuildLimit(
                svc.Catalog.Version,
                await svc.Mall.GetLimitAmountsAsync(acc, token)), token);
    }

    /// <summary>Soldes (pvalues, bonus) du compte — CPlayerEntity
    /// +0x44/+0x48 natifs, lus sur edenfr_member.tb_user par mid.</summary>
    private static async Task<(int, int)> GetPointsAsync(
        NetSession session, Services svc, CancellationToken token)
    {
        if (session.Username is { } account)
        {
            var pts = await svc.Users.GetItemMallPointsAsync(account, token);
            if (pts != null) return (pts.Value.Pvalues, pts.Value.Bonus);
        }
        return (0, 0);
    }

    /// <summary>Body du 57 : [s16 count] puis count×{u32 id, u16 item,
    /// u16 qty, str "YYYY-MM-DD HH:MM:SS"} — Serialize natif
    /// @0x80cd290 (records d'un deque à chunks de 0x80).</summary>
    private static byte[] BuildReceivableQueryBody(
        IReadOnlyList<ItemReceivableRow> rows)
    {
        var s = new Eden.Core.Networking.OutBinStream();
        // s16 = nombre de records (taille totale du deque natif).
        s.WriteShort((short)rows.Count);
        foreach (var r in rows)
        {
            // u32 receipt id.
            s.WriteInt(r.Id);
            // u16 item_id.
            s.WriteShort((short)r.ItemId);
            // u16 item_quantity.
            s.WriteShort((short)r.ItemQuantity);
            // str purchase_time au format texte PostgreSQL natif.
            s.WriteString(r.PurchaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        return s.ToArray();
    }
}
