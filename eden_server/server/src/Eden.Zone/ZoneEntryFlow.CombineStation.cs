using Eden.Core.Game.Work;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Station de combinaison (service 10 — CCombineOp, IsA 0x27) :
/// le NCID 170 PlayerWork est dispatché ici quand l'op combine est
/// ouverte (CItemFactory::CombineItem @0x809b8e0).
///
/// Fidélité native :
/// - armement : CheckCombineItem (recette catégorie 6, 4 matériaux,
///   catalyseur=matériau[0] au loc, booster 0x1f41, or, slot libre
///   si aucune pile ne se vide) → op+0x14=1 + enchant CombineItemID
///   (0x28×100 ms) + 433 [10][0x28][0] ; échec → 434 [10] ;
///   déjà armé → silence.
/// - expiration (DoCombineItem) : re-check → GainGold(-col9),
///   catalyseur au loc, matériaux 1-3 agrégés, booster → roll
///   rand%100 &lt; col13 (+bonus booster non mappé) →
///   succès : PlayerAction 0x2c + produit col10 ×1 + 435[10][1] ;
///   échec : 435[10][0] + PlayerAction 0x2b.
/// - StrengthenClose 10 → CCombineOp::Cancel : le cast meurt + 434[10].
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>170 avec op combine ouverte — CombineItem natif.
    /// Même wire que ToWork :
    /// [u32 recipeId][s16 catBag][s16 catSlot][s16 boostBag][s16 boostSlot].</summary>
    private async Task HandleCombineItemAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 12) return;
        var recipeId = BitConverter.ToInt32(payload, 0);     // u32 — recette
        var catBag = BitConverter.ToInt16(payload, 4);       // s16 — sac catalyseur
        var catSlot = BitConverter.ToInt16(payload, 6);      // s16 — loc catalyseur
        var boostBag = BitConverter.ToInt16(payload, 8);     // s16 — sac booster (−1 si aucun)
        var boostSlot = BitConverter.ToInt16(payload, 10);   // s16 — loc booster

        // CombineItem natif : op 0x27 + TargetInTalkingRange(op+4).
        if (st.PendingCombine != null) return; // enchant déjà armé → silence
        if (!OpInTalkingRange(st)) return;

        var catalyst = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, catBag, catSlot);
        var booster = boostBag >= 0 && boostSlot >= 0
            ? await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, boostBag, boostSlot)
            : null;
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        var free = await CountFreeBagSlotsAsync((int)st.CharId, items);
        var check = _services.CombineStation.CheckCombine(recipeId,
            catalyst, booster, items, st.Gold, free);
        if (!check.Ok)
        {
            // Texte explicite du natif quand il en émet un (0x249,
            // 0x4f9), sinon juste le cancel 434 [10].
            if (check.TextId > 0)
                await SendTextIndexAsync(session, check.TextId, token);
            await SendCombineCancelAsync(session, token);
            return;
        }

        st.PendingCombine = new PendingCombine
        {
            RecipeId = recipeId,
            CatBag = catBag, CatSlot = catSlot,
            BoostBag = booster?.BagId ?? -1,
            BoostSlot = booster?.Slot ?? -1,
        };
        // 433 [s16 10][s16 0x28][s16 0] — cast bar de la fenêtre
        // combine ; l'enchant CombineItemID vit 0x28×100 ms.
        var castUnits = Features.Craft.CraftTimes.CastUnits(_services, st, CombineStationService.CastUnits);
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        {
            Field1 = 10,
            Field2 = (short)castUnits,
            Field3 = 0,
        }, token);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        st.PendingCombine.Cts = cts;
        // Résolution à l'échéance du cast schedulée sur la boucle —
        // un Task.Delay dans le corps suspendrait le fil de jeu.
        ScheduleOnLoop(TimeSpan.FromMilliseconds(
                castUnits * WorkOrderService.UnitMs),
            _ => ResolveCombineAfterCastAsync(session.SessionId),
            cts.Token);
    }

    /// <summary>434 ClientStrengthenItemCancel [s16 10] — échec du
    /// check (CombineItem) ou abort de la résolution.</summary>
    private static async Task SendCombineCancelAsync(NetSession session,
        CancellationToken token)
        => await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
        { Value = 10 }, token);

    /// <summary>Expiration de l'enchant CombineItemID (0x28×100 ms) —
    /// DoCombineItem natif : re-check, consommation, roll, 435 +
    /// PlayerAction 0x2b/0x2c.</summary>
    private async Task ResolveCombineAfterCastAsync(Guid sessionId)
    {
        try
        {
            if (!_states.TryGetValue(sessionId, out var st)) return;
            var session = _services.Sessions.All
                .FirstOrDefault(s => s.SessionId == sessionId);
            var p = st.PendingCombine;
            st.PendingCombine = null;
            if (session == null || p == null || st.CharId == 0) return;
            await DoCombineItemAsync(session, st, p,
                CancellationToken.None);
        }
        catch (OperationCanceledException) { /* StrengthenClose 10 */ }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] ResolveCombine : {ex}");
        }
    }

    /// <summary>DoCombineItem — re-check natif puis consommation
    /// (or, catalyseur au loc, matériaux 1-3 agrégés, booster) et
    /// roll : succès → produit col10 ×1 + 435[10][1] + anim 0x2c ;
    /// échec → 435[10][0] + anim 0x2b.</summary>
    private async Task DoCombineItemAsync(NetSession session,
        EntryState st, PendingCombine p, CancellationToken token)
    {
        if (!OpInTalkingRange(st)) return;
        var def = _services.CombineStation.GetRecipe(p.RecipeId);
        var catalyst = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.CatBag, p.CatSlot);
        var booster = p.BoostBag >= 0 && p.BoostSlot >= 0
            ? await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, p.BoostBag, p.BoostSlot)
            : null;
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        var free = await CountFreeBagSlotsAsync((int)st.CharId, items);
        if (def == null || !_services.CombineStation.CheckCombine(
                p.RecipeId, catalyst, booster, items, st.Gold, free).Ok)
        {
            await SendCombineCancelAsync(session, token);
            return;
        }

        // Consommation DoCombineItem : or (col9), catalyseur au loc
        // (qté matériau[0]), matériaux[1..3] par DropItemByItemID,
        // booster 1 unité.
        if (def.Gold > 0)
        {
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, -def.Gold);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0,
                token);
        }
        await ConsumeItemAtAsync(session, st, p.CatBag, p.CatSlot,
            def.Materials[0].Quantity, token);
        foreach (var m in def.Materials.Skip(1))
            await DropItemsByItemIdAsync(session, st, m.ItemId,
                m.Quantity, token);
        if (booster != null)
            await ConsumeItemAtAsync(session, st, p.BoostBag,
                p.BoostSlot, 1, token);

        if (_services.CombineStation.RollSuccess(def))
        {
            // Succès — anim 0x2c aux voyeurs puis produit col10 ×1
            // (AddItemFromCombine → AddItemToCharacter natif).
            await BroadcastPlayerActionAsync(st, 0x2c, 0, 0, token);
            var touched = new List<(int BagId, int Slot, bool Merged)>();
            await _services.Inventory.AddItemAsync(
                (int)st.CharId, def.ProductItemId, 1, 0, 0, false,
                touched);
            foreach (var t in touched)
                await EmitPutItemAsync(session, st, t.BagId, t.Slot,
                    token);
            await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
            { Field1 = 10, Field2 = 1 }, token);
        }
        else
        {
            // Échec — 435[10][0] puis anim 0x2b (ordre natif : le 435
            // part AVANT le broadcast PlayerAction).
            await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
            { Field1 = 10, Field2 = 0 }, token);
            await BroadcastPlayerActionAsync(st, 0x2b, 0, 0, token);
        }
        EdenLog.Write($"[Zone] Combine char={st.CharId} " +
            $"recipe={p.RecipeId} → produit {def.ProductItemId}");
    }
}
