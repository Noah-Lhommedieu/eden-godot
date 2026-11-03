using Eden.Core.Data.Parsers;

namespace Eden.Zone.Combat;

/// <summary>
/// Résultat d'un roll d'attaque physique — valeurs du champ `type` du
/// paquet 272 ClientMeleeAttack (désasm caller @0x83c6xxx : iVar15 :
/// 1=coup, 2=critique, 8=miss ; 3=premier coup et 5=coup fatal sont
/// posés par le flow selon l'état de la cible — capture zsniff).
/// </summary>
public enum PhysicalOutcome : byte
{
    /// <summary>Coup normal (type 1 du 272).</summary>
    Hit = 1,
    /// <summary>Coup critique (type 2 — dégâts ×critDmg%/100).</summary>
    Crit = 2,
    /// <summary>Raté (type 8, dégâts 0 — roll rand%100 > hitRate).</summary>
    Miss = 8,
}

/// <summary>Dégâts + résultat d'une attaque physique résolue.
/// `Flags` = le mot drapeau natif complet : bit0=hit, bit1=crit,
/// bit3=miss, 0x10=block (`CalculateBlockDamage` → dmg 0),
/// 0x100=parry (`CalculateParryDamage` → dmg/2) — l'ordre du caller
/// natif (character.cc @0x1a42) : hit → dmg → crit → block → parry
/// (parry gaté par dmg&gt;0 — un blocage qui annule coupe la parade).</summary>
public readonly record struct PhysicalResult(int Damage,
    PhysicalOutcome Outcome, int Flags = 0)
{
    /// <summary>bit3 du flags-word natif (dégât nul — `*param_3=8`).</summary>
    public const int FlagMiss = 8;
    /// <summary>0x10 du flags-word natif — `CalculateBlockDamage`
    /// réussi : dégât mis à 0 par le bouclier.</summary>
    public const int FlagBlock = 0x10;
    /// <summary>0x100 du flags-word natif — `CalculateParryDamage`
    /// réussi : dégât divisé par 2.</summary>
    public const int FlagParry = 0x100;
}

/// <summary>Famille de résolution d'un sort — dispatch du flag mot
/// `SSpellData+0x94` calculé par `SSpellData::InitHurtType` @0x84ede40
/// depuis le type (`+0xc` = S_Spell **col18**, désasm
/// `CSpellQuery::Deserialize` : col18→`CSpellData+0x38`→`SSpellData+0xc`)
/// et l'élément (`+0xbe` = col33) puis lu par `OnHitLife` @0x84f2080 :
/// `(flags&0x68)!=0 && type∉{4,5}` → magique, `flags&4` → physique,
/// `type∈{4,5}` → restauration.</summary>
public enum SpellHitKind
{
    /// <summary>Aucun dégât (flags = 0 — sorts non offensifs).</summary>
    None = 0,
    /// <summary>`HitPhysicalSkill` → `CalculatePhysicalSkillDamage`
    /// (base mêlée + power×refPhys%).</summary>
    Physical = 1,
    /// <summary>`HitMagicSkill` → `CalculateMagicSkillDamage`
    /// (base M-ATK + power×refMag%, résist élémentaire).</summary>
    Magic = 2,
    /// <summary>`HitRestoreSkill` — sorts de type 4/5 (col18 : soins,
    /// résurrection — Cure 50028, Healing Light 50055, Revive 50030).</summary>
    Restore = 3,
}

/// <summary>Flags du champ résultat de `CNC_CZ_ClientSpellHitToLife`
/// (NCID 281, 5e champ i32) — valeurs posées par
/// `HitMagicSkill`/`HitPhysicalSkill`/`HitRestoreSkill`
/// (@0x84ef710/@0x84f1a00/@0x84f1f40) et lues par l'apply cible
/// (vtable+0x94) : `0x200` → +HP (affichage vert).</summary>
public static class SpellHitFlags
{
    /// <summary>Coup touché (HitXSkill pose `*flags = 1`).</summary>
    public const int Hit = 1;
    /// <summary>Raté — `*flags = 8` sur échec du roll hit.</summary>
    public const int Miss = 8;
    /// <summary>Blocage — `|0x10` si `CalculateBlockDamage` réussit.</summary>
    public const int Block = 0x10;
    /// <summary>Restauration (soin) — `HitRestoreSkill` pose
    /// `*flags = 0x200` ; le client affiche la valeur en vert.</summary>
    public const int Restore = 0x200;
    /// <summary>Critique — `|0x200000` sur roll crit réussi
    /// (dégâts ET soins — `HitRestoreSkill` le pose aussi).</summary>
    public const int Crit = 0x200000;
}

/// <summary>Soin + flag crit d'un sort de restauration résolu —
/// retour de <see cref="NativeCombatFormulas.ResolveSpellHeal"/>.</summary>
public readonly record struct SpellHealResult(int Amount, bool Critical);

/// <summary>
/// Transcription des formules de `lapis::CCharFormula` (désasm
/// `re/ghidra/zone/decompiled.c`) — damage/rating pipeline natif pour
/// la mêlée et les sorts, joueurs et monstres.
///
/// Paramètres S_Parameter utilisés (valeurs vérifiées dans
/// `S_Parameter.ini`, index = id de ligne = offset/4 dans
/// `CFormula::AllFormulaParas`) :
///   38  = 0.5   coef de def magique (CalculateMagicalDamage)
///   305 = 5     STR → attaque physique (BaseMeleeDamage/MeleeDamage)
///   306 = 0.3   ratio arme secondaire en dual-wield
///   307 = 5     INT → attaque magique (MagicDamage)
///   309 = 0.8   LCK → niveau de critique
///   310 = 3     base du niveau de critique
///   311 = 150   base des dégâts critiques %
///   312 = 1     LCK → niveau de critique magique
///   313 = 3     base du niveau de critique magique
///   314 = 150   base des dégâts critiques magiques %
///   315 = 1     LCK → niveau de précision
///   316 = 1     base du niveau de précision
///   317 = 1     AGI → niveau d'esquive
///   318 = 5     base du niveau d'esquive
///   325 = 100   base du taux de toucher (%)
///   326 = 3     param défense dans les dégâts physiques
///   327 = 3     param défense dans les dégâts de skill physique
///
/// Buffs natifs (champs kinddata/CCharAttr : +0xdc/+0xe0/+0x94/+0x96/
/// +0x98/+0xfc/+0x108/+0x114/+0x118/+0x15c/+0x178/+0x540/+0x304/+0x38c/
/// +0x1f4/+0x3d0/+0x4ac — enchants, maîtrises d'armes, susceptibilités)
/// : non modélisés serveur → 0, comme dans l'état native sans buff.
/// </summary>
public static class NativeCombatFormulas
{
    /// <summary>Jeu de paramètres résolu depuis `ParameterSet`
    /// (S_Parameter.ini) — les ids S_Parameter correspondent aux index
    /// `CFormula::AllFormulaParas + id*4` du natif.</summary>
    public readonly record struct Params(
        float MagicDefCoef,      // 38
        float StrToPAtk,         // 305
        float OffhandRatio,      // 306
        float IntToMAtk,         // 307
        float LckToCrit,         // 309
        float BaseCritLv,        // 310
        float BaseCritDmg,       // 311
        float LckToMCrit,        // 312
        float BaseMCritLv,       // 313
        float BaseMCritDmg,      // 314
        float LckToHit,          // 315
        float BaseHitLv,         // 316
        float AgiToDodge,        // 317
        float BaseDodgeLv,       // 318
        float BaseHitRate,       // 325
        float DefParam,          // 326
        float SkillDefParam,     // 327
        float AgiToParry,        // 319
        float StrToBlock,        // 320
        float AgiToAtkSpeed,     // 321 (0.5 natif — AGI→vitesse attaque)
        float AgiToCastSpeed,    // 322 (1 natif — AGI→vitesse cast)
        float WisToHeal,         // 323 (1.2 natif — WIS→G-Healing)
        float WisToBeHealed,     // 324 (1 natif — WIS→P-Healing)
        float DefToHpDivisor,    // 562 (pdef → bonus MaxHP)
        float ExpLevelDiff,      // 328 (9 natif — écart de niveau exp)
        float ExpLevelDec,       // 329 (9 natif — décrément exp par écart)
        float TeamExpShareCoef,  // 330 (25 natif — coef exp partagée)
        float TeamExpNoShareCoef,// 331 (5 natif — coef exp non partagée)
        float EquipExpRate,      // 496 (0.2 natif — % exp d'engrenage)
        float DesDeclineUp,      // 576 (17 — palier haut DesDeclineRate destin)
        float DesDeclineDown,    // 577 (20 — palier bas DesDeclineRate destin)
        float DpPerKill,         // 375 (25 — Destiny Points par kill ±5 lv)
        float DpRollCost,        // 376 (1250 — DP dépensés par avancée)
        float DpVitCapMul,       // 569 (10 — cap vit = 100×p)
        float DpPoolCapMul,      // 570 (10 — cap DP = p×DpRollCost)
        float SummonMobLifespan, // 363 (30 s — durée de vie mob d'event)
        float LuckyBagFailStep,  // 452 (30 — %+échec par lancer lucky bag)
        float LuckyBagFailCap);  // 453 (90 — cap cumul d'échec lucky bag)

