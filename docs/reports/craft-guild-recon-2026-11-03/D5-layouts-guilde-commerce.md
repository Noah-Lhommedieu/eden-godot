# D5 — STRUCTURE UI — layouts guilde(FAMILY)/VDG/commerce/social (corpus/fr-full/uidata/layouts/)

Statuts croisés avec `docs/reference/corpus/layout-impl-map.md` : **[IMPL]** implémenté / **[PART]** partiel / **[ABS]** absent. Même convention que D4 : `{{sx,px},{sy,py},{sw,pw},{sh,ph}}` = fraction + offset ; types abrégés (FrameWin, DefWin, Static, Slot, STxt/HTxt/ETxt/TxtS/BTxt, Btn/IBtn, Edit/MLEdit, MList, LBox, Scroll, CB/RB, Combo, PBar, SImg, Tabs). Note : ces `.layout` ont des fins de ligne mixtes CR/CRLF — les `:N` cités suivent le découpage « toute fin de ligne » (Python/éditeur), `wc -l`/`grep -n` compte ~8 % de lignes en moins. Aucun `Property Event` (cf. D4) : bindings en code natif ; les MList n'ont **pas** de colonnes dans le XML (définies au runtime).

**Guilde = « 城鎮 » (ville) dans les strings TW** : FamilyWnd = fenêtre guilde, FamilyBuildingSetWnd = gestion bâtiments VDG, FamilyBattle = tournoi de guildes.

---

## GUILDE — coquille & onglets

### familywnd.layout — [IMPL] (`GuildWindow.cs`, coquille onglets reproduite)
`Root/FamilyWnd` FrameWin `:4` rect `{{0.091,0},{0.023,0},{0.091,776},{0.023,681}}` minsize `{0,658}×{0,567}` — **uniquement** `TabControl` `:11` plein cadre TabH 20.7px : les pages (FamilyInfo, FamilyOrder, FamilyMessage, …) sont des `.layout` séparés injectés par le code.

### familywnd_na.layout — [IMPL] — identique, fenêtre 776×**684** `:4`, même TabControl `:11`

### familyinfownd.layout — [PART] (variante TW — Godot charge `_na`)
`Root/FamilyInfo` DefWin `:4` `{0,634}×{0,500}` (page onglet « info »)
- `NameBackGrand` Static `:10`
  - `FamilyNameText` STxt `:12` «阿卡貝拉友愛之鎮»
  - `BorText` BTxt `:20` → `BulletinEdit` MLEdit `:23` (annonce) + `Bulletin` `:46` «城鎮公告：»
  - `HelpBtn` IBtn `:55`
  - `FamilyBor` Border `:63` : `FamilyLevel` HTxt `:66` «等級：» / `FamilyLeader` `:74` «鎮長：…» / `FamilyMember` `:82` «人口：300/300，300位線上» / `Reputation` `:90` «聲望：999999/999999» / `GoodCommentText` `:98` «好評：» / `GoldText` `:106` «資金：» → `GoldValue` `:113`+`GoldImage` `:131`, `SilverValue` `:122`+`SliverImage` `:139` / `MemberTax` `:148` «鎮民稅率：100％» / `BusinessTax` `:156` «營業稅率：» ; `EnterTerritoryBtn` `:164` «進入領地» / `FamilyListBtn` `:170` «城鎮列表»
- `ListBackgrand` Static `:178` : `ListBox` **MList** `:180` (membres) ; `NoticeBtn` `:184` «編輯公告» / `RecruitBtn` `:190` «招攬人員» / `DonateBtn` `:196` «捐獻金錢» / `LevelUpBtn` `:202` / `EmblemBtn` `:208` / `EmblemTestBtn` `:214` / `EmblemMark` SImg `:220` + `EmblemBlock` `:225` (blason) / `ExitBtn` `:230` «退出公會» / `ListTitle` `:236` «會員清單» / `OnlineBtn` CB `:244` «顯示離線鎮民» / `DisbandBtn` `:258` «解散公會» / `CmtNew` `:264` «招募新人»

### familyinfownd_na.layout — [IMPL] (`GuildWindow.cs` → FamilyInfoWnd_NA.layout)
Même structure `{0,760}×{0,622}` `:4` ; différences : `FmailyFix` IBtn `:174` (sic), EnterTerritory **après** FamilyList `:182/:187`, **+ `FamilyMission02` `:285` «公會任務-副» et `FamilyMission01` `:290` «公會任務-野»** (boutons quêtes de guilde, spécifiques NA).

### cnfamilyinfownd.layout — [PART] — variante CN `{0,760}×{0,622}` `:4`
Différences : `NoticeBtn` «編輯公告» **dans** `BorText` `:68` ; `FamilyLeader` «**會長**：» `:85` ; `GoodCommentText` `:213` «x威望：» ; **+ `GetAward` `:167` «每日領地獎勵：未領取», `BoderEmblemBackgrand` `:175`→`EmblemSlot` `:180` + `EditEmblem` `:186` «編輯徽章», `GetBuff` `:191` «每日聖物效果：未領取», `Emblem` `:199`** (récompense territoire + buff relique quotidiens — CN only) ; pas d'EnterTerritory ; `FamilyListBtn` `:282` «找尋城鎮».

