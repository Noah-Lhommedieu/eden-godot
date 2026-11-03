# Lot — Étal joueur (player vending / stall) natif

Date : 2026-10-20 — suivi du master plan (stubs restants).

## Symptôme

`VendorHandlers.cs` est spéculatif : dictionnaire statique
`(ItemUid, Price, Quantity)`, aucun transfert d'item réel, aucun
paquet client natif, aucune validation (région, node, distance,
identité d'item). À remplacer par le sous-système natif complet.

## Preuves natives (zone/decompiled.c)

### Wire entrant (CZ→Zone)
- **71 AskVendor** `[i32]` → Execute : gate IsNoTrade → rootcmd
  "askvendor" inexistante = **no-op effectif** (@0x80fb5xx).
- **72 AskVendorContainer** `[i32 targetCharId]` → Execute :
  checks + installe `CVendorViewOp` (0x2d) + `VendorContainer()`
  (@0x80fb9xx).
- **74 VendorClose** `[u8][u8]` (2e u8 lu, non sérialisé) →
  `VendorClose(char, byte0 != 0)`.
- **75 VendorCloseView** `[i32 sellerId]` → reset op si type 0x2d
  et op.target == sellerId.
- **76 VendorBuy** `[i32 sellerId][u16 n][n × (s16 qty, u8 idx)]`.
- **77 VendorSellItem** `[i32 regionType][str title][u8 n]
  [n × (s16 container, s16 index, s16 loc?, i32 price)]` (4 vecteurs
  parallèles).
- **78 VendorCloseVending** → `VendorCloseVending` = **no-op natif**
  (@0x80ecd20 corps vide).
- **80 VendorUseItem** `[u8][s16]` → `VendorUseItem` = **no-op natif**
  (@0x80ecd40). `CanVendor` retourne 0 (no-op).

### Wire sortant (Zone→CZ)
- **375 ClientVendorClose** `[i32 charId][u8 bool]` (Serialize
  @0x81355b0 : i32 puis char).
- **376 ClientVendorCloseView** `[i32 sellerId]`.
- **377 ClientVendorStatus** `[u8][u8]` (@0x8135xxx).
- **378 ClientVendorUpdateView** `[i32 sellerId][str title][u8 n]
  [n × 62o]` — record SStallGoodsInfo (base = noeud+0x14) :
  u8 index | u32 itemId | i32 clone+8 (container_index) |
  s16 container | s16 index | i32 clone+0xc (loc index) |
  i32 clone+0xe | u16 clone+0x1c (durability) | s16 clone+0x10
  (combo) | u8 clone+0x24&7 (embedded_amount) | s16×6 clone+0x40
  (embedded ids) | s16×9 clone+0x34 (extra combos) | i32 price.
- **379 ClientVendorContainer** `[u8]` — jamais construit en zone
  = chemin mort (on émet [i32 target][u8 0] seulement sur
  target-miss d'AskVendorContainer, comme le natif).
- **275 ClientDropItem** `[s16×3]` au vendeur après achat.
- **274 ClientPutItemIntoInventory** à l'acheteur (`BuildPutItem`).
- **290 ClientPlayerAction** act 4 (sit, ouverture) / 5 (stand,
  fermeture) — NotifyClient + BroadCastToCanSee.
- **265 ClientSeeChar** broadcast à l'ouverture (flags +0x3b bit 0x40
  posé) — body ~0x60 décodé @0x811a970.
- Textes : 0x11 (état), 0x12 (délai), 0x13 (CanDo stall), 0x333
  (trop loin), 0x64c (duel), 0xd20 (file arène), 0xf07 (étal fermé),
  0xfd3 (vendeur cap or), 0xfd8 (en groupe), 0x1160 (file party),
  0x1176 (safe mode / no-trade).

### Sémantique métier
- **CheckBeginStallForPlayer** : pas mort ; `+0x3b&0x40` (déjà
  vendeur) → [0][0x11] ; CanDo(0xf) sauf si déjà assis (bit7) ;
  rate-limit **1,5 s** → [0][0x12] ; CanDo(0xe) → [0][0x13] ;
  `+0x95c != -1` → 0x64c (duel — pas de duels chez nous) ;
  **node == "S001"** (CheckStallNode) ; **région 600-609** via
  `[position]` col7 + rayon col4 (CheckStallRegion) ; party → 0xfd8 ;
  files arène/party → 0xd20/0x1160 ; `+0x4a==5`, `+0x44 handler`.
- **CheckBeginStallForStallGoods** : vecteurs non vides + même
  taille ; collisions (container,index) → [0][0x15] ; titre vide →
  [0][0x14] ; prix 0..999999999, **≤13 prix à 0** → [0][0x16].
- **VendorSellItem** : titre < 91 octets ; item à (container,index)
  existe ; pas `Flags&8` (no-trade col11 bit2) ; pas équipé ;
  quality != 1 rejeté ; ErasedItem de l'inventaire → clone dans le
  record ; `ClientVendorStatus[1][2]` ; PlayerAction 4 broadcast ;
  SeeChar broadcast ; op `CVendorOp` 0x2c ; goods erase →
  `+0x7ec` stall.
- **VendorBuy** (par ligne) : stall existe, idx dans map ;
  `stale-check` item_id (rec+0x14 vs itemdata+4) **et** clone+0xe
  (rec+0x24) — mismatch → erase + UpdateView viewers ; qty ≥1 ;
  or acheteur ≥ price ; or vendeur + price ≤ 999999999 (sinon 0xfd3) ;
  slot libre acheteur ; transfer `SwitchItemLocation` ; or ±price ;
  DropItem vendeur ; PutItem acheteur ; flush DB ×2 ; erase good ;
  UpdateView à tous les viewers.
- **VendorClose** : efface goods + viewers ; par viewer op==0x2d →
  reset + `ClientVendorCloseView[sellerId]` + texte 0xf07 ;
  `+0x4dd`++ ; RemoveDo(0xe) → PlayerAction 5 broadcast + NotifyClient
  + `ClientVendorClose[charId][u8 param]` au vendeur.
- **Distance** : dist² ≤ **100** (DAT_087cc680, 10 unités), sinon 0x333.
- IsNoTrade (compteur +0x8c/+0xc4) : pas d'équivalent → jamais bloqué.

## Points d'édition

- [x] `SceneParser` : `Scene.StallRegions` (col7 600-609 →
      x=col1, y=col2, rayon=col4).
- [x] `Eden.Core/Game/Vending/StallService.cs` (nouveau) : stalls
      par vendeur, goods, viewers, rate-limit, validations pures.
- [x] `Eden.Zone/Codecs/VendorPackets.cs` (nouveau) : parse 71-80,
      builders 375/376/377/378/379/265.
- [x] `ZoneEntryFlow.Vending.cs` (nouveau partial) : orchestration
      open/view/buy/close + intégration EntryState (ShopOpType 0x2c/
      0x2d, Dead, Sitting, PosX/PosZ, NodeId, Gold).
- [x] `ZoneEntryFlow.cs` : cases 71,72,74,75,76,77,78,80 dans
      `TryHandleAsync`.
- [x] Supprimer `VendorHandlers.cs` + son `Register` (Program.cs).
- [x] Tests : parse wire, record 62o, validations, achat complet,
      distances, fermeture/reset viewers, no-ops natifs.
- [x] Master plan + hash.

## État

Implémenté : StallService (Eden.Core), VendorPackets
(codec 96o + parses natifs), ZoneEntryFlow.Vending (7 NCIDs),
TransferItemAsync (serial préservé), régions stall scène,
cleanup déconnexion, suppression VendorHandlers. 17 tests verts.
