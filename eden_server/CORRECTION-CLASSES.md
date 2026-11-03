# CORRECTION - Analyse Réelle des Classes Awakened

**Date:** 15 Octobre 2026  
**Erreur commise:** J'ai inventé des classes "Warrior (awakened)" etc. au lieu d'analyser les vraies données.

## ❌ Ce que j'ai FAUSSEMENT documenté

- Classes 101-120 = "Warrior awakened", "Magician awakened"...  
- **C'EST FAUX** - ce sont juste des duplications avec le même nom

## ✅ VRAIE Structure (depuis T_Class.ini réel)

### Classes 1-20: Classes de BASE
```
1: Warrior
2: Magician  
3: Hunter
4: Cleric
5: Thief
6: Bard
7: Knight
8: Engineer
9: Martial Artist
10: Illusionist
11: Blade Dancer
12: Shaman
13: Templar
14: Ranger
15: Warlock
16: Samurai
17: Sage
18: Dragon Knight
19: Luminary
20: Mimic
```

### Classes 21-60: VRAIES Classes Awakened (2 branches par classe)

**Warrior branches (21-22):**
- 21: **Berserker** (attaque pure)
- 22: **Shielder** (défense pure)

**Magician branches (23-24):**
- 23: **Arch-Elementalist** (éléments)
- 24: **Gravity Manipulator** (gravité)

**Hunter branches (25-26):**
- 25: **Trainer** (dresseur de bêtes)
- 26: **Devil Hunter** (chasseur de démons)

**Cleric branches (27-28):**
- 27: **Adjudicator** (juge)
- 28: **Equilibrian** (équilibre lumière/ombre)

**Thief branches (29-30):**
- 29: **Assassin** 
- 30: **Kage** (ninja ombre)

**Bard branches (31-32):**
- 31: **Battlefield Poet**
- 32: **Life Worshipper**

**Knight branches (33-34):**
- 33: **Executioner**
- 34: **Glacier Knight**

**Engineer branches (35-36):**
- 35: **Mecha Ares**
- 36: **Annihilator**

**Martial Artist branches (37-38):**
- 37: **Asura**
- 38: **Rakshasa**

**Illusionist branches (39-40):**
- 39: **Demon Tamer**
- 40: **Chronologist**

**Blade Dancer branches (41-42):**
- 41: **Blade Acrobat**
- 42: **Elegant Dancer**

**Shaman branches (43-44):**
- 43: **Druid**
- 44: **Archimage**

**Templar branches (45-46):**
- 45: **Blood Knight**
- 46: **Paladin**

**Ranger branches (47-48):**
- 47-48: (placeholders)

**Warlock branches (49-50):**
- 49: **Demon Tamer** (duplicate)
- 50: **Conjurer**

**Samurai branches (51-52):**
- 51: **Blade Master**
- 52: **Master Fencer**

**Sage branches (53-54):**
- 53-54: (placeholders)

**Dragon Knight branches (55-56):**
- 55-56: (placeholders)

**Luminary branches (57-58):**
- 57-58: (placeholders)

**Mimic branches (59-60):**
- 59: **Inquisitor**
- 60: **Reaver**

### IDs 101-120, 201-220, 301-320...
Ce sont des **DUPLICATIONS** avec les mêmes noms que 1-20, probablement pour différents tiers/rangs.

## 🎯 VRAIE Logique du Système

1. **Classe de base** (1-20) niveau jusqu'à ~80
2. **Quest de déblocage awakened** à level 80
3. **Choix entre 2 branches** (ex: Warrior → Berserker OU Shielder)
4. Chaque classe awakened a son propre **nom**, **skills**, **stats**

## 🔧 Ce qui doit être CORRIGÉ

- [ ] Migration SQL: supprimer la colonne `is_awakened` (inutile)
- [ ] Migration SQL: ajouter `base_class_id` pour tracker la classe parente (21-22 → 1, etc.)
- [ ] ClassEntity: fixer la logique `IsAwakened` pour détecter IDs 21-60
- [ ] ClassEntity: ajouter mapping correct base → awakened branches
- [ ] Documentation: corriger TOUS les documents avec les vrais noms
- [ ] Status document: corriger la liste des classes awakened

## 📝 Prochaines Étapes

1. Analyser les binaires pour extraire la logique de déblocage
2. Identifier les quêtes requises pour chaque awakened class
3. Extraire les stats de S_Class.ini pour les IDs 21-60
4. Documenter correctement les 2 branches par classe
5. Implémenter le système de choix de branche

## 🚨 Leçon Apprise

**NE JAMAIS inventer des données.** 
**TOUJOURS analyser les fichiers réels AVANT de coder.**

Les fichiers T_Class.ini et les binaires contiennent LA VÉRITÉ.
Pas besoin d'inventer "Warrior awakened" quand le vrai nom est **Berserker** ou **Shielder**.
