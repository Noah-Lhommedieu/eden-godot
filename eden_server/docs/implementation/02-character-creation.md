# Phase 1.2: Character Creation & Deletion

**Date:** 15 Octobre 2026  
**Status:** 🚀 EN COURS  
**Progress:** 0% → Démarrage

---

## 🎯 Objectifs

Implémenter le système complet de création, validation et suppression de personnages.

### Fonctionnalités Cibles:
1. ✅ Validation de nom de personnage
2. ✅ Création de personnage (race, classe, genre, apparence)
3. ✅ Génération des stats initiales
4. ✅ Suppression de personnage (soft/hard delete)
5. ✅ Limite de personnages par compte

---

## 📊 Symboles Extraits (WorldServer)

### Packets Réseau (CNE/CNC)
```
Client → Server (CNE):
- CNE_CW_ServerCreateCharacterNameCheck @ 0x080d9c60
- CNE_CW_ServerCreateCharacterName @ 0x080d8fe0
- CNE_CW_ServerCreateCharacterData @ 0x080d7ef0
- CNE_CW_ServerCancelCreateCharacterReq @ 0x080d59d0
- CNE_CW_ServerRemoveCharacter @ 0x080e9c60

Server → Client (CNC):
- CNC_CW_ClientVerifyCharacterName @ 0x0807b570
- CNC_CW_ClientGetCharacters @ 0x080627e0
- CNC_CW_ClientCancelCreateCharacterResp @ 0x080cbaf0
```

### Classes DBO (Database Objects)
```
- CCharacterDBO @ 0x0809cf10 - Objet principal personnage
```

---

## 🏗️ Architecture Technique

### Flow de Création de Personnage

```
1. Client: Request Name Check
   → CNE_CW_ServerCreateCharacterNameCheck
   ↓
2. Server: Validate Name
   - Longueur (3-16 caractères)
   - Caractères valides (A-Z, a-z, 0-9, -, _)
   - Profanity filter
   - Unicité en base
   ↓
3. Server → Client: CNC_CW_ClientVerifyCharacterName
   - Success: true/false
   - Reason: error message si refusé
   ↓
4. Client: Create Character Request
   → CNE_CW_ServerCreateCharacterData
   - Name
   - Race (1-5: Human, Zumi, Ursun, Halfkin, Anuran)
   - Gender (1: Male, 2: Female)
   - Class (1-4 starting classes based on race)
   - Appearance (HairStyle, HairColor, FaceStyle, EyeColor)
   ↓
5. Server: Create Character
   - Validate request
   - Generate initial stats
   - Create database record
   - Generate starting items (placeholder)
   ↓
6. Server → Client: Success/Failure Response
```

### Flow de Suppression

```
1. Client: Delete Character Request
   → CNE_CW_ServerRemoveCharacter (CharacterId)
   ↓
2. Server: Validate Deletion
   - Character belongs to account
   - Not in-game
   - Grace period check (24h wait time)
   ↓
3. Server: Mark for Deletion
   - Set deleted_at timestamp
   - Schedule permanent delete (7 days)
   ↓
4. Server → Client: Success Response
```

---

## 📋 Fichiers de Données

### T_CharColor.ini (✅ DECODED)
**Format:** Text ini, 2 sections
- Section 1 (1-45): Hair colors
- Section 2 (101-125): Skin colors
- Section 3 (201-210): Eye colors (race 3?)
- Section 4 (301-322): Hair/Skin colors (race 4?)
- Section 5 (401-412): Colors (race 5?)
- Section 6 (501-512): Colors (pets?)

**Structure:**
```
ID|Name|
```

### S_Races.ini (🚧 ENCODED - MyCrypt)
**Header:** `<encode>` (8 bytes)
**Size:** 32 bytes total (24 bytes encoded data)

**Contenu attendu:**
- 5 races: Human, Zumi, Ursun, Halfkin, Anuran
- Starting classes per race
- Starting stats per race
- Racial buffs

**Options:**
1. Decode avec MyCrypt (même algo que S_Class.ini)
2. Hardcode les 5 races (connu du client)
3. Extract from client memory

### character.ini (🚧 ENCODED - MyCrypt)
**Header:** `<encode>` (8 bytes)
**Size:** 61 bytes total (53 bytes encoded data)

**Contenu attendu:**
- Default starting position (world, x, y, z)
- Starting items
- Default stats

---

## 🗄️ Schema Base de Données

