using Eden.Core.Data.Parsers;
using Eden.Core.Game.Monsters;

namespace Eden.Core.Game.Services;

/// <summary>
/// Gère les scènes de maps (<c>Data/scene/Sxxx.ini</c>) — instancie les
/// monstres, PNJ et zones d'événement dans la zone.
/// </summary>
public sealed class SceneService
{
    private readonly MonsterService _monsters;
    private readonly Dictionary<string, SceneParser.Scene> _scenes = new();
    private readonly Dictionary<string, List<MonsterEntity>> _liveMonsters = new();

    public SceneService(MonsterService monsters)
    {
        _monsters = monsters;
    }

    /// <summary>Charge toutes les scènes d'un répertoire.</summary>
    public void LoadScenes(string sceneDir)
    {
        foreach (var file in Directory.GetFiles(sceneDir, "*.ini"))
        {
            var scene = SceneParser.Parse(file);
            _scenes[scene.MapCode] = scene;
        }
    }

    private readonly Dictionary<string, NodeMapGrid> _nodeMaps =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Charge les murs invisibles natifs (<c>Sxxx.smp</c>) d'un
    /// répertoire <c>Data/NodeMap</c>. Retourne le nombre de cartes.</summary>
    public int LoadNodeMaps(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        foreach (var f in Directory.GetFiles(dir, "*.smp"))
        {
            var g = NodeMapGrid.Load(f);
            if (g != null)
                _nodeMaps[Path.GetFileNameWithoutExtension(f)] = g;
        }
        return _nodeMaps.Count;
    }

    /// <summary>Case (x, y Eden) bloquée par un mur invisible de la map.
    /// Faux si la map n'a pas de carte.</summary>
    public bool IsNodeBlocked(string mapCode, float x, float y) =>
        _nodeMaps.TryGetValue(mapCode, out var g) && g.IsBlocked(x, y);

    /// <summary>Récupère une scène par code de map (ex: "S001").</summary>
    public SceneParser.Scene? GetScene(string mapCode) =>
        _scenes.TryGetValue(mapCode, out var s) ? s : null;

    /// <summary>
    /// Spawn tous les monstres d'une map — instancie les
    /// <see cref="MonsterEntity"/> depuis leurs templates et positions.
    /// </summary>
    public IReadOnlyList<MonsterEntity> SpawnMapMonsters(string mapCode, int nodeId)
    {
        if (!_scenes.TryGetValue(mapCode, out var scene))
            return Array.Empty<MonsterEntity>();

        var spawned = new List<MonsterEntity>();
        foreach (var spawn in scene.Monsters)
        {
            // Filtres natifs du loader (GetAllRebornMonsterInfo
            // @0x083688a0) : ligne dormante (flag « né au boot » = 0)
            // ou masque Locate sans le bit du serveur → pas de spawn.
            if (!spawn.InitiallyActive) continue;
            var sdef = _monsters.GetDefinition(spawn.MonsterId);
            if (sdef != null && !sdef.VisibleAtLocate(
                    EdenConfig.ZoneLocate))
                continue;
            var entity = _monsters.Spawn(
                spawn.MonsterId, nodeId, spawn.X, spawn.Y, spawn.Z);
            if (entity == null) continue;
            entity.Facing = spawn.Facing;
            entity.RespawnSeconds = spawn.RespawnSeconds;
            entity.PatrolId = spawn.PatrolId;
            spawned.Add(entity);
        }
        _liveMonsters[mapCode] = spawned;
        return spawned;
    }

    /// <summary>Monstres vivants d'une map.</summary>
    public IReadOnlyList<MonsterEntity> LiveMonsters(string mapCode) =>
        _liveMonsters.TryGetValue(mapCode, out var l)
            ? l : Array.Empty<MonsterEntity>();

    /// <summary>Nombre de scènes chargées.</summary>
    public int SceneCount => _scenes.Count;

    /// <summary>Codes de map chargés (ex: "S001").</summary>
    public IEnumerable<string> SceneCodes => _scenes.Keys;

    /// <summary>Toutes les lignes <c>[gateway_area]</c> dont la clé
    /// wire <paramref name="keyId"/> (col4) matche — recherche globale
    /// utilisée par CheckNodeExit quand le suivi de node serveur est
    /// désynchronisé de la carte réellement affichée côté client (le
    /// client est autoritaire : il a physiquement cliqué le portail).
    /// Les keyIds ne sont PAS globalement uniques — l'appelant doit
    /// lever l'ambiguïté par la position du joueur (TestIn).</summary>
    public IEnumerable<(SceneParser.Scene Scene, SceneParser.Gateway Gateway)>
        FindGatewaysByKeyId(int keyId)
    {
        foreach (var scene in _scenes.Values)
            foreach (var g in scene.Gateways)
                if (g.KeyId == keyId)
                    yield return (scene, g);
    }

    /// <summary>Toutes les lignes <c>[gateway_area]</c> dont la clé
    /// <paramref name="eventAreaId"/> matche — recherche globale utilisée
    /// par CheckNodeExit quand le suivi de node serveur est désynchronisé
    /// de la carte réellement affichée côté client (le client est
    /// autoritaire : il a physiquement cliqué le portail). Les ids
    /// d'event_area ne sont PAS globalement uniques — l'appelant doit
    /// traiter le cas multi-scènes (préférer la scène courante).</summary>
    public IEnumerable<(SceneParser.Scene Scene, SceneParser.Gateway Gateway)>
        FindGatewaysByEventArea(int eventAreaId)
    {
        foreach (var scene in _scenes.Values)
            foreach (var g in scene.Gateways)
                if (g.EventAreaId == eventAreaId)
                    yield return (scene, g);
    }
}
