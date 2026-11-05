# R3 — Fenêtres UI natives du client : craft / guilde / commerce

Date : 2026-11-03. Binaire : `client_game` (game_.exe projet, strippé `FUN_*`,
complété par `rf_game` Reforged — mêmes adresses — et `rf_itemmall`/`rf_neobank`/
`limitless.c` qui documentent des offsets). `decompiled.c` de client_game n'est
plus sur disque : corps lus via `re/index.sqlite` ; binaire analysé :
`eden_server/client/game_.exe` (objdump).

Statuts : **[VERIFIED]** lu dans le décompilé/désasm · **[HYPOTHESIS]** déduction ·
**[GAP]** inconnu précis.

---

## 1. Architecture UI : couche CUI au-dessus de CEGUI 0.7.1

Le client a 2 couches : **CEGUI 0.7.1** brut (documenté par
`eden_server/clientmod/limitless/limitless.c:29-43`, preuves rf_game) et une
couche maison « CUI » (fenêtres nommées + messages numériques).

### 1.1 API CEGUI (offsets prouvés dans limitless.c, valables client_game car rf_game partage les mêmes adresses)

| Rôle | Adresse |
|---|---|
| `WindowManager` singleton | `DAT_010c50b8` |
| `getWindow(wm,name)` | `FUN_00ba5c10` |
| `Window::setText` (vfunc+0x48) | `FUN_00b8a320` |
| `PropertySet::setProperty` | `FUN_00ba7730` |
| `addChildWindow` (vfunc+0x120) | `FUN_00b7f720` |
| `setVisible` (`d_visible` à win+0xb8) | `FUN_00b7f550` |
| `ILoadLayout` (wrapper CeUIFrameWindow.inl → loadWindowLayout) | `FUN_00795040` @ 0x00795040 |
| `IGetWindow` | 0x0078e0f0 |
| `testClassName("Window")` (vfunc+0x114), nom de classe | `DAT_00ffa1a0` |
| `CefStr` (String 92o) ctor/dtor | `FUN_0078ec40` / `FUN_00ba4880` |
| racine GUI | `DAT_01060770` |

[VERIFIED — source limitless.c + constaté : `FUN_0078ec40("SlotType")` dans
`client_game @ 0x0083e4a0` = construction d'une CefStr avant getProperty.]

### 1.2 Couche CUI : fenêtres nommées + messages

- `FUN_0078a370(name)` @ 0x0078a370 → thunk vers `FUN_007a5f10` : **show/toggle**
  la fenêtre par nom (crée paresseusement). [VERIFIED]
- `FUN_0078a7e0(name,&msg)` @ 0x0078a7e0 → `FUN_00799e20` : **poste un message**
  `{msgId:u32, lParam}` à la fenêtre. Le nom est parfois un `DAT_00f0xxxx`
  (std::string cachée). [VERIFIED]
- `FUN_00408f70(name)` : enregistrement de la fenêtre dans la boucle d'init
  (appelé 3× par nom — vraisemblablement RegisterClass/RegisterEvents/
  RegisterFactory). [HYPOTHESIS]
- `FUN_0079a640(name,1)` : déclare la fenêtre « instanciable » (hotkey/menu). [HYPOTHESIS]
- `FUN_0079d3e0` @ 0x0079d3e0 (1623 lignes) : **fonction d'init UI globale** —
  remplit les `DAT_00f0xxxx` de noms (`DAT_00f09f74`="NpcTalkWnd",
  `DAT_00f0a2d0`="ShopWnd", `DAT_00f0a4e8`="CombineWnd",
  `DAT_00f0ab04`="StrengthenWnd_NA", `DAT_00f0a474`="FamilyStorageWnd",
  `DAT_00f0a190`="StorageUI2"), puis `FUN_0079a640` (~90 fenêtres) et
  `FUN_00408f70` (~70). [VERIFIED]

### 1.3 IDs de messages observés [VERIFIED, sémantique partiellement HYPOTHESIS]

| msgId | Sens déduit |
|---|---|
| 0x3e9 | ouvrir/afficher (posté après remplissage) |
| 0x3ea | fermer |
| 0x3ec | rafraîchir / reconstruire le contenu |
| 0x3ee | ouvrir variante (FamilyAppWnd, WorkListWnd) |
| 0xff | annuler/fermer service (envoyé par le handler « close ») |
| 0xfe | résultat service (code à +0x16 du paquet) |
| 0xfd | poser l'id contexte (NPC courant) |
| 0x100 | début de cast (barre) |
| 0x88 | opération inventaire (`lParam`=sous-code : 0xa4 shop, 0x10 storage, 0x97 family) |
| 0x172/0x173/0x174/0x175 | remplissage données (Collect/Combine/WorkResult/FamilyApp) |
| 0x1b5 | données study (Jewelry/Engineering) |
| 0x1af | données HotelWnd |
| 0x193/0x194/0x195 | StallBuy/StallUI (état vendeur, nb étals) |
| 0xa2 / 0xa5 | ShopWnd : onglet/flags (+0x18,bool +0x1a) / sous-type shop (+0x1c) |
| 0x409 / 0x417 / 0x41d / 0x3f8 | données Strengthen / BaseLottery / Activities / NewMagicCube |
| 0xa7 | objet dialogue → NpcTalkWnd |
| 0x63/0x4d/0x4e | mises à jour QuestWnd (progression/sélection quête) |
| 0xb4 | castingbarwnd (durée = +0x16) |
| 0x2/0x3/0x11 | ChannelWndV2 (poste texte) |
| 0xaea/0x10c | TutorailWnd (notification ouverture fenêtre) |
| 0x68/0x69 | FamilyInfoWnd_NA refresh/close |
| 0x52 | MissionLog refresh |
| 0xf4/99 | DressRoomUI |

### 1.4 Dispatch S→C : receiver vector (NCID = index) [VERIFIED]

- `DAT_010c3ea4` = `std::vector<Generator*>` (begin+4/end+8/cap+0xC), prouvé par
  limitless.c:20-23 (« 621 gens natifs = CZ 0..620 »).
- Rempli par `FUN_00c93a30` @ 0x00c93a30 **[FAILED décompilé → objdump]** :
  621 blocs `alloc 0x20 → ctor/push_back(FUN_00c35250) → mov %ax,DAT_010c6xxx`.
  **L'ordre de push = le NCID.** Extraction scriptée `/tmp/genmap.txt` (621
  entrées, `ncid → vtable/DAT`). Chaque `mov %ax,DAT_010c6xxx` stocke le handle
  renvoyé par push_back (ex. NCID 304 → `DAT_010c6b68`, NCID 432 →
  `DAT_010c6d60`, NCID 496 → `DAT_010c6e60`, NCID 285 → `DAT_010c6b28`).