    /// <summary>Résout le jeu de paramètres depuis S_Parameter
    /// (`AllFormulaParas` natif = id → float, même indexation) —
    /// chaque coefficient passe d'abord par les surcharges
    /// `GameBalance.Param` (balance.ini `param.<id>` /
    /// `EDEN_BAL_PARAM_<id>`) pour les ratios custom serveur.</summary>
    public static Params ResolveParameters(
        ParameterParser.ParameterSet p)
    {
        float G(int id, float fallback) =>
            Eden.Core.Game.GameBalance.Param(id) ?? p.Get(id, fallback);
        return new Params(
            MagicDefCoef: G(38, 0.5f),
            StrToPAtk: G(305, 5f),
            OffhandRatio: G(306, 0.3f),
            IntToMAtk: G(307, 5f),
            LckToCrit: G(309, 0.8f),
            BaseCritLv: G(310, 3f),
            BaseCritDmg: G(311, 150f),
            LckToMCrit: G(312, 1f),
            BaseMCritLv: G(313, 3f),
            BaseMCritDmg: G(314, 150f),
            LckToHit: G(315, 1f),
            BaseHitLv: G(316, 1f),
            AgiToDodge: G(317, 1f),
            BaseDodgeLv: G(318, 5f),
            BaseHitRate: G(325, 100f),
            DefParam: G(326, 3f),
            SkillDefParam: G(327, 3f),
            AgiToParry: G(319, 0.8f),
            StrToBlock: G(320, 1f),
            AgiToAtkSpeed: G(321, 0.5f),
            AgiToCastSpeed: G(322, 1f),
            WisToHeal: G(323, 1.2f),
            WisToBeHealed: G(324, 1f),
            DefToHpDivisor: G(562, 20f),
            ExpLevelDiff: G(328, 9f),
            ExpLevelDec: G(329, 9f),
            TeamExpShareCoef: G(330, 25f),
            TeamExpNoShareCoef: G(331, 5f),
            EquipExpRate: G(496, 0.2f),
            DesDeclineUp: G(576, 17f),
            DesDeclineDown: G(577, 20f),
            DpPerKill: G(375, 25f),
            DpRollCost: G(376, 1250f),
            DpVitCapMul: G(569, 10f),
            DpPoolCapMul: G(570, 10f),
            SummonMobLifespan: G(363, 30f),
            LuckyBagFailStep: G(452, 30f),
            LuckyBagFailCap: G(453, 90f));
    }

    private static int Round(double v)
        => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    // ==================== Attaques de base ====================

    /// <summary>`CCharFormula::MeleeDamage(CCharacter)` @0x83fa430 —
    /// attaque physique de base du joueur :
    /// `flat + (100+buff%)×(strTerm×para305 + equipPAtk + weaponTerm)/100`
    /// où `strTerm = ROUND(strTot×strPct/100)+flat` clamp [0,9999] ;
    /// weaponTerm = patk arme × (100+mastery)/100, dual = off×0.3+main
    /// (mastery = table enchant native kinddata+0x304, 0 sans buff) ;
    /// clamp [0,999999]. Le patk de l'arme compte DEUX fois nativement :
    /// dans equipPAtk (Σ col45 des items à addattr, RefreshEQAttr
    /// @0x8419f00) et dans weaponTerm — comportement natif reproduit
    /// fidèlement.</summary>
    public static int MeleeDamage(CombatantSnapshot c, Params p)
    {
        var strTerm = Math.Clamp(
            Round(c.StrTotal * c.StrPct * 0.01), 0, 9999);
        var wpn = c.DualWield
            ? c.OffWeaponPAtk * p.OffhandRatio + c.WeaponPAtk
            : c.WeaponPAtk;
        var melee = c.BonusPAtk + ((100f + c.BonusPAtkRate)
            * (strTerm * p.StrToPAtk + c.EquipPAtk + wpn) * 0.01f);
        // AttrWeaponDamageRate natif (kinddata+0x340+kind) : le % par
        // kind d'arme multiplie le dégât — appliqué ici (MeleeDamage
        // alimente le P-ATK du profil ET les dégâts réels).
        return Math.Clamp(Round(melee * WeaponDamageFactor(c)),
            0, 999999);
    }

    /// <summary>Facteur `(1 + rate[kind])` de l'accumulateur
    /// `AttrWeaponDamageRate` pour l'arme principale équipée —
    /// 1.0 si le kind est hors table (0x11..0x21).</summary>
    private static double WeaponDamageFactor(CombatantSnapshot c)
        => c.WeaponKind is >= 0x11 and <= 0x21
            ? 1.0 + c.BonusWeaponDamageRatePct[c.WeaponKind - 0x11] * 0.01
            : 1.0;

    /// <summary>`CCharFormula::MagicDamage(CCharacter)` @0x83f97a0 —
    /// miroir de MeleeDamage avec INT : `flat + (100+buff)×(intTerm×
    /// para307 + equipMAtk + wpnMatk)/100` ; dual → off×para306(0.3)
    /// + main ; clamp [0,999999].</summary>
    public static int MagicDamage(CombatantSnapshot c, Params p)
    {
        var intTerm = Math.Clamp(
            Round(c.IntTotal * c.IntPct * 0.01), 0, 9999);
        var wpn = c.DualWield
            ? c.OffWeaponMAtk * p.OffhandRatio + c.WeaponMAtk
            : c.WeaponMAtk;
        var matk = c.BonusMAtk + (100f + c.BonusMAtkRate)
            * (intTerm * p.IntToMAtk + c.EquipMAtk + wpn) * 0.01f;
        return Math.Clamp(Round(matk * WeaponDamageFactor(c)),
            0, 999999);
    }

    /// <summary>`CCharFormula::PhysicalAttack(CMonster)` @0x83f8910 —
    /// `(100+buff)×col14×0.01 + flat`, clamp [0,9999999].</summary>
    public static int PhysicalAttack(CombatantSnapshot c)
        => Math.Clamp(Round(100f * c.MonsterAttack * 0.01f), 0, 9999999);

    /// <summary>Attaque "auto" selon le type d'attaquant (MeleeDamage
    /// joueur / PhysicalAttack monstre — les formules de dégâts lisent
    /// la même base).</summary>
    public static int BasePhysicalAttack(CombatantSnapshot c, Params p)
        => c.IsMonster ? PhysicalAttack(c) : MeleeDamage(c, p);

    /// <summary>Attaque magique de base (vtable+0x10c natif :
    /// MagicDamage(CCharacter) / MagicDamage(CMonster) — le monstre
    /// réutilise template+0x4c = col14 pour les deux).</summary>
    public static int BaseMagicAttack(CombatantSnapshot c, Params p)
        => c.IsMonster ? PhysicalAttack(c) : MagicDamage(c, p);

    // ==================== Défenses ====================

    /// <summary>`PhysicalDefence` — joueur @0x83f55c0 :
    /// `(equipDef(char+0x3b8) + levelDef(char+0x370 = S_Level col4) +
    /// armorEnh)×(100+buff)/100 + flat` clamp [0,99999] (armorEnh =
    /// table de renfort kinddata+0x4ac → 0 sans fortify) ; monstre
    /// @0x83f46e0 : `col18×(100+buff)×0.01 + flat`.</summary>
    public static int PhysicalDefence(CombatantSnapshot c)
        => c.IsMonster
            ? Math.Clamp(Round(c.MonsterDefence), 0, 99999)
            // Natif : (eq+lvl+armorEnh)×(100+kinddata+0xf0)×0.01
            //        + kinddata+0xec — le flat arrive après le %
            //        (accumulateurs natifs float → Bonus* en double).
            : Math.Clamp(Round((c.EquipDefence + c.LevelDefence)
                * (100.0 + c.BonusDefenceRate) * 0.01
                + c.BonusDefence), 0, 99999);

    // ==================== Ratings (échelle ×100) ====================

