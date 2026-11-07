using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Enchant.ini — 24688 enchants/procs (|V.3|61|).
///
/// Systeme reverse: CEnchantMgr + CEC_Enchant* / CEC_OnAttackBuff* / CEC_OnBeAttack*.
/// Chaque enchant = une regle conditionnelle qui applique un sort/buff quand
/// une condition de trigger est remplie (attaque, coup recu, couche de buff...).
///
/// Colonnes (61):
///   [0]  Enchant ID (10001...)
///   [1]  Code groupe (E0302, E0132, J515...)
///   [3]  Spell/script reference (s14131)
///   [4]  Niveau de l'enchant
///   [5]  Nom/description courte (Big5: 判斷狂熱層數)
///   [7]  Flag actif (1)
///   [8]  Type de trigger (8208 = condition generique, autres = on-attack...)
///   [9]  Parametre 1 du trigger (spell id 3103, seuil 295...)
///   [10] Parametre 2 du trigger (spell cible 61056, valeur 100...)
///   [11] Parametre 3 (2, 1)
///   [12] Parametre 4
///   [16-20] Bloc secondaire: spell/result ids + valeurs (1921 100 61060 20 1)
///   [23-24] Spell ids additionnels (1823, 61056)
///   [37]  Chance de proc (1, 20, 50, 100)
///   [38-39] Spell applique + chance (637, 100)
///   [40]  Slots de commandes déclenchés au proc — liste `;` 1-based
///         dans les 4 blocs cmd+6vars (cols 9-36) ; natif : les blocs
///         listés sont DÉPLAÇÉS du vecteur passif vers le set de
///         déclenchement (CEnchantQuery::Deserialize @0x85ecf00 —
///         CEnchantData+0x38).
///   [41]  Événement déclencheur (CEnchantData+0x4c → SEffectData+0x10,
///         dispatch LoadAllEffectData @0x8394b10) : 1/2 = attaque
///         (self/opponent), 3/4 = coup reçu (self/opponent),
///         5 = cible, 6/7 = kill (self/opponent), 8/9 = être tué.
///   [42]  Chance du proc % (CEnchantData+0x50 → SEffectData+0x115).
///   [43]  Durée du sous-effet déclenché en s (+0x54 → +0x120).
///   [47]  Flag
///   [56]  Description longue (Big5)
///
/// NOTE: la semantique exacte par colonne reste a confirmer via decompilation
/// de CEnchantMgr::ReadData / CEC_Enchant*::Init — les Raw sont conserves.
/// </summary>
public static class EnchantParser
{
    public sealed class EnchantDef
    {
        public int Id { get; set; }
        public string GroupCode { get; set; } = string.Empty;   // E0302
        public string SpellRef { get; set; } = string.Empty;    // s14131
        public int Level { get; set; }
        public string Name { get; set; } = string.Empty;        // Big5
        public bool Enabled { get; set; }
        public int TriggerType { get; set; }                    // 8208...
        public int[] TriggerParams { get; set; } = new int[4];  // cols 9-12
        public int[] SecondaryBlock { get; set; } = new int[5]; // cols 16-20
        public int SpellId1 { get; set; }                       // col 23
        public int SpellId2 { get; set; }                       // col 24
        public int ProcChance { get; set; }                     // col 37
        public int AppliedSpellId { get; set; }                 // col 38
        public int AppliedChance { get; set; }                  // col 39
        public string TriggerSlotsRaw { get; set; } = string.Empty; // col 40
        public int TriggerEvent { get; set; }                   // col 41
        public int TriggerChance { get; set; }                  // col 42
        public int TriggerDuration { get; set; }                // col 43
        public string Description { get; set; } = string.Empty; // col 56
        /// <summary>Champs « pierre de chance » (enchant cmd 8002
        /// porté par un item mall — ex. 55168 derrière 41469) lus par
        /// <c>CGameData::Integration</c> : bonus de taux (col10),
        /// « safe level » (col12 — &gt;0 → bornes ignorées), flag
        /// « lie l'item » (col13) et fenêtre d'usage basse/haute
        /// (cols 14/15, bornes égales X/X → fenêtre 1..X côté
        /// natif).</summary>
        public int ChanceBonus { get; set; }
        public int ChanceSafeLevel { get; set; }
        public int ChanceBind { get; set; }
        public int ChanceLower { get; set; }
        public int ChanceUpper { get; set; }
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>Réinsère la cellule vide d'index 6 sur une ligne FR
    /// (61 cellules, ou 56/62 si <paramref name="headerCols"/> = 60 —
    /// c_itemmallenchant |V.2|60| a 62 cellules = schéma FR sans la
    /// colonne elf-pet + une colonne de fin en plus : les blocs de
    /// commandes y sont à l'index 8+7k, le schéma S_ les attend 9+7k —
    /// 55869 portait `2047|1` lu comme cmd 1). Les lignes S_ (62/57,
    /// header 61) et l'anomalie 46131 (55) sont inchangées.</summary>
    public static string[] NormalizeRow(string[] row0, int headerCols)
    {
        var fr = row0.Length == 61 ||
            (headerCols == 60 && row0.Length is 56 or 62);
        if (!fr) return row0;
        var row = new string[row0.Length + 1];
        Array.Copy(row0, 0, row, 0, 6);
        row[6] = string.Empty;
        Array.Copy(row0, 6, row, 7, row0.Length - 6);
        return row;
    }

