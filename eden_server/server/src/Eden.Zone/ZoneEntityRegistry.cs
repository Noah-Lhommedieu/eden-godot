using System.Collections.Concurrent;
using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>
/// Registre des entités combattantes de la zone (joueurs + monstres).
/// Équivalent minimal du <c>CLife</c>/<c>CLifeFactory</c> lapis —
/// associe un id réseau à des stats de combat et à l'HP courant.
/// </summary>
public sealed class ZoneEntityRegistry
{
    public sealed class ZoneEntity
    {
        public required int EntityId { get; init; }
        public required CombatService.CombatStats Stats { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public bool IsDead => CurrentHp <= 0;
        public bool IsPlayer { get; init; }
        public int MonsterId { get; init; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Dir { get; set; }
    }

    private readonly ConcurrentDictionary<int, ZoneEntity> _entities = new();
    private int _nextMonsterInstanceId = 1_000_000;

    /// <summary>Entités vivantes dans un rayon autour d'un point.</summary>
    public IEnumerable<ZoneEntity> FindInRange(float x, float y, float radius)
    {
        var r2 = radius * radius;
        foreach (var e in _entities.Values)
            if (!e.IsDead &&
                (e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y) <= r2)
                yield return e;
    }

    public ZoneEntity? Find(int entityId) =>
        _entities.TryGetValue(entityId, out var e) ? e : null;

    public void Add(ZoneEntity entity) => _entities[entity.EntityId] = entity;

    public bool Remove(int entityId) => _entities.TryRemove(entityId, out _);

    /// <summary>
    /// Spawn d'une instance de monstre depuis la définition parsée de
    /// <c>S_Monster.ini</c> (<see cref="Eden.Core.Data.Parsers.MonsterParser.MonsterDef"/>).
    /// </summary>
    public ZoneEntity SpawnMonster(Eden.Core.Data.Parsers.MonsterParser.MonsterDef def)
    {
        var entity = new ZoneEntity
        {
            EntityId = Interlocked.Increment(ref _nextMonsterInstanceId),
            MonsterId = def.Id,
            IsPlayer = false,
            MaxHp = (int)Math.Min(int.MaxValue, def.HP),
            CurrentHp = (int)Math.Min(int.MaxValue, def.HP),
            Stats = new CombatService.CombatStats
            {
                Level = def.Level,
                AttackPower = def.AttackPower,
                MagicPower = def.CombatStats.Length > 1 ? def.CombatStats[1] : 0,
                Defense = def.CombatStats.Length > 2 ? def.CombatStats[2] : 0,
                MagicDefense = def.CombatStats.Length > 3 ? def.CombatStats[3] : 0,
                Resistances = def.Resistances.Select(r => (float)r).ToArray(),
            }
        };
        _entities[entity.EntityId] = entity;
        return entity;
    }

    /// <summary>
    /// Enregistre un joueur comme entité vivante de la zone — le charId sert
    /// d'EntityId (les instances de monstres commencent à 1_000_000).
    /// </summary>
    public ZoneEntity RegisterPlayer(int charId, CombatService.CombatStats stats,
        int maxHp, float x = 0, float y = 0, float z = 0)
    {
        var entity = new ZoneEntity
        {
            EntityId = charId,
            IsPlayer = true,
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Stats = stats,
            X = x, Y = y, Z = z,
        };
        _entities[charId] = entity;
        return entity;
    }

    /// <summary>
    /// Applique des dégâts à une entité — retourne les HP restants.
    /// </summary>
    public int ApplyDamage(int entityId, int damage)
    {
        if (!_entities.TryGetValue(entityId, out var e)) return 0;
        e.CurrentHp = Math.Max(0, e.CurrentHp - damage);
        return e.CurrentHp;
    }

    public IEnumerable<ZoneEntity> All => _entities.Values;
}
