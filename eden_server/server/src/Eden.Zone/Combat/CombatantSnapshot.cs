using Eden.Core.Data.Parsers;

namespace Eden.Zone.Combat;

/// <summary>
/// Snapshot des entrées de combat d'un combattant — miroir des champs
/// lus par `lapis::CCharFormula` sur `CCharacter`/`CMonster` (désasm
/// zone `re/ghidra/zone/decompiled.c`).
///
/// Deux formes :
///   <see cref="Player"/> : alimenté par S_Level (stats de base),
///   S_Class (% de stats cols15-21), l'agrégat d'équipement
///   (char+0x38c..+0x3e2 natifs) et les ItemDef des slots d'arme
///   canoniques (main=loc7, off=loc8, longue/distance=loc10; EquipType
///   9→7 et 35→10, désasm CItemFactory::Equip @0x80b5810).
///   <see cref="Monster"/> : alimenté par <see
///   cref="MonsterParser.MonsterDef"/> (mapping CMonsterData→
///   CMonsterTemplate prouvé — atk col14→+0x4c, var col15→+0x44,
///   def col18→+0x5c, hit col19→+0x52, dodge col20→+0x50, crit
///   col21→+0x54, critdmg col22→+0x58, résists cols25-33→+0xa8,
///   élément col13→+0x70).
///
/// Tous les champs "buff" natifs (CCharAttr/CKindData : +0xe0/+0xdc/
/// +0x178/+0x540/+0x304..) sont absents du modèle serveur actuel → 0.
/// </summary>
public sealed class CombatantSnapshot
{
    /// <summary>Niveau de l'entité (vtable+0x124 natif —
    /// `ILifeEntity::GetLevel`, char+0x470 / monstre template).</summary>
    public int Level { get; init; }
    /// <summary>Attaquant issu de S_Monster (vtable+8 ≠ 7 — le natif
    /// branche le calcul d'offset sur le type d'entité).</summary>
    public bool IsMonster { get; init; }

    // ======================== ENTRÉES JOUEUR ========================

    /// <summary>Somme BRUTE `equip + base` avant % de classe — le natif
    /// calcule `Str() = (char+0x394 + char+0x354) × (kinddata+0x178 +
    /// char+0x338) × 0.01` : le % classe (+ buff, 0 ici) s'applique
    /// dans la formule, pas dans le champ. Ordre natif Str(+0x394+
    /// 0x354), Int(+0x398+0x358), Agi(+0x39c+0x35c), Lck(+0x3a4+0x364).</summary>
    public int StrTotal
    {
        get => _coreBase[0] + (int)Math.Round(CoreBonusPre[0]);
        init => _coreBase[0] = value;
    }
    /// <summary>INT effective (idem, % = S_Class col19).</summary>
    public int IntTotal
    {
        get => _coreBase[1] + (int)Math.Round(CoreBonusPre[1]);
        init => _coreBase[1] = value;
    }
    /// <summary>AGI/DEX effective (% = col18).</summary>
    public int AgiTotal
    {
        get => _coreBase[2] + (int)Math.Round(CoreBonusPre[2]);
        init => _coreBase[2] = value;
    }
    /// <summary>LCK/LUK effective (% = col21) — alimente hit et crit.</summary>
    public int LckTotal
    {
        get => _coreBase[4] + (int)Math.Round(CoreBonusPre[4]);
        init => _coreBase[4] = value;
    }
    /// <summary>WIS/SPR effective (% = col20) — alimente Heal/BeHeal
    /// (char+0x3a0+0x360 natif, para323/324).</summary>
    public int WisTotal
    {
        get => _coreBase[3] + (int)Math.Round(CoreBonusPre[3]);
        init => _coreBase[3] = value;
    }
    /// <summary>% STR de classe (S_Class col17 → char+0x338) +
    /// bonus % des stages du pipeline (kinddata+0x178 natif).</summary>
    public int StrPct
    {
        get => _corePctBase[0] + (int)Math.Round(CoreBonusPct[0]);
        init => _corePctBase[0] = value;
    }
    /// <summary>% INT de classe (S_Class col19 → char+0x33c).</summary>
    public int IntPct
    {
        get => _corePctBase[1] + (int)Math.Round(CoreBonusPct[1]);
        init => _corePctBase[1] = value;
    }
    /// <summary>% AGI de classe (S_Class col18 → char+0x340).</summary>
    public int AgiPct
    {
        get => _corePctBase[2] + (int)Math.Round(CoreBonusPct[2]);
        init => _corePctBase[2] = value;
    }
    /// <summary>% WIS de classe (S_Class col20 → char+0x344).</summary>
    public int WisPct
    {
        get => _corePctBase[3] + (int)Math.Round(CoreBonusPct[3]);
        init => _corePctBase[3] = value;
    }
    /// <summary>% LCK de classe (S_Class col21 → char+0x348).</summary>
    public int LckPct
    {
        get => _corePctBase[4] + (int)Math.Round(CoreBonusPct[4]);
        init => _corePctBase[4] = value;
    }

