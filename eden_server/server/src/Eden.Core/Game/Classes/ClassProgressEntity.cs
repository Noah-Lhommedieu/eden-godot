namespace Eden.Core.Game.Classes;

/// <summary>
/// Progression d'un joueur dans une classe spécifique
/// Stockée dans player_classes
/// </summary>
public sealed class ClassProgressEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int ClassId { get; set; } // 1-20: base, 21-60: awakened (Berserker, Shielder, etc.)
    public int ClassLevel { get; set; } = 1;
    public long ClassExp { get; set; } = 0;
    public int ClassPoints { get; set; } = 0; // Points pour talents
    /// <summary>aa_point natif (player_classlist) — flag émis tel quel
    /// dans les deux u8 de la ligne 489 (capture : la classe "active"
    /// portait aa_point=2, les autres 1).</summary>
    public int AaPoint { get; set; } = 0;
    /// <summary>`style` natif (player_classlist) — u8 émis à +2 de
    /// chaque entrée 489 (`SCharacterClass::Serialize` @0x863ba10).
    /// 0 sur tous nos joueurs (coiffure/silhouette de classe).</summary>
    public int Style { get; set; }
    /// <summary>Certificats équipés par slot — `lic_1..lic_7` natifs
    /// (player_classlist), 7 × u16 émis à +7 de l'entrée 489.
    /// `SetOtherClassLicense` (@0x82717f0) réécrit ce layout quand
    /// le client édite les certs d'une AUTRE classe ; la classe
    /// active sauvegarde le sien ici et expose le layout courant
    /// via `player_license.loc`.</summary>
    public int[] Licenses { get; set; } = new int[7];
    public bool LifelongUnlocked { get; set; } = false;
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Détermine si cette classe est awakened (IDs 21-60)
    /// </summary>
    public bool IsAwakened => ClassId >= 21 && ClassId <= 60;
    
    /// <summary>
    /// Récupère l'ID de la classe de base pour une classe awakened
    /// Ex: Berserker (21) → Warrior (1)
    /// </summary>
    public int GetBaseClassId()
    {
        if (!IsAwakened) return ClassId;
        
        // Mapping awakened → base
        return ClassId switch
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
            _ => ClassId
        };
    }
}
