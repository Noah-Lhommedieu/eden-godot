# Plan d'action — refonte fidélité totale (2026-10-26)

> Spec visuelle de référence : `docs/refshots-spec.md` (analysée des 79 images
> `ref-shots/`). Chaque item se clôt par une **preuve d'usage live** (driver
> `--live` / capture `--shot=` comparée à la ref) + build 0 erreur + petit
> commit. « Fonctionnel » = testé en jeu réel contre le serveur, jamais
> « juste affiché ».

## État au 2026-10-27 00:50 (point de reprise)

- **Positions fenêtres restaurées en liseré** — `ui_layout.cfg` gardait
  des positions hors champ (`world_map=-612`, `bank=-432`) et
  `DragHelper` ne garantissait que 48 px visibles. **Corrigé** :
  `ClampFullyVisible` au restore (fenêtre entière à l'écran si elle
  tient dans le viewport). Répare map/bank/social d'un coup.
- **`CeguiToGodot.SkinUnstyled(root)`** (nouveau, `scripts/ui/`) :
  passe de skin sombre sur un cadre natif — stylebox plat/blanc →
  panel sombre, skin texture blanc → teinté sombre (les vraies
  textures imageset ne sont jamais écrasées). Appliqué à map/social/
  class ; à réutiliser pour les autres fenêtres natives.
- **WorldMap** : `OptionUI` (« City Features » du ref) affiché +
  libellés Big5→EN (General/Weapon/Armor Merchant, Fame Shop, Auction,
  Revival Point, Warp Portal, Portal, New Quest, Quest NPC) ; capture
  validée — carte parchemin + checkboxes + Monster/Auto-Route +
  opacité/coords. Reste : icônes mapBtn rendues noires (imageset
  manquant) et filtres sans marqueurs à masquer (`shortcut:` noté).
- **Social** : monté sur `CommunityWnd.layout` (onglets natifs
  Friend/Search/Team/Lover) — Friend/Block en `ItemList` (charId en
  métadonnée, vert/gris online), boutons natifs Add Friend/Block →
  popup nom, bandeau Whisper/Remove/♥, invite ✓/×, déblocage au
  double-clic. Binds monde inchangés (12/22/14/13/10 + push 52/61/50/53).
- **Class (P)** : monté sur `JobWnd_NA.layout` (864×516) — liste en
  TabControl « Classes », détail dans Border gauche, PointStatic
  « Class Points », Bonus List, Switch Class + statut ; build paresseux
  (le dialogue naît avant `Hud.Tex`). Statuts traduits.
- **Captures** : `/tmp/cap_map3.png` (≈ref), `/tmp/cap_s4.png`,
  `/tmp/cap_c2.png` — build 0/0.
- **Reste** : onglets TabControl non rendus visuellement (Social),
  skin ItemList natif blanc sur d'autres fenêtres, NCID 78 tronqué,
  fuites RID/Objects à la sortie.

## État au 2026-10-26 23:55 (point de reprise)

- **Harness** : `game.tscn` direct (`--solo`/`--live` sans flux écrans)
  deadloquait — `LoadWorldSync()` bloquait le thread principal pendant
  que `MapLoader.LoadMap` repostait ses continuations dessus.
  **Corrigé** : toujours `LoadWorldAsync()` (no-op sans écran) ;
  `--quat-audit` déplacé dans `FinishWorldLoad`.
  → captures rapides : `godot --path godot_project res://scenes/game.tscn
  -- --solo --map=S001 --hud-open=<clé> --shot=/tmp/x.png` (~45 s,
  pas de serveur, fenêtres en données démo).
- **Stack serveur** : `eden_server/start-native.sh` **ne marche qu'en
  chemin absolu** (le script résout `.env` après `cd server/`).
  PostgreSQL local :5432, services : gateway 5560 / login 6543 /
  ticket 2067 / mission 7654 / world 5200+5301 / zone 5201.
- **HUD général** (capturé `--login --channel --char` sur S001) :
  layout conforme à `juste le jeu.png` — vitaux+grille 1-12, ULTIMATE,
  minimap+coords+icônes, tracker quêtes (onglets Track/Unacquired),
  chat Normal/Battle + canaux, double hotbar, icônes menus, gold,
  nameplates jaunes NPC, monde texturé/coloré.
