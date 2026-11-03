# Catalogue des captures de référence — `ref-shots/`

Captures d'écran du client de référence (Eden Eternal / Reforged), prises en
jeu sur serveur live. Chaque entrée décrit ce que montre la capture et ce
qu'elle **spécifie** comme comportement attendu pour le client Godot
(layout, couleurs, états, interactions).

Chemins relatifs à la racine du dépôt : `ref-shots/<fichier>`.

> Note corpus : `ref-shots/capture.sh` est le script de capture (spectacle
> toutes les 5 s) — pas une image. Plusieurs `shot_*.png` sont des captures
> **méta** : ils montrent la visionneuse Okular affichant une capture
> précédente (utile quand même : le contenu du jeu reste lisible). Le fichier
> `skill hover info` est un PNG sans extension.

---

## 1. Écrans d'entrée : canal, personnage, chargement, téléportation

- `ref-shots/Selection_channel.png` — Fenêtre « Welcome to Eden Reforged! »
  sur fond de lobby (dôme du temple). Tableau Server | Ch. : à gauche
  « Ch01 (Recommended) | 0 chara » en rouge ; à droite Ch01, Ch02
  (Maintenance). Boutons Confirm / Cancel, « Official Website » et « Quit »
  en bas à droite, disclaimer « We are not affiliated with Aeria Games or
  X-Legend… ».
  **Spécifie :** sélection de canal = tableau 2 colonnes, états du canal
  colorés (Recommended rouge, Maintenance grisé), compte de personnages par
  canal.

- `ref-shots/Selection_personnage.png` — Lobby de sélection : personnage
  (Clerc robe crème) debout dans le hall du temple. En haut à gauche
  panneau « Preview » = grille 2×5 d'icônes violettes (emotes). En haut à
  droite carte personnage : portrait, niveaux « 100 | 13 », nom
  « EEi??ti??O?C?I?? », zone « Aven », en-tête « Ch01 (Ch01) ». Flèches
  prev/next + bouton « Start Game » en bas ; colonne droite : Set password /
  Remove password / Select Server / Logout.
  **Spécifie :** layout du char-select (preview emotes à gauche, carte
  perso à droite, actions compte en colonne droite).

- `ref-shots/shot_0011_235711.png` — Même écran de sélection de personnage
  que ci-dessus (doublon de `Selection_personnage.png`, autre instantané).

- `ref-shots/changement de map - changement d'étape de connexion.png` —
  Écran de chargement d'Aven : titre illustré « Aven — The Island
  Sanctuary », paragraphe de lore en bas à gauche, astuce en haut à droite
  (« Obtain Fame to purchase exclusive items at Fame Exchang… »), barre de
  progression en bas ornée (lune, alpaga, cristal).
  **Spécifie :** loading screen = artwork de zone + lore + tip + barre de
  progression stylisée, pas de HUD pendant le chargement.

- `ref-shots/TransportationArray (TP changement carte).png` — Fenêtre
  « Portal » : dropdown « Central Continent », carte du monde avec icônes
  de cristaux bleus aux destinations, grandes flèches prev/next en bas,
  bouton Close. Derrière : le personnage devant le monument-portail d'Aven.
  **Spécifie :** téléportation = carte continentale + icônes de
  destination + choix par dropdown continent.

