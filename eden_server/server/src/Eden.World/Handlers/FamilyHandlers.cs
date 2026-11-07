using Eden.Core.Database.Repositories;
using Eden.Core.Game.Family;
using Eden.Core.Networking;
using Eden.Core.Logging;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.World.Handlers;

/// <summary>
/// Famille côté monde — <c>CNE_CW_ServerFamilyOp</c> (9),
/// <c>CNE_CW_ServerQueryFamilyInfo</c> (10),
/// <c>CNE_CW_ServerQueryFamilyEmblem</c> (73) — désasm world
/// (<c>Execute @ 0x80d9ea0</c> : switch natif de 22 ops).
/// Le natif délègue la persistance à la mission via des commandes
/// texte DBA (<c>CNC_DBA_AgentDBACommand</c>) ; notre monde écrit
/// directement dans edenfr_db puis pousse aux zones les NCIDs ZW natifs
/// (645 ChangeFamily / 646 NewFamily / 647 RemoveFamily).
/// </summary>
public static class FamilyHandlers
{
    /// <summary>Dépendances injectées par Program.cs.</summary>
    public sealed class Services
    {
        /// <summary>État famille monde (<c>AllFamilys</c> natif).</summary>
        public required FamilyService Families { get; init; }
        /// <summary>Accès à la table native <c>prestige</c>
        /// (<c>CExecutionerDBO::LoadPrestige</c> mission).</summary>
        public required Eden.Core.Database.Repositories
            .PrestigeRepository Prestige { get; init; }
        /// <summary>Id monde écrit en tête du paquet 50
        /// (<c>CGlobalInfo::WorldServerID</c> natif).</summary>
        public ushort WorldServerId { get; init; } = 1;
    }

    private static Services _s = null!;

    /// <summary>Indices des privilèges natifs
    /// (<c>FamilySetPrivilege</c> : 1=channel, 2=invite, 3=kickout,
    /// 4=editinfo, 5=setcadre, 6=invite25r, 7=fixequip, 8=msgboard,
    /// 9=delmsg, 10=treasury, 11=repair, 12-14=storage01..03).</summary>
    private const int PrivInvite = 2, PrivKickout = 3, PrivEditInfo = 4,
        PrivSetCadre = 5, PrivMsgboard = 8, PrivDelmsg = 9, PrivTreasury = 10;

    /// <summary>Enregistre les commandes CW de famille.</summary>
    public static void Register(PacketDispatcher d, Services s)
    {
        _s = s;
        // NCID 9 — opérations famille [u8 op][i32][i32][str][str].
        d.Register(() => new CNE_CW_ServerFamilyOp(), HandleFamilyOpAsync);
        // NCID 10 — requête infos famille [u8 flag].
        d.Register(() => new CNE_CW_ServerQueryFamilyInfo(), HandleQueryAsync);
        // NCID 73 — requête blob d'emblème [i32 family][i32].
        d.Register(() => new CNE_CW_ServerQueryFamilyEmblem(),
            async (session, msg, token) =>
            {
                var fam = _s.Families.GetFamily(msg.Field1);
                await session.SendPacketAsync(FamilyPackets.NcidFamilyEmblem,
                    FamilyPackets.FamilyEmblem(msg.Field1, msg.Field2, fam?.EmblemData), token);
            });
    }

