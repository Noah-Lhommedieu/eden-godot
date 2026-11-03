# Plan Maître d'Implémentation - Eden Eternal Server (.NET 8)

> **Version** : 1.1  
> **Date** : 15 Octobre 2026  
> **Dernière mise à jour** : 21 Octobre 2026
> **Objectif** : Reconstruction complète de tous les systèmes de jeu Eden Eternal en C#/.NET 8  
> **Approche** : Reverse engineering des binaires serveur originaux (ZoneServer, WorldServer, MissionServer) + implémentation systématique

> **Reprise audit 2026-10-21** : les anciennes cases `[x]` ne valent pas
> validation live. Les exigences du prompt3 sont réauditées depuis zéro
> dans `docs/temp-plans/2026-10-21-full-audit-inventory-profile-combat.md`;
> ne reporter aucun statut « terminé » sans preuve, test et vérification
> des deux clients.
>
> **Incidents test3 toujours ouverts** : inventaire non confirmé visuellement
> malgré la réponse 312 multi-sac; équipements hérités loc9/35 doivent être
> canonisés 7/10; affichage points/% du profil doit être remappé depuis
> `rf_game`; le client a reçu 292/52022 sur une ancienne DLL. La source
> actuelle retire le report générique, mais le combat n'est pas live-validé.
> Suivi détaillé et critères de sortie dans le plan d'audit ci-dessus.

---

## 📊 Inventaire des Classes lapis:: Extraites (Total: 9,123 classes)

### ZoneServer: 5,760 classes
### WorldServer: 905 classes  
### MissionServer: 2,458 classes

---

## 🎯 Roadmap d'Implémentation (100+ systèmes)

### ✅ PHASE 0: Protocole réseau et données (Bloquants résolus)

#### 0.1 Décodage MyCrypt ✅
- [x] Cipher = ZipCrypto canonique, password `ghfdsjdl` (@0x87cdff0)
- [x] `tools/mycrypt.py` + `Eden.Core/Data/MyCrypt.cs`
- [x] 164 fichiers décodés → `server_us/Data/db_decoded/`
- [x] `IniTable` parser générique (`|V.x|N|` + `V.x,N,` + multi-lignes)

#### 0.2 Vrais opcodes réseau ✅
- [x] `tools/extract_ncids.py` — opcode = index dans
      `InitPCCommandTable` (`mov %ax, <ncid_addr>`)
- [x] `docs/generated/ncid_map.json` — **1338 NCIDs réels**
      (Zone=871, World=204, Mission=205, Login=35, Gateway=23)
- [x] 1248 classes `Generated/*.g.cs` régénérées avec vrais opcodes
- [x] `tools/extract_cne_fields.py` + `fill_cne_fields.py` —
      **1209 corps de CNE_* remplis** depuis `Deserialize`
      (`operator>>(T&)`): ex `MeleeAttack` = int targetId + byte slot,
      `MoveSelf` = short + 6 floats + short, `TalkToNPC` = int npcId
- [x] Framing confirmé: `[u16 NCID][payload]` (CNetCommand::_Serialize)
- [x] Dispatch opcode→handler dans `Eden.Zone` (`PacketDispatcher`)
- [x] Handlers concrets prioritaires: MeleeAttack, MoveSelf,
      SpellCastToLife, TalkToNPC, ChatMessage
- [x] Handlers zone — **262/262 commandes CNE_CZ_ZoneServer** enregistrées
  (handlers domaine : entry, combat, inventory, trade, shop, social,
  team, elf, instance, player, item, vendor, family, housing, misc)
- [ ] Handlers World/Login/Mission restants (~440 commandes)
- [ ] Logique métier complète dans ~60% des handlers (38 TODO documentés
  "phase reverse" : layouts bouclés, persistance manquante)

#### 0.4 Payloads bouclés CNC ✅ (18 layouts reversés au désassembleur)
Layout récurrent natif : `u16/short count` + entrées (SoA ou AoS) +
éventuels scalaires suffixe. `tools/patch_cnc_loops.py` réécrit les
classes `.g.cs` en `List<Entry>` fidèle au wire.
- [x] `ClientMailList` — u16 + record complet/mail (boucle SoA
      0x8204660→0x8204b39, ~70 champs/entrée)
- [x] `ClientPlayerClassList` — [byte×2][u16 count] + byte +
      SCharacterClass(byte,byte,int,int,byte,ushort×7) → branché sur
      `player_classes` réel
- [x] `ClientTeamAllMembersInfo` — [short count] + membres AoS
      (stride 0x38) + [short] suffixe → broadcast réel
- [x] `ClientSearchTeamInfo` — boucles imbriquées équipe→membres
      (vector SSearchTeamData/SSearchTeamMemberData, strides 0x18/0x14)
- [x] `ClientEnterNodeShortcut` — [u16 bars] + par barre : u16 bitmask
      10 slots + int/slot marqué + u16 extras + int×n (patch manuel)
- [x] `ClientAuctionSellList`/`ClientAuctionBuyList` — u16(+short pour
      Buy) + record objet+enchère (~50 champs) → branchés sur
      AuctionService réel
- [x] `ClientLookEquip`/`PVPRankLookEquip`/`QueryPlayerCard`,
      `EnterNodeInventory`, `FamilyStorageLog`, `SearchRoomResult`,
      `AchievementUpdate`, `UpdateCollection`, `UpdateNPCSymbols`,
      `FamilyBattleKilledRank`, `UpdateFortuneBagCounter`
- [ ] `ClientEnterNodePlayerData` — ~30 scalaires + vecteur de records
      à maps imbriquées (3 boucles Rb_tree) — structure identifiée,
      sérialisation complète en cours
- [ ] `ClientEnterNodeEquipAndSpell` / `ClientGetStorageData` /
      `ClientFamilyStorageData` — diff-encodage conditionnel par objet
      (bitmask + champs émis si != sentinel) — nécessite un modèle
      de présence par champ
- [ ] `ClientEnterNodeElfContainer`/`PlayerRoom` — records d'objets
      (s×7,u,t×10,s×2,i64) + blobs binaires PutBinary sans longueur
      (taille implicite)

#### 0.3 Transport TCP complet ✅ (cassé par désassemblage)
- [x] Handshake: serveur envoie clé publique **RSA-2048** brute
      `[u32 n_len][u32 e_len][n][e]` (`MakeRSAKeyData` @0x8685610)
- [x] Client renvoie 256 octets `RSA_public_encrypt(clé RC4 5 octets)`,
      serveur `RSA_private_decrypt` (`ReceiveRC4Key` @0x868ca40)
- [x] Paquets: `[u16 len EN CLAIR][RC4(u16 NCID + champs)]`
      (`OnParsingHeader`/`MakePackets`/`NextOutputPacket` désassemblés)
- [x] RC4 = OpenSSL standard (@0x86a16a0), stream continu par direction,
      même clé 5 octets pour les deux sens — `Eden.Core/Networking/Rc4.cs`
- [x] `NetSession`: handshake RSA + framing + crypto intégrés
      (`SendRsaPublicKeyAsync`, `ReceiveRc4KeyAsync`, `ReadPacketsAsync`)
- [x] `std::string` réseau = `u16 len` + octets Big5 (operator>>@0x867af90)
- [x] Eden.Zone + Eden.World: boucle de réception réécrite sur le vrai
      framing + crypto (env `*_CRYPTO=0` pour mode clair debug)
- [x] RSA padding = **PKCS1** confirmé (`RSA_private_decrypt` arg=1,
      LoginServer `ReceiveRC4Key` @0x807c4af, résultat == 5 octets)
- [x] Canal client CMSTCPChannel : états 1→2→3 confirmés sur LoginServer
      (`Open` envoie blob RSA brut, `OnRead` état 2 → `ReceiveRC4Key`)
- [x] **POC Login opérationnel** : `Eden.Login` = vrai handshake RSA→RC4
      + dispatch NCID 35/36/37 + `ClientGetWorldServers` layout bouclé
      réel + ticket 22 octets — smoke test wire-level validé
      (voir `docs/implementation/45-login-poc.md`)
- [ ] Tests golden: valider nos octets contre captures/désassemblage

