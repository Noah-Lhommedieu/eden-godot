namespace Eden.Core.Game.MagicBox;

/// <summary>
/// Tables constantes de la boîte magique — extraites du .rodata du
/// ZoneServer natif (ELF32, mappées via segments de chargement).
///
/// `ConverEnergyRate`, `ConverEnergyAmount` et
/// `ScouringNeedEquipAmount` ne contiennent pas des valeurs de jeu mais
/// des **indices vers `CFormula::AllFormulaParas`** (le tableau de
/// S_Parameter.ini) — l'accès natif est
/// `AllFormulaParas[table[type*5 + n]]`. Les vraies valeurs vivent donc
/// dans S_Parameter.ini et sont lues via <see cref="MagicBoxService"/>.
///
/// `QualityNeed`/`ScouringQualityNeed` sont des masques directs u16.
///
/// Preuves : désasm `CItemFactory::CheckMagicBoxCosts` @0x8088200 et
/// `CItemFactory::DoMagicBox` @0x80a8b90
/// (`*(short*)(GameData::ConverEnergyRate + type*10 + slot*2)` →
/// `*(float*)(AllFormulaParas + sVar49*4)`).
/// </summary>
public static class MagicBoxTables
{
    /// <summary>Param ids S_Parameter du taux d'énergie par sacrifice —
    /// rodata natif, u16[15], index `[type*5 + bucket]` :
    /// type0(arme) → 624-628 {0, 0.1, 0.3, 0.4, 0.6},
    /// type1(armure) → 629-633 {1, 3, 6, 0, 20},
    /// type2(accessoire) → 634-638 {1, 3, 6, 0, 20}.
    /// Bucket : qualité{0→0, 2→1, 4→2, 3→3, 5/magic→4}.</summary>
    public static readonly ushort[] ConverEnergyRateParam =
        { 624, 625, 626, 627, 628, 629, 630, 631, 632, 633,
          634, 635, 636, 637, 638 };

    /// <summary>Param ids S_Parameter du seuil d'énergie par star —
    /// rodata natif, u16[15], index `[type*5 + star]` :
    /// type0 → 609-613 {500, 1500, 3600, 7500, 20000},
    /// type1 → 614-618 {8, 32, 128, 405, 1350},
    /// type2 → 619-623 {6, 25, 102, 270, 900}.</summary>
    public static readonly ushort[] ConverEnergyAmountParam =
        { 609, 610, 611, 612, 613, 614, 615, 616, 617, 618,
          619, 620, 621, 622, 623 };

    /// <summary>Param ids S_Parameter du nombre d'équipements requis en
    /// scouring — rodata natif, u16[15], index `[(star-1) + type*5]` :
    /// type0 → 639-643 {5, 5, 7, 7, 8},
    /// type1 → 644-648 {2, 2, 4, 5, 6},
    /// type2 → 649-653 {1, 1, 3, 4, 5}.</summary>
    public static readonly ushort[] ScouringNeedEquipAmountParam =
        { 639, 640, 641, 642, 643, 644, 645, 646, 647, 648,
          649, 650, 651, 652, 653 };

    /// <summary>Masques qualité requis en conversion — rodata natif
    /// u16[15] direct, index `[type*5 + star]` ; bits {1,2,4,8,0x10}
    /// = buckets qualité {0,2,4,3,5/magic}.</summary>
    public static readonly ushort[] QualityNeed =
        { 2, 14, 12, 28, 16, 3, 3, 3, 6, 20, 3, 3, 3, 6, 20 };

    /// <summary>Masques qualité requis en scouring — rodata natif
    /// u16[15] direct, index `[(star-1) + type*5]`.</summary>
    public static readonly ushort[] ScouringQualityNeed =
        { 2, 12, 12, 16, 16, 2, 2, 2, 4, 16, 2, 2, 2, 4, 16 };

    /// <summary>Bucket qualité natif d'un item — DoMagicBox @0x80a8b90 :
    /// `magic≠0` → 0x10, sinon switch `quality` {0→1, 2→2, 4→4, 3→8,
    /// 5→0x10, autre→0}. Retourne aussi l'index du slot de taux.</summary>
    public static int QualityBucket(int quality, int magic)
    {
        if (magic != 0) return 0x10;
        return quality switch
        {
            0 => 0x01,
            2 => 0x02,
            4 => 0x04,
            3 => 0x08,
            5 => 0x10,
            _ => 0,
        };
    }

    /// <summary>Slot de taux ConverEnergyRate pour un bucket qualité
    /// (switch natif : 0x01→slot0, 0x02→slot1, 0x04→slot2, 0x08→slot3,
    /// 0x10→slot4 ; 0 → -1 = qualité invalide).</summary>
    public static int RateSlot(int bucketMask) => bucketMask switch
    {
        0x01 => 0, 0x02 => 1, 0x04 => 2, 0x08 => 3, 0x10 => 4, _ => -1,
    };

    /// <summary>Type natif de l'item principal — flags hauts col11
    /// (CItemData+0x34) : bit 8 → 0 (arme), 0x20 → 1 (armure),
    /// 0x10 → 2 (accessoire). -1 = non éligible
    /// (CheckMagicBoxCosts "OpFlag type not match").</summary>
    public static int MainType(int flagsHi)
    {
        if ((flagsHi & 8) != 0) return 0;
        if ((flagsHi & 0x20) != 0) return 1;
        if ((flagsHi & 0x10) != 0) return 2;
        return -1;
    }

    /// <summary>AttrType combo associé au type — DoMagicBox : arme→4,
    /// armure→5, accessoire→6 (filtre `combo+0x14 == attrType` de
    /// GetRandMagicBoxItemCombo).</summary>
    public static int AttrTypeOf(int type) => type + 4;
}
