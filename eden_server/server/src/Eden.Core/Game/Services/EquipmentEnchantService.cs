using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Enchantement d'equipement +1..+20.
/// Chances de succes extraites de S_Parameter.ini:
///   armes  +11..+14 = param 517-520 (50%), +15..+20 = param 654-659 (25%)
///   armures +11..+14 = param 521-524 (100%), +15..+20 = param 660-665 (50%)
/// Les niveaux +1..+10 sont garantis (comportement EE standard).
/// </summary>
public sealed class EquipmentEnchantService
{
    private readonly ParameterParser.ParameterSet _params;
    private readonly Random _random = new();

    /// <summary>Ids des parametres de chance par niveau d'enchant.</summary>
    private static readonly int[] WeaponChanceParams =
        { 517, 518, 519, 520, 654, 655, 656, 657, 658, 659 };
    private static readonly int[] ArmorChanceParams =
        { 521, 522, 523, 524, 660, 661, 662, 663, 664, 665 };

    public EquipmentEnchantService(
        ParameterParser.ParameterSet? parameters = null)
        => _params = parameters
            ?? new ParameterParser.ParameterSet(
                new Dictionary<int, float>(),
                new Dictionary<int, string>());

    /// <summary>Chance de succes (0..1) pour passer a targetLevel.</summary>
    public double SuccessChance(int currentLevel, bool isWeapon)
    {
        if (currentLevel < 10) return 1.0;
        var index = currentLevel - 10;
        if (index >= 10) return 0;
        var paramIds = isWeapon ? WeaponChanceParams : ArmorChanceParams;
        return _params.Get(paramIds[index], 0f);
    }

    /// <summary>
    /// Tente un enchant. Retourne le nouveau niveau (+1 si succes,
    /// currentLevel si echec, currentLevel-1 si echec avec downgrade).
    /// </summary>
    public int TryEnchant(int currentLevel, bool isWeapon,
        bool hasProtection = false)
    {
        var chance = SuccessChance(currentLevel, isWeapon);
        if (_random.NextDouble() < chance)
            return currentLevel + 1;
        return hasProtection ? currentLevel : Math.Max(0, currentLevel - 1);
    }
}
