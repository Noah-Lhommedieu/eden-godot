# Spec visuelle extraite de ref-shots/ (client Reforged, référence cible)

Captures 1410×878 (fenêtré). Sources : `ref-shots/*.png` (captures du vrai client).

## Écrans de connexion

### Login (non capturé directement, style commun)
- Fond 3D animé (temple/hall intérieur Aven) + fenêtre modale centrée sombre CEGUI.
- Footer écran : texte disclaimer à gauche, boutons "Official Website" / "Quit" à droite.

### Sélection channel (`Selection_channel.png`)
- Fenêtre modale centrée ~700×400, sombre, titre bar.
- Deux colonnes : **Server** (gauche : "Ch01(Recommended)0 chara" en rouge) | **Ch.** (droite : Ch01, Ch02(Maintenance) avec puce gemme colorée).
- Scrollbar verticale droite. Bannière bas "Welcome to Eden Reforged!".
- Boutons "Confirm" / "Cancel" en bas.
- Derrière : le hall du temple (dôme vitrail + portail galaxie violet dans une arche au-dessus de la fenêtre).

### Sélection personnage (`Selection_personnage.png`, `shot_0011`)
- Même hall du temple : escalier central tapis rouge, personnage debout sur plateforme circulaire au centre.
- Panneau "Preview" en haut à gauche : grille d'icônes portraits.
- Carte personnage en haut à droite : portrait animé dans cadre orné sombre, ligne "Ch01(Ch01)", barres jaune 100 / bleue 13, nom perso + "Aven".
- Bas centre : flèches de rotation ◀ ▶ + bouton "Start Game".
- Bas droite : boutons "Set password", "Remove password", "Select Server", "Logout".

### Loading (`changement de map - changement d'étape de connexion.png`)
- **Plein écran illustré** : artwork de la map (Aven) avec cadre doré orné, JAMAIS de bandes noires.
- Haut gauche : nom map "Aven" + sous-titre "The Island Sanctuary".
- Haut droite : "Tip:" jaune + texte de tip.
- Bas gauche : paragraphe de lore/description map.
- Bas centre : barre de progression rose/dorée avec icônes (lune, alpaga, cristal) dessus.

## HUD in-game (`juste le jeu.png`, `mouvement caméra.png`, séquence 0016+)

### Layout (positions mesurées sur 1410×878)
- **Haut gauche** : portrait rond cadre orné (~110px). Dessous à droite : badges (épée 100, botte 1). Nom "EE???...辰???" sous le portrait, puis barre **HP rose/rouge** avec valeur "14999/15750", barre **MP bleue** "10000/11525". Sous : grille 2×6 slots numérotés 1-12 (barre objets rapide). À droite du portrait : petite rangée d'icônes skills de classe + buffs.
- **Haut centre** : bandeau "ULTIMATE" (titre) + "ULTIMATE Lv 1  EXP 200/400  Perks 2" + barre XP blanche/rose. Sous : rangée d'icônes buff (4 icônes, compteurs).
- **Haut droite** : minimap carrée (~200×200) avec nom map "Limestone Mtn."/"Aven" + icône planète. Dessous : rangée d'icônes utilitaires (mail✉, etc ~9 icônes), ligne "X:186 Y:279", horloge "00:01:33" + date "18:01:33".
- **Droite** : quest tracker collapsible — entrées "Use Portal Stone", "Background" (titres clicables ◆ bleu), onglets "Track Quests"/"Unacquired Quests" ; liste quêtes non acquises (L08 Angor Quarry I rouge…, radio ○ + "<Mining Captain> Kev").
- **Bas gauche** : chat window ~400×180 — onglets "Normal|Battle", texte "<System>…" orange/rouge, scrollbar, bouton "Settings". Dessous : Emoji + boutons canal Say/Peer/Party/Guild/Zone/World/Buzz.
- **Bas centre** : toasts tutoriel "How to move" (icône + texte).
- **Bas** : 2 barres fines (class exp "0.000%" icône classe + loyauté "100.000%"), puis hotbar 12 slots C1-C0/C-/C+ avec flèches page ▲▼ à gauche, icônes skills, + boutons edge.
- **Bas droite** : rangée ~10 icônes menu colorées (character/bag/skills/quests/social/guild/system).
- **Flottant monde** : nameplates — NPC/mobs jaune au repos, **rouge quand aggro/ciblé** ; nom joueur "[GM]…" + barre HP rouge sous le nom. Ville "Aven" en grand jaune au centre quand on entre dans la zone.
- **Messages centre** : "You have acquired <Hard Truth> state", "You have learned skill Ambush.", "Entering Combat"/"Leaving Combat" (rouge), "Switched Class to Bard" — avec FX ailes sombres latérales.

