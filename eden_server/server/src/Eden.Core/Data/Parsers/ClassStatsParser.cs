using Eden.Core.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Class.ini — definitions des classes par race.
///
/// Format decode (MyCrypt):
///   Header: |V.6|65|
///   305 lignes = 61 classes x 5 races
///   IDs: 1-61 (race 1), 101-161 (race 2), 201-261 (race 3),
///        301-361 (race 4), 401-461 (race 5)
///
/// Colonnes:
///   [0]  Class ID
///   [1]  Nom (Big5)
///   [2]  Index classe de base / ordre d'affichage
///   [3]  Code classe (J009, J033...)
///   [4]  Description (Big5)
///   [5]  Nom du trait (Big5)
///   [6]  Description du trait (Big5)
///   [7]  Prerequis textuels (Big5)
///   [8]  Flag
///   [9]  (vide)
///   [10] Flags
///   [13] HP growth %   (toujours 100)
///   [14] MP growth %   (toujours 100)
///   [15] STR %
///   [16] INT%
///   [17] CON%
///   [18] AGI/DEX%
///   [19] WIS/PSY%?     (eleve chez Bard/Cleric)
///   [20] LUK%?         (eleve chez Cleric=135)
///   [21] divers%       (~104-124)
///   [22-29]  IDs de certificats par palier (8 — CClassData+0x44,
///            octroyé au niveau de classe multiple de 10 :
///            GetLicense dans GainClassPoint ; voir CertificateIds)
///   [30-33]  IDs de sorts de classe — vecteur +0x50 (4)
///   [34-48]  IDs de sorts passifs/classe — vecteur +0x5c (15)
///   [49-54]  IDs de talents AA — vecteur +0x68 (6, NON octroyés
///            par GetClassSpell)
///   [55-64]  Progression par niveau: "level Axxxxx Axxxxx 0"
///            (10 paliers: lvl 1,10,20,...,90 avec 2 codes certificat)
/// </summary>
public class ClassStatsParser
{
    public class ClassStats
    {
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int HP { get; set; }
        public int MP { get; set; }
        public int STR { get; set; }
        public int DEX { get; set; }
        public int CON { get; set; }
        public int INT { get; set; }
        public int PSY { get; set; }
        public int AGI { get; set; }
    }

    /// <summary>
    /// Definition complete d'une classe (par race) depuis S_Class.ini.
    /// </summary>
    public sealed class ClassDefinition
    {
        public int Id { get; set; }
        public int RaceIndex => Id / 100;          // 0= race1, 1=race2...
        public int BaseClassIndex => Id % 100;     // 1-61
        public string Name { get; set; } = string.Empty;          // Big5
        public int DisplayIndex { get; set; }
        public string ClassCode { get; set; } = string.Empty;     // J009...
        public string Description { get; set; } = string.Empty;
        public string TraitName { get; set; } = string.Empty;
        public string TraitDescription { get; set; } = string.Empty;
        public string RequirementText { get; set; } = string.Empty;
        public int[] StatPercents { get; set; } = new int[9];     // cols 13-21 (+shift)
        /// <summary>Ids de certificats par palier de classe —
        /// `CClassData+0x44` = vector[8] natif (cols 22+shift..29+shift,
        /// `CClassQuery::Deserialize` @0x85ca7xx : 8 cellules après les
        /// 7 s16 stats). Index = `niveau de classe / 10 - 1` : le palier
        /// lv10 → [0], lv60 → [5] (GetLicense au level-up multiple de
        /// 10 — `GainClassPoint` @0x841c7xx). Ce sont des ids d'items
        /// S_Item (33xxx/34xxx), PAS des sorts — l'ancien champ
        /// « RacialSpellIds » recouvrait en fait les 6 premiers.</summary>
        public int[] CertificateIds { get; set; } = new int[8];
        /// <summary>Sorts de classe octroyés par
        /// `CSpellMasterFactory::GetClassSpell` (@0x8157230) — le
        /// natif itère UNIQUEMENT les vecteurs +0x50 (4 cellules
        /// c30-33) et +0x5c (15 cellules c34-48), gatés par
        /// `char+0x47e` = niveau de classe. Le vecteur +0x68
        /// (c49-54, <see cref="TalentIds"/>) n'y figure PAS —
        /// certaines classes y mettent de vrais ids S_Spell (ex.
        /// classe 17 → 50583-88) : les inclure ici les octroierait
        /// à tort.</summary>
        public int[] ClassSpellIds { get; set; } = Array.Empty<int>(); // cols 30-48 (+shift)
        /// <summary>Talents AA — `CClassData+0x68` = vector[6]
        /// (cols 49+shift..54+shift, 52581-86 pour guerrier).</summary>
        public int[] TalentIds { get; set; } = new int[6];
        /// <summary>Archétype d'arme de la classe — `CClassData+0x28`
        /// natif (col 12+shift = `certsBase - 10`) ∈ {1,2,3}. Lu par
        /// `CCharacter::GainEquipExp` @0x841b1d0 pour la compatibilité
        /// Mana Infusion : kinds d'arme {1,4,7,10,13}→arch 1,
        /// {2,5,8,11,14}→arch 2, {3,6,9,12,15}→arch 3. Hors {1,2,3}
        /// → aucune restriction (le switch natif ne rejette pas).</summary>
        public int Archetype { get; set; }
        public List<LevelEntry> LevelProgression { get; set; } = new();  // cols 55-64 (+shift)
    }

