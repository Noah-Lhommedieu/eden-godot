using Eden.Core.Game.Colosseum;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Colosseum (arène PvP par inscription) — NCIDs zone 196-201/219,
/// formats et gates transcrits du désassemblage natif :
/// <list type="bullet">
///   <item>196 <c>CNE_CZ_ZoneServerGladiatorTeamCreate</c>
///   [str][u8] — <b>no-op natif</b> : Execute @0x825cd90 =
///   tail-call <c>char-&gt;IsA()</c> ignoré (objdump). 197
///   <c>GladiatorTeamQuery</c> idem @0x825dd10.</item>
///   <item>198 <c>CNE_CZ_ZoneServerColosseumJoinAnswer</c>
///   [s16 accept] — Execute @0x8244720 : relais mission
///   <c>CNC_ZMis_MissionServerColosseumJoinAnswer</c>
///   [i32 charId][s16].</item>
///   <item>199 <c>CNE_CZ_ZoneServerColosseumJoinRequest</c> (vide) —
///   Execute @0x8244d30 : gates natives dans l'ordre (voir
///   <see cref="HandleColosseumJoinRequestAsync"/>).</item>
///   <item>200 <c>CNE_CZ_ZoneServerColosseumTeamQuery</c> (vide) —
///   Execute zone : char+0xbb4&gt;0 (en match) →
///   <c>CColosseumFactory::ColosseumTeamQuery</c> → 529.</item>
///   <item>201 <c>CNE_CZ_ZoneServerColosseumLeaveJoin</c> (vide) —
///   Execute @0x8245410 : en match (char+0xbb4≠0) → texte 0xd1f +
///   530 [charId][s16 0] ; sinon mission → 530.</item>
///   <item>219 <c>CNE_CZ_ZoneServerColosseumModeQuery</c> (vide) —
///   Execute @0x8245d30 : IsInNewerTown (node 94/768) → 0x2080 ;
///   sinon <c>ColosseumDataQuery</c> → 587 +
///   <c>ModeQueryResult</c> → 552.</item>
/// </list>
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>196/197 — `GladiatorTeamCreate`/`GladiatorTeamQuery`
    /// : le système gladiator est **mort en V.9** (Execute =
    /// tail-call <c>IsA()</c> dont le résultat n'est pas lu). On
    /// conserve le silence natif.</summary>
    private static bool HandleGladiatorDead() => true;

    /// <summary>198 `ColosseumJoinAnswer` [s16 accept] — réponse du
    /// client à l'invitation d'un match. Le natif relaie à la
    /// mission ; chez nous : refus (0) → sortie de file, sinon
    /// l'inscription est conservée (le match part à 2 inscrits).</summary>
    private async Task<bool> HandleColosseumJoinAnswerAsync(
        NetSession session, EntryState st, byte[] payload,
        CancellationToken ct)
    {
        if (payload.Length < 2) return true;
        var accept = BitConverter.ToInt16(payload, 0); // s16 — réponse
        if (accept == 0)
            _services.Colosseum.Leave((int)st.CharId);
        // accept != 0 : le natif laisse la mission décider — notre
        // file garde l'inscription jusqu'au match.
        return true;
    }

    /// <summary>199 `ColosseumJoinRequest` (vide) — inscription à la
    /// file d'arène. Ordre natif exact des gates
    /// (<c>Execute</c> @0x8244d30) :
    /// <list type="number">
    ///   <item>level : mode==2 (Twenty) exige &gt;59 → 0x12a5+528[1] ;
    ///   sinon ≥30 → 0xfc9+528[1].</item>
    ///   <item>node type 0xf (BattleRoyale) → 0x18cd+528[1] ;
    ///   0x11 (versus) → 0x1d98+528[1] ; 0x13 (crusade) →
    ///   0x225f+528[1].</item>
    ///   <item>pendings (file/match colosseum char+0xbb0/0xbb4,
    ///   versus pending, team≥11, lock +0x3b&amp;0x40) → 528[1]
    ///   silencieux.</item>
    ///   <item>node type interdit {4-9,0xa,0xb,0xc,0xe-0x13} →
    ///   528[5] silencieux.</item>
    ///   <item>node type 0xd (GreedyIsland) → 0x1661+528[1].</item>
    ///   <item>mode==3 (ClassPK) et SLV≤49 → 0x16d7+528[1].</item>
    ///   <item>OK → mission Join → file par catégorie.</item>
    /// </list></summary>
    private async Task<bool> HandleColosseumJoinRequestAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        var svc = _services.Colosseum;
        svc.RefreshMode(_services.GameData.Schedules,
            DateTimeOffset.UtcNow);

        // 1. Niveau : mode 2 → level>59 ; sinon ≥30.
        if (svc.Mode == ColosseumService.ModeTwenty)
        {
            if (st.Level < ColosseumService.TwentyMinLevel)
            {
                await SendTextIndexAsync(session, 0x12a5, ct);
                return await SendJoinResult(session, 1, ct);
            }
        }
        else if (st.Level < ColosseumService.MinLevel)
        {
            await SendTextIndexAsync(session, 0xfc9, ct);
            return await SendJoinResult(session, 1, ct);
        }
        var nt = NodeType(st);
        // 2. Scènes exclusives à texte (ordre natif).
        if (nt == 0xf)   // IsInBattleRoyale @0x8409a40
        {
            await SendTextIndexAsync(session, 0x18cd, ct);
            return await SendJoinResult(session, 1, ct);
        }
        if (nt == 0x11)  // IsInVersusInstance
        {
            await SendTextIndexAsync(session, 0x1d98, ct);
            return await SendJoinResult(session, 1, ct);
        }
        if (nt == 0x13)  // IsInBattleCrusade @0x8409b20
        {
            await SendTextIndexAsync(session, 0x225f, ct);
            return await SendJoinResult(session, 1, ct);
        }
        // 3. Pendings : +0xbb0 (file), +0xbb4 (match), +0xc40..0xc50
        // (pendings champ de bataille/versus), team≥11, lock +0x3b.
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (svc.InQueue((int)st.CharId) || svc.MatchOf((int)st.CharId)
                != null
            || st.VersusPending
            || (team != null && team.Members.Count >= 11)
            || InVersusScene(st))
            return await SendJoinResult(session, 1, ct);
        // 4. Node types interdits → 528[5] (silencieux natif).
        if (nt is >= 4 and <= 9 or 0xa or 0xb or 0xc
            or >= 0xe and <= 0x13)
            return await SendJoinResult(session, 5, ct);
        // 5. GreedyIsland (0xd) → 0x1661.
        if (nt == 0xd)
        {
            await SendTextIndexAsync(session, 0x1661, ct);
            return await SendJoinResult(session, 1, ct);
        }
        // 6. ClassPK : SLV (char+0x47e = niveau de classe) > 49.
        if (svc.Mode == ColosseumService.ModeClassPk
            && st.ClassLevel < ColosseumService.ClassPkMinClassLevel)
        {
            await SendTextIndexAsync(session, 0x16d7, ct);
            return await SendJoinResult(session, 1, ct);
        }
        // 7. Colosseum fermé → texte 0xdc8 natif (Join mission :
        // "Cant Find ColosseumWorldID" quand la zone arène est
        // absente — équivalent logique : pas ouvert).
        if (!svc.IsOpen)
        {
            await SendTextIndexAsync(session, 0xdc8, ct);
            return await SendJoinResult(session, 1, ct);
        }
        var formed = svc.Join((int)st.CharId, st.Level,
            st.ClassLevel);
        await SendJoinResult(session, 0, ct);   // inscription prise
        if (formed != null)
            await StartColosseumMatchAsync(formed, ct);
        return true;
    }

    /// <summary>200 `ColosseumTeamQuery` (vide) — le natif exige
    /// char+0xbb4&gt;0 (en match) puis construit le 529 depuis le
    /// <c>CColosseum</c> local (membres résolus par
    /// <c>GetCharacter</c> — seuls les connectés comptent).</summary>
    private async Task<bool> HandleColosseumTeamQueryAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        var match = _services.Colosseum.MatchOf((int)st.CharId);
        if (match == null) return true;
        var teams = new List<(short, IReadOnlyList<ColosseumPackets
            .TeamMember>)>();
        for (var t = 0; t < match.Teams.Count; t++)
        {
            var members = new List<ColosseumPackets.TeamMember>();
            foreach (var cid in match.Teams[t])
            {
                var mst = _states.Values.FirstOrDefault(
                    s => s.CharId == (uint)cid);
                if (mst == null) continue;   // hors zone (natif :
                // GetCharacter dans le node du match — les téléportés
                // restent résolus côté zone).
                members.Add(new ColosseumPackets.TeamMember(
                    cid,
                    0,                                  // u8 HeroFeature
                    (short)mst.Level,                   // s16 +0x470
                    0,                                  // u8 +0x47c
                    (short)mst.ClassLevel,              // s16 +0x47e
                    mst.CharName,                       // str +0x324
                    0,                                  // i32 +0xbb8
                    0,                                  // u16 +0xbc0
                    (byte)(mst.Dead ? 0 : 1)));         // u8 IsAlive
            }
            teams.Add(((short)(t + 1), members));
        }
        await session.SendPacketAsync(
            (ushort)ColosseumPackets.NcidTeamInfo,
            ColosseumPackets.TeamInfo((short)match.Category, teams),
            ct);
        return true;
    }

    /// <summary>201 `ColosseumLeaveJoin` (vide) — sortie de file.
    /// En match (char+0xbb4≠0) → texte 0xd1f + 530 [charId][0]
    /// natif ; sinon <c>LeaveJoin</c> mission → 530.</summary>
    private async Task<bool> HandleColosseumLeaveJoinAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        if (_services.Colosseum.MatchOf((int)st.CharId) != null)
        {
            await SendTextIndexAsync(session, 0xd1f, ct);
            await session.SendPacketAsync(
                (ushort)ColosseumPackets.NcidLeaveJoinResult,
                ColosseumPackets.LeaveJoinResult((int)st.CharId, 0),
                ct);
            return true;
        }
        _services.Colosseum.Leave((int)st.CharId);
        // Réponse mission native : [i32 charId][s16 result].
        await session.SendPacketAsync(
            (ushort)ColosseumPackets.NcidLeaveJoinResult,
            ColosseumPackets.LeaveJoinResult((int)st.CharId, 0), ct);
        return true;
    }

    /// <summary>219 `ColosseumModeQuery` (vide) — Execute @0x8245d30 :
    /// villes récentes (94/768) → 0x2080 ; sinon le natif demande le
    /// calendrier à la mission (587) puis répond le mode local
    /// (552). Ordre d'émission natif : 587 puis 552.</summary>
    private async Task<bool> HandleColosseumModeQueryAsync(
        NetSession session, EntryState st, CancellationToken ct)
    {
        if (st.NodeId is 94 or 768)
        {
            await SendTextIndexAsync(session, 0x2080, ct);
            return true;
        }
        var svc = _services.Colosseum;
        svc.RefreshMode(_services.GameData.Schedules,
            DateTimeOffset.UtcNow);
        await session.SendPacketAsync(
            (ushort)ColosseumPackets.NcidColosseumQuery,
            ColosseumPackets.ColosseumQuery(svc.TodaySlots(
                _services.GameData.Schedules, DateTimeOffset.UtcNow)),
            ct);
        await session.SendPacketAsync(
            (ushort)ColosseumPackets.NcidModeQueryResult,
            ColosseumPackets.ModeQueryResult((short)svc.Mode), ct);
        return true;
    }

    /// <summary>Emission du 528 `JoinResult` [s16].</summary>
    private async Task<bool> SendJoinResult(NetSession session,
        short result, CancellationToken ct)
    {
        await session.SendPacketAsync(
            (ushort)ColosseumPackets.NcidJoinResult,
            ColosseumPackets.JoinResult(result), ct);
        return true;
    }

    /// <summary>Démarrage d'un match — recode de
    /// <c>CColosseumFactory::NotifyEnterColosseum</c> mission
    /// @0x80575b0 + <c>Mission_TC_EnterColosseum</c> zone
    /// @0x83b61c0 : 527 [u8 1] à chaque membre puis téléportation
    /// vers le node arène (<c>CEventEnterColosseum</c> natif,
    /// ramené à une migration immédiate — le délai natif de l'event
    /// est court et sert à laisser le client lire la notif).</summary>
    private async Task StartColosseumMatchAsync(
        List<int> memberIds, CancellationToken ct)
    {
        var scene = _services.Scenes.GetScene(
            $"S{ColosseumService.ArenaNodeId:D3}");
        foreach (var cid in memberIds)
        {
            var mst = _states.Values.FirstOrDefault(
                s => s.CharId == (uint)cid);
            var sess = _services.Sessions.FindByCharacter(cid);
            if (mst == null || sess == null) continue;
            await sess.SendPacketAsync(
                (ushort)ColosseumPackets.NcidEnterColosseum,
                ColosseumPackets.EnterColosseum(1), ct);
            var (ex, ey, ez) = ResolveInstanceEntryPos(scene,
                Random.Shared);
            await MigrateToNodeAsync(sess, mst,
                (short)ColosseumService.ArenaNodeId, ex, ey, ez, ct);
        }
    }
}
