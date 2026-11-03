using Eden.Core.Game.Services;

namespace Eden.Core.Game.Stats;

/// <summary>
/// Indices de stats primaires du pipeline — ordre wire natif du 296
/// (NotifyRefreshAttr : Str, Int, Dex=Agi, Spr=Wis, Luk=Lck) + vitals.
/// </summary>
public enum StatIndex : byte
{
    Str = 0, Int = 1, Agi = 2, Wis = 3, Lck = 4, Hp = 5, Mp = 6,
}

/// <summary>
/// Contribution d'une étape du pipeline de stats — trois canaux par
/// stat, miroir de la formule <c>CCharFormula::Total*</c> native :
/// <code>total = (lvlStat + Σpre) × (classPct + Σpct)/100 + Σpost</code>
///   • <b>pre</b>  : rejoint l'accumulateur +0x38c (équipement,
///                    costumes — sommés par RefreshEQBonus) ;
///   • <b>pct</b>  : rejoint le multiplicateur `growth` (titres
///                    permanents, talents % passifs) ;
///   • <b>post</b> : ajouté après le produit (buffs flats/enchants
///                    `flat` natifs).
/// </summary>
public sealed class StatContribution
{
    // Les accumulateurs natifs CKindData sont des FLOATS
    // (CEC_Attr*::Execute : `kinddata+0xXX += float(arg×0.01)`) et les
    // données réelles ont des args fractionnels (« 0.5 », « 1.5 »,
    // « -2.5 » — p.ex. l'enchant Reforged de talent 54813 =
    // `2083|||0.5` → +0.5 % M-ATK/pt). Les canaux sont donc en
    // double : un cast (int) les annulait tous (bug « talents sans
    // stats »).
    private const int Count = 7;
    private readonly double[] _pre = new double[Count];
    private readonly double[] _pct = new double[Count];
    private readonly double[] _post = new double[Count];
    /// <summary>Bonus d'esquive en points de pourcentage, avant la
    /// conversion native vers la valeur wire ×100.</summary>
    public double DodgePercent { get; private set; }
    /// <summary>Bonus FLAT d'attaque physique — accumulateur
    /// `AttrPhysicoDamage` natif : il rejoint le terme <i>flat</i> de
    /// <c>CCharFormula::MeleeDamage</c> (hors produit par le % buff).
    /// Bonus de palier de classe (S_ClassRankBuff) notamment.</summary>
    public double PhysAttackAdd { get; private set; }
    /// <summary>Bonus FLAT d'attaque magique — miroir de
    /// <see cref="PhysAttackAdd"/> pour <c>MagicDamage</c>
    /// (`AttrMagicDamage` natif, arg 3 du cmd 2084).</summary>
    public double MagicAttackAdd { get; private set; }

    // ===== Canaux secondaires — miroir des accums CKindData natifs
    // (offsets prouvés par les CEC_Attr*::Execute et CCharFormula) =====

