# E6 — Filtres AuctionBuyQuery (hôtel des ventes)

Suite de la phase E. `auction_history` est déjà résolu : table maison
archivée par migration 016 — le natif n'en a pas. `BuySpell` (52) et le
`ItemMallService` zone ont été neutralisés/retirés (Execute natif vide ;
vrai itemmall en monde via `tb_user`). Reste : les filtres du 114.

## Preuves natives (mission @ decompiled.c)

- `TC_AuctionSell` @0x0823dfd0 : clone auction reçoit
  `+0x88 = itemdef+0x84` (item_type), `+0x8a = itemdef+0x3e` (level),
  `+0x90 = itemdef+0x48` (class i64), `+0x94 = duedate arrondie 300 s
  + 0x15180`, `+0x98 = prix`.
- `CExecutionerDBO::SaveAuction` @0x0815da30 : INSERT `auction` —
  `class = clone+0x90`, `quality_filter = 1 << quality` (clone+0x44),
  `item_name = maker + nom + "+N"` (escape `\`/`'`, tronqué 64).
- `CItemQuery::Deserialize` (mission) : `+0x3e` = col14 (level),
  `+0x3f` = col15, `+0x48` = **col18** (i64 masque classes),
  `+0x78` = col30 (price_type, confirmé E3), `+0x84` = **col33**
  (item_type). Distribution c_item : col18 = puissances de 2 + combos
  (0 = sans restriction) ; col33 = 62 catégories (1..10000).
- `CExecutionerDBO::QueryAuctionBuy` @0x08160710 — SQL fidèle :
  ```
  FROM auction_view_by_{level|quality|duedate|price}_{up|dn}
  WHERE item_type = t
    AND auction_price >= pmin AND auction_price <= pmax
    AND level >= lmin AND level <= lmax
    AND (quality_filter & qmask) != 0
    AND (class & classMask64) != 0
    AND auction_duedate > now
    [AND item_name LIKE E'<pat>' — seulement si len(kw) != 4]
  LIMIT 20 OFFSET max(0,page)*20
  ```
  Masque classe : si les deux moitiés i32 = 0 → sentinelle
  `0x1fffffffffffffff` (passe-tout). Keyword : les 2 premiers et 2
  derniers octets sont les jokers envoyés par le client (`%`),
  le milieu est échappé (`\`→`\\`, `'`→`''''`, `_`→`\_`).
- `TC_AuctionBuyQuery` (mission) : ordre des pops = notre wire 114
  identique `[s16 lvlMin][s16 lvlMax][i32 prixMin][i32 prixMax]
  [i32 classMask][s16 qualityMask][s16 itemType][s16 tri][s16 desc]
  [str kw][s16 page]`. Tri : 0=level,1=quality,2=duedate,3=price.

## Adaptation wire (client Godot seul)

Notre client envoie tout à 0/vide. Sémantique native stricte → liste
vide. Adaptation assumée (pas de client natif) : **0/vide = pas de
filtre** pour levelMax, priceMax, classMask, qualityMask, itemType ;
keyword vide = pas de filtre, sinon `item_name` contient le texte
(au lieu du pattern `%kw%` pré-emballé + quirk len==4 — documenté).

## Points d'édition

1. `ItemParser.cs` : `ItemDef.ItemType` (col33 → CItemData+0x84),
   `ItemDef.ClassMask` (col18 → CItemData+0x48, `long`).
2. `Database/026-auction-denorm.sql` : `active_auctions` +=
   `item_type smallint`, `level smallint`, `class bigint`,
   `quality_filter smallint`, `item_name text` (défauts rétro-compat).
3. `AuctionEntity` + `AuctionService.AuctionListing` : mêmes champs.
4. `AuctionRepository` : INSERT + SELECT des nouvelles colonnes.
5. `InventoryService.DefOf(itemId)` : accès public à l'ItemDef.
6. `AuctionService` : `ListPersistedAsync`/`ListAsync` reçoivent un
   `AuctionDenorm` ; `Search(AuctionQuery)` natif + retourne le total
   (COUNT) ; tri 4 axes asc/desc ; page 20.
7. `ShopHandlers` : Sell → dénorm (ItemDef + item.Quality +
   `maker+name"+N"`) ; BuyQuery → parse msg → `Search` paginé ;
   `MakeBuyEntry` remplit `ItemName`.
8. `tests/Eden.Tests/AuctionFilterTests.cs` : filtres unitaires +
   pagination + tri + compat défauts.

## Non-objectifs

- Snapshot item complet natif (durability, combos, embedded… dans
  `auction`) — hors lot ; notre modèle template+quantité reste.
- Arbre de catégories client (Phase D/G) : le client enverra les
  vrais `item_type` quand l'UI les affichera.
