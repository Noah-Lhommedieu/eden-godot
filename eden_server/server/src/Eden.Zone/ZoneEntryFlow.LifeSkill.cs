using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  ZoneEntryFlow.LifeSkill — métiers (S_LifeSkill.ini), fidèle à
//  lapis::CLifeSkillFactory (re/ghidra/zone/decompiled.c).
//
//  NCIDs : 213 QueryLifeSkill → 543 ClientPlayerLifeSkill ;
//          214 LifeSkill [u8 op][u16 id][s16 sac][s16 slot] :
//            0 → JewelryAnalysis(Field3,Field4) ; 1/2 → Produce(Field2,mode) ;
//            3 → ForgotLifeSkill ; 4 → Apperception(manuel) ; 5 → LevelUp.
//  Flux : validation → op CLifeSkillOp (0x4c = notre ShopOpType 22) +
//         enchant LifeSkillCastID (cast 0x32 = 5s) → EndLifeSkill à
//         l'expiration → DoProduce / DoJewelryAnalysis.
//  Paquets : 433 ClientStrengthenCasting [s16 0x68|0x69][s16 0x32][s16 0] ;
//           434 ClientStrengthenItemCancel [s16 0x68] ;
//           435 ClientStrengthenItemResult [s16 0x68][s16 1][0×4] ;
//           544 ClientLifeSkillUpdate [u8 op][u16 id][u32 val]
//           (0=exp, 1=apprise, 2=oubliée, 3=remplacée) ;
//           316 TextOutputIndex (erreurs/gain).
// -----------------------------------------------------------------------------
public sealed partial class ZoneEntryFlow
{
    /// <summary>Durée du cast métier natif (EffectToLife arg 0x32 —
    /// en ×100 ms → 5 s), renvoyée telle quelle dans le 433.</summary>
    private const short LifeSkillCastUnits = 0x32;

    /// <summary>Requête métier validée en cours de cast
    /// (CLifeSkillOp::SetData : recipeId + type + paire sac/slot).</summary>
    internal sealed class PendingLifeSkill
    {
        public int RecipeId;        // op+0x08 — recette S_LifeSkill
        public int OpType;          // op+0x0c — 0=analyse, 1/2=production
        public short SrcBag = -1;   // op+0x10 — analyse : sac source
        public short SrcSlot = -1;  // op+0x12 — analyse : slot source
        public CancellationTokenSource? Cts;
    }

