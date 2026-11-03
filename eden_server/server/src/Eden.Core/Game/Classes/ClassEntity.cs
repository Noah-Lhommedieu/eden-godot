namespace Eden.Core.Game.Classes;

/// <summary>
/// Représente une classe de personnage
/// Correspond aux données de T_Class.ini
/// </summary>
public sealed class ClassEntity
{
    public int Id { get; set; } // 1-20: base, 21-60: awakened avec noms uniques
    public string Name { get; set; } = string.Empty; // Ex: "Warrior", "Berserker", "Shielder"
    public string Description { get; set; } = string.Empty;
    public string HeroicTrait { get; set; } = string.Empty; // Nom du trait héroique
    public string DetailedDescription { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty; // Prérequis (level, classe requise)
    
    // Metadata
    public bool IsBeginningJob => Id >= 1 && Id <= 5; // Classes 1-5
    public bool IsAwakened => Id >= 21 && Id <= 60; // Classes awakened avec noms uniques
    
    /// <summary>
    /// ID de la classe de base pour une classe awakened
    /// Ex: Berserker (21) → Warrior (1)
    /// </summary>
    public int BaseClassId => IsAwakened ? GetBaseClassId() : Id;
    
    public int RequiredCharacterLevel { get; set; }
    public int RequiredClassId { get; set; } // Classe parente requise
    public int RequiredClassLevel { get; set; }
    public string RequiredQuest { get; set; } = string.Empty; // Quête spécifique
    
    private int GetBaseClassId()
    {
        return Id switch
        {
            21 or 22 => 1,  // Berserker/Shielder → Warrior
            23 or 24 => 2,  // Arch-Elementalist/Gravity Manipulator → Magician
            25 or 26 => 3,  // Trainer/Devil Hunter → Hunter
            27 or 28 => 4,  // Adjudicator/Equilibrian → Cleric
            29 or 30 => 5,  // Assassin/Kage → Thief
            31 or 32 => 6,  // Battlefield Poet/Life Worshipper → Bard
            33 or 34 => 7,  // Executioner/Glacier Knight → Knight
            35 or 36 => 8,  // Mecha Ares/Annihilator → Engineer
            37 or 38 => 9,  // Asura/Rakshasa → Martial Artist
            39 or 40 => 10, // Demon Tamer/Chronologist → Illusionist
            41 or 42 => 11, // Blade Acrobat/Elegant Dancer → Blade Dancer
            43 or 44 => 12, // Druid/Archimage → Shaman
            45 or 46 => 13, // Blood Knight/Paladin → Templar
            47 or 48 => 14, // → Ranger
            49 or 50 => 15, // Demon Tamer/Conjurer → Warlock
            51 or 52 => 16, // Blade Master/Master Fencer → Samurai
            53 or 54 => 17, // → Sage
            55 or 56 => 18, // → Dragon Knight
            57 or 58 => 19, // → Luminary
            59 or 60 => 20, // Inquisitor/Reaver → Mimic
            _ => Id
        };
    }
}
