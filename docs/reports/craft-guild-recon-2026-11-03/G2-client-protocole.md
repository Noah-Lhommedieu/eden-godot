# G2 — Client Godot : cartographie protocole craft / guilde / work / shop / enchant / collecte

Date : 2026-11-03 · Portée : `eden_godot/godot_project/scripts/` · Lecture seule.
Statuts : **[VERIFIED]** = constaté dans le code cette session · **[GAP]** = attendu côté natif, absent/inerte côté client.

## 1. Routeur NCID → handler

- `scripts/session/EdenSession.cs:89/96` — `_zoneDispatch`/`_worldDispatch` : `Dictionary<int, List<Action<BinReader>>>`.
  Enregistrement : `OnZone(ncid, handler)` :100, `OnWorld(ncid, handler)` :113 ; dispatch : `DispatchReceived` :142 ;
  émission : `SendZoneAsync` :898, `SendWorldAsync` :903. [VERIFIED]
- **Bind générique « complétude »** : `protocol/ZoneMiscBinds.cs` `MiscPackets.BindZone` :30 (≈160 classes S→C zone)
  et `BindWorld` :230. Chaque trame est désérialisée → `MiscPackets.Latest` :16 + event `Received` :19.
  Consommateurs : `FeatureDialogWindow`, `LotteryWindow`, `ShopWindow`. [VERIFIED]
- **Binds dédiés** (parse + état) :
  - craft : `hud/CraftWindow.cs:103-140` (496/497/498/500/433 + cache 312) ;
  - vente joueur : `hud/VendingWindow.cs:117-144` (375/376/377/379) ;
  - échange : `hud/TradeWindow.cs:98-193` (331-340,353,394-395) ;
  - guilde : `hud/GuildWindow.cs:493` (346) + pompe monde `hud/SocialWindow.cs:413-503` (50-80) ;
  - sac/boutique : `hud/HudExtensions.cs:305-365` (312/274/275/291/375-378), `BindEntryData` :430-477
    (**collection 472** :452, **lifeskill 543** :465) ;
  - enchants : `hud/BuffBridge.cs:91-132` (299/300/301/315/372/445/446) ;
  - entrée boutique : `GameScene.cs:2997` (304), `:2417` (615), storage famille via `FamilyStorageWindow`.

## 2. Table NCID → classe → direction → handler → état

### 2.1 Craft / ordres de travail (métiers 12-17)

| NCID | Classe (`fichier:ligne`) | Dir | Émetteur / handler | État |
|---|---|---|---|---|
| 496 | `ZoneEnterWorks` `ZoneCraftMessages.cs:14` | S→C | `CraftWindow.cs:103` → `OpenWorks` | [VERIFIED] corps `[i32 npc][s16 workType][s16 bld][s16 ?][u16]` |
| 497 | `ZonePlayerWorkUpdate` `:45` | S→C | `CraftWindow.cs:109` → `_running` | [VERIFIED] `WorkOrder{WorkNum,WorkType,WorkId,EventId(enchant 200+num),FmName}` |
| 498 | `ZonePlayerWorkResult` `:98` | S→C | `CraftWindow.cs:115` | [VERIFIED] `WorkId<0`=échec, `Products: WorkProduct{ItemId,Amount,SocketAmount,Combo,ExtraCombo×9}` `:91` — **combos/enchants produits conservés** |
| 500 | `ZonePlayerWorkResultEnd` `:149` | S→C | `CraftWindow.cs:121` | [VERIFIED] `WorkNum` |
| 433 | `ZoneStrengthenCasting` `:161` | S→C | `CraftWindow.cs:132` → barre cast | [VERIFIED] `WorkType,DurationUnits(×100ms)` |
| 170 | `ZonePlayerWork` `:186` | C→S | `CraftWindow.cs:225` (`OnCraftPressed`) | [VERIFIED] `WorkId,CatalystBag/Slot,BoostBag/Slot` |
| 171 | `ZonePlayerWorkResultOK` `:215` | C→S | `CraftWindow.cs:263` (`OnClaimPressed`) | [VERIFIED] `WorkNum` |
| 172 | `ZoneServerPlayerWorkCheckData` `ZoneClientToServerMessages.cs:695` | C→S | — aucun appelant | **[GAP]** classe corps vide, jamais émise (protocol-map dit « client émet » — faux) |

