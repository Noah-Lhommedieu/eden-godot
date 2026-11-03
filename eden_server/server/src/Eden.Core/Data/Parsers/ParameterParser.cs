using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

public static class ParameterParser
{
    /// <summary>Ids utiles (extraits des descriptions Big5).</summary>
    public const int ConToHpMax = 1;
    public const int ConToHpRegen = 2;
    public const int WisToMpMax = 3;
    public const int StrToAttack = 5;
    public const int AgiToRangedAttack = 6;
    public const int IntToMagicAttack = 7;
    public const int ConToDefense = 8;
    public const int WisToMagicDefense = 9;
    public const int AgiToDodge = 10;
    public const int AgiToCrit = 11;
    public const int BaseCritChance = 12;
    public const int BaseCritDamage = 13;
    public const int IntToMagicCrit = 14;
    public const int BaseMagicCritDamage = 15;
    public const int UnarmedAttackSpeed = 17;
    public const int WalkSpeedPerSecond = 18;
    public const int DualWieldMainSpeedMod = 19;
    public const int DualWieldOffSpeedMod = 20;
    public const int DualWieldDamageMod = 24;
    public const int UnarmedAttackRange = 25;
    public const int BaseHitChance = 30;
    public const int DefenseModifier = 32;
    public const int WeaponEnchantBonus = 56;
    public const int ArmorEnchantBonus = 55;
    /// <summary>Coefficient du coût de réparation
    /// (AllFormulaParas+0x56c → index 347 — S_Parameter 347 = 0.2,
    /// « 裝備修復費用參數 » ; désasm `GetItemRepairPrice`
    /// @0x808c480).</summary>
    public const int RepairCostFactor = 347;
    /// <summary>Coefficient du prix de revente aux PNJ
    /// (AllFormulaParas+0x568 → index 346 — S_Parameter 346 = 0.05,
    /// « 物品回收價格參數 » ; désasm `CNE_CZ_ZoneServerSellItems::
    /// Execute` : or rendu = `SysPrice × 0.05 × dur/maxDur`, vente
    /// refusée si `SysPrice × 0.05 < 1.0`).</summary>
    public const int SellBackFactor = 346;
    public const int GuildMaxLevel = 454;
    public const int AuctionLevelLimit = 460;
    /// <summary>Prix d'expansion du stockage perso — S_Parameter
    /// 409/410 « 小屋倉庫第N排擴充金額 » (1500/3500 ; la 3e rangée
    /// 411 n'est jamais atteinte : `expand_storage_cnt` natif &lt; 2).
    /// Table vectorielle `CZoneServer+0x554` indexée par le compteur —
    /// désasm `CPlayerFactory::ExpandStorage` @0x80f2670.</summary>
    public const int StorageExpandPrice1 = 409;
    public const int StorageExpandPrice2 = 410;
    public const int MutualDivorceCost = 594;
    public const int UnilateralDivorceCost = 595;

    public sealed class ParameterSet
    {
        private readonly Dictionary<int, float> _values;
        private readonly Dictionary<int, string> _descriptions;

        public ParameterSet(Dictionary<int, float> values,
            Dictionary<int, string> descriptions)
        {
            _values = values;
            _descriptions = descriptions;
        }

        public float Get(int id, float fallback = 0f)
            => _values.TryGetValue(id, out var v) ? v : fallback;

        public string Description(int id)
            => _descriptions.TryGetValue(id, out var d) ? d : string.Empty;

        public int Count => _values.Count;
    }

    public static ParameterSet Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var values = new Dictionary<int, float>();
        var descriptions = new Dictionary<int, string>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 3 ||
                !int.TryParse(row[0], out var id) ||
                !float.TryParse(row[1],
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val))
                continue;
            values[id] = val;
            descriptions[id] = row[2];
        }
        return new ParameterSet(values, descriptions);
    }
}
