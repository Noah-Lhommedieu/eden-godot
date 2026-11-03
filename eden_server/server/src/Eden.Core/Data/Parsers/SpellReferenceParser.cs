namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_SpellReference.ini — table par palier de niveau de
/// sort (clé = niveau cible). Deux usages natifs :
///   · `CSpellMasterFactory::SetSpellLevel` (désasm @0x8156280) pour le
///     coût d'amélioration :
///       `SSpellReference+0x78` = taux CP   → col ini 30
///       `SSpellReference+0x7c` = taux or   → col ini 31
///     Coût natif = `SSpellData+0x88` (S_Spell col43) × taux × 0.01.
///   · `CalculateMagicSkillDamage`/`CalculatePhysicalSkillDamage`
///     (@0x83fd010/@0x83fd8xx) pour l'échelle de dégâts par niveau :
///       `SSpellReference+0x08` = coef physique → col ini 2
///       `SSpellReference+0x0c` = coef magique  → col ini 3
///       `SSpellReference+0x10` = coef soin     → col ini 4
///     (désasm `CSpellReferenceQuery::Deserialize` @0x8627b80 :
///     lectures séquentielles des champs ; le coef soin est lu par
///     `CalculateMagicSkillHeal` @0x83f9310 — vérifié données :
///     palier 1 → phys=74, mag=186, heal=84).
/// </summary>
public static class SpellReferenceParser
{
    /// <summary>Taux et coefficients pour un palier (entiers % natifs).</summary>
    public readonly struct LevelCost
    {
        /// <summary>Coefficient de coût MP — col 1 → `SSpellReference+0x04`.
        /// `CCharFormula::CostMp` @0x83f44d0 :
        /// `ROUND(((ref+4>>16)×c + (ref+4&0xffff)) × base(SSpellData+0xbc)
        /// × 0.01)` puis modulation BuffCostRate du lanceur (char+0x8c+0xb4)
        /// — non modélisée. Les lignes réelles sont < 65536 → hi=0, le
        /// coût se réduit à `col1 × base × 0.01`.</summary>
        public int MpCoef { get; init; }
        /// <summary>Taux CP — col 30 → `SSpellReference+0x78`.</summary>
        public int CpRate { get; init; }
        /// <summary>Taux or — col 31 → `SSpellReference+0x7c`.</summary>
        public int GoldRate { get; init; }
        /// <summary>Coefficient de croissance physique — col 2 →
        /// `SSpellReference+0x08`. Terme `power × coef / 100` ajouté au
        /// dégât de mêlée dans CalculatePhysicalSkillDamage.</summary>
        public int PhysicalCoef { get; init; }
        /// <summary>Coefficient de croissance magique — col 3 →
        /// `SSpellReference+0x0c`. Terme `power × coef / 100` ajouté à
        /// l'attaque magique dans CalculateMagicSkillDamage.</summary>
        public int MagicCoef { get; init; }
        /// <summary>Coefficient de croissance de soin — col 4 →
        /// `SSpellReference+0x10`. Terme `healPower × coef / 100` de
        /// `CalculateMagicSkillHeal` @0x83f9310.</summary>
        public int HealCoef { get; init; }

        // --- Références d'attributs (commandes d'enchant mode 1) ---
        // `CEC_AttrBase::Init` @0x84f4xxx switch stat→offset :
        //   stat 3 (Str) → +0x54 (col22), stat 4 (Dex) → +0x4c (col18),
        //   stat 5 (Int) → +0x4e (col19), stat 6 (Spr) → +0x50 (col20),
        //   stat 7 (Luk) → +0x52 (col21).
        /// <summary>Réf. STR par palier — col 22 → `SSpellReference+0x54`.
        /// Échelle `AttrBase Str 1` (cmd 2081 var0) :
        /// `ref × var × 0.01`.</summary>
        public int StrRef { get; init; }
        /// <summary>Réf. DEX/AGI — col 18 → `+0x4c` (cmd 2081 var1).</summary>
        public int DexRef { get; init; }
        /// <summary>Réf. INT — col 19 → `+0x4e` (cmd 2081 var2).</summary>
        public int IntRef { get; init; }
        /// <summary>Réf. SPR/WIS — col 20 → `+0x50` (cmd 2081 var3).</summary>
        public int SprRef { get; init; }
        /// <summary>Réf. LUK/LCK — col 21 → `+0x52` (cmd 2081 var4).</summary>
        public int LukRef { get; init; }
        /// <summary>Réf. DEF — col 23 → `SSpellReference+0x58`
        /// (`CEC_AttrDefence::Execute` @0x85037f2, cmd 2082 var0).</summary>
        public int DefenceRef { get; init; }
        /// <summary>Réf. précision — col 24 → `+0x5c`
        /// (`CEC_AttrHitRate::Execute`, cmd 2082 var1).</summary>
        public int HitRef { get; init; }
        /// <summary>Réf. esquive — col 25 → `+0x5e` (lecture `strtol`
        /// manuelle dans Deserialize — cmd 2082 var2).</summary>
        public int DodgeRef { get; init; }
        /// <summary>Réf. vitesse d'attaque — col 26 → `+0x60`
        /// (`CEC_AttrAttackSpeedPlus::Execute`, cmds 2056/2072 mode 1).</summary>
        public int AttackSpeedRef { get; init; }
        /// <summary>Réf. temps de cast — col 27 → `+0x62`
        /// (`CEC_AttrCastTimePlus::Execute`, cmd 2059 var0).</summary>
        public int CastTimeRef { get; init; }
    }

    /// <summary>Parse la table indexée par niveau de sort (col 0).</summary>
    public static Dictionary<int, LevelCost> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var map = new Dictionary<int, LevelCost>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 32 || !int.TryParse(row[0], out var lv))
                continue;
            map[lv] = new LevelCost
            {
                MpCoef = IniTable.GetInt(row, 1),
                CpRate = IniTable.GetInt(row, 30),
                GoldRate = IniTable.GetInt(row, 31),
                PhysicalCoef = IniTable.GetInt(row, 2),
                MagicCoef = IniTable.GetInt(row, 3),
                HealCoef = IniTable.GetInt(row, 4),
                StrRef = IniTable.GetInt(row, 22),
                DexRef = IniTable.GetInt(row, 18),
                IntRef = IniTable.GetInt(row, 19),
                SprRef = IniTable.GetInt(row, 20),
                LukRef = IniTable.GetInt(row, 21),
                DefenceRef = IniTable.GetInt(row, 23),
                HitRef = IniTable.GetInt(row, 24),
                DodgeRef = IniTable.GetInt(row, 25),
                AttackSpeedRef = IniTable.GetInt(row, 26),
                CastTimeRef = IniTable.GetInt(row, 27),
            };
        }
        return map;
    }
}
