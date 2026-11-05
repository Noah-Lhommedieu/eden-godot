# D4 — STRUCTURE UI — layouts craft/forge/fusion/récolte (corpus/fr-full/uidata/layouts/)

Statuts croisés avec `docs/reference/corpus/layout-impl-map.md` : **[IMPL]** implémenté / **[PART]** partiel / **[ABS]** absent. Aucun `.layout` du corpus ne contient de `Property Event="..."` (grep exhaustif : 0 occurrence) — les bindings sont en code natif. Types abrégés : FrameWin, Static, Slot=SlotImage, STxt=StaticText(Lab), HTxt=HintText(Lab), ETxt=EmbedTextLab, TxtS=TextSmoothLab, BTxt=BorderText(Lab), Btn, IBtn=ImageButton, Edit, MList=MultiColumnList, Scroll=ScrollablePane, CB=Checkbox, IOCB=ImageOnlyCheckbox, ICB=ImageCheckbox, RB=RadioButton, Combo, PBar=ProgressBar, SImg=StaticImage.

**Convention rect CEGUI** : `{{sx,px},{sy,py},{sw,pw},{sh,ph}}` = fraction relative + offset absolu.

---

## Ordres de travail / récolte (work orders)

### worklistwnd.layout — [IMPL] (`scripts/hud/CraftWindow.cs` custom)
`Root/Work` FrameWin `:4` — titlebar, minsize 0, rect `{{0.327,0},{0.215,0},{0.327,292},{0.215,306}}`
- `Static` `:10` plein cadre
  - `Border` `:12` `{{0.043,0},{0.157,0},{0.957,0},{0.955,0}}` minsize `{0,252},{0,214}`
  - `TotalText` HTxtLab `:16` Text=«測試» `{{0.043,0},{0.045,0},{0.938,0},{0.112,0}}`
  - `ScrollabelPane` Scroll `:22` `{{0.065,0},{0.179,0},{0.935,0},{0.933,0}}` + `__auto_container__`

### workresultwnd.layout — [IMPL] (`CraftWindow.cs`)
`Root/WorkResult` FrameWin `:4` rect `{{0.361,0},{0.275,0},{0.361,232},{0.275,230}}`
- `Static` `:10`
  - `Border` `:12` `{{0.056,0},{0.063,0},{0.944,0},{0.75,0}}`
    - `Text1` HTxt `:14` Text=«親愛的白撫子您好，本次委託結果為：»
    - `ResultItem1-4` Slot `:24,:40,:44,:48` (4 slots 34px, y=0.348)
    - `Text2` HTxt `:28` Text=«感謝惠顧，我們將持續精進技術…»
    - `ErrText` HTxt `:52` / `EventTitle` HTxt `:58` Text=«123» / `EventContent` HTxt `:66`
    - `EventItem1-4` Slot `:72,:76,:80,:84` (mêmes rect que ResultItem)
  - `CloseBtn` Btn `:89` / `OKBtn` Btn `:92`

### lifeskillitem.layout — [ABS] (paquet 543 → PlayerProfileState, pas d'UI)
`LifeSkillItem` DefaultWindow `:4` (gabarit de ligne)
- `Item` Static `:6` `{{0.106,0},{0.231,0},{0.106,340},{0.231,52}}`
  - `ItemSlot` Slot `:9` / `ItemDeatilText` ETxtLab `:13` Text=«嚇人炸彈試作品»
  - `ExpBar` PBar `:21` progress 0 `{{0.153,0},{0.538,0},{0.741,0},{0.846,0}}`
  - `ForgetBtn` Btn `:28` Text=«遺忘» / `LvUpBtn` Btn `:33` Text=«領悟»
  - `FullExpText` STxtLab `:37` Text=«已熟練» rouge / `SelectFrame` Zframe `:47` Visible=False
  - `ExpText` STxtLab `:54` Text=«9999/9999» RightAligned

### collectwnd.layout — [PART] (`ArchiveWindow.cs`, dépôt drag item)
`Root/CollectWnd` FrameWin `:4` rect `{{0.269,0},{0.167,0},{0.269,380},{0.167,424}}` — **même gabarit que combinewnd** (fenêtre « commission récolte »)
- `Background` Static `:10`
  - `UseItemBg` Static `:12` → `UseItemSlot` Slot `:14` / `Itemmall/Bg` Static `:18` → `ItemmallSlot` Slot `:20`
  - `Border` `:24` `{{0.033,0},{0.415,0},{0.967,0},{0.876,0}}`
    - `ResultItemSlot0/1` Slot `:26,:29` + `ResultItemSlot2/3` Slot `:76,:79` (hors Border :26-31 hors/, :76-81 sous Background)
    - `ResultItemText0-3` STxt `:32,:58,:82,:100` Text=«一二三四五六» + `ResultItemNumText0-3` STxt `:50,:118,:136,:144` Text=«999／999»
  - `Help` IBtn `:153` (DejaVu HelpButton*)
  - `Text1` STxt `:159` Text=«使用道具» (nommée `Root/CollectWnd/Text1`, hors Background)
  - `Text2` STxt `:165` «特殊虛寶» / `Text3` `:172` «所需時間» / `Text4` `:178` «所需費用» / `Text5` `:184` «有機會獲得：»
  - `CommissionBtn` Btn `:190` Text=«Test送出委託» / `CloseBtn` Btn `:194` Text=«Test關閉»
  - `NeedTimeText` STxt `:198` Text=«99分99秒» / `GoldText` `:205` «999999» / `GoldIcon` SImg `:212` `set:DejaVuImage image:GoldIcon`
  - `SliverText` `:218` «999» / `SliverIcon` `:225` `set:DejaVuImage image:SilverIcon`
  - `WorkLevelText` STxt `:231` Text=«可以接受工作等級99以下的委託»

