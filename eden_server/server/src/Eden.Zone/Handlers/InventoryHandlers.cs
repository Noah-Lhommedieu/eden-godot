using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Codecs;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Inventaire : drop, déplacement/swap, équipement, split —
/// plus banque personnelle et stockage de famille.
/// </summary>
public static partial class InventoryHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // DropItem(6) — wire natif (désasm CPlayerFactory::DropItem
        // zone/decompiled.c:141315) : [s16 type][s16 loc][s16 index].
        // Le natif n'a PAS de drop au sol : CItemFactory::EradicateItem
        // détruit l'item du loc COMPLET — le 3e champ est l'index du
        // conteneur (n° de sac / groupe d'équipe), pas une quantité.
        // Routage (mêmes maps que SwitchItemLocation) :
        //   0 = sacs       (map char+0x4e4, index=n° de sac)
        //   1 = équipement (map char+0x4fc, loc ≤ 0x15, index=groupe)
        //   4 = stockage   (map char+0x514)
        //   5 = sacs spéciaux (map char+0x52c — chez nous persistés
        //       en container_index d'inventaire comme SwitchItemLocation)
        // Écho 275 ClientDropItem = miroir [type][loc][index].
        d.RegisterChar(() => new CNE_CZ_ZoneServerDropItem(),
            async (session, msg, charId, token) =>
            {
                var ok = false;
                // ItemId lu AVANT la suppression — nécessaire au 146 de
                // fin de bloc (objectif ItemCollect).
                var dropped = msg.Slot >= 0 && msg.Container is 0 or 5
                    ? await s.InventoryRepo.GetItemAsync(
                        charId, msg.Count, msg.Slot)
                    : null;
                if (msg.Slot >= 0)
                    ok = msg.Container switch
                    {
                        0 or 5 => await s.InventoryRepo.RemoveItemAsync(
                                charId, msg.Count, msg.Slot),
                        1 => msg.Slot <= 0x15 &&
                             await s.InventoryRepo.RemoveEquippedItemAsync(
                                charId, msg.Count, msg.Slot),
                        4 => await s.Bank.RemoveBankItemAsync(
                                charId, msg.Slot),
                        _ => false,
                    };
                EdenLog.Write($"[Zone] DropItem char={charId} " +
                    $"type={msg.Container} loc={msg.Slot} " +
                    $"idx={msg.Count} -> {(ok ? "ok" : "échec")}");
                // Ack systématique — un silence fige la fenêtre.
                await session.SendAsync(new CNC_CZ_ClientDropItem
                {
                    Field1 = msg.Container, Field2 = msg.Slot,
                    Field3 = msg.Count
                }, token);
                if (ok && msg.Container == 1)
                {
                    // Drop depuis l'équipement : UnEquip natif →
                    // NotifyChangeEquipment (276 slot vidé aux
                    // observateurs, msg.Count = index du conteneur)
                    // + RefreshEQsAttr (280 à soi).
                    await EmitChangeEquipAsync(s, session, charId,
                        msg.Slot, (short)msg.Count, null, token);
                    await EmitSelfEquipRefreshAsync(s, session, charId,
                        token);
                }
                // 146 : l'item quitte l'inventaire (jeter/détruire) →
                // objectif ItemCollect (UpdateQuestItemStatus natif).
                if (ok && dropped != null)
                    s.NotifyItemChanged(charId, dropped.ItemId);
            });

        // SwitchItemLocation — wire natif (désasm
        // CPlayerFactory::SwitchItemLocation @0x81237f0) :
        // [s16 srcType][s16 srcSlot][s16 srcIdx]
        // [s16 dstType][s16 dstSlot][s16 dstIdx]
        // srcIdx/dstIdx = clé de conteneur (n° de sac / groupe
        // d'équipement), srcSlot/dstSlot = loc à l'intérieur.
        // Types natifs : 0=sac (idx 0-3), 1=équipement (idx=groupe,
        // slot=loc d'équipe), 4/5=sacs spéciaux datés, 6=elfe, 7=quick.
        // Réponse 277 ClientSwitchItemLocationOK = écho + u8 résultat.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSwitchItemLocation(),
            async (session, msg, charId, token) =>
            {
                // CheckRide natif : si l'item déplacé est la monture
                // active et qu'il quitte le sac, on descend avant le
                // move (annulation de l'enchant + clear char+0x7f4).
                if (msg.SrcContainer == 0 &&
                    s.RideStates.TryGetValue(charId, out var ride) &&
                    msg.DstContainer != 0)
                {
                    var src = await s.InventoryRepo.GetItemAsync(
                        charId, msg.SrcIndex, msg.SrcSlot);
                    if (src != null && src.Id == ride.ItemSerial)
                    {
                        s.RideStates.TryRemove(charId, out _);
                        await session.SendAsync(
                            new CNC_CZ_ClientEnchantRemove
                            {
                                Field1 = charId,
                                Field2 = (short)ride.EnchantId,
                                Field3 = 0, Field4 = 0,
                            }, token);
                        // 302 : bit9 « mounted » retiré du mot d'état
                        // (NodifyFlagChange natif) — sinon le client
                        // garde le bit ride → strafes monture à pied.
                        if (s.RideFlagNotify != null)
                            await s.RideFlagNotify(session, charId,
                                token);
                    }
                }
                var (ok, rDstSlot, rDstIdx) = await SwitchItemAsync(s, charId,
                    msg.SrcContainer, msg.SrcSlot, msg.SrcIndex,
                    msg.DstContainer, msg.DstSlot, msg.DstIndex,
                    session, token);
                // Toujours répondre : un silence laisse le client
                // figer la fenêtre. Field7 = code résultat (1 = succès —
                // désasm SwitchItemLocation : piVar13+0x10 = 1 sur la
                // voie normale, CheckSwitchItem @0x8127300 émet 0 en
                // échec). IMPORTANT : les champs dst renvoyés sont les
                // valeurs RÉSOLUES — le natif passe slot/index par
                // short& et les réécrit (param_7=-1 → GetContainerWith
                // EmptySlot / loc d'équipement). Écho du -1 brut = le
                // client indexe hors tableau → alpaca crash constaté
                // live à l'équipe d'une arme.
                await session.SendAsync(new CNC_CZ_ClientSwitchItemLocationOK
                {
                    Field1 = msg.SrcContainer, Field2 = msg.SrcSlot,
                    Field3 = msg.SrcIndex,
                    Field4 = msg.DstContainer, Field5 = rDstSlot,
                    Field6 = rDstIdx, Field7 = (byte)(ok ? 1 : 0)
                }, token);
                // Swap équipement : la pièce déjà portée repart dans le
                // slot sac libéré (DB) mais le 277 ne la décrit pas —
                // sans 274 le client la voyait « disparaître » (le
                // slot sac vidé par l'ack restait vide jusqu'au relog).
                // Posé APRÈS l'ack : le 277 vide d'abord le slot source.
                if (ok && msg.SrcContainer == 0 && msg.DstContainer == 1)
                {
                    var back = await s.InventoryRepo.GetItemAsync(
                        charId, msg.SrcIndex, msg.SrcSlot);
                    if (back != null)
                        await session.SendPacketAsync(274,
                            NativeItemCodec.BuildPutItem(back), token);
                }
                // Déséquipement : l'ack ne porte pas l'itemId (le client
                // le devinait via l'état porté, déjà rafraîchi) —
                // 274 explicite de la pièce arrivée au slot résolu.
                if (ok && msg.SrcContainer == 1 && msg.DstContainer == 0)
                {
                    var un = await s.InventoryRepo.GetItemAsync(
                        charId, rDstIdx, rDstSlot);
                    if (un != null)
                        await session.SendPacketAsync(274,
                            NativeItemCodec.BuildPutItem(un), token);
                }
                if (!ok)
                    EdenLog.Write(
                        $"[Zone] SwitchItem refusé char={charId} " +
                        $"({msg.SrcContainer},{msg.SrcSlot},{msg.SrcIndex})" +
                        $"->({msg.DstContainer},{msg.DstSlot}," +
                        $"{msg.DstIndex})");
                else if (msg.DstContainer == 1 || msg.SrcContainer == 1)
                {
                    // Équipement touché : NotifyChangeEquipment (276
                    // par loc aux observateurs — émis dans les branches
                    // de SwitchItemAsync) + RefreshEQsAttr →
                    // NotifyRefreshAttr (280 vitals/stats à soi).
                    // Le natif n'envoie ni 282 ni 283 ici — 282 n'est
                    // que la réponse à UpdateEquipment(8).
                    await EmitSelfEquipRefreshAsync(s, session, charId,
                        token);
                }
            });

        // UpdateEquipment(8) — wire natif : [u32 mask des locs modifiés].
        // Désasm CNE_CZ_ZoneServerUpdateEquipment::Execute : le natif
        // répond par 282 ClientUpdateEquipment (état complet porté,
        // broadcast vue) puis un paquet de modification de modèle (283).
        // Sans cette réponse le panneau d'équipement du client reste
        // figé sur l'état précédent.
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateEquipment(),
            async (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] UpdateEquipment char={charId} mask=0x{msg.UpdateFlags:x8}");
                // 282 ClientUpdateEquipment (état porté complet) +
                // 283 modification de modèle — broadcast vue.
                // Sans eux le panneau d'équipement reste figé.
                await BroadcastEquipmentAsync(s, session, charId,
                    token);
            });

        // Split de stack — wire natif (désasm CItemFactory::DivideItem
        // @0x809ff30) : [s16 srcSlot][s16 srcSac][s16 dstSlot]
        // [s16 dstSac][s16 quantité][s16 type]. param_6 (Field5) = qté
        // retirée du stack source (ModifyItemDurability -count).
        // Notifications natives du split : 291 ModifyItemDurability
        // (nouveau count source) + 274 ClientPutItemIntoInventory
        // (le nouveau stack) — sans elles le client ne voyait rien
        // changer et le split semblait mort (bug live).
        d.RegisterChar(() => new CNE_CZ_ZoneServerDivideItem(),
            async (session, msg, charId, token) =>
            {
                var src = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field2, msg.Field1);
                if (src == null || !await s.Inventory.SplitItemAsync(
                        charId, msg.Field2, msg.Field1, msg.Field5,
                        msg.Field4, msg.Field3))
                    return;
                var left = src.Quantity - msg.Field5;
                await session.SendAsync(
                    new CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0,                    // s16 : contType
                        Field2 = msg.Field1,           // s16 : slot source
                        Field3 = (short)left,          // s16 : nouveau count
                        Field4 = (short)src.MaxDurability, // s16 : item+0xe
                        Field5 = msg.Field2,           // s16 : contIdx (sac)
                        Field6 = 0,
                    }, token);
                var body = Eden.Zone.Codecs.NativeItemCodec.BuildPutItem(
                    src.ItemId, msg.Field4, msg.Field3, msg.Field5,
                    (short)src.MaxDurability, 0x08, 0, meta: src);
                await session.SendPacketAsync(274, body, token);
                EdenLog.Write($"[Zone] DivideItem char={charId} " +
                    $"({msg.Field2},{msg.Field1}) x{msg.Field5} " +
                    $"-> ({msg.Field4},{msg.Field3}) reste={left}");
            });

        // --- Réparation / identification / tri ---
        // AskRepairEquipGold → coût total (prix vente × durabilité manquante)
        d.RegisterChar(() => new CNE_CZ_ZoneServerAskRepairEquipGold(),
            async (session, msg, charId, token) =>
            {
                var cost = await RepairCostAsync(s, charId, -1);
                await session.SendAsync(new CNC_CZ_ClientAskRepairEquipGold
                {
                    Value = (int)cost,
                }, token);
            });

        // AskRepairOneItemGold (byte bag, byte ?, short slot)
        d.RegisterChar(() => new CNE_CZ_ZoneServerAskRepairOneItemGold(),
            async (session, msg, charId, token) =>
            {
                var cost = await RepairCostAsync(s, charId, msg.Field3);
                await session.SendAsync(new CNC_CZ_ClientAskRepairOneItemGold
                {
                    Field1 = msg.Field1, Field2 = msg.Field2,
                    Field3 = (int)cost,
                }, token);
            });

        // AskIdentifyGold (ushort bag, ushort slot, short ?)
        d.RegisterChar(() => new CNE_CZ_ZoneServerAskIdentifyGold(),
            async (session, msg, charId, token) =>
            {
                var item = await s.InventoryRepo.GetItemAsync(charId, msg.Field1, msg.Field2);
                var cost = item != null
                    ? (int)ItemIdentifyCost(s, item.ItemId) : 0;
                await session.SendAsync(new CNC_CZ_ClientAskIdentifyGold
                {
                    Field1 = msg.Field1, Field2 = msg.Field2, Field3 = cost,
                }, token);
            });

        // ArrangeItem (short bag, short ?) — tri du sac → ClientArrangeItemOK
        d.RegisterChar(() => new CNE_CZ_ZoneServerArrangeItem(),
            async (session, msg, charId, token) =>
            {
                var items = (await s.InventoryRepo.GetItemsByCharacterIdAsync(charId))
                    .Where(i => i.BagId == msg.Field1 && !i.IsEquipped)
                    .OrderBy(i => i.ItemId).ToList();
                // Deux passes : l'index unique (player,bag,loc) refuse un
                // UPDATE vers un slot encore occupé — on déplace tout en
                // zone tampon (loc+10000) puis on récrit les slots finaux.
                // Tampon au-dessus de tout slot existant : un tri
                // interrompu laisse des lignes à 10000+ qui feraient
                // entrer le premier passage en collision.
                var buffer = items.Count == 0 ? 10000
                    : Math.Max(10000, items.Max(i => (int)i.Slot) + 1);
                for (var i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    it.Slot = (short)(buffer + i);
                    await s.InventoryRepo.UpdateItemAsync(it);
                }
                for (var i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    it.Slot = (short)i;
                    await s.InventoryRepo.UpdateItemAsync(it);
                }
                await session.SendAsync(new CNC_CZ_ClientArrangeItemOK
                {
                    Field1 = (uint)charId, Field2 = msg.Field1,
                }, token);
                EdenLog.Write($"[Zone] ArrangeItem char={charId} bag={msg.Field1} " +
                    $"({items.Count} items triés)");
            });

        // Banque : GetStorageData(62) / Deposit(60) / Withdraw(61) / Expand(69)
        // Deltas natifs (désasm CItemFactory::SwitchItemLocation /
        // GainItem ~0x809c970) : shrink source → 291
        // ClientModifyItemDurability ; slot vidé → 275 ClientDropItem ;
        // destination → 359 ClientPutItemIntoStorage / 274
        // ClientPutItemIntoInventory (même record, opcode différent).
        // Côté banque on renvoie 358 ClientGetStorageData (format
        // éprouvé à l'ouverture) — rafraîchit tous les onglets.
        d.RegisterChar(() => new CNE_CZ_ZoneServerStorageDeposit(),
            async (session, msg, charId, token) =>
            {
                // (npcId int, bagId ushort, slot short, count short)
                // ItemId lu AVANT le dépôt (la ligne peut disparaître)
                // — nécessaire au 146 de fin de bloc.
                var srcItem = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field2, msg.Field3);
                var (success, deposited) = await s.Bank.DepositFromInventoryAsync(
                    s.InventoryRepo, charId, msg.Field2, msg.Field3, msg.Field4);
                if (!success)
                {
                    EdenLog.Write($"[Zone] StorageDeposit char={charId} " +
                        $"bag={msg.Field2} slot={msg.Field3} x{msg.Field4} -> échec");
                    return;
                }
                // Slot inventaire source : reste → 291, vidé → 275.
                var left = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field2, msg.Field3);
                if (left == null)
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    {
                        Field1 = 0,                    // s16 contType — sac
                        Field2 = msg.Field3,           // s16 slot vidé
                        Field3 = (short)msg.Field2,    // s16 contIdx (sac)
                    }, token);
                else
                    await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0,                       // s16 contType
                        Field2 = msg.Field3,              // s16 slot
                        Field3 = (short)left.Quantity,    // s16 count restant
                        Field4 = (short)left.MaxDurability, // s16 item+0xe
                        Field5 = (short)msg.Field2,       // s16 contIdx
                        Field6 = 0,
                    }, token);
                // Côté banque : refresh 358 complet (onglets).
                await SendStorageDataAsync(session, s, charId, 0, token);
                // 146 : l'item quitte l'inventaire → un objectif
                // ItemCollect rempli peut revenir « en cours »
                // (UpdateQuestItemStatus natif, miroir delete).
                if (srcItem != null)
                    s.NotifyItemChanged(charId, srcItem.ItemId);
                EdenLog.Write($"[Zone] StorageDeposit char={charId} bag={msg.Field2} " +
                    $"slot={msg.Field3} x{msg.Field4} -> {deposited} déposés");
            });

        // MORT EN NATIF : Execute @0x828cd10 = corps vide — le retrait
        // banque passe par StorageDeposit/SwitchItemLocation côté US.
        // (handler local conservé s'il traite le cas client actuel.)
        d.RegisterChar(() => new CNE_CZ_ZoneServerStorageWithdraw(),
            async (session, msg, charId, token) =>
            {
                var (success, withdrawn, itemId, touched) =
                    await s.Bank.WithdrawToInventoryAsync(
                        s.Inventory, charId, msg.Field3, msg.Field4);
                if (!success)
                {
                    EdenLog.Write($"[Zone] StorageWithdraw char={charId} " +
                        $"bankSlot={msg.Field3} x{msg.Field4} -> échec");
                    return;
                }
                // Inventaire : 291 pour les merges, 274 par nouveau slot
                // (désasm SwitchItemLocation — même split de notifications).
                foreach (var (bag, slot, merged) in touched)
                {
                    var item = await s.InventoryRepo.GetItemAsync(
                        charId, bag, slot);
                    if (item == null) continue;
                    if (merged)
                        await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
                        {
                            Field1 = 0,                       // s16 contType
                            Field2 = (short)slot,             // s16 slot
                            Field3 = (short)item.Quantity,    // s16 nouveau count
                            Field4 = (short)item.MaxDurability, // s16 item+0xe
                            Field5 = (short)bag,              // s16 contIdx
                            Field6 = 0,
                        }, token);
                    else
                        // 274 natif — overload entité.
                        await session.SendPacketAsync(274,
                            NativeItemCodec.BuildPutItem(item), token);
                }
                await SendStorageDataAsync(session, s, charId, 0, token);
                // 146 : le retrait fait rentrer l'item dans le
                // comptage ItemCollect (UpdateQuestItemStatus natif).
                if (itemId > 0)
                    s.NotifyItemChanged(charId, itemId);
                EdenLog.Write($"[Zone] StorageWithdraw char={charId} bankSlot={msg.Field3} " +
                    $"x{msg.Field4} -> {withdrawn} retirés");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerGetStorageData(),
            async (session, msg, charId, token) =>
            {
                await SendStorageDataAsync(session, s, charId,
                    msg.Value, token);
            });

        // StorageExpand — désasm CNE_CZ_ZoneServerStorageExpand::Execute
        // → CPlayerFactory::ExpandStorage (zone:138134) : cnt<2 et
        // cnt<taille table prix (CZoneServer+0x554 ← S_Parameter
        // 409/410 « 小屋倉庫第N排擴充金額 » 1500/3500), or suffisant
        // (char+0x4b0) → débit + cnt++ + save ; réponse
        // ClientStorageExpandResult [s16 résultat] (0=ok,1=cap,2=or).
        // Textes natifs du CheckExecute : 0x5aad cap, 0x5aae or
        // manquant (%lld = montant manquant) ; 0x5ab4 post-expansion
        // (%hd = nouveau compteur).
        d.RegisterChar(() => new CNE_CZ_ZoneServerStorageExpand(),
            async (session, msg, charId, token) =>
            {
                var p = s.GameData.Parameters;
                var prices = new[]
                {
                    (int)p.Get(ParameterParser.StorageExpandPrice1),
                    (int)p.Get(ParameterParser.StorageExpandPrice2),
                };
                var cnt = await s.Bank.GetStorageExpandCountAsync(charId);
                var ch = await s.Characters.GetCharacterByIdAsync(charId);
                var gold = ch?.Gold ?? 0;
                var (result, cost) =
                    Eden.Core.Game.Services.BankService
                        .EvaluateExpandStorage(cnt, gold, prices);
                EdenLog.Write($"[Zone] StorageExpand char={charId} " +
                    $"cnt={cnt} gold={gold} cost={cost} -> résultat {result}");
                if (result == 0)
                {
                    await s.Characters.AddGoldAsync(charId, -cost);
                    await s.Bank.SetStorageExpandCountAsync(
                        charId, cnt + 1);
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        {
                            Field1 = 0x5ab4,
                            Field2 = (cnt + 1).ToString(),
                        }, token);
                }
                else if (result == 2)
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        {
                            Field1 = 0x5aae,
                            Field2 = (cost - gold).ToString(),
                        }, token);
                else
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        {
                            Field1 = 0x5aad,
                            Field2 = string.Empty,
                        }, token);
                await session.SendAsync(
                    new CNC_CZ_ClientStorageExpandResult
                    {
                        Value = result   // s16 : 0=ok, 1=cap, 2=or
                    }, token);
            });

        // Stockage famille : dépôt/retrait — wire natif (désasm
        // CNE_CZ_ZoneServerFamilyStoragePutItem::Execute @0x08258a00,
        // ::GetItem::Execute zone, TC_FamilyStorageInsertItemRequest
        // mission @0x081f43c0) : les deux paquets portent
        // [s16 index][s16 loc][s16 inv_index][s16 inv_loc] — coffre
        // (0-2), slot du coffre (-1 = auto), sac et slot d'inventaire.
        // PAS de quantité : la ligne entière (stack) est déplacée.
        // Gates natifs : priv CheckPrivilige(index+0xb) incluant les
        // niveaux de famille (coffre 0→lv3, 1→lv5, 2→lv6), cap 0x28,
        // itemdata+0xf2 bit0 → texte 0x20a3, +0xf0 bits 8|0x10 → 0x7eb,
        // coffre absent → 0x7ed, plein → 0x7ef.
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyStoragePutItem(),
            async (session, msg, charId, token) =>
            {
                if (!CheckFamilyStoragePrivilege(s, charId, msg.Field1))
                    return;
                if (s.Guilds.GetGuildOf(charId) is not { } guild) return;
                var index = msg.Field1;
                // Cap natif du coffre : < 0x28 items.
                if (await s.FamilyBank.GetItemCountAsync(guild.Id, index)
                        >= FamilyBankRepository.StorageSlotCapacity)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x7ef, Field2 = string.Empty }, token);
                    return;
                }

                var item = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field3, msg.Field4);
                if (item == null) return;
                // Flags de restriction S_Item col69/70 (itemdata+0xf0/+0xf2).
                var def = s.GameData.Items.FirstOrDefault(
                    d2 => d2.Id == item.ItemId);
                if (def == null) return;
                if ((def.RestrictFlags70 & 1) != 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x20a3, Field2 = string.Empty }, token);
                    return;
                }
                if ((def.RestrictFlags69 & 0x18) != 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x7eb, Field2 = string.Empty }, token);
                    return;
                }

                // Résolution du slot coffre natif : -1 → premier libre
                // 0..39 ; sinon le slot demandé doit être libre.
                var loc = msg.Field2;
                if (loc == -1)
                    loc = (short)(await s.FamilyBank.GetFreeLocAsync(
                        guild.Id, index) ?? -1);
                if (loc < 0 ||
                    await s.FamilyBank.GetItemAsync(guild.Id, index, loc)
                        != null)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x7ef, Field2 = string.Empty }, token);
                    return;
                }

                // Déplacement de la ligne entière vers family_storage —
                // UNE transaction (DELETE inventory + INSERT
                // family_storage) : avant, un crash entre les deux
                // appels (connexions distinctes) perdait ou dupliquait
                // l'item.
                item.CharacterId = guild.Id;
                item.BagId = index;
                item.Slot = loc;
                if (s.Social == null ||
                    !await s.Social.MoveItemToFamilyStorageAsync(
                        charId, item))
                    return;
                // Sac vidé → 275 ClientDropItem (miroir du retrait natif).
                await session.SendAsync(new CNC_CZ_ClientDropItem
                {
                    Field1 = 0,                     // s16 contType — sac
                    Field2 = msg.Field4,            // s16 slot vidé
                    Field3 = msg.Field3,            // s16 contIdx (sac)
                }, token);
                // 505 ClientFamilyStoragePutItem → tous les membres
                // famille en ligne (native : map family+0x94).
                var put = Eden.Zone.Codecs.FamilyStoragePackets
                    .BuildPutItem(index, item);
                await NotifyFamilyMembersAsync(s, guild, 505, put,
                    token);   // 505 = CNC_CZ_ClientFamilyStoragePutItem
                // 146 : l'item quitte l'inventaire → ItemCollect.
                s.NotifyItemChanged(charId, item.ItemId);
                // Journal natif (CLogFactory::FamilyStorageLog —
                // log_type 0 "Storage Insert", dump d'audit de l'item
                // déplacé dans le préfixe natif) + push 506.
                var log =
                    $"Storage Insert-ID:{item.Id},ItemID:{item.ItemId}," +
                    $"Dur:{item.Durability},MaxDur:{item.MaxDurability}," +
                    $"By:{charId}";
                await s.FamilyBank.AppendStorageLogAsync(guild.Id, 0, log);
                await NotifyFamilyMembersAsync(s, guild,
                    new CNC_CZ_ClientFamilyStorageLog
                    {
                        Entries = { new CNC_CZ_ClientFamilyStorageLog
                            .Entry { Action = 0, Detail = log } },
                    }, token);
                EdenLog.Write($"[Zone] FamilyStoragePut char={charId} " +
                    $"item={item.ItemId} -> coffre {index} loc={loc}");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyStorageGetItem(),
            async (session, msg, charId, token) =>
            {
                if (!CheckFamilyStoragePrivilege(s, charId, msg.Field1))
                    return;
                if (s.Guilds.GetGuildOf(charId) is not { } guild) return;
                var index = msg.Field1;

                var stored = await s.FamilyBank.GetItemAsync(
                    guild.Id, index, msg.Field2);
                if (stored == null) return;

                // Sac d'arrivée : inv_index/inv_loc (-1 → auto).
                var bagId = msg.Field3 >= 0 ? (int)msg.Field3 : 0;
                var slot = msg.Field4 >= 0 ? (int)msg.Field4
                    : await s.InventoryRepo.GetAvailableSlotAsync(
                        charId, bagId) ?? -1;
                if (slot < 0) return;

                // La ligne retourne dans inventory1 avec tous ses
                // attributs — DELETE family_storage + INSERT inventory1
                // en UNE transaction (même remarque crash que le dépôt ;
                // nouveau serial comme l'AddItemAsync historique).
                stored.CharacterId = charId;
                stored.BagId = bagId;
                stored.Slot = slot;
                if (s.Social == null ||
                    !await s.Social.MoveFamilyStorageItemToInventoryAsync(
                        stored))
                    return;
                // 274 ClientPutItemIntoInventory pour l'acteur.
                await session.SendPacketAsync(274,
                    Eden.Zone.Codecs.NativeItemCodec.BuildPutItem(stored),
                    token);
                // 504 ClientFamilyStorageDeleteItem → membres en ligne.
                await NotifyFamilyMembersAsync(s, guild,
                    new CNC_CZ_ClientFamilyStorageDeleteItem
                    { Field1 = (short)index, Field2 = msg.Field2 }, token);
                // 146 : le retrait fait rentrer l'item dans le
                // comptage ItemCollect.
                s.NotifyItemChanged(charId, stored.ItemId);
                // Journal natif — log_type 1 "Delete" (retrait) + 506.
                var log =
                    $"Delete-ID:{stored.Id},ItemID:{stored.ItemId}," +
                    $"Dur:{stored.Durability},MaxDur:{stored.MaxDurability}," +
                    $"By:{charId}";
                await s.FamilyBank.AppendStorageLogAsync(guild.Id, 1, log);
                await NotifyFamilyMembersAsync(s, guild,
                    new CNC_CZ_ClientFamilyStorageLog
                    {
                        Entries = { new CNC_CZ_ClientFamilyStorageLog
                            .Entry { Action = 1, Detail = log } },
                    }, token);
                EdenLog.Write($"[Zone] FamilyStorageGet char={charId} " +
                    $"item={stored.ItemId} <- coffre {index} " +
                    $"loc={msg.Field2} -> ({bagId},{slot})");
            });
    }

    /// <summary>Gate privilège du coffre de famille — priv natif
    /// `index + 0xb` (storage01/02/03_privilege) via
    /// <see cref="GuildService.CheckPrivilege"/> qui applique déjà les
    /// niveaux de famille requis (11→lv3, 12→lv5, 13→lv6) ; index
    /// borné aux 3 coffres natifs.</summary>
    private static bool CheckFamilyStoragePrivilege(
        ZoneServices s, int charId, int index)
    {
        if (index is < 0 or >= FamilyBankRepository.StorageCount)
            return false;
        if (s.Guilds.GetGuildOf(charId) is not { } guild) return false;
        var member = guild.Members.FirstOrDefault(
            m => m.CharacterId == charId);
        return member != null &&
            s.Guilds.CheckPrivilege(guild, member, index + 11);
    }

    /// <summary>Push d'un paquet coffre à tous les membres de la
    /// famille connectés sur cette zone — miroir natif de la boucle
    /// `family+0x94` (membres en ligne) de
    /// `CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute`.</summary>
    private static async Task NotifyFamilyMembersAsync(ZoneServices s,
        Eden.Core.Game.Services.GuildService.Guild guild, ushort ncid,
        byte[] body, CancellationToken token)
    {
        foreach (var m in guild.Members)
            if (s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                await ms.SendPacketAsync(ncid, body, token);
    }

    /// <inheritdoc cref="NotifyFamilyMembersAsync(ZoneServices,
    /// Eden.Core.Game.Services.GuildService.Guild, ushort, byte[],
    /// CancellationToken)"/>
    private static async Task NotifyFamilyMembersAsync(ZoneServices s,
        Eden.Core.Game.Services.GuildService.Guild guild,
        NetCommand cmd, CancellationToken token)
    {
        foreach (var m in guild.Members)
            if (s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                await ms.SendAsync(cmd, token);
    }

    /// <summary>Dispatche un SwitchItemLocation selon les types de
    /// conteneur (désasm CPlayerFactory::SwitchItemLocation) :
    /// 0→0 déplacement/merge/swap intra-sac, 0→1 équipement
    /// (CItemFactory::Equip), 1→0 déséquipement (UnEquip), 4/5 = sacs
    /// annexes persistés en container_index 4/5 de l'inventaire.</summary>
    /// <summary>Move d'item src→dst. Retourne succès + slot/index dst
    /// RÉSOLUS (le natif réécrit les short& passés quand la demande
    /// vaut -1 : premier emplacement libre ou loc d'équipement du
    /// template — SwitchItemLocation @0x8123xxx). L'écho de ces valeurs
    /// dans le 277 est obligatoire : -1 brut = crash client.</summary>
    private static async Task<(bool ok, short dstSlot, short dstIdx)>
        SwitchItemAsync(ZoneServices s,
        int charId, short srcType, short srcSlot, short srcIdx,
        short dstType, short dstSlot, short dstIdx,
        Eden.Core.Networking.Session.NetSession session,
        CancellationToken token)
    {
        switch ((srcType, dstType))
        {
            // Sac → sac : move/merge/swap (idx = n° de sac, slot = loc).
            // Type 4 = "STO" (banque — CStorage::GetType, map char+0x514),
            // type 5 = "BAGS" (sacs spéciaux, char+0x52c) — désasm
            // CPlayerFactory::SwitchItemLocation @0x81237f0.
            case (0, 0):
            {
                var moved = await s.InventoryRepo.GetItemAsync(
                    charId, srcIdx, srcSlot);
                var toBag = dstIdx >= 0 ? dstIdx : srcIdx;
                var toSlot = dstSlot >= 0 ? dstSlot
                    : await FirstFreeSlotAsync(s, charId, toBag);
                if (toSlot < 0) return (false, dstSlot, dstIdx);
                var ok = await s.Inventory.SwitchItemLocationAsync(charId,
                    srcIdx, srcSlot, toBag, toSlot);
                // UpdateFastDressFromInfo natif : les records dress
                // suivent l'item déplacé.
                if (ok && moved != null && s.FastDressItemMoved != null)
                    await s.FastDressItemMoved(charId, moved.Id,
                        0, toSlot, toBag);
                return (ok, toSlot, toBag);
            }

            // Sac → porte-sacs : équiper un sac (kind 43) — désasm
            // CPlayerFactory::SwitchBagFromContainer @0x8101160.
            // dstIdx = holder (0 = inventaire, 4 = banque — le holder
            // 4 redimensionne un CStorage, pas géré ici).
            // dstLoc = slot 1-4 du porte-sacs = n° du sac d'inventaire
            // ouvert ; loc 0 refusé (sac de base, return 0 natif).
            case (0, 5):
            {
                return await EquipBagAsync(s, charId, srcIdx, srcSlot,
                    dstIdx, dstSlot, session, token);
            }

            // Porte-sacs → sac : déséquiper — désasm
            // CPlayerFactory::SwitchBagToContainer @0x80f4c60 : le sac
            // d'inventaire à l'index srcLoc doit être VIDE (0x306),
            // puis DeleteContainer("inventory", srcLoc) — capacité
            // remise à 0 dans inventory_slots.
            case (5, 0):
            {
                return await UnequipBagAsync(s, charId, srcIdx, srcSlot,
                    dstIdx, dstSlot, session, token);
            }

            // Porte-sacs → porte-sacs : déplacer/échanger deux sacs
            // équipés — désasm CPlayerFactory::SwitchBagToBag
            // @0x810a770 (swap des locs + swap des capacités des sacs
            // d'inventaire correspondants).
            case (5, 5):
            {
                return await SwitchBagToBagAsync(s, charId, srcIdx,
                    srcSlot, dstIdx, dstSlot);
            }

            // Sac → banque : drag-dépôt (natif : requiert un CStorageOp
            // ouvert char+0x670, CheckAutoStackItem pour les merges).
            // Notifications : 291/275 source + 358 refresh banque.
            case (0, 4):
            {
                var src = await s.InventoryRepo.GetItemAsync(
                    charId, srcIdx, srcSlot);
                if (src == null) return (false, dstSlot, dstIdx);
                var (ok, dep) = await s.Bank.DepositFromInventoryAsync(
                    s.InventoryRepo, charId, srcIdx, srcSlot, src.Quantity);
                if (!ok) return (false, dstSlot, dstIdx);
                var left = await s.InventoryRepo.GetItemAsync(
                    charId, srcIdx, srcSlot);
                if (left == null)
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    {
                        Field1 = 0, Field2 = srcSlot, Field3 = srcIdx,
                    }, token);
                else
                    await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0, Field2 = srcSlot,
                        Field3 = (short)left.Quantity,
                        Field4 = (short)left.MaxDurability, Field5 = srcIdx,
                        Field6 = 0,
                    }, token);
                await SendStorageDataAsync(session, s, charId, 0, token);
                return (true, dstSlot, dstIdx);
            }

            // Banque → sac : drag-retrait — 274/291 inventaire + 358.
            case (4, 0):
            {
                var bank = (await s.Bank.GetBankItemsAsync(charId))
                    .FirstOrDefault(i => i.Slot == srcSlot);
                if (bank == null) return (false, dstSlot, dstIdx);
                var (ok, w, itemId, touched) =
                    await s.Bank.WithdrawToInventoryAsync(
                        s.Inventory, charId, srcSlot, bank.Quantity);
                if (!ok) return (false, dstSlot, dstIdx);
                foreach (var (bag, slot, merged) in touched)
                {
                    var item = await s.InventoryRepo.GetItemAsync(
                        charId, bag, slot);
                    if (item == null) continue;
                    if (merged)
                        await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
                        {
                            Field1 = 0, Field2 = (short)slot,
                            Field3 = (short)item.Quantity,
                            Field4 = (short)item.MaxDurability,
                            Field5 = (short)bag, Field6 = 0,
                        }, token);
                    else
                        // 274 natif — overload entité.
                        await session.SendPacketAsync(274,
                            NativeItemCodec.BuildPutItem(item), token);
                }
                await SendStorageDataAsync(session, s, charId, 0, token);
                // 146 : item rendu à l'inventaire → ItemCollect.
                if (itemId > 0)
                    s.NotifyItemChanged(charId, itemId);
                return (true, dstSlot, dstIdx);
            }

            // Banque → banque : swap/merge interne (idx = onglet).
            case (4, 4):
            {
                var toSlot = dstSlot >= 0 ? dstSlot : srcSlot;
                var ok = await s.Bank.SwitchBankItemsAsync(
                    charId, srcSlot, toSlot);
                if (!ok) return (false, dstSlot, dstIdx);
                await SendStorageDataAsync(session, s, charId, 0, token);
                return (true, toSlot, dstIdx);
            }

            // Sac → équipement : équiper (idx dst = groupe, slot dst =
            // loc d'équipement ; -1 → loc résolu par la traduction
            // native type→slot, pas la valeur brute de S_Item).
            case (0, 1):
            {
                var item = await s.InventoryRepo.GetItemAsync(
                    charId, srcIdx, srcSlot);
                if (item == null) return (false, dstSlot, dstIdx);
                // Œufs de familier (kind 60 — S_Partner) : le natif ne
                // les équipe jamais — le client peut quand même envoyer
                // un switch vers un slot quand l'utilisateur dépose
                // l'œuf sur le panneau familier. On route vers AddElf
                // (CKFElfManager::AddElf) ; comme dans DoUseItem natif
                // l'œuf est CONSOMMÉ au succès — sinon il reste
                // re-cliquable et duplique le familier.
                if (s.GameData.Items.FirstOrDefault(
                        i => i.Id == item.ItemId)?.Kind == 60 &&
                    s.ElfUseItem != null)
                {
                    var added = await s.ElfUseItem(session, charId,
                        item, token);
                    if (added)
                    {
                        // Consommation ItemConsume natif : décrément
                        // DB puis 291 (reste) / 275 (slot vidé).
                        await s.Inventory.RemoveItemAsync(charId,
                            srcIdx, srcSlot, 1);
                        var left = item.Quantity - 1;
                        if (left > 0)
                            await session.SendAsync(
                                new Eden.Core.Networking.Messages
                                    .Generated
                                    .CNC_CZ_ClientModifyItemDurability
                                {
                                    Field1 = 0,
                                    Field2 = (short)srcSlot,
                                    Field3 = (short)left,
                                    Field4 = (short)item.MaxDurability,
                                    Field5 = (short)srcIdx,
                                    Field6 = 0,
                                }, token);
                        else
                            // 275 natif [s16 contType][s16 loc]
                            // [s16 contIdx] — pas de quantité.
                            await session.SendAsync(
                                new Eden.Core.Networking.Messages
                                    .Generated.CNC_CZ_ClientDropItem
                                {
                                    Field1 = 0, Field2 = (short)srcSlot,
                                    Field3 = (short)srcIdx,
                                }, token);
                        s.NotifyItemChanged(charId, item.ItemId);
                    }
                    return (false, dstSlot, dstIdx);
                }
                var eIdx = Math.Max((short)0, dstIdx);
                // Gate par conteneur (natif Equip switch) : cont 0 =
                // kinds joueur, cont 1 = panneau pet (kinds 61-68/81),
                // cont 2-4 = pages mod3 armures, cont 5 = glyphes.
                var loc = await ResolveEquipLocAsync(s, charId,
                    item.ItemId, dstSlot, eIdx);
                // Échec → l'icône posée au drag reste fantôme côté
                // client (il a peint au loc de SON routage local
                // avant la réponse) : purge 275 ciblée sur les locs
                // candidats vides en DB. PAS de 311 mid-session —
                // le client ne le traite qu'à l'entrée de zone et
                // crash dessus en jeu (trace 19:33:41).
                if (loc < 0)
                {
                    await PurgeEquipGhostsAsync(s, session, charId,
                        item.ItemId, dstSlot, eIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                // Conflits de mains 1H/2H — un échec (sac plein) refuse
                // l'équipe : jamais de coexistence 2H + off-hand.
                if (!await ClearWeaponConflictsAsync(s, charId,
                    item.ItemId, loc, eIdx, session, token))
                {
                    await PurgeEquipGhostsAsync(s, session, charId,
                        item.ItemId, dstSlot, eIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                // Pièce déjà portée au loc cible : le swap natif la
                // renvoie dans le slot sac libéré — suivie aussi par
                // UpdateFastDressFromInfo.
                var oldEq = await s.InventoryRepo.GetEquipmentItemAsync(
                    charId, eIdx, loc);
                var eqOk = await s.InventoryRepo.MoveToEquipmentAsync(
                    charId, srcIdx, srcSlot, eIdx, loc);
                // Bind à l'équipe — natif Equip @0x080b5810 :
                // (def+0x30 signe = Flags & 0x80) && !(clone+0x24&0x10)
                // → clone |= 0x10. Couvre aussi le conteneur pet
                // (eIdx=1 ↔ ElfEquip @0x0808cb20). AVANT le reload
                // d'ÉmitChangeEquip pour que le 277 porte le flag.
                if (eqOk && !item.IsBound &&
                    (s.GameData.Items.FirstOrDefault(
                        i => i.Id == item.ItemId)?.Bindable ?? false))
                {
                    await s.InventoryRepo.SetItemBindAsync(item.Id);
                    item.IsBound = true;
                }
                if (eqOk && s.FastDressItemMoved != null)
                {
                    await s.FastDressItemMoved(charId, item.Id,
                        1, loc, eIdx);
                    if (oldEq != null)
                        await s.FastDressItemMoved(charId, oldEq.Id,
                            0, srcSlot, srcIdx);
                }
                if (eqOk)
                {
                    // NotifyChangeEquipment natif → 276 aux
                    // observateurs (Equip @0x80b5810).
                    await EmitChangeEquipAsync(s, session, charId,
                        loc, eIdx,
                        await s.InventoryRepo.GetEquipmentItemAsync(
                            charId, eIdx, loc), token);
                    // Le loc résolu diffère du slot où le client a
                    // déposé l'icône (ex. arme droppée sur un slot
                    // tissu → canonique 7) : purge 275 du slot
                    // d'origine — le 277 renvoie le loc résolu.
                    if (dstSlot >= 0 && dstSlot != loc)
                        await PurgeEquipGhostAsync(s, session, charId,
                            dstSlot, eIdx, token);
                }
                else
                {
                    await PurgeEquipGhostsAsync(s, session, charId,
                        item.ItemId, dstSlot, eIdx, token);
                }
                return (eqOk, loc, eIdx);
            }

            // Équipement → équipement : swap entre slots (natif
            // SwitchEquip — ex. passer une 1H de la loc 7 à la 8
            // pour équiper une seconde arme). La résolution de loc
            // s'applique aussi ici : une 2H (type 9) ne peut pas être
            // glissée en loc 8 — sinon 2H en main + item en loc 8
            // pouvaient coexister via ce chemin.
            case (1, 1):
            {
                if (dstSlot < 0) return (false, dstSlot, dstIdx);
                var eqItem = (await s.InventoryRepo
                        .GetEquippedItemEntitiesAsync(charId))
                    .FirstOrDefault(e => e.Slot == srcSlot &&
                                         e.BagId == srcIdx);
                // Slot source vide en DB = icône fantôme : purge 275
                // pour la retirer du panneau client.
                if (eqItem == null)
                {
                    await PurgeEquipGhostAsync(s, session, charId,
                        srcSlot, srcIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                var eqLoc = await ResolveEquipLocAsync(s, charId,
                    eqItem.ItemId, dstSlot, Math.Max((short)0, dstIdx));
                // Refus avec slot explicite → icône fantôme possible
                // (résolution canonique divergente) : purge 275.
                if (eqLoc < 0)
                {
                    await PurgeEquipGhostAsync(s, session, charId,
                        dstSlot, dstIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                if (!await ClearWeaponConflictsAsync(s, charId,
                    eqItem.ItemId, eqLoc, dstIdx, session, token))
                {
                    await PurgeEquipGhostAsync(s, session, charId,
                        dstSlot, dstIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                // L'item du loc cible bascule vers la position source —
                // deux suivis UpdateFastDressFromInfo natifs.
                var dstEq = await s.InventoryRepo.GetEquipmentItemAsync(
                    charId, dstIdx, eqLoc);
                var swOk = await s.InventoryRepo.SwapEquipmentAsync(charId,
                    srcIdx, srcSlot, dstIdx, eqLoc);
                if (swOk && s.FastDressItemMoved != null)
                {
                    await s.FastDressItemMoved(charId, eqItem.Id,
                        1, eqLoc, dstIdx);
                    if (dstEq != null)
                        await s.FastDressItemMoved(charId, dstEq.Id,
                            1, srcSlot, srcIdx);
                }
                if (swOk)
                {
                    // NotifyChangeEquipment ×2 natif — les deux locs
                    // changent (EquipForSwitch/UnEquipForSwitch).
                    await EmitChangeEquipAsync(s, session, charId,
                        eqLoc, dstIdx, eqItem, token);
                    await EmitChangeEquipAsync(s, session, charId,
                        srcSlot, srcIdx,
                        await s.InventoryRepo.GetEquipmentItemAsync(
                            charId, srcIdx, srcSlot), token);
                    // Swap vers un loc résolu différent du slot
                    // demandé → l'icône au slot demandé est fantôme.
                    if (eqLoc != dstSlot)
                        await PurgeEquipGhostAsync(s, session, charId,
                            dstSlot, dstIdx, token);
                }
                else
                    // Échec : le client a pu peindre l'icône au slot
                    // cible alors que rien n'a bougé côté serveur.
                    await PurgeEquipGhostAsync(s, session, charId,
                        dstSlot, dstIdx, token);
                return (swOk, eqLoc, dstIdx);
            }

            // Équipement → sac : déséquiper vers le slot demandé.
            // dstIdx=-1 = « n'importe quel sac » natif
            // (CInventory::GetContainerWithEmptySlot — itère les
            // conteneurs ouverts 0-4) : forcer bag0 débordait sa
            // capacité affichée → PutIn client OOB → crash live.
            case (1, 0):
            {
                // Slot équipement vide en DB + icône affichée =
                // fantôme : purge 275 à chaque issue d'échec.
                short toBag, toSlot;
                if (dstIdx >= 0 && dstSlot >= 0)
                {
                    toBag = dstIdx;
                    toSlot = dstSlot;
                }
                else if (dstIdx >= 0)
                {
                    toBag = dstIdx;
                    toSlot = await FirstFreeSlotAsync(s, charId, toBag);
                }
                else
                {
                    var any = await s.InventoryRepo
                        .GetAnyAvailableSlotAsync(charId);
                    if (any == null)
                    {
                        await PurgeEquipGhostAsync(s, session, charId,
                            srcSlot, srcIdx, token);
                        return (false, dstSlot, dstIdx);
                    }
                    (toBag, toSlot) = ((short)any.Value.BagId,
                        (short)any.Value.Slot);
                }
                if (toSlot < 0)
                {
                    await PurgeEquipGhostAsync(s, session, charId,
                        srcSlot, srcIdx, token);
                    return (false, dstSlot, dstIdx);
                }
                var uneq = await s.InventoryRepo.GetEquipmentItemAsync(
                    charId, srcIdx, srcSlot);
                var unOk = await s.InventoryRepo.MoveToInventoryAsync(
                    charId, srcIdx, srcSlot, toBag, toSlot);
                // UpdateFastDressFromInfo natif — la pièce suit dans
                // les records dress.
                if (unOk && uneq != null && s.FastDressItemMoved != null)
                    await s.FastDressItemMoved(charId, uneq.Id,
                        0, toSlot, toBag);
                if (unOk)
                {
                    // NotifyChangeEquipment natif → 276 slot vidé aux
                    // observateurs (UnEquip @0x80fdfd0).
                    await EmitChangeEquipAsync(s, session, charId,
                        srcSlot, srcIdx, null, token);
                }
                else
                {
                    // Icône fantôme : le client croit le slot occupé,
                    // la DB non — purge 275 (traitée en jeu).
                    await PurgeEquipGhostAsync(s, session, charId,
                        srcSlot, srcIdx, token);
                }
                return (unOk, toSlot, toBag);
            }

            // Sac → conteneur elf (dstType 6 — désasm
            // CPlayerFactory::SwitchItemLocation table de saut :
            // case 6 → vecteur d'elfs char+0x8ac, dstIdx = loc elf,
            // dstSlot = slot 0-3 du CElfContainer).
            // CItemFactory::ElfEquip @0x808cb20 :
            //   - kind 81 obligatoire (GetKind==0x51)
            //   - dstSlot 0xffff (-1) REFUSÉ natif — slot explicite
            //   - EquipLoc {7,19} → slots 0-1, {10,34} → slots 2-3
            //   - elf au loc dstIdx requis ; niveau elf ∈
            //     [data+0x3e = RequiredLevel, data+0x3f = MaxLevel]
            //   - durabilité > 0 (item+0xc ≥ 1).
            case (0, 6):
            {
                var item = await s.InventoryRepo.GetItemAsync(
                    charId, srcIdx, srcSlot);
                if (item == null || item.Quantity <= 0)
                    return (false, dstSlot, dstIdx);
                var def = s.GameData.Items.FirstOrDefault(
                    i => i.Id == item.ItemId);
                if (def?.Kind != 81 || dstSlot < 0 || dstSlot > 3)
                    return (false, dstSlot, dstIdx);
                var pair = def.EquipLoc switch
                {
                    7 or 19 => 0,    // arme du familier → slots 0-1
                    10 or 34 => 2,   // accessoire → slots 2-3
                    _ => -1,
                };
                if (pair < 0 || dstSlot < pair || dstSlot > pair + 1)
                    return (false, dstSlot, dstIdx);
                if (s.ElfRepo == null) return (false, dstSlot, dstIdx);
                var elf = (await s.ElfRepo.GetElfsAsync(charId))
                    .FirstOrDefault(e => e.Loc == dstIdx);
                if (elf == null) return (false, dstSlot, dstIdx);
                if (elf.Level < def.RequiredLevel ||
                    (def.MaxLevel > 0 && elf.Level > def.MaxLevel))
                    return (false, dstSlot, dstIdx);
                var ok6 = await s.InventoryRepo.MoveToElfContainerAsync(
                    charId, srcIdx, srcSlot, elf.ElfId, dstIdx, dstSlot);
                if (ok6)
                    await NotifyElfEquipRefreshAsync(s, session, charId,
                        dstIdx, token);
                return (ok6, dstSlot, dstIdx);
            }

            // Conteneur elf → sac : déséquipement familier (ElfEquip
            // sens inverse — l'occupant éventuel du slot sac revient
            // dans le slot elf libéré, PutIn croisé natif).
            case (6, 0):
            {
                var elfItem = (await s.InventoryRepo
                        .GetElfContainerItemsAsync(charId))
                    .FirstOrDefault(i => i.BagId == srcIdx &&
                                         i.Slot == srcSlot);
                if (elfItem == null) return (false, dstSlot, dstIdx);
                short toBag, toSlot;
                if (dstIdx >= 0 && dstSlot >= 0)
                {
                    toBag = dstIdx;
                    toSlot = dstSlot;
                }
                else if (dstIdx >= 0)
                {
                    toBag = dstIdx;
                    toSlot = await FirstFreeSlotAsync(s, charId, toBag);
                }
                else
                {
                    var any = await s.InventoryRepo
                        .GetAnyAvailableSlotAsync(charId);
                    if (any == null) return (false, dstSlot, dstIdx);
                    (toBag, toSlot) = ((short)any.Value.BagId,
                        (short)any.Value.Slot);
                }
                if (toSlot < 0) return (false, dstSlot, dstIdx);
                var okB = await s.InventoryRepo.MoveFromElfContainerAsync(
                    charId, srcIdx, srcSlot, toBag, toSlot);
                if (okB)
                {
                    await NotifyElfEquipRefreshAsync(s, session, charId,
                        srcIdx, token);
                    s.NotifyItemChanged(charId, elfItem.ItemId);
                }
                return (okB, toSlot, toBag);
            }

            // Swap intra-conteneur elf — CItemFactory::ElfSwitchEquip
            // @0x808d700 : même elf uniquement (cross-elf → return 0)
            // et paires adjacentes 0↔1 / 2↔3 uniquement.
            case (6, 6):
            {
                if (srcIdx != dstIdx) return (false, dstSlot, dstIdx);
                var adjacent = (srcSlot, dstSlot) is
                    (0, 1) or (1, 0) or (2, 3) or (3, 2);
                if (!adjacent) return (false, dstSlot, dstIdx);
                var okS = await s.InventoryRepo
                    .SwitchElfContainerSlotsAsync(charId, srcIdx,
                        srcSlot, dstSlot);
                if (okS)
                    await NotifyElfEquipRefreshAsync(s, session, charId,
                        srcIdx, token);
                return (okS, dstSlot, dstIdx);
            }

            default:
                return (false, dstSlot, dstIdx);
        }
    }

    /// <summary>Refresh des stats du familier après un move d'équipement
    /// — miroir de <c>CElfPet::RefreshEQsAttr</c> @0x845a230 +
    /// <c>NotifyRefreshAttr</c> @0x8459870 : si l'elf est invoqué
    /// (status 2, natif +0x3e4 != 0) il envoie 4 × 418
    /// ClientElfUpdateAttr [loc][type][somme équipements][str vide] —
    /// types 8 = attaque (addattr+0x3c → CItemAttr+0x20, col45),
    /// 9 = précision (+0x54 → attr+0x38, col51),
    /// 10 = critique (+0x58 → attr+0x3c, col53),
    /// 11 = 4ᵉ stat pet (+0x60 → attr+0x44, col57). Le natif saute un
    /// item si le niveau du pet &lt; itemdata+0xfc+0x22 — même check
    /// qu'à l'équipement (RequiredLevel).</summary>
    private static async Task NotifyElfEquipRefreshAsync(ZoneServices s,
        Eden.Core.Networking.Session.NetSession session, int charId,
        short elfLoc, CancellationToken token)
    {
        if (s.ElfRepo == null) return;
        var elf = (await s.ElfRepo.GetElfsAsync(charId))
            .FirstOrDefault(e => e.Loc == elfLoc);
        if (elf == null || elf.Status != 2) return;
        var gear = await ZoneEntryFlow.PetGearTotalsAsync(s, charId,
            elfLoc, elf.Level);
        int atk = gear.Atk, hit = gear.Hit, crit = gear.Crit,
            extra = gear.Extra;
        // Applique l'attaque équipement au pet live (RefreshEQsAttr).
        var live = s.Live.AllMobs.FirstOrDefault(m =>
            m.Alive && m.IsElfPet && m.ElfLoc == elfLoc &&
            m.OwnerCharId == charId);
        if (live != null && s.PartnerGrows.GetValueOrDefault(
                Math.Max(1, elf.Level)) is { } g)
            live.AttackPower = PetRules.AttackPower(
                g.Stat(0, elf.GrowAttack), atk);
        var values = new[] { atk, hit, crit, extra };
        for (short i = 0; i < 4; i++)
            await session.SendAsync(new CNC_CZ_ClientElfUpdateAttr
            {
                Field1 = elfLoc,                 // s16 — loc de l'elf
                Field2 = (short)(8 + i),         // s16 — type attr 8-11
                Field3 = values[i],              // i32 — somme équipements
                Field4 = string.Empty,           // str — natif envoie ""
            }, token);
    }

    /// <summary>Équipe un item sac (kind 43) dans le porte-sacs —
    /// miroir de `CPlayerFactory::SwitchBagFromContainer` @0x8101160 :
    /// dstLoc 1-4 (0 refusé), kind 0x2b obligatoire (texte 0x32a),
    /// si le slot est occupé le sac d'inventaire dstLoc doit être vide
    /// quand la nouvelle capacité est inférieure (texte 0x306). Ouvre/
    /// resize inventory_slots.bag_dstLoc_slots = capacité du sac.</summary>
    internal static async Task<(bool ok, short dstSlot, short dstIdx)>
        EquipBagAsync(ZoneServices s, int charId, short srcBagId,
        short srcSlot, short holderIdx, short holderLoc,
        Eden.Core.Networking.Session.NetSession session,
        CancellationToken token)
    {
        // Holder 4 = porte-sacs de la BANQUE (CStorage côté natif) —
        // non implémenté : refus natif (277 result=0).
        if (holderIdx != 0 || holderLoc <= 0 || holderLoc > 4)
            return (false, holderLoc, holderIdx);
        var item = await s.InventoryRepo.GetItemAsync(
            charId, srcBagId, srcSlot);
        if (item == null) return (false, holderLoc, holderIdx);
        var def = s.GameData.Items.FirstOrDefault(d => d.Id == item.ItemId);
        if (def == null || def.Kind != 0x2b)
        {
            // 0x32a — "l'item n'est pas un sac" (index texte natif).
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 0x32a, Field2 = string.Empty,
            }, token);
            return (false, holderLoc, holderIdx);
        }
        var existing = await s.InventoryRepo.GetEquippedBagAsync(
            charId, 0, holderLoc);
        if (existing != null)
        {
            // Swap de sacs : le sac inv dstLoc doit être vide si la
            // nouvelle capacité est plus petite (check natif
            // GetEmptySlotNum==GetContainerSize → erreur 0x306).
            var oldCap = s.GameData.Items
                .FirstOrDefault(d => d.Id == existing.ItemId)
                ?.BagCapacity ?? 0;
            if (def.BagCapacity < oldCap && !await BagIsEmptyAsync(
                s, charId, holderLoc))
            {
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                {
                    Field1 = 0x306, Field2 = string.Empty,
                }, token);
                return (false, holderLoc, holderIdx);
            }
            // Le sac sortant retourne à l'emplacement source du nouvel
            // item (SwitchContainerItem échange les deux positions).
            var moved = await s.InventoryRepo.MoveBagToInventoryAsync(
                charId, 0, holderLoc, srcBagId, srcSlot);
            if (!moved) return (false, holderLoc, holderIdx);
        }
        var ok = await s.InventoryRepo.MoveItemToBagHolderAsync(
            charId, srcBagId, srcSlot, 0, holderLoc);
        if (!ok) return (false, holderLoc, holderIdx);
        // Bind à l'équipe du sac — CheckAutoStackItem @0x8090410 :
        // déplacement vers conteneur type 5 + Flags&0x80 → |=0x10.
        if (!item.IsBound && def.Bindable)
        {
            await s.InventoryRepo.SetItemBindAsync(item.Id);
            item.IsBound = true;
        }
        // AddNewContainer/UpdateContainerSize natif : le sac d'inv
        // dstLoc prend la capacité du sac équipé (clone+0xc = col28).
        await s.InventoryRepo.SetBagCapacityAsync(
            charId, holderLoc, def.BagCapacity);
        // Sonde live (bug « dernier sac = 1 slot ») : trace chaque
        // écriture de capacité pour confirmer la valeur côté DB.
        EdenLog.Write($"[Zone] EquipBag char={charId} item=" +
            $"{item.ItemId} cap={def.BagCapacity} -> " +
            $"bag_{holderLoc}_slots");
        return (true, holderLoc, holderIdx);
    }

    /// <summary>Déséquipe un sac — miroir de `CPlayerFactory::
    /// SwitchBagToContainer` @0x80f4c60 : le sac d'inventaire srcLoc
    /// doit être vide (0x306), sinon refus. Le sac retourne dans
    /// l'inventaire au slot demandé/résolu ; DeleteContainer →
    /// capacité remise à 0.</summary>
    private static async Task<(bool ok, short dstSlot, short dstIdx)>
        UnequipBagAsync(ZoneServices s, int charId, short holderIdx,
        short holderLoc, short dstBagId, short dstSlot,
        Eden.Core.Networking.Session.NetSession session,
        CancellationToken token)
    {
        if (holderIdx != 0 || holderLoc <= 0 || holderLoc > 4)
            return (false, dstSlot, dstBagId);
        var bag = await s.InventoryRepo.GetEquippedBagAsync(
            charId, 0, holderLoc);
        if (bag == null) return (false, dstSlot, dstBagId);
        if (!await BagIsEmptyAsync(s, charId, holderLoc))
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 0x306, Field2 = string.Empty,
            }, token);
            return (false, dstSlot, dstBagId);
        }
        var toBag = dstBagId >= 0 ? dstBagId : (short)0;
        var toSlot = dstSlot >= 0 ? dstSlot
            : await FirstFreeSlotAsync(s, charId, toBag);
        if (toSlot < 0) return (false, dstSlot, dstBagId);
        var ok = await s.InventoryRepo.MoveBagToInventoryAsync(
            charId, 0, holderLoc, toBag, toSlot);
        if (!ok) return (false, dstSlot, dstBagId);
        // DeleteContainer natif → le sac d'inv se ferme.
        await s.InventoryRepo.SetBagCapacityAsync(charId, holderLoc, 0);
        return (true, toSlot, toBag);
    }

    /// <summary>Déplace/échange deux sacs équipés dans le porte-sacs —
    /// désasm `CPlayerFactory::SwitchBagToBag` @0x810a770 : les
    /// conteneurs d'inventaire srcLoc↔dstLoc sont ÉCHANGÉS items
    /// inclus (SwitchContainersAndItems natif — le sac d'inv suit le
    /// slot du porte-sacs), et les capacités suivent les sacs
    /// équipés.</summary>
    private static async Task<(bool ok, short dstSlot, short dstIdx)>
        SwitchBagToBagAsync(ZoneServices s, int charId, short srcHolderIdx,
        short srcLoc, short dstHolderIdx, short dstLoc)
    {
        if (srcHolderIdx != 0 || dstHolderIdx != 0 ||
            srcLoc <= 0 || srcLoc > 4 || dstLoc <= 0 || dstLoc > 4 ||
            srcLoc == dstLoc)
            return (false, dstLoc, dstHolderIdx);
        var bag = await s.InventoryRepo.GetEquippedBagAsync(
            charId, 0, srcLoc);
        if (bag == null) return (false, dstLoc, dstHolderIdx);
        // Natif : SwitchContainersAndItems("inventory",src,dst) — les
        // conteneurs entiers (items + capacité) sont échangés. La
        // capacité est portée par le sac équipé : elle suit l'ITEM,
        // donc après le swap des items de sacs on échange aussi les
        // capacités des sacs inv (le contenu suit le sac).
        var srcCap = await s.InventoryRepo.GetBagCapacity(
            charId, srcLoc) ?? 0;
        var dstCap = await s.InventoryRepo.GetBagCapacity(
            charId, dstLoc) ?? 0;
        await s.InventoryRepo.SwapInventoryBagsAsync(
            charId, srcLoc, dstLoc);
        await s.InventoryRepo.SetBagCapacityAsync(
            charId, srcLoc, dstCap);
        await s.InventoryRepo.SetBagCapacityAsync(
            charId, dstLoc, srcCap);
        await s.InventoryRepo.SwapEquippedBagLocsAsync(
            charId, 0, srcLoc, dstLoc);
        return (true, dstLoc, dstHolderIdx);
    }










}