#### 0.3 Tables de configuration ✅
- [x] `S_Parameter.ini` — 677 paramètres de formules du moteur
- [x] `S_Level.ini` — XP + stats niveaux 1-101
- [x] `S_Ai.ini` — 707 profils IA monstres
- [x] `S_RootCmds.ini` + `S_GMToolCmds.ini` — 346 commandes GM
- [x] `S_Store.ini` — 670 inventaires de shops NPC

---

### ✅ PHASE 1: Systèmes Fondamentaux du Personnage (Priorité MAX)

#### ✅ 1.1 Système de Classes et Changement de Classe
**Status:** COMPLETED  
**Documentation:** `docs/implementation/01-class-system.md`  
**Implémentation C#:**
- [x] ClassEntity, ClassProgressEntity, ClassStatsEntity
- [x] ClassDataParser (T_Class.ini)
- [x] IClassRepository, ClassRepository
- [x] ClassChangeService
- [x] Tests unitaires ClassSystemTests (7 tests passants)
- [x] Table player_classes (PostgreSQL)

#### ✅ 1.2 Création et Suppression de Personnage
**Status:** COMPLETED  
**Documentation:** `docs/implementation/02-character-creation.md`  
**Implémentation C#:**
- [x] CreateCharacterRequest, CreateCharacterResult
- [x] CharacterCreationService
- [x] Tests unitaires CharacterCreationTests (3 tests passants)
- [x] Validation de nom, race, genre, classe

#### 🟡 1.3 Stats et Profil Joueur
**Status:** IN PROGRESS  
**Documentation:** `docs/implementation/03-player-stats.md`  
**Implémentation C#:**
- [x] PlayerStatsEntity
- [x] StatsCalculationService — **vraies formules S_Parameter.ini**
      (CON→HP=20, WIS→MP=15, STR→atk=10, INT→matk=5, AGI→dodge=10,
      crit=AGI/20+5%, dmg crit=150%, hit=95%, def×0.5, unarmed=1.5s)
- [x] `ProgressionParsers.ParseLevels()` — S_Level.ini (XP/stats niv. 1-101)
- [x] `ProgressionParsers.ParseRaces()` — S_Races.ini
- [x] Table character_stats (PostgreSQL)
- [x] Parser S_Class.ini pour stats réelles (305 définitions)
- [ ] Breakpoints de stats
- [x] Tests unitaires ✅ 62% COMPLETE
**Symboles clés identifiés:**
- `CCharacter::ChangeClassAA`, `GainClassPoint`, `SetClassLevel`
- `CClassDBO::GetAllClasses`, `InitAAEffectSets`, `InitClassSpellSets`
- `CPlayerFactory::AddClass`, `ChangeClass`, `RemoveClass`, `SwitchClassEquip`
- `CNE_CZ_ZoneServerChangeClass`, `QueryClassList`
- `CSpellMasterFactory::GetClassSpell`, `RemoveClassSpell`

**Status de l'implémentation:**

✅ **Phase 1.1.A: Parsing & Chargement (85%)**
- [x] `ClassDataParser.cs` - Parse T_Class.ini (60 classes: 1-20 base + 21-60 awakened) ✅
- [x] `ClassDataService.cs` - Service wrapper avec cache et helpers ✅
- [x] `ClassEntity.cs` - Entité base de données ✅
- [x] `ClassProgressEntity.cs` - Progression par classe ✅
- [x] `ClassStatsEntity.cs` - Stats par niveau ✅
- [x] Tests unitaires pour ClassDataParser ✅
- [x] Encoding ISO-8859-1 identifié et appliqué ✅
- [🚧] `ClassStatsParser.cs` - S_Class.ini decoder (100% - MyCrypt CASSÉ ✅)
  - ✅ Password trouvé: `ghfdsjdl` (.rodata @ 0x87cdff0)
  - ✅ Algorithme = ZipCrypto standard (init 0x12345678/0x23456789/0x34567890)
  - ✅ `MyCrypt.cs` C# implémenté + testé round-trip
  - ✅ `IniTable.cs` parser générique pour format |V.x|N|
  - ✅ S_Class.ini parsé: 305 définitions (61 classes × 5 races)
  - ✅ Stats raciales, sorts de classe, progression niveaux extraits
  - Outil standalone: `tools/mycrypt.py` (decode-all fonctionnel)
  - 164 fichiers .ini décodés dans `server_us/Data/db_decoded/`

✅ **Phase 1.1.B: Validation & Équipement (40%)**
- [x] `ClassChangeValidator.cs` - Validation complète des prérequis ✅
  - Validation niveau caractère ✅
  - Validation classes prérequises ✅
  - Validation quêtes (placeholder) ✅
  - Validation awakening (level 80 base requis) ✅
  - Validation équipement (placeholder pour item system) ✅
  - Calcul buffs raciaux ✅
- [x] `ClassChangeService.cs` - Service amélioré avec validation ✅
  - Integration avec ClassChangeValidator ✅
  - Ordre des opérations documenté (extraction binaire) ✅
  - Logging complet ✅
  - Gestion buffs raciaux ✅
- [x] Tests unitaires pour ClassChangeValidator (9 tests) ✅
- [ ] Equipment auto-switch logic (⏳ dépend système items)
- [ ] Racial buff application (⏳ dépend système buffs)
- [ ] Quest validation integration (⏳ dépend système quêtes)

✅ **Phase 1.1.C: Handlers Réseau (60%)**
- [x] `ClassPackets.cs` - All packet definitions (CNE/CNC) ✅
  - ChangeClassRequest ✅
  - QueryClassListRequest ✅
  - ChangeClassShortcutRequest ✅
  - PlayerClassListResponse ✅
  - ChangeClassResponse ✅
  - ClassChangeBroadcast ✅
  - ClassShortcutResponse ✅
- [x] `ClassChangeHandler.cs` - Complete handler implementation ✅
  - HandleChangeClassAsync() ✅
  - HandleQueryClassListAsync() ✅
  - HandleChangeShortcutAsync() ✅
  - BroadcastClassChangeAsync() (placeholder) ✅
  - CalculateStatsForClass() ✅
- [x] Tests unitaires handlers (5 tests) ✅
- [ ] Session management integration (⏳ dépend network layer)
- [ ] Broadcasting implementation (⏳ dépend zone system)

**Fichiers créés/modifiés (COMPLET):**
```
✅ Eden.Core/Data/Parsers/ClassDataParser.cs (370 lignes)
✅ Eden.Core/Game/Classes/ClassDataService.cs (120 lignes)
✅ Eden.Core/Game/Classes/ClassChangeValidator.cs (200 lignes)
✅ Eden.Core/Game/Services/ClassChangeService.cs (230 lignes)
✅ Eden.Core/Data/Parsers/ClassStatsParser.cs (250 lignes - MyCrypt + placeholders)
✅ Eden.Core/Networking/Messages/ClassPackets.cs (300 lignes) [NOUVEAU]
✅ Eden.Core/Networking/Handlers/ClassChangeHandler.cs (250 lignes) [NOUVEAU]
✅ Eden.Tests/Data/Parsers/ClassDataParserTests.cs (80 lignes)
✅ Eden.Tests/Game/Classes/ClassChangeValidatorTests.cs (150 lignes)
✅ Eden.Tests/Networking/Handlers/ClassChangeHandlerTests.cs (100 lignes) [NOUVEAU]
```

**Métriques Finales:**
- Classes parsées: 60/60 (100%) ✅
- Code production: ~1,970 lignes
- Tests unitaires: ~330 lignes (19 tests)
- Documentation: 7 rapports complets

**Dépendances externes à implémenter:**
- ~~❗ S_Class.ini exact keys~~ ✅ RÉSOLU: password `ghfdsjdl`, ZipCrypto, 164 fichiers décodés
- Système d'items (pour validation équipement)
- Système de buffs (pour application buffs raciaux)
- Système de quêtes (pour validation quêtes)
- Network session management (pour routing packets)
- Zone system (pour broadcasting)

**Phase 1.1 Total: 62% COMPLETE** ✅  
*Progression: 15% → 62% (+47 points cette session)*

