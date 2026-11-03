using Eden.Core.Data.Parsers;

namespace Eden.Core.Game;

/// <summary>
/// Règles pures du système Break Point (« Barre de Rupture ») — miroir
/// des fonctions natives de la zone, sans état ni réseau (testable) :
///
/// • <c>CCharFormula::CalculatePatienceAttrDamage</c> @0x083f4260 (3-arg)
///   et @0x083f94e0 (4-arg) : un coup draine la « patience » du mob
///   (<c>CMonster+0x78</c>) si <c>mask_S_Monster &amp; (1 &lt;&lt; (attr−1))</c>
///   où <c>attr</c> est l'attribut de l'attaquant ;
/// • <c>CCharFormula::MaxPatience</c> @0x083f3d30 (cap 9 999 999) ;
/// • <c>CCharacter::AttackResult</c> @0x08416030, <c>HitMagicSkill</c>
///   @0x084ef710 : drain = dégâts du coup, puis rupture quand
///   <c>patience &lt; 1</c>.
///
/// Éléments (attr 1..9) : Slash, Strike, Pierce, Fire, Ice, Lightning,
/// Nature, Light, Dark. Ordre PROUVÉ en croisant le masque S_Monster de
/// 156 mobs boss avec les « Break Points= » du wiki (139 exacts, 12
/// sur-ensembles, 5 pages ambiguës) — l'ordre de la déduction initiale
/// du plan 13 (Fire/Lightning/Ice) était faux.
/// </summary>
public static class BreakPointRules
{
    /// <summary>Plafond natif de la patience (<c>MaxPatience</c>).</summary>
    public const int MaxPatienceCap = 9_999_999;

    /// <summary>Patience par défaut d'un mob sans donnée (<c>MaxPatience</c>
    /// renvoie 100 quand <c>template+0x120</c> est nul).</summary>
    public const int DefaultPatience = 100;

    /// <summary>Gate « tous secteurs » — octet 0xF = pas de filtre
    /// (<c>AttackResult</c> saute le test quand <c>data+0x2c == 0xF</c>).</summary>
    public const int GateAll = 0xF;

    /// <summary>Attribut Strike (coup de poing) : attaquant joueur sans
    /// arme → <c>uVar4 = 2</c> dans le 4-arg @0x083f94e0.</summary>
    public const int AttrStrike = 2;

    /// <summary>Attribut Slash — repli d'une entité non-joueur
    /// (<c>uVar4 = 1</c>).</summary>
    public const int AttrSlash = 1;

    /// <summary>Noms EN des 9 éléments, indexés par attr−1.</summary>
    public static readonly string[] ElementNames =
    {
        "Slash", "Strike", "Pierce", "Fire", "Ice",
        "Lightning", "Nature", "Light", "Dark",
    };

    /// <summary>Patience max effective d'un mob (cap natif).</summary>
    public static int MaxPatience(MonsterParser.MonsterDef? def)
    {
        if (def == null) return DefaultPatience;
        return Math.Clamp(def.Patience, 0, MaxPatienceCap);
    }

    /// <summary>3-arg natif : l'attribut <paramref name="attr"/> (1..9)
    /// figure-t-il dans le masque de faiblesses du mob ?</summary>
    public static bool IsWeakness(MonsterParser.MonsterDef? def, int attr)
    {
        if (def == null || attr < 1 || attr > 9) return false;
        return (def.BpWeakMask & (1 << (attr - 1))) != 0;
    }

    /// <summary>Attribut utilisé par l'attaquant pour le drain (4-arg
    /// @0x083f94e0, branche joueur) : l'enchant d'arme (<c>+0x15c</c>)
    /// l'emporte, puis l'attribut du sort (<c>SSpellData+0xbe</c>) ;
    /// en mêlée, l'attribut de l'arme (<c>item+0x128</c>) ; sans arme
    /// → Strike ; arme sans attribut → 0 (aucun drain).</summary>
    /// <param name="enchantAttr">Surcharge d'enchant d'arme (0 = aucune).</param>
    /// <param name="spellAttr">Attribut du sort (0 si mêlée ou sans attribut).</param>
    /// <param name="isSpell">Le coup vient d'un sort.</param>
    /// <param name="weaponAttr">Attribut de l'arme équipée.</param>
    /// <param name="hasWeapon">Une arme est équipée.</param>
    public static int AttackerAttr(int enchantAttr, int spellAttr,
        bool isSpell, int weaponAttr, bool hasWeapon)
    {
        if (isSpell)
        {
            if (spellAttr == 0 && enchantAttr == 0) return 0;
            return enchantAttr != 0 ? enchantAttr : spellAttr;
        }
        if (!hasWeapon) return AttrStrike;
        if (weaponAttr == 0 && enchantAttr == 0) return 0;
        return enchantAttr != 0 ? enchantAttr : weaponAttr;
    }

