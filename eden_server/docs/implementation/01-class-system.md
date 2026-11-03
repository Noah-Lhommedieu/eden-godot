# 01 - Système de Classes et Changement de Classe

> **Status:** 🟡 En cours d'implémentation  
> **Priorité:** MAX (Fondation du jeu)  
> **Complexité:** Élevée

---

## 1. Description du Système

Le système de classes d'Eden Eternal permet aux joueurs de:
- Choisir parmi **20 classes de base** (5 classes de départ + 15 classes avancées)
- Débloquer des classes éveillées (Awakened Classes) via des quêtes spéciales
- Changer de classe à tout moment (avec certaines restrictions)
- Accumuler des **Class Points** et **Class Talents** pour chaque classe
- Utiliser des buffs raciaux passifs spécifiques à chaque classe
- Gérer des certificats de classe (Class Licenses)

### Classes de Départ (Beginning Jobs)
1. **Warrior** (ID: 1) - Tank melee, armure lourde
2. **Magician** (ID: 2) - DPS magique, éléments
3. **Hunter** (ID: 3) - DPS ranged, pet tiger
4. **Cleric** (ID: 4) - Healer, buffs saints
5. **Thief** (ID: 5) - DPS melee rapide, critique

### Classes Avancées
6. **Bard** (ID: 6) - Support, buffs de groupe
7. **Knight** (ID: 7) - Tank avancé, Warrior Lv10 requis
8. **Engineer** (ID: 8) - DPS technology, Hunter Lv15 requis
9. **Martial Artist** (ID: 9) - DPS melee, Thief Lv25 requis
10. **Illusionist** (ID: 10) - Control, Magician Lv20 requis
11. **Blade Dancer** (ID: 11) - DPS melee, Bard Lv35 requis
12. **Shaman** (ID: 12) - Support nature, Cleric Lv30 requis
13. **Templar** (ID: 13) - Tank saint, Knight/Cleric Lv45 requis
14. **Ranger** (ID: 14) - DPS ranged avancé, Hunter/Martial Artist Lv50 requis
15. **Warlock** (ID: 15) - DPS dark magic, Magician/Shaman Lv40 requis
16. **Samurai** (ID: 16) - DPS melee katana, Warrior/Martial Artist Lv45 requis
17. **Sage** (ID: 17) - Healer avancé, quête "Sage's Trial"
18. **Dragon Knight** (ID: 18) - Tank dragon, quête "Awakening of the Drake's Soul"
19. **Luminary** (ID: 19) - DPS dual blade, light/dark
20. **Mimic** (ID: 20) - Hybride, quête "Death & Rebirth"

### Classes Éveillées (Awakened)
Les IDs 101-120 et 201-220 correspondent aux versions éveillées des classes ci-dessus avec des stats et skills améliorés.

---

## 2. Symboles lapis:: Analysés

### Classes Principales
- `CClassDBO` - Database Object pour les classes
- `CClassDBO::CClassDBO` - Constructeur
- `CClassDBO::~CClassDBO` - Destructeur
- `CClassDBO::GetAllClasses` - Récupération de toutes les classes
- `CClassDBO::InitClassSpellSets` - Initialisation des sets de spells par classe
- `CClassDBO::InitAAEffectSets` - Initialisation des sets d'effets AA (Alternate Awakening)

### Gestion des Classes sur Character
- `CCharacter::ChangeClassAA` - Changement de classe avec AA
- `CCharacter::GainClassPoint` - Gain de points de classe
- `CCharacter::SetClassLevel` - Définition du niveau de classe
- `CCharacter::SetClassExp` - Définition de l'XP de classe
- `CCharacter::GetClassGroup` - Récupération du groupe de classe
- `CCharacter::GetClassExpRate` - Taux d'XP de classe
- `CCharacter::GetClassExpDice` - Dice d'XP de classe (bonus aléatoire)
- `CCharacter::AddPlayerColosseumClassPK` - Ajout PK colosseum par classe

