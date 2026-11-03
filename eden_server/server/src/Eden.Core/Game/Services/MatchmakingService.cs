using Eden.Core.Game.Party;

namespace Eden.Core.Game.Services;

/// <summary>
/// Matchmaking / recherche de groupe (LFG).
/// Correspond a CAutoBattlefieldMatch + la file d'attente LFG du client
/// (random dungeons, role balancing).
/// </summary>
public sealed class MatchmakingService
{
    private readonly TeamService _teams;

    public sealed class QueueEntry
    {
        public int CharacterId { get; set; }
        public string Role { get; set; } = "dps";      // tank/heal/dps
        public int Level { get; set; }
        public int? BattlefieldId { get; set; }        // null = random
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    }

    private readonly List<QueueEntry> _queue = new();
    private readonly List<MatchResult> _formed = new();

    public sealed class MatchResult
    {
        public int? BattlefieldId { get; set; }
        public List<int> Members { get; } = new();
        public DateTime FormedAt { get; set; } = DateTime.UtcNow;
    }

    public MatchmakingService(TeamService teams) => _teams = teams;

    /// <summary>Rejoint la file d'attente.</summary>
    public void Enqueue(int characterId, string role, int level,
        int? battlefieldId = null)
    {
        _queue.RemoveAll(e => e.CharacterId == characterId);
        _queue.Add(new QueueEntry
        {
            CharacterId = characterId,
            Role = role,
            Level = level,
            BattlefieldId = battlefieldId,
        });
    }

    public void Dequeue(int characterId)
        => _queue.RemoveAll(e => e.CharacterId == characterId);

    /// <summary>Le char attend dans une file (arène/battlefield) —
    /// gate natif de CheckBeginStallForPlayer (char+0xbb0/0xc40
    /// family → textes 0xd20/0x1160).</summary>
    public bool IsQueued(int characterId) =>
        _queue.Any(e => e.CharacterId == characterId);

    public int QueueCount => _queue.Count;

    /// <summary>
    /// Forme des groupes: 1 tank + 1 heal + 3 dps pour un groupe de 5
    /// (taille par defaut des donjons).
    /// </summary>
    public IReadOnlyList<MatchResult> ProcessQueue(int groupSize = 5)
    {
        var formed = new List<MatchResult>();
        // groupes par battlefield cible
        var byBf = _queue.GroupBy(e => e.BattlefieldId);
        foreach (var grp in byBf)
        {
            var waiting = grp.OrderBy(e => e.QueuedAt).ToList();
            while (waiting.Count >= groupSize)
            {
                var picked = PickBalancedGroup(waiting, groupSize);
                if (picked == null) break;
                var result = new MatchResult { BattlefieldId = grp.Key };
                foreach (var e in picked)
                {
                    result.Members.Add(e.CharacterId);
                    waiting.Remove(e);
                    _queue.Remove(e);
                }
                formed.Add(result);
                _formed.Add(result);
            }
        }
        return formed;
    }

    /// <summary>
    /// Choix d'un groupe equilibre: 1 tank, 1 heal, le reste en dps.
    /// Fallback: n'importe quels membres si roles manquants.
    /// </summary>
    private List<QueueEntry>? PickBalancedGroup(
        List<QueueEntry> waiting, int size)
    {
        var picked = new List<QueueEntry>();
        var tank = waiting.FirstOrDefault(e =>
            e.Role == "tank" && !picked.Contains(e));
        if (tank != null) picked.Add(tank);
        var heal = waiting.FirstOrDefault(e =>
            e.Role == "heal" && !picked.Contains(e));
        if (heal != null) picked.Add(heal);
        foreach (var e in waiting)
        {
            if (picked.Count >= size) break;
            if (!picked.Contains(e)) picked.Add(e);
        }
        return picked.Count >= size ? picked : null;
    }
}
