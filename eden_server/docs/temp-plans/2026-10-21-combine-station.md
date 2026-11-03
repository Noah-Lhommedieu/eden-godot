# Plan de lot — station combine (service 10, CCombineOp op 0x27)

## Contexte

La fenêtre "combine" (service NPC 10 → 432 op=10, ou PlayerAction 14)
ouvre un `CCombineOp` natif (IsA 0x27). Le même NCID **170**
`PlayerWork` que les works dispatche vers `CombineItem` quand l'op
est 0x27 (CNE_CZ_ZoneServerPlayerWork::Execute @0x8270ef0).

## Preuves natives (zone/decompiled.c)

- `CheckCombineItem` @0x809b570 :
  - recette = CCombineData (S_FamilyCombine) avec **catégorie col1
    == 6** et exactement 4 matériaux (vec +0x40, stride 0xc
    {id,nom,qté}) — sinon 0x249.
  - chaque matériau : `CheckItemNum(id, qty)` agrégé sac 0.
  - catalyseur = matériau[0] à (bag,slot) exacts, qté ≥ requise.
  - booster optionnel (bag,slot ≠ -1) : CChanceItem kind champ
    +0x122 == **0x1f41** ; qty==1 compte comme slot libéré.
  - or : `char+0x4b0 >= combine+0x24` (col9).
  - slot libre requis seulement si AUCUNE pile ne sera vidée
    (bVar4) — sinon 0x4f9.
- `CombineItem` @0x809b8e0 : op 0x27 + talking range + check OK
  → `SetItemData` + op+0x14=1 + `EffectToLife(CombineItemID, 0x28)`
  → **433** `[s16 10][s16 0x28][s16 0]` ; déjà armé → silence ;
  check KO → **434** `[s16 10]`.
- `DoCombineItem` @0x80b0410 (expiration enchant — ~4 s) :
  re-check (434 si KO) → GainGold(-col9) → matériaux[1..3] par
  `DropItemByItemID` + catalyseur `ItemConsume` au loc + booster 1
  → roll `rand%100 < col13 + booster(+0x124)` :
  - succès → PlayerAction **0x2c** broadcast + AddItemFromCombine
    (produit col10 ×1) + **435** `[s16 10][s16 1][0][0]`
  - échec → **435** `[s16 10][s16 0][0][0]` + PlayerAction **0x2b**.

## Éditions

- [x] Plan (ce fichier)
- [x] `CombineStationService.cs` (Eden.Core/Game/Work) : CheckCombine
      pur (catégorie 6, 4 matériaux, catalyseur au loc, booster
      0x1f41, or, slot libre bVar4) + roll succès
- [x] `EntryState` : `PendingCombine` (recipeId + 4 locs + Cts)
      + cancel au disconnect
- [x] `ZoneEntryFlow.CombineStation.cs` : HandleCombineItemAsync
      (depuis le 170 quand ShopOpType==10), armement 433 +
      PendingCombine, résolution 4 s (consommation, roll, 435,
      PlayerAction 0x2b/0x2c, produit via AddItemAsync + 274)
- [x] `ZoneEntryFlow.Work.cs` : dispatch 170 — ShopOpType==10 →
      combine station avant le check WorkOpType
- [x] `StrengthenClose` : code 10 + PendingCombine → cancel + 434[10]
- [x] Croisement d'ops (pointeur natif unique char+0x670) :
      OpenShopOpAsync → WorkOpType=-1 ; services 12-17 →
      ShopOpType=-1 + cancel des casts
- [x] Tests CombineStationTests (8 tests — recette cat6 réelle,
      check, roll déterministe) + build + commit

## Hors scope

- Booster CChanceItem 0x1f41 : absent des données — validation
  acceptée par id, taux +0x124 non mappé (0).
- Product qty : le natif crée toujours ×1 (AddItemFromCombine).