### 2.2 Renforcement / sertissage / identification

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 432 | `ZoneClientEnterStrengthen` `ZoneMiscMessages.cs:1788` | S→C | `MiscPackets` + `FeatureDialogWindow.cs:104` | [VERIFIED] champs `Field1-5` **non décodés** |
| 434 | `ZoneClientStrengthenItemCancel` `:1821` | S→C | MiscPackets seul | [VERIFIED] `Field1` non décodé |
| 435 | `ZoneClientStrengthenItemResult` `:1842` | S→C | MiscPackets + `FeatureDialogWindow.cs:107` (chat brut) | [VERIFIED] `Field1-6` non décodés |
| 436 | `ZoneClientModifyItemStrengthen` `:1878` | S→C | MiscPackets seul | [VERIFIED] `Field1-5` non décodés |
| 437 | `ZoneClientSetSpellStone` `:1911` | S→C | MiscPackets seul | [VERIFIED] `Field1-3` non décodés |
| 119 | `ZoneServerStrengthenItem` `ZoneClientToServerMessages.cs:226` | C→S | — | **[GAP]** `Field1-9`, jamais émis — pas d'UI enclume |
| 120 | `ZoneServerStrengthenClose` `:268` | C→S | — | **[GAP]** `Value`, jamais émis |
| 370/371 | `ZoneClientEnterInlayShop` / `ZoneClientInlayItemResult` `ZoneMiscMessages.cs:1212/1233` | S→C | MiscPackets | [VERIFIED] parse seul ; **C→S 67 InlayItem : pas de classe** [GAP] |
| 363-365 | `ZoneClientAskIdentifyGold` / `IdentifyItem` / `EnterIdentifyShop` `:890/920/968` | S→C | MiscPackets | [VERIFIED] parse seul ; **C→S 64/65 : pas de classe** [GAP] |

### 2.3 Enchants (buffs/debuffs — sens « enchantement d'effet »)

| NCID | Classe `ZoneEnchantMessages.cs` | Dir | Handler | État |
|---|---|---|---|---|
| 299 | `ZoneEnchantAdd` `:33` | S→C | `BuffBridge.cs:91` | [VERIFIED] `Handle,EffectId,RestSeconds,StackCount,Level…` |
| 300 | `ZoneEnchantRemove` `:86` | S→C | `BuffBridge.cs:98` | [VERIFIED] |
| 301 | `ZoneEnchantUpdate` `:128` | S→C | `BuffBridge.cs:105` | [VERIFIED] `Entries[]{EffectId,CasterId,RestSeconds,Level}` |
| 315 | `ZoneEnchantReset` `:204` | S→C | `BuffBridge.cs:112` | [VERIFIED] |
| 372 | `ZoneTeamMemberEnchant` `:233` | S→C | `BuffBridge.cs:119` | [VERIFIED] |
| 445/446 | `ZoneEnchantPause`/`Start` `:294/:327` | S→C | `BuffBridge.cs:125/132` | [VERIFIED] |
| 18 | `ZoneEnchantUpdateRequest` `:364` | C→S | `BuffBridge.cs:169` | [VERIFIED] `TargetHandle` |
| 85 | `ZoneEnchantCancel` `:382` | C→S | `BuffBridge.cs:146` | [VERIFIED] `EffectId` |
| 127/128 | `ZoneEnchantPauseRequest`/`StartRequest` `:397/:412` | C→S | — | **[GAP]** `EffectId`, jamais émis |

### 2.4 MagicBox / Cube / loterie

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 612 | `ZoneClientOpenMagicBoxDialog` `ZoneMiscMessages.cs:6042` | S→C | MiscPackets | [VERIFIED] corps vide |
| 613 | `ZoneClientMagicBoxResult` `:6062` | S→C | MiscPackets | [VERIFIED] `Field1-11` non décodés |
| 257 | `ZoneServerExecuteMagicBox` `ZoneClientToServerMessages.cs:1627` | C→S | — | **[GAP]** codec réel corrigé (`Type,Sacrifices,MainBag/Slot,SelectedCombos,MallBag/Slot`) mais jamais émis |
| 572 | `ZoneClientLoadCubeItems` `ZoneComplexMessages.cs:304` | S→C | MiscPackets | [VERIFIED] `ProdBags/ProdSlots/ProdQtys` |
| 232 | `ZoneServerVerifyCubeCosts` `ZoneClientToServerMessages.cs:1348` | C→S | — | **[GAP]** `Field1-7`, jamais émis |
| 230 | CNE EquipTrainSetItem | C→S | — | **[GAP]** pas de classe |
| 448-449/511 | loterie `ZoneMiscMessages.cs:2088+` | S→C | `LotteryWindow` | [VERIFIED] `Field*` non décodés ; C→S 129 `ZoneServerGoLottery` émis `LotteryWindow.cs:120` |