    public static List<EnchantDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var enchants = new List<EnchantDef>(table.Rows.Count);

        foreach (var row0 in table.Rows)
        {
            // KB-01 : c_enchant EDENFR = |V.2|60| (60 cols + « | » terminal
            // = 61 cellules, ou 56 si la fin est tronquée) : le schéma S_
            // |V.3|61| (62/57 cellules) a en plus la colonne « groupe
            // elf-pet » en index 6 — on la réinsère vide pour aligner tous
            // les index ≥6. Ancien garde « Length == 60 » : jamais vrai.
            var row = NormalizeRow(row0, table.ColumnCount);
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;

            var e = new EnchantDef
            {
                Id = id,
                GroupCode = row.Length > 1 ? row[1] : string.Empty,
                SpellRef = row.Length > 3 ? row[3] : string.Empty,
                Level = IniTable.GetInt(row, 4),
                Name = row.Length > 5 ? row[5] : string.Empty,
                Enabled = IniTable.GetInt(row, 7) != 0,
                TriggerType = IniTable.GetInt(row, 8),
                SpellId1 = IniTable.GetInt(row, 23),
                SpellId2 = IniTable.GetInt(row, 24),
                ProcChance = IniTable.GetInt(row, 37),
                AppliedSpellId = IniTable.GetInt(row, 38),
                AppliedChance = IniTable.GetInt(row, 39),
                TriggerSlotsRaw = row.Length > 40 ? row[40] : string.Empty,
                TriggerEvent = IniTable.GetInt(row, 41),
                TriggerChance = IniTable.GetInt(row, 42),
                TriggerDuration = IniTable.GetInt(row, 43),
                Description = row.Length > 56 ? row[56] : string.Empty,
                Raw = row,
            };

            for (var i = 0; i < 4; i++)
                e.TriggerParams[i] = IniTable.GetInt(row, 9 + i);
            for (var i = 0; i < 5; i++)
                e.SecondaryBlock[i] = IniTable.GetInt(row, 16 + i);

            // Kind 8002 « pierre de chance » (CGameData::Integration
            // @0x85995b0) : param[0] = cmd 0x1f42 (col9) ; param[1]
            // (col10) = bonus % ; param[5] (col14) > 0 → safe-level
            // (niveau ≤ → succès garanti) et la fenêtre n'est pas lue ;
            // sinon param[9]/[11] (cols 18/20) = fenêtre lower/upper
            // validée 1-15 qui borne l'usage (bornes égales X/X →
            // fenêtre 1..X, bizarrerie native) ; param[7] (col16) =
            // flag « lie la cible ».
            if (e.TriggerParams[0] == 0x1f42)
            {
                e.ChanceBonus = IniTable.GetInt(row, 10);
                e.ChanceSafeLevel = IniTable.GetInt(row, 14);
                e.ChanceBind = IniTable.GetInt(row, 16);
                if (e.ChanceSafeLevel == 0)
                {
                    var lo = IniTable.GetInt(row, 18);
                    var up = IniTable.GetInt(row, 20);
                    if (lo is >= 1 and <= 15 && up is >= 1 and <= 15
                        && lo <= up)
                    {
                        e.ChanceLower = lo == up ? 1 : lo;
                        e.ChanceUpper = up;
                    }
                }
            }

            enchants.Add(e);
        }

        return enchants;
    }
}
