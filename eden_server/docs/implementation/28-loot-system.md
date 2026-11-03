# 28 - Système de Drops et Loot

> **Status:** 🟡 En cours — tables chargées  
> **Priorité:** HAUTE  
> **Complexité:** Moyenne

---

## 1. Description

Loot de monstres : tables de drop par classe de mob + tables détaillées.

## 2. Symboles lapis::

- `CTreasure`, `CItemFactory`
- `CMonster::GiveClassItemDrop`

## 3. Données

### S_ClassDropItem.ini (`|V.3|85|`, 290 tables)
Triplets répétés `chance|itemId|nom|qty` par colonnes :
`59013|100|33747|阿茲薩特的厄運烙印|1|...|100|33819|...|1|`

### S_DropItem.ini (`|V.3|176|`, 5 049 entrées)
Tables détaillées (nom de monstre, drops par index).

## 4. Implémentation C#

- `GameDataParsers.ParseClassDropItem()` — `ClassDropTable` →
  `ClassDropEntry { Chance, ItemId, ItemName, Quantity }`
  (scan par heuristique chance∈]0,100000] + itemId + nom non numérique)

## 5. Tests

`GameDataParsersTests.ParseClassDropItem_LoadsTables` : 290 tables, premier
drop = item 33747 @ chance 100.

## 6. TODO / À reverser

- `S_DropItem.ini` : parser dédié (176 cols)
- Distribution en party (FFA / round-robin) — `CTreasure`
- Drops spécifiques par classe joueur — `GiveClassItemDrop`
