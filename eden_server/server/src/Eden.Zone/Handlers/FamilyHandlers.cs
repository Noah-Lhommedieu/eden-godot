using System.Collections.Concurrent;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Famille/guilde avancé — CEC_Family* natif :
/// <list type="bullet">
/// <item>UpdateFamily (81) → ClientChangeFamily 346, désasm
/// <c>CNE_CZ_ZoneServerUpdateFamily::Execute</c> @0x829cf20.</item>
/// <item>FamilyStorageQueryLog (179) → ClientFamilyStorageLog 506,
/// désasm Execute @0x8259350 / Serialize @0x81e82f0.</item>
/// <item>FixEquipByFamily (240) : rate-limit 1 s + réparation payée
/// par la trésorerie, désasm Execute @0x825ae00.</item>
/// <item>FamilyTalkToNpc (239) : CTalkOp + ListDialogue mission,
/// désasm Execute @0x82598d0.</item>
/// </list>
/// (FamilyStoragePut/Get sont dans InventoryHandlers ; le conteneur
/// de bâtiments FamilyContainer* et la ville FamilyTown* relèvent du
/// sous-système de ville de famille — lot ultérieur.)
/// </summary>
public static class FamilyHandlers
{
    /// <summary>Timestamp du dernier FixEquipByFamily par perso —
    /// équivalent de `char+0x650/0x654` natif (rate-limit 1 s ;
    /// violation → texte 0x621).</summary>
    private static readonly ConcurrentDictionary<int, DateTimeOffset>
        _fixEquipAt = new();

    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Conteneur de bâtiments du hall (130/131) ---
        // Interceptés sur la boucle par ZoneEntryFlow.TryHandleOnLoopAsync
        // — ils exigent l'op 0x3b (CFamilyBuildingOp posée par le
        // service de dialogue 11) et l'EntryState du perso. Voir
        // ZoneEntryFlow.FamilyInstance.cs (Execute natifs
        // @0x8255c00/@0x82541c0).

        // --- Instance de guilde ---
        // FamilyInstanceCreate (173) et FamilyInstanceGetData (132)
        // sont interceptés par ZoneEntryFlow.TryHandleOnLoopAsync —
        // ils requièrent l'EntryState (niveau live, hall visité
        // char+0xaac natif). Voir ZoneEntryFlow.FamilyInstance.cs.

        // --- Ville de guilde ---
        // FamilyTownEnter [int townId] — implémenté sur la boucle dans
        // ZoneEntryFlow.FamilyInstance.cs (gates natifs @0x8259d30 :
        // visite char+0xaac + transport house_open, sortie char+0xa98).

        // FamilyTownQuery (174) et FamilyTownMemberQuery (175) sont
        // interceptés par ZoneEntryFlow.TryHandleOnLoopAsync — ils
        // partagent le timestamp de rate-limit char+0x650 natif avec
        // 173/176 et lisent le cache famille + bâtiments (→502/503).