    // Ordre des cores : STR, INT, AGI, WIS, LCK (= StatIndex 0..4).
    // Les « Total/Pct » ci-dessus = base posée à la construction
    // (init-only) + bonus versés par ApplyContribution (setters
    // privés → les ~30 sites de formule restent inchangés).
    private readonly int[] _coreBase = new int[5];
    private readonly int[] _corePctBase = new int[5];
    /// <summary>Agrégat équipement (Sum) déjà compris dans les
    /// <c>*Total</c> à la construction — retranché du canal pre du
    /// pipeline (le stage « equipment » le porte déjà) pour ne pas
    /// compter l'équipement deux fois. Ordre STR, INT, AGI, WIS, LCK.</summary>
    public int[] GearCore { get; init; } = new int[5];
    /// <summary>Bonus core plats pré-% venus du pipeline hors
    /// équipement (gemmes, fortify, talents, titres, buffs…).</summary>
    public double[] CoreBonusPre { get; } = new double[5];
    /// <summary>Points de % core ajoutés au % de classe par les stages
    /// (trait héroïque +10 % STR, buffs…).</summary>
    public double[] CoreBonusPct { get; } = new double[5];

    /// <summary>Attaque d'équipement cumulée (char+0x3a8 — Σ col45 des
    /// items possédant un CItemAddAttr, désasm RefreshEQAttr
    /// @0x8419f00).</summary>
    public int EquipPAtk { get; init; }
    /// <summary>Attaque magique cumulée (char+0x3ac — Σ col46).</summary>
    public int EquipMAtk { get; init; }
    /// <summary>Défense d'équipement cumulée (char+0x3b8 — Σ col50).</summary>
    public int EquipDefence { get; init; }
    /// <summary>Défense naturelle du niveau (char+0x370 — S_Level col4
    /// → leveldata+0x14 ; 359@lv1, 834@lv100).</summary>
    public int LevelDefence { get; init; }
    /// <summary>Précision d'équipement (char+0x3bc — Σ col51).</summary>
    public int EquipHit { get; init; }
    /// <summary>Esquive d'équipement (char+0x3be — Σ col52).</summary>
    public int EquipDodge { get; init; }
    /// <summary>Critique d'équipement (char+0x3c0 — Σ col53).</summary>
    public int EquipCrit { get; init; }
    /// <summary>Dégâts critiques d'équipement (char+0x3c2 — Σ col54).</summary>
    public int EquipCritDamage { get; init; }
    /// <summary>Critique magique d'équipement (char+0x3c4 — Σ col55).</summary>
    public int EquipMagicCrit { get; init; }
    /// <summary>Dégâts crit magiques d'équipement (char+0x3c6 —
    /// Σ col56).</summary>
    public int EquipMagicCritDamage { get; init; }
    /// <summary>Résistances élémentaires cumulées (char+0x3d4..+0x3e2 —
    /// Σ cols63-71) ; indexées par `element−1` (SResistAttribute::
    /// GetResist @0x83fe6c0).</summary>
    public int[] EquipResists { get; init; } = new int[9];
    /// <summary>Vitesse d'attaque d'équipement (char+0x3c8 — Σ col49
    /// — AttackSpeedLevel @0x83fbed0).</summary>
    public int EquipAttackSpeed { get; init; }
    /// <summary>Vitesse de cast d'équipement (CastSpeedLevel — même
    /// squelette que l'attaque avec para322).</summary>
    public int EquipCastSpeed { get; init; }
    /// <summary>Niveau de parade d'équipement (char+0x3d0 natif —
    /// terme flat de ParryLv avant l'AGI, alimenté par la Mana
    /// Infusion type 11).</summary>
    public int EquipParry { get; init; }
    /// <summary>Bonus de blocage d'équipement (kinddata+0x4de natif —
    /// Σ du bonus BlockDamageRate, notamment sur boucliers).</summary>
    public int EquipBlockRate { get; init; }
    /// <summary>Soin donné d'équipement (char+0x3cc natif — flat après
    /// les % de Heal(caster)).</summary>
    public int EquipHeal { get; init; }
    /// <summary>Soin reçu d'équipement (char+0x3ce natif — flat après
    /// les % de BeHeal(target)).</summary>
    public int EquipBeHealed { get; init; }
    /// <summary>Bonus de vitesse de déplacement actif en %
    /// (kinddata+0xa0 natif = multiplicateur-1 — monture via
    /// S_RideStep, enchant 2051 AttrMovement ; custom AGI ajouté dans
    /// la formule Movement).</summary>
    public float MoveSpeedBonusPct { get; init; }
    /// <summary>Bonus d'esquive des certificats en points de
    /// pourcentage — `CEC_AttrDodgeRate` modifie l'accumulateur
    /// `CCharAttr+0x11c` avant la conversion wire ×100.</summary>
    public double BonusDodgePercent { get; set; }
    /// <summary>Bonus flat d'attaque physique (accumulateur
    /// `AttrPhysicoDamage` natif — bonus de palier de classe /
    /// certificats). Rejoint le terme flat de MeleeDamage.</summary>
    public double BonusPAtk { get; set; }
    /// <summary>Bonus flat d'attaque magique — miroir de
    /// <see cref="BonusPAtk"/> pour MagicDamage.</summary>
    public double BonusMAtk { get; set; }