    /// <summary>`PhysicalHitRate` @0x83fc900 (joueur) / @0x83f8760
    /// (monstre) — joueur : `((equipHit(+0x3bc) + wpnHit +
    /// lckTerm×para315)×(1+buff))/(lvl×0.4+1) + para316) ×100`, clamp
    /// [0,30000] ; le hit d'arme (addattr+0x54 = col51) est ajouté
    /// ici ET dans l'agrégat +0x3bc (double comptage natif) ;
    /// désarmé → `CWeapon::TwoHand+0x50` (0). Mob : `(col19+flat)×
    /// (1+pct)/(lvl×0.4+1) + para316) ×100`.</summary>
    public static int PhysicalHitRate(CombatantSnapshot c, Params p)
    {
        double rate;
        if (c.IsMonster)
            rate = (c.MonsterHit / (c.Level * 0.4f + 1f) + p.BaseHitLv)
                * 100f;
        else
        {
            var lckTerm = Math.Clamp(
                Round(c.LckTotal * c.LckPct * 0.01), 0, 9999);
            var wpnHit = c.DualWield
                ? c.WeaponHit + c.OffWeaponHit * p.OffhandRatio
                : c.WeaponHit;
            // +0x114 = rating flat de buff (AttrHitRate), sommé à
            // l'équipement dans le numérateur natif ; le % AttrHitRateRate
            // (cmd 2088) multiplie le numérateur comme les crit.
            rate = ((c.EquipHit + wpnHit + c.BonusHitRating
                + lckTerm * p.LckToHit)
                * (1f + c.BonusHitRatePercent * 0.01f)
                / (c.Level * 0.4f + 1f) + p.BaseHitLv) * 100f;
        }
        return Math.Clamp(Round(rate), 0, 30000);
    }

    /// <summary>`PhysicalDodgeRate` @0x83fccd0 (joueur) /
    /// @0x83f6ff0 (monstre) — même squelette avec agiTerm×para317 +
    /// para318, clamp [0,10000].</summary>
    public static int PhysicalDodgeRate(CombatantSnapshot c, Params p)
    {
        double rate;
        if (c.IsMonster)
            rate = (c.MonsterDodge / (c.Level * 0.4f + 1f)
                + p.BaseDodgeLv) * 100f;
        else
        {
            var agiTerm = Math.Clamp(
                Round(c.AgiTotal * c.AgiPct * 0.01), 0, 9999);
            // +0x11c = rating flat de buff (AttrDodgeRate) dans le
            // numérateur ; BonusDodgePercent reste le terme additif
            // post-division des certificats (« EVA +N% »).
            rate = ((c.EquipDodge + c.BonusDodgeRating
                + agiTerm * p.AgiToDodge)
                * (1f + c.BonusDodgeRatePercent * 0.01f)
                / (c.Level * 0.4f + 1f) + p.BaseDodgeLv
                + c.BonusDodgePercent) * 100f;
        }
        return Math.Clamp(Round(rate), 0, 10000);
    }

    /// <summary>`PhysicalCriticalRate` @0x83fb370 (joueur) /
    /// @0x83f8660 (monstre) — joueur : `(equipCrit(+0x3c0) +
    /// wpnCritMast + lckTerm×para309)×(1+buff)/(lvl×0.4+1) +
    /// para310) ×100`, clamp [0,10000]. Le crit d'arme (addattr+0x58)
    /// est pondéré par la table de maîtrise kinddata+0x38c (enchant →
    /// 0) et compte déjà via EquipCrit (RefreshEQBonus ajoute col53
    /// inconditionnellement).</summary>
    public static int PhysicalCriticalRate(CombatantSnapshot c, Params p)
    {
        double rate;
        if (c.IsMonster)
            rate = (c.MonsterCrit / (c.Level * 0.4f + 1f)
                + p.BaseCritLv) * 100f;
        else
        {
            var lckTerm = Math.Clamp(
                Round(c.LckTotal * c.LckPct * 0.01), 0, 9999);
            // Natif : numérateur × (1.0 + kinddata+0x104) — accum
            // flottant AttrCriticalRate 2 (cmd 2040).
            rate = ((c.EquipCrit + c.BonusCritRating
                + lckTerm * p.LckToCrit)
                * (1f + c.BonusCritRatePercent * 0.01f)
                / (c.Level * 0.4f + 1f) + p.BaseCritLv) * 100f;
        }
        return Math.Clamp(Round(rate), 0, 10000);
    }

    /// <summary>`MagicCriticalRate` @0x83fab80 (joueur) / @0x83f45f0
    /// (monstre) — même squelette : `(equipMCrit(+0x3c4) +
    /// wpnMCritMast + lckTerm×para312)/(lvl×0.4+1) + para313) ×100`,
    /// clamp [0,10000].</summary>
    public static int MagicCriticalRate(CombatantSnapshot c, Params p)
    {
        double rate;
        if (c.IsMonster)
            rate = (c.MonsterMagicCrit / (c.Level * 0.4f + 1f)
                + p.BaseMCritLv) * 100f;
        else
        {
            var lckTerm = Math.Clamp(
                Round(c.LckTotal * c.LckPct * 0.01), 0, 9999);
            // Natif : numérateur × (1.0 + kinddata+0x110) — accum
            // AttrMagicCriticalRate 2 (cmd 2040 var1).
            rate = ((c.EquipMagicCrit + c.BonusMagicCritRating
                + lckTerm * p.LckToMCrit)
                * (1f + c.BonusMagicCritRatePercent * 0.01f)
                / (c.Level * 0.4f + 1f) + p.BaseMCritLv) * 100f;
        }
        return Math.Clamp(Round(rate), 0, 10000);
    }

    /// <summary>`PhysicalCriticalDamage` — % de dégâts critiques :
    /// `para311(150) + kinddata+0xfc(buff) + equipCritDmg` (joueur,
    /// char+0x3c2) / `para311 + kinddata+0xfc + col22` (monstre),
    /// clamp [0,300].</summary>
    public static int PhysicalCriticalDamage(CombatantSnapshot c, Params p)
    {
        var dmg = Round(p.BaseCritDmg)
            + (c.IsMonster ? c.MonsterCritDamage : c.EquipCritDamage)
            + c.BonusCritDamage;
        return Math.Clamp(Round(dmg), 0, 300);
    }

    /// <summary>`MagicCriticalDamage` — % de dégâts crit magiques :
    /// `para314(150) + kinddata+0x108(buff) + equipMCritDmg` (joueur,
    /// char+0x3c6) / `para314 + col24` (monstre), clamp [0,300].</summary>
    public static int MagicCriticalDamage(CombatantSnapshot c, Params p)
    {
        var dmg = Round(p.BaseMCritDmg)
            + (c.IsMonster
                ? c.MonsterMagicCritDamage : c.EquipMagicCritDamage)
            + c.BonusMagicCritDamage;
        return Math.Clamp(Round(dmg), 0, 300);
    }

    // ==================== Rolls ====================

    /// <summary>`CalculatePhysicalHitRate` @0x83f84d0 — taux de toucher
    /// effectif : `para325(100) + (atkHit − defDodge)/100 + ext(0)`,
    /// plancher 10 ; le caller lance `rand%100 < rate` → miss sinon.
    /// (`CalculateMagicHitRate` @0x83f8630 = même fonction — les sorts
    /// magiques utilisent la même précision.)</summary>
    public static bool RollPhysicalHit(CombatantSnapshot atk,
        CombatantSnapshot def, Params p, Random rng)
    {
        var rate = (int)p.BaseHitRate
            + (PhysicalHitRate(atk, p) - PhysicalDodgeRate(def, p)) / 100;
        if (rate < 10) rate = 10;
        return rng.Next(100) < rate;
    }

    /// <summary>`CalculatePhysicalCriticalRate` @0x83f5f40 — taux de
    /// crit effectif : `atkCrit + beCrit(kinddata+0x94, buff=0)×100`
    /// clamp [0,10000] ; le caller lance `rand%10000 < rate`.</summary>
    public static bool RollPhysicalCrit(CombatantSnapshot atk,
        CombatantSnapshot def, Params p, Random rng, int spellMod = 0)
    {
        // spellMod = GetSpellAttrModify(atk, kind3, spellId) natif —
        // ajouté ×100 au taux (CalculatePhysicalCriticalRate).
        var rate = Math.Clamp(PhysicalCriticalRate(atk, p)
            + spellMod * 100, 0, 10000);
        return rng.Next(10000) < rate;
    }