    /// <summary>DEF flat post-multiplicateur — accum natif
    /// `kinddata+0xec` (`AttrDefence`, cmds 2082/2084).</summary>
    public double DefenceAdd { get; private set; }
    /// <summary>DEF % — accum `kinddata+0xf0` (`AttrDefenceRate`,
    /// cmd 2083 var1) : `(eq+base)×(100+pct)×0.01`.</summary>
    public double DefenceRatePercent { get; private set; }
    /// <summary>Rating de précision flat — accum `kinddata+0x114`
    /// (`AttrHitRate`, cmds 2082 var1 / 2087 var0) : rejoint le
    /// numérateur de `PhysicalHitRate` avant la division par niveau.
    /// </summary>
    public double HitRatingAdd { get; private set; }
    /// <summary>Rating d'esquive flat — accum `kinddata+0x11c`
    /// (`AttrDodgeRate`, cmds 2082 var2 / 2087 var1).</summary>
    public double DodgeRatingAdd { get; private set; }
    /// <summary>Rating de critique flat — accum `kinddata+0x100`
    /// (`AttrCriticalRate` val1, cmd 2048 var0) : numérateur de
    /// `PhysicalCriticalRate`.</summary>
    public double CritRatingAdd { get; private set; }
    /// <summary>Rating de critique magique flat — accum
    /// `kinddata+0x10c` (`AttrMagicCriticalRate` val1, cmd 2048 var1).
    /// </summary>
    public double MagicCritRatingAdd { get; private set; }
    /// <summary>Dégâts critiques +% — accum `kinddata+0xfc` natif
    /// (ajouté à `PhysicalCriticalDamage` avec para311).</summary>
    public double CritDamageAdd { get; private set; }
    /// <summary>Dégâts crit magiques +% — accum `kinddata+0x108`
    /// (`MagicCriticalDamage`).</summary>
    public double MagicCritDamageAdd { get; private set; }
    /// <summary>P-ATK % — accum `kinddata+0xe0` (`AttrPhysicoDamageRate`,
    /// cmd 2083 var0) : multiplicateur `(100+pct)` de MeleeDamage.</summary>
    public double PhysAttackRatePercent { get; private set; }
    /// <summary>M-ATK % — accum `kinddata+0xe8` (`AttrMagicDamageRate`,
    /// cmd 2083 var2).</summary>
    public double MagicAttackRatePercent { get; private set; }
    /// <summary>Critique % multiplicatif — accum `kinddata+0x104`
    /// (`AttrCriticalRate 2`, cmd 2040 var0) : le natif stocke
    /// `arg×0.01` en float et multiplie le numérateur de
    /// `PhysicalCriticalRate` par `(1 + accum)`.</summary>
    public double CritRatePercent { get; private set; }
    /// <summary>Critique magique % — accum `kinddata+0x110`
    /// (`AttrMagicCriticalRate 2`, cmd 2040 var1) : même facteur
    /// `(1+accum)` dans `MagicCriticalRate`.</summary>
    public double MagicCritRatePercent { get; private set; }
    /// <summary>Vitesse d'attaque flat — accum `kinddata+0x124`
    /// (`AttrAttackSpeedPlus`, cmd 2056/2087*) : numérateur de
    /// `AttackSpeedLevelRate`.</summary>
    public double AttackSpeedAdd { get; private set; }
    /// <summary>Vitesse d'attaque % — accum `kinddata+0x12c`
    /// (`AttrAttackSpeedPlus 2`, cmd 2068) : facteur `(1+accum)`
    /// du numérateur d'`AttackSpeedLevel`.</summary>
    public double AttackSpeedRatePercent { get; private set; }
    /// <summary>Vitesse de cast flat — accum `kinddata+0x134`
    /// (`AttrCastTimePlus`) : numérateur de `CastSpeedLevelRate`.</summary>
    public double CastSpeedAdd { get; private set; }
    /// <summary>Vitesse de cast % — accum `kinddata+0x13c`
    /// (`AttrCastTimePlus 2`, cmd 2069) : facteur `(1+accum)`
    /// du numérateur de `MagicSpeedLevel`.</summary>
    public double CastSpeedRatePercent { get; private set; }
    /// <summary>Vitesse de déplacement % — accum `kinddata+0xa0`
    /// (`AttrMovement`, cmd 2051 — points de pourcentage).</summary>
    public double MoveSpeedPercent { get; private set; }
    /// <summary>Blocage +% — accum `kinddata+0x154` (`AttrBlock`,
    /// cmd 2117) : rejoint `BlockDamageRate`.</summary>
    public double BlockPercentAdd { get; private set; }
    /// <summary>Parade flat — accum `kinddata+0x158` (`AttrParry`) :
    /// rejoint `ParryLv`.</summary>
    public double ParryAdd { get; private set; }
    /// <summary>Résistances flat par élément (1-9) — accum
    /// `SResistAttribute` `kinddata+0x00..0x11` (`AttrResist`,
    /// cmds 2095 éléms 4-9 / 2101 éléms 1-3).</summary>
    public int[] ResistAdds { get; } = new int[9];
    /// <summary>Précision % multiplicatif — accum natif
    /// `AttrHitRateRate` (cmd 2088 var0, désasm 0x838f1a2 : la
    /// string native poussée par la branche arg0 est
    /// « AttrHitRateRate ») : facteur `(1+pct)` sur le taux de hit.
    /// </summary>
    public double HitRatePercent { get; private set; }
    /// <summary>Esquive % multiplicatif — accum natif
    /// `AttrDodgeRateRate` (cmd 2088 var1) : facteur `(1+pct)` sur
    /// le taux d'esquive.</summary>
    public double DodgeRatePercent { get; private set; }
    /// <summary>Dégâts % par type d'arme — accum natif
    /// `AttrWeaponDamageRate` (cmds 2102/2103/2104 : chaque arg = un
    /// kind d'arme, désasm CEC_AttrWeaponDamageRate::Execute
    /// @0x8518710 : index = (cmd−2102)*6 + arg, kinds 0x11..0x21 —
    /// accums float `kinddata+0x340..`). Indexé par kind−0x11 —
    /// double : les données réelles ont des fractionnels (« 1.5 »).</summary>
    public double[] WeaponDamageRatePct { get; } = new double[17];
    /// <summary>Soins prodigués +% — accum natif `HealPlus` mode 2
    /// (cmd 2038 arg0, désasm 0x8392e28).</summary>
    public double HealGivenPercent { get; private set; }
    /// <summary>Soins reçus +% — accum natif `HealPlus` mode 3
    /// (cmd 2038 arg1).</summary>
    public double HealTakenPercent { get; private set; }
    /// <summary>Coût MP flat — accum natif `CostMPPlus` mode 0
    /// (cmd 2015 arg0 ; valeurs négatives = réduction).</summary>
    public double MpCostFlat { get; private set; }
    /// <summary>Aggro générée % — accum natif `AttrHateRate`
    /// (cmd 2122 arg0).</summary>
    public double HateRatePercent { get; private set; }

