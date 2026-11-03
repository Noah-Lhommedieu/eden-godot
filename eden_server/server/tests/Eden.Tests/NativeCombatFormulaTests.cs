using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Zone.Combat;

namespace Eden.Tests;

/// <summary>
/// Tests déterministes des formules `lapis::CCharFormula` transposées
/// (désasm zone — cf. docs/temp-plans/2026-10-22-native-combat-
/// formulas.md). Random seedé → valeurs attendues calculées à la main
/// depuis les équations natives.
/// </summary>
public class NativeCombatFormulaTests
{
    /// <summary>ParameterSet factice avec les vraies valeurs
    /// S_Parameter (305=5, 306=0.3, 307=5, 309=0.8, 310=3, 311=150,
    /// 312=1, 313=3, 314=150, 315=1, 316=1, 317=1, 318=5, 319=0.8,
    /// 322=1, 325=100, 326=3, 327=3, 562=20).</summary>
    private static ParameterParser.ParameterSet ParamSet() => new(
        new Dictionary<int, float>
        {
            [38] = 0.5f, [305] = 5f, [306] = 0.3f, [307] = 5f,
            [309] = 0.8f, [310] = 3f, [311] = 150f, [312] = 1f,
            [313] = 3f, [314] = 150f, [315] = 1f, [316] = 1f,
            [317] = 1f, [318] = 5f, [319] = 0.8f, [322] = 1f,
            [325] = 100f, [326] = 3f, [327] = 3f, [562] = 20f,
        }, new Dictionary<int, string>());

    private static NativeCombatFormulas.Params P =>
        NativeCombatFormulas.ResolveParameters(ParamSet());

    /// <summary>Joueur lvl20, warrior-like : str100×125%, agi80×114%,
    /// int60×100%, lck80×104% ; équipement arme 200 patk / 241 matk.</summary>
    private static CombatantSnapshot Player(
        int level = 20, int variance = 0, int element = 0,
        int equipPAtk = 50, int equipDef = 100) => new()
    {
        Level = level,
        StrTotal = 100, StrPct = 125,
        AgiTotal = 80, AgiPct = 114,
        IntTotal = 60, IntPct = 100,
        LckTotal = 80, LckPct = 104,
        EquipPAtk = equipPAtk, EquipMAtk = 0,
        EquipDefence = equipDef, LevelDefence = 359,
        WeaponPAtk = 200, WeaponMAtk = 241,
        WeaponVariance = variance, WeaponElement = element,
        WeaponHit = 5, WeaponCrit = 0, EquipHit = 10,
    };

    private static CombatantSnapshot Mob(int level = 15, int atk = 186,
        int def = 47, int hit = 1, int dodge = 3, int variance = 0,
        int element = 0) => new()
    {
        IsMonster = true, Level = level,
        MonsterAttack = atk, MonsterDefence = def,
        MonsterHit = hit, MonsterDodge = dodge,
        MonsterVariance = variance, MonsterElement = element,
    };

    // ---------------- attaques de base ----------------

    /// <summary>MeleeDamage @0x83fa430 : strTerm = 100×1.25 = 125 →
    /// (125×5 + 50 + 200) × 100/100 = 875.</summary>
    [Fact]
    public void MeleeDamage_Player_MatchesNative()
    {
        Assert.Equal(875,
            NativeCombatFormulas.MeleeDamage(Player(), P));
    }

    /// <summary>Dual wield : off×para306(0.3) + main →
    /// (125×5 + 50 + (100×0.3 + 200)) = 905.</summary>
    [Fact]
    public void MeleeDamage_DualWield_AppliesOffhandRatio()
    {
        var s = Player();
        s = new CombatantSnapshot
        {
            Level = s.Level, StrTotal = s.StrTotal, StrPct = s.StrPct,
            EquipPAtk = s.EquipPAtk, WeaponPAtk = s.WeaponPAtk,
            DualWield = true, OffWeaponPAtk = 100,
        };
        Assert.Equal(905,
            NativeCombatFormulas.MeleeDamage(s, P));
    }