    /// <summary>Dispatch des 22 sous-ops natifs du NCID 9 —
    /// <c>Execute @ 0x80d9ea0</c>.</summary>
    private static async Task HandleFamilyOpAsync(
        NetSession session, CNE_CW_ServerFamilyOp msg, CancellationToken token)
    {
        if (session.CharacterId is not { } charId) return;
        byte op = msg.Field1;
        int a = msg.Field2, b = msg.Field3;
        string s1 = msg.Field4, s2 = msg.Field5;
        try
        {
            switch (op)
            {
                case 0x01: await InviteAsync(session, charId, s1, token); break;
                case 0x02: _s.Families.CancelInvitesOf(charId); break;
                case 0x03: await AcceptAsync(session, charId, a, token); break;
                case 0x04: await RejectAsync(session, charId, a, token); break;
                case 0x05: await QuitAsync(session, charId, token); break;
                case 0x06: await KickAsync(session, charId, a, token); break;
                case 0x07: await SelectLeaderAsync(session, charId, a, token); break;
                case 0x08: await DisbandAsync(session, charId, token); break;
                case 0x0a: await EditInfoAsync(session, charId, s1, token); break;
                case 0x0b: await CadreTitleAsync(session, charId, (byte)a, s1, token); break;
                case 0x0c: await SetCadreAsync(session, charId, a, (byte)b, token); break;
                case 0x0e: await SetPrivilegeAsync(session, charId, (byte)a, b, token); break;
                case 0x0f: await AddMessageAsync(session, charId, s1, s2, token); break;
                case 0x11: await DelMessageAsync(session, charId, a, token); break;
                case 0x12: await ReadMessageAsync(charId, a); break;
                case 0x13: await EditCommentAsync(session, charId, a, s1, token); break;
                case 0x14: await SetTaxAsync(session, charId, a, business: false, token); break;
                case 0x15: await SetTaxAsync(session, charId, a, business: true, token); break;
                case 0x16: await RefreshLevelAsync(session, charId, token); break;
                case 0x46: await SetEmblemDataAsync(session, charId, s1, token); break;
                case 0x4e: await SetRecruitAsync(session, charId, a, token); break;
                case 0x4f: await RecruitPageAsync(session, (short)a, token); break;
                case 0x50: await RecruitJoinAsync(session, charId, a, token); break;
                default:
                    EdenLog.Write($"[Family] op inconnue {op} char={charId}");
                    break;
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Family] op {op} char={charId} erreur : {ex.Message}");
        }
    }

    /// <summary>Envoie un paquet monde à un perso en ligne
    /// (<c>ClientSessions</c> du lien ZW).</summary>
    private static Task SendToAsync(int charId, ushort ncid, byte[] payload) =>
        ZoneLinkServer.ClientSessions.TryGetValue(charId, out var s)
            ? s.SendPacketAsync(ncid, payload)
            : Task.CompletedTask;

    /// <summary>Diffuse un paquet à tous les membres en ligne d'une
    /// famille (<c>CGlobalInfo::SendCommandToFamilyMember</c> natif).</summary>
    private static async Task SendToMembersAsync(Family fam, ushort ncid, byte[] payload, int? excludeId = null)
    {
        // Snapshot : les mutations de Members passent par LockedAsync
        // mais cette diffusion s'exécute hors verrou.
        foreach (var id in fam.Members.Keys.ToList())
            if (id != excludeId && ZoneLinkServer.ClientSessions.ContainsKey(id))
                await SendToAsync(id, ncid, payload);
    }

    /// <summary>Push ZW natif vers la zone active.</summary>
    private static Task PushZwAsync(ushort ncid, byte[] payload) =>
        ZoneLinkServer.ActiveZoneSession is { } z
            ? z.SendPacketAsync(ncid, payload)
            : Task.CompletedTask;

    /// <summary>Erreur d'opération — <c>ClientFamilyOpException</c>
    /// (47) : [u8 op en échec].</summary>
    private static Task OpErrorAsync(NetSession s, byte op) =>
        s.SendPacketAsync(FamilyPackets.NcidFamilyOpException,
            FamilyPackets.FamilyOpException(op));