### Factory pour Opérations de Classe
- `CPlayerFactory::AddClass` - Ajout d'une classe au joueur
- `CPlayerFactory::AddClassFromLimit` - Ajout avec limite
- `CPlayerFactory::ChangeClass` - Changement de classe actif
- `CPlayerFactory::RemoveClass` - Suppression d'une classe
- `CPlayerFactory::CheckWeaponBeforeChangeClass` - Vérification arme avant changement
- `CPlayerFactory::SwitchClassEquip` - Switch de l'équipement de classe
- `CPlayerFactory::UpdateClassSpellCard` - Mise à jour des spells de classe

### Spells de Classe
- `CSpellMasterFactory::GetClassSpell` - Récupération spell de classe
- `CSpellMasterFactory::RemoveClassSpell` - Suppression spell de classe
- `CSpellMasterTemplate::CheckClass` - Vérification de classe
- `CItemFactory::ClassItemDrop` - Drop d'items spécifiques à classe
- `CItemFactory::GetClassGlyphLoc` - Localisation glyph de classe

### Niveaux et XP
- `CLevelDBO::LoadClassLevelExp` - Chargement XP par niveau de classe
- `CLevelDBO::LoadClassWakenLevelExp` - Chargement XP classe éveillée
- `CMonsterDBO::GetAllMonsterClassDrops` - Drops spécifiques classe des monstres
- `CMonster::GiveClassItemDrop` - Donner drop de classe

### Messages Réseau
- `CNE_CZ_ZoneServerChangeClass` - Changement de classe (client → zone)
- `CNE_CZ_ZoneServerQueryClassList` - Query liste de classes
- `CNE_CZ_ZoneServerPlayerLicense::SetCurrentClassLicense` - Set license classe actuelle
- `CNE_CZ_ZoneServerPlayerLicense::SetOtherClassLicense` - Set license autre classe

### Effects de Classe
- `CEC_ClassExpRate` - Effet taux d'XP de classe
- `CEC_ClassExpRate::CanExecute` - Vérification
- `CEC_ClassExpRate::Execute` - Exécution
- `CEC_ClassExpRate::Init` - Initialisation
- `CEC_ClassExpRate::RollBack` - Rollback

### Colosseum
- `CColosseumFactory::CheckCanChangClass` - Vérification changement classe en colosseum
- `CCharacter::AddPlayerColosseumClassPK` - PK colosseum par classe

### Base de Données
- `CExecutionerData::GetClassLifelongInsertSQLCmd` - SQL insert classe lifelong
- `CExecutionerData::GetClassLifelongUpdateSQLCmd` - SQL update classe lifelong
- `CExecutionerData::GetClassListDeleteSQLCmd` - SQL delete liste classe
- `CExecutionerData::GetClassListInsertSQLCmd` - SQL insert liste classe
- `CExecutionerData::GetClassListUpdateSQLCmd` - SQL update liste classe
- `CExecutionerData::UpdatePlayerColosseumClassPK` - Update PK colosseum

### Mission Conditions
- `MC_IfClass` - Condition: si classe
- `MC_RewardClassPoint` - Récompense: point de classe
- `Mission_TC_BuyClassLifelongItem` - Achat item classe lifelong

### Structures Client
- `SCharacterClassClient` - Structure classe côté client
- `SCharacterClassClient::Deserialize` - Désérialisation

---

## 3. Fichiers de Données Associés

### T_Class.ini (Text Data)
- **Format:** 
  - Ligne 1: Nombre de champs (6)
  - Ligne 2: Colonnes (1|2|5|6|7|8)
  - Lignes suivantes: `ID|Name|Description|HeroicTrait|DetailedDescription|Requirements`
- **Contenu:** 20 classes de base + leurs versions éveillées (IDs 101-120, 201-220)
- **Encodage:** UTF-8 (lisible)

### S_Class.ini (Server Data)
- **Format:** Binaire/Encodé spécial (669 lignes)
- **Contenu:** Stats détaillées par classe (HP, MP, STR, DEX, etc. par niveau)
- **Encodage:** Non-UTF-8 (nécessite décodage spécifique)
- **Structure:** Stats par niveau de 1 à cap

### Fichiers connexes
- `S_ClassRankBuff.ini` - Buffs par rang de classe
- `S_Classes.ini` - Configuration des classes
- `S_DropItem.ini` - Tables de drop avec class-specific items

---

## 4. Protocole Réseau

