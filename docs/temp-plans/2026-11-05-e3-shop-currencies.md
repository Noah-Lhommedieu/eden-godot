# 2026-11-05 — E3 : monnaies typées des fame shops

## Preuve native

`lapis::CItemFactory::BuyItems` (zone @ 0x080aca40) : après les gardes
boutique et le check de slots libres (texte 0x4f9), le flux branche sur
`sVar31` = `u32(GameData::CItemData+0x78)` — le **priceType** de l'item,
col 30 (0-indexée) de `S_Item.ini` (lecture confirmée dans
`GameData::CItemQuery::Deserialize` @ 0x08602cf0 : colonne lue dans le
scratch `query+0x20` puis recopiée en `item+0x78`, vide → 0).

Switch des soldes (check zone, puis débit) :

| priceType | bourse | zone | mission (TC) |
|---|---|---|---|
| 1 | or (+ taxe famille `FamilyLevyBusinessTaxes`) ; ids 40001-52999 → crystal (char+0x4bc, cap 1e5) | oui | — |
| 2 | prestige 0x51 (`GetPrestigeExp`/`AddPrestigeExp`) | oui | — |
| 4 | lover coin — check « a un lover » (char+0xa8c) | — | `BuyLoverItem` → `CLover::GainCoin` |
| 5 | lifelong gold (char+0xc54/0x75c, CCM) | — | `BuyClassLifelongItem` |
| 6 | consomme item 34928 (`CheckItemNum`/`DropItemByItemID`) | oui | — |
| 0xa | trésor famille — check « a une famille » (char+0x5c) | — | `BuyFamilyItem` → `CFamily::DonateMoney` |
| 0xb | or colisée (char+0xbbc, cap 999999999) | oui | — |
| 0xc-0x11 | special_coins types 0-5 | — | `BuySpecialCoinItem` |
| 0x13 | war coin | — | `BuyWarItem` → `GainWarCoin` |
| 0x14 | alpaca coin | — | `BuyAlpacaItem` → `GainAlpacaCoin` |
| 0x15-0x17 | special_coins types 9-11 | — | `BuySpecialCoinItem` |
| autre (0, 100, 999…) | — | refus (return 0) | — |

`GetCoinTypeByPriceType` (@ 0x8409e40 zone, miroir
`CGlobalInfo::GetSpecialCoinType` @ 0x08072280 mission) :
0xc..0x11 → 0-5, 0x15..0x17 → 9-11, sinon -1.

Le natif débite en 2 temps pour les TC : la zone envoie
`CNC_DBA_AgentDBACommand` à la mission qui vérifie+débite puis
rappelle la zone (`param_4=true` → octroi + notifications). Notre zone
détenant toutes les bourses, le check+débit se fait en une passe —
résultat observable identique.

## Données EDENFR (c_item.ini ∩ c_store.ini, ~4900 items vendus)

1=or 3127 · 5=lifelong 621 · 0xb=colisée 402 · 0xc-0x17=special coins
378 · 0xa=famille 51 · 0x13=war 25 · 0x14=alpaca 40 · 4=lover 22 ·
'' (→0, refusés) 244 · 100/999 (refusés) 3. **Aucun** item prestige(2)
ni item-34928(6) ni crystal-range — ces branches restent implémentées
seulement là où la donnée existe.

## Implémentation (fait)

- `ItemParser.ItemDef.PriceType` : col30 → byte, défaut propriété 1
  (or) — le parseur pose 0 sur vide → refus, fidèle au natif.
- `ShopService.ShopCurrencyPort` record
  (`GetBalance`→solde|null non géré, `Debit`) + branche typée dans
  `BuyItemAsync` (param optionnel → compat tests) : pt≠1 → check
  solde → octroi → débit ; pt 0/null → refus.
- `CharacterRepository` : `GetShopCoinBalancesAsync` (colosseum/war/
  alpaca en une requête), `AddWarCoinAsync`, `AddAlpacaCoinAsync`
  (clamp [0,9999999], miroir MissionDb @0x8081490/0x8081160),
  `AddSpecialCoinAsync` (INSERT+UPDATE coin{type+1} [0,9999999],
  miroir CDBAgent::GainSpecialCoin @0x80863f0).