    public sealed class LevelEntry
    {
        public int Level { get; set; }
        public string CertCode1 { get; set; } = string.Empty;  // A01101
        public string CertCode2 { get; set; } = string.Empty;  // A01201
        public int Flag { get; set; }
    }

    /// <summary>
    /// Parse S_Class.ini complet — retourne toutes les definitions de classes
    /// (305 entrees: 61 classes x 5 races).
    /// </summary>
    public static List<ClassDefinition> ParseClassDefinitions(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var defs = new List<ClassDefinition>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 22 || !int.TryParse(row[0], out var id))
                continue;

            var def = new ClassDefinition
            {
                Id = id,
                Name = row[1],
                DisplayIndex = IniTable.GetInt(row, 2),
                ClassCode = row[3],
                Description = row[4],
                TraitName = row[5],
                TraitDescription = row[6],
                RequirementText = row[7],
            };

            // Alignement par ancrage : les régions avant les stats ont
            // une largeur VARIABLE (le texte de description et les
            // prérequis contiennent des `|` → IniTable produit des
            // lignes de 66 à 130 cellules — classes 2/6 = 72/71,
            // classes avec description longue >120). Le décalage
            // `len - 66` ne fonctionne que si l'excédent est AVANT
            // les stats — les cellules en trop traînent aussi en fin
            // de ligne (texte multi-ligne à index >60) → classes 2/6
            // lisaient des ids de SORTS comme certificats (50225… →
            // GetLicense(spellId) → crash client à class lv10).
            // Ancre fiable : le bloc sorts (vecteurs +0x50/+0x5c =
            // 19 cellules d'ids S_Spell 50xxx) commence au premier
            // run de ≥4 cellules ≥50000 ; les certificats = les 8
            // cellules juste avant (CClassData+0x44), les stats = les
            // 9 avant eux, talents = 6 après, progression = 10 après.
            var spellBase = FirstSpellBlockIndex(row);
            if (spellBase < 0) spellBase = 30;   // fallback canonique
            var certsBase = spellBase - 8;
            // col12 (+shift) = CClassData+0x28 = archétype d'arme —
            // la position brute row[12] n'est PAS fiable (descriptions
            // contenant '|' décalent la ligne) : ancrage certsBase-10.
            def.Archetype = IniTable.GetInt(row, certsBase - 10);

            for (var i = 0; i < 9; i++)
                def.StatPercents[i] =
                    IniTable.GetInt(row, certsBase - 9 + i);

            for (var i = 0; i < 8; i++)
                def.CertificateIds[i] =
                    IniTable.GetInt(row, certsBase + i);

