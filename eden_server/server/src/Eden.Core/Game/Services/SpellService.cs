using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de sorts et buffs — correspond a CSpellFactory / CSpellMasterFactory.
///
/// Gere le catalogue de sorts (S_Spell.ini) et les buffs actifs sur les
/// entites (joueurs, monstres). Correspond a CCharacter::ClearAllTeamSpell
/// pour le nettoyage des buffs de groupe.
/// </summary>
public sealed class SpellService
{
    private readonly Dictionary<int, SpellParser.SpellDef> _spells;
    private readonly Dictionary<int, SpellReferenceParser.LevelCost>
        _levelCosts;
    private readonly Dictionary<int, List<ActiveBuff>> _buffsByTarget = new();

    public sealed class ActiveBuff
    {
        public int SpellId { get; set; }
        public int CasterId { get; set; }
        public int RemainingTicks { get; set; }
        public int Stacks { get; set; }
        public SpellParser.SpellDef? Definition { get; set; }
    }

    public SpellService(IEnumerable<SpellParser.SpellDef> spells,
        Dictionary<int, SpellReferenceParser.LevelCost>? levelCosts = null)
    {
        _spells = spells.ToDictionary(s => s.Id);
        _levelCosts = levelCosts ?? new();
    }

    public static SpellService LoadFromDataDir(string decodedDir)
    {
        var spells = SpellParser.Parse(
            Path.Combine(decodedDir, "S_Spell.ini"));
        var refPath = Path.Combine(decodedDir, "S_SpellReference.ini");
        var costs = File.Exists(refPath)
            ? SpellReferenceParser.Parse(refPath)
            : new Dictionary<int, SpellReferenceParser.LevelCost>();
        return new SpellService(spells, costs);
    }

    public SpellParser.SpellDef? GetDefinition(int spellId)
        => _spells.TryGetValue(spellId, out var s) ? s : null;

    /// <summary>Taux de coût d'upgrade pour atteindre `newLevel`
    /// (S_SpellReference). `null` si le palier est hors table.</summary>
    public SpellReferenceParser.LevelCost? GetUpgradeCost(int newLevel)
        => _levelCosts.TryGetValue(newLevel, out var c) ? c : null;

    /// <summary>Ligne `S_SpellReference` d'un niveau de sort —
    /// références d'attributs consommées par les commandes d'enchant
    /// mode 1 (`AttrBase`/`AttrDefence`/… : `ref × var × 0.01`,
    /// `CEC_AttrBase::Init` natif — la table est indexée par niveau
    /// exact, cf. lookup `CZoneServer+0x1dc`).</summary>
    public SpellReferenceParser.LevelCost? GetReference(int spellLevel)
        => _levelCosts.TryGetValue(spellLevel, out var c) ? c : null;

    /// <summary>
    /// Applique un sort sur une cible. Si le sort est un buff (categorie 'O'),
    /// il est ajoute aux buffs actifs.
    /// </summary>
    public bool ApplySpell(int spellId, int casterId, int targetId)
    {
        var def = GetDefinition(spellId);
        if (def == null) return false;

        if (def.Category == "O" || def.Duration > 0)
        {
            var list = _buffsByTarget.TryGetValue(targetId, out var l)
                ? l : _buffsByTarget[targetId] = new List<ActiveBuff>();
            var existing = list.Find(b => b.SpellId == spellId);
            if (existing != null)
            {
                existing.Stacks++;
                existing.RemainingTicks = def.Duration;
            }
            else
            {
                list.Add(new ActiveBuff
                {
                    SpellId = spellId,
                    CasterId = casterId,
                    RemainingTicks = def.Duration,
                    Stacks = 1,
                    Definition = def,
                });
            }
        }
        return true;
    }

    /// <summary>Retire un buff precis d'une cible — annulation manuelle (StateCancel).</summary>
    public bool RemoveBuff(int targetId, int spellId)
        => _buffsByTarget.TryGetValue(targetId, out var l) &&
           l.RemoveAll(b => b.SpellId == spellId) > 0;

    /// <summary>Retire tous les buffs de groupe d'une cible — ClearAllTeamSpell.</summary>
    public void ClearAllBuffs(int targetId) => _buffsByTarget.Remove(targetId);

    /// <summary>Buffs actifs sur une cible.</summary>
    public IReadOnlyList<ActiveBuff> GetBuffs(int targetId)
        => _buffsByTarget.TryGetValue(targetId, out var l) ? l : Array.Empty<ActiveBuff>();

    /// <summary>Decremente les buffs d'une cible (tick de jeu).</summary>
    public void Tick(int targetId)
    {
        if (!_buffsByTarget.TryGetValue(targetId, out var list)) return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var b = list[i];
            if (b.RemainingTicks > 0 && --b.RemainingTicks == 0)
                list.RemoveAt(i);
        }
    }
}