- **Inventaire** : fenêtre `inventorywnd.layout` (NA2 écarté — cadre
  compact 466×189 sans monnaies) étirée aux proportions ref,
  `Static`/`SlotContainer`/`DownPanel` re-pinnés, bandeau d'onglets
  All/Potions/Gear/Town/Special ajouté, monnaies réelles
  (`PlayerVitals.Gold` 449|46 concordant avec le HUD, mall points en
  Invitation), rangée fonctions Stack/Break/Fortify/Fuse/Stall/Case
  (icônes DejaVu absentes → libellés ; Stall ouvre `StallPanel`),
  grille scrollée. Filtrage onglets conserve le slot réel des cellules
  (drag/drop/double-clic sains). **Prouvé live** `/tmp/cap_inv_live.png`.

## État au 2026-10-27 01:35 (point de reprise)

- **Positions persistées hors écran** : `DragHelper` restaure via
  `ClampIntoView` → fenêtres entièrement visibles (corrige les liserés
  `world_map=-612`, `bank=-432` de `ui_layout.cfg`).
- **WorldMap** : `ZoneMapUI.layout` + `OptionUI` (« City Features »)
  affiché et traduit Big5→EN — carte parchemin + checklist conformes.
- **Social (O)** : remonté sur `CommunityWnd.layout` (onglets natifs
  Friend/Search/Team/Lover), listes en `ItemList` sombres, boutons
  natifs Add Friend/Block + popup nom, bandeau Whisper/Remove/Couple.
  Binds monde inchangés (52/61/50/53).
- **Class (P)** : remonté sur `JobWnd_NA.layout` (864×516, liste en
  TabControl, points de classe, Switch Class EN) — build paresseux car
  le dialogue naît avant `Hud.Tex`. Protocole 489/168/510 inchangé.
- **`CeguiToGodot.SkinUnstyled(root)`** : passe partagée qui assombrit
  les contrôles natifs restés blancs/thème par défaut (remplace les
  fonds plats, conserve les vraies textures imageset).
- **Note live** : `game.tscn --live` direct stalle après le chargement
  de l'herbe — pour les captures live passer par le flux écrans
  (`godot --path godot_project -- --login=… --channel --char …`).

## Problèmes remontés (constat utilisateur)

- **P1 Perf** : ~20 s channel→sélection perso, ~15 s perso→jeu, 3,3 Go RAM.
- **P2 Textures** : couleurs de skins délavées/fausses ; monde blanchi
  (capture `final_live_hud.png` : géométrie blanche/bleue non texturée).
- **P3 Écrans** : login/channel/charselect = panneaux plats maison, RIEN du
  temple 3D + fenêtres CEGUI ornées ; bordures noires sur les fonds.
- **P4 HUD** : aucune fenêtre n'a le skin CEGUI (fond sombre translucide,
  cadres dorés/ornés, onglets, scrollbars natives) ; interactions non
  conformes (drag, onglets, tooltips, pagers).
- **P5 Fonctionnel** : les interfaces ne *fonctionnent* pas — dialogue NPC,
  quêtes, skills, buffs/états, guilde, classes, profil… à faire marcher
  par usage réel (voir phase 6).
- **P6 Saut** : mauvaise animation — doit être celle du client Reforged.
- **P7 NPC** : doublons + noms coréens → EN only depuis les bonnes tables.
- **P8 Mobs/combat** : téléportation au lieu d'interpolation, dégâts et
  animations d'attaque cassés.

## Phase 0 — Outillage ✅
- [x] ponytail → `.devin/skills/` (6 skills) + `.devin/rules/ponytail.md`.
- [x] code-review-graph → venv `~/.local/share/crg-venv`, MCP
  `.devin/mcp_config.json`, graphe construit (840 fichiers / 86k edges),
  `.code-review-graphignore` (re/, shots, dist, assets).
- [x] Spec extraite des ref-shots → `docs/refshots-spec.md`.

## Phase 1 — Diagnostic perf (avant toute modif)
- [ ] Instrumenter les transitions : logs timestamp `ScreenManager` (channel→
  charsel) et `GameScene`/`MapLoader` (charsel→ingame) — mesurer où partent
  les 20 s / 15 s (suspicion : chargement synchrone d'assets gamedata +
  GLB + textures à la demande sur le thread principal ; parses .ini
  répétés ; textures non cachées).
