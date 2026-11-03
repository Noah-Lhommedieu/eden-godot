# Plan de lot — player_works (ordres de fabrication)

## Contexte

Les "works" = ordres de travail lancés aux établis métiers (services
NPC 12-17 → `CWorkOperation` IsA 0x3d-0x42). Le client envoie
**NCID 170** `CNE_CZ_ZoneServerPlayerWork` ; le serveur enregistre un
`CWork` (char+0xb20..+0xb44, **10 slots**), le persiste dans
`player_works`, arme l'enchant de durée (= le cast lui-même, affiché
via 433 StrengthenCasting), dont l'expiration appelle EndCollect/
EndCombine, puis le joueur réclame via **NCID 171** `ResultOK` →
`AddItemFromWork` (sockets/combos/curse préservés) + **500**
`ResultEnd`.

## Preuves natives VERIFIÉES (zone/decompiled.c)

### Wire entrants
- **170** `Execute` @0x8270ef0 : `[u32 workId][s16 catBag][s16 catSlot]
  [s16 boostBag][s16 boostSlot]` — op IsA 0x27 → `CombineItem`
  (service 10, hors scope) ; op IsA 0x3d-0x42 → `GetShopType`
  {0x3d→12 … 0x42→17} → `CWorkManager::ToWork(char, type, workId,
  catBag, catSlot, boostBag, boostSlot)`. Op `CWorkOperation` requise,
  `IsCasting` interdit si déjà occupé.
- **171** `[s16 workNum]` → `WorkResultOK(char, workNum)`.
- **172** vide → `WorkCheckData(char)`.

### Wire sortants (relus byte-par-byte)
- **497** `ClientPlayerWorkUpdate::Serialize` @0x816bc40 :
  `[u16 n]` puis par entrée `[s16 workNum][s16 workType][i32 workId]
  [i32 effectId][string fmName]` — effectId = `work+4` (l'enchant
  200+slot armé par `EffectToLife`).