    // --- Bonus de statuts temporaires (CKindData natif : les accums
    // +0xa0/+0xdc..+0x158 remplis par les CEC_Attr* des enchants) ---

    /// <summary>P-ATK % — accum `kinddata+0xe0` (`AttrPhysicoDamageRate`
    /// cmd 2083 var0) : `(100+pct)` multiplie MeleeDamage.</summary>
    public double BonusPAtkRate { get; set; }
    /// <summary>M-ATK % — accum `kinddata+0xe8` (cmd 2083 var2).</summary>
    public double BonusMAtkRate { get; set; }
    /// <summary>DEF flat post-% — accum `kinddata+0xec`
    /// (`AttrDefence` cmds 2082 var0 / 2084 var1).</summary>
    public double BonusDefence { get; set; }
    /// <summary>DEF % — accum `kinddata+0xf0` (`AttrDefenceRate`
    /// cmd 2083 var1).</summary>
    public double BonusDefenceRate { get; set; }
    /// <summary>Précision flat — accum `kinddata+0x114`
    /// (`AttrHitRate` cmds 2082 var1 / 2087 var0).</summary>
    public double BonusHitRating { get; set; }
    /// <summary>Esquive flat — accum `kinddata+0x11c`
    /// (`AttrDodgeRate` cmds 2082 var2 / 2087 var1).</summary>
    public double BonusDodgeRating { get; set; }
    /// <summary>Critique flat — accum `kinddata+0x100` (cmd 2048 var0).</summary>
    public double BonusCritRating { get; set; }
    /// <summary>Critique magique flat — accum `kinddata+0x10c`
    /// (cmd 2048 var1).</summary>
    public double BonusMagicCritRating { get; set; }
    /// <summary>Critique % (points) — accum `kinddata+0x104`
    /// (cmd 2040 var0) : facteur `(1+pct×0.01)` du numérateur de
    /// `PhysicalCriticalRate`.</summary>
    public double BonusCritRatePercent { get; set; }
    /// <summary>Critique magique % (points) — accum `kinddata+0x110`
    /// (cmd 2040 var1).</summary>
    public double BonusMagicCritRatePercent { get; set; }
    /// <summary>Dégâts critiques +% — accum `kinddata+0xfc`.</summary>
    public double BonusCritDamage { get; set; }
    /// <summary>Dégâts crit magiques +% — accum `kinddata+0x108`.</summary>
    public double BonusMagicCritDamage { get; set; }
    /// <summary>Vitesse d'attaque flat — accum `kinddata+0x124`
    /// (`AttrAttackSpeedPlus` cmd 2056) : numérateur d'
    /// `AttackSpeedLevelRate`.</summary>
    public double BonusAttackSpeed { get; set; }
    /// <summary>Vitesse d'attaque % (points) — accum `kinddata+0x12c`
    /// (cmd 2068 `AttrAttackSpeedPlus 2`) : facteur `(1+pct×0.01)` du
    /// numérateur d'`AttackSpeedLevel`.</summary>
    public double BonusAttackSpeedPercent { get; set; }
    /// <summary>Vitesse de cast flat — accum `kinddata+0x134`
    /// (`AttrCastTimePlus`) : numérateur de `CastSpeedLevelRate`.</summary>
    public double BonusCastSpeed { get; set; }
    /// <summary>Vitesse de cast % (points) — accum `kinddata+0x13c`
    /// (cmd 2069 `AttrCastTimePlus 2`) : facteur `(1+pct×0.01)` du
    /// numérateur de `MagicSpeedLevel`.</summary>
    public double BonusCastSpeedPercent { get; set; }
    /// <summary>Déplacement +% — accum `kinddata+0xa0`
    /// (`AttrMovement` cmd 2051) — sommé à `MoveSpeedBonusPct`.</summary>
    public double BonusMoveSpeedPct { get; set; }
    /// <summary>Blocage +% — accum `kinddata+0x154` (`AttrBlock`).</summary>
    public double BonusBlockRate { get; set; }
    /// <summary>Parade flat — accum `kinddata+0x158` (`AttrParry`) :
    /// ajouté à `ParryLv`.</summary>
    public double BonusParry { get; set; }
    /// <summary>Résistances flat par élément — accum
    /// `kinddata+0x00..0x11` (`AttrResist` cmds 2095/2101) : sommées à
    /// `EquipResists` pour le wire ET le calcul de dégâts.</summary>
    public int[] BonusResists { get; } = new int[9];
    /// <summary>Précision % multiplicatif — accum natif
    /// `AttrHitRateRate` (cmd 2088 var0) : facteur `(1+pct×0.01)` du
    /// numérateur de `PhysicalHitRate`.</summary>
    public double BonusHitRatePercent { get; set; }
    /// <summary>Esquive % multiplicatif — accum natif
    /// `AttrDodgeRateRate` (cmd 2088 var1).</summary>
    public double BonusDodgeRatePercent { get; set; }
    /// <summary>Dégâts % par kind d'arme — accums float natifs
    /// `kinddata+0x340..` (cmds 2102/2103/2104 `AttrWeaponDamageRate`,
    /// indexés kind−0x11, désasm Execute @0x8518710) : facteur
    /// `(1+pct)` sur le dégât quand l'arme équipée est du kind —
    /// double (données fractionnelles).</summary>
    public double[] BonusWeaponDamageRatePct { get; } = new double[17];
    /// <summary>Soins prodigués +% — accum natif `HealPlus` mode 2
    /// (cmd 2038 arg0) : multiplie les soins émis.</summary>
    public double BonusHealGivenPct { get; set; }
    /// <summary>Soins reçus +% — accum natif `HealPlus` mode 3
    /// (cmd 2038 arg1) : multiplie les soins reçus.</summary>
    public double BonusHealTakenPct { get; set; }
    /// <summary>Soins prodigués +flat — `HealPlus` mode 4 (cmd 2047 arg0,
    /// KB-05).</summary>
    public double BonusHealGivenFlat { get; set; }
    /// <summary>Soins reçus +flat — `HealPlus` mode 5 (cmd 2047 arg1).</summary>
    public double BonusHealTakenFlat { get; set; }
    /// <summary>Coût MP flat — accum natif `CostMPPlus` mode 0
    /// (cmd 2015 arg0 ; négatif = réduction).</summary>
    public double BonusMpCostFlat { get; set; }
    /// <summary>Aggro générée +% — accum natif `AttrHateRate`
    /// (cmd 2122 arg0).</summary>
    public double BonusHateRatePct { get; set; }