### familyorderwnd.layout / familyorderwnd_na.layout / cnfamilyorderwnd.layout — [ABS] ×3
`Root/FamilyOrder` DefWin `:4` `{0,634}×{0,500}` (`_na`/`cn` `{0,760}×{0,622}`) — page « rangs & droits »
- `JobBackgrand` `:10` : `TitleText` «x職務名稱» ; `Border` `:21` → **EmText1-7** ETxt `:23,:39,:55,:95,:111,:127,:143` chacun `Icon` SImg + `Text` («會長…», «會員»×6) = **7 rangs** ; `SetJobBtn` `:159` «x職務設定»
- `FamilyEvent` `:166` : `TitleText` «x公會歷史» + `EternalListbox` LBox `:183` ; `TempText` «x城鎮事件» + `TempLisbox` `:193` ; `TitleText1` «x當月英雄公會榜　第99名» / `TitleText2` «x當月開拓公會榜　第99名» (classements mensuels)
- `Privilege` `:209` : `TitleText` «x職務權限» ; `Btn` `:220` «x權限設定» ; `Border` `:226` → `Text` + `Icon1-5` SImg `:237-260` + `ScrollPane` `:266` + `Icon6/7` `:282,:288` (7 icônes de privilèges en ligne)
- **cn only** : `LevelUpBtn` `:295` «公會升級» + `DisbandBtn` `:300` «解散公會» (déplacés ici depuis FamilyInfo)

### familymessagewnd.layout / _na — [ABS] ×2 — page « tableau de messages »
`Root/FamilyMessage` DefWin `:4` `{0,634}×{0,500}` (`_na` 760×622) → `MessageBoard` `:10` : `TitleText` «公會留言板» + `MultList` MList `:21` + `MessageBtn` «發表留言» + `Space` «剩餘空間 99 篇» + `HelpBtn`

### familyadministration.layout — [ABS] — page « gestion bâtiments »
`Root/FamilyAdministrationWnd` DefWin `:4` `{0,726}×{0,512}`
- `Block2` `:8` : **House0/1/2** Border `:10,:154,:237` — chacun `Slot` + `Name` «L09.煉金小屋» + `Durability` «毀損：100/999» + coûts `GoldTesxt`(sic)+`GoldIcon`/`SliverText`+`SliverIcon` + `ExpBar` PBar + `ExpText` + `MaintenanceCosts` «維修費» ; `RepairAllBG`→`RepairAll` `:93/:95` et `RepairBG`→`Repair` `:136/:138` ; `CommandText` «x城鎮命令» / `ProduceBuildingText` «x城鎮營運» ; `CommandBorder` `:116` → `BusinessTax` «x營業稅制» + `TaxText` ; `Help1/2` IBtn
- `Block1` `:321` : `DataBorder`→`Pane` Scroll `:325` (détail bâtiment) + `FamilyName` «x阿卡貝拉友愛之鎮»
- `Block3`(unnamed Static `:354` ; chemin `Block3//NPCList`) → `NPCList` MList `:356` (contrats PNJ affectés)

### familyappwnd.layout — [ABS] — « demande de fondation de ville »
`Root/FamilyAppWnd` FrameWin `:4` rect relatif `{0.32,0.115}→{0.615,0.86}` → `Border` SImg `:10` : `Title` «建鎮申請書» ; `Combobox` Combo `:24` + `Text2` «選擇建鎮地區：» ; `Text3` «創鎮資格：» ; `Text1` «城鎮收回條件：» ; `Border1` BTxt `:46`→`Edit` MLEdit `:48` (charte) ; `CreateText` BTxt `:88` ; `OKBtn`/`CloseBtn` ; `HelpBtn` «?»

### familyfindingwind.layout — [ABS] — coquille « chercher une guilde »
`Root/FamilyFindingWind` FrameWin `:4` `{478}×{476}` px → `TabControl` `:10` seul (pages ci-dessous injectées)

### familyfindinginfo.layout — [ABS] — page liste des guildes
`FamilyFindingInfo` **Static** `:4` `{0,462}×{0,418}` : `Title` ; `MultiList` MList `:17` ; `Closebutton` «string關閉» / `Joinbutton` «string申請加入» / `FindingTownbutton` «string尋找城鎮» ; `pageText` «99/99» + pagination 4 IBtn (`nextBtn/backBtn/mostnextBtn/mostbackBtn` `:40-61`) ; `Help` `:68` — **note** : les Text ont le préfixe littéral «string» (bug de données natif)

### familymemberfindingwnd.layout — [ABS] — « membres d'une ville »
`Root/TownFinding` FrameWin `:4` `{370}×{404}` → `Stc` `:10` : `MultiList` MList `:12` + `Btn` `:21` + `Title` «阿卡貝拉友愛之鎮(搜尋到 5 位)» + `Help`

