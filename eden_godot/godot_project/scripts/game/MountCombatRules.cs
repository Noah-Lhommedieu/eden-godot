namespace EdenClient.Game;

/// <summary>Règles natives des montures (pures, testables) : routage des
/// actions de combat et tiers de vitesse affichés.</summary>
public static class MountCombatRules
{
    /// <summary>Ids d'action que <c>FUN_00778660</c> (rf_game l.724328)
    /// route vers la monture de combat ; tout autre id passe par
    /// <c>FUN_007785d0</c> et donne <c>ride01</c> (pose assise tenue) :
    /// magic 301/309/319/320 (vtable +0x78), shoot 401/409/412/3001+
    /// (+0x7c), attack 1001 (+0x70) et 1021-1024 (+0x74). Donc magic03
    /// (303), magic02… ne jouent rien sur une monture, même de combat.
    /// <paramref name="name"/> = nom d'anim motion.ini (magic01…).</summary>
    public static bool RoutedAction(string name)
    {
        if (name.Length < 3 || !int.TryParse(name[^2..], out var n))
            return false;
        if (name.StartsWith("magic")) return n is 1 or 9 or 19 or 20;
        if (name.StartsWith("attack")) return n is >= 1 and <= 4;
        return name.StartsWith("shoot");
    }

    /// <summary>Type de la commande enchant 2134 (rideKey) d'une ligne
    /// brute c_enchant : cellules après 2134 = rideKey, flag, sièges,
    /// <b>type</b> — 2 = monture de combat (le ride survit à l'engage ;
    /// enchant-tables.md §1.3, ex. <c>2134|253020||1|2</c>). 0 = monture
    /// ordinaire ou pas de 2134.</summary>
    public static int RideTypeOf(string[]? raw)
    {
        if (raw == null) return 0;
        for (var i = 0; i + 1 < raw.Length; i++)
            if (int.TryParse(raw[i], out var code) && code == 2134)
            {
                var j = i + 4;                    // var3 = type
                return j < raw.Length &&
                       int.TryParse(raw[j], out var t) ? t : 0;
            }
        return 0;
    }

    /// <summary>Vrai si l'enchant porte une monture de combat
    /// (2134 type 2) — badge « épée » natif sur l'icône d'item.</summary>
    public static bool IsCombatRide(int enchantId) =>
        enchantId > 0 && RideTypeOf(
            Data.Tables.EnchantTable.Get(enchantId)?.Raw) == 2;

    /// <summary>Vitesse par couleur (spec propriétaire, custom serveur) :
    /// blanc 35 %, vert 55 %, bleu 70 %, or 90 %. MÊME règle que
    /// <c>Eden.Zone.RideSpeedRules.TierPct</c> (le serveur est la source ;
    /// ce miroir ne sert qu'à l'affichage des infobulles). Bonus natif
    /// ≤ 0 (malus voulus) : inchangé.</summary>
    public static int TierPct(int nativePct)
        => nativePct <= 0 ? nativePct
         : nativePct <= 25 ? 35
         : nativePct <= 42 ? 55
         : nativePct <= 65 ? 70
         : 90;

    private static readonly System.Text.RegularExpressions.Regex SpeedText =
        new(@"(VIT D[ÉE]P\s*)([+-]?)(\d+)(\s*%)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex SpeedProse =
        new(@"(vitesse(?: de déplacement)? de )(\d+)(\s*%)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Réécrit « VIT DÉP +75 % » / « vitesse de 15 % » de la
    /// description d'un enchant de monture avec le palier effectif
    /// (<see cref="TierPct"/> du bonus natif <paramref name="nativePct"/>
    /// lu dans la commande 2051). Texte sans motif, ou bonus ≤ 0 :
    /// renvoyé tel quel.</summary>
    public static string TierText(string text, int nativePct)
    {
        if (nativePct <= 0) return text;
        var tier = TierPct(nativePct);
        text = SpeedText.Replace(text, m => m.Groups[1].Value +
            (m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : "+") +
            tier + m.Groups[4].Value);
        return SpeedProse.Replace(text, m => m.Groups[1].Value + tier +
            m.Groups[3].Value);
    }
}
