# Plan d'action v3 — stabilité, rendu, écrans, HUD, panels

> **Remplacé par `2026-10-27-plan-action-v4.md`.** Au test utilisateur,
> seuls P1.1 et P1.4 sont validés ; les autres cases ✅ ci-dessous sont
> rouvertes dans le v4.

Rédigé le 27/10/2026 après le retour de test (capture en jeu Aven,
port) et une analyse du code client (`eden_godot/godot_project`), du
serveur (`eden_server/server`) et des `ref-shots/`.

Règles : `hud-interfaces.md` (fonctionnel, prioritaire) > `ref-shots/`
(visuel) > `REF-SHOTS-SPEC.md` (indicatif). Un seul client Godot à la
fois. Une case n'est cochée qu'avec preuve runtime (capture ou log de
test) + commit dédié. La barre ULTIMATE reste cachée (feature
post-portage).

Ordre de traitement : **P0 stabilité** → **P1 rendu/chargement** →
**P2 écrans hors-jeu** → **P3 HUD** → **P4 panels**. P0 d'abord : tant
que le jeu gèle au changement de map, aucun test de combat ou de quête
n'est fiable.

---

## P0 — Stabilité et performances (bloquant)

### P0.1 Gel / crash au changement de map — ✅
**Statut (27/10)** : commit `c54a004`. Migration asynchrone sous
`LoadingScreen.EnsureOverlay` : paquets d'entités en file pendant le
chargement, ancienne map libérée avant la nouvelle, file rejouée après.
`AssetCatalog.PreloadGlbProtos` séquentiel (deadlock ObjectDB). Cause
sœur trouvée plus tard : tout appel `GetImage()` depuis un worker
bloque si le main est dans un `Parallel.ForEach` (cf. `10206c4`).

**Constat** : après `//transport_node 2` (Aven → Limestone Mtn.), le
client écrit `[live] NodeData node=2`, `[grass] 5000 instances…` puis
plus aucune ligne (même réseau) jusqu'au kill. Même figement observé
par intermittence à l'entrée en jeu (après un spam de `NCID=326`).

**Cause identifiée (partielle)** : `GameScene.OnNodeData`
(`scripts/GameScene.cs` ~l.2933) recharge **toute la map de façon
synchrone sur le thread principal, dans un handler réseau**
(`LoadMapNode` → `MapLoader.LoadMap`), sans écran de chargement,
l'ancienne map encore vivante (libérée après). Les paquets reçus
pendant ce temps s'accumulent dans `_pending`. Les corps PNJ
(`_npcDrainBudget`) ne sont pas réarmés pour la nouvelle map.

**Actions**
1. Reproduire (`--live`, `//transport_node 2`) et capturer les piles
   gérées du process figé : `dotnet-stack report -p <pid>` (+ `gdb -p`
   pour le natif Godot) → identifier le deadlock exact (await
   `TaskCompletionSource` dans `_pending`, verrou `AssetCatalog`,
   `Node.Duplicate` concurrent, GC bloquant…).
2. Faire passer la migration par le **même chemin que l'entrée en
   jeu** : `LoadingScreen` (overlay qui survit au changement de scène,
   chargement par phases, cf. `scripts/screens/LoadingScreen.cs`) →
   suspendre le traitement des entités pendant le chargement, libérer
   l'ancienne map **avant** de construire la nouvelle, puis rejouer
   les paquets en attente.
3. Réarmer le streaming PNJ, la minimap, la Region Map et le tracker
   après chaque migration (`_npcDrainBudget = true`, `SetZone`).
4. Garde-fou : watchdog qui logue un dump si une frame dépasse 5 s.

**Validation** : 10 allers-retours Aven ↔ Limestone sans gel, puis
`--mob-test --combat-test` jusqu'au bout avec capture.

### P0.2 Crash si la connexion prend trop de temps — ✅
**Statut (27/10)** : commit `ed6a736`. Keepalive client 45 s sur le
lien ouvert (login/world/zone), `EDEN_TICKET_TTL_SEC` défaut 300 s,
déconnexion inattendue => message + retour login (Logout du
sélecteur idem). Validation : attentes >70 s et >6 min puis entrée.