### 2.5 LifeSkill / collecte (sens « métier de récolte »)

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 543 | `ZonePlayerLifeSkill` `ZoneEntryDataMessages.cs:682` | S→C | `HudExtensions.cs:465` → `PlayerProfileState` « lifeskill » | [VERIFIED] `RecipeIds/Exps/Dues` |
| 544 | `ZoneClientLifeSkillUpdate` `ZoneMiscMessages.cs:4557` | S→C | MiscPackets | [VERIFIED] `Field1 u8/Field2 u16/Field3 u32` non décodés |
| 581 | `ZoneClientLifeFloat` `:5327` | S→C | MiscPackets | [VERIFIED] `Field1-5` non décodés |
| 213 | `ZoneServerQueryLifeSkill` `ZoneClientToServerMessages.cs:1159` | C→S | — | **[GAP]** corps vide, jamais émis (map dit « client émet » — faux) |
| 214 | `ZoneServerLifeSkill` `:1177` | C→S | — | **[GAP]** `Field1-4`, jamais émis |

### 2.6 Boutique NPC / vending / enchères / banque

| NCID | Classe | Dir | Émetteur/handler | État |
|---|---|---|---|---|
| 304 | `ZoneEnterShop` `ZoneShopMessages.cs:204` | S→C | `GameScene.cs:2997` | [VERIFIED] `NpcUid,StoreId` |
| 308 | `ZoneBuyUp` `:234` | S→C | `HudExtensions`/`ShopState` | [VERIFIED] |
| 309 | `ZoneClientSellOut` `ZoneMiscMessages.cs:259` | S→C | MiscPackets | [VERIFIED] |
| 512 | `ZoneClientBuyBackList` `:3471` | S→C | MiscPackets | [VERIFIED] `Field1-20` non décodés ; **C→S 185 BuyBackOpen sans classe** [GAP] |
| 22/23 | `ZoneBuyItems`/`ZoneSellItems` `ZoneShopMessages.cs:23/49` | C→S | `GameScene.cs:698`, `ShopWindow` | [VERIFIED] `ShopIndex,ItemIndex,BuyBack` / triades slot,count,container |
| 342/343→45-48 | repair `ZoneClientToServerMessages.cs:91-139` | C→S+S→C | `ShopWindow` boutons fixAll/fixOne | [VERIFIED] |
| 375-379 | `ZoneClientVendorClose/CloseView/Status/UpdateView/Container` `ZoneVendingMessages.cs:170-403` | S→C | `VendingWindow.cs:117-144`, `HudExtensions.cs:344-365` | [VERIFIED] `VendorGood` record complet (:236) |
| 71 | `ZoneAskVendor` `ZoneShopMessages.cs:76` | C→S | — | **[GAP]** classe existe, jamais émise |
| 72 | `ZoneAskVendorContainer` `:87` | C→S | `VendingWindow.cs:196`, `GameScene.cs:2246` | [VERIFIED] |
| 74-77 | `ZoneVendorClose/CloseView/Buy/SellItem` `ZoneVendingMessages.cs:44-119` | C→S | `VendingWindow`/`GameScene.cs:2232-2275` | [VERIFIED] |
| 78/80 | VendorCloseVending / VendorUseItem | C→S | — | **[GAP]** pas de classe |
| 114-118 | `ZoneAuctionBuyQuery/Buy/SellQuery/Sell/Cancel` `ZoneAuctionMessages.cs:42-133` | C→S | `AuctionWindow.cs:371-408` | [VERIFIED] |
| 425-430 | `ZoneAuctionBuyList/BuyOk/CancelOk/SellList/SellResult/AuctionHouse` `:170-270` | S→C | `AuctionWindow` (via `MiscPackets` + lecteurs) | [VERIFIED] `AuctionEntry{AuctionId,ItemId,Price,ItemName,ExpireTime,SellerId}` — **pas de stats item** |
| 357-359,60-62,69,373 | storage banque `ZoneShopMessages.cs:272/292` + `ZoneStorageMessages.cs` | C→S+S→C | `BankWindow.cs`, `NeobankWindow.cs` (réutilise 60/61 — `ZoneNeobankMessages.cs` = helpers seuls, **aucun NCID**) | [VERIFIED] |