        // --- Divers guilde ---
        // FamilyBattleAskInfo (225) [s16 type] — intercepté par
        // ZoneEntryFlow.TryHandleOnLoopAsync
        // (ZoneEntryFlow.Territory.cs : type 0 → 561 ranks,
        // type 1 → 515 + 561 phase, type 2 → join de l'élimination).
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyBattleAskInfo(),
            (session, msg, charId, token) => Task.CompletedTask);

        // FamilyBuildingRepair (180) [s16 loc][u8 repairAll] —
        // réparation des bâtiments du hall payée par la trésorerie.
        // Natif : zone @0x8253b80 valide char/famille instanciée,
        // calcule le coût (max−cur)×costPer du template S_Buliding
        // (+0x20/+0x1c) ou GetAllBuildingRepaircosts pour repairAll,
        // puis relaie à la mission TC_FamilyBuildingRepair @0x81ef3d0
        // qui exige le privilège 10 (échec → texte 900), applique
        // UpdateBuildingDurability + DonateMoney(−cost). Chez nous le
        // débit + l'UPDATE sont directs (même portée que 240).
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyBuildingRepair(),
            async (session, msg, charId, token) =>
            {
                if (s.Guilds.GetGuildOf(charId) is not { } guild
                    || s.Guilds.GetMember(charId) is not { } member
                    || s.Social == null) return;
                // CheckPrivilige(fam, exec, 10) natif → 900.
                if (!s.Guilds.CheckPrivilege(guild, member, 10))
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 900, Field2 = string.Empty },
                        token);
                    return;
                }
                var buildings = await s.Social
                    .GetFamilyBuildingsAsync(guild.Id, 0);
                var repairAll = msg.Field2 != 0;
                // Liste des bâtiments à remettre au max : repairAll ne
                // touche que la production (types S_Buliding 4-9 —
                // GetAllBuildingRepaircosts/RepairAllProduction-
                // Building natifs), sinon l'item à `loc`.
                var targets = new List<(short Loc, int Cost,
                    short MaxDur)>();
                if (repairAll)
                {
                    foreach (var b in buildings.Where(b =>
                        b.ItemType == 0x29
                        && s.GameData.Buildings
                            .TryGetValue(b.Tid, out var def)
                        && def.Type is >= 4 and <= 9
                        && b.Durability < def.MaxDurability))
                    {
                        var def = s.GameData.Buildings[b.Tid];
                        targets.Add((b.Loc,
                            (def.MaxDurability - b.Durability)
                            * def.RepairCostPerPoint,
                            (short)def.MaxDurability));
                    }
                }
                else if (buildings.FirstOrDefault(b =>
                    b.Loc == msg.Field1) is { } b
                    && s.GameData.Buildings.TryGetValue(b.Tid,
                        out var def))
                {
                    if (b.Durability >= def.MaxDurability)
                    {
                        // Déjà à pleine durabilité → 0x8f2 natif.
                        await session.SendAsync(
                            new CNC_CZ_ClientTextOutputIndex
                            { Field1 = 0x8f2, Field2 = string.Empty },
                            token);
                        return;
                    }
                    targets.Add((b.Loc,
                        (def.MaxDurability - b.Durability)
                        * def.RepairCostPerPoint,
                        (short)def.MaxDurability));
                }
                var cost = targets.Sum(t => (long)t.Cost);
                if (cost <= 0)
                {
                    if (repairAll)
                    {
                        // Rien à réparer → 0x8f1 natif.
                        await session.SendAsync(
                            new CNC_CZ_ClientTextOutputIndex
                            { Field1 = 0x8f1, Field2 = string.Empty },
                            token);
                    }
                    return;   // loc inconnu / template absent : silence
                }
                // Trésorerie insuffisante → 0x8f0 natif.
                if (cost > guild.Treasury)
                {
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x8f0, Field2 = string.Empty },
                        token);
                    return;
                }
                if (!await s.Guilds.TrySpendTreasuryAsync(
                        guild.Id, cost)) return;
                foreach (var t in targets)
                    await s.Social.SetFamilyBuildingDurabilityAsync(
                        guild.Id, t.Loc, t.MaxDur);
                EdenLog.Write($"[Zone] FamilyBuildingRepair char=" +
                    $"{charId} guild={guild.Id} all={repairAll} " +
                    $"cost={cost} n={targets.Count}");
                // Le natif pousse la maj via ZMis 757 → CNC 486
                // (familier des visiteurs) ; le layout exact de 486
                // n'étant pas encore prouvé champ par champ, on
                // réémet 487 (codec prouvé) au demandeur — mêmes
                // données de conteneur.
                await session.SendPacketAsync(487,
                    Eden.Core.Game.Family.FamilyPackets
                        .FamilyInstanceData(guild.Id, guild.Name,
                            (short)guild.Level, guild.HouseOpen,
                            await s.Social.GetFamilyBuildingsAsync(
                                guild.Id, 0)), token);
            });

        // FamilyStatue [int,int,string,int,float] — statue de guilde.
        // Intercepté sur la boucle par ZoneEntryFlow.TryHandleOnLoopAsync
        // (case 161) — requiert l'EntryState (leader, hall visité) et
        // la vue des visiteurs. Voir HandleFamilyStatueAsync
        // (Execute natif @0x8257f10).

        // FamilyStorageQueryLog (179) → 506 ClientFamilyStorageLog
        // [u16 count][count×{s16 log_type, str log}] — journal du
        // coffre de guilde (SFamily+0xc8 natif : le deque est rempli
        // depuis family_storage_log au chargement de la famille).
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyStorageQueryLog(),
            async (session, msg, charId, token) =>
            {
                if (s.Guilds.GetGuildOf(charId) is not { } guild) return;
                var resp = new CNC_CZ_ClientFamilyStorageLog();
                foreach (var row in await s.FamilyBank
                             .GetStorageLogAsync(guild.Id))
                    resp.Entries.Add(new CNC_CZ_ClientFamilyStorageLog
                        .Entry { Action = row.LogType, Detail = row.Log });
                await session.SendAsync(resp, token);
            });

        // FamilyTalkToNpc (239) [u32 npcId] — le natif pose un CTalkOp
        // puis envoie CNC_ZMis_MissionServerListDialogue
        // {u32 charId, u32 npcId, s16 mapId} : même requête de liste
        // de dialogues que le TalkToNpc générique (pas de check de
        // portée dans l'Execute natif — les NPC famille sont joints
        // par l'UI de guilde, pas par clic dans le monde).
        d.RegisterChar(() => new CNE_CZ_ZoneServerFamilyTalkToNpc(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission is { Connected: true } mission)
                    await mission.SendListDialogueAsync(
                        (uint)charId, s.NodeId, msg.Value, token);
            });

        // FixEquipByFamily (240) — réparation de tout l'équipement
        // payée par la trésorerie de guilde. Natif : rate-limit 1 s
        // (char+0x650), GetItemRepairAllPrice, puis AgentDBACommand
        // "FamilyFixEquip Money = %d" vers le MissionServer qui débite
        // family.treasury — chez nous le débit est synchrone sur
        // guilds.treasury (même table portée par le repo).
        d.RegisterChar(() => new CNE_CZ_ZoneServerFixEquipByFamily(),
            async (session, msg, charId, token) =>
            {
                var now = DateTimeOffset.UtcNow;
                if (_fixEquipAt.TryGetValue(charId, out var last) &&
                    now - last < TimeSpan.FromSeconds(1))
                {
                    // Texte natif 0x621 (T_StringIndex : « action trop
                    // rapide ») — le natif annule silencieusement après
                    // le texte.
                    await session.SendAsync(
                        new CNC_CZ_ClientTextOutputIndex
                        { Field1 = 0x621, Field2 = string.Empty },
                        token);
                    return;
                }
                _fixEquipAt[charId] = now;
                if (s.Guilds.GetGuildOf(charId) is not { } guild) return;
                var cost = await ItemActionHandlers.RepairAllAsync(
                    s, charId);
                if (cost <= 0) return;
                if (!await s.Guilds.TrySpendTreasuryAsync(
                        guild.Id, cost)) return;
                await ItemActionHandlers.RepairAllAsync(
                    s, charId, apply: true);
                EdenLog.Write($"[Zone] FixEquipByFamily char={charId} " +
                    $"guild={guild.Id} cost={cost} (trésorerie)");
            });

        // UpdateFamily (81) [u32 charId] → 346 ClientChangeFamily
        // [u32 charId][i32 familyId][u8 flag][str name][i32 rank]
        // [s16 emblem][u8 revision] — rafraîchit les infos de guilde
        // du perso ciblé. Le natif exige la cible en ligne sur le node
        // (CNode::GetCharacter → NULL = silence).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateFamily(),
            async (session, msg, charId, token) =>
            {
                var targetId = (int)msg.Value;
                if (s.Sessions.FindByCharacter(targetId) == null) return;
                var guild = s.Guilds.GetGuildOf(targetId);
                var member = s.Guilds.GetMember(targetId);
                await session.SendAsync(new CNC_CZ_ClientChangeFamily
                {
                    // u32 = charId de la cible (char+0x34 natif).
                    Field1 = targetId,
                    // i32 = family_id (fam+4) ; 0 hors guilde.
                    Field2 = guild?.Id ?? 0,
                    // u8 = fam+0x18 (flag famille écrit par le ZW
                    // ChangeFamily — sémantique non confirmée, 0).
                    Field3 = 0,
                    // str = nom de famille (char+0x640 / fam+0xc).
                    Field4 = guild?.Name ?? string.Empty,
                    // i32 = rang natif 0-7 du membre (fam+0x1c ;
                    // -1 hors guilde).
                    Field5 = member != null ? member.FamilyRank : -1,
                    // s16 = id d'emblème (fam+0x20 ; 0xffff défaut).
                    Field6 = unchecked((short)(guild?.Emblem ?? -1)),
                    // u8 = révision d'affectation (char+0x4de) — le
                    // client compare pour invalider son cache famille.
                    Field7 = (byte)s.Guilds
                        .GetFamilyRevision(targetId),
                }, token);
            });
    }
}
