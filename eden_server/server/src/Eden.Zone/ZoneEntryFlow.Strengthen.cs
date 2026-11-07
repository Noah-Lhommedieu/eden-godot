using Eden.Core.Data.Parsers;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Handlers;
using Eden.Core.Logging;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  ZoneEntryFlow.Strengthen — renforcement d'équipement (fortify) + cycle de
//  vie des "ops" de boutique natives (COperation, char+0x670).
//
//  Preuves (re/ghidra/zone/decompiled.c) :
//    - SimpleAction (CPlayerFactory @0x8115a10, appelé par
//      CNE_CZ_ZoneServerPlayerAction::Execute @0x826fbd0) : code 13 →
//      CStrengthenOp + ClientEnterStrengthen(432) [i32 npcUid][s16 9]... ;
//      14 → CCombineOp + 432[s16 10] ; 16 → CLifeSkillOp + 432[s16 0x68] ;
//      101 ('e') → CCubeOp + 432[s16 0x1c]. Codes 7/8 → EquipAction.
//    - CEC_OpenShop (table `OpenShop <n>`, @0x8539xxx) : 8=strengthen,
//      9=combine — npcUid=0 (TargetInTalkingRange(0) → vrai @0x8428420).
//    - CItemFactory::StrengthenItem @0x808c… / CheckStrengthenItem
//      @0x8086… / DoStrengthenItem @0x80a2b00 : coût
//      `repairPrice*0.1+10` (DAT_087cc618/620), enchant de cast 13
//      (StrengthenItemID), ClientStrengthenCasting(433) [9][uVar9][0],
//      taux = S_FamilyCombine col13 si matériau ∈ liste SStuff,
//      niveau 1-9 → taux/2^niveau, 10-13 → S_Parameter 517-520 (armes
//      kind 0x11-0x21) / 521-524 (armures) ; pitié failCount×
//      EquipAddition[niveau ou niveau+1] ; résultat 435 + 436 + drop
//      275 + broadcast PlayerAction 0x2b + annonce 316 idx 0x69d (>+7).
//    - CStrengthenOp::Cancel @0x856f780 : retire l'enchant 13 + envoie
//      ClientStrengthenItemCancel(434) [s16 9] si un cast était actif.
//    - StrengthenClose (120) : [s16 code fenêtre] 9→op0x26, 10→0x27,
//      22→0x39, 28→0x52 → ResetOperation.
//    - ExecuteMagicBox (257) : op 0x58 requis ; CheckMagicBoxCosts
//      échoué → ClientStrengthenItemCancel [0x1f]. VerifyCubeCosts
//      (232) : op 0x52 requis.
// -----------------------------------------------------------------------------
public sealed partial class ZoneEntryFlow
{
    /// <summary>Bonus de pitié natif par échec —
    /// <c>lapis::EquipAddition</c> (rodata 0x87c3720) : indexé par
    /// niveau de renfort ; armures → [lvl], armes → [lvl+1].</summary>
    private static readonly float[] StrengthenEquipAddition =
    {
        0f, 0f, 0f, 5f, 3f, 1.53f, 0.5f, 0.15f, 0.04f, 0.01f,
        0.003f, 0.003f, 0.003f, 0.003f, 0.003f,
    };

    /// <summary>Type d'opération `CInlayShopOp` natif — `char+0x670`
    /// vaut 0x23 pour la boutique de sertissage NPC (TC_InlayShop
    /// @0x083c6de0 ; gate du 67 ZoneServerInlayItem @0x0825f630).</summary>
    private const int InlayShopOpType = 0x23;

    /// <summary>Requête de renfort validée, en attente de fin de cast
    /// (CStrengthenOp::SetItemData natif — combineId + 3 paires
    /// (conteneur,slot) ; le coût est recalculé à la résolution).</summary>
    internal sealed class PendingStrengthen
    {
        public int CombineId;       // Field2 — recette S_FamilyCombine 1-20
        public short TargetBag;     // paire 1 : cible (conteneur/sac)
        public short TargetSlot;
        public short MatBag;        // paire 2 : pierre de renfort
        public short MatSlot;
        public short ChanceBag;     // paire 3 : item de chance (-1 = aucun)
        public short ChanceSlot;
        public CancellationTokenSource? Cts;
    }