**Constat** : rester trop longtemps entre le login et l'entrée en jeu
→ crash.
**Pistes trouvées** : `Eden.Ticket/Services/TicketManager.cs` →
tickets valables **60 s** (`TimeSpan.FromSeconds(60)`) ;
`EdenConfig.IdleTimeoutSec` = 300 s (`EDEN_IDLE_TIMEOUT_SEC`) sans
octet reçu → fermeture de session ; aucun keepalive côté client
(`scripts/session`, `scripts/net` : pas de ping).

**Actions**
1. Reproduire : attendre 70 s puis 6 min sur la sélection de canal et
   de personnage ; lire les logs Gateway/Login/World/Zone
   (`CloseReason`, « tickets expirés purgés »).
2. Client : keepalive périodique sur les connexions login/world
   ouvertes (paquet natif de ping/heartbeat à retrouver dans
   `re/` + codecs générés), demander chaque ticket **au moment de
   l'utiliser** (pas au début du flux).
3. Serveur : TTL de ticket configurable (`EDEN_TICKET_TTL_SEC`,
   défaut ≥ 300 s).
4. Client : toute déconnexion = message clair + retour à l'écran de
   connexion (jamais un crash). Le même retour sert au « Logout » du
   Main Menu.

**Validation** : 10 min d'attente sur chaque écran puis entrée en jeu.

### P0.3 PNJ en double — ✅
**Statut (27/10)** : commit `a41b223` (conjoint P0.4 — même pipeline,
même validation). `MapLoader.SpawnSceneNpcs=false` en live : la table
de scène ne sert qu'au préchargement, EntityRegistry instancie seul.

**Cause identifiée** : `MapLoader.BuildEntities`
(`scripts/world/MapLoader.cs` l.567-700) crée un corps pour **chaque
PNJ de la scène** (`data/scene/sNNN.ini`, streamé 3/frame), et
`EntityRegistry` (`scripts/world/EntityRegistry.cs` l.670-) crée un
**second** corps pour chaque PNJ envoyé par le serveur (kind 8).

**Actions** : une seule source. Garder l'entité serveur (interaction,
quêtes, déplacements) et faire réutiliser par le registre le corps
déjà préconstruit par MapLoader (clé `npc_id`) au lieu d'en créer un
nouveau ; supprimer les holders de scène non réclamés après le burst
d'entrée. Vérifier aussi les doublons de nameplate.

**Validation** : 1 seul modèle et 1 seul nom par PNJ sur S001
(comptage `Npc_*` vs entités kind 8 logué).

### P0.4 ~10 s à moins de 20 FPS à l'arrivée en jeu — ✅
**Statut (27/10)** : commit `a41b223` + `c54a004` (GC.Collect
assoupli). Visuels PNJ étalés (budget ms/frame), rayon de
visibilité ~80 m, variantes de skins préchargées sous l'écran de
chargement. S001 : ~90 fps dès les premières secondes, 11,1 ms frame
moy, 12 498 nœuds vs 36 513 (tableau dans `PERF.md`).

**Mesures existantes** (`docs/PERF.md`) : S001 = 3 769 objets,
**263 PNJ**, 36 500 nœuds, ~3 250 draw calls, RSS ~1,9 Go. Coûts à
l'entrée : streaming des 263 corps PNJ (3/frame, ~20 ms chacun) en
plus des doublons P0.3, décompression S3TC des textures, compilation
de shaders, herbe (5 000 instances), puis `GC.Collect` **bloquant et
compactant** en fin de streaming (`GameScene._Process`).

**Actions**
1. Mesurer l'entrée live : `--perf --perf-seconds=30` + `PerfTrace`
   par phase → tableau frame-time des 30 premières secondes dans
   `PERF.md`.
2. Comme le client d'origine : **rayon de visibilité** pour PNJ, mobs
   et joueurs (spawn du corps sous ~60 m, désactivation au-delà,
   `visibility_range` Godot) ; streamer par distance au joueur, borné
   en millisecondes par frame plutôt qu'en nombre.