#### 1.2 Création et Suppression de Personnage ✅ 50% COMPLETE
**Symboles clés identifiés (WorldServer):**
- `CNE_CW_ServerCreateCharacterNameCheck` @ 0x080d9c60
- `CNE_CW_ServerCreateCharacterName` @ 0x080d8fe0
- `CNE_CW_ServerCreateCharacterData` @ 0x080d7ef0
- `CNE_CW_ServerRemoveCharacter` @ 0x080e9c60
- `CNC_CW_ClientVerifyCharacterName` @ 0x0807b570
- `CCharacterDBO` @ 0x0809cf10

**Status de l'implémentation:**

✅ **Phase 1.2.A: Database Schema (100%)**
- [x] Migration 003-characters-schema.sql ✅
  - Table characters (30+ colonnes)
  - character_stats_cache (performance)
  - character_playtime_log (analytics)
  - Triggers: character limit, rename prevention
  - Functions: soft_delete, cancel_deletion, purge_expired, name_availability
  - active_characters view
  - Comprehensive indexes

✅ **Phase 1.2.B: Domain Layer (100%)**
- [x] `CharacterEntity.cs` (210 lignes) ✅
  - Complete character model avec helpers
  - IsPendingDeletion, IsExpired, IsActive
  - GetRaceName(), GetGenderName(), GetPlaytime()
- [x] `CharacterRepository.cs` (350 lignes) ✅
  - Full CRUD operations
  - Soft delete avec grace period (7 jours)
  - Name availability, character count
  - Position updates, online status

✅ **Phase 1.2.C: Validation & Business Logic (100%)**
- [x] `CharacterNameValidator.cs` (200 lignes) ✅
  - Length validation (3-16 caractères)
  - Character validation (alphanumeric, dash, underscore)
  - No consecutive special chars
  - Profanity filter (basic list)
  - Database uniqueness check
- [x] `CharacterCreationService.cs` (250 lignes) ✅
  - Account limit validation (6 characters max)
  - Complete validation pipeline
  - Initial stats generation (race + class modifiers)
  - Starting position by race
  - Starting currency (100 gold)
- [x] `CharacterDeletionService.cs` (150 lignes) ✅
  - Soft delete with 7-day grace period
  - Cancel deletion
  - Purge expired characters
  - Ownership and online status validation

✅ **Phase 1.2.D: Race System (100%)**
- [x] `RaceDataParser.cs` (150 lignes) ✅
  - Hardcoded 5 races (Human, Zumi, Ursun, Halfkin, Anuran)
  - Base stats per race
  - Starting classes per race
  - Starting positions
  - Racial buff IDs (placeholder)
  - Ready for S_Races.ini decoding later

🚧 **Phase 1.2.E: Network Layer (0%)**
- [ ] CharacterPackets.cs (7 packets)
  - CreateCharacterNameCheckRequest
  - CreateCharacterDataRequest
  - RemoveCharacterRequest
  - VerifyCharacterNameResponse
  - CreateCharacterResponse
  - GetCharactersResponse
- [ ] CharacterCreationHandler.cs
  - HandleNameCheckAsync()
  - HandleCreateCharacterAsync()
  - HandleGetCharactersAsync()
  - HandleRemoveCharacterAsync()

🚧 **Phase 1.2.F: Tests (0%)**
- [ ] CharacterNameValidatorTests (6+ tests)
- [ ] CharacterCreationServiceTests (5+ tests)
- [ ] CharacterDeletionServiceTests (4+ tests)
- [ ] CharacterRepositoryTests (10+ tests)
- [ ] Integration tests

**Fichiers créés:**
```
✅ Database/003-characters-schema.sql (300+ lignes SQL)
✅ Eden.Core/Data/Entities/CharacterEntity.cs (210 lignes)
✅ Eden.Core/Data/Repositories/CharacterRepository.cs (350 lignes)
✅ Eden.Core/Data/Parsers/RaceDataParser.cs (150 lignes)
✅ Eden.Core/Game/Characters/CharacterNameValidator.cs (200 lignes)
✅ Eden.Core/Game/Characters/CharacterCreationService.cs (250 lignes)
✅ Eden.Core/Game/Characters/CharacterDeletionService.cs (150 lignes)
✅ docs/implementation/02-character-creation.md (800+ lignes)
```

**Métriques:**
- Code production: ~1,310 lignes ✅
- SQL: 300+ lignes ✅
- Documentation: 800+ lignes ✅
- Tables: 3 (characters, stats_cache, playtime_log) ✅
- Functions: 4 SQL functions ✅

**Dépendances externes:**
- Phase 2.1: Starting inventory creation
- Phase 1.1: player_classes integration
- Network layer: Session management, packet routing

**Phase 1.2 Total: 50% COMPLETE** ✅  
*Core implementation done, network layer pending*

#### 1.3 Stats et Profil Joueur
**Symboles clés:**
- `CCharacter`, `CCharFormula`, `CLevelDBO`
- Attributs: STR, DEX, CON, INT, PSY, AGI

**Livréables:**
- [ ] `docs/implementation/03-player-stats.md`
- [x] Formules de calcul de stats — `StatsCalculationService` utilise
      les vrais parametres `S_Parameter.ini` (CON→HP=20, WIS→MP=15,
      STR→atk=10, INT→matk=5, AGI→dodge=10, crit=AGI/20+5%, dmg crit=150%,
      hit=95%, def mod=0.5, unarmed=1.5s)
- [ ] Breakpoints de stats
- [x] Table de progression par niveau — `ProgressionParsers.ParseLevels()`
      charge `S_Level.ini` (niveaux 1-101: XP requise, HP/MP, stats)

---

### PHASE 2: Système d'Inventaire et Économie

#### 2.1 Inventaire et Sacs
**Symboles clés:**
- `CInventory`, `CBagContainer`, `CAddItemControl`
- `CCharacter::RegAllItems`, `ClearAllItems`
- Slot rental, expansion de sac

**Livréables:**
- [ ] `docs/implementation/04-inventory-system.md`
- [ ] Gestion des slots (équipement, consommable, matériaux)
- [ ] Système de stack/stack splitting
- [ ] Tri et auto-stack
- [ ] Location de slots (slot rental)

#### 2.2 Banque et Stockage
**Symboles clés:**
- `CBankShop`, `CFamilyStorageShop`

**Livréables:**
- [x] `docs/implementation/05-bank-storage.md`
- [x] Banque personnelle — BankService + BankRepository corrigé
  (slots libres via generate_series, auto-stack, swap, opcodes 60-69)
- [x] Stockage famille/guilde — FamilyBankRepository + handlers
  FamilyStoragePut/Get (inventaire ↔ family_bank)
- [x] Expansion de stockage — prix S_Parameter 409/410 (1500/3500),
  cap `expand_storage_cnt` < 2 persisté, 373 + textes natifs
- [ ] Accessibilité et cooldowns

#### 2.3 Système d'Échange (Trade)
**Symboles clés:**
- `CEC_StateNoTrade`, Trade window

**Livréables:**
- [ ] `docs/implementation/06-trade-system.md`
- [ ] Fenêtre d'échange P2P
- [ ] Validation des items
- [ ] Anti-scam (confirmation double)

#### 2.4 Hôtel des Ventes (Auction)
**Symboles clés:**
- `CAuctionShop`, `CNE_CZ_ZoneServerAuction*`

**Livréables:**
- [ ] `docs/implementation/07-auction-system.md`
- [x] Mise en vente — `AuctionService.List` (prix, duree, buyout)
- [x] Recherches et filtres — `Search` (itemId, prix max, vendeur)
- [x] Frais de vente et durée — depot 5%, commission 8%, TTL + expiration
- [x] Récupération des items/gold non vendus — `Expire` retourne au vendeur
- [x] Handlers zone branchés — AuctionSell (retrait inventaire + dépôt),
  AuctionBuy (gold + item livré par mail), AuctionCancel + queries

#### 2.5 Boutiques (AP/EP Item Mall)
**Symboles clés:**
- `CItemShop`, `CNE_CZ_ZoneServerItemMall*`
- `CPayManager`, `CGlobalInfo`

**Livréables:**
- [ ] `docs/implementation/08-item-mall.md`
- [x] `S_ItemMall.ini` charge (2.9MB en clair, meme schema 78 colonnes
      que S_Item.ini — items AP/EP deja indexables via ItemParser)
- [x] `S_ItemMallEnchant.ini` parse (enchants de la boutique)
- [x] Boutique AP (Aeria Points) — `ItemMallService` (wallets AP/EP,
      `Purchase`, `EffectivePrice` avec promos)