### CNE_CZ_ZoneServerChangeClass (Client → Zone)
**Opcode:** À déterminer (via NCID)  
**Direction:** Client envoi vers ZoneServer  
**Champs (déduits):**
- `ClassId` (short) - ID de la classe cible
- `WeaponCheck` (bool) - Vérification arme
- `Confirm` (bool) - Confirmation changement

### CNC_CZ_ClientUpdateEquipment (Zone → Client)
**Context:** Mise à jour de l'équipement après changement de classe  
**Champs:**
- `CharacterId` (int)
- `EquipmentData` (structure)

### CNE_CZ_ZoneServerQueryClassList (Client → Zone)
**Opcode:** À déterminer  
**Direction:** Query liste des classes disponibles pour le joueur

### CNC_CZ_ClientSendClassList (Zone → Client)
**Context:** Envoi de la liste des classes du joueur avec leurs niveaux

---

## 5. Base de Données PostgreSQL

### Tables Concernées

#### player_classes (probable)
```sql
CREATE TABLE player_classes (
    id SERIAL PRIMARY KEY,
    character_id INTEGER REFERENCES player_characters(id),
    class_id INTEGER NOT NULL,
    class_level INTEGER DEFAULT 1,
    class_exp BIGINT DEFAULT 0,
    class_points INTEGER DEFAULT 0,
    is_awakened BOOLEAN DEFAULT FALSE,
    lifelong_unlocked BOOLEAN DEFAULT FALSE,
    create_time TIMESTAMP DEFAULT NOW()
);
```

#### class_progression (probable)
```sql
CREATE TABLE class_progression (
    class_id INTEGER PRIMARY KEY,
    level INTEGER NOT NULL,
    required_exp BIGINT NOT NULL,
    hp_bonus INTEGER,
    mp_bonus INTEGER,
    str_bonus INTEGER,
    dex_bonus INTEGER,
    con_bonus INTEGER,
    int_bonus INTEGER,
    psy_bonus INTEGER,
    agi_bonus INTEGER
);
```

#### class_spells (probable)
```sql
CREATE TABLE class_spells (
    class_id INTEGER,
    spell_id INTEGER,
    required_level INTEGER,
    is_passive BOOLEAN,
    PRIMARY KEY (class_id, spell_id, required_level)
);
```

---

## 6. Implémentation C#

### 6.1 Entités

#### ClassEntity.cs
```csharp
namespace Eden.Core.Game.Classes;

/// <summary>
/// Représente une classe de personnage (Warrior, Magician, etc.)
/// Correspond aux données de T_Class.ini
/// </summary>
public sealed class ClassEntity
{
    public int Id { get; set; } // 1-20 pour base, 101-120/201-220 pour éveillé
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HeroicTrait { get; set; } = string.Empty; // Nom du trait héroique
    public string DetailedDescription { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty; // Prérequis (level, classe requise)
    
    // Metadata
    public bool IsBeginningJob { get; set; } // Classes 1-5
    public bool IsAwakened { get; set; } // Versions éveillées
    public int BaseClassId { get; set; } // ID de la classe de base si éveillé
    public int RequiredCharacterLevel { get; set; }
    public int RequiredClassId { get; set; } // Classe parente requise
    public int RequiredClassLevel { get; set; }
    public string RequiredQuest { get; set; } = string.Empty; // Quête spécifique
}
```

#### ClassProgressEntity.cs
```csharp
namespace Eden.Core.Game.Classes;

/// <summary>
/// Progression d'un joueur dans une classe spécifique
/// Stockée dans player_classes
/// </summary>
public sealed class ClassProgressEntity
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int ClassId { get; set; }
    public int ClassLevel { get; set; } = 1;
    public long ClassExp { get; set; } = 0;
    public int ClassPoints { get; set; } = 0; // Points pour talents
    public bool IsAwakened { get; set; } = false;
    public bool LifelongUnlocked { get; set; } = false;
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
```

