# Phase 1.1 - Système de Classes - Status Détaillé

**Date:** 15 Octobre 2026  
**Completion:** 40%  
**Prochaine étape:** Décompilation Ghidra des fonctions clés

---

## ✅ Terminé

### Infrastructure (100%)
- [x] PostgreSQL container opérationnel
- [x] Bases de données créées (fndb1, fnaccount, fnmember)
- [x] Migrations SQL appliquées (corrigées)
- [x] Script automatique `check_and_migrate_db.py`

### Entités C# (100%)
- [x] `ClassEntity.cs` - Structure correcte (1-20 base, 21-60 awakened)
- [x] `ClassProgressEntity.cs` - Avec méthode `GetBaseClassId()`
- [x] `ClassStatsEntity.cs` - Pour stats par niveau

### Repository (80%)
- [x] `IClassRepository.cs` - Interface
- [x] `ClassRepository.cs` - Implémentation Dapper
  - [x] `GetClassesByCharacterIdAsync()`
  - [x] `GetClassProgressAsync()`
  - [x] `AddClassAsync()`
  - [x] `ChangeClassAsync()`
  - [x] `AddClassExpAsync()`
  - [x] `AddClassPointsAsync()`

### Service (60%)
- [x] `ClassChangeService.cs` - Logique de base
  - [x] Validation des prérequis basiques
  - [ ] Validation équipement
  - [ ] Switch équipement automatique
  - [ ] Application buffs raciaux
  - [ ] Calcul stats après changement

### Documentation (100%)
- [x] Structure RÉELLE des classes documentée
- [x] CORRECTION-CLASSES.md créé
- [x] 002-awakened-classes-data.sql corrigé
- [x] Mapping awakened → base complet

### Extraction Binaires (50%)
- [x] Symboles ZoneServer extraits (5.3 MB)
- [x] Fonctions clés identifiées:
  ```
  0x081048c0: lapis::CPlayerFactory::ChangeClass(CCharacter*, short)
  0x080fecb0: lapis::CPlayerFactory::RemoveClass(CCharacter*, short)
  0x08409b80: lapis::CCharacter::ChangeClassAA(unsigned char)
  0x083d2500: lapis::TC_AddClass(...)
  ```
- [ ] Décompilation Ghidra
- [ ] Documentation de la logique originale

---

## 🚧 En Cours

### A. Décompilation Ghidra (MAINTENANT)

**Adresse:** `0x081048c0`  
**Fonction:** `lapis::CPlayerFactory::ChangeClass(lapis::CCharacter*, short)`

**À extraire:**
1. Validations effectuées (niveau, classe parente, quête)
2. Ordre des opérations
3. Gestion de l'équipement
4. Messages réseau envoyés
5. Calculs de stats
6. Application buffs

**Outils:**
- Ghidra 11.2.1
- Projet: `ghidra_projects/EdenZone.gpr`
- Binary: `server_us/ZoneServer/ZoneServer`

### B. Parser T_Class.ini

**Objectif:** Charger les 60 classes (1-60) depuis T_Class.ini

**Structure du fichier:**
```
6|                              # Nombre de colonnes
1|2|5|6|7|8|                    # IDs colonnes
ID|Name|Description|Trait|DetailedDesc|Requirements|
```

**Classes à parser:**
- 1-20: Base (Warrior, Magician, etc.)
- 21-60: Awakened (Berserker, Shielder, Arch-Elementalist, etc.)

**Fichier à créer:** `Eden.Core/Data/Parsers/ClassDataParser.cs`

### C. Décoder S_Class.ini

**Problème:** S_Class.ini est **encodé** (pas UTF-8)

**Plan:**
1. Chercher fonction de décodage dans ZoneServer
2. Identifier l'algorithme d'encodage
3. Implémenter décodeur C#
4. Extraire stats par niveau (HP, MP, STR, DEX, CON, INT, PSY, AGI)

**Symboles à chercher:**
```bash
grep -i "S_Class\|LoadClass\|ClassData" docs/generated/ZoneServer-symbols.txt
```

---

## 📋 À Faire - Sprint Actuel

### Sprint 1: Extraction et Parsing (2-3 jours)