    /// <summary>Tier de secteur (0..4) à partir de l'écart de direction
    /// mob − attaquant (radians, ±π). Natif : cinq intervalles bornés
    /// par les constantes <c>_DAT_0888e8ec…908</c> (binaire zone hors
    /// dépôt). HYPOTHESIS : bornes ±π/6, ±π/2 → face / flancs / dos,
    /// directions inconnues (les deux à 0) → tier 4. Aucun mob des
    /// données n'utilise un gate ≠ 0xF avec masque, donc sans effet.</summary>
    public static int SectorTier(float mobDir, float attackerDir)
    {
        if (mobDir == 0f && attackerDir == 0f) return 4;
        var d = mobDir - attackerDir;
        while (d > MathF.PI) d -= 2 * MathF.PI;
        while (d < -MathF.PI) d += 2 * MathF.PI;
        var a = MathF.Abs(d);
        if (a <= MathF.PI / 6) return 0;
        if (a <= MathF.PI / 2) return d < 0 ? 1 : 2;
        return d < 0 ? 3 : 4;
    }

    /// <summary>Gate natif : <c>gate == 0xF</c> → tout passe, sinon le bit
    /// <c>1 &lt;&lt; tier</c> doit être posé.</summary>
    public static bool GateAllows(int gate, int tier) =>
        (gate & 0xFF) == GateAll || ((gate >> tier) & 1) != 0;

    /// <summary>Résultat d'un drain.</summary>
    public readonly record struct DrainResult(int Patience, bool Drained,
        bool Broke);

    /// <summary>Applique un coup de <paramref name="damage"/> à la patience
    /// courante : <c>patience −= damage</c> (valeur négative possible)
    /// si la barre existe (<paramref name="requireBar"/> reproduit
    /// l'asymétrie native — la mêlée exige <c>mob+0x7d</c>, pas les
    /// sorts), l'attribut est une faiblesse et le gate passe.
    /// <c>Broke</c> = la patience passe sous 1 (le flag one-shot
    /// <c>mob+0x7c</c> est géré par l'appelant).</summary>
    public static DrainResult Drain(MonsterParser.MonsterDef? def,
        int patience, int attr, int damage, bool requireBar, int tier)
    {
        if (def == null || damage <= 0 ||
            (requireBar && !def.HasBpBar) ||
            !IsWeakness(def, attr) ||
            !GateAllows(def.BpGate, tier))
            return new DrainResult(patience, false, false);
        var np = (int)Math.Max(int.MinValue, (long)patience - damage);
        return new DrainResult(np, true, np < 1);
    }

    /// <summary>Chances de rupture d'un cast par coup reçu : natif
    /// <c>GetHurt</c> fin @0x0849ad40 — <c>rand%100 &lt; 2</c> puis
    /// <c>rand%100 &gt;= resist</c> (<c>data+0xc6</c>, jamais écrit par
    /// le loader S_Monster → 0). Retourne vrai si le cast est brisé.</summary>
    public static bool RollSpellBreak(Random rng, int interruptResist = 0) =>
        rng.Next(100) < 2 && rng.Next(100) >= interruptResist;

    /// <summary>Durée du « stagger » de rupture (secondes) quand la durée
    /// de la motion 0x3f7 est inconnue : <c>SetEventAction(4|3, 0x3f7)</c>
    /// — 4 s pour la mêlée (<c>AttackResult</c>), 3 s pour les sorts
    /// (<c>HitMagicSkill</c>/<c>HitPhysicalSkill</c>).</summary>
    public static int FallbackStaggerSeconds(bool melee) => melee ? 4 : 3;
}