### collectionwnd.layout — [IMPL] (`ArchiveWindow.cs` custom « Archive », C→S 152/150/151)
`Root/collectionWnd` FrameWin `:4` rect `{{0,0},{0,0},{0,628},{0,544}}` (taille fixe px)
- `TreasuryTree` Static `:9` `{{0,0},{0,0},{0.297,0},{0.802,0}}`
  - `List` Listbox `:13` / `HelpBtn` IBtn `:17` / `Title` BTxtLab `:25`
- `TreasuryUse` Static `:31` `{{0,0},{0.826,0},{0.297,0},{1,0}}`
  - `Iconslot` Slot `:35` / `CollectionBtn` Btn `:39` Text=«x收藏» / `Help` IBtn `:44`
  - `promptText` HTxtLab `:52` Text=«對著收藏品按右鍵即可取出收藏品»
- `TreasuryPage` Static `:61` `{{0.317,0},{0,0},{1,0},{0.656,0}}`
  - `CheckBox1` CB `:64` Text=«只顯示遊戲內寶物» Selected / `CheckBox2` `:74` «只顯示商城寶物» Selected / `CheckBox3` `:84` «只顯示已收藏的寶物» / `CheckBox4` `:126` «x反向排列»
  - `border` Border `:93` → `Scrollpane` Scroll `:96`
  - `title` BTxtLab `:112` → `RateText` STxtLab `:116` Text=«收藏綠：0.0％»
- `TreasuryReport` Static `:136` → `title` BTxtLab `:139` + `borderText` BTxt `:144` VertScrollbar

### collectionset.layout — [PART] (`ArchiveWindow.cs`)
`Root/CollectionSet` FrameWin `:4` rect `{{0.014,0},{0.010,0},{0.014,245},{0.010,303}}`
- `BackGround` Static `:10` → `Border` `:13`
  - `Btn1` Btn `:19` Text=«儲存設定» / `Btn2` Btn `:15` Text=«關閉»
  - `SlotIMG1-30` Slot `:23-111` — grille **6 colonnes × 5 rangées** (pas 0.16 x / 0.137 y)
  - `Text` STxtLab `:113` Text=«拖曳到此的物品，不會被「全部收藏」功能進行收藏» (liste noire du bouton « Collect all »)

---

## Fusion / commission (combine)

### combinewnd.layout — [ABS] (bouton « Fusion » du sac → notice seulement)
`Root/CombineWnd` FrameWin `:4` rect `{{0.269,0},{0.167,0},{0.269,380},{0.167,494}}` minsize `{0,370},{0,411}`
- `Background` Static `:10`
  - `UseItemBg` Static `:12` → `UseItemSlot` Slot `:14` / `Itemmall/Bg` Static `:18` → `ItemmallSlot` Slot `:20`
  - `Border` `:24` `{{0.033,0},{0.352,0},{0.967,0},{0.744,0}}` — **liste 4 ingrédients** : `StuffItemSlot0-3` Slot `:26,:29,:56,:59` + `StuffItemText0-3` STxt `:32,:48,:62,:70` Text=«一二三四五六» + `StuffItemNumText0-3` STxt `:40,:78,:86,:94` Text=«999»
  - `Help` IBtn `:103` / `Text1` STxt `:109` Text=«Test使用道具» (`Root/CombineWnd/Text1`) / `Text2` `:115` «Test特殊虛寶» / `Text3` `:122` «Test所需時間» / `Text4` `:128` «Test所需費用» / `Text5` `:134` «Test有機會獲得：»
  - `CommissionBtn` Btn `:140` Text=«Test送出委託» / `CloseBtn` Btn `:144` Text=«Test關閉»
  - `NeedTimeText` `:148` «Test99分99秒» / `GoldText` `:155` + `GoldIcon` SImg `:162` / `SliverText` `:168` + `SliverIcon` `:175`
  - `Text6` `:181` «x成品» / `Border1` `:188` → `ResultItem` Slot `:190` + `ResultText` HTxt `:193`
  - `WorkLevelText` `:200` «x可以接受工作等級99以下的委託» / `Text7` `:208` (vide)

---

## Renforcement (forge/enclume) — 5 variantes

**Version FR/production probable = `strengthenwnd_na.layout`** (client Aeria : structure « 3 slots en bandeau + MCL pleine largeur + grand % » ; le `_na_20` = variante 20 niveaux). `strengthenwnd.layout` = version CN/TW 5-colonnes compacte ; `14` = refonte px de la même ; `kr` = même, slots verticaux.