### familytownlistwnd.layout — [ABS] — **annuaire des villes** (ref-shot shot_0062_000149)
`Root/TownList` FrameWin `:4` `{852}×{524}` px
- `Static` `:10` : `TownList` LBox `:12` ; `Text1` «x城鎮清單» ; `Border` `:21` filtres : CB `Checkbox1-6` «x農場/x礦場/x祭壇/x煉金/x鑄造/x魔池» `:23-43` (**6 types de ville** : ferme/mine/autel/alchimie/forge/bassin mana) + `LevelLowerLimit` Edit «999» + `LevelupLimit` Edit + `TownSerachBtn` «x城鎮搜尋» + `SearchNpc` «xNPC» ; `EnterTownBtn` «x進入»
- `Static1` `:96` (détail ville) : `FamilyNameText` «xL01.阿卡貝拉友愛之鎮» / `LeaderText` «x會長：…» / `ExpText` «x聲望：» / `MemberCountText` / `BusinessTaxText` / `InstanceType` «x城鎮類型：» ; `BuildingListBorder`→`BuildingPane` Scroll `:141` ; `NPCList` MList `:157` ; `MemberSerachBtn` «x會員搜尋» / `CloseBtn`

### familyprivilege.layout — [ABS] — **gabarit ligne** de droit
`FamilyPrivilege` DefWin `:4` `{0.307,0}×{0.044,0}` : `Text` + `Btn1-5` RB `:9-21` — ligne « privilège × choix du rang mini » (5 radios = les 7 rangs ? ou seuils)

### familyachievement.layout — [ABS] — **gabarit ligne** haut-fait de guilde
`Achievement` DefWin `:4` `{0.035,0}→{0.42,0.21}` (bandeau) : `Type` «LV01～30副本討伐» + `Point` «0點聲望»

### Dialogues guilde (tous racine `Root/CommonDlg` FrameWin, sauf indication)
- `familybulletindlg` — [PART] (`GuildWindow` onglet Annonce custom) — `:4` `{0.295,328}×{0.29,232}` : `EditBox` + `MsgText` + `MultEdit` MLEdit `:23` (éditeur annonce multiligne) + OK/Cancel/Close
- `familycommentdlg` — [ABS] — `:4` `{0.25,302}×{0.31,176}` : `MsgText` + `EditBox` (commentaire/good-comment une ligne)
- `familydonatedlg` — [IMPL] (champ Donate → zone 159) — `:4` `{232}×{172}` : `MsgText` + `EditBox` + `GoldImg1` SImg + `EditBox2` + `GoldImg2` (deux champs = or **et** argent) + OK/Cancel/Close
- `familymessagedlg` — [ABS] — `:4` `{350}×{274}` : `EditBox` (titre) + `MultEdit` `:34` + `MsgText` «標題：» (message guilde)
- `familyjobnamedlg` — [ABS] — `:4` `{232}×{346}` : `MsgText` + `EditBox` + **EditBox1-7** `:42-112` + `Image1-7` SImg `:70-108` = renommage des **7 rangs** + icône par rang
- `familysetprivilegedlg` — [ABS] — `:4` `{312}×{308}` : `MsgText` + `EditBox` (nom rang) + `Border` `:29` → `Text` + `Icon1-5` + `ScrollPane` `:70` + `Icon6/7` (mêmes 7 privilèges que familyorderwnd) + OK/Cancel/Close
- `cnfamilyemblem` — [ABS] — `Root/FamilyEmblem` FrameWin `:4` `{266}×{200}` : `MsgText` + `EmblemBtn` «設置徽章» + `EmblemTestBtn` «測試徽章» + `BoderEmblem`→`EmblemBlockSlot` Slot `:52` + `EmblemBlock` (éditeur de blason CN)

### Stockage guilde (FamilyStorage)
- `familystoragewnd` — [IMPL] — `Root/FamilyStorage` FrameWin `:4` «背包» `{394}×{262}` → `Tab` Tabs `:11` (3 pages injectées)
- `familystoragetab1/2/3` — [IMPL] ×3 (pages 40 slots custom) — `FBPage1/2/3` DefWin `:4` `{0,378}×{0,204}` → `Listbox` LBox `:6` → **S0-S39** Slot `:9-165` + `CloseBtn` «x關閉» + `LogBtn` `:174` + `Cadre0-5` SImg `:181-218` — les 3 fichiers **identiques** hors nom racine
- `familystoragelogwnd` — [IMPL] (journal 506 custom) — `Root/FBHistory` FrameWin `:4` `{406}×{302}` → `st`→`Btn` «x關閉» + `Listbox` LBox `:19`

---

## BÂTIMENTS VDG (ville de guilde)

### familybuilding.layout — [ABS] — **ancienne génération** : plein écran `{1,0}×{1,0}` `:4`
`BuildingArea` SImg `:6` : `Building0-4` SImg (5 emplacements bâtiments) + **Npc1-48** SImg `:33-199` (48 emplacements PNJ positionnés sur la carte, `image:SlotBackdrop`) ; `CollectArea` `:204` (panneau droit `{{0.734..0.996}}`) : `BuildingBorder`→`BC0-15` (16 collectés) + `ScrollBar` ; `NpcBorder`→`NC0-19` (20 collectés) + `ScrollBar`