#### ClassStatsEntity.cs
```csharp
namespace Eden.Core.Game.Classes;

/// <summary>
/// Stats d'une classe à un niveau donné
/// Issue de S_Class.ini
/// </summary>
public sealed class ClassStatsEntity
{
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int HpBonus { get; set; }
    public int MpBonus { get; set; }
    public int StrBonus { get; set; }
    public int DexBonus { get; set; }
    public int ConBonus { get; set; }
    public int IntBonus { get; set; }
    public int PsyBonus { get; set; }
    public int AgiBonus { get; set; }
}
```

### 6.2 Parser de T_Class.ini

#### ClassDataParser.cs
```csharp
namespace Eden.Core.Data.Parsers;

using Eden.Core.Game.Classes;

/// <summary>
/// Parser pour T_Class.ini - Données textuelles des classes
/// Format: 6 champs séparés par |
/// </summary>
public static class ClassDataParser
{
    public static IReadOnlyList<ClassEntity> Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var classes = new List<ClassEntity>();
        
        // Ligne 1: nombre de champs (ignoré, fixé à 6)
        // Ligne 2: colonnes (ignoré, fixé à 1|2|5|6|7|8)
        
        for (int i = 2; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            
            var parts = line.Split('|');
            if (parts.Length < 6) continue;
            
            var classEntity = new ClassEntity
            {
                Id = int.Parse(parts[0]),
                Name = parts[1],
                Description = parts[2],
                HeroicTrait = parts[3],
                DetailedDescription = parts[4],
                Requirements = parts[5]
            };
            
            // Metadata dérivés
            classEntity.IsBeginningJob = classEntity.Id >= 1 && classEntity.Id <= 5;
            classEntity.IsAwakened = classEntity.Id >= 101;
            
            if (classEntity.IsAwakened)
            {
                classEntity.BaseClassId = classEntity.Id switch
                {
                    >= 201 => classEntity.Id - 200,
                    >= 101 => classEntity.Id - 100,
                    _ => classEntity.Id
                };
            }
            
            // Parsing des prérequis
            ParseRequirements(classEntity);
            
            classes.Add(classEntity);
        }
        
        return classes;
    }
    
    private static void ParseRequirements(ClassEntity classEntity)
    {
        var req = classEntity.Requirements;
        
        // Exemple: "* Character Level 25\n* Warrior Level 10"
        if (req.Contains("Character Level"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(req, @"Character Level (\d+)");
            if (match.Success)
            {
                classEntity.RequiredCharacterLevel = int.Parse(match.Groups[1].Value);
            }
        }
        
        if (req.Contains("Warrior Level"))
        {
            classEntity.RequiredClassId = 1; // Warrior
            var match = System.Text.RegularExpressions.Regex.Match(req, @"Warrior Level (\d+)");
            if (match.Success)
            {
                classEntity.RequiredClassLevel = int.Parse(match.Groups[1].Value);
            }
        }
        // ... autres classes
        
        if (req.Contains("Sage's Trial"))
        {
            classEntity.RequiredQuest = "Sage's Trial";
        }
        // ... autres quêtes
    }
}
```

### 6.3 Repository