### strengthenwnd.layout — [PART] (FeatureDialogWindow « Enclume ouverte » + CraftWindow cast bar)
`Root/Strengthen` FrameWin `:4` rect `{{0.225,0},{0.130,0},{0.225,488},{0.130,404}}`
- `Static` `:10` → `HelpBtn` IBtn `:13`
- `ItemBorder` Border `:22` `{{0.015,0},{0.071,0},{0.362,0},{0.699,0}}`
  - `Scroll` Slot `:26` / `ItemMall` Slot `:30` ID=2 / `Item` Slot `:35` ID=1
  - `ScrollText` STxtLab `:40` «強化捲軸» / `ChanceText` `:50` «成功機率 100.00％» / `EquipmentText` `:60` «放入裝備» / `ItemmallText` `:70` «特殊虛寶» / `WarningText` `:80` «※若強化失敗裝備將會消失»
- `StrengthenLevel` MList `:90` `{{0.378,0},{0.071,0},{0.982,0},{0.699,0}}` colonnes `:95-99` : 階段 / 攻擊加成 / 魔攻加成 / 攻擊總和 / 魔攻總和
- `StrengthenBtn` Btn `:101` / `CloseBtn` `:106` / `StrengthenFactory` Btn `:111`
- `CostText` `:116` / `GoldText` `:123` + `GoldIcon` SImg `:131` / `SilverText` `:139` + `SilverIcon` `:147`
- `Border` `:155` → `ComboText` STxt `:158` (VertScrollbar, log des résultats)

### strengthenwnd14.layout — [PART] — identique en structure, positionnement **en pixels** (rect `{scale,offset}` mixtes), fenêtre 488×504 `:9`, mêmes enfants `:11-168`

### krstrengthenwnd.layout — [PART] — même gabarit que `strengthenwnd` mais slots `Item/Scroll/ItemMall` **empilés verticalement** dans ItemBorder `:26-39`, `WarningText` `:80` texte long WordWrapCentred

### strengthenwnd_na.layout — [PART] — **refonte NA** : fenêtre 448×564 `:9`
- `Static` `:10` → `HelpBtn` `:13` ; `ItemBorder` `:21` → `Item` Slot `:24` + `EquipmentText` `:28` «裝備» ; `ItemBorder1` `:36` → `Scroll` `:38` + `ScrollText` `:41` «捲軸» ; `ItemBorder11` `:48` → `ItemMall` `:50` + `ItemmallText` `:53` «虛寶» ; `NoticeTip` `:60` «x若在有失敗加成下做其他操作時，會遺失該加成效果»
- `GoldText` `:67` / `GoldIcon` `:76` / `SilverText` `:84` / `SilverIcon` `:93`
- `StrengthenLevel` MList `:101` `{{0.028,0},{0.190,0},{0.965,0},{0.791,0}}` — **6 colonnes** `:106-111` (+附加能力 id:5)
- `StrengthenBtn` `:113` / `StrengthenFactory` `:118`
- `ChanceText` STxtLab `:122` «00.000%» font **F001-24** (gros %) / `ChanceText2` `:131` «成功機率：»
- `WarningText` `:139` «※若強化失敗裝備將會消失 / ※防爆水晶連續失敗次數：5次 / ※本階段目前強化加成：5.35%» BottomAligned

### strengthenwnd_na_20.layout — [PART] — même structure que `_na`, fenêtre 448×**684** `:9`, MList `:105` 6 colonnes, WarningText `:139` identique

### strengthenfactory.layout — [PART] — dialogue « forge auto »
`Root/StrengthenFactory` FrameWin `:4` rect `{{0.388,0},{0.339,0},{0.388,232},{0.339,188}}`
- `TimesText` STxtLab `:10` + `TimesInput` Edit `:17` / `LevelText` STxtLab `:24` + `LevelInput` Edit `:31`
- `StartBtn` Btn `:38` / `CancelBtn` Btn `:43` / `Static` `:48` → `HelpBtn` IBtn `:51`

### strengthenslot.layout — [PART] — gabarit de ligne du MCL
`Root/StrengthenSlot` DefaultWindow `:4` → `Icon` SImg `:7` (`set:DejaVu image:MiniVertScrollThumbNormal`) + `Text` STxtLab `:14` Text=«一階加成 3% 攻擊 999999»

---

## Magic Box / Magic Cube

