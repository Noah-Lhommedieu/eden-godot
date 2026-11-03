using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de quetes — correspond au MissionServer + CExecutionerData.
///
/// Interprete le langage de script des quetes (S_Mission.ini):
/// - Conditions: If_CharLevel, If_FMS, If_Gender, CheckBuff, PlayerLevel
/// - Objectifs : MonsterKilled, ItemCollect, MonsterTreasureGot, NPCTalk,
///               AreaTravel, AcceptInventoryItem, ItemGiven, ItemGot
/// - Recompenses: RewardGold, RewardExp, RewardItem, RewardBuff,
///                RewardReputation, RewardSpecialCoin, RewardChooseItem,
///                RewardClassPoint, RewardFamilyExp
/// </summary>
public sealed class MissionService
{
    private readonly Dictionary<int, MissionParser.MissionDef> _missions;
    /// <summary>Progression par (characterId, missionId) — multi-quêtes.</summary>
    private readonly Dictionary<(int CharId, int MissionId), QuestState> _progress = new();
    /// <summary>Quêtes terminées par personnage (journal).</summary>
    private readonly Dictionary<int, HashSet<int>> _completed = new();

    public sealed class QuestState
    {
        public int CharacterId { get; set; }
        public int MissionId { get; set; }
        public Dictionary<string, int> Counters { get; } = new(); // kills, items...
        public bool Completed { get; set; }
    }

    /// <summary>Contexte joueur pour evaluer les conditions de quete.</summary>
    public interface IPlayerContext
    {
        int CharacterId { get; }
        int Level { get; }
        int Gender { get; }
        int Fms { get; }
        bool HasBuff(int buffId);
        int ItemCount(int itemId);
    }

    public MissionService(IEnumerable<MissionParser.MissionDef> missions)
    {
        _missions = missions.ToDictionary(m => m.Id);
    }

    public static MissionService LoadFromDataDir(string decodedDir)
        => new(MissionParser.Parse(Path.Combine(decodedDir, "S_Mission.ini")));

    public MissionParser.MissionDef? GetMission(int missionId)
        => _missions.TryGetValue(missionId, out var m) ? m : null;

    /// <summary>Quêtes en cours d'un personnage.</summary>
    public IReadOnlyList<QuestState> ActiveQuests(int characterId)
        => _progress.Values.Where(q => q.CharacterId == characterId).ToList();

    /// <summary>Quête déjà acceptée/terminée ?</summary>
    public bool HasQuest(int characterId, int missionId)
        => _progress.ContainsKey((characterId, missionId))
           || (_completed.TryGetValue(characterId, out var s) && s.Contains(missionId));

    /// <summary>
    /// Verifie si un joueur peut accepter la quete (conditions [13]).
    /// </summary>
    public bool CanAccept(IPlayerContext player, int missionId)
    {
        var m = GetMission(missionId);
        if (m == null || HasQuest(player.CharacterId, missionId)) return false;
        return m.Conditions.All(c => EvaluateCondition(player, c));
    }

    /// <summary>Accepte la quete et initialise les compteurs d'objectifs.</summary>
    public QuestState? Accept(IPlayerContext player, int missionId)
    {
        if (!CanAccept(player, missionId)) return null;
        var state = new QuestState { CharacterId = player.CharacterId, MissionId = missionId };
        _progress[(player.CharacterId, missionId)] = state;
        return state;
    }

    /// <summary>Abandonne une quête — CNE_CZ_ZoneServerGiveUpMission.</summary>
    public bool GiveUp(int characterId, int missionId)
        => _progress.Remove((characterId, missionId));

