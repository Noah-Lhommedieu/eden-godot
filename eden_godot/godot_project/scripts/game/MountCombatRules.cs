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
    /// <b>type</b> — le natif ne teste que <c>≠ 0</c> : <c>CEC_Ride::SetState</c>
    /// (zone @0x0854f330) stocke var3 en char+0x824, relu à l'identique
    /// dans <c>CEC_Chaos::SetState</c> (@0x0851cac0) et
    /// <c>ISpell::CastTest</c> (@0x084f08c3) — 0 →
    /// <c>RemoveEnchantByState(9)</c> (dismount à l'engage/transform),
    /// ≠0 → le ride survit + KFM <c>r%06d</c> chargé ; client
    /// <c>FUN_00778660</c> route les anims de combat sur l'octet ride ≠0.
    /// Données : type 1 = montures à sorts (10 enchants, TOUS avec 2140
    /// RideSpell), type 2 = combat ordinaire (81) — combat dans les deux
    /// cas ; 0/absent = monture ordinaire ou pas de 2134
    /// (enchant-tables.md §1.3, ex. <c>2134|253020||1|2</c>).</summary>
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
    /// (2134 type ≠ 0, preuve native ci-dessus — même règle que le
    /// serveur <c>RideType != 0</c>) — badge « épée » natif sur
    /// l'icône d'item. Les montures à sorts (type 1 : dragons, canon,
    /// toutes avec 2140) survivent à l'engage comme les type 2.</summary>
    public static bool IsCombatRide(int enchantId) =>
        enchantId > 0 && RideTypeOf(
            Data.Tables.EnchantTable.Get(enchantId)?.Raw) != 0;

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