- `ref-shots/TransportationArray (TP changement carte)2.png` — Même fenêtre
  Portal avec tooltip « Rainbowfall Forest 100 » (niveau requis affiché sur
  l'icône) et boîte de confirmation « You need to pay 1 Silver to teleport
  to Limestone Mtn.. » Confirm/Cancel.
  **Spécifie :** TP payant → confirmation modale avec coût en Silver ;
  chaque icône de destination affiche son niveau au survol.

---

## 2. HUD de base : vitals, hotbar, barres d'XP, minimap

- `ref-shots/juste le jeu.png` — Vue de jeu standard à Aven (fontaine,
  PNJ étiquetés « Soul Guardian », « Armor Merchant »…). HUD complet :
  vitals haut-gauche, minimap haut-droite « Aven X:…,Y:… », chat bas-gauche,
  hotbar bas-centre, 10 boutons de menu bas-droite, toast rouge
  « You have acquired <Hard Truth> state », bulle d'aide « How to move ».
  Nom du joueur au-dessus de la tête avec barre HP rose.
  **Spécifie :** référence générale du HUD in-game ; toasts système rouges
  centrés ; noms + HP flottent au-dessus des entités.

- `ref-shots/TopLeftVitals.png` — Gros plan vitals : portrait circulaire
  anime dans cadre argenté orné, rangée de petites icônes (Lv 100, losange,
  11), nom, barre HP rose « 14607/14690 » (segment rouge = manquant), barre
  MP bleue « 11400+/12262 ». En dessous deux lignes de cases numérotées
  1–6 et 7–12 (grille cible/membres) + icône cadeau.
  **Spécifie :** portrait circulaire orné, HP rose / MP bleue avec
  valeurs `actuel/max`, grille numérotée 1–12 sous le portrait.

- `ref-shots/HotbarPanel.png` — Gros plan hotbar : petites flèches
  triangulaires à gauche (verrou/extension), slot « 1 », puis 5 icônes de
  skills bleus remplies, slots 7-8-9-0 et « - » « + » vides avec numéros
  de raccourci affichés. Bord droit : boutons +/× et cadenas.
  **Spécifie :** hotbar = rangée de slots numérotés 1-0/-/+, flèches de
  navigation de pages aux extrémités, verrou.

- `ref-shots/DoubleEXPbar.png` — Deux barres fines au-dessus de la hotbar :
  icône EXP dorée + « 0.000 % » (barre XP personnage, sombre/vide) ;
  icône bleue + « 100.000 % » (barre CP de classe, violette pleine).
  Petites flèches aux extrémités.
  **Spécifie :** deux barres d'XP superposées : EXP perso (%) et CP classe
  (%) avec icônes distinctes et pourcentage à 3 décimales.

- `ref-shots/party UI.png` — Cadres de groupe : trois cartes joueur
  empilées. Chacune : portrait circulaire cerclé or, ligne d'icônes de
  classes avec niveaux « 85 80 », pseudo « KAC », HP rouge « 26449/26449 »,
  MP bleue « 11785/13928 ». Le chef (« KoopDuMonde ») porte une couronne.
  Icône cadeau à gauche de la première carte.
  **Spécifie :** party frames = portrait + classe(s)+lv + nom + HP/MP ;
  couronne pour le leader ; listés verticalement en haut à gauche.

- `ref-shots/shot_0045_000016.png` — Scène Limestone Mtn avec la barre de
  buffs (icônes « 20 », « 2 », dorée, clé) sous la barre ULTIMATE ;
  tooltip de buff « Invincible — This state will be invalid after
  attacking or casting skills. » Tracker de quêtes déplié à droite.
  **Spécifie :** buffs = icônes sous la barre ULTIMATE top-center avec
  tooltip descriptif au survol.

---

## 3. Chat : panneau, canaux, bulles

- `ref-shots/ChatPanel.png` — Gros plan panneau chat : onglets
  Normal | Battle + bouton Settings ; message « <Peer>You've joined the
  Lv1-100 Peer Channel. Enter /o before your message to post a message in
  the channel » en violet. Ligne de saisie : bouton Emoji + onglets de
  canal Say/Peer/Party/Guild/Zone/World/Buzz.
  **Spécifie :** chat = onglets Normal/Battle, sélecteur de canal sous la
  zone de saisie, couleurs par canal (Peer = violet).

- `ref-shots/ChatPanel-Peer.png` — HUD complet : le chat montre des
  messages système rouges (« Switched Class to Thief », « You have learned
  skill '…' », « Searching, please wait / Search Finished! ») et un
  message Peer bleu « <Peer>[GM]EEi??ti??O?C?I??:yo ». Barre de canaux
  visible : Emoji + Say/Peer/Party/Guild/Zone/World/Buzz + boutons
  C1 C2 C3 C4 C5 C6 C7 C8 C9 C0 C' C+.
  **Spécifie :** préfixe de canal entre `<…>` coloré, pseudo préfixé
  `[GM]`, canaux personnalisés C1–C0/C'/C+ à droite des canaux standards.

- `ref-shots/shot_0077_000311.png` — Même barre de canaux de chat
  (C1…C0/C'/C+) en situation réelle à Limestone Mtn.

- `ref-shots/shot_0078_000316.png` — Fenêtre « Channel Settings » :
  liste de cases losange pour les canaux affichés — Background, General
  Chnl, Peer Chnl, Party Chnl, Guild Chnl, Region Chnl, World Chnl,
  Couple's Chnl, Battlefield Chnl, Sys Message, Whisper Chnl (cochées),
  puis EXP Acquired, Coin Acquired, Fame Acquired, Item Acquired, DMG
  Dealt, DMG Taken, Status Info (décochées/grisées). Confirm/Cancel.
  **Spécifie :** filtrage du chat par type de message via cases losange,
  deux groupes (canaux puis événements).

- `ref-shots/shot_0079_000322.png` — Dialogue « Please enter channel
  name: » avec champ de saisie + Confirm/Cancel : création d'un canal
  personnalisé (boutons C1–C+).
  **Spécifie :** création de canal custom = modale de saisie de nom.

- `ref-shots/shot_0081_000333.png` / `ref-shots/shot_0082_000338.png` /
  `ref-shots/shot_0083_000344.png` — Séquence Say : saisie sur canal Say,
  bulle « bonjour » affichée au-dessus de la tête du personnage (shot_0082),
  disparition de la bulle (shot_0083). Chat : « <Say>[GM]…:bonjour ».
  **Spécifie :** parler en Say affiche une bulle de texte temporaire
  au-dessus du personnage + ligne colorée dans le chat.

---

## 4. Panneau Classes (K) : skills, talents, certificats, changement

- `ref-shots/ClassePanel.png` — Fenêtre Class, onglet « Class Skills ».
  Colonne gauche : liste de classes (icône + nom + Lv, cadenas si
  verrouillée, « Current Clas » sur Clerc Lv.14, bouton Activate).
  Centre : « Reg. Skills » (or disponible « 550 Gold 9 Silver ») en deux
  rangées de skills Lv.1 avec boutons « +1 » et « Max » ; « Class Skills »
  (« 4331 remaining CP ») : Light's Sanction Lv.9, Life Cure Lv.11, Hard
  Truth Lv.11, Purifying Wind. Tooltip skill « Life Cure » : Cost 38 MP,
  Cooldown 30 s, effet, « Next level: … », « Learning the skill's next
  level requires 153 Class Points! ».
  **Spécifie :** montée de skills = +1/Max par skill, coût en Class Points
  rouge dans le tooltip, double section Reg./Class skills.

- `ref-shots/HoverToolTipClasse.png` — Même panneau avec tooltip de
  classe « Shaman » (description + prérequis rouges) et bouton
  « Transform! » au survol de la classe courante.
  **Spécifie :** hover classe courante → bouton Transform!; prérequis
  listés en rouge.

- `ref-shots/shot_0048_000033.png` — Même panneau (onglet Class Skills)
  avec tooltip de classe « Shaman » : « Shamans pray to ancient spirits… »
  + « Class Requirement: * Character Level 45 / * Cleric Level 30 » en
  rouge. Liste de classes à gauche (cadenas sur classes non débloquées,
  « Current Clas » sur Clerc Lv.14).
  **Spécifie :** tooltip de classe dans la liste = description + prérequis
  rouges ; classes verrouillées affichent un cadenas.

- `ref-shots/ClassePanel-ClassTalent&Certificat.png` — Onglet « License
  Knowledge » : arbre de talents en grille (icônes actives + flèches +
  icônes verrouillées grisées), tooltip « Religious Knowledge — Max Level
  20, For each level up, WIS +1 % » avec coût rouge « Learning the next
  Knowledge level will cost you 1 Knowledge Points! ». Panneau droit
  « Class Talent » : « 7 KPs Remain » / « 1 KPs Entered », Confirm /
  Refresh. Panneau bas « Certificates: 9 » : grille 4×3 de slots
  fleur-de-lis, icônes « + » dorées, liste à droite, Confirm.
  **Spécifie :** talents = arbre à points KPs (remain/entered) validé par
  Confirm ; certificats = grille de slots dédiée.

- `ref-shots/shot_0050_000043.png` / `shot_0051_000049.png` /
  `shot_0052_000054.png` — Variantes du même onglet License Knowledge
  (arbre de talents, certificats placés dans la grille fleur-de-lis).

- `ref-shots/case pour certificat.png` — Gros plan de la grille de
  certificats : slots fleur-de-lis sombres, quelques cases avec icône
  « + » orange (slot de placement) et icônes de certificats
  vert/violet/bleu/doré.
  **Spécifie :** slots certificat = fond fleur-de-lis ; ajout via case « + ».

- `ref-shots/ClassePanel-ClassNews.png` — Onglet « Class News » : liste de
  classes à gauche, au centre icône de classe + description (« Clerics call
  on holy power to heal wounds… »), icônes de caractéristiques, « Class
  Stat Bonus » (HP 103 %, MP 109 %, STR 95 %, AGI 95 %, INT 113 %,
  WIS 135 %, LCK 105 %), « Weekly Performance » (Territory War Kill 0,
  Arena Victories C 0, Monster Conqueror 0), aperçu du personnage en robe
  blanc/bleu, bouton « Set Outfit », colonne droite de boutons numérotés
  1–9.
  **Spécifie :** onglet classe = fiche descriptive + bonus de stats en %
  + performances hebdo + preview perso.

- `ref-shots/ClassePanel-ClassNews2.png` — Même onglet, aperçu du
  personnage en robe orange (autre tenue/classe sélectionnée), tooltip
  skill « Hammer of Discipline » (Cost 17 MP, Cast 1.5 s, AoE Holy,
  « Required weapon: Mace/Guitar/Staff/Grimoire », « Must reach class
  level 50 »).
  **Spécifie :** le preview change selon la classe ; tooltip skill inclut
  coût, cast, effet, arme requise, niveau requis.

- `ref-shots/ClassePanel-ClassNews3.png` — Même onglet avec un autre
  aperçu de tenue (variante).

- `ref-shots/ClassPanel-ChangementDeClasse.png` — Toast central
  « Switched Class to Thief » avec animation de deux oiseaux/phénix sombres
  traversant l'écran ; le panneau bascule sur Thief Lv.1 (skills Slam,
  Ambush…). Chat : « <System>Switched Class to Thief » + « You have
  learned skill '…' ».
  **Spécifie :** changement de classe = bannière + animation d'oiseaux +
  messages système de skills appris.

- `ref-shots/shot_0056_000116.png` — Même animation « Switched Class to
  Bard » (toast + oiseaux), panneau Class Skills Bard Lv.1, messages
  « You have learned skill '…' » dans le chat.

- `ref-shots/NouveauSkillApresLevelUp.png` — Annonce plein écran
  « You have learned skill Ambush. » (grand texte blanc central) + oiseaux
  sombres ; suite de messages « learned skill » dans le chat.
  **Spécifie :** apprentissage de skill = annonce centrale blanche +
  animation d'oiseaux.

- `ref-shots/ClassAbilitybonuslogo.png` — Tooltip « Class Ability Bonus » :
  « Total: HP 10108 points, MP 3309 points, ATK 7133 points, Magic 9927
  points, G-Healing 407 points » au survol de l'en-tête du panneau.
  **Spécifie :** bonus cumulé de classe affiché en tooltip (5 stats).

- `ref-shots/skill hover info` *(PNG sans extension)* — Tooltip de skill
  « Encourage — Cost: 382 MP, Cooldown: 60 seconds, For 30 seconds,
  increases Double Hit Rate +15 % and STR +244 ».
  **Spécifie :** format tooltip skill : nom, coût MP, cooldown, effet
  chiffré, durée.

---

## 5. Tooltips d'objets (hover inventaire / shop)

- `ref-shots/Blueprint.png` — Tooltip violet « Blueprint: Head Gem: Saint
  Blessing » (titre orange). Required Materials : Blueprint ×1, Etherworld
  Catalyzer ×3, Demon Crystal ×75, INT Magic Elimination Gem Lv5 ×1.
  Finished Item : Head Gem: Saint Blessing (icône). Texte d'usage rouge
  « To inlay the item, right-click on it, and select a piece of equipment
  with sockets. Can only be used on Lv75+ items. » Pied : « Gem / Head ».
  **Spécifie :** tooltip plan = fond violet, matériaux ×N, produit fini,
  consigne d'usage rouge, catégorie/slot en pied.

- `ref-shots/Hover Item Plans ressources.png` — Tooltip violet « Formula:
  Leg Enchant - Gale Lv7 » : Work Level 12, matériaux (Enchant - Slightness
  LV12 ×1, Red Spar Ore ×124, Flower Ink ×12), Finished Item, consigne
  rouge d'enchantement (« hold down the right mouse button… Can only be
  used on Leg Gear that is Lv 88 and above »), stats vertes (EVA +22,
  Move SPD +5 %), « Level Requirement 88 », note jaune « You can produce
  this item in the Magic Pools in towns. », pied « Magic Pool Material /
  Can't be Traded ».
  **Spécifie :** code couleur tooltip : stats vertes, restrictions rouges,
  astuces jaunes, flags « Can't be Traded » en pied.

