using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de calcul des stats et formules de combat.
/// Correspond a CCharFormula.
/// Les parametres viennent de S_Parameter.ini (677 parametres extraits
/// des donnees originales) — les descriptions Big5 documentent chaque
/// parametre. La direction des formules (multiplication vs division)
/// est marquee TODO(phase reverse) la ou le binaire n'a pas encore
/// confirme le sens.
/// </summary>
public sealed class StatsCalculationService
{
    private readonly ParameterParser.ParameterSet _params;

    public StatsCalculationService(ParameterParser.ParameterSet? parameters = null)
        => _params = parameters ?? EmptyParameters();

    private static ParameterParser.ParameterSet EmptyParameters()
        => new(new Dictionary<int, float>(), new Dictionary<int, string>());

    public static StatsCalculationService LoadFromDataDir(string decodedDir)
        => new(ParameterParser.Parse(
            Path.Combine(decodedDir, "S_Parameter.ini")));

    /// <summary>
    /// HP max — param 1 (體質轉換生命上限) = 20.
    /// HP = base(niveau) + CON * param1.
    /// TODO(phase reverse): verifier si param 1 est un diviseur ou
    /// multiplicateur dans CCharFormula.
    /// </summary>
    public int CalculateMaxHP(int level, int con, int baseHp)
        => baseHp + (int)(con * _params.Get(ParameterParser.ConToHpMax, 20f));

    /// <summary>MP max — param 3 (意志轉換能量上限) = 15.</summary>
    public int CalculateMaxMP(int level, int wis, int baseMp)
        => baseMp + (int)(wis * _params.Get(ParameterParser.WisToMpMax, 15f));

    /// <summary>Attaque physique — param 5 (力量轉換攻擊) = 10.</summary>
    public int CalculatePhysicalAttack(int str, int weaponDamage)
        => weaponDamage + (int)(str * _params.Get(ParameterParser.StrToAttack, 10f));

    /// <summary>Attaque magique — param 7 (智慧轉換魔攻) = 5.</summary>
    public int CalculateMagicAttack(int intStat, int weaponDamage)
        => weaponDamage + (int)(intStat * _params.Get(ParameterParser.IntToMagicAttack, 5f));

    /// <summary>Defense physique — param 8 (體質轉換防禦) = 2.</summary>
    public int CalculatePhysicalDefense(int con, int armorDefense)
        => armorDefense + (int)(con * _params.Get(ParameterParser.ConToDefense, 2f));

    /// <summary>Defense magique — param 9 (意志轉換魔防) = 2.</summary>
    public int CalculateMagicDefense(int wis, int armorDefense)
        => armorDefense + (int)(wis * _params.Get(ParameterParser.WisToMagicDefense, 2f));

    /// <summary>
    /// Chance de critique — param 11 (敏捷轉換爆擊) = 20,
    /// param 12 (基礎爆擊機率) = 5.
    /// Crit% = agi / param11 + param12(%).
    /// </summary>
    public double CalculateCriticalRate(int agi, double critBonus)
        => agi / _params.Get(ParameterParser.AgiToCrit, 20f)
            + _params.Get(ParameterParser.BaseCritChance, 5f)
            + critBonus;

    /// <summary>Esquive — param 10 (敏捷轉換閃避) = 10.</summary>
    public double CalculateDodgeRate(int agi, double dodgeBonus)
        => agi / _params.Get(ParameterParser.AgiToDodge, 10f) + dodgeBonus;

    /// <summary>
    /// Degats critiques — param 13 (基礎爆擊傷害) = 150%.
    /// </summary>
    public int ApplyCritDamage(int damage, bool isMagic)
    {
        var param = isMagic
            ? ParameterParser.BaseMagicCritDamage
            : ParameterParser.BaseCritDamage;
        return (int)(damage * _params.Get(param, 150f) / 100f);
    }

    /// <summary>Chance de toucher — param 30 (基礎命中機率) = 95%.</summary>
    public double BaseHitChance()
        => _params.Get(ParameterParser.BaseHitChance, 95f);

    /// <summary>
    /// Modificateur de defense — param 32 (守方防禦修正) = 0.5.
    /// </summary>
    public double ApplyDefenseModifier(int damage, int defense)
        => damage - defense * _params.Get(ParameterParser.DefenseModifier, 0.5f);

    /// <summary>
    /// Vitesse d'attaque a mains nues — param 17 = 1.5s/coup.
    /// </summary>
    public double UnarmedAttackInterval()
        => _params.Get(ParameterParser.UnarmedAttackSpeed, 1.5f);

    /// <summary>
    /// Vitesse de deplacement — param 18 = 17.5 cases/s.
    /// </summary>
    public double WalkSpeedPerSecond()
        => _params.Get(ParameterParser.WalkSpeedPerSecond, 17.5f);

    /// <summary>
    /// Bonus d'enchant — params 55/56: +3% defense/attaque par niveau +1.
    /// </summary>
    public double EnchantBonus(int enchantLevel, bool isWeapon)
    {
        var param = isWeapon
            ? ParameterParser.WeaponEnchantBonus
            : ParameterParser.ArmorEnchantBonus;
        return enchantLevel * _params.Get(param, 3f) / 100.0;
    }
}
