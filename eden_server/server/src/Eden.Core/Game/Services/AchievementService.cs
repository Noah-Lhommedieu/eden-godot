using Eden.Core.Data;
using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Définitions des succès — <c>S_Achievement.ini</c> parsée par
/// <see cref="GameDataParsers.ParseAchievements"/> (layout
/// <c>CAchievementData</c> natif, mission @0x082ae760).
///
/// Le service porte aussi la map membre→succès-groupe
/// (<c>GetReachedAchievementGroupID</c> natif @0x080764a0) : un
/// succès-groupe (AC5xx, liste « ; » en col13) est accordé quand tous
/// ses membres sont complétés — les membres complétés sont lus dans la
/// progression du perso.
///
/// Titres actifs : conservé ici (état runtime par perso, comme avant).
/// La progression persistante vit dans
/// <c>player_characters_achievement</c> via
/// <see cref="Eden.Core.Database.Repositories.AchievementRepository"/>
/// et <c>EntryState.Achievements</c> (boucle zone).
/// </summary>
public sealed class AchievementService
{
    private readonly Dictionary<int, GameDataParsers.AchievementDef>
        _defs = new();
    /// <summary>succès-membre → succès-groupes qui le listent.</summary>
    private readonly Dictionary<int, List<int>> _memberGroups = new();
    private readonly Dictionary<int, PlayerAchievements>
        _progress = new();

    public sealed class PlayerAchievements
    {
        public int CharacterId { get; set; }
        public int? ActiveTitleId { get; set; }
    }

    public AchievementService() { }

    public static AchievementService LoadFromDataDir(string decodedDir)
    {
        var svc = new AchievementService();
        foreach (var def in GameDataParsers.ParseAchievements(
                Path.Combine(decodedDir, "S_Achievement.ini")))
        {
            svc._defs[def.Id] = def;
            foreach (var m in def.Members)
            {
                if (!svc._memberGroups.TryGetValue(m, out var g))
                    svc._memberGroups[m] = g = new List<int>();
                g.Add(def.Id);
            }
        }
        return svc;
    }

    public int DefinitionCount => _defs.Count;

    /// <summary>Définition du succès (null si absent des données).</summary>
    public GameDataParsers.AchievementDef? Get(int achievementId)
        => _defs.GetValueOrDefault(achievementId);

    /// <summary>Succès-groupes dont <paramref name="achievementId"/>
    /// est membre (les complétions en cascade du natif).</summary>
    public IReadOnlyList<int> GroupsOf(int achievementId)
        => _memberGroups.TryGetValue(achievementId, out var g)
            ? g : (IReadOnlyList<int>)Array.Empty<int>();

    /// <summary>Vrai quand tous les membres du succès-groupe ont une
    /// ligne de progression — algorithme natif de
    /// <c>GetReachedAchievementGroupID</c> : compte les membres
    /// PRÉSENTS dans exec+0x528 (une ligne existe dès le premier grant,
    /// même incomplète).</summary>
    public bool GroupReached(int groupId,
        IReadOnlyDictionary<int, (int SuccessNum, int Point)> progress)
    {
        var def = Get(groupId);
        if (def == null || def.Members.Length == 0) return false;
        var present = 0;
        foreach (var m in def.Members)
            if (progress.ContainsKey(m)) present++;
        return present >= def.Members.Length;
    }

    private PlayerAchievements For(int charId)
        => _progress.TryGetValue(charId, out var p)
            ? p : _progress[charId] = new PlayerAchievements
                { CharacterId = charId };

    /// <summary>Active un titre (stats du titre appliquees).</summary>
    public void SetActiveTitle(int charId, int? titleId)
        => For(charId).ActiveTitleId = titleId;

    /// <summary>Titre actif du personnage (null = aucun).</summary>
    public int? GetActiveTitle(int charId)
        => _progress.TryGetValue(charId, out var p) ? p.ActiveTitleId
            : null;
}
