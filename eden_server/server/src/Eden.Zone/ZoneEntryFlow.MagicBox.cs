// ============================================================
//  ZoneEntryFlow.MagicBox.cs — boîte magique (conversion d'énergie
//  et scouring de combos d'équipement)
//
//  Portage de lapis::CItemFactory::{InitMagicBox, CheckMagicBoxCosts,
//  DoMagicBox, GetRandMagicBoxItemCombo, MagicBoxLevelUpGetCombo} +
//  lapis::CMagicBoxOp::{SetItemData,Cancel}
//  (désasm zone @0x8089550 / @0x8088200 / @0x80a8b90 / @0x80a3eb0 /
//  @0x80a8230 / @0x80ab7f0).
//
//  Fenêtre : service NPC type 31 → 612 ClientOpenMagicBoxDialog +
//  CMagicBoxOp (IsA 0x58, ShopOpType=31) — branché dans
//  ZoneEntryFlow.Strengthen.cs.
//
//  Wire 257 CNE_CZ_ZoneServerExecuteMagicBox (Execute @0x80ab7f0) :
//    [u16 type][u16 n][s16 idx×n][u16 n][s16 loc×n]
//    [s16 mainBag][s16 mainSlot]
//    [u16 n][s16 comboIds×n][s16 mallBag][s16 mallSlot]
//  type : 1 = conversion (énergie → star+combo), 2 = scouring.
//
//  Émis :
//    433 ClientStrengthenCasting [s16 0x1f][s16 0xf][s16 0]
//        (InitMagicBox — enchant MagicBoxGoing 0xf ticks ≈1.5 s)
//    434 ClientStrengthenItemCancel [s16 0x1f] — refus/annulation
//    613 ClientMagicBoxResult — corps manuel (modèle généré plat)
//    369 ClientModifyItemInfo — record complet quand combos changés
//    275 ClientDropItem [s16 0][s16 container][u16 slot] par item
//        sacrifié consommé
// ============================================================
using Eden.Core.Game.MagicBox;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;
using Eden.Zone.Handlers;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Durée du cast magic box en unités 100 ms — 0xf natif
    /// (EffectToLife(MagicBoxGoing, durée=0xf) dans InitMagicBox).</summary>
    private const short MagicBoxCastUnits = 0xf;

    /// <summary>Données de l'opération boîte magique
    /// (CMagicBoxOp::SetItemData natif — les 3 vecteurs + 4 shorts du
    /// wire 257 stockés bruts).</summary>
    internal sealed class PendingMagicBox
    {
        public ushort Type;
        /// <summary>Paires (conteneur,slot) des sacrifices posés.</summary>
        public List<(short Bag, short Slot)> Sacrifices = new();
        public short MainBag = -1;
        public short MainSlot = -1;
        /// <summary>Ids de combos sélectionnés (scouring).</summary>
        public List<short> SelectedCombos = new();
        public short MallBag = -1;
        public short MallSlot = -1;
        public CancellationTokenSource? Cts;
    }

    // ============================================================
    //  NCID 257 — CNE_CZ_ZoneServerExecuteMagicBox::Execute
    // ============================================================

    /// <summary>Parse le wire 257, valide via CheckMagicBoxCosts
    /// équivalent, pose l'op (SetItemData natif) et lance le cast
    /// MagicBoxGoing — la mutation réelle (DoMagicBox) se fait à
    /// l'expiration du cast.</summary>
    private async Task HandleExecuteMagicBoxAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.ShopOpType != 31 || st.PendingMagicBox != null) return;
        // op active requise : 0x58 via ShopOpType==31 (déjà mappé) —
        // TargetInTalkingRange vérifié à la résolution (DoMagicBox).
        if (!TryParseMagicBoxRequest(payload, out var p))
        {
            await MagicBoxCancelAsync(session, token);
            return;
        }
        var main = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.MainBag, p.MainSlot);
        var mainDef = main == null ? null : _services.MagicBox.ItemDef(main.ItemId);
        if (main == null || mainDef == null)
        {
            await MagicBoxCancelAsync(session, token);
            return;
        }
        var sacs = await ResolveMagicBoxSacrificesAsync(st, p, token);
        if (sacs == null || sacs.Count != p.Sacrifices.Count)
        {
            await MagicBoxCancelAsync(session, token);
            return;
        }
        var mall = await ResolveMagicBoxMallAsync(st, p, token);
        var mallParams = mall?.Params;
        var mainCombos = new[] { main.MagicBoxEnchant1,
            main.MagicBoxEnchant2, main.MagicBoxEnchant3 };
        var check = _services.MagicBox.CheckCosts(p.Type, mainDef,
            main.MagicBoxStarLevel, sacs, p.SelectedCombos, mainCombos,
            mallParams);
        if (!check.Ok)
        {
            EdenLog.Write(
                $"[Zone] MagicBox costs fail: {check.Error}");
            await MagicBoxCancelAsync(session, token);
            return;
        }
        st.PendingMagicBox = p;
        var cts = new CancellationTokenSource();
        p.Cts = cts;
        // 433 ClientStrengthenCasting [0x1f][0xf][0] — cast 1.5 s.
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        { Field1 = 0x1f, Field2 = 0x0f }, token);
        ScheduleOnLoop(TimeSpan.FromMilliseconds(
                MagicBoxCastUnits * 100),
            _ => ExecuteMagicBoxAfterCastAsync(session, st, p,
                cts.Token), cts.Token);
    }

    /// <summary>Parse strict du wire 257 — tailles bornées (malformé →
    /// refus natif : aucun dépassement de lecture).</summary>
    private static bool TryParseMagicBoxRequest(byte[] payload,
        out PendingMagicBox p)
    {
        p = new PendingMagicBox();
        var o = 0;
        bool U16(out int v) { v = o + 2 <= payload.Length
            ? BitConverter.ToUInt16(payload, o) : -1;
            if (v < 0) return false; o += 2; return true; }
        bool S16(out short v) { v = o + 2 <= payload.Length
            ? BitConverter.ToInt16(payload, o) : (short)-1;
            if (v < 0 && o + 2 > payload.Length) return false;
            o += 2; return true; }
        int type;
        if (!U16(out type)) return false;
        p.Type = (ushort)type;
        // Vecteurs sacrifices : [u16 n][s16×n] bags puis locs.
        if (!U16(out var n) || n > 64) return false;
        var bags = new short[n];
        for (var i = 0; i < n; i++)
            if (!S16(out bags[i])) return false;
        if (!U16(out var m) || m != n) return false;
        for (var i = 0; i < n; i++)
        {
            if (!S16(out var s)) return false;
            p.Sacrifices.Add((bags[i], s));
        }
        if (!S16(out p.MainBag) || !S16(out p.MainSlot)) return false;
        // Vecteur combos sélectionnés.
        if (!U16(out var k) || k > 3) return false;
        for (var i = 0; i < k; i++)
        {
            if (!S16(out var c)) return false;
            p.SelectedCombos.Add(c);
        }
        if (!S16(out p.MallBag) || !S16(out p.MallSlot)) return false;
        return o == payload.Length;
    }

    /// <summary>Résolution inventaire des sacrifices (GetItemFromInv
    /// natif + CheckItem — l'item appartient au char et existe).</summary>
    private async Task<List<MagicBoxService.Sacrifice>?>
        ResolveMagicBoxSacrificesAsync(EntryState st, PendingMagicBox p,
            CancellationToken token)
    {
        var list = new List<MagicBoxService.Sacrifice>(p.Sacrifices.Count);
        foreach (var (bag, slot) in p.Sacrifices)
        {
            if (bag < 0 || slot < 0) return null;
            var item = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, bag, slot);
            var def = item == null
                ? null : _services.MagicBox.ItemDef(item.ItemId);
            if (item == null || def == null) return null;
            // L'équipement sacrifié doit porter un flag de type éligible
            // (le natif vérifie les mêmes bits +0x34 sur les sacrifices —
            // QualityNeed=0 filtre sinon ; on garde le contrôle explicite).
            list.Add(new(item.Id, item.ItemId, item.Quality, item.Magic,
                def.RequiredLevel));
        }
        return list;
    }

    /// <summary>Catalyseur item mall — enchant 8022 (0x1f56) natif ;
    /// un item sans cette commande est ignoré (LAB_080884be : traité
    /// comme "pas de mall item").</summary>
    private sealed record MallItem(short Bag, short Slot,
        MagicBoxService.MallParams Params);

    private async Task<MallItem?> ResolveMagicBoxMallAsync(EntryState st,
        PendingMagicBox p, CancellationToken token)
    {
        if (p.MallBag < 0 || p.MallSlot < 0) return null;
        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.MallBag, p.MallSlot);
        var def = item == null ? null : _services.MagicBox.ItemDef(item.ItemId);
        if (item == null || def == null) return null;
        var cmds = _services.Enchants.GetUseCommands(def.UseEnchantId);
        if (cmds.Count == 0 || cmds[0].CommandId != 8022) return null;
        var a = cmds[0].Args;
        return new MallItem(p.MallBag, p.MallSlot,
            new MagicBoxService.MallParams(
                a.Length > 0 ? (int)a[0] : 0,
                a.Length > 1 ? (int)a[1] : 0,
                a.Length > 2 ? (int)a[2] : 0,
                a.Length > 3 ? (int)a[3] : 0,
                a.Length > 4 ? (int)a[4] : 0,
                a.Length > 5 && a[5] != 0));
    }

    /// <summary>Annulation/refus natif — 434 ClientStrengthenItemCancel
    /// [s16 0x1f] + flag d'op (op[0x38]=0 → PendingMagicBox=null).</summary>
    private async Task MagicBoxCancelAsync(NetSession session,
        CancellationToken token)
    {
        await session.SendAsync(new Eden.Core.Networking.Messages
            .Generated.CNC_CZ_ClientStrengthenItemCancel
        { Value = 0x1f }, token);
    }

    /// <summary>Résolution du cast — ExecuteMagicBox natif : revérifie
    /// (items potentiellement déplacés), applique DoMagicBox, consomme,
    /// persiste (UpdateMagicBoxData + flush) et notifie.</summary>
    private async Task ExecuteMagicBoxAfterCastAsync(NetSession session,
        EntryState st, PendingMagicBox p, CancellationToken ct)
    {
        try
        {
            // Délai de cast consommé au scheduling — corps direct.
            if (st.PendingMagicBox != p) return;
            st.PendingMagicBox = null;
            if (st.ShopOpType != 31) return;
            // DoMagicBox natif : TargetInTalkingRange sinon 434 [0x1f].
            if (!OpInTalkingRange(st))
            {
                await MagicBoxCancelAsync(session, ct);
                return;
            }

            var main = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, p.MainBag, p.MainSlot);
            var mainDef = main == null
                ? null : _services.MagicBox.ItemDef(main.ItemId);
            if (main == null || mainDef == null)
            {
                await MagicBoxCancelAsync(session, ct);
                return;
            }
            var sacs = await ResolveMagicBoxSacrificesAsync(st, p, ct);
            if (sacs == null || sacs.Count != p.Sacrifices.Count)
            {
                await MagicBoxCancelAsync(session, ct);
                return;
            }
            var mall = await ResolveMagicBoxMallAsync(st, p, ct);
            var mainCombos = new[] { main.MagicBoxEnchant1,
                main.MagicBoxEnchant2, main.MagicBoxEnchant3 };
            var check = _services.MagicBox.CheckCosts(p.Type, mainDef,
                main.MagicBoxStarLevel, sacs, p.SelectedCombos,
                mainCombos, mall?.Params);
            if (!check.Ok)
            {
                await MagicBoxCancelAsync(session, ct);
                return;
            }

            var combos = new int[3]
                { main.MagicBoxEnchant1, main.MagicBoxEnchant2,
                  main.MagicBoxEnchant3 };
            short mallBag = -1, mallSlot = -1;
            if (p.Type == 1)
            {
                var validated = sacs
                    .Select(s => (s, _services.MagicBox.ConversionRate(
                        check.MainType, main.MagicBoxStarLevel, s)))
                    .Where(x => x.Item2 >= 0).ToList();
                var outcome = _services.MagicBox.Convert(mainDef,
                    main.MagicBoxEnergy, main.MagicBoxStarLevel, combos,
                    validated, mall?.Params);
                if (outcome.Failed)
                {
                    await MagicBoxCancelAsync(session, ct);
                    return;
                }
                main.MagicBoxEnergy = outcome.Energy;
                main.MagicBoxStarLevel = outcome.StarLevel;
                if (mall != null && outcome.MallConsumed)
                {
                    mallBag = mall.Bag; mallSlot = mall.Slot;
                }
            }
            else
            {
                var outcome = _services.MagicBox.Scour(mainDef,
                    main.MagicBoxStarLevel, combos, p.SelectedCombos,
                    mall?.Params);
                if (mall != null && outcome.MallConsumed)
                {
                    mallBag = mall.Bag; mallSlot = mall.Slot;
                }
            }
            main.MagicBoxEnchant1 = combos[0];
            main.MagicBoxEnchant2 = combos[1];
            main.MagicBoxEnchant3 = combos[2];

            // CItemDBO::UpdateMagicBoxData + FlushAll natifs.
            await _services.InventoryRepo.UpdateMagicBoxDataAsync(
                main.Id, combos[0], combos[1], combos[2],
                main.MagicBoxEnergy, main.MagicBoxStarLevel);
            await _services.InventoryRepo.UpdateItemAsync(main);

            // Consommation des sacrifices validés — EradicateItem +
            // ClientDropItem [0][container][slot] natif (ordre : après
            // la persistance, comme DoMagicBox).
            var idx = new short[p.Sacrifices.Count];
            var loc = new short[p.Sacrifices.Count];
            for (var i = 0; i < p.Sacrifices.Count; i++)
            {
                idx[i] = p.Sacrifices[i].Bag;
                loc[i] = p.Sacrifices[i].Slot;
                // 146 — item sacrifié (ItemId via la résolution, la
                // ligne disparaît) → ItemCollect.
                if (i < sacs.Count)
                    _services.NotifyItemChanged((int)st.CharId,
                        sacs[i].ItemId);
                await _services.InventoryRepo.RemoveItemAsync(
                    (int)st.CharId, p.Sacrifices[i].Bag,
                    p.Sacrifices[i].Slot);
                // 275 natif [contType][loc][contIdx] — Field2=loc,
                // Field3=index de sac (inversés avant le fix).
                await session.SendAsync(new CNC_CZ_ClientDropItem
                { Field1 = 0, Field2 = (short)p.Sacrifices[i].Slot,
                  Field3 = (short)p.Sacrifices[i].Bag }, ct);
            }
            // Consommation du catalyseur (ItemConsume ×1 natif).
            if (mallBag >= 0)
            {
                await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, mallBag, mallSlot, 1);
                // 275 natif [contType][loc][contIdx] — même ordre.
                await session.SendAsync(new CNC_CZ_ClientDropItem
                { Field1 = 0, Field2 = (short)mallSlot,
                  Field3 = (short)mallBag }, ct);
            }
            // 369 ClientModifyItemInfo — record complet de l'item
            // muté (DoMagicBox émet quand les combos ont changé).
            if (p.Type == 2 || combos.Any(c => c > 0))
            {
                var row = await _services.InventoryRepo
                    .GetItemFullRowAsync(main.Id);
                if (row != null)
                    // u8 natif = flag socket-capable (bit3 clone+0x24
                    // = lapis+0xf6 — CWeapon/CArmor). Le main item de
                    // la magic box vit dans un sac → GetType()=0
                    // (CInventory), pas container_index.
                    await session.SendPacketAsync(369,
                        MagicBoxPackets.BuildModifyItemInfo(row,
                            0, p.MainBag, p.MainSlot,
                            (byte)(Eden.Core.Game.Inlay.InlayService
                                .IsSocketCapable(mainDef) ? 1 : 0)),
                        ct);
            }
            // Combos magicbox_enchant1-3 d'une pièce portée → stage
            // « magicbox » du StatPipeline : Profil à jour sans relog.
            await InventoryHandlers.EmitSelfEquipRefreshAsync(_services,
                session, (int)st.CharId, ct);
            // 613 ClientMagicBoxResult natif.
            await session.SendPacketAsync(613,
                MagicBoxPackets.BuildMagicBoxResult((short)p.Type,
                    combos.Select(c => (short)c).ToArray(),
                    (ushort)main.MagicBoxEnergy,
                    (ushort)main.MagicBoxStarLevel, idx, loc,
                    mallBag, mallSlot), ct);
        }
        catch (OperationCanceledException) { /* StrengthenClose 31 */ }
        catch (Exception ex)
        { EdenLog.Write($"[Zone] DoMagicBox: {ex.Message}"); }
    }

    /// <summary>StrengthenClose 31 → CMagicBoxOp::Cancel natif :
    /// annule le cast en cours + 434 [0x1f].</summary>
    private async Task CancelPendingMagicBoxAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingMagicBox == null) return;
        st.PendingMagicBox.Cts?.Cancel();
        st.PendingMagicBox = null;
        await MagicBoxCancelAsync(session, token);
    }
}