    /// <summary>`CalculateMagicCriticalRate` @0x83f5d00 — taux de crit
    /// magique effectif : `atkMCrit + beMCrit(kinddata+0x96)×100`
    /// clamp [0,10000] ; le caller lance `rand%10000 < rate`.</summary>
    public static bool RollMagicCrit(CombatantSnapshot atk,
        CombatantSnapshot def, Params p, Random rng, int spellMod = 0)
    {
        // spellMod = GetSpellAttrModify(atk, kind5, spellId) natif.
        var rate = Math.Clamp(MagicCriticalRate(atk, p)
            + spellMod * 100, 0, 10000);
        return rng.Next(10000) < rate;
    }

    /// <summary>`CalculatePhysicalCriticalDamage` @0x83f7780 — dégât
    /// après critique physique :
    /// `pct = PCritDmg(atk) + wpnMastery(0) + beCritDmg(+0x98, 0) −
    /// round(defPdef/(atkLvl^1.6+30)) + spellMod(0)` clamp [100,300],
    /// puis `dmg × pct / 100`.</summary>
    public static int ApplyPhysicalCrit(CombatantSnapshot atk,
        CombatantSnapshot def, int dmg, Params p, int spellMod = 0)
    {
        var adjust = Math.Pow(atk.Level, 1.6) + 30.0;
        var reduce = Math.Clamp(
            PhysicalDefence(def) / (float)adjust, 0f, 70f);
        // spellMod = GetSpellAttrModify(atk, kind4, spellId) natif —
        // ajouté au pct avant clamp (CalculatePhysicalCriticalDamage
        // @0x83f7780 : `spell_modify` dans la somme).
        var pct = Math.Clamp(
            PhysicalCriticalDamage(atk, p) - Round(reduce) + spellMod,
            100, 300);
        return Math.Max(0, dmg * pct / 100);
    }

    /// <summary>`CalculateMagicCriticalDamage` @0x83f5e20 — dégât après
    /// critique magique : `pct = MCritDmg(atk) + beMCritDmg(+0x9a, 0) +
    /// spellMod(0)` clamp [100,300], puis `dmg × pct / 100` (pas de
    /// réduction par la défense — contrairement au physique).</summary>
    public static int ApplyMagicCrit(CombatantSnapshot atk,
        CombatantSnapshot def, int dmg, Params p, int spellMod = 0)
    {
        // spellMod = GetSpellAttrModify(atk, kind6, spellId) natif
        // (@0x83f5e20 : ajouté au pct, clamp [100,300]).
        var pct = Math.Clamp(MagicCriticalDamage(atk, p) + spellMod,
            100, 300);
        return Math.Max(0, dmg * pct / 100);
    }

    // ==================== Dégâts ====================

    /// <summary>Offset de niveau du natif : joueur et elf pet (type 7 /
    /// 0x50b) → `clamp(Δlvl, −10, 10)` ; monstre normal (type 0xb) →
    /// `clamp(Δlvl×5, 0, 500)` (CalculatePhysicalDamage 0x83fd8c0 /
    /// 0x83fdec0, CalculatePhysicalSkillDamage 0x83fd470,
    /// CalculateMagicSkillDamage 0x83fd190).</summary>
    private static int LevelOffset(CombatantSnapshot atk,
        CombatantSnapshot def)
    {
        var d = atk.Level - def.Level;
        return atk.IsMonster
            ? Math.Clamp(d * 5, 0, 500)
            : Math.Clamp(d, -10, 10);
    }

    /// <summary>Résistance du défenseur à l'élément d'attaque —
    /// `AttrResist` (vtable+0xe8) = `resists[element−1]` pour
    /// element∈[1,9] (SResistAttribute::GetResist @0x83fe6c0).</summary>
    private static int Resist(CombatantSnapshot def, int element)
    {
        if (element < 1 || element > 9) return 0;
        var r = def.IsMonster ? def.MonsterResists : def.EquipResists;
        var base_ = element - 1 < r.Length ? r[element - 1] : 0;
        // kinddata+0x00..0x11 : AttrResist des statuts temporaires,
        // sommé à l'équipement (SResistAttribute::AddResist).
        return base_ + (def.IsMonster ? 0
            : def.BonusResists[element - 1]);
    }

    /// <summary>Roll de variance natif : `rand mod (var×10000) × 1e-4
    /// ×2` → uniforme [0, 2var) ; le multiplicateur effectif est
    /// `roll + 100 − var` ∈ [100−var, 100+var).</summary>
    private static float VarianceRoll(int variance, Random rng)
        => variance <= 0 ? 0f
            : rng.Next(variance * 10000) * 0.0001f * 2f;

    /// <summary>`CalculatePhysicalDamage` @0x83fd8c0 (joueur) /
    /// @0x83fdec0 (monstre) — dégât d'auto-attaque physique :
    /// `dmg = (2×offset + 100 − defTerm) × (100−res)×1e-5 × phys` où
    /// `phys = attack×0.01×(roll + 100 − var)` et
    /// `defTerm = clamp(def.pdef×para326/adjust, 0, 70)`,
    /// `adjust = atkLvl^1.6+30` ; dégât <5 → rand%4+1.</summary>
    public static int CalculatePhysicalDamage(CombatantSnapshot atk,
        CombatantSnapshot def, Params p, Random rng)
    {
        var adjust = Math.Pow(atk.Level, 1.6) + 30.0;
        var offset = LevelOffset(atk, def);
        var variance = atk.IsMonster
            ? atk.MonsterVariance
            : atk.DualWield
                ? (int)(atk.OffWeaponVariance * p.OffhandRatio
                    + atk.WeaponVariance)
                : atk.WeaponVariance;
        var element = atk.IsMonster ? atk.MonsterElement : atk.WeaponElement;
        var resist = element != 0
            ? Math.Clamp(Resist(def, element), -70f, 70f) : 0f;
        var defTerm = Math.Clamp(
            PhysicalDefence(def) * p.DefParam / (float)adjust, 0f, 70f);
        var roll = VarianceRoll(variance, rng);
        var phys = BasePhysicalAttack(atk, p) * 0.01f
            * (roll + 100f - variance);
        var dmg = Round((2f * offset + 100f - defTerm)
            * (100f - resist) * 1e-5f * phys);
        return dmg < 5 ? rng.Next(4) + 1 : dmg;
    }

    /// <summary>`CCharacter::CanBlock` @0x8409110 — le blocage exige
    /// un bouclier en main secondaire : item du slot off-hand de kind
    /// <b>0x22</b> (les autres kinds ne déclenchent jamais le roll).</summary>
    public static bool CanBlock(CombatantSnapshot def)
        => !def.IsMonster && def.OffWeaponKind == 0x22;

    /// <summary>`CCharacter::CanParry` @0x8409160 — la parade exige
    /// une arme à deux mains : kind main ∈ {0x14..0x17, 0x1b..0x21}
    /// (2H mêlée, arcs, fusils, bâtons 2H). Une 1H+off-hand ne pare
    /// jamais — comportement natif vérifié par la liste de kinds.</summary>
    public static bool CanParry(CombatantSnapshot def)
        => !def.IsMonster && def.WeaponKind is
            >= 0x14 and <= 0x17 or >= 0x1b and <= 0x21;

    /// <summary>`CCharFormula::BlockDamageRate` @0x83fa270 — taux de
    /// blocage du défenseur : `bonusEquipé(+0x4de) +
    /// strTerm×100/((atkLvl+9)×para320)` — strTerm = STR totale×pct
    /// (param_2+0x394/+0x354 × kinddata+0x178 + char+0x338). Le natif
    /// cape à 100 ; le serveur applique `GameBalance.BlockRateCapPct`
    /// (custom 50% — spec live). Retour en échelle %.</summary>
    public static float BlockDamageRate(CombatantSnapshot def,
        int attackerLevel, Params p)
    {
        var strTerm = StatTerm(def.StrTotal, def.StrPct);
        // kinddata+0x154 = AttrBlock des statuts temporaires.
        var rate = def.EquipBlockRate + def.BonusBlockRate
            + strTerm * 100.0 / ((attackerLevel + 9f) * p.StrToBlock);
        return (float)Math.Clamp(rate, 0.0,
            (double)Eden.Core.Game.GameBalance.BlockRateCapPct);
    }

