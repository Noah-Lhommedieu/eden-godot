// ============================================================
//  ZoneEntryFlow.Cube.cs — cube Horadric (transmutation d'items)
//
//  Portage de lapis::CItemFactory::{InitCubeOp, CheckCubeCosts,
//  ExecuteCubeOp, PickAnItem, PickAColor, CreateCubeItem,
//  DeductItem} + lapis::CCubeOp::{SetItemData,GetItemData,Cancel}
//  (désasm zone @0x80bb0f0 / @0x80ba380 / @0x80bceb0 / @0x809d210 /
//  @0x80a0460 / @0x80aff30 / @0x80bc870 / @0x856cfxx).
//
//  Fenêtre : PlayerAction 101 → CCubeOp (IsA 0x52) + 432 [0][0x1c]
//  (déjà branché dans ZoneEntryFlow.Strengthen.cs — ShopOpType=28).
//
//  Wire 232 CNE_CZ_ZoneServerVerifyCubeCosts (Execute @0x829f0d0) :
//    [u32 recipeId][u16 n][s16 sacs×n][u16 n][s16 slots×n]
//    [s16 luckySac][s16 luckySlot]
//  — les deux vecteurs sont parallèles (bag,slot) des items posés
//  (CheckCubeCosts lit param_3[i]=sac, param_4[i]=slot) ; les deux
//  derniers shorts = position du cube chanceux (param_5/param_6 de
//  InitCubeOp — CChanceItem enchant 8007).
//
//  Émis :
//    433 ClientStrengthenCasting [s16 0x1c][s16 0xf][s16 0]
//        (InitCubeOp — enchant CubeCrafting 0xf ticks ≈1.5s)
//    434 ClientStrengthenItemCancel [s16 0x1c] — refus/annulation
//    435 ClientStrengthenItemResult [s16 0x1c][s16 ok][s16 0×4]
//    572 ClientLoadCubeItems [u16 n][s16 sacs][u16 n][s16 slots]
//        [u16 n][s16 qtés] — GetProdData/AppendProdData natif
//        (sérialiseur généré faux : format plat — corps manuel)
//    274/275 notifications inventaire + 316 textes (0x232 or,
//    0x4f9 sac plein, 0x15af lucky insuffisant, 0x21c4 bonus requis)
//    broadcast PlayerAction : 0x2b succès, '-'/'*'/'/' échec.
// ============================================================
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Cube;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Durée du cast cube en unités 100 ms — 0xf natif
    /// (EffectToLife(CubeCrafting, durée=0xf) dans InitCubeOp).</summary>
    private const short CubeCastUnits = 0xf;

    /// <summary>Données de l'opération cube (CCubeOp::SetItemData +
    /// SetAuxData natifs — item vector + aux vector).</summary>
    internal sealed class PendingCube
    {
        public int RecipeId;                            // op+0x08
        /// <summary>Slots posés (bag,slot parallèles) — op+0x0c/+0x10.</summary>
        public List<(short Bag, short Slot)> Placed = new();
        public short LuckyBag = -1;                     // op+0x18 (param_5)
        public short LuckySlot = -1;                    // op+0x1a (param_6)
        public CancellationTokenSource? Cts;
    }

    // ============================================================
    //  NCID 232 — CNE_CZ_ZoneServerVerifyCubeCosts::Execute @0x829f0d0
    //  acteur perso + op 0x52 (CCubeOp) → InitCubeOp : CheckCubeCosts
    //  puis SetItemData + enchant CubeCrafting + 433, ou 434 échec.
    // ============================================================
    private async Task HandleVerifyCubeCostsAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.ShopOpType != 28 || st.PendingCube != null
            || payload.Length < 8) return;
        var recipeId = BitConverter.ToInt32(payload, 0);    // u32 : id recette
        var n1 = BitConverter.ToUInt16(payload, 4);         // u16 : nb sacs
        if (payload.Length < 6 + n1 * 2 + 2) return;
        var off = 6;
        var bags = new short[n1];
        for (var i = 0; i < n1; i++, off += 2)
            bags[i] = BitConverter.ToInt16(payload, off);   // s16 : index sac
        var n2 = BitConverter.ToUInt16(payload, off);       // u16 : nb slots
        off += 2;
        if (payload.Length < off + n2 * 2 + 4 || n1 != n2) return;
        var slots = new short[n2];
        for (var i = 0; i < n2; i++, off += 2)
            slots[i] = BitConverter.ToInt16(payload, off);  // s16 : slot
        var luckyBag = BitConverter.ToInt16(payload, off);  // s16 : sac lucky
        var luckySlot = BitConverter.ToInt16(payload, off + 2); // s16 : slot

        var recipe = _services.Cube.Recipe(recipeId);
        if (recipe == null) { await CubeCancelAsync(session, token); return; }

        var placed = await ResolveCubePlacedAsync(st, bags, slots, token);
        if (placed == null) { await CubeCancelAsync(session, token); return; }

        CubeService.PlacedItem? lucky = null;
        if (luckyBag >= 0 && luckySlot >= 0)
        {
            var li = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, luckyBag, luckySlot);
            if (li == null) { await CubeCancelAsync(session, token); return; }
            lucky = ToPlaced(li);
        }

        var check = _services.Cube.CheckCosts(recipe, st.Gold, st.Level,
            await EmptySlotsAsync(st, token), placed, lucky);
        if (!check.Ok)
        {
            // Textes natifs de CheckCubeCosts avant le cancel.
            if (check.FailTextId != 0)
                await SendTextIndexAsync(session, check.FailTextId, token);
            await CubeCancelAsync(session, token);
            return;
        }

        // InitCubeOp → SetItemData + flag actif (op+0x64) + cast.
        st.PendingCube = new PendingCube
        {
            RecipeId = recipeId,
            LuckyBag = luckyBag,
            LuckySlot = luckySlot,
        };
        foreach (var (bag, slot) in bags.Zip(slots))
            st.PendingCube.Placed.Add((bag, slot));
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        {
            Field1 = 0x1c,                  // s16 : fenêtre cube
            Field2 = CubeCastUnits,         // s16 : 15×100ms
            Field3 = 0,                     // s16 : non écrit nativement
        }, token);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        st.PendingCube.Cts = cts;
        ScheduleOnLoop(TimeSpan.FromMilliseconds(CubeCastUnits * 100),
            _ => ExecuteCubeAfterCastAsync(session, st, st.PendingCube,
                cts.Token), cts.Token);
    }

    /// <summary>Résout les (sac,slot) posés en items + defs —
    /// GetItemFromInv natif ; null si un slot est vide/invalide.</summary>
    private async Task<List<CubeService.PlacedItem>?> ResolveCubePlacedAsync(
        EntryState st, short[] bags, short[] slots, CancellationToken token)
    {
        var list = new List<CubeService.PlacedItem>(bags.Length);
        for (var i = 0; i < bags.Length; i++)
        {
            var item = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, bags[i], slots[i]);
            if (item == null) return null;
            list.Add(ToPlaced(item));
        }
        return list;
    }

    /// <summary>Entité inventaire → PlacedItem du check natif
    /// (stack = durability/count natif, quality = clone+0x2c — le
    /// clone natif copie itemdata+0x50 à la création, donc un 0 en DB
    /// = qualité de base de la def).</summary>
    private CubeService.PlacedItem ToPlaced(
        Eden.Core.Game.Inventory.ItemEntity item)
    {
        var def = _services.Cube.ItemDef(item.ItemId);
        var quality = item.Quality != 0 ? item.Quality : def?.Quality ?? 0;
        return new CubeService.PlacedItem(item.ItemId,
            Math.Max(item.Quantity, 1), quality, item.IsBound, def);
    }

    /// <summary>CCubeOp::Cancel natif — 434 [s16 0x1c] (code fenêtre).</summary>
    private async Task CubeCancelAsync(NetSession session,
        CancellationToken token)
    {
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
        { Value = 0x1c }, token);
    }

    /// <summary>Annulation du cast cube (StrengthenClose 28 /
    /// déconnexion) — le natif retire l'enchant CubeCrafting puis
    /// émet le 434.</summary>
    private async Task CancelPendingCubeAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingCube == null) return;
        st.PendingCube.Cts?.Cancel();
        st.PendingCube = null;
        await CubeCancelAsync(session, token);
    }

    // ============================================================
    //  Fin de cast → CItemFactory::ExecuteCubeOp @0x80bceb0.
    //  1) re-CheckCubeCosts (exec) → 434 ; 2) DeductItem × slots posés
    //  + lucky ; 3) GainGold(-recipe+0x24) ; 4) jet rand(100) <
    //  rate+bonus → produits ou 435 échec + anim.
    // ============================================================
    private async Task ExecuteCubeAfterCastAsync(NetSession session,
        EntryState st, PendingCube p,
        CancellationToken ct)
    {
        try
        {
            if (st.PendingCube != p) return;
            st.PendingCube = null;
            if (st.ShopOpType != 28) return;

            var recipe = _services.Cube.Recipe(p.RecipeId);
            if (recipe == null) { await CubeCancelAsync(session, ct); return; }

            var bags = p.Placed.Select(x => x.Bag).ToArray();
            var slots = p.Placed.Select(x => x.Slot).ToArray();
            var placed = await ResolveCubePlacedAsync(st, bags, slots, ct);
            if (placed == null) { await CubeCancelAsync(session, ct); return; }

            CubeService.PlacedItem? lucky = null;
            if (p.LuckyBag >= 0 && p.LuckySlot >= 0)
            {
                var li = await _services.InventoryRepo.GetItemAsync(
                    (int)st.CharId, p.LuckyBag, p.LuckySlot);
                lucky = li == null ? null : ToPlaced(li);
            }

            // Revalidation exec (items potentiellement déplacés).
            var check = _services.Cube.CheckCosts(recipe, st.Gold, st.Level,
                await EmptySlotsAsync(st, ct), placed, lucky);
            if (!check.Ok) { await CubeCancelAsync(session, ct); return; }

            // DeductItem natif : consommation réelle par slot posé
            // (aux = 0 — non consommés), puis le cube chanceux.
            for (var i = 0; i < p.Placed.Count; i++)
            {
                var take = check.Consumed[i];
                if (take <= 0) continue;
                var newCount = placed[i].Stack - take;
                await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, bags[i], slots[i], take);
                // Wire natif : pile vidée → 275 [contType][loc]
                // [contIdx] ; pile résiduelle → 291 [contType][loc]
                // [newCount][maxDur][contIdx][0] (DeductItem utilise
                // ItemConsume/DropItemByItemID — même format).
                if (newCount > 0)
                    await session.SendAsync(
                        new CNC_CZ_ClientModifyItemDurability
                        {
                            Field1 = 0, Field2 = slots[i],
                            Field3 = (short)newCount,
                            Field4 = (short)(placed[i].Def
                                ?.MaxDurability ?? 0),
                            Field5 = bags[i], Field6 = 0,
                        }, ct);
                else
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    { Field1 = 0, Field2 = slots[i],
                      Field3 = bags[i] }, ct);
                // 146 — ingrédient consommé → ItemCollect.
                _services.NotifyItemChanged((int)st.CharId,
                    placed[i].ItemId);
            }
            if (lucky != null)
            {
                var luckyNewCount = lucky.Stack - 1;
                await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, p.LuckyBag, p.LuckySlot, 1);
                // Même wire natif : 291 si la pile survit, 275 sinon.
                if (luckyNewCount > 0)
                    await session.SendAsync(
                        new CNC_CZ_ClientModifyItemDurability
                        {
                            Field1 = 0, Field2 = p.LuckySlot,
                            Field3 = (short)luckyNewCount,
                            Field4 = (short)(_services.Cube
                                .ItemDef(lucky.ItemId)
                                ?.MaxDurability ?? 0),
                            Field5 = p.LuckyBag, Field6 = 0,
                        }, ct);
                else
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    { Field1 = 0, Field2 = p.LuckySlot,
                      Field3 = p.LuckyBag }, ct);
                // 146 — cube chanceux consommé → ItemCollect.
                _services.NotifyItemChanged((int)st.CharId,
                    lucky.ItemId);
            }

            // Or — GainGold(-recipe+0x24) natif + attr 0x02.
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, -recipe.Gold);
            await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0, ct);

            // Jet rand(100) < rate(+0x28) + bonus lucky + pool non vide
            // (pool vide après SiftByItemBlackList = même échec natif).
            var pool = _services.Cube.ProductPool(recipe, check.AuxItemIds);
            if (!_services.Cube.RollSuccess(recipe, check.LuckyBonus)
                || pool.Count == 0)
            {
                await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
                { Field1 = 0x1c, Field2 = 0 }, ct);        // échec
                // Anim d'échec aux voyeurs : '-' (rand0), '/' (rand1),
                // '*' (rand2) — GetShortRand(3) natif.
                var anim = new[] { (byte)'-', (byte)'/', (byte)'*' }[
                    Random.Shared.Next(3)];
                await BroadcastCubeActionAsync(st, anim, ct);
                return;
            }

            await ProduceCubeItemsAsync(session, st, recipe, check,
                pool, ct);
        }
        catch (OperationCanceledException) { /* StrengthenClose 28 */ }
        catch (Exception ex)
        { EdenLog.Write($"[Zone] ExecuteCubeOp: {ex.Message}"); }
    }

    /// <summary>Production natif : non-stack → `amount` picks
    /// (erase quand pool tendu = items distincts) ; stackable →
    /// min(amount,3,pool) stacks à qté aléatoire. Puis broadcast 0x2b,
    /// 435 succès, 572 prod vectors.</summary>
    private async Task ProduceCubeItemsAsync(NetSession session,
        EntryState st, ShopAndMiscParsers.ItemChestDef recipe,
        CubeService.CostCheck check, HashSet<int> pool,
        CancellationToken ct)
    {
        var prodDef = _services.Cube.ProductDef(recipe);
        var picks = new List<(int ItemId, int Qty)>();
        var poolLeft = new HashSet<int>(pool);
        var rng = Random.Shared;

        if (prodDef == null || prodDef.MaxStack < 2)
        {
            // Non-stackable : 1 item par pièce, quantité +0x34.
            for (var i = 0; i < Math.Max(recipe.ProductAmount, 1)
                    && poolLeft.Count > 0; i++)
            {
                var id = _services.Cube.PickItem(poolLeft);
                // erase natif quand les picks restants couvrent le pool.
                if (recipe.ProductAmount - i <= poolLeft.Count)
                    poolLeft.Remove(id);
                picks.Add((id, 1));
            }
        }
        else
        {
            foreach (var qty in _services.Cube.SplitStackAmount(
                    Math.Max(recipe.ProductAmount, 1), poolLeft.Count))
            {
                var id = _services.Cube.PickItem(poolLeft);
                poolLeft.Remove(id);   // erase systématique (stackable)
                picks.Add((id, qty));
            }
        }

        // AppendProdData : (sac, slot, qté) par item créé — CreateCubeItem.
        var prodBags = new List<short>();
        var prodSlots = new List<short>();
        var prodQtys = new List<short>();
        foreach (var (id, qty) in picks)
        {
            var quality = _services.Cube.PickQuality(recipe,
                check.AuxQualities, id);
            var stackable = _services.Cube.MaxStack(id) > 1;
            // Stackable : bound=false + set vide natifs ; merge via
            // AddItemAsync (AddItemToCharacter auto-stack). Non-stack :
            // insert direct pour porter Quality/Bind natifs.
            if (stackable)
            {
                var touched = new List<(int BagId, int Slot, bool Merged)>();
                var (ok, _, bag, slot) = await _services.Inventory
                    .AddItemAsync((int)st.CharId, id, qty, 0, 0,
                        false, touched);
                if (!ok) continue;
                prodBags.Add((short)(bag ?? 0));
                prodSlots.Add((short)(slot ?? 0));
                prodQtys.Add((short)qty);
                await NotifyItemAddedAsync(session, st, id, touched, ct);
            }
            else
            {
                var any = await _services.InventoryRepo
                    .GetAnyAvailableSlotAsync((int)st.CharId);
                if (any == null) continue;
                var def = _services.Cube.ItemDef(id);
                var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await _services.InventoryRepo.AddItemAsync(
                    new Eden.Core.Game.Inventory.ItemEntity
                    {
                        CharacterId = (int)st.CharId,
                        BagId = any.Value.BagId,
                        Slot = any.Value.Slot,
                        ItemId = id,
                        Quantity = 1,
                        Durability = Math.Max(def?.MaxDurability ?? 0, 1),
                        MaxDurability = Math.Max(def?.MaxDurability ?? 0, 1),
                        IsBound = check.ProductBound,
                        Quality = quality,
                        CreateTime = now,
                        DueDate = def?.DurationSeconds > 0
                            ? now + def.DurationSeconds : 0,
                    });
                prodBags.Add((short)any.Value.BagId);
                prodSlots.Add((short)any.Value.Slot);
                prodQtys.Add(1);
                await NotifyItemAddedAsync(session, st, id,
                    [(any.Value.BagId, any.Value.Slot, false)], ct);
            }
        }

        // Broadcast PlayerAction 0x2b (anim de réussite — BroadCastToCanSee).
        await BroadcastCubeActionAsync(st, 0x2b, ct);

        // 435 [s16 0x1c][s16 1][s16 0×4] — succès cube.
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
        { Field1 = 0x1c, Field2 = 1 }, ct);

        // 572 GetProdData : 3 vecteurs<s16> parallèles — corps manuel
        // (généré aplati, cf. note d'en-tête).
        if (prodBags.Count > 0)
        {
            await session.SendAsync(new CNC_CZ_ClientLoadCubeItems
            {
                ProdBags = prodBags.ToArray(),
                ProdSlots = prodSlots.ToArray(),
                ProdQtys = prodQtys.ToArray(),
            }, ct);
        }
    }

    /// <summary>Broadcast PlayerAction aux voyeurs — acteur exclu
    /// (BroadCastToCanSee natif, même forme que le renfort).</summary>
    private async Task BroadcastCubeActionAsync(EntryState st, byte act,
        CancellationToken token)
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientPlayerAction
        {
            Field1 = st.CharId,
            Field2 = act,
            Field3 = st.Facing,
        });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != st.NodeId || !ost.Migrated ||
                ost.InstanceGroup != st.InstanceGroup ||
                !ost.Sent.ContainsKey((uint)st.CharId)) continue;
            await SendPreparedToAsync(s, op, payload, token);
        }
    }
}
