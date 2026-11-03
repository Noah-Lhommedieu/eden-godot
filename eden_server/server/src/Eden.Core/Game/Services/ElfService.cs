using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service des familiers (elfs) — correspond a CElfContainer + CEC_Elf*.
/// Gere capture, mood, energie, travail (craft via ElfCombine) et skills.
/// </summary>
public sealed class ElfService
{
    private readonly Dictionary<int, ElfParser.ElfDef> _defs;
    private readonly Dictionary<int, ElfInstance> _instances = new();
    private int _nextInstanceId = 1;

    public sealed class ElfInstance
    {
        public int InstanceId { get; set; }
        public int OwnerCharacterId { get; set; }
        public ElfParser.ElfDef Definition { get; set; } = null!;
        public int Mood { get; set; } = 100;      // moral: impacte le craft
        public int Energy { get; set; } = 100;    // energie: consommee au craft
        public int Friendship { get; set; }       // amitie avec le joueur
        public bool IsWorking { get; set; }
        public string Name { get; set; } = string.Empty;   // surnom joueur
    }

    public ElfService(IEnumerable<ElfParser.ElfDef> defs)
        => _defs = defs.ToDictionary(d => d.Id);

    public static ElfService LoadFromDataDir(string decodedDir)
        => new(ElfParser.Parse(Path.Combine(decodedDir, "S_Elf.ini")));

    public ElfParser.ElfDef? GetDefinition(int elfId)
        => _defs.TryGetValue(elfId, out var d) ? d : null;

    /// <summary>Capture/equipe un familier pour un joueur.</summary>
    public ElfInstance? Acquire(int characterId, int elfId)
    {
        var def = GetDefinition(elfId);
        if (def == null) return null;
        var inst = new ElfInstance
        {
            InstanceId = _nextInstanceId++,
            OwnerCharacterId = characterId,
            Definition = def,
            Friendship = def.BaseStats.Length > 0 ? def.BaseStats[0] : 0,
        };
        _instances[inst.InstanceId] = inst;
        return inst;
    }

    /// <summary>
    /// Consomme de l'energie pour un travail (craft ElfCombine).
    /// Correspond a CEC_ElfWork / CEC_ElfEnergy.
    /// </summary>
    public bool ConsumeEnergy(int elfInstanceId, int amount)
    {
        if (!_instances.TryGetValue(elfInstanceId, out var e)) return false;
        if (e.Energy < amount) return false;
        e.Energy -= amount;
        return true;
    }

    /// <summary>Modifie le moral — CEC_ElfMood.</summary>
    public void AdjustMood(int elfInstanceId, int delta)
    {
        if (_instances.TryGetValue(elfInstanceId, out var e))
            e.Mood = Math.Clamp(e.Mood + delta, 0, 200);
    }

    /// <summary>Nourrit le familier — restaure energie/mood.</summary>
    public void Feed(int elfInstanceId, int energyAmount, int moodAmount = 0)
    {
        if (!_instances.TryGetValue(elfInstanceId, out var e)) return;
        e.Energy = Math.Min(200, e.Energy + energyAmount);
        e.Mood = Math.Clamp(e.Mood + moodAmount, 0, 200);
    }

    /// <summary>Modifie l'amitie (Friend Plus X Y dans les scripts).</summary>
    public void AdjustFriendship(int elfInstanceId, int delta)
    {
        if (_instances.TryGetValue(elfInstanceId, out var e))
            e.Friendship = Math.Max(0, e.Friendship + delta);
    }

    public ElfInstance? Get(int elfInstanceId)
        => _instances.TryGetValue(elfInstanceId, out var e) ? e : null;

    public IReadOnlyList<ElfInstance> GetOwnedBy(int characterId)
        => _instances.Values.Where(e => e.OwnerCharacterId == characterId).ToList();

    private readonly Dictionary<int, int> _summonedBy = new();

    /// <summary>Invoque un familier possédé — CNE_CZ_ZoneServerElfCallElf.</summary>
    public bool Summon(int characterId, int elfInstanceId)
    {
        if (!_instances.TryGetValue(elfInstanceId, out var e) ||
            e.OwnerCharacterId != characterId) return false;
        _summonedBy[characterId] = elfInstanceId;
        return true;
    }

    /// <summary>Renvoie le familier invoqué — CNE_CZ_ZoneServerElfReturn.</summary>
    public bool Dismiss(int characterId) => _summonedBy.Remove(characterId);

    /// <summary>Familier actuellement invoqué par le joueur, s'il y en a un.</summary>
    public ElfInstance? GetSummoned(int characterId)
        => _summonedBy.TryGetValue(characterId, out var id) ? Get(id) : null;

    /// <summary>Renomme un familier possédé — CNE_CZ_ZoneServerElfChangeName.</summary>
    public bool Rename(int characterId, int elfInstanceId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 24) return false;
        if (!_instances.TryGetValue(elfInstanceId, out var e) ||
            e.OwnerCharacterId != characterId) return false;
        e.Name = name;
        return true;
    }
}