    /// <summary>`CCharFormula::BlockRate` @0x83f9d00 — taux de blocage
    /// <b>affiché</b> (s16 #2 du 296, vfunc GetBlockRate+0x184) :
    /// `ROUND((equipBlock(+0x3d2) + kinddata+0x154 + strTerm×para320)
    /// / (level×0.4+1) + shield(+0x56c)) × 100`, clamp [0,5000] (50%).
    /// Formule DISTINCTE du roll `BlockDamageRate` (qui divise par
    /// (atkLvl+9)×para320). Le bonus bouclier +0x56c (AttrShieldBlock,
    /// cmd 2108) est rabattu sur <see cref="CombatantSnapshot
    /// .BonusBlockRate"/> ; monstre → 0 (pas de blocage affiché).</summary>
    public static int BlockRate(CombatantSnapshot c, Params p)
    {
        if (c.IsMonster) return 0;
        var strTerm = StatTerm(c.StrTotal, c.StrPct);
        var rate = (c.EquipBlockRate + c.BonusBlockRate
            + strTerm * p.StrToBlock) / (c.Level * 0.4f + 1f) * 100.0;
        return Math.Clamp(Round(rate), 0, 5000);
    }

    /// <summary>`CalculateBlockDamage` @0x83fc780 — roll natif
    /// `rand%10000 &lt; rate×100` → dégât réduit à **0** (blocage
    /// intégral natif, flag 0x10).</summary>
    public static bool RollBlock(CombatantSnapshot def,
        int attackerLevel, Params p, Random rng)
        => rng.Next(10000) < BlockDamageRate(def, attackerLevel, p)
            * 100f;

    /// <summary>`CalculateParryDamage` @0x83fc840 — roll natif
    /// `rand%10000 &lt; ParryRate` (déjà en échelle ×100, cap 7000 =
    /// 70%) → dégât divisé par 2, flag 0x100.</summary>
    public static bool RollParry(CombatantSnapshot def, Params p,
        Random rng)
        => rng.Next(10000) < ParryRate(def, p);

    /// <summary>Applique la chaîne block→parry du caller natif
    /// (character.cc @0x1a42 + ISpell::HitPhysicalSkill @0x84f1b30) :
    /// CanBlock → roll → dmg=0 + 0x10 ; CanParry && dmg&gt;0 → roll →
    /// dmg/2 + 0x100. La spec serveur étend la parade aux dégâts
    /// magiques (« qu'importe le type ça divise par deux ») — le natif
    /// la limite aux coups physiques.</summary>
    private static (int Dmg, int Flags) ApplyBlockParry(
        CombatantSnapshot def, int dmg, int attackerLevel, Params p,
        Random rng, bool allowParry)
    {
        var flags = 0;
        if (CanBlock(def) && RollBlock(def, attackerLevel, p, rng))
        { dmg = 0; flags |= PhysicalResult.FlagBlock; }
        if (allowParry && CanParry(def) && dmg > 0 &&
            RollParry(def, p, rng))
        { dmg /= 2; flags |= PhysicalResult.FlagParry; }
        return (dmg, flags);
    }

    /// <summary>Attaque physique complète (roll hit → roll crit →
    /// dégâts) — ordre du caller natif @0x83c6xxx : miss → type 8
    /// dmg 0 ; crit → type 2, dégât ×PhysicalCriticalDamage ;
    /// puis block (0x10 → dmg 0) / parry (0x100 → dmg/2) si le
    /// défenseur est un joueur équipé (CanBlock/CanParry) ;
    /// sinon type 1.</summary>
    public static PhysicalResult ResolvePhysicalAttack(
        CombatantSnapshot atk, CombatantSnapshot def, Params p,
        Random rng)
    {
        if (!RollPhysicalHit(atk, def, p, rng))
            return new PhysicalResult(0, PhysicalOutcome.Miss,
                PhysicalResult.FlagMiss);
        var dmg = CalculatePhysicalDamage(atk, def, p, rng);
        var flags = 1;
        if (RollPhysicalCrit(atk, def, p, rng))
        {
            dmg = ApplyPhysicalCrit(atk, def, dmg, p);
            flags |= 2;
        }
        var (d2, f2) = ApplyBlockParry(def, dmg, atk.Level, p, rng,
            allowParry: true);
        return new PhysicalResult(d2,
            (flags & 2) != 0 ? PhysicalOutcome.Crit
                : PhysicalOutcome.Hit, flags | f2);
    }

    /// <summary>`CalculatePhysicalSkillDamage` @0x83fd470 — dégât de
    /// skill physique : `ROUND((meleeBase + power×refPhys×0.01)×1e-7
    /// ×(roll+100−var)×(100+2×offset−defTerm)×(100−res))` ;
    /// `defTerm` utilise para327 ; élément = arme+0x128 (joueur) ou
    /// template+0x70 (mob type 0xb), résist facteur natif (resist>70→30,
    /// ≤−70→170 — équivaut à clamp ±70) ; <6 → rand%4+1.
    /// `meleeBase` = param5 du caller = MeleeDamage/PhysicalAttack.</summary>
    public static int CalculatePhysicalSkillDamage(CombatantSnapshot atk,
        CombatantSnapshot def, int spellPower, int variance,
        float refCoef, Params p, Random rng)
    {
        var adjust = Math.Pow(atk.Level, 1.6) + 30.0;
        var offset = LevelOffset(atk, def);
        var defTerm = Math.Clamp(
            PhysicalDefence(def) * p.SkillDefParam / (float)adjust,
            0f, 70f);
        var element = atk.IsMonster ? atk.MonsterElement : atk.WeaponElement;
        var resist = element != 0
            ? Math.Clamp(Resist(def, element), -70f, 70f) : 0f;
        var roll = VarianceRoll(variance, rng);
        var dmg = Round(
            (BasePhysicalAttack(atk, p) + spellPower * refCoef) * 1e-7f
            * (roll + 100f - variance)
            * (100f + 2f * offset - defTerm)
            * (100f - resist));
        return dmg < 6 ? rng.Next(4) + 1 : dmg;
    }

    /// <summary>`CalculateMagicSkillDamage` @0x83fd190 — dégât de skill
    /// magique : `ROUND((2×offset+100)×1e-5×skill×(100−res))` où
    /// `skill = (matk + power×refMag×0.01)×0.01×(roll−var+100)` et
    /// l'élément vient du sort (SSpellData+0xbe = col33, ou le buff
    /// kinddata+0x15c = 0) — pas de terme de def physique ;
    /// résist clampée ±70 ; <5 → rand%4+1. (Le post-traitement
    /// SpellDamage — enchant attr modify — est 0 sans buff.)</summary>
    public static int CalculateMagicSkillDamage(CombatantSnapshot atk,
        CombatantSnapshot def, int spellPower, int variance, int element,
        float refCoef, Params p, Random rng)
    {
        var offset = LevelOffset(atk, def);
        var resist = Math.Clamp(Resist(def, element), -70f, 70f);
        var roll = VarianceRoll(variance, rng);
        var skill = (BaseMagicAttack(atk, p) + spellPower * refCoef)
            * 0.01f * (roll - variance + 100f);
        var dmg = Round((2f * offset + 100f) * 1e-5f * skill
            * (100f - resist));
        return dmg < 5 ? rng.Next(4) + 1 : dmg;
    }

    /// <summary>`CalculateMagicalDamage` @0x83fd060 — auto-attaque
    /// magique : `matk × ROUND(100 − mdef×para38/(atkLvl+9))/100` ;
    /// la def magique native est toujours 0 (template+0x60 jamais écrit
    /// et CCharacter::GetMagicDefence = 0) → dégât = matk ; <11 →
    /// rand%8+2.</summary>
    public static int CalculateMagicalDamage(CombatantSnapshot atk,
        CombatantSnapshot def, Params p, Random rng)
    {
        var dmg = BaseMagicAttack(atk, p) * Round(100f) / 100;
        return dmg < 11 ? rng.Next(8) + 2 : dmg;
    }

    // ==================== Getters d'affichage (296) ====================

    /// <summary>Terme de stat natif `(equip+base)×pct/100` clampé
    /// [0,9999] — partagé par toutes les formules de niveau (HitLv,
    /// MCriLv, ParryLv, AttackSpeedLevel — désasm : ROUND((+0x39x+
    /// +0x36x)×(kinddata+buffPct + classPct)×0.01) + flat).</summary>
    private static int StatTerm(int total, int pct)
        => Math.Clamp(Round(total * pct * 0.01), 0, 9999);

    /// <summary>`CCharFormula::ParryLv` @0x83fb9b0 — niveau de parade :
    /// `equipParry(char+0x3d0=0) + buff(0) + agiTerm×para319`.</summary>
    /// <summary>`CCharFormula::ParryLv` — `equipParry(+0x3d0) +
    /// buff(0) + agiTerm×para319` (le terme équipement inclut la Mana
    /// Infusion type 11).</summary>
    public static float ParryLv(CombatantSnapshot c, Params p)
        => (float)(c.EquipParry + c.BonusParry
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToParry);

