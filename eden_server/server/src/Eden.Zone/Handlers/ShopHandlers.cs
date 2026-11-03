using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Commerces : shops PNJ (vendor/buy/sell/buyback), hôtel des ventes,
/// transport payant (S_Transport.ini).
/// </summary>
public static class ShopHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // AskVendor : le store vient du script PNJ — #:Service <dlg> 3 <storeId>
        d.RegisterChar(() => new CNE_CZ_ZoneServerAskVendor(),
            async (session, msg, charId, token) =>
            {
                var npc = s.GameData.Npcs.FirstOrDefault(n => n.Id == msg.Value);
                session.CurrentStoreId = npc?.StoreId ?? 0;
                // npcUid conservé pour le 304 du retour onglet Items
                // (Field1 = id local NPC, désasm OpenShop @0x8579a70).
                session.CurrentShopNpcHandle = npc?.Id ?? 0;
                EdenLog.Write($"[Zone] AskVendor char={charId} npc={msg.Value} " +
                    $"({npc?.Name}) store={session.CurrentStoreId}");
                await session.SendAsync(new CNC_CZ_ClientEnterShop(), token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerBuyItems(),
            async (session, msg, charId, token) =>
            {
                // Wire natif (Execute @0x823f1b0 → CItemFactory) :
                //   [u16 storeSlot][u16 count][u8 buybackFlag]
                // buybackFlag!=0 → BuyBackItems(char, u16 index)
                // @0x8092cd0 : storeSlot = index dans la file buyback.
                if (msg.Count != 0)
                {
                    var (okBb, errBb, bbItemId, bbQty, bbBag, bbSlot) =
                        await s.Shops.BuyBackItemAsync(charId,
                            msg.ShopIndex);
                    EdenLog.Write($"[Zone] BuyBack char={charId} " +
                        $"index={msg.ShopIndex} -> {(okBb ? "ok" : errBb)}");
                    if (!okBb) return;
                    // 274 ClientPutItemIntoInventory — ordre natif
                    // [id][bag][slot][dur][maxdur] (PutIntoContainer
                    // @0x809ea00) ; l'entité relue donne la durability
                    // réelle (count pour un stack mergé) + metas.
                    var bbItem = bbBag != null && bbSlot != null
                        ? await s.InventoryRepo.GetItemAsync(
                            charId, bbBag.Value, bbSlot.Value)
                        : null;
                    if (bbItem != null)
                        await session.SendPacketAsync(274,
                            Codecs.NativeItemCodec.BuildPutItem(bbItem),
                            token);
                    // 512 flag=2 après rachat réussi — natif
                    // CNE_ZMis_ZoneServerBuyBackGetItem::CostMoney
                    // @0x830b630 : erase du serial dans la deque
                    // char+0x5d0, « Delete Item in BuyBackQuery » vers
                    // la mission, puis CNC_CZ_ClientBuyBackList flag=2
                    // avec l'index dans le s16 terminal (+0x17c). Le
                    // client FUN_00a7aa50 efface la row à cet index —
                    // sans lui la row fantôme restait cliquable
                    // (symptôme live : l'item racheté ne disparaît
                    // pas de la liste).
                    await session.SendPacketAsync(512,
                        Codecs.BuybackPackets.BuildClientBuyBackRemove(
                            msg.ShopIndex),
                        token);
                    await SendGoldAttrAsync(session, charId, s, token);
                    if (bbItem != null)
                        // 146 : le rachat peut remplir un objectif
                        // ItemCollect (UpdateQuestItemStatus natif).
                        s.NotifyItemChanged(charId, bbItem.ItemId);
                    return;
                }
                var (success, err, itemId, bag, slot, touched) =
                    await s.Shops.BuyItemAsync(
                        charId, session.CurrentStoreId, msg.ShopIndex,
                        msg.ItemIndex);
                EdenLog.Write($"[Zone] BuyItem char={charId} store={session.CurrentStoreId} " +
                    $"slot={msg.ShopIndex} x{msg.ItemIndex} -> {(success ? "ok" : err)}");
                if (!success) return;
                // Notifications natives (désasm BuyItems @0x80aca40) :
                // stack mergé → 291 ModifyItemDurability sur le stack
                // existant ; nouveau slot → 274 ClientPutItemInto-
                // Inventory. ERREUR corrigée : un 274 sur un slot déjà
                // occupé faisait matérialiser un clone au-dessus de
                // l'existant → crash client (bug live achat boutique).
                await Eden.Zone.Codecs.NativeItemCodec
                    .EmitItemSlotsAsync(session, s.InventoryRepo,
                        charId, touched, token);
                // 308 ClientBuyUp (corps vide — désasm : Serialize = return)
                await session.SendAsync(new CNC_CZ_ClientBuyUp(), token);
                // 280 attr 0x02 = or (GainGold natif émet cette attr)
                await SendGoldAttrAsync(session, charId, s, token);
                // 146 : l'achat peut remplir un objectif ItemCollect
                // (UpdateQuestItemStatus natif @0x8096b10).
                s.NotifyItemChanged(charId, itemId);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerSellItems(),
            async (session, msg, charId, token) =>
            {
                // Wire natif : [i32 n][n × (u16 container, u16 slot, u16 count)]
                // Par item vendu l'Execute natif émet :
                //   · texte 0xaa "%s,%d" (nom,quantité) — feedback "sold" ;
                //   · EradicateItem → 275 ClientDropItem
                //     `[s16 0][s16 loc][s16 contIdx]` (slot vidé) ;
                //   · sur stack partiel → 291 ModifyItemDurability ;
                // puis GainGold (280) et ClientSellOut (309). Sans le
                // 275 l'item restait affiché dans le sac après vente.
                var any = false;
                var soldThisCall = 0;
                for (var i = 0; i < msg.Items.Count; i++)
                {
                    var (success, err, soldId, left, sold, name, _,
                            maxDur) =
                        await s.Shops.SellItemAsync(charId,
                            msg.Items[i].Container, msg.Items[i].Slot,
                            i < msg.Counts.Count ? msg.Counts[i]
                                : (ushort)1);
                    if (!success)
                    {
                        EdenLog.Write(
                            $"[Zone] SellItem échec : {err}");
                        continue;
                    }
                    any = true;
                    soldThisCall++;
                    // Texte natif "%s,%d" (vfunc+0x58 → TextOutputIndex
                    // 0xaa) — nom d'item + QUANTITÉ vendue (désasm
                    // Execute @0x08287160 : équipe→1, stack→count),
                    // pas le prix.
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        {
                            Field1 = 0xaa,
                            Field2 = $"{name},{sold}",
                        }, token);
                    if (left > 0)
                        // Stack partiellement vendu — 291 natif
                        // `[contType][loc][nouveauCount][maxDur]
                        // [contIdx][0]` (ItemConsume @0x0808f460 :
                        // +0x8=clone+0xc count, +0xa=clone+0xe max).
                        // Field4=0 précédemment → le client ignorait
                        // l'update, compteur non rafraîchi.
                        await session.SendAsync(
                            new CNC_CZ_ClientModifyItemDurability
                            {
                                Field1 = 0,
                                Field2 = (short)msg.Items[i].Slot,
                                Field3 = (short)left,
                                Field4 = (short)maxDur,
                                Field5 = (short)msg.Items[i].Container,
                                Field6 = 0,
                            }, token);
                    else
                        // Slot vidé — 275 natif `[s16 0][s16 loc]
                        // [s16 contIdx]` de l'Execute (EradicateItem).
                        await session.SendAsync(
                            new CNC_CZ_ClientDropItem
                            {
                                Field1 = 0,
                                Field2 = (short)msg.Items[i].Slot,
                                Field3 = (short)msg.Items[i].Container,
                            }, token);
                    // 146 : la vente peut casser un objectif
                    // ItemCollect déjà rempli.
                    s.NotifyItemChanged(charId, soldId);
                }
                if (!any) return;
                // Ordre natif : 280 UpdateAttr émis dans GainGold
                // (@0x0842a7f0, pendant le traitement), puis 309
                // ClientSellOut corps vide EN DERNIER (miroir de
                // ClientBuyUp en fin de BuyItems @0x080aca40).
                await SendGoldAttrAsync(session, charId, s, token);
                await session.SendAsync(new CNC_CZ_ClientSellOut(), token);
                // 512 flag=1 = DELTA uniquement (preuve double) :
                //  · relais zone CNE_ZMis_ZoneServerBuyBackList::
                //    Execute @0x830c970 — flag==1 → push des serials
                //    de la mission dans la deque char+0x5d0 (ce sont
                //    les NOUVEAUX vendus, jamais la liste complète —
                //    un envoi complet re-empilerait les serials).
                //  · client FUN_00a7aa50 — flag!=3 → APPEND chaque
                //    entrée du message aux rows ; AUCUN clear nulle
                //    part. Renvoyer la liste entière duplique les
                //    rows (bug live : +1 row par envoi, index qui
                //    dérivent → « Invalid buyback index »).
                // On n'émet donc que les entrées ajoutées par CETTE
                // vente (TakeLast : l'ajout se fait en fin de deque
                // et le trim à 10 pop_front — robuste même au cap).
                var bbList = s.Shops.GetBuybackList(charId);
                if (soldThisCall > 0)
                    await session.SendPacketAsync(512,
                        Codecs.BuybackPackets.BuildClientBuyBackList(
                            Codecs.BuybackPackets.ContentFlag,
                            bbList.TakeLast(soldThisCall).ToList()),
                        token);
            });

        // NCID 185 `[u8 flag]` — onglets de la fenêtre shop
        // (Execute natif @0x823eaf0, flag lu en this+0x12) :
        //   flag!=0 → onglet « buyback » : le natif restaure le
        //     contexte du ShopOp sauvegardé (char+0x670) puis exécute
        //     `shop buy_back <npcUid> -1` → 512 flag=3 VIDE (le
        //     handler client FUN_00a7aa50 ignore tout remplissage
        //     pour flag==3), puis la liste flag=1 relayée par le
        //     serveur mission (BuybackListUpdateItems('\x01')).
        //     On émet les deux dans le même ordre (même wire final).
        //   flag==0 → onglet « items » : le natif ré-exécute l'op
        //     shop courante sauvegardée (`shop <op> <npcUid>
        //     <storeId>`) → 304 EnterShop re-peuple la boutique.
        //     Sans ce renvoi l'onglet Items restait blanc au retour.
        d.RegisterChar(() => new CNE_CZ_ZoneServerBuyBackOpen(),
            async (session, msg, charId, token) =>
            {
                if (msg.Value == 0)
                {
                    // Retour boutique — 304 comme à l'ouverture ; le
                    // storeId actif est conservé sur la session (le
                    // natif le lit dans le contexte char+0x5fc).
                    EdenLog.Write($"[Zone] ShopReturn char={charId} " +
                        $"store={session.CurrentStoreId}");
                    if (session.CurrentStoreId > 0)
                        // 304 complet — le natif OpenShop émet npcUid
                        // (Field1) + storeId ; un Field1=0 laissait
                        // l'onglet Items vide (client lie la fenêtre
                        // à l'entité).
                        await session.SendAsync(new CNC_CZ_ClientEnterShop
                        {
                            Field1 = session.CurrentShopNpcHandle,
                            Field2 = (short)session.CurrentStoreId,
                        }, token);
                    return;
                }
                var list = s.Shops.GetBuybackList(charId);
                EdenLog.Write($"[Zone] BuyBackOpen char={charId} -> {list.Count} items");
                // flag=3 SEUL — « shop buy_back » natif n'émet que
                // l'ouverture vide (le client FUN_00a7aa50 saute le
                // remplissage pour flag==3). Les rows déjà poussées
                // par les deltas flag=1 aux ventes persistent côté
                // client : renvoyer la liste complète ici RE-APPENDAIT
                // chaque entrée → duplicatas + index désynchronisés
                // (clic droit sur row fantôme → « Invalid buyback
                // index », preuve log live).
                await session.SendPacketAsync(512,
                    Codecs.BuybackPackets.BuildClientBuyBackList(
                        Codecs.BuybackPackets.NativeFlag,
                        System.Array.Empty<ShopService.BuybackEntry>()),
                    token);
            });

        d.Register(() => new CNE_CZ_ZoneServerLeaveShop(),
            async (session, msg, token) =>
            {
                session.CurrentStoreId = 0;
                session.CurrentShopNpcHandle = 0;
                await session.SendAsync(
                    new CNC_CZ_ClientVendorClose { Field1 = 0, Field2 = 0 }, token);
            });

        d.Register(() => new CNE_CZ_ZoneServerTalkToNPC(),
            async (session, msg, token) =>
            {
                var npcId = msg.NpcId;
                var npc = s.GameData.Npcs.FirstOrDefault(n => n.Id == npcId);
                EdenLog.Write($"[Zone] TalkToNPC charId={session.CharacterId} " +
                    $"npc={npcId} ({npc?.Name}, role={npc?.Role})");
                if (session.CharacterId is { } cid)
                    s.Missions.NotifyEvent(cid, "NPCTalk", npcId);
                // Vendeur → ouvrir le shop directement
                if (npc is { StoreId: > 0 } && session.CharacterId != null)
                {
                    session.CurrentStoreId = npc.StoreId;
                    session.CurrentShopNpcHandle = npc.Id;
                    await session.SendAsync(new CNC_CZ_ClientEnterShop(), token);
                }
                // TODO(phase reverse): dialogues/quêtes via les scripts #:Talk
            });

        // Hôtel des ventes : Sell(117)/Buy(115)/Cancel(118)/queries
        // CNE AuctionSell : [short bag, short slot, int price, int buyout]
        d.RegisterChar(() => new CNE_CZ_ZoneServerAuctionSell(),
            async (session, msg, charId, token) =>
            {
                var price = Math.Max(1, msg.Field3);
                var buyout = msg.Field4 > 0 ? (long?)msg.Field4 : null;

                var item = await s.InventoryRepo.GetItemAsync(charId, msg.Field1, msg.Field2);
                if (item == null || item.IsEquipped || item.IsBound) return;

                // Frais de dépôt 5% (S_Parameter)
                var fee = s.Auctions.DepositFee(price);
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < fee) return;

                // Quantité vendue : stack complet pour un consommable,
                // 1 pour un équipement — sa durability est une usure,
                // pas un count (une arme 75 de durabilité était listée
                // ×75 puis rendue ×75 à l'annulation — bug dupe).
                var qty = s.Inventory.IsStackable(item.ItemId)
                    ? item.Quantity : 1;
                // Mise en vente ATOMIQUE sur repo SQL : retrait de la
                // ligne inventaire + débit des frais + INSERT annonce
                // en une tx (CreateListingAsync) — plus de fenêtre
                // « annonce active, item encore en sac » au crash.
                var (listing, atomic) = await s.Auctions
                    .ListPersistedAsync(charId, item.ItemId, qty, price,
                        TimeSpan.FromHours(48), buyout,
                        msg.Field1, msg.Field2, fee);
                if (listing == null) return;
                if (!atomic)
                {
                    // Repli non-SQL (tests) : frais en delta puis
                    // retrait — le chemin historique, non atomique.
                    await s.Characters.AddGoldAsync(charId, -fee);
                    await s.Inventory.RemoveItemAsync(charId, msg.Field1,
                        msg.Field2, qty);
                }
                s.NotifyItemChanged(charId, item.ItemId);
                await session.SendAsync(new CNC_CZ_ClientAuctionSellResult { Value = 0 }, token);
                EdenLog.Write($"[Zone] AuctionSell char={charId} item={item.ItemId} " +
                    $"x{qty} price={price} listing={listing.Id}");
            });

        // Achat : gold débité, vendeur crédité, item envoyé par mail (flux natif)
        d.RegisterChar(() => new CNE_CZ_ZoneServerAuctionBuy(),
            async (session, msg, charId, token) =>
            {
                var listing = s.Auctions.GetListing(msg.Value);
                if (listing == null) return;
                var gold = await s.Characters.GetGoldAsync(charId);
                var cost = listing.BuyoutPrice ?? listing.Price;
                if (gold < cost) return;

                var sale = await s.Auctions.BuyAsync(charId, msg.Value);
                if (sale == null) return;
                // Règlement atomique étendu : débit acheteur (guardé),
                // crédit vendeur (delta clampé), bascule
                // active_auctions.status='sold', mail de livraison +
                // ligne mailitem (pièce jointe récupérable) — UNE
                // transaction ; plus de fenêtres « or débité, annonce
                // active » ou « vendu, mail sans item ».
                var (dur, maxDur) = AttachmentDur(s, listing.ItemId,
                    listing.Quantity);
                var settled = await s.Mail.SettleAuctionPurchaseAsync(
                    charId, cost, listing.SellerId, sale.Value.ToSeller,
                    new MailRow
                    {
                        RecipientId = charId, SenderId = 0,
                        SenderName = "Auction House",
                        Subject = "Auction purchase",
                        AttachedItemId = listing.ItemId,
                        AttachedItems =
                            $"{listing.ItemId}:{listing.Quantity}",
                        SentAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddDays(30),
                    },
                    listing.Id, dur, maxDur);
                if (!settled)
                {
                    // Solde redevenu insuffisant ou annonce basculée
                    // entre le check et la tx — la ligne DB reste
                    // 'active' (elle renaîtra au prochain chargement ;
                    // la marque mémoire Sold bloque tout re-achat dans
                    // cette session — log pour audit).
                    EdenLog.Write($"[Zone] AuctionBuy char={charId} " +
                        $"listing={msg.Value} — règlement refusé en tx");
                    return;
                }
                await session.SendAsync(new CNC_CZ_ClientAuctionBuyOK(), token);
                EdenLog.Write($"[Zone] AuctionBuy char={charId} listing={msg.Value} " +
                    $"item={listing.ItemId} cost={cost}");
            });

        // Annulation : item rendu PAR MAIL (flux natif — pas de
        // dépendance à la place libre en sac ; la tx
        // SettleAuctionReturnAsync fait status='cancelled' + mail +
        // mailitem atomiques — avant, AddItemAsync hors tx pouvait
        // perdre l'item sur sac plein ou crash).
        d.RegisterChar(() => new CNE_CZ_ZoneServerAuctionCancel(),
            async (session, msg, charId, token) =>
            {
                var l = s.Auctions.GetListing(msg.Value);
                if (l == null) return;
                var (dur, maxDur) = AttachmentDur(s, l.ItemId,
                    l.Quantity);
                if (await s.Auctions.CancelAsync(charId, msg.Value,
                        s.Mail, dur, maxDur) != null)
                    await session.SendAsync(
                        new CNC_CZ_ClientAuctionCancelOK(), token);
            });

        // AuctionSellQuery → objets en vente du joueur ; les annonces
        // expirées du vendeur sont soldées au passage (retour mail tx,
        // même seam que l'annulation — elles n'expiraient jamais en
        // DB auparavant et leur item était perdu).
        d.RegisterChar(() => new CNE_CZ_ZoneServerAuctionSellQuery(),
            async (session, msg, charId, token) =>
            {
                foreach (var l in s.Auctions.ListingsFor(charId))
                {
                    if (l.Sold || l.ExpiresAt > DateTime.UtcNow) continue;
                    var (dur, maxDur) = AttachmentDur(s, l.ItemId,
                        l.Quantity);
                    await s.Auctions.ExpireAsync(l.Id, s.Mail,
                        dur, maxDur);
                }
                var resp = new CNC_CZ_ClientAuctionSellList();
                foreach (var l in s.Auctions.ListingsFor(charId))
                    resp.Entries.Add(MakeAuctionEntry(l));
                await session.SendAsync(resp, token);
            });

        // AuctionBuyQuery → catalogue filtrable
        d.RegisterChar(() => new CNE_CZ_ZoneServerAuctionBuyQuery(),
            async (session, msg, charId, token) =>
            {
                var resp = new CNC_CZ_ClientAuctionBuyList();
                foreach (var l in s.Auctions.Search())
                    resp.Entries.Add(MakeBuyEntry(l));
                await session.SendAsync(resp, token);
            });

        // Transport : CNE short routeId → coût par palier de niveau
        d.RegisterChar(() => new CNE_CZ_ZoneServerTransport(),
            async (session, msg, charId, token) =>
            {
                var ch = await s.Characters.GetCharacterByIdAsync(charId);
                var route = s.GameData.TransportRoutes.FirstOrDefault(r => r.Id == msg.Value);
                if (ch == null || route == null) return;

                // Coût par palier de niveau (13 paliers dans la table)
                var tier = Math.Clamp((ch.Level - 1) / 10, 0, route.Costs.Length - 1);
                var cost = route.Costs[tier];
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < cost) return;
                await s.Characters.AddGoldAsync(charId, -cost);

                var enter393 = new CNC_CZ_ClientEnterTransport
                { Field3 = cost };
                enter393.Routes.Add((short)route.Id);
                await session.SendAsync(enter393, token);
                EdenLog.Write($"[Zone] Transport char={charId} route={route.Id} " +
                    $"cost={cost} (niveau {ch.Level}, palier {tier})");
            });
    }

    /// <summary>280 ClientUpdateAttr attr 0x02 — l'or après achat/vente.
    /// Format natif (GainGold → ClientUpdateAttr) :
    /// [u32 handle][s16 attrId][i32 cur][i32 max][u32 0].</summary>
    private static async Task SendGoldAttrAsync(NetSession session,
        int charId, ZoneServices s, CancellationToken token)
    {
        var gold = await s.Characters.GetGoldAsync(charId);
        await session.SendAsync(new CNC_CZ_ClientUpdateAttr
        {
            Field1 = (int)charId,           // u32 : handle joueur
            Field2 = 2,                     // s16 : attrId 0x02 = or
            Field3 = (int)gold,             // i32 : nouvelle valeur
            Field4 = 0,                     // i64 : p2 (0 natif)
        }, token);
    }

    private static CNC_CZ_ClientAuctionSellList.Entry MakeAuctionEntry(
        AuctionService.AuctionListing l) => new()
    {
        AuctionId = l.Id,
        ItemId = l.ItemId,
        Price = (int)Math.Clamp(l.Price, 0, int.MaxValue),
        SellerId = (uint)l.SellerId,
        ExpireTime = new DateTimeOffset(l.ExpiresAt).ToUnixTimeSeconds(),
    };

    /// <summary>Gabarit de la ligne mailitem d'une annonce —
    /// durability = count du stack (consommable) ou usure restaurée
    /// au max du template (équipement : l'usure réelle n'est pas
    /// retenue par l'annonce) ; max = cap de pile ou usure max.</summary>
    private static (int dur, int max) AttachmentDur(ZoneServices s,
        int itemId, int quantity)
    {
        var stackable = s.Inventory.IsStackable(itemId);
        var max = stackable ? s.Inventory.MaxStackOf(itemId)
            : Math.Max(s.Inventory.MaxDurabilityOf(itemId), 1);
        return (stackable ? quantity : max, max);
    }

    private static CNC_CZ_ClientAuctionBuyList.Entry MakeBuyEntry(
        AuctionService.AuctionListing l) => new()
    {
        AuctionId = l.Id,
        ItemId = l.ItemId,
        Price = (int)Math.Clamp(l.Price, 0, int.MaxValue),
        SellerId = (uint)l.SellerId,
        ExpireTime = new DateTimeOffset(l.ExpiresAt).ToUnixTimeSeconds(),
    };
}