### 2.7 Guilde / famille (monde + zone)

| NCID | Classe | Dir | Émetteur/handler | État |
|---|---|---|---|---|
| 10 | `WorldQueryFamilyInfo` `WorldMessages.cs:68` | C→S | `GuildWindow.cs:508`, `SocialWindow.cs:519` | [VERIFIED] |
| 9 | `WorldFamilyOp` `ZoneSocialMessages.cs:1079` + `FamilyOps` :1048-1070 | C→S | `GuildWindow.cs` (:543 Invite, :585 Op, :592 SetCadre, :633 Accept/Reject, :656/:667 board, :675 RecruitJoin, :195/:203 Quit/Disband, :211 SetRecruit, :315 EditInfo, :369 RecruitPage) | [VERIFIED] — **map « orphelin » obsolète** |
| 73 | `WorldQueryFamilyEmblem` `:1129` | C→S | — | **[GAP]** jamais émis |
| 46-51,60,67,68,74,80 | `WorldFamilyMemberUpdate/OpException/Invitation/Quit/Info/Schedule/Prestige/Achievement/LogUpdate/Emblem/RecruitList` | S→C | `SocialWindow.cs:413-503` → `GuildState` | [VERIFIED] `WorldFamilyInfo` :630 types 0/5/8 testés |
| 49,81,82,159 | `ZoneFamilyCreate/UpdateFamily/SetFamilyEmblem/FamilyDonateMoney` `:1621/:1635/:1648/:1669` | C→S | `GuildWindow.cs:553/500/569/562` | [VERIFIED] |
| 346 | `ZoneClientChangeFamily` `:1684` | S→C | `GuildWindow.cs:493` | [VERIFIED] |
| 347/384 | `ZoneClientEnterFamilyCreation/SelectFamilyEmblem` `ZoneMiscMessages.cs:505/1326` | S→C | MiscPackets | [VERIFIED] parse seul |
| 565 | `ZoneClientR25UpdateFamily` + `ZoneR25UpdateFamily` `:4973/1729` | S→C | MiscPackets | [VERIFIED] |

### 2.8 Famille — coffre / ville / bâtiment / conteneurs

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 507/508 | `ZoneEnterFamilyStorage`/`ZoneFamilyStorageData` `ZoneStorageMessages.cs:132/222` | S→C | `FamilyStorageState` + `FamilyStorageWindow` | [VERIFIED] record fixe 93B `FamilyStorageItemCodec` :260 |
| 504-506 | `ZoneFamilyStorageDelete/PutNotify/Log` `:146/:169/:196` | S→C | `FamilyStorageState` | [VERIFIED] |
| 177-179 | `ZoneFamilyStoragePut/Get/QueryLog` `:44/:75/:105` | C→S | `FamilyStorageWindow.cs:218-323`, `GameScene.cs:1699-1723` | [VERIFIED] |
| 483-486 | `ZoneClientEnterFamilyBuilding`/`FamilyContainerData`/`DeleteItem`/`PutItem` `ZoneMiscMessages.cs:2922-3027` | S→C | MiscPackets | [VERIFIED] `Field*` non décodés ; **C→S 130/131 existent (`ZoneClientToServerMessages.cs:319/349`) mais jamais émis** [GAP] |
| 487/501 | `ZoneClientFamilyInstanceData`/`EnterFamilyInstanceCreation` `:3078/:3219` | S→C | MiscPackets | [VERIFIED] parse seul ; C→S 132/173 jamais émis [GAP] |
| 502/503/514 | `ZoneClientFamilyTownQuery`/`MemberQuery`/`EnterFamilyTownList` `:3243/:3360/:3627` | S→C | MiscPackets | [VERIFIED] `Field1-20` non décodés ; C→S 174-176 jamais émis [GAP] |
| 561/562/566 | `ZoneClientFamilyBattleInfo`/`KilledRank`/`TerritoryFamilyScore` `:4886/+` | S→C | MiscPackets | [VERIFIED] parse seul ; C→S 225 jamais émis [GAP] |
| 161 | `ZoneServerFamilyStatue` `ZoneClientToServerMessages.cs:550` | C→S | — | **[GAP]** |
| 180/239/240 | FamilyBuildingRepair / FamilyTalkToNpc / FixEquipByFamily | C→S | — | **[GAP]** pas de classe |

