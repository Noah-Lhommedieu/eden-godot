# Lot — Prix économie native (achat/vente/identification/réparation)

Source : `re/ghidra/zone/decompiled.c` —
`CCloneItem::GetSysPrice` @0x84e5440, `GetRepairPrice` @0x84e54b0,
`CItemFactory::GetItemIdentifyPrice` @0x808c3f0,
`CNE_CZ_ZoneServerSellItems::Execute` @0x8287xxx,
`CItemFactory::BuyItems` @0x80aca40, `CItemDBO::SetItemAttrData`
@0x8358200, `CItemQuery::Deserialize` @0x8602xxx.

## Preuves natives

- **`sys_price` = S_Item col 31** (float → `GameData::CItemData+0x7c`,
  recopié lapis `CItem+0x10` / `CItemAttr+0xc0`). Le comptage des
  tokens du Deserialize verrouille col31 (3 lectures strtol manuelles
  à +0xf8/+0xfc/+0x74 avant +0x7c ; +0x114=col76 repair confirmé).
  Valeurs : arme lv93 → 35262, potion 32631 → 23, armure shopée
  32378 → 194400. Notre parser l'expose déjà (champ `SellPrice`,
  mal nommé).
- `col17` (notre `BuyPrice`) = monnaie alternative (~1000-6000 sur
  les items de boutique, VIDE sur le reste) — **pas** le prix or.
- **Achat** (`BuyItems` @0x80aca40) : `arrondi(SysPrice × remise)`
  — remise prestige `AllFormulaParas+0x114+level*4`, sinon 1.0.
  Epsilon `DAT_087cc688` = 0.5f (arrondi au plus proche).
- **Revente** (`SellItems::Execute`) : `SysPrice × param346` par
  unité × `curDur/maxDur` (items non-combo) ; rejet si
  `SysPrice×0.05 < 1.0` (`_DAT_087b2754`=1.0f, erreur 0x6e0).
  S_Parameter **346 = 0.05** (« 物品回收價格參數 »).
- **Identification** (`GetItemIdentifyPrice` @0x808c3f0) :
  `max(1, floor(SysPrice × 0.05))` — constante 0.05f codée en dur
  (`_DAT_087cc628`), PAS le param 346. Éligible : `def+0x3c & 1 == 0`
  et `GetType() < 2` (armes/armures uniquement — pas d'accessoires).
- **Réparation un item** (`GetItemRepairPrice` @0x808c480) : déjà
  portée (`ItemRepairCost`, repairPrice col76 × param347=0.2 /
  maxDur × missingDur, min 1) — mais `RepairOneItem` utilise encore
  `SellPrice × missing%` (surcharge massive).

## Bugs corrigés

| Emplacement | Avant | Après |
|---|---|---|
| `ShopService.BuyItemAsync` | col17 (souvent 0 → gratuit) | col31 × remise 1.0 |
| `ShopService.SellItemAsync` | col31 plein (~20× trop) | floor(col31×param346×dur/maxDur), seuil 1.0 |
| `AskIdentifyGold` | col31/10 | max(1, col31×0.05) + éligibilité |
| `IdentifyItem` | col31/10 | idem |
| `RepairOneItem` | col31×missing% | formule native ItemRepairCost |

## Implémentation

1. `ParameterParser` : `SellBackFactor = 346`.
2. `ItemParser.ItemDef` : commentaires corrigés (col31 = sys price,
   col17 = monnaie alternative).
3. `ShopService` : ctor += `ParameterParser.ParameterSet` ;
   `BuyPriceOf` → col31 ; `SellPriceOf(item)` → formule native avec
   durabilité (besoin de l'entité : la passer en param).
4. `InventoryHandlers.AskIdentifyGold` + `ItemActionHandlers.
   IdentifyItem` : formule + éligibilité kinds armes/armures
   (1-16, 37-40, 17-29 → type<2).
5. `ItemActionHandlers.RepairOneItem` → `ItemRepairCost`.
6. Tests + build + commit.

## État

- [x] `SellBackFactor = 346` dans ParameterParser
- [x] col31 = sys price documentée (col17 = monnaie alternative)
- [x] `ShopService` : achat = col31 ; revente = col31×param346
      (×dur/maxDur pour EquipLoc>0 uniquement — un stack porte son
      compte dans durability, pas son usure) ; seuil 1.0 ; blocage
      ids 40001-53000 (erreur native 0x25d)
- [x] `ItemIdentifyCost` (SysPrice×0.05, min 1, kinds type<2) —
      `AskIdentifyGold` + `IdentifyItem`
- [x] `RepairOneItem` → `ItemRepairCost` (formule native partagée)
- [x] Build 0 err/0 warn ; `ShopServiceTests` 8/8 (2 nouveaux
      tests durabilité + seuil) — l'échec baseline
      `SellItem_AddsGoldAndRegistersBuyback` corrigé (compte stack
      = durability)
- [x] Suite : 292 verts, 9 échecs baseline (était 10) — zéro régression

## Déviations documentées
- Remise prestige (`AllFormulaParas+0x114` par niveau) : prestige
  non implémenté → remise 1.0.
- Prix addattr (CItemAddAttr+0xdc) : scaling des prix par
  attributs roulés — non porté (équipement = prix de base col31).
- Revente d'items « combo-able » (def+0xf0&1, col30>1) : branche
  vectorielle native par attributs — fallback formule standard.