    /// <summary>MagicDamage @0x83f97a0 : intTerm = 60×1.0 = 60 →
    /// (60×5 + 0 + 241) = 541.</summary>
    [Fact]
    public void MagicDamage_Player_MatchesNative()
        => Assert.Equal(541,
            NativeCombatFormulas.MagicDamage(Player(), P));

    /// <summary>PhysicalAttack(mob) @0x83f8910 : col14 × 1.0 = 186.</summary>
    [Fact]
    public void PhysicalAttack_Monster_IsTemplateValue()
        => Assert.Equal(186,
            NativeCombatFormulas.PhysicalAttack(Mob()));

    /// <summary>PhysicalDefence : joueur = naturalDef + equipDef ;
    /// mob = col18.</summary>
    [Fact]
    public void PhysicalDefence_PlayerVsMonster()
    {
        Assert.Equal(459,
            NativeCombatFormulas.PhysicalDefence(Player()));
        Assert.Equal(47,
            NativeCombatFormulas.PhysicalDefence(Mob()));
    }

    // ---------------- ratings ----------------

    /// <summary>PhysicalHitRate joueur @0x83fc900 :
    /// ((10+5 + 83×1) / (20×0.4+1) + 1) ×100 = (98/9 + 1)×100
    /// ≈ 1188.9 → 1189.</summary>
    [Fact]
    public void PhysicalHitRate_Player_MatchesNative()
        => Assert.Equal(1189,
            NativeCombatFormulas.PhysicalHitRate(Player(), P));

    /// <summary>PhysicalDodgeRate joueur : (91×1/9 + 5)×100 = 1511.</summary>
    [Fact]
    public void PhysicalDodgeRate_Player_MatchesNative()
        => Assert.Equal(1511,
            NativeCombatFormulas.PhysicalDodgeRate(Player(), P));

    [Fact]
    public void PhysicalDodgeRate_IncludesCertificatePercentBonus()
    {
        var player = Player();
        player.BonusDodgePercent = 4;

        Assert.Equal(1911,
            NativeCombatFormulas.PhysicalDodgeRate(player, P));
    }

    /// <summary>PhysicalCriticalRate joueur : (83×0.8/9 + 3)×100
    /// ≈ 1038.</summary>
    [Fact]
    public void PhysicalCriticalRate_Player_MatchesNative()
        => Assert.Equal(1038,
            NativeCombatFormulas.PhysicalCriticalRate(Player(), P));

    /// <summary>CritDmg rating : para311(150) + equip, clamp [0,300].</summary>
    [Fact]
    public void PhysicalCriticalDamage_BaseAndClamp()
    {
        Assert.Equal(150,
            NativeCombatFormulas.PhysicalCriticalDamage(Player(), P));
        var hi = Player();
        hi = new CombatantSnapshot { EquipCritDamage = 500 };
        Assert.Equal(300,
            NativeCombatFormulas.PhysicalCriticalDamage(hi, P));
    }

    // ---------------- dégâts ----------------

    /// <summary>Auto-attaque joueur→mob, variance 0 (roll=0) :
    /// adjust = 20^1.6+30 ≈ 150.9 ; offset = clamp(20−15)=5 ;
    /// defTerm = 47×3/150.9 ≈ 0.9347 ; phys = 875×0.01×(0+100−0)=875 ;
    /// dmg = ROUND((10+100−0.9347)×1e-5×875) = ROUND(95.43) = 95.</summary>
    [Fact]
    public void PhysicalDamage_PlayerVsMob_Deterministic()
    {
        var dmg = NativeCombatFormulas.CalculatePhysicalDamage(
            Player(), Mob(), P, new Random(1));
        Assert.Equal(95, dmg);
    }

