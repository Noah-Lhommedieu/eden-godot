using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Progression XP/niveau — correspond au gain d'expérience de
/// <c>CCharacter::AddExp</c> et à la montée de niveau via <c>S_Level.ini</c>.
/// </summary>
public sealed class ProgressionService
{
    private readonly ICharacterRepository _characters;
    private readonly List<ProgressionParsers.LevelRow> _levels;

    public ProgressionService(ICharacterRepository characters,
        IEnumerable<ProgressionParsers.LevelRow> levels)
    {
        _characters = characters;
        _levels = levels.OrderBy(l => l.Level).ToList();
    }

    /// <summary>
    /// XP requise pour passer de <paramref name="level"/> au niveau suivant —
    /// <c>S_Level.ini</c> stocke l'XP nécessaire pour ATTEINDRE un niveau,
    /// donc le seuil = ExpNeeded du niveau level+1.
    /// </summary>
    public long ExpForLevel(int level) =>
        _levels.FirstOrDefault(l => l.Level == level + 1)?.ExpNeeded
        ?? long.MaxValue;

    /// <summary>Palier max atteint ?</summary>
    public int MaxLevel => _levels.Count > 0 ? _levels[^1].Level : 1;

    /// <summary>Stats du niveau (HP/MP/stats de base).</summary>
    public ProgressionParsers.LevelRow? StatsForLevel(int level) =>
        _levels.FirstOrDefault(l => l.Level == level);

    /// <summary>
    /// Accorde de l'XP au personnage — gère la/les montée(s) de niveau.
    /// Retourne le nouveau niveau (inchangé si pas de level-up).
    /// </summary>
    public async Task<(int NewLevel, int LevelsGained)> GrantExpAsync(
        int characterId, long exp, int currentLevel, long currentExp)
    {
        if (exp <= 0 || currentLevel >= MaxLevel)
            return (currentLevel, 0);

        var newExp = currentExp + exp;
        var newLevel = currentLevel;
        var gained = 0;

        while (newLevel < MaxLevel && newExp >= ExpForLevel(newLevel))
        {
            newExp -= ExpForLevel(newLevel);
            newLevel++;
            gained++;
        }
        if (newLevel >= MaxLevel) newExp = 0;

        await _characters.UpdateExpAsync(characterId, newExp, newLevel);
        return (newLevel, gained);
    }
}
