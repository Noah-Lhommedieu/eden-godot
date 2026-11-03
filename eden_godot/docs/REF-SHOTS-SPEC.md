# REF-SHOTS-SPEC — spécification visuelle exhaustive du client natif

> Cahier des charges pour la **copie totale** du client Eden Eternal.
> Chaque image de `ref-shots/` est décrite intégralement : contenu,
> mise en page, style, états. Les descriptions servent de référence
> pour recoder les UI, HUD et panels — aucune feature visible sur une
> ref ne doit manquer dans le client Godot.
>
> Ordre : I. constantes de style → II. écrans hors-jeu → III. HUD
> permanent → IV. fenêtres/panels → V. états de jeu dynamiques →
> VI. inventaire des frames séquences.

## I. Constantes de style (transverses à toutes les refs)

- **Fenêtre CEGUI** : cadre gris anthracite/argenté, barre de titre
  sombre, texte blanc aligné gauche, bouton **X** en haut à droite ;
  bouton **« ? »** doré en haut à droite des sous-panneaux listes.
- **Checkbox** : losange (vide/coché), jamais carré.
- **Scrollbar** : bleu pétrole, fine.
- **Bouton** : rectangle gris foncé, texte blanc, groupé en bas de
  fenêtre (« Default | Confirm | Cancel », « Share | Drop | Close »).
- **Intérieurs de listes** : blanc cassé/parchemin clair sur fond
  sombre ; onglets en haut du corps de fenêtre.
- **Textes flottants monde** : noms de mobs en jaune, noms joueurs en
  blanc, états acquis en rouge, titre de zone en jaune géant.
- **Animations fenêtres** : fade-in à l'ouverture (cf. shot_0040 —
  la fenêtre Quest arrive semi-transparente).
- **Horloges doubles** sous la minimap : heure de session + heure
  serveur (ex. `23:57` / `17:57:33`, puis `00:00:00` / `18:00:00`).
- Fenêtre en **mode fenêtré** 1410×878 typiquement, titre « Eden
  Eternal », icône globe en haut à gauche, boutons − □ X.

## II. Écrans hors-jeu

### Selection_channel.png
Écran de sélection de canal. **Le décor est un rendu 3D** : grande
halle dorée (dôme à poutres, ciel bleu, arche ornée violet/argent,
escaliers tapis rouge). Fenêtre centrée sans titre : deux colonnes
« Server » / « Ch. » ; liste serveur avec « Ch01(Recommended)0 chara »
en **rouge** ; liste canaux « Ch01 » (icône gemme verte), « Ch02
(Maintenance) » grisée ; barre type lecteur média (◀ ● ▶) sous la
liste serveur ; bouton « ? » haut-droite ; zone message « Welcome to
Eden Reforged! » ; boutons « Confirm » / « Cancel » centrés en bas.
Coin bas-droite : « Official Website », « Quit ». Texte trademark
centré en bas (« We are not affiliated with Aeria Games or X-Legend… »).

### Selection_personnage.png (+ shot_0011_235711.png)
Sélection de personnage dans la **même halle 3D** : personnage debout
sur un sceau turquoise lumineux au pied d'un escalier tapis rouge,
bannières bleues, statues gardien-dogs dorées, portes émettant de la
lumière dorée.
- **Haut-gauche** : fenêtre « Preview » — grille 5×2 d'icônes de
  portraits de classes (violet/bleu).
- **Haut-droite** : carte perso « Ch01(Ch01) » — portrait orné +
  badges « 100 » / « 13 » + nom « EEi??ti??O?C?i?? » + zone « Aven ».
- **Bas-centre** : deux gros boutons flèches courbes (rotation du
  perso) + bouton « Start Game ».
- **Bas-droite empilé** : « Set password », « Remove password »,
  « Select Server », « Logout ».

### changement de map - changement d'étape de connexion.png
**Écran de chargement natif** (LA référence pour le nôtre) :
- Illustration plein écran de la zone (ici Aven : ville blanche et
  turquoise sur île, mer, aéronefs, cerisiers roses).
- **Cadre doré orné** tout autour (volutes + gemmes aux coins).
- **Haut-gauche** : nom de zone « Aven » + sous-titre « The Island
  Sanctuary ».