- [x] Boutique EP (Eden Points)
- [x] Catégories et promotions (discount + SaleEndsAt)
- [x] Items limités/time-limited (`PurchaseLimit` par joueur)

---

### PHASE 3: Système de Craft et Amélioration

#### 3.1 Craft de Base 🚧
**Symboles clés:**
- `CEC_CombineItem`, `CCubeCrafting`
- `CCombineOp`, `CCompoundOp`, `CCubeOp`

**Livréables:**
- [🚧] `docs/implementation/09-crafting-basic.md`
- [x] `CraftRecipeParser.cs` — recettes elfe (S_ElfCombine.ini: 957), guilde (S_FamilyCombine.ini: 3275), combos (S_ItemCombo.ini)
- [x] `CraftService.cs` — exécution recettes, scripts bonus pondérés
- [ ] Chance de succès (formule exacte à reverser depuis CCombineOp)
- [ ] Consommation matériaux (attend API inventaire par itemId)

#### 3.2 Craft de Race 🚧
**Symboles clés:**
- Système de craft spécifique par race

**Livréables:**
- [🚧] `docs/implementation/10-race-crafting.md`
- [x] Recettes raciales chargées (S_ElfCombine col 2 = race/elf requirement)
- [ ] Bons raciaux et utilisation

#### 3.3 Enchantement (Amélioration d'équipement) 🚧
**Symboles clés:**
- `CEnchantMgr`, `CEC_Enchant*` (250+ fonctions)
- `CCharacter::RecoverAllEnchants`, `RemoveEnchant`
- Enchant levels,风险评估

**Livréables:**
- [🚧] `docs/implementation/11-enchanting-system.md`
- [x] `EnchantParser.cs` — S_Enchant.ini (19 829 enchants, triggers, spells, chances)
- [x] `EnchantService.cs` — moteur de procs (trigger events + rolls)
- [x] Niveaux d'enchant (+1 à +20) — `EquipmentEnchantService`
- [x] Taux de succès et risques de downgrade — vraies chances de
      `S_Parameter.ini` (arme +11..+14 = 50%, +15..+20 = 25%;
      armure +11..+14 = 100%, +15..+20 = 50%; downgrade -1 sauf protection)
- [ ] Enchant raciaux (AA enchants)
- [x] Protection enchant (`TryEnchant(..., hasProtection)`)

#### 3.4 Fusion et Upgrade
**Symboles clés:**
- `CCloneItem`, `CEC_Assemble`
- Système de fusion d'items

**Livréables:**
- [ ] `docs/implementation/12-item-fusion.md`
- [ ] Fusion d'équipements
- [ ] Upgrade de stats
- [ ] Conservation de bonus

---

### PHASE 4: Système de Combat Avancé

#### 4.1 Skills et Sorts 🚧
**Symboles clés:**
- `CSpellFactory`, `CSpellMasterFactory`
- `CSpellTemplate`, `SSpellData`
- `CCharacter::ClearAllTeamSpell`

**Livréables:**
- [🚧] `docs/implementation/13-spell-system.md`
- [x] `SpellParser.cs` — S_Spell.ini (4 485 sorts: id, code, catégorie, cible, durée, résultat)
- [x] `SpellService.cs` — application, buffs actifs + stacks, ClearAllBuffs (=ClearAllTeamSpell), ticks
- [ ] Cooldowns (données dans S_Spell cols non encore mappées)
- [ ] MP/SP consumption
- [ ] Types de sorts (dégâts, buff, debuff, heal)
- [ ] Passifs raciaux (RacialSpellIds extraits de S_Class.ini)

#### 4.2 Proc d'Items et Enchants 🚧
**Symboles clés:**
- `CEC_OnAttackBuff*`, `CEC_OnBeAttack*`
- Chance on hit, chance on be hit

**Livréables:**
- [🚧] `docs/implementation/14-item-procs.md`
- [x] `EnchantService.Evaluate()` — procs OnAttack/OnBeAttacked/OnCritical/OnKill/OnCast
- [x] Chance rolls depuis S_Enchant.ini (cols 37-39)
- [ ] Stack limit et durée (partiel — via SpellService.Stacks)

#### 4.3 Breakpoints de Combat
**Symboles clés:**
- `CCharFormula`, formules de dégâts

**Livréables:**
- [ ] `docs/implementation/15-combat-breakpoints.md`
- [x] Pipeline de combat complet — `CombatService` (hit 95% → dodge
      AGI/10 → block → crit AGI/20+5% → dmg = atk-def×0.5 ±10% →
      crit ×150% → résistances → procs)
- [x] `PhysicalAttack` / `SpellAttack` / `RollDodge`
- [x] Procs intégrés (OnAttack/OnCritical/OnBeAttacked/OnCast)
- [ ] Breakpoint vitesse d'attaque
- [x] Formules exactes de calcul — `NativeCombatFormulas` (lot `09a1f30`)

---

### PHASE 5: Systèmes de Groupe et Contenu Multijoueur

#### 5.1 Party (Groupe)
**Symboles clés:**
- `CTeamManager`, `CTeamMember`
- `CCharacter::OnCreateTeam`, `OnQuitFromTeam`
- `CEC_PartyExp`

**Livréables:**
- [x] `TeamEntity.cs` — Party (5) / Raid (25), loot rules
- [x] `TeamService.cs` — create/add/leave/kick/leadership, ShareExperience (CEC_PartyExp), PickLootReceiver
- [ ] `docs/implementation/16-party-system.md`
- [ ] Round-robin réel + taunt

#### 5.2 Raid (25+ joueurs)
**Symboles clés:**
- `CCharacter::IsInRaid25Instance`, `IsInRaidInstance`

**Livréables:**
- [x] Gestion de 25 joueurs (`TeamEntity.IsRaid`, MaxRaidSize=25)
- [ ] `docs/implementation/17-raid-system.md`
- [ ] Raid instances dédiées + mécaniques raid

#### 5.3 Random/Matchmaking
**Symboles clés:**
- `CCharacter::ChangeRecruitTeamData`
- Système LFG (Looking For Group)

**Livréables:**
- [ ] `docs/implementation/18-matchmaking.md`
- [x] Queue random dungeon — `MatchmakingService` (Enqueue/Dequeue/
      ProcessQueue par battlefield cible)
- [x] Role balancing (tank/heal/dps) — `PickBalancedGroup` 1 tank +
      1 heal + 3 dps, fallback flex
- [ ] Rewards de random

#### 5.4 Guildes/Familles
**Symboles clés:**
- `CFamily`, `CFamilyDBO`
- `CFamilyBattle`, `CFamilyBuildingShop`
- Système complet de guilde

**Livréables:**
- [x] `GuildService.cs` — création, rangs (Leader→Recruit), XP/niveau,
      trésor, taxes (FamilyLevyTaxes), distribution
- [ ] `docs/implementation/19-guild-system.md`
- [ ] Family battles (Territory War)

---

### PHASE 6: Donjons et Instances