    /// <summary>Ouvre une opération de boutique et envoie 432
    /// ClientEnterStrengthen — corps natif prouvé
    /// <c>[i32 npcUid][s16 op][u16 0][s16 -1][s16 0]</c>
    /// (CEC_OpenShop / SimpleAction : npcUid=0 pour l'ouverture directe,
    /// = l'uid du NPC pour le chemin service de dialogue).</summary>
    private async Task OpenShopOpAsync(NetSession session, EntryState st,
        int opType, int packetOp, int npcUid, int npcHandle,
        CancellationToken token)
    {
        st.ShopOpType = opType;
        st.ShopOpNpcUid = npcUid;
        st.ShopOpNpcHandle = npcHandle;
        // Op unique (char+0x670 natif) — l'ouverture détruit l'op
        // works en cours.
        st.WorkOpType = -1;
        CancelPendingStrengthen(st);
        var body = new List<byte>();
        await session.SendAsync(new CNC_CZ_ClientEnterStrengthen
        {
            Field1 = npcUid,                                // i32 : uid NPC (0 = UI directe)
            Field2 = (short)packetOp,                       // s16 : type fenêtre
            Field4 = -1,
        }, token);
    }

    /// <summary>PlayerAction (NCID 14) — les codes d'ouverture
    /// d'opération natifs. Retourne true si le code a été consommé
    /// (sinon l'appelant garde le broadcast d'émoticône).</summary>
    private async Task<bool> TryPlayerActionOpAsync(NetSession session,
        EntryState st, byte act, int p1, int p2,
        CancellationToken token)
    {
        switch (act)
        {
            case 13:  // fortify → CStrengthenOp + 432 [0][9]…
                await OpenShopOpAsync(session, st, 9, 9, 0, 0, token);
                return true;
            case 14:  // combine → CCombineOp + 432 [0][10]…
                await OpenShopOpAsync(session, st, 10, 10, 0, 0, token);
                return true;
            case 16:  // lifeskill → CLifeSkillOp + 432 [0][0x68]…
                await OpenShopOpAsync(session, st, 22, 0x68, 0, 0, token);
                return true;
            case 101: // 'e' → CCubeOp + 432 [0][0x1c]…
                await OpenShopOpAsync(session, st, 28, 0x1c, 0, 0, token);
                return true;
            case 7: case 8:
                // EquipAction natif (CPlayerFactory::EquipAction
                // @0x8104540) : 7 = toggle bit2 de +0x4ac →
                // ModelModify(0x2000) + broadcast PlayerAction ;
                // 8 = masque de visibilité 10 bits → ModelModify(0x4000)
                // (jamais de broadcast PlayerAction).
                if (act == 7)
                {
                    st.Vis4ac = st.Vis4ac & ~4 | (p1 & 1) << 2;
                    await SendModelModifyAsync(session, st, 0x2000,
                        token, (st.Vis4ac >> 2) & 1);
                    await BroadcastPlayerActionAsync(st, 7, p1, p2,
                        token);
                }
                else
                {
                    if (p1 == -1) st.Vis4ab = p2 & 0xff;
                    else
                    {
                        var mask = (st.Vis4ac & 3) << 8 | st.Vis4ab;
                        var bit = 1 << (p1 & 0x1f);
                        mask = p2 == 0 ? mask & ~bit : mask | bit;
                        st.Vis4ab = mask & 0xff;
                        st.Vis4ac = st.Vis4ac & ~3 | mask >> 8 & 3;
                    }
                    await SendModelModifyAsync(session, st, 0x4000,
                        token, (st.Vis4ac & 3) << 8 | st.Vis4ab);
                }
                return true;
            case 15:  // '\x0f' → return 1 sans rien émettre (natif)
                return true;
            default:
                return false;
        }
    }

    /// <summary>Portée parlée native — TargetInTalkingRange @0x8428420 :
    /// uid 0 → vrai ; sinon le NPC doit exister et être à ≤10.0 sur
    /// chaque axe (abs dx et dz, pas euclidien).</summary>
    private bool OpInTalkingRange(EntryState st)
    {
        if (st.ShopOpNpcUid == 0) return true;
        if (!st.EntityPos.TryGetValue((uint)st.ShopOpNpcHandle,
                out var pos))
            return false;
        return Math.Abs(pos.X - st.PosX) <= 10f
            && Math.Abs(pos.Y - st.PosZ) <= 10f;
    }

