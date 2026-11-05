# S3 — Recon serveur C# : commerce (shops, HV, trade, étals, item mall, banque)

Date : 2026-11-03. Périmètre : `eden_server/server/src/Eden.{Zone,World,Core}`.
Lecture seule. Statuts : [VERIFIED] = lu dans le code · [HYPOTHESIS] = inférence ·
[GAP] = absent ou non prouvé.

## 1. Dispatch

`Program.cs:960-970` : `ZoneEntryFlow.TryHandleAsync` (switch NCID brut, sur le
fil de `ZoneLoop`) court-circuite le `PacketDispatcher` typé — un `case` qui
retourne `true` consomme le paquet avant les `d.Register*`. Les shops PNJ
utilisent le dispatcher typé (`Handlers/ShopHandlers.cs`, 501 l.) ; les étals
joueurs passent par le switch brut (`ZoneEntryFlow.Vending.cs`, 514 l.) car les
formats à vecteurs parallèles ne tiennent pas dans les `.g.cs`
(`Codecs/VendorPackets.cs:8-18`).

## 2. Matrice NCID → handler → état

### Shops PNJ (vendor buy/sell/buyback) — `ShopHandlers.cs`

| NCID | Nom | Handler | Réponse | État |
|---|---|---|---|---|
| 22 | `CNE_CZ_ZoneServerBuyItems` | `ShopHandlers.cs:32` (`ZoneEntryFlow.cs:880` retourne `false` pour laisser passer) | 274/291 + 308 `ClientBuyUp` + 280 attr 0x02 | implémenté [VERIFIED] |
| 23 | `CNE_CZ_ZoneServerSellItems` | `ShopHandlers.cs:105` | 275/291 par item + texte 0xaa + 280 + 309 `ClientSellOut` + 512 flag=1 (delta buyback) | implémenté [VERIFIED] |
| 63 | `…LeaveShop` | `ShopHandlers.cs:257` + `ZoneEntryFlow.cs:899` (double) | 375 `ClientVendorClose` | implémenté |
| 185 | `…BuyBackOpen [u8 flag]` | `ShopHandlers.cs:218` | flag=0 → 304 ré-ouverture ; flag≠0 → 512 flag=3 vide | implémenté · matrice `NON PROUVE` |
| — | rachat buyback | `BuyItems.Count!=0` → `ShopService.BuyBackItemAsync` (`ShopHandlers.cs:39-79`) | 274 + 512 flag=2 + 280 | implémenté |
| 114 | `…AuctionBuyQuery` | `ShopHandlers.cs:420` | 425 `ClientAuctionBuyList` | implémenté |
| 115 | `…AuctionBuy` | `ShopHandlers.cs:331` | 426 `ClientAuctionBuyOK` | implémenté |
| 116 | `…AuctionSellQuery` | `ShopHandlers.cs:402` | 428 `ClientAuctionSellList` (+ solde les annonces expirées via mail) | implémenté |
| 117 | `…AuctionSell` | `ShopHandlers.cs:287` | 429 `ClientAuctionSellResult` | implémenté |
| 118 | `…AuctionCancel` | `ShopHandlers.cs:385` | 427 `ClientAuctionCancelOK` | implémenté |

- **Ouverture boutique** : AskVendor (71) est intercepté par
  `ZoneEntryFlow.cs:962` = no-op natif ; l'enregistrement `ShopHandlers.cs:19`
  (lookup `npc.StoreId` → 304) est **inaccessible** [GAP mineur, code mort].
  Chemin réel : `TalkToNPC` (`ShopHandlers.cs:266-281`, `StoreId>0` → 304) ou
  dialogue `#:Service`/`#fn#` → `RunNpcServiceAsync` cases 1-4/23/24/26/29 →
  `SendEnterShopAsync` (`ZoneEntryFlow.Dialogue.cs:265-272`).
- **Sélection boutique par NPC** : `NpcDef.StoreId` = premier `#:Service` de
  type 3 (`WorldParsers.cs:51`) ; `FuncId` 1/2/3 → arg = id `S_Store`
  (`WorldParsers.cs:53-58`). 304 = `[i32 npcUid][s16 storeId][u8][s16]`
  (`ZoneMessages.g.cs:6636`).

### Trade joueur — `TradeHandlers.cs` (184 l.) + `TradeService.cs` (337 l.)

36 Request (rate-limit 0,5 s, dist ≤10, `TradeHandlers.cs:28`) · 37 Accept ·
38 AddItem (réservation tx `trade_items`) · 39 RemoveItem · 40 ModifyGold ·
41 Confirm (double → 340 `ClientTradeComplete`, `TradeHandlers.cs:116-135`) ·
42 Cancel · 56 Reset · 91 Ready · 92 UnConfirm. Réponses 331-340/353/394-395
(DESASM). Exécution atomique `TradeRepository.SettleTradeAsync`
(`TradeService.cs:218-231`). 340 marqué NON PROUVE en matrice malgré l'émission.