### familybuilding_forest.layout / familybuilding_mountain.layout — [ABS] ×2 — **nouvelle génération** `{0,726}×{0,512}`
Structure identique aux deux (seuls préfixes «x» des textes diffèrent) :
- `CollectArea` `:8` : `BuildingBorder`→`BC0-15` **Slot** `:12-71` + VScroll ; `NpcBorder`→`NC0-19` Slot `:86-161` + VScroll ; `BHelp/BHelp1` ; `BText` «建物收藏冊» / `BText1` «人物契約收藏冊»
- `BackGround` `:199`→`BuildingArea` `:202` : **Area0,1,3,2,4** DefWin `:204,:242,:280,:318,:356` — chacun `BuildingName{N}` BTxt «L10 煉金小屋» + `BuildingStatic{N}`→`Building{N}` Slot + 8 `Npc{N}1-8` Slot ; + `BuildingStatic5`→`Building5` `:396` + `BuildingName5` «聖物祭祀區» (zone relique)

### familycenter.layout — [ABS] — `Root/FamilyBuildingCenter` FrameWin `:4` `{742}×{570}` → `Tab` Tabs `:10` seul (coquille centre de bâtiments, pages = familybuilding_*/administration)

---

## FAMILY BATTLE (tournoi de guildes) — tous [ABS]

- `familybattleinfownd` — HUD en match : `FamilyBattleInfo` DefWin `:4` `{0.725,200}×{0.2,200}` : `EnergyBarR` PBar + `EnergyBarB` MpBar ; `WinCntIcon1-5` **RedCheckbox** `:22-39` (victoires rouge) + `WinCntIcon1-5` CB `:43-59` (bleu) ; `BlueMessage`/«海綿寶寶» `RedMessage`/«派大星» ; `Score`/`Score1`/`Score2` «50»«33»«：»
- `familybattlejoinwnd` — `FamilyBattleJoinWnd` DefWin `:4` `{0,738}×{0,490}` → `Border` : `MapNameLab` «x公會淘汰賽場地» ; `ZoneMapImg` SImg `:20` (carte) ; `FamilyNameLab` «x冠軍公會» + `FamilyName` ETxt «x傳奇網路» ; `TerritoryList`→`ListPane` Scroll `:42` ; `IntroText` BTxt ; `TimeTableBtn` «x賽程表» / `RankBtn` «x目前排名»
- `familybattlejudgewnd` — `Root/CompetitionSchedule` FrameWin `:4` «x賽程表» `{760}×{586}` (imagesets **CompetitionSchedule+promotion**) → `Fixed` : **TradeUnion1-31** Static `:17-390` (case = `Name` STxt — arbre de tournoi **31 équipes affichées**) + `Way1-30` SImg `:399-574` (connecteurs de bracket) + `HelpBot` `:224`
- `familybattlekilledrankwnd` — `FamilyBattleKilledRank` FrameWin `:4` «x戰場排名» `{386}×{556}` → `MultiColumnList` MList + `CampsPlace` «x陣營排行» + `Leave` «x離開戰場»
- `familybattlekilledrankwnd2` — DefWin→`Static` : `RedList`+`BlueList` MList (2 colonnes camps) + `RedText` «x紅軍»/`BlueText` «x藍軍» + Close
- `familybattlerankwnd` — `FamilyBattlrRank`(sic) FrameWin `:4` «公會排名» `{386}×{492}` → MList + `Placing` «目前排名» + `Newtime`
- `familybattletimewnd` — HUD `{0.517,276}×{0.107,149}` : `WarTimeName`→`TitleText` «x戰場剩餘時間» ; `Timetable`→`TimeText` «x0：00：0» ; `ImageBG` ; `WarPopulation`→`FightsFigure` «x對戰人數» ; `WarFigure`→`PlayerCntTxt` «x49 VS 50»

### bulletterrainwnd.layout — [ABS] — **hors sujet** : `Root/BulletWnd` FrameWin `:4` `{580}×{139}` — boutons `ResetPhysics/Shoot/Ragdoll` + 16 CB debug (`DrawWireframe`, `DrawAabb`, `ProfileTimings`, `EnableCCD`…) = **fenêtre de debug Bullet physics** (leftover démo CEGUI), pas une UI « terrain de guilde »

---

## NPC / COMMERCE

### npctalkwnd.layout — [IMPL] (`NpcDialogueWindow.cs` → NpcTalkWnd.layout)
`Root/NpcTalkWnd` FrameWin `:4` Text=«Title» (titre runtime) `{{0.031,0},{0.133,0},{0.031,364},{0.133,418}}` → `Backgrand` `:12` → `Listbox` **Listbox2** `:14` (liste de choix dialogue) + `closeBtn` `:19`

### shopwnd.layout — [IMPL] (`ShopWindow.cs` → ShopWnd.layout)
`Root/shop` FrameWin `:4` `{302}×{386}` → `tab` Tabs `:10`, 2 onglets :
- `Tabsell` «商 品» `:14` → `panel` : `fixAllBtn`+`fixoneBtn` IBtn (réparer tout/un) + `help` ; `border`→`scrollbar` VScroll `:43` (grille articles, items=`shopitem`) ; `SellAllBtn` IBtn `:52` ID=2001 ; `Coin` «888888»+`CoinImage` + `CoinText` «目前擁有»
- `TabBuy` «買 回» `:91` [hidden] ID=1 → `border`→`scrollbar` + `buy` IBtn + `scrollpanel` Scroll `:125` (rachat)

