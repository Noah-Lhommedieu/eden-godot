# Lot — Item Mall : catalogue, points, receivables (monde+zone)

Source : `re/ghidra/world/decompiled.c`, `re/ghidra/zone/decompiled.c`,
`re/ghidra/gateway/decompiled.c`, schéma `fnaccount` (itemmall 515 lignes,
item_receivable, item_receipt, itemmall_limit_amount).

## Flux natif décodé

### Client ↔ Monde (table CW)
- **15** `CNE_CW_ServerItemMallAnythingReceivable` (vide) → **56** `[u8 has]`
  (`SELECT id FROM item_receivable WHERE account_name=? AND state=1` LIMIT 1).
- **17** `CNE_CW_ServerItemMallReceivableQuery` `[u16 limit][str t1][str t2]` →
  **57** `[s16 count][count×{u32 id, u16 item_id, u16 qty, str purchase_time}]`.
  Variantes SQL : t1 vide → toutes ; t1 seul → `purchase_time > t1` ;
  t1+t2 → `purchase_time NOT BETWEEN t1 AND t2`. `ORDER BY purchase_time DESC`.
- **18** `CNE_CW_ServerItemMallReceieveAttempt` `[u32 receivableId]` →
  claim : `SELECT ... state=1` → `UPDATE state=2` → forward zone (ZW 24) ;
  erreurs DB → **58** `[s16 code, u32 id]` (0xb/0xc/99). Pas de réponse client
  quand le forward part (le client voit l'item arriver + texte 0x89).
- **23** `CNE_CW_ServerItemMallReqBuyItem`
  `[s16 key=money*100+group][s16 item_index][s16 amount][u16 version][str a][str b][str c]`
  → validations catalogue (version → resync 62/79 si obsolète, item existant,
  quantité 1+ sauf stackable=1 & item_num=1 → erreur, level_limit, num_limit via
  itemmall_limit_amount, points suffisants) → `INSERT INTO item_receivable`
  → **63** `[s16 result, i32 points1, i32 points2]` ; erreurs : 1/3 (points),
  5/6 (limite), 7 (niveau), 99.
- **24** `CNE_CW_ServerItemMallListQuery` `[s16 ?][u16 clientVersion]` →
  si version != ItemMallVersion → **62** + **79**.
  - **62** `[u16 version][u16 count][count×{s16 money*100+group, s16 item_index,
    u16 item_id, u16 item_num, u32 point, u32 special_price, u16 num_limit,
    i64 item_color, i32 not_sell_epoch, str fortune_bag, str note,
    u16 level_limit, i32 sell_epoch}]`
    (sell_date/not_sell_date `YYYYMMDDHH` → epoch timelocal ; sell passé → 0 ;
    not_sell passé → item exclu ; num_limit renseigné seulement groupe 8).
  - **79** `[u16 ?][u16 count][count×{s16 group, s16 index, u16 item_id,
    u16 item_num, u16 sell_amount}]` (itemmall_limit_amount du compte).
- **25** `CNE_CW_ServerItemMallPointInfo` → **64** `[i32 p1, i32 p2]`
  (natif : forward gateway → billing `get_point_info(account)` ; ici table
  `account_points` — déviation documentée : la base billing n'existe pas).

### Monde ↔ Zone (lien ZW, table InitZoneWorldCommandTable partagée)
- **24** W→Z `CNE_ZW_ZoneServerItemMallReceieveAttempt`
  `[u32 receivableId][u32 charId][u16 itemId][u16 qty][i64 itemColor]`
  → zone : char online (21), item template connu (24), slot libre (25) →
  livraison inventaire → texte 0x89 « %s x %d ».
- **12** Z→W `CNC_ZW_WorldServerItemMallReceieveAttempt`
  `[s16 result][u32 receivable][u32 charId][u32 uniqueId][u32 createTime]`
  → succès : `ItemMallReceieveSuccess` (SELECT state=2 → INSERT item_receipt
  (avec unique_id/create_time) → DELETE item_receivable) ;
  échec : `ItemMallReceieveFailure` (`UPDATE state=1`).

### Client ↔ Zone (CZ)
- **70** `CNE_CZ_ZoneServerItemMallReceivableEnter` (vide) → pose op
  `CItemMallReceivableOp` (IsA 0x2f, timestamp) via ResetOperation.
- **83** `CNE_CZ_ZoneServerItemMallUseItem`
  `[s16 sel(0=sac/1=équipé)][s16 container][s16 slot][i32 uiItemId][str params]`
  → `UseItemParam(container, slot, uiItemId, params, -1,-1,-1)` — même chemin
  que NCID 19 mais cible explicite sac/équipé + uiItemId.

## Implémentation

1. [x] `ItemMallRepository` (connexion **fnaccount**) :
   catalogue (sell=1, filtres dates natifs, num_limit groupe 8),
   receivables (query/claim/success/failure), limit_amount.
2. [x] **Points : pas de migration `account_points`** — le schéma
   fnmember réel a `tb_user.pvalues`/`bonus` (colonnes natives déjà
   provisionnées). `UserRepository.GetItemMallPointsAsync` /
   `TryDeductItemMallPointsAsync` (UPDATE atomique `solde >= coût`).
   Le billing externe natif est remplacé par la déduction synchrone.
3. [x] `Eden.World/Handlers/ItemMallHandlers.cs` : handlers 15/17/18/23/
   24/25 + émission 56/57/58/62/63/64/79 (builders dédiés dans
   `Eden.Core/Game/Mall/ItemMallPackets.cs` — les classes générées sont
   par-entrée, pas le format vecteur natif).
4. [x] `ZoneLinkServer` : envoi 24 à la zone, réception 12 →
   `CompleteReceivableAsync` (insert item_receipt + delete) ou
   `RequeueReceivableAsync` (state 1) + 58 erreur client si échec.
5. [x] `Eden.Zone` : `ZoneWorldLink` NCID 24 →
   `ZoneEntryFlow.HandleItemMallDeliveryAsync` (codes natifs 0x15
   char absent / 0x18 itemdata / 0x19 plein, AddItemAsync + texte
   0x89 + 274) → réponse 12 {result, receipt, char, serial, ctime}.
6. [x] Zone CZ : NCID 70 (`ShopOpType = 0x2f` — CItemMallReceivableOp
   natif) + NCID 83 `ItemMallUseItem` → pipeline partagé
   `UseItemFinalAsync` (enchant → 291 → 303 → 275), sel=1 équipement
   consomme via `RemoveEquippedItemAsync` (pas le sac !).
7. [x] money_unit 3 : déduit sur pvalues (pas de billing externe —
   commentaire HYPOTHESIS dans `TryDeductItemMallPointsAsync`).
8. [x] 23 tests `ItemMallTests` : filtres Load, coût/amount, Find,
   wire 62/79/ZW24/ZW12 byte-par-byte, claim→receipt DB, requeue,
   limit_amount, points fnmember atomiques.
9. [x] Build 0 erreur/warning ; suite : 281 verts, 10 échecs =
   baseline pré-existante (mêmes tests que les lots inlay/works).

## Exclusions déviation documentées
- Soldes lus sur `fnmember.tb_user` (pvalues/bonus) — la base billing
  native n'existe pas ; déduction synchrone au lieu du circuit
  gateway→billing `get_point_info`.
- money_unit=3 : rabattu sur pvalues (HYPOTHESIS documentée).
- fortune_bag / recognized_percentage : colonnes propagées, tirage sac
  loterie hors scope (système lottery séparé).
- Vécu live client : non testé encore (à valider sur les deux clients).
