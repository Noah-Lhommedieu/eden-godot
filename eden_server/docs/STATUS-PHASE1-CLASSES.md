# Status Phase 1.1 - Système de Classes

**Date:** 15 Octobre 2026  
**Phase:** 1.1 - Système de Classes et Changement de Classe  
**Priorité:** MAX  
**Status:** 🟡 En cours d'implémentation

---

## ✅ Réalisations

### Infrastructure de Base de Données
- [x] Base de données PostgreSQL opérationnelle (fndb1, fnaccount, fnmember)
- [x] Migration `001-initial-schema.sql` appliquée
- [x] Table `player_classes` créée avec support des classes éveillées
- [x] Tables `class_progression`, `class_spells`, `class_talents` créées
- [x] Colonne `is_awakened` pour tracker les classes éveillées (IDs 101-120)
- [x] Colonne `lifelong_unlocked` pour le système de déblocage permanent
- [x] Script automatique `check_and_migrate_db.py` pour setup DB

### Entités C# Core
- [x] `ClassEntity.cs` - Représentation d'une classe (T_Class.ini)
- [x] `ClassProgressEntity.cs` - Progression joueur dans une classe
- [x] `ClassStatsEntity.cs` - Stats par niveau d'une classe (S_Class.ini)

### Repositories
- [x] `IClassRepository.cs` - Interface repository
- [x] `ClassRepository.cs` - Implémentation avec Dapper
  - [x] `GetClassesByCharacterIdAsync()` - Récupère toutes les classes d'un personnage
  - [x] `GetClassProgressAsync()` - Récupère la progression d'une classe spécifique
  - [x] `AddClassAsync()` - Ajoute une nouvelle classe débloquée
  - [x] `ChangeClassAsync()` - Change la classe active
  - [x] `AddClassExpAsync()` - Ajoute de l'XP à une classe
  - [x] `AddClassPointsAsync()` - Ajoute des points de classe

### Services
- [x] `ClassChangeService.cs` - Logique métier de changement de classe
  - [x] `ChangeClassAsync()` - Validation et changement de classe
  - [x] `UnlockClassAsync()` - Déblocage d'une nouvelle classe
  - [x] `GetExpForNextLevel()` - Calcul XP requise

### Documentation
- [x] `docs/implementation/01-class-system.md` - Documentation complète
- [x] `Database/002-awakened-classes-data.sql` - Documentation classes éveillées
- [x] Correspondance IDs classes éveillées (101-120 = awakened de 1-20)

### Fichiers de Données Identifiés
- [x] `client/data/db/T_Class.ini` - Données textuelles des classes (UTF-8)
- [x] `server_us/Data/db/S_Class.ini` - Stats encodées par niveau (binaire/encodé)
- [x] Classes 1-20: Classes de base
- [x] Classes 21-60: Classes avancées/spécialisées
- [x] Classes 101-120: Versions éveillées des classes 1-20
- [x] Classes 121-160: Classes avancées éveillées

---

## 🚧 En Cours

### Parsers de Données
- [ ] Parser pour T_Class.ini (UTF-8, lisible)
  - [ ] Lecture des 60+ classes
  - [ ] Extraction des requirements
  - [ ] Parsing des prérequis de niveau/classe
  - [ ] Identification des quêtes requises
- [ ] Décodeur pour S_Class.ini (encodé)
  - [ ] Identification du format d'encodage
  - [ ] Extraction des stats par niveau
  - [ ] HP, MP, STR, DEX, CON, INT, PSY, AGI par niveau
  - [ ] Support jusqu'au level cap (actuellement ~100-150)

### Système de Validation
- [ ] Vérification des prérequis de changement de classe
  - [ ] Niveau personnage minimum
  - [ ] Niveau classe parente requis
  - [ ] Quêtes complétées
  - [ ] Items requis (lifelong items)
- [ ] Validation d'équipement compatible
  - [ ] Armes utilisables par la classe
  - [ ] Armures compatibles
  - [ ] Switch automatique d'équipement

---

## 📋 À Faire (Priorité Haute)

### Phase 1.1.A - Parsing et Chargement des Données
1. **Implémenter ClassDataParser.cs**
   - Lire T_Class.ini
   - Parser tous les champs
   - Extraire les requirements complexes
   - Générer `List<ClassEntity>`

2. **Implémenter ClassStatsParser.cs**
   - Décoder S_Class.ini
   - Extraire stats par niveau pour chaque classe
   - Populate table `class_progression`

3. **Implémenter ClassSpellsParser.cs**
   - Identifier les sorts de chaque classe
   - Lier avec T_Spell.ini
   - Populate table `class_spells`

### Phase 1.1.B - Système de Changement de Classe
4. **Compléter ClassChangeService.cs**
   - [x] Validation basique
   - [ ] Validation complète avec quêtes
   - [ ] Validation équipement
   - [ ] Switch d'équipement automatique
   - [ ] Application des buffs raciaux passifs
   - [ ] Mise à jour des stats calculées

5. **Handler Réseau pour Changement de Classe**
   - [ ] `CNE_CZ_ZoneServerChangeClass` handler
   - [ ] `CNE_CZ_ZoneServerQueryClassList` handler
   - [ ] Envoi de la liste des classes au client
   - [ ] Confirmation du changement de classe
   - [ ] Broadcast aux joueurs proches