### shopitem.layout — [PART] (lignes custom) — `ShopItem` Static `:4` `{245}×{40}` : `itemslot` Slot + `ItemDeatilText` + `GlodText`(sic) «999999» + `si1` + `SilverText` + `si2`

### auctionwnd.layout — [IMPL] — `Root/AuctionWnd` FrameWin `:4` «拍賣場» `{728}×{498}` → `tabcontrol` Tabs `:12` (pages buy/sell injectées)

### auctionbuywnd.layout — [IMPL] (`AuctionWindow.cs`, onglets custom)
`AuctionBuyWnd` DefWin `:4` `{0,712}×{0,438}`
- `ItemListBG` `:10` : `TitleText` «ItemList» ; `Tree` **Tree** `:20` (arbre catégories) ; `GoldText`/`SilverText`+icônes ; `border`→`PageText` «100/100» + 4 IBtn pagination (`RBtn/RRBtn/LBtn/LLBtn`) + `listbox` **MultiColumnList2** `:107` ; `buyBtn` «Buy»/`closeBtn`
- `searchBG` `:130` : `border1` qualité — CB `WhitBtn/GreenBtn/buleBtn/OrageBtn/GoldBtn/purpleBtn` «白色/綠色/藍色/橘色/金色/紫色» ; `border2` prix — `MiniGoldEdit`/`MaxGoldEdit`/`MiniSilverEdit`/`MaxSilverText`+icônes ; `border3` niveau — `MniiLvEdit`/`MaxLvEdit` «Lv~Lv» ; `border4` `searchEdit` + `researchBtn` «search»

### auctionsellwnd.layout — [IMPL] — `AuctionSellWnd` DefWin `:4` `{0,712}×{0,438}`
`backgrand` : `goldText`/`silverText` BTxt (solde) ; `title1` «拍賣中的物品» ; `border` `:86` : `title2`/`titile3` **«オークション価格» (restes JP)** ; `SellBtn` ; `goldedit`/`silveredit` ; `taxGold`/`taxsilver` ; `slot` Slot + `EmbedBackground`→`SlotName` ; `list` MultiColumnList2 `:183` ; `cancleBtn`(sic) ; `titile4` «拍賣價格»

### cnauctionbuywnd.layout — [PART] — variante CN `{0,664}×{0,400}` : même Tree+listbox+pagination ; recherche **condensée** en un `border4` (`searchEdit`, lv min/max, `mark` «品質：» + `ItemComboBox` Combo `:197` en remplacement des 6 CB couleurs)

### hotelwnd.layout — [IMPL] (`HotelWindow.cs` → HotelWnd.layout)
`Root/HotelWnd` FrameWin `:4` `{696}×{368}` → `Background` : `MemberListText` «x住戶名單» + `MemberList` MList ; `SearchMember`→`SearchBox`+`SearchBtn` ID=5 ; `OwnInfo` Border `:49` «x自宅狀況» : `Address`/`AddressText` «x目前沒有承租房屋», `Rent`+`GoldText`/`SilverText`+icônes, `Popularity` «x本週人氣», `PrettyPoint`/`ConfortablePoint`/`PeculiarPoint` «x華麗/舒適/奇特積分»+valeurs ; boutons `VisitBtn` ID=1 / `RentBtn` ID=2 / `PrivilegeBtn` ID=3 / `EnterBtn` ID=4 / `PayBtn` «x支付租金» ID=6 / `HelpBtn`

### hotelstoragewnd.layout — [ABS] — `Root/HotelStorage` FrameWin `:4` `{322}×{122}` → `Tab` Tabs → `TabCommon` «x小屋倉庫» → `listbox` Border → `backgrand` **HintImage** → `stimage1-3` + `SettingBtn` — **layout quasi vide** (grille d'items créée au runtime)

### hotelprivilegedlg.layout — [ABS] — `Root/HotelPrivilege` FrameWin `:4` `{258}×{164}` → `Border` : `Icon1-5` SImg (5 rangs d'accès) + `TitleText1` «x進入房間» + `Check11-51` CB (5×1 = droit « entrer » par rang) + OK/Close

### stallui.layout — [IMPL] (`VendingWindow.cs` → StallUI+StallBuy)
`Root/StallUI` FrameWin `:4` `{574}×{510}` → `TabControl` → `Tab` «擺攤上架» → `Background` : `Text1` «拍賣價格» ; `Border`→`Slot`+`goldedit`+`goldedit1`+icônes+`SetProductBtn` «物品上架» ; `Border2`→`EditBox` MLEdit (message de boutique) ; `Text2` «拍賣中的物品» + `list` MultiColumnList2 ; solde `goldText`/`silverText` ; `OutBtn` «物品下架» / `StallBtn` «開始擺攤»

### stallbuy.layout — [IMPL] — `Root/StallBuy` FrameWin `:4` «XXX的個人商店» `{334}×{458}` → `Tab` «物品清單» : `Border`→`Listbox` MultiColumnList2 ; `TitleText` «商品瀏覽» ; `BuyBtn` «購買»/`CloseBtn` ; `GoldText`/`SilverText`+icônes

