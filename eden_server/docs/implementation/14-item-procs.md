# 14 - Proc d'Items et Enchants

> **Status:** 🟡 En cours — moteur livré, triggers à affiner  
> **Priorité:** HAUTE  
> **Complexité:** Moyenne

---

## 1. Description

"X% de chance quand on attaque / reçoit un coup d'obtenir le buff/malus Y".
Les règles viennent de `S_Enchant.ini` (même table que le système
d'enchantement — 19 829 entrées).

## 2. Symboles lapis::

- `CEC_OnAttackBuff*` — procs à l'attaque (150+ fonctions)
- `CEC_OnBeAttack*` — procs à la réception
- `CEC_Enchant*` — évaluation générique

## 3. Implémentation C#

`EnchantService.Evaluate(enchantId, TriggerEvent, stacks)` :
- `OnAttack` — le joueur frappe → roll `AppliedChance` → `AppliedSpellId`
- `OnBeAttacked` — le joueur reçoit un coup
- `OnCritical`, `OnKill`, `OnCast`
- Sorts secondaires : `SpellId1`/`SpellId2` (cols 23-24) rollés avec
  `ProcChance` (col 37)

Intégration : à appeler depuis le pipeline de combat quand
`CCharacter::OnAttack`/`OnBeAttacked` seront portés.

## 4. TODO

- Mapping des `TriggerType` (8208...) → conditions réelles (décompiler
  `CEC_Enchant*::CanExecute`)
- Limites de stacks (partiel : `SpellService.ActiveBuff.Stacks`)
- Procs on-cast/heal spécifiques