- Deux formes de générateur : (a) objet 0x20 dont +0 = **vtable dans .text**
  (ex. 0xc471a4) ; (b) ctor `FUN_00c5e610`/`FUN_00c69f60` posant
  `*obj=&PTR_FUN_00c5e628`.
- La « vtable .text » a la forme `{0x00cd23a0, execThunk, …}` ; vtable[1] =
  thunk Execute : `alloc 0x30/0x34 → ctor commande (FUN_00c376a0/FUN_00c5e6c0)
  → vfunc+8`. Le `operator()` de la commande désérialise puis appelle le handler
  métier `FUN_006f*/0070*`. [VERIFIED objdump client/game_.exe 0xc93a30+]
- `FUN_00d75da0` = pompe de dispatch sur thread principal ;
  `FUN_00d776d0` draine le ring par session (limitless.c). [VERIFIED doc]
- **[GAP]** jointure NCID→FUN_ handler non faite pour les 621 : mécanique prête
  (vtable[1] → thunk → ctor → `operator()` → handler) ; à scripter sur la
  table `/tmp/genmap.txt`.

---

## 2. Le flux NPC → dialogue → fenêtre [VERIFIED]

### 2.1 Interaction

- `FUN_0071ded0` @ 0x0071ded0 : `SetCurrentNPC` — écrit playerStruct+0x4f0 =
  npcId, +0x51c = dernier NPC. Appelé par **tous** les handlers d'ouverture.
- PlayerStruct (retour `FUN_006e5130`) champs utiles : +0xc container courant,
  +0x474 = **voie de craft** (1=Jewelry, 2..5=Engineering), +0x47c/+0x480 =
  opérations/services en cours, +0x484+idx*4 = **slots « work »** (12 max).

### 2.2 Dialogue

- `DAT_00f09f74` = "NpcTalkWnd" (classe `CUINPCTalkWnd`,
  `..\\CeguiDialog\\` implicite, erreurs `CUINPCTalkWnd failed assigning…`
  @ 0x00f101e0).
- `FUN_00711440` @ 0x00711440 : handler vue dialogue (debug) — montre le format :
  options = vecteur de records **0x1c octets** (label), id option = index ;
  msg 0xa7 pousse l'objet dialogue vers la fenêtre.