### magicboxwnd.layout — [PART] (`FeatureDialogWindow.cs` 612/613 → dialogue générique)
`Root/MagicBox` FrameWin `:4` rect `{{0.086,0},{0.124,0},{0.086,518},{0.124,413}}` → `TabControl` `:9` TabHeight 20.48px, 2 onglets :
- `Tab1` DefaultWin `:13` Text=«x轉化» (transmutation), Visible=False
  - `Background` Static `:17` → `OkButton` Btn `:19` «x轉化» / `CloseButton` `:23` «x關閉»
    - `BackgroundLeft02` Border `:27` : `Title01` TxtS `:29` «x主要裝備» / `Title02` `:36` «x消耗裝備» ; slots `Second01-08` `:43-65` (grille 4×2) ; `Text01` `:67` «x需求裝備:» / `ColorText01` `:74` «x紅橙黃» / `EquipText01` `:81` «x刀槍砲劍» / `Text02` `:89` «x主裝備» / `Text03` `:97` «x特殊虛寶» ; `First01` Static `:105` → `UseItem01` Slot `:107` ; `First02` `:111` → `UseItem01` `:113`
    - `BackgroundRight02` Border `:118` : `Title01` TxtS `:120` «x轉化預覽» / `Text01` `:127` «x魔方能量» ; `Star01-05` SImg `:134-157` ; `Energy01` SImg `:159` (`set:main image:InfoBackGrand`) → `MpBar` MpProgressBar `:164` → `text` STxtLab `:170` «100/100» ; `Power01` Border `:181` → `Title01` «x魔方力量» + `Image01-03` SImg `:190,:202,:214` → `Power01/02/03` STxt «x1111111/2/3»
    - `Help` IBtn `:228`
- `Tab2` DefaultWin `:236` Text=«x洗鍊» (refonte) : `OkButton` `:241` «x洗鍊» / `CloseButton` `:245` «關閉» ; `BackgroundLeft02` `:249` mêmes slots (Second01-08, First01→UseItem01 `:314`, First02→UseItem02 `:320`) ; `BackgroundRight02` `:341` : `Title01` «x洗鍊成果» + `PowerBG` Border `:350` (Title01 «x魔方力量», Image01-03 → Power01-03) + Star01-05 `:409-432` ; `Help` IBtn `:435` ID=1

### magiccube00.layout — [ABS] — cube Kradia classique
`Root/MagicCube00Wnd` FrameWin `:4` Text=«x布諾拉克方塊x» rect absolu `{{0,100},{0,100},{0,494},{0,416}}`
- `Background` Static `:11` → `Border` `:14`
  - `static01` Static `:17` : **StuffSlot01-16** Slot `:22-68` (4×4) + `Mask` SImg `:70` `set:MagicCube image:Animation` Alpha 0.6
  - `static02` Static `:80` : **StuffPreview01-16** Slot `:84-130` (4×4, aperçu résultats)
  - `static03` Static `:133` → `slot` Slot `:136` (catalyseur central)
  - `StuffSlotText` STxt `:141` «x原料放置區» / `StuffPreviewText` `:149` «x成品預覽區» / `GoodsText` `:157` «x特殊虛寶»
  - `ChanceIcon` SImg `:165` `set:MagicCube image:Probability` → `probability` STxtLab `:172` «x機率%»
  - `Quality` Static `:182` → `Green/Blue/Purple` SImg `:187,:194,:201` (`DejaVuImage HeroIcon3/2/4`)
  - `Lock` IOCB `:209` (`set:main image:FunctionLock*`)
  - `Help` IBtn `:217` / `ActionButton` Btn `:224` «x啟動» / `ListButton` `:229` «x配方» / `ClearButton` `:233` «xClearx»
  - `FunctionList` Static `:238` → `FunctionButton01-05` IBtn `:243,:250,:257,:264,:271` (`set:MagicCube image:Function0N*`)
  - `ellipsis` SImg `:279` `set:MagicCube image:else` / `flier` Border `:288` → `border` `:293` → `image` SImg `:297` `runner0` + `border` `:304` → `text` STxt `:308` «鑑定成功！物品重新進行了能力組合。» (bandeau défilant)

### magiccube01.layout — [ABS] — liste des recettes cube
`MagicCube01Wnd` FrameWin `:4` rect `{{0.098,0},{0.107,0},{0.098,640},{0.107,317}}`
- `BackGround` Static `:10` → `Border01` `:13` → `MethodList` MList `:18` + `PageItems` Static `:23` → pagination `mostbackBtn/backBtn` IBtn `:28,:35`, `pages` STxtLab `:42` «99/99», `forwardBtn/mostforwardBtn` `:49,:56` (`DejaVuImage NumberLeft*/NumberRight*`)
- `FunctionText` STxtLab `:65` «x方塊功能名稱» / `HelpBtn` IBtn `:72`

### magiccube02.layout — [ABS] — fenêtre auxiliaire (filtres)
`Root/MagicCube02Wnd` FrameWin `:4` Text=«xAuxilliaryWndx» rect `{{0.2,0},{0.2,0},{0.2,246},{0.2,316}}`
- `Tab` TabControl `:11`
  - `ItemList` Static `:15` Text=«xItemListx» Visible=False → `List` Border `:19` → `Scroll` ScrollablePane `:21` ; `title` STxtLab `:38` «xGroupNamex» ; `PageItems` `:45` → 4 boutons pagination + `pages` «99/99»
  - `CheckList` Static `:89` Text=«xCheckListx» → `Border` Listbox `:92`
    - `Left` Static `:94` : CB `OneHand/OffHand/TwoHand/Head/Body/Hand/Feet/Back/Ring/Necklace` `:98-157` (Selected=True)
    - `Right` Static `:159` : CB `KusoOneHand/KusoOffHand/KusoTwoHand/KusoHead/KusoBody/KusoFace/KusoSpecial/KusoBack/Others/All` `:163-222`