### Fenêtres (toutes CEGUI : cadre sombre translucide, titre haut-gauche, X fermeture haut-droite, "?" aide, déplaçables)
- **Backpack** (`shot_0019/0020`) : onglets haut All/Potions/Gear/Town/Special ; grille slots (fond fleur-de-lis sombre) ; icônes items + stack counts + liseré bleu qualité ; scrollbar droite ; bas : or "550"+crystal "6", "Archive All", rangée outils (swap/dye/fusion…), "Archive Save".
- **Mail** (`shot_0021`) : onglets Inbox/Send Mail ; checkbox Mark All/Delete emptied ; liste à puces ◆ ; boutons Collect All/Delete/Close.
- **Region Map** (`shot_0022/0023`) : carte Aven gauche avec labels districts ; centre "Search Listing" liste ; droite "City Features" (General Merchant…Portal, New Quest, Quest NPC) ; bas : World Map/Mainland/Regional + Coord X/Y + Icon Setting. Tooltips sur markers.
- **Shop NPC** (`shot_0024`) : fenêtre boutique (onglets catégories, grille items + prix).
- **Character Info → Title** (`shot_0025`) : liste titres + arbre + description droite.
- **Achievement** (`shot_0026/0027`) : arbre gauche, liste droite icônes ?, Incomplete, Victory/Defeat Quote.
- **Portal** (`shot_0028/0029`) : carte monde "Central Continent" avec icônes portails + flèches prev/next ; dialog confirm "You need to pay 1 Silver to … Limestone Mtn." Confirm/Cancel.
- **Quest** (`shot_0040/0041/0042`) : onglets Quest/Party/Legend ; gauche "Quests: 0/25 Track All" + arbre "> Unacquired Quests" (groupes 💎 3-Player DGN, Expedition Trial) ; droite détail quête (titre "L87. Shaxia Fae Fields (Daily) Party", "Environment to take:", "Targets to take:" "<Soul Guardian> [Go]") ; boutons Share/Drop/Close.
- **Class (K)** (`shot_0046/0047/0051/0052/0053`) : gauche liste classes triable (Mimic, Warrior Lv.1 Transform!, Knight, Templar, Dragon Knight, Thief, … Ranger, Inquisitor, Cleric Lv.14 "Current Clas", Bard, Shaman, Sage) + "Class Ability Bonus"/"CE List" ; droite onglets Class Skills/License Knowledge/Class News :
  - Class Skills : header icônes classe + nom + Activate + Lv ; "Reg. Skills" (coût or, grille skills Lv + "+1"/"Max") ; "Class Skills" (CP restants + skills Lv). Tooltip détaillé (coût MP, cooldown, effet, next level, coût CP).
  - License Knowledge : grille talents fléchée ; "Class Talent" KPs Remain/Entered + Confirm/Refresh ; "Certificates: 9" grille étoiles + Confirm.
  - Class News : description classe, Characteristics, Class Stat Bonus (%), Weekly Performance, preview perso + Set Outfit, icon rows.
- **Community** (`shot_0060/0061`) : onglets Friend/Search/Couple ; Friends List (colonnes Ch./Name/Lv/Class/Location/Info) + Block List + Edit Profile/Add/Block ; Search : résultats + filtre Lv min/max + classe tree + Others (Omit/Omit anon) + Search btn ; dialog "Enter Character Name".
- **Guild Recruitment / Town List** (`shot_0063`) : liste guildes (Name/Leader/Mbrs/Lv/Guild Town), Find Town, pager 1/1, Apply to Join/Close ; panneau filtres (Farm/Mine/Altar/Alchemy Lab/Foundry/Magic Pool + level + NPC Names).
- **Daily Reward / Event** (`daily reward.png`) : onglets Recommended Event/The Path of Destiny/Login Reward ; grille Day 1-28 + colonne Bonus + Claim ; panneau Information droite ; "Claim all (2)".

### Combat (`shot_0030/0031/0034/0035/0037/0038`)
- Cibler un mob → **target frame** haut-centre-gauche : portrait orné + nom + barres HP/MP ; **Target's Target** petit cadre à droite.
- Mob aggro : nameplate rouge + barre HP rouge ; cercle rouge sous le mob ciblé.
- Cast : **cercle magique doré au sol** sous le lanceur + cast bar ornée avec nom du sort ("Earth Shock") au-dessus + effet AoE coloré.
- Messages "Entering Combat"/"Leaving Combat" rouges top-centre.
- Course : traînée/poussière blanche sous les pieds.

### Divers
- Tooltip standard : fond sombre, titre, lignes de stats colorées (rouge = requis manquant).
- Caméra : follow derrière le perso, le monde défile (mouvement caméra.png = dash blur).
- Buffs : rangée sous barre ULTIMATE, icônes avec stack count ×N.