### Phase 1.1.C - Classes Éveillées (Awakened)
6. **Système de Déblocage des Classes Éveillées**
   - [ ] Quêtes de déblocage par classe
   - [ ] Validation niveau minimum (généralement 80+)
   - [ ] Validation classe de base niveau requis
   - [ ] Application des bonus de classe éveillée
   - [ ] Stats améliorées vs classe de base

7. **Migration des Stats Awakened**
   - [ ] Extraire stats classes 101-120 de S_Class.ini
   - [ ] Comparer avec classes 1-20
   - [ ] Documenter les différences de stats
   - [ ] Populate `class_progression` pour awakened

### Phase 1.1.D - Système de Class Points et Talents
8. **Class Points System**
   - [ ] Gain de class points par niveau
   - [ ] Formule de calcul des points
   - [ ] Stockage dans `player_classes.class_points`
   - [ ] Interface de dépense des points

9. **Class Talents / Certificates**
   - [ ] Table `class_talents` population
   - [ ] Déblocage des talents avec class points
   - [ ] Application des bonus de talents
   - [ ] Persistence dans DB

### Phase 1.1.E - Buffs Raciaux Passifs
10. **Racial Passive Buffs par Classe**
    - [ ] Identifier les buffs dans S_ClassRankBuff.ini
    - [ ] Lier buffs à classes
    - [ ] Application automatique au changement de classe
    - [ ] Gestion des stacks de buffs

---

## 🔍 Symboles à Analyser (Ghidra)

### Priorité Immédiate
- [ ] `CClassDBO::InitClassSpellSets` - Comment les spells sont associés aux classes
- [ ] `CCharacter::ChangeClassAA` - Logique complète du changement de classe avec AA
- [ ] `CLevelDBO::LoadClassLevelExp` - Formule d'XP par niveau de classe
- [ ] `CLevelDBO::LoadClassWakenLevelExp` - Formule d'XP pour classes éveillées
- [ ] `CPlayerFactory::SwitchClassEquip` - Switch automatique d'équipement

### Priorité Moyenne
- [ ] `CEC_ClassExpRate` - Buffs taux d'XP de classe
- [ ] `CCharacter::GainClassPoint` - Formule de gain de class points
- [ ] `CPlayerFactory::CheckWeaponBeforeChangeClass` - Validation arme

### Priorité Basse (après MVP)
- [ ] `CCharacter::AddPlayerColosseumClassPK` - Système PK Colosseum par classe
- [ ] `CColosseumFactory::CheckCanChangClass` - Changement en colosseum

---

## 📊 Métriques de Progression

**Completion Phase 1.1:** 35%

| Catégorie | Completion | Détails |
|-----------|-----------|---------|
| DB Schema | 100% | 4/4 tables créées |
| Entités C# | 100% | 3/3 classes core |
| Repositories | 80% | Méthodes de base OK, manque validation avancée |
| Services | 50% | Logique basique OK, manque validation complète |
| Parsers | 0% | À implémenter |
| Handlers Réseau | 0% | À implémenter |
| Tests | 0% | À écrire |

---

## 🎯 Objectif Suivant

**Sprint actuel: Phase 1.1.A - Parsing et Chargement des Données**

1. Implémenter `ClassDataParser.cs` pour lire T_Class.ini
2. Charger les 120+ classes dans `List<ClassEntity>`
3. Écrire tests unitaires pour le parser
4. Documenter le format exact de T_Class.ini

**Durée estimée:** 2-3 heures  
**Blockers:** Aucun

---

## 📝 Notes Techniques

### Format T_Class.ini
```
6|                              # Nombre de colonnes
1|2|5|6|7|8|                    # IDs des colonnes
ID|Name|Description|Trait|DetailedDesc|Requirements|
```

### IDs Classes
- **1-5:** Classes de départ (Beginning Jobs)
- **6-20:** Classes avancées basiques
- **21-60:** Classes avancées/spécialisées
- **101-120:** Awakened versions de 1-20
- **121-160:** Awakened versions de 21-60
- **201-220:** Réservé (éventuelles évolutions futures)

### Prérequis Typiques
```
- Character level 60
- Warrior level 45
- Martial Artist level 45
```
ou
```
* Finish the complete "Sage's Trial" Mission.
```

### Classes Éveillées Confirmées
- 101 = Warrior (awakened)
- 102 = Magician (awakened)
- 103 = Hunter (awakened)
- 104 = Cleric (awakened)
- 105 = Thief (awakened)
- 106 = Bard (awakened)
- 107 = Knight (awakened)
- 108 = Engineer (awakened)
- 109 = Martial Artist (awakened)
- 110 = Illusionist (awakened)
- 111 = Blade Dancer (awakened)
- 112 = Shaman (awakened)
- 113 = Templar (awakened)
- 114 = Ranger (awakened)
- 115 = Warlock (awakened)
- 116 = Samurai (awakened)
- 117 = Sage (awakened)
- 118 = Dragon Knight (awakened)
- 119 = Luminary (awakened)
- 120 = Mimic (awakened)

---

## 🔗 Liens

- [Master Implementation Plan](MASTER-IMPLEMENTATION-PLAN.md)
- [01-class-system.md](implementation/01-class-system.md)
- [Database Migration 001](../Database/001-initial-schema.sql)
- [Database Migration 002](../Database/002-awakened-classes-data.sql)
