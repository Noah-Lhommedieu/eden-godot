# 39 - Système de Quêtes

> **Status:** 🟡 En cours — 2 831 quêtes + machine d'état livrées  
> **Priorité:** CRITIQUE  
> **Complexité:** Élevée

---

## 1. Description

2 831 quêtes scriptées dans `S_Mission.ini` avec un langage complet
de conditions/objectifs/récompenses (`#:Commande args:`).

## 2. Symboles lapis::

- `CMission`, `CMissionTemplate`, `CMissionDBO`
- `CNE_ZMis_MissionServer*` (100+ commandes MissionServer)
- `CExecutionerData` — exécution des scripts

## 3. Données : S_Mission.ini (`|V.5|28|`)

| Col | Contenu |
|-----|---------|
| 0 | Mission ID |
| 1 | Nom du script (script01) |
| 2 | Nom de quête (Big5) |
| 10 | Nom de la map |
| 13 | **Script de conditions** `#:If_CharLevel >= 80:...` |
| 16 | **Script d'objectifs** `#:MonsterKilled 59333 1:...` |
| 20 | **Script de récompenses** `#:RewardGold 7:RewardExp 38:...` |
| 23 | Catégorie (S002，主線 = quête principale) |
| 27 | Description |

### Langage de script (27 commandes recensées)

| Type | Commandes |
|------|-----------|
| Conditions | `If_CharLevel`(op+val), `If_FMS`, `If_Gender`, `CheckBuff`, `PlayerLevel` |
| Objectifs | `MonsterKilled`(mobId qty), `ItemCollect`, `MonsterTreasureGot`, `NPCTalk`, `AcceptInventoryItem`, `ItemGiven`, `ItemGot`, `AreaTravel`, `NPCItem`, `MC`, `DoTimes`, `WinTimes`, `AcceptCloseMission`, `AcceptTPA` |
| Récompenses | `RewardGold`, `RewardExp`, `RewardItem`(id qty), `RewardBuff`, `RewardReputation`, `RewardSpecialCoin`, `RewardChooseItem`, `RewardClassPoint`, `RewardFamilyExp` |

## 4. Implémentation C#

- `Eden.Core/Data/Parsers/MissionParser.cs`
  - `MissionDef` + `ScriptCommand`
  - `ParseScript` : `#:` préfixe, `:` sépare les commandes
- `Eden.Core/Game/Services/MissionService.cs`
  - `IPlayerContext` (level, gender, fms, buffs, items)
  - `CanAccept` → conditions ; `Accept` → `QuestState`
  - `NotifyEvent(kind, targetId, amount)` → compteurs d'objectifs
  - `IsComplete` → `Complete` → récompenses

## 5. Tests

`MissionParserTests` (4 tests) : chargement, parsing `#:RewardGold...`,
cycle accept→event→complete, refus par `If_CharLevel >= 80`.

## 6. TODO / À reverser

- Persistance (`character_quests` table à ajouter)
- Chaînes de quêtes (cols 19/21/25/26 = étapes suivantes)
- Daily/weekly quests, quêtes de scénario
- `MC`, `DoTimes`, `WinTimes` : sémantique exacte (PVP/multijoueur?)
- Interactions NPC : `S_Dialogue.ini` (12.2)
