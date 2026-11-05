# G3 — Infrastructure UI client réutilisable (craft / guilde / shop)

Audit statique 2026-11-03, client Godot 4.8
(`eden_godot/godot_project/scripts/…`, cité `scripts/…`).
Statuts : **[VERIFIED]** lu dans le code cité · **[HYPOTHESIS]** déduit
des commentaires · **[GAP]** absent / stub.

---

## 1. Points d'entrée par fenêtre

### 1.1 Mécanismes génériques

- **Menu bas-droite natif** (`FunctionMenu.layout` /
  `FunctionMenu5pPet.layout`) : `MenuIconsPanel.Build` instancie le
  layout CEGUI et mappe les boutons → actions logiques
  `MenuAction` (`scripts/hud/MenuIconsPanel.cs:15-31,44-108`).
  Dispatch : `HudRoot.OnMenuAction` (`scripts/hud/HudRoot.cs:750-782`) :
  - `CommuBtn` → `ToggleByName("SocialPanel")` (social O) — l.760 ;
  - `CityBtn` → `ToggleByName("GuildPanel")` (guilde) — l.761-762 ;
  - `WorkBtn` → `ToggleByName("StallPanel")` = **étal joueur**
    (VendingWindow), **pas** le craft — l.765-767 ;
  - `SysBtn` → Main Menu `HudWindows.Toggle(SystemMenu)` — l.769.
- **Hotkeys** : actions `eden_*` rebindables
  (`settings/ClientSettings.cs:169-200`), dispatchées par
  `SettingsApplier._Input/Dispatch`
  (`settings/SettingsApplier.cs:559-661`) : I inventaire, C profil,
  K skills, J quêtes, M carte, N familier, L succès, O social.
  **[GAP]** pas d'action `eden_guild`/`eden_craft` — la guilde a une
  touche **G codée en dur** hors InputMap
  (`hud/GuildWindow.cs:441-453`, `_UnhandledKeyInput`).
- **`--hud-open=<clé>`** : parse dans `HudRoot` (l.299-307) →
  `ForceOpen` (l.386-648). Clés utiles : `guild` → `GuildWnd.Open()`
  (l.483-485), `craft` → nœud `CraftPanel|CraftWindow` (l.633),
  `shop` → `ShopWnd` (l.630), `familystorage` (l.502-506). Chaque
  fenêtre externe suit le pattern `ForceOpenExt<T>(node, w=>w.EnsureDemo())`
  (l.653-667) pour les captures hors session.
- **Registre fenêtres** : `HudWindows` (`hud/HudWindows.cs:17-148`) —
  Open/Close/Toggle/ToggleIn, premier plan au clic (`Front`,
  `OnPress`), Échap ferme la plus haute (`CloseTopmost`).
  Inscription automatique via `DragHelper.EnsureTitleBar`
  (`hud/DragHelper.cs:47-52`) et `EeSkin.MakeWindow`.
- **Montage** : `HudExtensions.MountAll` instancie toutes les
  fenêtres 2D sur le canvas HUD (`hud/HudExtensions.cs:166-271`,
  `Mount()` l.64-84 : FullRect + `MouseFilter.Ignore`). Exceptions :
  `SocialWindow`+`GuildWindow` montées par `MinimapPanel.Build`
  (`hud/MinimapPanel.cs:268-286`) ; `CraftWindow` est un nœud de
  `scenes/game.tscn` (`ZoneWindows/CraftWindow`).

### 1.2 Par fenêtre cible

| Fenêtre | Ouverture joueur | Code |
|---|---|---|
| **Guilde** | `CityBtn` menu, touche G (hardcodée), `--hud-open=guild` | `hud/HudRoot.cs:761,483` ; `hud/GuildWindow.cs:441-462` |
| **Craft (ordres)** | NPC métier 12-17 → 496 EnterWorks auto-ouvre ; bouton « LS / Racial Craft » de l'inventaire → `OpenWindow("craft")` | `hud/CraftWindow.cs:103-108,150-160` ; `hud/InventoryWindow.cs:246-251` |
| **Shop NPC** | dialogue NPC → option → 304 EnterShop → `ShopState.Changed` | `hud/ShopWindow.cs:16-17,59,189-199` |
| **Strengthen** | **[GAP]** 432 EnterStrengthen → stub texte « Enclume ouverte » seulement | `hud/FeatureDialogWindow.cs:104-108` |