- [ ] Profiler mémoire : `OS.GetStaticMemoryUsage`/`RenderingServer` +
  `--verbose` → où sont les 3,3 Go (textures non compressées en RAM ?
  doubles instances ? gamedata parsée en mémoire massive ?).
- [ ] Livrable : tableau des temps par étape + top 5 postes mémoire.

## Phase 2 — Rendu monde & textures
- [ ] Pourquoi le monde rend blanc : vérifier `ToonMaterializer`/`AssetCatalog`
  (matériaux par défaut quand texture absente, sRGB/flags, mips).
- [ ] Skins persos : pipeline `CharVisual`/`CharDressing` — vérifier espace
  couleur des textures corps (DDS/TGA→import), `albedo` multiplié, teintes
  `CharColorTable`, faces `KFACES*`.
- [ ] Fonds d'écran écrans : la map temple doit remplir tout le viewport —
  vérifier `TempleBackdrop` (camera FOV/aspect, scale, culling) → zéro bande noire.

## Phase 3 — Écrans login / channel / charselect
- [ ] Réutiliser le backdrop temple 3D existant (scène + caméra orbitale
  lente) comme fond commun aux 3 écrans, plein écran.
- [ ] Login : fenêtre centrée style CEGUI (sombre, cadre orné), champs
  compte/mdp, boutons Confirm/Cancel, bannière, footer disclaimer +
  Official Website/Quit.
- [ ] Channel : 2 colonnes Server/Ch. + puce gemme + scrollbar + bannière
  "Welcome to Eden Reforged!" + Confirm/Cancel.
- [ ] Charselect : panneau Preview (grille portraits) haut-gauche ; carte
  perso haut-droite (portrait orné, Ch01, barres jaune/bleue, nom+Aven) ;
  rotate ◀▶ + Start Game bas-centre ; Set/Remove password, Select Server,
  Logout bas-droite ; perso sur plateforme circulaire, caméra hall temple.
- [ ] Loading : plein écran illustré (artwork map + cadre doré, titre/sous-
  titre map haut-gauche, Tip haut-droite, lore bas-gauche, barre
  progression rose/dorée avec icônes).

## Phase 4 — HUD : skin CEGUI global
- [ ] Le projet a déjà `CeguiImageset`/`CeguiScheme`/`CeguiLookNFeel`/
  `CeguiLayout`/`CeguiToGodot`/`CeguiBuilder` — vérifier que les thèmes/
  imagesets sont chargés et appliqués aux fenêtres (sinon le câbler :
  Theme Godot généré depuis imageset natif pour Panel/Window/Button/
  ScrollBar/TabContainer/LineEdit/ItemList/Tree/Tooltip).
- [ ] Refondre `HudRoot` pour matcher le layout mesuré : portrait+badges+
  HP/MP+grille 1-12 ; bandeau ULTIMATE+buffs ; minimap+coords+horloge ;
  quest tracker (onglets Track/Unacquired, ◆ cliquables) ; chat (onglets
  Normal/Battle, canaux, ⚙ Channel Settings) ; double hotbar C-row/num row
  + pages ▲▼ ; rangée menus bas-droite ; nameplates jaune/rouge ; toasts ;
  messages centre + ailes phénix.
- [ ] Fenêtres une par une contre ref : Backpack, Mail, RegionMap, Shop,
  CharInfo/Title, Achievement, Portal, Quest, Class(K), Community, Guild,
  Item Mall, Pet, Settings (5 onglets), Main Menu, Channel Settings,
  Daily Reward/Event.
- [ ] Interactions : drag fenêtres (DragHelper existe), tooltips sombres
  multi-couleurs, pagers ◀◀▶▶, onglets, scrollbars, focus/close.

## Phase 5 — Fonctionnel des interfaces (preuve par usage live)

Critère commun : chaque système est ouvert depuis son raccourci/icône,
utilisé en conditions réelles contre le serveur, et la preuve est capturée
(capture + log `[live]` montrant la requête et la réponse).

### Dialogue & quêtes
- [x] **Talk NPC** : clic NPC → fenêtre dialogue style ref (nom, bulle,
  choix) ; réponses clickable ; fin de dialogue propre.
  *(prouvé : --talk-test + --quest-test=50296 — 285 EN, options, rituel
  Soul Guardian 285→11→531→285)*