- `HelpBtn` IBtn `:227`

### magiccube03.layout — [ABS] — `Root/CommonDlg` FrameWin `:4` rect `{{0.399,0},{0.304,0},{0.399,321},{0.304,230}}` : `MsgText` STxtLab `:8`, `EditBox` `:16` MaxLen 16, `OKBtn` `:22`, `CloseBtn` `:26`, `static/border` `:30/:33`, `CancelBtn` `:40` (popup générique cube, p.ex. verrouillage)

### magiccubeitem.layout — [ABS] — gabarit ligne : `CubeItem` DefaultWin `:4` `{0,250}×{0,973}` → `Item` Static `:6` → `Back` Static `:11` Alpha .85 + `Slot` Slot `:15` + `Name` STxtLab `:20` «Very long item name»

### newmagiccubewnd.layout — [ABS] — **nouveau cube Brandt** (version la plus récente ; imagesets MagicCube + CNDejaVu2)
`Root/NewMagicCubeWnd` FrameWin `:4` Text=«x新布蘭德方塊» rect `{{0.371,0},{0.096,0},{0.371,553},{0.096,618}}`
- `Background01` Static `:11`
  - `Border` `:14` (haut-gauche, matières) → `static01` `:17` : **StuffSlot01-16** 4×4 `:22-68` + `ClearButton` Btn `:71` «x清空方塊» + `Mask` SImg `:75`
  - `Help` IBtn `:83` / `CloseButton` Btn `:90` «x關閉»
  - `FunctionList` Static `:94` → `FunctionButton01-05` IBtn `:99-127` (MagicCube Function0N*)
  - `Background02` **MList** `:135` `{{0.307,0},{0.069,0},{0.987,0},{0.448,0}}` (aperçu résultats tabulaire)
  - `Background03` Border `:139` (formule) : `NeedItemListButton` Btn `:141` «x查看» ; `MainMaterial` TxtS `:145` «x主要原料» / `SecondaryMaterial` `:152` «x消耗材料» ; `Main01-08` Slot `:159-181` (4×2) ; `Second01-04` Slot `:183-193` ; `countnum1-5` SImg `:195-255` (`MagicCube countbackground1/2` → `numtext` «x 25»)
  - `Background04` Border `:258` (produits) : `ChanceIcon` SImg `:260` → `Probability` `:266` «99%» ; `Static01` `:276` → `SpecialSlot` Slot `:278` ; `SpecialItem` STxt `:282` «x特殊虛寶» ; `StartButton` Btn `:290` «x啟動» ; `Going` `:294` «x進行» / `Times` `:302` «x次» ; `CompleteListButton` `:310` «x查看» ; `GoldCoin`/`SilverCoin` SImg `:314,:320` ; `EditBox` `:326` Text=«1» (nb exécutions) ; `Gold` `:332` / `Silver` `:340` ; `Complete` TxtS `:348` «x成品» ; `Complete01-08` Slot `:355-377` (4×2) ; `countnum1` `:379`
  - `Text1-3` STxtLab `:393,:402,:411` «x公式總覽/x原料放置區/x公式內容» (rects négatifs = ancrage droite/bas)
  - `Arrow` Border `:420` → `ArrowImage` SImg `:424` `set:CNDejaVu2 image:RightArrow`
  - `Message` SImg `:431` `set:MagicCube image:Success` / `LoadingText` STxtLab `:439` «xLoading...»

### newmagiccuberecipewnd.layout — [ABS] — `Root/NewMagicCubeRecipeWnd` FrameWin `:4` Text=«x新布蘭德方塊詳細資料» rect `{{0.206,0},{0.192,0},{0.206,290},{0.192,312}}` → `Detail` Static `:11` → `Border` `:14` → `Scrollbar` VerticalScrollbar `:17` + `ItemTitle` ETxtLab `:28` «x詳細資料類型» (le détail utilise les gabarits `newmagiccuberecipedetailwnd`)

### newmagiccuberecipedetailwnd.layout — [ABS] — gabarit ligne : `Root/NewMagicCubeRecipeDetailWnd` Static `:4` MousePassThrough, `{0,245}×{0,40}` → `ItemSlot` Slot `:7` + `ItemText` STxtLab `:10` «x物品名稱»

---

## Études de craft (par métier)