- **Haut-droite** : « Tip: Obtain Fame to purchase exclusive items
  at Fame Exchang… » (astuce contextuelle).
- **Bas-gauche** : bloc lore 3 paragraphes blancs sur dégradé sombre
  (« Aven, the Holy City, built of glimmering ivory and blue stone… »).
- **Bas-centre** : barre de progression **ornée** — extrémité lune/
  étoile dorée à gauche, sprite alpaga blanc au milieu, cristal violet
  à droite, remplissage rose.

## III. HUD permanent (visible sur la plupart des refs)

Cible = `juste le jeu.png`, `mouvement caméra.png`, `QuestTracker.png`,
`HotbarPanel.png`, `DoubleEXPbar.png`, `TopLeftVitals.png`,
`ChatPanel.png`, `ChatPanel-Peer.png`.

### TopLeftVitals.png — vitals haut-gauche
Portrait rond dans cadre orné sombre ; nom du perso à sa droite ;
badges « 100 » (jaune) et « 11 » (bleu) ; barre **HP rose**
« 14307/14999 » ; barre **MP bleue** « 11160/12262 » ; en dessous
**grille 2×6 de slots numérotés 1-12** (petites cases sombres
chiffrées — slots de sorts liés aux vitals).

### Barre ULTIMATE — haut-centre (⚠️ NATIVE — présente partout)
Titre « ULTIMATE » dans un bandeau sombre ; sous-titre « ULTIMATE
Lv 1   EXP 200/400   Perks 2 » ; barre de progression rose/blanche
avec embouts ornés ; **4 icônes empilées à sa droite** (skills ULT).
Présente dans juste le jeu, carte, ClassePanel, mouvement caméra,
TimedCastSkill, tous les shots combat.

### Minimap — haut-droite
Carte carrée en cadre orné ; **nom de zone** (« Aven »,
« Limestone Mtn. ») au-dessus ; **coordonnées « X:501 Y:583 »** ;
colonne d'icônes à droite du cadre ; **ligne d'icônes colorées**
sous la carte (≈10 : lettre, parchemin, boussole…) ; **deux lignes
d'horloge** avec icônes horloge/calendrier (« 23:57 » +
« 17/57/22 » ou « 23:56:05 » / « 17:56:05 »).

### Quest tracker — droite, sous la minimap (QuestTracker.png)
Panneau semi-transparent à bord clair : en-tête = losange
« Use Portal Stone » + losange « Background » (bleu) + bouton
« − » de repli ; deux boutons-onglets sombres « Track Quests » /
« Unacquired Quests » ; liste scrollable : noms de quêtes rouges/
roses (« L08. Angor Quarry I », « L65. Mutant Beast Expedition
(Daily) »…) suivis des cibles blanches (« Mining Captain Kev »,
« <Mutant Beast Expedition> Shania »), chaque ligne objectif
précédée d'un losange blanc ; scrollbar bleue à droite.

### ChatPanel.png — chat bas-gauche
Onglets « Normal » / « Battle » **en haut à gauche** ; bouton
« Settings » (icône +) **en haut à droite du panneau** ; zone de
texte à scrollbar bleue à droite ; ligne « Emoji » + icône ;
ligne de canaux « ◀ Say Peer Party Guild Zone World Buzz ▶ » ;
raccourcis canaux « C1 C2 C3 C4 C5 C6 C7 C8 C9 C0 C' C+ » +
bouton ✕ en bout. Texte coloré par canal (Peer = violet,
System = rouge, Say = blanc). `ChatPanel-Peer.png` montre le spam
rouge « <System>You have learned skill "Jewel Fusion"… » et le
scroll actif.

### DoubleEXPbar.png — doubles barres XP sous le chat
Deux barres fines superposées : barre haute = losange **orange**
« Exp » + barre sombre « 0.000% » centré blanc ; barre basse =
losange **violet** (CP/classe) + barre violette « 100.000% ».

### HotbarPanel.png — hotbar bas-centre
À **gauche** : flèches ▲▼ de page + indicateur « 1 » (petit
losange). 12 slots carrés bord clair/doré : icônes de sorts
(bleues clerc) + slots vides noirs ; **label de touche au-dessus
de chaque slot** (« 7 8 9 0 ' + ») + petits chiffres blancs en
coin. À **droite** : petit bouton système + verrou.
Une **seconde rangée C1…C+/C0/C'** existe au-dessus (visible dans
SystemPanel/QuestTracker refs).