- [ ] **Quest** : accepter/rendre une quête depuis un NPC (!/?), fenêtre
  Quest avec onglets + détails (objectifs, rewards, N/Done), tracker mis
  à jour en live, completion "Quest Completed!".
  *(fenêtre prouvée EN ; accept/reward 388→87 pas encore déclenché live)*

### Skills, buffs & états
- [ ] **Skills (K)** : liste par classe avec icônes réelles, Learn/level-up
  coûtant des points, drag vers hotbar.
- [ ] **Hotbar usage** : touche → lancement réel du skill (anim + paquet) ;
  cooldown grisé ; consommables utilisables (HP/MP bougent).
- [ ] **Buffs/états** : bandeau buffs sous ULTIMATE avec icônes+timer+
  tooltip ; liste des états actifs (fenêtre Status/Buff list) ; buffs
  appliqués par skills (test : caster un buff → icône + durée qui défile,
  retiré à expiration).
- [ ] **Cast** : cast bar centrée ornée + nom du sort + cercle magique au
  sol ; interruption affiche "Interrupt".

### Personnage, classe & progression
- [ ] **CharInfo (C)** : onglets Gen./Stats/Equip./Title — stats réelles
  du serveur (STR/AGI/…), équipement dans les 15 slots, tooltip items.
- [ ] **Class & changement de classe** : fenêtre Class (icônes toutes
  classes + niveaux, "Switch Class" fonctionnel — coût conditions, swap
  effectif en jeu, barres ULTIMATE mises à jour).
- [ ] **Profile/Title panel** : panneau profil (nom, niveau, classe,
  titres) + Achievement/Title list (noms réels, catégories, progress) ;
  sélection de titre active le bonus si implémenté serveur.
- [ ] **Level up** : message centre + buff visual, stats recalculées.

### Social & communauté
- [ ] **Community (O)** : amis liste, add/del par nom, online status live.
- [ ] **Guild** : fenêtre Recruitment (liste guildes tri/filtres ville,
  Apply to Join) ; fenêtre Guild (infos, membres, Grade, Quitter).
- [ ] **Party** : invitation cible → fenêtre party, membres listés avec
  HP ; leave fonctionnel (driver `--live` existe à étendre).
- [ ] **Chat** : canaux Say/Peer/Party/Guild/Zone/World + Whisper réel
  (clic nom → whisper) ; Channel Settings (⚙) filtres actifs.
- [ ] **Elf/familier** : fenêtre Elf (4 onglets, list, summon/dismiss,
  callback) fonctionnelle contre serveur.

### Économie & objets
- [~] **Backpack (B)** : grille réelle + onglets catégorie + monnaies
  + rangée fonctions (prouvé live — items/gold concordants). Drag &
  drop sac↔sac, split stack, use/equip câblés (NCID 7/8/19/84) — à
  prouver par usage live ; boutons sac 0-5 = sélecteur d'extension.
- [x] **Shop NPC** : acheter/vendre par usage (qty, prix mis à jour,
  gold décrémentée).
  *(prouvé : achat «Pure Dew» 245g — gold 48 690→48 445, ack 308)*
- [x] **Trade joueur** : request → accept → fenêtre 2×3 slots, add item,
  confirm, échange effectif.
  *(prouvé 2 clients admin/test123 : 36→37→332→40+38→41, TradeState.
  Completed des deux côtés, "Trade complete!")*
- [ ] **Vending** : pose boutique perso + achat par un second client.
- [x] **Bank/Neobank** : dépôt/retrait items + gold.
  *(prouvé : 357 ouverture → 62→358, dépôt 0→2, retrait 2→1,
  extension +20 (373), drags NCID 7, coffre famille 177/178 + journal)*
- [ ] **Auction (si testable)** : liste, recherche, enchère/achat immédiat.
- [ ] **Mail** : liste reçus, lire, récupérer pièces jointes.
- [x] **Item Mall** : onglets Item Mall/Loyalty Shop, catégories, Buy/
  Gift/BuyStack réels, points mis à jour, Collect All.
  *(prouvé : catalogue 472 items, achat grp=7 item=40362 → 63 OK,
  points 136→135)*
- [ ] **Repair** : Repair All au forgeron avec coût calculé.