### Étal joueur (vending/stall) — `ZoneEntryFlow.Vending.cs` + `StallService`

| NCID | Handler | État |
|---|---|---|
| 71 AskVendor | `Vending.cs:29` no-op natif | implémenté |
| 72 AskVendorContainer | `Vending.cs:201` → 377 [1][2] + 378 UpdateView | implémenté |
| 74 VendorClose | `Vending.cs:362` → viewers 376+texte 0xf07, vendeur 375 | implémenté |
| 75 VendorCloseView | `Vending.cs:373` reset op 0x2d | implémenté |
| 76 VendorBuy | `Vending.cs:235` — lot complet (qty==count), tx `SettleStallPurchaseAsync`, 275/274/280×2 | implémenté |
| 77 VendorSellItem | `Vending.cs:44` — gates équipe/file, node S001, régions 600-609, titre <91 o Big5 | implémenté |
| 78/80 | no-op natifs (`ZoneEntryFlow.cs:978-980`) | implémenté |

Codec `VendorPackets.cs` : 77 `[i32 région][str][u8 n][n×10o]`, 76
`[i32 vendeur][u16 n][n×(s16,u8)]`, 378 = records 98 o (`GoodsRecordBytes`).
265 `ClientSeeChar` de l'étal volontairement non émis (`Vending.cs:189-192` —
risque crash, format non prouvé) [GAP visuel : silhouette d'étal].

### Item mall — monde (`ItemMallHandlers.cs`) + zone (`ZoneEntryFlow.ItemMall.cs`)

| NCID | Sens | Handler | État matrice |
|---|---|---|---|
| CW 15 AnythingReceivable | C→W | `ItemMallHandlers.cs:42` → 56 [u8] | INCOMPLET/DESASM |
| CW 17 ReceivableQuery | C→W | `:54` → 57 records | NON PROUVE |
| CW 18 ReceieveAttempt | C→W | `:66` claim 1→2 → ZW 24 → 58 | PASSTHRU |
| CW 23 ReqBuyItem | C→W | `:106/:132` → 63 {result,pvalues,bonus} | INCOMPLET |
| CW 24 ListQuery | C→W | `:113` → 62 batches + 79 limits | INCOMPLET |
| CW 25 PointInfo | C→W | `:120` → 64 | GOLDEN |
| ZW 24 / ZW 12 | W↔Z | `ZoneWorldLink.cs:165` → `ZoneEntryFlow.ItemMall.cs:40` ; résultat → `ZoneLinkServer.cs:263` | NON PROUVE |
| CZ 70 ReceivableEnter | C→Z | `ZoneEntryFlow.cs:937` pose op 0x2f | implémenté |
| CZ 83 ItemMallUseItem | C→Z | `ItemActionHandlers.cs:560` — pipeline ItemUse 19 | implémenté |

Monnaies : `pvalues`/`bonus` de `edenfr_member.tb_user`
(`ItemMallHandlers.cs:264`) ; unit 3 (temps de jeu) rabattu sur pvalues —
le billing LP_ natif absent (`:176-178`). Catalogue = table `itemmall` SQL
(`ItemMallRepositories.cs:31-56`), validée natif (`ItemMallCatalog.cs:37`).

### Banque / hôtel / family storage

60 StorageDeposit `InventoryHandlers.cs:316` · 61 Withdraw `:366` (Execute natif
mort — handler conservé) · 62 GetStorageData `:409` **partiel** (replay
post-entrée) · 69 StorageExpand `:425` (40+20×`expand_storage_cnt`,
`BankRepository.cs:48`). Ouverture : service 6 → 357 `EnterStorage`
(`Dialogue.cs:278`). Famille : service 19 → 507/508 + put/get/log NCIDs
(`InventoryHandlers.cs:486-…`, `FamilyBankRepository`). Hôtel/chambres :
services 50-54 → 539 `ClientOpenHotel` + `CNE_CZ_ZoneServerEnterRoom`
(enregistrement inerte, `InstanceHandlers.cs:68`; vrai flux
`ZoneEntryFlow.Room.cs`).

### Spell shop / repair / identify

- Spell shop : 349 `EnterSpellShop` via commande `OpenShop 1`
  (`ZoneEntryFlow.Items.cs:372`). Achat : NCID 52 `BuySpell` — **Execute natif
  vide**, mais implémenté avec formule inventée `level×100 or`
  (`InstanceHandlers.cs:78-114`) [HYPOTHESIS non-native].
- Repair : 350 `EnterRepairShop` (Mission.Commands) ; `RepairEquipment`/`RepairOneItem`
  (`ItemActionHandlers.cs:24,37`), formule native `GetItemRepairPrice`
  col76×0.2 (`:1331`, `InventoryHandlers.ItemRepairCost`) ; devis
  `AskRepairOneItemGold` (`InventoryHandlers.cs:249`).
- Identify : `AskIdentifyGold` (`InventoryHandlers.cs:261`), `IdentifyItem`
  (`ItemActionHandlers.cs:58`), 365 `EnterIdentifyShop`.

## 3. Logique prix/taxes [VERIFIED]

- Achat boutique = `S_Item` col31 `SellPrice` (SysPrice) × 1.0, remise prestige
  absente (`ShopService.cs:89-93`). Vente = `floor(SysPrice × S_Parameter 346
  SellBackFactor × dur/maxDur)` équipement, seuil <1.0 refusé, ids 40001-53000
  non vendables (`ShopService.cs:95-114,170-174`).
- Buyback : deque 10 max mémoire session, prix payé restauré
  (`ShopService.cs:27-30,229-265`) — pas de table SQL (conforme natif).
- HV : dépôt 5 % (`AuctionService.cs:37`), commission 8 % (`:39`), durée figée
  48 h (`ShopHandlers.cs:313`), achat = prix/buyout, règlement atomique
  `MailRepository.SettleAuctionPurchaseAsync`, retour/expiré par mail tx.
- Étal : prix libre par lot, or cap 999 999 999 (`Vending.cs:278`), tx vendeur
  verrouillée (`BuyLock`).
- Transport : `S_Transport.ini` paliers de niveau (`ShopHandlers.cs:430-450`).

## 4. Données & tables

- `ParseStores` lit **`S_Store.ini`** depuis `eden_server/server_us/Data/db/`
  (`GameDataService.cs:333`, `ShopAndMiscParsers.cs:18`) — triplets
  `itemId|nom|count` dès col 3. **`corpus/fr-full/data/db/c_store.ini` n'est
  chargé par aucun parser serveur** [VERIFIED]. Ambiguïté : le 3ᵉ champ est
  nommé `Count` au parse mais lu comme `flag` (« not for sale » si 0,
  `ShopService.cs:135-137`).
- Tables : `active_auctions`+`auction_history` (001), `active_trades`+`trade_items`
  (015), `storage` vue UNION `storage1/storage2` (`BankRepository.cs:8-15`,
  `player_bank` archivée 016), `family_storage` (`FamilyBankRepository`),
  `itemmall`/`item_receivable`/`item_receipt`/`itemmall_limit_amount` (008,
  edenfr_account), `special_coins` (`MissionDb.cs:431-445`), `player_mail`/`mailitem`.
- `auction_history` : créée, **jamais écrite** [GAP].

## 5. Doc vs code (`docs/implementation/07-auction-system.md`)

Le doc annonce `AuctionSell{ItemId,Quantity,Price,Duration}` — le wire réel est
`[s16 bag][s16 slot][i32 price][i32 buyout]` (`ZoneMessages.g.cs:19573`,
`ShopHandlers.cs:286`), commission doc 5 % vs code dépôt 5 % + vente 8 %,
durée doc paramétrable vs 48 h fixe. `AuctionBuyQuery` : les filtres client
sont ignorés — `Search()` renvoie tout, trié prix (`ShopHandlers.cs:420-427`,
`AuctionService.cs:129`) [GAP filtres]. Pas d'enchères à enchérir : achat
direct au prix/buyout seulement.

## 6. Tests

`ShopServiceTests.cs` 19 (buy/sell/buyback+wire 512), `TradeServiceTests.cs` 7,
`VendorStallTests.cs` 17 (codecs+gates), `ItemMallTests.cs` 19 (catalogue,
claim, limites, déduction), `BankServiceTests.cs` 10 + `BankTests.cs` 2.
Pas de test handler niveau paquet pour l'HV [GAP].

## 7. GAPs consolidés

1. **Fame shop / prestige régional** (fn 23) et **boutique spéciale** (fn 24) :
   routés vers le 304 générique `S_Store` en or — aucune monnaie
   prestige/special-coin côté achat (`Dialogue.cs:268-272`).
2. **Special coin shops** : `special_coins` gagné/poussé (80), jamais dépensé.
3. **NeoBank** : aucune trace serveur (mod Reforged client uniquement).
4. **Shop de guilde** : absent (`GuildFunction.ini` non consommé pour shops).
5. `auction_history` morte ; filtres HV ignorés ; durée fixe 48 h.
6. `ItemMallService.cs` (wallets AP/EP mémoire) = code non câblé, doublon du
   flux natif `itemmall`/tb_user.
7. `ClientVendorContainer` 379 émis vide seulement ; `SeeChar` d'étal non émis.
8. `GetStorageData` 62 / `ClientPutItemIntoStorage` 359 partiels.
9. `BuySpell` 52 implémenté hors preuve native (formule inventée).
10. Enregistrement `AskVendor` `ShopHandlers.cs:19` mort (court-circuit flow).
