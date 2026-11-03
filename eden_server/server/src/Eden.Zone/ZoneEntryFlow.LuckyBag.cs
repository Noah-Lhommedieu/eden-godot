using Eden.Core.Game.Fortune;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Combat;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  Sac chanceux (chance lucky bag) — machine à état char+0xc70..0xc81
//  native, déclenchée par l'usage d'un item sac dans [50001,51001).
//
//  Preuves (zone/decompiled.c) :
//  - Dispatch d'usage : `UseItemToLifeControl::UseItem` @0x084ec180 —
//    item type 0xc routé par plage : [49001,50001)∪[51001,53001) →
//    OpenFortuneBag (instantané, hors scope de ce fichier) ;
//    [50001,51001) → ChanceLuckyBagStart(char, bagId, false).
//  - `CItemFactory::ChanceLuckyBagStart` @0x08097420 : verrous
//    FortuneUpdating (texte 0x638) / sac déjà actif (0x1219), lookup
//    bag + item data (type 0xc), état initialisé puis 432
//    [i32 0][s16 99][u16 0][s16 -1][s16 0] + premier lancer.
//  - `CItemFactory::DoChanceLuckyBagNext` @0x08096b50 : échec
//    `random()%100+1 < min(rolls×S_Parameter452, S_Parameter453)` →
//    556 [bag][1][0][0][1] + PlayerAction 0x2d + clear ; sinon
//    PlayerAction 0x2b + tirage pondéré (GetDoubleRand(0, range) dans
//    la map cumProb) → 556 [bag][0][newFail][itemId][itemNum].
//  - `CItemFactory::ChanceLuckyBagNext` @0x08097180 : 222 ; sac inactif
//    → 434 [s16 99] ; enchant 21 déjà posé → refus silencieux ;
//    cast>0 → EffectToLife(21) + 433 (les sacs officiels ont
//    cast=0 → lancer immédiat).
//  - `CItemFactory::ChanceLuckyBagGetItem` @0x080bf220 : 221 ; ajoute
//    le lot (échec → état conservé), annonce écran si bulletin
//    (texte 0x69e %s1,%s2,%s3) via DBA BroadCastScreenMessage Type=8 +
//    HandleRAMarquee(1), puis 556 [bag][2][0][0][1] + clear total.
// -----------------------------------------------------------------------------