    /// <summary>`CCharFormula::ParryRate` @0x83fc240 — taux de parade :
    /// `ROUND((ParryLv + masteryArme(0))/(lvl×0.4+1) ×100)`, cap 7000
    /// (0x1b59).</summary>
    public static int ParryRate(CombatantSnapshot c, Params p)
        => Math.Clamp(
            Round(ParryLv(c, p) / (c.Level * 0.4f + 1f) * 100f),
            0, 7000);

    /// <summary>`CCharFormula::MCriLv` @0x83fb130 — niveau de critique
    /// magique affiché : `(equipMCrit(+0x3c4) + buff(+0x10c) +
    /// lckTerm×para312) × (1+buffPct(+0x110))` — le numérateur de
    /// MagicCriticalRate, affiché en f32 dans le 493.</summary>
    public static float MCriLv(CombatantSnapshot c, Params p)
        => (float)((c.EquipMagicCrit + c.BonusMagicCritRating
            + StatTerm(c.LckTotal, c.LckPct) * p.LckToMCrit)
            * (1.0 + c.BonusMagicCritRatePercent * 0.01));

    /// <summary>`CCharFormula::CriLv` @0x83fb2a0 — niveau de critique
    /// physique affiché : `(equipCrit(+0x3c0) + buff(+0x100, cmd 2048
    /// mode 0) + lckTerm×para309) × (1+buffPct(+0x104, cmd 2040))` —
    /// le « taux de crit » flat de la fiche (493 field2) ; le proc
    /// `2048|360` DOIT le faire monter.</summary>
    public static float CriLv(CombatantSnapshot c, Params p)
        => (float)((c.EquipCrit + c.BonusCritRating
            + StatTerm(c.LckTotal, c.LckPct) * p.LckToCrit)
            * (1.0 + c.BonusCritRatePercent * 0.01));

    /// <summary>`CCharFormula::HitLv` @0x83fa970 — `(equipHit(+0x3bc)
    /// + hit d'arme + buff(+0x114) + lckTerm×para315) × (1+buffPct
    /// (+0x118))` — numérateur de PhysicalHitRate, field1 du 493.</summary>
    public static float HitLv(CombatantSnapshot c, Params p)
    {
        var wpnHit = c.DualWield
            ? c.WeaponHit + c.OffWeaponHit * p.OffhandRatio
            : c.WeaponHit;
        return (float)((c.EquipHit + wpnHit + c.BonusHitRating
            + StatTerm(c.LckTotal, c.LckPct) * p.LckToHit)
            * (1.0 + c.BonusHitRatePercent * 0.01));
    }

    /// <summary>`CCharFormula::DodgeLv` @0x83fcf70 — `(equipDodge
    /// (+0x3be) + buff(+0x11c) + agiTerm×para317) × (1+buffPct
    /// (+0x120))` — field4 du 493.</summary>
    public static float DodgeLv(CombatantSnapshot c, Params p)
        => (float)((c.EquipDodge + c.BonusDodgeRating
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToDodge)
            * (1.0 + c.BonusDodgeRatePercent * 0.01));

    /// <summary>`CCharFormula::BlockLv` @0x83fa120 — `(equipBlock
    /// (+0x3d2) + buff(+0x154) + strTerm×para320)` — PAS de facteur %
    /// natif — field6 du 493.</summary>
    public static float BlockLv(CombatantSnapshot c, Params p)
        => (float)(c.EquipBlockRate + c.BonusBlockRate
            + StatTerm(c.StrTotal, c.StrPct) * p.StrToBlock);

    /// <summary>`CCharFormula::AttackSpeedLevel` @0x83fbed0 —
    /// `(kinddata+0x124 buff + equipAtkSpd(+0x3c8) + agiTerm×para321)
    /// × (1+buffPct(+0x12c))` — numérateur d'AttackSpeedLevelRate,
    /// field13 du 493.</summary>
    public static float AttackSpeedLv(CombatantSnapshot c, Params p)
        => (float)((c.EquipAttackSpeed + c.BonusAttackSpeed
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToAtkSpeed)
            * (1.0 + c.BonusAttackSpeedPercent * 0.01));

    /// <summary>`CCharFormula::MagicSpeedLevel` @0x83fbc30 —
    /// `(equipCastSpd(+0x3ca) + buff(+0x134) + agiTerm×para322)
    /// × (1+buffPct(+0x13c))` — field14 du 493.</summary>
    public static float MagicSpeedLv(CombatantSnapshot c, Params p)
        => (float)((c.EquipCastSpeed + c.BonusCastSpeed
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToCastSpeed)
            * (1.0 + c.BonusCastSpeedPercent * 0.01));

    /// <summary>Variantes « Base* » des paliers — le terme de stat
    /// SEUL × para (sans équipement ni buffs) : BaseHitLv @0x83fa8d0,
    /// BaseCriLv @0x83fb200, BaseMCriLv @0x83fb090, BaseDodgeLv
    /// @0x83fde00, BaseParryLv @0x83fba60, BaseBlockLv @0x83fa1d0.</summary>
    public static float BaseHitLv(CombatantSnapshot c, Params p)
        => StatTerm(c.LckTotal, c.LckPct) * p.LckToHit;
    /// <summary>BaseCriLv — `lckTerm×para309`.</summary>
    public static float BaseCriLv(CombatantSnapshot c, Params p)
        => StatTerm(c.LckTotal, c.LckPct) * p.LckToCrit;
    /// <summary>BaseMCriLv — `lckTerm×para312`.</summary>
    public static float BaseMCriLv(CombatantSnapshot c, Params p)
        => StatTerm(c.LckTotal, c.LckPct) * p.LckToMCrit;
    /// <summary>BaseDodgeLv — `agiTerm×para317`.</summary>
    public static float BaseDodgeLv(CombatantSnapshot c, Params p)
        => StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToDodge;
    /// <summary>BaseParryLv — `agiTerm×para319`.</summary>
    public static float BaseParryLv(CombatantSnapshot c, Params p)
        => StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToParry;
    /// <summary>BaseBlockLv — `strTerm×para320`.</summary>
    public static float BaseBlockLv(CombatantSnapshot c, Params p)
        => StatTerm(c.StrTotal, c.StrPct) * p.StrToBlock;

    /// <summary>`CCharFormula::AttackSpeedLevelRate` @0x83fbed0 /
    /// `AttackSpeedLevel` — `level = equipAtkSpeed(+0x3c8) + buff(0) +
    /// masteryArme(0) + agiTerm×para322`, rate = `level/(lvl×0.4+1)`.
    /// </summary>
    public static float AttackSpeedLevelRate(CombatantSnapshot c,
        Params p)
        // Natif AttackSpeedLevel : (agiTerm×para + equip + flat
        // kinddata+0x124) × (1.0 + kinddata+0x12c) — le facteur %
        // vient de cmd 2068 `AttrAttackSpeedPlus 2`.
        => (float)((c.EquipAttackSpeed + c.BonusAttackSpeed
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToAtkSpeed)
            * (1.0 + c.BonusAttackSpeedPercent * 0.01)
            / (c.Level * 0.4 + 1));

    /// <summary>`CCharFormula::Movement(CCharacter)` @0x83f53b0 —
    /// vitesse de marche : `moveSpeedBuff(1.0) × eMoveSpeed100(18.85)
    /// × 0.5` = 9.425 sans buff (DAT_0888b6d0/0888b690).</summary>
    public const float BaseMovement = 18.85f * 0.5f;

    /// <summary>TEMP (tests) : multiplicateur de vitesse joueur ×3
    /// (300 %). Garder égal à PlayerController.TempMoveSpeedMul côté
    /// client. 1f pour retirer.</summary>
    public static float TempMoveSpeedMul =>
        Eden.Core.Game.ServerConfig.MoveSpeedBaseMul;

    /// <summary>`Movement(CCharacter)` custom serveur — natif :
    /// `buff × 18.85 × 0.5` avec buff = kinddata+0xa0 clampé
    /// DAT_087b273c (200%). Custom (spec live) : le bonus % inclut
    /// `agiTerm × GameBalance.AgiToMoveSpeed` (0.08%/AGI, inexistant
    /// natif) et le cap est `GameBalance.MoveSpeedCapPct` (300%).
    /// `MoveSpeedBonusPct` du snapshot = bonus actifs (monture,
    /// enchant 2051 AttrMovement).</summary>
    public static float Movement(CombatantSnapshot c)
    {
        // Buffs/talents/AGI : sommés puis plafonnés (MoveSpeedCapPct).
        var bonusPct = c.BonusMoveSpeedPct
            + StatTerm(c.AgiTotal, c.AgiPct)
                * Eden.Core.Game.GameBalance.AgiToMoveSpeed;
        var buff = Math.Min(1.0 + bonusPct / 100.0,
            Eden.Core.Game.GameBalance.MoveSpeedCapPct / 100.0);
        // Monture : facteur PROPRE, appliqué après le plafond — un buff
        // de vitesse (dev +200 %, talents) saturant le cap ne doit pas
        // annuler le bonus de la monture (bug « la monture ne donne pas
        // de move speed »).
        var ride = 1.0 + c.MoveSpeedBonusPct / 100.0;
        return (float)(BaseMovement * buff * ride) * TempMoveSpeedMul;
    }