### Menu icônes — bas-droite
Grille compacte d'icônes colorées (≈8-10 : tête, coffre,
parchemin, carte, personnage, monstre, coffre-cadeau, chat,
engrenage…) ; badge « ! » rouge possible sur une icône
(QuestTracker.png).

### Frame de cible (shots 0034/0037/0085)
Cadre orné argent/or centré haut-gauche : portrait du mob +
« Red Cap Mushroid » + barres HP/MP + icônes de buffs sous le
portrait ; sous-frame « **Target's Target** » avec nom du joueur.

### Éléments contextuels permanents
- « Entering Combat » rouge / « Leaving Combat » blanc centre-écran.
- Titre de zone jaune géant à l'arrivée (« Limestone Mtn. »).
- « You have acquired <État> state » rouge centré.
- **Popup tutoriel** bas-centre « How to move — Use WASD or
  left-click on the ground to move… » avec icône parchemin.
- **Bulle de chat** au-dessus de la tête du perso (« bonjour » —
  shot_0082).
- **Bandeau « Switched Class to X »** plein écran avec dragon
  blanc (shot_0056).

## IV. Fenêtres / panels

### Backpack (shot_0017/0018/0019)
Grande fenêtre sac : grille ~10 colonnes d'items stackés ; onglets
All/Potions/Gear/Town/Special ; bas : « Archive All », compteurs
d'or « 550 » et « 6 ». L'UI ne bouge pas quand la caméra tourne.

### carte.png — Region Map
« Region Map - Aven » en grand : carte colorée à gauche (quartiers
étiquetés : Industrial Ward, Courthouse, Market Zone×2, Trading
District, Temple Knight, City Hall, Pier) + « **Search Listing** »
milieu (arbre `>` repliable ~30 entrées : Arena Battle Merchant,
Library, Alpaca Shop, Class Awakening Quest, Mystic Dungeon, Warp
Portal, Arena…) + « **City Features** » droite (légende d'icônes :
General/Weapon/Armor Merchant, Fame Shop, Auction, Revival Point,
Warp Portal, New Quest, Quest NPC). Bas : « World Map | Mainland |
Regional », « Coord: X:509 Y:296 », « Icon Setting ».

### ClassePanel.png + HoverToolTipClasse.png + ClassPanel-ChangementDeClasse.png (+ shots 0048/0050/0052/0056)
Fenêtre « Class » :
- **Colonne gauche** « Sort by class » + « ? » : liste classes avec
  icône/nom/lv — Ranger 🔒, Inquisitor 🔒, **Cleric Lv.14 « Current
  Clas »** surligné (bordure verte + tooltip « Transform! »), Bard
  Lv.1, Shaman, Sage 🔒 ; plus bas Mimic, Warrior Lv.1, Knight,
  Templar, Dragon Knig, Thief Lv.1 ; bas : « Class Ability Bonus »
  + bouton « CE List ».
- **En-tête droite** : 3 grandes icônes + « Cleric Lv.14 » + bouton
  « Activate ».
- **Onglets** : Class Skills | License Knowledge | Class News.
- « Reg. Skills » : bandeau « 550 Gold 9 Silver Available », grille
  2 colonnes (icône + nom + Lv.N + boutons « +1 » / « Max ») : Earth
  Shock, Cure, Revive, Strategic Retreat, Recovery Link, Wind of the
  Ancestor, Harden | Tornado, Prevention, Snub, Health Charm, Tribal
  Counterattack, Ancestral Roar, Spiritual Inspiration.
- « Class Skills » : « 4331 remaining CP », Light's Sanction Lv.9,
  Life Cure Lv.11, Hard Truth Lv.11, Purifying Wind Lv.1.
- **Tooltip sort** (ClassePanel.png) : « Life Cure — Cost: 38 MP,
  Cooldown: 30 seconds, For 15 seconds recovers +84 HP per second.
  Next level: Cost 41 MP, +88 HP… Learning requires 153 Class
  Points! » en rouge.