    /// <summary>Cancel natif du cast de renfort en cours (annule le
    /// timer et envoie 434 [s16 9] comme CStrengthenOp::Cancel).</summary>
    private async Task CancelPendingStrengthenAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingStrengthen == null) return;
        st.PendingStrengthen.Cts?.Cancel();
        st.PendingStrengthen = null;
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
        { Value = 9 }, token);
    }

    /// <summary>Purge synchrone du cast en cours (ouverture d'une
    /// nouvelle op) — aucun paquet : le natif détruit l'op précédente
    /// via ResetOperation.</summary>
    private static void CancelPendingStrengthen(EntryState st)
    {
        st.PendingStrengthen?.Cts?.Cancel();
        st.PendingStrengthen = null;
        // ResetOperation natif détruit l'op précédente — le cast métier
        // (enchant 211) meurt avec elle, sans paquet.
        st.PendingLifeSkill?.Cts?.Cancel();
        st.PendingLifeSkill = null;
        // ResetOperation détruit aussi l'op combine — le cast
        // CombineItemID en vol meurt avec elle, sans paquet.
        st.PendingCombine?.Cts?.Cancel();
        st.PendingCombine = null;
    }

    /// <summary>NCID 119 CNE_CZ_ZoneServerStrengthenItem — validation
    /// (CheckStrengthenItem) puis armement du cast : 433 + résolution
    /// différée (DoStrengthenItem). Corps : [s16 ?][i32 combineId]
    /// [s16 sacCible][s16 slotCible][s16 sacMat][s16 slotMat]
    /// [s16 sacChance][s16 slotChance][s16 rapide].</summary>
    private async Task HandleStrengthenItemAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.ShopOpType != 9) return;               // op ≠ 0x26 → silence
        if (!OpInTalkingRange(st)) return;
        if (payload.Length < 20 || st.PendingStrengthen != null) return;

        var combineId = BitConverter.ToInt32(payload, 2);   // i32 : recette
        var tc = BitConverter.ToInt16(payload, 6);          // s16 : sac cible
        var ts = BitConverter.ToInt16(payload, 8);          // s16 : slot cible
        var mc = BitConverter.ToInt16(payload, 10);         // s16 : sac matériau
        var ms = BitConverter.ToInt16(payload, 12);         // s16 : slot matériau
        var cc = BitConverter.ToInt16(payload, 14);         // s16 : sac chance
        var cs = BitConverter.ToInt16(payload, 16);         // s16 : slot chance
        var quick = BitConverter.ToInt16(payload, 18);      // s16 : 1 = cast court

        var cancel = new CNC_CZ_ClientStrengthenItemCancel { Value = 9 };
        var check = await CheckStrengthenItemAsync(st, combineId,
            tc, ts, mc, ms, cc, cs);
        if (!check)
        {
            await session.SendAsync(cancel, token);
            return;
        }

        // Coût : repairPrice×0.1 + 10 (DAT_087cc618/620) — repairPrice
        // = GetRepairPrice natif (itemdata+0x14 = S_Item col 76 ; la
        // variante addattr+0xe0 échelle par attributs non portée).
        var target = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, tc, ts);
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == target!.ItemId);
        var cost = (int)(def!.RepairPrice * 0.1 + 10.0);
        if (cost < 0 || st.Gold < cost)
        {
            await session.SendAsync(cancel, token);
            return;
        }

        // Déjà en cast (enchant 13 natif) → le natif retourne sans paquet.
        var pending = new PendingStrengthen
        {
            CombineId = combineId,
            TargetBag = tc, TargetSlot = ts,
            MatBag = mc, MatSlot = ms,
            ChanceBag = cc, ChanceSlot = cs,
        };
        st.PendingStrengthen = pending;

        // Cast : Field9==1 → 0x14 (2s), sinon 0x28 (4s) — le natif passe
        // la valeur à l'enchant 13 et la renvoie dans 433.
        var castUnits = (short)(quick == 1 ? 0x14 : 0x28);
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        {
            Field1 = 9,         // s16 : type fenêtre (renfort)
            Field2 = castUnits, // s16 : durée du cast en ×100ms
            Field3 = 0,         // s16 : 0 (natif ne l'écrit pas)
        }, token);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        pending.Cts = cts;
        ScheduleOnLoop(TimeSpan.FromMilliseconds(castUnits * 100),
            _ => ResolveStrengthenAfterCastAsync(session, st, pending,
                cts.Token), cts.Token);
    }

    /// <summary>Fin du cast (enchant 13 expiré) → DoStrengthenItem :
    /// re-validation, coûts, roll, résultat/persistance.</summary>
    private async Task ResolveStrengthenAfterCastAsync(NetSession session,
        EntryState st, PendingStrengthen pending,
        CancellationToken ct)
    {
        try
        {
            await DoStrengthenItemAsync(session, st, pending, ct);
        }
        catch (OperationCanceledException) { /* StrengthenClose */ }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] DoStrengthenItem: {ex.Message}");
        }
    }

    /// <summary>DoStrengthenItem @0x80a2b00 — résolution du renfort :
    /// revalidation complète (le joueur a pu bouger/retirer l'item),
    /// consommation or+matériaux, roll, paquets 435/436/275/290,
    /// persistance `strengthen`, annonce &gt;+7.</summary>
    private async Task DoStrengthenItemAsync(NetSession session,
        EntryState st, PendingStrengthen p, CancellationToken token)
    {
        if (st.PendingStrengthen != p) return;
        st.PendingStrengthen = null;
        if (st.ShopOpType != 9 || !OpInTalkingRange(st)) return;

        var cancel = new CNC_CZ_ClientStrengthenItemCancel { Value = 9 };
        if (!await CheckStrengthenItemAsync(st, p.CombineId,
                p.TargetBag, p.TargetSlot, p.MatBag, p.MatSlot,
                p.ChanceBag, p.ChanceSlot))
        {
            await session.SendAsync(cancel, token);
            return;
        }

        // Recette : le taux n'est appliqué que si le matériau figure
        // dans la liste SStuff de la station (S_FamilyCombine).
        var recipe = _services.GameData.FamilyRecipes
            .FirstOrDefault(r => r.Id == p.CombineId);
        var material = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.MatBag, p.MatSlot);
        var allowed = recipe?.StrengthenMaterials
            .Any(m => m.ItemId == material?.ItemId) == true;
        var baseRate = allowed ? recipe!.StrengthenRate : -1;
        if (baseRate < 0)
        {
            await session.SendAsync(cancel, token);
            return;
        }

        var target = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.TargetBag, p.TargetSlot);
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == target!.ItemId);
        var cost = (int)(def!.RepairPrice * 0.1 + 10.0);
        if (cost < 0 || st.Gold < cost)
        {
            await session.SendAsync(cancel, token);
            return;
        }

        var level = target!.EnchantLevel;
        var kind = def!.Kind;
        var isWeapon = kind is >= 0x11 and <= 0x21;

        // Taux de base : niv 0 → recette ; 1-9 → recette/2^niveau ;
        // 10-13 → S_Parameter 517+lvl%10 (armes) / 521+lvl%10 (armures
        // kind<0x11 ou bouclier 0x22) ; autres kinds → 0.
        double rate;
        if (level == 0)
            rate = baseRate;
        else if (level is >= 10 and <= 13)
        {
            var paramId = isWeapon ? 517 + level % 10
                : kind < 0x11 || kind == 0x22 ? 521 + level % 10 : -1;
            rate = paramId < 0 ? 0
                : _services.GameData.Parameters.Get(paramId);
        }
        else
            rate = baseRate / (double)(2 << (level - 1));

        // Pitié : failCount × EquipAddition — armures (kind<0x11 ou
        // 0x51) → [niveau] ; armes/bouclier → [niveau+1].
        double pity;
        if (kind is >= 1 and <= 0x10 || kind == 0x51)
            pity = level < StrengthenEquipAddition.Length
                ? st.StrengthenFailCount * StrengthenEquipAddition[level] : 0;
        else if (isWeapon || kind == 0x22)
            pity = level + 1 < StrengthenEquipAddition.Length
                ? st.StrengthenFailCount * StrengthenEquipAddition[level + 1] : 0;
        else
            pity = 0;

        // Item de chance (CChanceItem natif) : l'item mall (ex. 41469
        // « Cristal d'amplification parfaite ») porte un enchant kind
        // 8002 → bonus de taux + borne de garantie (niveau ≤ borne →
        // 100 %) + flag « lie la cible ». Niveau 0 → 100 (natif).
        EnchantParser.EnchantDef? chanceEnch = null;
        if (p.ChanceBag != -1 && p.ChanceSlot != -1)
        {
            var ch0 = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, p.ChanceBag, p.ChanceSlot);
            var chDef = ch0 == null ? null : _services.GameData.Items
                .FirstOrDefault(i => i.Id == ch0.ItemId);
            chanceEnch = chDef == null ? null : _services.GameData
                .Enchants.FirstOrDefault(
                    e => e.Id == chDef.UseEnchantId);
        }
        // DoStrengthenItem @0x80a2b00 : garantie tant que niveau ≤
        // borne (safe-level p5, ou borne haute de la fenêtre p9/p11) ;
        // au-delà → taux + bonus p1.
        var guarantee = chanceEnch?.ChanceSafeLevel ?? 0;
        if (guarantee == 0) guarantee = chanceEnch?.ChanceUpper ?? 0;
        double total = guarantee >= level ? 100.0
            : pity + (chanceEnch?.ChanceBonus ?? 0) + rate;

        // Consommation (or + pierre + chance ×1 — après validation).
        st.Gold = (int)await _services.Characters.AddGoldAsync(
            (int)st.CharId, -cost);
        await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0, token);
        await _services.Inventory.RemoveItemAsync(
            (int)st.CharId, p.MatBag, p.MatSlot, 1);
        // Wire natif ItemConsume : pile résiduelle → 291, pile
        // vidée → 275 (comme le chemin DeductItem du cube).
        if (material != null)
        {
            var matLeft = material.Quantity - 1;
            var matDef2 = _services.GameData.Items
                .FirstOrDefault(i => i.Id == material.ItemId);
            if (matLeft > 0)
                await session.SendAsync(
                    new CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0, Field2 = p.MatSlot,
                        Field3 = (short)matLeft,
                        Field4 = (short)(matDef2?.MaxDurability ?? 0),
                        Field5 = p.MatBag, Field6 = 0,
                    }, token);
            else
                await session.SendAsync(new CNC_CZ_ClientDropItem
                { Field1 = 0, Field2 = p.MatSlot,
                  Field3 = p.MatBag }, token);
            // 146 — matériau consommé → ItemCollect.
            _services.NotifyItemChanged((int)st.CharId, material.ItemId);
        }
        if (p.ChanceBag != -1 && p.ChanceSlot != -1)
        {
            // 146 — item de chance consommé (ItemId lu avant retrait).
            var ch = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, p.ChanceBag, p.ChanceSlot);
            await _services.Inventory.RemoveItemAsync(
                (int)st.CharId, p.ChanceBag, p.ChanceSlot, 1);
            if (ch != null)
            {
                var chLeft = ch.Quantity - 1;
                var chDef2 = _services.GameData.Items
                    .FirstOrDefault(i => i.Id == ch.ItemId);
                if (chLeft > 0)
                    await session.SendAsync(
                        new CNC_CZ_ClientModifyItemDurability
                        {
                            Field1 = 0, Field2 = p.ChanceSlot,
                            Field3 = (short)chLeft,
                            Field4 = (short)(chDef2?.MaxDurability ?? 0),
                            Field5 = p.ChanceBag, Field6 = 0,
                        }, token);
                else
                    await session.SendAsync(new CNC_CZ_ClientDropItem
                    { Field1 = 0, Field2 = p.ChanceSlot,
                      Field3 = p.ChanceBag }, token);
                _services.NotifyItemChanged((int)st.CharId, ch.ItemId);
            }
        }
        // Flag bind de la pierre (chance+0x129 natif) → la cible
        // devient liée (CItemDBO::SetItemBind + bit 0x10 avant le
        // tirage ; le 436 de succès renvoie l'état réel).
        if (chanceEnch?.ChanceBind != 0 && !target.IsBound)
        {
            await _services.InventoryRepo.SetItemBindAsync(target.Id);
            target.IsBound = true;
        }

        var roll = Random.Shared.NextDouble() * 100.0;
        if (roll < total)
        {
            // SUCCÈS — niveau+1, anim 0x2b aux voyeurs, 436+435,
            // persistence. Les paliers 6/8/10/12/14 appellent
            // AddComboStrengthen (kind ≠ 0x51).
            st.StrengthenFailCount = 0;
            if (_mission.Connected)
                await _mission.SendAgentDbaCommandAsync(st.CharId,
                    "TempStrengthenFailTime = 0", token);
            target.EnchantLevel = level + 1;
            var comboId = 0;
            if (def.Kind != 0x51 &&
                target.EnchantLevel is 6 or 8 or 10 or 12 or 14)
            {
                // AddComboStrengthen @0x080a2160 — roll natif sur les
                // slots extra_combo 6..10, groupe/poids col7/8.
                comboId = Eden.Core.Game.Services.StrengthenComboService
                    .Roll(target, def.Kind, def.RequiredLevel,
                        _services.GameData.ItemCombos);
            }
            await _services.InventoryRepo.UpdateItemAsync(target);
            if (comboId > 0)
            {
                // CItemDBO::ModifyItemCombo — 11 u16 extra_combo_id.
                await _services.InventoryRepo.UpdateItemCombosAsync(target);
                // 369 ClientModifyItemInfo — record complet (natif émet
                // le record au possesseur quand les combos changent).
                var row = await _services.InventoryRepo
                    .GetItemFullRowAsync(target.Id);
                if (row != null)
                    await session.SendPacketAsync(369,
                        Eden.Zone.Codecs.MagicBoxPackets
                            .BuildModifyItemInfo(row, 0, p.TargetBag,
                                p.TargetSlot,
                                (byte)(Eden.Core.Game.Inlay.InlayService
                                    .IsSocketCapable(def) ? 1 : 0)),
                        token);
            }

            // 290 PlayerAction 0x2b — anim de réussite aux voyeurs
            // (BroadCastToCanSee natif).
            var (actOp, actPayload) = PreparePacket(
                new CNC_CZ_ClientPlayerAction
                {
                    Field1 = st.CharId,
                    Field2 = 0x2b,
                    Field3 = st.Facing,
                });
            foreach (var s in _services.Sessions.All)
            {
                if (!_states.TryGetValue(s.SessionId, out var ost) ||
                    ost.NodeId != st.NodeId || !ost.Migrated ||
                    ost.InstanceGroup != st.InstanceGroup ||
                    !ost.Sent.ContainsKey((uint)st.CharId)) continue;
                await SendPreparedToAsync(s, actOp, actPayload, token);
            }

            // 436 [contType][slot][niveau][contIdx][protégé]
            await session.SendAsync(new CNC_CZ_ClientModifyItemStrengthen
            {
                Field1 = 0,                      // s16 : type conteneur (sacs)
                Field2 = p.TargetSlot,           // s16 : slot
                Field3 = (short)target.EnchantLevel, // s16 : nouveau niveau
                Field4 = p.TargetBag,            // s16 : index conteneur
                Field5 = (short)(target.IsBound ? 1 : 0), // s16 : bind
            }, token);
            // 435 [9][1][0×4] — succès
            await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
            {
                Field1 = 9, Field2 = 1,
            }, token);

            // +N d'une pièce portée → stage « fortify » du StatPipeline :
            // le Profil doit refléter le nouveau palier sans relog
            // (RefreshEQsAttr natif).
            await InventoryHandlers.EmitSelfEquipRefreshAsync(_services,
                session, (int)st.CharId, token);

            // Annonce serveur à partir de +8 (idx texte 0x69d,
            // args "%s,+%d,%s" = nom,+N,item).
            if (target.EnchantLevel > 7)
                await BroadcastTextIndexAsync(st.NodeId, st.InstanceGroup, 0x69d,
                    token, $"{st.CharName},+{target.EnchantLevel},{def.Name}");
        }
        else
        {
            // ÉCHEC — l'item est détruit (EradicateItem) +
            // 435[9][0] + 275 ; le compteur de pitié natif
            // (+0xd80) monte quand même d'un cran.
            st.StrengthenFailCount++;
            if (_mission.Connected)
                await _mission.SendAgentDbaCommandAsync(st.CharId,
                    $"TempStrengthenFailTime = {st.StrengthenFailCount}",
                    token);
            await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
            {
                Field1 = 9, Field2 = 0,
            }, token);
            await _services.InventoryRepo.RemoveItemAsync(
                (int)st.CharId, p.TargetBag, p.TargetSlot);
            // 146 — item détruit par l'échec → ItemCollect.
            _services.NotifyItemChanged((int)st.CharId, target.ItemId);
            // 275 [contType][slot][contIdx]
            await session.SendAsync(new CNC_CZ_ClientDropItem
            {
                Field1 = 0, Field2 = p.TargetSlot, Field3 = p.TargetBag,
            }, token);
            // Pièce portée détruite → ses stats disparaissent du Profil.
            await InventoryHandlers.EmitSelfEquipRefreshAsync(_services,
                session, (int)st.CharId, token);
        }
    }

    /// <summary>CheckStrengthenItem @0x8087xxx — validations cible /
    /// recette / matériau / item de chance. Indices sacs = index de
    /// conteneur (BagId côté nous).</summary>
    private async Task<bool> CheckStrengthenItemAsync(EntryState st,
        int combineId, short tc, short ts, short mc, short ms,
        short cc, short cs)
    {
        if (tc < 0 || ts < 0) return false;

        var target = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, tc, ts);
        if (target == null || target.ItemId <= 0 || target.Quantity <= 0)
            return false;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == target.ItemId);
        if (def == null) return false;
        if (target.EnchantLevel >= 0xe) return false;   // max +14
        if (def.Kind == 0x10) return false;             // kind exclu

        var isWeapon = def.Kind is >= 0x11 and <= 0x21;
        var isShield = def.Kind == 0x22 || def.Kind == 34;
        if (combineId is >= 11 and <= 20)
        {
            // Armure : pas une arme, pas un bouclier.
            if (isWeapon || isShield) return false;
        }
        else if (combineId is >= 1 and <= 10)
        {
            // Arme : classe arme ou bouclier.
            if (!isWeapon && !isShield) return false;
        }
        else return false;

        // Matériau : pierre kind 46 (ou EquipLoc 46) présente à la
        // position indiquée ; pierres interdites 0x9d00/0x9d01 →
        // cible déjà >+9.
        if (mc < 0 || ms < 0) return false;
        var material = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, mc, ms);
        if (material == null || material.ItemId <= 0) return false;
        var matDef = _services.GameData.Items
            .FirstOrDefault(i => i.Id == material.ItemId);
        if (matDef == null || (matDef.Kind != 46 && matDef.EquipLoc != 46))
            return false;
        if ((matDef.Id == 0x9d00 || matDef.Id == 0x9d01) &&
            target.EnchantLevel <= 9)
            return false;

        // Item de chance : -1/-1 = absent ; sinon item dont l'enchant
        // d'usage est kind 8002 (0x1f42 — les « pierres de chance »
        // mall 40174/40471/41469… → enchant 55168 & co.). Bornes
        // non nulles → fenêtre lower ≤ niveau ≤ upper requise (le
        // natif envoie le texte 0x151c puis refuse).
        if (cc == -1 && cs == -1) return true;
        if (cc < 0 || cs < 0) return false;
        var chance = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, cc, cs);
        var chanceDef = chance == null ? null : _services.GameData.Items
            .FirstOrDefault(i => i.Id == chance.ItemId);
        var chanceEnch = chanceDef == null ? null : _services.GameData
            .Enchants.FirstOrDefault(e => e.Id == chanceDef.UseEnchantId);
        if (chanceEnch?.TriggerParams[0] != 0x1f42) return false;
        // Gate natif (chance+0x12a/+0x12c) : borne nulle → libre ;
        // sinon le niveau doit être dans la fenêtre.
        return chanceEnch.ChanceUpper == 0 || chanceEnch.ChanceLower == 0 ||
            (chanceEnch.ChanceLower <= target.EnchantLevel &&
             target.EnchantLevel <= chanceEnch.ChanceUpper);
    }

    /// <summary>NCID 120 CNE_CZ_ZoneServerStrengthenClose — ferme une
    /// fenêtre d'op. Codes natifs : 9→renfort, 10→combine, 22→métier,
    /// 28→cube. Si un cast de renfort était en cours → cancel + 434.</summary>
    private async Task HandleStrengthenCloseAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2) return;
        var code = BitConverter.ToInt16(payload, 0);
        if (code != st.ShopOpType) return;   // fenêtre inconnue → silence
        if (st.ShopOpType == 9 && st.PendingStrengthen != null)
            await CancelPendingStrengthenAsync(session, st, token);
        // 10 → op 0x27 (station combine) : CCombineOp::Cancel retire
        // l'enchant CombineItemID — le cast meurt + 434 [s16 10].
        if (st.ShopOpType == 10 && st.PendingCombine != null)
        {
            st.PendingCombine.Cts?.Cancel();
            st.PendingCombine = null;
            await SendCombineCancelAsync(session, token);
        }
        // 22 → op 0x39 (métier) : CLifeSkillOp::Cancel retire le cast
        // LifeSkillCastID + envoie 434 [s16 0x68] si un cast tournait.
        if (st.ShopOpType == 22 && st.PendingLifeSkill != null)
            await CancelPendingLifeSkillAsync(session, st, token);
        // 28 → op 0x52 (cube Horadric) : CCubeOp::Cancel retire
        // l'enchant CubeCrafting + envoie 434 [s16 0x1c].
        if (st.ShopOpType == 28 && st.PendingCube != null)
            await CancelPendingCubeAsync(session, st, token);
        // 31 → op 0x58 (boîte magique) : CMagicBoxOp::Cancel retire
        // l'enchant MagicBoxGoing + envoie 434 [s16 0x1f].
        if (st.ShopOpType == 31 && st.PendingMagicBox != null)
            await CancelPendingMagicBoxAsync(session, st, token);
        // 150/151 → ops lottery (0x54/0x56) : CLotteryOp::Cancel
        // retire l'enchant du cast + envoie 434 [s16 fenêtre] puis
        // UnregisterUser détruit l'op — LotteryOp repasse à -1.
        if (st.ShopOpType is 150 or 151)
        {
            if (st.PendingLottery != null)
                await CancelLotteryCastAsync(session, st, token);
            st.LotteryOp = -1;
        }
        st.ShopOpType = -1;
        st.ShopOpNpcUid = 0;
        st.ShopOpNpcHandle = 0;
    }

    /// <summary>67 CNE_CZ_ZoneServerInlayItem
    /// <c>[u16 f1][s16 equipLoc][u16 f3][s16 f4]</c> — Execute natif
    /// @0x0825f630 : gate op `CInlayShopOp` (char+0x670 type 0x23,
    /// ouverte par `inlay_shop`) + `TargetInTalkingRange` du NPC, puis
    /// `CItemFactory::InlayItem`… qui est un <b>STUB retournant 0</b>
    /// dans ce build natif (feature NPC morte — le vrai sertissage
    /// passe par UseItemToItemControl 0x2d, voir `InlayService`).
    /// Réponse fidèle : 371 ClientInlayItemResult [s16 0].</summary>
    private async Task HandleInlayItemAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (payload.Length < 8) return;
        if (st.ShopOpType != InlayShopOpType) return;
        // TargetInTalkingRange : le NPC d'inlay doit être à portée —
        // même tolérance 5u que les dialogues (EntityPos du handle).
        if (st.ShopOpNpcHandle != 0 &&
            st.EntityPos.TryGetValue((uint)st.ShopOpNpcHandle,
                out var npcPos))
        {
            var dx = npcPos.X - st.PosX; var dy = npcPos.Y - st.PosZ;
            if (dx * dx + dy * dy > 25f) return;
        }
        // Résultat natif : CItemFactory::InlayItem → 0 (stub).
        await session.SendAsync(new CNC_CZ_ClientInlayItemResult
        { Value = 0 }, token);
    }

    /// <summary>283 ClientModelModify natif (ctor @0x813f340,
    /// Serialize @0x813f8xx) :
    /// <c>[i32 charId][u8 char+0x4dc][i32 flags][u16 n][u32×n][u16 0]</c>
    /// — la liste = 1 valeur par flag (0x2000→bit casque,
    /// 0x4000→masque 10 bits) ; le second vecteur (+0x18) = copie
    /// char+0x7fc (toujours vide ici). Émis à l'acteur uniquement
    /// (NotifyClient natif).</summary>
    private static async Task SendModelModifyAsync(NetSession session,
        EntryState st, int flags, CancellationToken token,
        params int[] values)
    {
        var msg = new CNC_CZ_ClientModelModify
        { Field1 = (int)st.CharId, Field2 = 0, Field3 = flags };
        foreach (var v in values) msg.Field4.Add((uint)v);
        if ((flags & 0x8000) != 0)                         // flag 0x8000 :
            msg.Field4.Add((uint)(st.Vis4ad >> 2 & 1));    //   bit carte-char +0x4ad
        await session.SendAsync(msg, token);
    }

    /// <summary>Broadcast PlayerAction aux VOYEURS uniquement
    /// (BroadCastToCanSee natif — l'acteur ne le reçoit pas).
    /// Corps 16B = [u32 charId][u8 action][f32 facing][u8 p1][u8 p2]
    /// [i32 0][u8 1].</summary>
    private async Task BroadcastPlayerActionAsync(EntryState st,
        byte act, int p1, int p2, CancellationToken token)
    {
        var msg = new CNC_CZ_ClientPlayerAction
        {
            Field1 = st.CharId, Field2 = act,
            Field3 = st.Facing,                       // f32 : char+0x58
            Field4 = (byte)p1, Field5 = (byte)p2,
            Field6 = 0, Field7 = 1,
        };
        var stream = new OutBinStream();
        msg.Serialize(stream);
        var body = stream.ToArray();
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != st.NodeId || !ost.Migrated ||
                ost.InstanceGroup != st.InstanceGroup ||
                ost.CharId == st.CharId ||
                !ost.Sent.ContainsKey((uint)st.CharId)) continue;
            try { await s.SendPacketAsync(290, body, token); }
            catch { /* session morte */ }
        }
    }
}