public sealed partial class ZoneEntryFlow
{
    /// <summary>Ouverture du sac à l'usage de l'item —
    /// <c>ChanceLuckyBagStart</c> @0x08097420 (param_3=false, le
    /// checkOnly ne sert qu'à la validation). Retourne false sur tout
    /// refus — l'item sac n'est alors pas consommé (CheckExecute natif
    /// @0x084ec520 : validation AVANT consommation).</summary>
    private async Task<bool> StartLuckyBagAsync(NetSession session,
        EntryState st, int bagId, CancellationToken token)
    {
        if (_services.FortuneBags.FortuneUpdating)
        {
            // Verrou de rechargement — texte natif 0x638.
            await SendTextIndexAsync(session, 0x638, token);
            return false;
        }
        if (st.LuckyBagId != 0)
        {
            // Un sac est déjà actif — texte natif 0x1219.
            await SendTextIndexAsync(session, 0x1219, token);
            return false;
        }
        var bag = _services.FortuneBags.Find(bagId);
        // Lookup +0x124 natif puis CItemData du sac (type 0xc requis —
        // ici : la déf existe dans les tables chargées).
        if (bag == null ||
            _services.GameData.Items.All(i => i.Id != bagId))
        {
            EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
                $"sac={bagId} invalide — ignoré");
            return false;
        }
        // État initialisé (char+0xc70..0xc7c) puis EnterStrengthen
        // 432 [i32 0][s16 99][u16 0][s16 -1][s16 0].
        st.LuckyBagId = bagId;
        st.LuckyBagPendingItemId = 0;
        st.LuckyBagPendingCount = 0;
        st.LuckyBagRolls = 0;
        st.LuckyBagBulletin = false;
        st.LuckyBagBlocked = false;
        await session.SendAsync(new CNC_CZ_ClientEnterStrengthen
        {
            Field1 = 0,                 // i32 : réservé natif
            Field2 = 99,                // s16 : marqueur lucky bag
            Field3 = 0,                 // u16
            Field4 = -1,                // s16 : -1 natif
            Field5 = 0,                 // s16
        }, token);
        EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
            $"START sac={bagId}");
        // Premier lancer immédiat (DoChanceLuckyBagNext dans le corps
        // de Start natif).
        await DoLuckyBagRollAsync(session, st, token);
        return true;
    }

    /// <summary>222 ChanceLuckyBagReTry — <c>ChanceLuckyBagNext</c>
    /// @0x08097180 : relance le dé sur le sac actif.</summary>
    private async Task HandleLuckyBagRetryAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.LuckyBagId == 0)
        {
            // Pas de sac actif → StrengthenItemCancel 434 [s16 99].
            await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
            { Value = 99 }, token);
            return;
        }
        if (_services.FortuneBags.Find(st.LuckyBagId) == null)
            return;                                     // log natif seul
        // Enchant 21 (ChanceLuckyBagID) déjà posé → refus silencieux
        // (un cast est en cours côté natif). Nos sacs ont cast=0 —
        // l'état LuckyBagBlocked couvre l'interruption équivalente.
        if (st.LuckyBagBlocked) return;
        st.LuckyBagBlocked = false;                     // char+0xc81 = 0
        // CItemData+0x94 (cast du sac) = 0 dans les données officielles
        // → DoChanceLuckyBagNext direct (le chemin enchanté émettrait
        // EffectToLife(21) + 433 [99][cast][0]).
        await DoLuckyBagRollAsync(session, st, token);
    }

    /// <summary>221 ChanceLuckyBagGetReward —
    /// <c>ChanceLuckyBagGetItem</c> @0x080bf220 : donne le lot en
    /// attente puis clôt la machine.</summary>
    private async Task HandleLuckyBagGetRewardAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.LuckyBagPendingItemId == 0)
            return;                                     // char+0xc74 nul
        var itemId = st.LuckyBagPendingItemId;
        var count = st.LuckyBagPendingCount;
        // AddItemToCharacter natif : échec (sac plein) → état conservé,
        // retry possible ; texte 0x4f9 comme le callback OnInventoryFull.
        var need = StacksNeeded(itemId, count);
        if (await EmptySlotsAsync(st, token) < need)
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return;
        }
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var res = await _services.Inventory.AddItemAsync(
            (int)st.CharId, itemId, count, isBound: false,
            touched: touched);
        if (!res.Success)
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return;
        }
        await NotifyItemAddedAsync(session, st, itemId, touched, token);
        if (st.LuckyBagBulletin)
        {
            await BroadcastFortunePrizeAsync(st, st.LuckyBagId,
                itemId, token);
            // HandleRAMarquee(char, 1) natif : +1 sur les RA
            // catégorie 0xe/param 1 (S_RecommendActivity id 175).
            await BumpRecommendMarqueeAsync(session, st, token);
        }
        EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
            $"REWARD {itemId}x{count} sac={st.LuckyBagId}");
        // 556 terminal : [bagId][2][0][0][1].
        await SendLuckyBagResultAsync(session, st.LuckyBagId, 2, 0, 0,
            1, token);
        ClearLuckyBag(st);
    }

    /// <summary>Cœur du lancer — <c>DoChanceLuckyBagNext</c>
    /// @0x08096b50 : jet d'échec cumulé puis tirage pondéré.</summary>
    private async Task DoLuckyBagRollAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.LuckyBagBlocked || st.LuckyBagId == 0) return;
        var bag = _services.FortuneBags.Find(st.LuckyBagId);
        var set = bag?.FirstSet;
        if (bag == null || set == null || set.Prizes.Count == 0)
        {
            // "Invalid fortune bag" — le natif log + return 0.
            EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
                $"sac={st.LuckyBagId} sans lots — état figé");
            return;
        }
        var p = NativeCombatFormulas.ResolveParameters(
            _services.GameData.Parameters);
        var failChance = Math.Min(
            (int)(st.LuckyBagRolls * p.LuckyBagFailStep),
            (int)p.LuckyBagFailCap);
        // random() % 100 + 1 < failChance → échec natif.
        if (Random.Shared.Next(1, 101) < failChance)
        {
            EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
                $"FAIL sac={st.LuckyBagId} chance={failChance}");
            await SendLuckyBagResultAsync(session, st.LuckyBagId, 1, 0,
                0, 1, token);
            ClearLuckyBag(st);
            await SendPlayerActionAsync(session, st, 0x2d, 0, token);
            return;
        }
        await SendPlayerActionAsync(session, st, 0x2b, 0, token);
        // GetDoubleRand(0, diceRange) → lower_bound sur cumProb.
        var dice = (float)(Random.Shared.NextDouble() * set.DiceRange);
        var prize = bag.Roll(set, dice);
        if (prize == null)
        {
            EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
                $"dice={dice:F2} hors map — roll annulé");
            return;
        }
        // Le lot doit exister dans les tables d'items (lookup +0x10c
        // natif — "Invalid item" sinon, état conservé).
        if (_services.GameData.Items.All(i => i.Id != prize.ItemId))
        {
            EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
                $"lot={prize.ItemId} hors tables — roll annulé");
            return;
        }
        st.LuckyBagPendingItemId = prize.ItemId;
        st.LuckyBagPendingCount = prize.ItemNum;
        st.LuckyBagBulletin = prize.Bulletin != 0;
        st.LuckyBagRolls++;
        var newFail = Math.Min(
            (int)(st.LuckyBagRolls * p.LuckyBagFailStep),
            (int)p.LuckyBagFailCap);
        EdenLog.Write($"[Zone] LuckyBag char={st.CharId} " +
            $"dice={dice:F2} lot={prize.ItemId}x{prize.ItemNum} " +
            $"chance={newFail}");
        // 556 succès : [bagId][0][newFailChance][itemId][itemNum].
        await SendLuckyBagResultAsync(session, st.LuckyBagId, 0,
            newFail, prize.ItemId, prize.ItemNum, token);
    }

    /// <summary>556 CNC_CZ_ClientChanceLuckyBagResult — layout natif
    /// [u16 bagId][s16 code][u16 f3][u16 f4][u16 f5] : code 0 = lot
    /// tiré (f3=nouveau risque d'échec, f4=item, f5=qté), 1 = échec
    /// terminal, 2 = lot récupéré.</summary>
    private static async Task SendLuckyBagResultAsync(
        NetSession session, int bagId, int code, int failChance,
        int itemId, int itemNum, CancellationToken token)
    {
        await session.SendAsync(new CNC_CZ_ClientChanceLuckyBagResult
        {
            Field1 = (ushort)bagId,             // u16 : sac
            Field2 = (short)code,               // s16 : 0/1/2
            Field3 = (ushort)failChance,        // u16 : risque cumulé
            Field4 = (ushort)itemId,            // u16 : item gagné
            Field5 = (ushort)itemNum,           // u16 : quantité
        }, token);
    }

    /// <summary>Remise à zéro de la machine (fin de
    /// DoChanceLuckyBagNext échec / ChanceLuckyBagGetItem succès —
    /// char+0xc70..0xc81).</summary>
    private static void ClearLuckyBag(EntryState st)
    {
        st.LuckyBagId = 0;
        st.LuckyBagPendingItemId = 0;
        st.LuckyBagPendingCount = 0;
        st.LuckyBagRolls = 0;
        st.LuckyBagBulletin = false;
        st.LuckyBagBlocked = false;
    }

    /// <summary>Annonce écran du lot (bulletin) : texte 0x69e
    /// « Congratulations! %s1 has used the %s2 and acquired the %s3. »
    /// — %s1 joueur, %s2 nom du sac, %s3 nom du lot
    /// (ReplaceOrderParm "%s,%s,%s" natif), envoyé par DBA
    /// « BroadCastScreenMessage Type= 8 Msg= … ».</summary>
    private async Task BroadcastFortunePrizeAsync(EntryState st,
        int bagId, int prizeItemId, CancellationToken token)
    {
        _services.GameData.Texts.TryGetValue(0x69e, out var tpl);
        var bagName = _services.GameData.Items
            .FirstOrDefault(i => i.Id == bagId)?.Name
            ?? bagId.ToString();
        var prizeName = _services.GameData.Items
            .FirstOrDefault(i => i.Id == prizeItemId)?.Name
            ?? prizeItemId.ToString();
        var msg = (tpl ?? "%s1 %s2 %s3")
            .Replace("%s1", st.CharName ?? "?", StringComparison.Ordinal)
            .Replace("%s2", bagName, StringComparison.Ordinal)
            .Replace("%s3", prizeName, StringComparison.Ordinal);
        await _mission.SendAgentDbaCommandAsync(st.CharId,
            $"BroadCastScreenMessage Type= 8 Msg= {msg}", token);
    }

    /// <summary><c>CCharacter::HandleRAMarquee(1)</c> @0x082aa4f0 :
    /// pour chaque activité recommandée chargée (map char+0xc90) dont
    /// la catégorie vaut 0xe et le paramètre 1, +1 progression puis
    /// FinishedProcess (560 + flag pending si cible atteinte).</summary>
    private async Task BumpRecommendMarqueeAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        foreach (var (id, progress) in st.RecommendProgress.ToList())
        {
            var def = _services.GameData.GetRecommendActivity(id);
            if (def is not { EventKind: 0xe, EventParam: 1 }) continue;
            if (progress >= def.Target) continue;
            var next = progress + 1;
            st.RecommendProgress[id] = next;
            // Même flush que la poussee mission 832 : cible atteinte →
            // pending + DBA « Update RecommendActivity … Finished = 0 ».
            if (next < (uint)def.Target ||
                st.RecommendFinished.ContainsKey(id))
            {
                continue;
            }
            st.RecommendFinished[id] = 0;
            if (_services.Mission is { } ml)
                await ml.SendAgentDbaCommandAsync(st.CharId,
                    $"Update RecommendActivity ActivityID = {id} " +
                    $"Parameter = {next} Finished = 0", token);
            // 560 ClientUpdateRecommendActivities [u16 id][i32 n][u16 0]
            await session.SendAsync(
                new CNC_CZ_ClientUpdateRecommendActivities
                { Field1 = id, Field2 = next, Field3 = 0 }, token);
        }
    }
}
