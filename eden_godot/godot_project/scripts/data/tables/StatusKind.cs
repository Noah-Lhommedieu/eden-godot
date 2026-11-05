namespace EdenClient.Data.Tables;

/// <summary>Catégorie d'affichage d'un status (définitions propriétaire,
/// <c>docs/knowledge/definitions.md</c>).</summary>
public enum StatusKind
{
    /// <summary>Pas un status : effet instantané (soin, dissipation,
    /// téléport…) ou enchant sans commande — jamais affiché.</summary>
    None,
    /// <summary>Augmente des statistiques.</summary>
    Buff,
    /// <summary>Réduit des statistiques.</summary>
    Nerf,
    /// <summary>Bonus/malus hors statistiques (monture, portail gratuit,
    /// quête, instance, évènement, DoT/HoT…).</summary>
    State,
}

/// <summary>
/// Classification pure (sans Godot) d'un enchant en <see cref="StatusKind"/>
/// à partir de ses commandes. Ce fichier est compilé par le client ET lié
/// dans les tests serveur (<c>Eden.Tests</c>) : le serveur ne pose aucun
/// status pour <see cref="StatusKind.None"/> (<c>InstantOnlyCommands</c>,
/// ZoneEntryFlow.Status.cs) et un test vérifie l'identité des deux listes.
///
/// Règle : commandes toutes instantanées → None ; commande de monture
/// (2134) ou id d'état connu → State ; sinon le signe net des arguments des
/// commandes de STATS (table <see cref="StatArgSign"/>, miroir des cas de
/// <c>LicenseService.ApplyEnchantCommands</c>) tranche Buff / Nerf ; aucun
/// argument de stat → State. Signes mêlés → le drapeau « bénéfique » de
/// c_enchant (col7 bit0) départage.
/// </summary>
public static class StatusKinds
{
    /// <summary>Commandes à effet INSTANTANÉ natif (aucun CEnchant
    /// persistant) — identique à <c>ZoneEntryFlow.InstantOnlyCommands</c>.
    /// </summary>
    public static readonly IReadOnlySet<int> InstantCommands = new HashSet<int>
    {
        1001, 1011,                       // HP / MP min max
        1802, 1803, 1804, 1807, 1809,     // DistanceGoto / summon
        1811, 1812,                       // RevivePlus / Revive
        1821, 1822, 1823, 1824, 1825,     // dispels / EnchantIncrease
        1997, 1999, 2000,                 // LifeFloat / SpellAttack
    };

    /// <summary>États connus par effect_id : pierre de portail gratuite
    /// du débutant (55869, 2047|1) et sa variante item-mall FREE (55870).
    /// Leur commande 2047 est un marqueur natif, pas un bonus de soin.
    /// </summary>
    public static readonly IReadOnlySet<int> KnownStateIds = new HashSet<int>
    {
        55869, 55870,
    };

    /// <summary>Commande 2134 = monture (CEC_Ride) : état de monture.</summary>
    public const int RideCommand = 2134;

    /// <summary>Commandes qui alimentent le pipeline de statistiques.</summary>
    public static readonly IReadOnlySet<int> StatCommands = new HashSet<int>
    {
        1004, 1014, 2001, 2002, 2003, 2011, 2013, 2014, 2015, 2038, 2040,
        2043, 2044, 2047, 2048, 2051, 2052, 2053, 2056, 2058, 2068, 2069,
        2081, 2082, 2083, 2084, 2085, 2086, 2087, 2088, 2095, 2101, 2102,
        2103, 2104, 2108, 2109, 2117,
    };

    /// <summary>Nombre d'arguments réellement lus par la commande de stat
    /// (les suivants sont inertes ou hors stats).</summary>
    public static int StatArgCount(int command) => command switch
    {
        2040 or 2043 or 2044 or 2048 or 2038 or 2047 or 2087 or 2088 => 2,
        2081 or 2082 or 2085 or 2086 or 2053 => 5,
        2083 or 2084 or 2101 => 3,
        2095 or 2102 or 2103 or 2104 or 2052 => 6,
        _ => 1,
    };

    /// <summary>+1 si un argument POSITIF de la commande est un gain,
    /// −1 s'il est une perte (vitesse d'attaque et de cast natives :
    /// négatif = plus rapide ; coût PM : négatif = moins cher).</summary>
    public static int StatArgSign(int command, int argIndex) => command switch
    {
        2052 => -1,
        2015 => -1,
        2053 => argIndex == 0 ? -1 : 1,
        _ => 1,
    };

    /// <summary>Classe un enchant. <paramref name="beneficial"/> = drapeau
    /// c_enchant col7 bit0.</summary>
    public static StatusKind Classify(int effectId,
        IReadOnlyList<(int Id, double[] Args)> commands, bool beneficial)
    {
        if (commands.Count == 0 ||
            commands.All(c => InstantCommands.Contains(c.Id)))
            return StatusKind.None;
        if (commands.Any(c => c.Id == RideCommand) ||
            KnownStateIds.Contains(effectId))
            return StatusKind.State;
        int gain = 0, loss = 0;
        foreach (var (id, args) in commands)
        {
            if (!StatCommands.Contains(id)) continue;
            for (var i = 0; i < Math.Min(args.Length, StatArgCount(id)); i++)
            {
                if (args[i] == 0) continue;
                var better = (args[i] > 0) == (StatArgSign(id, i) > 0);
                if (better) gain++; else loss++;
            }
        }
        if (gain == 0 && loss == 0) return StatusKind.State;
        if (loss == 0) return StatusKind.Buff;
        if (gain == 0) return StatusKind.Nerf;
        return beneficial ? StatusKind.Buff : StatusKind.Nerf;
    }
}