### engineeringstudy.layout — [ABS] — **version FR probable** (ref-shot `Blueprint.png`)
`Root/EngineeringStudy` FrameWin `:4` rect `{{0.162,0},{0.045,0},{0.162,412},{0.045,640}}`
- `Background` Static `:10`
  - `BG` Static `:13` → `Slot` Slot `:16` (icône plan sélectionné) / `InfoText` TxtS `:21` (desc plan)
  - `HelpBtn` IBtn `:31`
  - `Box` Border `:132` `{{0.030,0},{0.116,0},{0.970,0},{0.561,0}}` → `ScrollPane` Scroll `:135` (grille de plans, items `lifeskillitem`-like)
  - `Border` `:39` `{{0.030,0},{0.641,0},{0.970,0},{0.920,0}}` (matériaux) : `DemendInfo1` STxtLab `:42` «嚇人炸彈試作品» ; `StuffItem0/1/2` SImg `:50,:77,:104` → `ItemSlot0-2` Slot `:55,:82,:109` + `ItemText0-2` STxt `:59,:86,:113` «一二三四五六七八» + `ItemNumText0-2` STxt `:67,:94,:121` «99/99»
  - `CreateBtn` Btn `:153` «x製作» / `DemendInfo` `:158` «x需求材料» / `LevelInfo` `:222` «x需求等級» / `LevelText` ETxtLab `:229` «99»
  - `ManufactureExpense` `:165` «x製作費用» / `GoldText` `:174` + `GoldIcon` `:183` / `SilverText` `:191` + `SilverIcon` `:200` / `Editbox` Edit `:208` «99» (quantité) / `ManufactureExpense2` `:215` «times»

### jewelrystudy.layout — [ABS] — joaillerie (analyse + craft)
`Root/JewlryStudy` FrameWin `:4` rect `{{0.204,0},{0.021,0},{0.204,412},{0.021,582}}`
- `Background` Static `:10`
  - `Box` Border `:13` `{{0.030,0},{0.184,0},{0.030,372},{0.184,320}}` → `ScrollPane` `:16`
  - `BG` Static `:34` → `Slot` `:37` / `InfoText` TxtS `:42` / `BG1` `:48` → `Slot1` `:50` → `AnalysisItemNum` STxtLab `:52` «*/99»
  - `StudyBtn` Btn `:62` «x分析» / `StudyEditBox` Edit `:258` «99» / `StudyEditText` `:265` «x次»
  - `HelpBtn` IBtn `:67` / `BG2` `:75` → `Slot2` `:77` + `BG3/Text1` `:80` «x精品»
  - `CreateBtn` Btn `:88` «x精工» / `CreateEditBox` `:271` «99» / `CreateEditText` `:277` «x次»
  - `InfoText1` TxtS `:92` «x施展所有既能所消耗的MP減少999點»
  - `BG3` Static `:176` (matériaux) → `ItemSlot0-2` Slot `:178,:188,:206` → `ItemNumText0-2` «*/99» + `BG3/Text` `:198` «x精工需求»
  - `GoldText` `:217` / `ManufactureExpense` `:225` «x製作費用» / `GoldIcon` `:233` / `SilverText` `:241` / `SilverIcon` `:250` / `LevelInfo` `:283` «x需求等級» / `LevelText` `:290` «99»

---

## Glyphes de classe

### jobglyph.layout — [ABS] — `Root/JobGlyph` **DefaultWindow** `:4` `{0,343}×{0,452}` (panneau embarqué, sans cadre)
- `Static` `:9` → `Background` SImg `:12` `set:JobGlyph image:backgrand` ; **Slot1-20** Slot `:17-116` (~10% × ~7.7% chacun, disposition arbre/hexagone) ; `Listbox` Listbox `:117` `{{0.018,0},{0.748,0},{0.982,0},{0.964,0}}` (inventaire glyphes) ; `HelpBtn` IBtn `:122`

### jobglyph_na.layout — [ABS] — **version NA/production probable** : `Root/JobGlyph` **FrameWindow** `:4` rect `{{0.2,0},{0.199,0},{0.2,298},{0.199,431}}` — mêmes Slot1-20 **+ Slot21** `:16` (glyphe central), Listbox `:100`, HelpBtn `:104`

---

## Divers équipement/objet

### gembreaker.layout — [ABS] — `Root\GemBreaker` FrameWin `:4` (note `\`) rect `{{0.313,0},{0.165,0},{0.313,264},{0.165,324}}`
- `sta` Static `:10` → `Btn` Btn `:13` ; `Border` `:17` → `Content` STxt `:20` «請選擇要取出的寶石» ; `Rune1-6` STxt `:29-81` (6 lignes de gemmes) ; `SelectBG` SImg `:83` (surlignage ligne 1)