    /// <summary>Op 1 — invitation par nom
    /// (<c>FamilyInvitation</c> : privilège invite, cible sans famille
    /// et en ligne → <c>ClientFamilyInvitation</c> 48).</summary>
    private static async Task InviteAsync(NetSession session, int charId, string targetName, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !FamilyService.HasPrivilege(fam, charId, PrivInvite))
        { await OpErrorAsync(session, 0x01); return; }
        var targetId = await _s.Families.Repo.CharacterIdByNameAsync(targetName);
        if (targetId is null || targetId == charId || await _s.Families.Repo.FamilyIdOfAsync(targetId.Value) != 0)
        { await OpErrorAsync(session, 0x01); return; }
        if (!ZoneLinkServer.ClientSessions.ContainsKey(targetId.Value))
        { await OpErrorAsync(session, 0x01); return; }
        var inviterName = fam.Members.TryGetValue(charId, out var me) ? me.Name : targetName;
        _s.Families.AddInvite(targetId.Value,
            new FamilyService.PendingInvite(fam.Id, charId, inviterName, fam.Name));
        await SendToAsync(targetId.Value, FamilyPackets.NcidFamilyInvitation,
            FamilyPackets.FamilyInvitation(inviterName, fam.Name));
    }

    /// <summary>Op 3 — acceptation (<c>FamilyInvitationAccept</c> :
    /// ajout du membre + <c>UpdateFamilyMember</c> 46 aux membres +
    /// ZW 645 à la zone + persistance).</summary>
    private static async Task AcceptAsync(NetSession session, int charId, int inviterId, CancellationToken token)
    {
        var join = await _s.Families.LockedAsync(async () =>
        {
            if (!_s.Families.TryConsumeInvite(charId, inviterId, out var inv)) return (Family?)null;
            var fam = _s.Families.GetFamily(inv.FamilyId);
            if (fam is null || _s.Families.GetFamilyOf(charId) is not null) return null;
            var m = await _s.Families.Repo.LoadMemberSummaryAsync(charId);
            if (m is null) return null;
            m.Online = true;
            _s.Families.AddMember(fam, m);
            await _s.Families.Repo.JoinAsync(charId, fam.Id);
            await _s.Families.Repo.AddLogAsync(fam.Id, 1, charId, m.Name, "join");
            return fam;
        });
        if (join is null) { await OpErrorAsync(session, 0x03); return; }
        await NotifyMemberJoinedAsync(join, charId, token);
    }

    /// <summary>Notifications d'arrivée — 46 à tous + infos complètes
    /// au nouveau + push ZW 645.</summary>
    private static async Task NotifyMemberJoinedAsync(Family fam, int charId, CancellationToken token)
    {
        var m = fam.Members[charId];
        var rec = FamilyPackets.UpdateFamilyMember(m);
        await SendToMembersAsync(fam, FamilyPackets.NcidUpdateFamilyMember, rec);
        // Le nouveau membre reçoit la fiche complète (type 0 + membres).
        await SendToAsync(charId, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId));
        await SendToAsync(charId, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 8, 0, new List<FamilyMemberSummary>(fam.Members.Values)));
        // ZW ChangeFamily : [i32 charId][s16 node][i32 familyId]
        // [u8 flag][i32 rank][s16 emblem][s16 0].
        await PushZwAsync(ZoneLinkServer.NcidZoneServerChangeFamily,
            ZwChangeFamily(charId, fam, m));
    }

    /// <summary>Trame ZW ChangeFamily — layout natif
    /// <c>CNE_ZW_ZoneServerChangeFamily::Deserialize</c> (zone
    /// @0x82aae70 : i32,s16,i32,u8,i32,s16,s16). NCID compact 31 de
    /// notre lien ZW (le natif émet son indice de table 154).</summary>
    private static byte[] ZwChangeFamily(int charId, Family? fam, FamilyMemberSummary? m)
    {
        var s = new OutBinStream();
        s.WriteInt(charId);                       // i32 — charId ciblé
        s.WriteShort(0);                          // s16 — node_id (0 = toutes)
        s.WriteInt(fam?.Id ?? 0);                 // i32 — family_id
        s.WriteByte(0);                           // u8 — flag famille
        s.WriteInt(m?.Rank ?? 0);                 // i32 — rang (fam+0x1c zone)
        s.WriteShort((short)(fam?.Emblem ?? -1)); // s16 — emblem
        s.WriteShort(0);                          // s16 — réservé
        return s.ToArray();
    }

    /// <summary>Op 4 — refus d'invitation (l'inviteur est notifié
    /// par le 47 du natif : op 4 renvoyée).</summary>
    private static async Task RejectAsync(NetSession session, int charId, int inviterId, CancellationToken token)
    {
        _s.Families.TryConsumeInvite(charId, inviterId, out _);
        await SendToAsync(inviterId, FamilyPackets.NcidFamilyOpException,
            FamilyPackets.FamilyOpException(0x03));
    }

    /// <summary>Op 5 — départ volontaire (<c>FamilyQuit</c> : le chef
    /// ne peut pas quitter — texte 0x381 natif → 47).</summary>
    private static async Task QuitAsync(NetSession session, int charId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null) return;
        if (fam.LeaderId == charId) { await OpErrorAsync(session, 0x05); return; }
        await LeaveFamilyAsync(fam, charId, token);
        await session.SendPacketAsync(FamilyPackets.NcidFamilyQuit,
            FamilyPackets.FamilyQuit(), token);
    }

    /// <summary>Retire un membre : cache + persistance + 46 aux
    /// membres + ZW 645 famille=0.</summary>
    private static async Task LeaveFamilyAsync(Family fam, int charId, CancellationToken token)
    {
        FamilyMemberSummary? leaving = null;
        await _s.Families.LockedAsync(async () =>
        {
            fam.Members.TryGetValue(charId, out leaving);
            _s.Families.RemoveMember(fam, charId);
            await _s.Families.Repo.LeaveAsync(charId);
        });
        if (leaving is not null)
        {
            leaving.Online = false;
            await SendToMembersAsync(fam, FamilyPackets.NcidUpdateFamilyMember,
                FamilyPackets.UpdateFamilyMember(leaving));
        }
        await PushZwAsync(ZoneLinkServer.NcidZoneServerChangeFamily,
            ZwChangeFamily(charId, null, null));
    }

    /// <summary>Op 6 — exclusion (<c>FamilyKickOutMember</c> :
    /// privilège kickout, cible membre, pas le chef).</summary>
    private static async Task KickAsync(NetSession session, int charId, int targetId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || targetId == fam.LeaderId || !fam.Members.ContainsKey(targetId)
            || !FamilyService.HasPrivilege(fam, charId, PrivKickout)
            || (charId != fam.LeaderId && RankOf(fam, targetId) >= RankOf(fam, charId)))
        { await OpErrorAsync(session, 0x06); return; }
        await LeaveFamilyAsync(fam, targetId, token);
        await SendToAsync(targetId, FamilyPackets.NcidFamilyQuit,
            FamilyPackets.FamilyQuit());
        static short RankOf(Family f, int id) =>
            f.Members.TryGetValue(id, out var m) ? m.Rank : (short)0;
    }

    /// <summary>Op 7 — passation de chef (<c>FamilySelectLeader</c> :
    /// chef actuel uniquement, cible membre).</summary>
    private static async Task SelectLeaderAsync(NetSession session, int charId, int targetId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.LeaderId != charId || !fam.Members.ContainsKey(targetId))
        { await OpErrorAsync(session, 0x07); return; }
        await _s.Families.LockedAsync(async () =>
        {
            fam.LeaderId = targetId;
            if (fam.Members.TryGetValue(targetId, out var m)) m.Rank = 0;
            await _s.Families.Repo.SetLeaderAsync(fam.Id, targetId);
        });
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId));
    }

    /// <summary>Op 8 — dissolution (<c>FamilyDisband</c> : chef
    /// uniquement — delete_time + sortie de tous + ZW 647 + 49).</summary>
    private static async Task DisbandAsync(NetSession session, int charId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.LeaderId != charId)
        { await OpErrorAsync(session, 0x08); return; }
        var memberIds = fam.Members.Keys.ToList();
        await _s.Families.LockedAsync(async () =>
        {
            _s.Families.RemoveFamily(fam);
            await _s.Families.Repo.DisbandAsync(fam.Id);
        });
        // ZW 647 RemoveFamily [i32 family_id] + 49 à chaque membre.
        var s = new OutBinStream();
        s.WriteInt(fam.Id);
        await PushZwAsync(ZoneLinkServer.NcidZoneServerRemoveFamily, s.ToArray());
        foreach (var id in memberIds)
        {
            await SendToAsync(id, FamilyPackets.NcidFamilyQuit,
                FamilyPackets.FamilyQuit());
            await PushZwAsync(ZoneLinkServer.NcidZoneServerChangeFamily,
                ZwChangeFamily(id, null, null));
        }
    }

    /// <summary>Op 0xA — édition de l'info publique (privilège
    /// editinfo ; Field4 = nouveau texte).</summary>
    private static async Task EditInfoAsync(NetSession session, int charId, string info, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !FamilyService.HasPrivilege(fam, charId, PrivEditInfo))
        { await OpErrorAsync(session, 0x0a); return; }
        fam.Info = info;
        await _s.Families.Repo.EditInfoAsync(fam.Id, info);
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId));
    }

    /// <summary>Op 0xB — titre de cadre (privilège setcadre ; index
    /// 2..8 dans le tableau des 9 titres natif).</summary>
    private static async Task CadreTitleAsync(NetSession session, int charId, byte index, string title, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || index < 2 || index > 8
            || !FamilyService.HasPrivilege(fam, charId, PrivSetCadre))
        { await OpErrorAsync(session, 0x0b); return; }
        fam.Titles[index] = title;
        await _s.Families.Repo.SetCadreTitleAsync(fam.Id, index, title);
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 3, _s.WorldServerId));
    }

    /// <summary>Op 0xC — rang de cadre (privilège setcadre ; rang
    /// 0..7, 0 = retrait — <c>family_cadre_members</c>).</summary>
    private static async Task SetCadreAsync(NetSession session, int charId, int targetId, byte rank, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || targetId == fam.LeaderId || !fam.Members.TryGetValue(targetId, out var m)
            || rank > 7 || !FamilyService.HasPrivilege(fam, charId, PrivSetCadre))
        { await OpErrorAsync(session, 0x0c); return; }
        m.Rank = rank;
        await _s.Families.Repo.SetCadreAsync(fam.Id, targetId, rank);
        await SendToMembersAsync(fam, FamilyPackets.NcidUpdateFamilyMember,
            FamilyPackets.UpdateFamilyMember(m));
    }

    /// <summary>Op 0xE — privilège (chef uniquement ; index 1..14).</summary>
    private static async Task SetPrivilegeAsync(NetSession session, int charId, byte index, int value, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.LeaderId != charId || index < 1 || index > Family.PrivilegeCount)
        { await OpErrorAsync(session, 0x0e); return; }
        fam.Privileges[index - 1] = value;
        await _s.Families.Repo.SetPrivilegeAsync(fam.Id, index, value);
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 4, _s.WorldServerId));
    }

    /// <summary>Op 0xF — message board (privilège msgboard ;
    /// Field4 = titre, Field5 = contenu → <c>family_msgboard</c>).</summary>
    private static async Task AddMessageAsync(NetSession session, int charId, string title, string content, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !FamilyService.HasPrivilege(fam, charId, PrivMsgboard))
        { await OpErrorAsync(session, 0x0f); return; }
        var msg = await _s.Families.Repo.AddMessageAsync(fam.Id, title, content,
            fam.Members.TryGetValue(charId, out var me) ? me.Name : "");
        fam.Messages[msg.MessageId] = msg;
        // Le board est relu via l'agenda (51) — refresh aux membres.
        await SendToMembersAsync(fam, FamilyPackets.NcidFamilySchedule,
            FamilyPackets.FamilySchedule(0, 0, fam.Messages.Values.ToList()));
    }

    /// <summary>Op 0x11 — suppression de message (privilège delmsg).</summary>
    private static async Task DelMessageAsync(NetSession session, int charId, int messageId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !FamilyService.HasPrivilege(fam, charId, PrivDelmsg))
        { await OpErrorAsync(session, 0x11); return; }
        fam.Messages.Remove(messageId);
        await _s.Families.Repo.DeleteMessageAsync(fam.Id, messageId);
        await SendToMembersAsync(fam, FamilyPackets.NcidFamilySchedule,
            FamilyPackets.FamilySchedule(0, 0, fam.Messages.Values.ToList()));
    }

    /// <summary>Op 0x12 — marque de lecture
    /// (<c>player_characters.family_msgboard_state</c>).</summary>
    private static async Task ReadMessageAsync(int charId, int state)
    {
        if (_s.Families.GetFamilyOf(charId)?.Members.TryGetValue(charId, out var m) == true)
            m.MsgBoardState = state;
        await _s.Families.Repo.SetMsgBoardStateAsync(charId, state);
    }

    /// <summary>Op 0x13 — commentaire membre (Field2 = charId cible,
    /// Field4 = texte → <c>family_comment</c>).</summary>
    private static async Task EditCommentAsync(NetSession session, int charId, int targetId, string comment, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !fam.Members.TryGetValue(targetId, out var m)
            || (targetId != charId && !FamilyService.HasPrivilege(fam, charId, PrivEditInfo)))
        { await OpErrorAsync(session, 0x13); return; }
        m.FamilyComment = comment;
        await _s.Families.Repo.SetMemberCommentAsync(targetId, comment);
        await SendToMembersAsync(fam, FamilyPackets.NcidUpdateFamilyMember,
            FamilyPackets.UpdateFamilyMember(m));
    }

    /// <summary>Ops 0x14/0x15 — taxes (privilège treasury).</summary>
    private static async Task SetTaxAsync(NetSession session, int charId, int value, bool business, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || !FamilyService.HasPrivilege(fam, charId, PrivTreasury))
        { await OpErrorAsync(session, (byte)(business ? 0x15 : 0x14)); return; }
        if (business) { fam.BusinessTax = (short)value; await _s.Families.Repo.SetBusinessTaxAsync(fam.Id, value); }
        else { fam.TaxRate = (short)value; await _s.Families.Repo.SetTaxRateAsync(fam.Id, value); }
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId));
    }

    /// <summary>Niveau max de guilde (guide : 12 niveaux ; la table
    /// c_level en porte davantage).</summary>
    public const int MaxFamilyLevel = 12;

    private static List<Eden.Core.Data.Parsers.ProgressionParsers.LevelRow>?
        _levels;

    private static Eden.Core.Data.Parsers.ProgressionParsers.LevelRow?
        LevelRowOf(int level)
    {
        if (_levels == null)
        {
            var dir = Environment.GetEnvironmentVariable(
                Eden.Core.Data.ReforgedDataLocator.EnvVar);
            var path = dir == null ? null : Path.Combine(dir, "c_level.ini");
            _levels = path != null && File.Exists(path)
                ? Eden.Core.Data.Parsers.ProgressionParsers.ParseLevels(path)
                : new();
        }
        return _levels.FirstOrDefault(r => r.Level == level);
    }

    /// <summary>Op 0x16 — « Niveau sup. » : <c>CGlobalInfo::FamilySetLevel</c>
    /// world @0x08052ad0 + <c>TC_FamilySetLevel</c> mission @0x081ef170.
    /// Niveau ≥ 3 ; chef seul ; ligne c_level[niveau+1] : gloire
    /// (col 11) ≤ exp de guilde, fonds (col 18) ≤ trésorerie ; puis
    /// niveau+1, trésorerie −fonds, exp −gloire.</summary>
    private static async Task RefreshLevelAsync(NetSession session, int charId, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.Level < 3) return;
        if (fam.LeaderId != charId)
        { await OpErrorAsync(session, 0x16); return; }
        var next = fam.Level + 1;
        if (next > MaxFamilyLevel || LevelRowOf(next) is not { } row)
            return;
        if (fam.Exp < row.FamilyExpNeeded || fam.Treasury < row.FamilyFundNeeded)
        {
            EdenLog.Write($"[Family] niveau sup. refusé fam={fam.Id} " +
                $"lv={fam.Level}→{next} gloire {fam.Exp}/{row.FamilyExpNeeded} " +
                $"fonds {fam.Treasury}/{row.FamilyFundNeeded}");
            await OpErrorAsync(session, 0x16);
            return;
        }
        await _s.Families.LockedAsync(async () =>
        {
            fam.Level = (byte)next;
            fam.Exp -= row.FamilyExpNeeded;
            if (row.FamilyFundNeeded > 0)
                fam.Treasury = await _s.Families.Repo.AddTreasuryAsync(
                    fam.Id, -row.FamilyFundNeeded);
            await _s.Families.Repo.SetLevelAsync(fam.Id, fam.Level, fam.Exp);
        });
        EdenLog.Write($"[Family] niveau sup. fam={fam.Id} → {next} " +
            $"(−{row.FamilyExpNeeded} gloire, −{row.FamilyFundNeeded} fonds)");
        await SendToMembersAsync(fam, FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId));
    }

    /// <summary>Op 0x46 — blob d'emblème (Field4 = octets du blob ;
    /// <c>FamilySetEmblemData</c> → <c>family_emblem</c> + push 74).</summary>
    private static async Task SetEmblemDataAsync(NetSession session, int charId, string blob, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.LeaderId != charId)
        { await OpErrorAsync(session, 0x46); return; }
        var data = System.Text.Encoding.Latin1.GetBytes(blob);
        fam.EmblemData = data;
        await _s.Families.Repo.SetEmblemDataAsync(fam.Id, data);
        await SendToMembersAsync(fam, FamilyPackets.NcidFamilyEmblem,
            FamilyPackets.FamilyEmblem(fam.Id, 0, data));
    }

    /// <summary>Op 0x4E — ouverture du recrutement (chef, colonne
    /// <c>recruit</c>).</summary>
    private static async Task SetRecruitAsync(NetSession session, int charId, int recruit, CancellationToken token)
    {
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null || fam.LeaderId != charId)
        { await OpErrorAsync(session, 0x4e); return; }
        fam.Recruit = (short)recruit;
        await _s.Families.Repo.SetRecruitAsync(fam.Id, recruit);
    }

    /// <summary>Op 0x4F — page de recrutement (param s16 +0x14 =
    /// page ; natif <c>FamilySendRecruitPage</c> : éligible =
    /// <c>recruit &gt; 0 &amp;&amp; !IsFull</c>, 20 par page, page
    /// recalée vers le bas si hors borne).</summary>
    private static async Task RecruitPageAsync(NetSession session, short page, CancellationToken token)
    {
        var eligible = _s.Families.Families.Values
            .Where(f => f.Recruit > 0 && f.Members.Count < 30) // IsFull natif
            .ToList();
        short total = (short)eligible.Count;
        while (page * 20 >= total && page > 0) page--;        // recalage natif
        var entries = eligible.Skip(page * 20).Take(20).ToList();
        await session.SendPacketAsync(FamilyPackets.NcidQueryFamilyRecruitList,
            FamilyPackets.QueryFamilyRecruitList(entries, page, total), token);
    }

    /// <summary>Op 0x50 — adhésion via recrutement (Field2 = family
    /// id ; requiert recruit &gt; 0, pas de famille).</summary>
    private static async Task RecruitJoinAsync(NetSession session, int charId, int familyId, CancellationToken token)
    {
        var joined = await _s.Families.LockedAsync(async () =>
        {
            var fam = _s.Families.GetFamily(familyId);
            if (fam is null || fam.Recruit <= 0 || _s.Families.GetFamilyOf(charId) is not null)
                return (Family?)null;
            var m = await _s.Families.Repo.LoadMemberSummaryAsync(charId);
            if (m is null) return null;
            m.Online = true;
            _s.Families.AddMember(fam, m);
            await _s.Families.Repo.JoinAsync(charId, fam.Id);
            return fam;
        });
        if (joined is null) { await OpErrorAsync(session, 0x50); return; }
        await NotifyMemberJoinedAsync(joined, charId, token);
    }

    /// <summary>NCID 10 — <c>ServerQueryFamilyInfo</c> : flag=0 → rien
    /// ; famille absente → log natif, rien émis côté famille (capture :
    /// seul le prestige 60 part, poussé par le chemin attach natif) ;
    /// sinon type-0 + chunks type-8 de 30 + agenda 51 + journal 68
    /// (<c>Execute @ 0x80e7480</c>). Le prestige réel est synchronisé
    /// dans les deux cas — <c>AllPrestigeData</c> natif
    /// (mission @0x0808b5a0) : la capture montre la trame 60 juste
    /// après la réponse au 10.</summary>
    private static async Task HandleQueryAsync(
        NetSession session, CNE_CW_ServerQueryFamilyInfo msg, CancellationToken token)
    {
        if (msg.Value == 0 || session.CharacterId is not { } charId) return;
        var fam = _s.Families.GetFamilyOf(charId);
        if (fam is null)
        {
            // Natif : GetFamily(0) → NULL → log + return ; le prestige
            // arrive par le push attach (TC_QueryFriend →
            // AllPrestigeData → 60 + update_prestige zone).
            await SendPrestigeSyncAsync(session, charId, _s.Prestige,
                token);
            return;
        }
        if (fam.Members.TryGetValue(charId, out var me)) me.Online = true;
        // 1) fiche famille type 0.
        await session.SendPacketAsync(FamilyPackets.NcidQueryFamilyInfo,
            FamilyPackets.QueryFamilyInfo(fam, 0, _s.WorldServerId), token);
        // 2) membres par chunks de 30 (iVar22 > 0x1d natif).
        var members = fam.Members.Values.ToList();
        for (var i = 0; i < members.Count; i += Family.MemberChunkSize)
            await session.SendPacketAsync(FamilyPackets.NcidQueryFamilyInfo,
                FamilyPackets.QueryFamilyInfo(fam, 8, 0,
                    members.Skip(i).Take(Family.MemberChunkSize).ToList()), token);
        // 3) agenda (51 — le natif l'émet vide au login).
        await session.SendPacketAsync(FamilyPackets.NcidFamilySchedule,
            FamilyPackets.FamilySchedule(0, 0, fam.Messages.Values.ToList()), token);
        // 4) journal (68 — 6 vecteurs parallèles).
        await session.SendPacketAsync(FamilyPackets.NcidFamilyLogUpdate,
            FamilyPackets.FamilyLogUpdate(1,
                (short)(fam.Members.TryGetValue(charId, out var mm) ? mm.MsgBoardState : 0),
                fam.Logs.Where(l => l.Type == 1).ToList()), token);
        // 5) succès famille (67 flag=3 — synchro complète natif :
        // total + toutes les paires type→point de fam+0xb4, vecteurs
        // charId/channel à 0 par _M_fill_assign).
        await session.SendPacketAsync(
            FamilyPackets.NcidFamilyAchievementUpdate,
            FamilyPackets.FamilyAchievementUpdate(3, fam.AchievementTotal,
                fam.Achievements.Select(kv => (kv.Key, kv.Value, 0, 0u))
                    .ToList()), token);
        // 6) prestige — même synchro que le cas sans famille.
        await SendPrestigeSyncAsync(session, charId, _s.Prestige, token);
    }

    /// <summary>Synchro prestige complète — équivalent de
    /// <c>CGlobalInfo::AllPrestigeData</c> natif (mission @0x0808b5a0) :
    /// toutes les entrées <c>prestige</c> du perso → NCID 60
    /// <c>[u16 n][n×(s16 id, s16 level, i32 exp)]</c> au client +
    /// <c>update_prestige &lt;ch&gt; &lt;node&gt; &lt;id&gt; &lt;lv&gt;
    /// &lt;exp&gt;</c> par entrée vers la zone (miroir
    /// <c>CCharacter</c> map+0x6c4 — <c>CCharacter::UpdatePrestige</c>
    /// zone @0x08419ee0).</summary>
    internal static async Task SendPrestigeSyncAsync(NetSession session,
        int charId, PrestigeRepository prestige, CancellationToken token)
    {
        var rows = await prestige.LoadAllAsync(charId);
        var entries = rows
            .Select(kv => (kv.Key, kv.Value.Level, kv.Value.Exp))
            .ToList();
        // 60 CNC_CW_ClientUpdatePrestigeData
        // [u16 n][n×(s16 id, s16 level, i32 exp)].
        await session.SendPacketAsync(60,
            PrestigePackets.UpdatePrestigeData(entries), token);
        if (ZoneLinkServer.ActiveZoneSession is { } zone)
            foreach (var e in entries)
                await ZoneLinkServer.PushPrestigeToZoneAsync(zone,
                    charId, e.Key, e.Level, e.Exp, token);
    }
}
