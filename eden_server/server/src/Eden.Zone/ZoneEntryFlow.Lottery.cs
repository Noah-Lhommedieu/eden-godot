using System.Buffers.Binary;
using Eden.Core.Game.Lottery;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Autels Eden Crystal / Eden Gem — portage fidèle des ops natives
/// <c>CInGameLotteryOp</c> (IsA 0x54), <c>CBaseLotteryOp</c> (0x56) et
/// de <c>CPlayerFactory::{Go,Result,Dice,Receive}*Lottery</c> +
/// <c>*ReceiveWeekCountItem</c> + <c>CharacterLotteryCounting</c> +
/// tick de décroissance (zone décompilé).
///
/// Entrées client :
///   NCID 95  MailOpenBox [s16 op]  — op 150 (0x96) → fenêtre crystal
///            in-game, op 151 (0x97) → fenêtre crystal/gem partagée.
///   NCID 129 GoLottery (14B) <code>[s16 autel][u16 item][s16 p3]
///            [s16 p4][s16 p5][u32 p6]</code> — autel 0 normal,
///            1 in-game, 2 diamond ; p3 = ticket/cristal demandé ou
///            0 = récupération du lot ; p4 != 0 = claim palier hebdo.
///   NCID 184 CheckUpdateLottery <code>[u16][u16 version][s16 autel]
///            </code> → 511 si la version diffère.
/// Sorties : 432 fenêtre, 433 cast, 434 cancel, 448 état/résultat,
/// 449 fin de récupération, 450 mise à jour du plus, 511 config,
/// 316 textes, 274/275 items, 474 annonce (via mission DBA 21 →
/// <c>CNC_MW_WorldTextCommand</c> 20).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Durée du cast : <c>EffectToLife(*LotteryEffectID,10)</c>
    /// → 10 × 100 ms affiché tel quel dans le 433.</summary>
    private const int LotteryCastMs = 1000;

    // ============================================================
    //  NCID 95 — MailOpenBox ops 150/151
    //  (CNE_CZ_ZoneServerMailOpenBox::Execute @0x8265510) :
    //    0x96 → CInGameLotteryOp(+4=1) + 432 [0][0x96][0][-1][0]
    //    0x97 → CBaseLotteryOp(+4=2)  + 432 [0][0x97][0][2][switch]
    //  Le natif n'émet AUCUN état à l'ouverture — le client demande
    //  via 129/184. Les autres ops (7/22/152) restent au dispatcher
    //  legacy (return false).
    // ============================================================

    /// <summary>Ouvre la fenêtre autel pour un op MailOpenBox —
    /// retourne false pour les ops non lottery (7/22/152…).</summary>
    private async Task<bool> TryHandleLotteryOpenAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2) return false;
        var op = BitConverter.ToInt16(payload, 0);
        if (op is not (150 or 151)) return false;
        if (st.CharId == 0) return false;

        bool inGame = op == 150;
        // CGlobalInfo::InGameLotterySwitch — table introuvable =
        // service coupé → texte 0x1c5c natif, pas de fenêtre.
        var altar = inGame ? AltarKind.InGame : AltarKind.Normal;
        if (inGame && _services.Lottery.Table(altar) == null)
        {
            await SendTextIndexAsync(session, 0x1c5c, token);
            return true;
        }

        st.ShopOpType = op;                     // code fenêtre (close 120)
        st.LotteryOp = inGame ? 0x54 : 0x56;    // type IsA de l'op natif
        await session.SendAsync(new CNC_CZ_ClientEnterStrengthen
        {
            Field1 = 0,                        // i32 — npcUid (0 = UI)
            Field2 = op,                       // s16 — code fenêtre
            Field3 = 0,                        // u16 — 0
            Field4 = (short)(inGame ? -1 : 2), // s16 — sous-type op
                                               // (-1 ingame / 2 base)
            Field5 = (short)(inGame ? 0        // s16 — DiamondLotterySwitch
                : _services.Lottery.Table(AltarKind.Diamond) != null
                    ? 1 : 0),
        }, token);
        return true;
    }

    // ============================================================
    //  NCID 129 — CNE_CZ_ZoneServerGoLottery::Execute @0x825d8c0
    //  Dispatch autel : 0 → GoLottery, 1 → GoInGameLottery,
    //  2 → GoDiamondLottery. Chacun exige l'op ouverte (IsA 0x54/0x56)
    //  et un flag « going » libre (op+8 == 0).
    // ============================================================
    private async Task HandleGoLotteryAsync(NetSession session,
        EntryState st, byte[] p, CancellationToken token)
    {
        if (p.Length < 14 || st.CharId == 0) return;
        short altarRaw = BitConverter.ToInt16(p, 0);   // s16 — autel 0/1/2
        ushort item = BitConverter.ToUInt16(p, 2);     // u16 — item requête
        short req = BitConverter.ToInt16(p, 4);        // s16 — ticket/crystal ou 0
        short weekClaim = BitConverter.ToInt16(p, 6);  // s16 — claim palier
        short slot = BitConverter.ToInt16(p, 8);       // s16 — slot input
        uint bag = BitConverter.ToUInt32(p, 10);       // u32 — sac input
        if (altarRaw is < 0 or > 2) return;
        var altar = (AltarKind)altarRaw;
        var cfg = LotteryService.Config(altar);
        EdenLog.Write($"[Zone] GoLottery char={st.CharId} altar={altarRaw} " +
            $"item={item} req={req} claim={weekClaim} " +
            $"op=0x{st.LotteryOp:x} pending={(st.PendingLottery != null)}");

        // IsA natif : l'op ouverte doit être du type requis.
        if (st.LotteryOp != cfg.RequiredOp) return;
        // Flag « going » (op+8) — un cast en vol bloque tout.
        if (st.PendingLottery != null) return;

        var s = StateOf(st, altar);
        // Décroissance paresseuse du plus (tick natif, appliquée à la
        // première interaction — même observable qu'un timer).
        await ApplyLotteryDecayAsync(session, st, altar, token);

        // Dispatch — deux layouts wire observés/prouvés :
        //   A) server_us (Deserialize @0x825db00) : la requête de
        //      paiement est en param_3 (+0x14, « req ») et le claim
        //      hebdo en param_4 (+0x16) — testé AVANT param_3.
        //   B) clients réels client_game+rf_game (FUN_006cdbe0 :
        //      objet sérialisé dans l'ordre +0x10,+4,+6,+8,+10,+0xc)
        //      → wire = [altar][request][0][slot][bag][0] ; la
        //      requête arrive donc en « item » (+0x12) et le slot
        //      d'inventaire (-1 = auto) en « claim » (+0x16).
        // Un claim ≠0 ET ≠-1 = vrai palier hebdo ; -1 = sentinelle
        // slot du client B, jamais un claim. GoInGameLottery ne lit
        // jamais param_4 (WeekColumn<0) → pas d'interception.
        if (cfg.WeekColumn >= 0 && weekClaim != 0 && weekClaim != -1)
        {
            await ReceiveWeekCountAsync(session, st, cfg, token);
            return;
        }
        // Requête effective : « req » (layout A) sinon « item »
        // (layout B — le client réel y place ticket 0x6c91/cristaux).
        var request = req != 0 ? req : (short)item;
        if (request == 0)
        {
            await ReceiveItemAsync(session, st, cfg, token);
            return;
        }

        if (s.Counts == 0)
        {
            // — Nouveau tirage : sélection du paiement —
            if (!s.CanGoNext) return;
            if (request == cfg.TicketRequestId)
            {
                // CheckItemNum natif dans l'ordre de la table
                // (premium type3 en tête) → le 1er ticket possédé.
                int ticket = await FirstOwnedTicketAsync(st, cfg, token);
                if (ticket == 0) return;
                s.InputItemId = (short)ticket;
                s.LotteryType = (short)(ticket == cfg.PremiumTicketId ? 3 : 0);
            }
            else if (request == cfg.CrystalBId)
            {
                if (s.CrystalB < 10) return;
                s.InputItemId = (short)cfg.CrystalBId;
            }
            else if (cfg.CrystalCId != 0 && request == cfg.CrystalCId)
            {
                if (s.CrystalC < 10) return;
                s.InputItemId = (short)cfg.CrystalCId;
            }
            else return;

            // Reset natif Go*Lottery : lot/stage/reset/event/can_go_next
            // (cristaux et extra préservés).
            s.ItemId = 0; s.ItemAmount = 0;
            s.EventFlag = 0; s.ResetBonus = false; s.CanGoNext = true;
            s.WonBulletin = false;
            s.InputSlot = slot; s.InputBag = (short)bag;
            await ArmLotteryCastAsync(session, st, cfg, token);
            return;
        }

        // — Continuation : le tirage précédent doit autoriser l'étape —
        if (!s.CanGoNext) return;
        if (s.Counts >= cfg.MaxStage)
        {
            // counts >= MaxStage + requête != claim : le natif purge
            // l'item en attente et supprime l'état (GoLottery
            // `(counts-1) < 6` else → item=0 + Delete).
            if (s.ItemId != 0)
            {
                s.ItemId = 0;
                await _services.LotteryRepo.DeleteStateAsync(
                    (int)st.CharId, altar);
                s.Persisted = false;
            }
            return;
        }
        // Natif : continuation exige le lot en attente (+0x932 != 0) —
        // le wire B met la request en « item », pas le lot courant.
        if (s.ItemId == 0) return;
        await ArmLotteryCastAsync(session, st, cfg, token);
    }

    /// <summary>Premier ticket d'autel possédé — ordre
    /// <see cref="AltarConfig.TicketIds"/> = l'ordre des CheckItemNum
    /// natifs (premium en tête).</summary>
    private async Task<int> FirstOwnedTicketAsync(EntryState st,
        AltarConfig cfg, CancellationToken token)
    {
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        foreach (int ticket in cfg.TicketIds)
            if (items.Any(i => i.ItemId == ticket && !i.IsEquipped
                               && i.Quantity > 0))
                return ticket;
        return 0;
    }

    // ============================================================
    //  Cast — ClientStrengthenCasting 433 + enchant *LotteryEffectID
    //  (10 × 100 ms). Résolution différée comme ArmLifeSkillCastAsync.
    // ============================================================
    private async Task ArmLotteryCastAsync(NetSession session,
        EntryState st, AltarConfig cfg, CancellationToken token)
    {
        var pending = new PendingLottery { Altar = cfg.Altar };
        st.PendingLottery = pending;            // op+8 « going » natif
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        {
            Field1 = cfg.CastWindow,   // s16 — 0x96 ingame / 0x97 base
            Field2 = 10,               // s16 — durée ×100 ms
            Field3 = cfg.CastFlag,     // s16 — 2 diamond, 0 sinon
        }, token);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        pending.Cts = cts;
        ScheduleOnLoop(TimeSpan.FromMilliseconds(LotteryCastMs),
            _ => ResolveLotteryAfterCastAsync(session, st, pending,
                cts.Token), cts.Token);
    }

    /// <summary>Fin de cast → *LotteryResult natif : consomme le
    /// paiement, stage++ puis DiceLottery (roll + 448 + persistance).</summary>
    private async Task ResolveLotteryAfterCastAsync(NetSession session,
        EntryState st, PendingLottery p,
        CancellationToken ct)
    {
        try
        {
            if (st.PendingLottery != p) return;
            st.PendingLottery = null;
            var cfg = LotteryService.Config(p.Altar);
            if (st.LotteryOp != cfg.RequiredOp) return;
            var s = StateOf(st, p.Altar);
            if (s.Counts >= cfg.MaxStage) return;

            if (s.Counts == 0)
            {
                // Consommation natif *LotteryResult :
                //   ticket   → DropItemByItemID(id,1)
                //   crystalB → crystal_b -= 10 → stage 2
                //   crystalC → crystal_c -= 10 → stage 4
                if (s.InputItemId == cfg.CrystalBId)
                {
                    if (s.CrystalB < 10) return;
                    s.CrystalB -= 10;
                    s.Counts = 1;                 // +1 ci-dessous → 2
                }
                else if (cfg.CrystalCId != 0
                         && s.InputItemId == cfg.CrystalCId)
                {
                    if (s.CrystalC < 10) return;
                    s.CrystalC -= 10;
                    s.Counts = 3;                 // +1 ci-dessous → 4
                }
                else
                {
                    if (!await DropItemsByItemIdAsync(session, st,
                            s.InputItemId, 1, ct))
                        return;
                    s.Counts = 0;
                }
                s.InputItemId = 0;
                s.InputSlot = -1; s.InputBag = -1;
            }

            // Transition natif : lot précédent effacé, stage++ avant roll.
            s.ItemId = 0; s.ItemAmount = 0;
            s.Counts++;
            await DiceLotteryAsync(session, st, cfg, ct);
        }
        catch (OperationCanceledException) { /* StrengthenClose */ }
        catch (Exception ex)
        { EdenLog.Write($"[Zone] LotteryResult: {ex.Message}"); }
    }

    // ============================================================
    //  Tirage — *DiceLottery : rand % Σpoids cumulés, win/lose,
    //  annonce, plus, persistance, 448.
    // ============================================================
    private async Task DiceLotteryAsync(NetSession session,
        EntryState st, AltarConfig cfg, CancellationToken token)
    {
        var s = StateOf(st, cfg.Altar);
        var entry = _services.Lottery.Roll(cfg.Altar, s.Counts, out _);

        if (entry != null)
        {
            s.WonBulletin = entry.Bulletin;
            if (entry.Bulletin)
                await BroadcastLotteryAsync(st, cfg, entry, token);
            // Bonus exact : nouvelle hauteur de stage → nouvel item
            // bonus (RefreshBonusAt natif + UpdateBonus DBO).
            if (_services.Lottery.RefreshBonusAt(cfg.Altar, s, s.Counts))
                await _services.LotteryRepo.UpdateBonusAsync(
                    (int)st.CharId, cfg.Altar, s);
            s.Plus = LotteryService.ClampPlus(
                s.Plus + (s.LotteryType == 1 ? 1 : 10));
            s.ItemId = (short)entry.ItemId;
            s.ItemAmount = (short)entry.ItemAmount;
            s.ResetBonus = entry.ResetBonus;
            s.CanGoNext = !entry.CantGoNext;

            if (!s.Persisted || s.Counts < 2)
                await _services.LotteryRepo.InsertStateAsync(
                    (int)st.CharId, cfg.Altar, s);
            else
                await _services.LotteryRepo.UpdateStateAsync(
                    (int)st.CharId, cfg.Altar, s);
            await _services.LotteryRepo.UpdatePlusAsync(
                (int)st.CharId, cfg.Altar, s);
        }
        else
        {
            // Lose natif (round absent de la semaine) : +2/+20 pity,
            // reset des champs courants — cristaux et extra conservés
            // en mémoire — puis DELETE + UpdatePlus.
            s.Plus = LotteryService.ClampPlus(
                s.Plus + (s.LotteryType == 1 ? 2 : 20));
            s.LotteryType = 0; s.ItemId = 0; s.ItemAmount = 0;
            s.Counts = 0; s.InputItemId = 0;
            s.InputSlot = -1; s.InputBag = -1;
            s.EventFlag = 0; s.WonBulletin = false;
            s.ResetBonus = false; s.CanGoNext = true;
            await _services.LotteryRepo.DeleteStateAsync(
                (int)st.CharId, cfg.Altar);
            s.Persisted = false;
        }
        await SendLotteryResultAsync(session, st, cfg.Altar, token);
    }

    // ============================================================
    //  Réception du lot — *LotteryReceiveItem : multiplicateur du
    //  bonus exact, cristaux ou item lié, points hebdo, 449, reset.
    // ============================================================
    private async Task ReceiveItemAsync(NetSession session,
        EntryState st, AltarConfig cfg, CancellationToken token)
    {
        var s = StateOf(st, cfg.Altar);
        if (s.ItemId == 0) return;

        // Multiplicateur natif : counts == extra_counts &&
        // item == extra_item → reset_bonus=1, qty × extra_multiple.
        int mult = 1;
        if (s.Counts == s.ExtraCounts && s.ItemId == s.ExtraItemId)
        {
            s.ResetBonus = true;
            mult = s.ExtraMultiple;
        }
        int qty = s.ItemAmount * mult;

        if (s.ItemId == cfg.CrystalBId)
        {
            // Lot « cristal B » : pool += qty (cap 999 + textes).
            s.CrystalB += (short)qty;
            await SendTextIndexAsync(session, cfg.TextCrystalB,
                token, qty.ToString());
            if (s.CrystalB >= 1000) s.CrystalB = 999;
            if (s.CrystalB > 899)
                await SendTextIndexAsync(session, cfg.TextCrystalBCap,
                    token);
        }
        else if (cfg.CrystalCId != 0 && s.ItemId == cfg.CrystalCId)
        {
            // Cap-quirk natif fidèle : le texte de cap C teste
            // crystal_B (+0x8f4/+0x91c), pas C.
            s.CrystalC += (short)qty;
            await SendTextIndexAsync(session, cfg.TextCrystalC,
                token, qty.ToString());
            if (s.CrystalC > 999) s.CrystalC = 999;
            if (s.CrystalB > 899 && cfg.TextCrystalCCap != 0)
                await SendTextIndexAsync(session, cfg.TextCrystalCCap,
                    token);
        }
        else
        {
            // GetEmptySlotNum vs stacks requis — l'échec préserve
            // l'état (texte 0x4f9 « sac plein », claim plus tard).
            var need = StacksNeeded(s.ItemId, qty);
            if (await EmptySlotsAsync(st, token) < need)
            {
                await SendTextIndexAsync(session, 0x4f9, token);
                return;
            }
            // lottery_type 3 = ticket premium → items liés (natif
            // AddItemFromElf(…, bind, bind)).
            bool bind = s.LotteryType == 3;
            var touched = new List<(int BagId, int Slot, bool Merged)>();
            var res = await _services.Inventory.AddItemAsync(
                (int)st.CharId, s.ItemId, qty, isBound: bind,
                touched: touched);
            if (!res.Success)
            {
                await SendTextIndexAsync(session, 0x4f9, token);
                return;
            }
            await NotifyItemAddedAsync(session, st, s.ItemId, touched,
                token);
        }

        // CharacterLotteryCounting (dans ReceiveItem natif, pas au
        // tirage) — gain hebdo + texte 0x2324 "%d,%d" + persistance.
        await AddWeekPointsAsync(session, st, cfg, token);

        // Bonus exact réclamé → extra_* vidés (ResetBonus DBO).
        if (s.ResetBonus)
        {
            s.ExtraItemId = 0; s.ExtraCounts = 0; s.ExtraMultiple = 0;
            await _services.LotteryRepo.ResetBonusAsync(
                (int)st.CharId, cfg.Altar);
        }

        await SendLotteryEndAsync(session, st, cfg, s, st.LotteryWeek,
            cfg.ReceiveFlag, token);

        // Reset natif post-449 : champs courants + input + event ;
        // cristaux conservés ; DELETE de la ligne.
        s.LotteryType = 0; s.ItemId = 0; s.ItemAmount = 0; s.Counts = 0;
        s.InputItemId = 0; s.InputSlot = -1; s.InputBag = -1;
        s.EventFlag = 0; s.WonBulletin = false;
        s.ResetBonus = false; s.CanGoNext = true;
        await _services.LotteryRepo.DeleteStateAsync(
            (int)st.CharId, cfg.Altar);
        s.Persisted = false;
    }

    // ============================================================
    //  Paliers hebdo — *ReceiveWeekCountItem : claim SÉQUENTIEL
    //  (week_stage = index du palier), seuil = probability de la
    //  ligne round-0, spécial cristal → pool, sinon item. Après le
    //  dernier palier → stage=0 + point=0 + texte 0x230a. 449 flag
    //  3 (normal) / 4 (diamond).
    // ============================================================
    private async Task ReceiveWeekCountAsync(NetSession session,
        EntryState st, AltarConfig cfg, CancellationToken token)
    {
        var wk = st.LotteryWeek;
        var milestones = _services.Lottery.WeekMilestones(cfg.Altar);
        int stage = cfg.WeekColumn == 0 ? wk.NormalStage : wk.DiamondStage;
        if (stage < 0 || stage >= milestones.Count) return;
        int point = cfg.WeekColumn == 0 ? wk.NormalPoint : wk.DiamondPoint;
        var m = milestones[stage];
        if (point < (int)m.Probability) return;
        var s = StateOf(st, cfg.Altar);

        if (m.ItemId == cfg.CrystalBId)
        {
            s.CrystalB += (short)m.ItemAmount;
            await SendTextIndexAsync(session, cfg.TextCrystalB,
                token, m.ItemAmount.ToString());
            if (s.CrystalB >= 1000) s.CrystalB = 999;
            if (s.CrystalB > 899)
                await SendTextIndexAsync(session, cfg.TextCrystalBCap,
                    token);
        }
        else if (cfg.CrystalCId != 0 && m.ItemId == cfg.CrystalCId)
        {
            s.CrystalC += (short)m.ItemAmount;
            await SendTextIndexAsync(session, cfg.TextCrystalC,
                token, m.ItemAmount.ToString());
            if (s.CrystalC > 999) s.CrystalC = 999;
            if (s.CrystalB > 899 && cfg.TextCrystalCCap != 0)
                await SendTextIndexAsync(session, cfg.TextCrystalCCap,
                    token);
        }
        else
        {
            var need = StacksNeeded(m.ItemId, m.ItemAmount);
            if (await EmptySlotsAsync(st, token) < need)
            {
                await SendTextIndexAsync(session, 0x4f9, token);
                return;
            }
            var touched = new List<(int BagId, int Slot, bool Merged)>();
            var res = await _services.Inventory.AddItemAsync(
                (int)st.CharId, m.ItemId, m.ItemAmount, touched: touched);
            if (!res.Success)
            {
                await SendTextIndexAsync(session, 0x4f9, token);
                return;
            }
            await NotifyItemAddedAsync(session, st, m.ItemId,
                touched, token);
        }

        stage++;
        bool reset = stage >= milestones.Count;  // « weekcount max »
        if (reset)
        {
            stage = 0;
            await SendTextIndexAsync(session, 0x230a, token);
        }
        if (cfg.WeekColumn == 0)
        { wk.NormalStage = stage; if (reset) wk.NormalPoint = 0; }
        else
        { wk.DiamondStage = stage; if (reset) wk.DiamondPoint = 0; }
        await PersistWeekAsync(st, token);

        // 449 flag 3 = palier normal / 4 = palier diamond (natif).
        await SendLotteryEndAsync(session, st, cfg, s, st.LotteryWeek,
            (short)(cfg.Altar == AltarKind.Normal ? 3 : 4), token);
    }

    /// <summary>Points hebdo — `CharacterLotteryCounting` : gain par
    /// cas natif (bulletin/bonus/no-next/ordinaire), plafond param,
    /// texte 0x2324 « %d,%d » (gain,total), persistance.</summary>
    private async Task AddWeekPointsAsync(NetSession session,
        EntryState st, AltarConfig cfg, CancellationToken token)
    {
        var s = StateOf(st, cfg.Altar);
        int gain = _services.Lottery.WeekPointGain(cfg.Altar, s);
        if (gain <= 0) return;
        var wk = st.LotteryWeek;
        int cap = _services.Lottery.WeekCap(cfg.Altar);
        if (cfg.WeekColumn == 0)
        {
            if (cap > 0) gain = Math.Min(gain, Math.Max(0, cap - wk.NormalPoint));
            wk.NormalPoint += gain;
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x2324, Field2 = $"{gain},{wk.NormalPoint}" },
                token);
        }
        else
        {
            if (cap > 0) gain = Math.Min(gain, Math.Max(0, cap - wk.DiamondPoint));
            wk.DiamondPoint += gain;
            await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
            { Field1 = 0x2324, Field2 = $"{gain},{wk.DiamondPoint}" },
                token);
        }
        await PersistWeekAsync(st, token);
    }

    /// <summary>Persiste la ligne `player_lottery_point` (INSERT si
    /// première écriture, UPDATE sinon — Persisted natif).</summary>
    private async Task PersistWeekAsync(EntryState st,
        CancellationToken token)
    {
        var wk = st.LotteryWeek;
        if (wk.Persisted)
            await _services.LotteryRepo.UpdateWeekPointsAsync(
                st.AccountId, wk);
        else
            await _services.LotteryRepo.InsertWeekPointsAsync(
                st.AccountId, wk);
    }

    // ============================================================
    //  NCID 184 — CheckUpdateLottery → 511 (version différée)
    // ============================================================
    private async Task HandleCheckUpdateLotteryAsync(NetSession session,
        EntryState st, byte[] p, CancellationToken token)
    {
        // Wire : [u16 ?][u16 version][s16 autel] — le natif n'émet
        // que si la version client != version serveur.
        if (p.Length < 6 || st.CharId == 0) return;
        ushort clientVersion = BitConverter.ToUInt16(p, 2);
        short altarRaw = BitConverter.ToInt16(p, 4);
        if (altarRaw is < 0 or > 2) return;
        if (clientVersion == (ushort)LotteryService.CurrentWeek) return;
        var body = _services.Lottery
            .BuildUpdateBody((AltarKind)altarRaw);
        if (body != null)
            await session.SendPacketAsync(511, body, token);
    }

    // ============================================================
    //  NCID 224 — UpdateLotteryBonus : validation anti-dérive du
    //  bonus exact. CNE_CZ_ZoneServerUpdateLotteryBonus::Execute
    //  @0x81e5f40 → CPlayerFactory::UpdateLotteryBonus /
    //  UpdateInGameLotteryBonus @0x81078xx. Si le couple
    //  (extra_item, extra_counts) rapporté par le client diffère de
    //  l'état serveur, ou si la config du round ne contient plus
    //  l'item bonus (AllElfLotterysDBO/AllInGameLotterysDBO natif) →
    //  reset : +0x8f8=1 (reset_bonus), extra_* à 0,
    //  ResetCharacterLotteryBonus DBO, puis 448 resync.
    // ============================================================
    private async Task HandleUpdateLotteryBonusAsync(
        NetSession session, EntryState st, byte[] p,
        CancellationToken token)
    {
        // Wire : [s16 autel 0/1][u16 extra_item][s16 extra_counts]
        // (Deserialize natif : +0x16 op, +0x12 item, +0x14 counts).
        if (p.Length < 6 || st.CharId == 0) return;
        short op = BitConverter.ToInt16(p, 0);
        ushort clientItem = BitConverter.ToUInt16(p, 2);
        short clientCounts = BitConverter.ToInt16(p, 4);
        // Natif : 0 = elf lottery (normal), 1 = in-game ; tout autre
        // op est ignoré silencieusement.
        var altar = op switch
        {
            0 => AltarKind.Normal,
            1 => AltarKind.InGame,
            _ => (AltarKind)(-1),
        };
        if ((int)altar == -1) return;

        var s = StateOf(st, altar);
        // bVar14 natif : la config du round `extra_counts` contient
        // encore `extra_item` — sinon le bonus est orphelin.
        var round = _services.Lottery.Table(altar)
            ?.Round(LotteryService.CurrentWeek, s.ExtraCounts);
        var stillValid = s.ExtraItemId != 0 && round != null
            && round.Exists(e => e.ItemId == s.ExtraItemId);

        if (clientItem == s.ExtraItemId
            && clientCounts == s.ExtraCounts && stillValid)
            return;                       // synchro — rien à faire

        s.ResetBonus = true;              // char+0x8f8 = 1
        s.ExtraItemId = 0;                // char+0x8fa
        s.ExtraCounts = 0;                // char+0x8fc
        s.ExtraMultiple = 0;              // char+0x8fe
        if (s.Persisted)
            await _services.LotteryRepo.ResetBonusAsync(
                (int)st.CharId, altar);
        await SendLotteryResultAsync(session, st, altar, token);
    }

    // ============================================================
    //  Fermeture — StrengthenClose 120 codes 150/151 : exige le
    //  type IsA natif, annule le cast (434), retire l'op et le
    //  registre utilisateur.
    // ============================================================
    private async Task CancelLotteryCastAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingLottery == null) return;
        var cfg = LotteryService.Config(st.PendingLottery.Altar);
        st.PendingLottery.Cts?.Cancel();
        st.PendingLottery = null;
        // CLotteryOp::Cancel → retrait enchant + 434 [s16 window].
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
        { Value = cfg.CastWindow }, token);
    }

    // ============================================================
    //  Paquets sortants
    // ============================================================

    /// <summary>448 <c>CNC_CZ_ClientLotteryResult</c> — 42 octets,
    /// ordre vérifié dans `::Serialize` @0x8135230 : le premier s16
    /// est l'AUTEL (+0x22 objet), puis item/type/amount/counts/plus/
    /// event/0/crystaux/drapeaux/extra/point hebdo/stage hebdo.</summary>
    private async Task SendLotteryResultAsync(NetSession session,
        EntryState st, AltarKind altar, CancellationToken token)
    {
        var s = StateOf(st, altar);
        var wk = st.LotteryWeek;
        bool diamond = altar == AltarKind.Diamond;
        await session.SendAsync(new CNC_CZ_ClientLotteryResult
        {
            Field1 = (short)altar,           // s16 — autel 0/1/2
            Field2 = (ushort)s.ItemId,       // u16 — item gagné
            Field3 = s.LotteryType,          // s16 — type ticket
            Field4 = s.ItemAmount,               // s16 — quantité
            Field5 = s.Counts,               // s16 — stage courant
            Field6 = s.Plus,                 // i32 — plus 500-5000
            Field7 = (uint)s.EventFlag,      // u32 — event_flag
            Field8 = 0,                      // s16 — toujours 0 natif
            Field9 = s.CrystalB,             // s16 — crystal B
            Field10 = s.CrystalC,            // s16 — crystal C
            Field11 = (byte)(s.ResetBonus ? 1 : 0),   // u8
            Field12 = (byte)(s.CanGoNext ? 1 : 0),    // u8
            Field13 = (ushort)s.ExtraItemId, // u16 — lot bonus
            Field14 = (ushort)s.ExtraCounts, // u16
            Field15 = s.ExtraMultiple,       // s16 — ×2-5
            Field16 = diamond ? wk.DiamondPoint : wk.NormalPoint,   // i32
            Field17 = (short)(diamond ? wk.DiamondStage : wk.NormalStage),
        }, token);
    }

    /// <summary>449 <c>CNC_CZ_ClientLotteryEnd</c> — 13 octets natif
    /// <code>[s16 flag][s16 crysB][s16 crysC][u8 reset][i32 point]
    /// [s16 stage]</code> — flag = autel (0/1/2) à la réception du
    /// lot, 3/4 aux paliers hebdo.</summary>
    private async Task SendLotteryEndAsync(NetSession session,
        EntryState st, AltarConfig cfg, PlayerLotteryState s,
        LotteryWeekPoints wk, short flag, CancellationToken token)
    {
        // Point/stage hebdo : l'autel ingame (WeekColumn -1) ne
        // compte pas — le natif émet 0/0 sur son 449.
        await session.SendAsync(new CNC_CZ_ClientLotteryEnd
        {
            Field1 = flag,                                     // s16
            Field2 = s.CrystalB,                               // s16
            Field3 = s.CrystalC,                               // s16
            Field4 = (byte)(s.ResetBonus ? 1 : 0),             // u8
            Field5 = cfg.WeekColumn == 0 ? wk.NormalPoint      // i32
                : cfg.WeekColumn == 1 ? wk.DiamondPoint : 0,
            Field6 = (short)(cfg.WeekColumn == 0               // s16
                ? wk.NormalStage
                : cfg.WeekColumn == 1 ? wk.DiamondStage : 0),
        }, token);
    }

    /// <summary>450 <c>CNC_CZ_ClientLotteryUpdatePlus</c> — wire natif
    /// <code>[i32 plus][s16 autel]</code> émis au tick de
    /// décroissance.</summary>
    /// <summary>Tick de décroissance natif appliqué à la demande :
    /// plus −100/3600 s, plancher 500 → UpdatePlus + UpdateUpdate +
    /// 450 au client.</summary>
    private async Task ApplyLotteryDecayAsync(NetSession session,
        EntryState st, AltarKind altar, CancellationToken token)
    {
        var s = StateOf(st, altar);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!LotteryService.DecayPlus(s, now)) return;
        if (s.Persisted)
        {
            await _services.LotteryRepo.UpdatePlusAsync(
                (int)st.CharId, altar, s);
            await _services.LotteryRepo.UpdatePlusTimestampAsync(
                (int)st.CharId, altar, s);
        }
        await session.SendAsync(new CNC_CZ_ClientLotteryUpdatePlus
        {
            Field1 = s.Plus,                // i32 : valeur plus
            Field2 = (short)altar,          // s16 : autel
        }, token);
    }

    /// <summary>Annonce jackpot : <c>BroadCastScreenMessage Type= 8
    /// Msg= …</c> (DBA 21) → mission → WorldTextCommand 20 → 474.
    /// Texte single ou ×quantité selon le stack natif.</summary>
    private async Task BroadcastLotteryAsync(EntryState st,
        AltarConfig cfg, LotteryReward entry, CancellationToken token)
    {
        int textId = _services.Inventory.MaxStackOf(entry.ItemId) < 2
            || entry.ItemAmount == 1
            ? cfg.TextWinSingle : cfg.TextWinMulti;
        _services.GameData.Texts.TryGetValue(textId, out var tpl);
        var itemName = _services.GameData.Items
            .FirstOrDefault(i => i.Id == entry.ItemId)?.Name
            ?? entry.ItemId.ToString();
        // ReplaceOrderParm natif : %s nom joueur, %s item, %d quantité.
        var msg = (tpl ?? "%s %s %d")
            .Replace("%s", st.CharName ?? "?", StringComparison.Ordinal)
            .Replace("%d", entry.ItemAmount.ToString());
        var i = msg.IndexOf("%s", StringComparison.Ordinal);
        if (i >= 0) msg = msg[..i] + itemName + msg[(i + 2)..];
        await _mission.SendAgentDbaCommandAsync(st.CharId,
            $"BroadCastScreenMessage Type= 8 Msg= {msg}", token);
    }

    // ============================================================
    //  Accès / aides
    // ============================================================

    /// <summary>État de l'autel dans l'EntryState.</summary>
    private static PlayerLotteryState StateOf(EntryState st,
        AltarKind altar) => altar switch
    {
        AltarKind.Normal => st.LotteryNormal,
        AltarKind.InGame => st.LotteryInGame,
        _ => st.LotteryDiamond,
    };

    /// <summary>Slots libres de TOUS les sacs ouverts — capacités
    /// réelles (inventory_slots, base 32 natif + sacs) moins les items
    /// posés. Avant : 64-count du seul sac0 — surestimait et visait
    /// des locs hors capacité (crash client au put).</summary>
    private async Task<int> EmptySlotsAsync(EntryState st,
        CancellationToken token)
    {
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        var caps = await _services.InventoryRepo
            .GetAllBagCapacitiesAsync((int)st.CharId);
        var free = 0;
        foreach (var (bag, cap) in caps)
            if (cap > 0)
                free += Math.Max(0,
                    cap - items.Count(i => i.BagId == bag));
        return free;
    }

    /// <summary>Stacks nécessaires pour `qty` — formule native
    /// ceil(qty/maxStack) (maxStack = itemdata+0x76).</summary>
    private int StacksNeeded(int itemId, int qty)
    {
        var max = _services.Inventory.MaxStackOf(itemId);
        return max <= 1 ? qty : (qty + max - 1) / max;
    }

    /// <summary>Notification d'insertion merge-aware — 291 sur stack
    /// fusionné / 274 sur nouveau slot pour CHAQUE slot touché
    /// (avant : un seul 274 sur le dernier slot — slots éclatés non
    /// annoncés + crash sur merge). 146 miroir quêtes ensuite.</summary>
    private async Task NotifyItemAddedAsync(NetSession session,
        EntryState st, int itemId,
        List<(int BagId, int Slot, bool Merged)> touched,
        CancellationToken token)
    {
        await Codecs.NativeItemCodec.EmitItemSlotsAsync(session,
            _services.InventoryRepo, (int)st.CharId, touched, token);
        _ = _mission.SendQuestItemChangedAsync(st.CharId, itemId, token);
    }

    /// <summary>316 avec args de format (TextOutputIndex natif —
    /// Field2 = la chaîne d'arguments type « %d »).</summary>
    private async Task SendTextIndexAsync(NetSession session,
        int index, CancellationToken token, string args = "")
    {
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        { Field1 = (short)index, Field2 = args }, token);
    }

    // ============================================================
    //  Chargement — états des 3 autels + points hebdo du compte.
    // ============================================================
    private async Task LoadLotteryStateAsync(EntryState st,
        CancellationToken token)
    {
        foreach (var altar in new[]
                 { AltarKind.Normal, AltarKind.InGame, AltarKind.Diamond })
        {
            var s = await _services.LotteryRepo
                .GetStateAsync((int)st.CharId, altar);
            if (s == null) continue;
            // Décroissance offline du plus (tick natif au chargement).
            LotteryService.DecayPlus(s,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            switch (altar)
            {
                case AltarKind.Normal: st.LotteryNormal = s; break;
                case AltarKind.InGame: st.LotteryInGame = s; break;
                default: st.LotteryDiamond = s; break;
            }
        }
        var wk = await _services.LotteryRepo
            .GetWeekPointsAsync(st.AccountId);
        if (wk != null) st.LotteryWeek = wk;
    }
}
