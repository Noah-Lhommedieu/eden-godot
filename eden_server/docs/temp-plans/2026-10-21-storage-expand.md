# Lot — Expansion de stockage (NCID 373/374)

## Preuve native

- `CNE_CZ_ZoneServerStorageExpand::Execute` → `CPlayerFactory::
  ExpandStorage` (désasm zone ~0x80f2670) : `expand_storage_cnt`
  (char+0x77c) doit être `< 2` **et** `< taille de la table de prix`
  (`CZoneServer+0x554`, vecteur) ; l'or (char+0x4b0) ≥ `prix[cnt]`
  → débit + `cnt++` + save `"gold expand_storage_cnt"` ; réponse
  `CNC_CZ_ClientStorageExpandResult` opcode 373 `[s16 résultat]`
  (0 = ok, 1 = cap, 2 = or insuffisant).
- `CStorageExpand::CheckExecute` (commande UI) émet les textes :
  `0x5aad` cap atteint, `0x5aae` or manquant (`%lld` = manquant) ;
  `Execute` émet `0x5ab4` (`%hd` = nouveau compteur).
- Source des prix : **S_Parameter 409/410** = 1500/3500
  (« 小屋倉庫第N排擴充金額 », entrepôt du logis). La 3e rangée 411
  est inatteignable (cap natif à 2). `NewStorageRule=1` rend le
  chemin-or secondaire (extensions cash via S_ItemMall).

## Implémenté

- [x] `ParameterParser` : consts `StorageExpandPrice1` = 409,
  `StorageExpandPrice2` = 410.
- [x] `IBankRepository`/`BankRepository` : `SetStorageExpandCountAsync`
  (UPDATE `player_characters.expand_storage_cnt`).
- [x] `BankService` : `MaxStorageExpansions` = 2,
  `EvaluateExpandStorage` (décision pure native → (résultat, coût)),
  passe-plats `GetStorageExpandCountAsync`/`SetStorageExpandCountAsync`.
- [x] `InventoryHandlers` NCID 374 : lecture cnt+or → évaluation →
  débit via `SetGoldAsync` + incrément persistant → textes natifs
  (0x5ab4/0x5aae/0x5aad) → résultat 373. Aucun prix hardcodé :
  les valeurs viennent de S_Parameter via `GameData.Parameters`.
- [x] `PlayerHandlers` PVPFlagModify : commentaire corrigé — le
  `Execute` natif est **vide** (flag géré ailleurs), notre log suffit.

## Tests

- [x] `EvaluateExpandStorage_NativeDecision` : 1re/2e rangée, cap 2,
  or insuffisant, compteur négatif, table vide.
- [x] 8/8 tests banque verts ; suite : 308/317 — 9 échecs baseline
  pré-existants identiques, zéro régression.