## 2. Flux NPC → panneau [VERIFIED]

1. **Clic droit sur NPC** (kind 8) → approche ≤ `TalkRange` (3 u) →
   NCID 10 `ZoneTalkToNpc` — `player/PlayerController.cs:20-21,694-726`
   (armé l.713-714) et l.1211-1234 (envoi à portée). Picking entité =
   `PickCore` (projection écran, `PickRadiusPx` 36 l.65).
2. Serveur → **NCID 285** `ClientDisplayDialogView`
   `[i32 ctx][s16 n][n×(displayId,str,flag)][u8][i32 npcUid]` —
   bind `hud/HudNetBridge.cs:268-289` → `Dialogue.ShowDialogue`.
3. `NpcDialogueWindow` : layout natif `NpcTalkWnd.layout` (358×664,
   ancre 3 % / 1,8 %) + contenu injecté (papier crème, options
   icône+libellé) — `hud/NpcDialogueWindow.cs:48-93,99-249`. Clic
   option → `OptionChosen(index)` → NCID 11 `ZoneReplyDialog`
   (`HudNetBridge.cs:398-416` ; GameScene `SendDialogReplyAsync`
   `GameScene.cs:690-693`).
4. Réponses spécialisées auto-ouvrent les panneaux :
   **304 EnterShop** → `ShopState.OnEnterShop` → `Changed` →
   `ShopWindow` (bind GameScene.cs:2997) ; **496 EnterWorks** →
   `CraftWindow.OpenWorks` ; **393 EnterTransport** →
   `TransportState`/`TransportWindow` ; **357 EnterStorage** → bank ;
   **432 EnterStrengthen** → stub [GAP].
5. Sondes live existantes : `--talk-test`, `--service-test`,
   `--quest-test=<npcId>` (GameScene.cs:91-119,930-970,1299-1369) —
   téléportation + TalkToNpc sur NPCs de scène (`sNNN.ini [npc]`).

## 3. Tooltips items [VERIFIED]

`hud/ItemTooltip.cs` (statique, un seul tooltip via `Host` CanvasLayer
layer 200, l.669-736) :

- **API** : `Build(slot,def)` l.117, `BuildForId(id)` l.518,
  `Bind(control, Func<InventorySlot?>)` l.622, `BindId` l.634,
  `BindCustom` (tooltips libres : sorts…) l.641. Position clampée
  écran, sous le curseur (+16,+18) — l.724-734.
- **En-tête** : icône 36 px + nom coloré qualité
  (`QualityColor`, palette native Grey/Green/Orange/Blue/Gold/Purple,
  l.38-51) ; `+N` de fortification collé au nom (l.144).
- **Groupes filet-séparés** (l.214-251) : description codes `$N$`
  (couleurs l.53-67 : 7 jaune flavour, 12 rouge, 13 bleu-clair
  **liste de matériaux**, 15 vert enchant, 35 doré), bloc
  type/flags/durabilité/stats de base/sockets/étoiles (l.290-335),
  bloc stats + fortification (`Fortify`, l.342-407), bloc niveau
  requis + « Fortification Level: +N » cyan + bonus verts (l.420-450).
- **Plan/recette** : `&id&` dans la description → item produit
  **incrusté** (`AppendEmbedded` l.269-286, profondeur 1) — ref
  « Blueprint » / « Hover Item Plans ». **[GAP]** les matériaux
  possédés/requis ne sont **pas** colorés selon l'inventaire :
  `$13$` = bleu-clair fixe, aucune comparaison PlayerBag.
- Cas spéciaux : œuf familier (`AppendPetBody` l.459-474), livre de
  sorts pet (l.391-400), prix absent du tooltip (affiché par
  ShopWindow à côté).
- Sonde : `--tip-test=ids` (`hud/BagTipProbe.cs:54-83`) — pose les
  tooltips et logue leur texte (preuve capture).
- Tooltip de sort : `ItemTooltip.SkillTip.cs` (408 l., partial).

## 4. Pipeline icônes item → slot [VERIFIED]

