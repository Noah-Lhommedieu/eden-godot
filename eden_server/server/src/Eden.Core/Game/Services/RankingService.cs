using System.Text;
using Eden.Core.Data;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Classements (ranking) + recompenses — correspond a CRankingMgr.
/// S_RankAward.ini (|V.1|52|): par categorie, items recompense par rang.
/// Categories: prestige, classe, kills de monstres, villes, arene.
/// </summary>
public sealed class RankingService
{
    /// <summary>Categories de classement (d'apres S_RankAward.ini col 1).</summary>
    public enum RankCategory
    {
        Prestige = 1,
        Class = 3,
        MonsterKills = 4,
        Town = 5,
        ArenaWeekly = 8,
    }

    private readonly Dictionary<int, SortedDictionary<int, int>> _scores = new();
    private readonly List<RankAwardRow> _awards = new();

    public sealed class RankAwardRow
    {
        public int Id { get; set; }
        public int Category { get; set; }
        public List<(int ItemId, string Desc, int Count)> Rewards { get; } = new();
    }

    public RankingService() { }

    public static RankingService LoadFromDataDir(string decodedDir)
    {
        var svc = new RankingService();
        var table = IniTable.ParseFile(
            Path.Combine(decodedDir, "S_RankAward.ini"),
            EdenEncoding.Big5);
        foreach (var row in table.Rows)
        {
            if (row.Length < 15 || !int.TryParse(row[0], out var id)) continue;
            var r = new RankAwardRow
            {
                Id = id, Category = IniTable.GetInt(row, 1),
            };
            // triplets [itemId|desc|count] a partir de col 11
            for (var i = 11; i + 2 < row.Length; i += 4)
            {
                var itemId = IniTable.GetInt(row, i);
                if (itemId <= 0) continue;
                r.Rewards.Add((itemId, IniTable.GetStr(row, i + 1),
                    IniTable.GetInt(row, i + 2)));
            }
            svc._awards.Add(r);
        }
        return svc;
    }

    /// <summary>Ajoute des points a un joueur dans une categorie.</summary>
    public void AddScore(int categoryId, int charId, int points)
    {
        var board = _scores.TryGetValue(categoryId, out var b)
            ? b : _scores[categoryId] = new SortedDictionary<int, int>();
        board[charId] = board.GetValueOrDefault(charId) + points;
    }

    /// <summary>Top N d'une categorie (score descendant).</summary>
    public IReadOnlyList<(int CharId, int Score)> GetTop(
        int categoryId, int count = 10)
        => !_scores.TryGetValue(categoryId, out var board)
            ? Array.Empty<(int, int)>()
            : board.OrderByDescending(kv => kv.Value)
                   .Take(count)
                   .Select(kv => (kv.Key, kv.Value))
                   .ToList();

    /// <summary>Rang d'un joueur dans une categorie (1-based, 0 si absent).</summary>
    public int GetRank(int categoryId, int charId)
    {
        if (!_scores.TryGetValue(categoryId, out var board)) return 0;
        var score = board.GetValueOrDefault(charId);
        var rank = board.Count(kv => kv.Value > score) + 1;
        return score > 0 ? rank : 0;
    }

    /// <summary>Recompenses pour une categorie et un rang donne.</summary>
    public IReadOnlyList<(int ItemId, string Desc, int Count)> GetRewards(
        int categoryId, int rank)
        => _awards.FirstOrDefault(a => a.Category == categoryId)?.Rewards
            ?? (IReadOnlyList<(int, string, int)>)Array.Empty<(int, string, int)>();

    public int AwardRowCount => _awards.Count;

    /// <summary>Reset hebdo/mensuel.</summary>
    public void Reset(int categoryId) => _scores.Remove(categoryId);
}