### Suite fenêtres
- **Guild Recruitment** (`shot_0064`) : tableau Guild Name/Leader/Mbrs/Lv/Guild Town ; "Find Town" ; pager "1/1" ◀◀▶▶ ; Apply to Join/Close. Filtres ville : Farm/Mine/Altar/Alchemy Lab/Foundry/Magic Pool + NPC Names.
- **Item Mall** (`shot_0065/0066`) : onglets "Item Mall"/"Loyalty Shop" ; gauche "Select a category" (Limited Item, Featured, Crafting Items, Pets & Mounts, Gear, Boosts, Fashion, Colorful Dye) ; centre "Catalog" : cartes items (icône, nom, prix 💎, barre stock "N left this week!" rouge, boutons Buy/Gift/BuyStack) + pager "1/5" ; droite "Hot Items" ; bas : Collect All + soldes "10721 💎 / 0 💠" + Close. Tooltip item complet (desc colorée, footer "Item / Can't be Traded"). NOTE : un item "Hot Items" affiché en chinois (三十格擴充背包圖樣) — le client Reforged lui-même a des noms non-EN dans le mall.
- **Item Mall détails** (`shot_0067/0068`) : catégories Limited Item/Featured/Crafting Items/Pets & Mounts/Gear/Boosts/Fashion/Colorful Dye ; lignes catalog avec "Buy Character Level: N" jaune + prix barré promo (Life Leaf 490→250) ; grille slots à gauche sous catégories.
- **Pet** (`shot_0069/0070`) : Pet List (Number: 0/30) + Seal/Unbind ; Pet Info : portrait, stats ATK/M-ATK/ACC/CRIT/M-CRIT/ATK SPD/Attack Attribute + Advanced ; Pet Gear 4 slots ; Rename, Pet Lunch Box+Feed ; Battle 5 slots+btn ; Pray 5 slots+btn.
- **Main Menu ESC** (`shot_0071`) : Settings, Reset UI, Ch., Logout, Quit, Continue (petit menu centré).
- **Settings** (`shot_0072`) : onglets Display/Audio/Chat/Hotkey/UI Settings. Display : Optimize Low/Mid/High, Window Mode dropdown, Anti-aliasing Off/2x/4x, sliders Char Draw/Field/Max FX/Shadows/Max FPS/Background/Groundcover, Models Low/High, Textures Low/Mid/High, Glow FX Off/Say/Max, Outline Off/Charac/All, Special FX checkboxes (Death/Photo/Blur/Glare/Water Wave/Shadows/3D Effect) ; Default/Confirm/Cancel.
- **Settings Audio** (`shot_0073`) : Mute ◇, sliders Music/SFX.
- **Settings Character** (`shot_0074`) : Mouse slider + ◇ : Name, Guild, Title, Vitals, Health Bar, Damage, Self-Cast, NPC Name, Critical Shock, Vibration, Decline Tandem, Moving Mouse, Invert Camera | Guild Insignia, Refuse Trade, No Duel, No Party, Search denied, Jelly Effect, Camera Follow, Battle Recorder, Event Effect, PVP War Armor, Left-click to route, Recommended Equ.
- **Settings Hotkey** (`shot_0075`) : table Option/Hotkeys — Community O, Pet N, Party 1-10 F1-F10, Move W/S/A/D, Turn Q/E, **Jump Space**, Sit/Stand X, Toggle WPN Z, Attack…
- **Settings UI** (`shot_0076`) : warning rouge "Avoid setting font size larger than UI", sliders UI Size/Font Size/Chat Font Size, dropdowns "Noble Irongray Style"/Filter Effect/Game Font.
- **Hotbar vide** (`shot_0077`) : slots vides = motif fleur-de-lis sombre ; libellés C1-C0 C' C+.
- **Channel Settings** (`shot_0078`, depuis bouton ⚙ chat) : Background — General/Party/Region/Couple's/Sys/Peer/Guild/World/Battlefield/Whisper Chnl (◇) ; EXP/Fame/DMG Dealt/Status Info/Coin/Item/DMG Taken (◇ off) ; Confirm/Cancel.

### Combat / FX fin de séquence (`shot_0080/0085/0086`)
- Messages appris : "You have learned skill Sneak." / "Swift Attack." — texte blanc centré + **paire d'ailes de phénix sombres** qui flanquent le texte + halo jaune sur le perso.
- "Entering Combat" rouge centré ; target frame = portrait monstre circulaire orné + nom + ★ + HP/MP + niveau ; **Target's Target** mini-cadre à droite.
- Dégâts : chiffres blancs bold flottants ("127", "6", "8" en rouge critique?).
- Chat : `<System>You have learned skill "Jewel Fusion"` rouge ; `<Say>[GM]…:bonjour`.
- Hotbar = **2 lignes** : ligne haute C1..C0 C' C+ (raccourcis Ctrl), ligne basse 1..0 - = + flèches page ▲▼ ; slots vides = damier fleur-de-lis.
- Grille 1-12 sous portrait = barre d'objets rapide (potions).