### Table: characters (NOUVEAU)
```sql
CREATE TABLE "public"."characters" (
  "id" SERIAL PRIMARY KEY,
  "account_id" INTEGER NOT NULL,
  "name" VARCHAR(16) NOT NULL UNIQUE,
  "race" SMALLINT NOT NULL, -- 1-5
  "gender" SMALLINT NOT NULL, -- 1: Male, 2: Female
  "current_class_id" INTEGER NOT NULL DEFAULT 1,
  "character_level" INTEGER NOT NULL DEFAULT 1,
  "experience" BIGINT NOT NULL DEFAULT 0,
  
  -- Appearance
  "hair_style" SMALLINT NOT NULL,
  "hair_color" SMALLINT NOT NULL,
  "face_style" SMALLINT NOT NULL,
  "eye_color" SMALLINT NOT NULL,
  
  -- Stats (base)
  "str" INTEGER NOT NULL DEFAULT 10,
  "dex" INTEGER NOT NULL DEFAULT 10,
  "con" INTEGER NOT NULL DEFAULT 10,
  "int" INTEGER NOT NULL DEFAULT 10,
  "psy" INTEGER NOT NULL DEFAULT 10,
  "agi" INTEGER NOT NULL DEFAULT 10,
  
  -- Position
  "world_id" INTEGER NOT NULL DEFAULT 1,
  "pos_x" FLOAT NOT NULL DEFAULT 0,
  "pos_y" FLOAT NOT NULL DEFAULT 0,
  "pos_z" FLOAT NOT NULL DEFAULT 0,
  
  -- Metadata
  "gold" BIGINT NOT NULL DEFAULT 0,
  "ep" INTEGER NOT NULL DEFAULT 0, -- Eden Points
  "ap" INTEGER NOT NULL DEFAULT 0, -- Aeria Points
  "deleted_at" TIMESTAMP NULL,
  "created_at" TIMESTAMP NOT NULL DEFAULT NOW(),
  "last_login" TIMESTAMP NULL
);

CREATE INDEX idx_characters_account ON characters(account_id);
CREATE INDEX idx_characters_name ON characters(name);
CREATE INDEX idx_characters_deleted ON characters(deleted_at) WHERE deleted_at IS NOT NULL;
```

### Table: character_appearance (NOUVEAU - optionnel)
```sql
CREATE TABLE "public"."character_appearance" (
  "character_id" INTEGER PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
  "hair_style" SMALLINT NOT NULL,
  "hair_color" SMALLINT NOT NULL,
  "face_style" SMALLINT NOT NULL,
  "eye_color" SMALLINT NOT NULL,
  "body_size" SMALLINT DEFAULT 0, -- Pour customization avancée
  "skin_color" SMALLINT NOT NULL
);
```

---

## 💻 Implémentation C#

### Fichiers à Créer

#### 1. Models/CharacterEntity.cs
```csharp
public class CharacterEntity
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public short Race { get; set; }
    public short Gender { get; set; }
    public int CurrentClassId { get; set; }
    public int CharacterLevel { get; set; }
    public long Experience { get; set; }
    
    // Appearance
    public short HairStyle { get; set; }
    public short HairColor { get; set; }
    public short FaceStyle { get; set; }
    public short EyeColor { get; set; }
    
    // Stats
    public int Str { get; set; }
    public int Dex { get; set; }
    public int Con { get; set; }
    public int Int { get; set; }
    public int Psy { get; set; }
    public int Agi { get; set; }
    
    // Position
    public int WorldId { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    
    // Currency
    public long Gold { get; set; }
    public int EP { get; set; }
    public int AP { get; set; }
    
    // Metadata
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}
```

#### 2. Game/Characters/CharacterNameValidator.cs
```csharp
public class CharacterNameValidator
{
    private const int MinNameLength = 3;
    private const int MaxNameLength = 16;
    
    public ValidationResult ValidateName(string name);
    private bool ContainsProfanity(string name);
    private bool HasValidCharacters(string name);
    private async Task<bool> IsNameAvailable(string name);
}
```

#### 3. Game/Characters/CharacterCreationService.cs
```csharp
public class CharacterCreationService
{
    public async Task<CharacterEntity> CreateCharacterAsync(
        int accountId,
        string name,
        short race,
        short gender,
        int startingClass,
        CharacterAppearance appearance);
        
    private CharacterStats GenerateInitialStats(short race, int classId);
    private (int worldId, Vector3 position) GetStartingPosition(short race);
    private async Task CreateStartingInventory(int characterId, int classId);
}
```

