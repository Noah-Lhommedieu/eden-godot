using Eden.Core.Data.Parsers;
using Eden.Core.Game.Maps;

namespace Eden.Zone;

/// <summary>
/// Type de map de chaque node, calculé UNE fois (puis mis en cache) à partir
/// des définitions de nodes et des scènes. Unique source pour les règles qui
/// dépendent du type de map (perte d'EXP à la mort, entrées, modificateurs).
/// </summary>
public sealed class MapKindResolver
{
    private readonly IReadOnlyDictionary<int, GameDataParsers.NodeDef> _nodes;
    private readonly Func<int, SceneParser.Scene?> _scene;
    private readonly Dictionary<int, MapKind> _cache = new();
    private readonly object _lock = new();

    public MapKindResolver(IEnumerable<GameDataParsers.NodeDef> nodes,
        Func<int, SceneParser.Scene?> sceneOfNode)
    {
        _nodes = nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        _scene = sceneOfNode;
    }

    public MapKind KindOf(int nodeId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(nodeId, out var k)) return k;
            k = Compute(nodeId);
            _cache[nodeId] = k;
            return k;
        }
    }

    private MapKind Compute(int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var n)) return MapKind.Unknown;
        var respawn = _scene(nodeId)?.Monsters
            .Select(m => m.RespawnSeconds).ToArray() ?? Array.Empty<int>();
        return MapClassifier.Classify(new MapFacts(nodeId, n.Type, n.Name, respawn));
    }
}
