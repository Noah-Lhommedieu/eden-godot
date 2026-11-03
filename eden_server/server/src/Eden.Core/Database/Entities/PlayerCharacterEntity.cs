namespace Eden.Core.Database.Entities;

/// <summary>
/// Entité complète de personnage joueur stockée dans 'edenfr_db' (table 'player_characters').
/// </summary>
public sealed class PlayerCharacterEntity
{
    public int Id { get; set; }
    public string GivenName { get; set; } = string.Empty;
    public int NodeId { get; set; } = 5;
    
    // Coordonnées spatiales dans le monde / la zone
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double FacingDirection { get; set; }

    // Attributs de classe, race et genre
    public int ClassId { get; set; }
    public int RaceId { get; set; }
    public int GenderId { get; set; }

    // Personnalisation visuelle
    public int HairStyle { get; set; }
    public int FaceStyle { get; set; }
    public int HairColor { get; set; }
    public int SkinColor { get; set; } = 1;
    public int EyesColor { get; set; }
    public int EyesColor2 { get; set; } = 26;
    public int Style { get; set; }

    // Couleurs de costume équipées (garde-robe) — champs natifs
    // char+0x48e/+0x496/+0x49e (3×u64 : couleur + infos de teinture
    // packées), copiées depuis le slot player_closet choisi par
    // ClosetSet (167) ou remises à zéro.
    public long HelmetColor { get; set; }
    public long ClothesColor { get; set; }
    public long CloakColor { get; set; }

    /// <summary>Blob d'apparence brut de 32 octets (SNewCharacter+0x15) —
    /// renvoyé tel quel dans CNC_CW_ClientGetCharacters.</summary>
    public byte[] AppearanceData { get; set; } = new byte[32];

    /// <summary>3e octet de SNewCharacter (+0x14) — renvoyé comme 2e short
    /// de l'entrée de liste (observé 0 pour un perso frais, 1 sinon).</summary>
    public int CreationFlag { get; set; }

    // Progression et statistiques
    public int Level { get; set; } = 1;
    public long Experience { get; set; }
    public int SkillPoints { get; set; }
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    public int ComboPoints { get; set; }

    // Économie
    public int Gold { get; set; }
    public int BindGold { get; set; }

    // Métadonnées du compte et du serveur
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int Privilege { get; set; }
    public int FamilyId { get; set; }
    public int ReviveAreaId { get; set; }
    /// <summary>Dernière aire NORMALE traversée — `node&lt;&lt;16|clé`
    /// de `[normal_area]` (exec+0x34 natif). Utilisée par la
    /// redirection de login quand le `node_id` persisté est un type
    /// instance (<c>CNE_DBA_ClientPlayerData::Execute</c> zone
    /// @0x082f4ea0, branche <c>bVar9</c>).</summary>
    public int LastNormalAreaId { get; set; }
    public int ClassLevel { get; set; } = 1;
    public int ClassPoints { get; set; }
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public int DeletedTime { get; set; }
    /// <summary>Groupe de métier (colonne `life_skill`, -1 = jamais
    /// dérivé) — 1 = analyse joaillerie, 2-5 = professions de
    /// production. Dérivé une fois par le CheckLifeSkill natif depuis
    /// le nibble profession de `flags` (octet0&gt;&gt;4 : {0,1}→1,
    /// {2,3}→2, {4,5}→3, {8,9}→4, {6,7}→5) — renvoyé en s16 dans
    /// CNC_CZ_ClientPlayerLifeSkill (char+0xcc8).</summary>
    public int LifeSkill { get; set; } = -1;

    /// <summary>Famille dont le perso visite actuellement le hall
    /// (`visit_family_id` — exec+0x114 « VisitFamilyID » → char+0xaac
    /// zone). Persisté : le natif ne l'efface jamais après la visite,
    /// seule une autre visite l'écrase.</summary>
    public int VisitFamilyId { get; set; }

    /// <summary>Node de retour d'instance (`cross_zone_node` —
    /// exec+0xf0 « CrossZoneData » → char+0xa98 zone) : dernier node
    /// non-exclu quitté (les types 9/10/13/15/16/17/19 ont leur
    /// mécanisme de retour dédié et ne l'écrasent pas).</summary>
    public int CrossZoneNode { get; set; }

    /// <summary>Position sauvegardée du point de retour
    /// (`cross_zone_x`/`cross_zone_y` — exec+0xf4/+0xf8 →
    /// char+0xa9c/+0xaa0 ; consommée par
    /// <c>CEventQuitNodeFamilyInstance::TransportCharacterToPoint</c>
    /// natif).</summary>
    public double CrossZoneX { get; set; }
    public double CrossZoneY { get; set; }

    /// <summary>Titre actif du perso (`player_characters.appellation`
    /// = char+0x6fc natif, u16 ; persisté par
    /// `SavePlayerCharacter "appellation"` à chaque set).</summary>
    public int Appellation { get; set; }
}
