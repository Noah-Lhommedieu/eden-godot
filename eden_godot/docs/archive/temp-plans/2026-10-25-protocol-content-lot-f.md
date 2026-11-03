# Plan — lot protocole/contenu §F (25 oct. 2026)

Objectif : avancer `docs/MASTER-PLAN.md` §F — inventaire NCIDs,
dialogues NPC, vitaux, équipement, shops, transport, sorts, banque.
Périmètre : `scripts/protocol/*`, `scripts/data/*`, dispatchs
`GameScene.cs` (ajout seul), `tools/NetTests/*`,
`docs/generated/NCIDS.md`. Serveur (`eden_server/`) en lecture seule.

## Livré

### F4 — `docs/generated/NCIDS.md`
- `tools/gen_ncids.py` : fusionne `ncid_map.json`, les `Opcode =>` des
  `.g.cs` (valeur wire — l'index map diffère pour login/ticket/world :
  table de commandes partagée du binaire), `FEATURE-MATRIX.md` et les
  codecs client `NetMessage` (`Opcode =>` + marqueur `C→S`/`S→C` du
  `<summary>`). 1344 entrées, 124 implémentés client (96 zone).
  `--check` pour CI.

### F2 — Dialogues NPC + shops
- `ZoneDialogueMessages.cs` : **285 `ZoneDisplayDialogView`**
  `[i32 ctx][s16 n][n×(i32 displayId,str,s16 flag)][u8 type][i32 npcUid]`
  (golden ncid_285.json) ; `DialogueState` pour le HUD
  (options/index → réponse = NCID 11 existant).
- `TextResolver.Dialogue` (t_dialogue.ini) ; fallback nom embarqué.
- `ZoneShopMessages.cs` : **304 `ZoneEnterShop`**, 308 `ZoneBuyUp` ;
  C→S **22/23/71/72** (wire ShopHandlers.cs — SellItems entrelacé) ;
  `ShopState` + `data/tables/StoreTable.cs` (C_Store.ini — triplets
  itemId/nom/count, prix via `ItemTableCache` c_item col31).

### F5 — Vitaux
- `ZoneVitalsMessages.cs` : **492 BaseAttrRefresh**, **493
  AttrLevelRefresh**, **328 ModifyAttr**, **305 Die**, **306 Revive**
  (goldens ncid_*.json) ; `PlayerStats` (mapping nommé du 296 via
  ProfileStatEmitter.cs) ; **`PlayerVitals`** : HP/MP/Max, XP (600),
  or (attr 2), XP classe (attr 6 p1 — désasm GainClassPoint), events
  `VitalsChanged/ExpChanged/ClassExpChanged/GoldChanged/StatsChanged/
  Died/Revived`.

### F6 — Équipement / sorts / banque / transport
- `NativeItemRecord.cs` : lecteur partagé des 3 variantes de
  item-records (Inventory WriteItem 34B ; EquipView 34B ;
  UpdateEquip 16B + bloc train bit21) — `NativeInventoryParser`
  refactoré dessus.
- `ZoneEquipMessages.cs` : C→S **8** `[u32 locMask]` ; S→C **282**
  UpdateEquipment + **276** ChangeEquip ; `EquipAndSpellParser`
  (311 : conteneurs bitmap + sorts u16) ; `PlayerEquipment` /
  `PlayerSkills` (noms T_Spell).
- `ZoneShopMessages.cs` : **393 EnterTransport** `[u16 n][n×s16][i32]`
  + `TransportState`/`TransportTable` (C_Transport coûts, T_Transport
  noms EN) ; **357/358** banque + `StorageState` (bitmap MSB-first +
  item-records) ; C→S **88** Transport, **15** CheckNodeExit,
  **20** Revive, **60/61** dépôt/retrait (62 existait).

### GameScene (dispatch ajouté seulement)
- Binds 285/492/493/328/305/306/282/276/304/308/393/357/358 ; 311 →
  parse réel ; 280/296/600 → `PlayerVitals` ; 293 NodeExit → ferme
  dialogue/shop/transport/banque ; 357 → auto-62.
- Helpers publics HUD : `SendDialogReplyAsync`, `BuyShopItemAsync`,
  `SellItemAsync`, `LeaveShopAsync`, `SelectTransportAsync`,
  `SendReviveAsync`, `QueryStorageAsync`, `SendCheckNodeExitAsync`,
  `SendUpdateEquipmentAsync`.
- `--talk-test` : MoveSelf+TalkToNpc sur les NPC en vue (live headless).

## Preuves
- Tests : **157/157** NetTests dont goldens réels 285/311/358/305/
  492/493/328/296 + encodes C→S.
- Live (`--login --channel --char --talk-test --nohud`) : TalkToNpc →
  **285 DialogView** ×3 (« Village Head » = **Andrew** ctx=4002 +
  option #4570 ; Madam 4003 ; Panicked 102294 + opt #102295 ;
  Dye Merch 4713) ; **304 EnterShop** ×2 (General Merchant store 3,
  5 articles ; Andrew store 46, 3 articles — C_Store résolu) ;
  **393 EnterTransport** (Warp Portal, 40 routes) ; 311 → 5 items
  portés + 141 sorts ; 358 → 1 onglet banque.
- Reste à couvrir en jeu : 282/276 (changement d'équipement en vue),
  305/306 (mort/respawn), 60/61 (dépôt banque réel).