### 2.9 Collection (archive/cartes)

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 472 | `ZoneUpdateCollection` `ZoneEntryDataMessages.cs:555` | S→C | `HudExtensions.cs:452` → `PlayerProfileState` « collection » | [VERIFIED] `BookId,U1,CardIds[]` |
| 150/151/152 | `ZoneServerSaveCollection`/`GetCollection`/`QueryCollection` `ZoneClientToServerMessages.cs:451/:487/:505` | C→S | `ArchiveWindow.cs:246/:251/:258` | [VERIFIED] Save=`Field1-7`, Get=`Value`, Query=vide |

### 2.10 Elf (travail elfe — voisin du craft)

| NCID | Classe | Dir | Handler | État |
|---|---|---|---|---|
| 415/416/421 | `ZoneClientElfWorkResult`/`End`/`GoToWork` `ZoneMiscMessages.cs:1539/:1581/:1602` | S→C | MiscPackets + `ElfBridge` | [VERIFIED] |
| 107 | `ZoneElfWork` `ZoneElfMessages.cs:41` | C→S | `ElfBridge.cs:262-271` | [VERIFIED] `Loc,Mode(0 repos/1 mix/2 invoke)` |
| 111/112 | ElfWorkResultOK / ElfCheckData | C→S | — | **[GAP]** pas de classe |

## 3. Encodage des champs item (enchant / strengthen / sockets)

| Record | `fichier:ligne` | itemId | dur | amount | **strengthen** | **enchant ids (combos)** | sockets | autres |
|---|---|---|---|---|---|---|---|---|
| `ItemRecord` | `NativeItemRecord.cs:46` | u16 | s16×2 | s16 bit6 | s16 bit19 (inv) / bit18 (equip) | **lues puis jetées** (`ReadConditional` :250-268) | u8 flags | mask u24 conservé brut ; due_date/train skippés ; queue fixe 34B (16B pour 282, avec 3×s16 magicbox :25) |
| `InventorySlot` | `ZoneHudMessages.cs:372` | ✓ | ✓ cur/max | ✓ | ✓ | **non stockés** | flags u8 | état sac `PlayerBag.cs:28` (312/274/275/291) |
| `FamilyStorageItemCodec` | `ZoneStorageMessages.cs:260` | ✓ | ✓ | ✓ emb_amount | ✓ s16 +0x2c :288 | **lues puis jetées** (6×s16 embedded :281, combo+9×u16 :286-287, 3×s16 mb_ench :298, ec10/11, gold_ench, mb_energy/star) | flags u8 | record FIXE 93B + maker |
| `VendorGood` | `ZoneVendingMessages.cs:236` | u32 | u16 | EmbeddedAmount | `Quality` = s16 +0x2c :269 | **conservés** : `Combo` :260, `EmbeddedIds[6]` :261, `ExtraCombos[9]`+`Extra9/10` :278 | FlagLow3 | `RequiredLevel` :243, `TrainLevel/Slots`, `DueDate`, `MagicBox[3]`/`Energy`/`Star` :283-285 — **record le plus complet** |
| `WorkProduct` | `ZoneCraftMessages.cs:91` | i32 | — | i32 | — | **conservés** : `Combo` + `ExtraCombo×9` | `SocketAmount` | produit d'ordre craft |
| `AuctionEntry` | `ZoneAuctionMessages.cs:149` | i32 | — | — | — | **absents** | — | `Price,ExpireTime,SellerId` seuls |
| Enchant (buff) | `ZoneEnchantMessages.cs` | — | — | — | — | `EffectId` (id c_enchant) | — | `RestSeconds,StackCount,Level,CasterId` |