### tradewnd.layout — [IMPL] (`TradeWindow.cs` → TradeWnd.layout)
`Root/TradeWnd` FrameWin `:4` `{232}×{466}` → `Backgrand` : `LockBtn`«Lock»/`OkBtn`/`CancelBtn` ; `selfBorder` `:29` : `SlotBorder`→**MySlot1-8** Slot `:35-76` ID 0-7 + `GlodEdit`(sic)+`SilverEdit`+icônes ; `MyNameText` + `LockOnMyImage` ; `targetBorder` `:127` : **TargetSlot1-8** ID 10-17 + `TGoldText`/`SilverText` ETxt (lecture seule) + `TargetNameText` + `LockTargetImage`

### storageui.layout — [IMPL] (`BankWindow.cs` → StorageUI.layout)
`Root/StorageUI` FrameWin `:4` `{385}×{135}` → `backgrand`→`border`→**bag0-4** Slot `:20-40` (5 sacs banque) ; `border` Static→`closeBtn`+`rentBtn` «Rent»

### storagerentdlg.layout — [ABS] — `Root/StorageRentDlg` FrameWin `:4` `{232}×{218}` : `ContextText` «一份倉庫租約可租用一個月» (1 bail = 1 mois) + `radioBtn1-4` RB «第一格…第四格» + ok/cancel — location des sacs 2-5

### itemmall.layout — [IMPL] (`ItemMallWindow.cs` → ItemMall.layout)
`Root/ItemMal` FrameWin `:4` `{762}×{546}`
- `VigourTab` `:10`→`panel` : `box`→`list` Border + pagination 4 IBtn + `pageText` «99/99» ; `itembox` LBox → **sp1-15** Slot `:62-104` (grille 3×5 vedettes) ; `list` LBox `:108` (articles, items=`itemmaillitem`) ; `st1`«choose»/`st2`«index»/`st3`«hot» ; `getallBtn` «全部領取» / `getBtn` «領取虛寶» ; `toplist` Border `:151` (top ventes, items=`itemmailltopitem`) ; `passBtn` «設定密碼» / `saveBtn` «儲 值» / `closeBtn` ; `moneyText`+`coin`, `BonusmoneyText`+`coin1`, `ForeverGoldText`+`coin2` (**3 monnaies**)
- `MsgBGImage`→`text` (bandeau info) ; `tab` Tabs `:214` : `Tab1` «Global» / `Tab2` «Team» [hidden] / `Tab3` «forever» [hidden]

### cnitemmall.layout — [PART] — variante CN `{730}×{510}` : même gabarit ; onglets «禮卷商城/紅利商城/點卷商城» (coupon/bonus/cash) tous [hidden] ; + `WebBtn` «x開啟網頁» ; pas de `passBtn`

### itemmallmailwnd.layout — [PART] (envoi cadeau — champ destinataire)
`ItemMaillMailWnd` FrameWin `:4` `{344}×{390}` : `sendBtn`«send»/`closeBtn`/`AddressbookBtn` «通訊錄» ; `Title`/`targetText` ; `TargetEdit` + `titleEdit` + `contextEdit` MLEdit ; `border`→`itemslot` Slot (cadeau joint)

### itemmaillitem.layout — [IMPL] (lignes) — `ItemMallItem` Static `:4` `{360}×{52}` : `itemslot`→`specilText` ; `ItemDeatilText` ETxt ; `ItemPointext` «9999»→`delete` ; `itemmaillIcon` ; `buyBtn`/`GiftBtn`/`detailBtn` ; `SpecilIcon` + `SpecilPointext` (prix promo)

### itemmailltopitem.layout — [PART] — `ItemMallTopItem` Static `:4` `{188}×{50}` : `itemslot`+`ItemDeatilText`+`ItemPointext`+`itemmaillIcon`+`buyBtn`

### itemmallquestitem.layout — [ABS] — `QuestItem` DefWin `:4` `{200}×{21}` : `silver` SImg + `SilverText` (ligne prix argent)

### itemmallserial.layout — [ABS] — `ItemMallSerial` FrameWin `:4` `{266}×{158}` : `SerialText` «序號» + `SerialInput` «123456789012345678901234» (24 ch.) + `PasswordText` «密碼» + `PasswordInput` + Ok/Cancel (activation de code)

### specialcoinui.layout / specialcoinui2.layout / specialcoinui2_na.layout — [ABS] ×3 — magasin « monnaie spéciale » (loyalty)
`SpecialCoin` : `_na` = **FrameWin** `:4` `{363}×{502}` autonome ; les 2 autres = **DefWin** `{0,343}×{0,449}` (panneau embarqué) → `Background`→`Border` + `ScrollPane` Scroll `:18/:44` (liste items=`specialcoinitem`) + `HelpBtn` ; `ui2` et `cnspecialcoinui` ajoutent `LongLifeSlot` `:16` = item épinglé «x水晶十字勳章» (NameText+Icon+NumText «999»)

### specialcoinitem.layout / cnspecialcoinitem.layout — [ABS] ×2 — gabarit ligne `{305/339}×{24}` : `NameText`+`Icon`+`NumText`