    /// <summary>Ajoute une contribution sur une stat.</summary>
    public StatContribution Add(StatIndex stat, double pre = 0,
        double pct = 0, double post = 0)
    {
        _pre[(int)stat] += pre;
        _pct[(int)stat] += pct;
        _post[(int)stat] += post;
        return this;
    }

    /// <summary>Accumule une autre contribution (somme par canal —
    /// les étapes se composent sans ordre puisque tout est additif).</summary>
    public StatContribution Merge(StatContribution o)
    {
        for (var i = 0; i < Count; i++)
        {
            _pre[i] += o._pre[i];
            _pct[i] += o._pct[i];
            _post[i] += o._post[i];
        }
        DodgePercent += o.DodgePercent;
        PhysAttackAdd += o.PhysAttackAdd;
        MagicAttackAdd += o.MagicAttackAdd;
        DefenceAdd += o.DefenceAdd;
        DefenceRatePercent += o.DefenceRatePercent;
        HitRatingAdd += o.HitRatingAdd;
        DodgeRatingAdd += o.DodgeRatingAdd;
        CritRatingAdd += o.CritRatingAdd;
        MagicCritRatingAdd += o.MagicCritRatingAdd;
        CritDamageAdd += o.CritDamageAdd;
        MagicCritDamageAdd += o.MagicCritDamageAdd;
        PhysAttackRatePercent += o.PhysAttackRatePercent;
        MagicAttackRatePercent += o.MagicAttackRatePercent;
        CritRatePercent += o.CritRatePercent;
        MagicCritRatePercent += o.MagicCritRatePercent;
        AttackSpeedAdd += o.AttackSpeedAdd;
        AttackSpeedRatePercent += o.AttackSpeedRatePercent;
        CastSpeedAdd += o.CastSpeedAdd;
        CastSpeedRatePercent += o.CastSpeedRatePercent;
        MoveSpeedPercent += o.MoveSpeedPercent;
        BlockPercentAdd += o.BlockPercentAdd;
        ParryAdd += o.ParryAdd;
        HitRatePercent += o.HitRatePercent;
        DodgeRatePercent += o.DodgeRatePercent;
        HealGivenPercent += o.HealGivenPercent;
        HealTakenPercent += o.HealTakenPercent;
        MpCostFlat += o.MpCostFlat;
        HateRatePercent += o.HateRatePercent;
        for (var i = 0; i < 9; i++) ResistAdds[i] += o.ResistAdds[i];
        for (var i = 0; i < 17; i++)
            WeaponDamageRatePct[i] += o.WeaponDamageRatePct[i];
        return this;
    }