#### 4. Game/Characters/CharacterDeletionService.cs
```csharp
public class CharacterDeletionService
{
    public async Task<bool> MarkForDeletion(int characterId, int accountId);
    public async Task<bool> CancelDeletion(int characterId, int accountId);
    public async Task PermanentlyDeleteExpiredCharacters();
}
```

#### 5. Networking/Messages/CharacterPackets.cs
```csharp
// Request packets (CNE)
public class CreateCharacterNameCheckRequest : INetworkPacket
{
    public string Name { get; set; } = string.Empty;
}

public class CreateCharacterDataRequest : INetworkPacket
{
    public string Name { get; set; } = string.Empty;
    public short Race { get; set; }
    public short Gender { get; set; }
    public int ClassId { get; set; }
    public CharacterAppearance Appearance { get; set; } = new();
}

public class RemoveCharacterRequest : INetworkPacket
{
    public int CharacterId { get; set; }
}

// Response packets (CNC)
public class VerifyCharacterNameResponse : INetworkPacket
{
    public bool IsAvailable { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class CreateCharacterResponse : INetworkPacket
{
    public bool Success { get; set; }
    public int CharacterId { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class GetCharactersResponse : INetworkPacket
{
    public List<CharacterInfo> Characters { get; set; } = new();
}

// Support classes
public class CharacterAppearance
{
    public short HairStyle { get; set; }
    public short HairColor { get; set; }
    public short FaceStyle { get; set; }
    public short EyeColor { get; set; }
}

public class CharacterInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public short Race { get; set; }
    public short Gender { get; set; }
    public int CurrentClassId { get; set; }
    public int Level { get; set; }
    public CharacterAppearance Appearance { get; set; } = new();
    public DateTime? DeletedAt { get; set; }
}
```

#### 6. Networking/Handlers/CharacterCreationHandler.cs
```csharp
public class CharacterCreationHandler
{
    public async Task<VerifyCharacterNameResponse> HandleNameCheckAsync(
        CreateCharacterNameCheckRequest request);
        
    public async Task<CreateCharacterResponse> HandleCreateCharacterAsync(
        int accountId, 
        CreateCharacterDataRequest request);
        
    public async Task<GetCharactersResponse> HandleGetCharactersAsync(
        int accountId);
        
    public async Task<bool> HandleRemoveCharacterAsync(
        int accountId,
        RemoveCharacterRequest request);
}
```

#### 7. Data/Parsers/RaceDataParser.cs
```csharp
public class RaceDataParser
{
    // Option 1: Hardcoded (rapide)
    public static Dictionary<short, RaceData> GetHardcodedRaces()
    {
        return new Dictionary<short, RaceData>
        {
            { 1, new RaceData { Id = 1, Name = "Human", StartingClasses = new[] { 1, 2, 3 }, ... } },
            { 2, new RaceData { Id = 2, Name = "Zumi", StartingClasses = new[] { 1, 2, 3 }, ... } },
            { 3, new RaceData { Id = 3, Name = "Ursun", StartingClasses = new[] { 1, 2, 3 }, ... } },
            { 4, new RaceData { Id = 4, Name = "Halfkin", StartingClasses = new[] { 1, 2, 3 }, ... } },
            { 5, new RaceData { Id = 5, Name = "Anuran", StartingClasses = new[] { 1, 2, 3 }, ... } }
        };
    }
    
    // Option 2: Decode S_Races.ini (plus tard)
    public static Dictionary<short, RaceData> ParseRacesFile(string filePath)
    {
        // TODO: Decode avec MyCrypt
    }
}
```

---

## 📊 Règles de Validation

### Nom de Personnage
- ✅ Longueur: 3-16 caractères
- ✅ Caractères autorisés: A-Z, a-z, 0-9, tiret (-), underscore (_)
- ✅ Pas de double tiret/underscore consécutifs
- ✅ Commence et finit par une lettre/chiffre
- ✅ Unicité en base de données
- ✅ Filtre de profanité (liste de mots interdits)

### Création de Personnage
- ✅ Race valide (1-5)
- ✅ Gender valide (1-2)
- ✅ Classe de départ valide pour la race
- ✅ Apparence valide (IDs dans les ranges autorisés)
- ✅ Limite de 6 personnages par compte (configurable)

### Suppression
- ✅ Personnage appartient au compte
- ✅ Personnage non connecté
- ✅ Grace period de 24h avant marquage définitif
- ✅ Suppression permanente après 7 jours

---

## 🎯 Plan d'Exécution

### Étape 1: Schema & Entities (1h)
- [x] Créer migration 003-characters-schema.sql
- [ ] Créer CharacterEntity.cs
- [ ] Créer CharacterRepository.cs
- [ ] Tests unitaires repository