- **Tooltip classe verrouillée** : description + « Class
  Requirement: * Character Level 45 * Cleric Level 30 » rouge.
- Onglet **License Knowledge** (shot_0050) : arbre Class Talent
  (icônes reliées par flèches, badges « 1 »), « 1 KPs Remain »
  rouge / « 7 KPs Entered » vert, boutons Confirm/Refresh ;
  « Certificates: 9 », grille 3×4 slots fleur-de-lis (2 dorés posés)
  + 1 bleu en liste, bouton Confirm.
- Onglet **Class News** : flux d'annonces (ClassNews2/3 refs).
- « Switched Class to Bard » = grand bandeau animé + dragon blanc.

### ParameterPanel-*.png — fenêtre « Settings » (5 onglets)
- **Display** : « Optimize » losange + radios Low/Mid/High ;
  dropdown « Window Mode : 1280x720 » ; Anti-aliasing Off/2x/4x ;
  sliders à flèches avec icônes : Char Draw, Field, Max FX,
  Shadows, Max FPS, Background, Groundcover ; radios Models
  Low/High, Textures Low/Mid/High, Glow FX Off/…/Max, Outline
  Off/Character/All ; section « Special FX » = 7 losanges (Death,
  Photo, Blur, Glare, Water Wave, Shadows, 3D Effect).
- **Audio** : losange « Mute » + sliders Music / SFX (reste vide).
- **3e onglet (préférences)** : slider « Mouse » + ~24 losanges en
  2 colonnes : Name, Guild, Title, Vitals, Health Bar, Damage,
  Self-Cast, NPC Name, Critical Shock, Vibration, Decline Tandem,
  Moving Mouse, Invert Camera | Guild Insignia, Refuse Trade,
  No Duel, No Party, Search denied, Jelly Effect, Camera Follow,
  Battle Recorder, Event Effect, PVP War Armor, Left-click to
  route, Recommended Equ…
- **Hotkeys** : liste 2 colonnes « Option » / « Hotkeys » +
  scrollbar : Community=O, Pet=N, Party Member 1-10=F1-F10, Move=W,
  Step Back=S, Move Left=A, Move Right=D, Turn Left=Q, Turn
  Right=E, Jump=Space, Sit/Stand=X, Toggle WPN=Z, Attack…
- **UI Settings** : message rouge « Avoid setting font size larger
  than UI » + « ? » ; sliders UI Size / Font Size / Chat Font Size ;
  dropdowns style « Noble Irongray Style », « Filter Effect » Off,
  « Game Font » « Default (Lat RU AR) ».
- Bas commun : « Default | Confirm | Cancel ».

### SystemPanel.png — menu Échap
Petite fenêtre « Main Menu » : 6 boutons empilés pleine largeur —
Settings, Reset UI, Ch., Logout, Quit, Continue.

### profile.png + profile-Title.png + Profile-Achievements*.png
« Character Info » ~⅔ d'écran : en-tête emblème couronne + nom +
LV 100 + icône classe + « Cleric LV 11 » + « Perks 2 » ; onglets
Character Stats | Title | Fame | Achievement.
- **Character Stats** : 4 blocs de stats empilés à gauche (Core
  Stats STR/AGI/INT/WIS/LCK/ACC/Move SPD+50%, Physical Offensive
  P-ATK 1027 rouge/ATK SPD/P-Healing/CRIT, Magical Offensive
  M-ATK 1757 rouge/Cast SPD/G-Healing/M-CRIT, Defensive DEF 1016
  vert/EVA/Parry/Block) chacun avec « ? » ; centre = rendu 3D du
  perso + colonne slots équipement losanges bleus ; sous : « Gear
  Upgrade » / « Gear Swap » ; grille équipement « Cloth Armor
  (Current Gear) » + lignes Light/Heavy Armor grisées ; bande
  verticale droite ~10 icônes colorées + compteur « 0 » (monnaies).
- **Title** : bandeau « No Title » ; arbre « List of Titles »
  (General replié, Honor déplié ~21 titres) ; « Ability
  description » + « Method of Acquisition » à droite ; losange
  « Displaying owned title » ; bouton « Set Title ».
