using Eden.Core.Game.Fortune;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  Sacs de fortune instantanés — `CItemFactory::OpenFortuneBag`
//  zone @0x080bb300, déclenché par l'usage d'un item sac dans
//  [49001,50001) ∪ [51001,53001) (dispatch type 0xc de
//  UseItemToLifeControl::UseItem @0x084ec180).
//
//  Flux natif :
//  - FortuneUpdating → texte 0x638 ; sac inconnu → log seul ;
//    slots vides (tous sacs) < bagdef+0x1c (nb de sets) → 0x4f9.
//  - Compteur char+0x574[bag] (player_fortune_bag par account_id) —
//    créé à 0 seulement si item_counter ≥ 1.
//  - Pour CHAQUE set (ordre croissant) :
//      pity = item_counter≥1 && compteur≥seuil && !bulletinDrawn →
//        tirage pondéré sur les lots bulletin==1 (poids = cumH−cumB) ;
//        liste vide → tirage normal ; map vide au tirage → abort.
//      sinon dé ∈ [0,range) (set==99 → [0,100)) → upper_bound
//        (1ère clé > dé) ; aucune clé : set≠99 → abort, 99 → sauté.
//      octroi : merge stack / nouveau slot (AddItemAsync) ; bind =
//        sac source lié || lot.bind>0 ; texte 0x6b1 « nom x qté » ;
//      bulletin → annonce écran 0x69e (DBA BroadCastScreenMessage
//        Type= 8) + HandleRAMarquee(1) ; bulletinDrawn = true.
//  - Fin : item_counter≥1 → compteur = bulletinDrawn ? 0 : +1 ;
//    char+0x574 mis à jour ; DBA « FortuneBag Item= %hd Counter= %hd »
//    (mission : UPSERT player_fortune_bag) ; 557 [u16 1][bag][n].
//  - 223 QueryFortuneBagCounter @0x08277f00 → 557 avec toutes les
//    entrées de char+0x574 (ordre de la map = croissant).
// -----------------------------------------------------------------------------