- `ref-shots/item hover (basic ressources on classic mob on map).png` —
  Inventaire (grille d'icônes) avec tooltip « Ancient Stone Elephant —
  Seems to be quite an ancient item, someone might be willing to pay a
  high price to acquire it. » Pied : « Item / Can't be Traded ».
  **Spécifie :** loot basique = tooltip descriptif court + flags en pied.

- `ref-shots/Item hover consommable avec durability.png` — Tooltip noir
  « Light Beam Gun » : flavour jaune, « Loses 1 Durability point per use »
  rouge, sections « Item / Already Bound / Durability: 3/20 », effet
  « In time, the body is enlarged to 200 % », « Cooldown: 1 min. 30 sec. »
  **Spécifie :** consommable = durabilité `x/y`, cooldown, flavour text
  jaune, contraintes rouges.

- `ref-shots/item hover pet (legendary).png` — Tooltip noir « Forgotten
  Ghostblade » (titre doré = légendaire) : « Pet Level: Lv1 0 % », « Pet /
  Already Bound », notes de type « Type: Slash A / ATK B / ACC B+ /
  CRIT C+ / SPD D+ », puis 3 skills de combat décrits (Spirit Scar,
  Ambush, Slam) et aptitudes passives maître (« High Speed Star — Owner's
  Move SPD +5 % », Wealth/Greed « Owner's Loot Rate +10 %/+25 % »).
  **Spécifie :** tooltip pet = notes par lettre (S/A/B/C/D) par stat,
  skills avec effets, bonus « Owner's ».

- `ref-shots/item hover (plan mine ressources).png` — Fenêtre marchand
  (RedLightInlayMerchant) : articles « Lv85/Lv90 Ore Location Map » avec
  prix or+argent ; tooltip « Lv90 Ore Location Map » : Work Level 12,
  « Minerals that might be extracted: Crimson Diamond ×1, Bluefire
  Crystal ×1, Deepfathom Shell ×1, Lavender Crystal ×1 », note jaune
  « Minerals can be extracted from the town's mine », pied « Mining
  Material / Can't be Traded / Level Requirement 90 ».
  **Spécifie :** shop = liste articles + prix or/argent ; tooltip produit
  = loot possible listé.

- `ref-shots/item hover (pray book) for pet pray skill.png` — Tooltip noir
  « Prayer Book III - Fast Learner » (titre bleu) : « This book will
  replace the pet's third pray slot. » rouge, pied « Item ».
  **Spécifie :** livre de prière pet = slot remplacé indiqué en rouge.

- `ref-shots/item hover (trophée).png` — Tooltip noir « Holy - Neondo's
  Imperial Contract » (titre jaune) : flavour italique, sections
  « Accessory / Trophy Exclusive / Can't be Traded / Already Bound »,
  stats vertes (Move SPD +5 %, Slash Resistance +12, « When attacking,
  5 % chance to apply a debuff… DEF -10 %, All Resistances -10 »),
  « Level Requirement 90 ».
  **Spécifie :** trophée = slot exclusif, flags empilés en une ligne,
  procs en vert.

- `ref-shots/item hover (trophy enchant).png` — Tooltip violet « Trophy
  Enchant - Guardian Lv13 » : consigne rouge d'enchantement (trophys
  Lv 90+), stats vertes (DEF +1365, HP +463, Block +93, résistances +9,
  All Elemental Resistances +3, Malice +10 %) et malus rouges (ACC -8 %,
  G-Healing -83).
  **Spécifie :** enchant trophée = bonus verts ET malus rouges dans le
  même bloc de stats.

- `ref-shots/item hover (weapon).png` — Tooltip noir « Holy - Blazing
  Jet » : type « Artillery Gun / ATK SPD 2.0 Seconds / Can't be Traded /
  Durability: 105/105 », « Basic P-ATK 24571 (120 %) / Basic M-ATK 12285
  (120 %) », stats vertes (AGI +83, INT +111, LCK +194, M-CRIT Rate +73,
  M-CRIT DMG +18), proc « When attacking, 5 % chance to gain an effect for
  10 seconds — Effect: INT +198, AGI +198, M-ATK +3756, ACC +198 »,
  « Level Requirement 90 ».
  **Spécifie :** arme = ATK SPD, durabilité, stats de base avec % qualité,
  proc détaillé sur deux lignes.

- `ref-shots/item hover (weapon 2).png` — Grand tooltip violet « Heritage
  Spear Lv60+12 » (titre bleu) : notes bleues (« Heritage Weapon: Improves
  as your character improves… ») puis rouges (stats selon niveau porteur,
  fortification basique seulement, pas de Gear Upgrade, « Requires Level
  31 or higher »), bloc blanc « Artillery Gun / 2-Handed / ATK SPD… /
  Durability: 102/105 / Basic P-ATK 3209 (100 %) », stats vertes (P-ATK
  +5615, STR +69…), « Level Requirement 31 », ligne cyan « Fortification
  Level: +12 » + bonus verts de fortification.
  **Spécifie :** équipement heritage = sections colorées bleu/rouge/blanc
  empilées ; fortification affichée en cyan + liste de bonus.

---

## 6. Hôtel des ventes (Auction House)

- `ref-shots/Auction (hotel de vente) line item on sell time.png` — Une
  ligne d'annonce : icône d'objet (stack « 999 »), nom « Crimson
  Sequoia », quantité « 60 », prix « 1500 » or et « 996 » argent avec
  icônes de pièces.
  **Spécifie :** ligne enchère = icône+stack, nom, qté, prix or/argent
  séparés avec icônes.

- `ref-shots/Auction (hotel de vente) multiple line item on sell time and colonne name.png` — Liste d'annonces avec en-têtes de colonnes
  « Style | Item Name/Quality | Lv | Selling Price » ; lignes « Demon Ark »
  (objet violet) aux prix 25000/4, 24980/4, 23000/4.
  **Spécifie :** colonnes triables nommées, qualité visible via couleur du
  nom, prix triés.

---

## 7. Courrier (Mail)

- `ref-shots/Mail.png` — Fenêtre Mail : onglets Inbox | Send Mail, cases
  « Mark All » et « Delete emptied mails », liste vide avec cases losange
  par ligne, boutons Collect All / Delete / Close.
  **Spécifie :** inbox = liste à cases + actions groupées.

- `ref-shots/Mail2.png` — Onglet Send Mail/liste : mails de DrDott
  (« Demon Ark », 24 hours), Astria (« Crystal Root », 3 days), et
  plusieurs « Official: Auction » jaunes (365 days, « Auction successful:
  1H We… », « Auction received: Demon Ark »). Panneau droit : From /
  Subject / corps / objet joint (icône) / « Cash on Del… », montants
  20000 or, boutons Collect / Reply / Return / Close.
  **Spécifie :** mails système jaunes, durée restante affichée, lecture =
  panneau droit avec pièce jointe à collecter.

---

## 8. Social : communauté, guilde, groupe, villes

- `ref-shots/CommunityPanel.png` — Fenêtre Community, onglet Friends :
  tableau Ch.|Name|Lv|Class|Location|Info ; boîte « Enter Character Name »
  (champ « test1 », Confirm/Close) ; section Block List avec Edit Profile /
  Add / Block. Onglets Friends/Search/Couple en haut.
  **Spécifie :** ajout d'ami = modale de saisie de nom ; trois onglets.

- `ref-shots/CommunityPanel-SearchPlayers.png` — Onglet Search :
  « Search Results: 0 players found », colonnes Name/Lv/Class/Location/
  Info ; filtres Lv Min/Max, classes (Defense, Melee DMG, Ranged DMG,
  Healing, Magic DMG, Special), Others (Omit, Omit anon.), bouton Search.
  Chat : « Searching, please wait » / « Search Finished! ».
  **Spécifie :** recherche joueurs = filtres par niveau + rôle de classe
  + feedback dans le chat.

- `ref-shots/FindGuild.png` — Fenêtre Guild Recruitment : colonnes Guild
  Name/Leader/Mbrs./Lv/Guild Town, liste vide, bouton Find Town,
  pagination « 1/1 », Apply to Join, Close. Derrière : bulle « How to
  move » et champ de champignons.
  **Spécifie :** recrutement guilde = tableau + postuler + chercher une
  ville.

- `ref-shots/shot_0062_000149.png` — Fenêtre « Town List » : filtres à
  gauche (checkbox Farm/Mine/Altar/Alchemy Lab/Foundry/Magic Pool, Min/Max
  Level, NPC Names, Town Name, Search), colonnes droites Leader/Fame/
  Member/Tax %, Town Type, liste résultats vide. Chat : « Searching,
  please wait » / « Search Finished! ».
  **Spécifie :** recherche de ville de guilde par bâtiments/niveau/nom.

- `ref-shots/shot_0063_000154.png` — Capture méta (Okular affichant
  shot_0062) : même fenêtre Town List.

---

## 9. Profil : stats, titres, succès

- `ref-shots/profile.png` — Character Info, onglet « Character Stats » :
  Core Stats (STR/AGI/INT/WIS/LCK/ACC, Move SPD 50/50 %), Physical
  Offensive (P-ATK, ATK SPD %, P-Healing, CRIT Rate, CRIT DMG), Magical
  Offensive (M-ATK, Cast SPD, G-Healing, M-CRIT), Defensive (DEF, EVA %,
  Parry %, Block %) — valeurs de base/diminishing « x / y % » ; rendu 3D du
  personnage au centre, grille de slots d'équipement à droite, boutons
  Gear Upgrade/Gear Swap ; panneau bas « Cloth Armor (Current Gear) » +
  onglets Light/Heavy Armor ; colonne droite d'icônes colorées à 0.
  **Spécifie :** fiche perso = 4 blocs de stats avec valeurs réelles et %
  effectifs, sets d'armure en bas.

- `ref-shots/profile-Title.png` — Onglet « Title » : bannière « No Title »,
  arborescence des titres (No Title/General Title/Honor Title → dizaines
  de titres : Simmel Crusader, Dagger Master…), panneau droit Ability
  (« Two-handed strike weapon P-ATK & M-ATK +3 % ») + description +
  Method of Acquisition, case « Displaying owned title », bouton Set
  Title.
  **Spécifie :** titres = liste arborescente + effet + méthode
  d'obtention ; titre équipé optionnel.

- `ref-shots/Profile-Achievements.png` / `Profile-Achievements2.png` —
  Onglet « Achievement » : compteurs Total/Monster Conquest/Town
  Achievement, arbre de catégories (Legendary Achievement, Monster
  Conquests par tranche de niveau, Guild Bosses, Rare Monsters, Event,
  Quest, Class Achievements par rôle) ; liste droite « Legendary
  Achievement 0.00 % Complete » avec lignes « ? » (First Find, Deep
  Miner…, Incomplete). Tooltip succès : « You who got so many rare
  treasures… / Achievement Points: 4 / Achievement Title: Secret Treasure
  Hunter / Increase the Max HP +500 points / This title effect is active
  even if not equipped. » Bas : Victory Quote / Defeat Quote + Set Quote.
  **Spécifie :** succès donnent points + titre + bonus passif actif même
  non équipé ; quotes de victoire/défaite configurables.

---

## 10. Quêtes : journal, tracker, dialogue

- `ref-shots/QuestLog.png` — Fenêtre Quest (onglets Quest/Legend) :
  « Quests: 0/25 », case « Track All », arbre gauche (Unacquired Quests →
  « 3-Player DGN: Angor Quarry », « Expedition Trial » avec lignes
  L65/L70…), panneau droit haut « L87. Shaxia Fae Fields (Daily) Party »,
  panneau droit bas « Environment to take: Daily Quest / Use Portal Stone
  / Targets to take: <Soul Guardian> Soul Guardian [Go] », astuce
  « Shift+Left-click to track a quest », boutons Share/Drop/Close.
  **Spécifie :** journal = arbre par catégorie + détail quête (cible avec
  lien [Go]) + partage/abandon ; limite 25 quêtes.

- `ref-shots/QuestTracker.png` — Tracker droit déplié sur HUD : liste de
  quêtes « L08. Angor Quarry I », « L65. Mutant Beast Expedition
  (Daily) »… avec PNJ cible sous chaque titre ; boutons « Track Quests » /
  « Unacquired Quests ». Boîte « How to move » visible.
  **Spécifie :** tracker = titres de quêtes colorés par niveau + cible
  PNJ, repliable.

- `ref-shots/QuestTracker .png` *(nom avec espace avant .png)* — Gros plan
  tracker : titre rouge « 85. Awakening Mecha Ares I » + note jaune
  « Right-click the quest book for additional information » + objectifs
  avec losanges bleus (récoltes « 1/1 », « 10/10 ») et violets (kills
  « 0/1 »).
  **Spécifie :** objectifs = losange + texte + compteur x/y ; couleur du
  losange par type d'objectif.

- `ref-shots/quest UI.png` — Dialogue de quête PNJ « L85. Awakening
  Dawnbreaker II » : texte de lore entre guillemets, « Objective details »
  à puces, « Required items: », « Objectives: » (Talk to Gromkar, Collect
  Rusty Gear 1/1, Slate Statue 0/10, Defeat Gromkar 0/1, Ascendancy Token
  0/6, Templar Ascendancy - Dawnbreaker 1/1), boutons Confirm/Close.
  En haut : cadre cible « 81 ★ Archangel of Corruption ».
  **Spécifie :** fenêtre de quête = lore + liste d'objectifs comptés +
  items requis ; acceptation via Confirm.

---

## 11. Paramètres et menu système

- `ref-shots/ParameterPanel-GraphicsChoice.png` — Settings, onglet
  Display : preset Optimize (Low/Mid/High), Window Mode « 1280x720 »,
  Anti-aliasing Off/2x/4x, sliders Char Draw/Field/Max FX/Shadows,
  Models Low/High, Textures Low/Mid/High, Glow FX, Max FPS, Background,
  Outline Off/Character/All, Groundcover, cases Special FX (Death, Blur,
  Water Wave, 3D Effect, Photo, Glare, Shadows). Boutons
  Default/Confirm/Cancel.
  **Spécifie :** options graphiques = presets + toggles radio + sliders +
  cases losange ; boutons système en bas.

- `ref-shots/ParameterPanel-Sound.png` — Onglet Audio : case « Mute » +
  sliders « Music » et « SFX ». Minimal.
  **Spécifie :** audio = mute + 2 volumes.

- `ref-shots/ParameterPanel-Preferences.png` — Onglet préférences
  (chat/options) : slider « Mouse », deux colonnes de cases losange
  (Name/Guild/Title/Vitals/Health Bar/Damage/Self-Cast/NPC Name/Critical
  Shock/Vibration/Decline Tandem/Moving Mouse/Invert Camera ; Guild
  Insignia/Refuse Trade/No Duel/No Party/Search denied/Jelly Effect/
  Camera Follow/Battle Recorder/Event Effect/PVP War Armor/Left-click to
  route/Recommended Equ…).
  **Spécifie :** préférences = longue liste de toggles losange en 2
  colonnes (affichage de noms, refus d'interactions, caméra).

- `ref-shots/ParameterPanel-Hotkeys.png` — Onglet Hotkeys : table
  Option|Hotkeys scrollable (Community O, Pet N, Party Member 1-10 =
  F1-F10, Move W, Step Back S, Move Left A, Move Right D, Turn Left Q,
  Turn Right E, Jump Space, Sit/Stand X, Toggle WPN Z, Attack…).
  **Spécifie :** binds = liste Option→touche scrollable, F1-F10 pour
  cibler les membres du groupe.

- `ref-shots/ParameterPanel-UISettings.png` — Onglet UI Settings : note
  rouge « Avoid setting font size larger than UI », sliders UI Size /
  Font Size / Chat Font Size, dropdowns « Noble Irongray Style » (thème),
  « Off » (Filter Effect), « Default (Lat RU AR) » (Game Font).
  **Spécifie :** UI scale + polices via sliders ; choix du thème d'UI par
  dropdown (correspond aux atlas `themes/<Couleur>/`).

- `ref-shots/SystemPanel.png` — « Main Menu » modal centré : Settings /
  Reset UI / Ch. (changer de canal) / Logout / Quit / Continue.
  **Spécifie :** menu système = boutons empilés, retour canal/personnage
  sans quitter.

---

## 12. Boutique, craft, pets, sac, carte, événements

- `ref-shots/ItemMallPanel.png` — Item Mall onglet « Limited Item » :
  vignettes avec prix + badge rouge « N left this week! », prix barré
  (promo 6→4, 9→4), boutons Buy/Gift/BuyStack par item ; colonne « Hot
  Items » à droite ; solde « 10721 » diamant + « 0 » gemme en bas ;
  tooltip « Viridian Safety Stone (NT) » (protection de fortification
  +3~+6, lie l'objet ; « Item / Can't be Traded »). Pagination « 1/5 ».
  **Spécifie :** boutique = grille catalogue + stock hebdo limité + prix
  barrés promo + double monnaie premium ; NT = No Trade.

- `ref-shots/ItemMallPanel-LoyaltyShop.png` — Item Mall (onglets
  « Item Mall | Loyalty Shop »), catégorie catalogue (Limited Item,
  Featured, Crafting Items, Pets & Mounts, Gear, Boosts, Fashion,
  Colorful Dye) : Eternal Power Crystal 120, Life Leaf (NT) 490→290,
  Eternal EXP/CP Talisman I/II avec « Character Level » requis ; colonne
  Hot Items ; bouton « Collect All ».
  **Spécifie :** onglets Item Mall/Loyalty Shop, catégories en liste
  gauche, niveau requis sous le prix.

- `ref-shots/shot_0066_000211.png` — Item Mall, catégorie sacs/packs :
  Gear Upgrade Reset Scroll 29, 5-Slot Pack 49, 20-Slot Pack 346,
  37/15/20-Slot Backpack Expansion 1 ; colonne Hot Items (dont objet au
  nom chinois «  » 1) ; solde 10721 diamants.
  **Spécifie :** variante de catégorie boutique (extensions de sac).

- `ref-shots/shot_0067_000216.png` / `shot_0070_000233.png` — Captures
  méta (Okular) montrant respectivement shot_0066 (l'Item Mall packs
  ci-dessus) et la fenêtre Pet — doublons utiles des panneaux
  correspondants.

- `ref-shots/FusionPanel Craft (et guild npc crafter magic pool, forger...).png` — Fenêtre craft « Magic Pool » : « Available for Class
  LV 11 (and below) commission », slot Use Item, « Time 0 min. 10 sec. »,
  « Cost 1080 gold 0 silver », slot Charm, « Materials Required »
  (Blueprint ×1/1, Sanguine Fragment 71/40, Infinity Core 400/50, Demonic
  Catalyst 0/3 — compteurs colorés selon dispo), « Success Rate », produit
  « Sanguine Fortunate Safety Stone ×1 », boutons Request/Close.
  **Spécifie :** craft Magic Pool = matériaux `possédé/requis` colorés,
  coût or, durée, taux de réussite, restriction de niveau de classe.

- `ref-shots/PetsPanel.png` — Fenêtre Pet : panneau « Pet List »
  (« Number: 0/30 », Seal/Unbind) + panneau « Pet Info » (emblème, stats
  ATK/M-ATK, ACC, CRIT/M-CRIT, ATK SPD, Attack Attribute, bouton
  Advanced) ; « Pet Gear » 4 slots torche ; « Battle » 5 slots
  fleur-de-lis + bouton Battle ; « Pray » 5 slots + bouton Pray ;
  Rename / Pet Lunch Box (Feed).
  **Spécifie :** pet = liste + fiche stats + équipement + skills Battle/
  Pray en slots dédiés, limite 30.

- `ref-shots/shot_0017_235743.png` / `shot_0018_235749.png` /
  `shot_0019_235754.png` — Fenêtre « Backpack » : onglets
  All/Potions/Gear/Town/Special, grande grille de slots fleur-de-lis
  (~200), objets empilés avec compteurs (×2…), « 550 » or + icône gemme
  « 6 » en bas, bouton « Archive All » + rangée de 8 icônes + « Archive
  Save ».
  **Spécifie :** sac = onglets par catégorie, grille fleur-de-lis,
  monnaies en pied, archivage groupé.

- `ref-shots/carte.png` / `shot_0022_235811.png` — « Region Map » d'Aven
  plein écran : carte avec quartiers étiquetés (Industrial Ward,
  Courthouse, Market Zone, Trading District, Temple Knight, City Hall,
  Pier) ; panneau « Search Listing » à droite (marchands/PNJ quêtes :
  Arena Battle Merchant, Library, Alpaca Shop, Couple, Crystal Cross
  Merchant, Class Awakening Quest, Mystic Dungeon, Alpaca King, Merchant,
  Territory War Merchant, Gather/Craft, Quest, Guild Town, Event, Auction
  House, Nine Prestiges, Warp Portal, Arena, Daily Challenges, Trial,
  Mutated Creature, 2025 Reforged Halloween, Temple Knight, FWR Center) ;
  légende « City Features » (General/Weapon/Armor Merchant, Fame Shop,
  Auction, Revival Point, Warp Portal, Required Supplies, Portal, New
  Quest, Quest NPC) ; boutons World Map/Mainland/Regional, « Coord
  X:509 Y:296 », Icon Setting.
  **Spécifie :** carte de région = fond illustré + recherche de PNJ par
  catégorie + légende d'icônes + coordonnées curseur + 3 niveaux de zoom.

- `ref-shots/daily reward.png` — Fenêtre Event (onglets Recommended
  Event / The Path of… / Login Reward) : grille Day 1–28 d'objets avec
  bouton « Claim » par jour + colonne Bonus par semaine ; bloc
  « Information » à 5 règles numérotées + Note ; bouton « Claim all (2) »
  avec hint « Claim every reward that is ready, all at once ».
  **Spécifie :** login reward = calendrier 28 jours + claim individuel/
  global + bonus hebdo.

---

## 13. Monde, caméra, combat, feedback

- `ref-shots/mouvement caméra.png` — Aven place du marché, PNJ « CC
  Auctions » (Asst.Purr, Asst.Kay) avec icônes de coffre rouge au-dessus ;
  caméra à hauteur de personnage.
  **Spécifie :** icônes de service flottent au-dessus des PNJ ; caméra
  3ᵉ personne standard.

- `ref-shots/Fumée quand on se déplace.png` — Le personnage court à Aven
  en laissant une traînée de poussière/fumée blanche.
  **Spécifie :** déplacement rapide = traînée de particules de poussière.

- `ref-shots/TailleMob.png` — Champ de RedCapMushroid de **tailles
  variées** (petits et grands champignons).
  **Spécifie :** échelle aléatoire des mobs d'une même espèce.

- `ref-shots/targetframe (Elite mob).png` — Cadre cible : portrait
  circulaire orné (icône cible), « 81 ★★ Archangel of Sin » (2 étoiles
  dorées = élite), barres HP rose + MP bleue, icône de buff bleue à
  droite. En monde : nom rouge « <LegionElite> Archangel of Sin » flottant.
  **Spécifie :** cadre cible = niveau + étoiles de rang (★★ élite) +
  HP/MP + buffs ; étiquette monde `<Type> Nom` en rouge.

- `ref-shots/TimedCastSkill.png` — Barre de cast bas-centre « Earth
  Shock » (jauge rose) ; « ! » bleu au-dessus du lanceur ; glyphe runique
  vert sous le personnage ; « Leaving Combat » blanc centré ; toast rouge
  « You have acquired <Purifying Wind> state ».
  **Spécifie :** cast = barre nommée + glyphe au sol + indicateur « ! » ;
  entrées/sorties de combat annoncées au centre.

- `ref-shots/SkillAnimationTornado(cleric).png` — VFX tornade verte/
  jaune plein écran (skill Clerc) sur champ de champignons ; cadre cible
  « Red Cap Mushroid » ★ ; hotbar avec overlays de cooldown numérotés.
  **Spécifie :** sorts AoE = VFX envahissant la zone visée ; cooldown =
  balayage sombre + compteur sur le slot.

- `ref-shots/shot_0030_235854.png` — Limestone Mtn : grand titre de zone
  jaune « Limestone Mtn. » au centre à l'entrée ; barre « ULTIMATE Lv 1
  EXP 200/400 Perks 2 » top-center ; étiquettes jaunes de mobs.
  **Spécifie :** entrée de zone = titre jaune centré ; barre ULTIMATE
  (Lv/EXP/Perks) visible en permanence.

- `ref-shots/shot_0033_235911.png` / `shot_0036_235927.png` /
  `shot_0044_000011.png` — Captures **méta** : fenêtre Okular affichant
  des shots précédents (explosion dorée sur champignons, « Leaving
  Combat » + glyphe + cast Earth Shock, champ de champignons + « How to
  move »). Le contenu jeu reste lisible dans la vignette Okular.

- `ref-shots/shot_0034_235916.png` / `shot_0037_235932.png` /
  `shot_0038_235938.png` / `shot_0039_235943.png` — Limestone Mtn :
  champignons rouges étiquetés, cadre cible « Red Cap Mushroid », toasts
  « You have acquired <Purifying Wind> state », nom GM au-dessus du joueur.

- `ref-shots/shot_0040_235949.png` — Fenêtres de quête superposées
  semi-transparentes (état de transition/ghosting) ; chat « <System>Window »
  ×3 rouge (événements d'ouverture de fenêtre).
  **Spécifie :** ouverture/fermeture de panneaux = transition
  semi-transparente ; message système « Window ».

- `ref-shots/shot_0041_235954.png` — Fenêtre Quest ouverte + tracker
  déplié (dailies L65-L86 avec PNJ) + « <System>Window » dans le chat.

- `ref-shots/shot_0057_000122.png` — Capture méta (Okular) de
  shot_0056 (changement de classe Bard).

- `ref-shots/shot_0084_000349.png` — « Leaving Combat » + « You have
  learned skill Sneak. » + oiseaux sombres + lueur dorée + chiffre de soin
  « 127 » flottant.
  **Spécifie :** nombres de soins/dégâts flottent au-dessus des entités.

- `ref-shots/shot_0085_000355.png` — « Entering Combat » rouge ; cadre
  cible « Red Cap Mushroid » + sous-cadre « Target's Target » (nom du
  joueur ciblé par le mob, HP/MP).
  **Spécifie :** Target-of-Target = petit cadre accolé au cadre cible.

- `ref-shots/shot_0086_000400.png` — « Entering Combat » + « You have
  learned skill Swift Attack. » + oiseaux + dégât « 6 » ; cadre cible
  avec HP bas + Target's Target.

---

## Récapitulatif

| Thème | Nb captures |
|---|---|
| Entrée (login/canal/perso/chargement/TP) | 6 |
| HUD de base | 6 |
| Chat | 8 |
| Panneau Classes | 16 |
| Tooltips objets | 11 |
| Hôtel des ventes | 2 |
| Mail | 2 |
| Social/guilde | 5 |
| Profil/succès/titres | 4 |
| Quêtes | 4 |
| Paramètres/système | 6 |
| Boutique/craft/pets/sac/carte/events | 13 |
| Monde/combat/feedback | 20 |
| **Total images cataloguées** | **103** |