- **498** `ClientPlayerWorkResult::Serialize` @0x816bb40 :
  `[i32 workId][s16 workType][s16 workNum][string fmName][s16 nRes]`
  puis par résultat `[u16 len][i32×len]` — len = 13 ints par
  `GetWorkItemVector` @0x84d1a90 : `[itemId, amount, socket_amount,
  combo, extra_combo×9]` (le byte curse +0x40 n'est PAS émis).
- **500** `ClientPlayerWorkResultEnd` `[s16 workNum]` — émis au claim
  quand work_event==0 (ResetWork) ; work_event!=0 → flag work+0x30=1 +
  `ClientPlayerEvent` (famille — hors scope).
- **433** `ClientStrengthenCasting [s16 type][s16 durée×100ms]` — le
  cast bar pendant toute la durée du travail.

### Validation (CheckToCollect @0x81628b0 / CheckToCombine @0x8163ba0)
- Catalyseur : collect → `item_id == collect.col2` (0x249) ; kind par
  type {12→0x33, 13→0x32, 14→0x34} ; combine → kind {15→0x35,
  16→0x36, 17→0x37}. Template type 0x15 = kinds recette — le check
  kind suffit.
- Quantité catalyseur `CheckItemNum(col4)` → 0x352 ; combine :
  `CheckItemNum` par matériau → texte 0x9f4.
- Or : `gold + opTax(op+0x18 %)` — NPC public op+0x18=0 → coût =
  col13/col9 ; insuffisant → 0x232.
- Sans famille : `bldLevel(col5/col2) ≤ 2` sinon 0x9f1 ; avec famille
  : bâtiment présent + niveau + durabilité (hors scope).
- instant (collect col9==1 / combine col5==1) ou type 14 : consommation
  DÉCALÉE à la fin via `op.SetAllItemLoc` ; sinon consommation au
  départ (matériaux `DropItemByItemID`, catalyseur `ItemConsume`, or).
  Instant exige ≥1 slot libre (0x4f9).
- Niveau catalyseur : `itemdata+0xfc` = sous-struct `{+0x22=col14
  RequiredLevel, +0x24=col15 MaxLevel(0=∞)}` vs char level (ToWork).

### Déroulé
- `ToCollect` : durée = instant ? col10 : col10×600 (×100ms) ;
  booster `CChanceItem` (enchant +0x122 ∈ {0x1f43-0x1f48}) optionnel
  (consommé au départ pour non-instant) ; `SaveWork(workId, type,
  decline, bonus, fmName, fmId, bldId, extSuccess, extBuilding)` ;
  work+0x36=1 ; EffectToLife(200+slot, durée) ; **433** + **497**.
- `EndCollect` @0x8166f60 : instant → consomme catalyseur col4 + or +
  booster mall ; roll par produit : `fail = 100-rate ;
  fail -= fail*decline/100 ; rand%100 < (100-fail)+extSuccess` →
  `AddWrokItem(id, qty+bonus)` ; rien → `SaveWork(-workId,0)` ;
  **498**.
- `EndCombine` @0x81652b0 : instant → consomme booster + matériaux +
  or ; roll succès `fail=100-col13 ; fail-=fail*decline/100 ;
  rand < (100-fail)+extSuccess` → produit col10 ×col12 ; équipement
  → roll sockets (+0xf6 max, +0xf7 rate — même boucle que
  `ChangeEmbeddedAmount`) + `SelectWorkItemCombo` (RandItemCombo) +
  curse `rand%1e8 <= col14` ; **498**.
- `WorkCheckData` @0x8169350 : par slot — enchant actif → entrée 497
  agrégée ; terminé → **498** par slot (+0x36=1) ; work_event!=0 →
  cmd map (collect 12-13 / combine 15-17) sinon ResetWork.
- `WorkResultOK` @0x81646a0 : enchant terminé requis ; work+0x30 →
  `ToExecCmd` (event) ; sinon slots libres ≥ produits non-stacks
  (0x4f9 texte) → `AddItemFromWork` par produit → exp recette
  (collect+0x20 = col8) → event==0 → ResetWork + **500**.

### Schéma player_works (fndb1, réel)
`player_id+work_num` PK, work_type/id/event/decline/bouns/fm_name/
fm_id/building_item_id/ext_success/ext_building + item1-4 ×
{id,amount,socket_amount,combo,extra_combo1_1..9,curse} — pas de
colonne timer → ajout `finish_at bigint` (serveur-seul).

## Éditions

- [x] `WorkOrderParser` (Eden.Core/Data/Parsers) : `WorkCollectDef`
      (col2 catalyst, col4 qty, col5 bldLevel, col9 instant,
      col10 durée, col13 or, col14+ produits×4 {id,nom,qté,taux}) +
      `WorkCombineDef` (col2 bldLevel, col5 instant, col6 durée,
      col9 or, col10 produit, col12 qté, col13 succès, col14 curse,
      col15+ matériaux×3 {id,nom,qté})
- [x] `GameDataService` : `WorkCollectById` (S_FamilyCollect.ini) +
      `WorkCombineById` (même S_FamilyCombine.ini, modèle travaux)
- [x] `ItemParser` : `MaxLevel` (col15) — niveau max catalyseur
      (vide = ∞ sur les catalyseurs réels)
- [x] `ItemEntity` + `NativeItemRow` + `LiveSelect` + `AddItemAsync` :
      `combo_id`, `extra_combo_id1..9`, `witchcraft` (curse)
- [x] `WorkRepository` : EnsureRowsAsync (11 lignes natives),
      GetWorksAsync (rows dict), SaveAsync/EnsureAndSaveAsync/
      ResetAsync (UPDATE complet) + `finish_at` (migration 004,
      ALTER live appliqué)
- [x] `WorkOrderService` (Eden.Core/Game/Work) : ValidateStart
      (type/slot/catalyseur+kind/niveau/or), HasMaterials,
      RollCollectProducts (taux+decline+bonus par produit),
      RollCombineProduct (succès+curse+sockets), SlotsNeededFor
- [x] `ZoneEntryFlow.Work.cs` : handlers 170/171/172, timers par
      work_num (WorkCts, annulés à la déconnexion), finish_at
      persisté → reconnect/restart safe, emit 433/497/498/500,
      claim → inventaire + texte 0x534 "%s x %d" consommations
- [x] `ZoneEntryFlow.Dialogue.cs` : `WorkOpType=type` sur services
      12-17 (l'op works natif) — 496 inchangé
- [x] `ZoneEntryFlow.cs` : cases 170/171/172, EntryState
      (Works/WorkOpType/WorkOpStarted/WorkDeferredLocs/WorkCts),
      LoadWorksAsync à l'entrée, cancel des timers au disconnect
- [x] `WorkPackets` (Eden.Zone/Codecs) : 497 (u16 n + entrées
      num/type/id/enchant 200+num/fm_name) + 498 (id/type/num/nom +
      vecteurs 13 i32) — formats relus sur les Serialize natifs
- [x] `MiscHandlers.cs` : stubs no-op retirés (le flow court-circuite
      avant le dispatcher)
- [x] `NativeItemCodec` : WriteItem/BuildPutItem émettent sockets
      (bits0-5+6), combo (bit7), extra_combos (bits10-18) via meta
- [x] Tests : WorkOrderTests (18 tests — parser, slots, validation,
      tirages déterministes, codecs byte-exacts, repo DB live) +
      build zéro warning + commit

## Hors scope (lots suivants)

- Combine op 0x27 (service 10 — `CombineItem`/`ExecuteCombine`,
  fenêtre strengthen op 10 : cast 0x28 + enchant CombineItemID)
- `EndSummon` type 14 (invocation — EndCollect couvre le produit ;
  le spawn de familier reste à porter si distinct)
- `ToExecCmd` (work_event≠0, CWorkEventCmd — récompenses
  événementielles famille) + `ClientPlayerEvent`
- Contributions famille : bâtiments, taxes, UpdateBuildingDurability/
  Revenue, HandleRACombine/HandleRASpecificItem
- Boosters CChanceItem item-mall (consommation à la fin via locs op)
  — la validation + roll des effets +0x122 est implémentée ; la
  consommation du sac mall natif reste un point ouvert
