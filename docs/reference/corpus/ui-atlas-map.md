# Carte atlas UI → panneaux (`uidata/imagesets` ↔ `layouts` ↔ `ref-shots/`)

Carte « quel fichier texture/imageset CEGUI alimente quel panneau visible ».
Sources : `corpus/fr-full/uidata/imagesets/*.imageset` (34 XML),
`corpus/fr-full/uidata/layouts/*.layout` (415 XML), PNG lisibles dans
`artifacts/corpus-png/uidata/imagesets/`, captures `ref-shots/` +
`eden_godot/docs/client-assets/ref-shots-catalog.md`. Complète
`docs/reference/corpus/ui-et-cegui.md` (inventaire brut).

**Méthode** : les `.imageset` déclarent `Imagefile` + régions nommées ; les
`.layout` référencent les régions via `set:<Imageset> image:<Région>` dans les
propriétés `Image`/`NormalImage`/`HoverImage`/`PushedImage`/`DisabledImage`.
Jointure imageset↔layout par grep `set:` ; jointure panneau↔ref-shot par nom de
fenêtre racine (`Root/…`) + nommage de régions + aspect visuel des PNG.

**Statuts** : **vérifié** = référence textuelle trouvée (le layout cite le
`set:`/`image:`) ; **probable** = correspondance par nommage ou visuel, non
confirmée par une référence textuelle directe.

**Casse** : les `Imagefile` sont en CamelCase (`DejaVu.png`, `ToolTips.png`)
alors que les fichiers corpus sont en minuscules (`dejavu.png`,
`tooltips.png`) — résolution insensible à la casse requise (comme `EdenVfs`).

## Rappel de câblage CEGUI

- `schemes/dejavu.scheme` ne charge **qu'un seul imageset** : `DejaVu`. Tous
  les autres doivent être chargés par le code client (avant l'ouverture des
  layouts qui en référencent).
- `looknfeel/dejavu.looknfeel` (skin des widgets `DejaVu/*`) n'utilise que des
  régions `set:DejaVu` (84 refs : boutons, scrollbars, onglets, cases, cadres,
  curseurs souris…). **Tout layout hérite donc de `dejavu.png`** même sans
  aucune référence `set:` explicite : **151 layouts / 415 n'ont aucune**
  référence `set:` (ex. `castingbarwnd`, `characterwnd`, `questwnd` — frames,
  barres et boutons viennent du skin).
- `main.imageset` déclare `NativeHorzRes="800" NativeVertRes="600"` (seul
  imageset avec résolution native).
- `imagesets/<couleur>/` (10 dossiers : `blue`, `grapes`, `green`, `lemon`,
  `orange`, `purple`, `red`, `strawberry`, `water`, `yellow`) = **variantes de
  thème recolorées** des 5 atlas de base (`dejavu`, `dejavuimage`, `main`,
  `radar`, `cndejavu2` — absent de `yellow/`). Contenu distinct vérifié
  (md5 différents). Correspond au dropdown de thème UI vu dans
  `ref-shots/ParameterPanel-UISettings.png` (**probable**).
- `dejavu.ufo` (358 Kio, OLE2) dans `imagesets/` = données fontes, pas une
  texture UI.

## 1. Imageset → texture → layouts → panneau

