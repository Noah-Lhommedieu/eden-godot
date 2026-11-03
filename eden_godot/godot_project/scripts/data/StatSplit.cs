namespace EdenClient.Data;

/// <summary>
/// A5.3 : hover d'une stat de base = split natif « X + Y » — X =
/// naturel (classe/niveau/race = NCID 492 bases), Y = gear/buffs/
/// certifs/talents (total NCID 296 moins la base).
/// </summary>
public static class StatSplit
{
    /// <summary>(naturel, bonus) ; null si la base est inconnue. Le
    /// bonus peut être négatif (malus).</summary>
    public static (int Natural, int Bonus)? Split(int total, int? baseValue) =>
        baseValue is { } b ? (b, total - b) : null;

    /// <summary>« 123 = 100 + 23 » (modèle déjà traduit par l'appelant :
    /// <c>{0}</c> total, <c>{1}</c> naturel, <c>{2}</c> bonus).</summary>
    public static string Format(string template, int total, int? baseValue)
    {
        if (Split(total, baseValue) is not { } s)
            return total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            template, total, s.Natural, s.Bonus);
    }
}
