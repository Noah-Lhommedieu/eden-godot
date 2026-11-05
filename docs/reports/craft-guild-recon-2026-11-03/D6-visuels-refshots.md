# D6 — Spécifications visuelles des écrans craft / guilde / récolte / commerce

Date : 2026-11-03. Sources : PNG de `ref-shots/` **lus visuellement** + spéc existante
`eden_godot/docs/client-assets/ref-shots-catalog.md` (concordante — les écarts
observés sont signalés), layouts `corpus/fr-full/uidata/layouts/` (415 fichiers),
imagesets `corpus/fr-full/uidata/imagesets/` (32 paires .imageset/.png),
`knowledge/guides-fr/images/` (17 fichiers), planches
`artifacts/corpus-inventory/sheets-index.tsv` (35 020 lignes ; le chemin
« artifacts/corpus-sheets/sheets-index.tsv » de la tâche n'existe pas — c'est
`corpus-inventory/` qui l'héberge).

Convention : `[x]` = case losange, `×` = bouton fermer, `?` = bouton aide.
Le client de capture est Reforged EN ; les fenêtres FR proviennent des
captures `tradeNPCSystem.png` et `proposition nouvelle quete.png`.

---

## 1. Craft — Magic Pool (« FusionPanel Craft (et guild npc crafter magic pool, forger...).png », 362×480)

Fenêtre flottante, titre « Magic Pool » + ×.
- En-tête : texte « Available for Class LV 11 (and below) commission » + `?`.
- Deux zones de slots côte à côte : **Use Item** (gauche, slot contenant le
  blueprint/parchemin) / **Charm** (droite, slot vide — booster optionnel).
- Sous Use Item : `Time   0 min. 10 sec.` puis `Cost   1080` [icône or] `0`
  [icône argent].
- Deux colonnes : « Materials Required: » (gauche) / « Success Rate: » (droite,
  valeur tronquée « 1.. » ≈ 100 %).
- Liste matériaux dans un cadre à filet bleu, lignes `icône + nom + possédé/requis`
  aligné à droite : `Blueprint: Sanguine Fortunate…` (orange) 1/1 ;
  `Sanguine Fragment` 71/40 ; `Infinity Core` 400/50 ; `Demonic Catalyst` 0/3
  (grisé = manquant).
- « Product: » : cadre bleu, slot icône + stack ×1 + nom blanc
  `Sanguine Fortunate Safety Stone`.
- Bas : boutons `Request` / `Close`.

Croisement layout : `worklistwnd.layout` (Root/Work : FrameWindow + Border +
TotalText + ScrollablePane — liste des commissions), `workresultwnd.layout`,
`magicboxwnd.layout`, `collectwnd.layout` (Root/CollectWnd : UseItemSlot +
ItemmallSlot + Border de ResultItemSlot0..n — **même gabarit Use Item/Charm**,
vraisemblablement la fenêtre de récolte/extraction). Sprites `MagicCube*` dans
`magiccube.imageset` + `radar.imageset`.

## 2. Tooltips de plans/formules (craft)

**`Blueprint.png`** (305×282) — tooltip fond violet foncé : icône parchemin +
titre **orange** `Blueprint: Head Gem: Saint Blessing` ; `Required Materials:`
liste blanche `…*1 / Etherworld Catalyzer *3 / Demon Crystal *75 / INT Magic
Elimination Gem Lv5 *1` ; `Finished Item:` + icône + nom orange ; texte
**rouge** `To inlay the item, right-click on it, and select a piece of equipment
with sockets. Can only be used on Lv75+ items.` ; pied `Gem | Head`.

**`Hover Item Plans ressources.png`** (283×433) — même gabarit :
`Formula: Leg Enchant - Gale Lv7`, `Work Level 12`, matériaux ×N,
`Finished Item:`, consigne **rouge** d'enchantement (right-click sur équipement,
« Can only be used on Leg Gear that is Lv 88 and above »), bloc stats **vert**
(`EVA +22`, `Move SPD +5%`), `Level Requirement 88`, note **jaune** entre
guillemets `«You can produce this item in the Magic Pools in towns.»`, pied
`Magic Pool Material | Can't be Traded`, ligne type `Enchanting Formula | Legs`.