public sealed partial class ZoneEntryFlow
{
    /// <summary>Ouverture instantanée — le corps complet de
    /// <c>OpenFortuneBag(char, bagId, checkOnly=false, srcItem)</c> :
    /// un tirage par set, octroi immédiat, compteur pity.</summary>
    private async Task<bool> OpenFortuneBagAsync(NetSession session,
        EntryState st, int bagId, bool srcBound,
        CancellationToken token)
    {
        if (_services.FortuneBags.FortuneUpdating)
        {
            // Rechargement des tables — texte natif 0x638.
            await SendTextIndexAsync(session, 0x638, token);
            return false;
        }
        var bag = _services.FortuneBags.Find(bagId);
        if (bag == null)
        {
            // « Invalid fortune bag [%d] » — log natif, aucun paquet.
            EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                $"sac={bagId} invalide — ignoré");
            return false;
        }
        // bagdef+0x1c natif = nb de sets = slots vides requis — check
        // AVANT consommation (CheckExecute @0x084ec520, texte 0x4f9).
        var free = await EmptySlotsAsync(st, token);
        if (free < bag.RequiredSlots)
        {
            EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                $"sac={bagId} refus : {free} libres < " +
                $"{bag.RequiredSlots} requis");
            await SendTextIndexAsync(session, 0x4f9, token);
            return false;
        }
        // char+0x574 : l'entrée n'existe que si le sac a un seuil
        // pity (item_counter ≥ 1 — `sVar10 < 1` court-circuite tout).
        short counter = 0;
        if (bag.ItemCounter >= 1 &&
            !st.FortuneCounters.TryGetValue((ushort)bagId, out counter))
        {
            counter = 0;
            st.FortuneCounters[(ushort)bagId] = 0;
        }
        var bulletinDrawn = false;
        foreach (var set in bag.Sets.Values
                     .OrderBy(s => s.SetId))
        {
            var prize = TryFortunePityRoll(bag, set, counter,
                bulletinDrawn, out var pityFailed);
            if (pityFailed)
            {
                // « Invalid in OpenFortuneBag » — map pity non vide
                // mais aucune clé > dé (poids nuls) : abort natif.
                EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                    $"sac={bagId} set={set.SetId} pity sans clé — " +
                    $"abort");
                return true;
            }
            prize ??= FortuneNormalRoll(bag, set);
            if (prize == null)
            {
                // Aucune clé > dé : set==99 → set sauté (les pools 99
                // n'ont pas l'obligation de sommer 100) ; sinon abort.
                if (set.SetId == 99) continue;
                EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                    $"sac={bagId} set={set.SetId} hors map — abort");
                return true;
            }
            if (!await GrantFortunePrizeAsync(session, st, bag, set,
                    prize, srcBound, token))
            {
                return true;
            }
            bulletinDrawn |= prize.Bulletin != 0;
        }
        if (bag.ItemCounter < 1) return true;
        // bVar4 natif : bulletin tiré → remise à zéro ; sinon +1.
        counter = (short)(bulletinDrawn ? 0 : counter + 1);
        st.FortuneCounters[(ushort)bagId] = counter;
        // « FortuneBag Item= %hd Counter= %hd » — la mission UPSERT
        // player_fortune_bag par account_id (TC natif @0x081f8fxx).
        if (_mission != null)
            await _mission.SendAgentDbaCommandAsync(st.CharId,
                $"FortuneBag Item= {bagId} Counter= {counter}", token);
        await SendFortuneCounterAsync(session,
            [(bagId, counter)], token);
        return true;
    }

    /// <summary>223 CNE_CZ_ZoneServerQueryFortuneBagCounter — renvoie
    /// 557 avec toutes les entrées char+0x574 (Execute zone
    /// @0x08277f00 : itération de la map = ordre croissant).</summary>
    private async Task HandleFortuneCounterQueryAsync(
        NetSession session, EntryState st, CancellationToken token)
    {
        await SendFortuneCounterAsync(session,
            st.FortuneCounters
                .OrderBy(kv => kv.Key)
                .Select(kv => ((int)kv.Key, (int)kv.Value))
                .ToList(), token);
    }

    /// <summary>557 CNC_CZ_ClientUpdateFortuneBagCounter —
    /// `[u16 n][u16 bag][u16 counter]×n` (Serialize natif
    /// @0x080c3bd0).</summary>
    private static async Task SendFortuneCounterAsync(
        NetSession session,
        IReadOnlyList<(int BagId, int Count)> entries,
        CancellationToken token)
    {
        var cmd = new CNC_CZ_ClientUpdateFortuneBagCounter();
        foreach (var (bagId, count) in entries)
        {
            cmd.Entries.Add(
                new CNC_CZ_ClientUpdateFortuneBagCounter.Entry
                {
                    // u16 fil (s16 signé — les ids sac > 32767
                    // débordent en négatif, bytes identiques).
                    BagId = unchecked((short)bagId),
                    Count = (short)count,
                });
        }
        await session.SendAsync(cmd, token);
    }

    /// <summary>Tirage pity natif : quand le compteur atteint le seuil
    /// `item_counter` et qu'aucun bulletin n'est encore sorti, les lots
    /// `bulletin==1` du set forment une map pondérée par
    /// (cum haute − cum basse = <see cref="FortunePrize.Probability"/>)
    /// — `dice ∈ [0, Σpoids)` → upper_bound. Null = retomber au tirage
    /// normal (aucun lot bulletin) ; <paramref name="pityFailed"/> =
    /// map non vide mais aucune clé > dé (abort natif).</summary>
    private static FortunePrize? TryFortunePityRoll(FortuneBagDef bag,
        FortuneSet set, short counter, bool bulletinDrawn,
        out bool pityFailed)
    {
        pityFailed = false;
        if (bag.ItemCounter < 1 || counter < bag.ItemCounter ||
            bulletinDrawn)
        {
            return null;
        }
        var weights = set.PityWeights();
        if (weights.Count == 0) return null;    // local_4c==0 → normal
        var total = weights[^1].Cum;
        // Σ=0 → dé 0 (dVar35 == _DAT_087c2728 → fVar34=0.0).
        var dice = total > 0
            ? (float)(Random.Shared.NextDouble() * total) : 0f;
        var prize = FortuneSet.PityRoll(weights, dice);
        if (prize != null) return prize;
        pityFailed = true;                      // aucune clé > dé
        return null;
    }

    /// <summary>Tirage normal natif : set==99 → dé ∈ [0,100) ;
    /// sinon dé ∈ [0,range) (range==0 → dé 0) ; upper_bound sur la
    /// map cumulée (<see cref="FortuneBagDef.Roll"/>).</summary>
    private static FortunePrize? FortuneNormalRoll(FortuneBagDef bag,
        FortuneSet set)
    {
        var dice = set.SetId == 99
            ? (float)(Random.Shared.NextDouble() * 100f)
            : set.DiceRange != 0f
                ? (float)(Random.Shared.NextDouble() * set.DiceRange)
                : 0f;
        return bag.Roll(set, dice);
    }

    /// <summary>Octroi d'un lot : merge stack / nouveau slot
    /// (<c>AddItemAsync</c> = PutIntoContainer+AddCloneItemWorld) ;
    /// bind = sac source lié (item+0x24 bit5 natif) || lot.bind&gt;0 ;
    /// texte 0x6b1 « nom x qté » puis annonce écran si bulletin.</summary>
    private async Task<bool> GrantFortunePrizeAsync(NetSession session,
        EntryState st, FortuneBagDef bag, FortuneSet set,
        FortunePrize prize, bool srcBound, CancellationToken token)
    {
        // Lot absent des tables items — « Invalid item(%d) » natif,
        // abort sans octroi.
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == prize.ItemId);
        if (def == null)
        {
            EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                $"lot={prize.ItemId} hors tables — abort");
            return false;
        }
        var bound = srcBound || prize.Bind > 0;
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var res = await _services.Inventory.AddItemAsync(
            (int)st.CharId, prize.ItemId, prize.ItemNum,
            isBound: bound, touched: touched);
        if (!res.Success)
        {
            // « InvalidSlot » / PutIntoContainer NULL — abort natif
            // (les lots déjà posés des sets précédents restent, comme
            // le natif qui grant au fil de la boucle).
            EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
                $"lot={prize.ItemId}x{prize.ItemNum} " +
                $"non placé ({res.ErrorMessage}) — abort");
            return false;
        }
        await NotifyItemAddedAsync(session, st, prize.ItemId, touched,
            token);
        // Texte 0x6b1 : la string du paquet = « %s x %d » déjà
        // formatée par le serveur natif (TextOutputIndex fmt+varargs).
        await SendTextIndexAsync(session, 0x6b1, token,
            $"{def.Name} x {prize.ItemNum}");
        EdenLog.Write($"[Zone] FortuneBag char={st.CharId} " +
            $"sac={bag.Id} set={set.SetId} → {prize.ItemId}x" +
            $"{prize.ItemNum}{(prize.Bulletin != 0 ? " bulletin" : "")}");
        if (prize.Bulletin != 0)
        {
            // prize+0xe != 0 → annonce écran (BroadCastScreenMessage
            // Type= 8 via DBA) + HandleRAMarquee(char, 1).
            await BroadcastFortunePrizeAsync(st, bag.Id, prize.ItemId,
                token);
            await BumpRecommendMarqueeAsync(session, st, token);
        }
        return true;
    }
}