    // --- Slots d'arme (native GetWeaponItem : 7=main, 8=off, 10=2H) ---

    /// <summary>P-ATK de l'arme principale (col45 — addattr+0x3c).</summary>
    public int WeaponPAtk { get; init; }
    /// <summary>Variance % de l'arme principale (col47 — addattr+0x44).</summary>
    public int WeaponVariance { get; init; }
    /// <summary>Élément d'attaque de l'arme (col44 — CWeapon+0x128) ;
    /// 0 = physique neutre.</summary>
    public int WeaponElement { get; init; }
    /// <summary>Précision de l'arme (col51 — addattr+0x54, lu dans
    /// PhysicalHitRate séparément d'EquipHit).</summary>
    public int WeaponHit { get; init; }
    /// <summary>Critique de l'arme (col53 — addattr+0x58 ; le natif le
    /// pondère par la table de maîtrise enchant → 0 par défaut).</summary>
    public int WeaponCrit { get; init; }
    /// <summary>M-ATK de l'arme principale (col46 — addattr+0x40).</summary>
    public int WeaponMAtk { get; init; }
    /// <summary>Kind de l'arme principale (S_Item col_kind) — gate de
    /// `CanParry` natif (2H : 0x14-0x17, 0x1b-0x21).</summary>
    public int WeaponKind { get; init; }
    /// <summary>Kind de l'item en main secondaire — gate de
    /// `CanBlock` natif (bouclier kind 0x22 uniquement).</summary>
    public int OffWeaponKind { get; init; }