### specialcoinps.layout — [ABS] — `SpecialCoinPS` DefWin `:4` `{0,170}×{0,20}` : `Text`+`Point`+`Icon` (ligne « coût en points »)

### buymsgdlg.layout — [PART] (popup étal ; VendingWindow couvre l'étal)
`Root/BuyMsgDlg` FrameWin `:4` «彈出對話框» `{234}×{188}` : `Border`→`MsgLb` STxt + `GoldText` ; `Btn1` «OK!»/`Btn2` «No»/`Btn3` «Cancel» (confirmation achat)

### dividedlg.layout — [IMPL] (`InventoryWindow.cs`, popup split → NCID 84)
`Root/DivideDlg` FrameWin `:4` `{232}×{168}` : `Title` ; stepper `LLBTn/LBtn` IBtn + `editbox` + `RBtn/RRBTn` ; `cancelBTn`/`okBtn`

### moneyconfirmitem.layout — [ABS] — `MoneyConfirmItem` DefWin `:4` `{185}×{20}` : `gold`+`silver` SImg + `GoldText`+`SilverText` ETxt (gabarit ligne « coût or+argent »)

---

## SOCIAL

### communitywnd.layout — [IMPL] (`SocialWindow.cs` → CommunityWnd.layout)
`Root/Community` FrameWin `:4` `{500}×{484}` → `TabControl` `:9` TabH 24.7px, **4 onglets** :
- `FriendTab` «人際關係» `:13` [hidden] : `FriendList` MList + `BlockList` MList ; `EditCardBtn` «編輯名片»/`AddFriendBtn` «加入好友»/`BlockBtn` «加入封鎖» ; titres «好友名單/封鎖名單»
- `SearchTab` «在線搜尋» `:72` [hidden] : `List` MList + `SearchBtn` ; `FilterBorder` niveaux min/max Edit ; `ClassBorder`→`Tree` Tree `:150` (filtre classes) + `OptionBorder`→`ScrollPane` ; `CheckBorder` CB `TeamBtn` «過濾不組隊玩家» + `CardBtn` «過濾無名片玩家»
- `TeamTab` «隊伍搜尋» `:202` [hidden] : `SearchSelect`→CB `CheckBox1/11/12/13` «僅顯示同場景/同分流/適合等級/過濾封鎖隊員» + `TypeBox` Combo «隊伍» + `SearchBtn` ; `SearchList` MList ; `DetailBorder`→`PurposeTextLab`/`DemandTextLab` ETxt + `MemberList` MList ; `RecruitBtn` «隊員招募»
- `LoverTab` «情侶資訊» `:329` (visible) : `BreakBtn` «提出分手» ; `CharacterBorder` fiche partenaire (Nom/Lv/職業/性別/職等) ; `DetailBorder` «每週紅利/交往日期/交往天數» ; `LoveTalkBtn` «編輯情話» ; `SkillBorder`→4×`Item`(Slot+Text «情侶技能一») + pagination `LeftBtn/RightBtn` ; `MoneyTitle`+`progressBar` PBar+`MoneyText` «999999» (cagnotte couple) ; `MessageBorderToMe`/`MessageBorderToLover` → `TalktomeText`/`TalkToHeText` HTxt

### recruit.layout — [IMPL] (`SystemServiceWindows.cs::RecruitWindow` → Recruit.layout)
`Root/Recruit` FrameWin `:4` `{470}×{180}` : `SettingText` «隊員招募設定» ; `Border`→`PurposeText`/`PurposeEdit` + `DemandText`/`DemandEdit` MLEdit ; `AutoJoinBox` CB «自動組隊» + `StartRecruitBox` «開啟招募» ; `SettingBtn` «設定»

### messageboardui.layout — [IMPL] (`MessageBoardWindow.cs` → MessageBoardUI.layout)
`MsgBoard` DefWin `:4` plein écran `{1,0}×{1,0}` — **3 sous-fenêtres** :
- `ReadFWin` FrameWin `:7` : Title/Date/Content/Author STxt + `ContentMLEBox` MLEdit + `TitleEBox` + `NameEBox` + `DelBtn` «刪除»/`CancelBtn`
- `WriteFWin` FrameWin `:85` : `TitleEBox` + `ContentMLEBox` + `AgreeChBox` CB «我同意留言規章» + OK/Cancel
- `MsgBorardFWin` FrameWin `:138` : `MsgBoardTabCtrl` → `TabMsg` «留言板» (`ContentMCList` MList + `PagesText` «1 / 1» + Back/Next) + `TabRule` «留言規章» (MLEdit) ; `CapacityText` «剩餘空間 100篇» ; `WriteBtn` «我要留言»

### mailwnd.layout — [IMPL] (`MailWindow.cs` → MailWnd.layout)
`Root/MailWnd` FrameWin `:4` `{362}×{455}` → `tabcontrol` Tabs `:11` seul (3 pages injectées)

### maillistwnd.layout — [IMPL] — `MaillistWnd` DefWin `:4` `{0,338}×{0,388}` : `border`→`scrollbar` VScroll (liste items=`mailitem`) + `deleteBtn` + `closeBtn` + `helpBtn`

