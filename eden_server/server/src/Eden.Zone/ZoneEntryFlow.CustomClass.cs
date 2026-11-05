using Eden.Core.Game.Classes;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

// -----------------------------------------------------------------------------
//  Classes personnalisées (« angel style ») — NCID 242 PlayerCCDataSave.
//  `CNE_CZ_ZoneServerPlayerCCDataSave::Execute` zone @0x08272bc0 :
//   - gates : combat (char+0x3c&4) → 0x193d ; enchant 46605 actif →
//     0x193e ; mort (char+0x3a&1) → silencieux ; riding (char+0x3b&0x20)
//     → 0x193f ; verrou versus/instance (char+0x3b&0x40) → silencieux ;
//     slot > nb cartes, sorts > 8, equip ≥ 4, weapon ≥ 2, spell
//     inexistant (map zone+0x1b8) → silencieux.
//   - corps : RemoveAllEnchants(CCheckRemoveChangeClass) →
//     EffectToLife(46605, rest 0x32, 0x2000, true) → carte [slot] =
//     {equip,weapon,sorts} → active = slot → char+0x4ad bit2 = weapon&1
//     → InvokePassivities/SpellStones/AAEnchants → 509 licences →
//     SwitchClassEquip(ancienType, nouveauType) → 592 flag=1 →
//     ModelModify(0x8000) self+CanSee → ModelModify(0x5e00)
//     self+CanSee → ZMis 845 (persistance mission).
// -----------------------------------------------------------------------------

public sealed partial class ZoneEntryFlow
{
    /// <summary>NCID 242 — sauvegarde d'une carte de classe
    /// personnalisée. Wire natif (Deserialize @0x08273a30) :
    /// <code>[u8 slot][u8 equip_type][u8 weapon_type]
    /// [u16 n][i32 ×n spell_ids]</code></summary>
    private async Task HandlePlayerCCDataSaveAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (st.CharId == 0 || payload.Length < 5) return;
        var slot = payload[0];                       // u8 : index carte
        var equipType = payload[1];                  // u8 : EClassEquipType
        var weaponType = payload[2];                 // u8 : type d'arme
        var spellCount = BitConverter.ToUInt16(payload, 3);
        if (payload.Length < 5 + spellCount * 4) return;
        var spells = new List<int>(spellCount);
        for (var i = 0; i < spellCount; i++)
            spells.Add(BitConverter.ToInt32(payload, 5 + i * 4));

        // --- Gates natifs (ordre de l'Execute) ---
        if (st.InCombat)                             // char+0x3c & 4
        {
            await SendTextIndexAsync(session,
                CustomClass.ErrorInCombat, token);
            return;
        }
        if (st.Statuses.ContainsKey(CustomClass.AngelStyleEnchantId))
        {
            await SendTextIndexAsync(session,
                CustomClass.ErrorAlreadyActive, token);
            return;
        }
        if (st.Dead) return;                         // char+0x3a & 1
        if (_services.RideStates.ContainsKey((int)st.CharId))
        {                                            // char+0x3b & 0x20
            await SendTextIndexAsync(session,
                CustomClass.ErrorRiding, token);
            return;
        }
        if (InVersusScene(st)) return;               // char+0x3b & 0x40
        // Le gate natif est `slot <= nb cartes` mais l'écriture est
        // indexée — slot == count écrirait hors du vecteur natif.
        if (slot >= st.CustomClassSlots.Count ||
            spells.Count > CustomClass.MaxSpells ||
            equipType >= 4 || weaponType >= 2) return;
        // Chaque spell doit exister (map de sorts zone+0x1b8).
        var spellIds = _services.GameData.Spells
            .Select(s => s.Id).ToHashSet();
        if (spells.Any(id => !spellIds.Contains(id))) return;

        // --- Corps natif ---
        var oldEquipType = st.ActiveCCIndex < st.CustomClassSlots.Count
            ? st.CustomClassSlots[st.ActiveCCIndex].EquipType
            : (byte)1;

        // RemoveAllEnchants(CCheckRemoveChangeClass) — le functor
        // garde les enchants dont SEffectData+0xc bit7 est levé
        // (transformation : les buffs de combat tombent). La colonne
        // du flag n'est pas résolue — les statuts temporisés
        // (rest ≥ 0 = transitoires) sont retirés, les permanents
        // (rest < 0 : monture…) survivent.
        foreach (var eid in st.Statuses
                     .Where(kv => kv.Value.RestSeconds >= 0)
                     .Select(kv => kv.Key).ToList())
            await RemovePlayerEnchantAsync(session, st, eid, token);

