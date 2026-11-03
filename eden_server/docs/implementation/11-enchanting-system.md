# 11 - Système d'Enchantement

> **Status:** 🟡 En cours — données chargées, moteur de procs livré  
> **Priorité:** HAUTE  
> **Complexité:** Élevée

---

## 1. Description

Deux couches :
1. **Enchantement d'équipement** (+1 à +20) — `CEnchantMgr`, `CEC_Enchant*`
2. **Procs/enchants conditionnels** — `S_Enchant.ini` (19 829 règles) :
   "si condition X alors appliquer spell Y avec chance Z".

## 2. Symboles lapis::

- `CEnchantMgr` — gestionnaire central
- `CEC_Enchant*` — 250+ fonctions d'enchant
- `CCharacter::RecoverAllEnchants`, `RemoveEnchant`
- `CEC_OnAttackBuff*`, `CEC_OnBeAttack*` — triggers de proc

## 3. Données : S_Enchant.ini (`|V.3|61|`, 19 829 entrées)

| Col | Contenu |
|-----|---------|
| 0 | Enchant ID (10001...) |
| 1 | Code groupe (E0302, E0132, J515) |
| 3 | Référence spell (s14131) |
| 4 | Niveau |
| 5 | Nom (Big5) |
| 7 | Flag actif |
| 8 | **Type de trigger** (8208 = condition générique) |
| 9-12 | Paramètres du trigger (spell ids, seuils, couches) |
| 16-20 | Bloc secondaire (spell résultat, valeurs) |
| 23-24 | Spell ids additionnels |
| 37 | Chance de proc (%) |
| 38-39 | Spell appliqué + sa chance |
| 47 | Flag |
| 56 | Description longue (Big5) |

## 4. Implémentation C#

- `Eden.Core/Data/Parsers/EnchantParser.cs` — `EnchantDef` (champs typés + Raw)
- `Eden.Core/Game/Services/EnchantService.cs`
  - `Evaluate(enchantId, TriggerEvent, stacks)` → liste de `ProcResult`
  - Événements : `OnAttack`, `OnBeAttacked`, `OnCritical`, `OnKill`, `OnCast`
  - Rolls de chance : `AppliedChance` pour le spell principal,
    `ProcChance` pour les spells secondaires

## 5. Tests

`EnchantParserTests` : chargement 19 829 entrées, structure de l'enchant
10001 (E0302, trigger 8208), `EnchantService.Evaluate` applique le spell 637.

## 6. TODO / À reverser

- Sémantique exacte des types de trigger (8208 et autres valeurs) —
  décompiler `CEnchantMgr::ReadData` + `CEC_Enchant*::Init`
- Niveaux +1..+20 : formule de coût/risque (`Enchant levels,风险评估`)
- Enchant raciaux (AA enchants), protection d'enchant
