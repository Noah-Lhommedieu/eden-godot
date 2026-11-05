# G1 — Inventaire client Godot : fenêtres craft / guilde / commerce

Date : 2026-11-03. Périmètre : `eden_godot/godot_project/scripts/` (client Godot
4.8 C#). Sources citées `fichier:ligne`. Croisé avec
`docs/reference/corpus/layout-impl-map.md`, `D4-layouts-craft-fusion.md` et
`00-PLAN.md` (vocabulaire pivot : guilde=family, VDG=familytown, work=ordres,
lifeskill=racial, strengthen=fortification, combinewnd=fusion, magicbox/cube).

**Verdict global** : le propriétaire a raison — **aucune fenêtre craft n'est
fidèle au natif**. Le wire réseau est largement câblé (NCIDs prouvés) mais les
UI sont soit des panneaux plats codés en C# (CraftWindow, GuildWindow coquille,
FeatureDialogWindow, LotteryWindow, AltarWindow), soit des layouts natifs
instanciés puis **re-remplis de contenu custom** (listes Godot Button/ItemList
là où le natif a des SlotImageItem / MultiColumnList stylés). Aucune fenêtre
craft n'instancie `worklistwnd`, `workresultwnd`, `strengthenwnd*`,
`combinewnd`, `magicboxwnd`, `magiccube*`, `lifeskillitem` — pourtant tous
présents dans `corpus/fr-full/uidata/layouts/` et instanciables via
`HudRoot.Build()` (`scripts/hud/HudRoot.cs:106-124`).

Échelle d'état : **ABSENT** (rien) / **SQUELETTE** (stub texte ou boutons
morts) / **PARTIEL** (wire OK + UI custom ou contenu non natif) /
**FONCTIONNEL** (utilisable en jeu) / **FIDELE** (layout natif + contenu
natif). Aucune fenêtre auditée n'atteint FIDELE.

---

## 1. Tableau d'inventaire

