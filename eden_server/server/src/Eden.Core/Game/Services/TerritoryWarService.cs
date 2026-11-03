namespace Eden.Core.Game.Services;

/// <summary>
/// Guerre de territoire — correspond a CTerritory / CFamilyBattle.
/// D'apres S_Territory.ini + S_Parameter.ini:
///   params 483-487 = points par rang cristal (A=200 B=180 C=160
///   D=140 E=120), param 488 = points par kill (1), param 489 =
///   item reward (49998), params 490-495 = quantites par rang
///   (1er=3, 2e=2, 3e-6e=1).
/// Deux camps rouge/bleu se disputent 4 cristaux.
/// </summary>
public sealed partial class TerritoryWarService
{
    public enum Side { Red, Blue }

    public sealed class TerritoryBattle
    {
        public int TerritoryId { get; set; }
        public Dictionary<Side, List<int>> Guilds { get; } = new();
        public Dictionary<Side, int> Scores { get; } = new();
        public Dictionary<Side, int> Crystals { get; } = new();
        public bool Active { get; set; }
        public DateTime StartedAt { get; set; }
    }

    private readonly Dictionary<int, TerritoryBattle> _battles = new();

    /// <summary>Demarre une bataille de territoire.</summary>
    public TerritoryBattle StartBattle(int territoryId)
    {
        var b = new TerritoryBattle
        {
            TerritoryId = territoryId,
            Active = true,
            StartedAt = DateTime.UtcNow,
        };
        b.Guilds[Side.Red] = new List<int>();
        b.Guilds[Side.Blue] = new List<int>();
        b.Scores[Side.Red] = 0;
        b.Scores[Side.Blue] = 0;
        b.Crystals[Side.Red] = 0;
        b.Crystals[Side.Blue] = 0;
        _battles[territoryId] = b;
        return b;
    }

    /// <summary>Enregistre une guilde dans un camp.</summary>
    public bool JoinGuild(int territoryId, int guildId, Side side)
    {
        if (!_battles.TryGetValue(territoryId, out var b) || !b.Active)
            return false;
        b.Guilds[side].Add(guildId);
        return true;
    }

    /// <summary>Kill d'un joueur adverse: +1 point (param 488).</summary>
    public void OnKill(int territoryId, Side killerSide)
    {
        if (_battles.TryGetValue(territoryId, out var b) && b.Active)
            b.Scores[killerSide] += 1;
    }

    /// <summary>
    /// Capture d'un cristal — ajoute les points du rang
    /// (params 483-487: A=200 B=180 C=160 D=140 E=120).
    /// </summary>
    public void OnCrystalCapture(int territoryId, Side side,
        int crystalRank = 0)
    {
        if (!_battles.TryGetValue(territoryId, out var b) || !b.Active)
            return;
        var points = crystalRank switch
        {
            0 => 200, 1 => 180, 2 => 160, 3 => 140, _ => 120,
        };
        b.Scores[side] += points;
        b.Crystals[side]++;
    }

    /// <summary>Termine la bataille — retourne le camp gagnant.</summary>
    public Side? EndBattle(int territoryId)
    {
        if (!_battles.TryGetValue(territoryId, out var b)) return null;
        b.Active = false;
        if (b.Scores[Side.Red] == b.Scores[Side.Blue])
            return b.Crystals[Side.Red] > b.Crystals[Side.Blue]
                ? Side.Red : Side.Blue;
        return b.Scores[Side.Red] > b.Scores[Side.Blue]
            ? Side.Red : Side.Blue;
    }

    /// <summary>
    /// Recompenses par rang de guilde dans le camp gagnant
    /// (params 490-495: rang 1=3 items, 2=2, 3-6=1).
    /// </summary>
    public int RewardQuantityForRank(int rank)
        => rank switch
        {
            1 => 3, 2 => 2, <= 6 => 1, _ => 0,
        };

    public TerritoryBattle? GetBattle(int territoryId)
        => _battles.TryGetValue(territoryId, out var b) ? b : null;
}
