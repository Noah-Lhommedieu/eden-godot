using Eden.Core.Networking;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inlay;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Actions sur items : identification, réparation effective, sertissage
/// de gemmes, renforcement, utilisation, métiers de vie.
/// (Les devis AskRepair*/AskIdentify* sont dans InventoryHandlers.)
/// </summary>
public static class ItemActionHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // RepairEquipment — répare tout l'équipement porté (or → coût calculé)
        d.RegisterChar(() => new CNE_CZ_ZoneServerRepairEquipment(),
            async (session, msg, charId, token) =>
            {
                var cost = await RepairAllAsync(s, charId);
                if (cost <= 0) return;
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < cost) return;
                await s.Characters.AddGoldAsync(charId, -cost);
                await RepairAllAsync(s, charId, apply: true);
                EdenLog.Write($"[Zone] RepairEquipment char={charId} cost={cost}");
            });

        // RepairOneItem [byte bag, byte ?, short slot]
        d.RegisterChar(() => new CNE_CZ_ZoneServerRepairOneItem(),
            async (session, msg, charId, token) =>
            {
                var item = (await s.InventoryRepo.GetItemsByCharacterIdAsync(charId))
                    .FirstOrDefault(i => i.Slot == msg.Field3);
                if (item == null || item.MaxDurability <= 0) return;
                // Formule native GetItemRepairPrice (col76×0.2/maxDur
                // ×missingDur, min 1) — partagée avec RepairAll.
                var cost = InventoryHandlers.ItemRepairCost(s, item);
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < cost) return;
                await s.Characters.AddGoldAsync(charId, -cost);
                item.Durability = item.MaxDurability;
                await s.InventoryRepo.UpdateItemAsync(item);
            });

        // IdentifyItem [u16 containerId, u16 loc, s16 ?] — flux natif
        // CItemFactory::IdentifyItem @0x809da40 : container 0 =
        // inventaire (natif : toute autre valeur = rejet loggé),
        // éligible = type lapis < 2 (ItemIdentifyCost > 0),
        // clone+0x24 bit3 = déjà identifié → texte 0x5a43.
        d.RegisterChar(() => new CNE_CZ_ZoneServerIdentifyItem(),
            async (session, msg, charId, token) =>
            {
                if (msg.Field1 != 0) return;
                var item = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field1, msg.Field2);
                if (item == null) return;
                // Textes natifs : 0x5a45 non éligible, 0x5a43 déjà
                // identifié, 0x5a44 or insuffisant, 0x5a46 succès.
                var cost = InventoryHandlers.ItemIdentifyCost(s, item.ItemId);
                if (cost <= 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x5a45, Field2 = "" }, token);
                    return;
                }
                if (item.Identify != 0)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x5a43, Field2 = "" }, token);
                    return;
                }
                var gold = await s.Characters.GetGoldAsync(charId);
                if (gold < cost)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x5a44, Field2 = "" }, token);
                    return;
                }
                await s.Characters.AddGoldAsync(charId, -cost);

                // ChangeEmbeddedAmount natif : le roll des sockets
                // ouvertes peut changer à l'identification — rejoué
                // seulement si aucune socket n'était ouverte (sinon
                // le natif écraserait des gemmes serties).
                var def = s.GameData.Items.FirstOrDefault(
                    d2 => d2.Id == item.ItemId);
                if (item.EmbeddedAmount == 0 && def != null &&
                    Eden.Core.Game.Inlay.InlayService.IsSocketCapable(def))
                {
                    var (sockCount, sockIds) = s.Inlay.RollSockets(def);
                    if (sockCount > 0)
                    {
                        item.EmbeddedAmount = sockCount;
                        item.EmbeddedId1 = sockIds[0];
                        item.EmbeddedId2 = sockIds[1];
                        item.EmbeddedId3 = sockIds[2];
                        item.EmbeddedId4 = sockIds[3];
                        item.EmbeddedId5 = sockIds[4];
                        item.EmbeddedId6 = sockIds[5];
                        await s.InventoryRepo.UpdateEmbeddedAsync(item.Id,
                            sockCount, sockIds);
                    }
                }

                // clone+0x24 |= 8 → colonne identify = 1.
                item.Identify = 1;
                await s.InventoryRepo.UpdateItemAsync(item);

                // 364 ClientIdentifyItem — corps 22 o mappé sur le
                // ctor natif @0x809dc3x :
                // [s16 contType][s16 loc][u16 sockCount][6×s16
                //  embedded_ids][s16 contIndex].
                await session.SendAsync(new CNC_CZ_ClientIdentifyItem
                {
                    Field1 = 1,                     // s16 type conteneur (inventaire)
                    Field2 = msg.Field2,            // s16 loc demandée
                    Field3 = (short)(item.EmbeddedAmount & 7), // u16 nb sockets
                    Field4 = (short)item.EmbeddedId1,
                    Field5 = (short)item.EmbeddedId2,
                    Field6 = (short)item.EmbeddedId3,
                    Field7 = (short)item.EmbeddedId4,
                    Field8 = (short)item.EmbeddedId5,
                    Field9 = (short)item.EmbeddedId6,
                    Field10 = (short)msg.Field1,    // s16 container_index
                }, token);
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0x5a46, Field2 = "" }, token);
                EdenLog.Write($"[Zone] Identify char={charId} " +
                    $"item={item.ItemId} cost={cost} ok");
            });

        // InlayItem (67) — intercepté par ZoneEntryFlow.TryHandleAsync :
        // gate CInlayShopOp (0x23, ouverte par `inlay_shop`) +
        // talking-range → 371 ClientInlayItemResult. Le
        // CItemFactory::InlayItem natif est un stub retournant 0 —
        // le vrai sertissage passe par UseItemToItemControl 0x2d
        // (InlayService, chemin item-sur-item déjà implémenté).

        // StrengthenItem (119) — intercepté par ZoneEntryFlow
        // (TryHandleAsync) : flux natif complet CheckStrengthenItem →
        // cast 433 → DoStrengthenItem (435/436/275) dans
        // ZoneEntryFlow.Strengthen.cs.

        // LifeSkill (214) / QueryLifeSkill (213) — interceptés par
        // ZoneEntryFlow.TryHandleAsync : flux natif complet
        // CLifeSkillFactory (Produce/Analysis/Exp/Forget/LevelUp/
        // Apperception) dans ZoneEntryFlow.LifeSkill.cs.

        // ItemUse — wire natif (désasm CNE_CZ_ZoneServerItemUse::Execute
        // @0x8260490) : [s16 contType][s16 contIdx][s16 slot][i32 cible]
        // [string][3×s16]. contType : 0 = map sacs (char+0x139),
        // 1 = map équipement (char+0x13f) ; contIdx = clé de la map
        // (n° de sac) ; slot = loc dans le conteneur. Slot vide ou hors
        // bornes → le natif envoie une string-id (0x5ee/0x5f7), jamais
        // de silence.
        d.RegisterChar(() => new CNE_CZ_ZoneServerItemUse(),
            async (session, msg, charId, token) =>
            {
                // Field1 = type de conteneur, Field2 = index (sac),
                // Field3 = slot — NE PAS confondre avec (bag,slot) !
                // StateNoUseItem natif (compteur char+0x8c+0xc0 > 0,
                // enchant cmd 2055 var5) : l'usage est refusé pendant
                // stun/silence-item — ack pour libérer le client.
                if (((s.StateLocksOf?.Invoke(charId) ?? 0) & 16) != 0)
                {
                    await SendItemUseAckAsync(session, charId, 0,
                        msg.Field1, msg.Field2, msg.Field3, token);
                    return;
                }
                if (msg.Field1 != 0)
                {
                    // Conteneur équipement : usage d'un item porté
                    // (non implémenté — ack pour débloquer le client).
                    await SendItemUseAckAsync(session, charId, 0,
                        msg.Field1, msg.Field2, msg.Field3, token);
                    return;
                }
                var item = await s.InventoryRepo.GetItemAsync(
                    charId, msg.Field2, msg.Field3);
                if (item == null || item.Quantity <= 0)
                {
                    // Toujours répondre : un silence laisse le client
                    // en attente modale → freeze (constaté live).
                    await SendItemUseAckAsync(session, charId, 0,
                        msg.Field1, msg.Field2, msg.Field3, token);
                    return;
                }
                // Normalisation u16 : un item_id signé en DB (37115 →
                // -28421, observé live) rendait l'item « hors tables »
                // et inutilisable. On masque et on LOGUE l'anomalie —
                // si un chemin réécrit un id signé, le log le dira.
                if (item.ItemId < 0)
                {
                    var fixedId = item.ItemId & 0xffff;
                    EdenLog.Write($"[Use] item_id anormal char={charId} " +
                        $"serial={item.Id} raw={item.ItemId} → {fixedId}");
                    item.ItemId = fixedId;
                }
                var def = s.GameData.Items.FirstOrDefault(i => i.Id == item.ItemId);
                if (def == null)
                {
                    // Item hors TOUTES les tables (S_Item + S_ItemMall
                    // fusionnées) : ack natif sans consommation.
                    await SendItemUseAckAsync(session, charId,
                        item.ItemId, 0, msg.Field2, msg.Field3, token);
                    EdenLog.Write($"[Zone] ItemUse char={charId} " +
                        $"item={item.ItemId} hors tables — ack sans effet");
                    return;
                }

                // Item à durée expirée — CheckUseItemCondition natif
                // refuse l'usage (le client le fait déjà côté UI :
                // "0 sec. remain" + "Unable to use", mais le paquet
                // peut quand même partir). Refus texte 0x5ef comme
                // le check natif.
                if (item.DueDate > 0 && item.DueDate <
                        (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x5ef, Field2 = "" }, token);
                    return;
                }

                // Garde mort/vie : mort = aucun usage hors item de
                // revive ; vivant = un item de revive n'est pas gaspillé.
                if (s.ItemUseLifeGate != null &&
                    !await s.ItemUseLifeGate(session, charId, item.ItemId,
                        def.UseEnchantId, token))
                {
                    await SendItemUseAckAsync(session, charId,
                        item.ItemId, 0, msg.Field2, msg.Field3, token);
                    EdenLog.Write($"[Zone] ItemUse char={charId} " +
                        $"item={item.ItemId} refusé par la garde mort/vie");
                    return;
                }

                // Cooldown d'usage natif : CheckIsCoolDowning (refus +
                // texte 0x5ef) puis AddCoolDownItem — armé AVANT
                // l'exécution comme DoUseItem @0x80b8a10.
                if (!await TryArmItemCooldownAsync(session, s, charId,
                        item, def, token))
                    return;

                // Item-sur-item natif : use-type 3/5
                // (CItemData+0x38 = S_Item col12) →
                // UseItemToItemControl — les 3 s16 de queue du wire
                // portent (contType, contIdx, loc) de la CIBLE
                // (désasm CItemFactory::UseItemParam @0x80b9490).
                if (def.UseType is 3 or 5)
                {
                    await HandleItemOnItemAsync(session, s, charId,
                        msg, item, def, token);
                    return;
                }

                // Arming natif de durée (DoUseItem @0x80b9a10) :
                // si l'item a une durée (CItemData+0x70 ≠ 0) et
                // due_date == 0 → due_date = now + durée, persiste
                // (CItemDBO::UpdateItemDueDate) + ClientItemRent 461
                // [s16 contType][s16 contIdx][s16 loc][u32 dueDate].
                // Sans ça les items à durée restent "0 sec. remain".
                if (def.DurationSeconds > 0 && item.DueDate == 0)
                {
                    item.DueDate = (int)DateTimeOffset.UtcNow
                        .ToUnixTimeSeconds() + def.DurationSeconds;
                    await s.InventoryRepo.UpdateDueDateAsync(item.Id,
                        item.DueDate);
                    await session.SendAsync(new CNC_CZ_ClientItemRent
                    {
                        Field1 = msg.Field1,            // s16 type de conteneur (0 = sac)
                        Field2 = msg.Field2,            // s16 index du sac
                        Field3 = msg.Field3,            // s16 loc dans le conteneur
                        Field4 = (uint)item.DueDate,    // u32 due_date epoch armé
                    }, token);
                }

                // Monture : l'enchant d'usage de l'item porte la
                // commande 2134 (CEC_Ride) — le natif n'applique pas
                // l'item comme consommable mais toggle l'état de ride
                // (CPlayerFactory::CheckRide / CEC_RideSpell 2140 pour
                // les buffs de monture de combat). L'ack ClientItemUse
                // est OBLIGATOIRE ici aussi : sans lui le client garde
                // l'item en attente d'usage → "cooldown" visuel
                // permanent constaté live.
                if (def.UseEnchantId > 0 &&
                    s.Enchants.GetRideInfo(def.UseEnchantId) is { } ride)
                {
                    await SendItemUseAckAsync(session, charId,
                        item.ItemId, 0, msg.Field2, msg.Field3, token);
                    // Bind à l'usage — `DoUseItem`/`DoUseItemAfterCheck`
                    // natifs appellent `CPlayerFactory::BindItem`
                    // @0x08116870 dès que le contrôle d'usage a exécuté
                    // (def+0xc signe = Flags & 0x80, clone+0x24 & 0x10
                    // non posé) — indépendamment de la consommation.
                    // Les montures togglent le ride sans consommer
                    // l'item : le chemin `ConsumeUsedItemAsync`
                    // (stacks résiduels) ne les atteint jamais → une
                    // monture « can't be traded » restait non liée.
                    // 369 mode 8 natif — le paquet bind dédié (ctor
                    // 369 : +0x68=8, +0x6a = flags>>4 & 1 = bind).
                    // Le record mode -1 ne porte pas le flag — sans
                    // mode 8 le tooltip restait « unbound » (preuve :
                    // FastDressDoItemBind @0x080b9d60).
                    if (!item.IsBound && def.Bindable)
                    {
                        await s.InventoryRepo.SetItemBindAsync(item.Id);
                        item.IsBound = true;
                        // GetType() du conteneur = 0 (sac CInventory)
                        // — l'item monture n'est jamais équipé.
                        await session.SendPacketAsync(369,
                            Codecs.MagicBoxPackets.BuildModifyItemInfoBind(
                                0, msg.Field2, msg.Field3, 1),
                            token);
                    }
                    if (s.RideStates.TryGetValue(charId, out var cur) &&
                        cur.ItemSerial == item.Id)
                    {
                        // Descendre : retrait du ride (CheckRide
                        // natif → CancelPlayerEnchant + clear 0x7f4) —
                        // séquence complète DismountAsync déléguée à
                        // la boucle (300 + 302 bit9 + 283 mask2=0 +
                        // 545 vide + purge player_enchants/stats).
                        if (s.RideRemove != null)
                            await s.RideRemove(session, charId, token);
                    }
                    else
                    {
                        // Monter : l'enchant de ride devient un vrai
                        // statut persisté (EffectToLife natif — le
                        // ride survit à la migration via la
                        // restauration player_enchants). Le hook 2134
                        // d'AddPlayerEnchantAsync émet 299 + 302
                        // bit9 + 283 mask=2 + 545 et peuple
                        // RideStates — même ordre wire que la
                        // séquence manuelle d'avant.
                        // Une seule monture à la fois : une autre monture
                        // déjà montée est d'abord retirée (300 + 302 +
                        // 283 vide), sinon deux statuts de ride
                        // coexistaient.
                        if (s.RideStates.ContainsKey(charId) &&
                            s.RideRemove != null)
                            await s.RideRemove(session, charId, token);
                        if (s.RideApply != null)
                            await s.RideApply(session, charId, item.Id,
                                def.UseEnchantId, token);
                    }
                    EdenLog.Write($"[Zone] Ride char={charId} " +
                        $"item={item.ItemId} enchant={def.UseEnchantId} " +
                        $"key={ride.RideKey} flag={ride.Flag} " +
                        $"seats={ride.Seats} speed=+{ride.SpeedPct}% " +
                        $"buffs=[{string.Join(',', ride.CombatBuffs)}]");
                    return;
                }

                // Équipement au clic droit : le client envoie NCID 7
                // (SwitchItemLocation) — mais selon les builds il peut
                // AUSSI envoyer un ItemUse. Le natif ne crée un control
                // que pour les UseType 1/2/3/5/6 (`UseItemParam`
                // @0x80b9490 ; équipement = UseType 0 → `DoUseItem`
                // @0x80b8a10 « param_4==NULL → return 0 »). Ce chemin
                // est donc une extension maison — RESTREINTE aux vrais
                // kinds équipement joueur : l'ancien prédicat
                // `EquipLoc>0` équipait aussi les scrolls/gemmes/items
                // pet (un ItemUse scroll→arc équipait la robe du slot
                // source → « enchant en body cloth », bug live 19:37).
                if (def.UseEnchantId == 0 &&
                    Eden.Core.Game.Inventory.NativeEquipmentSlots
                        .IsPlayerEquippableKind(def.Kind))
                {
                    // Loc résolu par la traduction native type→slot
                    // (Equip @0x80b5810 : 19→7, 35→10, 9→7, dual 7→8)
                    // — sans elle haches/bâtons partaient dans des
                    // locs inexistants → item disparu du sac.
                    var eloc = await InventoryHandlers
                        .ResolveEquipLocAsync(s, charId, item.ItemId, -1);
                    if (eloc < 0)
                    {
                        await SendItemUseAckAsync(session, charId,
                            item.ItemId, 0, msg.Field2, msg.Field3,
                            token);
                        return;
                    }
                    // Conflits 1H/2H (2H vide l'off-hand, off-hand avec
                    // 2H équipée retire la 2H) + swap natif : si le loc
                    // est occupé l'ancienne pièce repart dans le sac au
                    // slot libéré (MoveToEquipmentAsync transactionnel —
                    // « si le slot est occupé alors juste remplacer »).
                    var ok = await InventoryHandlers
                        .ClearWeaponConflictsAsync(
                            s, charId, item.ItemId, (short)eloc, 0,
                            session, token)
                        && await s.InventoryRepo.MoveToEquipmentAsync(
                        charId, msg.Field2, msg.Field3, 0, (short)eloc);
                    // Toujours répondre (même en échec — sinon le client
                    // fige la fenêtre en attente d'ack, vu live).
                    await session.SendAsync(
                        new CNC_CZ_ClientSwitchItemLocationOK
                        {
                            Field1 = 0, Field2 = msg.Field3,
                            Field3 = msg.Field2,
                            Field4 = 1, Field5 = (short)eloc,
                            Field6 = 0, Field7 = (byte)(ok ? 1 : 0),
                        }, token);
                    // RefreshEQsAttr natif : profil = base + équipement
                    // (296/492/493) — rejoué après l'équipe aussi par
                    // ce chemin (le client n'envoie pas toujours le
                    // NCID 8 après un clic droit).
                    if (ok)
                    {
                        // CCharKfm natif rebuild ses pools d'anim au
                        // changement d'arme — sans invalidation les
                        // anims restent sur l'ancienne paire/kind.
                        s.InvalidateWeaponAnim?.Invoke(session.SessionId);
                        await ProfileStatEmitter.SendForCharacterAsync(
                            session, s, charId, token);
                    }
                    EdenLog.Write($"[Zone] ItemUse-equip char={charId} " +
                        $"item={item.ItemId} kind={def.Kind} -> " +
                        $"loc={eloc} ({(ok ? "ok" : "refusé")})");
                    return;
                }

                // Livres de sorts — UseItemControl natif @0x84ec260 :
                // kind 22 (CSpellBookItem) → SimpleGetSpell(char, sort,
                // level, 1) ; kind 25 (CSpellLvUpBookItem) →
                // SetSpellLevelByBook : sort inconnu → niveau 1,
                // connu → niveau courant + 1. Sort enseigné = S_Item
                // col35 (CSpellBookItem+0x138 = puVar3[0x23], désasm
                // @0x835b446). Persistance = SQL natif
                // GetSpellMasterInsert/Update (LearnSpellAsync) puis
                // 286 ClientGetSpell + 316 nom — même corps que le
                // flux spellmaster (PlayerHandlers BuySpell).
                if ((def.Kind == 22 || def.Kind == 25) &&
                    def.LearnSpellId > 0)
                {
                    var smId = unchecked((short)def.LearnSpellId);
                    var lvl = await s.Classes.LearnSpellAsync(
                        charId, smId);
                    s.SpellLevelsDirty?.Invoke(charId);
                    await session.SendAsync(new CNC_CZ_ClientGetSpell
                    {
                        Field1 = smId,                  // s16 : sort appris
                        Field2 = smId,                  // s16 : sort (doublé natif)
                        Field3 = (short)lvl,            // s16 : niveau atteint
                        Field4 = 0,                     // i32 : réservé natif
                    }, token);
                    var spellName = s.GameData.SpellNames
                        .GetValueOrDefault(unchecked((ushort)smId), "");
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    {
                        Field1 = 0x05f4,            // s16 : slot de nom
                        Field2 = spellName,         // str : nom T_Spell
                    }, token);
                    // Le livre se consomme comme un consommable —
                    // on poursuit vers le bloc de consommation natif
                    // (RemoveItem + 291/303/275) ci-dessous.
                }

                // Items œuf de familier (kind 60 — S_Partner) :
                // CItemFactory::DoUseItem natif @0x80b8xxx appelle
                // CKFElfManager::AddElf (sVar10 == 0x3c) puis POURSUIT
                // le pipeline d'usage — l'œuf passe par
                // DoUseItemAfterCheck et se consomme comme tout
                // consommable (return 0 si AddElf échoue → item gardé).
                // Garder l'item après succès = re-cliquable = dup d'elf.
                if (def.Kind == 60)
                {
                    var added = s.ElfUseItem != null &&
                        await s.ElfUseItem(session, charId, item,
                            token);
                    if (added)
                        await ConsumeUsedItemAsync(session, s, charId,
                            item, def, 0, msg.Field2, msg.Field3, token);
                    else
                        // Refus AddElf (10 slots pleins) — ack UI,
                        // item conservé (return 0 natif).
                        await SendItemUseAckAsync(session, charId,
                            item.ItemId, 0, msg.Field2, msg.Field3,
                            token);
                    EdenLog.Write($"[Zone] ElfItem char={charId} " +
                        $"item={item.ItemId} -> " +
                        $"{(added ? "elf ajouté + œuf consommé" : "refusé")}");
                    return;
                }

                // Livres de sorts pet — S_Item col12 UseType 6 →
                // `UseItemToElfControl` natif (NotifyUseItem @0x84ebeb0
                // : la loc elf = field8 = param_7). Commandes enchant
                // 6035-6038 → LearnSpell / LearnSpellFromGroup sur
                // l'elf — JAMAIS sur le joueur : ce chemin court-
                // circuite les effets joueur. Consommé uniquement si
                // le sort est appris (refus natif = item gardé + ack).
                if (def.UseType == 6)
                {
                    var learned = s.ElfBookApply != null &&
                        await s.ElfBookApply(session, charId,
                            def.UseEnchantId, msg.Field8, token);
                    if (learned)
                        await ConsumeUsedItemAsync(session, s, charId,
                            item, def, 0, msg.Field2, msg.Field3, token);
                    else
                        // Ack d'usage quoi qu'il arrive — sinon la
                        // fenêtre d'inventaire reste en attente.
                        await SendItemUseAckAsync(session, charId,
                            item.ItemId, 0, msg.Field2, msg.Field3,
                            token);
                    EdenLog.Write($"[Zone] ElfBook char={charId} " +
                        $"item={item.ItemId} enchant={def.UseEnchantId} " +
                        $"elfLoc={msg.Field8} -> " +
                        $"{(learned ? "sort appris" : "refusé")}");
                    return;
                }

                // Sac (kind 43) au clic droit : équipe dans le
                // porte-sacs — le client n'envoie pas toujours le
                // SwitchItemLocation 0→5 ; sans ce chemin le sac
                // ne s'équipe jamais à la souris (le porte-sacs
                // reste à 1 slot affiché faute d'item équipé).
                if (def.Kind == 0x2b && def.BagCapacity > 0)
                {
                    short holder = 0;
                    for (short l = 1; l <= 4; l++)
                        if (await s.InventoryRepo.GetEquippedBagAsync(
                                charId, 0, l) == null)
                        { holder = l; break; }
                    if (holder == 0)
                    {
                        await SendItemUseAckAsync(session, charId,
                            item.ItemId, 0, msg.Field2, msg.Field3,
                            token);
                        return;
                    }
                    var (bok, bLoc, bIdx) = await InventoryHandlers
                        .EquipBagAsync(s, charId, msg.Field2,
                            msg.Field3, 0, holder, session, token);
                    EdenLog.Write($"[Zone] BagUse char={charId} " +
                        $"item={item.ItemId} cap={def.BagCapacity} " +
                        $"-> holder={holder} ({(bok ? "ok" : "refusé")})");
                    await session.SendAsync(
                        new CNC_CZ_ClientSwitchItemLocationOK
                        {
                            Field1 = 0, Field2 = msg.Field3,
                            Field3 = msg.Field2, Field4 = 5,
                            Field5 = bLoc, Field6 = bIdx,
                            Field7 = (byte)(bok ? 1 : 0),
                        }, token);
                    return;
                }

                // Canalisation d'usage (CItemData+0x54 = S_Item col20
                // ≠ 0 — DoUseItem @0x80b8a10) : le natif émet 407
                // ClientUseItemTime et retarde DoUseItemAfterCheck à
                // +N décisecondes (CEventUseItemTime) au lieu d'user
                // l'item immédiatement. Les items temporisés sont tous
                // UseType 2 → ils arrivent tous ici. char+0x90 occupé
                // → silence natif (l'item reste).
                if (def.UseTimeDs > 0 && s.ItemUseChannel != null &&
                    await s.ItemUseChannel(session, charId, item.ItemId,
                        item.Id, msg.Field2, msg.Field3, def.UseTimeDs,
                        (used, ct) => UseItemFinalAsync(session, s,
                            charId, used, def, 0, msg.Field2,
                            msg.Field3, ct), token))
                    return;

                // Les items « à cible » (UseType 3/5) sont déjà
                // sortis plus haut → HandleItemOnItemAsync ; le reste
                // suit le pipeline consommable natif.
                await UseItemFinalAsync(session, s, charId, item, def,
                    0, msg.Field2, msg.Field3, token);
            });

        // ItemMallUseItem (83) — CItemFactory::UseItemParam natif
        // (désasm CNE_CZ_ZoneServerItemMallUseItem::Execute) :
        // [s16 sel][s16 contIdx][s16 slot][i32 uiItemId][str params].
        // sel : 0 = map sacs, 1 = map équipement — même pipeline
        // d'usage que l'ItemUse 19, cible (-1,-1,-1). uiItemId =
        // clé des param-commands kind-keyed natives (drill 0xf3
        // absent des données — usage standard uniquement).
        d.RegisterChar(() => new CNE_CZ_ZoneServerItemMallUseItem(),
            async (session, msg, charId, token) =>
            {
                // GetItemContainer sel → conteneur sac ou équipement.
                var item = msg.Field1 == 0
                    ? await s.InventoryRepo.GetItemAsync(
                        charId, msg.Field2, msg.Field3)
                    : await s.InventoryRepo.GetEquipmentItemAsync(
                        charId, 0, msg.Field3);
                if (item == null || item.Quantity <= 0)
                {
                    await SendItemUseAckAsync(session, charId, 0,
                        msg.Field1, msg.Field2, msg.Field3, token);
                    return;
                }
                // Même normalisation u16 que l'ItemUse 19 (id signé →
                // masqué + logué).
                if (item.ItemId < 0)
                {
                    var fixedId = item.ItemId & 0xffff;
                    EdenLog.Write($"[Use] item_id anormal char={charId} " +
                        $"serial={item.Id} raw={item.ItemId} → {fixedId}");
                    item.ItemId = fixedId;
                }
                var def = s.GameData.Items
                    .FirstOrDefault(i => i.Id == item.ItemId);
                if (def == null)
                {
                    await SendItemUseAckAsync(session, charId,
                        item.ItemId, msg.Field1, msg.Field2, msg.Field3,
                        token);
                    return;
                }
                // Cooldown d'usage — même gate que l'ItemUse 19.
                if (!await TryArmItemCooldownAsync(session, s, charId,
                        item, def, token))
                    return;
                // Armement de durée — même bloc que l'ItemUse :
                // due_date = now + DurationSeconds → 461 ItemRent.
                if (def.DurationSeconds > 0 && item.DueDate == 0)
                {
                    item.DueDate = (int)DateTimeOffset.UtcNow
                        .ToUnixTimeSeconds() + def.DurationSeconds;
                    await s.InventoryRepo.UpdateDueDateAsync(item.Id,
                        item.DueDate);
                    await session.SendAsync(new CNC_CZ_ClientItemRent
                    {
                        Field1 = msg.Field1,
                        Field2 = msg.Field2,
                        Field3 = msg.Field3,
                        Field4 = (uint)item.DueDate,
                    }, token);
                }
                // Canalisation d'usage — même gate que l'ItemUse 19
                // (le natif passe aussi par UseItemParam → DoUseItem).
                if (def.UseTimeDs > 0 && s.ItemUseChannel != null &&
                    await s.ItemUseChannel(session, charId, item.ItemId,
                        item.Id, msg.Field2, msg.Field3, def.UseTimeDs,
                        (used, ct) => UseItemFinalAsync(session, s,
                            charId, used, def, msg.Field1, msg.Field2,
                            msg.Field3, ct), token))
                    return;
                await UseItemFinalAsync(session, s, charId, item, def,
                    msg.Field1, msg.Field2, msg.Field3, token);
            });

    }

    /// <summary>
    /// Cooldown d'usage — `CheckIsCoolDowning` + `AddCoolDownItem`
    /// natifs. <c>DoUseItem</c> @0x80b8a10 : le pipeline n'existe que
    /// pour les use-types portés par un `UseItemControl`
    /// (1/2/3/5/6 — <c>UseItemParam</c> @0x80b9490 retourne 0 pour les
    /// autres avant tout check) ; la durée (S_Item col20 →
    /// CItemData+0x58) doit être &gt; 0. Refus = texte 0x5ef (vfunc
    /// +0x58 du char), sans ack — le client gère le grisage depuis sa
    /// propre table. À l'armement on émet le 368
    /// `ClientUpdateCoolDown` (snapshot des groupes actifs).
    /// </summary>
    /// <returns>false si l'usage doit être refusé (groupe en CD).</returns>
    private static async Task<bool> TryArmItemCooldownAsync(
        NetSession session, ZoneServices s, int charId, ItemEntity item,
        ItemParser.ItemDef def, CancellationToken token)
    {
        if (def.CooldownDs <= 0 ||
            def.UseType is not (1 or 2 or 3 or 5 or 6))
            return true;
        var group = (short)def.CooldownGroup;
        if (s.ItemCooldowns.IsCooling(charId, group))
        {
            // Refus natif : string-id 0x5ef, et c'est tout
            // (CheckUseItemCondition retourne 0 → UseItemParam 0).
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                { Field1 = 0x5ef, Field2 = "" }, token);
            EdenLog.Write($"[Zone] ItemUse CD char={charId} " +
                $"item={item.ItemId} groupe={group} actif — refus");
            return false;
        }
        s.ItemCooldowns.Arm(charId, group, item.ItemId,
            def.CooldownDs);
        await s.ItemCooldowns.SendUpdateAsync(session, charId, token);
        return true;
    }

    /// <summary>
    /// Queue commune d'usage d'un consommable — ordre natif
    /// DoUseItemAfterCheck (zone/decompiled.c @0x80b8340) : enchant
    /// d'usage → ModifyItemDurability -1 (291) → ack ClientItemUse
    /// (303) → ClientDropItem 275 sur stack épuisé. Partagée par les
    /// handlers ItemUse (19) et ItemMallUseItem (83).
    /// </summary>
    private static async Task UseItemFinalAsync(NetSession session,
        ZoneServices s, int charId, ItemEntity item,
        ItemParser.ItemDef def, short contType,
        short contIdx, short slot, CancellationToken token)
    {
        // Sacs natifs : UseItemToLifeControl::UseItem @0x84ec180
        // route les items type 0xc par plage d'id — [50001,51001) =
        // ChanceLuckyBag, [49001,50001)∪[51001,53001) =
        // OpenFortuneBag (le sac a une table fortune_bag chargée).
        // L'ouverture remplace l'enchant d'usage (les sacs n'en ont
        // pas) mais l'item se consomme comme tout consommable.
        var isLuckyBag = item.ItemId is >= 50001 and < 51001 &&
            s.FortuneBags.Find(item.ItemId) != null;
        var isFortuneBag =
            item.ItemId is (>= 49001 and < 50001)
                or (>= 51001 and < 53001)
            && s.FortuneBags.Find(item.ItemId) != null;

        // Boîte/paquet cadeau (kind 48 sans enchant d'usage) : contenu
        // = S_DropItem[itemId] (format B « items ouvrables », chances
        // sommant à 100 → UN lot exclusif). Refus (sac plein) = item
        // NON consommé + texte 0x4f9 ; jamais de destruction silencieuse.
        if (def.Kind == 48 && def.UseEnchantId == 0 &&
            s.Loot.GetDropTable(item.ItemId) is { } boxTable &&
            boxTable.Entries.Any(e => e.ItemId > 0))
        {
            await OpenItemBoxAsync(session, s, charId, item, def, boxTable,
                contType, contIdx, slot, token);
            return;
        }

        // Item sans enchant d'usage, non équipable et non livre de
        // sorts — ex. Blessed Crystal 49004 : le natif acquitte sans
        // consommer (UseItemParam retourne 0 sur les types non gérés).
        if (def.UseEnchantId == 0 && def.LearnSpellId <= 0 &&
            !isLuckyBag && !isFortuneBag)
        {
            await SendItemUseAckAsync(session, charId, item.ItemId,
                contType, contIdx, slot, token);
            EdenLog.Write($"[Zone] ItemUse char={charId} " +
                $"item={item.ItemId} ({def.Name}) sans enchant " +
                $"d'usage — ack UI sans consommation");
            return;
        }

        //   ⓪ CheckExecute natif (DoUseItem : contrôle AVANT toute
        // consommation) — refus = item conservé + ack UI. Ex. item de
        // quête SummonInEventArea utilisé hors du node requis (0x5c0).
        if (s.ItemUseCanExecute != null && def.UseEnchantId > 0 &&
            !await s.ItemUseCanExecute(session, charId,
                def.UseEnchantId, token))
        {
            await SendItemUseAckAsync(session, charId, item.ItemId,
                contType, contIdx, slot, token);
            EdenLog.Write($"[Zone] ItemUse char={charId} " +
                $"item={item.ItemId} ({def.Name}) refusé par " +
                "CanExecute — item conservé");
            return;
        }
        //   ① commandes d'enchant d'usage (virtual +0x10)
        if (s.ItemUseApply != null && def.UseEnchantId > 0)
            await s.ItemUseApply(session, charId, def.UseEnchantId,
                def.UseBuffSeconds, token);
        //   ①ter kind 48 AVEC enchant : le dispatch type 0xc natif
        // (UseItemToLifeControl::UseItem @0x84ec180) roule AUSSI
        // S_DropItem[itemId] après InvokeItemEffect — ex. la clé de
        // quête 32811 (enchant 53009 invoque le coffre visuel, le roll
        // donne l'« instrument usé » 32812 = l'ItemCollect de 1104).
        // OpenItemBoxAsync gère don + consommation ; sac plein →
        // boîte conservée comme le chemin sans enchant.
        if (def.Kind == 48 && def.UseEnchantId > 0 &&
            s.Loot.GetDropTable(item.ItemId) is { } enchantedBox &&
            enchantedBox.Entries.Any(e => e.ItemId > 0))
        {
            await OpenItemBoxAsync(session, s, charId, item, def,
                enchantedBox, contType, contIdx, slot, token);
            return;
        }
        //   ①bis dispatch type 0xc natif — ChanceLuckyBagStart pose
        // l'état (char+0xc70..), émet 432 EnterStrengthen et roule le
        // premier dé ; OpenFortuneBag tire un lot par set (sur la
        // boucle, via le callback ZoneEntryFlow). Le natif valide
        // AVANT de consommer (CheckExecute @0x084ec520 : slots libres,
        // sac déjà actif, verrou reload) — refus = item NON consommé.
        if (isLuckyBag && s.LuckyBagUse != null &&
            !await s.LuckyBagUse(session, charId, item.ItemId, token))
            return;
        if (isFortuneBag && s.FortuneBagUse != null &&
            !await s.FortuneBagUse(session, charId, item.ItemId,
                item.IsBound, token))
            return;
        await ConsumeUsedItemAsync(session, s, charId, item, def,
            contType, contIdx, slot, token);
    }

    /// <summary>Ouverture d'une boîte (kind 48) — tirage pondéré dans
    /// S_DropItem[boîte] parmi les lignes qui donnent un vrai item
    /// (les lignes monnaie id 0 ne sont pas portées). Le lot est posé
    /// en sac AVANT la consommation : sac plein → refus + 0x4f9, la
    /// boîte reste intacte.</summary>
    private static async Task OpenItemBoxAsync(NetSession session,
        ZoneServices s, int charId, ItemEntity box,
        ItemParser.ItemDef def,
        DropAndSpeedParsers.DropTable table, short contType,
        short contIdx, short slot, CancellationToken token)
    {
        var pool = table.Entries.Where(e => e.ItemId > 0 &&
            e.ChancePercent > 0).ToList();
        var roll = Random.Shared.NextDouble() *
            pool.Sum(e => (double)e.ChancePercent);
        var win = pool[^1];
        foreach (var e in pool)
        {
            roll -= e.ChancePercent;
            if (roll < 0) { win = e; break; }
        }
        var qty = Math.Max(1, win.Quantity);
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, _, _, _) = await s.Inventory.AddItemAsync(charId,
            win.ItemId, qty, 0, 0, false, touched);
        if (!ok)
        {
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                { Field1 = 0x4f9, Field2 = string.Empty }, token);
            await SendItemUseAckAsync(session, charId, box.ItemId,
                contType, contIdx, slot, token);
            EdenLog.Write($"[Zone] Box char={charId} item={box.ItemId} " +
                "sac plein — refus (boîte conservée)");
            return;
        }
        await Codecs.NativeItemCodec.EmitItemSlotsAsync(session,
            s.InventoryRepo, charId, touched, token);
        s.NotifyItemChanged(charId, win.ItemId);
        EdenLog.Write($"[Zone] Box char={charId} item={box.ItemId} " +
            $"({def.Name}) -> {win.ItemId} x{qty}");
        await ConsumeUsedItemAsync(session, s, charId, box, def,
            contType, contIdx, slot, token);
    }

    /// <summary>Usage d'un item « à cible » (parchemin posé sur un
    /// autre item) — `UseItemToItemControl::UseItem` natif
    /// @0x84eb780. La cible est résolue par tgtSel (0 = sacs, 1 =
    /// équipement — même convention que le sélecteur conteneur du
    /// NCID 19), tgtKey = index de conteneur, tgtSlot = loc. Seule la
    /// commande 1914 (`CEC_ItemUnBind`) est implémentée ; les autres
    /// kinds (0x2d inlay, 0x38/0x4f witchcraft, 0x3b teinture, 0x54
    /// transgold) restent côté handlers dédiés.</summary>
    private static async Task UseItemOnItemAsync(NetSession session,
        ZoneServices s, int charId, ItemEntity scroll,
        ItemParser.ItemDef def, short tgtSel, short tgtKey,
        short tgtSlot, CancellationToken token)
    {
        // Cible native : tgtSel = sélecteur de conteneur — 0 = sacs,
        // 1 = équipement porté, 4 = coffre (même convention que
        // `GetCloneItemFromContainer` / l'inlay kind 45).
        var target = tgtSel switch
        {
            1 => await s.InventoryRepo.GetEquipmentItemAsync(
                charId, tgtKey, tgtSlot),
            4 => await s.InventoryRepo.GetStorageItemAsync(charId,
                tgtKey, tgtSlot),
            _ => await s.InventoryRepo.GetItemAsync(charId, tgtKey,
                tgtSlot),
        };
        var cmds = def.UseEnchantId > 0
            ? s.Enchants.GetUseCommands(def.UseEnchantId) : null;
        var unbind = cmds?.FirstOrDefault(c => c.CommandId == 1914);
        if (target == null || unbind == null)
        {
            // Type de cible non géré ou cible absente — ack UI sans
            // consommer (UseItemParam retourne 0 si le contrôle n'a
            // pas pu s'exécuter).
            await SendItemUseAckAsync(session, charId, scroll.ItemId,
                0, tgtKey, tgtSlot, token);
            return;
        }
        await UseUnbindScrollAsync(session, s, charId, scroll, def,
            unbind, tgtSel, target, tgtKey, tgtSlot, token);
    }

    /// <summary>`CEC_ItemUnBind::Execute` @0x0853f820 — parchemin
    /// « délier un équipement » :
    /// var0 = seuil de réussite /10000 (rand &lt; var0 → succès),
    /// var1 = % de purge des attrs Mana Infusion (train),
    /// var2 = % de réduction du niveau de renforcement,
    /// var3 = mode de réduction (0 = pondéré natif, 1 = moitié, 2 =
    /// remise à 0), var4 = niveau max de l'item cible,
    /// var5 = filtre UseType de la cible.
    /// Succès → flag bind retiré + ModifyItemBind + 436
    /// ModifyItemStrengthen + 369 ModifyItemInfo ; échec → texte
    /// 0x23c7. Le parchemin est consommé dans tous les cas.</summary>
    private static async Task UseUnbindScrollAsync(NetSession session,
        ZoneServices s, int charId, ItemEntity scroll,
        ItemParser.ItemDef def, EnchantService.UseCommand cmd,
        short tgtSel, ItemEntity target, short tgtKey, short tgtSlot,
        CancellationToken token)
    {
        var successRate = cmd.Args[0];
        var growUpRate = cmd.Args[1];
        var strengthenRate = cmd.Args[2];
        var failType = cmd.Args[3];
        var maxLevel = cmd.Args[4];
        var targetDef = s.GameData.Items.FirstOrDefault(
            d => d.Id == target.ItemId);
        // Cible éligible : liée et niveau ≤ var4 (Init natif valide
        // ItemLevel 1-100 ; la vérification cible vit dans
        // CheckExecute). Non liée → rien à délier, échec direct.
        var eligible = target.IsBound && targetDef != null &&
            (maxLevel <= 0 || targetDef.RequiredLevel <= maxLevel);
        var ok = eligible &&
            Random.Shared.Next(10000) < successRate;
        if (!ok)
        {
            // Échec natif : texte 0x23c7 « %s » (nom de l'item) puis
            // ack ClientItemUse — le parchemin est quand même
            // consommé par la queue commune.
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = 0x23c7,
                Field2 = targetDef?.Name ?? string.Empty,
            }, token);
            await ConsumeUsedItemAsync(session, s, charId, scroll, def,
                0, (short)scroll.BagId, (short)scroll.Slot, token);
            return;
        }
        // Succès : clone+0x24 &= 0xef + ModifyItemBind persistant.
        await s.InventoryRepo.ClearItemBindAsync(target.Id);
        target.IsBound = false;
        // 369 mode 8 bind=0 — notification immédiate du déliement
        // (même paquet dédié que le bind : +0x6a = flags>>4 & 1).
        await session.SendPacketAsync(369,
            Codecs.MagicBoxPackets.BuildModifyItemInfoBind(
                tgtSel, tgtKey, tgtSlot, 0), token);
        // var1% : purge des attrs d'entraînement (CEquipTrainAttrs::
        // Clean — TrainLevel/Exp/slots remis à zéro).
        if (growUpRate > 0 &&
            Random.Shared.Next(100) < growUpRate &&
            (target.TrainLevel != 0 || target.TrainExp != 0))
        {
            target.TrainLevel = 0;
            target.TrainExp = 0;
            target.TrainSlot1Type = 0;
            await s.InventoryRepo.UpdateItemAsync(target);
        }
        // var2% : réduction du renforcement selon le mode var3
        // (0 = retrait pondéré 0-3 via CFormula — approché par
        // Random 0-3, les scrolls en données ont var2=0).
        var newLevel = target.EnchantLevel;
        if (strengthenRate > 0 &&
            Random.Shared.Next(100) < strengthenRate &&
            newLevel > 0)
        {
            newLevel = failType switch
            {
                0 => Math.Max(0, newLevel - Random.Shared.Next(4)),
                1 => newLevel / 2,
                _ => 0,
            };
            target.EnchantLevel = newLevel;
            await s.InventoryRepo.UpdateItemAsync(target);
        }
        // 436 ClientModifyItemStrengthen — [contType][slot][niveau]
        // [contIdx][protégé] sur la CIBLE (ctor natif : container
        // GetType, slot param_2 = index cible, niveau clone+0x10).
        await session.SendAsync(new CNC_CZ_ClientModifyItemStrengthen
        {
            Field1 = tgtSel, Field2 = tgtSlot,
            Field3 = (short)target.EnchantLevel, Field4 = tgtKey,
            Field5 = 0,
        }, token);
        // 369 ClientModifyItemInfo — record complet de la cible
        // (ctor natif mode -1 émis par Execute après le unbind).
        var row = await s.InventoryRepo.GetItemFullRowAsync(target.Id);
        if (row != null)
            await session.SendPacketAsync(369,
                Codecs.MagicBoxPackets.BuildModifyItemInfo(row,
                    tgtSel, tgtKey, tgtSlot,
                    (byte)(targetDef != null &&
                        Eden.Core.Game.Inlay.InlayService
                            .IsSocketCapable(targetDef) ? 1 : 0)),
                token);
        await ConsumeUsedItemAsync(session, s, charId, scroll, def,
            0, (short)scroll.BagId, (short)scroll.Slot, token);
        EdenLog.Write($"[Zone] UnBind char={charId} " +
            $"scroll={scroll.ItemId} target={target.ItemId} " +
            $"-> succès (bind retiré, niv={target.EnchantLevel})");
    }

    /// <summary>Queue de consommation commune — ordre natif de
    /// <c>DoUseItemAfterCheck</c> @0x80b84c0 :
    /// ② ModifyItemDurability -1 → 291, ③ ack 303 ClientItemUse,
    /// ④ stack épuisé → 275 ClientDropItem + notification mission
    /// (objectif ItemCollect cassé — 146 zone→mission).</summary>
    internal static async Task ConsumeUsedItemAsync(NetSession session,
        ZoneServices s, int charId, ItemEntity item,
        ItemParser.ItemDef def, short contType,
        short contIdx, short slot, CancellationToken token)
    {
        // Conteneur équipement (NCID 83 sel=1) : l'item porté n'est
        // jamais stackable → DELETE equipment*, pas touche au sac à
        // la même (bag,slot).
        // Outil à usure (pioche/gants/boule, kind 44 non empilable) :
        // l'usage retire 1 de durabilité, la ligne reste (natif
        // ModifyItemDurability -1) ; RemoveItemAsync supprimerait
        // la ligne entière d'un item non empilable.
        var wearTool = contType == 0 && def.Kind == 44 &&
            !s.Inventory.IsStackable(item.ItemId) && item.Durability > 1;
        bool removed;
        int left;
        if (wearTool)
        {
            item.Durability -= 1;
            removed = await s.InventoryRepo.UpdateItemAsync(item);
            left = item.Durability;
        }
        else
        {
            removed = contType == 1
                ? await s.InventoryRepo.RemoveEquippedItemAsync(
                    charId, contIdx, slot)
                : await s.Inventory.RemoveItemAsync(
                    charId, contIdx, slot, 1);
            left = item.Quantity - 1;
        }
        // Bind à l'usage — natif DoUseItem/DoUseItemAfterCheck →
        // BindItem @0x08116870 : Flags & 0x80 && clone non lié →
        // |=0x10 + SetItemBind. Le stack résiduel (potions…) devient
        // lié ; un item épuisé disparaît, rien à lier.
        if (removed && left > 0 && !item.IsBound && def.Bindable)
        {
            await s.InventoryRepo.SetItemBindAsync(item.Id);
            item.IsBound = true;
            // 369 mode 8 — même paquet bind dédié que le chemin
            // monture (FastDressDoItemBind natif) : le stack résiduel
            // passe « already binded » immédiatement.
            await session.SendPacketAsync(369,
                Codecs.MagicBoxPackets.BuildModifyItemInfoBind(
                    contType, contIdx, slot, 1), token);
        }
        if (removed && left > 0)
            // 291 ModifyItemDurability — ordre natif vérifié sur le
            // builder de DoUseItemAfterCheck @0x80b84c0 :
            // [s16 contType][s16 loc][s16 item+0xc = nouveau count]
            // [s16 item+0xe = maxDur][s16 contIdx][s16 0]. Field4/5
            // inversés envoyés avant → le client appliquait le count
            // au mauvais conteneur (compteur potion jamais décrémenté
            // visuellement — bug live).
            await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
            {
                Field1 = contType,
                Field2 = slot,
                Field3 = (short)left,
                Field4 = (short)item.MaxDurability,
                Field5 = contIdx,
                Field6 = 0,
            }, token);
        //   ③ ack ClientItemUse (303) [i32 charId][i32 0][u16 itemId]
        // [s16 contType][s16 contIdx][s16 slot][s16 3].
        await SendItemUseAckAsync(session, charId, item.ItemId,
            contType, contIdx, slot, token);
        //   ④ stack épuisé → 275 ClientDropItem + eradicate.
        if (removed && left <= 0)
            await session.SendAsync(new CNC_CZ_ClientDropItem
            {
                Field1 = contType, Field2 = slot, Field3 = contIdx,
            }, token);
        if (removed && s.Mission is { Connected: true } mlink)
            // La consommation peut casser un objectif ItemCollect
            // déjà rempli (146 zone→mission).
            _ = mlink.SendQuestItemChangedAsync((uint)charId, item.ItemId);
        EdenLog.Write($"[Zone] ItemUse char={charId} " +
            $"item={item.ItemId} ({def.Name}) enchant=" +
            $"{def.UseEnchantId} -> " +
            $"{(removed ? left > 0 ? $"reste {left}" : "épuisé" : "échec")}");
    }

    /// <summary>
    /// Item-utilisé-sur-item — `UseItemToItemControl` natif (désasm
    /// CheckExecute @0x84ecd60 / UseItem @0x84eb780). Cases portées :
    /// gemme kind 45 (Spellstone → inlay, case 0x2d) et scrolls
    /// d'enchant kinds 56/79 (case 0x38/0x4f → witchcraft). Les kinds
    /// 59 (teinture) et 84 (transmutation) ont leurs propres cases —
    /// non portés, ack neutre sans consommation.
    /// </summary>
    /// <param name="msg">Payload 19 : Field1-3 = source,
    /// Field6/7/8 = cible (contType, contIdx, loc).</param>
    private static async Task HandleItemOnItemAsync(
        NetSession session, ZoneServices s, int charId,
        CNE_CZ_ZoneServerItemUse msg, ItemEntity item,
        ItemParser.ItemDef def, CancellationToken token)
    {
        // Scrolls d'enchantement — case natif 0x38/0x4f : l'id du
        // scroll s'écrit dans `witchcraft` (clone+0x4c) de la cible.
        if (def.Kind is 56 or 79)
        {
            await ApplyWitchcraftAsync(session, s, charId, msg,
                item, def, token);
            return;
        }
        if (def.Kind != 45)
        {
            // Kinds sans case dédié → branche `default` de
            // `UseItemToItemControl::UseItem` @0x84eb7xx :
            // `CItemFactory::InvokeItemEffect` exécute les commandes
            // de l'enchant d'usage sur la CIBLE. Seule la commande
            // 1914 (`CEC_ItemUnBind` — parchemins mall 45150+,
            // kind 44) est portée ; les autres kinds gardent l'ack
            // neutre (fidèle au natif sans commande).
            var cmds = def.UseEnchantId > 0
                ? s.Enchants.GetUseCommands(def.UseEnchantId) : null;
            if (cmds != null && cmds.Any(c => c.CommandId == 1914))
            {
                await UseItemOnItemAsync(session, s, charId, item,
                    def, msg.Field6, msg.Field7, msg.Field8, token);
                return;
            }
            // Item-on-item d'un kind non porté : ack sans
            // consommation (fidèle au default natif sans case).
            await SendItemUseAckAsync(session, charId, item.ItemId,
                msg.Field1, msg.Field2, msg.Field3, token);
            EdenLog.Write($"[Zone] ItemOnItem char={charId} " +
                $"kind={def.Kind} non porté — ack neutre");
            return;
        }

        // Cible native : (contType, contIdx, loc) dans les 3 s16 de
        // queue (CheckExecute @0x80b9ec8). 0=sac, 1=équipé, 4=coffre
        // — fidèle à CInventory::GetItemContainer.
        ItemEntity? target = msg.Field6 switch
        {
            0 => await s.InventoryRepo.GetItemAsync(charId,
                msg.Field7, msg.Field8),
            1 => await s.InventoryRepo.GetEquipmentItemAsync(charId,
                msg.Field7, msg.Field8),
            4 => await s.InventoryRepo.GetStorageItemAsync(charId,
                msg.Field7, msg.Field8),
            _ => null,
        };
        var targetDef = target == null ? null
            : s.GameData.Items.FirstOrDefault(i => i.Id == target.ItemId);
        if (target == null || targetDef == null)
        {
            // Natif : AssertLog + return false — on ack pour ne pas
            // bloquer le client modal.
            await SendItemUseAckAsync(session, charId, item.ItemId,
                msg.Field1, msg.Field2, msg.Field3, token);
            EdenLog.Write($"[Zone] Inlay char={charId} cible " +
                $"absente cont={msg.Field6} idx={msg.Field7} " +
                $"loc={msg.Field8}");
            return;
        }

        var level = (short)((await s.Characters
            .GetCharacterByIdAsync(charId))?.Level ?? 0);
        var v = s.Inlay.ValidateInlay(def, target, targetDef, level);
        if (!v.Ok)
        {
            // Texte d'erreur natif (TextOutPut) — ex. 194 « This
            // item cannot be inlaid with Spellstones ».
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = (short)v.ErrorTextId,
                Field2 = string.Empty,
            }, token);
            await SendItemUseAckAsync(session, charId, item.ItemId,
                msg.Field1, msg.Field2, msg.Field3, token);
            EdenLog.Write($"[Zone] Inlay char={charId} refusé " +
                $"text={v.ErrorTextId}");
            return;
        }

        // Application + persistance — UseItem case 0x2d natif :
        // embedded_id[idx]=gemId, UpdateCloneItemAddAttr,
        // ModifyItemEmbedded, texte 0xc1.
        InlayService.ApplyInlay(target, v.SocketIndex, item.ItemId);
        await s.InventoryRepo.UpdateEmbeddedAsync(target.Id,
            target.EmbeddedAmount & 7,
            new[] { target.EmbeddedId1, target.EmbeddedId2,
                    target.EmbeddedId3, target.EmbeddedId4,
                    target.EmbeddedId5, target.EmbeddedId6 });
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = (short)InlayService.TextSuccess,
            Field2 = string.Empty,
        }, token);

        // Cible équipée → recalcul du profil (RefreshEQAttr natif).
        if (target.IsEquipped)
        {
            await ProfileStatEmitter.SendForCharacterAsync(
                session, s, charId, token);
            // Le snapshot combat se reconstruit (stage « gems »).
            s.InvalidateWeaponAnim?.Invoke(session.SessionId);
        }

        // Consommation (DoUseItemAfterCheck natif) : −1 de stack —
        // 291 ClientModifyItemDurability si reste, 275
        // ClientDropItem si épuisé.
        var left = await ConsumeItemOnItemSourceAsync(session, s,
            charId, item, msg.Field2, msg.Field3, token);
        EdenLog.Write($"[Zone] Inlay char={charId} " +
            $"gemme={item.ItemId} → item={target.ItemId} " +
            $"socket={v.SocketIndex} left={Math.Max(left, 0)}");
    }

    /// <summary>Consomme 1 exemplaire de l'item source d'un
    /// item-on-item (gemme/scroll) — ordre natif
    /// `DoUseItemAfterCheck` : 291 si stack restant, ack 303, 275
    /// si épuisé. Retourne le stack restant (-1 si échec).</summary>
    private static async Task<int> ConsumeItemOnItemSourceAsync(
        NetSession session, ZoneServices s, int charId,
        ItemEntity item, short bag, short slot, CancellationToken token)
    {
        var left = item.Quantity - 1;
        var removed = await s.Inventory.RemoveItemAsync(
            charId, bag, slot, 1);
        if (removed && left > 0)
            await session.SendAsync(new CNC_CZ_ClientModifyItemDurability
            {
                Field1 = 0,
                Field2 = slot,
                Field3 = (short)left,
                Field4 = (short)item.MaxDurability,
                Field5 = bag,
                Field6 = 0,
            }, token);
        await SendItemUseAckAsync(session, charId, item.ItemId,
            0, bag, slot, token);
        if (removed && left <= 0)
            await session.SendAsync(new CNC_CZ_ClientDropItem
            {
                Field1 = 0, Field2 = slot, Field3 = bag,
            }, token);
        return removed ? left : -1;
    }

    /// <summary>Application d'un scroll d'enchantement — case natif
    /// 0x38 (kind 56) / 0x4f (kind 79, enchants costume) de
    /// `UseItemToItemControl::UseItem` @0x84eb780 : `witchcraft`
    /// (clone+0x4c) de la cible ← itemId du scroll, persistance
    /// `CItemDBO::ModifyItemWitchCraft` (`UPDATE … SET witchcraft`),
    /// `RefreshEQAttr` + `NotifyRefreshAttr` quand la cible est
    /// portée, texte 0x9f7, puis `CNC_CZ_ClientModifyItemInfo` (369)
    /// record complet de la cible (queue commune du UseItem natif)
    /// et consommation du scroll.
    /// Validation = `CheckExecute` @0x84ecd60 cases 0x38/0x4f :
    /// cible équipement (kind&lt;0x28, ≠0x51), compatibilité
    /// EquipLoc↔kind, pas le même enchant déjà posé, niveau.</summary>
    private static async Task ApplyWitchcraftAsync(
        NetSession session, ZoneServices s, int charId,
        CNE_CZ_ZoneServerItemUse msg, ItemEntity item,
        ItemParser.ItemDef def, CancellationToken token)
    {
        // Cible (contType, contIdx, loc) dans les 3 s16 de queue —
        // même résolution que l'inlay (0=sac, 1=équipé, 4=coffre).
        ItemEntity? target = msg.Field6 switch
        {
            0 => await s.InventoryRepo.GetItemAsync(charId,
                msg.Field7, msg.Field8),
            1 => await s.InventoryRepo.GetEquipmentItemAsync(charId,
                msg.Field7, msg.Field8),
            4 => await s.InventoryRepo.GetStorageItemAsync(charId,
                msg.Field7, msg.Field8),
            _ => null,
        };
        var targetDef = target == null ? null : s.GameData.Items
            .FirstOrDefault(i => i.Id == target.ItemId);

        var charLevel = (await s.Characters
            .GetCharacterByIdAsync(charId))?.Level ?? 0;
        var err = Eden.Core.Game.Inlay.InlayService.ValidateWitchcraft(
            def, target, targetDef, charLevel);
        if (err != 0)
        {
            // Textes natifs : 0x9f6 « cannot be Enchanted », 0x9f8
            // « level too low » (CheckExecute 0x38 → TextOutPut).
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            {
                Field1 = (short)err, Field2 = string.Empty,
            }, token);
            await SendItemUseAckAsync(session, charId, item.ItemId,
                msg.Field1, msg.Field2, msg.Field3, token);
            EdenLog.Write($"[Zone] Enchant char={charId} refusé " +
                $"scroll={item.ItemId} text=0x{err:x}");
            return;
        }

        // Application — target+0x4c = itemId du scroll, puis
        // ModifyItemWitchCraft natif (UPDATE witchcraft sur la table
        // du conteneur ; le serial est global → balayage).
        target!.Witchcraft = item.ItemId;
        await s.InventoryRepo.UpdateWitchcraftAsync(target.Id,
            item.ItemId);

        // Texte 0x9f7 natif « Enchantment added » — émis avant le
        // ModifyItemInfo.
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = (short)Eden.Core.Game.Inlay.InlayService
                .TextEnchantAdded,
            Field2 = string.Empty,
        }, token);

        // Queue commune du UseItem natif : 369 ClientModifyItemInfo
        // record complet de la cible (le client relit embedded +
        // witchcraft → affiche la ligne enchant). GetType() du
        // conteneur = Field6 (0 sac, 1 équipé, 4 coffre).
        var row = await s.InventoryRepo.GetItemFullRowAsync(target.Id);
        if (row != null)
            await session.SendPacketAsync(369,
                Codecs.MagicBoxPackets.BuildModifyItemInfo(row,
                    msg.Field6, msg.Field7, msg.Field8,
                    (byte)(Eden.Core.Game.Inlay.InlayService
                        .IsSocketCapable(targetDef!) ? 1 : 0)),
                token);

        // RefreshEQAttr natif — la cible portée recalcule le profil.
        if (msg.Field6 == 1)
        {
            await ProfileStatEmitter.SendForCharacterAsync(
                session, s, charId, token);
            s.InvalidateWeaponAnim?.Invoke(session.SessionId);
        }

        var left = await ConsumeItemOnItemSourceAsync(session, s,
            charId, item, msg.Field2, msg.Field3, token);
        EdenLog.Write($"[Zone] Enchant char={charId} " +
            $"scroll={item.ItemId} kind={def.Kind} → " +
            $"item={target.ItemId} cont={msg.Field6},{msg.Field7}," +
            $"{msg.Field8} left={Math.Max(left, 0)}");
    }



    /// <summary>Accusé natif `CNC_CZ_ClientItemUse` (303) — corps
    /// exact d'après le remplissage désasm (zone/decompiled.c,
    /// constructeur dans le flux d'usage) :
    /// `[i32 charId][i32 0][u16 itemId][s16 contType][s16 contIdx]
    /// [s16 slot][s16 3]`. Le dernier s16 vaut 3 dans toutes les
    /// émissions natives observées (code de résultat succès).</summary>
    private static Task SendItemUseAckAsync(NetSession session,
        int charId, int itemId, short contType, short contIdx,
        short slot, CancellationToken token)
        => session.SendAsync(new CNC_CZ_ClientItemUse
        {
            Field1 = charId,              // i32 : handle du perso
            Field2 = 0,                   // i32 : 0 natif
            Field3 = (ushort)itemId,      // u16 : id de l'item
            Field4 = contType,            // s16 : type de conteneur
            Field5 = contIdx,             // s16 : index (n° de sac)
            Field6 = slot,                // s16 : loc dans le conteneur
            Field7 = 3,                   // s16 : résultat (3 natif)
        }, token);

    /// <summary>283 `ClientModelModify` mask=2 (EModelTransition
    /// Ride) — le paquet qui affiche réellement la monture
    /// (désasm `CEC_Ride::SetState` @0x854f2xx →
    /// `CNC_CZ_ClientModelModify` ctor 9 args @0x813f2c0 +
    /// Serialize @0x813c230). Corps mask=2 :
    /// `[i32 handle][u8 version(char+0x4dc)][i32 mask=2]
    /// [u16 2][u32 rideKey][u32 flag(&1)][u16 seats][u32 passager0
    /// =handle][u32×(seats-1) 0]` ; au démontage rideKey/flag=0 et
    /// le vecteur passagers est vide (u16 0). Émis au client acteur
    /// puis broadcast can-see (toutes les sessions du node — la zone
    /// est par-node, même critère que l'EnchantAdd ride).</summary>
    internal static async Task SendRideModelAsync(ZoneServices s,
        NetSession session, int charId, int rideKey, int flag,
        int seats, CancellationToken token, bool selfOnly = false)
    {
        var ver = s.ModelVersions.AddOrUpdate(charId, (byte)1,
            (_, v) => (byte)(v + 1));
        var msg = new CNC_CZ_ClientModelModify
        {
            Field1 = charId, Field2 = ver, Field3 = 2,
        };                                              // mask 2 = ride
        msg.Field4.Add((uint)rideKey);                  // 0 = démonté
        msg.Field4.Add((uint)flag);                     // flag ride (&1)
        var n = rideKey != 0 ? Math.Max(seats, 1) : 0;  // sièges : ≥1 monté, 0 démonté
        for (var i = 0; i < n; i++)
            msg.Field5.Add(i == 0 ? (uint)charId : 0u); // passager 0 = conducteur
        // Broadcast : le corps est sérialisé une fois puis rejoué brut
        // vers chaque session (équivalent Broadcast natif).
        var stream = new OutBinStream();
        msg.Serialize(stream);
        var bytes = stream.ToArray();
        await session.SendPacketAsync(283, bytes, token);
        if (selfOnly) return;
        foreach (var other in s.Sessions.All)
        {
            if (other.SessionId == session.SessionId) continue;
            try { await other.SendPacketAsync(283, bytes, token); }
            catch { /* session morte */ }
        }
    }

    /// <summary>Coût ou exécution de la réparation de tout l'équipement
    /// — formule native GetItemRepairAllEquipPrice (@0x808c570) :
    /// somme des GetItemRepairPrice sur les slots équipés, puis remise
    /// à maxDurabilité par item (RepairAll natif).</summary>
    /// <summary>Coût de réparation de tout l'équipement porté —
    /// `CItemFactory::GetItemRepairAllPrice` natif ; <paramref name="apply"/>
    /// applique la réparation (utilisé par RepairEquipment et
    /// FixEquipByFamily).</summary>
    internal static async Task<long> RepairAllAsync(ZoneServices s,
        int charId, bool apply = false)
    {
        var items = await s.InventoryRepo.GetItemsByCharacterIdAsync(charId);
        long cost = 0;
        foreach (var it in items.Where(i => i.IsEquipped && i.MaxDurability > 0))
        {
            cost += InventoryHandlers.ItemRepairCost(s, it);
            if (apply)
            {
                it.Durability = it.MaxDurability;
                await s.InventoryRepo.UpdateItemAsync(it);
            }
        }
        return cost;
    }
}