### mailreadwnd.layout — [IMPL] — `MailReadWnd` FrameWin `:4` `{352}×{400}` : `ReplayBtn`/`ReturnBtn`/`closeBtn` ; `senderitle` «寄件人»+`sendText`, `titleTxt` «標題»+`titletext`, `contextTxt` BTxt ; `border` : `getmoneyTxt` + `getBtn` (récupérer pièce jointe) + `goldtext`/`slivertext` + `Item` Slot + `CODBtn` (paiement à la livraison)

### mailsendwnd.layout — [IMPL] — `MailSendWnd` DefWin `:4` `{0,338}×{0,388}` : `sendBtn`/`AddressbookBtn`/`close` ; `TargetEdit`+`titleEdit`+`contextEdit` MLEdit ; `PostageTxt` «郵資» + `glodtext`(sic)/`sliverText`+icônes ; `border` : `itemslot` + `commonBTn` RB ID=1 / `MoneyBtn` RB ID=2 (envoi item **ou** argent) + `goldEdit`/`sliverEdit`

### mailitem.layout — [IMPL] — `mailitem` DefWin `:4` `{285}×{38}` (ligne) : `TreasureImage` Slot→`frame` Zframe+`ItemCount` ; `NameText` «寄送人»/`TitleText` «標題»/`DayText` «13天»/`returnText` «退件»

---

## GAPs

- **`bulletterrainwnd` = leftover debug Bullet-physics** (boutons Shoot/Ragdoll/DrawWireframe), pas une UI guilde — à reclasser en « outils moteur » dans la carte.
- **Pages d'onglets injectées au runtime** : `familywnd`, `familyfindingwind`, `familycenter`, `familystoragewnd`, `auctionwnd`, `shopwnd`, `itemmall`, `mailwnd` ne sont que des coquilles `TabControl` — la reconstitution doit charger les sous-layouts (FamilyInfo/FamilyOrder/FamilyMessage/…, FBPage1-3, AuctionBuy/Sell, Tab1-3, Maillist/Read/Send).
- **Rangs guilde = 7** (EmText1-7 de familyorderwnd ↔ EditBox1-7+Image1-7 de familyjobnamedlg) mais la ligne `familyprivilege` n'a que **5 radios** → les 7 icônes Icon1-7 des privilèges = 7 droits, pas 7 rangs ; la matrice exacte droit↔rang reste à confirmer au runtime.
- **Variantes régionales divergent réellement** (pas juste des rects) : `_na` ajoute `FamilyMission01/02` (quêtes guilde) ; `cn` ajoute `GetAward`/`GetBuff`/`EditEmblem`+`cnfamilyemblem` (récompenses territoire quotidiennes + éditeur de blason) et déplace `LevelUp`/`Disband` dans FamilyOrder. EDENFR : la variante chargée prouvée = `familyinfownd_na` [IMPL] ; pour order/message/emblem **aucune variante n'est implémentée** → laquelle le client FR chargeait ? ( probablement `_na` où elle existe : order_na, message_na ; pas d'emblem `_na` → fonctionnalité blason possiblement absente de FR).
- **familybuilding.layout ≠ _forest/_mountain** : deux générations du gestionnaire VDG (ancienne plein-écran SImg vs nouvelle 726×512 en Slot+Area0-4+zone relique `Building5` «聖物祭祀區»). `_forest`/`_mountain` identiques à un «x» près → même structure, thème terrain différent.
- **familyadministration** ne montre que **3 bâtiments** (House0-2) + NPCList + commande «營業稅制» — complément de familybuilding_* (visualisation ↔ administration/entretien).
- **Strings restes étrangers** : auctionsellwnd contient du **japonais** («オークション価格», `:88/:97`) ; familyfindinginfo préfixe «string» littéral (`:21-29`) ; nombreux `sic` (GlodText, cancleBtn, FmailyFix, FamilyBattlrRank, Block3//NPCList) — reproduire les Name **tels quels** (bindings natif par nom).
- **Titres runtime** : npctalkwnd «Title», stallbuy «XXX的個人商店», familymemberfindingwnd «…(搜尋到 5 位)», familybattlerankwnd `Newtime` vide → remplis en code.
- **Types CEGUI spécifiques** à supporter : `Listbox2` (npctalkwnd), `MultiColumnList2` (auction/stall), `Tree` (auction catégories, community classes), `MpProgressBar` (battleinfo), `RedCheckbox` (battleinfo), `HintImage` (hotelstoragewnd), `Zframe` (mailitem).
- **Absents notables** (aucun équivalent Godot) : hotelstoragewnd/hotelprivilegedlg (maison : stockage+droits), storagerentdlg (location sacs banque 2-5), specialcoin* (7 fichiers, magasin monnaie spéciale/loyalty), itemmallserial+questitem (codes promo, ligne prix), moneyconfirmitem, familycenter/administration/appfinding/jobname/privilege/message — toute la boucle « fonder/administrer une ville de guilde » est [ABS], seuls donnation+annonce+storage sont couverts.
- **Colonnes MList jamais dans le XML** (grep «Column» : seules propriétés `ColumnsMovable`) → la définition des colonnes (membres, HDV, boutique, rangs) vit dans le code natif/`re/` — croiser `protocol-map.md` + décompilé pour les reconstructeurs.