    /// <summary>Ajoute un bonus explicite de taux d'esquive.</summary>
    public StatContribution AddDodgePercent(double points)
    {
        DodgePercent += points;
        return this;
    }

    /// <summary>Accumulateurs secondaires — cmd→canal natif (preuve
    /// objdump `DecodeEffectCommand` @0x8380ad0 + CEC_*::Execute).</summary>
    public StatContribution AddSecondary(
        double defence = 0, double defencePct = 0, double hitRating = 0,
        double dodgeRating = 0, double critRating = 0,
        double magicCritRating = 0,
        double critDamage = 0, double magicCritDamage = 0,
        double physAttackPct = 0, double magicAttackPct = 0,
        double attackSpeed = 0, double castSpeed = 0,
        double moveSpeedPct = 0,
        double blockPct = 0, double parry = 0, double critRatePct = 0,
        double magicCritRatePct = 0, double attackSpeedPct = 0,
        double castSpeedPct = 0, double hitRatePct = 0,
        double dodgeRatePct = 0,
        double healGivenPct = 0, double healTakenPct = 0,
        double mpCost = 0, double hateRatePct = 0)
    {
        DefenceAdd += defence;
        DefenceRatePercent += defencePct;
        HitRatingAdd += hitRating;
        DodgeRatingAdd += dodgeRating;
        CritRatingAdd += critRating;
        MagicCritRatingAdd += magicCritRating;
        CritDamageAdd += critDamage;
        MagicCritDamageAdd += magicCritDamage;
        PhysAttackRatePercent += physAttackPct;
        MagicAttackRatePercent += magicAttackPct;
        AttackSpeedAdd += attackSpeed;
        AttackSpeedRatePercent += attackSpeedPct;
        CastSpeedAdd += castSpeed;
        CastSpeedRatePercent += castSpeedPct;
        CritRatePercent += critRatePct;
        MagicCritRatePercent += magicCritRatePct;
        MoveSpeedPercent += moveSpeedPct;
        BlockPercentAdd += blockPct;
        ParryAdd += parry;
        HitRatePercent += hitRatePct;
        DodgeRatePercent += dodgeRatePct;
        HealGivenPercent += healGivenPct;
        HealTakenPercent += healTakenPct;
        MpCostFlat += mpCost;
        HateRatePercent += hateRatePct;
        return this;
    }

    /// <summary>Dégâts % pour un kind d'arme — canal
    /// <see cref="WeaponDamageRatePct"/> (`AttrWeaponDamageRate`,
    /// cmds 2102-2104 : slot = (cmd−2102)*6 + index d'arg =
    /// kind−0x11, désasm Execute @0x8518710).</summary>
    public StatContribution AddWeaponDamageRate(int kindSlot,
        double pct)
    {
        if (kindSlot is >= 0 and < 17)
            WeaponDamageRatePct[kindSlot] += pct;
        return this;
    }

