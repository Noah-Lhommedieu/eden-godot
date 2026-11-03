using Eden.Core.Game.Party;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Groupe : invitations, composition, départ, chef — quêtes (GiveUp,
/// QueryStates, SetTrace) et commandes GM/texte.
/// </summary>
public static class TeamHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Party/Team : Invite / Answer / Leave / ChangeLeader ---
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamInvite(),
            async (session, msg, charId, token) =>
            {
                var targetId = (int)msg.CharId;
                if (s.Teams.Invite(charId, targetId) == null) return;
                if (s.Sessions.FindByCharacter(targetId) is { } target)
                {
                    // Le client doit connaître l'inviteur : c'est le
                    // charId qu'il renvoie en +0x14 de la réponse 30.
                    var name = (await s.Characters
                            .GetCharacterByIdAsync(charId))?.GivenName
                        ?? string.Empty;
                    await target.SendAsync(new CNC_CZ_ClientTeamInviteNotice
                    {
                        Field1 = charId,   // i32 : inviteur
                        Field2 = name,     // str : son nom affiché
                    }, token);
                }
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamInviteAnswer(),
            async (session, msg, charId, token) =>
            {
                // Wire natif (Execute @0x82918f0) : +0x12 = type de
                // réponse (1 = accept), +0x14 = charId de l'inviteur —
                // pas d'id d'invitation : la résolution se fait par
                // la paire (inviteur, cible). `msg.Accepted` (+0x18)
                // accepté aussi pour compatibilité.
                var team = s.Teams.AnswerInviteByPair(msg.InviterId,
                    charId, msg.InviteId == 1 || msg.Accepted != 0,
                    session.Username ?? $"#{charId}", 1, 0);
                if (team == null) return;
                EdenLog.Write($"[Zone] char={charId} a rejoint team={team.Id} " +
                    $"({team.Members.Count} membres)");
                await BroadcastTeamInfo(s, team, token);
                // RecvWorldTeamJoinTeam natif → NotifyTeamMemberPos :
                // 392 positions des coéquipiers migrés au nouveau
                // membre (sa carte d'équipe sinon vide jusqu'à la
                // prochaine migration).
                if (s.TeamMemberPositions != null)
                    await s.TeamMemberPositions(session, charId, token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamLeave(),
            async (session, msg, charId, token) =>
            {
                var team = s.Teams.GetTeamOf(charId);
                // Natif : CharId ≠ émetteur = exclusion par le chef
                // (clic droit « Kick » du cadre de groupe). Le kické
                // reçoit 317, les restants le roster rafraîchi.
                var target = (int)msg.CharId;
                if (team != null && target != 0 && target != charId)
                {
                    if (s.Teams.KickMember(charId, target))
                    {
                        if (s.Sessions.FindByCharacter(target) is { } ks)
                            await ks.SendAsync(new CNC_CZ_ClientTeamLeaved(), token);
                        if (s.Teams.GetTeamOf(charId) is { } rest)
                            await BroadcastTeamInfo(s, rest, token);
                    }
                    return;
                }
                var memberIds = team?.Members.Select(m => m.CharacterId).ToList();
                if (s.Teams.LeaveTeam(charId) && memberIds != null)
                    foreach (var mid in memberIds)
                        if (s.Sessions.FindByCharacter(mid) is { } ms)
                            await ms.SendAsync(new CNC_CZ_ClientTeamLeaved(), token);
            });

        // TeamChangeLeader [u32 targetCharId] — désasm
        // CNE_CZ_ZoneServerTeamChangeLeader::Execute @0x80f4050 →
        // CPlayerFactory::TeamChangeLeader : leader uniquement, cible
        // membre de l'équipe, pas en SkyTower ; membre IsA==3
        // (hors-ligne/cadavre) → texte 0xb6b au demandeur. Succès →
        // CForceTeam::SetLeader @0x8475900 : texte 0xd6 ("%s" = nom
        // du nouveau chef) à chaque membre vivant + textex
        // "WorldTeamSetLeader" [u32 uid][i32 newLeader] vers le
        // monde — chez nous le roster est zone-local → refresh 324.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamChangeLeader(),
            async (session, msg, charId, token) =>
            {
                var team = s.Teams.GetTeamOf(charId);
                if (team == null || team.LeaderCharacterId != charId)
                    return;
                var target = team.Members.Find(
                    m => m.CharacterId == (int)msg.Value);
                if (target == null) return;
                if (!target.IsOnline)
                {
                    await session.SendAsync(new CNC_CZ_ClientTextOutputIndex
                    { Field1 = 0xb6b, Field2 = "" }, token);
                    return;
                }
                if (!s.Teams.TransferLeadership(charId, (int)msg.Value))
                    return;
                foreach (var m in team.Members)
                    if (m.IsOnline &&
                        s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                        await ms.SendAsync(new CNC_CZ_ClientTextOutputIndex
                        {
                            Field1 = 0xd6,       // s16 : « %s chef »
                            Field2 = target.Name,// str : nom du chef
                        }, token);
                await BroadcastTeamInfo(s, team, token);
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamInviteCancel(),
            (session, msg, charId, token) =>
            {
                s.Teams.CancelInvite(charId);
                return Task.CompletedTask;
            });

        // --- Quêtes : GiveUp / QueryMissionStates / SetMissionTrace ---
        // 51 GiveUpMission [s16 missionId] — natif : relayé tel quel à
        // la mission (CNC_ZMis_MissionServerGiveUpMission
        // `[i32 charUid][s16 nodeId][s16 mid]`, désasm Execute
        // @0x825d240 / Serialize @0x825d7e0). L'état de quête appartient
        // à la mission — pas de mutation locale.
        d.RegisterChar(() => new CNE_CZ_ZoneServerGiveUpMission(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendGiveUpMissionAsync((uint)charId,
                        s.NodeId, msg.Value, token);
                EdenLog.Write(
                    $"[Zone] GiveUpMission char={charId} mid={msg.Value}");
            });

        // 50 QueryMissionStates -> 348 ClientMissionStates.
        // Serialize natif @0x8202790 — structure exacte :
        //   [s16 n1][n1 × (u16,u16)]   map<u16,u16>  (+0x18 = node_count)
        //   [s16 n2][n2 × (u16,s16)]   map<u16,s16>  (+0x30)
        //   [s16 n3][n3 × (u16,s16)]   map<u16,s16>  (+0x48)
        //   [s16 n4][n4 × (u32,u16)]   map<u32,u16>  (+0x60)
        //   [s16 n5][n5 × s8]          vector<char>
        //   [s16 n6][n6 × s16]         vector<short>
        //   [u16 n7][n7 × i32]         vector<int>
        // La 1re map = personal_mission_states (id=char, index, state).
        // Le reste est laissé vide : la mission pousse les deltas par
        // 67->391 ; l'état initial d'un perso sans quête = 7 compteurs 0.
        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryMissionStates(),
            async (session, msg, charId, token) =>
            {
                var states = await s.MissionRepo
                    .GetPersonalMissionStatesAsync(charId, token);
                var reply = new CNC_CZ_ClientMissionStates();
                foreach (var (index, state) in states)
                    reply.Fms.Add(new() { Mid = (ushort)index,
                        State = (ushort)state });
                await session.SendAsync(reply, token);
                EdenLog.Write(
                    $"[Zone] QueryMissionStates char={charId} -> " +
                    $"{states.Count} états");
            });

        d.RegisterChar(() => new CNE_CZ_ZoneServerSetMissionTrace(),
            (session, msg, charId, token) =>
            {
                // Natif Execute @0x08286650 : stocke les 10 ids à
                // char+0x8a0 puis forward AgentDBACommand vers la mission
                // (mission → UPDATE mission_trace SET trace01..10).
                var ids = msg.MissionIds;
                var args = string.Join(' ', Enumerable.Range(0, 10)
                    .Select(i => $"mission_trace{(i + 1):D2} = " +
                        (i < ids.Count ? ids[i] : 0)));
                EdenLog.Write($"[Zone] SetMissionTrace char={charId} " +
                    $"ids=[{string.Join(',', ids)}]");
                if (s.Mission is { Connected: true } link)
                    return link.SendAgentDbaCommandAsync((uint)charId,
                        $"SetMissionTrace {args}", token);
                return Task.CompletedTask;
            });

        // --- TextCommand(GM + chat zone) ---
        // NCID 2 WhoIs est intercepté en amont par
        // ZoneEntryFlow.TryHandleOnLoopAsync → 263/301/326 natifs.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTextCommand(),
            async (session, msg, charId, token) =>
            {
                var text = msg.Text;
                if (!text.StartsWith("//"))
                {
                    // Texte normal de chat zone — broadcast à toute la zone
                    await s.Sessions.BroadcastAllAsync(new ChatMessage
                    {
                        SenderName = session.Username ?? $"#{charId}",
                        Channel = 0,
                        Message = text,
                    }, token);
                    return;
                }
                // Commande GM — "//nom args" via S_RootCmds.ini +
                // handlers async (clone_item, gain_gold, points…).
                // La réponse revient au demandeur en chat système.
                var ch = await s.Characters.GetCharacterByIdAsync(charId);
                var response = await s.Gm.ExecuteAsync(text[2..],
                    ch?.Privilege ?? 0,
                    new GmCommandService.Context(charId, session, token));
                EdenLog.Write($"[Zone] GM char={charId} '{text}' -> {response ?? "?"}");
                if (response != null)
                    await session.SendAsync(new Eden.Core.Networking
                        .Messages.World.ChatMessage
                    {
                        SenderName = "[GM]",
                        Channel = 0,
                        Message = response,
                    }, token);
            });

        // TeamOperation — wire natif [s16 op][s16 param1][s16 param2]
        // (désasm CNE_CZ_ZoneServerTeamOperation::Execute
        // zone:532135). Ops leader-gated : 0=loot rule (ChageLootRule,
        // param1=1 Free/2 Random/3 Taunt-Exclusive), 1=→raid cap 10
        // (ChangeToRaid), 3=roll rule (ChageRollRule, param1=seuil
        // qualité 0-2), 6=→raid cap 25 (CreateRaid25Team). Les ops
        // evaluate (4 p1=3 = TeamCheck trial, 5 = réponse) sont
        // interceptées en amont par TryHandleOnLoopAsync →
        // ZoneEntryFlow.Evaluate.cs ; op 7/8 (tour R25) restent
        // ignorées — sous-système d'instances inexistant.
        // Notification : le natif pousse WorldTeamUpdateTeamInfo via la
        // mission → 381 ClientTeamInfo [s16 type][s16 valeur] aux
        // membres (9=loot rule, 10=roll rule — champs team +0x98/+0x9a
        // de RecvWorldTeamInfo).
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamOperation(),
            async (session, msg, charId, token) =>
            {
                var team = msg.Field1 switch
                {
                    0 => s.Teams.ChangeLootRule(charId, msg.Field2),
                    3 => s.Teams.ChangeRollRule(charId, msg.Field2),
                    1 => s.Teams.ConvertToRaid(charId, 10),
                    6 => s.Teams.ConvertToRaid(charId, 25),
                    _ => null,
                };
                EdenLog.Write($"[Zone] TeamOp char={charId} " +
                    $"op={msg.Field1} param={msg.Field2} " +
                    $"-> {(team != null ? "ok" : "refusé/exclu")}");
                if (team == null) return;
                switch (msg.Field1)
                {
                    case 0:  // type 9 = loot rule (RecvWorldTeamInfo +0x98)
                        await NotifyTeamInfoAsync(s, team, 9,
                            team.LootRuleValue, token);
                        break;
                    case 3:  // type 10 = roll rule (+0x9a)
                        await NotifyTeamInfoAsync(s, team, 10,
                            team.RollRule, token);
                        break;
                    case 1:  // ChangeToRaid : texte 1578 « Convert
                    case 6:  // Party to Raid » + rafraîchissement complet
                        await NotifyRaidConversionAsync(s, team, token);
                        break;
                }
            });

        // SearchTeamV2 — wire natif [s16][s16][string] (Deserialize
        // @0x082855xx) ; le natif délègue à la mission
        // (RecruitTeamSearchTeamV2) et relaie
        // ClientSearchTeamInfoV2 — notre index d'annonces est
        // zone-local (TeamService._recruits) tant que le bridge
        // RecruitTeam n'existe pas ; la réponse client est
        // identique.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSearchTeamV2(),
            async (session, msg, charId, token) =>
            {
                var resp = new CNC_CZ_ClientSearchTeamInfoV2();
                foreach (var post in s.Teams.SearchRecruits(
                    msg.Field1, msg.Field2, msg.Field3))
                {
                    var entry = new CNC_CZ_ClientSearchTeamInfoV2
                        .Entry { Comment = post.Message,
                                 Type = post.F2 };
                    var team = post.TeamId != 0
                        ? s.Teams.GetTeam(post.TeamId) : null;
                    if (team != null)
                        foreach (var m in team.Members)
                            entry.Members.Add(MemberInfo(s, m));
                    else if (await SoloMemberAsync(s, post.OwnerCharId)
                        is { } solo)
                        entry.Members.Add(solo);
                    if (entry.Members.Count > 0)
                        resp.Entries.Add(entry);
                }
                await session.SendAsync(resp, token);
            });

        // TeamRecruitV2 — wire natif
        // [u8 enable][s16][s16][s16][u32][string] (Deserialize
        // @0x082935xx) → CCharacter::ChangeRecruitDataV2 : solo →
        // annonce perso ; en équipe → leader uniquement (refus natif
        // texte 0x1819 si un membre est en map type 0xe — non
        // reproduit : pas d'annonce si non leader). Le broadcast
        // ClientTeamRecruitMsgV2 [u8][s16][s16][string][u32] est
        // émis à tout le monde, annonceurs et chercheurs.
        d.RegisterChar(() => new CNE_CZ_ZoneServerTeamRecruitV2(),
            (session, msg, charId, token) =>
            {
                var post = s.Teams.RegisterRecruit(charId,
                    msg.Field1 != 0, msg.Field2, msg.Field3,
                    msg.Field4, msg.Field5, msg.Field6);
                if (post == null) return Task.CompletedTask;
                return s.Sessions.BroadcastAllAsync(
                    new CNC_CZ_ClientTeamRecruitMsgV2
                    {
                        Field1 = msg.Field1,        // u8  : annonce active
                        Field2 = msg.Field2,        // s16 : param recrut
                        Field3 = msg.Field4,        // s16 : param recrut
                        Field4 = msg.Field6,        // string : message
                        Field5 = (uint)charId,      // u32 : charId déposant
                    }, token);
            });
    }

    /// <summary>Entrée membre d'une annonce — champs s16 M3-M6 non
    /// désassemblés (probablement map/online/flags comme
    /// `ClientTeamAllMembersInfo`) → 0.</summary>
    /// <summary>Entrée membre V2 — partagée avec le SearchTeam V1 de
    /// MiscHandlers. Layout `SSearchTeamMemberDataV2` natif
    /// (CTeamMember::GetTeamMemberData @0x81c8b40 mission) :
    /// [i32 charId][str nom][s16 lvl][s16 classe][s16 +0xc][s16
    /// zoneId][s16 +0x30][s16 position=+0x87e]. M6 = position de
    /// recrutement (ChangePositionV2, défaut natif 2).</summary>
    internal static CNC_CZ_ClientSearchTeamInfoV2.Member MemberInfo(
        ZoneServices s, TeamEntity.TeamMember m) => new()
    {
        CharId = m.CharacterId,
        Name = m.Name,
        Level = (short)m.Level,
        ClassId = (short)m.ClassId,
        M6 = s.Teams.GetRecruitPosition(m.CharacterId),
    };

    /// <summary>Entrée membre d'une annonce solo — données fraîches
    /// de la DB (le déposant peut être offline : son annonce reste
    /// cherchable, comme le natif via la mission).</summary>
    internal static async Task<CNC_CZ_ClientSearchTeamInfoV2.Member?>
        SoloMemberAsync(ZoneServices s, int charId)
    {
        var c = await s.Characters.GetCharacterByIdAsync(charId);
        if (c == null) return null;
        return new CNC_CZ_ClientSearchTeamInfoV2.Member
        {
            CharId = charId,
            Name = c.GivenName,
            Level = (short)c.Level,
            ClassId = (short)c.ClassId,
            M6 = s.Teams.GetRecruitPosition(charId),
        };
    }

    /// <summary>
    /// Envoie la composition complète de l'équipe à tous les membres
    /// (layout natif : short count + entrées + short suffixe).
    /// </summary>
    /// <summary>Diffuse la composition de l'équipe — réutilisé par le
    /// recrutement d'instance (NCID 220, équipe formée par le
    /// matchmaking sans passer par les invites).</summary>
    internal static async Task BroadcastTeamInfo(ZoneServices s,
        TeamEntity team, CancellationToken token)
    {
        var msg = new CNC_CZ_ClientTeamAllMembersInfo();
        short slot = 0;
        foreach (var m in team.Members)
        {
            msg.Entries.Add(new CNC_CZ_ClientTeamAllMembersInfo.Entry
            {
                Slot = slot++,
                Flags = (short)(m.IsLeader ? 1 : 0),
                CharId = (uint)m.CharacterId,
                CharName = m.Name,
                Level = m.Level,
                M2 = (short)m.ClassId,
                M3 = (short)m.MapId,
                M4 = (short)(m.IsOnline ? 1 : 0),
            });
        }
        // TeamMode = règle de loot native (1-3) — le client l'affiche
        // via les textes 208/207/2156, pas notre enum interne.
        msg.TeamMode = team.LootRuleValue;
        foreach (var m in team.Members)
            if (s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                await ms.SendAsync(msg, token);
    }

    /// <summary>381 ClientTeamInfo `[s16 type][s16 valeur]` à chaque
    /// membre — équivalent du `CForceTeam::NotifyTeamInfo` mission →
    /// `RecvWorldTeamInfo` zone (paquet [type][valeur] via NotifyLocal,
    /// désasm zone:137617/157835). Types : 9=loot rule, 10=roll rule.</summary>
    private static async Task NotifyTeamInfoAsync(ZoneServices s,
        TeamEntity team, short type, short value, CancellationToken token)
    {
        foreach (var m in team.Members)
            if (s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                await ms.SendAsync(new CNC_CZ_ClientTeamInfo
                {
                    Field1 = type,     // s16 : champ team modifié
                    Field2 = value,    // s16 : nouvelle valeur
                }, token);
    }

    /// <summary>Conversion party→raid (`CForceTeam::ChangeToRaid`) :
    /// texte natif 1578 « Convert Party to Raid » à chaque membre
    /// (notify 0x62a avec nom du leader côté natif — arg %s1) puis
    /// rafraîchissement complet de la composition.</summary>
    private static async Task NotifyRaidConversionAsync(ZoneServices s,
        TeamEntity team, CancellationToken token)
    {
        var leaderName = team.Members
            .FirstOrDefault(m => m.IsLeader)?.Name ?? string.Empty;
        foreach (var m in team.Members)
            if (s.Sessions.FindByCharacter(m.CharacterId) is { } ms)
                await ms.SendAsync(new CNC_CZ_ClientTextOutputIndex
                {
                    Field1 = 1578,        // s16 : « Convert Party to Raid »
                    Field2 = leaderName,  // str : arg %s1 (nom du leader)
                }, token);
        await BroadcastTeamInfo(s, team, token);
    }
}