**Constat clé** : le pipeline inventaire (274/291/312/358/508) **décode la longueur des combos mais jette leurs valeurs** — un item de sac ne peut pas afficher ses enchantements dans le tooltip (`InventorySlot` n'a pas de champ). Seuls vending (378/379), résultats craft (498) et le stockage famille côté fil (mais valeurs non conservées) transportent les ids d'enchant. [VERIFIED]
Pas de champ `required_level` côté sac (donnée table c_item) ; `VendorGood.RequiredLevel` est le seul niveau requis wire. [VERIFIED]

## 4. Couverture `tools/NetTests`

- Work/craft : `ZoneLotS6S8Tests.cs:147-241` — EnterWorks, PlayerWork, ResultOK, WorkUpdate, Result (échec/produits), ResultEnd, StrengthenCasting. [VERIFIED]
- Enchants : `ZoneVague2Tests.cs:41-159` — Add/Remove/Update/Reset/TeamMember/Pause/Start + requêtes C→S (18/85/127/128). [VERIFIED]
- Guilde : `ZoneSocialTests.cs:224-721` — FamilyInfo (types 0/5/8), FamilyOp (Invite/Kick/SetCadre/AddMessage), Emblem, RecruitList, ZoneCreate/Update/Emblem/Donate, ChangeFamily, GuildState. [VERIFIED]
- Coffre famille : `ZoneStorageTests.cs:21-268` — wires C→S, records 93B (assert Strengthen :209), FamilyStorageState. [VERIFIED]
- Shop : `ZoneServicesTests.cs:268-287` (BuyItems/SellItems) ; inventaire : `ZoneHudTests.cs:121-197` (mask, amount, strengthen). [VERIFIED]
- **Non testés** : vending wire 375-379, auction 114-118/425-430, MagicBox 612/613/257, Cube 572/232, collection 472/150-152, lifeskill 543/544/213/214, strengthen 432-437/119/120, PlayerWorkCheckData 172, town/building 502-503/173-176/483-487. [GAP]

## 5. Écarts avec `docs/knowledge/protocol-map.md`

- 9 `WorldFamilyOp` taggé « orphelin » → **émis** (`GuildWindow.cs`, 15+ opcodes FamilyOps). [VERIFIED]
- 172 `ZoneServerPlayerWorkCheckData` et 213 `ZoneServerQueryLifeSkill` taggés « client émet » → **aucun call site** (`grep new …` = 0 référence hors définition). [VERIFIED]
- 92 `ZoneTradeUnConfirm` « orphelin » confirmé ; 71 `ZoneAskVendor` confirmé orphelin. [VERIFIED]

## 6. GAPs consolidés

1. **Émetteurs morts** (classe C→S définie, jamais envoyée) : 119/120 strengthen, 127/128 enchant pause/start, 130/131/132 family container+instance, 161 statue, 172 work-check, 173/174/175/176 family town, 213/214 lifeskill, 225 family battle, 232 cube, 257 magicbox, 92 trade-unconfirm, 71 ask-vendor, 56 trade-reset, 73 family emblem, 9? non (9 émis).
2. **NCID C→S natifs sans aucune classe** : 64/65 identify, 67 inlay, 78 vendor-close-vending, 80 vendor-use-item, 105/106 elf-stone, 109 elf-call (classe existe : `ZoneElfCallElf`, non émise), 111/112 elf-work/check, 180 family-building-repair, 185 buyback-open, 230 equip-train, 239/240 family-talk/fix-equip, 52 buy-spell.
3. **S→C parsés mais non décodés** (`Field1..N` génériques, aucune sémantique) : strengthen 432/434/435/436/437, MagicBoxResult 613, LifeSkillUpdate 544, LifeFloat 581, FamilyContainer 484/485/486, FamilyTown 502/503, BuyBackList 512, FamilyBattleInfo 561. Consommés seulement comme stubs (`FeatureDialogWindow`).
4. **Enchant ids perdus sur le fil inventaire** : combos/embedded/magicbox lus pour la longueur mais non stockés (`ItemRecord`/`InventorySlot`/`FamilyStorageItemCodec`) → tooltips sac sans lignes d'enchant, achat vendeur sans comparaison possible côté sac. [VERIFIED]
5. **Aucune UI métier** au-delà de `CraftWindow` : pas de fenêtre Strengthen/MagicBox/Cube/LifeSkill/BuyBack dédiée — les S→C d'ouverture tombent sur des stubs génériques, et les C→S correspondants sont tous orphelins (cohérent : l'UI n'existe pas).
6. **Neobank** : `ZoneNeobankMessages.cs` = settings/locks locaux seulement (aucun NCID) ; `NeobankWindow` réutilise 60/61 — attendu par design (`NeobankWindow.cs:15`).