### Monde & utilitaires
- [ ] **RegionMap (M)** : carte région avec icônes NPC (noms au survol),
  click → coordonnées, world map.
- [ ] **Portal/transport** : liste destinations avec coûts, teleport
  effectif.
- [ ] **Pet (N)** : Pet List + Pet Info + Battle/Pray slots, Seal/Unbind,
  Feed/Lunch Box, Rename, summon.
- [ ] **Settings** : 5 onglets fonctionnels (Display/Audio/Char/Hotkey/UI)
  — persistance des options, application immédiate (mute, sliders,
  checkboxes nameplates…).
- [ ] **Main Menu (ESC)** : Settings, Reset UI (réel), Ch. (retour
  channel), Logout, Quit, Continue.
- [ ] **Daily Reward/Event** : ouverture, collecte, cooldown respecté.
- [ ] **Rare Monster/Field Boss infos** (si serveur les pousse) : panneau
  Kill/Battle/Alive.
- [ ] **Mini-map** : coordonnées cliquables→worldmap, icônes NPC/mobs/
  quêtes, horloge ; bouton → MapWindow.

## Phase 6 — Gameplay feel
- [ ] Saut : reprendre l'anim Reforged (`jump_feature`/motion table) —
  vérifier le mapping `MotionTable` et l'anim jouée (actuellement
  `jump01` PlayOnce) ; comparer frame par frame à une capture Reforged.
- [ ] Mobs : téléportation → interpolation : vérifier `InterpBuffer`/
  `NetworkInterpolator`/`EntityRegistry` (dt de snap, vitesse anim walk/
  run, prédiction serveur) ; le serveur envoie des positions brutes ?
  lisser + jouer anim move.
- [ ] Combat : dégâts flottants style ref (chiffres bold blancs/rouges),
  cercle magique doré au sol au cast, cast bar ornée + nom du sort,
  target frame orné + Target's Target, nameplates aggro rouges,
  messages Entering/Leaving Combat.

## Phase 7 — Contenu
- [ ] NPC dupliqués ×2 + coréens : auditer `MonsterTable`/`TextTable`/
  `TextResolver` — source des noms (tables EN vs KR dans gamedata) ;
  filtrer les spawns serveur côté `MapLoader`/`EntityRegistry` (doublon
  probable : NPC spawnés depuis 2 sources ou scene+table).
- [ ] Vérifier que SEULE la table EN (US) alimente les noms ; logguer tout
  nom non-ASCII latin restant.

## Phase 8 — Validation & delivery
- [ ] Matrice de comparaison capture-par-capture : chaque écran/fenêtre
  Godot vs ref-shot du même état (script `--shot` + galerie
  `godot-shots/`).
- [ ] Checklist fonctionnelle phase 5 : un test live par système (étendre
  les drivers `--live` existants : talk/service/storage/trade/vending/
  mall… ajouter quest/buff/class/guild).
- [ ] Mesures perf avant/après documentées dans `docs/PERF.md`.
- [ ] Commits par sujet, docs d'état à jour.

## Entrées code repérées
- Channel→Char : `ChannelSelectScreen.cs:258-266` — `SelectWorldAsync` +
  `WorldLoginAsync` + `ReceiveCharacterListAsync` séquentiels → mesurer
  chaque await (timestamps).
- Char→Game : `CharSelectScreen.cs:506` `ChangeSceneToFile` →
  `GameScene._Ready` → `LoadWorldAsync` (`Task.Run(loader.LoadMap)`).
  Suspects : `MapLoader.LoadMap` (GLB/terrain/props), parses .ini à la
  volée, préviews persos GLB dans CharSelectScreen, textures uploadées
  en RAM double (mipmaps générés CPU → +31 % VRAM noté au plan
  précédent).
- CEGUI : `CeguiContext.Load(uidata)` ~1 s async OK ; `UiTextureCache`
  atlas → vérifier si les fenêtres l'utilisent vraiment (capture
  actuelle = panneaux plats → probablement pas appliqué).
- RAM 3,3 Go : suspects GLB en mémoire ×2 (parsed + nodes), textures
  `Image` non libérées après `ImageTexture`, gamedata tables chargées
  en entier (`TextTable` strings massives), mipmap génération CPU.
