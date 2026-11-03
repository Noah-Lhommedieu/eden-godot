using System.Collections.Concurrent;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Monsters;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion des monstres — correspond a CMonsterDBO + CAIFSM*.
/// Charge S_Monster.ini et gere spawn/cible/mort/regeneration.
/// `_instances` est concurrent : `TickRespawns` tourne sur le thread
/// timer pendant que les threads de session spawn/kill/despawn — un
/// Dictionary non concurrent levait InvalidOperationException dans le
/// callback Elapsed (unhandled → arrêt du process).
/// </summary>
public sealed class MonsterService
{
    private readonly Dictionary<int, MonsterParser.MonsterDef> _definitions;
    private readonly ConcurrentDictionary<long, MonsterEntity> _instances = new();
    private long _nextInstanceId;

    public MonsterService(IEnumerable<MonsterParser.MonsterDef> definitions)
    {
        _definitions = definitions.ToDictionary(d => d.Id);
    }

    public static MonsterService LoadFromDataDir(string decodedDir)
        => new(MonsterParser.Parse(Path.Combine(decodedDir, "S_Monster.ini")));

    /// <summary>
    /// Trie les références brutes (S_Monster cols 40-49) par table réelle :
    /// les ids présents dans S_Spell alimentent <c>SpellIds</c> (sorts que
    /// le mob lance en combat), ceux présents dans S_ClassDropItem
    /// alimentent <c>ClassDropIds</c> (drops "breakpoint"). Les deux
    /// plages se chevauchent dans les données natives (59277 existe dans
    /// les deux tables) — un id dans les deux est rangé dans les deux.
    /// </summary>
    public void ResolveRefs(IReadOnlySet<int> spellIds,
        IReadOnlySet<int> classDropIds)
    {
        foreach (var def in _definitions.Values)
            foreach (var id in def.RefIds)
            {
                if (spellIds.Contains(id)) def.SpellIds.Add(id);
                if (classDropIds.Contains(id)) def.ClassDropIds.Add(id);
            }
    }

    public MonsterParser.MonsterDef? GetDefinition(int monsterId)
        => _definitions.TryGetValue(monsterId, out var d) ? d : null;

    /// <summary>Spawn un monstre — correspond a CNC_ZM_MonsterServerSpawn.</summary>
    public MonsterEntity? Spawn(int monsterId, int mapId, float x, float y, float z)
    {
        var def = GetDefinition(monsterId);
        if (def == null || !def.Enabled) return null;

        var entity = new MonsterEntity(
            Interlocked.Increment(ref _nextInstanceId), def, mapId, x, y, z);
        _instances[entity.InstanceId] = entity;
        return entity;
    }

    /// <summary>Tue un monstre — correspond a CNC_ZM_MonsterServerDead.</summary>
    public void Kill(long instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var m))
        {
            m.CurrentHP = 0;
            m.DeadAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Tick de respawn — fait réapparaître les monstres morts depuis
    /// <see cref="MonsterEntity.RespawnSeconds"/> secondes.
    /// Retourne les instances réapparues (pour broadcast spawn).
    /// </summary>
    public IReadOnlyList<MonsterEntity> TickRespawns()
    {
        var now = DateTime.UtcNow;
        var respawned = new List<MonsterEntity>();
        foreach (var m in _instances.Values)
        {
            if (m.IsAlive || m.RespawnSeconds <= 0 || m.DeadAt == null)
                continue;
            if ((now - m.DeadAt.Value).TotalSeconds < m.RespawnSeconds)
                continue;
            m.CurrentHP = m.Definition.HP;
            m.CurrentMP = m.Definition.MP;
            m.DeadAt = null;
            respawned.Add(m);
        }
        return respawned;
    }

    /// <summary>Retire l'instance de la zone.</summary>
    public void Despawn(long instanceId) => _instances.TryRemove(instanceId, out _);

    public MonsterEntity? Get(long instanceId)
        => _instances.TryGetValue(instanceId, out var m) ? m : null;

    public IReadOnlyCollection<MonsterEntity> GetAliveOnMap(int mapId)
        => _instances.Values.Where(m => m.MapId == mapId && m.IsAlive).ToList();

    /// <summary>
    /// Degats appliques au monstre — retourne les spell ids de proc si mort.
    /// Correspond a CMonster::OnBeAttacked.
    /// </summary>
    public IReadOnlyList<int> ApplyDamage(long instanceId, long damage)
    {
        if (!_instances.TryGetValue(instanceId, out var m) || !m.IsAlive)
            return Array.Empty<int>();
        if (m.TakeDamage(damage))
        {
            m.DeadAt = DateTime.UtcNow;
            return m.Definition.SpellIds; // sorts declenches a la mort/drop
        }
        return Array.Empty<int>();
    }
}
