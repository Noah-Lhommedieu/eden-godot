using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>Donjons à deux intensités. Le natif porte chaque donjon en
/// deux nodes : la version « 多人強度 » (multijoueur, ex. S101) et la
/// version « 單人強度 » (faisable seul, ex. S701), repérées par le suffixe
/// du nom dans c_node. Les monstres (stats, exp) sont propres à chaque
/// node dans les scènes : l'écart de difficulté est natif.
/// Les « 毀滅幻境 » de type 18 (Illusion Destructrice) ont leur propre
/// système (Evaluate) et restent hors catalogue.</summary>
public sealed class DungeonCatalog
{
    public const string SoloMarker = "單人強度";
    public const string MultiMarker = "多人強度";

    public sealed record Dungeon(int Key, string Name, int MultiNode,
        int SoloNode);

    private readonly Dictionary<int, Dungeon> _byNode = new();

    public IEnumerable<Dungeon> All =>
        _byNode.Values.DistinctBy(d => d.Key);

    public DungeonCatalog(IEnumerable<GameDataParsers.NodeDef> nodes)
    {
        var multi = new Dictionary<string, int>();
        var solo = new Dictionary<string, int>();
        foreach (var n in nodes)
        {
            if (n.Type != 5) continue;     // 18 = Illusion Destructrice
            if (Strip(n.Name, MultiMarker) is { } m) multi[m] = n.Id;
            else if (Strip(n.Name, SoloMarker) is { } s) solo[s] = n.Id;
        }
        foreach (var (name, mid) in multi)
        {
            if (!solo.TryGetValue(name, out var sid)) continue;
            var d = new Dungeon(mid, name, mid, sid);
            _byNode[mid] = d;
            _byNode[sid] = d;
        }
    }

    /// <summary>Nom sans le suffixe d'intensité, ou null si absent.</summary>
    private static string? Strip(string name, string marker)
    {
        var i = name.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        return name[..i].TrimEnd('【', '[', '〈', '<', ' ');
    }

    public Dungeon? ByNode(int nodeId) =>
        _byNode.TryGetValue(nodeId, out var d) ? d : null;

    public bool IsMulti(int nodeId) =>
        _byNode.TryGetValue(nodeId, out var d) && d.MultiNode == nodeId;
}

/// <summary>Règles pures des entrées journalières de donjon.</summary>
public static class DungeonEntryRules
{
    /// <summary>Entrées par jour et par intensité (décision propriétaire ;
    /// le wiki FR parle de 2 en solo et 10 en multi — non repris).</summary>
    public const int MaxDailyEntries = 10;

    /// <summary>Enchant dev permanent (caché, non persisté, posé à
    /// chaque entrée de zone) : pas de décompte d'entrées de donjon.
    /// Provisoire — retiré plus tard.</summary>
    public const int DevPassBuffId = 65002;

    /// <summary>Multiplicateur exp/or du mode multijoueur — valeur de
    /// réglage demandée par le propriétaire, aucune source native
    /// (exp et or de base sont identiques entre S101 et S701).</summary>
    public const double MultiRewardMultiplier = 1.5;

    /// <summary>Jour de décompte en heure locale du serveur : même
    /// convention que ResetDailyMission natif (changement de jour du
    /// calendrier local, cf. QuestEngine.DailyCount).</summary>
    public static int DayIndex(DateTime local) =>
        (int)(local.Date - DateTime.UnixEpoch.Date).TotalDays;

    /// <summary>Entrées restantes (0..max) pour un compteur daté.</summary>
    public static int Remaining(int usedDay, int used, int today) =>
        usedDay == today ? Math.Max(0, MaxDailyEntries - used)
                         : MaxDailyEntries;
}
