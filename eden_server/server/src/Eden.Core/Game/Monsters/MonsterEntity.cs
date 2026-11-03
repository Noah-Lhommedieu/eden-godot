using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Monsters;

/// <summary>
/// Instance de monstre en jeu — correspond a CMonster.
/// </summary>
public sealed class MonsterEntity
{
    public long InstanceId { get; set; }
    public MonsterParser.MonsterDef Definition { get; }
    public int MapId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public long CurrentHP { get; set; }
    public long CurrentMP { get; set; }
    public bool IsAlive => CurrentHP > 0;
    public int TargetCharacterId { get; set; }
    /// <summary>Orientation du spawn (radians, données scène).</summary>
    public float Facing { get; set; }
    /// <summary>Temps de respawn en secondes (0 = pas de respawn).</summary>
    public int RespawnSeconds { get; set; }
    /// <summary>Route de patrouille ([mob_patrol_mode] de la scène).</summary>
    public int PatrolId { get; set; }
    /// <summary>Timestamp de la mort pour le timer de respawn.</summary>
    public DateTime? DeadAt { get; set; }

    public MonsterEntity(long instanceId, MonsterParser.MonsterDef def,
        int mapId, float x, float y, float z)
    {
        InstanceId = instanceId;
        Definition = def;
        MapId = mapId;
        X = x; Y = y; Z = z;
        CurrentHP = def.HP;
        CurrentMP = def.MP;
    }

    /// <summary>Applique des degats; retourne true si le monstre meurt.</summary>
    public bool TakeDamage(long damage)
    {
        CurrentHP = Math.Max(0, CurrentHP - damage);
        return CurrentHP == 0;
    }
}
