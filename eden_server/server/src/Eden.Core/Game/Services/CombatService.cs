using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Pipeline de combat — correspond a CCharFormula + CCharacter::OnAttack.
/// Ordre des etapes (parametres extraits de S_Parameter.ini):
///   1. Roll hit (base 95% — param 30)
///   2. Roll dodge (AGI/10)
///   3. Roll block (params 21-23)
///   4. Roll crit (AGI/20 + 5% — params 11-12; magic INT/38 + 5% — param 14)
///   5. Damage = atk - def * 0.5 (param 32) ± variance
///   6. Crit multiplie par 150% (param 13) / magic crit 150% (param 15)
///   7. Resistances elementaires (S_Monster.ini)
///   8. Procs (EnchantService) OnAttack/OnBeAttacked/OnCritical
/// </summary>
public sealed class CombatService
{
    private readonly StatsCalculationService _stats;
    private readonly EnchantService _enchants;
    private readonly SpellService _spells;
    private readonly Random _random = new();

    public sealed class AttackResult
    {
        public bool Miss { get; set; }
        public bool Dodged { get; set; }
        public bool Blocked { get; set; }
        public bool Critical { get; set; }
        public int FinalDamage { get; set; }
        public List<int> ProcSpellIds { get; } = new();
    }

    /// <summary>Stats d'un combattant pour le calcul.</summary>
    public sealed class CombatStats
    {
        public int Level { get; set; }
        public int Str { get; set; }
        public int Agi { get; set; }
        public int Int { get; set; }
        public int Con { get; set; }
        public int Wis { get; set; }
        public int AttackPower { get; set; }
        public int MagicPower { get; set; }
        public int Defense { get; set; }
        public int MagicDefense { get; set; }
        public double HitBonus { get; set; }
        public double DodgeBonus { get; set; }
        public double CritBonus { get; set; }
        public double BlockChance { get; set; }
        /// <summary>Resistances elementaires du defenseur (0-9).</summary>
        public float[] Resistances { get; set; } = new float[10];
        /// <summary>Enchants procs de l'attaquant (enchantIds equipes).</summary>
        public List<int> AttackEnchantIds { get; } = new();
        /// <summary>Enchants procs du defenseur (OnBeAttacked).</summary>
        public List<int> DefenseEnchantIds { get; } = new();
    }

    public CombatService(StatsCalculationService stats,
        EnchantService enchants, SpellService spells)
    {
        _stats = stats;
        _enchants = enchants;
        _spells = spells;
    }

    public static CombatService LoadFromDataDir(string decodedDir)
        => new(
            StatsCalculationService.LoadFromDataDir(decodedDir),
            EnchantService.LoadFromDataDir(decodedDir),
            SpellService.LoadFromDataDir(decodedDir));

    /// <summary>Attaque physique standard — pipeline complet.</summary>
    public AttackResult PhysicalAttack(CombatStats attacker,
        CombatStats defender, int casterId, int targetId)
    {
        var r = new AttackResult();

        // 1. Hit vs dodge
        var hitChance = _stats.BaseHitChance() + attacker.HitBonus;
        var dodgeChance = _stats.CalculateDodgeRate(
            defender.Agi, defender.DodgeBonus);
        var roll = _random.NextDouble() * 100;
        if (roll < dodgeChance) { r.Dodged = true; return r; }
        if (roll >= hitChance) { r.Miss = true; return r; }

        // 2. Block — param 23: blocage garde 20% des degats
        if (_random.NextDouble() * 100 < defender.BlockChance)
            r.Blocked = true;

        // 3. Crit — AGI/20 + 5%
        var critChance = _stats.CalculateCriticalRate(
            attacker.Agi, attacker.CritBonus);
        r.Critical = _random.NextDouble() * 100 < critChance;

        // 4. Degats
        var dmg = _stats.ApplyDefenseModifier(
            attacker.AttackPower, defender.Defense);
        dmg *= 0.9 + _random.NextDouble() * 0.2;
        if (r.Blocked) dmg *= 0.2;
        if (r.Critical)
            dmg = _stats.ApplyCritDamage((int)dmg, isMagic: false);
        r.FinalDamage = Math.Max(1, (int)dmg);

        // 5. Procs attaquant (OnAttack, OnCritical si crit)
        foreach (var enchId in attacker.AttackEnchantIds)
        {
            foreach (var proc in _enchants.Evaluate(
                enchId, EnchantService.TriggerEvent.OnAttack))
            {
                r.ProcSpellIds.Add(proc.SpellId);
                _spells.ApplySpell(proc.SpellId, casterId, targetId);
            }
            if (r.Critical)
                foreach (var proc in _enchants.Evaluate(
                    enchId, EnchantService.TriggerEvent.OnCritical))
                {
                    r.ProcSpellIds.Add(proc.SpellId);
                    _spells.ApplySpell(proc.SpellId, casterId, targetId);
                }
        }
        // procs defenseur (OnBeAttacked)
        foreach (var enchId in defender.DefenseEnchantIds)
            foreach (var proc in _enchants.Evaluate(
                enchId, EnchantService.TriggerEvent.OnBeAttacked))
            {
                r.ProcSpellIds.Add(proc.SpellId);
                _spells.ApplySpell(proc.SpellId, targetId, casterId);
            }

        return r;
    }

    /// <summary>Sort offensif — pipeline magique.</summary>
    public AttackResult SpellAttack(CombatStats attacker,
        CombatStats defender, int casterId, int targetId,
        int elementIndex = -1)
    {
        var r = new AttackResult();
        var hitChance = _stats.BaseHitChance() + attacker.HitBonus;
        if (_random.NextDouble() * 100 > hitChance)
        { r.Miss = true; return r; }

        var critChance = attacker.Int / 38.0 + 5;  // param 14
        r.Critical = _random.NextDouble() * 100 < critChance;

        var dmg = _stats.ApplyDefenseModifier(
            attacker.MagicPower, defender.MagicDefense);
        dmg *= 0.9 + _random.NextDouble() * 0.2;

        if (elementIndex >= 0 && elementIndex < defender.Resistances.Length)
            dmg *= 1.0 - defender.Resistances[elementIndex] / 100.0;

        if (r.Critical)
            dmg = _stats.ApplyCritDamage((int)dmg, isMagic: true);
        r.FinalDamage = Math.Max(1, (int)dmg);

        foreach (var enchId in attacker.AttackEnchantIds)
            foreach (var proc in _enchants.Evaluate(
                enchId, EnchantService.TriggerEvent.OnCast))
            {
                r.ProcSpellIds.Add(proc.SpellId);
                _spells.ApplySpell(proc.SpellId, casterId, targetId);
            }

        return r;
    }

    /// <summary>Roll d'esquive pur.</summary>
    public bool RollDodge(CombatStats defender)
        => _random.NextDouble() * 100 <
            _stats.CalculateDodgeRate(defender.Agi, defender.DodgeBonus);
}
