using Eden.Core.Game.Inventory;
using Eden.Core.Game;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// NCID 253 PayEventSelect — échanges « pay » (items de quête contre
/// récompenses), fenêtre PaySystemWnd côté client.
/// Preuve : <c>CNE_CZ_ZoneServerPayEventSelect::Execute</c> zone
/// @0x0826e530 → <c>CPayManager::TriggerPayEvent</c> @0x080eb510.
/// Wire entrant : [u32 event_id][s16 choose] (6 octets — Deserialize
/// @0x0826e6b0). Réponse : 604 CNC_CZ_ClientPayEventOK (corps vide)
/// + texte 316 (0x1e66 succès / 0x1e67 conditions / 0x1e65 award ou
/// choix ; 0x4f9 sac plein et 0x6e2 choix requis émis en amont par
/// les checks eux-mêmes). Ordre natif : échec → texte puis 604 ;
/// succès → 604 puis texte.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>253 — TriggerPayEvent complet : rate-limit 1 s
    /// (char+0x650 µs natif), lookup def+cmd, CheckCondition,
    /// CheckAward, CheckChooseItem, consommation des 4 items de coût,
    /// ExecAwardCmd, 604 + texte.</summary>
    private async Task HandlePayEventSelectAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 6) return;
        var eventId = BitConverter.ToInt32(payload, 0);   // u32 @+0x14
        var choose = BitConverter.ToInt16(payload, 4);    // s16 @+0x18

        // Rate-limit natif : > 1 s depuis le dernier trigger
        // (timestamp µs char+0x650/0x654) — sinon return silencieux.
        if (DateTime.UtcNow - st.LastPayEventAt
                < TimeSpan.FromSeconds(1))
            return;
        st.LastPayEventAt = DateTime.UtcNow;

        // Gate natif `event_id > taille map` + def/cmd NULL → return.
        if (eventId <= 0
            || !_services.GameData.PayEvents.TryGetValue(eventId,
                out var entry))
            return;
        var (def, cmd) = entry;

        // CheckCondition — texte 0x1e67 + 604 en échec.
        if (!await CheckPayEventConditionAsync(st, cmd))
        {
            await SendTextIndexAsync(session, 0x1e67, token);
            await session.SendAsync(new CNC_CZ_ClientPayEventOK(),
                token);
            return;
        }

        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);

        // CheckAward + CheckChooseItem — texte 0x1e65 + 604 en échec
        // (0x4f9/0x6e2 déjà poussés par les checks).
        if (!await CheckPayEventAwardAsync(session, st, cmd, items,
                token)
            || !await CheckPayEventChooseAsync(session, st, cmd,
                choose, items, token))
        {
            await SendTextIndexAsync(session, 0x1e65, token);
            await session.SendAsync(new CNC_CZ_ClientPayEventOK(),
                token);
            return;
        }

        // CheckItemNum natif sur chaque coût non nul — manquant →
        // return silencieux (aucun paquet).
        foreach (var cost in def.CostItems)
        {
            if (cost.ItemId == 0 || cost.Count == 0) continue;
            var have = items.Where(i =>
                    i.ItemId == cost.ItemId && !i.IsEquipped)
                .Sum(i => i.Quantity);
            if (have < cost.Count) return;
        }

        // DropItemByItemID(char,0,id,count,0x49) — consommation
        // multi-stacks + 275 par slot touché ; échec → return.
        foreach (var cost in def.CostItems)
        {
            if (cost.ItemId == 0 || cost.Count == 0) continue;
            if (!await DropPayEventCostAsync(session, st, items,
                    cost.ItemId, cost.Count, token))
                return;
        }

        await ExecPayEventAwardAsync(session, st, cmd, choose, token);
        await session.SendAsync(new CNC_CZ_ClientPayEventOK(), token);
        await SendTextIndexAsync(session, 0x1e66, token);
    }

    /// <summary>CheckCondition natif : plages de niveau perso
    /// (vecteur +0x0c, 0=non borné, 0/0=ko), conditions de niveau de
    /// classe (vecteur +0x00 : classId == classe courante + comparaison
    /// char+0x470), plages de prestige (vecteur +0x18 :
    /// GetPrestigeExp dans [lo,hi]).</summary>
    private async Task<bool> CheckPayEventConditionAsync(EntryState st,
        PayEventCmd cmd)
    {
        foreach (var c in cmd.LevelConds)
            if (!PayEventInRange(st.Level, c.Min, c.Max)) return false;

        foreach (var c in cmd.ClassLevelConds)
        {
            if (c.ClassId != st.ClassId) return false;
            var ok = c.Op switch
            {
                1 => st.ClassLevel >= c.Level,   // ">="
                2 => st.ClassLevel == c.Level,   // "="
                3 => st.ClassLevel <= c.Level,   // "<="
                _ => true,                       // op 0 = toujours vrai
            };
            if (!ok) return false;
        }

        foreach (var c in cmd.RepConds)
        {
            var exp = await _services.PayEventRepo
                .GetPrestigeExpAsync((int)st.CharId, c.RepId);
            if (!PayEventInRange(exp, c.Lo, c.Hi)) return false;
        }
        return true;
    }

    /// <summary>Plage native [lo,hi] avec 0=non borné ; le couple
    /// (0,0) échoue toujours (cas dégénéré du désasm
    /// CheckCondition).</summary>
    private static bool PayEventInRange(long val, long lo, long hi)
    {
        if (lo == 0 && hi == 0) return false;
        if (lo > 0 && val < lo) return false;
        if (hi > 0 && val > hi) return false;
        return true;
    }

    /// <summary>CheckAward natif : pour chaque GiveItem (id≠0, scan
    /// stoppé au premier 0, def inconnue ignorée) — non-stackable
    /// (SItem+0x76&lt;2) → +1 slot requis ; stackable sans stack
    /// pouvant absorber → +1. Total > GetEmptySlotNum → 0x4f9+false.</summary>
    private async Task<bool> CheckPayEventAwardAsync(
        NetSession session, EntryState st, PayEventCmd cmd,
        IReadOnlyList<ItemEntity> items, CancellationToken token)
    {
        var needed = 0;
        foreach (var (itemId, count) in cmd.Items)
        {
            if (itemId == 0) break;                    // id 0 → fin de scan
            var def = _services.GameData.Items
                .FirstOrDefault(i => i.Id == itemId);
            if (def == null) continue;                 // def absente → ignorée
            if (_services.Inventory.MaxStackOf(itemId) < 2
                || !items.Any(i => i.ItemId == itemId && !i.IsEquipped
                    && i.Quantity + count
                        <= _services.Inventory.MaxStackOf(itemId)))
                needed++;
        }
        if (needed > await EmptySlotsAsync(st, token))
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return false;
        }
        return true;
    }

    /// <summary>CheckChooseItem natif : choose==-1 → liste vide
    /// requise (sinon 0x6e2+false) ; choose ≥ taille → false ;
    /// item 0 ou def absente → true ; sinon 1 slot requis si aucun
    /// stack n'absorbe → 0x4f9+false.</summary>
    private async Task<bool> CheckPayEventChooseAsync(
        NetSession session, EntryState st, PayEventCmd cmd,
        short choose, IReadOnlyList<ItemEntity> items,
        CancellationToken token)
    {
        if (choose == -1)
        {
            if (cmd.ChooseItems.Count == 0) return true;
            await SendTextIndexAsync(session, 0x6e2, token);
            return false;
        }
        if (choose >= cmd.ChooseItems.Count) return false;
        var (itemId, count) = cmd.ChooseItems[choose];
        if (itemId == 0) return true;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == itemId);
        if (def == null) return true;
        var needed = 0;
        if (_services.Inventory.MaxStackOf(itemId) < 2
            || !items.Any(i => i.ItemId == itemId && !i.IsEquipped
                && i.Quantity + count
                    <= _services.Inventory.MaxStackOf(itemId)))
            needed = 1;
        if (needed > await EmptySlotsAsync(st, token))
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return false;
        }
        return true;
    }

    /// <summary>DropItemByItemID natif (reason 0x49) : consomme
    /// `count` sur les stacks non équipés (slots croissants), un 275
    /// [s16 sac][s16 loc][s16 n] par slot touché.</summary>
    private async Task<bool> DropPayEventCostAsync(NetSession session,
        EntryState st, IReadOnlyList<ItemEntity> items, int itemId,
        int count, CancellationToken token)
    {
        var remaining = count;
        foreach (var it in items
            .Where(i => i.ItemId == itemId && !i.IsEquipped
                && i.Quantity > 0)
            .OrderBy(i => i.BagId).ThenBy(i => i.Slot))
        {
            var take = Math.Min(remaining, it.Quantity);
            var newCount = it.Quantity - take;
            if (!await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, it.BagId, it.Slot, take))
                return false;
            // Wire natif DropItemByItemID : pile vidée → 275
            // [contType][loc][contIdx] ; pile résiduelle → 291
            // [contType][loc][newCount][maxDur][contIdx][0].
            if (newCount > 0)
                await session.SendAsync(new Eden.Core.Networking
                    .Messages.Generated.CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0, Field2 = (short)it.Slot,
                        Field3 = (short)newCount,
                        Field4 = (short)it.MaxDurability,
                        Field5 = (short)it.BagId, Field6 = 0,
                    }, token);
            else
                await session.SendAsync(new Eden.Core.Networking
                    .Messages.Generated.CNC_CZ_ClientDropItem
                    {
                        Field1 = 0, Field2 = (short)it.Slot,
                        Field3 = (short)it.BagId,
                    }, token);
            remaining -= take;
            if (remaining <= 0) break;
        }
        _services.NotifyItemChanged((int)st.CharId, itemId);
        return remaining <= 0;
    }

    /// <summary>ExecAwardCmd natif : or (taxe famille 10 % puis gain
    /// intégral), items GiveItem, item ChooseItem (index du client),
    /// CharExp → GainExp, ClassPoint → GainClassPoint+GainNp,
    /// Reputation → DBA « GainExp Prestige », GiveBuff → EffectToLife
    /// (0→-1, -1→permanent -2, sinon ×10 s), SpecialCoin → DBA
    /// « GMGainSpecialCoin ».</summary>
    private async Task ExecPayEventAwardAsync(NetSession session,
        EntryState st, PayEventCmd cmd, short choose,
        CancellationToken token)
    {
        foreach (var gold in cmd.Gold)
        {
            // FamilyLevyTaxes natif : 10 % à la trésorerie de la
            // famille (retour ignoré — le joueur garde tout), texte
            // 0x36c « taxe perçue » si taxe > 0.
            var tax = await _services.Guilds.LevyGoldTaxAsync(
                (int)st.CharId, gold);
            if (tax > 0)
                await SendTextIndexAsync(session, 0x36c, token,
                    tax.ToString());
            st.Gold = (int)await _services.Characters.AddGoldAsync(
                (int)st.CharId, gold);
            await SendAttrAsync(session, st.CharId, 0x02,
                st.Gold, 0, token);
        }

        foreach (var (itemId, count) in cmd.Items)
            if (itemId != 0 && count != 0)
                await GrantPayEventItemAsync(session, st, itemId,
                    count, token);

        // Item choisi (param_3 != -1 et liste non vide → give).
        if (choose != -1 && choose < cmd.ChooseItems.Count)
        {
            var (itemId, count) = cmd.ChooseItems[choose];
            if (itemId != 0 && count != 0)
                await GrantPayEventItemAsync(session, st, itemId,
                    count, token);
        }

        foreach (var exp in cmd.CharExp)
            if (exp > 0)
                await GrantCharExpAsync(session, st, exp);

        // GainClassPoint + GainNp même montant — GrantClassPointAsync
        // fait déjà les deux comme le natif.
        foreach (var cp in cmd.ClassPoints)
            if (cp > 0)
                await GrantClassPointAsync(session, st, cp);

        // AddPrestigeExp natif → DBA « GainExp Prestige %hd = %d »
        // (mission TC_GainPrestigeExp → persist + broadcast monde).
        // Chez nous le monde tient la table : ZW 636 gain_prestige
        // → upsert + 60 client + update_prestige retour.
        foreach (var (repId, amount) in cmd.Reputations)
            await GainPrestigeExpAsync(st, repId, amount, token);

        // EffectToLife : dur==0 → -1 s flag 0x2000 ; dur==-1 → -2
        // permanent flag 0x2020 ; sinon dur*10 s flag 0x2000.
        foreach (var (buffId, dur) in cmd.Buffs)
        {
            var rest = dur == 0 ? -1 : dur == -1 ? -2 : dur * 10;
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                buffId, rest, 1, token);
        }

        // SpecialCoin → DBA "GMGainSpecialCoin Type = %hd Money = %d"
        // (mission → special_coins + TextOutputIndex 0x1100/0x1101 +
        // broadcast monde).
        foreach (var (type, amount) in cmd.SpecialCoins)
            await _mission.SendAgentDbaCommandAsync(st.CharId,
                $"GMGainSpecialCoin Type = {type} Money = {amount}",
                token);
    }

    /// <summary>AddItemFromPaySystem natif — insertion auto-stack +
    /// notifications 291/274 par slot (merge-aware).</summary>
    private async Task GrantPayEventItemAsync(NetSession session,
        EntryState st, int itemId, int count, CancellationToken token)
    {
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, error, _, _) = await _services.Inventory.AddItemAsync(
            (int)st.CharId, itemId, count, touched: touched);
        if (!ok)
        {
            EdenLog.Write($"[Zone-Entry] PayEvent add {itemId}x{count} " +
                $"échoué : {error}");
            return;
        }
        await NotifyItemAddedAsync(session, st, itemId, touched, token);
    }
}
