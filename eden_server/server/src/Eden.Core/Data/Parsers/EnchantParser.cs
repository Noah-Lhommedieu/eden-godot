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
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    public static List<EnchantDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var enchants = new List<EnchantDef>(table.Rows.Count);

        foreach (var row0 in table.Rows)
        {
            var row = row0;
            // c_enchant EDENFR = |V.2|60| : le schéma S_ |V.3|61|
            // moins la colonne « groupe elf-pet » en index 6 — on la
            // réinsère vide pour que tous les index ≥6 s'alignent.
            if (row.Length == 60)
            {
                row = new string[61];
                Array.Copy(row0, 0, row, 0, 6);
                row[6] = string.Empty;
                Array.Copy(row0, 6, row, 7, 54);
            }
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

            enchants.Add(e);
        }

        return enchants;
    }
}
