# Prochaines Étapes - Extraction des Binaires

**Date:** 15 Octobre 2026  
**Status:** Corrections effectuées, prêt pour extraction

## ✅ Corrections Terminées

1. **Structure des classes corrigée**
   - Classes 1-20: Base
   - Classes 21-60: Awakened avec noms uniques (Berserker, Shielder, etc.)
   - Code C#, SQL, documentation mis à jour

2. **Symboles extraits**
   - `ZoneServer-symbols.txt` (5.3 MB, 409 messages CNE_*)
   - Prêt pour analyse

## 🎯 Plan d'Action - Phase 1.1 (Système de Classes)

### Étape 1: Extraction depuis les Binaires (MAINTENANT)

#### A. Analyser CPlayerFactory
```bash
grep "CPlayerFactory" docs/generated/ZoneServer-symbols.txt
```

Fonctions clés à décompiler avec Ghidra:
- `CPlayerFactory::AddClass` - Comment une classe est ajoutée
- `CPlayerFactory::ChangeClass` - Logique complète du changement
- `CPlayerFactory::RemoveClass` - Suppression d'une classe
- `CPlayerFactory::SwitchClassEquip` - Switch automatique d'équipement
- `CPlayerFactory::CheckWeaponBeforeChangeClass` - Validation arme

#### B. Analyser CCharacter (ZoneServer)
```bash
grep "CCharacter.*Class" docs/generated/ZoneServer-symbols.txt
```

Fonctions clés:
- `CCharacter::ChangeClassAA` - Changement avec Alternate Awakening
- `CCharacter::GainClassPoint` - Gain de points de classe
- `CCharacter::SetClassLevel` - Set niveau classe
- `CCharacter::GetClassExpRate` - Taux d'XP de classe

#### C. Analyser Messages Réseau
```bash
grep "CNE_CZ.*Class" docs/generated/ZoneServer-symbols.txt
```

Messages à documenter:
- `CNE_CZ_ZoneServerChangeClass` - Requête changement de classe
- `CNE_CZ_ZoneServerQueryClassList` - Query liste classes
- Format des packets (via Ghidra)

#### D. Analyser CClassDBO (Database Object)
```bash
grep "CClassDBO" docs/generated/ZoneServer-symbols.txt
```

Fonctions:
- `CClassDBO::GetAllClasses` - Chargement depuis T_Class.ini
- `CClassDBO::InitClassSpellSets` - Init spells par classe
- `CClassDBO::InitAAEffectSets` - Init effets AA

### Étape 2: Parser T_Class.ini (Code)

Créer `ClassDataParser.cs`:
```csharp
public static class ClassDataParser
{
    public static List<ClassEntity> Parse(string filePath)
    {
        // Lire T_Class.ini
        // Parser les 60 premières classes (1-60)
        // Extraire name, description, requirements
        // Retourner List<ClassEntity>
    }
}
```

### Étape 3: Décoder S_Class.ini (Stats)

S_Class.ini est **encodé**. Il faut:
1. Identifier l'encodage dans les binaires
2. Extraire la fonction de décodage de ZoneServer
3. Implémenter le décodeur en C#
4. Parser les stats par niveau

### Étape 4: Implémenter Handlers Réseau

Créer `ClassChangeHandler.cs`:
```csharp
public class ClassChangeHandler
{
    public async Task HandleChangeClass(int characterId, int targetClassId)
    {
        // 1. Valider prérequis
        // 2. Appeler ClassChangeService
        // 3. Envoyer réponse au client
        // 4. Broadcast aux joueurs proches
    }
}
```

### Étape 5: Tests Unitaires

Écrire tests pour:
- ClassDataParser
- ClassChangeService validation
- Mapping awakened → base

## 🔧 Outils Nécessaires

### Ghidra
- **Usage:** Décompiler les fonctions C++ identifiées
- **Projet:** `ghidra_projects/EdenZone.gpr` (à créer)
- **Binaire:** `server_us/ZoneServer/ZoneServer`

### Script Python d'Extraction
```python
# tools/extract_class_logic.py
# Extraire la logique de ChangeClass depuis Ghidra
```

### IDA Pro (Alternative)
Si Ghidra ne suffit pas pour certaines fonctions complexes

## 📋 Checklist Phase 1.1.A

- [ ] Extraire tous les symboles `CPlayerFactory::*Class*`
- [ ] Décompiler `CPlayerFactory::ChangeClass` avec Ghidra
- [ ] Documenter les étapes de validation dans le code original
- [ ] Extraire format packet `CNE_CZ_ZoneServerChangeClass`
- [ ] Implémenter `ClassDataParser.cs` pour T_Class.ini
- [ ] Identifier encodage de S_Class.ini
- [ ] Écrire décodeur S_Class.ini en C#
- [ ] Implémenter `ClassChangeHandler.cs`
- [ ] Écrire tests unitaires
- [ ] Valider avec client (test en local)

## 📊 Métriques

**Symboles ZoneServer:** 5.3 MB extraits  
**Messages réseau:** 409 CNE_* identifiés  
**Temps estimé Phase 1.1.A:** 2-3 jours

## 🚀 Après Phase 1.1.A

Passer à Phase 1.1.B: Validation complète et switch d'équipement
Puis Phase 1.1.C: Classes awakened (déblocage, quêtes)