#### 6.1 Dungeons Solo et Multi
**Symboles clés:**
- `CBattlefield`, `CBattlefieldFactory`
- `CDoor` (portes d'instance)

**Livréables:**
- [x] `docs/implementation/20-dungeon-system.md`
- [x] `BattlefieldParser.cs` — S_Battlefield.ini (donjons: mode, joueurs, mapId, minLevel, 3×9 paliers rewards)
- [x] `DungeonService.cs` — CanEnter/Enter/Complete/Abandon, états instance
- [ ] Respawn et checkpoint, CDoor entrées

#### 6.2 Colosseum (PvP)
**Symboles clés:**
- `CColosseum`, `CColosseumFactory`
- `CCharacter::AddPlayerColosseumClassPK`

**Livréables:**
- [ ] `docs/implementation/21-colosseum-pvp.md`
- [ ] Arenas 1v1, 3v3, 5v5
- [ ] Class PK rankings
- [ ] Matchmaking PvP
- [ ] Seasonal rewards

#### 6.3 Battle Crusade (Large PvP)
**Symboles clés:**
- `CBattleCrusade`, `CBattleCrusadeFactory`
- Événements massifs

**Livréables:**
- [ ] `docs/implementation/22-battle-crusade.md`
- [ ] Large scale PvP events
- [ ] Objectifs et capture
- [ ] World PvP integration

#### 6.4 Battle Royale
**Symboles clés:**
- `CBattleRoyale`, `CBattleRoyaleFactory`

**Livréables:**
- [ ] `docs/implementation/23-battle-royale.md`
- [ ] Mode Battle Royale
- [ ] Shrinking zone
- [ ] Last man standing

---

### PHASE 7: Relations Sociales

#### 7.1 Amis (Friends)
**Symboles clés:**
- `CFriend`, `CCharacter::IsMyFriend`
- `CNE_CW_ServerAddFriend`, `RemoveFriend`

**Livréables:**
- [x] `FriendService.cs` — demandes/accept/refus/remove/block,
      Search communauté (filtres nom/niveau/classe)
- [ ] `docs/implementation/24-friend-system.md`
- [ ] Online status + friend chat

#### 7.2 Couple/Lover (Mariage)
**Symboles clés:**
- `CLover`, `CWedding`, `CEventWedding`
- `CCharacter::GetLover`, `IsInWedding`

**Livréables:**
- [x] `LoverService.cs` — propose/accept/decline, affection, mariage
      (CWedding), divorce, GetPartnerForTeleport (CEC_GotoLover)
- [x] `WeddingService.cs` (mission) — cérémonies planifiées
      `CWeddingFactory` (≤5, statuts 0-3 + durées natives), relais
      `WeddingInfoQuery`/`ReserverChuch`/`CloseWedding` → ZMis 193 → 599
- [ ] `docs/implementation/25-marriage-system.md`
- [ ] Couple skills/buffs, `VisitWedding`/salle de cérémonie (node +
      téléport monde), `PrintedInvitations`, `GetMarry`, `MarryOper`

#### 7.3 Community Search
**Symboles clés:**
- `CNE_CW_ServerLookingForGroup`, `LookingForMember`

**Livréables:**
- [x] Recherche joueurs + filtres (dans `FriendService.Search`)
- [ ] `docs/implementation/26-community-search.md`
- [ ] Recrutement guilde dédié

---

### PHASE 8: Systèmes de Monstres et Drops

#### 8.1 Monstres et Boss 🚧
**Symboles clés:**
- `CMonster`, `CMonsterDBO`, `CMonsterTemplate`
- `CMonsterGroup`, `CRebornMonster`
- IA: `CAIFSM*` (Idle, Hunt, Roam, Combat, etc.)

**Livréables:**
- [🚧] `docs/implementation/27-monster-system.md`
- [x] `MonsterParser.cs` — S_Monster.ini (12 896 monstres: HP/MP/ATK/résistances/spells)
- [x] `MonsterEntity.cs` — instance en jeu (HP, position, dégâts, mort)
- [x] `MonsterService.cs` — spawn/kill/despawn, détection boss (HP ≥ 1M)
- [ ] AI et patrouille (CAIFSM*)
- [ ] Aggro et threat (taunt)
- [ ] Reborn monsters (world bosses)

#### 8.2 Drops et Loot 🚧
**Symboles clés:**
- `CTreasure`, `CItemFactory`
- `CMonster::GiveClassItemDrop`

**Livréables:**
- [🚧] `docs/implementation/28-loot-system.md`
- [x] `GameDataParsers.ParseClassDropItem()` — S_ClassDropItem.ini (290 tables)
- [x] `DropAndSpeedParsers.ParseDropItem()` — S_DropItem.ini (5 049 tables pondérées)
- [x] `LootService.cs` — RollDropTable (pondéré), RollClassDrop (par chance), LootMonster
- [ ] Distribution party

---

### PHASE 9: Systèmes Alternatifs

#### 9.1 Familiers (Pets/Elfs)
**Symboles clés:**
- `CElfContainer`, `CNE_CZ_ZoneServerElf*`
- `CEC_Elf*` (Elf mood, work, energy, etc.)

**Livréables:**
- [x] `docs/implementation/29-pet-system.md`
- [x] `ElfParser.cs` — S_Elf.ini (226 elfs: stats, skills, mood scripts)
- [x] `ElfService.cs` — Acquire/Mood/Energy/Friendship/Feed, intégré au craft
- [ ] Evolution/fusion d'elfs, S_ElfCollect/S_ElfEvent

#### 9.2 Montures (Rides)
**Symboles clés:**
- `CEC_RideSpell`, `CRideList`

**Livréables:**
- [ ] `docs/implementation/30-mount-system.md`
- [ ] Acquisition et équipement de montures
- [ ] Vitesse et bonus
- [ ] Mount skills (and battle mount)

#### 9.3 Maisons Personnalisables
**Symboles clés:**
- `CPlayerRoom`, `CPlayerRoomDBO`
- `CDecorate`

**Livréables:**
- [ ] `docs/implementation/31-housing-system.md`
- [ ] Instance de maison
- [ ] Décoration et furniture
- [ ] Expansion de rooms

#### 9.4 Success et Titres 🚧
**Symboles clés:**
- `S_Achievement`, `T_Achievement`
- `S_Title`, `T_Title`

**Livréables:**
- [🚧] `docs/implementation/32-achievement-titles.md`
- [x] `GameDataParsers.ParseAchievements()` — S_Achievement.ini (3 844 succès)
- [x] `GameDataParsers.ParseTitles()` — S_Title.ini (337 titres + spell bonus + conditions)
- [x] `AchievementService` — compteurs de progression, Complete/IsCompleted,
      titre actif (stats du titre)
- [ ] Conditions de déblocage (moteur à relier à MissionService/events)
- Honor title stats (no need equip, just possessed for get permanent buff stats) 

#### 9.5 Dream of Destruction (Facultatif)
**Symboles clés:**
- Système spécial identifié dans les logs

**Livréables:**
- [ ] `docs/implementation/33-dream-destruction.md`
- [ ] Mécaniques spécifiques
- [ ] Rewards uniques

#### 9.6 Ranking et Leaderboards
**Symboles clés:**
- `CCharacter::AddPlayerColosseumClassPK`
- Rankings multiples

**Livréables:**
- [ ] `docs/implementation/34-ranking-system.md`
- [x] `RankingService` — scores par categorie (prestige, classe, kills,
      villes, arene hebdo), Top N, rang joueur, reset hebdo/mensuel
- [x] `S_RankAward.ini` parse (40 categories de recompenses par rang)
- [ ] Class PK rankings
- [ ] Guild rankings
- [ ] Achievement rankings

---

### PHASE 10: Systèmes de Cartes et Téléportation

#### 10.1 Maps et Téléportation 🚧
**Symboles clés:**
- `CNode`, `CNodeTemplate`
- `CDoor` (téléporteurs)
- `CEC_UpdatePCXY`, `CEC_GotoLover`

**Livréables:**
- [🚧] `docs/implementation/35-map-teleportation.md`
- [x] `GameDataParsers.ParseNodes()` — S_Node.ini (379 maps: codes, bornes, voisins, POI, BGM)
- [x] `WorldParsers.ParseTransport()` — S_Transport.ini (40 routes:
      couts par palier de niveau, flags 'c1' conditions)
- [ ] Système de téléporteurs inter-maps (service runtime)
- [ ] Save points, Recall/City scrolls

#### 10.2 Fast Travel
**Symboles clés:**
- `CCityScroll`, `CFriendScroll`

**Livréables:**
- [ ] `docs/implementation/36-fast-travel.md`
- [ ] Scrolls de téléportation
- [ ] Costs et cooldowns
- [ ] Unlock requirements

---

### PHASE 11: Systèmes Économiques Avancés

#### 11.1 Mail System
**Symboles clés:**
- `CMailShop`, `CNE_ZMis_MissionServerMail*`

**Livréables:**
- [x] `docs/implementation/37-mail-system.md`
- [x] `MailService.cs` — envoi (gold+items×5+COD), inbox, claim,
      TTL 30j, retour des pièces jointes à l'expéditeur
- [x] Persistance — table `player_mail` + `MailRepository`
- [x] Handlers zone — MailSend/Query/Read/GetGold/GetItem/Delete/Return
      + CNC MailSendResult/MailOpenBox

#### 11.2 Taxation et Revenus
**Symboles clés:**
- `CCharacter::FamilyLevyTaxes`, `FamilyLevyBusinessTaxes`

**Livréables:**
- [ ] `docs/implementation/38-taxation-system.md`
- [ ] Taxes de guilde
- [ ] Revenus de boutiques
- [ ] Distribution aux membres

---

### PHASE 12: Systèmes de Quêtes et Missions

#### 12.1 Système de Quêtes
**Symboles clés:**
- `CMission`, `CMissionTemplate`, `CMissionDBO`
- `T_Mission.ini`, `S_Mission.ini`
- `CNE_ZMis_MissionServer*` (100+ commandes)

**Livréables:**
- [🚧] `docs/implementation/39-quest-system.md`
- [x] `MissionParser.cs` — S_Mission.ini (2 831 quêtes: scripts `#:Cmd`)
- [x] `MissionService.cs` — machine d'état: CanAccept (If_CharLevel, If_FMS,
      If_Gender, CheckBuff), NotifyEvent (MonsterKilled, ItemCollect, NPCTalk,
      AreaTravel...), IsComplete, Complete → récompenses (RewardGold/Exp/Item/
      Buff/Reputation/SpecialCoin/ChooseItem/ClassPoint/FamilyExp)