#### ClassRepository.cs
```csharp
namespace Eden.Core.Database.Repositories;

using Dapper;
using Npgsql;
using Eden.Core.Game.Classes;

public interface IClassRepository
{
    Task<IReadOnlyList<ClassProgressEntity>> GetClassesByCharacterIdAsync(int characterId);
    Task<ClassProgressEntity?> GetClassProgressAsync(int characterId, int classId);
    Task<int> AddClassAsync(int characterId, int classId);
    Task<bool> ChangeClassAsync(int characterId, int newClassId);
    Task AddClassExpAsync(int characterId, int classId, long expGain);
    Task AddClassPointsAsync(int characterId, int classId, int points);
}

public sealed class ClassRepository : IClassRepository
{
    private readonly string _connectionString;
    
    public ClassRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<IReadOnlyList<ClassProgressEntity>> GetClassesByCharacterIdAsync(int characterId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT id, character_id, class_id, class_level, class_exp, 
                   class_points, is_awakened, lifelong_unlocked, create_time
            FROM player_classes
            WHERE character_id = @CharacterId";
        
        var results = await connection.QueryAsync<ClassProgressEntity>(sql, new { CharacterId = characterId });
        return results.ToList();
    }
    
    public async Task<ClassProgressEntity?> GetClassProgressAsync(int characterId, int classId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT id, character_id, class_id, class_level, class_exp, 
                   class_points, is_awakened, lifelong_unlocked, create_time
            FROM player_classes
            WHERE character_id = @CharacterId AND class_id = @ClassId
            LIMIT 1";
        
        return await connection.QueryFirstOrDefaultAsync<ClassProgressEntity>(sql, 
            new { CharacterId = characterId, ClassId = classId });
    }
    
    public async Task<int> AddClassAsync(int characterId, int classId)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            INSERT INTO player_classes (character_id, class_id, class_level, class_exp, class_points)
            VALUES (@CharacterId, @ClassId, 1, 0, 0)
            RETURNING id";
        
        return await connection.ExecuteScalarAsync<int>(sql, 
            new { CharacterId = characterId, ClassId = classId });
    }
    
    public async Task<bool> ChangeClassAsync(int characterId, int newClassId)
    {
        // Vérifier que le joueur a cette classe
        var progress = await GetClassProgressAsync(characterId, newClassId);
        if (progress == null) return false;
        
        // Mise à jour du personnage principal
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE player_characters 
            SET class = @ClassId
            WHERE id = @CharacterId";
        
        var affected = await connection.ExecuteAsync(sql, 
            new { ClassId = newClassId, CharacterId = characterId });
        
        return affected > 0;
    }
    
    public async Task AddClassExpAsync(int characterId, int classId, long expGain)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE player_classes
            SET class_exp = class_exp + @ExpGain
            WHERE character_id = @CharacterId AND class_id = @ClassId";
        
        await connection.ExecuteAsync(sql, 
            new { CharacterId = characterId, ClassId = classId, ExpGain = expGain });
    }
    
    public async Task AddClassPointsAsync(int characterId, int classId, int points)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE player_classes
            SET class_points = class_points + @Points
            WHERE character_id = @CharacterId AND class_id = @ClassId";
        
        await connection.ExecuteAsync(sql, 
            new { CharacterId = characterId, ClassId = classId, Points = points });
    }
}
```

### 6.4 Service de Changement de Classe

#### ClassChangeService.cs
```csharp
namespace Eden.Core.Game.Services;

using Eden.Core.Game.Classes;
using Eden.Core.Database.Repositories;

/// <summary>
/// Service gérant le changement de classe et la progression
/// Correspond à CPlayerFactory::ChangeClass et fonctions associées
/// </summary>
public sealed class ClassChangeService
{
    private readonly IClassRepository _classRepository;
    private readonly IReadOnlyList<ClassEntity> _allClasses;
    
    public ClassChangeService(IClassRepository classRepository, IReadOnlyList<ClassEntity> allClasses)
    {
        _classRepository = classRepository;
        _allClasses = allClasses;
    }
    
    /// <summary>
    /// Tente de changer la classe active du personnage
    /// Correspond à CPlayerFactory::ChangeClass
    /// </summary>
    public async Task<(bool Success, string ErrorMessage)> ChangeClassAsync(
        int characterId, 
        int targetClassId,
        int currentCharacterLevel,
        IReadOnlyList<ClassProgressEntity> currentClasses)
    {
        // 1. Vérifier que la classe existe
        var targetClass = _allClasses.FirstOrDefault(c => c.Id == targetClassId);
        if (targetClass == null)
        {
            return (false, "Class not found");
        }
        
        // 2. Vérifier que le joueur a débloqué cette classe
        var classProgress = currentClasses.FirstOrDefault(c => c.ClassId == targetClassId);
        if (classProgress == null)
        {
            return (false, "Class not unlocked");
        }
        
        // 3. Vérifier les prérequis de niveau
        if (currentCharacterLevel < targetClass.RequiredCharacterLevel)
        {
            return (false, $"Character level {targetClass.RequiredCharacterLevel} required");
        }
        
        // 4. Vérifier les prérequis de classe parente
        if (targetClass.RequiredClassId > 0)
        {
            var parentProgress = currentClasses.FirstOrDefault(c => c.ClassId == targetClass.RequiredClassId);
            if (parentProgress == null || parentProgress.ClassLevel < targetClass.RequiredClassLevel)
            {
                return (false, $"Parent class {targetClass.RequiredClassId} level {targetClass.RequiredClassLevel} required");
            }
        }
        
        // 5. Vérifier les prérequis de quête
        if (!string.IsNullOrEmpty(targetClass.RequiredQuest))
        {
            // TODO: Vérifier que la quête est complétée
            // Pour l'instant, on assume que c'est validé par ailleurs
        }
        
        // 6. Effectuer le changement
        var success = await _classRepository.ChangeClassAsync(characterId, targetClassId);
        if (!success)
        {
            return (false, "Failed to update character class");
        }
        
        return (true, string.Empty);
    }
    
    /// <summary>
    /// Ajoute une nouvelle classe au joueur
    /// Correspond à CPlayerFactory::AddClass
    /// </summary>
    public async Task<int> UnlockClassAsync(int characterId, int classId)
    {
        // Vérifier que la classe n'est pas déjà débloquée
        var existing = await _classRepository.GetClassProgressAsync(characterId, classId);
        if (existing != null)
        {
            return existing.Id; // Déjà débloquée
        }
        
        return await _classRepository.AddClassAsync(characterId, classId);
    }
    
    /// <summary>
    /// Calcule l'XP requise pour le niveau suivant d'une classe
    /// Correspond à CLevelDBO::LoadClassLevelExp
    /// </summary>
    public long GetExpForNextLevel(int classId, int currentLevel)
    {
        // Formule de base (à affiner avec les données de S_Class.ini)
        return (long)Math.Pow(currentLevel, 2) * 100;
    }
}
```

