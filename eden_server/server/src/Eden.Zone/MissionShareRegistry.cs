namespace Eden.Zone;

/// <summary>
/// État de partage de quête par équipe — équivalent de
/// <c>CForceTeam::GetShareMissionMember/GetShareMissionID/
/// SetShareMissionMember</c> (zone @0x0846cdf0/0x0846ce00/0x0846e440) :
/// une équipe ne partage qu'une quête à la fois ; la liste d'attente est
/// celle de l'opération <c>CMissionListOp</c> du partageur. Accédé sur la
/// boucle de zone uniquement (pas de verrou). Règles de texte :
/// <see cref="Eden.Core.Game.Services.QuestShareRules"/>.
/// </summary>
public sealed class MissionShareRegistry
{
    private sealed record Share(int Sharer, int Mid, HashSet<int> Pending);

    private readonly Dictionary<int, Share> _byTeam = new();

    /// <summary>L'équipe a un partage en cours (GetShareMissionMember ≠ 0).</summary>
    public bool IsActive(int teamId) => _byTeam.ContainsKey(teamId);

    /// <summary>Id de la quête en cours de partage (0 = aucune) —
    /// GetShareMissionID natif.</summary>
    public int ActiveMid(int teamId) =>
        _byTeam.TryGetValue(teamId, out var s) ? s.Mid : 0;

    /// <summary>Partageur courant de l'équipe (0 = aucun).</summary>
    public int SharerOf(int teamId) =>
        _byTeam.TryGetValue(teamId, out var s) ? s.Sharer : 0;

    /// <summary>Ouvre un partage. False si l'équipe partage déjà ou si
    /// aucun destinataire (natif : SetShareMissionMember seulement si
    /// au moins un membre a été sollicité).</summary>
    public bool Start(int teamId, int sharer, int mid,
        IEnumerable<int> receivers)
    {
        if (_byTeam.ContainsKey(teamId)) return false;
        var pending = new HashSet<int>(receivers);
        pending.Remove(sharer);
        if (pending.Count == 0) return false;
        _byTeam[teamId] = new Share(sharer, mid, pending);
        return true;
    }

    /// <summary>Réponse d'un destinataire : le retire de la liste
    /// d'attente ; <paramref name="finished"/> = liste vide → partage
    /// clos (SetShareMissionMember(0,0)). Null si le destinataire n'est
    /// pas attendu.</summary>
    public (int Sharer, int Mid)? Answer(int teamId, int receiver,
        out bool finished)
    {
        finished = false;
        if (!_byTeam.TryGetValue(teamId, out var s) ||
            !s.Pending.Remove(receiver)) return null;
        if (s.Pending.Count == 0)
        {
            _byTeam.Remove(teamId);
            finished = true;
        }
        return (s.Sharer, s.Mid);
    }

    /// <summary>Annule le partage (équipe dissoute).</summary>
    public void Clear(int teamId) => _byTeam.Remove(teamId);

    /// <summary>Annule les partages lancés par ce perso (il quitte
    /// l'équipe ou se déconnecte) — et le retire des listes d'attente.</summary>
    public void ClearChar(int charId)
    {
        foreach (var (team, s) in _byTeam.ToList())
        {
            s.Pending.Remove(charId);
            if (s.Sharer == charId || s.Pending.Count == 0)
                _byTeam.Remove(team);
        }
    }
}