- [x] Persistance DB de la progression — `MissionRepository` +
      `MissionDb` côté Eden.Mission (états FMS, trace, progression)
- [ ] Daily/Weekly quests
- [ ] Scenario missions

#### 12.2 Dialogue System
**Symboles clés:**
- `T_Dialogue.ini`, `S_Dialogue.ini`
- `CNE_ZMis_MissionServerListDialogue`

**Livréables:**
- [ ] `docs/implementation/40-dialogue-system.md`
- [x] `WorldParsers.ParseDialogues()` — S_Dialogue.ini (2 800+ textes id|Big5)
- [x] `WorldParsers.ParseNpcs()` — S_Npc.ini (NPCs: scripts
      `#:Talk/#:Service`, role queteur/marchand)
- [ ] Branching conditions
- [ ] NPC interactions (ALL specials interaction of each npc who exist in database and game)

---

### PHASE 13: Skills de Vie (Life Skills)

#### 13.1 Life Skills
**Symboles clés:**
- `CNE_CZ_ZoneServerLifeSkill*`
- `S_LifeSkill.ini`

**Livréables:**
- [ ] `docs/implementation/41-life-skills.md`
- [x] `WorldParsers.ParseLifeSkills()` — S_LifeSkill.ini (nodes de
      recolte → produits: itemId, valeur, temps)
- [x] `LifeSkillService` — StartGather (check niveau), TickGather
      (production), niveaux par joueur
- [ ] Mining, Herbalism, Fishing
- [ ] Skill leveling

---

### PHASE 14: Systèmes Administratifs

#### 14.1 GM Commands
**Symboles clés:**
- `CGMTCPChannel`, `CNE_G_Client*`
- `S_GMToolCmds.ini`

**Livréables:**
- [ ] `docs/implementation/42-gm-commands.md`
- [x] `ProgressionParsers.ParseGmCommands()` — S_RootCmds.ini (332 cmds)
      + S_GMToolCmds.ini (ban, no_speak, gmannounce, tpa_save, speed, item give...)
- [x] `GmCommandService` — dispatch `/cmd args`, check niveau d'acces,
      handlers enregistrables
- [ ] Logging des actions GM

#### 14.2 Anti-Cheat et Sécurité
**Symboles clés:**
- `CEventCaptcha`, `CIllpluginDBO`

**Livréables:**
- [ ] `docs/implementation/43-security.md`
- [ ] Captcha system
- [ ] Speed hack detection
- [ ] Item duplication prevention

---

### PHASE 15: Systèmes de Performance et Optimisation

#### 15.1 Performance Monitoring
**Symboles clés:**
- `PerfTimer`, `CEventMemoryRecycle`

**Livréables:**
- [ ] `docs/implementation/44-performance.md`
- [ ] Memory management
- [ ] Performance counters
- [ ] Garbage collection tuning

---

## 📁 Structure du Dossier d'Implémentation

```
docs/implementation/
├── 01-class-system.md
├── 02-character-creation.md
├── 03-player-stats.md
├── 04-inventory-system.md
├── 05-bank-storage.md
├── 06-trade-system.md
├── 07-auction-system.md
├── 08-item-mall.md
├── 09-crafting-basic.md
├── 10-race-crafting.md
├── 11-enchanting-system.md
├── 12-item-fusion.md
├── 13-spell-system.md
├── 14-item-procs.md
├── 15-combat-breakpoints.md
├── 16-party-system.md
├── 17-raid-system.md
├── 18-matchmaking.md
├── 19-guild-system.md
├── 20-dungeon-system.md
├── 21-colosseum-pvp.md
├── 22-battle-crusade.md
├── 23-battle-royale.md
├── 24-friend-system.md
├── 25-marriage-system.md
├── 26-community-search.md
├── 27-monster-system.md
├── 28-loot-system.md
├── 29-pet-system.md
├── 30-mount-system.md
├── 31-housing-system.md
├── 32-achievement-titles.md
├── 33-dream-destruction.md
├── 34-ranking-system.md
├── 35-map-teleportation.md
├── 36-fast-travel.md
├── 37-mail-system.md
├── 38-taxation-system.md
├── 39-quest-system.md
├── 40-dialogue-system.md
├── 41-life-skills.md
├── 42-gm-commands.md
├── 43-security.md
└── 44-performance.md
```

---

## 🔬 Méthodologie de Reverse Engineering

Pour chaque système:

1. **Analyse des symboles lapis::** Extraire toutes les classes/fonctions liées
2. **Décompilation Ghidra:** Analyser le code assembleur des fonctions clés
3. **Corrélation fichiers .ini:** Identifier les fichiers de données associés
4. **Protocole réseau:** Identifier les messages CNC_*/CNE_* associés
5. **Documentation DB:** Identifier les tables PostgreSQL concernées
6. **Implémentation C#:**
   - Créer les entités C#
   - Implémenter la logique métier
   - Créer les handlers réseau
   - Écrire les tests unitaires
7. **Documentation:** Rédiger le markdown d'implémentation avec:
   - Description du système
   - Symboles analysés
   - Formules et algorithmes
   - Code C# implémenté
   - Tests de validation

---

## 📈 Métriques de Progression

- **Total systèmes:** 44
- **Total classes à analyser:** 9,123
- **Total fichiers .ini à parser:** 150+
- **Estimation temps complet:** 6-12 mois (full-time)

---

## 🚀 Ordre Prioritaire

**Mois 1-2:** Phases 1-3 (Personnage, Inventaire, Craft)
**Mois 3-4:** Phases 4-6 (Combat, Groupes, Donjons)
**Mois 5-6:** Phases 7-9 (Social, Monstres, Alternatifs)
**Mois 7-8:** Phases 10-12 (Maps, Économie, Quêtes)
**Mois 9-10:** Phases 13-15 (Life Skills, Admin, Performance)

---

*Ce plan est évolutif et sera mis à jour au fur et à mesure de l'analyse détaillée des binaires.*

---

## 🗜️ Exports Ghidra — les 16 binaires décompilés

Exports pseudo-C complets dans `re/ghidra/<nom>/` (`decompiled.c` +
`functions.txt` + `strings.txt` + `log.txt`), produits par
`ExportAll.java` (GhidraScript natif — le Ghidra système n'a **pas** de
PyGhidra). Total ~289M de pseudo-C, ~190k fonctions.

### Serveurs natifs (ELF avec symboles `lapis::*` — navigation directe)

| Dossier | Binaire | Fonctions | Usage |
|---|---|---|---|
| `zone/` | ZoneServer (14M) | 41 034 | combat, anims, spawn, dialogues, shops |
| `world/` | WorldServer | 17 958 | channel, parties, social |
| `mission/` | MissionServer | 15 562 | quêtes, `TC_Service` type→`shop <verb>` |
| `login/` | LoginServer | 6 350 | auth, comptes |
| `gateway/` | GatewayServer | 5 998 | routage, crypto |
| `ticket/` | TicketServer | 4 658 | tickets session |
| `httpa/` | HttpAgent | 8 741 | API web, cash shop |

### Client vanilla (PE strippé — fonctions `FUN_*`)