| Imageset | Texture (`Imagefile` → PNG miroir) | Régions | Layouts qui citent `set:` | Panneau / usage (ref-shot) |
|---|---|---|---|---|
| `dejavu` | `DejaVu.png` → `dejavu.png` (1024×512) | 437 | **216** (52 % des layouts — quasi tous) | Skin maître : cadres de fenêtres, boutons, cases losange, scrollbars, onglets, tooltips, slots fleur-de-lis (`Slot*`), cadres qualité objet (`QualityW/G/B/O/P/Y*`), barre de cast (`castingBar*`), curseurs souris (`Mouse*`), `ScreenMessageBG`, `HelpButton*`. Tout panneau visible (**vérifié** — scheme + looknfeel). |
| `dejavuimage` | `DejaVuImage.png` → `dejavuimage.png` (512×512) | 133 | **109** | Iconographie partagée : slots équipement (`Head/Body/Hand/Shoes/Ring/Necklace/L/R/AL/ARWeapon/Hat/Back*`), résistances (`Anti*`), monnaies (`Gold/Silver/AlpacaCoin/Crystalcross`), achat/vente/réparation (`Buy*`, `Sell*`, `FixAll/Fixone*`, `Stall*`, `Helper*`, `Chest*`), états serveur (`ServerGreen/Red/Gray/Yellow`), item mall (`ItemMall*`, `itemmallBonus`), notifications (`Notification*` : badges « NEW TITLE / ACHIEVEMENT / NEW JOB / LEVEL UP / JOB SPELL / NEW AVATAR / LICENSE / STORY » visibles dans la texture), humeurs (`Mood*`), classes (`FClass1-7`, `HeroIcon1-10`). |
| `main` | `main.png` → `main.png` (1024×512) | 115 | 40 | **HUD** : cadre portrait orné `faceFrame`, cadres cible `TargetNPC/King/Human/UnDead/Monster` + `MonsterStar` (★ rang élite), vitals `MiniinfoBG`/`MiniInfoHP/Mp/BP`/`InfoHpBG/InfoBpBG`, hotbar `FunctionFastBar`/`FunctionSlot`/`FunctionUp/Down`/`FunctionLock`, barre XP `FunctionExpBackgrand`, boutons menu `Function{Character,Item,Skill,Mission,Team,Commu,City,Pet,Sys,Bagr}*` + `FunctionMenuBG`, chat `channel*`/`Channel*`/`Transparent*`, buffs `BuffBackground*`, modes pet `Pet{Attack,Collect}Mode`, classes `AttackIcon`/`HealerIcon`/`DefenderIcon`, `MiniInfoLeaderIcon`/`PkIcon`, `Mission{Hide,Hover,Action}` (tracker), résistances `Anti*` (doublon de DejaVuImage). **vérifié.** |
| `radar` | `Radar.png` → `radar.png` (512×512) | 102 | 11 | **Minimap** : cadre `RadarBody`/`RadarPic`, zoom `ZoomIn/Out*`, globe `Globle*`, masquage `OpenHiding/CloseHiding*`, et toute la rangée de boutons-fonctions autour de la minimap (Mail, Map, Help, Rank, Lty=loyalty, Ach=achievement, App=appellation, Col=collect, Com?, Pre=prestige, LS, SF, Territory, MagicCube + boutons `*NA{D,H,N,P}` = états Disable/Hover/Normal/Press pour Shop/Team/Carve/Combo/Option/Collect/MagicCube/SpecialCoin). **vérifié** (`radarui*`, `cnradarui`). Réutilisé hors minimap : `fateroad*` prend 39× `AppNormal` pour ses `RoadIcon*`, `inventorywnd_{cn,na}` et `miniinfownd*` prennent les boutons `OpenHiding/CloseHiding`. |
| `cndejavu` | `CNDejaVu.png` → `cndejavu.png` (512×512) | 62 | 9 | Boutons HUD variante CN : `BagButton*`/`JobButton*`/`CharButton*`/`TeamButton*`/`FamilyButton*`/`FriendButton*`/`MissionButton*`/`SystemButton*`/`ItemmallButton*`/`Pet*`/`CloseButton*`/`PinkCloseButton*`/`SliverCloseButton*`, `auto1-6`, `Gold/SilverIcon`, `beginners_guide_*`. → barre menu (`functionmenu*`) et sac (`inventorywnd*`) (**vérifié**), `cnarrowmessage`. |
| `cndejavu2` | `CNDejaVu2.png` → `cndejavu2.png` (512×512) | 21 | 6 | Flèches de navigation `Up/Left/Right/BottomArrow`, `MainBase/MainBase2`, marqueurs page `NumberLeft/Right*` + `AL*`. → `cnradarui`, `cnitemmall`, `functionmenucn*`, `newmagiccubewnd`, `cnarrowmessage` (**vérifié**). |
| `cdani` | `CoolDownSets.png` → `cooldownsets.png` (512×512) | 172 | 8 | Arène/versus : `ArenaVS`, `ArenaFire1-6`, `ArenaLightOn/Off`, boutons `AT/BT/HT*`, `Mask*`, `SelectFrame`, `BattleDungeonWinner`, `Chess`, `Light*` ; frames cooldown `b1-9`, `c1-120`, `auto1-6`. → `arenaduel*`, `mosterarena`, `dpsrecord` (boutons AT/BT/HT), `fateroadwnd*` (`Chess`), `versusinstancerankwnd` (**vérifié**). Le sweep de cooldown des skills en jeu est probablement `b*`/`c*` (**probable**, cf. overlays numérotés de `SkillAnimationTornado(cleric).png`). |
| `magiccube` | `MagicCube.png` → `magiccube.png` (512×512) | 50 | 2 | Mini-jeu Magic Cube (alchimie/alpaga) : `Function01-06*` (grille de fonctions, 5 états), `Success`/`Fail`, `Animation`, `Probability`, `runner0-3`, `countbackground*`. → `magiccube00`, `newmagiccubewnd` (**vérifié**). |
| `newhand` | `newhand.png` → `newhand.png` (512×256) | 44 | 1 | Guide débutant : main/pointeur + icônes de tutorat nommées comme les fonctions (`Bag`, `Skill`, `Mail`, `Team`, `Quest`, `Mission`, `Lty`, `LSN`, `SFN`, `SYS`, `Itemmall`, `Achieve`, `Collect`, `Prestige`, `Territory`, `MonsterKing`, `Crystalcross`, `HeroIcon01-09`…). → `guidewnd` ne cite que `newhand:Yes` (**vérifié**) ; le reste est posé par code (**probable** pour les bulles d'aide type « How to move »). |
| `promotion` | `promotion.png` → `promotion.png` (512×512) | 11 | 1 | Graphique « VS »/lettres (`LImage1-11`). → `familybattlejudgewnd` (fenêtre `Root/CompetitionSchedule`) (**vérifié** — guerre de guildes). |
| `competitionschedule` | `CompetitionSchedule.png` → `competitionschedule.png` (744×548) | 1 | 1 | Arbre de tournoi à dragons (`fixed`). → `familybattlejudgewnd` (**vérifié**). |
| `cutscene` | `cutScenePlay.png` → `cutsceneplay.png` (256×256) | 13 | 2 | Lecteur de cinématique : `play`/`pause`/`stop` (+hover/push), `LetterBox`, `MainBar`, `KeyBar(+Select)`. → `cs_actionbarwnd`, `cs_letterboxwnd` (**vérifié**). |
| `jobglyph` | `JobGlyph.png` → `jobglyph.png` (512×512) | 1 | 2 | Cercle de glyphes doré à 15 emplacements (`backgrand`). → `jobglyph`, `jobglyph_na` (**vérifié** — panneau glyphes de classe). |
| `injured` | `Injured.png` → `injured.png` (2048×2048) | 1 | 1 | Flash radial rouge plein écran (mort/blessure). → `screenmessagewnd_na` (**vérifié**). |
| `krconsideration` | `KRConsideration.png` → `krconsideration.png` (640×360) | 1 | 1 | Avertissement légal KR (`Img`). → `krconsiderationwnd` (**vérifié**). |
| `krpaywarning` | `KRPayWarning.png` → `krpaywarning.png` (640×360) | 1 | 1 | Avertissement paiement KR (`Img`). → `krpaywarningwnd` (**vérifié**). |
| `roundicon` | `Roundicon.png` → `roundicon.png` (64×64) | 1 | 1 | Icône ronde (`Image`). → `liveupdate` (patcher `Root/LiveUpdateWnd`) (**vérifié**). |
| `bigworld` | `mapworld00.png` → `mapworld00.png` (512×512) | 1 | 5 | Fond carte du monde « big » (`Background`). → `transportwnd`, `zonemapui*`, `battlecrusadewnd` (**vérifié**). |
| `worldmap` | `mapworld01.png` → `mapworld01.png` (512×512) | 1 | 5 | Fond continent `Background` (la carte peinte « Central Continent »). → `transportwnd` (fenêtre **Portal** = `TransportationArray (TP changement carte)*.png`), `zonemapuiver2`, `zonemapuicn`, `battlecrusadewnd`, `territoryrankwnd` (**vérifié**). |
| `worldmap11` | `mapworld011.png` → `mapworld011.png` | 1 | 1 | Variante fond continent. → `zonemapui` (**vérifié**). |
| `worldmap12` | `mapworld012.png` → `mapworld012.png` | 1 | 0 | Variante fond continent — orphelin (voir §3). |
| `worldmapcity` | `worldmap.png` → `worldmap.png` (128×128) | 4 | 5 | **Icônes cristal de destination** `CityNormal/Hover` (+2 tailles) posées sur la carte monde — ce sont les cristaux bleus des ref-shots Portal/carte (**vérifié** : `transportwnd` 84 refs, `zonemapui*` 69-87, `territoryrankwnd` 57, `battlecrusadewnd` 84). |
| `worldmapcity2` | `worldmap2.png` → `worldmap2.png` (128×128) | 4 | 1 | Mêmes marqueurs ville, autre variante. → `battlecrusadewnd` (**vérifié**). |
| `bigworldset` | `worldmap1.png` → `worldmap1.png` (1024×512) | 5 | 5 | Découpe de la carte monde en 5 sous-régions (`North`, `Center`, `EastSouth`, `WestNorth`, `WestSouth`). → `transportwnd`, `zonemapui*`, `battlecrusadewnd` (**vérifié**). |
| `map` | `mapworld01.png` | 1 | 0 | Doublon de `worldmap` (région `map`) — orphelin. |
| `map2` | `mapworld02.png` | 1 | 0 | Fond continent alternatif — orphelin. |
| `map3` | `mapworld03.png` | 1 | 0 | Idem — orphelin. |
| `map31` | `mapworld031.png` | 1 | 0 | Idem — orphelin. |
| `newknowledge` | `NewKnowledge.png` → `newknowledge.png` (512×512) | 1 | 0 | Illustration statue d'ange = fond illustré de l'onglet talents — orphelin en `set:` mais assigné au `BGImage` sans `Image` de `newknowledge.layout` (**probable, structurellement confirmé**). |
| `newlicence` | `NewLicence.png` → `newlicence.png` (512×512) | 1 | 0 | Illustration cristal = fond onglet certificats — même mécanisme (`newlicence.layout`) (**probable**). |
| `newskilllist` | `NewSkillList.png` → `newskilllist.png` (512×512) | 1 | 0 | Illustration temple/cercle = fond onglet skills — même mécanisme (`newskilllist.layout`) (**probable**). |
| `teamscorelevel` | `TeamScoreLevel.png` → `teamscorelevel.png` (640×640) | 8 | 0 | Notes d'équipe `S/A/B/C` + `+` — orphelin (notation arène/équipe posée par code, **probable**). |
| `tooltips` | `ToolTips.png` → `tooltips.png` (64×64) | 4 | 0 | Puces `Embed{None,Jewelry}` + `Star{None,Jewelry}` (marqueurs socket/étoile équipement) — orphelin (tooltip objet assemblé par code, **probable**). |
| `cnguiderb` | `CNGuideRB.png` → `cnguiderb.png` (512×512) | 1 | 0 | Bulle-bd alpaga guide CN — orphelin (**probable** : tutoriel/guide). |

Régions « vides » : `familybuilding_forest.layout` et `familybuilding_mountain.layout`
contiennent 6 propriétés `Value="set:"` sans nom d'image (placeholders).

## 2. Panneaux ref-shot → fichiers constitutifs

Sauf mention contraire, le skin (cadre, boutons, scrollbars, onglets) vient
toujours de `dejavu.png` via `dejavu.looknfeel`.

### HUD

| Ref-shot / panneau | Layout(s) | Atlas utilisés (régions clés) |
|---|---|---|
| `TopLeftVitals.png` — vitals haut-gauche | `miniinfownd.layout`, `miniinfownd_equipmentswitch.layout` (`Root/MiniInfoWnd`) | `main` : `faceFrame` (cadre portrait orné), `MiniinfoBG`, `MiniInfoHP/Mp`, `InfoHpBG`, `MiniInfoLeaderIcon`, `MiniInfoPkIcon` ; `Radar` : `OpenHiding/CloseHidingNormal` (pliage) ; `DejaVuImage` : `Helper*` (**vérifié**) |
| `HotbarPanel.png` — barre de raccourcis | `functionwnd.layout` (`Root/FunctionWnd`) + gabarits de slot `slotskill`, `slotjob`, `slotjobver2`, `slotlicense`, `slotlifelong` | `main` : `FunctionFastBar` (barre), `FunctionUp/Down*` (flèches de page), `FunctionLock*` (cadenas), `FunctionSlot` (case) ; `DejaVuImage` : `NotificationNew` (badge « NEW » sur slot) (**vérifié**). Les icônes de skill dans les slots sont des DDS `ui/skillicon/`, hors imagesets. |
| `DoubleEXPbar.png` — double barre EXP/CP | `fucntionbarwnd.layout` (`Root/FunctionBarWnd` : `expBackGrand` + `SoulBackGrand`) | `main` : `FunctionExpBackgrand` ; remplissage = `DejaVu/ProgressBar` du skin (**vérifié**) |
| Menu bas-droite (10 boutons, cf. `juste le jeu.png`) | `functionmenu.layout`, `functionmenu_na`, `functionmenucn`, `functionmenucnpet`, `functionmenu5ppet` (`Root/FunctionMenu`) | `main` : `FunctionMenuBG` + `Function{Character,Item,Skill,Mission,Team,Commu,City,Pet,Sys,Bagr}{Normal,Hover,Press}` ; `CNDejaVu` : variantes boutons (**vérifié**) |
| `pet skillbar.png` | `petfunctionwnd.layout`, `petfunctionwnd5p.layout` (`Root/PetFunctionWnd`) | `main` : `FunctionFastBar`, `FunctionLock*`, `PetAttackMode`, `PetCollectMode` (**vérifié**) |
| `ChatPanel.png`, `ChatPanel-Peer.png` | `channelwnd.layout`, `channelwndv2.layout` (`Root/ChannelWnd`) | `main` : `channel*` (41 refs : cadre, onglets, `ChannelName`, `ChannelHover`, `ChannelEnable/Disable`, `ChannelInputBackground`, `channelVertScrollBar*`), `Transparent*` ; `DejaVu` + `DejaVuImage` (**vérifié**) |
| `shot_0078_000316.png` — Channel Settings | `channelconfig.layout`, `channelconfigv2.layout` | skin DejaVu seul (0 `set:`) — losanges = `Checkbox*`/`RedCheckbox*` de `dejavu.png` (**probable**) |
| `targetframe (Elite mob).png` — cadre cible (+ Target's Target de `shot_0085`) | `targetinfownd.layout`, `targetinfownd1.layout` (`Root/TargetInfoWnd`) | `main` : `faceFrame`, `MonsterStar` (★ rang), `MiniinfoBG`, `MiniInfoHP/Mp/BP`, `InfoHpBG/InfoBpBG`, `AntiStroke` ; `DejaVu` (`HelpButton*`) ou `DejaVuImage` (`Chest*`) (**vérifié**) |
| `party UI.png` — cadres de groupe | `partywnd.layout` (`Root/party`) + `teamwndframe.layout`, `teamwndframe1` (`Root/TeamWndFrame`) ; raid : `raidwndframe.layout` | `main` : `MiniinfoBG`, `MiniInfoHP/Mp`, `InfoHpBG`, `MiniInfoLeaderIcon` (couronne chef) (**vérifié**) |
| Minimap haut-droite (`juste le jeu.png`) | `radarui.layout`, `radarui_ver2.layout`, `cnradarui.layout` (`Root/Radar`) | `Radar` : `RadarBody`, `RadarPic`, `Zoom*`, `Globle*`, boutons-fonctions NAD/NAH/NAN/NAP (**vérifié**). La carte elle-même = DDS `ui/map/S###m.dds`, hors imagesets. |
| Buffs sous barre ULTIMATE (`shot_0045`) | `buffwnd.layout`, `buffwnd1.layout` (`Root/BuffWnd`) + `slotbuff` | `main` : `BuffBackground*` (9-patch) ; `DejaVuImage` : `BuffBtn` (**vérifié**) |
| `TimedCastSkill.png` — barre de cast | `castingbarwnd.layout` (`Root/castingbarWnd`, type `DejaVu/CastingBarImage`) | `dejavu.png` : `castingBar{L,R,T,B,LT,RT,LB,RB,C}` via le skin (**vérifié par le looknfeel**) |
| `QuestTracker*.png` — tracker de quêtes | `questtracewnd.layout` (`Root/QuestTraceWnd`) + `slotquest` ; parties repliables | `DejaVu` : `CreateZoomIn*` ; `main` : `Mission{Hide,Hover,Action}` (dans `main`, cités par `fucntionbarwnd`/`functionmenu*`) (**partiellement vérifié / probable**) |
| Toasts centraux (« acquired state », « Switched Class », « learned skill ») | `screenmessagewnd.layout`, `screenmessagewnd_na` (`Root/DWindow`), `nitificationtipwnd` (`Root/NotificationTipWnd`), `notifywnd`, `messagewnd` | `dejavu.png` : `ScreenMessageBG` ; `Injured` (variante NA) ; `main` : `FunctionLock*` (**vérifié**). Oiseaux du changement de classe : `flybirditem.layout` (`Root/FlyBirdItem`) — **probable**. |

### Panneau Classes (K) — `ClassePanel*.png`, `HoverToolTipClasse.png`, `case pour certificat.png`

Conteneur : `jobwnd.layout` (`Root/Job`, `FrameWindow` + `TabControl` vide —
les onglets sont remplis par le code avec les sous-fenêtres). Sous-layouts :

| Onglet / zone (ref-shot) | Layout(s) | Atlas |
|---|---|---|
| Liste des classes (colonne gauche, cadenas, « Current Clas ») | `joblistwnd*.layout`, `newjoblist`, `krjoblistwnd2` (`Root/JobList`) | `DejaVu` (cadre, `TreeList*`), `DejaVuImage` (`NotificationNew`, `ServerGreen`…) (**vérifié**) |
| Skills classe + « Reg. Skills » (`ClassePanel.png`) | `jobcurrentwnd*.layout` (`Root/JobCurrent` : `Knowledge*`, `InnateBorder*`), `jobskill_na`, `jobskill_na02`, `jobskill_angel`, `newskilllist.layout` (`Root/NewSkillList`) | `DejaVu`, `DejaVuImage` ; fond illustré `NewSkillList.png` assigné par code au `BGImage` (**probable**) |
| « License Knowledge » = arbre de talents + KPs (`ClassePanel-ClassTalent&Certificat.png`) | `newknowledge.layout` (`Root/NewKnowledge` : `InnateBorder`, `Arrow1-17`, `KnowledgeIcon/Lv`) | `DejaVu` (`TreeListOpened/Closed` pour les flèches) ; fond `NewKnowledge.png` (statue d'ange) par code (**probable**) |
| « Certificates » (grille fleur-de-lis) | `newlicence.layout` (`Root/NewLicence` : `Licenseborder`, `LicenseScrpllBG`) + `slotlicense` | `DejaVu` (slots `Slot*`) ; fond `NewLicence.png` (cristal) par code (**probable**) |
| « Class News » / fiche descriptive + stats % + preview (`ClassePanel-ClassNews*.png`) | `jobinfo.layout` (`Root/JobInfo`), `jobcurrentwnd*` | `DejaVu`, `DejaVuImage` (**vérifié**) |
| Bouton « Transform! » / changement de classe | `jobwnd_na`, `changetypewnd`, `changetypeconfirm` | `DejaVu` (**probable**) |
| Glyphes | `jobglyph.layout`, `jobglyph_na` (`Root/JobGlyph`) | `JobGlyph` : `backgrand` (cercle 15 glyphes) (**vérifié**) |

### Expertise / « Class Ability Bonus » — `panel primaire d'expertise d'une classe.png`, `second panel expertise d'une classe.png`

Le premier panneau (« Full Class Ability Bonus List », lignes classe + bouton
« CE List ») et le second (« Engineer Ability Bonus List ») ne citent aucune
texture propre : skin `dejavu.png` + icônes de classe posées par code.
Candidats par nommage (**probable**, non confirmé) :

- `cooperationtab.layout` (`Root/CooperationTab`) / `cooperation_ver2.layout`
  (`Root/CooperationNA`) — liste avec `SearchList`/`SearchBtn` ;
- `jobpointlist.layout`, `jobpointlist2.layout` (`Root/JobPointList[2]` :
  `BuffListText` + `ScrollPane`) + gabarit de ligne `jobpointawarditem[._na]` ;
- le tooltip « Class Ability Bonus » de `ClassAbilitybonuslogo.png` relève du
  même système.

### Inventaire / sac — `shot_0017-0019` (Backpack), tooltips `item hover *.png`

- `inventorywnd.layout` (`Root/Bag` : `tab/TabCommon/listbox` + `stimage*`,
  `GoldText`/`SilverText`) + variantes `inventorywnd_cn` (CNDejaVu +
  `Radar:OpenHiding`), `inventorywnd_na` (idem), gestion de sacs
  `bagmanagement2`, `krbagmanagement2`, `usbagmanagement2`.
- Atlas : `DejaVu` (grille slots `Slot*` fleur-de-lis + cadres qualité
  `QualityW/G/B/O/P/Y*`), `DejaVuImage` (`GoldIcon`, `SilverIcon`, `FixAll*`,
  `Fixone*`, `Chest*`, `Stall*`), `CNDejaVu` (`BagButton*`) (**vérifié**).
- Icônes d'objets : DDS `ui/itemicon/` hors imagesets ; marqueurs socket/étoile
  probablement `tooltips.png` (orphelin, **probable**).

### Cartes & téléportation — `carte.png`, `shot_0022`, `TransportationArray*.png`, `panel tp map`

| Niveau | Layout | Atlas |
|---|---|---|
| « Region Map » plein écran (Aven, Search Listing, City Features) | `zonemapui.layout` (`WorldMap11` bg), `zonemapuiver2` (`WorldMap` bg), `zonemapuicn` (`WorldMap` bg) + `zonemapoption*` | `worldmapcity` (marqueurs cristal ville, 78-87 refs), `BigWorld`/`BigWorldSet` (continents), `DejaVu` (**vérifié**). Fond de région illustré = DDS `ui/map/S###s.dds` hors imagesets. |
| « Portal » téléportation | `transportwnd.layout` (`Root/TransportWnd`) + `transporttip` | `WorldMap` (`mapworld01.png` = continent « Central Continent » du dropdown), `worldmapcity` (84 cristaux bleus de destination), `BigWorldSet`, `BigWorld`, `DejaVu` (**vérifié**) |
| Carte guerre de territoire | `battlecrusadewnd` (`mapBtn1-38`, `BigWorldImage`), `territoryrankwnd` | `WorldMap`, `worldmapcity2` (84 refs), `worldmapcity`, `BigWorldSet` (**vérifié**) |

### Autres panneaux ref-shot

| Ref-shot | Layout(s) | Atlas (statut) |
|---|---|---|
| `Selection_personnage.png` | `selectcharacterwnd` (`Root/SelectCharacterWnd`) + `charpassworddlg`, `deletecharacterdlg`, `createcharacter*` | `main`, `DejaVu`, `DejaVuImage` (**vérifié**) |
| `Selection_channel.png` | `cnworldserverlistwnd`, `cworldserverlistwnd` (`Root/CWorldServerListWnd`) | `DejaVu` ; états canal `DejaVuImage` : `ServerGreen/Red/Gray/Yellow` (**vérifié**) |
| Écran de chargement (`changement de map…`) | — aucun layout | artwork `ui/loadingframe/loading_###.dds` hors CEGUI (**probable**) |
| `profile.png` (Character Info + équipement) | `characterwnd`/`characterwnd_new2` (`Root/CharacterUI`), `equipmentwnd*` (`Root/EquipmentWnd`), `newequipment`, `otherequipmentwnd*`, `pvpequipmentscanner` | `DejaVu` ; `DejaVuImage` : icônes slots + `Anti*` résistances (**vérifié**) |
| `profile-Title.png` | `appellationwnd`, `appellationwnd_ver2` (`Root/AppellationWnd`) | `DejaVu` (**vérifié**) |
| `Profile-Achievements*.png` | `achievement`, `achievement_ver2`, `achievementviewwnd`, `achievementgroup`, `achievementtipwnd*` | `DejaVu` + `DejaVuImage` (`AchievementTip`, `NotificationAchievement`) (**vérifié**) |
| `QuestLog.png`, `quest log*.png` | `missionlog` (`Root/Mission`), `autoquestlist` (`Root/MissionList`), `questexp`, `questautonotifywnd` | `DejaVu` (**vérifié**) |
| `quest UI.png`, `npc talk*.png`, `proposition nouvelle quete.png` | `questwnd` (`Root/QuestWnd`), `npctalkwnd`, `scriptwnd` (`Root/Script`), `scriptslot`, `questitem` | `DejaVu`, `DejaVuImage` (**vérifié**) |
| `Mail.png`, `Mail2.png` | `maillistwnd`, `mailreadwnd`, `mailsendwnd`, `mailwnd`, `mailitem` | `DejaVu`, `DejaVuImage` (**vérifié**) |
| `Auction*.png` | `auctionwnd`, `auctionbuywnd`, `auctionsellwnd`, `cnauctionbuywnd` | `DejaVu`, `DejaVuImage` (**vérifié**) |
| `CommunityPanel*.png` | `communitywnd`, `krcommunitywnd` (`Root/Community`) | `DejaVu`, `DejaVuImage` (**vérifié**) |
| `FindGuild.png`, `shot_0062` (Town List) | `familyfindinginfo`/`familyfindingwind` (`Root/FamilyFinding*`), `familytownlistwnd` (`Root/TownList`), `familyappwnd` | `DejaVu` (**vérifié**) |
| `ParameterPanel-*.png` | `optioncontrolui`, `optioncontroluiver2`, `optioncontroluicn` (`Root/OpControl/OptionControlUI`) | `DejaVu` (**vérifié**) |
| `SystemPanel.png` (Main Menu) | `optionmenuui`, `kroptionmenuui` (`Root/System`) + `logoutwnd` (`Root/Logout`) | `DejaVu` (**vérifié**) |
| `ItemMallPanel*.png`, `shot_0066` | `itemmall`, `cnitemmall` (`Root/ItemMal`), `itemmaillitem`, `itemmailltopitem`, `itemmallquestitem`, `itemmallmailwnd`, `itemmallserial` | `DejaVu`, `DejaVuImage` (`ItemMall*`, `itemmallBonus`, `Crystalcross`, `AlpacaCoin`), `CNDejaVu2` (**vérifié**) |
| `FusionPanel Craft` (Magic Pool) | `combinewnd` (`Root/CombineWnd`), `engineeringstudy`, `jewelrystudy`, `strengthenwnd*`/`strengthenfactory`/`krstrengthenwnd` (fortification), `magicboxwnd` | `DejaVu`, `DejaVuImage`, `main` (**vérifié**) |
| `PetsPanel.png` | `petwnd`, `petwndv2` (`Root/Pet`), `petitem`, `petlistwnd`, `petupgradewnd`, `changepetnamewnd` | `DejaVu`, `DejaVuImage` (`PetIcon`, `PetUpgrade*`) (**vérifié**) |
| `daily reward.png` (Event) | `activitieswnd` (`Root/Activities` + TabControl), `loginreward` (`EverydayTab`), `recommendactivitieswnd` (`MissionTab`), `starway_v2` (`Starway`), `activitiesitem` | `DejaVu`, `main` (**vérifié**) |
| `tradeNPCSystem.png` (marchand PNJ) | `shopwnd` (`Root/shop` : `Tabsell`/`TabBuy`), `shopitem` | `DejaVu`, `DejaVuImage` (`Buy*`, `Sell*`, `FixAll*`, `Fixone*`) (**vérifié**) |
| Échange joueur | `tradewnd` (`Root/TradeWnd`) | `DejaVu`, `DejaVuImage` (**vérifié**) |
| Étals joueurs | `stallui` (`Root/StallUI`), `stallbuy` | `DejaVu`, `DejaVuImage` (`Stall*`) (**vérifié**) |
| Banque/maison/stockage | `hotelwnd`, `hotelstoragewnd`, `storageui`, `storagerentdlg` | `DejaVu`, `DejaVuImage` (**vérifié**) |
| Loteries / Magic Cube | `baselotterywnd`, `lotterywnd*`, `crystallotterywnd`, `diamondlotterywnd`, `ingamelotterywndver2`, `chanceluckybag`, `luckybagdetailwnd` ; `magiccube00-03`, `newmagiccubewnd`, `newmagiccuberecipe*`, `magiccubeitem` | `MagicCube` (vérifié pour `magiccube00` + `newmagiccubewnd`), `DejaVu`, `DejaVuImage`, `CNDejaVu2` |
| Cinématiques | `cs_*` (18 layouts : `cs_actionbarwnd`, `cs_letterboxwnd`, `cs_normal*`, `cs_option*`, `cs_model*`, `cs_functionalwnd`…) | `cutScene` (`cs_actionbarwnd` : play/pause/stop ; `cs_letterboxwnd` : `LetterBox`, **vérifié**) |
| `arena*` / duels / battle royale | `arenaduel*`, `arenainformation*`, `arenamain`, `mosterarena`, `battleroyale*`, `battlefieldlistui`, `duelwnd`, `duelrequestwnd`, `versusinstance*` | `CDAni` (`Arena*`, `AT/BT/HT*`), `main`, `DejaVu` (**vérifié**) |
| `Fate Road` | `fateroadwnd`, `fateroadwndna2` (`Root/FateRoadWnd` : `RoadIcon1-39`), `fateroadinfo`, `fateroadtipwnd` | `Radar` (`AppNormal` ×39), `CDAni` (`Chess`), `main`, `DejaVu` (**vérifié**) |
| Guide/tutoriel (« How to move ») | `guidewnd` (`Root/GuideWnd`), `tutorailwnd` | `newhand` (`Yes` cité ; autres régions par code, **probable**) |
| Guerre de guildes (bracket) | `familybattlejudgewnd` (`Root/CompetitionSchedule`) | `CompetitionSchedule` (arbre dragons) + `promotion` (**vérifié**) |
| Système : revive, roll, loot | `revivemenuui`, `revivecount`, `rollui`, `lootrulewnd`, `newlootrulewnd`, `cnlootrulewnd` | `DejaVu` (**vérifié**) |
| Patcher | `liveupdate` (`Root/LiveUpdateWnd`) | `Roundicon` (**vérifié**) |
| Légal KR/CN : captcha, anti-addiction, security card | `captchawnd`, `againstindulges*`, `cnsecuritycard`, `krpaywarningwnd`, `krconsiderationwnd`, `paysystemwnd` | `KRPayWarning`, `KRConsideration` (**vérifié**), `DejaVu` |

## 3. Orphelins

### Imagesets jamais référencés par un `set:` dans les 415 layouts (11)

| Imageset | Texture | Hypothèse d'usage |
|---|---|---|
| `newknowledge` | `newknowledge.png` (statue d'ange) | Fond `BGImage` de `newknowledge.layout` posé par code — **probable, étayé** (le `BGImage` n'a pas de propriété `Image`) |
| `newlicence` | `newlicence.png` (cristal) | Idem pour `newlicence.layout` — **probable** |
| `newskilllist` | `newskilllist.png` (temple) | Idem pour `newskilllist.layout` — **probable** |
| `tooltips` | `tooltips.png` (4 puces Embed/Star) | Marqueurs socket/étoile des tooltips d'équipement, assemblés en code — **probable** |
| `teamscorelevel` | `teamscorelevel.png` (S/A/B/C +) | Notation d'équipe/arène — **probable** |
| `cnguiderb` | `cnguiderb.png` (bulle alpaga) | Guide CN — **probable** |
| `map` | `mapworld01.png` | Doublon de `worldmap` (même texture) — résidu |
| `worldmap2` | `mapworld02.png` | Continent alternatif sélectionné par code — **probable** |
| `worldmap3` | `mapworld03.png` | Idem — **probable** |
| `worldmap31` | `mapworld031.png` | Idem — **probable** |
| `worldmap12` | `mapworld012.png` | Variante de `worldmap11` — **probable** |

Aucun de ces noms n'apparaît non plus dans `lua_scripts/`, `schemes/`,
`looknfeel/` ou `fonts/` : leur chargement relève forcément du code client
(ou ils sont morts).

### Textures UI non couvertes par un imageset

Tout le reste de l'UI visible est en DDS chargé directement par le code
(`ui/` + `UI/`, ~16 000 fichiers — voir `ui-et-cegui.md`) :

- `ui/itemicon/` (6 070) — icônes d'objets dans les slots sac/shop/équipement ;
- `ui/skillicon/` (4 742) — icônes de sorts dans hotbar/panneaux classe/buffs ;
- `ui/uiicon/` (4 550) — icônes divers (marqueurs carte, PNJ, émotes…) ;
- `ui/map/` (299) — fonds de « Region Map » (`S###s.dds`) et minimaps
  (`S###m.dds`) affichés par `zonemapui*`/`radarui*` ;
- `ui/loadingframe/` (393) — écrans de chargement (`loading_###.dds`) ;
- `imagesets/<couleur>/` — variantes de thème (voir § « câblage »), jamais
  référencées par les layouts (sélection par code) ;
- les PNG orphelins à côté des `.imageset` sont tous couverts par un imageset
  sauf aucun (chaque `*.png` du répertoire a son `.imageset` ou est le
  `Imagefile` d'un autre : `cooldownsets.png`→`cdani`, `cutsceneplay.png`→
  `cutscene`, `worldmap1.png`→`bigworldset`, `worldmap2.png`→`worldmapcity2`,
  `mapworld0*.png`→`worldmap*`/`bigworld`/`map*`).

## 4. Limites

- **Régions posées en code** : les propriétés `Image` absentes du XML sont
  remplies à l'exécution (icônes de skills/items DDS, `BGImage` des onglets
  classe, régions `newhand` autres que `Yes`, balayage de cooldown `CDAni`
  `b*/c*`, contenu de `RadarPic`). La carte ci-dessus ne couvre que les
  références textuelles.
- **Assemblage runtime** : les onglets (`Root/Job`, `Root/Activities`,
  `Root/StallUI`…) ne citent pas leurs sous-fenêtres ; la correspondance
  onglet↔layout est inférée par nommage.
- **Variantes `_cn`/`_na`/`_kr`/`_us`/`ver2`** choisies par le client selon la
  région/version — non déterminé laquelle EDENFR utilise réellement ; les
  panneaux KR (`kr*`, `*Warning`, `againstindulges*`, `cnsecuritycard`,
  `captchawnd`) et CN (`cn*`, `CNDejaVu*`) sont probablement invisibles en
  EDENFR.
- **Textures non relues pixel par pixel** : la correspondance ref-shot↔atlas
  repose sur nommage + aperçu des PNG principaux (`main`, `dejavu`,
  `dejavuimage`, `radar`, `magiccube`, `newhand`, `worldmap`, `mapworld01`,
  `newknowledge/licence/skilllist`, `teamscorelevel`, `jobglyph`, `injured`,
  `tooltips`, `competitionschedule`, `promotion`, `cnguiderb`).
- **Expertise/Class Ability Bonus** : aucun layout ne cite textuellement ce
  panneau ; `cooperationtab`/`jobpointlist*` proposés par nommage seulement.
- **Doublons de régions** : `Anti*` (dans `main` et `dejavuimage`),
  `GoldIcon`/`SilverIcon` (`dejavuimage` et `cndejavu`), `FunctionTeam*`
  (`main` et `cndejavu`), `auto1-6` (`cdani` et `cndejavu`) portent le même
  nom dans plusieurs atlas — la source effective dépend du `set:` cité, pas
  du nom de région.
- `ref-shots/` vient du client **Reforged** (EN), pas d'EDENFR : certains
  panneaux (Fate Road, Starway, Greedy Island…) peuvent ne pas exister dans
  les données FR.