`c_item.ini` col1 → `ItemTable.ItemDef.Code` →
`AssetCatalog.GetTexture("UI/itemicon/{Code}.png")` →
`LoadNativeDds` lit le `.dds` EDENFR (`Native.DdsTexture.Load`) sous
`corpus/fr-full/UI/itemicon/` — `scripts/AssetCatalog.cs:69-79,641-693`.
Repli courant : `UI/skillicon/` puis `UI/uiicon/` (ex.
`hud/BuffBar.cs:676-678`, `hud/DestinyWindow.cs:480-482`).
Cellule modèle : `hud/StorageItemCell.cs:122-141` (icône +
quantité `StackCount` l.107-114 + `ItemTooltip.Bind`) ; fond de case
`EeSkin.EmptySlot` (fleur-de-lys DejaVu `Slot*`, `hud/EeSkin.cs:36-41`)
ou `hud.Region("main","FunctionSlot")` (`EquipSlotsPanel.cs:87`).
Imagesets CEGUI : `hud.Tex.GetRegion(new CeguiImageRef(set,img))`
(`hud/HudRoot.cs:130-131`, `ui/CeguiToGodot.cs:193`).

## 5. Drag & drop [VERIFIED]

- **Fenêtres** : `DragHelper.EnsureTitleBar` (barre native DejaVu ou
  repli + × `__auto_closebtn__` + drag-par-le-fond `HudWindowDrag`) et
  `DragHelper.Persist` (position → `user://clientSettings.json`,
  clampée viewport) — `hud/DragHelper.cs:26-160`. `EdenWindow`
  factorise tout : `BuildWindow(layout,titre,persistKey,pin)`
  (`hud/EdenWindow.cs:20-77`).
- **Items** : Godot `_GetDragData/_CanDropData/_DropData`, payload
  Dictionary `{kind,cont,slot,bag,itemId}` — `StorageItemCell.cs:163-199`
  (aperçu = icône 40 px). Inventaire : `ItemCell`
  (`InventoryWindow.cs:1460-1660`), drop → NCID 7 SwitchItemLocation,
  Maj+drop stack>1 → popup split NCID 84 ; ack 277 appliqué
  localement (l.856-930). Équipement : `EquipCell` drop → équipe
  (`EquipSlotsPanel.cs:204-209,380-392`), double-clic → déséquipe.
- Routage NCID laissé à la fenêtre hôte (banque type 4, fchest
  177/178 — `StorageItemCell.cs:26-27`). **Réutilisable tel quel**
  pour slots strengthen/craft.

## 6. Thème [VERIFIED]

- **Layouts CEGUI natifs** : `hud.Build("X.layout")` →
  `CeguiToGodot.BuildLayoutFile` (`hud/HudRoot.cs:106-124`) ; contexte
  `CeguiContext.Load(uidata)` async (`hud/HudRoot.cs:77-95`,
  `ui/CeguiBuilder.cs:142-176` : `schemes/DejaVu.scheme` +
  `looknfeel/dejavu.looknfeel` + imagesets + fonts).
- **Chrome fenêtre** : frame 9-slice `EeSkin.Nine`, titlebar/close
  natifs (`DragHelper.cs:95-157`), `EeSkin.MakeWindow`,
  `EeSkin.SkinLists` (parchemin), `CeguiToGodot.SkinUnstyled`.
- **Palette thémable** : `ThemePalette` (rôles WindowBody/Parchment/
  Tab/GreyBtn…, `hud/ThemePalette.cs:18-118`) dérivée de l'atlas
  thémé ; thèmes Blue/Coffee/Green/Purple/Red
  (`gamedata/themes/*`), appliquée avant construction des fenêtres
  (`HudRoot.cs:147-151`), `UiThemeBoot.Normalize`
  (`ui/UiThemeBoot.cs:7-21`). `EeSkin.PanelTab/InsetBox/GreyButton/
  Grey<T>` suivent `_palette` (`EeSkin.cs:179-323`).

## 7. Fenêtres-modèles à suivre

- **ShopWindow** (430 l.) : modèle NPC-service — layout natif
  `ShopWnd.layout` + onglets détournés + contenu injecté + état
  (`ShopState.Changed`) + envois directs via `SessionCarry` +
  `DragHelper.Persist` (`hud/ShopWindow.cs:40-140`).