    /// <summary>Arme secondaire présente ET dual-wield actif (classe
    /// twin 0x14 ou licence UseTwoWeapon — char+0x47c/0x77e natifs).</summary>
    public bool DualWield { get; init; }
    /// <summary>P-ATK de l'arme secondaire (col45 — addattr+0x3c).</summary>
    public int OffWeaponPAtk { get; init; }
    /// <summary>Variance % de l'arme secondaire.</summary>
    public int OffWeaponVariance { get; init; }
    /// <summary>Précision de l'arme secondaire (pondérée ×para306 en
    /// dual — addattr+0x54 × 0.3 dans PhysicalHitRate).</summary>
    public int OffWeaponHit { get; init; }
    /// <summary>Critique de l'arme secondaire.</summary>
    public int OffWeaponCrit { get; init; }
    /// <summary>M-ATK de l'arme secondaire.</summary>
    public int OffWeaponMAtk { get; init; }
    /// <summary>Même kind d'arme main/off (désasm MeleeDamage : kinds
    /// identiques → maîtrise appliquée une fois ; différents → les deux
    /// tables s'additionnent — toutes deux 0 par défaut).</summary>
    public bool OffWeaponSameKind { get; init; }

    // ======================== ENTRÉES MONSTRE ========================

    /// <summary>Puissance d'attaque (S_Monster col14 →
    /// CMonsterTemplate+0x4c — PhysicalAttack/MagicDamage la partagent
    /// chez le monstre).</summary>
    public int MonsterAttack { get; init; }
    /// <summary>Variance d'attaque % (col15 → template+0x44).</summary>
    public int MonsterVariance { get; init; }
    /// <summary>Élément d'attaque (col13 → template+0x70).</summary>
    public int MonsterElement { get; init; }
    /// <summary>Défense physique (col18 → template+0x5c —
    /// PhysicalDefence(CMonster) @0x83f46e0).</summary>
    public int MonsterDefence { get; init; }
    /// <summary>Base de précision (col19 → template+0x52).</summary>
    public int MonsterHit { get; init; }
    /// <summary>Base d'esquive (col20 → template+0x50).</summary>
    public int MonsterDodge { get; init; }
    /// <summary>Crit physique (col21 → template+0x54).</summary>
    public int MonsterCrit { get; init; }
    /// <summary>Dégâts crit phys % (col22 → template+0x58).</summary>
    public int MonsterCritDamage { get; init; }
    /// <summary>Crit magique (col23 → template+0x56 —
    /// MagicCriticalRate(CMonster) @0x83f45f0).</summary>
    public int MonsterMagicCrit { get; init; }
    /// <summary>Dégâts crit mag % (col24 → template+0x5a —
    /// MagicCriticalDamage(CMonster) @0x83f45a0).</summary>
    public int MonsterMagicCritDamage { get; init; }
    /// <summary>Résistances élémentaires (cols25-33 → template+0xa8 —
    /// bloc SResistAttribute de 9 s16).</summary>
    public int[] MonsterResists { get; init; } = new int[9];

