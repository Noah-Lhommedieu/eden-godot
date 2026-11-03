using Eden.Core.Database.Repositories;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Fusion d'elfs — port de <c>CKFElfManager::ElfUpgrade</c> /
/// <c>DoElfUpgrade</c> / <c>CanUpgrade</c> / <c>UpgradeGetSpell</c> /
/// <c>GetNotRepeatSpell</c> / <c>ReturnElf</c> natifs (zone
/// decompiled.c) :
///
/// <b>Entrée</b> : NCID 227 CNE_CZ_ZoneServerElfMix —
/// <c>[s16 elfLoc1][s16 elfLoc2][s16 sac][s16 slot]</c>
/// (Execute @0x8249990 : crée CPetUpgradeOp puis ElfUpgrade).
///
/// <b>Validation</b> (CanUpgrade @0x8072150, dans l'ordre natif) :
/// les deux elfs existent ; level ≥ 40 (texte 5466) ; les 4 grow de
/// l'elf principal ne sont pas tous > 8 (texte 5509) ; aucun elf
/// invoqué — CKFElf+0x58 (texte 5475) ; <b>mix_star égaux</b>
/// (+0x38, texte 5476) ; étoile &lt; max param 509 = 4 (texte 5478) ;
/// aucun item dans le conteneur d'équipement des deux elfs (texte
/// 5232) ; l'item matériau existe et est un des catalyseurs
/// 0x1790/0x1791/0x1792.
///
/// <b>Déroulé</b> : ElfUpgrade (@0x8072590) pose l'op, émet
/// 574 ClientPetUpgradeCasting <c>[s16 40]</c> et arme l'effet
/// PetUpgradeID de 40 ticks (≈4 s). À la fin DoElfUpgrade
/// (@0x8079a00) re-valide, consomme le matériau, applique les grow
/// (nombre d'incréments = GrowType de l'elf secondaire :
/// 0→1, 2→2, 3→4, 4→3, 5→5), fusionne les sorts
/// (UpgradeGetSpell+GetNotRepeatSpell), mix_star+1, level=1, exp=0,
/// hp=max, persiste ("Update Elf Mix"), consomme l'elf secondaire
/// (ReturnElf → 414 [loc][1]) et émet 568 ClientUpdateElfMixElf.
/// Le remplacement d'op annule → 575 ClientPetUpgradeCancel (corps
/// vide).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Ids des pierres de fusion acceptées par le natif —
    /// <c>itemdata vtable+0xc ∈ {0x1790, 0x1791, 0x1792}</c>
    /// (DoElfUpgrade @0x8079b60). Ces items ne figurent pas dans la
    /// S_Item d'époque (ajout item-mall ultérieur) — le check reste
    /// fidèle au désassemblage.</summary>
    private static readonly HashSet<int> ElfMixMaterialIds =
        new() { 0x1790, 0x1791, 0x1792 };

    /// <summary>Plafond d'étoile de fusion — paramètre natif index
    /// 509 (AllFormulaParas+0x7f4, valeur DB 4 ; CanUpgrade compare
    /// <c>star >= param</c>).</summary>
    private const short ElfMixMaxStar = 4;

    /// <summary>Durée du cast de fusion — param 0x28 (40) de
    /// <c>CEffectFactory::EffectToLife</c> dans ElfUpgrade : le même
    /// entier est poussé dans le 574 (barre de cast client). Le tick
    /// natif ≈ 100 ms → 4 s.</summary>
    private static readonly TimeSpan ElfMixCast =
        TimeSpan.FromMilliseconds(4000);

    /// <summary>227 CNE_CZ_ZoneServerElfMix — corps
    /// <c>[s16 elf1][s16 elf2][s16 sac][s16 slot]</c> (Execute
    /// @0x8249990). Un nouveau 227 annule d'abord le cast en cours
    /// (ResetOperation → CPetUpgradeOp::Cancel → 575).</summary>
    private async Task HandleElfMixAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        await EnsureElfsLoadedAsync(session, st, token);
        if (payload.Length < 8) return;
        var loc1 = BitConverter.ToInt16(payload, 0);   // s16 : elf principal
        var loc2 = BitConverter.ToInt16(payload, 2);   // s16 : elf consommé
        var bag = BitConverter.ToInt16(payload, 4);    // s16 : conteneur item (-1 permis)
        var slot = BitConverter.ToInt16(payload, 6);   // s16 : slot item (-1 permis)

        // ResetOperation natif : un nouveau 227 pendant le cast
        // annule l'op précédente → CPetUpgradeOp::Cancel émet 575.
        if (st.PendingElfMix != null)
        {
            st.PendingElfMix = null;
            await session.SendAsync(new CNC_CZ_ClientPetUpgradeCancel(),
                token);
        }

        // Bornes Execute natif : 0 ≤ loc < taille du vecteur (10).
        if (loc1 < 0 || loc1 > 9 || loc2 < 0 || loc2 > 9 || loc1 == loc2)
            return;
        if (!await CanElfUpgradeAsync(session, st, loc1, loc2, bag,
                slot, token))
            return;

        st.PendingElfMix = (loc1, loc2, bag, slot);
        // 574 ClientPetUpgradeCasting — [s16 durée ticks] (40 natif).
        await session.SendAsync(new CNC_CZ_ClientPetUpgradeCasting
        { Value = 40 }, token);
        ScheduleOnLoop(ElfMixCast,
            ct => ResolveElfMixAsync(session, st, ct), token);
        EdenLog.Write($"[Elf] char={st.CharId} mix cast " +
            $"{loc1}+{loc2} mat=({bag},{slot})");
    }

    /// <summary>CanUpgrade natif (@0x8072150) — émet les textes
    /// d'échec exacts du natif, retourne true si la fusion peut
    /// démarrer.</summary>
    private async Task<bool> CanElfUpgradeAsync(NetSession session,
        EntryState st, short loc1, short loc2, short bag, short slot,
        CancellationToken token)
    {
        st.ElfSlots.TryGetValue(loc1, out var e1);
        st.ElfSlots.TryGetValue(loc2, out var e2);
        if (e1 == null || e2 == null) return false;
        // Niveau ≥ 40 (char CKFElf+0x14) — texte 5466.
        if (e1.Level < 40 || e2.Level < 40)
        {
            await SendElfTextAsync(session, 5466, token);
            return false;
        }
        // Les 4 grow déjà tous > 8 → plus rien à monter — 5509.
        if (e1.Grow[0] > 8 && e1.Grow[1] > 8 && e1.Grow[2] > 8
            && e1.Grow[3] > 8)
        {
            await SendElfTextAsync(session, 5509, token);
            return false;
        }
        // Elf invoqué (CKFElf+0x58 = CElfPet live) — 5475.
        if (e1.Status == 2 || e2.Status == 2)
        {
            await SendElfTextAsync(session, 5475, token);
            return false;
        }
        // mix_star identiques (+0x38) — 5476, puis plafond — 5478.
        if (e1.MixStar != e2.MixStar)
        {
            await SendElfTextAsync(session, 5476, token);
            return false;
        }
        if (e1.MixStar >= ElfMixMaxStar)
        {
            await SendElfTextAsync(session, 5478, token);
            return false;
        }
        // Aucun item dans le conteneur d'équipement des deux elfs —
        // 5232 (map native char+0x544 clé = loc elf).
        var conts = await _services.InventoryRepo
            .GetElfContainerItemsAsync((int)st.CharId);
        if (conts.Any(i => i.BagId == loc1 || i.BagId == loc2))
        {
            await SendElfTextAsync(session, 5232, token);
            return false;
        }
        // Matériau — requis seulement si sac+slot fournis (Execute
        // natif passe -1,-1 pour sauter la consommation).
        if (bag != -1 && slot != -1)
        {
            var mat = await _services.InventoryRepo.GetItemAsync(
                (int)st.CharId, bag, slot);
            if (mat == null)
            {
                // 1520 (conteneur absent) / 1527 (slot vide) — le natif
                // distingue les deux ; notre lookup fusionne : le sac
                // manquant ou le slot vide retombent sur 1527, assez
                // proche pour le joueur.
                await SendElfTextAsync(session, 1527, token);
                return false;
            }
            if (!ElfMixMaterialIds.Contains(mat.ItemId))
            {
                // Natif : DebugLog serveur seulement (« ElfUpgrade
                // False : Invalid Item ») — silence côté client.
                EdenLog.Write($"[Elf] char={st.CharId} mix : " +
                    $"matériau invalide {mat.ItemId} @({bag},{slot})");
                return false;
            }
        }
        return true;
    }

    /// <summary>Fin du cast (DoElfUpgrade @0x8079a00) — re-validation,
    /// consommation du matériau, merge grow/sorts, resets,
    /// persistance, consommation de l'elf secondaire, 568.</summary>
    private async Task ResolveElfMixAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var op = st.PendingElfMix;
        if (op == null) return;   // annulé
        st.PendingElfMix = null;
        if (!await CanElfUpgradeAsync(session, st, op.Value.Elf1Loc,
                op.Value.Elf2Loc, op.Value.MatBag, op.Value.MatSlot,
                token))
            return;
        var e1 = st.ElfSlots[op.Value.Elf1Loc];
        var e2 = st.ElfSlots[op.Value.Elf2Loc];

        // ---- Croissance : le GrowType de l'elf consommé fixe le
        // nombre d'incréments (switch natif @0x8079c88) ; chaque
        // incrément touche une position <9 tirée au hasard. ----
        var grow = (short[])e1.Grow.Clone();
        var bumps = e2.Quality switch
        { 0 => 1, 2 => 2, 3 => 4, 4 => 3, 5 => 5, _ => 0 };
        for (var n = 0; n < bumps; n++)
        {
            var start = Random.Shared.Next(4);
            var i = start;
            while (grow[i] >= 9)
            {
                i = (i + 1) % 4;
                if (i == start) goto GrowDone;   // tout à 9 — break natif
            }
            grow[i]++;
        }
    GrowDone:;

        // ---- Sorts : UpgradeGetSpell + GetNotRepeatSpell natifs.
        // Sans pierre 1790/1791 portant des flags de conservation
        // (param_5 char* natif) — tous les slots en pile-a-face. ----
        var battle = MergeElfSpells(e1.Battle, e2.Battle);
        var integ = MergeElfSpells(e1.Integrate, e2.Integrate);
        if (battle == null || integ == null)
        {
            // « Get Spell Failed » → texte 5510 natif (0x1586).
            await SendElfTextAsync(session, 5510, token);
            return;
        }
        // Bonus étoile 3 natif (params 894-896 : chance + plage de sort
        // offerte) : paramètres absents de la DB — non porté.

        e1.Grow = grow;
        for (var i = 0; i < 5; i++)
        {
            e1.Battle[i] = battle[i];
            e1.Integrate[i] = integ[i];
        }
        e1.MixStar++;                 // local_82 + 1
        e1.Level = 1;                 // CKFElf+0x14 = 1
        e1.Exp = 0;                   // CKFElf+0x18 = 0
        e1.Hp = e1.MaxHp;             // CKFElf::GetMaxHp = +0x20

        if (_services.ElfRepo != null)
        {
            var row = ToElfRow(e1, (int)st.CharId);
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.UpdateElfMixAsync(row));
        }

        // ---- ReturnElf(elf2, notify=false) natif : supprime le nœud
        // elf_container, "Delete ElfLeave", 414 [loc][1], slot libéré.
        // (Les enchants intégrés du joueur — RemoveAllEnchants
        // loc+1 — ne sont pas encore implémentés chez nous : les
        // sorts intégrés n'ont pas de canal d'effet actif.)
        if (_services.ElfRepo != null)
        {
            var eid = e2.ElfId;
            var eloc = e2.Loc;
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.InventoryRepo.DeleteElfContainerAsync(
                    (int)st.CharId, eloc));
            await _persistence.EnqueueAsync((int)st.CharId,
                _ => _services.ElfRepo!.DeleteElfAsync(eid));
        }
        st.ElfSlots.Remove(e2.Loc);
        await session.SendAsync(new CNC_CZ_ClientElfReturnOK
        { Field1 = e2.Loc, Field2 = 1 }, token);

        // Consommation du matériau (ItemConsume natif, 1 unité).
        if (op.Value.MatBag != -1 && op.Value.MatSlot != -1)
            await ConsumeItemAtAsync(session, st, op.Value.MatBag,
                op.Value.MatSlot, 1, token);

        // 568 ClientUpdateElfMixElf — [loc][elf_id][star] + 3 vecteurs.
        await session.SendPacketAsync(568,
            Codecs.NativeElfCodec.BuildUpdateElfMixElf(e1.Loc, e1.ElfId,
                e1.MixStar, grow, e1.Battle, e1.Integrate), token);
        EdenLog.Write($"[Elf] char={st.CharId} mix OK " +
            $"{e2.Loc}->{e1.Loc} star={e1.MixStar} " +
            $"grow={string.Join(',', grow)}");
    }

    /// <summary>UpgradeGetSpell (@0x8073430) + GetNotRepeatSpell
    /// (@0x8071010) natifs : par slot, si main[i]==0 → {0,0} ; sinon
    /// si sub[i]!=0 → paire {main,sub} ou {sub,main} au hasard ;
    /// sinon {main,main}. Puis GetNotRepeatSpell choisit une position
    /// par paire en évitant les doublons non nuls — énumération
    /// bit-par-bit (bit=1 → .first), première combinaison valide
    /// retenue ; retourne null si impossible (texte 5510 natif).</summary>
    private static short[]? MergeElfSpells(int[] main, int[] sub)
    {
        var n = main.Length;
        var pairs = new (short a, short b)[n];
        for (var i = 0; i < n; i++)
        {
            var m = (short)main[i]; var s = (short)sub[i];
            if (m == 0) pairs[i] = (0, 0);
            else if (s != 0)
                pairs[i] = Random.Shared.Next(2) == 0 ? (m, s) : (s, m);
            else pairs[i] = (m, m);
        }
        var combos = 1 << n;
        for (var mask = 0; mask < combos; mask++)
        {
            var pick = new short[n];
            for (var i = 0; i < n; i++)
                pick[i] = (mask & (1 << i)) != 0 ? pairs[i].a : pairs[i].b;
            var dup = false;
            for (var i = 0; i < n && !dup; i++)
            {
                if (pick[i] == 0) continue;
                for (var j = i + 1; j < n; j++)
                    if (pick[j] == pick[i]) { dup = true; break; }
            }
            if (!dup) return pick;
        }
        return null;
    }

    /// <summary>Conversion ElfSlot → ligne `elf` pour la persistance
    /// de la fusion (toutes colonnes portées par UpdateElfMixAsync).</summary>
    private static ElfRepository.ElfRow ToElfRow(ElfSlot e,
        int charId) => new()
        {
            ElfId = e.ElfId,
            PlayerId = charId,
            TemplateId = e.TemplateId,
            Status = e.Status,
            GivenName = e.GivenName,
            Level = e.Level,
            Exp = e.Exp,
            Hp = e.Hp,
            GrowAttack = e.Grow[0],
            GrowHit = e.Grow[1],
            GrowCritical = e.Grow[2],
            GrowAttackSpeed = e.Grow[3],
            Loc = e.Loc,
            MixStar = e.MixStar,
            BattleSpell1 = e.Battle[0],
            BattleSpell2 = e.Battle[1],
            BattleSpell3 = e.Battle[2],
            BattleSpell4 = e.Battle[3],
            BattleSpell5 = e.Battle[4],
            IntegrateSpell1 = e.Integrate[0],
            IntegrateSpell2 = e.Integrate[1],
            IntegrateSpell3 = e.Integrate[2],
            IntegrateSpell4 = e.Integrate[3],
            IntegrateSpell5 = e.Integrate[4],
        };

    /// <summary>Texte d'échec du panneau familiers —
    /// <c>(code*)(char+0x58)(char, texteId, 0xffffff97)</c> natif.</summary>
    private static async Task SendElfTextAsync(NetSession session,
        int textId, CancellationToken token)
        => await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        { Field1 = (short)textId, Field2 = "" }, token);
}