- `FUN_0081a5e0` @ 0x0081a5e0 (classe NpcTalkWnd, l.859573+) : **clic option** —
  itère options (vecteur this+0x10c..0x110 d'ints, +0x11c..0x120 records 0x1c) ;
  si 1 choix → `FUN_006cc550(cmdId)` puis msgs QuestWnd 0x63/0x4d/0x4e ; si
  plusieurs → `FUN_0081a0f0` ; si aucun → ferme (0x3ea) + `FUN_0078dde0`
  (fin d'interaction si flag joueur &0x33==0x33).
- `FUN_006cc550` @ 0x006cc550 : **émet la réponse de dialogue** — alloue
  commande 0x14 via `FUN_004012e0`, ctor `FUN_006b47f0(cmdId,1)`, envoie par
  `FUN_00b5ee10`. = `CNE_CZ_ZoneServerReplyDialog` (op 11). [VERIFIED mécanisme,
  HYPOTHESIS pour l'op — la classe a `param_1[1]=&DAT_006c9360`,
  `param_1[4]=&PTR_FUN_00f01870`, champ u16=cmdId.]
- `FUN_0070f5d0` @ 0x0070f5d0 : handler « dialog option type » — switch sur le
  type à +0x14 : 0x355/0xc56 → son 0xf + ferme NpcTalkWnd ; 0xc0 → son 0x2b +
  effet "s02051" ; 0x2df → `FUN_006cb3c0(0x1e)` (autre sender). Options de
  quête/GM routées par ce type.
- `FUN_006f5d20`-classe "FunctionMenu_NA" (`CUIFunctionMenuWnd`, string
  0x00f14d88) : le **menu contextuel NPC** (boutons boutique/fortification/…),
  rafraîchi par msg 0x10b/0x3ec depuis les handlers work/family.

### 2.3 Le grand répartiteur « type de service » (le cœur du flux)

Trois handlers siblings switchant sur un `type/service id` du paquet :

- `FUN_006fe2c0` @ 0x006fe2c0 — **ouverture de la fenêtre de service**
  (switch sur +0x18, avant : `SetCurrentNPC(+0x14)`) :

  | type | fenêtre | msgs |
  |---|---|---|
  | 9 | `StrengthenWnd_NA` | 0x409 (lvl à +0x1a) + 0x3e9 |
  | 10 | `CombineWnd` | 0x173 (données) + 0x3e9 |
  | 0x16 | `CrystalLotteryWnd` | 0x3e9+0x3ec |
  | 0x96 | `InGameLotteryWndVer2` | 0x3e9+0x3ec |
  | 0x97 | `BaseLotteryWnd2` (+0x1c==2 → 0x417) | 0x3e9 |
  | 0x68 | `JewelryStudy` si player+0x474==1, `EngineeringStudy` si 2..5 | 0x1b5 + 0x3e9 |
  | 99 | `ChanceLuckyBag` | 0x3e9 |
  | 0x1c | `NewMagicCubeWnd` | 0x3f8 |
  | 0x98 | `ActivitiesWnd` | 0x41d + 0x3e9 |

- `FUN_0070fe30` @ 0x0070fe30 — ouverture depuis la **vue dialogue** (+0x18) :
  0xc/0xd/0xe → `CollectWnd` (0x172+0x3e9) ; 0xf/0x10/0x11 → `CombineWnd`
  (0x172+0x3e9) ; puis inventaire 0x88/0x96|0x44 + ferme NpcTalkWnd.
- `FUN_006ff390` @ 0x006ff390 — **début de cast service** (+0x14) : msg 0x100 à
  la fenêtre + son 0x2f/0x30/0x53 + caption localisée (0x233 strengthen,
  0x3ba/0x3bb/0x3bc/0x3bd combine×4, 0x4f7 combine-10, 0xf78..0x108f selon
  player+0x474, 0xf7c, 0x121b, 0x1bfb) + `castingbarwnd` 0xb4.
- `FUN_006ffa70` @ 0x006ffa70 — **résultat service** (+0x14 type, +0x16 code) :
  msg 0xfe à la fenêtre ; succès → poste string localisée à ChannelWndV2
  (0x3cd combine, 0x1bfd/0x1bfc cube) + sons 0x3a/0x3b/0x54/0x55.
- `FUN_006ff800` @ 0x006ff800 — **annulation/fermeture service** (+0x14) :
  msg 0xff ; types : 9 Strengthen, 10/0xf/0x10/0x11 Combine, 0xe Collect,
  0x16/0x96/0x97 loteries, 0x68 study, 99 ChanceLuckyBag, 0x1c NewMagicCube,
  **0x1e `IteminheritWnd`**, **0x1f `MagicBoxWnd`** ; puis `castingbarwnd` 0xb4.
- `FUN_006feb20(type)` : mémorise le « service courant » (0x2f strengthen /
  0x30 autre / 0x53 cube) — utilisé pour l'annulation serveur. [HYPOTHESIS]

**Énum service consolidée** [VERIFIED] : 9 strengthen · 10/0xe/0xf/0x10/0x11
combine variants · 0xc/0xd collect · 0x16 crystal-lottery · 0x1c magiccube ·
0x1e item-inherit · 0x1f magicbox · 0x68 lifeskill-study (jewelry/engineering) ·
0x69 jewelry-study direct · 0x96/0x97 ingame/base lottery · 0x98 activities ·
99 luckybag · 0x355/0xc56 dialog-end · 0xc0/0x2df dialog-spéciaux.

---

## 3. Inventaire des fenêtres (runtime-name ↔ FUN_* ↔ rôle)

### 3.1 Craft / services NPC

| Fenêtre runtime | Source C++ / strings | Handlers S→C (remplissage/ouverture) | Classe CUI (méthodes) |
|---|---|---|---|
| `StrengthenWnd_NA` | `CeguiDialog\\StrengthenWnd.cpp` @ 0x00f16774 ; widgets `/StrengthenBtn` `/StrengthenLevel` `/StrengthenFactory` `StrengthenSlot` `StrengthenWnd_NA` `StrengthenFactory` | `FUN_006fe2c0` (ouvre, msg 0x409) ; `FUN_006ff390` (cast) ; `FUN_006ffa70` (résultat 0xfe) ; `FUN_006ff800` (annule 0xff) | 0x008bxxxx–0x008cxxxx : `FUN_008c00d0` (notif TutorailWnd), `FUN_008c2c80`, `FUN_008c0330`, `FUN_008c5d20` (bind `/StrengthenLevel`), `FUN_008c3ac0` (ctor, `__FILE__`) |
| `CombineWnd` | `CeguiDialog\\CombineWnd.cpp` @ 0x00f1eef8 | `FUN_0070fe30` (types f-11, msg 0x172), `FUN_006fe2c0` (type 10, 0x173) ; cast/result/cancel ci-dessus | `FUN_009924d0`…`FUN_00996b00` (0x0099xxxx) |
| `CollectWnd` | `CeguiDialog\\CollectWnd.cpp` @ 0x00f1e988 ; `/CollectArea/BuildingBorder` | `FUN_0070fe30` (types c-e, 0x172+0x3e9) ; résultat 0xfe | classe 0x0099xxxx (voisine Combine) |
| `WorkListWnd` | `WorkListWnd` 0x00f0a4f4, `WorkListItem` 0x00f0ae1c | `FUN_00711a70` @ 0x00711a70 (remplit slots +0x484.., msgs 0x3ee/0x3ec) ; `FUN_00711d90` ; `FUN_0070fff0`/`FUN_0070ff50` (résultat → `WorkResultWnd` 0x3ea/0x174+0x3ec) | `FUN_0098d5d0`…`FUN_009956a0` (0x0098-0x0099), `FUN_008914e0` (registre 0x3e9/0x3ec) |
| `WorkResultWnd` | `CeguiDialog\\WorkResultWnd.cpp` @ 0x00f1ec08 | cf. `FUN_0070fff0`/`FUN_0070ff50` (msgs 0x3ea + 0x174) | — |
| `MagicBoxWnd` | `CeguiDialog\\MagicBoxWnd.cpp` @ 0x00f1c490 | type service 0x1f ; `FUN_006fd530`,`FUN_006fceb0`,`FUN_006fc660` | `FUN_00951700`,`FUN_009504b0`,`FUN_00953100`,`FUN_009547a0`,`FUN_00958370` (0x0095xxxx) |
| `NewMagicCubeWnd` + `NewMagicCubeRecipeWnd` | `NewMagicCubeWnd` 0x00f0ab88, `MagicCube03.layout` 0x00f1b1e8, `NewMagicCubeRecipeDetailWnd.layout`, images `set:MagicCube image:Function0X{normal,hover,press,active}` + `Fail`/`Success` | type 0x1c (msg 0x3f8) ; résultat 0xfe + strings 0x1bfb/0x1bfd/0x1bfc | 0x0095xxxx |
| `IteminheritWnd` | string + type 0x1e | `FUN_006fcee0`, `FUN_006ff800` | `FUN_009ec3c0`,`FUN_009ec890`,`FUN_009efb10`,`FUN_009f00e0` (0x009exxxx) |
| `JewelryStudy` / `EngineeringStudy` | `CeguiDialog\\CeUIJewelryWnd.cpp` / `CeUIEngineeringWnd.cpp` | type 0x68/0x69 (msg 0x1b5+0x3e9), choix selon player+0x474 | — |
| `DyeingWindow` | `DyeingWindow` 0x00f0a520, `DyeingColor`, `g_DyeingColor0..3` | `FUN_006feb70`, `FUN_007129a0` | `FUN_00997810`,`FUN_00997d70`,`FUN_00997f20`,`FUN_00998ae0`,`FUN_00998e60`,`FUN_00998fb0` (0x0099xxxx) |
| `GemBreaker` | `GemBreaker` 0x00f0a764 | enregistré `FUN_00408f70` (f793 l.1256) | — |
| `GrowthEquipment` | 0x00f0a908, `GrowthEquipmentItem.layout` | `FUN_0074af40` (aussi Iteminherit/Dyeing partagé) | — |
| `JobGlyph_NA` | `JobGlyph_NA` 0x00f0a258, `JobGlyphItem` | enregistré | — |
| `ChangeTypeWnd` | 0x00f0ab48, `ChangeTypeConfirm.layout` | — | — |
| `RemoveRuneWnd` | `CeguiDialog\\RemoveRuneWnd.cpp` @ 0x00f18038 | — | — |
| `LifeSkillItem` | `LifeSkillItem.layout` 0x00f217c8, `data\\db\\C_LifeSkill.ini` 0x00f4ded0 | lifeskill data S→C (543/544) — handler à rattacher | — |

### 3.2 Guilde (Family) / ville de guilde (VDG)

Runtime-names : `FamilyWnd_NA` 0x00f0a394, `FamilyInfoWnd_NA` 0x00f0a3a4,
`FamilyAppWnd` 0x00f0a438, `FamilyOrderWnd_NA`, `FamilyMessageWnd_NA`,
`FamilyStorageWnd` 0x00f0a474, `FamilyStorageLogWnd` 0x00f0a5e0,
`FamilyTownListWnd` 0x00f0a448, `FamilyMemberFindingWnd`,
`FamilyCenter`, `FamilyBuilding_Forest`/`_Mountain` (2 layouts !),
`FamilyBuildingItem`, `FamilyBattle*`, `FamilyFindingWind/Info`,
`TerritoryMainWnd/List`. Layouts dlg : `FamilyDonateDlg.layout`,
`FamilyJobNameDlg.layout`, `FamilyMessageDlg.layout`,
`FamilyCommentDlg.layout`, `FamilyBulletinDlg.layout`,
`FamilySetPrivilegeDlg.layout`. Sources : `FamilyAdministrationWnd.cpp`,
`FamilyBuildingWnd.cpp`, `FamilyTownListWnd.cpp`, `FamilyAppWnd.cpp`,
`FamilyStorageTabWnd.cpp`, `FamilyStorageLogWnd.cpp`, `FamilyInfoWnd.cpp`,
`FamilyOrderWnd.cpp`, `FamilyMessageWnd.cpp`, `FamilyMemberFindingWnd.cpp`.

Handlers :
- `FUN_006f5050` @ 0x006f5050 — ouvre `FamilyCenter` (0x3e9) + inventaire
  0x88/0x97 + SetCurrentNPC → **CNC_CZ_ClientEnterFamilyBuilding** (op 483)
  ou EnterFamilyCreation (347). [VERIFIED ouverture, HYPOTHESIS op]
- `FUN_006f1e30` @ 0x006f1e30 — ouvre `FamilyAppWnd` (0x175+0x3ee) — option
  dialogue « rejoindre guilde ».
- `FUN_006f3300` @ 0x006f3300 — FamilyInfoWnd 0x69 + ferme NpcTalkWnd (fin
  d'interaction guilde).
- `FUN_006f6670` @ 0x006f6670 — fin création/join guilde : nettoie NPC courant,
  FamilyInfoWnd 0x68, FamilyWnd 0x3ea, FunctionMenu 0x3ec.
- `FUN_006f4770` @ 0x006f4770 — mise à jour objet family-town (slot +0x1c,
  linked-list this+0x120), refresh FamilyCenter/ZoneMapUIVer2.
- `FUN_006f4980`, `FUN_006f5220` — autres msgs FamilyCenter (données bâtiments).
- Classe FamilyBuilding : `FUN_00984580`, `FUN_00984950`, `FUN_009864b0`,
  `FUN_00986e80`, `FUN_009876c0`, `FUN_0097ddb0` (0x0097-0x0098) ; widgets
  `/BuildingArea/*`, `/CollectArea/BuildingBorder/*`, `/Static1/BuildingListBorder/BuildingPane`, `FamilyBuildingItem`, `ProduceBuildingText`.
- FamilyAdministration : `FUN_0097ebe0`,`FUN_00980cf0`,`FUN_009838e0`.
- FamilyStorageTab : `FUN_0099f460`. FamilyInfoWnd classe : `FUN_008b9bf0`,
  `FUN_008b9f10`, `FUN_00a96300`.
- **GAPS** : donate (`CNE_CZ_ZoneServerFamilyDonateMoney` op159), statue (161),
  réparation bâtiment (180), storage put/get (177/178), emblème (82/384) —
  les boutons appellent des senders `FUN_006c*` non encore labellisés (§5).

### 3.3 Commerce

| Fenêtre | Strings | Handlers S→C | Classe |
|---|---|---|---|
| `ShopWnd` (DAT_00f0a2d0) | `CeguiDialog\\ShopWnd.cpp`, `ShopItem.layout` | `FUN_006fff50` @ 0x006fff50 = **EnterShop (304)** : SetCurrentNPC(+0x14), 0x3e9, 0xfd=npcId, 0xa2=onglet(+0x18), inv 0x88/0xa4 ; `FUN_006fef80` = variante (+0x1c → msg 0xa5, +0x1a bool) = EnterSpellShop/RepairShop/IdentifyShop/InlayShop (349/350/365/370) | `FUN_008afc70`,`FUN_008b4940`,`FUN_008b56c0` (0x008a-0x008b) |
| `TradeWnd` | `CeguiDialog\\TradeWnd.cpp` | `FUN_006fc520`..`FUN_006fdc50` (handlers 331-340/353/394-395) | `FUN_0084f5f0`..`FUN_00853077` (0x0084-0x0085) |
| `StallUI` + `StallBuy` | `CeguiDialog\\StallWnd.cpp`, enregistrés ×2 | `FUN_006fe6b0` (compteur étals player+0xc+0x2d0, msgs 0x195/0x3ea) ; `FUN_006fe730` (+0x15: 2=compte++,3→StallBuy 0x194/0x193, autre→0x3ec ; 0→erreur string 0xeb0) ; `FUN_00701d20` | `FUN_008ecee0`…`FUN_008f1850` (0x008e-0x008f, ~800 lignes) |
| `AuctionWnd` (regroupe Buy/Sell) | `CeguiDialog\\AuctionSellWnd.cpp`, `AuctionBuyWnd.cpp` | `FUN_006fc5d0`,`FUN_006fc600`,`FUN_006fcdf0`,`FUN_006fce20`,`FUN_006fce50` (425-430) | `FUN_00883a60`,`FUN_00883d40`,`FUN_00883f70`,`FUN_00a617e0`,`FUN_00a63270`,`FUN_00a669e0`,`FUN_00a6a650` |
| `StorageUI2` | 0x00f0a190 | `FUN_006ff0d0` @ 0x006ff0d0 (2 params : npcId @+0x1c du 2e → SetCurrentNPC ; flag @+4 → si conteneur player+0xc+0x2f8 ouvert : `FUN_00b5eb10(pkt)` = **renvoie un paquet C→S** ; sinon 0x3e9 + inv 0x88/0x10) ; `FUN_006fe0c0`, `FUN_006fd530`, `FUN_006fd700` | `FUN_0084a5f0`,`FUN_0084b350`,`FUN_0084f200` |
| `HotelWnd` / `HotelStorageWnd` / `HotelPrivilegeDlg` | `CeguiDialog\\HotelWnd.cpp` etc. | `FUN_006f3af0` @ 0x006f3af0 = **OpenHotel (539)** : type @+0x14 → 0=HotelStorageWnd, 4=PartyWnd, autre=HotelWnd (msg 0x1af+0x3e9) ; ferme NpcTalkWnd | `FUN_009c9920`,`FUN_009ca2c0` (0x009c) ; `FUN_006f20e0`,`FUN_006f7920` |
| `ItemMall` + `ItemmallMailWnd` | `CeguiDialog\\ItemMallWnd.cpp`, `ItemMallMailWnd.cpp`, `/Data/ItemMall/Slot` | `FUN_00854dc0` (0x0085) | — |
| `SpecialCoinUI2_NA` | 0x00f0a264, `SpecialCoin`, `SpecialCoinPS.layout`, cmd GM `RewardSpecialCoin` | `FUN_006f5790`,`FUN_006f5930`,`FUN_006fa6a0`,`FUN_00703bb0`,`FUN_00703d30`,`FUN_00703e10`,`FUN_0071f060`,`FUN_007076b0` | `FUN_0085f760`,`FUN_00879780` |
| `FamilyStorageWnd`/`LogWnd` | §3.2 | — | `FUN_0099f460` (tab) |
| `PaySystemWnd` | `CeguiDialog\\PaySystemWnd.cpp` | enregistré | — |
| `BuyBack` | ops 512/185, strings `CNC_ZMis_ZoneServerBuyBack*` | handlers à rattacher | — |
| `castingbarwnd` | string | msg 0xb4 (durée) | — |

### 3.4 Autres enregistrées (extrait FUN_0079d3e0, [VERIFIED])

`InventoryWnd_NA2`, `EquipmentWnd_new2`, `EquipmentWndSwitch`,
`MiniInfoWnd_EquipmentSwitch`, `BagManagement2`, `DressRoomUI`,
`CharacterWnd_new2`, `JobWnd_NA`, `JobPointList`, `SpellLearnWnd`,
`SpellCardWnd`, `SpellWnd`, `QuestWnd`, `MissionLog`, `QuestLog`,
`AutoQuestList`, `CommunityWnd`, `AddressBookWnd`, `OtherEquipmentWnd`,
`PVPEquipmentScanner`, `PrestigeWnd_new`, `Achievement_Ver2`,
`AchievementGroup`, `AppellationWnd_ver2`, `collectionWnd_Ver2`,
`CollectionSet`, `CollectionPointList{,2}`, `MonsterInfo`, `RankMainWnd`,
`RankInfoWnd`, `RankPVPInfoWnd`, `SearchTeamWndV2`, `ArenaMain`,
`ArenaInformation`, `ArenaDuel2/3`, `TerritoryMainWnd`, `TerritoryList`,
`InstanceDungeonControl`, `TowerWnd`, `SkyTower`(source), `GreedyIsland*`,
`FamilyBattle*`, `Wedding`, `EvaluateWnd`, `EvaluateMemberWnd`,
`BattleCrusadeWnd`, `Cooperation_Ver2`, `ChannelWndV2`, `PartyWnd`,
`CoRideWnd`, `PetUpgradeWnd`, `PetLotteryWnd0-9`, `CrystalLotteryWnd`,
`DiamondLotteryWnd`, `BaseLotteryWnd2`, `InGameLotteryWndVer2`,
`ChanceLuckyBag`, `CeUILotteryWnd/DiamondLottery/LotteryCount` (sources),
`ScriptWnd` (source `ScriptSlot.layout`), `MessageWnd`, `MessageBoard`,
`NotificationTipWnd`, `TriggerAreaWnd`, `OptionControlUIVer2`,
`OptionReadMeUI`, `AdventurelogWnd`, `ZoneMapUIVer2`, `TargetInfoWnd1`,
`SetConditionWnd2`, `StatusDefendWnd3`, `RecommendActivitiesWnd`,
`StarWayWnd`, `DestinyRoadWnd`, `RaidWnd/Raid25List`, `SearchTeamWndV2`,
`MailReadWnd/MailSendWnd/MaillistWnd`, `PersonalCardWnd/View`,
`StatusSupporterWnd`, `ItemSupporterWnd`, `ItemShiftWnd`,
`GladiatorTeam*`, `CNSecurityCard`, `CaptchaWnd`, `WebBrowserWnd`,
`TransportWnd`, `TutorailWnd`, `GuideWnd`, `FunctionMenu_NA`,
`NpcTalkWnd`, `FamilyCenter`, `SpecialCoinUI2_NA`, `Login`,
`SelectCharacterWnd`, `CreateCharacterWndVer2`, `WorldServerListWnd`,
`RollWnd`, `ChangePetNameWnd`, `DuelRequestWnd`, `LootRuleWnd`,
`PVPLookEquipWnd`, `FaceChange`, `LiveUpdate`, `QuestlogWnd/QuestLog`
(doublon), `ScriptWnd`, `EvaluateWnd`, `SkyTowerWnd`…

---

## 4. Liaison NCID ↔ fenêtre (côté S→C, mapping opcode Eden.Core ↔ handler client_game)

| NCID (Eden.Core) | Nom | Handler client_game | Effet [VERIFIED] |
|---|---|---|---|
| 285 | CNC_CZ_ClientDisplayDialogView | (gen #285 → DAT_010c6b28) | vue NpcTalkWnd (msg 0xa7) |
| 304 | ClientEnterShop | `FUN_006fff50` | ouvre ShopWnd |
| 349/350/365/370/366 | Enter{Spell,Repair,Identify,Inlay}Shop / ExchageItemService | `FUN_006fef80` (famille) | ShopWnd sous-type 0xa5 |
| 357 | ClientEnterStorage | `FUN_006ff0d0` (famille) | StorageUI2 + requête C→S |
| 332-340/353/394-395 | ClientTrade* | `FUN_006fc520`..`FUN_006fdc50` | TradeWnd |
| 375-379 | ClientVendor* | (handlers région 0x006f-0x0070) | étal joueur |
| 425-430 | ClientAuction* | `FUN_006fc5d0`-`FUN_006fce50` | AuctionWnd buy/sell |
| 432-436 | ClientEnterStrengthen/Casting/Cancel/Result/Modify | gen #432-436 (DAT_010c6d60..70) → `FUN_006fe2c0`,`FUN_006ff390`,`FUN_006ffa70`,`FUN_006ff800` | StrengthenWnd_NA |
| 483 | ClientEnterFamilyBuilding | `FUN_006f5050` (famille FamilyCenter) | FamilyCenter |
| 484-487 | ClientFamilyContainer* / InstanceData | (gen #484..) | FamilyStorage/Instance |
| 496-500 | ClientEnterWorks / PlayerWorkUpdate / Result / ResultEnd | `FUN_00711a70`/`FUN_00711d90` (liste) ; `FUN_0070ff50`/`FUN_0070fff0` (résultat) | WorkListWnd / WorkResultWnd |
| 501 | ClientEnterFamilyInstanceCreation | — | FamilyInstance |
| 502/503/514 | ClientFamilyTownQuery / TownMemberQuery / EnterFamilyTownList | (gen #502/503/514 → DAT_010c6e78..) | FamilyTownListWnd |
| 505-508 | ClientFamilyStorage* | (gen #505-508) | FamilyStorageWnd/Log |
| 539 | ClientOpenHotel | `FUN_006f3af0` | HotelWnd/Storage/Party |
| 543/544 | ClientPlayerLifeSkill / LifeSkillUpdate | (gen #543/544) | LifeSkillItem (données) |
| 564 | ClientOpenInstanceDialog | (gen #564) | dialogue instance |
| 585 | ClientOpenMissionShop | (gen #585) | shop mission |
| 612/613 | ClientOpenMagicBoxDialog / MagicBoxResult | (gen #612/613) | MagicBoxWnd |
| 415/416/421/422 | ClientElfWorkResult*/ElfGoToWork/ElfDecompose | (gen #415..) | elf-work UI |
| 472 | ClientUpdateCollection | `BindEntryData` côté Godot ; natif : collectionWnd_Ver2 | — |
| 384 | ClientSelectFamilyEmblem | — | emblème guilde |
| 346/347 | ClientChangeFamily / EnterFamilyCreation | `FUN_006f3300`/`FUN_006f6670` (famille) | FamilyInfo/Wnd |

**[GAP]** les handlers listés « gen #NNN » sont certains d'exister (slot
rempli à l'init) mais la jointure vtable→thunk→commande→`FUN_*` reste à
scripter ; adresses non confirmées individuellement.

---

## 5. Émission C→S : bloc `FUN_006cb3f0`…`FUN_006d1xxx` (210 senders) [VERIFIED]

- Pattern constant : `if (DAT_0105f410==0) return; obj=alloc(FUN_004012e0,size)
  → ctor FUN_006b*/FUN_006c*(args…,1) → FUN_00b5ee10(obj)`.
- `FUN_00b5ee10` @ 0x00b5ee10 : sérialise (archive `FUN_00b4e740` /
  `FUN_00d75cb0` / `FUN_00d76610`) puis envoie — framework boost-style,
  objets multi-héritage (vtable `PTR_LAB_006b86dc` sérialise /
  `PTR_LAB_00c848d0` exécute / `PTR_FUN_00f01870` info-type partagée).
- 161 senders contigus `FUN_006cb3f0`–`FUN_006d1fff` (+49 ailleurs) extraits :
  `/tmp/sender_callers.txt` (sender → fonctions appelantes) et
  `/tmp/sender_ctor.txt` (sender → ctor de commande). [VERIFIED]
- Identifiés :
  - `FUN_006cc550` = reply dialogue (op 11, HYPOTHESIS), appelé par
    `FUN_0081a5e0`/`FUN_0081a0f0` (NpcTalkWnd), `FUN_00817590`,
    `FUN_008176a0`, `FUN_00a138a0`.
  - `FUN_006cb7a0` = sender **sans args** (obj 0x18, ctor `FUN_006b8660`) appelé
    par NpcTalk-close (`FUN_00a04500`), Shop (`FUN_008b5100`), Stall
    (`FUN_008f0890`), WorkList (`FUN_00994ed0`/`FUN_0099d6a0`), Auction
    (`FUN_00883b00`), Family (`FUN_0094e0b0`/`FUN_009df090`/`FUN_0097d970`),
    NpcTalk (`FUN_0081aa00`) → **« quitter le service NPC »**
    [HYPOTHESIS = CNE_CZ_ZoneServerLeaveShop op 63].
  - `FUN_006ce1e0` (ctor `FUN_006c1ae0`) appelé par `FUN_00711d90` (WorkList) →
    émission ordre de travail [HYPOTHESIS = PlayerWork op 170].
  - `FUN_006cb7f0` (ctor `FUN_006b40a0`, 3 args type/slot/slot2) appelé par
    `FUN_0074f970` (types 0/4/5) → poser item sur service [HYPOTHESIS].
  - `FUN_006cb850` (ctor `FUN_00750b80`) : 15+ appelants 0x0075xxxx/0x0084xxxx —
    sender d'action container (move/swap) [HYPOTHESIS].
- **[GAP principal]** label NCID de chaque sender : le vtable de la commande
  (PTR_LAB_00xxxxxx dans le ctor) contient `GetNCID()` mais les corps des
  petits vfuncs ne sont pas décompilés proprement (`return imm` introuvable) —
  résolution manuelle par vtable → vfuncs → objdump possible, non faite.

---

## 6. Drag&drop / slots / tooltips [VERIFIED partiel]

- `FUN_0083e4a0` @ 0x0083e4a0 (~430 lignes) : handler clic/drag d'un slot
  d'inventaire : lit `param_2+0x20` flags (bit31=?, bit2/3), vérifie la
  propriété widget **`SlotType`** (`FUN_0078ec40("SlotType")` + compare
  `BagSlotItem` via `FUN_00b89690`/`FUN_00b7e2d0`), appelle
  `FUN_0083e3f0(itemId)` ; cas bit3 → aperçu `DressRoomUI` (msgs 0xf4/99) ;
  bit2 → drop via `FUN_0083dda0`. Types de slots : `BagSlotItem`,
  `StorageSlotItem`, `StrengthenSlot`, `ProductSlot`, `ItemSlot`, `JobSlot`,
  `SkillSlot`, `SlotLifeLong`, `FunctionSlot`, `LikeHateSlot`, `PCardSlot`,
  `SupporterSlot`, `WorkListItem`, `SpecialCoinItem`, `JobGlyphItem`,
  `FamilyBuildingItem`, `TrainAttrsSlot*`, `CEGUI/SlotImageItem`,
  `DejaVu/SlotImageItem` (type widget). [VERIFIED]
- Tooltips : widget chemins `Root/TipDetail/Slot1..4`, `/TipItem/Slot1/Slot2`
  → fenêtre dédiée « Tip » (contenu piloté par les mêmes msgs CUI). [HYPOTHESIS]
- Layouts chargés par nom `"X.layout"` → pkgfs sert `uidata\layouts\X.layout`
  (limitless.c:56-60 ; ex. `limitless/LimitlessHud.layout` — sous-dossier
  accepté dans le nom). [VERIFIED]
- Enregistrement des events boutons : pattern CEGUI `Register events`
  (`"XxxWnd Register events failed"` strings) dans les ctors 0x008x-0x00ax —
  handlers d'events = méthodes de la classe (vtable CUI), **non tracés par
  `callers`** (vfuncs). [VERIFIED + limite outil]

## 7. Erreurs / strings métier (exemples cités)

- Strengthen : `StrengthenWnd Register events failed` 0x00f167c8,
  `StrengthenWnd failed assigning component pointers to object` 0x00f167f0,
  `StrengthenFactory Register Events Failed` 0x00f166d8.
- CollectSet debug : `CollectSetItem%dStrengthen=%d\n` 0x00f23354.
- LifeSkill : `ERROR - Init LifeSkill to ItemData TemplateID[%ld] is NULL`
  0x00f4baf8 ; table `.\data\db\C_LifeSkill.ini` 0x00f4ded0.
- NPCTalk : `NPCTalk`, `NPCTalk <rest_input>` (commande GM/debug),
  `ERROR!!-GetNPCTalkPMS- MissionID [%ld >= 4201]` 0x00f4d050.
- Guide : seuils d'ids guides 0xc355/0xc3fe/0xc477..0xc4ab/0xc4da/0xc4dd/
  0xc559/0xc588/0xc55f/0xc590/0xc5e8/0xc5f2 (msgs GuideWnd 0x1c0+code
  0x13/0x14/0x19/0x1c/0x1e/0x20 — `FUN_00711440`).

## 8. Mods Reforged (bonus)

- `rf_itemmall` (dll 0x10000000) : patch « purchase hook » (scan de signature
  `FUN_10001db0(0x1fd700,…,9)`), « paced purchasing », suppression shader-cache,
  `ItemMall.ini` — **prouve qu'un point unique d'achat ItemMall est hookable
  dans game_.exe** (adresse précise non extraite — [GAP]).
- `rf_neobank` : greffe une banque custom `neo/Bank/*` sur `Root/StorageUI`
  (layout XML embarqué @ 0x10022280) — modèle de greffe CEGUI.
- `limitless` (source lisible) : hook receiver zone `DAT_010c3ea4` + HUD/pass
  CEGUI — pattern exact pour intercepter/étendre les fenêtres natives.
- `rf_gamefix` : patches FPS/refresh, sans lien UI.

## 9. Ce qui reste inconnu — méthode de résolution

1. **NCID→handler (S→C)** : table `/tmp/genmap.txt` prête ; pour chaque ncid :
   `vtable[1]` → thunk (objdump) → `call ctor` → vtable commande →
   `operator()` → `FUN_00xxxx` handler. Scriptable.
2. **Sender→NCID (C→S)** : pour chaque `FUN_006c*` : ctor `FUN_006b*` → vtables
   `PTR_LAB_*` → chercher la vfunc retournant l'imm NCID via objdump
   (`mov eax,imm`/`return`), ou croiser layout de champs avec les classes
   `lapis::CNE_CZ_*` de `zone` (non-strippé).
3. **Classe fenêtre ↔ runtime-name** : la table d'enregistrement
   (name→ctor CUI) vit dans `FUN_0079a640`/`FUN_00408f70` — les noms sont
   [VERIFIED] mais la correspondance ctor↔nom exige de lire les args de
   chaque appel (non fait).
4. **Sémantique complète des msgIds** (0x400+, ex. 0xaea/0x417/0x41d/0x1b5) —
   à lire dans les méthodes des classes fenêtre (0x008x-0x00ax) au cas par cas.