### Étape 2: Validation (1h)
- [ ] CharacterNameValidator.cs
- [ ] Règles de validation complètes
- [ ] Profanity filter (liste de base)
- [ ] Tests unitaires validation

### Étape 3: Services (2h)
- [ ] CharacterCreationService.cs
  - Création personnage
  - Génération stats initiales
  - Position de départ
  - Inventaire initial
- [ ] CharacterDeletionService.cs
  - Soft delete
  - Hard delete schedulé
- [ ] Tests unitaires services

### Étape 4: Network Layer (2h)
- [ ] CharacterPackets.cs (7 packets)
- [ ] CharacterCreationHandler.cs
- [ ] Tests unitaires handlers

### Étape 5: Data Files (1h)
- [ ] RaceDataParser.cs (hardcoded pour l'instant)
- [ ] CharColorDataParser.cs (T_CharColor.ini)
- [ ] Tests unitaires parsers

### Étape 6: Integration (1h)
- [ ] Tests d'intégration complets
- [ ] Documentation API
- [ ] Mise à jour MASTER-IMPLEMENTATION-PLAN.md

**Total estimé: 8 heures**

---

## 🔬 Tests à Implémenter

### Unit Tests
```csharp
// CharacterNameValidatorTests.cs
- ValidName_WithValidInput_ReturnsSuccess()
- InvalidName_TooShort_ReturnsError()
- InvalidName_TooLong_ReturnsError()
- InvalidName_InvalidCharacters_ReturnsError()
- InvalidName_Profanity_ReturnsError()
- InvalidName_AlreadyExists_ReturnsError()

// CharacterCreationServiceTests.cs
- CreateCharacter_WithValidData_CreatesSuccessfully()
- CreateCharacter_InvalidRace_ThrowsException()
- CreateCharacter_ExceedsLimit_ReturnsError()
- GenerateInitialStats_ForWarrior_ReturnsCorrectStats()

// CharacterDeletionServiceTests.cs
- DeleteCharacter_ValidRequest_MarksForDeletion()
- DeleteCharacter_NotOwned_ThrowsException()
- PermanentDelete_ExpiredCharacters_DeletesFromDB()
```

### Integration Tests
```csharp
// CharacterCreationIntegrationTests.cs
- FullFlow_NameCheck_Then_Create_Then_Delete()
- CreateMultipleCharacters_Concurrent_HandlesCorrectly()
```

---

## 📋 Checklist Phase 1.2

### Schema & Data
- [ ] Migration SQL créée
- [ ] CharacterEntity défini
- [ ] Repository implémenté
- [ ] Tests repository passent

### Validation
- [ ] CharacterNameValidator complet
- [ ] Règles de validation implémentées
- [ ] Profanity filter basique
- [ ] Tests validation passent

### Services
- [ ] CharacterCreationService implémenté
- [ ] CharacterDeletionService implémenté
- [ ] Stats initiales générées correctement
- [ ] Tests services passent

### Networking
- [ ] 7 packets définis
- [ ] Handler implémenté
- [ ] Tests handlers passent

### Data Files
- [ ] RaceDataParser (hardcoded)
- [ ] CharColorDataParser (T_CharColor.ini)
- [ ] Tests parsers passent

### Documentation
- [ ] Ce document complété
- [ ] API documentation
- [ ] MASTER-IMPLEMENTATION-PLAN.md mis à jour

---

## 🚀 Prochaines Étapes

**NOW:** Créer migration SQL 003-characters-schema.sql

**NEXT:** Implémenter CharacterEntity et Repository

**AFTER:** CharacterNameValidator

---

## 📊 Métriques Attendues

### Code Production
```
CharacterEntity.cs:             100 lignes
CharacterRepository.cs:         150 lignes
CharacterNameValidator.cs:      100 lignes
CharacterCreationService.cs:    250 lignes
CharacterDeletionService.cs:    150 lignes
CharacterPackets.cs:            300 lignes
CharacterCreationHandler.cs:    250 lignes
RaceDataParser.cs:              150 lignes
CharColorDataParser.cs:         100 lignes
────────────────────────────────────────
Total:                        1,550 lignes
```

### Tests
```
Tests:                          500 lignes
Scenarios:                      20+ tests
```

### Documentation
```
Cette doc:                      800 lignes
API doc:                        200 lignes
```

**Phase 1.2 Target: 0% → 100%**

---

**Document Status:** ✅ PLANNING COMPLETE  
**Ready For:** Implementation kickoff  
**ETA:** 8 heures (1 session complète)
