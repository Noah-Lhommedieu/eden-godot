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
    
    /// <summary>Classe éveillée (21-58, 60, 61 — voir
    /// <see cref="ClassRules"/>).</summary>
    public bool IsAwakened => ClassRules.IsAwakened(ClassId);

    /// <summary>Classe de base d'une éveillée (21/22 → Guerrier …
    /// 60/61 → Juge) ; une base se renvoie elle-même.</summary>
    public int GetBaseClassId() => ClassRules.BaseOf(ClassId);
}