| Dossier | Binaire | Fonctions |
|---|---|---|
| `client_game/` | `game_.exe` (12.6M, **non packé** : `.text` 11.3M) | 69 773 |
| `client_launcher/` | `Launcher.exe` | 6 512 |
| `cl_alaudio/` | `ALAudio.dll` | 6 949 |
| `cl_steams/` | `SteamS.dll` (shim "Gaia Protector") | 485 |
| `cl_webbrowser/` | `WebBrowser.dll` | 83 |

`dbghelp.dll` (Microsoft) et `CrashRpt.dll` (open-source) exclus volontairement.

### Client Reforged (`reforged/EdenReforged/`)

| Dossier | Binaire | Fonctions | Usage |
|---|---|---|---|
| `rf_d3d9/` | `d3d9.dll` (21M) | 1 201 | proxy D3D9 — **injection UI CEGUI** |
| `rf_gamefix/` | `mods/_testing/gameFix.dll` | 461 | patches client |
| `rf_itemmall/` | `mods/_testing/ItemMall.dll` | 487 | boutique cash |
| `rf_neobank/` | `mods/_testing/NeoInventoryBank.dll` | 763 | UI banque/sacs |
| `rf_game/` | `game_.exe` Reforged | 70 271 | diff-able vs `client_game` (`.text` identique) |

Le "Reforged" ne vit pas dans l'exe (quasi identique à vanilla) mais
dans `d3d9.dll` (proxy + hooks CEGUI) et `themes/` (assets CEGUI en
clair : PNG + XML, extractibles sans décompilation). Porter la
nouvelle UI = comprendre l'injection `d3d9.dll` + copier thèmes/assets.

### Découvertes issues des exports (session courante)

- **285 `ClientDisplayDialogView`** : `[i32 ctx][u16 n][n×{i32 dispId,
  u16 len, texte, s16 flag}][u8 type][i32 npcUid]` — `dispId=-1` +
  texte brut = option libre ; `dispId=T_Dialogue` résolu client.
  `dispId=0` → "NULL????" (corrigé — plus d'entrée bidon).
- **56 `ListDialogue` → zone** : `ctx ∈ {-1,0}` + npcUid≠0 →
  `CNE_ZMis_ZoneServerListDialogue::Execute` fait `ResetOperation` et
  **n'émet PAS de 285** (pas de fenêtre).
- **Table `TC_Service`** (mission/decompiled.c) : type → verbe shop —
  1/2=`eq`, 3/4/23/26/29=`item`, 6=`bank`→**357**, 8=`transport`→393,
  18=`auction`→**430**, 24=`prescription_item`, 58=`pay_system`→**602**
  `[i32 npc][u16 shop]`. ATTENTION 6↔18 inversés vs intuition
  (vérifié sur noms Big5 des NPC S001).
- **FuncId `#fn#arg#`** → `InsertNPCInitServiceDB` crée le scénario
  `NPC[x] Init Service` dont la transition exécute `Service fn arg` —
  ouverture directe du service au talk, sans menu.
- **280 `ClientUpdateAttr`** : attr `0x02`=gold (preuve
  `GainGold`→attr 2), `0x2c`=classe active, `0x11/0x12`=HP/MP self.
- **302 `ClientFlagModify`** : `[u32 uid][u32 flags][u8 state]` —
  émis par `CStateController::NodifyFlagChange`, seulement quand un
  FSM d'état est retiré (`Do` n'émet rien sinon → pas de 302 en
  mêlée pure, le swing vient du 272).
- **326 `ClientMovePrediction`** : `[i32][u16 x×16][u16 z×16][u8]
  [u16 speed|bit0=hasDst][u16 dx][u16 dz]` — bit0 oblige les 4
  derniers octets (corrigé : facing×16384 y corrompait le stream).
- **272 `ClientMeleeAttack`** : `[u32 atk][u32 tgt][i32 dmg]
  [i32 type][i32 hpLeft|-1]` — 272 immédiat à chaque NCID=5.