    // ============================================================
    //  NCID 213 — CNE_CZ_ZoneServerQueryLifeSkill::Execute @0x8278700
    //  Corps 543 natif (Serialize @0x822c…) :
    //    [s16 char+0xcc8 = life_skill]
    //    [u16 n][u16 recipeId ×n]
    //    [u16 n][u32 exp ×n]
    //    [u16 n][u32 due ×n]
    //  (le sérialiseur généré est faux : format plat — corps manuel).
    // ============================================================
    private async Task HandleQueryLifeSkillAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var msg = new CNC_CZ_ClientPlayerLifeSkill
        { Field1 = (short)st.LifeSkill };
        foreach (var (id, kv) in st.LifeSkillLearned)
        {
            msg.RecipeIds.Add((ushort)id);
            msg.Exps.Add((uint)kv.Exp);
            msg.Dues.Add((uint)kv.Due);
        }
        await session.SendAsync(msg, token);
    }

    // ============================================================
    //  NCID 214 — CNE_CZ_ZoneServerLifeSkill::Execute @0x8260e40 :
    //  acteur=perso (type 7) + op CLifeSkillOp (IsA()==0x4c) requis,
    //  puis dispatch sur l'octet d'opération.
    // ============================================================
    private async Task HandleLifeSkillAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.ShopOpType != 22 || payload.Length < 7)
        {
            EdenLog.Write($"[Zone] LifeSkill rejeté : shopOp={st.ShopOpType} len={payload.Length}");
            return;
        }
        var op = payload[0];                              // u8 : opération
        var recipeId = BitConverter.ToUInt16(payload, 1); // u16 : id recette
        var bag = BitConverter.ToInt16(payload, 3);       // s16 : sac (analyse)
        var slot = BitConverter.ToInt16(payload, 5);      // s16 : slot (analyse)

        switch (op)
        {
            case 0:
                await TryJewelryAnalysisAsync(session, st, bag, slot, token);
                break;
            case 1:
            case 2:
                await TryProduceAsync(session, st, recipeId, op, token);
                break;
            case 3:
                await ForgotLifeSkillAsync(session, st, recipeId, token);
                break;
            case 4:
                await ApperceptionLifeSkillAsync(session, st, recipeId,
                    manual: true, token);
                break;
            case 5:
                await LevelUpLifeSkillAsync(session, st, recipeId,
                    apperceptionOnly: false, token);
                break;
        }
    }

    // ============================================================
    //  Produce @0x80d0370 — validations puis armement du cast.
    //  Tout échec "de droit" est silencieux (le natif return sans
    //  paquet) sauf les erreurs explicites de ProduceCheck.
    // ============================================================
    private async Task TryProduceAsync(NetSession session, EntryState st,
        int recipeId, int opType, CancellationToken token)
    {
        if (!st.LifeSkillLearned.ContainsKey(recipeId)) return;
        if (st.PendingLifeSkill != null) return;          // enchant 211 actif
        if (st.Level < 0x1e) return;                      // char+0x470 < 30
        if (!_services.GameData.LifeSkillById
                .TryGetValue(recipeId, out var recipe)) return;
        if (st.Level < recipe.ReqLevel) return;
        if (st.Gold < recipe.Gold)
        {   // texte 0xeb1 — "or insuffisant"
            await SendTextIndexAsync(session, 0xeb1, token);
            return;
        }
        if (!CheckLifeSkillType(st, recipe.Group)) return;
        var mats = await ProduceCheckAsync(session, st, recipe, token);
        if (mats == null) return;

        st.PendingLifeSkill = new PendingLifeSkill
        { RecipeId = recipeId, OpType = opType };
        await ArmLifeSkillCastAsync(session, st, 0x68, token);
    }

    // ============================================================
    //  JewelryAnalysis @0x80d05f0 — la recette cible se résout via le
    //  lien item→recette (itemdata+0x178 posé par InitLifeSkillItem
    //  pour les groupes 1) ; Field3/Field4 = sac/slot source.
    // ============================================================
    private async Task TryJewelryAnalysisAsync(NetSession session,
        EntryState st, short bag, short slot, CancellationToken token)
    {
        var recipe = await AnalysisCheckAsync(session, st, bag, slot, token);
        if (recipe == null) { EdenLog.Write($"[Zone] analyse : item introuvable bag={bag} slot={slot}"); return; }
        if (!CheckLifeSkillType(st, recipe.Group)) { EdenLog.Write($"[Zone] analyse : type grp={recipe.Group} ls={st.LifeSkill}"); return; }
        // La recette d'analyse doit être apprise ET non maîtrisée
        // (+0x1c cap ≤ exp → texte via jumptable irrécupérable —
        // retour silencieux comme les autres refus du natif).
        if (!st.LifeSkillLearned.TryGetValue(recipe.Id, out var learned))
        { EdenLog.Write($"[Zone] analyse : recette {recipe.Id} non apprise"); return; }
        if (recipe.ExpCap <= learned.Exp) { EdenLog.Write($"[Zone] analyse : recette {recipe.Id} maîtrisée"); return; }
        if (st.PendingLifeSkill != null) { EdenLog.Write("[Zone] analyse : cast en cours"); return; }

        st.PendingLifeSkill = new PendingLifeSkill
        { RecipeId = recipe.Id, OpType = 0, SrcBag = bag, SrcSlot = slot };
        await ArmLifeSkillCastAsync(session, st, 0x69, token);
    }

    /// <summary>Arme le cast métier : 433 [s16 type][s16 0x32][s16 0]
    /// puis résolution différée (l'enchant LifeSkillCastID natif vit
    /// 0x32×100ms). type = 0x68 production, 0x69 analyse.</summary>
    private async Task ArmLifeSkillCastAsync(NetSession session,
        EntryState st, short winType, CancellationToken token)
    {
        var pending = st.PendingLifeSkill!;
        await session.SendAsync(new CNC_CZ_ClientStrengthenCasting
        {
            Field1 = winType,               // s16 : 0x68 prod / 0x69 analyse
            Field2 = LifeSkillCastUnits,    // s16 : durée ×100ms
            Field3 = 0,                     // s16 : non écrit nativement
        }, token);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        pending.Cts = cts;
        ScheduleOnLoop(TimeSpan.FromMilliseconds(
                LifeSkillCastUnits * 100),
            _ => ResolveLifeSkillAfterCastAsync(session, st, pending,
                cts.Token), cts.Token);
    }

    /// <summary>Fin de cast → EndLifeSkill @0x80d28a0 : op+0x14=0 puis
    /// dispatch type 0 → DoJewelryAnalysis, 1/2 → DoProduce.</summary>
    private async Task ResolveLifeSkillAfterCastAsync(NetSession session,
        EntryState st, PendingLifeSkill p,
        CancellationToken ct)
    {
        try
        {
            if (st.PendingLifeSkill != p) return;
            st.PendingLifeSkill = null;
            if (st.ShopOpType != 22) return;
            if (p.OpType == 0)
                await DoJewelryAnalysisAsync(session, st, p, ct);
            else
                await DoProduceAsync(session, st, p, ct);
        }
        catch (OperationCanceledException) { /* StrengthenClose 22 */ }
        catch (Exception ex)
        {
            EdenLog.Write($"[Zone] EndLifeSkill: {ex.Message}");
        }
    }

    /// <summary>Annulation natif CLifeSkillOp::Cancel @0x856da60 :
    /// si le cast était actif → retrait enchant + 434 [s16 0x68].</summary>
    private async Task CancelPendingLifeSkillAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (st.PendingLifeSkill == null) return;
        st.PendingLifeSkill.Cts?.Cancel();
        st.PendingLifeSkill = null;
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemCancel
        { Value = 0x68 }, token);
    }

    // ============================================================
    //  DoProduce @0x80d23b0 — consommation des matériaux + or,
    //  exp (mode 2), ajout du produit, résultat 435.
    // ============================================================
    private async Task DoProduceAsync(NetSession session, EntryState st,
        PendingLifeSkill p, CancellationToken token)
    {
        if (st.Level <= 0x1d || p.RecipeId == 0) return;
        if (!_services.GameData.LifeSkillById
                .TryGetValue(p.RecipeId, out var recipe)) return;
        if (st.Level < recipe.ReqLevel)
        {   // texte 0xfe1 — niveau insuffisant
            await SendTextIndexAsync(session, 0xfe1, token);
            return;
        }
        var mats = await ProduceCheckAsync(session, st, recipe, token);
        if (mats == null || mats.Count == 0) return;
        // op+0xc != 2 → pas d'exp ; == 2 → GainLifeSkillExp requis.
        if (p.OpType == 2 && !await GainLifeSkillExpAsync(
                session, st, recipe, token))
            return;

        foreach (var (matId, qty) in mats)
        {
            // DropItemByItemID(0, id, qty, 0x39) — échec → abort natif
            // (goto cleanup, aucun paquet de résultat).
            if (!await DropItemsByItemIdAsync(session, st, matId, qty,
                    token))
                return;
            // texte 0x534 "%s x %d" — nom du matériau consommé.
            var matDef = _services.GameData.Items
                .FirstOrDefault(i => i.Id == matId);
            if (matDef != null)
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                {
                    Field1 = 0x534,
                    Field2 = $"{matDef.Name} x {qty}",
                }, token);
        }

        st.Gold = (int)await _services.Characters.AddGoldAsync(
            (int)st.CharId, -recipe.Gold);
        await SendAttrAsync(session, st.CharId, 0x02, st.Gold, 0, token);

        // AddItemFromLifeSkill(itemId, count) — ajout natif + 274.
        var touched = new List<(int BagId, int Slot, bool Merged)>();
        var (ok, _, bag, slot) = await _services.Inventory.AddItemAsync(
            (int)st.CharId, recipe.ItemId, Math.Max(recipe.Count, 1),
            0, 0, false, touched);
        if (ok)
        {
            // Merge → 291, nouveau slot → 274 (règle native —
            // NotifyItemSlotsAsync / EmitItemSlotsAsync).
            await Codecs.NativeItemCodec.EmitItemSlotsAsync(session,
                _services.InventoryRepo, (int)st.CharId, touched, token);
            // Succès de collection — AddItemFromLifeSkill natif
            // appelle SetItemAchievement sur le produit (zone
            // @0x80b1xxx).
            await TryGrantItemAchievementAsync(session, st,
                recipe.ItemId);
            // 146 — produit de métier → ItemCollect.
            _services.NotifyItemChanged((int)st.CharId, recipe.ItemId);
        }

        // 435 [s16 0x68][s16 1][s16 0×4] — succès métier.
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
        { Field1 = 0x68, Field2 = 1 }, token);
    }

    // ============================================================
    //  DoJewelryAnalysis @0x80d2690 — revalidation, exp, consommation
    //  de `recipe.Count` unités de l'item source, résultat 435.
    // ============================================================
    private async Task DoJewelryAnalysisAsync(NetSession session,
        EntryState st, PendingLifeSkill p, CancellationToken token)
    {
        if (st.Level <= 0x1d || p.RecipeId == 0
            || p.SrcBag == -1 || p.SrcSlot == -1) return;
        var recipe = await AnalysisCheckAsync(session, st, p.SrcBag,
            p.SrcSlot, token);
        if (recipe == null || p.RecipeId != recipe.Id) return;
        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, p.SrcBag, p.SrcSlot);
        if (item == null) return;

        // GainLifeSkillExp réussi → ItemConsume(sac, slot, recipe.Count).
        if (await GainLifeSkillExpAsync(session, st, recipe, token))
        {
            await _services.Inventory.RemoveItemAsync(
                (int)st.CharId, p.SrcBag, p.SrcSlot, recipe.Count);
            // 275 [s16 sac][s16 loc][s16 n] — retrait côté client.
            await session.SendPacketAsync(275, Concat(
                BitConverter.GetBytes(p.SrcBag),
                BitConverter.GetBytes(p.SrcSlot),
                BitConverter.GetBytes((short)Math.Max(recipe.Count, 0))),
                token);
        }
        await session.SendAsync(new CNC_CZ_ClientStrengthenItemResult
        { Field1 = 0x68, Field2 = 1 }, token);
    }

    // ============================================================
    //  ProduceCheck @0x80d0010 — capacité de sortie (slot libre si
    //  aucun stack absorbable) puis présence des matériaux
    //  (CheckItemNum). Erreurs : texte + 434 [0x68]. Retourne la
    //  liste (itemId, qté) validée ou null.
    // ============================================================
    private async Task<List<(int ItemId, int Qty)>?> ProduceCheckAsync(
        NetSession session, EntryState st,
        Eden.Core.Data.Parsers.LifeSkillParser.Recipe recipe,
        CancellationToken token)
    {
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        bool Combines(int itemId, int qty) => items.Any(i =>
            i.ItemId == itemId && !i.IsEquipped
            && i.Quantity + qty <= _services.Inventory.MaxStackOf(itemId));

        var anyCombine = recipe.Materials.Any(m => Combines(m.ItemId, m.Qty));
        if (!anyCombine)
        {
            var free = await _services.InventoryRepo
                .GetAnyAvailableSlotAsync((int)st.CharId);
            if (free == null)
            {   // texte 0x4f9 — pas de place en sac
                await SendTextIndexAsync(session, 0x4f9, token);
                await session.SendAsync(
                    new CNC_CZ_ClientStrengthenItemCancel
                    { Value = 0x68 }, token);
                return null;
            }
        }

        var mats = new List<(int, int)>();
        foreach (var (matId, qty) in recipe.Materials)
        {
            if (matId == 0) continue;
            if (!_services.GameData.Items.Any(i => i.Id == matId))
                return null;                        // item data absent → 0
            var have = items.Where(i => i.ItemId == matId && !i.IsEquipped)
                .Sum(i => i.Quantity);
            if (have < qty)
            {   // texte 0x9f4 — matériau manquant
                await SendTextIndexAsync(session, 0x9f4, token);
                await session.SendAsync(
                    new CNC_CZ_ClientStrengthenItemCancel
                    { Value = 0x68 }, token);
                return null;
            }
            mats.Add((matId, qty));
        }
        return mats;
    }

    // ============================================================
    //  AnalysisCheck @0x80d0250 — item source présent, lien
    //  item→recette, niveau d'item ≥ exigence recette (col4).
    // ============================================================
    private async Task<Eden.Core.Data.Parsers.LifeSkillParser.Recipe?>
        AnalysisCheckAsync(NetSession session, EntryState st,
        short bag, short slot, CancellationToken token)
    {
        var item = await _services.InventoryRepo.GetItemAsync(
            (int)st.CharId, bag, slot);
        if (item == null) return null;
        if (!_services.GameData.LifeSkillByItemId
                .TryGetValue(item.ItemId, out var recipe))
        {   // texte 0xf63 — objet non analysable
            await SendTextIndexAsync(session, 0xf63, token);
            return null;
        }
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == item.ItemId);
        if ((def?.RequiredLevel ?? 0) < recipe.Count)
        {   // texte 0xfe4 — niveau d'objet insuffisant
            await SendTextIndexAsync(session, 0xfe4, token);
            return null;
        }
        return recipe;
    }

    // ============================================================
    //  GainLifeSkillExp @0x80d1f30 — gain randomisé, clamp au cap,
    //  apperception (groupe 1) / level-up (2-5) à la maîtrise,
    //  persistance + 316 (0xf99 "%s,%d") + 544 [0][id][exp].
    // ============================================================
    private async Task<bool> GainLifeSkillExpAsync(NetSession session,
        EntryState st, Eden.Core.Data.Parsers.LifeSkillParser.Recipe recipe,
        CancellationToken token)
    {
        if (st.Level <= 0x1d) return false;
        if (!st.LifeSkillLearned.TryGetValue(recipe.Id, out var cur))
            return false;

        var gain = recipe.ExpBase;
        if (recipe.ExpVar <= recipe.ExpBase)
            gain = recipe.ExpBase - recipe.ExpVar
                 + Random.Shared.Next(recipe.ExpVar * 2 + 1);

        // Natif @0x80d1f30 : *puVar3 = nouvel exp (clampé au cap)
        // commit AVANT ApperceptionLifeSKill/LevelUpLifeSkill — sinon
        // leur garde `exp < cap` lit l'ancienne valeur et abandonne.
        var mastered = cur.Exp + gain >= recipe.ExpCap;
        var newExp = mastered ? recipe.ExpCap : cur.Exp + gain;
        var gained = newExp - cur.Exp;
        st.LifeSkillLearned[recipe.Id] = (newExp, cur.Due);
        if (mastered)
        {
            if (recipe.Group == 1)
                await ApperceptionLifeSkillAsync(session, st,
                    (ushort)recipe.Id, manual: false, token);
            else if (recipe.Group is > 0 and < 6)
                await LevelUpLifeSkillAsync(session, st,
                    (ushort)recipe.Id, apperceptionOnly: false, token);
        }
        // UpdateLifeSkillExp — natif via mission, direct DB ici.
        await _services.LifeSkills.UpdateExpAsync(
            (int)st.CharId, recipe.Id, newExp, cur.Due);
        // 316 TextOutputIndex 0xf99 — args "%s,%d" (nom recette, gain).
        await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        { Field1 = 0xf99, Field2 = $"{recipe.Name},{gained}" }, token);
        // 544 [u8 0][u16 recipeId][u32 exp]
        await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
        { Field1 = 0, Field2 = (ushort)recipe.Id,
            Field3 = (uint)newExp }, token);
        return true;
    }

    // ============================================================
    //  ApperceptionLifeSKill @0x80d08e0 — maîtrise → choix pondéré
    //  dans la map de résultats. Groupe 1 : remplacement de la
    //  recette (544 op3) ; groupes 2-5 : insertion (544 op1).
    // ============================================================
    private async Task ApperceptionLifeSkillAsync(NetSession session,
        EntryState st, int recipeId, bool manual,
        CancellationToken token)
    {
        if (st.Level < 0x1e) return;
        if (!st.LifeSkillLearned.ContainsKey(recipeId)) return;
        if (!_services.GameData.LifeSkillById
                .TryGetValue(recipeId, out var recipe)) return;
        // Demande manuelle (op 4) : groupe 1 → retour immédiat natif.
        if (manual && recipe.Group == 1) return;
        if (!CheckLifeSkillType(st, recipe.Group)) return;

        var cur = st.LifeSkillLearned[recipeId];
        if (cur.Exp < recipe.ExpCap)
        {   // texte 0xf84 — maîtrise non atteinte
            await SendTextIndexAsync(session, 0xf84, token);
            return;
        }
        // Si l'un des résultats est déjà appris → texte 0xfe3 "%s".
        foreach (var (_, resId) in recipe.Results)
        {
            if (st.LifeSkillLearned.ContainsKey(resId))
            {
                var res = _services.GameData.LifeSkillById
                    .GetValueOrDefault(resId);
                await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                { Field1 = 0xfe3, Field2 = res?.Name ?? "" }, token);
                return;
            }
        }
        if (recipe.Results.Count == 0 || recipe.TotalWeight <= 0) return;
        // lower_bound natif : premier poids cumulé ≥ tirage.
        var roll = Random.Shared.Next(recipe.TotalWeight);
        var picked = recipe.Results.FirstOrDefault(r => r.CumulWeight >= roll);
        if (picked.RecipeId == 0) picked = recipe.Results[^1];
        if (!_services.GameData.LifeSkillById
                .TryGetValue(picked.RecipeId, out var newRecipe)) return;
        if (st.LifeSkillLearned.ContainsKey(picked.RecipeId)) return;

        if (recipe.Group == 1)
        {
            st.LifeSkillLearned.Remove(recipeId);
            st.LifeSkillLearned[picked.RecipeId] = (0, 0);
            await _services.LifeSkills.ReplaceAsync(
                (int)st.CharId, recipeId, picked.RecipeId);
            // 544 [u8 3][u16 ancienId][u32 nouvelId]
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 3, Field2 = (ushort)recipeId,
                Field3 = (uint)picked.RecipeId }, token);
        }
        else
        {
            st.LifeSkillLearned[picked.RecipeId] = (0, 0);
            await _services.LifeSkills.InsertAsync(
                (int)st.CharId, picked.RecipeId);
            // 544 [u8 1][u16 nouvelId][u32 0]
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 1, Field2 = (ushort)picked.RecipeId,
                Field3 = 0 }, token);
        }
        _ = newRecipe; // (le natif ne s'en sert qu'au log)
    }

    // ============================================================
    //  LevelUpLifeSkill @0x80d16a0 — maîtrise → apprentissage de la
    //  recette suivante (col10). Groupe 1 : remplacement (544 op3) ;
    //  groupes 2-5 : insertion si pas déjà apprise (544 op1).
    // ============================================================
    private async Task LevelUpLifeSkillAsync(NetSession session,
        EntryState st, int recipeId, bool apperceptionOnly,
        CancellationToken token)
    {
        if (st.Level < 0x1e) return;
        if (!st.LifeSkillLearned.ContainsKey(recipeId)) return;
        if (!_services.GameData.LifeSkillById
                .TryGetValue(recipeId, out var recipe)) return;
        // param_3 natif : mode "apperception" → groupe 1 seul.
        if (apperceptionOnly && recipe.Group != 1) return;
        if (!CheckLifeSkillType(st, recipe.Group)) return;

        var cur = st.LifeSkillLearned[recipeId];
        if (cur.Exp < recipe.ExpCap)
        {   // texte 0xf83 — maîtrise non atteinte
            await SendTextIndexAsync(session, 0xf83, token);
            return;
        }
        if (!_services.GameData.LifeSkillById
                .TryGetValue(recipe.NextId, out _)) return;
        var nextId = (ushort)recipe.NextId;

        if (recipe.Group == 1)
        {
            if (st.LifeSkillLearned.ContainsKey(nextId)) return;
            st.LifeSkillLearned.Remove(recipeId);
            st.LifeSkillLearned[nextId] = (0, 0);
            await _services.LifeSkills.ReplaceAsync(
                (int)st.CharId, recipeId, nextId);
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 3, Field2 = (ushort)recipeId,
                Field3 = nextId }, token);
        }
        else
        {
            if (st.LifeSkillLearned.ContainsKey(nextId)) return;
            st.LifeSkillLearned[nextId] = (0, 0);
            await _services.LifeSkills.InsertAsync(
                (int)st.CharId, nextId);
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 1, Field2 = nextId, Field3 = 0 }, token);
        }
    }

    // ============================================================
    //  ForgotLifeSkill @0x80d1020 — oubli : groupe 1 remplace par la
    //  recette précédente (col11 ≥ 2) ; groupes 2-5 suppriment la
    //  recette et remettent l'exp de la précédente à 0.
    // ============================================================
    private async Task ForgotLifeSkillAsync(NetSession session,
        EntryState st, int recipeId, CancellationToken token)
    {
        if (st.Level < 0x1e) return;
        if (!st.LifeSkillLearned.ContainsKey(recipeId)) return;
        if (!_services.GameData.LifeSkillById
                .TryGetValue(recipeId, out var recipe)) return;
        if (!CheckLifeSkillType(st, recipe.Group)) return;
        // col11 < 2 → recette de base, non oubliable (retour natif).
        if (recipe.PrevId < 2) return;
        if (!_services.GameData.LifeSkillById
                .ContainsKey(recipe.PrevId)) return;
        var prevId = (ushort)recipe.PrevId;

        if (recipe.Group == 1)
        {
            if (st.LifeSkillLearned.ContainsKey(prevId)) return;
            st.LifeSkillLearned.Remove(recipeId);
            st.LifeSkillLearned[prevId] = (0, 0);
            await _services.LifeSkills.ReplaceAsync(
                (int)st.CharId, recipeId, prevId);
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 3, Field2 = (ushort)recipeId,
                Field3 = prevId }, token);
        }
        else
        {
            // Le natif retire d'abord l'entrée, exige ensuite que la
            // précédente soit apprise (sinon retour sans paquets).
            st.LifeSkillLearned.Remove(recipeId);
            await _services.LifeSkills.DeleteAsync(
                (int)st.CharId, recipeId);
            if (!st.LifeSkillLearned.ContainsKey(prevId)) return;
            st.LifeSkillLearned[prevId] = (0, 0);
            await _services.LifeSkills.UpdateExpAsync(
                (int)st.CharId, prevId, 0, 0);
            // 544 [u8 2][u16 ancienId][u32 0] — oubli.
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 2, Field2 = (ushort)recipeId, Field3 = 0 }, token);
            // 544 [u8 0][u16 précédent][u32 exp=0] — resync.
            await session.SendAsync(new CNC_CZ_ClientLifeSkillUpdate
            { Field1 = 0, Field2 = prevId, Field3 = 0 }, token);
        }
    }

    // ============================================================
    //  Helpers
    // ============================================================

    /// <summary>CheckLifeSkillType @0x80cfeb0 — compatibilité
    /// profession/recette. Le nibble natif (flags octet0&gt;&gt;4 :
    /// {0,1}→grp1, {2,3}→grp2, {4,5}→grp3, {8,9}→grp4, {6,7}→grp5)
    /// est recollé depuis notre colonne `life_skill` (groupe 1-5 ;
    /// -1/0 → nibble 0). La paire de nibbles par groupe est gardée
    /// littérale — tier avancé (x+1) accepté comme le natif.</summary>
    private static bool CheckLifeSkillType(EntryState st, int group)
    {
        var nib = st.LifeSkill > 0 ? st.LifeSkill << 4 : 0;
        return group switch
        {
            1 => nib is 0 or 0x10,
            2 => nib is 0x20 or 0x30,
            3 => nib is 0x40 or 0x50,
            4 => nib is 0x80 or 0x90,
            5 => nib is 0x60 or 0x70,
            _ => false,
        };
    }

    /// <summary>DropItemByItemID(conteneur 0) natif — consomme `qty`
    /// exemplaires d'un itemId à travers les sacs (275 par slot
    /// touché, comme les retraits de quête). Retourne false si le
    /// total possédé est insuffisant (abort natif).</summary>
    private async Task<bool> DropItemsByItemIdAsync(NetSession session,
        EntryState st, int itemId, int qty, CancellationToken token)
    {
        if (qty <= 0) return true;
        var items = await _services.InventoryRepo
            .GetItemsByCharacterIdAsync((int)st.CharId);
        if (items.Where(i => i.ItemId == itemId && !i.IsEquipped)
                 .Sum(i => i.Quantity) < qty) return false;
        var left = qty;
        foreach (var it in items.Where(
                     i => i.ItemId == itemId && !i.IsEquipped))
        {
            if (left <= 0) break;
            var take = Math.Min(left, Math.Max(1, it.Quantity));
            var newCount = it.Quantity - take;
            if (!await _services.Inventory.RemoveItemAsync(
                    (int)st.CharId, it.BagId, it.Slot, take)) continue;
            // Wire natif DropItemByItemID @0x8090b50 : pile vidée →
            // 275 ClientDropItem [s16 contType][s16 loc][s16 contIdx]
            // (le paquet ne porte PAS de quantité) ; pile résiduelle
            // → 291 ClientModifyItemDurability [contType][loc]
            // [newCount][maxDur][contIdx][0]. L'ancien corps
            // [sac][loc][qté] décalait les champs → consommation
            // invisible côté client (bug live « ressources du craft
            // non perdues »).
            if (newCount > 0)
                await session.SendAsync(
                    new CNC_CZ_ClientModifyItemDurability
                    {
                        Field1 = 0,                     // contType sac
                        Field2 = (short)it.Slot,        // loc
                        Field3 = (short)newCount,       // item+0xc
                        Field4 = (short)it.MaxDurability, // item+0xe
                        Field5 = (short)it.BagId,       // contIdx
                        Field6 = 0,
                    }, token);
            else
                await session.SendAsync(new CNC_CZ_ClientDropItem
                {
                    Field1 = 0, Field2 = (short)it.Slot,
                    Field3 = (short)it.BagId,
                }, token);
            left -= take;
        }
        // 146 — retrait multi-slots → ItemCollect
        // (UpdateQuestItemStatus natif, voie delete).
        if (left < qty)
            _services.NotifyItemChanged((int)st.CharId, itemId);
        return left <= 0;
    }
}