    /// <summary>Résist élémentaire clampée ±70 : résist 200 →
    /// (100−70)=30% de dégâts — multiplicateur exact.</summary>
    [Fact]
    public void PhysicalDamage_ResistanceClampedAt70()
    {
        var mob = Mob();
        mob = new CombatantSnapshot
        {
            IsMonster = true, Level = mob.Level,
            MonsterAttack = mob.MonsterAttack,
            MonsterDefence = mob.MonsterDefence,
            MonsterResists = new[] { 500, 0, 0, 0, 0, 0, 0, 0, 0 },
        };
        var atk = Player(element: 1);
        var dmg = NativeCombatFormulas.CalculatePhysicalDamage(
            atk, mob, P, new Random(1));
        // defTerm ≈ 0.9347, res factor = 0.30 :
        // ROUND(109.065 × 0.30 × 1e-2 × 8.75... ) — (110−0.9347)×
        // (100−70)×1e-5×875×1 = 28.64 → 29
        Assert.Equal(29, dmg);
    }

    /// <summary>Offset monstre : Δlvl×5 clampé [0,500] — mob lvl 40
    /// vs joueur lvl 20 → offset = 100.</summary>
    [Fact]
    public void PhysicalDamage_MonsterOffset_TimesFive()
    {
        var dmg = NativeCombatFormulas.CalculatePhysicalDamage(
            Mob(level: 40, atk: 500, def: 0, variance: 0),
            Player(equipDef: 0), P, new Random(1));
        // adjust = 40^1.6+30 ≈ 395.84 ; pdef = 0+359 = 359 →
        // defTerm = 359×3/395.84 ≈ 2.72 ; phys = 500×0.01×100 = 500 ;
        // dmg = ROUND((200+100−2.72)×100×1e-5×500) = ROUND(148.64) = 149
        Assert.Equal(149, dmg);
    }

    /// <summary>Plancher natif : dégât <5 → rand%4+1 ∈ [1,4].</summary>
    [Fact]
    public void PhysicalDamage_BelowFive_RollsSmallDamage()
    {
        var dmg = NativeCombatFormulas.CalculatePhysicalDamage(
            Mob(level: 1, atk: 1, def: 0), Player(), P, new Random(7));
        Assert.InRange(dmg, 1, 4);
    }

    // ---------------- skills ----------------

    /// <summary>`CalculateMagicSkillDamage` @0x83fd190 : matk 541 +
    /// power 100×coef 1.0 → skill = 641×0.01×100 = 641 ;
    /// offset clamp(20−15)=5 → (110)×1e-5×641×(100−res[0]=100)
    /// = ROUND(70.51) = 71.</summary>
    [Fact]
    public void MagicSkillDamage_Deterministic()
    {
        var dmg = NativeCombatFormulas.CalculateMagicSkillDamage(
            Player(), Mob(), spellPower: 100, variance: 0,
            element: 1, refCoef: 1.0f, P, new Random(1));
        Assert.Equal(71, dmg);
    }

    /// <summary>`CalculatePhysicalSkillDamage` @0x83fd470 :
    /// (875 + 100×1.0)×1e-7×100×(110−0.9347)×(100−0)
    /// = ROUND(975×1e-5×109.065) = ROUND(106.34) = 106.</summary>
    [Fact]
    public void PhysicalSkillDamage_Deterministic()
    {
        var dmg = NativeCombatFormulas.CalculatePhysicalSkillDamage(
            Player(), Mob(), spellPower: 100, variance: 0,
            refCoef: 1.0f, P, new Random(1));
        Assert.Equal(106, dmg);
    }

