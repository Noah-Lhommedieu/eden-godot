# Lot Collection (cartes de monstres) — 2026-10-23

## Preuves natives

- **Modèle** : chaque carte (item 30xxx listé dans `S_Collection.ini`)
  déposée est déplacée de `inventory` vers la table `collection`
  (même schéma item + `account_id` = player_id). Map native
  char+0x55c : collection_id(=item_id) → item. Points :
  char+0xb68 total + map char+0xe14 (groupe/totaux 1000/1001).
- `S_Collection` : `[u32 id(=item_id)][str name][u8 flag]
  [s16][s16][str text][i32 locate][s16 points][s16 group]` —
  flag=1 → compteur total clé 1001, sinon 1000.
- `S_CollectionPoint` : `[id][?][?][enchant][5×(key,threshold)]` —
  `RefreshCollectionPointAward` retire les enchants accordés,
  réévalue {key → points≥threshold} sur la map groupes, applique
  `EffectToLife(eid, -2)` puis RefreshEQsAttr.
- **150 SaveCollection** : rate-limit ~500 ms ; wire flag=0 →
  `[u8 0][s16 container][s16 slot]`, flag≠0 → `[u8][u16 n]
  [n×s16 containers][u16 m][m×s16 slots]`. Par carte :
  `PutItemToCollection` — checks item, collection def + locate,
  pas déjà collecté (texte 0x680) → EradicateItem + INSERT
  collection + `collection_point` save → 275 `[0][slot][container]`
  + 472 type 1 `{ids}`.
- **151 GetCollection** `[s16 collection_id]` : retrait — item
  rendu au sac (PutItemInInventory) → 472 **type 2** `{id}` +
  274 ClientPutItemIntoInventory.
- **152 QueryCollection** : 472 **type 0** `[s16 0][i32 points]
  [u16 n][n×s16 collection_ids]`.
- 472 Serialize : `[s16 type][i32 points][u16 n][n×s16]`.
- DELETE natif : `Delete Item collection id= %d item_id= %hd`.

## Implémentation

- [ ] `GameDataService` : `Collections` (S_Collection) +
      `CollectionPointAwards` (S_CollectionPoint).
- [ ] `InventoryRepository` : `GetCollectionItemsAsync`,
      `MoveItemToCollectionAsync` (INSERT…SELECT + DELETE, tx),
      `MoveItemFromCollectionAsync` (idem + loc/bag du sac).
- [ ] `EntryState` : `CollectedItemIds`, `CollectionPoints`,
      `CollectionGroupPoints`, `CollectionAwardIds`,
      `LastCollectionSaveAt`.
- [ ] `Lifecycle` : charge `collection` (recompute points natif
      UpdateCollectionPoint mode 1).
- [ ] `ZoneEntryFlow.Collection.cs` : handlers 150/151/152 +
      `RefreshCollectionAwardAsync` + émissions 275/472/274.
- [ ] `player_achievement_rank.collection_point` mis à jour
      (best-effort — colonne native du total).
- [ ] Dispatch + stubs HousingHandlers → commentaires.
- [ ] Tests codec 472 + parsers + compteurs.
