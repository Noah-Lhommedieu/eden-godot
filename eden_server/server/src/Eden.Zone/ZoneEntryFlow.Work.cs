using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Work;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Ordres de travail (player_works — métiers 12-17) : handlers
/// NCID 170/171/172 + chargement/timers. Portage de
/// <c>CNE_CZ_ZoneServerPlayerWork::Execute → CWorkManager::ToWork/
/// ToCollect/ToCombine</c>, <c>EndCollect/EndCombine</c> (timer),
/// <c>WorkResultOK</c> (claim) et <c>WorkCheckData</c> (zone/
/// decompiled.c).
///
/// Fidélité native :
/// - 170 = ToWork : catalyseur à (bag,slot), kind par type, niveau
///   dans [RequiredLevel, MaxLevel], slot 0-9 (+10 en secours),
///   consommation immédiate des recettes chronométrées, différée
///   des instantanées (SetAllItemLoc natif), puis 433 si cast-bar
///   et 497 agrégé.
/// - Timer : le natif arme l'enchant 200+work_num ; chez nous un
///   Task.Delay + finish_at persisté (survie reconnect/restart).
/// - Fin : EndCollect (tirage par produit) / EndCombine (produit
///   unique + sockets + malédiction) ; échec → work_id négatif.
///   498 émis seulement si la session vit encore.
/// - 171 = WorkResultOK : remise des produits (274), EXP joueur,
///   remise à zéro du slot et 500 (work_event == 0 — les commandes
///   d'événement ne sont pas portées).
/// - 172 = WorkCheckData : 498 par ordre terminé non réclamé,
///   497 agrégé des ordres encore en cours.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Cap optionnel des durées de travail — 0 = durées
    /// natives des tables (col min × 600 : mine 30 min, ferme 20,
    /// autel 25). Un plafond de dev s'active via
    /// <c>EDEN_DEV_WORK_CAP</c> (unités de 100 ms, ex. 100 = 10 s) —
    /// voir <see cref="EdenConfig.DevWorkCapUnits"/>.</summary>
    private static int DevWorkCap => Eden.Core.EdenConfig.DevWorkCapUnits;

    // ========================================================
    //  170 — CNE_CZ_ZoneServerPlayerWork (démarrage d'ordre)
    // ========================================================

    /// <summary>Démarre un ordre — wire natif
    /// <c>[u32 workId][s16 catBag][s16 catSlot][s16 boostBag]
    /// [s16 boostSlot]</c>. Le workType vient de l'opération works
    /// ouverte (service 12-17 → 496), pas du paquet.</summary>
    private async Task HandlePlayerWorkAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 12) return;
        var workId = BitConverter.ToInt32(payload, 0);        // u32 — recette
        var catBag = BitConverter.ToInt16(payload, 4);        // s16 — sac catalyseur
        var catSlot = BitConverter.ToInt16(payload, 6);       // s16 — loc catalyseur
        var boostBag = BitConverter.ToInt16(payload, 8);      // s16 — sac booster (−1 si aucun)
        var boostSlot = BitConverter.ToInt16(payload, 10);    // s16 — loc booster

        // Dispatch natif sur l'op ouverte (char+0x670) : IsA 0x27 →
        // CombineItem (station combine service 10), IsA 0x3d-0x42 →
        // ToWork (works 12-17).
        if (st.ShopOpType == 10)
        {
            await HandleCombineItemAsync(session, st, payload, token);
            return;
        }
        var workType = st.WorkOpType;
        if (!WorkTypes.IsValid(workType)) return; // pas d'op works ouverte
        var catalyst = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, catBag, catSlot);
        var err = _services.Works.ValidateStart(workType, workId,
            st.Works, catalyst, st.Level, out var plan, out var workNum);
        if (!err.Ok || plan == null)
        {
            // Diagnostic live : texte 4 « Item does not exist » remonté
            // par le joueur sur la carte de mine Lv65 — tracer la cause
            // exacte (catalyseur absent / def / recette / slot).
            EdenLog.Write($"[Zone] Work refusé char={st.CharId} " +
                $"type={workType} id={workId} textId={err.TextId} " +
                $"cat=({catBag},{catSlot})→{catalyst?.ItemId.ToString() ?? "null"} " +
                $"boost=({boostBag},{boostSlot})");
            if (err.TextId > 0)
                await SendTextIndexAsync(session, err.TextId, token);
            return;
        }

        // Gates famille natifs (CheckToCombine/CheckToCollect) — la
        // station de hall exige un bâtiment lié à l'op works :
        // absent → 0x251, niveau insuffisant → 0x392, durabilité
        // < 1 → 0xa09 ; sinon fm_name/fm_id/building_item_id armés.
        Eden.Core.Game.Family.FamilyBuildingItem? bld = null;
        // Hors hall de famille (Aven, zone industrielle) : param_2 ==
        // null dans CheckToCollect/CheckToCombine → toute recette dont
        // le niveau de bâtiment requis > 2 est refusée (0x9f1) —
        // « les niveaux 3 et plus nécessitent une visite en ville de
        // guilde » (guide le-craft.md). Natif CheckToCombine
        // @0x08163cd0 (`2 < (byte)param_4[8]`), CheckToCollect
        // @0x08162a00 (`2 < param_4+0x14`).
        var inHall = st.VisitedFamilyInstanceId != 0
            && IsInFamilyInstanceNode(st);
        if (!inHall)
        {
            var needed = plan.IsCollect
                ? plan.Collect!.BuildingLevel : plan.Combine!.BuildingLevel;
            if (needed > 2)
            {
                EdenLog.Write($"[VDG-Work] refus 0x9f1 char={st.CharId} " +
                    $"type={workType} id={workId} bldLvReq={needed} " +
                    "(hors ville de guilde)");
                await SendTextIndexAsync(session, 0x9f1, token);
                return;
            }
        }
        else if (st.WorkOpBuildingItemId == 0)
        {
            // Dans un hall : GetBuildingItemByItemNetID(0) → null →
            // 0x251 « No BuildingItem » (natif).
            EdenLog.Write($"[VDG-Work] refus 0x251 char={st.CharId} " +
                $"type={workType} id={workId} : aucun bâtiment lié " +
                $"à l'op works visit_fm={st.VisitedFamilyInstanceId}");
            await SendTextIndexAsync(session, 0x251, token);
            return;
        }
        if (st.WorkOpBuildingItemId != 0
            && st.VisitedFamilyInstanceId != 0)
        {
            bld = await ResolveWorksBuildingAsync(st);
            if (bld == null)
            {
                EdenLog.Write($"[Zone] Work refusé char={st.CharId} " +
                    $"No BuildingItem[{st.WorkOpBuildingItemId}] " +
                    $"visit_fm={st.VisitedFamilyInstanceId}");
                await SendTextIndexAsync(session, 0x251, token);
                return;
            }
            var reqLevel = plan.IsCollect
                ? plan.Collect!.BuildingLevel : plan.Combine!.BuildingLevel;
            if (bld.Level < reqLevel)
            {
                await SendTextIndexAsync(session, 0x392, token);
                return;
            }
            if (bld.Durability < 1)
            {
                await SendTextIndexAsync(session, 0xa09, token);
                return;
            }
            // Coût taxé (op+0x18 natif) : tax = round(rate×gold/100).
            if (st.WorkOpTaxRate != 0)
                plan.GoldCost = Eden.Core.Game.Family
                    .FamilyBuildingWorkRules.TaxedGold(plan.GoldCost,
                        st.WorkOpTaxRate);
        }

        // Booster optionnel — kinds natifs CChanceItem 0x1f43..0x1f48
        // (rejet natif → return 0 de ToCollect/ToCombine si l'item du
        // slot n'est pas un CChanceItem, désasm @0x8168d50). Ces kinds
        // n'existent pas dans les données actuelles → déviation
        // documentée : les poudres de production réelles
        // (Corona's Essence 37006-37018, Essence Powder 22433-22445)
        // sont acceptées et donnent +30 de ext_success_rate.
        var booster = boostBag >= 0 && boostSlot >= 0
            ? await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, boostBag, boostSlot)
            : null;
        var boostRate = booster != null
            ? WorkBoosterSuccessRate(booster.ItemId) : -1;
        // Un slot booster vide ou pointant sur un item quelconque ne
        // doit PAS faire échouer l'ordre avec « Item does not exist » —
        // le client envoie parfois des coords résiduelles. Le natif
        // échouait silencieusement ; on ignore simplement le booster
        // (l'ordre démarre sans bonus) — déviation documentée.
        if (boostBag >= 0 && boostSlot >= 0 && boostRate < 0)
            EdenLog.Write($"[Zone] Work booster ignoré " +
                $"char={st.CharId} ({boostBag},{boostSlot})→" +
                $"{booster?.ItemId.ToString() ?? "vide"}");
        if (boostRate > 0)
            plan.ExtSuccessRate = boostRate;
        else
            booster = null;

        if (st.Gold < plan.GoldCost)
        {
            await SendTextIndexAsync(session,
                WorkOrderService.Texts.NotEnoughGold, token);
            return;
        }

        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        if (!plan.Deferred)
        {
            // Matériaux hors catalyseur — CheckToCombine agrège par id.
            var counts = items.Where(i => !i.IsEquipped)
                .GroupBy(i => i.ItemId)
                .ToDictionary(g => g.Key, g => (long)g.Sum(i => i.Quantity));
            if (!_services.Works.HasMaterials(plan, counts))
            {
                await SendTextIndexAsync(session,
                    WorkOrderService.Texts.NotEnoughItems, token);
                return;
            }
        }
        else
        {
            // Instantané : le natif exige ≥1 slot libre (0x4f9) — les
            // produits sont remis immédiatement après la mini-durée.
            if (await CountFreeBagSlotsAsync((int)st.CharId, items) < 1)
            {
                await SendTextIndexAsync(session,
                    WorkOrderService.Texts.NoInventorySpace, token);
                return;
            }
        }

        var locs = new WorkDeferredLocs
        {
            CatalystBag = catBag, CatalystSlot = catSlot,
            CatalystItemId = catalyst!.ItemId,
            BoosterBag = booster?.BagId ?? -1,
            BoosterSlot = booster?.Slot ?? -1,
            BoosterItemId = booster?.ItemId ?? 0,
            TaxRate = st.WorkOpTaxRate,
        };
        // Consommation immédiate (recette chronométrée) — l'instantané
        // consomme à la matérialisation (SetAllItemLoc natif).
        if (!plan.Deferred && !await ConsumeWorkInputsAsync(session, st,
                plan, locs, deferred: false, token))
        {
            await SendTextIndexAsync(session,
                WorkOrderService.Texts.NotEnoughItems, token);
            return;
        }

        // Armement du slot CWork (SaveWork natif) — work_event = id de
        // commande d'événement (0 pour les ordres NPC) ; l'enchant
        // 200+num émis dans le 497 n'est pas persisté.
        var work = st.Works[workNum];
        work.WorkType = workType;
        work.WorkId = workId;
        work.WorkEvent = 0;
        work.FailureRateOfDecline = 0;
        work.BonusItemAmount = 0;
        work.FmName = bld != null
            ? (_services.Guilds.GetGuild(st.VisitedFamilyInstanceId)
                ?.Name ?? string.Empty)
            : plan.IsCollect
                ? plan.Collect!.Name : plan.Combine!.ProductName;
        if (bld != null)
        {
            // fm_id (+0x28) = famille propriétaire de la ville,
            // building_item_id (+0x2c) = uid DB de la station.
            work.FmId = st.VisitedFamilyInstanceId;
            work.BuildingItemId = bld.Id;
        }
        work.ExtSuccessRate = plan.ExtSuccessRate;
        work.ExtBuildingRate = plan.ExtBuildingRate;
        work.Running = true;
        // Moteur de modificateurs (VIP, config) puis perk Limitless « craft_time » (ex. -0.2 = -20 %).
        var durUnits = Features.Craft.CraftTimes.WorkUnits(_services, st, plan.DurationUnits,
            !plan.IsCollect && _services.Limitless != null
                ? await _services.Limitless.GetPerkValueAsync((int)st.CharId, "craft_time") : 0);
        // Durées natives des tables ; le cap optionnel
        // EDEN_DEV_WORK_CAP ne borne que si > 0 (dev rapide).
        if (DevWorkCap > 0)
            durUnits = Math.Min(durUnits, DevWorkCap);
        work.FinishAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            + (long)durUnits * WorkOrderService.UnitMs;
        st.WorkDeferredLocs[workNum] = locs;
        await _services.WorksRepo.EnsureAndSaveAsync(
            (int)st.CharId, work);
        st.WorkOpStarted = true;
        // Usure au démarrage (recettes non instantanées) —
        // ToCombine/ToCollect natif : rand%100 < chance → -amount.
        if (bld != null && !plan.Deferred)
        {
            var (wearChance, wearAmount) = plan.IsCollect
                ? (plan.Collect!.BuildingDamageChance,
                   plan.Collect!.BuildingDamage)
                : (plan.Combine!.BuildingDamageChance,
                   plan.Combine!.BuildingDamage);
            if (wearAmount > 0
                && Eden.Core.Game.Family.FamilyBuildingWorkRules
                    .RollWear(Random.Shared, wearChance,
                        inclusive: false))
                await WearBuildingAsync(session, st, work.FmId, bld,
                    -wearAmount, token);
        }

        // 433 ClientStrengthenCasting [s16 workType][s16 durée×100ms]
        // [s16 0] — combine : toujours ; collect : instant/type-14.
        // La durée émise = celle effectivement armée (plafond dev
        // compris) — sinon la cast bar mentirait sur l'échéance.
        if (plan.CastBar)
            await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
            {
                Field1 = (short)workType,
                Field2 = (short)Math.Min(durUnits, short.MaxValue),
                Field3 = 0,
            }, token);
        // Enchant de travail natif — ToCollect/ToCombine arme
        // l'enchant 200+work_num (CWork+4 = Work1ID+num, rodata
        // 0x87b4ae8=0xc8) via EffectToLife (ToCollect @0x816aaf) :
        // le client lit le rest_time de cet enchant pour afficher
        // « N min N sec remaining » dans la fenêtre works — sans ce
        // 299 le compte à rebours reste à 0 (retour joueur).
        await SendWorkEnchantAsync(session, st, work.WorkNum,
            durUnits, token);
        await SendWorkUpdateAsync(session, st, token);
        ArmWorkTimer(session, st, work);
        EdenLog.Write($"[Zone] Work armé char={st.CharId} " +
            $"num={workNum} type={workType} id={workId} " +
            $"dur={plan.DurationUnits}u" +
            (plan.Deferred ? " (instant)" : string.Empty));
    }

    /// <summary>Bonus de taux de réussite d'un booster d'ordre —
    /// -1 = item refusé (natif : ToWork retourne 0 quand le slot
    /// booster ne contient pas un CChanceItem, kinds 0x1f43..0x1f48 ;
    /// désasm ToCombine @0x8169100 / ToCollect @0x816a790).
    /// Les kinds natifs n'existent pas dans les données — déviation :
    /// Corona's Essence Lv45/50/55/60 (37006/37012/37015/37018) et
    /// Essence Powder Lv65/70/75 (22433/22439/22445) sont acceptées
    /// et rapportent +30 (description EN : « increase the success
    /// rate … by 30% »).</summary>
    private static int WorkBoosterSuccessRate(int itemId) =>
        itemId switch
        {
            >= 0x1f43 and <= 0x1f48 => 0, // kinds natifs — absents des données
            37006 or 37012 or 37015 or 37018 => 30, // Corona's Essence
            22433 or 22439 or 22445 => 30,          // Essence Powder
            _ => -1,
        };

    // ========================================================
    //  171 — CNE_CZ_ZoneServerPlayerWorkResultOK (récupération)
    // ========================================================

    /// <summary>Réclame un ordre terminé — wire <c>[s16 workNum]</c>.
    /// WorkResultOK natif : abort si l'enchant de travail court
    /// encore ; matérialise d'abord si la deadline est passée ; puis
    /// AddItemFromWork (274 par produit), EXP joueur (collect col8),
    /// remise à zéro du slot et 500 ClientPlayerWorkResultEnd. Le
    /// chemin PlayerEvent/ToExecCmd (work_event ≠ 0) n'est pas porté
    /// — les ordres NPC n'ont pas de commande d'événement.</summary>
    private async Task HandleWorkResultOKAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 2) return;
        var workNum = BitConverter.ToInt16(payload, 0);   // s16 — slot
        if (workNum < 0 || workNum >= st.Works.Length) return;
        var work = st.Works[workNum];
        if (work.WorkId == 0) return;                     // slot vide
        if (work.Running)
        {
            // Enchant encore armé — le natif refuse. Deadline passée
            // mais non matérialisée (timer tué au crash) : on conclut.
            if (work.FinishAtMs >
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return;
            await MaterializeWorkAsync(session, st, work,
                emitResult: false, token);
        }
        if (!work.Completed) return;

        // Capacité — WorkResultOK compte les slots requis par
        // SWorkItem (stackable → fusion possible) vs slots libres.
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        var needed = _services.Works.SlotsNeededFor(work, items);
        if (needed > 0
            && await CountFreeBagSlotsAsync((int)st.CharId, items)
                < needed)
        {
            await SendTextIndexAsync(session,
                WorkOrderService.Texts.NoInventorySpace, token);
            return;
        }

        // AddItemFromWork natif : stackable → AddItem (274 merge) ;
        // non-stackable → une copie par unité avec les métadonnées
        // du SWorkItem (sockets/combo/curse propagées).
        foreach (var p in work.Products)
        {
            if (p.IsEmpty) continue; Eden.Core.Game.Events.DomainEventBus.Shared.Publish(new Eden.Core.Game.Events.ItemCrafted((int)st.CharId, p.ItemId, Math.Max(p.Amount, 1)));
            var def = _services.GameData.Items
                .FirstOrDefault(i => i.Id == p.ItemId);
            var stackable = (def?.MaxStack ?? 1) > 1
                && (def?.EquipLoc ?? 0) <= 0;
            if (stackable)
            {
                var touched = new List<(int BagId, int Slot, bool Merged)>();
                var add = await _services.Inventory.AddItemAsync(
                    (int)st.CharId, p.ItemId, Math.Max(p.Amount, 1),
                    0, 0, false, touched);
                foreach (var t in touched)
                    await EmitPutItemAsync(session, st, t.BagId,
                        t.Slot, token);
                if (!add.Success)
                    EdenLog.Write($"[Zone] WorkResult : inv plein " +
                        $"char={st.CharId} item={p.ItemId}");
            }
            else
            {
                for (var n = Math.Max(p.Amount, 1); n > 0; n--)
                    await CreateWorkItemAsync(session, st, p, token);
            }
            // SetItemAchievement natif — AddItemFromWork
            // (@0x080b1750) l'appelle sur chaque produit réclamé.
            await TryGrantItemAchievementAsync(session, st, p.ItemId);
        }

        // EXP joueur — EndCollect/WorkResultOK ajoute col8 de la
        // recette (collect ; les combines ne rapportent pas d'EXP).
        if (work.WorkType is >= 12 and <= 14 && work.WorkId > 0
            && _services.GameData.WorkCollectById
                .TryGetValue(work.WorkId, out var collect)
            && collect != null && collect.Exp > 0)
            await GrantCharExpAsync(session, st, collect.Exp);

        // EXP de famille — S_FamilyCollect col6 / S_FamilyCombine
        // col3 : le natif l'envoie à la FIN de l'ordre (EndCombine/
        // EndCollect → SFamily::GainExp de la famille de la ville,
        // work+0x28), pas au claim ni à la guilde du joueur —
        // voir CompleteFamilyWorkAsync.

        // ResetWork + 500 ClientPlayerWorkResultEnd [s16 workNum].
        st.WorkDeferredLocs.Remove(work.WorkNum);
        await _services.WorksRepo.ResetAsync((int)st.CharId, work);
        await session.SendAsync(
            new CNC_CZ_ClientPlayerWorkResultEnd
            { Value = (short)workNum }, token);
    }

    /// <summary>Crée une copie non-stackable d'un produit dans le sac
    /// 0 avec ses métadonnées SWorkItem (socket_amount → sockets
    /// ouvertes vides, combo/extra_combos, curse → witchcraft) puis
    /// émet le 274 correspondant.</summary>
    private async Task CreateWorkItemAsync(NetSession session,
        EntryState st, WorkProduct p, CancellationToken token)
    {
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == p.ItemId);
        var any = await _services.InventoryRepo.GetAnyAvailableSlotAsync(
            (int)st.CharId);
        if (any == null)
        {
            EdenLog.Write($"[Zone] WorkResult : sac plein " +
                $"char={st.CharId} item={p.ItemId}");
            return;
        }
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entity = new ItemEntity
        {
            CharacterId = (int)st.CharId,
            BagId = any.Value.BagId,
            Slot = any.Value.Slot,
            ItemId = p.ItemId,
            Quantity = 1,
            Durability = Math.Max(def?.MaxDurability ?? 0, 1),
            MaxDurability = Math.Max(def?.MaxDurability ?? 0, 1),
            CreateTime = now,
            DueDate = def?.DurationSeconds > 0
                ? now + def.DurationSeconds : 0,
            Combo = p.Combo,
            Witchcraft = p.Curse ? 1 : 0,
        };
        // socket_amount → sockets ouvertes VIDEs (0 = ouverte sans
        // gemme ; -1 = non ouverte) — sémantique embedded_id native.
        for (var i = 0; i < 6; i++)
            entity.SetEmbeddedId(i, i < p.SocketAmount ? 0 : -1);
        entity.EmbeddedAmount = p.SocketAmount;
        for (var i = 0; i < p.ExtraCombos.Length; i++)
            entity.SetExtraCombo(i, p.ExtraCombos[i]);
        await _services.InventoryRepo.AddItemAsync(entity);
        // 274 natif [id][bag][slot][dur][maxdur] — entité directe.
        await session.SendPacketAsync(274,
            NativeItemCodec.BuildPutItem(entity), token);
        // 146 — produit de craft → ItemCollect.
        _services.NotifyItemChanged((int)st.CharId, p.ItemId);
    }

    /// <summary>274 d'un slot touché par AddItemFromWork stackable —
    /// recharge l'entité pour propager sockets/combos persistés.</summary>
    private async Task EmitPutItemAsync(NetSession session, EntryState st,
        int bag, int slot, CancellationToken token)
    {
        var e = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, bag, slot);
        if (e == null) return;
        // 274 natif [id][bag][slot][dur][maxdur] — entité directe.
        await session.SendPacketAsync(274,
            NativeItemCodec.BuildPutItem(e), token);
        // 146 — l'item créé compte pour ItemCollect
        // (UpdateQuestItemStatus natif, voie insert).
        _services.NotifyItemChanged((int)st.CharId, e.ItemId);
    }

    // ========================================================
    //  172 — CNE_CZ_ZoneServerPlayerWorkCheckData (rafraîchissement)
    // ========================================================

    /// <summary>Rafraîchit la fenêtre works — corps vide. Natif :
    /// 498 pour chaque slot dont l'enchant a expiré (terminé non
    /// réclamé) puis un 497 agrégé des ordres encore en cours.</summary>
    private async Task HandleWorkCheckDataAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.CharId == 0) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var work in st.Works)
        {
            if (work.Running && work.FinishAtMs <= now)
                await MaterializeWorkAsync(session, st, work,
                    emitResult: true, token);
            else if (work.Completed)
                await session.SendPacketAsync(498,
                    WorkPackets.BuildWorkResult(work.WorkId,
                        (short)work.WorkType, (short)work.WorkNum,
                        work.FmName, work.Products), token);
        }
        await SendWorkUpdateAsync(session, st, token);
    }

    // ========================================================
    //  Chargement, timers et matérialisation
    // ========================================================

    /// <summary>LoadPlayerWork natif — les 11 lignes player_works à
    /// l'entrée. Les ordres échus pendant l'absence sont matérialisés
    /// silencieusement (le client demandera le 498 via 172) ; ceux
    /// encore en cours repartent sur un timer du temps restant.</summary>
    private async Task LoadWorksAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        try
        {
            await _services.WorksRepo.EnsureRowsAsync((int)st.CharId);
            st.Works = await _services.WorksRepo
                .GetWorksAsync((int)st.CharId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var work in st.Works)
            {
                if (work.WorkId == 0) continue;
                if (work.FinishAtMs <= now)
                    await MaterializeWorkAsync(session, st, work,
                        emitResult: false, token);
                else if (work.Running)
                {
                    // Cap optionnel (EDEN_DEV_WORK_CAP) — identique
                    // à l'armement : borne aussi les ordres armés
                    // avant la déco.
                    var devCapMs = (long)DevWorkCap
                        * WorkOrderService.UnitMs;
                    if (DevWorkCap > 0 && work.FinishAtMs - now > devCapMs)
                    {
                        work.FinishAtMs = now + devCapMs;
                        await _services.WorksRepo.EnsureAndSaveAsync(
                            (int)st.CharId, work);
                    }
                    ArmWorkTimer(session, st, work);
                    // Réémet le 299 de l'enchant 200+num avec le
                    // reste réel — sans lui la fenêtre works affiche
                    // « 0 min 0 sec » à la reconnexion.
                    var rest = (int)Math.Max(1,
                        (work.FinishAtMs - now)
                        / WorkOrderService.UnitMs);
                    await SendWorkEnchantAsync(session, st,
                        work.WorkNum, rest, token);
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[Zone] LoadWorks char={st.CharId} : {ex.Message}");
            st.Works = Array.Empty<WorkSlot>();
        }
    }

    /// <summary>Arme le timer du slot (remplace l'enchant natif
    /// 200+work_num) — annulé à la déconnexion via WorkCts.</summary>
    private void ArmWorkTimer(NetSession session, EntryState st,
        WorkSlot work)
    {
        var cts = new CancellationTokenSource();
        st.WorkCts[work.WorkNum]?.Cancel();
        st.WorkCts[work.WorkNum]?.Dispose();
        st.WorkCts[work.WorkNum] = cts;
        var delay = TimeSpan.FromMilliseconds(Math.Max(0,
            work.FinishAtMs
            - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        // Échéance schedulée sur la boucle de zone — le timer natif
        // survit à la latence réseau ; l'annulation (cts) expire
        // l'événement sans mutation.
        ScheduleOnLoop(delay,
            _ => WorkTimerTickAsync(session.SessionId, work.WorkNum),
            cts.Token);
    }

    /// <summary>Échéance du timer — conclut le travail si le slot est
    /// toujours armé sur le même ordre (deadline identique).
    /// Tourne sur le fil de la boucle.</summary>
    private async Task WorkTimerTickAsync(Guid sessionId, int workNum)
    {
        try
        {
            if (!_states.TryGetValue(sessionId, out var st)) return;
            var session = _services.Sessions.All
                .FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null) return;
            var work = st.Works.ElementAtOrDefault(workNum);
            if (work == null || !work.Running) return;
            await MaterializeWorkAsync(session, st, work,
                emitResult: true, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] WorkTimer num={workNum} : {ex}");
        }
    }

    /// <summary>Fin d'ordre (EndCollect/EndCombine natif) :
    /// consommation différée pour les instantanés, tirage des
    /// produits dans les SWorkItem, work_id négatif si tout a
    /// échoué, persistance puis 498 (si <paramref name="emitResult"/>
    /// — les matérialisations au login restent silencieuses, le
    /// client réclame via 172).</summary>
    private async Task MaterializeWorkAsync(NetSession session,
        EntryState st, WorkSlot work, bool emitResult,
        CancellationToken token)
    {
        if (!work.Running) return;
        // Consommation différée — SetAllItemLoc natif : catalyseur et
        // booster à leur emplacement figé (recherche par id si
        // déplacé), matériaux agrégés, or recalculé.
        if (st.WorkDeferredLocs.TryGetValue(work.WorkNum, out var locs)
            && st.WorkOpStarted)
        {
            var plan = RebuildPlan(work, locs);
            if (plan is { Deferred: true })
            {
                if (!await ConsumeWorkInputsAsync(session, st, plan,
                        locs, deferred: true, token))
                {
                    // Ingrédients partis entre-temps — échec natif
                    // (SaveWork(-id,0)).
                    work.WorkId = -Math.Abs(work.WorkId);
                }
            }
        }

        if (work.WorkId > 0)
        {
            // Type 14 invocation — EndSummon natif @0x8163110 : les
            // « produits » de S_FamilyCollect sont des entrées
            // {mobId, qté, taux} — un dé cumulatif choisit UNE entrée
            // et BornMonster fait apparaître `qté` monstres hostiles
            // qui attaquent l'invocateur (BecomeFightingCandicate +
            // ChangeOwner). Pas de produits/498/claim : ResetWork +
            // 435 [0xe][1][0]×4 clôturent l'ordre directement.
            if (work.WorkType == WorkTypes.Summon
                && _services.GameData.WorkCollectById
                    .TryGetValue(work.WorkId, out var summon)
                && summon != null)
            {
                await MaterializeSummonAsync(session, st, work, summon,
                    token);
                return;
            }
            if (_services.GameData.WorkCollectById
                    .TryGetValue(work.WorkId, out var collect)
                && collect != null && WorkTypes.IsCollect(work.WorkType))
                _services.Works.RollCollectProducts(collect, work);
            else if (_services.GameData.WorkCombineById
                    .TryGetValue(work.WorkId, out var combine)
                && combine != null && WorkTypes.IsCombine(work.WorkType))
                _services.Works.RollCombineProduct(combine, work);
            // Tout a raté / recette introuvable → SaveWork(-id,0).
            if (work.Products.All(p => p.IsEmpty))
                work.WorkId = -Math.Abs(work.WorkId);
        }
        // Bloc famille natif — exécuté même sur échec (EndCombine :
        // le GainBuildingExp/GainExp/usure est hors du test de succès
        // et conditionné seulement par fm_id ≠ 0).
        await CompleteFamilyWorkAsync(session, st, work, token);
        work.Running = false;
        work.FinishAtMs = 0;
        st.WorkCts[work.WorkNum]?.Cancel();
        await _services.WorksRepo.EnsureAndSaveAsync(
            (int)st.CharId, work);
        // Expiration de l'enchant de travail 200+work_num — à
        // l'échéance le natif retire le CEnchant (RestarupEvent) →
        // 300 au client ; sans lui l'icône/compte à rebours reste.
        await RemoveWorkEnchantAsync(session, st, work.WorkNum,
            token);
        if (emitResult)
            await session.SendPacketAsync(498,
                WorkPackets.BuildWorkResult(work.WorkId,
                    (short)work.WorkType, (short)work.WorkNum,
                    work.FmName, work.Products), token);
    }

    /// <summary>Fin d'un ordre d'invocation (type 14) — EndSummon
    /// natif @0x8163110 : dé cumulatif sur les taux des entrées
    /// produits (SStuff {mobId, qté, taux} — le « produit » est un
    /// template de monstre), puis BornMonster × qté hostile ciblant
    /// l'invocateur (BecomeFightingCandicate + ChangeOwner natifs —
    /// le mob droppe son propre loot à la mort). Le slot de travail
    /// est clôturé sans 498 ni claim (ResetWork + 435).</summary>
    private async Task MaterializeSummonAsync(NetSession session,
        EntryState st, WorkSlot work,
        WorkOrderParser.WorkCollectDef def, CancellationToken token)
    {
        // Dé pondéré natif : rand%100 contre les taux cumulés des
        // entrées (+bouns_item_amount sur la qté de l'entrée tirée).
        var roll = Random.Shared.Next(100);
        float acc = 0;
        WorkOrderParser.WorkCollectDef.Product? pick = null;
        foreach (var p in def.Products)
        {
            acc += Math.Max(p.Rate, 0f);
            if (roll < acc) { pick = p; break; }
        }
        if (pick != null && pick.ItemId > 0)
        {
            var count = Math.Max(1,
                pick.Quantity + work.BonusItemAmount);
            var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
            for (var i = 0; i < count; i++)
            {
                // BornMonster(position du joueur, décalage aléatoire
                // ~2u, aggro immédiate sur l'invocateur).
                var mx = st.PosX +
                    (float)(Random.Shared.NextDouble() * 4.0 - 2.0);
                var mz = st.PosZ +
                    (float)(Random.Shared.NextDouble() * 4.0 - 2.0);
                var mob = _services.Live.SpawnHostileMob(st.NodeId,
                    st.InstanceGroup, pick.ItemId, (int)st.CharId,
                    mx, mz, _services.Live.GroundHeightAt(scene, mx, mz),
                    st.Facing);
                EdenLog.Write($"[Zone] Summon work char={st.CharId} " +
                    $"num={work.WorkNum} → mob {pick.ItemId} " +
                    $"handle={mob.Handle:X8}");
            }
            // Annonce immédiate (298) : sans ça le mob reste invisible
            // jusqu'au prochain déplacement du joueur.
            await UpdateSightAsync(session, st, token, force: true);
        }
        else
            EdenLog.Write($"[Zone] Summon work char={st.CharId} " +
                $"num={work.WorkNum} : dice sans entrée " +
                $"(workId={work.WorkId})");
        // Bloc famille natif d'EndSummon (EXP bâtiment/famille/usure)
        // — avant ResetWork qui efface fm_id.
        await CompleteFamilyWorkAsync(session, st, work, token);
        // ResetWork natif — le slot se libère directement (aucune
        // remise d'items : les produits du plan sont le mob).
        st.WorkDeferredLocs.Remove(work.WorkNum);
        work.Reset();
        st.WorkCts[work.WorkNum]?.Cancel();
        await _services.WorksRepo.ResetAsync((int)st.CharId, work);
        await RemoveWorkEnchantAsync(session, st, work.WorkNum, token);
        // 435 ClientStrengthenItemResult [s16 0xe][s16 1][s16 0×4] —
        // corps natif du NotifyClient d'EndSummon.
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
        { Field1 = (short)WorkTypes.Summon, Field2 = 1 }, token);
    }

    /// <summary>299 ClientEnchantAdd pour l'enchant de travail
    /// <c>200+work_num</c> — portage de l'<c>EffectToLife(char, char,
    /// work+4, durée, 0x2200, true, 0, 1, 0, 0)</c> de ToCollect
    /// @0x816aaf / ToCombine : les enchants « PlayerWork N » sont
    /// synthétisés au runtime (PGSpellDBO @0x837cxx, icône non cachée
    /// → le 299 part). <paramref name="restUnits"/> = rest_time en
    /// unités de 100 ms (GetRestTime @0x85603d0 rend des
    /// dixièmes de seconde — c'est la même base que la durée
    /// d'ordre). Envoyé aussi aux voyeurs (BroadCastToCanSee
    /// natif).</summary>
    private async Task SendWorkEnchantAsync(NetSession session,
        EntryState st, int workNum, int restUnits,
        CancellationToken token)
    {
        var add = new CNC_CZ_ClientEnchantAdd
        {
            Field1 = (int)st.CharId,                 // i32 : handle entité
            Field2 = (short)WorkOrderService
                .EventIdFor(workNum),                // s16 : effect 200+num
            Field3 = restUnits,                      // i32 : rest_time ×100 ms
            Field4 = st.NextAction(),                // u8  : serial char+0x280
            Field5 = 1,                              // s16 : counter +0x30
            Field6 = 0,                              // i32 : flagBits (+0x28)
            Field7 = 1,                              // s16 : level +0x3c
            Field8 = 0,                              // i32 : counter2 +0x34
        };
        await session.SendAsync(add, token);
        await BroadcastToNodeAsync(session, st, add, token);
    }

    /// <summary>300 ClientEnchantRemove de l'enchant de travail
    /// <c>200+work_num</c> — expiration/retrait (même corps que
    /// RemovePlayerEnchantAsync : act 0, +0x10/+0x14 à 0).</summary>
    private async Task RemoveWorkEnchantAsync(NetSession session,
        EntryState st, int workNum, CancellationToken token)
    {
        var rm = new CNC_CZ_ClientEnchantRemove
        {
            Field1 = (int)st.CharId,                 // i32 : handle entité
            Field2 = (short)WorkOrderService
                .EventIdFor(workNum),                // s16 : effect 200+num
            Field3 = st.NextAction(),                // u8  : serial char+0x280
            Field4 = 0, Field5 = 0,                  // i32 ×2 : +0x10/+0x14
        };
        await session.SendAsync(rm, token);
        await BroadcastToNodeAsync(session, st, rm, token);
    }

    /// <summary>Reconstruit le plan de consommation d'un ordre armé —
    /// nécessaire à la consommation différée (la recette donne les
    /// matériaux ; les emplacements figés portent catalyseur et
    /// booster).</summary>
    private WorkOrderService.StartPlan? RebuildPlan(WorkSlot work,
        WorkDeferredLocs locs)
    {
        var id = Math.Abs(work.WorkId);
        WorkOrderService.StartPlan? plan = null;
        if (WorkTypes.IsCollect(work.WorkType)
            && _services.GameData.WorkCollectById
                .TryGetValue(id, out var collect) && collect != null)
            plan = new WorkOrderService.StartPlan
            {
                IsCollect = true, Collect = collect,
                CatalystQty = Math.Max(collect.CatalystAmount, 1),
                GoldCost = collect.Gold,
                Deferred = collect.Instant
                    || work.WorkType == WorkTypes.Summon,
            };
        else if (WorkTypes.IsCombine(work.WorkType)
            && _services.GameData.WorkCombineById
                .TryGetValue(id, out var combine) && combine != null)
        {
            plan = new WorkOrderService.StartPlan
            {
                IsCollect = false, Combine = combine,
                CatalystQty = Math.Max(combine.Materials
                    .FirstOrDefault(m => m.ItemId == locs.CatalystItemId)
                    ?.Quantity ?? 1, 1),
                GoldCost = combine.Gold,
                Deferred = combine.Instant,
            };
            foreach (var m in combine.Materials)
                if (m.ItemId != locs.CatalystItemId)
                    plan.Materials.Add((m.ItemId, Math.Max(m.Quantity, 1)));
        }
        // Taxe figée à l'armement (op+0x18) — même coût taxé à la
        // consommation différée qu'au démarrage.
        if (plan != null && locs.TaxRate != 0)
            plan.GoldCost = Eden.Core.Game.Family.FamilyBuildingWorkRules
                .TaxedGold(plan.GoldCost, locs.TaxRate);
        return plan;
    }

    // ========================================================
    //  Consommation (catalyseur, booster, matériaux, or)
    // ========================================================

    /// <summary>Consomme les entrées de l'ordre — booster 1 unité,
    /// catalyseur à son emplacement (ou par id si différé), matériaux
    /// agrégés par id, or. Texte natif 0x534 « %s x %d » par ligne
    /// consommée. Retourne false si une entrée manque (abort).</summary>
    private async Task<bool> ConsumeWorkInputsAsync(NetSession session,
        EntryState st, WorkOrderService.StartPlan plan,
        WorkDeferredLocs locs, bool deferred, CancellationToken token)
    {
        var charId = (int)st.CharId;
        // Booster — ItemConsume (loc figé) : 1 unité.
        if (locs.BoosterItemId != 0 && !await ConsumeItemAtAsync(
                session, st, locs.BoosterBag, locs.BoosterSlot, 1,
                token))
            return false;
        // Catalyseur — loc figé en démarrage ; recherche par id pour
        // la consommation différée (l'item a pu bouger).
        if (!(deferred
                ? await DropItemsByItemIdAsync(session, st,
                    locs.CatalystItemId, plan.CatalystQty, token)
                : await ConsumeItemAtAsync(session, st,
                    locs.CatalystBag, locs.CatalystSlot,
                    plan.CatalystQty, token)))
            return false;
        // Matériaux (combine) — agrégat par item_id (CheckItemNum).
        foreach (var (matId, qty) in plan.Materials)
            if (!await DropItemsByItemIdAsync(session, st, matId, qty,
                    token))
                return false;
        // Or — ToCombine/ToCollect natifs : FamilyLevyBusinessTaxes(tax)
        // crédite la taxe à la ville visitée (texte 0xd15) PUIS
        // GainGold(−total). Taxe = coût taxé − coût de la recette.
        if (plan.GoldCost > 0)
        {
            var baseGold = plan.Combine?.Gold ?? plan.Collect?.Gold ?? 0;
            var tax = locs.TaxRate != 0
                ? Math.Max(0, plan.GoldCost - baseGold) : 0;
            if (tax > 0 && st.VisitedFamilyInstanceId != 0
                && IsInFamilyInstanceNode(st)
                && await _services.Guilds.CreditBusinessTaxAsync(
                    st.VisitedFamilyInstanceId, tax) is { } townName)
            {
                EdenLog.Write($"[VDG-Work] taxe {tax} → trésorerie de " +
                    $"<{townName}> (fm={st.VisitedFamilyInstanceId}, " +
                    $"taux={locs.TaxRate} %)");
                await SendTextIndexAsync(session, 0xd15, token,
                    $"{townName},{tax}");
            }
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                charId, -plan.GoldCost);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
                token);
        }
        // Texte 0x534 « %s x %d » — les matériaux consommés.
        var catName = _services.GameData.Items
            .FirstOrDefault(i => i.Id == locs.CatalystItemId)?.Name
            ?? locs.CatalystItemId.ToString();
        await SendTextIndexAsync(session, 0x534, token,
            $"{catName} x {plan.CatalystQty}");
        foreach (var (matId, qty) in plan.Materials)
        {
            var name = _services.GameData.Items
                .FirstOrDefault(i => i.Id == matId)?.Name
                ?? matId.ToString();
            await SendTextIndexAsync(session, 0x534, token,
                $"{name} x {qty}");
        }
        return true;
    }

    /// <summary>Consomme `qty` à l'emplacement exact — 291 (reste)
    /// ou 275 (slot vidé) comme la consommation inlay native.</summary>
    private async Task<bool> ConsumeItemAtAsync(NetSession session,
        EntryState st, int bag, int slot, int qty,
        CancellationToken token)
    {
        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, bag, slot);
        if (item == null || item.Quantity < qty) return false;
        if (!await _services.Inventory.RemoveItemAsync(
                (int)st.CharId, bag, slot, qty)) return false;
        var left = item.Quantity - qty;
        if (left > 0)
            // 291 ClientModifyItemDurability — corps natif 6×s16
            // [contType][loc][nouveau count][item+0xe=maxDur]
            // [contIdx][0] (Serialize @0x810e570 + builder
            // DoUseItemAfterCheck @0x80b84c0). Le format 6B envoyé
            // avant désynchronisait le flux RC4.
            await session.SendAsync(
                new Eden.Core.Networking.Messages.Generated
                    .CNC_CZ_ClientModifyItemDurability
                {
                    Field1 = 0,
                    Field2 = (short)slot,
                    Field3 = (short)left,
                    Field4 = (short)item.MaxDurability,
                    Field5 = (short)bag,
                    Field6 = 0,
                }, token);
        else
            // 275 ClientDropItem natif [s16 contType][s16 loc]
            // [s16 contIdx] — EradicateItem vide le slot entier, le
            // paquet ne porte PAS de quantité (ItemConsume
            // @0x808f460 : off4=type conteneur, off6=loc,
            // off8=index conteneur). L'ancien corps [sac][loc][qté]
            // décalait le sac dans le champ index → le client
            // n'enlevait rien (consommation invisible du craft).
            await session.SendAsync(new CNC_CZ_ClientDropItem
            {
                Field1 = 0, Field2 = (short)slot, Field3 = (short)bag,
            }, token);
        // 146 — consommation (matériau/cube/craft) → ItemCollect.
        _services.NotifyItemChanged((int)st.CharId, item.ItemId);
        return true;
    }

    // ========================================================
    //  Émission 497 + capacité
    // ========================================================

    /// <summary>497 ClientPlayerWorkUpdate agrégé — les ordres encore
    /// en cours (work_num, type, id, enchant 200+num, fm_name).</summary>
    private async Task SendWorkUpdateAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var entries = st.Works
            .Where(w => w.Running && w.WorkId != 0)
            .Select(w => new WorkPackets.WorkUpdateEntry(
                (short)w.WorkNum, (short)w.WorkType, w.WorkId,
                WorkOrderService.EventIdFor(w.WorkNum), w.FmName))
            .ToList();
        await session.SendPacketAsync(497,
            WorkPackets.BuildWorkUpdate(entries), token);
    }

    /// <summary>Slots libres sur les sacs 0-3 — GetEmptySlotNum natif
    /// (capacités inventory_slots − items posés).</summary>
    private async Task<int> CountFreeBagSlotsAsync(int charId,
        IReadOnlyList<ItemEntity> items)
    {
        var caps = await _services.InventoryRepo
            .GetAllBagCapacitiesAsync(charId);
        var free = 0;
        foreach (var (bag, cap) in caps)
        {
            if (cap <= 0) continue;
            free += cap - items.Count(
                i => i.BagId == bag && !i.IsEquipped
                    && i.Slot >= 0 && i.Slot < cap);
        }
        return Math.Max(free, 0);
    }
}