| Fenêtre | Fichier (montage) | Layout natif utilisé | NCID S→C / C→S | État | Écarts majeurs vs natif | Ref-shot |
|---|---|---|---|---|---|---|
| **Ordres de travail** (works, NPC 12-17) | `hud/CraftWindow.cs` (`scenes/game.tscn` nœud `ZoneWindows/CraftWindow`, ext_resource `2_craft`) | **AUCUN** — `BuildPanel()` 100 % code (`CraftWindow.cs:415-500`) | 496/497/498/500/433/312 ; C→S 170 PlayerWork, 171 ResultOK (`CraftWindow.cs:103-146`) | **PARTIEL** | `worklistwnd`/`workresultwnd` jamais instanciés : panneau 660×500 sombre + 2 `ItemList` + `ProgressBar` ; pas de slots items (ResultItem1-4), pas de fenêtre résultat dédiée, pas de rendu matériaux/prix façon « Magic Pool » ; filtre recettes HYPOTHESIS documentée (`CraftWindow.cs:33-36`) | `FusionPanel Craft…png` (Magic Pool : slots Use Item/Charm, compteurs possédé/requis colorés, Success Rate, Request/Close) |
| **Fortification** (anvil) | `hud/FeatureDialogWindow.cs` (autoload `HudExtensions`, `HudExtensions.cs:235`) | **AUCUN** — panneau plat titre+label | `ZoneClientEnterStrengthen` (bind `ZoneMiscBinds.cs:93`) → texte « Enclume ouverte » (`FeatureDialogWindow.cs:104-105`) ; résultat → chat (`:107-109`) ; cast bar réutilisée dans CraftWindow (433) | **SQUELETTE** | 6 layouts `strengthenwnd*.layout` + `strengthenfactory`/`strengthenslot` au corpus, jamais chargés ; **C→S 119 StrengthenItem / 120 StrengthenClose codecs écrits mais JAMAIS envoyés** (`ZoneClientToServerMessages.cs:226-271`) — aucun chemin d'envoi | `FusionPanel Craft…png` |
| **Fusion** (ItemCombo) | — | `combinewnd.layout` présent corpus, **jamais chargé** | — | **ABSENT** | bouton sac « Fusion » → simple notice « Fusion: via NPC » (`InventoryWindow.cs:250`) | `FusionPanel Craft…png` |
| **Magic Box** (612/613) | `FeatureDialogWindow` commentaire seul | `magicboxwnd.layout` présent corpus, jamais chargé | S→C `ZoneClientOpenMagicBoxDialog`/`ZoneClientMagicBoxResult` bindés (`ZoneMiscBinds.cs:219-220`) → `MiscPackets.Received` ; **aucun `case` dans FeatureDialogWindow** → rien à l'écran ; C→S 257 `ZoneServerExecuteMagicBox` codec mort (`ZoneClientToServerMessages.cs:1627`) | **SQUELETTE (cassé)** | commentaire obsolète `FeatureDialogWindow.cs:76-77` prétend qu'une `MagicBoxWindow` existe — **aucune classe de ce nom n'existe** (grep exhaustif) ; log `HudExtensions.cs:269` idem (« magicbox » listé sans montage) | — |
| **Magic Cube / Arcane Box** | — | 9 layouts `magiccube*` + `newmagiccube*` au corpus, 0 code | — | **ABSENT** | pile UI entière manquante (D4 : « plus grosse masse de travail UI craft ») | — |
| **Craft racial** (LifeSkill, 5 races) | — (données seules) | `lifeskillitem`, `engineeringstudy`, `jewelrystudy`, `jobglyph*` présents corpus, jamais chargés | S→C 543 → `PlayerProfileState` « lifeskill » (`HudExtensions.cs:468`) ; C→S 213/214 codecs (`ZoneClientToServerMessages.cs:1159-1180`) | **SQUELETTE** | bouton sac « LS / Racial Craft » → `OpenWindow("craft")` = fenêtre **works** (`InventoryWindow.cs:251`) — mauvaise cible : le natif ouvre le panneau métier racial ; aucune UI liste/skills/oubli | — |
| **Guilde** | `hud/GuildWindow.cs` (montée `MinimapPanel.cs:283-286`) | Coquille custom `Panel` 780×700 + **FamilyInfoWnd_NA.layout imbriqué** dans l'onglet Infos (`GuildWindow.cs:716-771`) | monde 9 FamilyOp / 10 QueryInfo / 73 Emblem ; zone 49 create / 81 refresh / 82 emblem / 159 donate / 346 self (`GuildWindow.cs:23-33`) | **PARTIEL** | coquille onglets reproduite en code (pas `familywnd.layout`) ; **29/40 layouts family absents** : bâtiments (`familybuilding*`, `familycenter`), ville (`familytownlistwnd`, `familymemberfindingwnd`), recherche (`familyfinding*`), ordres (`familyorderwnd*`), privilèges (`familyprivilege`, `familysetprivilegedlg`), messages (`familymessagewnd*`), achievement, administration | `FindGuild.png` (recrutement : tableau+postuler+Find Town), `shot_0062_000149.png` (TownList) |
| **Coffre famille** | `hud/FamilyStorageWindow.cs` (`HudExtensions.cs:185`) | `FamilyStorageWnd.layout` coquille ; Tab natif masqué, pages custom (`:65-69`) | 507/508/504/505/506 ; C→S 177/178/179 (`FamilyStorageWindow.cs:16-36`) | **PARTIEL** | cellules `StorageItemCell` custom vs `familystoragetab1-3` (slots natifs) ; journal custom vs `familystoragelogwnd` | — |
| **Banque** | `hud/BankWindow.cs` (`HudExtensions.cs:182`) | `StorageUI.layout` coquille ; contenu custom (`rentBtn` masqué `:65-67`) | 357/358/373 ; C→S 60/61/62/69 + 7 switch (`BankWindow.cs:16-36`) | **PARTIEL** | grille `StorageItemCell` vs slots natifs ; `storagerentdlg` absent | — |
| **Boutique PNJ** | `hud/ShopWindow.cs` (`HudExtensions.cs:181`) | `ShopWnd.layout` (TabControl réutilisé : Tabsell=achat, TabBuy→vente `:90-101`) | 304 EnterShop ; C→S 22 buy / 23 sell / 63 leave ; réparation 45→342→46, 47→343→48 (`ShopWindow.cs:123-145,236-357`) | **PARTIEL** | lignes = `Button` Godot avec icône vs `shopitem.layout` (cellule native) ; fixAll/fixone re-parentés manuellement | `tradeNPCSystem.png` |
| **Hôtel des ventes** | `hud/AuctionWindow.cs` (`HudExtensions.cs:189`) | `AuctionWnd.layout` coquille ; onglets buy/sell custom | 430 open ; C→S 114 query / 115 buy / 116 sellquery / 117 sell / 118 cancel (`AuctionWindow.cs:16-34`) | **PARTIEL** | `auctionbuywnd`/`auctionsellwnd` (colonnes MultiColumnList natives) remplacés par `VBoxContainer` de lignes ; pas de colonnes nom/prix/temps stylées ; pas d'enchère progressive (protocole sans bid) | `Auction…line item on sell time.png`, `…multiple line item…colonne name.png` |
| **Échange J↔J** | `hud/TradeWindow.cs` (`HudExtensions.cs:227`) | `TradeWnd.layout` (selfBorder/targetBorder + cellules custom `TradeCell`) | 331-340/353/394/395 ; C→S 36-42/91 (`TradeWindow.cs:19-31`) | **PARTIEL** | cellules custom sur gabarit natif — le plus proche du natif du lot commerce | — |
| **Étal joueur** | `hud/VendingWindow.cs` (`HudExtensions.cs:228`) | `StallUI.layout` + `StallBuy.layout` ; staging cells `StallDropCell` custom | 375-379 ; C→S 72 view / 74 close / 75 closeview / 76 buy / 77 sell (`VendingWindow.cs:12-43`) | **PARTIEL** | contenu custom ; `buymsgdlg` (popup message étal) absent | — |
| **Hôtel/chambres** | `hud/HotelWindow.cs` (`HudExtensions.cs:254`) | `HotelWnd.layout` + liste custom (MemberList natif détourné `:24-40`) | 539 open / 537 search / 522,525 room ; C→S 207 EnterRoom (`HotelWindow.cs:11-14`) | **PARTIEL** | `hotelstoragewnd`, `hotelprivilegedlg`, `playerroomwnd` absents ; boutons loyer/visite/privilèges sans wire | — |
| **Loterie** | `hud/LotteryWindow.cs` (`HudExtensions.cs:236`) | **AUCUN** — panneau code | 511/436-438/448/449/552/607-608 ; C→S 129 GoLottery (`LotteryWindow.cs:8-16`) | **PARTIEL** | layouts `crystallotterywnd`/`diamondlotterywnd` non utilisés | `ItemMallPanel-LoyaltyShop.png` |
| **Autels Star/Gem** | `hud/AltarWindow.cs` ×2 (`HudExtensions.cs:240-252`) | **AUCUN** — `EeSkin.MakeWindow` | solde via `PlayerCoins` ; **aucun wire de tirage — boutons Draw désactivés** (`AltarWindow.cs:60-62`) | **SQUELETTE** | ouverts par boutons radar Lty/IGLty (`MinimapPanel.cs:207-212`) ; doublon fonctionnel avec LotteryWindow | — |
| **Dialogue service générique** | `hud/FeatureDialogWindow.cs` (`HudExtensions.cs:235`) | **AUCUN** | misc packets (instance 564/575, hôtel, église, paysystem, missionshop, strengthen…) | **SQUELETTE** | stub titre+texte+Close ; `paysystemwnd`, `messagewnd` non chargés | — |
| **Dialogue NPC** | `hud/NpcDialogueWindow.cs` (monté par HudRoot — `hud.Dialogue`) | `NpcTalkWnd.layout` + contenu custom (paper crème + options) ; Listbox natif masqué (`NpcDialogueWindow.cs:67-74`) | 285 dialog → C→S 11 ReplyDialog index (`HudNetBridge.cs:398-414`) ; 388 fiche quête | **PARTIEL/FONCTIONNEL** | fonctionnel en jeu ; options servies par le serveur (shop/craft/gossip/quêtes) ; style papier custom ≠ gabarit natif | ref « npc talk » |
| **Aides partagées** | `hud/EdenWindow.cs` (base `BuildWindow`/`FinishWindow` — 1 seule fenêtre l'utilise : `TransportWindow.cs:27`) ; `hud/EeSkin.cs` (`MakeWindow` :233, 9-slice atlas) ; `hud/DragHelper.cs` (titlebar+persist `ui_layout.cfg`) ; `hud/StorageItemCell.cs` (cellule drag bank/fchest/bag) ; `hud/EquipSlotsPanel.cs` (équipement porté, NCID 7/8) ; `hud/ClientUiStore.cs` (settings json) | — | — | — | `EdenWindow` factorise layout→repli mais **CraftWindow/GuildWindow/etc. ne l'utilisent pas** (pattern dupliqué à la main) | — |

## 2. Pipeline CEGUI → Godot

- **Parseur** `ui/CeguiLayout.cs` : XML tolérant (contrôle, `<?xml?>` dupliqué,
  repli Big5 — `CeguiLayout.cs:186-231`) ; URect CEGUI 3e/4e paires = bords
  droit/bas → ancres Godot (`CeguiLayout.cs:59-183`).
- **Spec intermédiaire** `ui/CeguiBuilder.cs` : `UiNodeSpec` pur C# testable
  hors moteur (`CeguiBuilder.cs:44-117`) ; ~30 types mappés
  (`CeguiBuilder.cs:325-379`) : FrameWindow→Panel+titlebar auto, PushButton
  →Button/TextureButton, Editbox→LineEdit, MultiLineEditbox→TextEdit,
  Listbox/ComboDropList/ItemListbox/**MultiColumnList**→ItemList (colonnes
  aplaties — perte sémantique des en-têtes), TabControl→TabContainer,
  Combobox→OptionButton, Spinner→SpinBox, ProgressBar, ScrollablePane
  →ScrollContainer, **SlotImageItem→TextureRect** (statique — **pas une
  cellule interactive** : aucun drag/drop/quantité natif), ListHeader(+Segment)
  →Control/Button, Falagard StaticText/Image/Static→Label/TextureRect/Panel.
- **Matérialisation** `ui/CeguiToGodot.cs` : `BuildLayoutFile`/`BuildTree`
  (`:356-376`) + `UiTextureCache` (atlas `res://ui_textures/imagesets/`, thèmes
  recolorés `:46-72`) + `SkinUnstyled` (`:738`).
- **LookNFeel/scheme/imageset** : `CeguiLookNFeel.cs`, `CeguiScheme.cs`,
  `CeguiImageset.cs` — contexte `CeguiContext.Load(uidata)` chargé async
  (`HudRoot.cs:77-95`).
- **Instanciable directement** : oui — `hud.Build("X.layout")`
  (`HudRoot.cs:106-124`) retourne un `Control` ou null ; helper
  `HudExtensions.BuildNativeWindow` (`HudExtensions.cs:95-109`). Les 3
  fenêtres « modèle » du pattern : ShopWindow/BankWindow/TradeWindow.
- **Limite structurante** [VERIFIED D4] : 0 `Property Event` dans les layouts —
  tout binding est code C# ; les **cellules d'items (SlotImageItem) arrivent
  non interactives** → chaque fenêtre re-code ses cellules (StorageItemCell,
  TradeCell, StallDropCell, SellDropSlot).

## 3. Points d'entrée

- **`--hud-open=<clés>`** (`HudRoot.cs:299-318, 386-647`) : `craft` (générique
  → `CraftPanel`/`CraftWindow` `:633`), `guild` (`:483`), `bank` (`:488`),
  `familystorage|familybank|fchest` (`:502`), `trade` (`:493`),
  `vending|stall` (`:497`), `shop` (générique → `ShopWnd` `:630`),
  `auction` (`:446`), `hotel` (`:589`), `lottery|altar` (`:612` — ouvre
  LotteryWnd, pas les AltarWindow), `featuredlg` (`:607`), `itemmall`,
  `neobank`, `mail`. **Aucune clé strengthen/combine/magicbox/lifeskill.**
- **Menu bas droite** (`HudRoot.OnMenuAction` `:750-781`) : `city`→GuildPanel,
  `community`→SocialPanel, `work`→StallPanel, `itemmall`→ItemMallWnd ;
  autres actions → « not implemented ».
- **Barre fonctions du sac** (`InventoryWindow.cs:243-258`) : Archive,
  **Fortify→notice seulement**, **Fusion→notice seulement**, **LS→OpenWindow("craft")**,
  Backpack manager, Stall→`ToggleStall`, Special currency→notice, Sort.
- **Radar** (`MinimapPanel.cs:207-212`) : Lty/IGLty→AltarWindow Star/Gem.
- **Touches** : `G`→GuildWindow (`GuildWindow.cs:441-453`), `O` social,
  `B/I` sac… ; pas de touche craft.
- **Flux NPC** : clic NPC → C→S 10 TalkToNpc → S→C 285 → `NpcDialogueWindow`
  (`HudNetBridge.cs:75-77`) → clic option → C→S 11 ReplyDialog(index)
  (`HudNetBridge.cs:398-414`) → le **serveur** pousse la fenêtre service :
  304 shop→`ShopState` (`GameScene.cs:2997`), 357 banque (`:3028`),
  393 transport, 496 works→CraftWindow, 430 HDV, 507 coffre famille,
  539 hôtel, 612 magicbox (sans UI), EnterStrengthen (texte seul).
  [VERIFIED] le dialogue propose bien les choix shop/craft — c'est le contenu
  serveur du 285 qui les fournit (`NpcDialogueWindow.cs:255-269`).

## 4. Tooltips

`hud/ItemTooltip.cs` (737 lignes, custom) affiche : nom +`Strengthen` (`:144`),
bloc enchant vert T_Enchant (`:376-393`), « Fortification Level: +N » cyan +
stats fortifiées `ItemTipFormat.Fortify` (`:417-444`), « Level Requirement »
(`:425`). **Manques vs natif** : pas de « work level »/niveau de métier, pas
d'enchantements magic-box (`MagicBox[3]` wire `ZoneVendingMessages.cs:286-292`
jamais rendus), pas d'item-on-item (enchant scroll → cible : `ZoneItemUse`
a les champs Target* `ZoneItemMessages.cs:122-131` mais l'envoi n'en remplit
aucun — `InventoryWindow.cs:1205-1209`). Sonde : `BagTipProbe.cs` (`--tip-test`).

## 5. GAPs

1. **[GAP] Aucune UI craft fidèle** — `worklistwnd`/`workresultwnd` jamais
   instanciés ; la fenêtre « ordres » est un panneau custom.
2. **[GAP] Fortification = stub** — 6+2 layouts présents, aucun chargé ;
   C→S 119/120 morts ; pas de slots item/charme/sécurité, pas de résultat UI.
3. **[GAP] Fusion, MagicBox, MagicCube, LifeSkill, Glyph** — layouts au corpus,
   zéro fenêtre ; MagicBox même sans case dans FeatureDialogWindow malgré les
   binds posés (`ZoneMiscBinds.cs:219-220`) — commentaire obsolète à corriger.
4. **[GAP] SlotImageItem non interactif** — le pipeline rend les slots natifs
   en TextureRect : toute refonte devra fournir une cellule slot native
   (drag/drop/qty/tooltip) réutilisable, sinon chaque fenêtre re-codera la sienne.
5. **[GAP] Guilde : 29/40 layouts family absents** — VDG (ville, bâtiments,
   ordres, privilèges, recherche, messages) entièrement sans UI côté client ;
   `familybuilding*`/`familycenter`/`familytownlistwnd`/`familyfinding*` au corpus.
6. **[GAP] Commerce partiel** — lignes custom vs cellules `shopitem`/
   `auctionbuywnd`/`auctionsellwnd` (colonnes natives perdues : ItemList aplati) ;
   `buymsgdlg`, `itemmallquestitem`, `specialcoin*`, `mailitem` gabarits non utilisés.
7. **[GAP] Item-on-item (enchant scroll→équipement)** — wire supporté
   (`ZoneItemUse` Target*) mais jamais rempli ; pas de curseur de ciblage.
8. **[GAP] EdenWindow sous-utilisé** — le pattern layout→repli factorisé n'est
   appliqué que par TransportWindow ; chaque fenêtre duplique la chorégraphie.
9. **[GAP] Tooltip** — pas de work level, pas d'enchants magic-box, pas de
   rendu `tipdetail.layout` natif.
10. **[GAP] --hud-open** — aucune clé pour strengthen/combine/magicbox/
    lifeskill → captures QA impossibles pour ces fenêtres.