            var spells = new List<int>();
            for (var i = spellBase;
                 i <= spellBase + 18 && i < row.Length; i++)
            {
                var v = IniTable.GetInt(row, i);
                if (v > 0) spells.Add(v);
            }
            def.ClassSpellIds = spells.ToArray();

            for (var i = 0; i < 6; i++)
                def.TalentIds[i] =
                    IniTable.GetInt(row, spellBase + 19 + i);

            for (var i = spellBase + 25;
                 i <= spellBase + 34 && i < row.Length; i++)
            {
                var parts = row[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && int.TryParse(parts[0], out var lvl))
                {
                    def.LevelProgression.Add(new LevelEntry
                    {
                        Level = lvl,
                        CertCode1 = parts[1],
                        CertCode2 = parts[2],
                        Flag = parts.Length > 3 && int.TryParse(parts[3], out var f) ? f : 0,
                    });
                }
            }

            defs.Add(def);
        }

        return defs;
    }

    /// <summary>Premier index du bloc de sorts de la ligne — vecteurs
    /// natifs +0x50 (4 cellules) + +0x5c (15) = 19 cellules d'ids
    /// S_Spell (50xxx). Ancre : premier run de ≥4 cellules ≥ 50000.
    /// Le bloc de sorts ne commence jamais par des ids &lt;50000 dans
    /// les données natives (les spellmasters de classe sont tous
    /// ≥50000) — les certificats (33xxx/34xxx/21xxx/24xxx) restent
    /// sous le seuil.</summary>
    private static int FirstSpellBlockIndex(string[] row)
    {
        var run = 0;
        for (var i = 0; i < row.Length; i++)
        {
            run = IniTable.GetInt(row, i) >= 50000 ? run + 1 : 0;
            if (run >= 4) return i - 3;
        }
        return -1;
    }

    /// <summary>
    /// Parse S_Class.ini file — retourne les stats par (classId, level).
    /// Les stats par niveau proviennent de LevelProgression; les pourcentages
    /// de stats de StatPercents servent de base de calcul.
    /// </summary>
    public static Dictionary<(int classId, int level), ClassStats> Parse(string filePath)
    {
        var defs = ParseClassDefinitions(filePath);
        var stats = new Dictionary<(int, int), ClassStats>();

        foreach (var def in defs)
        {
            // Les niveaux listes explicitement dans LevelProgression
            foreach (var entry in def.LevelProgression)
            {
                stats[(def.Id, entry.Level)] = new ClassStats
                {
                    ClassId = def.Id,
                    Level = entry.Level,
                    HP = 100 * def.StatPercents[0] / 100 * entry.Level,
                    MP = 50 * def.StatPercents[1] / 100 * entry.Level,
                    STR = entry.Level * def.StatPercents[2] / 100,
                    DEX = entry.Level * def.StatPercents[5] / 100,
                    CON = entry.Level * def.StatPercents[4] / 100,
                    INT = entry.Level * def.StatPercents[3] / 100,
                    PSY = entry.Level * def.StatPercents[6] / 100,
                    AGI = entry.Level * def.StatPercents[7] / 100,
                };
            }
        }

        return stats;
    }

    /// <summary>
    /// Generate placeholder stats (fallback si le fichier n'est pas disponible).
    /// </summary>
    public static ClassStats GeneratePlaceholderStats(int classId, int level)
    {
        return new ClassStats
        {
            ClassId = classId,
            Level = level,
            HP = 100 + (level * 10),
            MP = 50 + (level * 5),
            STR = level,
            DEX = level,
            CON = level,
            INT = level,
            PSY = level,
            AGI = level
        };
    }

    /// <summary>
    /// Genere un dictionnaire complet de stats placeholder pour toutes les classes et niveaux.
    /// </summary>
    public static Dictionary<(int classId, int level), ClassStats> GenerateAllPlaceholderStats(
        int maxClassId = 60,
        int maxLevel = 100)
    {
        var stats = new Dictionary<(int, int), ClassStats>();

        for (int classId = 1; classId <= maxClassId; classId++)
        {
            for (int level = 1; level <= maxLevel; level++)
            {
                stats[(classId, level)] = GeneratePlaceholderStats(classId, level);
            }
        }

        return stats;
    }
}