    /// <summary>`MovementRate` @0x83f3dxx — bonus de vitesse affiché
    /// en % : `(multiplicateur effectif − 1) × 100` (natif :
    /// `(kinddata+0xa0 clampé − 1.0) × 100`).</summary>
    public static float MovementRate(CombatantSnapshot c)
        => Movement(c) / TempMoveSpeedMul / BaseMovement * 100f - 100f;

    /// <summary>`CastSpeedLevelRate` — miroir de
    /// <see cref="AttackSpeedLevelRate"/> avec para322 (AGI→cast) :
    /// `(equipCastSpd + agiTerm×para322)/(lvl×0.4+1)` — utilisé par le
    /// calcul de durée de cast (réduit le cast time).</summary>
    public static float CastSpeedLevelRate(CombatantSnapshot c,
        Params p)
        // Natif MagicSpeedLevel : numérateur × (1.0 + kinddata+0x13c)
        // — accum du mode 2 de `AttrCastTimePlus` (cmd 2069).
        => (float)((c.EquipCastSpeed + c.BonusCastSpeed
            + StatTerm(c.AgiTotal, c.AgiPct) * p.AgiToCastSpeed)
            * (1.0 + c.BonusCastSpeedPercent * 0.01)
            / (c.Level * 0.4 + 1));

    /// <summary>`CCharFormula::DefenceReduce` @0x83f6180 — réduction
    /// de dégâts affichée : `pdef×para326/(lvl^1.6+30)` clamp [0,70]
    /// (le même terme `defTerm` des formules de dégâts).</summary>
    public static float DefenceReduce(CombatantSnapshot c, Params p)
        => Math.Clamp(PhysicalDefence(c) * p.DefParam
            / (float)(Math.Pow(c.Level, 1.6) + 30.0), 0f, 70f);

    /// <summary>`CCharFormula::CriDmgReduce` @0x83f6100 — réduction des
    /// dégâts critiques subis : `pdef/(lvl^1.6+30)` clamp [0,70]
    /// (sans para326 — la formule lit la pdef brute).</summary>
    public static float CriDmgReduce(CombatantSnapshot c)
        => Math.Clamp(PhysicalDefence(c)
            / (float)(Math.Pow(c.Level, 1.6) + 30.0), 0f, 70f);

    // ==================== Dispatch de sorts ====================

    /// <summary>`SSpellData::Init` @0x84eecf0 — calcul du flag mot bas
    /// `+0x94` depuis le type (+0xc = S_Spell col17) et l'élément
    /// (+0xbe = col33) : types {6,7}→0x0c, {4,5}→0x44, {2,3}→0x24,
    /// autres→0x04 ; puis élément==1 → inchangé, élément∈[4,9] → variante
    /// uVar4, sinon → uVar3.</summary>
    private static int SpellFlags(int type, int element)
    {
        int u3, u5, u4;
        if (type < 8)
        {
            var bit = 1 << (type & 0x1f);
            if ((bit & 0xc0) != 0) { u3 = 8; u5 = 0x0c; u4 = 8; }
            else if ((bit & 0x30) != 0) { u3 = 0x40; u5 = 0x44; u4 = 0x48; }
            else if ((bit & 0x0c) != 0) { u3 = 0x20; u5 = 0x24; u4 = 0x28; }
            else { u3 = 0; u5 = 4; u4 = 8; }
        }
        else { u3 = 0; u5 = 4; u4 = 8; }
        if (element != 1)
        {
            if (element < 1) u5 = u3;
            else
            {
                u5 = u4;
                if ((ushort)(element - 4) > 5) u5 = u3;
            }
        }
        return u5;
    }

    /// <summary>Dispatch du caller de cast (@0x84ef9xx, désasm) :
    /// `(flags & 0x68) != 0 && type ∉ {4,5}` → HitMagicSkill ;
    /// `(flags & 4) != 0` → HitPhysicalSkill ; type∈{4,5} →
    /// HitRestoreSkill ; sinon rien. Attention au sens natif :
    /// les skills de classe (type 2/3/6/7) passent TOUS par la formule
    /// « magique » (base M-ATK + power×coef) — la résistance physique
    /// (resist[0]) est sélectionnée par l'élément 1 ; seules les
    /// attaques de type 0/1 à élément 1 et les sorts mob type 8
    /// élément 1 prennent le canal « physique » (base mêlée).</summary>
    public static SpellHitKind SpellHitKindOf(SpellParser.SpellDef s)
    {
        var flags = SpellFlags(s.Type, s.AttackElement);
        var restore = s.Type is 4 or 5;
        if ((flags & 0x68) != 0 && !restore) return SpellHitKind.Magic;
        if ((flags & 4) != 0) return SpellHitKind.Physical;
        return restore ? SpellHitKind.Restore : SpellHitKind.None;
    }

    /// <summary>Coefficient de niveau d'un sort — `SSpellReference`
    /// (col2 physique / col3 magique du palier) lu comme pourcentage :
    /// le décodage natif `(hi16×65536 + lo16) × 0.01` reforme l'u32
    /// brut ÷100 → `power × col / 100`.</summary>
    public static float SpellCoef(int percentColumn)
        => percentColumn * 0.01f;

    /// <summary>Résolution complète d'un sort offensif (joueur ou mob) —
    /// pipeline `HitPhysicalSkill`/`HitMagicSkill` du natif : hit roll
    /// (même formule pour les deux familles — CalculateMagicHitRate
    /// délègue à CalculatePhysicalHitRate) → roll crit (physique ou
    /// magique selon le canal) → dégâts ; miss → dmg 0.</summary>
    public static PhysicalResult ResolveSpell(CombatantSnapshot atk,
        CombatantSnapshot def, SpellParser.SpellDef spell,
        int spellLevelCoef, Params p, Random rng,
        Func<int, int>? spellAttr = null)
    {
        // spellAttr = GetSpellAttrModify(atk, kind, spellId) natif —
        // kinds 3/4 (phys) ou 5/6 (magic) consommés par le canal.
        int Mod(int k) => spellAttr?.Invoke(k) ?? 0;
        var kind = SpellHitKindOf(spell);
        if (kind is SpellHitKind.None or SpellHitKind.Restore)
            return new PhysicalResult(0, PhysicalOutcome.Hit, 1);
        if (!RollPhysicalHit(atk, def, p, rng))
            return new PhysicalResult(0, PhysicalOutcome.Miss,
                PhysicalResult.FlagMiss);
        var coef = SpellCoef(spellLevelCoef);
        int dmg, flags;
        if (kind == SpellHitKind.Physical)
        {
            dmg = CalculatePhysicalSkillDamage(atk, def,
                spell.DamagePower, spell.DamageVariance, coef, p, rng);
            flags = 1;
            if (RollPhysicalCrit(atk, def, p, rng, Mod(3)))
            { dmg = ApplyPhysicalCrit(atk, def, dmg, p, Mod(4));
              flags |= 2; }
            // HitPhysicalSkill natif : block ET parry sur le canal
            // physique (caller @0x84f1b30).
            var (d2, f2) = ApplyBlockParry(def, dmg, atk.Level, p, rng,
                allowParry: true);
            return new PhysicalResult(d2,
                (flags & 2) != 0 ? PhysicalOutcome.Crit
                    : PhysicalOutcome.Hit, flags | f2);
        }
        else
        {
            dmg = CalculateMagicSkillDamage(atk, def,
                spell.DamagePower, spell.DamageVariance,
                spell.AttackElement, coef, p, rng);
            flags = 1;
            if (RollMagicCrit(atk, def, p, rng, Mod(5)))
            { dmg = ApplyMagicCrit(atk, def, dmg, p, Mod(6));
              flags |= 2; }
            // HitMagicSkill natif : block seul — la spec serveur
            // autorise AUSSI la parade sur les dégâts magiques
            // (« qu'importe le type ça divise par deux »).
            var (d2, f2) = ApplyBlockParry(def, dmg, atk.Level, p, rng,
                allowParry: true);
            return new PhysicalResult(d2,
                (flags & 2) != 0 ? PhysicalOutcome.Crit
                    : PhysicalOutcome.Hit, flags | f2);
        }
    }