- **NCID complets** : table extraite de `InitPCCommandTable`
  (NCID = index d'enregistrement) — cf. `docs/WIRE-EVIDENCE.md`.


---

## 🎯 Plan d'action live — backlog gameplay (pointé par test client)

Voir `docs/ACTION-PLAN.md` pour le détail. Blocs :

- **A. État perso/classes** : classe affichée (310@23 insuffisant),
  portrait post-class-change, persistance barre skills, commandes GM.
- **B. Quêtes** : 348 états réels (mission, pas replay), dialogues
  NPCTalk dynamiques (Bled), indicateur d'objectif bleu, Portal Stone,
  spawns conditionnés quêtes/events.
- **C. Combat/déplacement** : implémentation historique présente, mais
  validation live invalidée par les derniers rapports test3 (mob à 0 PV
  encore agressif, freeze, DOT et teinte). Ne pas considérer ce bloc comme
  terminé avant reproduction sans régression sur référence et Reforged;
  voir le plan d'audit du 2026-10-21.
- **D. Économie/UI** : shop achat/vente ✅ (274+308+280 gold), slots
  inventaire libres, chat canaux, amélioration skills pour l'or,
  localisation client.

### Nouvelles preuves natives décodées (session)
- 285 entrée quête : `displayId=-3` + `E=<mid en texte>` (capture 17:23:20).
- Quest verbs : prête→`mission-reward` (388 op2), finish=op4 post-claim.
- 391 : `[u8 keyType][i32 key][s16 state][s16 progress]` — keyType0=mid :
  states 3=active, 4=objectifs OK, 1=finie.
- 348 ClientMissionStates : `[s16 n]n×(u16,u16)` ×3 maps + map u32/u16 +
  vec<u8> + vec<s16> + vec<i32> (Serialize @0x82029d0).
- 489 : entrée `[u8 id][u8 lv][u8 style][i32 exp][i32 point][u8 aa][7×u16 lic]`.
- `#:Service` : refId optionnel (banquier 50347 `#:Service 5218 6  #`).

### Schémas natifs décodés (session — tables de jeu réelles)

- **FMS `final_mission_state1/2`** (double-buffer) : u32 `state` =
  **10 missions × 3 bits**, `index=(mid-1)/10`, `k=(mid-1)%10`,
  `FMS=(state>>k*3)&7`. Machine d'états : 0→3 (accept), 3→4
  (objectifs OK→récompense), 4→1 (finie), 5=checked (→vec s16 du 348).
  index 101→missions 1011..1020. test1 : 1013=3 (sa quête active,
  = mission_trace01).
- **`mission_trace01..10`** : 10 slots i32 de suivi HUD (mid, 0=vide).
- **`personal_mission_states`** : (index=mid, state=`status|progress<<8`)
  — la map3 du 348 (clés ≤0xFFFF) + clés d'instance 0x3c52xxxx (map4).
  ⚠ ce n'est PAS la liste des quêtes actives — le flood de quêtes haut
  level venait de là (corrigé : map1 = FMS 3/4 uniquement).
- **`inventory1/2` + vue `inventory`** : items du sac (test1 : 10 rows,
  loc=slot, container_index=sac, strengthen/bind/create_time présents).
  `player_items` (custom) vide → remplacé partout.
- **`equipment1/2` + vue `equipment`** : items portés (8 slots du 283
  ModelModify mask 0x5e00 — portrait conservé).
- **`storage1/2` + vue `storage`** : banque perso (même layout
  loc/container_index) — BankRepository migré.
- **`player_spells`** (30 rows test1) : `spellmaster_id` s16 signé +
  `spell_order` — queue du 311 `[u16 n][n×(s16,u8)]` régénérée live.

### Paquets reconstruits depuis le natif (session)

- **312 ClientEnterNodeInventory** : `[u8 bags][u16 cap=32][u8 flag]
  [s16 bitmapLen][bitmap MSB-first][items][u8 n=2][2×(u8 key,u16 n,
  n×u16)]` — bitmap MSB prouvé par `ffc00000` = slots 0..9.
- **Item record** : `[u16 id][s16 dur][s16 maxdur][u16 slen][str]
  [u8 flags][u24 mask][cond sparse][34B tail]` — bit8=create_time i32,
  bit19/20=enchant/refine u16. Validé byte-pour-byte (10 items parsés).
- **283 ModelModify** : les 8 slots portent les `item_id` de la vue
  `equipment` (capture : 827 au slot7) — plus de zéros → portrait OK.
- **MoveSelf (NCID 1)** : wire = `[i32 n][n×(f32 x,y,z,dx,dz)][s16]`
  — 26B pour n=1. Le parseur u16-n droppait tout → position serveur
  figée → lockout combat/déplacement corrigé.
- **CommandFail 410** émis sur cible invalide (cast 9 sur mob mort,
  attaque 5 sur handle effacé) — sinon le client reste bloqué en
  attente de réponse (cause principale du lockout post-combat).
- **Cast des sorts** : temps de cast = S_Spell **col 26** en
  décisecondes (`CSpellData+0x64`→`SSpellData+0x38`, `OnCast` ouvre un
  `CEventSpellCasting` si ≠0, plancher 0.4 s) — col 35 ≠ cast.
  Séquence : 279 self (i32 = castTime, 0 = instantané) → 278 broadcast
  → délai → 288 blast → 281 hit. Kill mêlée : 272 type=1 kfId (swing)
  → +300 ms → 272 type=5 −1 (twin fatal) → 305.
- **Exp** : `CCharacter::GainExp` → **600 ClientUpdateAttrNewExp**
  `[i32 char][i64 exp]` ; level-up → **284 ClientLevelUp**
  `[u32 char][u8][s16 lvl][i64 maxHp][i32 maxMp]` + attrs HP/MP.
  Exp mob = S_Monster **col 18** (col 14 = grade).
- **Regen** : tick natif hors combat → RecoverHP+RecoverMP ; flag
  combat → `RecoverMP(0.25)` seul ; assis → taux ×4 (1.0 vs 0.25).
- **HP/MP max** : `CCharFormula::MaxHP/MaxMP` = `S_Level.MaxHp +
  CON_eff×(100/S_Parameter:ConToHpMax=20)` (warrior lv1 ≈ 311),
  `S_Level.MaxMp + WIS_eff×(100/WisToMpMax=15)` ; stats effectives =
  base S_Level × %classe (S_Class cols 13-21).
- **Class skills** : `LoadAllCommonSpell` indexe chaque spellmaster
  par mask S_Spell **col 23** u64 bit(classId-1) ; `GetClassSpell`
  itère le vecteur S_Class cols 30-54. Octroi = (vecteur ∪ mask
  mono-classe) filtré `RequiredLevel` col 22 ≤ classLevel, dédup par
  code (chaînes J = paliers certificat). Réponse NCID 25 inventaire =
  312 + 280 attr `0x3f0=1`.

---

## Lots implémentés — session 2026-10-20 (commits)

| Commit | Lot | Preuves natives |
|---|---|---|
| `2013d4f` | **player_works** : ordres de fabrication/collection (NCID 170/171/172 → 496/497/498/500), 11 CWork persistés (`player_works` + `finish_at`), boosters catalyseurs, consommation immédiate/différée | `CWork::SaveWork/ToWork/ToCollect/ToCombine/EndCollect/EndCombine/WorkCheckData` |
| `b54ced8` | **Station combine** (service 10, `CCombineOp` op 0x27) : recettes S_FamilyCombine cat.6, cast 4 s via NCID 170, exclusivité des ops | `CCombineOp::CheckCombineItem/DoCombineItem/AddItemFromCombine` |
| `52db376` | **Item Mall natif** : pipeline zone↔world (ZW24 livraison / ZW12 résultat), catalogue 62/79 (13 vecteurs parallèles + version), claim 15, use 83, points `fnaccount.mid/pvalues/bonus` | `UpdatePlayerItemMallList`, `ItemMallReqBuyItem(Reault)`, `ItemMallReceieve*` |
| `ce1938f` | **Handlers famille** : `UpdateFamily` (ClientChangeFamily + révision +0x4de), `FamilyStorageQueryLog` (log natif 0/1/2), `FixEquipByFamily` (rate-limit 1 s + trésorerie), `FamilyTalkToNpc` | `CItemFactory::FixEquipByFamily`, dumps `family_log` |
| `5f8a7b9` | **Prix économie natifs** : achat = `S_Item` col31 (sys price) × remise, revente = `floor(sys × param346=0.05 × dur/maxDur)` équipements seuls, identif. = `max(1, sys×0.05)`, rejet items spéciaux 40001-53000 | `GetSysPrice/GetRepairPrice`, `_DAT_087cc628`=0.05 |
| `fb5aa8a` | **Buyback** : deque session cap 10, wire 512 `[u8 mode][u16 n][39 champs][s16 -1]`, rachat via `BuyItems` count≠0 + index, snapshot item complet + `RestoreItemAsync` | `CItemFactory::BuyBackItems`, `ClientBuyBackList::Serialize` |
| `fd2bb54` | **Identification** (op 65) : coût `sys×0.05`, flag `identify` colonne DB + bit3 (`0x08`) du flags-byte du record item, tests byte-level | `CItemFactory::IdentifyItem`, `clone+0x24` |
| `b31b330` | **Drop d'item** : `EradicateItem` (pas de ground-drop natif), routage type 0/5→inventaire, 1→équipement (loc≤21 + refresh 282/283), 4→stockage ; field3 = index conteneur | `CPlayerFactory::DropItem` |
| `ca02305` | **TeamOperation** : op 3 change loot rule (1-3, chef seul), op 2 change roll rule, op 1 conversion raid — notifs 381 `ClientTeamInfo` type 9/10 + broadcast membres | `CNE_CZ_ZoneServerTeamOperation::Execute`, `CForceTeam::ChageLootRule/ChageRollRule` |
| `e4bc865` | **TradeRequest** : invite remplie, refus à > 10 de distance (`ClientTradeCancel[partner][s16 1]` + texte 0x333), rate-limit natif | `CNE_CZ_ZoneServerTradeRequest::Execute` |
| `f733c81` | **Expansion de stockage** : prix S_Parameter 409/410 (1500/3500), cap `expand_storage_cnt` < 2 persisté, textes 0x5aad/0x5aae/0x5ab4, résultat 373 | `CPlayerFactory::ExpandStorage`, `CStorageExpand::CheckExecute` |
| `ab7c076` | **Cérémonies de mariage** : relais `WeddingInfoQuery 1` → `WeddingService` mission (≤5, abonnés, statuts 0→3 : 0s/600s/7200s/600s), ZMis 193 → 599 `leftTime`, `ReserverChuch` (carte 0x667f) et `CloseWedding` via DBA + TextOutputIndex 66 | `TC_WeddingInfoQuery/ReserverChurch/CloseWedding`, `CWeddingFactory`/`CEventWedding` |
| — | **Étal joueur (vendor/stall)** : NCIDs 71/72/74-78/80 natifs, validations `CheckBeginStallForPlayer` (node S001, régions 600-609 scène, rate-limit 1,5 s, équipe/files/duel) + `CheckBeginStallForStallGoods` (collision, ≤13 gratuits, prix ≤999999999), record 98o `ClientVendorUpdateView`, achat atomique sous verrou avec stale-checks et transfert serial-préservé, fermeture avec reset viewers (376+0xf07) ; 78/80 = no-ops natifs | `CPlayerFactory::VendorSellItem/VendorBuy/VendorClose`, `ClientVendorUpdateView::Serialize` |
| `09a1f30` | **Formules de combat natives** (`CCharFormula`) : `CombatantSnapshot` + `NativeCombatFormulas` — Melee/MagicDamage (str/int×para305/307 + Σequip + arme, dual ×0.3), dégâts phys joueur (offset ±10, def×3/adjust cap 70, resist ±70) / mob (Δ×5 cap 500), skills phys/mag (power×refCoef), ratings hit≥10%/dodge/crit plancher 3%, crit appliqué −pdef adverse, variance ±var%, dispatch spell flags+0x94 (magic→phys→restore, quirk type4/elem1), rolls miss→272 type8 / crit→type2 ; 6 sites hardcodés remplacés, snapshot caché, 296 = getters natifs ; 23 tests déterministes | `MeleeDamage`/`PhysicalDamage`/`HitMagicSkill`/`HitPhysicalSkill`/`HitRestoreSkill`, `SetItemAttrData`, `SSpellData::Init` |

État tests : 359/368 — 9 échecs baseline pré-existants (dispatcher,
scene/item parsers, loop messages, login serialization).
