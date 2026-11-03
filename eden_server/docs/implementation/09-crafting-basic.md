# 09 - Système de Craft de Base

> **Status:** 🟡 En cours — données chargées, service v1 livré  
> **Priorité:** HAUTE  
> **Complexité:** Moyenne

---

## 1. Description

Le système de craft couvre :
- **ElfCombine** : craft réalisé par les familiers (elfes) — `S_ElfCombine.ini`
- **FamilyCombine** : craft de guilde/famille — `S_FamilyCombine.ini`
- **ItemCombo** : combos/sets d'items — `S_ItemCombo.ini`

## 2. Symboles lapis::

- `CEC_CombineItem` — exécution d'un craft
- `CCubeCrafting` — interface de craft (la "cube")
- `CCombineOp`, `CCompoundOp`, `CCubeOp` — opérations internes

## 3. Données décodées (MyCrypt)

### S_ElfCombine.ini — `V.5,23,` (957 recettes, séparateur `,`)
| Col | Contenu |
|-----|---------|
| 0 | Recipe ID |
| 1 | Type (vide ou 8) |
| 2 | Elfe/race requis |
| 6 | Coût en gold |
| 7 | Item matériau |
| 9/12 | Item résultat |
| 14 | Quantité résultat |
| 18 | Flags |
| 19-22 | Scripts de bonus `#itemId#itemId#weight#Effect Min Max##` |

### S_FamilyCombine.ini — `|V.1|44|` (3275 recettes, séparateur `|`)
| Col | Contenu |
|-----|---------|
| 0 | Recipe ID |
| 1 | Catégorie (7 = scroll) |
| 13 | Flags (512/1024/2048) |
| 18 | Item résultat |
| 19 | Nom résultat (Big5) |
| 20 | Quantité |

### S_ItemCombo.ini — `|V.4|42|`
- [0] ID, [1] code (G00003), [2] préfixe nom, [3] types d'items `#17#18#...#`,
  [5-6] plage de niveaux, [7] tier, [8] valeur, [11] rang.

## 4. Implémentation C#

- `Eden.Core/Data/Parsers/CraftRecipeParser.cs`
  - `ParseElfCombine` / `ParseFamilyCombine` / `ParseItemCombo`
  - `BonusScript` : parse `#id#id#weight#effet##`
- `Eden.Core/Game/Services/CraftService.cs`
  - `CraftElfAsync` : vérifie énergie elfe, consomme matériau,
    choisit le résultat (bonus pondéré par `weight`), ajoute l'item
  - `CraftFamilyAsync`, `GetCombosForItemType`
  - `LoadFromDataDir(decodedDir)` : chargement des 3 tables

## 5. Tests

`CraftParserTests` (5 tests) : chargement des recettes, bonus scripts,
combos, `S_Item.ini` en clair.

## 6. TODO / À reverser

- Formule exacte de chance de succès (`CCombineOp` à décompiler)
- Consommation de matériaux : attendre une API inventaire de recherche
  par itemId (le réseau enverra le slot exact)
- Scripts bonus : mapper les effets (`Friend Plus`, `SoulAbility Plus`,
  `Monkey Plus`, `Energy Minus`, `Success Minus`...) sur les stats d'elfe