    /// <summary>Construit le snapshot monstre depuis la définition
    /// S_Monster — mappage CMonsterData→CMonsterTemplate prouvé
    /// (atk col14→+0x4c, variance col15→+0x44, def col18→+0x5c,
    /// hit col19→+0x52, dodge col20→+0x50, crits cols21-24→+0x54..+0x5a,
    /// résists cols25-33→+0xa8, élément col13→+0x70 ; la def magique
    /// template+0x60 n'est jamais écrite → 0 natif).</summary>
    public static CombatantSnapshot FromMonster(
        MonsterParser.MonsterDef d) => new()
    {
        IsMonster = true,
        Level = Math.Max(d.Level, 1),
        MonsterAttack = d.AttackPower,
        MonsterVariance = d.AttackVariance,
        MonsterElement = d.AttackElement,
        MonsterDefence = d.PhysicalDef,
        MonsterHit = d.Hit,
        MonsterDodge = d.Dodge,
        MonsterCrit = d.PhysicalCrit,
        MonsterCritDamage = d.PhysicalCritDamage,
        MonsterMagicCrit = d.MagicCrit,
        MonsterMagicCritDamage = d.MagicCritDamage,
        MonsterResists = d.Resistances?.Length >= 9
            ? d.Resistances : new int[9],
    };

    /// <summary>Construit le snapshot joueur — même agrégation que
    /// `SCharAttribute::SetData`/`RefreshEQAttr` natifs : stats BRUTES
    /// S_Level + agrégat équipement (le % classe s'applique dans les
    /// formules, pas ici), def naturelle col4, ItemDefs des deux mains.
    /// `twoWeaponLicensed` = licence deux-armes (char+0x77e) — le dual
    /// est aussi actif pour la classe twin 0x14
    /// (`param_1[0x47c]==0x14` natif).</summary>
    /// <param name="statPercents">S_Class StatPercents — [4]=str%,
    /// [5]=agi%, [6]=int%, [8]=lck% (cols17-21).</param>
    public static CombatantSnapshot FromPlayer(int level,
        ProgressionParsers.LevelRow? lvlRow, int[]? statPercents,
        Eden.Core.Game.Services.EquipmentStats eq,
        ItemParser.ItemDef? mainDef, ItemParser.ItemDef? offDef,
        bool twoWeaponLicensed, int classId,
        float moveSpeedBonusPct = 0f)
    {
        int Pct(int i) => statPercents != null
            && statPercents.Length > i ? statPercents[i] : 100;
        return new CombatantSnapshot
        {
            Level = Math.Max(level, 1),
            StrTotal = (lvlRow?.Str ?? 0) + eq.Str,
            AgiTotal = (lvlRow?.Con ?? 0) + eq.Agi,
            IntTotal = (lvlRow?.Int ?? 0) + eq.Int,
            WisTotal = (lvlRow?.Wis ?? 0) + eq.Wis,
            LckTotal = (lvlRow?.Lck ?? 0) + eq.Lck,
            GearCore = new[] { eq.Str, eq.Int, eq.Agi, eq.Wis, eq.Lck },
            StrPct = Pct(4), AgiPct = Pct(5), IntPct = Pct(6),
            WisPct = Pct(7), LckPct = Pct(8),
            EquipPAtk = eq.PAtk, EquipMAtk = eq.MAtk,
            EquipDefence = eq.Def,
            EquipHit = eq.Hit, EquipDodge = eq.Dodge,
            EquipCrit = eq.Crit, EquipCritDamage = eq.CritDamage,
            EquipMagicCrit = eq.MagicCrit,
            EquipMagicCritDamage = eq.MagicCritDamage,
            EquipResists = eq.Resists ?? new int[9],
            EquipAttackSpeed = eq.AttackSpeed,
            EquipCastSpeed = eq.CastSpeed,
            EquipParry = eq.Parry,
            EquipBlockRate = eq.Block,
            EquipHeal = eq.Heal,
            EquipBeHealed = eq.BeHealed,
            WeaponPAtk = mainDef?.PhysicalAttack ?? 0,
            WeaponMAtk = mainDef?.MagicAttack ?? 0,
            WeaponVariance = mainDef?.PhysicalVariance ?? 0,
            WeaponElement = mainDef?.AttackElement ?? 0,
            WeaponHit = mainDef?.Hit ?? 0,
            WeaponCrit = mainDef?.Critical ?? 0,
            WeaponKind = mainDef?.Kind ?? 0,
            OffWeaponKind = offDef?.Kind ?? 0,
            OffWeaponPAtk = offDef?.PhysicalAttack ?? 0,
            OffWeaponMAtk = offDef?.MagicAttack ?? 0,
            OffWeaponVariance = offDef?.PhysicalVariance ?? 0,
            OffWeaponHit = offDef?.Hit ?? 0,
            OffWeaponCrit = offDef?.Critical ?? 0,
            OffWeaponSameKind = mainDef != null && offDef != null
                && mainDef.Kind == offDef.Kind,
            LevelDefence = lvlRow?.NaturalDef ?? 0,
            DualWield = offDef != null
                && (classId == 0x14 || twoWeaponLicensed),
            MoveSpeedBonusPct = moveSpeedBonusPct,
        };
    }

