# Spec visuelle de référence — ref-shots/ vs client Godot EDENFR

Critères « conforme / non conforme » pour comparer les panneaux Godot aux
captures du client natif. Sources : `ref-shots/*.png` (lues une par une),
`eden_godot/docs/client-assets/ref-shots-catalog.md`,
`docs/reference/corpus/layout-impl-map.md`.

## 0. Conventions transverses (à vérifier sur TOUS les panneaux)

| # | Élément | Critère conforme |
|---|---|---|
| T1 | Chrome fenêtre | Fond gris anthracite opaque, cadre fin gris clair, **titre en haut à gauche**, bouton **X** carré en haut à droite ; bouton **?** souvent près de l'angle supérieur droit |
| T2 | Onglets | Onglets carrés en haut de la zone de contenu (pas en bas), onglet actif surligné |
| T3 | Cases à cocher | **Losanges** (pas des carrés) ; coché = losange plein |
| T4 | Boutons | Gris foncé, bord clair, libellé centré ; disposés en ligne en bas de fenêtre |
| T5 | Slots d'objets | Fond **fleur-de-lis** sombre, icône carrée, compteur de stack blanc en bas à droite, **bordure colorée par qualité** (blanc/vert/bleu/violet/orange) |
| T6 | Monnaies | Prix = valeur + **icône pièce or** et valeur + **icône pièce argent** (empilés en 2 lignes dans les cellules de prix) |
| T7 | Pagination | Format « ◂ ◂ 1/5 ▸ ▸ » (flèches doubles + « page/total ») |
| T8 | Tooltips objets | Fond noir/violet translucide ; titre coloré par qualité ; icône+nom en tête ; sections séparées par lignes fines ; **stats bonus vertes**, **restrictions/malus rouges**, **flavour/hints jaunes**, **fortification cyan** ; pied = « Catégorie / Sous-catégorie / flags (Can't be Traded / Already Bound / Durability x/y) » |
| T9 | Langue | Client Godot = EDENFR → **libellés FR** ; les ref-shots EN ne valident que le layout. Refs FR existantes : quest log*.png, questtracker*.png, proposition nouvelle quete.png, tradeNPCSystem.png, drop*.png |
| T10 | Scrollbar | Fine, flèches triangulaires aux extrémités, curseur losange |

## 1. Inventaire (sac) + drag & drop

Ref : `shot_0017_235743.png` (= `shot_0018_235749.png`, `shot_0019_235754.png` — doublons), `item hover (basic ressources on classic mob on map).png` (grille + tooltip).

| Élément | Critère conforme |
|---|---|
| Titre/onglets | Fenêtre « Backpack » (attendu FR : « Sac »/« Inventaire ») ; **5 onglets en haut : All / Potions / Gear / Town / Special** |
| Grille | ~18 colonnes × ~13 lignes de slots fleur-de-lis ; objets stackés avec compteur ×N ; certaines icônes bordées bleu/orange (qualité) |
| Monnaies en pied | « 550 » + icône or et « 6 » + icône gemme, en bas à gauche |
| Bas de fenêtre | Bouton « Archive All » à gauche, rangée de **8 icônes** au centre, « Archive Save » à droite |
| Divers | Scrollbar à droite ; petites icônes de tri en haut à droite de la grille ; tooltip item au survol (fond violet translucide sur la grille) |
| Drag & drop | **AUCUN ref-shot** du drag (pas de capture du fantôme d'icône ni du swap). Critère minimal d'après code : `scripts/hud/DragHelper.cs`, popup split `dividedlg.layout` (NCID 84). À spécifier depuis `uidata/layouts/inventorywnd*.layout` + `dividedlg.layout` |

## 2. Tooltips d'objets (matrice par type)

| Type | Ref-shot | Spécificités à vérifier |
|---|---|---|
| Arme légendaire | `item hover (weapon).png` | Fond noir, titre doré « Holy - Blazing Jet » ; bloc blanc : « Artillery Gun / ATK SPD 2.0 Seconds / Can't be Traded / Durability: 105/105 / Basic P-ATK 24571 (120%) / Basic M-ATK 12285 (120%) » ; stats vertes (AGI/INT/LCK/M-CRIT…) ; proc jaune-vert « When attacking, 5% chance… » + ligne « Effect: … » ; « Level Requirement 90 » en pied |
| Arme heritage +12 | `item hover (weapon 2).png` | Fond violet ; titre bleu « Heritage Spear Lv60+12 » ; **notes bleues puis rouges empilées** ; bloc blanc type/main/ATK SPD/durabilité/ATK (100%) ; stats vertes ; **« Fortification Level: +12 » en cyan** + liste bonus verts |
| Consommable | `Item hover consommable avec durability.png` | Fond noir, icône bordure rouge, titre ; flavour jaune ; « loses 1 Durability point per use » rouge ; pied « Item / Already Bound / Durability: 3/20 » ; effet + « Cooldown: 1 min. 30 sec. » |
| Ressource commune | `item hover (basic ressources on classic mob on map).png` | Tooltip court : nom blanc, description jaune, pied « Item / Can't be Traded » |
| Blueprint/plan | `Blueprint.png`, `Hover Item Plans ressources.png` | Fond violet, titre orange ; « Required Materials: » items ×N colorés ; « Finished Item: » icône+nom ; **consigne d'usage rouge** ; stats de l'enchant vertes + éventuels malus rouges ; note jaune « You can produce this item in the Magic Pools in towns. » ; « Level Requirement N » ; pied « Magic Pool Material / Can't be Traded » |
| Trophée | `item hover (trophée).png` | Titre jaune « Holy - Neondo's Imperial Contract » ; flavour italique jaune ; ligne flags « Accessory / Trophy Exclusive / Can't be Traded / Already Bound » ; stats vertes dont proc « When attacking, 5% chance… » + « Debuff: DEF -10%, All Resistances -10. » ; « Level Requirement 90 » |
| Enchant trophée | `item hover (trophy enchant).png` | Bonus verts **et** malus rouges dans le même bloc (ACC -8%, G-Healing -83) |
| Pet légendaire | `item hover pet (legendary).png` | Titre doré ; « Pet Level: Lv1 0% » ; « Pet / Already Bound » ; **notes lettres** « Type: Slash A / ATK B / ACC B+ / CRIT C+ / SPD D+ » ; 3 skills décrits + bonus « Owner's … » |
| Livre prière pet | `item hover (pray book) for pet pray skill.png` | Titre bleu ; ligne rouge « This book will replace the pet's third pray slot. » |
| Plan mine | `item hover (plan mine ressources).png` | « Work Level 12 », « Rarity: », « Minerals that might be extracted: » ×1, note jaune, pied « Mining Material / Can't be Traded / Level Requirement 90 » |

## 3. Boutique NPC (marchand) + « Payer » (gloire)

Ref EN : `item hover (plan mine ressources).png`. Ref FR : `tradeNPCSystem.png`.

| Élément | Critère conforme |
|---|---|
| Fenêtre shop | Titre « Shop » (FR attendu : « Boutique »/nom marchand) + X ; **2 onglets « Items / Buy Back »** ; « ? » en haut à droite |
| Lignes articles | Icône + nom coloré + **prix or et argent empilés avec icônes de pièces** ; flèches de scroll |
| Pied | Hint « Hold the 'Shift' key and left-click an item to select an amount » (FR attendu : équivalent « Maj + clic gauche ») ; backpack visible à côté (fenêtre shop + sac ouverts ensemble) |
| Tooltip article | Tooltip violet standard (cf. §2) — ici « Lv90 Ore Location Map » |
| Fenêtre « Payer » (gloire faction, ref FR) | Titre « Payer » ; **colonne gauche « Liste de paiement »** (items requis) + bloc « Guide de contenu » (lore faction « Amoureux de la Nature ») ; colonne droite « Contenu de paiement / Limite de paiement / Points de gloire … / Objet de paiement » avec objet « 0/15 » ; « Points de gloire [Amoureux de la Nature] 195 » ; boutons **« Payer » / « Fermer »** |

## 4. Item Mall

Ref : `ItemMallPanel.png`, `ItemMallPanel-LoyaltyShop.png`, `shot_0066_000211.png`.

| Élément | Critère conforme |
|---|---|
| Structure | Titre « Item Mall » ; **2 onglets : Item Mall / Loyalty Shop** ; **3 colonnes : « Select a category » (gauche) / « Catalog » (centre) / « Hot Items » (droite)** |
| Catégories | Liste : Limited Item, Featured, Crafting Items, Pets & Mounts, Gear, Boosts, Fashion, Colorful Dye |
| Lignes catalogue | Icône + nom coloré ; **badge rouge « N left this week! »** OU « Buy Character Level: N » jaune ; prix avec icône diamant bleu, **prix barré sur promo** (ex. 490→290 rouge) ; boutons **Buy / Gift / BuyStack** |
| Vignettes | Rangée de miniatures sous le catalogue + slots fleur-de-lis vides |
| Pied | « Collect All » à gauche ; soldes « 10721 [diamant bleu] » + « 0 [gemme jaune] » ; « Close » ; pagination « 1/5 » |
| Tooltip | Tooltip noir standard, suffixe « (NT) » = No Trade (cf. Viridian Safety Stone) |

## 5. Hôtel des ventes (HDV)

Ref : `Auction (hotel de vente) multiple line item on sell time and colonne name.png`, `Auction (hotel de vente) line item on sell time.png`.

| Élément | Critère conforme |
|---|---|
| En-têtes colonnes | **« Style / Item Name/Quality / Lv / Selling Price »** (bandeau violet) — colonnes triables |
| Ligne annonce | Icône objet (stack « 999 » visible), **nom coloré par qualité**, Lv, **prix or+argent empilés avec icônes pièces** |
| Exemple | « Demon Ark » (violet) : 25000 or / 4 arg ; « Crimson Sequoia » qté « 60 », 1500/996 |

## 6. Guilde / ville de guilde

Ref : `FindGuild.png`, `shot_0062_000149.png` (+ méta `shot_0063_000154.png`).

| Élément | Critère conforme |
|---|---|
| Guild Recruitment | Titre « Guild Recruitment » + X + « ? » ; colonnes **Guild Name / Leader / Mbrs. / Lv / Guild Town** ; boutons « Find Town », pagination « 1/1 », « Apply to Join », « Close » |
| Town List | Filtres à gauche : **checkboxes Farm / Mine / Altar / Alchemy Lab / Foundry / Magic Pool**, Min/Max Level, NPC Names, Town Name, Search ; colonnes droites **Leader / Fame / Member / Tax %, Town Type** ; feedback chat « Searching, please wait / Search Finished! » |
| **SANS ref-shot** | Fenêtre FamilyInfo (info guilde propre) : spec = `uidata/layouts/familyinfownd_na.layout` (`GuildWindow.cs`) |

## 7. Mort / « Revivre ici » — **SANS ref-shot**

Aucune capture de l'écran de mort. Spec = `uidata/layouts/revivemenuui.layout`
(implémenté par `scripts/hud/DeathWindow.cs` ; `revivecount.layout` = compteur,
partiel). À extraire du layout : titre (Big5 « 一二三四五六七八對你施展復活技能 »
→ traduction runtime FR), options attendues type « Revivre ici / point de
retour », minuterie ReviveCount.

## 8. Sélection / création de personnage / canal

Ref : `Selection_channel.png`, `Selection_personnage.png` (+ `shot_0011_235711.png`).

| Élément | Critère conforme |
|---|---|
| Canal | Tableau 2 colonnes **Server / Ch.** ; « Ch01(Recommended)\|0 chara » **rouge**, Ch02 « (Maintenance) » grisé ; losange de sélection ; « Welcome to Eden Reforged! » ; **Confirm / Cancel** ; « Official Website » + « Quit » bas-droite ; disclaimer bas de page |
| Sélection perso | Lobby temple 3D ; **« Preview » haut-gauche = grille 2×5 d'icônes violettes (emotes)** ; carte perso haut-droite : « Ch01(Ch01) », portrait, badges **« 100 » orange + « 13 » bleu**, nom, zone « Aven » ; flèches prev/next + **« Start Game »** bas-centre ; colonne droite : **Set password / Remove password / Select Server / Logout** |
| Création perso | **AUCUN ref-shot** → spec = `createcharacterwnd.layout` (+ ver2, ver2_2, `cncreatecharacterwnd.layout`) → `scripts/screens/CharCreateScreen.cs` |

## 9. Quêtes : journal, tracker, dialogues

Ref FR : `quest log.png`, `quest log 2.png`, `quest log 3.png`, `questtracker le vrai.png`, `questtracker le vrai 2.png`, `questtracker quete oublier.png`, `questracker objectif avec item de quete a utiliser.png`, `proposition nouvelle quete.png`, `drop au sol…`, `drop item…`. Ref EN : `QuestLog.png`, `QuestTracker.png`, `QuestTracker .png`, `quest UI.png`, `npc talk propositions.png`, `npc talk réponse….png`.

| Élément | Critère conforme |
|---|---|
| Journal (FR) | Titre « Quête » ; onglets **« Quête / Légende »** ; « Quête(s) : N/25 » ; losange « Tout Suivre » ; arbre gauche catégories (« DNJ 5 Joueurs : … », « Expédition Héroïque », « Quête de la classe … », zones, « Quêtes Manquées »), sous-quêtes « Nv. NN - … » **titres rouges/violets/verts** ; panneau droit : titre quête, lore « … », « Choix de l'environnement: / Choix des cibles: » jaunes, cibles avec bouton **« OK »**, récompense or/argent ; hint bas « Maj + clic gauche pour suivre une quête. » ; boutons **« Partager / Abandonner / Fermer »** (« Abandonner » grisé si non sélectionnable) |
| Tracker (FR) | En-têtes « Utiliser Pierre de Portail » + « Arrière-plan » (icônes losange) ; 2 boutons-onglets **« Suivi de quêtes / Quêtes oubliées »** ; titres quêtes **rouges « Nv. NN - … »** ; objectifs = **losange bleu** (collecte/done) ou **losange violet/grisé** (kills) + texte « Battre "Golem de Calcaire" 0/5 » ; **« Terminé » jaune** à droite des quêtes finies ; lore/descr en vert ; objet de quête utilisable = icône item intégrée au tracker |
| Offre de quête PNJ (FR) | Titre = nom PNJ (« Contremaître de la Mine Kev ») + « Terminé » haut-droit ; puce verte « Nv. 6 Oracle Magicienne V » ; lore « … » ; sections « Aller voir : / Objectifs : / Récompense : / Récompenses disponibles : » + icônes items + « 66 [argent] » + « EXP: 800 » ; boutons **« Confirmer / Retour »** |
| Dialogue PNJ (EN) | Titre = nom PNJ ; texte italique ; options listées : quête préfixée **« ! » orange** (« ! L05. Oracle Magi II »), option shop avec icône sac ; « Close » bas-droite |
| Feedback loot (FR) | Toast « Obtenir : <item> n/N » jaune haut-gauche + mini-carte item ; message chat « Vous avez accepté une quête multi-joueur… » |

## 10. Chat

Ref : `ChatPanel.png`, `ChatPanel-Peer.png`, `shot_0077_000311.png`, `shot_0078_000316.png`, `shot_0079_000322.png`, `shot_0081/0082/0083`.

| Élément | Critère conforme |
|---|---|
| Onglets | « Normal / Battle » + bouton « Settings » à droite des onglets |
| Barre canaux | **« ◂ Say / Peer / Party / Guild / Zone / World / Buzz ▸ »** puis canaux custom **C1 C2 … C9 C0 C' C+** ; bouton « Emoji » + icône portrait à gauche |
| Messages | Préfixe **« <Canal> » coloré par canal** (Peer = violet, System = rouge) ; pseudo préfixé « [GM] » ; bulle temporaire au-dessus du perso en Say |
| Channel Settings | Fenêtre : cases **losange** pour canaux (Background, General Chnl, Peer Chnl, Party Chnl, Guild Chnl, Region Chnl, World Chnl, Couple's Chnl, Battlefield Chnl, Sys Message, Whisper Chnl cochés) puis événements (EXP/Coin/Fame/Item Acquired, DMG Dealt/Taken, Status Info grisés) ; Confirm/Cancel |
| Canal custom | Modale « Please enter channel name: » champ + Confirm/Cancel |

## 11. Classes / sorts / talents

Ref : `ClassePanel.png`, `HoverToolTipClasse.png`, `shot_0048_000033.png`, `ClassePanel-ClassTalent&Certificat.png`, `shot_0050/0051/0052`, `case pour certificat.png`, `ClassePanel-ClassNews.png`(+2,+3), `ClassPanel-ChangementDeClasse.png`, `shot_0056`, `NouveauSkillApresLevelUp.png`, `ClassAbilitybonuslogo.png`, `skill hover info`, `panel primaire d'expertise d'une classe.png`, `second panel expertise d'une classe.png`.

| Élément | Critère conforme |
|---|---|
| Fenêtre | Titre « Class » ; **3 onglets : Class Skills / License Knowledge / Class News** ; colonne gauche « Sort by class » (icône+nom+Lv, **cadenas** si verrouillée, « Current Clas » sur la classe active, bouton « Transform! »/« Activate ») ; « Class Ability Bonus » + « CE List » bas-gauche |
| Onglet Class Skills | En-tête classe (icône + nom + Lv) ; **« Reg. Skills » avec « 550 Gold 9 Silver Available »** = 2 colonnes de skills (icône+nom+Lv + boutons **« +1 » / « Max »**) ; **« Class Skills » avec « N remaining CP »** ; tooltip skill = coût MP, cooldown, effet, « Next level » rouge, coût **« … Class Points! » rouge** |
| License Knowledge | Arbre de talents (icônes + flèches, verrouillés grisés) ; panneau droit « Class Talent : N KPs Remain / N KPs Entered » + Confirm/Refresh ; panneau bas « Certificates: N » = **grille 4×3 slots fleur-de-lis** avec cases « + » orange |
| Class News | Fiche : description classe, « Class Stat Bonus » en % (HP/MP/STR/AGI/INT/WIS/LCK), « Weekly Performance », preview perso, « Set Outfit », boutons numérotés 1-9 |
| Tooltip classe | Description + **prérequis rouges** (« Class Requirement: * Character Level 45 / * Cleric Level 30 ») ; tooltip « Class Ability Bonus » = Total HP/MP/ATK/Magic/G-Healing |
| Changement classe | Toast central « Switched Class to X » + animation oiseaux sombres + messages chat « You have learned skill '…' » ; annonce skill = grand texte blanc centré |

## 12. Craft / Fusion / Renforcement

Ref : `FusionPanel Craft (et guild npc crafter magic pool, forger...).png`.

| Élément | Critère conforme |
|---|---|
| Magic Pool | Titre « Magic Pool » ; « Available for Class LV 11 (and below) commission » + « ? » ; **slot « Use Item » + slot « Charm »** ; « Time 0 min. 10 sec. » ; « Cost 1080 or 0 arg » ; « Materials Required: » = lignes icône+nom **compteurs `possédé/requis` colorés** (vert/rouge selon dispo, grisé si 0) ; « Success Rate: » ; « Product: » icône+nom ×N ; boutons **Request / Close** |
| Strengthen/Fusion/Combine | **Pas d'autre ref-shot** (même shot Magic Pool cité pour strengthenwnd) → spec = `strengthenwnd*.layout`, `combinewnd.layout` (absent → notice seulement dans Godot), `worklistwnd.layout`/`workresultwnd.layout` (ordres de travail, `CraftWindow.cs`) |

## 13. Trade joueur / Étals — **SANS ref-shot**

| Panneau | Spec de repli |
|---|---|
| Trade joueur | `uidata/layouts/tradewnd.layout` → `scripts/hud/TradeWindow.cs` (implémenté) — extraire disposition 2 zones d'échange + Confirm |
| Étals joueur (vente/achat) | `stallui.layout` + `stallbuy.layout` (titre Big5 « XXX的個人商店 ») → `scripts/hud/VendingWindow.cs` ; `buymsgdlg.layout` (popup) |

## 14. HUD général

Ref : `juste le jeu.png`, `TopLeftVitals.png`, `HotbarPanel.png`, `DoubleEXPbar.png`, `party UI.png`, `targetframe (Elite mob).png`, `pet skillbar.png`, `shot_0030_235854.png`, `shot_0045_000016.png`, `TimedCastSkill.png`, `carte.png`/`shot_0022`, `drop au sol…`, `mouvement caméra.png`.

| Élément | Critère conforme |
|---|---|
| Vitals haut-gauche | Portrait circulaire **cadre argenté orné** ; badges « 100 » orange + losange + « 11 » bleu ; nom ; **HP rose « actuel/max »** (segment rouge = manquant) ; **MP bleue** ; grille 1-6 / 7-12 sous le portrait ; icône cadeau |
| ULTIMATE | Barre top-center « ULTIMATE Lv 1  EXP 200/400  Perks 2 » + jauge rose + 3 icônes ; buffs = icônes sous cette barre avec tooltip au survol |
| Minimap | Haut-droite : carte + « Aven  X:…,Y:… » + orbe bleu ; ~10 icônes sous la map ; horloge « 23:57 » + date « 17/57/22 » ; tracker quêtes en dessous |
| Hotbar | Bas-centre : flèche « A »+triangle gauche, **slots numérotés 1-0/-/+** (raccourci blanc sous chaque slot), icônes skills, boutons +/× + cadenas à droite ; **2 barres fines au-dessus : EXP « 0.000% » (dorée) + CP « 100.000% » (violette), 3 décimales** |
| Menu | **10 boutons colorés bas-droite** (perso, sac, quête, classes, communauté, succès, mall, pet, …, engrenage système) |
| Party frames | Cartes empilées : portrait cerclé or, classes+Lv, pseudo, HP rouge, MP bleue ; **couronne sur le leader** |
| Cadre cible | Portrait circulaire, **« 81 ★★ Nom » (étoiles dorées = élite)**, HP/MP ; étiquette monde « <Type> Nom » rouge ; Target's Target accolé |
| Feedback monde | Toasts **rouges centrés** (« You have acquired <…> state ») ; « Entering/Leaving Combat » ; titre de zone jaune géant ; noms+HP flottent ; icônes service au-dessus des PNJ ; toast loot FR « Obtenir : item n/N » ; pilier lumineux violet sur drop de quête |
| Carte région | « Region Map » plein écran + panneau « Search Listing » droit + légende « City Features » + « World Map/Mainland/Regional » + coords + « Icon Setting » |
| Pet bar | `pet skillbar.png` → `PetFunctionWnd5p.layout` |

## 15. Panneaux SANS ref-shot (spec = layouts CEGUI `uidata/layouts/*.layout`)

| Système | Layout(s) de référence | Script Godot | Statut (layout-impl-map) |
|---|---|---|---|
| Trade joueur | `tradewnd.layout` | `TradeWindow.cs` | implémenté |
| Étals joueur | `stallui.layout`, `stallbuy.layout`, `buymsgdlg.layout` | `VendingWindow.cs` | implémenté/partiel |
| Mort/Revive | `revivemenuui.layout`, `revivecount.layout` | `DeathWindow.cs` | implémenté/partiel |
| Création perso | `createcharacterwnd*.layout`, `cncreatecharacterwnd.layout` | `CharCreateScreen.cs` | implémenté |
| Drag & drop sac | `dividedlg.layout` (split), `inventorywnd*.layout` | `InventoryWindow.cs`, `DragHelper.cs` | implémenté |
| Info guilde (membre) | `familyinfownd_na.layout` | `GuildWindow.cs` | implémenté |
| Fusion (bouton sac) | `combinewnd.layout` | — | absent |
| Renforcement | `strengthenwnd*.layout`, `strengthenfactory/slot` | `FeatureDialogWindow.cs`+`CraftWindow.cs` | partiel |
| Hôtel (stockage) | `hotelstoragewnd.layout` | — | absent |
| Fame/Réputation | `reputation.layout`, `likehatewnd.layout` | `ProfileWindow.cs` (stub) | absent |
