using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>Partage de quête en équipe — partial de ZoneEntryFlow
/// (plan futur-feature/01, étape 5). Transcription de
/// <c>CMissionListOp::DoOperation</c> @0x0856e7f0 (cas op 6 « Share » et
/// op 7 « réponse du destinataire », garde 0xb8a des ops &lt; 6). La
/// validation d'éligibilité (ShareMission @0x0807bfd0) vit côté
/// mission (QuestEngine.Share.cs). Règles/textes :
/// <see cref="QuestShareRules"/> ; état : <see cref="MissionShareRegistry"/>.
/// Hypothèse documentée : le test « membre occupé » natif
/// (<c>piVar13[0x19c] != 0</c>, texte 0x60c) n'est pas porté — son
/// champ n'est pas identifié.</summary>
public sealed partial class ZoneEntryFlow
{
    private readonly MissionShareRegistry _shares = new();

    /// <summary>Traite les ops de partage du C→S 87. Retourne true si
    /// l'op est consommée ici (pas de relais DBA générique).</summary>
    private async Task<bool> HandleMissionShareOpAsync(NetSession session,
        EntryState st, short op, int mid, int arg, CancellationToken token)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (op < 6)
        {
            // Garde native : une quête en cours de partage ne se
            // manipule pas (texte 0xb8a, aucune op relayée).
            if (team == null || mid <= 0 ||
                _shares.ActiveMid(team.Id) != mid) return false;
            await SendTextIndexAsync(session,
                QuestShareRules.TextBusyOp, token);
            return true;
        }
        if (op == 6) return await StartMissionShareAsync(session, st, team,
            mid, token);
        if (op == 7) return await AnswerMissionShareAsync(session, st,
            team, arg, token);
        return false;
    }

    /// <summary>Op 6 : sollicite chaque membre local de l'équipe.</summary>
    private async Task<bool> StartMissionShareAsync(NetSession session,
        EntryState st, Eden.Core.Game.Party.TeamEntity? team, int mid,
        CancellationToken token)
    {
        if (team == null || mid <= 0) return true;
        if (_shares.IsActive(team.Id))
        {
            await SendTextIndexAsync(session,
                QuestShareRules.TextAlreadySharing, token);
            return true;
        }
        var locals = LocalTeamMembers(st, team);
        var asked = new List<int>();
        foreach (var m in team.Members)
        {
            if (m.CharacterId == (int)st.CharId) continue;
            if (!locals.Exists(l => l.CharId == (uint)m.CharacterId))
            {
                await SendTextIndexArgsAsync(session,
                    QuestShareRules.TextNotLocal, m.Name, token);
                continue;
            }
            if (_mission.Connected)
                await _mission.SendAgentDbaCommandAsync(st.CharId,
                    $"MissionServiceOp {mid} 6 {m.CharacterId}", token);
            asked.Add(m.CharacterId);
        }
        _shares.Start(team.Id, (int)st.CharId, mid, asked);
        EdenLog.Write($"[QuestShare] char={st.CharId} partage {mid} → " +
            $"{asked.Count} membre(s)");
        return true;
    }

    /// <summary>Op 7 : le destinataire accepte (arg ≠ 0) ou refuse.</summary>
    private async Task<bool> AnswerMissionShareAsync(NetSession session,
        EntryState st, Eden.Core.Game.Party.TeamEntity? team, int arg,
        CancellationToken token)
    {
        if (team == null) return true;
        var ans = _shares.Answer(team.Id, (int)st.CharId, out _);
        if (ans == null) return true;
        var (sharerId, shareMid) = ans.Value;
        var sharer = _states.Values.FirstOrDefault(
            o => o.CharId == (uint)sharerId && o.Session != null);
        if (arg != 0 && _mission.Connected)
            // = accept natif (TC_MissionServiceOp cas 7 → SetFMS(3)).
            await _mission.SendAgentDbaCommandAsync(st.CharId,
                $"MissionServiceOp {shareMid} 7 {arg}", token);
        if (sharer?.Session != null)
            await SendTextIndexArgsAsync(sharer.Session,
                arg == 0 ? QuestShareRules.TextDeclined
                    : QuestShareRules.TextAccepted, st.CharName, token);
        EdenLog.Write($"[QuestShare] char={st.CharId} " +
            $"{(arg == 0 ? "refuse" : "accepte")} {shareMid} " +
            $"(partageur {sharerId})");
        return true;
    }
}