**`item hover (plan mine ressources).png`** (407×528) — fenêtre `Shop`
(onglets `Items | Buy Back`, nom PNJ `RedLightInlayMerchant` en bandeau jaune
au-dessus, `?`) : liste d'articles `Lv85/Lv90 Ore Location Map` + prix
`160 or / 228 argent`, `… or / 704 argent` avec icônes de pièces. Tooltip
`Lv90 Ore Location Map` (orange) : `Work Level 12`, `Minerals that might be
extracted:` liste ×1 (Crimson Diamond, Bluefire Crystal, Deepfathom Shell,
Lavender Crystal), note jaune `«Minerals can be extracted from the town's
mine»`, pied `Mining Material | Can't be Traded | Level Requirement 90`.

Gabarit tooltip objet général (confirmé par `item hover (weapon).png`,
`weapon 2`, `trophy enchant`) : titre coloré par qualité (blanc<vert<bleu<
orange/jaune légendaire), blocs empilés : type/slot + flags (`Can't be Traded`,
`Already Bound`, `Durability: x/y`), stats de base avec `(120%)` qualité, stats
**vertes**, malus **rouges**, proc vert sur 2 lignes, `Level Requirement`,
ligne cyan `Fortification Level: +N` + bonus verts (weapon 2), flavour/notes
**jaunes**, pied catégorie + flags. Sprites `EmbedJewelry`/`StarJewelry` dans
`tooltips.imageset`.

## 3. Guilde

**`FindGuild.png`** — fenêtre `Guild Recruitment` (transparente, centrée) +
`?` : tableau colonnes `Guild Name | Leader | Mbrs. | Lv | Guild Town` (vide) ;
bas : `Find Town` (grisé) + pagination `◀◀ ◀ 1/1 ▶ ▶▶` + `Apply to Join` +
`Close`. Layout probable `familyfindingwind.layout` / `familyappwnd.layout`.

**`shot_0062_000149.png`** — `Town List` (transition ghosting) : filtres gauche
cases losange `Farm | Alchemy Lab` / `Mine | Foundry` / `Altar | Magic Pool`,
champs `Min Level` `Max Level`, `NPC Names`, `Town Name`, bouton `Search` ;
panneau droit détail ville : `Leader:` `Fame:` `Town Type:` `Member:` `Tax: %` ;
tableau `Name | Function | Time Remaining` ; `Close`. Layout
`familytownlistwnd.layout`. `shot_0063` = doublon méta (Okular).

**GAP majeur** : aucune capture du panneau guilde principal `familywnd` /
`familyinfownd` (infos, membres, rangs), ni `familystoragewnd` (banque 3 onglets
`familystoragetab1-3` + `familystoragelogwnd`), ni gestion de ville
(`familybuilding`, `familycenter`, `familybuilding_forest/_mountain`), ni
`familydonatedlg` (don guilde), ni `familyachievement`, ni batailles
(`familybattle*`, `familyorderwnd*` = ordres/annonces), ni `cnfamilyemblem`
(blason). 41 layouts `family*` existent dans le corpus → reconstruction possible
par layout, non vérifiée visuellement.

## 4. Commerce / services PNJ