    /// <summary>Résistance élémentaire flat — `elem` ∈ [1,9]
    /// (AttrResist natif : `resists[elem−1] += value`).</summary>
    public StatContribution AddResist(int elem, int value)
    {
        if (elem is >= 1 and <= 9) ResistAdds[elem - 1] += value;
        return this;
    }

    /// <summary>Ajoute un bonus flat d'attaque (canal
    /// <see cref="PhysAttackAdd"/>/<see cref="MagicAttackAdd"/>).</summary>
    public StatContribution AddAttack(double phys = 0,
        double magic = 0)
    {
        PhysAttackAdd += phys;
        MagicAttackAdd += magic;
        return this;
    }

    public double Pre(StatIndex s) => _pre[(int)s];
    public double Pct(StatIndex s) => _pct[(int)s];
    public double Post(StatIndex s) => _post[(int)s];

    /// <summary>Fabrique la contribution « équipement » depuis
    /// l'agrégat <see cref="EquipmentStats"/> — canal <b>pre</b> :
    /// l'équipement entre dans la somme modulée par le % de classe
    /// (RefreshEQsAttr → +0x38c).</summary>
    /// <summary>Contribution « tous canaux » d'une source d'items
    /// additionnelle (gemmes, fortification) : stats core et HP/MP en
    /// <b>pre</b>, le reste dans les accumulateurs secondaires. À la
    /// différence de <see cref="FromEquipment"/> (core+vitals seulement,
    /// le reste de l'équipement passe par le snapshot combat), ces
    /// sources n'entrent pas dans <c>EquipmentStats</c> du snapshot.</summary>
    public static StatContribution FromBonusStats(EquipmentStats eq)
    {
        var c = FromEquipment(eq);
        c.AddAttack(eq.PAtk, eq.MAtk);
        c.AddSecondary(defence: eq.Def, hitRating: eq.Hit,
            dodgeRating: eq.Dodge, critRating: eq.Crit,
            magicCritRating: eq.MagicCrit, critDamage: eq.CritDamage,
            magicCritDamage: eq.MagicCritDamage,
            attackSpeed: eq.AttackSpeed, castSpeed: eq.CastSpeed,
            parry: eq.Parry);
        for (var i = 0; i < 9; i++)
            c.AddResist(i + 1, eq.Resists?.ElementAtOrDefault(i) ?? 0);
        return c;
    }

    public static StatContribution FromEquipment(EquipmentStats eq)
        => new StatContribution()
            .Add(StatIndex.Str, pre: eq.Str)
            .Add(StatIndex.Int, pre: eq.Int)
            .Add(StatIndex.Agi, pre: eq.Agi)
            .Add(StatIndex.Wis, pre: eq.Wis)
            .Add(StatIndex.Lck, pre: eq.Lck)
            .Add(StatIndex.Hp, pre: eq.BonusHp)
            .Add(StatIndex.Mp, pre: eq.BonusMp);
}