### growthequipment.layout — [ABS] — `Root/GrowthEquipment` FrameWin `:4` rect `{{0.137,0},{0.219,0},{0.137,372},{0.219,364}}` ClippedByParent=False
- `Background` Static `:10` → `Border1` `:13` (item source) → `Static1` `:15` → `Slot` `:17` ; `Text1-3` HTxtLab `:21,:27,:33` ; `ExpBar` GreenProgressBar `:39` + `ExpBar/BarText` STxtLab `:44` ; `TitleText` `:51` / `HelpBtn` `:56` / `TitleText1` `:62` / `TitleText11` `:95` ; `Border2` `:67` → `ScrollablePane1` `:70` (liste d'items matériau, ContentPaneAutoSized=False) ; `OKBtn` Btn `:89` / `NOBtn` Btn `:92`

### growthequipmentitem.layout — [ABS] — gabarit ligne : `GrowthEquipmentItem` DefaultWin `:4` → `Item` Static `:6` `{0,320}×{0,32}` → `PLvText` `:9` / `PPText` `:14` / `RankText` `:19` HTxtLab + `combobox` Combobox `:25` ReadOnly + `AttrName` HTxt `:31`

### iteminheritwnd.layout — [ABS] — transfert d'enchant/forge entre équipements
`Root/IteminheritWnd` FrameWin `:4` rect `{{0.190,0},{0.102,0},{0.190,484},{0.102,410}}`
- `Border` Static `:10` : `Title1` STxtLab `:13` «放置轉出能力的裝備» (jaune) / `Title2` `:83` «放置繼承能力的裝備» / `Title3` `:90` «轉移虛寶：» / `Title4` `:97` «轉移能力：»
  - `inherit1` Border `:21` → `slot` Slot `:24` / `inherit2` Border `:43` → `slot` `:45`
  - `Info1` Border `:49` → `Slot` `:51` + `text` STxt `:54` «使用後，能將武器或防具上的強化階段，強化洞數，鑲嵌珠寶，附魔效果，轉移到另一個相同類型的武器或防具上。»
  - `Info2` Border `:33` → `text` STxt `:35` / `btn` Btn `:29` «進行轉移» / `HelpBtn` Btn `:104` / `Titlei5`,`Titlei6` SImg `:111,:117` (`DejaVuImage NumberRightMostH` = flèches →)

### changetypewnd.layout — [IMPL] (`ClassChangeDialog.cs` + `ClassWindow.cs` « Transform! », C→S 168)
`Root/ChangeTypeWnd` FrameWin `:4` rect `{{0.391,0},{0.211,0},{0.391,236},{0.211,454}}`
- `BackGround` Static `:10` : `SName` STxtLab `:13` «請選擇欲改變的英雄特質» ; `text` BTxtLab `:21` → `text` MultiLineEditbox `:24` ReadOnly ; **Feature1-8** Slot `:35-58` (grille 4×2 des traits) ; `OkBtn` Btn `:60`

### changetypeconfirm.layout — [PART] — `Root/CommonDlg` FrameWin `:4` rect `{{0.393,0},{0.395,0},{0.393,232},{0.395,192}}` : `MsgText0` STxt `:13` / `SImage0` Slot `:20` / `MsgText` `:29` + `MsgText1` `:23` «1234» / `OKBtn` `:35` / `CancelBtn` `:39` / `EditBox` `:43` / `CloseBtn` `:48`

### dyeingwindow.layout — [PART] (`ItemPreviewWindow.cs` — fenêtre dédiée absente)
`Root/DyeingWindow` FrameWin `:4` rect `{{0.232,0},{0.037,0},{0.232,426},{0.037,534}}`
- `Static` `:10` (nom `Root/DyeingWindow/`) : `Model` SImg `:13` `{{0.029,0},{0.117,0},{0.785,0},{0.871,0}}` (preview mannequin) ; `Finish` Btn `:16`
  - `ModelIcon0-9` Slot `:19-67` (colonne droite, 10 teintures)
  - `Part` Border `:69` → RB `1` `:71` «第一部位», `2` `:75`, `3` `:79`
  - `Text` STxtLab `:84` «此染劑有機會染成以下顏色» ; `Color1-5` Slot `:91-104` ; `help` IBtn `:106`

### petupgradewnd.layout — [IMPL] (`ElfWindow.cs` custom)
`Root/PetUparadeWnd` FrameWin `:4` rect `{{0.079,0},{0.109,0},{0.079,588},{0.109,440}}`
- `Static` `:10` : `Title1` BTxt `:319` «X進階寵物» / `Title2` `:160` «X輔助寵物»
  - `Pet1` Border `:166` + `Pet2` Border `:13` — **2 fiches pet symétriques** : `Main` Static `:195/:42` → `Icon` Slot + `Name` BTxt «xLV.30魔法的小喜馬拉雅犬» + `ExpBar` PBar ; `Value` Static `:212/:59` → `N1` «x攻擊/魔力» `N2` «x命中» `N3` «x爆擊/魔爆» `N4` «x攻速» + `T1-4` ETxtLab + `G1-4` SImg (`DejaVuImage AntiHoly/AntiFire/AntiChop/AntiNature`) ; `FightSKill` Static `:168/:15` → `S1-5` Slot + `Title` «x戰鬥技能» ; `PraySkill` `:285/:132` → `S1-5` + `Title` «x祈禱技能»
  - `Item` Static `:325` → `Title` BTxt «X特殊寶物» + `Slot` Slot `:335`
  - `Btn` IBtn `:339` (`DejaVuImage PetUpgrade*`) / `BtnName` BTxt `:347` «X開始進階» / `Help` IBtn `:313`

### newskilllist.layout — [IMPL] (`ClassWindow.cs` onglet Class Skills) — `Root/NewSkillList` DefaultWin `:4` `{0,380}×{0,486}`
- `Background` Static `:7` → `BGImage` SImg `:10` : `Text1` BTxtLab `:15` «共用技能爹斯» (skills communs) / `Text11` `:24` «職業技能爹死» (skills de classe) / `HelpBtn` IBtn `:32` ; `SkillCastText1` ETxtLab `:40` / `Money` ETxtLab `:45` ; `SkillList` Scroll `:52` `{{0.517,0},{0.056,0},{0.992,0},{0.946,0}}` (col. droite) ; `CommonSkillList` Scroll `:80` `{{0.008,0},{0.056,0},{0.483,0},{0.946,0}}` (col. gauche) ; `BorderL`/`BorderR` Border `:70,:75`

### slotlifelong.layout — [ABS] — gabarit ligne life-skill : `SlotLifeLong` Static `:4` `{0,142}×{0,40}` → `Icon` Slot `:7` `set:main image:FunctionSlot` + `Name` STxtLab `:13` «領地戰擊殺數» + `LvText` `:20` «疲勞 2» + `Text` ETxtLab `:26` «16000»

### scriptslot.layout — [PART] (`NpcDialogueWindow.cs` options custom) — `Root/ScriptSlot` DefaultWin `:4` Text=«1234»
- `Column` ICB `:7` (`DejaVu TreeList*`, Text=«1234») ; `Item` SImg `:16` → `Icon` Slot `:21` + `Name` STxt `:26` «一二三四五六七八九十» + `newImage` SImg `:32` `DejaVuImage NotificationNew` + `SelectFrame` Zframe `:41` Visible=False

### scriptwnd.layout — [PART] — **journal de quêtes « script »** (634×570, centré)
`Root/Script` DefaultWin `:4`
- `TreeStatic` Static `:8` → `Border` `:11` → `ScrollPane` Scroll `:14` (arbre chapitres, items `scriptslot`)
- `ScriptContent` Static `:32` `{{0.385,0},{0,8},{1,-8},{1,-8}}` : `Chapter` BTxt `:35` «榮譽高地» / `SubChapter` BTxtLab `:64` «榮譽高地» → `newImage` SImg `:92` / `help` Btn `:100`
  - `ScriptContent/TabControl` TabControl `:107` `{{0.031,0},{0.578,0},{0.969,0},{0.979,0}}` — **Tab1-5** DefaultWin `:112,:135,:159,:183,:207` («任務一»…«任務五», ID 0-4, Visible=False) → `Static1-5` → `TaskImg1-5` Slot (minsize 34px) + `Task1-5` BTxt (VertScrollbar)
  - `ScriptStory/TabControl` TabControl `:232` `{{0.031,0},{0.151,0},{0.969,0},{0.557,0}}` — `Tab1` `:237` «背景» → `static` + `Text` BTxt ; `Tab2` `:255` «完結» → `Text` + `static` (onglets histoire)

---

## Variante FR/production recommandée

- **Renforcement** : `strengthenwnd_na.layout` (= UI Aeria ; `_na_20` si le serveur monte à 20 niveaux — même structure, fenêtre 684px). `strengthenwnd`/`14`/`kr` = CN/TW/KR historiques.
- **Glyphes** : `jobglyph_na.layout` (FrameWindow + Slot21) ; `jobglyph.layout` est la version embarquée sans cadre.
- **Cube** : fr-full ère Aeria → les deux générations coexistent ; `newmagiccubewnd` (Brandt, imageset MagicCube + CNDejaVu2) est la plus récente et la plus riche (formule 8+4 slots, MList résultats). `magiccube00` = cube Kradia classique. `magicboxwnd` (轉化/洗鍊) = transmutation/reroll d'équipement — packets 612/613 déjà routés vers FeatureDialogWindow.
- **Études** : `engineeringstudy`/`jewelrystudy` non suffixées = production FR (ref-shot `Blueprint.png`).
- `collectwnd` = commission **récolte** (même gabarit que combinewnd, textes non préfixés « Test » → version finalisée ; combinewnd garde des placeholders « Test… »).

## GAPs (attendus mais absents du corpus ou non couverts)

- **Aucun layout « harvest/farm/ranch/mining » dédié** — la récolte passe par les ordres de travail (`worklistwnd`/`collectwnd`) et les bâtiments de guilde (`familybuilding*.layout`, hors scope liste mais présents au corpus, tous [ABS]).
- **Aucun layout « enchant/sort de classe »** au-delà de `strengthen*` — l'enchantement d'équipement se fait via scrolls dans `strengthenwnd` (slot `Scroll`).
- **Aucun Event/AutoWindow-event dans les layouts** — tous les noms d'events sont vides ; le glue code natif est irrecouvrable depuis le XML seul (reconstruire les bindings en C#).
- Layouts **Reforged absents du corpus fr-full** (notés dans la carte, `layout-impl-map.md` §Limites) : `PetWndV3.layout`, `StorageUI2.layout`, `limitless/LimitlessHud.layout` — non liés au craft.
- **20 layouts de la liste sont [ABS]** dont toute la pile Magic Cube (7), les 2 études, gembreaker, growthequipment(+item), iteminheritwnd, combinewnd, jobglyph(+_na), lifeskillitem, slotlifelong — c'est la plus grosse masse de travail UI craft restante ; **9 [PART]** (strengthen*, magicboxwnd, collectwnd, collectionset, dyeing, changetypeconfirm, scriptslot, scriptwnd) ; **6 [IMPL]** (worklistwnd, workresultwnd, collectionwnd, changetypewnd, petupgradewnd, newskilllist).
