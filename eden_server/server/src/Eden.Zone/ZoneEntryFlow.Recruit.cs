using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Codecs;

namespace Eden.Zone;

/// <summary>
/// Sous-système « tableau de recrutement d'instance » (LFG) —
/// NCID 220 <c>CNE_CZ_ZoneServerRecruitInstance</c> (inscription/
/// retrait), NCID 553 <c>CNC_CZ_ClientRecruitInstanceResult</c>
/// (résultats) et NCID 163 <c>CNE_CZ_ZoneServerNodeGroupReset</c>
/// (reset des instances d'équipe).
///
/// Preuves natives :
/// - wire 220 : <c>Deserialize</c> zone @0x0827c210 —
///   <c>[s16 count][count×s16 nodeIds][s16 role][u8 flag]</c> ;
///   dispatch flag 0/1 dans <c>Execute</c> @0x0827c2f0.
/// - gates add : <c>CPlayerFactory::RecruitInstanceAdd</c> zone
///   @0x08100c40 puis <c>CTeamManager::RecruitInstanceAdd</c> mission
///   @0x080ce5a0 (la mission est repliée en zone chez nous —
///   architecture mono-monde, comme le trial).
/// - matching : <c>CheckMakeTeam</c> mission @0x080c8490 + règles
///   <c>InitRecruitRule</c> @0x080cda70 → <see cref="RecruitBoard"/>.
/// - transport : <c>RecruitInstanceTransport</c> zone @0x080f1230 —
///   aire (node&lt;&lt;16)|0x15 (même remap [normal_area] key 21 que le
///   trial) → 553 [3][1] ; mort → 0x115a + [3][0].
/// - leave : <c>LeaveRecruitInstance</c> zone @0x080fff80 +
///   <c>RecruitInstanceLeave</c> mission @0x080c7290 → 553 [1][0]
///   par nœud retiré ; <c>RecruitInstanceLogout</c> à la déco ;
///   auto-leave à l'entrée d'un nœud instance hors {5,7,12}
///   (zone @0x08414960).
/// - 163 : <c>CCharacter::NodeGroupReset</c> zone @0x08424ff0 +
///   <c>CNodeGroup::CheckResetAllNode</c> @0x084a8210.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Types de nœuds « instance » — liste native
    /// <c>CNode::IsAnyInstance</c> zone @0x0812b430 (types 4-9, 10,
    /// 12, 14-19), partagée avec les gates du trial. Internal : le
    /// préchauffage boot (Program.cs) exclut ces types — leur
    /// population se crée par groupe à l'entrée.</summary>
    internal static readonly HashSet<int> InstanceNodeTypes = new()
        { 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 16, 17, 18, 19 };

    /// <summary>Types destination qui purgeaient la file à l'entrée
    /// du nœud (auto-leave natif zone @0x08414960) — tous les types
    /// instance SAUF 5, 7 et 12 qui sont les cibles de transport.</summary>
    private static readonly HashSet<int> RecruitAutoLeaveTypes = new()
        { 4, 6, 8, 9, 10, 14, 15, 16, 17, 18, 19 };

    /// <summary>Plages de nœuds à compteur d'entrées —
    /// <c>AddNodeTimes</c> natif zone @0x0841e9a0 : [100,300) et
    /// [600,900) uniquement.</summary>
    private static bool IsCountedNode(int nodeId) =>
        nodeId is >= 100 and < 300 or >= 600 and < 900;

    // ------------------------------------------------------------------
    //  Dispatch — NCID 220 (flag 0 = add, 1 = leave) et NCID 163.
    // ------------------------------------------------------------------

    /// <summary>NCID 220 — inscription ou retrait de la file de
    /// recrutement. Parse dédié : le wire variable est documenté dans
    /// <see cref="RecruitPackets.TryParse"/>.</summary>
    private async Task HandleRecruitInstanceAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken token)
    {
        if (!RecruitPackets.TryParse(payload, out var req))
        {
            EdenLog.Write($"[Recruit] char={st.CharId} trame 220 " +
                $"invalide len={payload.Length}");
            return;
        }
        if (req.Flag == 0)
            await RecruitAddAsync(session, st, req.Nodes, req.Role,
                token);
        else if (req.Flag == 1)
            await RecruitLeaveAsync(session, st, req.Role, token);
    }

    // ------------------------------------------------------------------
    //  Inscription (flag 0)
    // ------------------------------------------------------------------

    /// <summary>Flux natif complet : gates zone
    /// (<c>RecruitInstanceAdd</c> zone @0x08100c40) puis gates mission
    /// (@0x080ce5a0) puis insertion board + tentative de match par
    /// nœud. Émissions identiques : texte d'erreur puis 553.</summary>
    private async Task RecruitAddAsync(NetSession session, EntryState st,
        short[] nodes, short role, CancellationToken token)
    {
        // Gates zone dans l'ordre natif. IsNodeClosed (liste runtime
        // vide chez nous) et IsEncaged/flag-statue non modélisés —
        // skips assumés, cf. plan temp-plans/2026-11-04.
        if (st.RecruitState is not (-1 or 3))
        {
            await RecruitRefuseAsync(session, 0x1154, token);
            return;
        }
        if (st.RecruitState == 3 ||
            _services.Teams.GetTeamOf((int)st.CharId) != null)
        {
            await RecruitRefuseAsync(session, 0x1156, token);
            return;
        }
        if (nodes.Length is 0 or > 4)
        {
            await RecruitRefuseAsync(session, 0x1155, token);
            return;
        }
        if (InstanceNodeTypes.Contains(NodeType(st.NodeId)))
        {
            await RecruitRefuseAsync(session, 0x1157, token);
            return;
        }
        // +0xbb0/+0xbb4 natifs : membre d'une équipe-trial ou perso
        // déjà taggé evalId → refus 0x114d.
        if (st.EvalId != 0 ||
            (_services.Teams.GetTeamOf((int)st.CharId)?.EvalId ?? 0) != 0)
        {
            await RecruitRefuseAsync(session, 0x114d, token);
            return;
        }

        // Gates mission par nœud (RecruitInstanceAdd @0x080ce5a0) :
        // existant, niveau (+0x78 = RecruitMinLevel col 41), limite
        // d'entrées (+0x8a = MaxEntryTimes vs player_node_times).
        // +0x7a (mission requise, col 43 = '1' partout) et +0x7c/+0x80
        // (buff interdit, col 44 vide ou token unique → no-op prouvé)
        // sont documentés-skippés.
        foreach (var nodeId in nodes)
        {
            var def = _services.GameData.Nodes
                .FirstOrDefault(n => n.Id == nodeId);
            if (def == null || st.Level < def.RecruitMinLevel ||
                await RecruitTriesExceeded(st, def))
            {
                await RecruitRefuseAsync(session, 0x1169, token);
                return;
            }
        }

        // Enregistrement : état 0 + insertion board (natif : état et
        // liste posés AVANT le forward mission).
        st.RecruitState = 0;
        st.RecruitRole = role;
        st.RecruitNodes = nodes.ToList();
        _services.Recruits.Add((int)st.CharId, role, nodes);

        // CheckMakeTeam par nœud dans l'ordre de la liste — le premier
        // match forme l'équipe et transporte tout le monde.
        foreach (var nodeId in nodes)
        {
            var def = _services.GameData.Nodes
                .FirstOrDefault(n => n.Id == nodeId);
            if (def == null) continue;
            var matched = _services.Recruits.TryMatch(nodeId,
                def.CharLimit, RecruitCandidateOnline);
            if (matched != null)
            {
                await RecruitMakeTeamAsync(nodeId, matched, token);
                return;
            }
        }
        // En file : 553 [0][1].
        await session.SendAsync(
            new CNC_CZ_ClientRecruitInstanceResult
                { Field1 = 0, Field2 = 1 }, token);
    }

    /// <summary>Candidat valide pour le match — en ligne et
    /// encore en file (les entrées obsolètes d'un perso déjà sorti
    /// sont ignorées comme le retry -1 natif).</summary>
    private bool RecruitCandidateOnline(int charId) =>
        _services.Sessions.FindByCharacter(charId) is { } s &&
        _states.TryGetValue(s.SessionId, out var cst) &&
        cst.RecruitState == 0;

    /// <summary>Limite d'entrées mission-side : MaxEntryTimes ≠ 0 et
    /// compteur atteint. Charge la map paresseuse comme le trial.</summary>
    private async Task<bool> RecruitTriesExceeded(EntryState st,
        Eden.Core.Data.Parsers.GameDataParsers.NodeDef def)
    {
        if (def.MaxEntryTimes == 0) return false;
        st.NodeTimes ??= await _services.EvaluateRepo
            .GetNodeTimesAsync((int)st.CharId);
        return st.NodeTimes.GetValueOrDefault(def.Id, 0)
            >= def.MaxEntryTimes;
    }

    /// <summary>Refus d'inscription : texte index puis 553 [0][0] —
    /// ordre natif (TextOutputIndex puis le paquet résultat).</summary>
    private static async Task RecruitRefuseAsync(NetSession session,
        short textIndex, CancellationToken token)
    {
        await SendTextIndexAsync(session, textIndex, token);
        await session.SendAsync(
            new CNC_CZ_ClientRecruitInstanceResult
                { Field1 = 0, Field2 = 0 }, token);
    }

    // ------------------------------------------------------------------
    //  Formation d'équipe + transport (match trouvé)
    // ------------------------------------------------------------------

    /// <summary><c>InstanceMakeTeam</c> + <c>RecruitInstanceTransport</c>
    /// natifs, repliés mono-zone : le leader est le premier pick ;
    /// chaque membre quitte son équipe puis rejoint celle du leader
    /// (l'InviteAnswer forcée native), broadcast de la composition,
    /// puis transport individuel vers l'aire 21 du nœud et 553 [3][x].
    /// </summary>
    private async Task RecruitMakeTeamAsync(int nodeId,
        List<int> matched, CancellationToken token)
    {
        var leaderSess = _services.Sessions.FindByCharacter(matched[0]);
        if (leaderSess == null ||
            !_states.TryGetValue(leaderSess.SessionId, out var lst))
            return;
        // Équipes à notifier quand un membre est arraché (le leave
        // interne de CreateTeam pour le leader, LeaveTeam explicite
        // pour les autres — comme le leave manuel natif).
        var leaderOld = _services.Teams.GetTeamOf(matched[0]);
        var team = _services.Teams.CreateTeam(matched[0], lst.CharName,
            lst.Level, lst.ClassId);
        var (leaveOp, leavePayload) = PreparePacket(
            new CNC_CZ_ClientTeamLeaved());
        if (leaderOld != null)
            foreach (var mid in leaderOld.Members
                     .Select(m => m.CharacterId))
                if (mid != matched[0] &&
                    _services.Sessions.FindByCharacter(mid) is { } os)
                    await SendPreparedToAsync(os, leaveOp, leavePayload,
                        token);
        foreach (var cid in matched.Skip(1))
        {
            var old = _services.Teams.GetTeamOf(cid);
            if (_services.Teams.LeaveTeam(cid) && old != null)
                foreach (var mid in old.Members.Select(m => m.CharacterId))
                    if (_services.Sessions.FindByCharacter(mid)
                        is { } old2)
                        await SendPreparedToAsync(old2, leaveOp,
                            leavePayload, token);
            if (_services.Sessions.FindByCharacter(cid) is { } ms &&
                _states.TryGetValue(ms.SessionId, out var mst))
                _services.Teams.AddMember(team.Id, cid, mst.CharName,
                    mst.Level, mst.ClassId);
        }
        await Handlers.TeamHandlers.BroadcastTeamInfo(_services, team,
            token);

        // Transport : aire (node<<16)|0x15 — même remap normal_area
        // key 21 que l'entrée du trial (SetValidCoordInArea natif).
        var area = ResolveEvaluateEntryArea(nodeId);
        var (koOp, koPayload) = PreparePacket(
            new CNC_CZ_ClientRecruitInstanceResult
                { Field1 = 3, Field2 = 0 });
        var (okOp, okPayload) = PreparePacket(
            new CNC_CZ_ClientRecruitInstanceResult
                { Field1 = 3, Field2 = 1 });
        foreach (var cid in matched)
        {
            if (_services.Sessions.FindByCharacter(cid) is not { } ms ||
                !_states.TryGetValue(ms.SessionId, out var mst))
                continue;
            mst.RecruitState = 3;
            mst.RecruitRole = -1;
            mst.RecruitNodes.Clear();
            if (area == null || mst.PlayerHp <= 0)
            {
                // Branche transport KO : mort → 0x115a + [3][0]
                // (l'aire absente est une erreur serveur — même état
                // final 3 que le natif).
                if (mst.PlayerHp <= 0)
                    await SendTextIndexAsync(ms, 0x115a, token);
                else
                    EdenLog.Write($"[Recruit] node={nodeId} aire " +
                        "21 introuvable");
                await SendPreparedToAsync(ms, koOp, koPayload, token);
                continue;
            }
            if (IsCountedNode(nodeId))
                await AddNodeTimesAsync(ms, mst, nodeId, token);
            mst.Facing = area.Facing;
            await MigrateToNodeAsync(ms, mst, nodeId,
                area.X, area.Y, area.Z, token);
            await SendPreparedToAsync(ms, okOp, okPayload, token);
        }
        EdenLog.Write($"[Recruit] node={nodeId} équipe formée " +
            $"({matched.Count} membres → team {team.Id})");
    }

    // ------------------------------------------------------------------
    //  Retrait (flag 1) + auto-leave + logout
    // ------------------------------------------------------------------

    /// <summary>Retrait de la file — <c>LeaveRecruitInstance</c> zone
    /// @0x080fff80 : état 2 → texte 0x1159 ; état 3 → silencieux ;
    /// sinon reset + retrait board + 553 [1][0] par nœud retiré.</summary>
    private async Task RecruitLeaveAsync(NetSession session, EntryState st,
        short role, CancellationToken token)
    {
        if (st.RecruitState == 2)
        {
            await SendTextIndexAsync(session, 0x1159, token);
            return;
        }
        if (st.RecruitState == 3) return;
        st.RecruitState = -1;
        st.RecruitRole = -1;
        st.RecruitNodes.Clear();
        var removed = _services.Recruits.Remove((int)st.CharId);
        // Quirk natif fidèle : le 553 [1][0] n'est émis que si le
        // rôle de la requête < 3 (gate `(ushort)param_2 < 3` de
        // RecruitInstanceLeave mission @0x080c7290). Remove() purge
        // les deux maps quel que soit le rôle — les entrées obsolètes
        // que le natif laissait fuir sont filtrées au match.
        if (role < 3)
            foreach (var _ in removed)
                await session.SendAsync(
                    new CNC_CZ_ClientRecruitInstanceResult
                        { Field1 = 1, Field2 = 0 }, token);
    }

    /// <summary>Auto-leave à l'entrée d'un nœud instance hors
    /// {5,7,12} (zone @0x08414960) — appelé depuis MigrateToNodeAsync
    /// quand le perso est encore en file.</summary>
    private async Task RecruitAutoLeaveOnEnterAsync(NetSession session,
        EntryState st, int targetNodeId, CancellationToken token)
    {
        if (st.RecruitState == -1) return;
        var type = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == targetNodeId)?.Type ?? 0;
        if (RecruitAutoLeaveTypes.Contains(type))
        {
            EdenLog.Write($"[Recruit] char={st.CharId} quitte la " +
                $"file en entrant node={targetNodeId}");
            await RecruitLeaveAsync(session, st, st.RecruitRole, token);
        }
    }

    /// <summary>RecruitInstanceLogout natif : la file est purgée à la
    /// déconnexion (appelé depuis OnDisconnectAsync — la session est
    /// déjà morte, les paquets sont ignorés en interne).</summary>
    private void RecruitOnDisconnect(EntryState st)
    {
        if (st.RecruitState == -1) return;
        st.RecruitState = -1;
        st.RecruitRole = -1;
        st.RecruitNodes.Clear();
        _services.Recruits.Remove((int)st.CharId);
    }

    // ------------------------------------------------------------------
    //  NCID 163 — NodeGroupReset (payload vide)
    // ------------------------------------------------------------------

    /// <summary>Reset des instances de l'équipe —
    /// <c>CCharacter::NodeGroupReset</c> zone @0x08424ff0 +
    /// <c>CheckResetAllNode</c> @0x084a8210 : si un nœud instancié du
    /// groupe contient encore des membres → 1696 (0x6a0, "%s"=nom du
    /// nœud) au demandeur + 1699 (0x6a3) aux autres membres ; sinon
    /// destruction, 1698 (0x6a2) aux autres membres et 1697 (0x6a1)
    /// au demandeur.</summary>
    private async Task HandleNodeGroupResetAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        var others = team?.Members
            .Where(m => m.CharacterId != (int)st.CharId)
            .Select(m => m.CharacterId).ToList() ?? new List<int>();
        if (team?.EvalId is > 0 &&
            _services.Evaluates.Get(team.EvalId) is { } ev)
        {
            var nodeName = _services.GameData.Nodes
                .FirstOrDefault(n => n.Id == ev.Def.NodeTid)?.Name
                ?? string.Empty;
            if (ev.MemberCharIds.Count > 0)
            {
                // Occupants présents → refus avec nom du nœud.
                await SendTextIndexParamAsync(session, 0x6a0,
                    nodeName, token);
                foreach (var cid in others)
                    if (_services.Sessions.FindByCharacter(cid) is { } ms)
                        await SendTextIndexParamAsync(ms, 0x6a3,
                            nodeName, token);
                return;
            }
            // RemoveAllNode + RemoveAllNodeMap : destruction de
            // l'instance et dé-référencement de l'équipe — le
            // consentement en attente est annulé avec l'instance.
            _services.Evaluates.Remove(ev.Id);
            team.EvalId = 0;
            team.EvalNodeId = 0;
            foreach (var cid in others)
                if (_services.Sessions.FindByCharacter(cid) is { } ms)
                    await SendTextIndexAsync(ms, 0x6a2, token);
        }
        // CheckResetAllNode natif généralisé : le groupe d'instances
        // de l'équipe (ou le groupe personnel en solo) ne peut être
        // détruit que s'il ne contient plus aucun perso — sinon
        // refus 0x6a0 au demandeur / 0x6a3 aux autres membres avec
        // le nom du nœud occupé.
        var inst = team?.Id ?? -(int)st.CharId;
        var occupant = _states.Values.FirstOrDefault(
            o => o.InstanceGroup == inst);
        if (occupant != null)
        {
            var busyName = _services.GameData.Nodes
                .FirstOrDefault(n => n.Id == occupant.NodeId)?.Name
                ?? string.Empty;
            await SendTextIndexParamAsync(session, 0x6a0,
                busyName, token);
            foreach (var cid in others)
                if (_services.Sessions.FindByCharacter(cid) is { } ms)
                    await SendTextIndexParamAsync(ms, 0x6a3,
                        busyName, token);
            return;
        }
        DestroyGroupInstances(inst);
        foreach (var cid in others)
            if (_services.Sessions.FindByCharacter(cid) is { } ms)
                await SendTextIndexAsync(ms, 0x6a2, token);
        await SendTextIndexAsync(session, 0x6a1, token);
    }

    /// <summary>Variante de <see cref="SendTextIndexAsync"/> avec
    /// paramètre de format ("%s" natif — nom du nœud pour les
    /// refus 0x6a0/0x6a3 et le nœud fermé 0x174c).</summary>
    private static Task SendTextIndexParamAsync(NetSession session,
        short index, string param, CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = index,   // s16 : index dans T_TextIndex
            Field2 = param,   // str : argument "%s" du format
        }, token);
}