**Jour 1:**
- [ ] Décompiler `CPlayerFactory::ChangeClass` avec Ghidra
- [ ] Documenter la logique complète
- [ ] Identifier les validations

**Jour 2:**
- [ ] Implémenter `ClassDataParser.cs`
- [ ] Charger toutes les classes depuis T_Class.ini
- [ ] Parser les requirements (regex pour extraire level/classe requis)
- [ ] Tests unitaires pour le parser

**Jour 3:**
- [ ] Identifier encodage S_Class.ini
- [ ] Extraire fonction de décodage du binaire
- [ ] Implémenter décodeur C#
- [ ] Parser stats par niveau

### Sprint 2: Implémentation Complète (3-4 jours)

**Validation:**
- [ ] Implémenter validation complète dans `ClassChangeService`
- [ ] Vérifier équipement compatible
- [ ] Vérifier quêtes complétées (système de quêtes à implémenter)

**Switch Équipement:**
- [ ] Analyser `CPlayerFactory::SwitchClassEquip` (Ghidra)
- [ ] Implémenter switch automatique
- [ ] Sauvegarder équipement de l'ancienne classe

**Buffs Raciaux:**
- [ ] Identifier buffs dans binaire
- [ ] Appliquer/retirer buffs au changement
- [ ] Gérer buffs passifs

**Handlers Réseau:**
- [ ] Implémenter `ClassChangeHandler`
- [ ] Parser packet `CNE_CZ_ZoneServerChangeClass`
- [ ] Envoyer réponse `CNC_CZ_ClientChangeClass...`
- [ ] Broadcast changement aux joueurs proches

---

## 🔍 Symboles Clés Extraits

### Changement de Classe
```
0x081048c0: lapis::CPlayerFactory::ChangeClass(lapis::CCharacter*, short)
0x08409b80: lapis::CCharacter::ChangeClassAA(unsigned char)
```

### Ajout/Suppression
```
0x083d2500: lapis::TC_AddClass(lapis::IControlObj*, std::list<unsigned char*, std::allocator<unsigned char*> >&, char*)
0x080fecb0: lapis::CPlayerFactory::RemoveClass(lapis::CCharacter*, short)
```

### Messages Réseau
```
0x08240620: lapis::CNE_CZ_ZoneServerChangeClass::GetNCID()
0x08240040: lapis::CNE_CZ_ZoneServerChangeClass::Execute()
0x08277360: lapis::CNE_CZ_ZoneServerQueryClassList::GetNCID()
```

### Équipement
```
0x08104540: lapis::CPlayerFactory::EquipAction(unsigned char, int, int, lapis::CCharacter*)
```

### Effects
```
0x087b4afe: lapis::CEffectFactory::ChangeClassID
0x087b4b16: lapis::CEffectFactory::FieldChangeClass
```

---

## 📊 Métriques

**Code C#:**
- Fichiers créés: 7
- Classes: 3 entités + 1 repository + 1 service
- Lignes: ~500

**Documentation:**
- Fichiers: 5
- Corrections: CORRECTION-CLASSES.md
- Plan extraction: NEXT-STEPS-EXTRACTION.md

**Binaires:**
- Symboles extraits: 5.3 MB
- Messages CNE_*: 409
- Fonctions identifiées: 8+

**Base de Données:**
- Tables: 4 (player_classes, class_progression, class_spells, class_talents)
- Migrations: 2 (001, 002)

---

## 🎯 Objectif Phase 1.1

**Système de classes COMPLET fonctionnel:**
- ✅ Chargement des classes depuis fichiers .ini
- ✅ Changement de classe avec validation
- ✅ Classes awakened (21-60) supportées
- ✅ Switch équipement automatique
- ✅ Application buffs raciaux
- ✅ Handlers réseau fonctionnels
- ✅ Tests unitaires passants

**Deadline:** 5-7 jours (à partir de maintenant)

---

## 🔗 Références

- [MASTER-IMPLEMENTATION-PLAN.md](MASTER-IMPLEMENTATION-PLAN.md)
- [NEXT-STEPS-EXTRACTION.md](NEXT-STEPS-EXTRACTION.md)
- [CORRECTION-CLASSES.md](../CORRECTION-CLASSES.md)
- [ZoneServer Symbols](generated/ZoneServer-symbols.txt)
