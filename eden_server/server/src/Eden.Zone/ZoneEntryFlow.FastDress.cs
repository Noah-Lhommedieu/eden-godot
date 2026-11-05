using Eden.Core.Game.Inventory;
using Eden.Core.Logging;
using Eden.Zone.Codecs;
using Eden.Zone.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    // ==================== Tenues rapides (fast dress) ====================
    //
    // Transcription du sous-système natif :
    //   CNE_CZ_ZoneServerEnterNodeFastDress::Execute zone @0x0827af50
    //     → CNC_CZ_ClientEnterNodeFastDress (593) = tous les records
    //     LIVE (map char+0x58c), ctor @0x08232f10, Serialize @0x08232e70.
    //   CPlayerFactory::CheckFastDressUseItem zone @0x080ff560
    //     (= RecordFastDressInfo 246 — validation + bind + écriture
    //     dans la map WORKING char+0x5a4 → 594 mode 4 ; échecs →
    //     textes 0x5f7/0x59e/0x5a3 + mode 2, échec bind → mode 1).
    //   CPlayerFactory::FastDressCheckEquip zone @0x080ed390
    //     (EquipLoc → clé dress autorisée + conflits 1M/2M + clears).
    //   CPlayerFactory::CheckSrcDataExistPlus zone @0x080ed2d0.
    //   CPlayerFactory::RemoveFastDressInfo zone @0x080ffd30
    //     (clear du record working → 594 mode 5 + page working).
    //   lapis::RemoveAllFastDressInfo zone @0x0810b4f0
    //     (clear des 24 clés working → 594 mode 4 + page working).
    //   CNE_CZ_ZoneServerUpdateFastDressPage::Execute (247)
    //     → working page := live page → 594 mode 3 (records live).
    //   CItemFactory::SaveFastDressInfo (245)
    //     → live := working pour les 24 clés + UPDATE fast_dress →
    //     594 mode 0 (records live).
    //   CPlayerFactory::ApplyFastDress zone @0x08124f00 (244)
    //     → swaps d'équipement via SwitchItemLocation + enchant
    //     CEffectFactory::FastDressID (46690, .rodata 0x087b4b0e).
    //   CNC_CZ_ClientUpdateFastDressData (594) : Serialize @0x08232e90
    //     [s16 mode] puis si mode∉{1,7,8} [s16 n][i32 page][n×6×i32] ;
    //     ctor @0x0813e6d0 : modes {0,3}→records live, {2,4}→working,
    //     {1,7,8}→mode seul ; RemoveFastDressInfo construit mode 5 à la
    //     main avec les records working.
    //   Mission : CExecutionerDBO::LoadFastDress @0x08156820 seed 72
    //     lignes (24 clés × 3 pages) quand fast_dress est vide —
    //     reproduit dans FastDressRepository (notre zone persiste
    //     directement, pas de relais DBA comme le natif).
    //
    // Maps natives : char+0x58c = page→(clé→SFastDressInfo) LIVE
    // (appliquée/persistée), char+0x5a4 = WORKING (édition UI).
    // char+0x5bc = clé→index de bit du masque d'apply (bit i =
    // FastDressEquipLoc[i]). char+0x4b8 = flag « apply en cours ».

    /// <summary>Enchant posé à la fin d'un apply réussi —
    /// <c>CEffectFactory::FastDressID</c> (.rodata zone 0x087b4b0e =
    /// 46690, ligne S_Enchant existante).</summary>
    private const int FastDressEnchantId = 46690;

    /// <summary>Conteneur natif inventaire (IItemContainer type 0).</summary>
    private const int FastDressConInventory = 0;

    /// <summary>Conteneur natif équipement (type 1).</summary>
    private const int FastDressConEquipment = 1;

    /// <summary>Gate commun des 6 commandes — le natif exige
    /// `ILifeEntity::IsA() == 7` dans chaque Execute (243-248) :
    /// <c>CCharacter::IsA()</c> zone @0x0844dc30 retourne 7 —
    /// « l'expéditeur est un personnage joueur », <b>pas</b> un type
    /// de nœud. La vérification est donc toujours vraie ici (le
    /// packet est bindé à un perso) ; il ne reste que la présence de
    /// l'état chargé. Silencieux en cas de refus, comme le natif.</summary>
    private bool FastDressAllowed(EntryState st)
        => st.FastDress != null;

    // ------------------------------------------------------------
    //  Chargement (équivalent CNE_DBA_ClientFastDress 846 natif)
    // ------------------------------------------------------------

    /// <summary>Charge `fast_dress` à l'entrée en zone — reproduit
    /// <c>CNE_DBA_ClientFastDress::Execute</c> + <c>LoadFastDress</c>
    /// natifs : les 3 pages des deux maps (live + working) reçoivent
    /// les records lus (les 72 lignes seedées par le repo couvrent les
    /// 24 clés).</summary>
    private async Task LoadFastDressAsync(EntryState st,
        CancellationToken token)
    {
        var rows = await _services.FastDressRepo.LoadAsync(
            (int)st.CharId, token);
        var state = FastDressState.Create();
        foreach (var r in rows)
        {
            if (r.Page is < 0 or >= FastDressSlots.PageCount) continue;
            if (state.Saved[r.Page].ContainsKey(r.TarLoc)) continue;
            state.Saved[r.Page][r.TarLoc] = r;
            var copy = FastDressRecord.Empty(r.Page, r.TarLoc);
            copy.CopyFrom(r);
            state.Edited[r.Page][r.TarLoc] = copy;
        }
        // Les 24 clés existent toujours dans chaque page working
        // (les Execute Record/Remove font des lookups directs).
        for (var p = 0; p < FastDressSlots.PageCount; p++)
            foreach (var key in FastDressSlots.EquipLocs)
            {
                state.Edited[p].TryGetValue((int)key, out _);
                if (!state.Edited[p].ContainsKey((int)key))
                    state.Edited[p][(int)key] =
                        FastDressRecord.Empty(p, (int)key);
            }
        st.FastDress = state;
    }

    // ------------------------------------------------------------
    //  243 — EnterNodeFastDress → 593 (records live)
    // ------------------------------------------------------------

    /// <summary>Ouverture de la fenêtre — <c>EnterNodeFastDress::
    /// Execute</c> : gate type 7, envoie tous les records LIVE des 3
    /// pages (593). <c>Action(5)</c> natif = compteur de délai
    /// interne, sans paquet.</summary>
    private async Task HandleFastDressEnterAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        var records = st.FastDress!.Saved.Values
            .SelectMany(m => m.Values).ToList();
        await session.SendPacketAsync(593,
            FastDressPackets.BuildEnterNode(records), token);
    }

    // ------------------------------------------------------------
    //  245 — SaveFastDressData : working→live + persistance → mode 0
    // ------------------------------------------------------------

    /// <summary><c>CItemFactory::SaveFastDressInfo</c> natif : copie
    /// les 24 clés de la page WORKING vers la LIVE, UPDATE
    /// `fast_dress` pour chacune, puis 594 mode 0 avec les records
    /// live de la page.</summary>
    private async Task HandleFastDressSaveAsync(NetSession session,
        EntryState st, int page, CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        if (page is < 0 or >= FastDressSlots.PageCount) return;
        var edited = st.FastDress!.Edited[page];
        var saved = st.FastDress.Saved[page];
        foreach (var key in FastDressSlots.EquipLocs)
        {
            if (!edited.TryGetValue((int)key, out var src)) continue;
            if (!saved.TryGetValue((int)key, out var dst))
            {
                dst = FastDressRecord.Empty(page, (int)key);
                saved[(int)key] = dst;
            }
            dst.CopyFrom(src);
            await _services.FastDressRepo.UpdateRecordAsync(
                (int)st.CharId, dst);
        }
        await session.SendPacketAsync(594,
            FastDressPackets.BuildUpdate(0, page,
                saved.Values.OrderBy(r => r.TarLoc)), token);
    }

    // ------------------------------------------------------------
    //  247 — UpdateFastDressPage : working := live → mode 3
    // ------------------------------------------------------------

    /// <summary><c>UpdateFastDressPage::Execute</c> natif : restaure
    /// la page WORKING depuis la LIVE pour les 24 clés (annulation de
    /// l'édition en cours) → 594 mode 3 avec les records live.</summary>
    private async Task HandleFastDressPageUpdateAsync(
        NetSession session, EntryState st, int page,
        CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        if (page is < 0 or >= FastDressSlots.PageCount) return;
        var edited = st.FastDress!.Edited[page];
        var saved = st.FastDress.Saved[page];
        foreach (var key in FastDressSlots.EquipLocs)
        {
            var src = saved.TryGetValue((int)key, out var s)
                ? s : FastDressRecord.Empty(page, (int)key);
            if (!edited.TryGetValue((int)key, out var dst))
            {
                dst = FastDressRecord.Empty(page, (int)key);
                edited[(int)key] = dst;
            }
            dst.CopyFrom(src);
        }
        await session.SendPacketAsync(594,
            FastDressPackets.BuildUpdate(3, page,
                saved.Values.OrderBy(r => r.TarLoc)), token);
    }

    // ------------------------------------------------------------
    //  248 — RemoveFastDressInfo [s16 all][i32 page][i32 clé]
    // ------------------------------------------------------------

    /// <summary><c>RemoveFastDressInfo::Execute</c> natif : all!=1 →
    /// clear du record working à la clé → 594 mode 5 + page working ;
    /// all==1 → <c>RemoveAllFastDressInfo</c> : clear des 24 clés →
    /// 594 mode 4 + page working.</summary>
    private async Task HandleFastDressRemoveAsync(NetSession session,
        EntryState st, short all, int page, int key,
        CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        if (page is < 0 or >= FastDressSlots.PageCount) return;
        var edited = st.FastDress!.Edited[page];
        if (all == 1)
        {
            foreach (var k in FastDressSlots.EquipLocs)
                if (edited.TryGetValue((int)k, out var r))
                    r.Clear();
            await session.SendPacketAsync(594,
                FastDressPackets.BuildUpdate(4, page,
                    edited.Values.OrderBy(r => r.TarLoc)), token);
            return;
        }
        if (edited.TryGetValue(key, out var rec)) rec.Clear();
        await session.SendPacketAsync(594,
            FastDressPackets.BuildUpdate(5, page,
                edited.Values.OrderBy(r => r.TarLoc)), token);
    }

    // ------------------------------------------------------------
    //  246 — RecordFastDressInfo [i32 con][i32 slot][i32 idx]
    //        [i32 clé][i32 page] → CheckFastDressUseItem
    // ------------------------------------------------------------

    /// <summary>Enregistrement d'un item dans la page working —
    /// <c>RecordFastDressInfo::Execute</c> →
    /// <c>CPlayerFactory::CheckFastDressUseItem</c> @0x080ff560 :
    /// validations natives, bind (<c>FastDressDoItemBind</c>), écriture
    /// du record working → 594 mode 4 ; échecs → textes + mode 2/1.</summary>
    private async Task HandleFastDressRecordAsync(NetSession session,
        EntryState st, int conType, int slot, int conIdx, int tarKey,
        int page, CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        if (page is < 0 or >= FastDressSlots.PageCount) return;
        var edited = st.FastDress!.Edited[page];

        // Gates d'état natifs : char+0x3b bit 0x40 (versus) et 0x20
        // (mort) → return 0 silencieux → Execute envoie 0x32a + mode 2.
        if (st.Dead || InVersusScene(st))
        {
            await SendTextIndexAsync(session, 0x32a, token);
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        // Item source : le natif lit le conteneur indexé par conIdx
        // dans la map char+0x4e4 (sacs d'inventaire) puis le slot —
        // le client envoie conType=0 pour un drag sac → dress. On
        // supporte aussi 1 (déjà équipé) pour la robustesse.
        var item = conType switch
        {
            FastDressConInventory =>
                await _services.InventoryRepo.GetItemAsync(
                    (int)st.CharId, conIdx, slot),
            FastDressConEquipment =>
                await _services.InventoryRepo.GetEquipmentItemAsync(
                    (int)st.CharId, conIdx, slot),
            _ => null,
        };
        if (item == null)
        {
            await SendTextIndexAsync(session, 0x5f7, token);
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == item.ItemId);
        if (def == null)
        {
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        // Groupe d'armure natif dérivé du kind :
        // {3,6,9,12,15}→2, {2,5,8,11,14}→3, {1,4,7,10,13}→4,
        // kinds 16-39 → 0, autre → -1 (CheckFastDressUseItem).
        var group = FastDressSlots.GroupOfKind(def.Kind);
        // FastDressCheckEquip : EquipLoc → clé autorisée + conflits +
        // clears automatiques dans la page working.
        if (!FastDressCheckEquip(edited, def.EquipLoc, group, tarKey,
                item.ItemId))
        {
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        if (group == -1)
        {
            await SendTextIndexAsync(session, 0x59e, token);
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        // CheckLimit natif — sous-ensemble fidèle : niveau requis de
        // l'item (les limites de classe/genre natives lisent des
        // colonnes S_Item non parsées — documenté).
        if (def.RequiredLevel > 0 && st.Level < def.RequiredLevel)
        {
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        // Conflit même item_id dans la clé jumelle — EquipLoc
        // {12,19,20} (flag +0x30<0 natif, posé quand EquipLoc∈{12,19,
        // 20} — CItemQuery::Deserialize).
        if (ItemIdConflictsInPair(edited, def.EquipLoc, item.ItemId))
        {
            await SendTextIndexAsync(session, 0x5a3, token);
            await SendFastDressModeAsync(session, 2, page, edited,
                token);
            return;
        }
        // FastDressDoItemBind : pose le flag de bind de l'item
        // (item+0x24 |= 0x10 natif) — notre IsBound couvre le bit ;
        // un item introuvable/non bindable → mode 1.
        if (!item.IsBound)
        {
            item.IsBound = true;
            await _services.InventoryRepo.UpdateItemAsync(item);
        }
        // Écriture du record working (ordre natif : uid, item_id,
        // con, loc=slot, idx=conIdx ; page/clé inchangés).
        var rec = edited[tarKey];
        rec.Uid = item.Id;
        rec.ItemId = item.ItemId;
        rec.ContainerType = conType;
        rec.FromLoc = slot;
        rec.FromIndex = conIdx;
        await SendFastDressModeAsync(session, 4, page, edited, token);
    }

    /// <summary><c>FastDressCheckEquip</c> @0x080ed390 — valide la clé
    /// dress pour l'EquipLoc de l'item, gère les conflits de paires
    /// (1M/2M, accessoires jumeaux) et le clear automatique de la clé
    /// adverse (2M posée → la clé offhand est vidée dans la page
    /// working). Effets de bord identiques au natif.</summary>
    private bool FastDressCheckEquip(
        Dictionary<int, FastDressRecord> page, int equipLoc,
        int group, int key, int itemId)
    {
        switch (equipLoc)
        {
            case 9: // arme 2M — clé 7 imposée + clear de la clé 8.
                if (key != 7) return false;
                if (page.TryGetValue(8, out var off)) off.Clear();
                return true;
            case 7:
            case 19: // main 1 / dual — clés 7/8, pas le même uid
                     // des deux côtés (CheckSrcDataExistPlus).
                if (key is not (7 or 8)) return false;
                if (PairSameItem(page, key == 7 ? 8 : 7, itemId))
                    return false;
                ClearTwoHandedAt(page, 7);
                return true;
            case 8: // bouclier/offhand — clé 8 imposée.
                if (key != 8) return false;
                ClearTwoHandedAt(page, 7);
                return true;
            case 12: // paire haute — clés 14/15.
                if (key is not (14 or 15)) return false;
                return !PairSameItem(page, key == 14 ? 15 : 14, itemId);
            case 11:
            case 20: // paire d'accessoires — clés 12/13.
                if (key is not (12 or 13)) return false;
                return !PairSameItem(page, key == 12 ? 13 : 12, itemId);
            case 6: return key == 6;
            case 10: return key == 11;
            case 35: return key == 10;
            case > 5: return true; // locs non mappés → toute clé.
            case > 0: // armure 1-5 : la clé doit être dans la colonne
                      // du kind de l'item et égaler EquipLoc.
                var baseKey = group switch
                { 2 => 600, 3 => 610, 4 => 620, _ => -1 };
                return baseKey > 0 && key - baseKey == equipLoc;
            default: return true;
        }
    }

    /// <summary>La clé 7 contient une 2M (EquipLoc 9) → le natif la
    /// vide quand on enregistre une main/offhand
    /// (FastDressCheckEquip, bloc post-valid).</summary>
    private void ClearTwoHandedAt(
        Dictionary<int, FastDressRecord> page, int key)
    {
        if (!page.TryGetValue(key, out var rec) || rec.ItemId <= 0)
            return;
        var def = _services.GameData.Items
            .FirstOrDefault(i => i.Id == rec.ItemId);
        if (def?.EquipLoc == 9) rec.Clear();
    }

    /// <summary>Vrai si le record à <paramref name="otherKey"/> porte
    /// le même item_id — conflit natif (même type d'item des deux
    /// côtés d'une paire).</summary>
    private static bool PairSameItem(
        Dictionary<int, FastDressRecord> page, int otherKey,
        int itemId)
        => page.TryGetValue(otherKey, out var r) &&
           r.ItemId == itemId && itemId > 0;

    /// <summary>Bloc « +0x30 &lt; 0 » natif : pour les EquipLoc
    /// {12,19,20}, refuse si une clé jumelle contient déjà le même
    /// item_id (texte 0x5a3 + mode 2).</summary>
    private bool ItemIdConflictsInPair(
        Dictionary<int, FastDressRecord> page, int equipLoc,
        int itemId)
        => equipLoc switch
        {
            19 => PairSameItem(page, 7, itemId) ||
                  PairSameItem(page, 8, itemId),
            20 => PairSameItem(page, 12, itemId) ||
                  PairSameItem(page, 13, itemId),
            12 => PairSameItem(page, 14, itemId) ||
                  PairSameItem(page, 15, itemId),
            _ => false,
        };

    /// <summary>594 — modes live {0,3} / working {2,4,5} / seul
    /// {1,7,8} : le ctor natif choisit la map selon le mode —
    /// helper qui envoie depuis la map demandée.</summary>
    private async Task SendFastDressModeAsync(NetSession session,
        short mode, int page, Dictionary<int, FastDressRecord> records,
        CancellationToken token)
        => await session.SendPacketAsync(594,
            FastDressPackets.BuildUpdate(mode, page,
                records.Values.OrderBy(r => r.TarLoc)), token);

    // ------------------------------------------------------------
    //  244 — ApplyFastDressData [i32 page][i32 masque]
    // ------------------------------------------------------------

    /// <summary><c>ApplyFastDress</c> @0x08124f00 : application de la
    /// tenue live — gates natifs (mort/transform/versus/ChangeClass/
    /// page/archétype), swaps inv↔équipement par record dont le bit du
    /// masque est posé (bit i = FastDressEquipLoc[i]), déséquipes
    /// forcées, enchant 46690 final. Échecs → textes + 594 mode 7/8.
    /// </summary>
    private async Task HandleFastDressApplyAsync(NetSession session,
        EntryState st, int page, int mask, CancellationToken token)
    {
        if (!FastDressAllowed(st)) return;
        if (page is < 0 or >= FastDressSlots.PageCount)
        {
            await SendApplyFailAsync(session, page, token);
            return;
        }
        // Gates natifs : +0x3c&4 transformé (0x1ac0), +0x3a&1 mort ou
        // enchant ChangeClass (0x1ac1), page live absente ou type
        // d'armure de classe inconnu (0x1a65) → mode 8.
        if (st.Dead || InVersusScene(st))
        {
            await SendTextIndexAsync(session, 0x1ac1, token);
            await SendApplyFailAsync(session, page, token);
            return;
        }
        var saved = st.FastDress!.Saved[page];
        var archetype = _services.GameData.Classes
            .FirstOrDefault(c => c.Id == st.ClassId)?.Archetype ?? 0;
        var column = archetype switch
        { 1 => 4, 2 => 3, 3 => 2, _ => 0 };
        if (column == 0)
        {
            await SendTextIndexAsync(session, 0x1a65, token);
            await SendApplyFailAsync(session, page, token);
            return;
        }
        // Plan : déséquipes forcées (slots cochés vides) + équipes.
        var unequips = new List<(int group, int loc)>();
        var equips = new List<(FastDressRecord rec, int group, int loc)>();
        foreach (var rec in saved.Values.OrderBy(r => r.TarLoc))
        {
            var col = FastDressSlots.ColumnOfKey(rec.TarLoc);
            if (col != 0 && col != column) continue;
            if (!FastDressSlots.BitIsSet(mask, rec.TarLoc)) continue;
            var (g, loc) = FastDressSlots.EquipPosOf(rec.TarLoc);
            if (rec.Uid <= 0 || rec.ItemId <= 0)
            {
                // Record vide coché → le slot doit être libéré.
                var eq = await _services.InventoryRepo
                    .GetEquipmentItemAsync((int)st.CharId, g, loc);
                if (eq != null) unequips.Add((g, loc));
                continue;
            }
            var already = await _services.InventoryRepo
                .GetEquipmentItemAsync((int)st.CharId, g, loc);
            if (already?.Id == rec.Uid) continue; // déjà en place.
            equips.Add((rec, g, loc));
        }
        // Conflits de mains natifs : une 2M enregistrée en clé 7
        // libère la clé 8 ; un item 1M/offhand vise une clé occupée
        // par une 2M → déséquipe forcée (prologue d'ApplyFastDress).
        await AddWeaponConflictUnequipsAsync(st, saved, mask,
            unequips, equips);
        if (unequips.Count == 0 && equips.Count == 0)
        {
            // Rien à appliquer → texte 0x1a30 + 594 mode 7 natif.
            await SendTextIndexAsync(session, 0x1a30, token);
            await session.SendPacketAsync(594,
                FastDressPackets.BuildUpdate(7, page, null), token);
            return;
        }
        // Espace libre requis pour les déséquipes (texte 0x4f9 natif).
        if (!await HasRoomForUnequipsAsync(st, unequips.Count))
        {
            await SendTextIndexAsync(session, 0x4f9, token);
            return;
        }
        await ExecuteFastDressPlanAsync(session, st, saved, unequips,
            equips, token);
        // Enchant FastDressID natif si absent — EffectToLife(char,
        // char, 46690, 0x32, 0x20, false, 0, 1, 0, 0) à la fin
        // d'ApplyFastDress : le param 0x32 = rest_time 50 ticks
        // (dixièmes) = 5 s.
        if (!st.Statuses.ContainsKey(FastDressEnchantId))
            await AddPlayerEnchantAsync(session, st,
                (int)st.CharId, FastDressEnchantId,
                EnchantRestSeconds(0x32), 1, token);
        // RefreshEQsAttr natif (280 à soi) — les 276 par loc ont été
        // émis dans ExecuteFastDressPlanAsync (Equip/UnEquipForSwitch).
        await InventoryHandlers.EmitSelfEquipRefreshAsync(_services,
            session, (int)st.CharId, token);
    }

    /// <summary>Déséquipes forcées du prologue natif : les clés 7/8
    /// (mains) — un apply sans la clé 8 cochée avec une 2M en clé 7
    /// (ou l'inverse) requiert de libérer l'autre main.</summary>
    private async Task AddWeaponConflictUnequipsAsync(EntryState st,
        Dictionary<int, FastDressRecord> page, int mask,
        List<(int group, int loc)> unequips,
        List<(FastDressRecord rec, int group, int loc)> equips)
    {
        // 2M appliquée en clé 7 : l'offhand équipé doit être libéré
        // même si la clé 8 n'est pas cochée (prologue natif).
        if (FastDressSlots.BitIsSet(mask, 7) &&
            page.TryGetValue(7, out var rec7) && rec7.ItemId > 0)
        {
            var def = _services.GameData.Items
                .FirstOrDefault(i => i.Id == rec7.ItemId);
            if (def?.EquipLoc == 9)
            {
                var eq = await _services.InventoryRepo
                    .GetEquipmentItemAsync((int)st.CharId, 0, 8);
                if (eq != null && !unequips.Contains((0, 8)))
                    unequips.Add((0, 8));
            }
        }
        // Clé 7/8 cochée dont le record est vide mais une 2M est
        // portée en loc 7 → la 2M doit partir (elle occupe les deux
        // mains).
        if ((FastDressSlots.BitIsSet(mask, 7) ||
             FastDressSlots.BitIsSet(mask, 8)) &&
            !unequips.Contains((0, 7)))
        {
            var rec7e = page.TryGetValue(7, out var r7e) ? r7e : null;
            var rec8e = page.TryGetValue(8, out var r8e) ? r8e : null;
            var eq7 = await _services.InventoryRepo
                .GetEquipmentItemAsync((int)st.CharId, 0, 7);
            if (eq7 != null &&
                ((rec7e?.Uid ?? 0) <= 0 || (rec8e?.Uid ?? 0) <= 0))
            {
                var def = _services.GameData.Items
                    .FirstOrDefault(i => i.Id == eq7.ItemId);
                if (def?.EquipLoc == 9) unequips.Add((0, 7));
            }
        }
    }

    /// <summary>Les sacs ont-ils <paramref name="count"/> slots libres
    /// (check <c>IItemContainer::GetEmptySlotNum</c> natif avant les
    /// déséquipes — capacités réelles via inventory_slots).</summary>
    private async Task<bool> HasRoomForUnequipsAsync(EntryState st,
        int count)
    {
        if (count <= 0) return true;
        var items = await _services.InventoryRepo
            .GetLiveItemsAsync((int)st.CharId);
        var perBag = items.Where(i => !i.IsEquipped)
            .GroupBy(i => i.BagId)
            .ToDictionary(g => g.Key, g => g.Count());
        var free = 0;
        foreach (var bag in new[] { 0, 1, 2, 3, 4 })
        {
            var cap = await _services.InventoryRepo
                .GetBagCapacity((int)st.CharId, bag) ?? (bag == 0 ? 32 : 0);
            var used = perBag.TryGetValue(bag, out var n) ? n : 0;
            free += Math.Max(0, cap - used);
            if (free >= count) return true;
        }
        return false;
    }

    /// <summary>Exécution des swaps — ordre natif : déséquipes
    /// (1→0) puis équipes (0→1 ou 1→1). Après chaque move, les
    /// records live/working qui référencent l'item déplacé sont
    /// resynchronisés (UpdateFastDressFromInfo natif) et persistés.</summary>
    private async Task ExecuteFastDressPlanAsync(NetSession session,
        EntryState st, Dictionary<int, FastDressRecord> saved,
        List<(int group, int loc)> unequips,
        List<(FastDressRecord rec, int group, int loc)> equips,
        CancellationToken token)
    {
        foreach (var (g, loc) in unequips)
        {
            var eq = await _services.InventoryRepo
                .GetEquipmentItemAsync((int)st.CharId, g, loc);
            if (eq == null) continue;
            var any = await _services.InventoryRepo
                .GetAnyAvailableSlotAsync((int)st.CharId);
            if (any == null) continue;
            if (!await _services.InventoryRepo.MoveToInventoryAsync(
                    (int)st.CharId, g, loc, any.Value.BagId,
                    any.Value.Slot)) continue;
            // UnEquipForSwitch natif → 276 slot vidé aux observateurs.
            await InventoryHandlers.EmitChangeEquipAsync(_services,
                session, (int)st.CharId, (short)loc, (short)g, null,
                token);
            await SyncFastDressSourceAsync(st, eq.Id,
                FastDressConInventory, any.Value.Slot, any.Value.BagId);
            var moved = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, any.Value.BagId, any.Value.Slot);
            if (moved != null)
                await session.SendPacketAsync(274,
                    NativeItemCodec.BuildPutItem(moved), token);
        }
        foreach (var (rec, g, loc) in equips)
        {
            if (rec.ContainerType == FastDressConEquipment)
            {
                // Item déjà porté ailleurs → EQ switch (1→1) natif.
                var eq = await _services.InventoryRepo
                    .GetEquipmentItemAsync((int)st.CharId,
                        rec.FromIndex, rec.FromLoc);
                if (eq == null || eq.Id != rec.Uid) continue;
                if (!await _services.InventoryRepo.SwapEquipmentAsync(
                        (int)st.CharId, rec.FromIndex, rec.FromLoc,
                        g, loc)) continue;
                // EquipForSwitch/UnEquipForSwitch natifs → 276 ×2
                // (les deux locs changent de contenu).
                await InventoryHandlers.EmitChangeEquipAsync(_services,
                    session, (int)st.CharId, (short)loc, (short)g,
                    await _services.InventoryRepo.GetEquipmentItemAsync(
                        (int)st.CharId, g, loc), token);
                await InventoryHandlers.EmitChangeEquipAsync(_services,
                    session, (int)st.CharId, (short)rec.FromLoc,
                    (short)rec.FromIndex,
                    await _services.InventoryRepo.GetEquipmentItemAsync(
                        (int)st.CharId, rec.FromIndex, rec.FromLoc),
                    token);
                await SyncFastDressSourceAsync(st, rec.Uid,
                    FastDressConEquipment, loc, g);
                continue;
            }
            // Source inventaire : vérif uid+item_id à la position
            // enregistrée (sinon texte 0x1950 natif — item déplacé).
            var item = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, rec.FromIndex, rec.FromLoc);
            if (item == null || item.Id != rec.Uid ||
                item.ItemId != rec.ItemId)
            {
                var def = _services.GameData.Items
                    .FirstOrDefault(i => i.Id == rec.ItemId);
                await SendTextIndexAsync(session, 0x1950, token,
                    def?.Name ?? "");
                continue;
            }
            // Gate kind/conteneur natif (Equip @0x80b5810) — un
            // record dress enregistré avant le gate ou sur un item
            // muté ne doit pas injecter un non-équipement.
            if (await Handlers.InventoryHandlers.ResolveEquipLocAsync(
                    _services, (int)st.CharId, item.ItemId,
                    (short)loc, (short)g) < 0) continue;
            if (!await _services.InventoryRepo.MoveToEquipmentAsync(
                    (int)st.CharId, rec.FromIndex, rec.FromLoc,
                    g, loc)) continue;
            // Equip natif → 276 du loc aux observateurs (le contenu
            // final du slot couvre aussi le swap-back éventuel).
            await InventoryHandlers.EmitChangeEquipAsync(_services,
                session, (int)st.CharId, (short)loc, (short)g,
                await _services.InventoryRepo.GetEquipmentItemAsync(
                    (int)st.CharId, g, loc), token);
            await SyncFastDressSourceAsync(st, rec.Uid,
                FastDressConEquipment, loc, g);
            // Le swap natif a remis l'ancien item dans le slot libéré —
            // resynchroniser son record dress s'il en a un.
            var swapped = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, rec.FromIndex, rec.FromLoc);
            if (swapped != null)
            {
                await SyncFastDressSourceAsync(st, swapped.Id,
                    FastDressConInventory, rec.FromLoc, rec.FromIndex);
                await session.SendPacketAsync(274,
                    NativeItemCodec.BuildPutItem(swapped), token);
            }
            else
                await session.SendAsync(new CNC_CZ_ClientDropItem
                {
                    Field1 = 0, Field2 = (short)rec.FromLoc,
                    Field3 = (short)rec.FromIndex,
                }, token);
        }
    }

    /// <summary><c>CItemFactory::UpdateFastDressFromInfo</c> →
    /// <c>CCharacter::ChangeFastDressInfo</c> natifs : après un
    /// déplacement d'item, tous les records (live+working) qui pointent
    /// ce serial prennent sa nouvelle position — persistés par UPDATE
    /// (GetUpdateFastDressInfoSQLCmd).</summary>
    private async Task SyncFastDressSourceAsync(EntryState st,
        int serial, int conType, int loc, int index)
    {
        if (st.FastDress == null) return;
        for (var p = 0; p < FastDressSlots.PageCount; p++)
            foreach (var map in new[]
                     { st.FastDress.Saved[p], st.FastDress.Edited[p] })
                foreach (var rec in map.Values)
                    if (rec.Uid == serial)
                    {
                        rec.ContainerType = conType;
                        rec.FromLoc = loc;
                        rec.FromIndex = index;
                        if (ReferenceEquals(map, st.FastDress.Saved[p]))
                            await _services.FastDressRepo
                                .UpdatePositionAsync((int)st.CharId,
                                    p, rec.TarLoc, conType, loc,
                                    index);
                    }
    }

    /// <summary>Échec d'apply natif : 594 mode 8 (mode seul).</summary>
    private static async Task SendApplyFailAsync(NetSession session,
        int page, CancellationToken token)
        => await session.SendPacketAsync(594,
            FastDressPackets.BuildUpdate(8, page, null), token);
}