**`tradeNPCSystem.png`** (693×492) — fenêtre **FR** `Payer`, thème violet
lavande (style legacy) : 4 quadrants — haut-gauche `Liste de paiement` (arbre
`Gloire des Amoureux de la Nature` → `- Branche Sèche / - Branche Dure /
- Pin Montagnard / - Sequoia Nordique / - Sequoia Écarlate` (sélection violette)
`/ - Sequoia à Pois… / - Acajou Polaire / - Pin Rouge / - Sequoia` + icône
parchemin) ; bas-gauche `Guide de contenu` (`Union de mission: Amoureux de la
Nature` + paragraphe lore FR) ; haut-droite `Contenu de paiement` (`Limite de
paiement:` « Points de gloire Amoureux de la Nature Plus de 2001 pts… » +
`Objet de paiement:` icône + `Sequoia Nordique 0/15`) ; bas-droite
`Points de gloire: [Amoureux de la Nature] 195` (dropdown) + zone résultat.
Boutons `Payer` (grisé) / `Fermer`. Layout `paysystemwnd.layout`
(`/Root/PaySystem`, textes sources `x繳交列表`/`x內容說明`). C'est le don de
ressources récoltées aux factions de gloire.

**`npc talk propositions.png`** (262×300) — dialogue `Zumi Merchant Alec` :
corps = texte italique entre guillemets (2 paragraphes), puis options avec
icônes : parchemin `!` + `L05. Oracle Magi II` (quête) ; icône sac violet +
`What would you like to buy?` (ouvre Shop) ; `Close` en bas.
**`npc talk réponse…"normal".png`** (262×484) — même fenêtre `Mining Captain
Laz`, réponse texte sans options, pseudo joueur inséré (`tutodebutant`).