    /// <summary>
    /// Notifie un evenement de jeu — fait progresser TOUTES les quêtes
    /// actives du joueur dont un objectif correspond.
    /// kind = "MonsterKilled" | "ItemCollect" | "NPCTalk" | "AreaTravel"...
    /// </summary>
    public void NotifyEvent(int characterId, string kind, int targetId, int amount = 1)
    {
        foreach (var state in _progress.Values.Where(q => q.CharacterId == characterId))
        {
            var m = GetMission(state.MissionId);
            if (m == null) continue;

            foreach (var action in m.Actions)
            {
                if (!action.Name.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;
                // targetId doit correspondre à l'arg0 (monstre/item/pnj visé)
                var wanted = action.Args.Count > 0 ? action.Args[0] : null;
                if (wanted != null && wanted != "0" && wanted != targetId.ToString())
                    continue;
                var key = $"{kind}:{wanted ?? targetId.ToString()}";
                state.Counters[key] = state.Counters.GetValueOrDefault(key) + amount;
            }
        }
    }

    /// <summary>Verifie que tous les objectifs [16] sont remplis.</summary>
    public bool IsComplete(int characterId, int missionId)
    {
        if (!_progress.TryGetValue((characterId, missionId), out var state)) return false;
        var m = GetMission(state.MissionId);
        if (m == null) return false;

        if (m.Actions == null || m.Actions.Count == 0) return false;

        foreach (var action in m.Actions)
        {
            var required = action.Args.Count > 1 && int.TryParse(action.Args[1], out var r) ? r : 1;
            var key = $"{action.Name}:{(action.Args.Count > 0 ? action.Args[0] : "")}";
            var count = state.Counters.GetValueOrDefault(key);
            if (count < required && !(required == 1 && count >= 1))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Termine la quete et retourne les recompenses [20] a appliquer.
    /// </summary>
    public IReadOnlyList<MissionParser.ScriptCommand> Complete(int characterId, int missionId)
    {
        if (!IsComplete(characterId, missionId)) return Array.Empty<MissionParser.ScriptCommand>();
        var m = GetMission(missionId)!;
        _progress[(characterId, missionId)].Completed = true;
        _progress.Remove((characterId, missionId));
        (_completed.TryGetValue(characterId, out var s)
            ? s : _completed[characterId] = new HashSet<int>()).Add(missionId);
        return m.Rewards;
    }

    /// <summary>
    /// Interprète une récompense scriptée → (kind, arg1, arg2) :
    /// "RewardGold 100" → ("gold",100,0), "RewardItem 1001 2" → ("item",1001,2),
    /// "RewardExp 500" → ("exp",500,0), "RewardBuff 50001" → ("buff",50001,0).
    /// </summary>
    public static (string Kind, int A1, int A2) DecodeReward(MissionParser.ScriptCommand c)
    {
        var a1 = c.Args.Count > 0 && int.TryParse(c.Args[0], out var v1) ? v1 : 0;
        var a2 = c.Args.Count > 1 && int.TryParse(c.Args[1], out var v2) ? v2 : 0;
        return c.Name switch
        {
            "RewardGold" => ("gold", a1, 0),
            "RewardExp" => ("exp", a1, 0),
            "RewardItem" or "RewardChooseItem" => ("item", a1, a2 == 0 ? 1 : a2),
            "RewardBuff" => ("buff", a1, a2),
            "RewardReputation" => ("reputation", a1, a2),
            "RewardSpecialCoin" => ("coin", a1, a2),
            "RewardClassPoint" => ("classpoint", a1, 0),
            "RewardFamilyExp" => ("familyexp", a1, 0),
            _ => (c.Name, a1, a2),
        };
    }

    private static bool EvaluateCondition(IPlayerContext p, MissionParser.ScriptCommand c)
        => c.Name switch
        {
            // "If_CharLevel >= 199" — args = [operateur, valeur]
            "If_CharLevel" or "PlayerLevel" => CompareNumeric(
                p.Level, c.Args.Count > 0 ? c.Args[0] : ">=",
                c.Args.Count > 1 && int.TryParse(c.Args[1], out var v) ? v : 0),
            "If_Gender" => c.Args.Count == 0
                || c.Args.Any(a => int.TryParse(a, out var g) && p.Gender == g),
            "If_FMS" => CompareNumeric(
                p.Fms, c.Args.Count > 0 ? c.Args[0] : ">=",
                c.Args.Count > 1 && int.TryParse(c.Args[1], out var v) ? v : 0),
            "CheckBuff" or "If_CheckBuff" => c.Args.Count > 0
                && int.TryParse(c.Args[0], out var b) ? p.HasBuff(b) : true,
            _ => true, // commandes inconnues: permissif
        };

    private static bool CompareNumeric(int actual, string op, int expected)
        => op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            "==" or "=" => actual == expected,
            "!=" => actual != expected,
            _ => int.TryParse(op, out var v) ? actual >= v : true, // pas d'operateur: arg0=valeur
        };
}