    /// <summary>Résolution complète d'un sort de restauration —
    /// pipeline natif `HitRestoreSkill` @0x84f1f40 +
    /// `CalculateMagicSkillHeal` @0x83f9310 :
    /// <code>base = ROUND(healPower(col36) × healCoef% / 100)</code>
    /// <code>r    = rand%(variance(col37)×10000) × 0.0001 ∈ [0,var)</code>
    /// <code>heal = ROUND((2r + 100 − variance) × base × 0.01)</code>
    /// puis modificateurs `Heal(caster)`/`BeHeal(target)` — enchant
    /// G/P-Healing, ≈identité tant que ces enchants ne sont pas
    /// modélisés (fields kinddata+0x144..0x150 natifs absents) — et
    /// `SpellDamage` (modif par sort, identité par défaut).
    /// Roll crit magique (`CalculateMagicCriticalRate` +
    /// `CalculateMagicCriticalDamage`, clamp [100,300]%) — les soins
    /// CRITTENT nativement et le client les affiche en vert (flag
    /// 0x200 du 281).</summary>
    /// <param name="healCoef">`SSpellReference+0x10` (col4) du palier.</param>
    public static SpellHealResult ResolveSpellHeal(CombatantSnapshot atk,
        CombatantSnapshot def, SpellParser.SpellDef spell,
        int healCoef, Params p, Random rng)
    {
        var power = spell.HealPower;
        if (power <= 0) return default;
        // base = ROUND(power × coef/100) — le natif ROUND avant les
        // termes de variance et avant Heal()/BeHeal().
        var baseHeal = (double)Math.Round(power * SpellCoef(healCoef));
        // Heal(caster) @0x83f9210 — AVANT la variance : base ×
        // (100 + G-Heal% + wisTerm×para323/(lvl×0.4+1))/100 + flat.
        if (!atk.IsMonster)
            baseHeal = baseHeal * (100f
                + StatTerm(atk.WisTotal, atk.WisPct) * p.WisToHeal
                    / (atk.Level * 0.4f + 1f)
                + atk.BonusHealGivenPct) * 0.01f + atk.EquipHeal
                + atk.BonusHealGivenFlat;
        // Variance centrée natif : rand%(var×10000)×0.0001 → 2r − var
        // → multiplicateur ∈ ]1−var/100, 1+var/100[ (0 si var=0).
        var varPct = spell.DamageVariance;
        double r2 = 0;
        if (varPct > 0)
            r2 = rng.Next(varPct * 10000) * 0.0001 * 2.0;
        var heal = Math.Max(0,
            (int)Math.Round((r2 + 100.0 - varPct) * baseHeal * 0.01));
        // Critique de soin natif : rand%10000 < rate →
        // CalculateMagicCriticalDamage sur le soin.
        var crit = RollMagicCrit(atk, def, p, rng);
        if (crit)
            heal = ApplyMagicCrit(atk, def, heal, p);
        // BeHeal(target) @0x83f9110 — APRÈS le crit (ordre natif du
        // caller CalculateMagicSkillHeal : Heal→variance→crit→BeHeal)
        // : heal × (100 + BeHeal% + wisTerm(def)×para324/(lvl×0.4+1))
        // /100 + flat.
        if (!def.IsMonster)
            heal = Math.Max(0, (int)Math.Round(heal * (100f
                + StatTerm(def.WisTotal, def.WisPct) * p.WisToBeHealed
                    / (def.Level * 0.4f + 1f)
                + def.BonusHealTakenPct) * 0.01f)
                + def.EquipBeHealed + (int)def.BonusHealTakenFlat);
        return new SpellHealResult(heal, crit);
    }

    /// <summary>Mappe le résultat d'un sort vers le champ flags du 281
    /// `ClientSpellHitToLife` (5e champ i32 — voir
    /// <see cref="SpellHitFlags"/>) : miss→8, hit→1, crit→1|0x200000,
    /// puis les bits défense 0x10 (block) / 0x100 (parry) conservés du
    /// `PhysicalResult.Flags` natif. Kind None/Restore hors dégâts :
    /// le natif laisse la valeur initiale (1) — le cas Restore passe
    /// par <see cref="ResolveSpellHeal"/> qui fournit 0x200|crit.</summary>
    public static int SpellHitFlagsOf(PhysicalResult r) =>
        (r.Flags & (PhysicalResult.FlagBlock
            | PhysicalResult.FlagParry)) | (r.Outcome switch
        {
            PhysicalOutcome.Miss => SpellHitFlags.Miss,
            PhysicalOutcome.Crit => SpellHitFlags.Hit | SpellHitFlags.Crit,
            _ => SpellHitFlags.Hit,
        });

    // ==================== Exp de groupe ====================

    /// <summary>`CCharFormula::ExpDeclineRate` @0x083f5150 — malus
    /// d'exp selon l'écart niveau perso/mob. Renvoie un facteur
    /// [0,1] ; 1.0 à niveau égal. `lv_param` borné [0,100] : au-delà
    /// de ~±16 niveaux d'écart l'exp tombe à 0. Branche « mob plus
    /// fort » du décompilé : littéral −5 (le log natif annonce
    /// eExpLevelDiff — incohérence d'origine, reproduite telle quelle
    /// avec le param 328 utilisé des deux côtés pour rester
    /// configurable).</summary>
    public static float ExpDeclineRate(int charLv, int mobLv,
        Params p)
    {
        if (charLv == mobLv) return 1f;
        int lvParam;
        if (charLv < mobLv)
            lvParam = Round((mobLv - charLv - 5f) * p.ExpLevelDec);
        else
            lvParam = Round((charLv - p.ExpLevelDiff - mobLv)
                * p.ExpLevelDec);
        if (lvParam < 0) lvParam = 0;
        else if (lvParam > 100) lvParam = 100;
        return (100 - lvParam) * 0.01f;
    }

    /// <summary>`CCharFormula::DesDeclineRate` @0x083f4db0 — malus de
    /// Destiny Points selon l'écart niveau perso/mob (distinct de
    /// <see cref="ExpDeclineRate"/> : params 576/577, palier
    /// `((diff×100)−100)/p`). 1.0 à niveau égal, 0 au-delà de
    /// ~±(p+1) niveaux d'écart.</summary>
    public static float DesDeclineRate(int charLv, int mobLv, Params p)
    {
        if (charLv == mobLv) return 1f;
        float f;
        int diff;
        if (mobLv < charLv)
        {
            f = p.DesDeclineDown;
            if (f == 0f) f = 1f;
            diff = charLv - mobLv;
        }
        else
        {
            f = p.DesDeclineUp;
            if (f == 0f) f = 1f;
            diff = mobLv - charLv;
        }
        var lvParam = Round((diff * 100f - 100f) / f);
        if (lvParam < 0) lvParam = 0;
        else if (lvParam > 100) lvParam = 100;
        return (100 - lvParam) * 0.01f;
    }

    /// <summary>`CCharFormula::ExpForTeam` @0x083f4470 — part d'exp
    /// du membre en mode partagé :
    /// `ROUND(base × 0.01 × lv × (n × para330 + 100) / Σlv)`, capé à
    /// `base`. <paramref name="memberCount"/> = membres éligibles
    /// (mêmes node, vivants, à ±10 du niveau max exclus), somme des
    /// niveaux déjà expurgée des exclus.</summary>
    public static int ExpForTeam(int baseExp, int memberCount,
        int memberLevel, int sumLevels, Params p)
    {
        if (sumLevels <= 0) return 0;
        var share = Round(baseExp * 0.01f * memberLevel
            * (memberCount * p.TeamExpShareCoef + 100f) / sumLevels);
        return Math.Min(share, baseExp);
    }

    /// <summary>`CCharFormula::ExpForTeamNoShare` @0x083f5340 — part
    /// du tueur quand le partage est refusé (hors ±10 ou flag de
    /// partage levé) : `ROUND((n × para331 + 100) × 0.01 × base
    /// × decline(lv,mobLv))` ; le natif ramène à `base` si le
    /// résultat dépasse `base×2` (règle littérale conservée).</summary>
    public static int ExpForTeamNoShare(int baseExp, int memberCount,
        int memberLevel, int mobLv, Params p)
    {
        var decline = ExpDeclineRate(memberLevel, mobLv, p);
        var share = Round((memberCount * p.TeamExpNoShareCoef + 100f)
            * 0.01f * baseExp * decline);
        return share > baseExp * 2 ? baseExp : share;
    }
}