- `ShopHandlers` : `BuildCurrencyPort`/`CurrencyBalanceAsync`/
  `CurrencyDebitAsync` — bourses : LifelongRepo (5),
  Guilds.TrySpendTreasuryAsync (0xa), AddColosseumGoldAsync (0xb),
  AddSpecialCoinAsync (0xc-0x17), AddWarCoinAsync (0x13),
  AddAlpacaCoinAsync (0x14).
- Notifications client : 555 lifelong (via callback boucle
  `LifelongGoldDelta` → miroir `EntryState.LifelongGold`), 576 war,
  580 alpaca, ZW 30→78 push complet special_coins. Colisée : refresh
  au prochain 525 (pas de push dédié). Famille : texte broadcast 0xa73
  natif omis (cosmétique).

## Gaps documentés

- **priceType 4 lover** : 22 items EDENFR, aucun stockage couple-coin
  (`player_couples` n'a pas de colonne coin) → refus propre, consigné
  `docs/workflows/EDENFR-GAPS.md`.
- priceType 2/6, crystal (ids 40001-52999) : aucun item vendu dans
  EDENFR → refus natif-fidèle.
- Broadcast famille 0xa73 : non émis.

## Preuves

- `dotnet build` zone : 0 erreur / 0 warning.
- Tests : 6 nouveaux `ShopServiceTests` (débit, solde insuffisant,
  monnaie non gérée, port nul, or non touché) + `ItemParserTests.
  PriceType_Col30` (items réels 19→5, 57→0xb, 304→0x13, 34216→4).
- Reste à prouver : achat live sur chaque boutique typée (client).

## Validation live boutique (2026-11-05, ClericQA / Alec store 3)

**Bug trouvé** : désynchronisation des tables store — le client indexe
`c_store.ini` (3 articles pour store 3) après un 304 qui ne porte que
le storeId, le serveur indexait `S_Store.ini` (5 articles) → slot 1
affichait la potion 32631 mais le serveur résolvait 20021 à 100 000 cu.
**Fix** : `GameDataService.OverlayFrStores` + appel au démarrage Zone
(overlay `EDEN_REFORGED_DB` → `corpus/fr-full/data/db`, repli table
native si absent). Log : `boutiques : 659 stores c_store EDENFR`.

**Bugs UI corrigés** :
- `ItemSlotCell` sans ctor sans paramètre → `Duplicate()` plantait,
  lignes de shop jamais créées.
- Titres d'onglets pris sur le nom de nœud au lieu du `Text` CEGUI
  (« 商 品 »/« 買回 » → traduits « Objets »/« Racheter »).
- Enfants de `FrameWindow` positionnés à {0,0} sous la titlebar
  auto-générée → bande d'onglets invisible. Fix : hôte « zone client »
  décalé de la hauteur du titre dans `CeguiToGodot.BuildTree`.

**Prouvé live** (client réel, `flock`, captures flameshot) :
- 304 → store 3 = 3 articles identiques client/serveur.
- Achat slot 1 : `BuyItem -> ok`, or 5000→4977 (23 cu), 32631 en sac.
- Vente : NCID 23, 512 remplie, or 4977→4978.
- Onglets cliqués en entrée réelle (`@tabclick` au centre du rect du
  TabBar) : « Racheter » affiche « Minuscule Potion de Soins » vendue ;
  « Objets » restaure la liste. `BuyBackOpen -> 1 items`.
- Rachat : `BuyBack index=0 -> ok`, 512 vidée, 32631 de retour, or 4977.
- Bouton X (`__auto_closebtn__`) fermé en clic réel (`@uiclick`).
- Capture finale : `eden_godot/godot-shots/shop-tabs-final.png`.

**Notes** : prix 23 cu → « 0 0 » or/argent fidèle (le layout natif n'a
pas de champ cuivre). Placeholder « (Nothing to buy back) » reste en
anglais et blanc-sur-clair — cosmétique. Warning `move_child()` dans
`StrengthenWindow.Ready` observé, sans impact connu.

Canal diagnostic : `GmFileProbe` `--cmd-file=/tmp/gm-cmds.txt` —
`@click/@uiclick/@tabclick/@hit` (input réel), `@talk/@dlg/@buy/@sell/
@buyback/@inv/@tab` (wire/UI). `@click` = coords viewport physiques
(pas de stretch → pixels fenêtre).