        // EffectToLife(46605, 0x32, 0x2000, true, 0, 1) — enchant
        // AngelChangeStyleID, rest 0x32 ticks = 5 s.
        if (!st.Statuses.ContainsKey(CustomClass.AngelStyleEnchantId))
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                CustomClass.AngelStyleEnchantId,
                EnchantRestSeconds(CustomClass.AngelStyleRestDs),
                1, token);

        // Écriture de la carte + index actif + bit genre.
        var card = st.CustomClassSlots[slot];
        card.EquipType = equipType;
        card.WeaponType = weaponType;
        card.Spells.Clear();
        card.Spells.AddRange(spells);
        st.ActiveCCIndex = slot;
        st.Vis4ad = st.Vis4ad & ~4 | (weaponType & 1) << 2;

        // InvokePassivities/SpellStones/AAEnchants → refresh stats
        // (RefreshEQsAttr) : pool armes + blocs 296/492/493.
        st.WeaponResolved = false;
        st.EntryDirty = true; // bundle d'entrée périmé (Phase 2)
        await ResolveWeaponAsync(st);
        await SendStatBlocksAsync(session, st, st.ClassId,
            st.ClassLevel, token);

        // 509 — le natif renvoie le vecteur +0xaf0 complet.
        await HandleQueryLicenseAsync(session, st, token);

        // SwitchClassEquip(ancien, nouveau) — stash equipment
        // container_index = 5−type, swap slots 1..5.
        await SwitchClassEquipAsync(st, oldEquipType, equipType);

        // 592 flag=1 — la carte sauvegardée.
        await session.SendPacketAsync(592,
            CustomClassPackets.BuildSavedSlot(slot, card), token);

        // ModelModify(0x8000) self + CanSee — le bit genre CC.
        var maskBody = BuildModelModifyBody(st, 0x8000);
        await session.SendPacketAsync(283, maskBody, token);
        await BroadcastToNodeAsync(session, st, 283, maskBody, token);
        // ModelModify(0x5e00) self + CanSee — le modèle complet.
        var fullBody = await BuildModelModifyBodyAsync(st, 0x5e00);
        await session.SendPacketAsync(283, fullBody, token);
        await BroadcastToNodeAsync(session, st, 283, fullBody, token);

        // Persistance — SaveCustomClassData (UPDATE carte) +
        // current_custom_data_index (exec+0x88c → gros UPDATE natif).
        var savedCopy = new CustomClassSlot
        {
            EquipType = card.EquipType,
            WeaponType = card.WeaponType,
        };
        savedCopy.Spells.AddRange(card.Spells);
        await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.CustomClassRepo.SaveSlotAsync(
                (int)st.CharId, slot, savedCopy));
        await _persistence.EnqueueAsync((int)st.CharId,
            ct => _services.CustomClassRepo.SetActiveIndexAsync(
                (int)st.CharId, slot));
    }

    /// <summary>`CPlayerFactory::SwitchClassEquip` @0x080f1f90 :
    /// échange les slots 1..5 de l'équipement actif (conteneur 0) avec
    /// le stash du NOUVEAU type après avoir rangé la tenue courante
    /// dans le stash de l'ANCIEN type — EquipNoCheck(cont0,i→
    /// stashAncien,i) puis EquipNoCheck(stashNouveau,i→cont0,i).
    /// Les conteneurs existent toujours dans notre modèle lignes
    /// (un swap sur ligne absente est un no-op — l'équivalent natif
    /// « conteneur manquant » retournait 0 sans swap).</summary>
    private async Task SwitchClassEquipAsync(EntryState st,
        int oldEquipType, int newEquipType)
    {
        if (oldEquipType == newEquipType) return;
        var oldStash = CustomClass.StashContainerKey(oldEquipType);
        var newStash = CustomClass.StashContainerKey(newEquipType);
        var charId = (int)st.CharId;
        for (var slot = 1; slot <= 5; slot++)
        {
            // cont0[slot] → stash[ancien][slot]
            await _services.InventoryRepo.SwapEquipmentAsync(
                charId, 0, slot, oldStash, slot);
            // stash[nouveau][slot] → cont0[slot]
            await _services.InventoryRepo.SwapEquipmentAsync(
                charId, newStash, slot, 0, slot);
        }
    }

    /// <summary>Body du 283 pour le mask 0x8000 — bit0x8000 du ctor
    /// natif @0x0813f2c0 : 1 u32 = `char+0x4ad &gt;&gt; 2 &amp; 1`
    /// (bit genre de la carte CC) puis le vecteur +0x7fc (handle
    /// propre — même forme que le 0x5e00 capturé).</summary>
    private static byte[] BuildModelModifyBody(EntryState st,
        int mask)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(st.CharId));   // i32 handle
        body.Add(0x0a);                                    // s8 char byte
        body.AddRange(BitConverter.GetBytes(mask));        // i32 mask
        body.AddRange(BitConverter.GetBytes((short)1));    // u16 : 1 u32
        body.AddRange(BitConverter.GetBytes(
            st.Vis4ad >> 2 & 1));                          // u32 : bit genre
        body.AddRange(BitConverter.GetBytes((short)1));    // u16 : vec
        body.AddRange(BitConverter.GetBytes(st.CharId));   // u32 : handle
        return body.ToArray();
    }

    /// <summary>Body du 283 pour le mask 0x5e00 — les 8 u32 modèle
    /// (= ids des items portés, miroir des champs natifs
    /// char+0x48e..0x4a2 mis à jour par RefreshEQsAttr) + le vecteur
    /// de handles. Extrait de <see cref="SendModelModifyAsync"/> pour
    /// la variante broadcast du flux CC.</summary>
    private async Task<byte[]> BuildModelModifyBodyAsync(EntryState st,
        int mask)
    {
        var slots = new uint[8];
        var eq = await _services.InventoryRepo.GetEquippedItemsAsync(
            (int)st.CharId);
        foreach (var (loc, itemId) in eq)
            if (loc is >= 0 and < 8 && slots[loc] == 0)
                slots[loc] = (uint)itemId;
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(st.CharId));   // i32 handle
        body.Add(0x0a);                                    // s8 char byte
        body.AddRange(BitConverter.GetBytes(mask));        // i32 mask
        body.AddRange(BitConverter.GetBytes((short)8));    // u16 : 8 u32
        foreach (var s in slots)
            body.AddRange(BitConverter.GetBytes(s));       // u32 : equipement
        body.AddRange(BitConverter.GetBytes((short)1));    // u16 : vec
        body.AddRange(BitConverter.GetBytes(st.CharId));   // u32 : handle
        return body.ToArray();
    }
}