    /// <summary>Dispatch `SSpellData+0x94` : type2 elem5 → magique ;
    /// type0 elem1 → physique ; type4 → restauration.</summary>
    [Fact]
    public void SpellHitKind_MatchesNativeFlags()
    {
        Assert.Equal(SpellHitKind.Magic,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 2, AttackElement = 5 }));
        Assert.Equal(SpellHitKind.Physical,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 0, AttackElement = 1 }));
        // Restore réel : type 4/5 avec élément 10 → flag 0x40 (bit4 libre)
        // → HitRestoreSkill (éléments 7/10 typiques des revives natifs).
        Assert.Equal(SpellHitKind.Restore,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 4, AttackElement = 10 }));
        // Quirk natif : type 4 + élément 1 → flag 0x44 → bit4 posé →
        // HitPhysicalSkill (le bit4 gagne sur le type restore).
        Assert.Equal(SpellHitKind.Physical,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 4, AttackElement = 1 }));
        // Skill de classe type2 elem1 → canal magique (base M-ATK +
        // résist physique résolue par l'élément 1).
        Assert.Equal(SpellHitKind.Magic,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 2, AttackElement = 1 }));
        // Type hors table (≥8) élément 1 → canal physique natif.
        Assert.Equal(SpellHitKind.Physical,
            NativeCombatFormulas.SpellHitKindOf(
                new SpellParser.SpellDef { Type = 8, AttackElement = 1 }));
    }

    // ---------------- rolls ----------------

    /// <summary>Attaque infaillible + crit garanti : hit rate ≥ 100 et
    /// crit rate = 10000 → toujours Crit (caller natif).</summary>
    [Fact]
    public void ResolvePhysicalAttack_MaxRatings_AlwaysCrit()
    {
        var atk = Player();
        atk = new CombatantSnapshot
        {
            Level = 100, StrTotal = 9999, StrPct = 100,
            LckTotal = 9999, LckPct = 125, EquipPAtk = 5000,
            WeaponPAtk = 500, EquipHit = 5000,
            EquipCrit = 5000, WeaponCrit = 0,
        };
        var res = NativeCombatFormulas.ResolvePhysicalAttack(
            atk, Mob(level: 1, dodge: 0), P, new Random(2));
        Assert.Equal(PhysicalOutcome.Crit, res.Outcome);
        Assert.True(res.Damage > 0);
    }

    /// <summary>Roll hit : rate planché à 10 (natif) → ~10 % de coups —
    /// sur 500 rolls seedés, le compte est borné.</summary>
    [Fact]
    public void RollPhysicalHit_Floor10Percent()
    {
        var atk = new CombatantSnapshot { Level = 1 };
        var def = Mob(level: 99, dodge: 9999);
        var rng = new Random(42);
        var hits = 0;
        for (var i = 0; i < 500; i++)
            if (NativeCombatFormulas.RollPhysicalHit(atk, def, P, rng))
                hits++;
        // rate = max(10, 100 + (0−dodgeRate)/100) = 10 → ~50 hits
        // attendus ; borne large pour rester insensible au seed.
        Assert.InRange(hits, 25, 80);
    }

    /// <summary>Plancher natif : critLv=0 → rate = para310(3)×100 = 300
    /// → ~3 % de crits toujours possibles (1000 rolls seedés bornés).</summary>
    [Fact]
    public void RollPhysicalCrit_FloorRate_BaseCritLv()
    {
        var rng = new Random(3);
        var atk = new CombatantSnapshot { Level = 1 };
        var crits = 0;
        for (var i = 0; i < 1000; i++)
            if (NativeCombatFormulas.RollPhysicalCrit(atk, Mob(), P, rng))
                crits++;
        Assert.InRange(crits, 10, 80);
    }

    /// <summary>`CalculatePhysicalCriticalDamage` @0x83f7780 : dmg×150%
    /// sans pdef adverse (pct clampé [100,300]).</summary>
    [Fact]
    public void ApplyPhysicalCrit_150PercentNoDefense()
    {
        var dmg = NativeCombatFormulas.ApplyPhysicalCrit(
            Player(), Mob(def: 0), 100, P);
        Assert.Equal(150, dmg);
    }

    /// <summary>Crit magique : même pct base 150 (para314), pas de
    /// réduction de défense.</summary>
    [Fact]
    public void ApplyMagicCrit_150Percent()
        => Assert.Equal(150, NativeCombatFormulas.ApplyMagicCrit(
            Player(), Mob(), 100, P));

    // ---------------- parsers ----------------

    /// <summary>`ResolveParameters` lit les ids natifs (index
    /// `AllFormulaParas`) — aucun hardcodage.</summary>
    [Fact]
    public void ResolveParameters_ReadsNativeIds()
    {
        var p = P;
        Assert.Equal(5f, p.StrToPAtk);
        Assert.Equal(0.3f, p.OffhandRatio);
        Assert.Equal(150f, p.BaseCritDmg);
        Assert.Equal(150f, p.BaseMCritDmg);
        Assert.Equal(0.8f, p.AgiToParry);
        Assert.Equal(20f, p.DefToHpDivisor);
        Assert.Equal(3f, p.SkillDefParam);
    }

    /// <summary>Getters 296 — MCriLv = equipMCrit + lckTerm×para312 ;
    /// DefenceReduce = pdef×3/(lvl^1.6+30) ; Movement = 9.425.</summary>
    [Fact]
    public void DisplayGetters_MatchNative()
    {
        var s = Player();
        // MCriLv = 0 + 83×1 = 83
        Assert.Equal(83f,
            NativeCombatFormulas.MCriLv(s, P));
        // DefenceReduce = 459×3/(20^1.6+30=150.94) ≈ 9.14
        Assert.Equal(9.14f,
            NativeCombatFormulas.DefenceReduce(s, P), 2);
        // CriDmgReduce = 459/150.94 ≈ 3.05
        Assert.Equal(3.05f,
            NativeCombatFormulas.CriDmgReduce(s), 2);
        Assert.Equal(9.425f,
            NativeCombatFormulas.BaseMovement, 3);
    }

    // ---------------- mapping S_Spell / soins (bug live 20/10) ----------------

    private static readonly string SpellIni =
        TestPaths.RepoRoot + "/" +
        "server_us/Data/db_decoded/S_Spell.ini";

    static NativeCombatFormulaTests() =>
        System.Text.Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);

    /// <summary>Mapping `CSpellQuery::Deserialize` séquentiel (désasm
    /// zone) : col17 → `SSpellData+8` = famille, col18 → `+0xc` = TYPE
    /// de dispatch, col33 → `+0xbe` élément, col35 → `+0xc0` power,
    /// col36 → `+0xc2` heal power, col37 → `+0xc4` variance. Régression
    /// sur données réelles : avant ce mapping les soins (famille 6 ≠
    /// type 4) tombaient en sortie `None`/magique → 0 HP rendus.</summary>
    [Fact]
    public void SpellParser_NativeColumnMapping()
    {
        var spells = SpellParser.Parse(SpellIni)
            .ToDictionary(s => s.Id);
        // Cure 50028 : type dispatch 4, élément 10, heal 110, var 15.
        var cure = spells[50028];
        Assert.Equal(4, cure.Family);
        Assert.Equal(4, cure.Type);
        Assert.Equal(10, cure.AttackElement);
        Assert.Equal(0, cure.DamagePower);
        Assert.Equal(110, cure.HealPower);
        Assert.Equal(15, cure.DamageVariance);
        // Healing Light 50055 : famille 6 (classe) mais type 4 —
        // LA preuve que col17 n'est pas le type : avant le fix ce sort
        // partait dans le canal magique avec power 0 → 0 dégât/0 soin.
        var hl = spells[50055];
        Assert.Equal(6, hl.Family);
        Assert.Equal(4, hl.Type);
        Assert.Equal(90, hl.HealPower);
        Assert.Equal(15, hl.DamageVariance);
        // Radiance 50109 : type 5 (restore de groupe).
        var rad = spells[50109];
        Assert.Equal(5, rad.Type);
        Assert.Equal(80, rad.HealPower);
        // Earth Shock 50026 : famille 4 mais type 1 — le dispatch ne
        // doit PAS le router en restore ; power col35=120.
        var es = spells[50026];
        Assert.Equal(4, es.Family);
        Assert.Equal(1, es.Type);
        Assert.Equal(120, es.DamagePower);
        Assert.Equal(7, es.AttackElement);
        Assert.Equal(10, es.DamageVariance);
    }

    /// <summary>Champs c26/c27/c32/c23/c22 natifs (Deserialize
    /// @0x862af40 + builder @0x8380130) : cast ds, down-time ds,
    /// MP base, class mask i64 mono-cellule, niveau requis.</summary>
    [Fact]
    public void SpellParser_TimingAndCostColumns()
    {
        var spells = SpellParser.Parse(SpellIni)
            .ToDictionary(s => s.Id);
        // Slam 50011 : instant (c26 vide), CD 20ds (c27), MP 60 (c32).
        var slam = spells[50011];
        Assert.Equal(0, slam.CastTimeDs);
        Assert.Equal(20, slam.CooldownDs);
        Assert.Equal(60, slam.MpCost);
        Assert.Equal(1, slam.RequiredLevel);
        // Cure 50028 : cast 20ds (c26), pas de down-time (c27 vide),
        // MP 112, niveau 3, portée 40 (c28 → +0x40 float).
        var cure = spells[50028];
        Assert.Equal(20, cure.CastTimeDs);
        Assert.Equal(0, cure.CooldownDs);
        Assert.Equal(112, cure.MpCost);
        Assert.Equal(3, cure.RequiredLevel);
        Assert.Equal(40, cure.Range);
        // Onrush 50038 : CD 80ds, mask warrior=1 (c23 i64 mono-cellule —
        // 50011 prouve que c23 porte l'i64 entier : 54095984974762049).
        var onrush = spells[50038];
        Assert.Equal(80, onrush.CooldownDs);
        Assert.Equal(1UL, onrush.ClassMask);
        Assert.Equal(54095984974762049UL, spells[50011].ClassMask);
        Assert.Equal(16UL, spells[50058].ClassMask);   // Thief bit4
        // 火矢 50126 : CD 30ds, MP 100, power 150, élément 6 (foudre).
        var bolt = spells[50126];
        Assert.Equal(30, bolt.CooldownDs);
        Assert.Equal(100, bolt.MpCost);
        Assert.Equal(150, bolt.DamagePower);
        Assert.Equal(6, bolt.AttackElement);
    }

    /// <summary>Dispatch `OnHitLife` natif sur les sorts réels : type 4/5
    /// (col18) → `HitRestoreSkill`, type 1 → offensif selon l'élément.
    /// Régression du routage col17→col18.</summary>
    [Fact]
    public void SpellHitKind_RealSpells_DispatchCorrectly()
    {
        var spells = SpellParser.Parse(SpellIni)
            .ToDictionary(s => s.Id);
        Assert.Equal(SpellHitKind.Restore,
            NativeCombatFormulas.SpellHitKindOf(spells[50028]));
        Assert.Equal(SpellHitKind.Restore,
            NativeCombatFormulas.SpellHitKindOf(spells[50055]));
        Assert.Equal(SpellHitKind.Restore,
            NativeCombatFormulas.SpellHitKindOf(spells[50109]));
        // Earth Shock type1 elem7 → canal magique natif.
        Assert.Equal(SpellHitKind.Magic,
            NativeCombatFormulas.SpellHitKindOf(spells[50026]));
        // Slam 50038 type1 elem1 → canal physique (mêlée).
        Assert.Equal(SpellHitKind.Physical,
            NativeCombatFormulas.SpellHitKindOf(spells[50038]));
    }

    /// <summary>`CalculateMagicSkillHeal` @0x83f9310 : variance 0 →
    /// `heal = ROUND(power × coef/100)` exact, ou ×crit% si le roll
    /// passe (les soins crittent nativement — `HitRestoreSkill` pose
    /// `|0x200000`). power 100 × coef 100% → 100 ou 150 (crit ×150).</summary>
    [Fact]
    public void ResolveSpellHeal_ZeroVariance_IsBaseOrCrit()
    {
        var spell = new SpellParser.SpellDef
            { Type = 4, HealPower = 100, DamageVariance = 0 };
        var res = NativeCombatFormulas.ResolveSpellHeal(
            Player(), Mob(), spell, 100, P, new Random(5));
        Assert.Equal(res.Critical ? 150 : 100, res.Amount);
        Assert.Equal(res.Amount > 100, res.Critical);
    }

    /// <summary>Variance col37=15 : multiplicateur natif
    /// `(100+2r−15)/100` avec r ∈ [0,15) → heal ∈ [85%,115%) du base
    /// (puis ×crit éventuel). 200 rolls seedés bornés.</summary>
    [Fact]
    public void ResolveSpellHeal_Variance15_StaysInNativeBand()
    {
        var spell = new SpellParser.SpellDef
            { Type = 4, HealPower = 110, DamageVariance = 15 };
        var rng = new Random(9);
        for (var i = 0; i < 200; i++)
        {
            var res = NativeCombatFormulas.ResolveSpellHeal(
                Player(), Mob(), spell, 100, P, rng);
            // base = 110 ; non-crit ∈ [93,126], crit ≤ 189.
            Assert.InRange(res.Amount, 93, 190);
            if (!res.Critical) Assert.InRange(res.Amount, 93, 126);
        }
    }

    /// <summary>Sans puissance de soin (col36 vide — Revive 50030) le
    /// restore ne rend rien : montant 0, pas de crit affiché.</summary>
    [Fact]
    public void ResolveSpellHeal_NoPower_ReturnsZero()
    {
        var spell = new SpellParser.SpellDef { Type = 4, HealPower = 0 };
        var res = NativeCombatFormulas.ResolveSpellHeal(
            Player(), Mob(), spell, 100, P, new Random(5));
        Assert.Equal(0, res.Amount);
        Assert.False(res.Critical);
    }

    /// <summary>Flags du 281 `ClientSpellHitToLife` (5e i32 — désasm
    /// `HitXSkill`) : miss→8, hit→1, crit→1|0x200000, restore→0x200.</summary>
    [Fact]
    public void SpellHitFlags_MatchNativeConstants()
    {
        Assert.Equal(SpellHitFlags.Miss,
            NativeCombatFormulas.SpellHitFlagsOf(
                new PhysicalResult(0, PhysicalOutcome.Miss)));
        Assert.Equal(SpellHitFlags.Hit,
            NativeCombatFormulas.SpellHitFlagsOf(
                new PhysicalResult(50, PhysicalOutcome.Hit)));
        Assert.Equal(SpellHitFlags.Hit | SpellHitFlags.Crit,
            NativeCombatFormulas.SpellHitFlagsOf(
                new PhysicalResult(75, PhysicalOutcome.Crit)));
        // Constantes natives posées par HitRestoreSkill @0x84f1f40.
        Assert.Equal(0x200, SpellHitFlags.Restore);
        Assert.Equal(0x200000, SpellHitFlags.Crit);
    }

    /// <summary>Mods crit par sort (AttrSpell*CriRates/Damages —
    /// `GetSpellAttrModify` kinds 3-6 dans les Calculate*Crit*
    /// natifs) : rate += mod×100, dmg += mod sur le pct clampé
    /// [100,300]. spellMod=100 → crit garanti ; spellMod dmg passe
    /// le pct de 150 à 200.</summary>
    [Fact]
    public void SpellCritMods_NativeScaling()
    {
        var atk = new CombatantSnapshot { Level = 50, LckTotal = 1 };
        var def = Mob();
        // Rate base ≈ 3 % (para310/313) — sans mod, le seed 7 donne
        // un résultat mixte ; +100 (mod) → 10000 → toujours crit.
        Assert.True(NativeCombatFormulas.RollPhysicalCrit(atk, def, P,
            new Random(7), spellMod: 100));
        Assert.True(NativeCombatFormulas.RollMagicCrit(atk, def, P,
            new Random(7), spellMod: 100));
        // Damage : pct = MCritDmg(150) + 50 = 200 → dmg × 2.
        var crit = NativeCombatFormulas.ApplyMagicCrit(atk, def, 1000,
            P, spellMod: 50);
        Assert.Equal(2000, crit);
        // Clamp 300 : mod énorme ne dépasse pas ×3.
        Assert.Equal(3000, NativeCombatFormulas.ApplyMagicCrit(atk,
            def, 1000, P, spellMod: 500));
    }
}
