using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Eden.Core.Logging;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour T_Class.ini - Contient les données de 60 classes (1-20 base, 21-60 awakened)
/// Encoding: latin-1 (ISO-8859-1)
/// Format: pipe-separated values avec en-têtes
/// </summary>
public class ClassDataParser
{
    public class ClassData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Trait { get; set; } = string.Empty;
        public string DetailedDescription { get; set; } = string.Empty;
        public ClassRequirements Requirements { get; set; } = new();
    }

    public class ClassRequirements
    {
        public int MinCharacterLevel { get; set; }
        public List<ClassPrerequisite> Prerequisites { get; set; } = new();
        public List<string> QuestRequirements { get; set; } = new();
    }

    public class ClassPrerequisite
    {
        public int ClassId { get; set; }
        public int MinLevel { get; set; }
    }

    /// <summary>
    /// Parse le fichier T_Class.ini et retourne toutes les classes
    /// </summary>
    public static Dictionary<int, ClassData> ParseClassFile(string filePath)
    {
        var classes = new Dictionary<int, ClassData>();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        try
        {
            // Lire le fichier avec latin-1 encoding
            var lines = File.ReadAllLines(filePath, Encoding.GetEncoding("ISO-8859-1"));

            // Skip header lines (6|, column indices, column names)
            var dataStartIndex = FindDataStartIndex(lines);

            // Parser chaque classe
            for (int i = dataStartIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                try
                {
                    var classData = ParseClassLine(line);
                    if (classData != null)
                    {
                        classes[classData.Id] = classData;
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue parsing
                    EdenLog.Write($"Warning: Failed to parse line {i}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse class file: {ex.Message}", ex);
        }

        return classes;
    }

    /// <summary>
    /// Trouve l'index de la première ligne de données (après les en-têtes)
    /// </summary>
    private static int FindDataStartIndex(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Skip header lines
            if (line == "6|" || line.StartsWith("1|2|5|6|7|8|"))
                continue;
            
            if (line.StartsWith("ID|Name|"))
                continue;

            // First data line starts with a number
            if (char.IsDigit(line[0]))
                return i;
        }

        throw new InvalidOperationException("Could not find data start in class file");
    }

    /// <summary>
    /// Parse une seule ligne de classe
    /// Format: ID|Name|ShortDesc|Trait|DetailedDesc|Requirements|
    /// </summary>
    private static ClassData? ParseClassLine(string line)
    {
        // Split par pipe, mais attention aux descriptions qui peuvent contenir des pipes
        var parts = line.Split('|');
        
        if (parts.Length < 6)
            return null;

        if (!int.TryParse(parts[0].Trim(), out var classId))
            return null;

        // Filter ranges: 1-20 (base), 21-60 (awakened)
        // Ignore 101-120, 201-220 etc (duplicates)
        if ((classId > 60 && classId < 101) || (classId > 120 && classId < 201))
            return null;

        // For now, parse only primary classes (1-60)
        if (classId > 60 && (classId % 100) > 60)
            return null;

        var classData = new ClassData
        {
            Id = classId,
            Name = parts[1].Trim(),
            ShortDescription = parts[2].Trim(),
            Trait = parts[3].Trim(),
            DetailedDescription = parts[4].Trim(),
            Requirements = ParseRequirements(parts[5])
        };

        return classData;
    }

    /// <summary>
    /// Parse la section Requirements
    /// Format:
    /// "Class Requirement: Beginning Job|"
    /// "* Character Level X|"
    /// "* ClassName Level Y|"
    /// "*Complete the quest: ..."
    /// </summary>
    private static ClassRequirements ParseRequirements(string requirementText)
    {
        var reqs = new ClassRequirements
        {
            MinCharacterLevel = 1  // Default
        };

        if (string.IsNullOrEmpty(requirementText))
            return reqs;

        // Split par | et traiter chaque ligne
        var lines = requirementText.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            // Ligne de catégorie "Class Requirement:" - skip
            if (trimmedLine.StartsWith("Class Requirement", StringComparison.OrdinalIgnoreCase))
                continue;

            // Ligne "Beginning Job" - skip (racine)
            if (trimmedLine == "Beginning Job")
                continue;

            // Ligne quête: "*Complete the quest:"
            if (trimmedLine.StartsWith("*Complete", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("*Finish", StringComparison.OrdinalIgnoreCase))
            {
                reqs.QuestRequirements.Add(trimmedLine.TrimStart('*').Trim());
                continue;
            }

            // Ligne niveau caractère: "* Character Level X"
            if (trimmedLine.StartsWith("* Character Level", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(
                    Regex.Match(trimmedLine, @"\d+").Value,
                    out var charLevel))
                {
                    reqs.MinCharacterLevel = charLevel;
                }
                continue;
            }

            // Ligne classe prérequise: "* ClassName Level X"
            if (trimmedLine.StartsWith("*"))
            {
                ParseClassPrerequisite(trimmedLine, reqs);
            }
        }

        return reqs;
    }

    /// <summary>
    /// Parse une ligne de prérequis de classe
    /// Format: "* ClassName Level X"
    /// </summary>
    private static void ParseClassPrerequisite(string line, ClassRequirements reqs)
    {
        // Remove leading asterisk and trim
        var cleaned = line.TrimStart('*', ' ');

        // Extract level number (last number in the string)
        var levelMatch = Regex.Match(cleaned, @"(\d+)$");
        if (!levelMatch.Success)
            return;

        if (!int.TryParse(levelMatch.Groups[1].Value, out var level))
            return;

        // Extract class name (everything before the level)
        var classNamePart = cleaned.Substring(0, cleaned.LastIndexOf(levelMatch.Value)).Trim();
        
        // Remove "Level" suffix if present
        if (classNamePart.EndsWith("Level", StringComparison.OrdinalIgnoreCase))
        {
            classNamePart = classNamePart.Substring(0, classNamePart.Length - 5).Trim();
        }

        // Try to match class name to ID (this would need a lookup table)
        var classId = GetClassIdByName(classNamePart);
        if (classId > 0)
        {
            reqs.Prerequisites.Add(new ClassPrerequisite
            {
                ClassId = classId,
                MinLevel = level
            });
        }
    }

    /// <summary>
    /// Lookup table: nom classe → ID
    /// </summary>
    private static int GetClassIdByName(string className)
    {
        var classMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Warrior", 1 },
            { "Magician", 2 },
            { "Hunter", 3 },
            { "Cleric", 4 },
            { "Thief", 5 },
            { "Bard", 6 },
            { "Knight", 7 },
            { "Engineer", 8 },
            { "Martial Artist", 9 },
            { "Martial Arts", 9 },
            { "Illusionist", 10 },
            { "Blade Dancer", 11 },
            { "Shaman", 12 },
            { "Templar", 13 },
            { "Ranger", 14 },
            { "Warlock", 15 },
            { "Samurai", 16 },
            { "Sage", 17 },
            { "Dragon Knight", 18 },
            { "Luminary", 19 },
            { "Mimic", 20 },
            // Awakened
            { "Berserker", 21 },
            { "Shielder", 22 },
            { "Arch-Elementalist", 23 },
            { "Gravity Manipulator", 24 },
            { "Trainer", 25 },
            { "Devil Hunter", 26 },
            { "Adjudicator", 27 },
            { "Equilibrian", 28 },
            { "Assassin", 29 },
            { "Kage", 30 },
            { "Battlefield Poet", 31 },
            { "Life Worshipper", 32 },
            { "Executioner", 33 },
            { "Glacier Knight", 34 },
            { "Mecha Ares", 35 },
            { "Annihilator", 36 },
            { "Asura", 37 },
            { "Rakshasa", 38 },
            { "Demon Tamer", 39 },
            { "Chronologist", 40 },
            { "Blade Acrobat", 41 },
            { "Elegant Dancer", 42 },
            { "Druid", 43 },
            { "Archimage", 44 },
            { "Blood Knight", 45 },
            { "Paladin", 46 },
            { "Ranger 2", 47 },
            { "Ranger 3", 48 },
            { "Conjurer", 50 },
            { "Blade Master", 51 },
            { "Master Fencer", 52 },
            { "Sage 2", 53 },
            { "Sage 3", 54 },
            { "Dragon Knight 2", 55 },
            { "Dragon Knight 3", 56 },
            { "Luminary 2", 57 },
            { "Luminary 3", 58 },
            { "Inquisitor", 59 },
            { "Reaver", 60 }
        };

        return classMap.TryGetValue(className, out var id) ? id : 0;
    }
}