- **Achievement** : « Total Achievement 0 », « Monster Conquest
  308848954 », « Town Achievement 0 » ; arbre catégories (Legendary,
  Monster Conquests par tranches Lv, Event Performance, Quest
  Performance, Class Achievements) ; liste à droite (icône + nom +
  « Incomplete » + « ? ») ; bouton « Progress » ; bas : champs
  « Victory Quote » + « Set Quote » / « Defeat Quote » +
  « Set Quote ». **Tooltip achievement** : titre blanc, desc
  rouge/rose, « Achievement Points: 4 » doré, « Achievement
  Title: … », bonus vert « Increase the Max HP +500 points. This
  title effect is active even if not equipped. »

### CommunityPanel.png + CommunityPanel-SearchPlayers.png
« Community » : onglets Friends | Search | Couple.
- **Friends** : tableau vide (Ch.|Name|Lv|Class|Location|Info) +
  « Block List » (liste Name + Edit Profile/Add/Block). Popup
  modal « Enter Character Name » + champ + Confirm|Close.
- **Search** : « Search Results: 0 players found » ; tableau vide
  + panneau « Filter » : groupe Lv (Min/Max Level), groupe Class
  (Defense, Melee DMG, Ranged DMG, Healing, Magic DMG, Special),
  groupe Others (losanges « Omit », « Omit anon. ») ; bouton
  « Search ».

### FindGuild.png — « Guild Recruitment »
Tableau vide 5 colonnes : Guild Name | Leader | Mbrs. | Lv | Guild
Town ; pied : « Find Town », pagination « < 1/1 > », « Apply to
Join » | « Close ».

### Mail.png — « Mail »
Onglets Inbox | Send Mail ; sous-options : losange « Mark All »,
losange « Delete emptied mails », « ? » ; liste mails (colonne
losanges + liste blanche + scrollbar) ; bas : Collect All |
Delete | Close.

### QuestLog.png — « Quest »
Onglets Quest | Legend. Gauche : « Quests: 0/25 » + « ? »,
losange « Track All », arbre (Unacquired Quests déplié → « 3-Player
DGN: Angor Quarry » → « L08. Angor Quarry I P… » ; Expedition
Trial déplié ~15 lignes L65./L70. avec losanges bleus) ; hint
« Shift+Left-click to track a quest ». Droite : en-tête losange +
« L87. Shaxia Fae Fields (Daily) » + tag « Party » jaune ;
description ; objectifs « Environment to take: Daily Quest » +
losange « Use Portal Stone », « Targets to take: <Soul Guardian>
Soul Guardian » + bouton « Go ». Bas : Share | Drop | Close.

### daily reward.png — « Event »
Onglets : Recommended Event | The Path of Destiny | **Login
Reward**. Grille : colonnes Day 1→7 + colonne « Bonus! », 4 lignes
(Days 1-7/8-14/15-21/22-28 + Bonus par ligne) ; chaque cellule =
carte « Day N » + icône cadeau (badge ×2/×3) + bouton « Claim »
(Day 3 : compte à rebours rouge « 09:55 »). Panneau droit
« Information » : règles numérotées 1-5 + « Note » + scrollbar.
Bas : « Claim all (2) » avec tooltip « Claim every reward that is
ready, all at once ».

### PetsPanel.png — « Pet »
3 colonnes : ① « Pet List » + « Number: 0/30 » jaune + « ? » +
liste vide + scrollbar ; bas : Seal | Unbind. ② « Pet Info » +
« ? » : viewport carré beige (rendu familier, blason doré si vide),
« Advanced », « Pet Gear » 4 slots losange ; bas : Rename | Pet
Lunch Box | icône | Feed. ③ champ nom (ornement floral) + lignes
stats (label + champ gris + case : ATK/M-ATK, ACC, CRIT/M-CRIT,
ATK SPD, Attack Attribute) ; « Battle » 5 slots + bouton ;
« Pray » 5 slots + bouton.