    /// <summary>Verse tous les canaux Bonus* d'une
    /// <see cref="Eden.Core.Game.Stats.StatContribution"/> dans le
    /// snapshot — miroir des accumulateurs CKindData natifs remplis
    /// par les CEC_Attr* des enchants (licences, talents, titres,
    /// buffs, monture). Appelé à l'application/retrait d'un statut :
    /// sans ça les bonus ne vivaient que dans l'émission 296
    /// (affichage) et jamais dans les formules de dégâts réelles
    /// (bug live « buffs sans effet sur les stats »).</summary>
    public void ApplyContribution(
        Eden.Core.Game.Stats.StatContribution contrib)
    {
        // Cores : pre/pct du pipeline (gemmes, fortify, trait, talents…)
        // moins l'équipement déjà dans *Total. Le canal post (flat après
        // le %) n'a pas d'équivalent dans les formules natives de niveau.
        var cores = new[] { Eden.Core.Game.Stats.StatIndex.Str,
            Eden.Core.Game.Stats.StatIndex.Int,
            Eden.Core.Game.Stats.StatIndex.Agi,
            Eden.Core.Game.Stats.StatIndex.Wis,
            Eden.Core.Game.Stats.StatIndex.Lck };
        for (var i = 0; i < 5; i++)
        {
            CoreBonusPre[i] = contrib.Pre(cores[i]) - GearCore[i];
            CoreBonusPct[i] = contrib.Pct(cores[i]);
        }
        BonusDodgePercent = contrib.DodgePercent;
        BonusPAtk = contrib.PhysAttackAdd;
        BonusMAtk = contrib.MagicAttackAdd;
        BonusPAtkRate = contrib.PhysAttackRatePercent;
        BonusMAtkRate = contrib.MagicAttackRatePercent;
        BonusDefence = contrib.DefenceAdd;
        BonusDefenceRate = contrib.DefenceRatePercent;
        BonusHitRating = contrib.HitRatingAdd;
        BonusDodgeRating = contrib.DodgeRatingAdd;
        BonusCritRating = contrib.CritRatingAdd;
        BonusMagicCritRating = contrib.MagicCritRatingAdd;
        BonusCritRatePercent = contrib.CritRatePercent;
        BonusMagicCritRatePercent = contrib.MagicCritRatePercent;
        BonusCritDamage = contrib.CritDamageAdd;
        BonusMagicCritDamage = contrib.MagicCritDamageAdd;
        BonusAttackSpeed = contrib.AttackSpeedAdd;
        BonusAttackSpeedPercent = contrib.AttackSpeedRatePercent;
        BonusCastSpeed = contrib.CastSpeedAdd;
        BonusCastSpeedPercent = contrib.CastSpeedRatePercent;
        BonusMoveSpeedPct = contrib.MoveSpeedPercent;
        BonusBlockRate = contrib.BlockPercentAdd;
        BonusParry = contrib.ParryAdd;
        BonusHitRatePercent = contrib.HitRatePercent;
        BonusDodgeRatePercent = contrib.DodgeRatePercent;
        BonusHealGivenPct = contrib.HealGivenPercent;
        BonusHealTakenPct = contrib.HealTakenPercent;
        BonusHealGivenFlat = contrib.HealGivenFlat;
        BonusHealTakenFlat = contrib.HealTakenFlat;
        BonusMpCostFlat = contrib.MpCostFlat;
        BonusHateRatePct = contrib.HateRatePercent;
        for (var i = 0; i < 9; i++)
            BonusResists[i] = contrib.ResistAdds[i];
        for (var i = 0; i < 17; i++)
            BonusWeaponDamageRatePct[i] = contrib.WeaponDamageRatePct[i];
    }
}