/// <summary>
/// Pipeline de stats ordonné et composable — la demande explicite :
/// <b>base → équipement → titres/permanents → costumes →
/// buffs temporaires/procs</b>. Chaque étape nommée fournit une
/// <see cref="StatContribution"/> ; la formule finale reste celle de
/// <c>CCharFormula::Total*</c> (somme modulée par le % de classe,
/// puis flats).
///
/// Enregistrement (ordre des étapes = ordre d'appel) :
/// <code>
///   pipe.AddStage("equipment", …);      // RefreshEQsAttr natif
///   pipe.AddStage("permanents", …);     // titres d'honneur, talents
///   pipe.AddStage("costumes", …);       // attrs des costumes
///   pipe.AddStage("buffs", …);          // enchants temporaires/procs
/// </code>
/// Les étapes sans source de données encore câblée ne sont pas
/// enregistrées — <see cref="ResolveAsync"/> ne retourne que les
/// contributions réelles.
/// </summary>
/// <summary>Lignes préchargées passées aux étapes du pipeline pour
/// éviter de re-requêter la base (Phase 2 — chargement d'entrée
/// hors-boucle). Clés conventionnelles lues par les étapes de
/// <c>Program.cs</c> : « classlist » (IReadOnlyList&lt;
/// ClassProgressEntity&gt;), « char » (PlayerCharacterEntity),
/// « aa » (Dictionary&lt;short,short&gt;), « enchants »
/// (IReadOnlyList&lt;PlayerEnchantEntity&gt;), « limitless.perks »
/// (IReadOnlyList&lt;LimitlessService.Perk&gt; — null si service
/// absent) et « appellation » (int — titre actif, 0 = aucun).
/// Une clé présente vaut réponse — même à null : l'étape ne fait
/// alors AUCUNE requête.</summary>
public sealed class StatFetch
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    // Cache écrit par les étapes elles-mêmes (ex. liste d'équipement
    // chargée UNE fois pour equipment/gems/fortify/costumes).
    private readonly Dictionary<string, object?> _overlay = new();

    public StatFetch(IReadOnlyDictionary<string, object?> values)
        => _values = values;

    /// <summary>Mémorise une valeur chargée par une étape pour les
    /// étapes suivantes du même <c>ResolveAsync</c>.</summary>
    public void Put(string key, object? value) => _overlay[key] = value;

    /// <summary>true si la clé était fournie — <paramref name="value"/>
    /// peut alors être null (information « pas de donnée »).</summary>
    public bool TryGet<T>(string key, out T? value)
    {
        if (_overlay.TryGetValue(key, out var ov))
        {
            value = ov is T ot ? ot : default;
            return true;
        }
        if (_values.TryGetValue(key, out var v))
        {
            value = v is T t ? t : default;
            return true;
        }
        value = default;
        return false;
    }
}

public sealed class StatPipeline
{
    /// <summary>Fournit la contribution d'une étape pour un perso.
    /// <paramref name="fetch"/> porte les lignes déjà lues par un
    /// chargement hors-boucle : l'étape l'utilise en priorité et ne
    /// retombe sur la DB que si sa clé est absente.</summary>
    public delegate Task<StatContribution> Provider(int charId,
        CancellationToken token, StatFetch? fetch);

    private readonly List<(string Name, Provider Provide)> _stages = new();

    /// <summary>Noms des étapes dans l'ordre d'enregistrement —
    /// visible pour le diagnostic.</summary>
    public IReadOnlyList<string> StageNames =>
        _stages.ConvertAll(st => st.Name);

    /// <summary>Ajoute une étape nommée à la fin du pipeline.</summary>
    public StatPipeline AddStage(string name, Provider provider)
    {
        _stages.Add((name, provider));
        return this;
    }

    /// <summary>Résout toutes les étapes dans l'ordre et somme leurs
    /// contributions — le résultat alimente <see cref="ResolveStat"/>.
    /// </summary>
    public async Task<StatContribution> ResolveAsync(int charId,
        CancellationToken token = default, StatFetch? fetch = null)
    {
        var total = new StatContribution();
        // Fetch vide si absent : les étapes y partagent leurs chargements.
        fetch ??= new StatFetch(new Dictionary<string, object?>());
        foreach (var (_, provide) in _stages)
            total.Merge(await provide(charId, token, fetch));
        return total;
    }

    /// <summary>Formule <c>CCharFormula::TotalX</c> :
    /// <c>(lvlStat + pre) × (classPct + pct) / 100 + post</c> —
    /// `classPct` = le % de classe de la stat (S_Class cols17-21).</summary>
    public static int ResolveStat(StatContribution c, StatIndex s,
        int lvlStat, int classPct)
        => Math.Clamp((int)((lvlStat + c.Pre(s))
            * (classPct + c.Pct(s)) / 100 + c.Post(s)), 0, 32000);
}
