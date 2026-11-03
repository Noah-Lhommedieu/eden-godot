namespace Eden.Core.Game.Classes;

/// <summary>Règles pures sur les ids de classe (KB-04). Ids locaux 1-61
/// (le préfixe racial ×100 est ignoré) : 1-20 et 59 = classes de base ;
/// 21-58, 60, 61 = classes éveillées (deux par classe de base).</summary>
public static class ClassRules
{
    /// <summary>`CCharacter::AddClass` @0x080fd070 : une classe éveillée
    /// est créée niveau 80 / aa_point 80 (pas niveau 1).</summary>
    public const int AwakenedStartLevel = 80;

    public static bool IsAwakened(int classId)
    {
        var local = classId % 100;
        return local is >= 21 and <= 58 or 60 or 61;
    }

    /// <summary>Classe de base d'une éveillée : 21/22 → 1 … 57/58 → 19,
    /// 60/61 → 59 (Juge) ; une classe de base se renvoie elle-même.</summary>
    public static int BaseOf(int classId)
    {
        var local = classId % 100;
        var race = classId - local;
        if (local is >= 21 and <= 58) return race + (local - 19) / 2;
        if (local is 60 or 61) return race + 59;
        return classId;
    }
}

/// <summary>`CCharacter::AddClassFromLimit` @0x080fd440 (KB-03) : classes
/// qui se débloquent d'elles-mêmes (niveau perso + classes prérequises),
/// appelé à PostInit/GainClassPoint/GainExp. Les classes de quête
/// (prérequis niveau perso 0) ne passent jamais ici.</summary>
public static class ClassUnlockRules
{
    /// <param name="classes">table S_Class (305 lignes).</param>
    /// <param name="owned">(classId local ou racial, niveau de classe)
    /// possédées.</param>
    public static List<int> Unlockable(
        IEnumerable<Eden.Core.Data.Parsers.ClassStatsParser.ClassDefinition> classes,
        int raceId, int charLevel,
        IReadOnlyCollection<(int ClassId, int Level)> owned)
    {
        var res = new List<int>();
        var lv = new Dictionary<int, int>();
        foreach (var (id, l) in owned) lv[id % 100] = l;
        var list = classes as IList<Eden.Core.Data.Parsers.ClassStatsParser.ClassDefinition>
            ?? classes.ToList();
        for (var cls = 1; cls <= 61; cls++)
        {
            if (lv.ContainsKey(cls) || ClassRules.IsAwakened(cls)) continue;
            var def = ClassSkillTable.ClassFor(list, raceId, cls);
            if (def == null || def.RequiredCharLevel <= 0 ||
                charLevel < def.RequiredCharLevel) continue;
            var ok = true;
            for (var bit = 0; bit < 63 && ok; bit++)
                if ((def.RequiredClassMask & (1UL << bit)) != 0 &&
                    (!lv.TryGetValue(bit + 1, out var l) ||
                     l < def.RequiredClassLevel))
                    ok = false;
            if (ok) res.Add(cls);
        }
        return res;
    }
}
