namespace Eden.Zone.Combat;

/// <summary>
/// Moteur de calcul de combat : formules d'attaque, défense, coups critiques et points d'expérience.
/// Reversé depuis lapis::CCombatManager et lapis::CSpell.
/// </summary>
public static class CombatEngine
{
    public record CombatResult(int Damage, bool IsCritical, bool IsDodged, int NewHp);

    public static CombatResult CalculateDamage(
        int attackerAttack,
        int targetDefense,
        int targetCurrentHp,
        double critRate = 0.05,
        double dodgeRate = 0.05,
        Random? rng = null)
    {
        var random = rng ?? Random.Shared;

        // Vérification esquive
        if (random.NextDouble() < dodgeRate)
        {
            return new CombatResult(0, false, true, targetCurrentHp);
        }

        // Formule de dégâts de base : max(1, Attaque - (Défense * 0.5)) + variation aléatoire (95% à 105%)
        var baseDmg = Math.Max(1.0, attackerAttack - (targetDefense * 0.5));
        var variance = 0.95 + (random.NextDouble() * 0.10);
        var finalDmg = baseDmg * variance;

        var isCrit = random.NextDouble() < critRate;
        if (isCrit)
        {
            finalDmg *= 1.5; // Multiplicateur critique x1.5
        }

        var damageInt = (int)Math.Round(finalDmg);
        var newHp = Math.Max(0, targetCurrentHp - damageInt);

        return new CombatResult(damageInt, isCrit, false, newHp);
    }

    public static long CalculateExperienceReward(int monsterLevel, int playerLevel)
    {
        var baseExp = monsterLevel * 100L;
        var levelDiff = monsterLevel - playerLevel;

        if (levelDiff >= 0)
        {
            return baseExp + (levelDiff * 20L);
        }
        else if (levelDiff >= -5)
        {
            return Math.Max(10L, baseExp + (levelDiff * 15L));
        }
        else
        {
            return Math.Max(1L, baseExp / 10);
        }
    }
}