**`Auction …line item on sell time.png`** (376×48) — ligne : icône+stack `999`,
nom `Crimson Sequoia`, qté `60`, prix `1500` [pièce or] `996` [pièce argent].
**`Auction …multiple line item…colonne name.png`** (382×135) — en-têtes
`Style | Item Name/Quality | Lv | Selling Price` ; lignes `Demon Ark` (icône
violette, nom jaune), Lv `0`, prix `25000/4`, `24980/4`, `23000/4`.
**GAP** : pas de capture de la fenêtre Auction complète (`auctionwnd`,
`auctionbuywnd`, `auctionsellwnd` : onglets recherche/mes ventes, dépôt d'objet).

**`Mail.png`** — `Mail`, onglets `Inbox | Send Mail` ; cases `Mark All` +
`Delete emptied mails` + `?` ; liste à cases losange + scrollbar ; bas
`Collect All | Delete | Close`.
**`Mail2.png`** — inbox remplie : lignes `icône + expéditeur + sujet + durée`
(`DrDott / Demon Ark / 24 hours`, `Astria / Crystal Root / 3 days`, `Official:
Auction / Auction successful:… / 365 days` en **jaune**) ; panneau droit
`From / Subject / corps / pièce jointe (icône + Cash on Del… + 20000 or) +
Collect` ; bas `Reply | Return | Close`. Layouts `mailwnd`, `maillistwnd`,
`mailreadwnd`, `mailsendwnd`, `mailitem`. **GAP** : onglet Send Mail non capturé.

**`ItemMallPanel.png`** — `Item Mall`, onglets `Item Mall | Loyalty Shop` ;
3 colonnes : `Select a category` (Limited Item, Featured, **Crafting Items**,
Pets & Mounts, Gear, Boosts, Fashion, Colorful Dye), `Catalog` central,
`Hot Items` droite (+`?`). Ligne catalogue = icône+stack + nom + badge rouge
`N left this week!` + prix barré promo + prix losange bleu + boutons
`Buy/Gift/BuyStack` ; solde `10721` losange bleu + `0` losange jaune ; mini
grille bas-gauche + `Collect All` ; pagination `1/5`. Tooltip `Viridian Safety
Stone (NT)` : flavour + effet fortification +3~+6 + rouge « will cause this
item to bind ». `shot_0066` = cat. Limited (Gear Forge Stone, World Call NT) ;
`shot_0067` = cat. packs (5/20-Slot Pack, Backpack Expansion) ; `shot_0070` =
fenêtre `Pet` (Pet List 0/30 + Seal/Unbind | Pet Info stats + Advanced | Pet
Gear 4 slots torche | Battle 5 slots fleur-de-lis + Battle | Pray 5 slots +
Pray | Rename / Pet Lunch Box Feed). Layouts `itemmall*` ×6 ; sprites
`Itemmall*`/`Mall*`/`ItemMallGold`/`ItemMallPoint` dans `cndejavu`/`newhand`.

**`SystemPanel.png`** — `Main Menu` modal : `Settings / Reset UI / Ch. /
Logout / Quit / Continue` empilés.

## 5. Social / carte / divers

**`CommunityPanel.png`** — `Community`, onglets `Friends | Search | Couple` ;
table `Ch.|Name|Lv|Class|Location|Info` ; modale `Enter Character Name`
(champ+Confirm/Close) ; `Block List` + `Edit Profile/Add/Block`.
**`CommunityPanel-SearchPlayers.png`** — onglet Search : `Search Results:
0 players found`, filtres `Lv Min/Max`, rôles `Defense/Melee DMG/Ranged
DMG/Healing/Magic DMG/Special`, `Others` `Omit`/`Omit anon.`, `Search` ; feedback
chat `Searching, please wait`/`Search Finished!`. Layout `communitywnd.layout`.

**`carte.png`** / `shot_0022` — `Region Map - Aven` : carte + quartiers ; bas
`World Map | Mainland | Regional`, `Coord: X:… Y:…`, `Icon Setting` ; colonne
`Search Listing` contient **`Gather/Craft`** et **`Guild Town`** (catégories
de recherche PNJ) ; légende `City Features` (incl. `Fame Shop`, `Auction`,
`Required Supplies`). Layouts `zonemapui*`, sprites `worldmap*`/`map*`.

**`daily reward.png`** — `Event`, onglets `Recommended Event | The Path of… |
Login Reward` ; grille Day 1-28 + `Claim` + colonne `Bonus!` ×4 ; bloc
`Information` 5 règles + Note ; `Claim all (2)` + hint. Layout `loginreward.layout`.

**`proposition nouvelle quete.png`** (302×558) — fenêtre quête **FR** thème
lavande : en-tête losange `Nv. 6 Oracle Magicienne V` + `Terminé` ; texte lore
FR entre guillemets (PNJ `Contremaître de la Mine Kev`) ; `Aller voir:` /
`Objectifs : Livrer l'objet "Rapport Traduit" à "Kev."` / `Récompense(s)
disponibles:` icônes ×N + `66` argent + `10` or + `EXP: 800` ; `Confirmer |
Retour`. Gabarit différent du QuestLog EN (`questwnd.layout`).

**`drop item, actualisation avancement objectif quete.png`** (545×432) — toast
jaune `Obtenir : Spore Fétide Fraîche` + tracker `Suivi de quêtes` : objectifs
losange + compteurs `x/y` (`Récupérer l'objet "…" auprès de "…" 0/2`, `2/2`),
texte quête FR, quête barrée rouge = abandonnée/verrouillée.

## 6. Série shot_0011→0056 (vérifiées — aucun craft/guilde)

`shot_0011` char-select ; `shot_0017/0018/0019` **Backpack** (onglets
All/Potions/Gear/Town/Special, grille fleur-de-lis ~200 slots, or `550`+gemme
`6`, `Archive All` + rangée 8 icônes + `Archive Save` — **shot_0018 : tooltip
`Fusion` sur le 3e bouton de la rangée = accès craft depuis le sac**) ;
`shot_0022` Region Map ; `shot_0030` titre zone ; `shot_0033/0036/0044/0057`
méta Okular ; `shot_0034/0037/0038/0039` combat champignons ; `shot_0040/0041`
Quest wnd ghosting + journal (`Quests: 0/25`, Track All, `Unacquired Quests`,
détail `Environment to take / Targets to take <PNJ> [Go]`, Share/Drop/Close) ;
`shot_0045` buffs ; `shot_0048/0050/0051/0052` panneau Class (onglets `Class
Skills | License Knowledge | Class News`, liste classes + `Current Clas` +
`Transform!` au survol, `Reg. Skills` or dispo + `+1`/`Max`, `Class Skills` CP,
arbre talents `N KPs Remain`/`Entered` + Confirm/Refresh, `Certificates: 9`
grille fleur-de-lis + Confirm, `Class Ability Bonus`/`CE List`) ; `shot_0056`
`Switched Class to Bard` + oiseaux.

## 7. Assets UI corpus liés (Partie 2)

- **Imagesets** : `magiccube` (MagicCube, MagicCubeNA{D,H,N,P}, boutons) ;
  `jobglyph` (JobGlyph) ; `tooltips` (`EmbedJewelry`, `StarJewelry`) ;
  `cndejavu` (`FamilyButton{Normal,Hover,Press,Light}`, `Itemmall*`,
  `ItemMallGold/Point`) ; `newhand` (`Itemmall`, `MallBonus/Gold/Point`) ;
  `radar` (`MagicCube*`, `Mail{Normal,Hover,Press}`, `ShopNA{D,H,N,P}`) ;
  `main`/`dejavu`/`cnguiderb` = atlas UI générique (fenêtres, slots fleur-de-lis,
  castingBar*).
- **Icônes** : `ui/itemicon/` 4 641 `.dds` (`a10109.dds`…), `ui/skillicon/`
  3 896, `ui/uiicon/` 4 050 — noms numériques, mapping via `data/db/*.ini`
  (colonne icône) ; pas de nommage métier.
- **Layouts clés** : craft `worklistwnd`/`workresultwnd`/`collectwnd`/
  `magicboxwnd`/`magiccube00-03`/`newmagiccube{wnd,recipewnd,recipedetailwnd}`/
  `strengthenwnd{,_na,_na_20,14}`/`strengthenfactory`/`strengthenslot`/
  `jewelrystudy`/`engineeringstudy`/`combinewnd`/`growthequipment` ;
  guilde 41 `family*` ; commerce `shopwnd`/`shopitem`/`auction*`/`itemmall*`/
  `mail*`/`tradewnd`/`stallui`/`stallbuy`/`paysystemwnd`/loteries/`loginreward`.
- **guides-fr/images/** : 17 fichiers — surtout rendus d'armes (epesquimo,
  katabanana, grimoire, pastequetranchante, capeazuree, messie_terrain,
  pierregravi, cristalalpaga, patch23_13) ; pas d'écrans UI craft/guilde.
- **sheets-index.tsv** (`artifacts/corpus-inventory/`, 35 020 lignes) : table
  `base | png | planche | ligne | colonne` — chaque `.dds` du corpus est rangé
  dans une planche `artifacts/corpus-sheets/…/sheet-NNN.png` ; permet de
  reconstruire les icônes `uiicon/ui/itemicon` par position.

## 8. GAPs — écrans attendus sans capture

| Écran | Layout corpus | Statut |
|---|---|---|
| Panneau guilde principal | `familywnd`/`familyinfownd`/`familymessagewnd` | **GAP** |
| Banque guilde 3 onglets | `familystoragewnd`+`tab1-3`+`logwnd` | **GAP** |
| Ville de guilde / bâtiments | `familybuilding*`/`familycenter`/`bulletterrainwnd` | **GAP** (Town List seule) |
| Fortification équipement | `strengthenwnd*`/`strengthenfactory` | **GAP** |
| Magic Cube (fusion objets) | `newmagiccubewnd`/`recipe*`/`magiccube00-03` | **GAP** |
| Études métier | `engineeringstudy`/`jewelrystudy` | **GAP** |
| Fenêtre récolte/extraction | `collectwnd` | **GAP** |
| Stand joueur | `stallui`/`stallbuy` | **GAP** |
| Échange joueur↔joueur | `tradewnd` | **GAP** |
| Auction fenêtre complète | `auctionwnd`/`auctionbuywnd`/`auctionsellwnd` | lignes+colonnes seules |
| Send Mail / lecture seule | `mailsendwnd`/`mailreadwnd` | inbox seule |
| Liste commissions craft | `worklistwnd` | Magic Pool seule (pas la liste) |
| Boutique PNJ onglet Buy Back | `shopwnd` | onglet Items seul |