3. Supprimer le `GC.Collect` bloquant (ou le faire sous l'écran de
   chargement) ; tout le préchargement (GLB, anims, textures) **pendant
   l'écran de chargement**, avec la barre qui reflète l'avancement
   réel.
4. Textures : conversion hors ligne en formats GPU (`.ctex`/BPTC/S3TC)
   au lieu du décodage DDS à l'exécution ; préchauffe des shaders
   (matériaux toon/eau/herbe instanciés une fois hors champ pendant le
   chargement).
5. Herbe et props : vérifier le culling à distance (PerfCulling) et
   le batching (PropBatcher) sur S001/S002.

**Validation** : ≥ 60 FPS dès la première seconde en jeu sur S001
(1280×720), frame max < 50 ms, mesuré et consigné dans `PERF.md`.

---

## P1 — Rendu et chargement des modèles

### P1.1 Textures absentes sur la map (murs roses du port d'Aven) — ✅

**Statut (27/10)** : commit `5f82b22`. Deux causes réelles, pas une :
(a) helpers de collision Max (`01block02`, `Line01`, `Bone@#`,
`true wall2`, `UA103-s_7`, `Box_#N`…) rendus car non couverts par
`HelperName` → rose saumon ; regex étendues (collision conservée).
(b) GLB stale : index PNG construit après conversion → 16 k nœuds
avaient perdu leur binding alors que les PNG existaient ; repli de
variante à 2 chiffres (`UA00151`→`UA00101`) ajouté à `convert.py` +
reconversion du corpus complet (17 822 sources, 27 échecs
format pré-existants). Garde-fou runtime : log `[toon]` des surfaces
visibles non texturées (1/modèle). **Preuves** : captures
`vis_test` S009/S001/S002 sans surface rose, 0 warning `[toon]`
S001 ; rapport `docs/nif_report_map.txt` + `docs/nif_report.txt`.

**Cause identifiée** : `ToonMaterializer.ConvertMaterial`
(`scripts/world/ToonMaterializer.cs` l.258) : un matériau **sans
texture d'albédo** garde sa couleur unie NIF (rose saumon) → ce sont
des textures non trouvées à la conversion NIF→GLB (`tools/nif2gltf`).
Le dernier rapport (`docs/nif_report.txt`) est vide (0 fichier
traité), donc pas d'historique.

**Actions**
1. Script d'audit : parcourir `assets/models/{map,house,decorate}`
   et lister les matériaux sans `albedo_texture` (+ texture NIF
   référencée attendue).
2. Pour chaque texture manquante : retrouver le fichier dans
   `raw_assets` (casse, extension `.dds/.tga/.bmp`, dossiers
   `texture/` partagés, textures animées `.ifl`), corriger la
   résolution dans `nif2gltf` et reconvertir.
3. Garde-fou runtime : loguer chaque matériau sans texture (une fois
   par modèle) au lieu d'afficher une couleur unie silencieusement.

**Validation** : audit à 0 (ou liste justifiée), captures du port
d'Aven et de Limestone sans surface unie.

### P1.2 Parties de modèles noires (épaules de l'humain…) — ✅