- **InventoryWindow** : modèle grille multi-item codée (pas de
  layout natif Reforged) — EeSkin + `ItemCell` + onglets + lignes
  redimensionnables (`InventoryWindow.cs:115-319`) ; resize ancré =
  `ui/GridResizeMath.cs`.
- **FamilyStorageWindow/BankWindow** : modèle `StorageItemCell` +
  grille scroll + NCID dépôt/retrait.
- **NpcDialogueWindow** : modèle layout natif + contenu procédural
  injecté (papier + boutons).
- **CraftWindow/GuildWindow actuels = 100 % codés** (`Panel` +
  `TabContainer`/`ItemList`, `CraftWindow.cs:415-501`,
  `GuildWindow.cs:78-115`) — candidates directes à reconstruire sur
  layouts natifs.

## 8. Assets UI natifs disponibles [VERIFIED]

Layouts `corpus/fr-full/uidata/layouts/` pertinents (inutilisés par le
code — aucun `hud.Build` ne les charge) :
- **craft** : `worklistwnd.layout` (Root/Work), `workresultwnd.layout`,
  `combinewnd.layout`, `collectwnd.layout`, `collectionwnd.layout`,
  `collectionset.layout`, `krcollectionwnd.layout` ;
- **fortif/fusion** : `strengthenwnd.layout`,
  `strengthenwnd14.layout`, `strengthenwnd_na.layout`,
  `strengthenwnd_na_20.layout`, `strengthenslot.layout`,
  `strengthenfactory.layout`, `krstrengthenwnd.layout` ;
- **guilde** : `familywnd.layout`, `familyinfownd.layout` (+`_na`),
  `familyappwnd.layout`, `familyadministration.layout`,
  `familyprivilege.layout`, `familyfindingwind.layout`,
  `familymessagewnd.layout`, `familybattlerankwnd.layout`, etc.
  (~30 fichiers `family*`) ;
- **commerce** : `shopwnd.layout` (déjà utilisé), `shopitem.layout`,
  `stallui.layout`, `autosellui` imageset.
Ces layouts ne référencent que `set:DejaVu`/`set:DejaVuImage`
(vérifié par grep) → tous skinnables avec l'atlas existant.
Imagesets corpus : 78 fichiers ; aucun imageset dédié craft/family
[GAP — icônes métier à sourcer depuis `main`/`Radar`/`ToolTips` ou
`UI/uiicon`]. Dérivés déjà convertis : `godot_project/ui_textures/
imagesets/` (94 fichiers, dont `ToolTips.png`, `EdenQualityTier.png`,
`RepairAllBtn.png`) + `gamedata/uipatch/`.

## 9. GAPs consolidés

1. **Strengthen** : 432 reçu → stub `FeatureDialogWindow` seulement ;
   C→S 119 `ZoneServerStrengthenItem` existe
   (`protocol/ZoneClientToServerMessages.cs:225-268`) mais aucune UI
   slot+drag → construire sur `strengthenwnd*.layout`.
2. **Tooltip matériaux** : `$13$` bleu fixe, pas de coloration
   possédé/manquant vs PlayerBag (à ajouter pour recettes).
3. **Hotkeys guilde/craft** : G hardcodé hors `eden_*` bindings ;
   craft sans raccourci (menu WorkBtn = étal, pas métier).
4. **Craft racial (LS)** : `OpenWindow("craft")` montre le panneau
   ordres NPC — l'UI raciale dédiée (`collectwnd`/`combinewnd`)
   n'existe pas ; recettes `CraftTable` déjà chargées
   (`CraftWindow.cs:63-65,275-277`).
5. **GuildWindow** = panneau codé générique ; `familywnd.layout` et
   les ~30 layouts family* (infos, administration, app, privilèges,
   bâtiments) sont disponibles mais non consommés.
6. **Imagesets métier** absents (work/craft/family) — les layouts
   n'utilisent que DejaVu/DejaVuImage.
7. `worklistwnd.layout`/`workresultwnd.layout` existent mais
   CraftWindow n'en utilise aucun (tout est procédural).
