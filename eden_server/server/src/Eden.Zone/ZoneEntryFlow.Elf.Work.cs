using System.Buffers.Binary;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Partial <see cref="ZoneEntryFlow"/> — modes de travail des familiers
/// (NCID 107 <c>CNE_CZ_ZoneServerElfWork</c> [s16 loc][s16 mode] →
/// <c>CKFElfManager::ToWork</c> natif @0x807c780).
///
/// États natifs (<c>CKFElf+0x58</c>, persisté par
/// <c>CKFElf::SetElfStatus</c> @0x8456bb0 → DBA « Update Elf Status ») :
/// 0 = repos, 1 = « mix » (slot d'intégration/fusion), 2 = invoqué,
/// 7/8/9 = loteries (refusées).
///
/// Portage :
/// <list type="bullet">
/// <item><c>ToMix</c> @0x80736d0 (mode 1) : met l'elf dans le slot
/// d'intégration — un seul à la fois, cooldown 60 s partagé avec
/// l'appel (texte 0x1472 «%s,%d»), renvoi du pet live si c'est lui,
/// purge des enchants intégrés précédents (vecteur char+0x8c4,
/// RemoveMixElf @0x8072780), <c>UpdateElfMixSpell</c> @0x84116c0 =
/// applique les <b>ids d'enchant</b> integrate_spell_* des elfs en
/// mix au joueur via <c>ElfGetSpell</c> → <c>EffectToLife(-2,
/// 0x2020)</c> (permanent), <c>SetMixElfTemplateID</c> (char+0xbf8)
/// puis 567 <c>ClientUpdateMixElf</c> [i32 charId][i32 templateId]
/// au client + can-see.</item>
/// <item><c>ToIdle</c> @0x80732c0 (mode 0) : status 1 → status 0 +
/// RemoveMixElf(notify) ; status 2 → CallElfBack.</item>
/// <item><c>CElfPet::UpdateBattleSpell</c> @0x845aaf0 +
/// <c>InvokePassivities</c> @0x845a410 : les slots battle_spell_4/5
/// (pet+0x480/+0x488) — sorts <b>famille 9</b> — appliquent leur
/// enchant lié (S_Spell col39 ResultSpellId, ex. 58762→61477) au
/// maître via <c>EffectToLife(-2, flags|0x2020)</c> tant que le pet
/// est invoqué ; retirés par <c>CallElfBack</c> @0x8072ac0
/// (vecteur char+0x8d0).</item>
/// </list>
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Byte <c>CNodeTemplate+0x10</c> natif (seuls les bits
    /// 0x20/0x40 intéressent les familiers) dérivé de
    /// <c>S_Node col42</c> (<see cref="GameDataParsers.NodeDef.
    /// PetFlag"/>) : 0x20 = invocation/combat permis, 0x40 =
    /// prière/intégration permise. Cascade native prouvée dans
    /// <c>CNodeDBO::GetNodesForServer</c> @0x836bf00 →
    /// <c>CNodeTemplate::CNodeTemplate</c> @0x84a3e90
    /// (param_3→remap de bits, bit5←bit6, bit6←bit7).</summary>
    private int PetMapFlags(EntryState st)
    {
        var f = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.PetFlag ?? 0;
        return ((f is 1 or 3) ? 0x20 : 0) |
               ((f is 2 or 3) ? 0x40 : 0);
    }

    /// <summary>Gate de <c>CNE_CZ_ZoneServerElfWork::Execute</c>
    /// natif @0x824b0b0, avant ToWork :
    /// <list type="bullet">
    /// <item>ni 0x20 ni 0x40 → texte 0x209e (8350 « 此場景不可使用寵物
    /// »), tout refusé ;</item>
    /// <item>0x40 seul et mode 2 (invocation) → 0x209f (8351
    /// « 寵物無法戰鬥 ») ;</item>
    /// <item>0x20 seul et mode 1 (prière/mix) → 0x20a0 (8352
    /// « 寵物無法祈禱 ») ;</item>
    /// <item>BattleRoyale (node type 15 = template+0x2c) → mode 1
    /// refusé silencieusement (<c>IsInBattleRoyale || mode-1 &gt; 1
    /// </c>).</item>
    /// </list></summary>
    private async Task<bool> CheckElfWorkMapGateAsync(
        NetSession session, EntryState st, short mode,
        CancellationToken token)
    {
        // IsInBattleRoyale natif : node type 15 → mode 1 bloqué.
        var node = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId);
        if (node?.Type == 15 && mode == 1) return false;
        var flags = PetMapFlags(st);
        if ((flags & 0x20) == 0)
        {
            if ((flags & 0x40) == 0)
            {
                await SendTextIndexAsync(session, 0x209e, token);
                return false;
            }
            if (mode == 2)
            {
                await SendTextIndexAsync(session, 0x209f, token);
                return false;
            }
        }
        else if ((flags & 0x40) == 0 && mode == 1)
        {
            await SendTextIndexAsync(session, 0x20a0, token);
            return false;
        }
        return true;
    }

    /// <summary>107 mode 1 — ToMix natif : met l'elf dans le slot
    /// d'intégration (status 1) et applique ses enchants intégrés au
    /// joueur.</summary>
    private async Task HandleElfToMixAsync(NetSession session,
        EntryState st, short loc, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        if (!st.ElfSlots.TryGetValue(loc, out var elf) ||
            elf.Status == 1 || (uint)(elf.Status - 7) <= 2) return;

        // Pet live = cet elf → CallElfBack natif (char+0x7dc premier
        // slot, type 0x50b, pet+0x40c == loc).
        if (st.ElfPetHandle != 0 &&
            _services.Live.GetMob(st.ElfPetHandle)?.ElfLoc == loc)
        {
            await CallElfBackAsync(session, st, notify: false, token);
        }

        // Cooldown d'appel 60 s (elf+0xa0) — texte natif 0x1472
        // «%s,%d» (nom elf, secondes restantes).
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (elf.LastCallUnix >= 0 && now - elf.LastCallUnix < 60)
        {
            var left = 60 - (now - elf.LastCallUnix);
            var name = string.IsNullOrEmpty(elf.GivenName)
                ? elf.Def?.Name ?? "" : elf.GivenName;
            await SendTextIndexArgsAsync(session, 0x1472,
                $"{name},{left}", token);
            return;
        }
        elf.LastCallUnix = -1;

        // Un seul elf en mix — purge les anciens (status 0 +
        // RemoveMixElf sans notif : le 567 final couvre l'état).
        foreach (var other in st.ElfSlots.Values
                     .Where(e => e.Status == 1).ToList())
        {
            await SetElfStatusAsync(st, other, 0);
            await RemoveMixElfAsync(session, st, notify: false, token);
        }
        // +0x8d0 : purge des enchants passifs du pet invoqué.
        await RemovePetPassivesAsync(session, st, token);

        await SetElfStatusAsync(st, elf, 1);
        // UpdateElfMixSpell : enchants intégrés des elfs en mix →
        // EffectToLife(-2, 0x2020) sur le joueur.
        await UpdateElfMixSpellsAsync(session, st, token);
        st.MixElfLoc = elf.Loc;
        st.MixElfTemplateId = elf.TemplateId;

        // 567 ClientUpdateMixElf [i32 charId][i32 mixTemplateId] —
        // NotifyClient + BroadCastToCanSee natifs (ToMix @0x80738xx).
        await SendUpdateMixElfAsync(session, st, token);
        EdenLog.Write($"[Elf] char={st.CharId} ToMix loc={loc} " +
            $"elf={elf.ElfId} tpl={elf.TemplateId}");
    }

    /// <summary>Sort l'elf du slot mix — <c>RemoveMixElf</c> natif :
    /// retire les enchants intégrés du joueur (vecteur +0x8c4),
    /// MixElfTemplateID→0, 567 si notify.</summary>
    private async Task RemoveMixElfAsync(NetSession session,
        EntryState st, bool notify, CancellationToken token)
    {
        foreach (var eid in st.MixSpellEnchants.ToList())
            await RemovePlayerEnchantAsync(session, st, eid, token);
        st.MixSpellEnchants.Clear();
        st.MixElfTemplateId = 0;
        st.MixElfLoc = -1;
        if (notify)
            await SendUpdateMixElfAsync(session, st, token);
    }

    /// <summary>UpdateElfMixSpell natif : (re)constitue la liste des
    /// enchants intégrés des elfs en mix et les applique au joueur
    /// (EffectToLife durée -2 flag 0x2020 — permanent).</summary>
    private async Task UpdateElfMixSpellsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var ids = st.ElfSlots.Values
            .Where(e => e.Status == 1)
            .SelectMany(e => e.Integrate)
            .Where(id => id != 0).ToList();
        st.MixSpellEnchants.Clear();
        st.MixSpellEnchants.AddRange(ids);
        foreach (var eid in ids)
        {
            // ElfGetSpell natif : l'id doit être un enchant connu
            // (map +0x214) sinon texte 0x5a8 — on saute silencieusement
            // les ids absents des données.
            if (_services.Enchants.Get(eid) == null) continue;
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                eid, -2, 1, token);
        }
    }

    /// <summary>Passifs du pet invoqué —
    /// <c>CElfPet::UpdateBattleSpell</c> natif : battle_spell_4/5
    /// (pet+0x480/+0x488) = sorts famille 9 dont l'enchant lié
    /// (S_Spell col39 ResultSpellId) s'applique au maître en
    /// permanent (EffectToLife -2, flags|0x2020).</summary>
    private async Task ApplyPetPassivesAsync(NetSession session,
        EntryState st, ElfSlot elf, CancellationToken token)
    {
        for (var i = 3; i < elf.Battle.Length; i++)
        {
            var spellId = elf.Battle[i];
            if (spellId == 0) continue;
            var spell = _services.GameData.Spells
                .FirstOrDefault(s => s.Id == spellId && s.Family == 9);
            var eid = spell?.ResultSpellId ?? 0;
            if (eid == 0 || _services.Enchants.Get(eid) == null)
                continue;
            st.PetGrantedEnchants.Add(eid);
            await AddPlayerEnchantAsync(session, st, (int)st.CharId,
                eid, -2, 1, token);
        }
    }

    /// <summary>Purge du vecteur char+0x8d0 natif — les enchants
    /// passifs accordés par le pet invoqué (CallElfBack / ToIdle /
    /// ToMix).</summary>
    private async Task RemovePetPassivesAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        foreach (var eid in st.PetGrantedEnchants.ToList())
            await RemovePlayerEnchantAsync(session, st, eid, token);
        st.PetGrantedEnchants.Clear();
    }

    /// <summary>SetElfStatus natif — change <c>CKFElf+0x58</c> et
    /// persiste via la queue (DBA « Update Elf Status »).</summary>
    private async Task SetElfStatusAsync(EntryState st, ElfSlot elf,
        short status)
    {
        if (elf.Status == status) return;
        elf.Status = status;
        if (_services.ElfRepo != null)
        {
            var eid = elf.ElfId;
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.UpdateStatusAsync(eid, status));
        }
    }

    /// <summary>567 ClientUpdateMixElf
    /// <c>[i32 charId][i32 mixTemplateId]</c> — NotifyClient +
    /// BroadCastToCanSee natifs (ctor 2 args dans ToMix @0x80738xx).</summary>
    private async Task SendUpdateMixElfAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, (int)st.CharId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4),
            st.MixElfTemplateId);
        await session.SendPacketAsync(567, body, token);
        await BroadcastToNodeAsync(session, st, 567, body, token);
    }
}