**Statut (27/10)** : commits `117f922` + `10206c4`. La « zone noire »
était des boîtes de fixation non texturées `*Nub*` (`Hair Nub_#G100`,
`Purse Nub`, `Left/Right Sleeve #G2110 Nub 01`) rendues sur les corps —
ajoutées à `HelperName`. Au passage : `TintMeshes` deadlockait sur
`Parallel.ForEach` (`Texture2D.GetImage()` = readback bloqué tant que
le main n'a pas pompé de frame) et teintait ~300 tenues cachées —
boucle séquentielle + filtre `Visible` (`BuildBody` >300 s → ~5 s ;
teintage 160-175 ms) + `RetintVisible` après `CharDressing.ApplyLook`.
**Preuves** : les 7 corps (b/c/f/g/k/l/m) rendus teintés sans zone
noire, 0 warning `[toon]`, captures `vis_test --char=* --dist=8`.

**Hypothèses** : slot de texture secondaire (dark/detail/gloss,
env map) pris pour l'albédo, alpha mal interprété, ou texture de
variante d'équipement (`4pequip.ini`) non résolue → couleur noire.
**Actions** : même audit que P1.1 sur `assets/models/char`,
`item`, `ride`, `monster` (matériaux noirs ou sans texture), toutes
races et genres, avec et sans équipement ; corriger la conversion ou
le choix de texture dans `CharVisual.ApplyEquipment`.
**Validation** : planche de captures (5 races × 2 genres × tenue de
base + 3 tenues), aucune zone noire.

### P1.3 Modèle du joueur chargé en retard (sélection perso, tenue, skin) — ✅
**Statut (27/10)** : implémenté dans `f792028` (`CharBodyCache` +
prewarm du roster à la réception de la liste, `ChannelSelectScreen`
et `CharSelectScreen._Ready`), preuve via `--char-cache` (`30498a1`)
: `Prewarm` ≈ 5 s en tâche de fond pendant le choix, `GetOrBuild`
= **10 ms** sur le main pour une copie du proto (cible <100 ms).
Le `TintMeshes` qui plafonnait `BuildBody` est réduit à ~170 ms
(`10206c4`) — le coût restant est le parse GLB (~3,5 s), absorbé
par le prewarm.

**Actions** : préchargement en arrière-plan des corps, têtes et
équipements des personnages de la liste dès la réception de la liste
(cache `AssetCatalog` persistant entre scènes) ; changement de
tenue/skin = remplacement instantané depuis le cache ; pas de
reconstruction complète du corps (~7 s mentionnées dans
`TopLeftVitals.SetPortrait`).
**Validation** : sélection d'un perso → modèle affiché < 100 ms ;
équiper une tenue → visuel mis à jour dans la frame.

### P1.4 Écran de chargement : alpaga + petit humain — ✅
**Statut (27/10)** : implémenté dans `c54a004`, preuve via
`--loading-test` (`30498a1`) : frac=0.55 → alpaga qui marche au bout
du remplissage rose + humain qui marche à l'extrémité droite sur les
cristaux (identique au ref-shot « changement de map ») ; frac=1.0 →
frame câlin (alpaga+humain+cœur) avant le fondu. Même écran pour
l'entrée (`RunAsync`) et les migrations (`EnsureOverlay`, P0.1).

**Constat** : `animation.png` (352×60) = 5 images : **0-1 alpaga qui
marche, 2 câlin (alpaga + humain + cœur), 3-4 humain qui marche**.
`LoadingScreen.cs` fait défiler les 5 en boucle.
**Actions** : pendant le chargement, afficher **les deux** (alpaga
0↔1 et humain 3↔4) qui avancent avec la barre ; image 2 (câlin)
seulement quand la barre est à 100 %, puis fondu. Même écran pour
l'entrée en jeu et pour chaque changement de map (P0.1).

---

## P2 — Écrans hors-jeu

### P2.1 Fond de l'écran de connexion qui change après quelques secondes
**Cause identifiée** : `TempleBackdrop` (`scripts/screens/
TempleBackdrop.cs`) affiche une image 2D provisoire (`_placeholder`)
puis la remplace par le temple 3D quand le GLB est prêt.
**Actions** : garder l'écran de démarrage jusqu'à ce que le temple 3D
soit prêt (préchauffe déjà lancée au boot), ou fondu enchaîné court
entre les deux.

### P2.2 Connexion et choix de canal (ref `Selection_channel.png`)
**Actions** : construire les deux écrans sur les layouts natifs
présents dans `uidata/layouts` : `Login.layout`,
`CWorldServerListWnd.layout` (actuellement codés à la main). Ref
canal : emblème cristal orné au-dessus de la fenêtre, listes
parchemin « Server » / « Ch. » (`Ch01(Recommended)` en rouge, icônes
gemme verte, `Ch02 (Maintenance)` grisé), barre ◀●▶ sous la liste
serveur, zone de message, Confirm/Cancel centrés, « Official
Website » / « Quit » en bas à droite, texte trademark centré en bas.
La connexion reprend le même habillage (pas de ref : même famille
visuelle que la fenêtre de canal).

### P2.3 Sélection de personnage (ref `Selection_personnage.png`)
**Actions** : layout natif `SelectCharacterWnd.layout`. Carte du perso
en haut à droite (portrait orné, badges niveau/classe, nom, zone),
flèches de rotation et « Start Game » en bas au centre, pile Set
password / Remove password / Select Server / Logout en bas à droite.
Pas de panneau « Preview » (demande utilisateur), pas d'onglets de
personnages en haut ; choix du personnage par la carte ou par clic
sur le modèle. Modèle immédiat (P1.3).

---

## P3 — HUD

### P3.1 Barres d'EXP perso et classe fonctionnelles
**Données** : le serveur lit la courbe dans `S_Level.ini`
(`ProgressionParsers.ParseLevels` : col 1 = EXP du niveau, col 10 =
EXP de classe `ClassExpNeeded`, alors que `Combat.Rewards`
(l.263) commente col 11 → à trancher en lisant
`ProgressionService.ExpForLevel` et le natif). `C_Level.ini` client a
le même format `|V.8|27|`.
**Actions** : table `LevelTable` client calquée sur le serveur ;
ratio = EXP courante (600 / `PlayerVitals.Exp`) ÷ EXP du niveau
(niveau 310) ; EXP de classe = attr 0x06 ÷ seuil du niveau de classe ;
affichage `XX.XXX %` (précision au clic droit déjà en place), vert
pomme / violet ; mise à jour à chaque gain.
**Validation** : tuer un mob → la barre avance du bon ratio
(comparé au calcul serveur logué).

### P3.2 Quest tracker
- Lignes séparées par de fins contours, survol en surbrillance
  (`MakeObjective` / `MakeLine` de `QuestTracker.cs`).
- « Use Portal Stone » = **case à cocher fonctionnelle** : cochée,
  le clic sur un objectif envoie **NCID 160 MissionGoto**
  `[s16 mid][s16 type][s16 npcId][s16 flag=1]` (serveur prêt :
  `ZoneEntryFlow.PortalStone.cs`, consomme une Portal Stone
  `0x9c54/0xa476`, téléporte) ; décochée = auto-follow actuel.
- « Background » = case qui affiche ou masque le fond du tracker.
- Boutons « Track Quests » / « Unacquired Quests » : basculer la liste
  (quêtes suivies / quêtes non prises), suivi via NCID 186
  `SetMissionTrace`.

### P3.3 Minimap : boutons et icônes
Mapping vérifié dans `Radar.imageset`, à corriger dans
`MinimapPanel.cs` :

| Bouton (layout) | Image | Fonction réelle |
|---|---|---|
| `BattleBtn` | `MapNormal` (épée rose/rouge) | **Arena** (3v3, 10v10, monster arena) |
| `TerritoryBtn` | `TerritoryNormal` (épée violette) | **Battlefields** (GvG, territory war…) |
| bouton ajouté « Events » | `BONAN` (sac + flèches) | **Tri automatique de l'inventaire** (serveur : `ArrangeItem` → `ClientArrangeItemOK`) |
| `FtyBtn` | `LtyNormal` (bleu, boule) | **Autels Star Diamond / Eden Crystal** |
| `InGameBtn` | `IGLtyNormal` (rose, boule) | **Autel Eden Gem** |
| `ALBtn` | `ALNormal` (sac orange) | à confirmer (Item Mall / Loyalty ?) |
| bouton ajouté « Arena » | `ColNormal` (coffre) | à confirmer (Events / Daily ?) |

Confirmer les deux dernières lignes dans `re/` (chaînes du client
Reforged) ; chaque bouton ouvre une vraie fenêtre (autels =
fenêtres à créer, cf. P4). Marqueurs PNJ/mobs de la minimap :
icônes par fonction (P4.6).

### P3.4 Échap ferme les panels
**Constat** : `SettingsApplier.Dispatch("eden_system")` ne connaît que
6 fenêtres (`HudHasOpenWindow`) ; les fenêtres montées par
`HudExtensions` (Mail, Social, Guild, Shop…) ne sont pas fermées.
**Actions** : registre central des fenêtres (chaque fenêtre créée par
`EeSkin.MakeWindow` ou une fenêtre native s'y inscrit) ; Échap ferme
la plus haute, ou toutes, puis ouvre le Main Menu si rien n'est ouvert.

### P3.5 Thèmes fonctionnels pour tout le HUD et tous les panels
**Constat** : `UiTextureCache.SetTheme` ne remplace que les atlas ;
les couleurs `EeSkin` et les styles codés sont figés.
**Actions** : `ThemePalette` (jetons : corps, bandeau, liseré,
parchemin, texte, accent, boutons…) par thème (Irongray, Blue,
Coffee, Green, Purple, Red) ; `EeSkin` ne fabrique plus de styles mais
**partage des instances de StyleBox par rôle**. Changer de thème
modifie ces instances en place (aucune reconstruction, aucun lag) et
remplace les atlas. Aucune couleur en dur hors palette (vérification
par grep dans `scripts/hud`).
**Validation** : changer de thème dans Settings → tout le HUD et
toutes les fenêtres ouvertes changent dans la frame (capture par
thème).

### P3.6 Contours des fenêtres
**Constat** : `EeSkin.MakeWindow` = liseré `Window*` modulé + fond
plat ; rendu jugé moche.
**Actions** : reproduire le cadre de la ref au pixel (bandeau titre
sombre en dégradé à coins arrondis, liseré argenté de 2 px, coins
ornés) à partir des pièces natives (`DejaVu/FrameWindow` +
`Titlebar*` du looknfeel), en comparant crop ref et capture côte à
côte ; idem encadrés internes (onglets, listes, boutons « ? »).

---

## P4 — Panels

### P4.1 Inventaire (refonte fonctionnelle)
- **Taille** : fenêtre plus compacte ; **lignes et colonnes
  configurables** (Settings > UI ou poignée de redimensionnement),
  défilement **case par case** (molette = une ligne).
- **Slots utilisables vs verrouillés** : capacité = sac de base
  (32, cf. serveur `GetBagCapacity ?? 32`) + capacité des sacs
  équipés (`BagCapacity` de l'item, serveur `InventoryHandlers`
  l.1255-1300) ; cases au-delà = « lock » (visuel grisé, refus de
  dépôt).
- **Barre de fonctions** avec icônes : Archive, Fortify, Fusion,
  Craft racial, Backpack manager, devenir marchand (Stall), Special
  currency, Tri auto (`ArrangeItem`). Icônes natives à retrouver :
  imagesets (`Stall*`, `SpecialCoin*`, `AlpacaCoin`, `Bag*` existent)
  + comparaison d'images des icônes de `shot_0017` contre
  `UI/uiicon` (5 558 fichiers) ; le `d3d9.dll` Reforged ne contient
  que l'atlas emojis et des fonds parchemin.
- **Déplacement des objets** dans le sac : vérifier et réparer le
  glisser-déposer (NCID 7 SwitchItemLocation, ack 277) dans la
  nouvelle grille scrollable.
- **Objets → hotbar** : glisser un objet ou un sort sur un slot de la
  hotbar → `UpdateShortcut` (serveur prêt :
  `PlayerHandlers` l.120-136, persistance `SetShortcutAsync`) ; la
  hotbar affiche les **raccourcis serveur** (`EnterNodeShortcut`,
  352, 510) au lieu de l'ordre de la liste des sorts ; touche ou clic
  droit = sort (cast), consommable (ItemUse 19) ou équipement (7+8).

### P4.2 Character Info — refaire entièrement selon `profile.png`
Éléments relevés sur la ref : en-tête (couronne, nom, LV, icône et nom
de classe, LV de classe) ; 4 blocs de stats avec **2 colonnes**
(valeur + %), valeurs **vertes = au-dessus de la base** (492
`BaseAttrRefresh` vs 296) et rouges pour P-ATK / M-ATK ; boutons « ? »
d'aide ; aperçu 3D sur fond de pierre ; à droite de l'aperçu,
**7 slots d'accessoires** (silhouettes + losanges bleus) puis « Gear
Upgrade » / « Gear Swap » ; dessous, armes et bijoux (2 rangées), puis
**sets d'armure par type** : « Cloth Armor (Current Gear) » (losange =
set actif), « Light Armor », « Heavy Armor », 5 pièces chacun ; colonne
de droite = **9 résistances élémentaires** (icônes + valeurs, données
`PlayerStats.Resists`). Onglets Title, Fame et Achievement selon
`profile-Title.png` et `Profile-Achievements*.png` (arbre des
titres, description et méthode d'obtention, « Set Title » ;
compteurs, arbre des catégories, liste, Victory/Defeat Quote).
Décoder l'échelle des taux (ACC %, CRIT %, EVA %…) depuis le natif.

### P4.3 Classe — compléter selon les 7 refs
- Lister **toutes les classes** (débloquées ou non, `ClassTable` /
  `c_class.ini`), cadenas sur les verrouillées, tooltip
  « Class Requirement » en rouge (`HoverToolTipClasse.png`), ordre
  et groupes de la ref (Ranger, Inquisitor, Cleric, Bard, Shaman,
  Sage, Mimic, Warrior…).
- **License Knowledge** (`ClassePanel-ClassTalent&Certificat.png`) :
  arbre « Class Talent » (icônes reliées par flèches, niveaux,
  « N KPs Remain » / « N KPs Entered », Confirm / Refresh), serveur
  prêt : **148 SetAAPoint / 149 SetAARate** ; « Certificates » :
  grille 3×4 de slots fleur-de-lys + liste, Confirm, serveur prêt :
  **181 QueryLicense → 509, 182 PlayerLicense, 183 QueryLicenseSet**.
- **Class News** (`ClassePanel-ClassNews*.png`) : description de la
  classe, « Characteristics » (4 icônes), « Class Stat Bonus » (HP %,
  MP %, STR…), « Weekly Performance », aperçu 3D de la tenue de
  classe + « Set Outfit » (9 slots), « Class Skills » et « Class
  Certificates » en rangées.
- Tooltips de sort complets (`ClassePanel.png`) : coût MP, cooldown,
  effet, niveau suivant, CP requis en rouge.
- Bandeau animé « Switched Class to X » avec dragon
  (`ClassPanel-ChangementDeClasse.png`).

### P4.4 QuestLog
Garder le placement ; corriger l'affichage des **récompenses**
(`MissionObjectives.RewardLines`) : icônes d'objets + quantités, EXP,
or, points (au lieu d'une ligne de texte brute) ; contours et
habillage selon P3.6.

### P4.5 Region Map — icônes des PNJ et entités
**Constat** : marqueurs = carrés de couleur (`RegionMapWindow`).
**Actions** : icônes par fonction de PNJ (General / Weapon / Armor
merchant, Fame shop, Auction, Revival point, Warp portal, Quest NPC,
New quest…) depuis les imagesets carte (`map*.imageset`,
`Bigworld*`), fonction déduite du titre ou du type de PNJ
(`c_npc` / `t_npc`) ; légende « City Features » avec ces icônes ;
mêmes icônes sur la minimap ; marqueurs de quête « ! » / « ? ».

### P4.6 Fenêtres restantes
Community, Guild Recruitment, Mail, Event/Daily, Pet, Item Mall /
Loyalty, Portal, Channel Settings, autels (P3.3) — même méthode.

---

## Récapitulatif de l'ordre d'exécution

1. P0.1 gel au changement de map → P0.3 PNJ en double → P0.4 perfs
   d'entrée → P0.2 timeouts de connexion.
2. P1.1 / P1.2 audit textures et matériaux → P1.3 préchargement des
   modèles → P1.4 écran de chargement.
3. P3.5 système de thèmes + P3.6 cadres (socle de tous les panels) →
   P3.4 Échap → P3.1 EXP → P3.2 tracker → P3.3 minimap.
4. P2 écrans hors-jeu.
5. P4.1 inventaire + hotbar → P4.2 Character Info → P4.3 Classe →
   P4.4 QuestLog → P4.5 icônes carte → P4.6.