---

## 7. Tests de Validation

### ClassSystemTests.cs
```csharp
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Classes;

public class ClassSystemTests
{
    [Fact]
    public void ParseT_ClassIni_Returns20Classes()
    {
        var classes = ClassDataParser.Parse("server_us/Data/db/T_Class.ini");
        Assert.Equal(20, classes.Count(c => c.Id >= 1 && c.Id <= 20));
    }
    
    [Fact]
    public void Warrior_IsBeginningJob()
    {
        var classes = ClassDataParser.Parse("server_us/Data/db/T_Class.ini");
        var warrior = classes.First(c => c.Id == 1);
        Assert.True(warrior.IsBeginningJob);
    }
    
    [Fact]
    public void Knight_RequiresWarriorLevel10()
    {
        var classes = ClassDataParser.Parse("server_us/Data/db/T_Class.ini");
        var knight = classes.First(c => c.Id == 7);
        Assert.Equal(1, knight.RequiredClassId); // Warrior
        Assert.Equal(10, knight.RequiredClassLevel);
    }
    
    [Fact]
    public void AwakenedClass_HasBaseClassId()
    {
        var classes = ClassDataParser.Parse("server_us/Data/db/T_Class.ini");
        var awakenedWarrior = classes.First(c => c.Id == 101);
        Assert.True(awakenedWarrior.IsAwakened);
        Assert.Equal(1, awakenedWarrior.BaseClassId);
    }
}
```

---

## 8. À Faire (TODO)

- [ ] Parser S_Class.ini (binaire) pour les stats par niveau
- [ ] Implémenter le système de Class Talents
- [ ] Implémenter les buffs raciaux passifs
- [ ] Créer les handlers réseau pour ChangeClass
- [ ] Implémenter le système de certificats (Class Licenses)
- [ ] Créer la table player_classes dans PostgreSQL
- [ ] Implémenter CPlayerFactory::SwitchClassEquip
- [ ] Ajouter les tests d'intégration
- [ ] Documenter les formules exactes d'XP de classe

---

## 9. Notes de Reverse Engineering

- Le fichier S_Class.ini est encodé (pas UTF-8), nécessite un décodeur spécial
- Les classes éveillées (IDs 101-120, 201-220) partagent le même nom mais avec des stats améliorées
- Le système de "Class Points" est lié aux talents et certificats
- Les buffs raciaux sont des passifs appliqués automatiquement selon la classe active
- Le changement de classe peut être bloqué en Colosseum (cf. CColosseumFactory::CheckCanChangClass)
- Les "Class Lifelong Items" sont des items achetables qui débloquent une classe pour toujours sur le compte

---

*Document généré le 15 Octobre 2026*