- Systèmes : fenêtres dans `scripts/hud/` + `scripts/ui/` ; drivers live
  existants dans `GameScene.cs` (`RunTalkTestAsync`, `RunServiceTestAsync`,
  `RunStorageTestAsync`, `RunMobTestAsync`, `RunTradeTestAsync`,
  `RunVendingTestAsync`, `RunMallTestAsync`, `RunCombatTestAsync`) —
  à étendre pour quest/buff/class/guild.
  
  
  
  
  De nouvelles tâche à placer dans le document actuel de projet de refonte :
  
- Quand j'arrive en jeu je lag ENORMEMENT (moins de 60 fps très clairement).
- Quand on arrive sur la page login le jeu prend environ 2 secondes pour afficher l'image en fond, c'est quoi qui charge ? c'est pas une simple image ?
- Les page de connexion au compte et de choix de channel sont VRAIMENT moche, tu as des skills pour rendre beau des UX/UI avec des couleurs, tu as des thèmes inclut dans les assets.
- La selection de personnage est également longue a charger les données du model skin du joueur, y'a t-il pas une méthode plus simple ?
- La selection de personnage montre des preview de classe ? retire simplement le coté preview, c'est une fonctionnalité peu utile.
- L'interface de création d'un personnage est bug, caméra trop rapproché du personnage montré, mais surtout trop de temps a charger les models skin encore une fois.
- Les épaules et certaines partie de corps de certaines race (ou homme / femme en ezelin et humain) ont pas de couleur (noir, vide etc).
- L'interface de création d'un personnage a droite est extremement moche, trop de trou sur l'UI etc, le bouton "check name" marche pas, et le bouton back non plus, je n'ai pas vérifier pour créer un personnage mais ça marche probablement pas.
- En jeu tu n'a toujours pas créer de système de thème, il est ESSENTIEL de formulé via héritage le système de frontend des page / UI / Interface en jeu, normalement en jeu il y aura des thèmes (que tu as dans les assets, bleue, violet, rouge, gris...), ces thèmes modifieront les entêtes et contour de quasiment tous les panel, HUD et UI en général en jeu, en rajoutant en plus le fait que TOUTES les panels, UI et HUD doivent être déplaçable, si tu ne fais pas un code propre pour créer des panels a partir d'un code initial (comme la mentalité d'héritage en programmation orienté objet), tu n'arriveras jamais a recréer tous les panels HUD et UI proprement. Ca permet également de faire via la logique d'héritage des panels et HUD identique s'ils sont "simple", et custom si le code override par dessus l'héritage certaines fonction etc.
- Tu devrais essayer d'analyser le client reforged et son code via le dossier "re" car il y a beaucoup de chose mal refaite, par exemple le chargement de selection personnage vers le jeu, ce chargement
- Il faut arrêter d'appeler le jeu "Eden Eternal Reborn", il faut l'appeler "Eden Limitless".

- Les mobs se téléportent toujours même s'ils ont des animations pour courir.
- Soit -> maps sont plus grande en ratio que dans le jeu de base de environ 20-30% a vu d'oeil en tant qu'humain, et les mobs sont également un peu plus grand de 20% environ. Soit -> Les models joueur sont trop petit et pas assez rapide par rapport aux mobs et aux maps. Tu peux soit augmenter les tailles de joueur et leur vitesse, soit baisser les tailles de map et les mobs (a toi de voir selon le plus facile et stable a faire, a première vue je dirais augmenter humain et move speed mais vérifie). Dans tous les cas pour le temps du dév rajoute une vitesse de déplacement x5 au joueur histoire de pouvoir faire les tests plus vite.
- J'ai corrigé pas mal de chose dans le fichier hud-interfaces.md sur les noms ainsi que les fonctions que doivent avoir les HUD et Panels, j'ai également renommé de nouvelles images dans le dossier "ref-shots" pour t'indiquer pour quel image quoi aller chercher comme infos visuel pour les HUD et panels.
- Coté HUD il y a énormément d'HUD parasite qui ont RIEN A FOUTRE là, vérifie bien le code actuel par rapport a ce qu'il faut dans le docs "hud-interfaces.md" avec mes modifications.
- Certains endroits sur les maps (où parfois y'a de l'eau dans un lac etc) doivent pas être atteignable, normalement le jeu contient des "murs invisible" a certains endroits.
- Il manque les animations d'attaque et skill.