### ItemMallPanel.png + ItemMallPanel-LoyaltyShop.png — « Item Mall »
Onglets Item Mall | Loyalty Shop ; 3 colonnes :
① « Select a category » (New Item, Hot Item, Limited Item, VIP,
Beautiful Item, Enchantment, Convenience, Pets & Mounts, Others /
Featured, Crafting Items, Gear, Boosts, Fashion, Colorful Dye) +
grille 2×4 icônes + « Collect All » + champ recherche « Item ».
② « Catalog » : lignes (icône + nom + prix losange **bleu** +
« N left this week! » rouge + Buy | Gift | BuyStac) ; prix barrés
sur promos ; pagination « 1/5 ». ③ « Hot Items » : liste
icône+nom+prix+Buy. Bas droite : « Close ».
**Loyalty Shop** : prix en losange **doré** + tag « Buy Character
Level: NN » jaune ; pied : soldes « 10721 💠bleu » + « 0 💠or ».
Tooltip item : nom + description + « Note: binds item » + « Can't
be Traded » rouge.

### TransportationArray*.png — « Portal »
Dropdown « Central Continent » ; **grande carte aquarelle** avec
marqueurs-gouttes **bleus** (portails débloqués) + points blancs +
curseur vert (position) + routes pointillés jaunes ; grosses
flèches ◀ ▶ rose/rouges bas-gauche/droite ; « Close » bas-droite.
**Popup TP** : « You need to pay 1 Silver to [teleport to]
Limestone Mtn.. » + Confirm | Cancel ; tooltip marqueur :
« Rainbowfall Forest / 100 🪙 ».

### Channel Settings (shot_0078) + canal nommé (shot_0079)
Dialog « Channel Settings » : champ « Background » + 2 colonnes
de losanges (General Chnl, Peer Chnl, Party Chnl, Guild Chnl,
Region Chnl, World Chnl, Couple's Chnl, Battlefield Chnl, Sys
Message, Whisper Chnl | EXP/Coin/Fame/Item Acquired, DMG Dealt,
DMG Taken, Status Info) + Confirm | Cancel. Modale « Please enter
channel name: » + champ + Confirm | Cancel.

### Town List (shot_0062)
Volet « Town List » : losanges Farm✓ Mine✓ Altar✓, Alchemy Lab,
Foundry, Magic Pool ; champs Min/Max Level, NPC Names. Fenêtre
résultats : champs Leader, Fame, Member, Tax%, Town Type ;
tableau Name/Function/Time Remaining.

## V. États de jeu dynamiques (séquences shot_*)

- **shot_0030** : transition zone — titre jaune géant
  « Limestone Mtn. », mobs labels jaunes « RedCapMushroid »,
  « You have acquired <…> state » rouge, tutorial « How to move ».
- **shots 0034/0037** : frame cible + Target's Target, buffs sous
  portrait, « Entering Combat ».
- **shot_0036/0084** : combat — AoE doré au sol sous le perso,
  phénix noirs enflammés (effet de skill), dégâts blancs « 127 »/« 6 »,
  « Leaving Combat ».
- **shot_0040** : fenêtre en **fade-in** semi-transparent.
- **shot_0045** : tooltip buff « Invincible — This state will be
  invalid after attacking or casting skills. » sous la barre ULT.
- **shot_0056** : bandeau « Switched Class to Bard » + dragon blanc.
- **shot_0082** : bulle « bonjour » au-dessus de la tête + double
  ligne « <Say>[GM]EE…:bonjour » dans le chat.
- **shot_0086** : icône de sort appris qui se place en hotbar.
- **TailleMob.png** : échelle mobs — mushroid ≈ 0,7× joueur.
- **TimedCastSkill.png** : cast bar ornée (embouts dorés) +
  nom « Earth Shock » **au-dessus** de la barre, position ~60 %
  hauteur, cercle AoE doré animé au sol.
- **SkillAnimationTornado(cleric).png** : animation de sort
  tornade sur clerc (FX visuel à reproduire).
- **Fumée quand on se déplace.png** : trail de poussière/fumée
  pendant la course.
- **NouveauSkillApresLevelUp.png** : notification « nouveau sort »
  après level-up (popup à reproduire).
- shot_0057/0063/0033/0036/0044 : artefacts Okular (captures de
  captures) — le contenu interne vaut oracle, pas le rendu.

## VI. Inventaire fichier → statut mapping

Tous les fichiers `ref-shots/` sont couverts ci-dessus :
82 fichiers analysés — 5 écrans, ~10 éléments HUD, ~20 panels,
~45 frames de séquence. `capture.sh` = script de capture (non
image).
