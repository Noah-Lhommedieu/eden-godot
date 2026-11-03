# Plan d'action v4 — stabilité, gameplay, rendu, écrans, HUD, panels

Remplace le plan v3 (`2026-10-27-plan-action-v3-stabilite-ui.md`).
Réécrit le 27/10/2026 après le test utilisateur :

- **Validés par l'utilisateur** : P1.1 (textures de map) et P1.4
  (écran de chargement alpaga + humain). Rien d'autre.
- **Rouverts** : tous les autres points du v3, y compris ceux cochés
  ✅ avec un commit (P0.1 `c54a004`, P0.2 `ed6a736`, P0.3/P0.4
  `a41b223`, P1.2 `117f922`/`10206c4`, P1.3 `f792028`, P2.1 `8fcef2a`).
  Les commits restent dans l'historique ; ils servent de point de
  départ, pas de preuve.
- **Ajoutés** : 12 nouveaux problèmes remontés au test (classés plus
  bas, importance « Moyenne » ou « Basse » sauf mention).

## Règles

- Sources : `hud-interfaces.md` (fonctionnel, prioritaire) >
  `ref-shots/` (visuel) > `REF-SHOTS-SPEC.md` (indicatif). La barre
  ULTIMATE reste cachée (feature post-portage).
- **Une case n'est cochée que lorsque l'utilisateur la valide.** Les
  preuves de l'agent (captures `--shot`, flags de diagnostic) ne
  suffisent plus : chaque point fournit un **scénario de test à
  rejouer dans le flux réel** (login → canal → personnage → jeu →
  changement de map), écrit dans la section « Test ».
- Un seul client Godot à la fois. Petits commits par sujet (auteur
  Noah Lhommedieu, trailer Devin, cf. `AGENTS.md`).
- Les logs d'erreur du client (`ERROR:` / exceptions) sont un critère
  de refus : un scénario validé ne doit produire aucune exception.

## Niveaux d'importance

| Niveau | Sens |
|---|---|
| **Critique** | Bloque le jeu (crash, gel, exception en boucle) ou bloque tout test. |
| **Haute** | Gameplay de base cassé ou gênant à chaque minute de jeu. |
| **Moyenne** | Fonction attendue absente ou fausse, contournable. |
| **Basse** | Finition visuelle, confort, chantier long sans blocage. |

## Ordre d'exécution

1. Critique : C1 → C2 → C3 → C4 → C5
2. Haute : H1 → H2 → H3 → H4 → H5 → H6 → H7 → H8 → H9 → H10 → H11 → H12 → H13
3. Moyenne : M1 → M16
4. Basse : B1 → B6

---

## CRITIQUE

### C1 — Exceptions en boucle au changement de map (`erreur.txt`)
**Constat** : après un changement de map, 11 exceptions identiques :
`ObjectDisposedException: Cannot access a disposed object 'Godot.Node3D'`
dans `MapLoader.TryGetGodotHeight` (l.178) ← délégué de
`GameScene.HookPlayerGround` (l.3305) ← `PlayerController.UpdateJump`
(l.805) ← `_PhysicsProcess`.
**Cause** : `GameScene.MigrateMapAsync` (l.3009-) libère l'ancien
`MapLoader` (`_map?.QueueFree()`) puis attend le chargement de la
nouvelle map **sur plusieurs frames** (`await Task.Run(LoadMap)`), mais
`_player.GroundHeight` pointe toujours sur l'ancien loader : la physique
du joueur l'interroge à chaque frame jusqu'à `HookPlayerGround()` en fin
de migration.
**Actions**
1. Au début de la migration : `_player.GroundHeight = null`, gel de la
   physique et des entrées du joueur (`SetPhysicsProcess(false)`)
   jusqu'à `RepositionPlayer`.
2. Auditer tous les autres consommateurs de `_map` / `MapLoader`
   pendant la migration (snap des entités, clic-sol, minimap,
   AutoRoute, caméra, herbe) : garde `IsInstanceValid` + accès
   uniquement via `GameScene` (jamais capturé dans une closure).
3. Vérifier la libération des ressources de l'ancienne map
   (RSS avant/après 10 migrations).
**Test** : 10 allers-retours Aven ↔ Limestone Mtn. (portail et
`//transport_node`) → zéro `ERROR:` dans la console, pas de gel,
joueur posé au sol à l'arrivée.

### C2 — Gel / crash au changement de map (v3 P0.1, rouvert)
**État** : `c54a004` a rendu la migration asynchrone sous l'overlay de
chargement, mais le scénario réel n'est pas validé (cf. C1).
**Actions** : après C1, rejouer les migrations dans le flux réel ;
si un gel subsiste, capturer les piles (`dotnet-stack report -p
<pid>`, `gdb -p`) ; watchdog qui logue un dump si une frame dépasse
5 s ; rejouer la file des paquets différés et réarmer HUD, minimap,
Region Map, tracker, streaming des entités.
**Test** : identique à C1, plus combat sur Limestone Mtn. après la
migration (`--mob-test --combat-test` va jusqu'au bout).

### C3 — Crash si la connexion prend trop de temps (v3 P0.2, rouvert)
**État** : `ed6a736` (keepalive 45 s, `EDEN_TICKET_TTL_SEC` = 300 s,
retour login propre) non validé.
**Actions** : rejouer le scénario réel, lire les logs serveur
(`CloseReason`, tickets purgés) pour chaque écran (login, canal,
personnage, chargement) ; corriger chaque cause trouvée.
**Test** : attendre 2 min puis 10 min sur chaque écran, puis entrer en
jeu → pas de crash ; une vraie déconnexion affiche un message et
ramène à l'écran de connexion.

### C4 — PNJ en double (v3 P0.3, rouvert)
**État** : `a41b223` (`MapLoader.SpawnSceneNpcs=false` en live) non
validé.
**Actions** : compter en jeu les corps PNJ (`Npc_*` + entités kind 8)
par `npc_id` sur S001, S002 et après une migration ; vérifier aussi les
nameplates et les marqueurs minimap.
**Test** : place d'Aven et port : un seul modèle et un seul nom par
PNJ, avant et après un aller-retour de map.

### C5 — Performances à l'arrivée en jeu (v3 P0.4, rouvert)
**État** : `a41b223` (rayon ~80 m, budget ms/frame) et ~90 fps mesurés
par l'agent ; non validé en conditions réelles.
**Actions** : mesurer avec `--perf` dans le flux réel (login → jeu) et
après une migration ; tableau des 30 premières secondes dans
`docs/PERF.md` ; traiter chaque pic (> 33 ms) : préchargement sous
l'écran de chargement, textures en format GPU, préchauffe des shaders,
pas de GC bloquant en jeu.
**Test** : ≥ 60 fps dès la première seconde sur Aven en 1280×720,
ressenti fluide (pas de saccade de 10 s).

---

## HAUTE

### H1 — Le curseur disparaît et revient au centre de l'écran (nouveau)
**Cause** : `PlayerController.UpdateMouseMode` (l.440) passe en
`MouseModeEnum.Captured` dès qu'un clic gauche/droit devient un drag
(ou au clic molette) : Godot cache le curseur et le recentre au retour
en `Visible`.
**Actions** : orbiter la caméra sans capturer (ou capturer puis
`Input.WarpMouse(_pressPos)` au relâchement) ; un simple clic
(gauche ou droit) ne change jamais le mode souris ; seuil de drag
respecté.
**Test** : clic gauche, clic droit, glisser gauche, glisser droit,
clic molette au centre et ailleurs : le curseur reste visible à sa
position (ou y revient au relâchement du drag).

### H2 — La molette fait défiler plusieurs éléments à la fois (nouveau)
**Cause** : le zoom caméra est traité dans
`PlayerController._UnhandledInput` (l.372) ; les zones du HUD qui ne
consomment pas la molette (lignes du tracker, listes en
`MouseFilter.Pass`/`Ignore`) la laissent passer à la caméra.
**Actions** : tout conteneur défilant du HUD (`ScrollContainer`,
`ItemList`, `Tree`, chat) consomme la molette ; la caméra ne zoome que
si aucun `Control` du HUD n'est survolé
(`GuiGetHoveredControl() == null`).
**Test** : molette sur le tracker, le chat, chaque liste de panel →
seule la liste défile ; molette sur le monde → seule la caméra zoome.

### H3 — Fenêtres difficiles à fermer et à déplacer (nouveau)
**Constat** : croix de 12 px (`EeSkin.MakeWindow`), drag par le fond
qui entre en conflit avec le contenu, fenêtres natives sans barre de
titre fiable, positions persistées qui peuvent sortir de l'écran.
**Actions** : barre de titre draggable sur toute sa largeur pour
toutes les fenêtres, zone cliquable de la croix ≥ 20 px, drag réservé
à la barre de titre, clamp dans l'écran à l'ouverture et au
redimensionnement, z-order (la fenêtre cliquée passe devant).
**Test** : ouvrir/déplacer/fermer chaque fenêtre (sac, profil, classe,
quêtes, carte, settings, menu, mail, social…) à la souris, du premier
coup.

### H4 — Échap ferme les panels (v3 P3.4, non fait)
**Constat** : `SettingsApplier.Dispatch("eden_system")` ne connaît
qu'une liste fixe de fenêtres.
**Actions** : registre central des fenêtres (inscription automatique
par `EeSkin.MakeWindow` et par les fenêtres natives) ; Échap ferme la
fenêtre au premier plan ; quand plus rien n'est ouvert, il ouvre le
Main Menu.
**Test** : ouvrir 3 fenêtres quelconques → 3 Échap les ferment une à
une → le 4e ouvre le Main Menu → le 5e le ferme.

### H5 — Le marqueur de ciblage (pour npc, mobs...) n'a pas le bon model d'animation de ciblage et les entités peuvent clique droit alors qu'elle sont super loin (nouveau)

**Test** : cibler successivement 3 mobs et 2 PNJ par clic et par Tab
→ le cercle et le cadre de cible suivent toujours la bonne entité.

### H6 — Le cadre de cible affiche de faux PV (nouveau)
**Pistes** : `TargetFrame.SetVitals` alimenté par 444 puis 280
(attrId 0) ; confusion possible handle/charId, PV max du registre vs
PV max réel du mob (`c_monster`), mise à jour sur le mauvais handle.
**Actions** : tracer 444/280/298 pour la cible et comparer aux PV
réels côté serveur ; une seule source de vérité pour PV courant/max.
**Test** : frapper un mob → la barre baisse du montant des dégâts
affichés et atteint 0 à sa mort ; cibler un joueur → ses vrais PV.

### H7 — Mobs invisibles dans Aven (nouveau)
**Constat** : `--mob-test` sur S001 se déplace vers des spawns de
monstres (`#61276 M086`, `#61273 M001`, `#21071 M001`) : la table de
scène d'Aven contient des monstres, le serveur les fait apparaître et
le client ne les affiche pas.
**Actions** : côté serveur, ne pas faire apparaître de monstres sur
les maps pacifiques (drapeau de node/ville `t_node`/`c_node`, ou
liste explicite) sauf événement ; côté client, aucun mob invisible :
modèle manquant = repli visible + log.
**Test** : se promener dans tout Aven → aucun mob ni entité invisible
(ciblage Tab, minimap, collisions) ; Limestone Mtn. garde ses mobs.

### H8 — Barres d'EXP perso et classe fonctionnelles (v3 P3.1, non fait)
**Données** : `S_Level.ini` côté serveur
(`ProgressionParsers.ParseLevels` : col 1 = EXP du niveau, col 10 =
EXP de classe ; `Combat.Rewards` l.263 commente col 11 → trancher en
lisant `ExpForLevel` et le natif) ; `C_Level.ini` client, même format.
**Actions** : table de niveaux client calquée sur le serveur ; ratio =
EXP courante (600) ÷ EXP du niveau ; EXP de classe = attr 0x06 ÷ seuil
du niveau de classe ; `XX.XXX %` (précision au clic droit), vert pomme
et violet ; mise à jour à chaque gain.
**Test** : tuer un mob → les deux barres avancent du bon ratio ;
montée de niveau → retour proche de 0 %.

### H9 — Modèle du joueur chargé en retard (v3 P1.3, rouvert)
**État** : `f792028` (cache des corps + préchargement de la liste) non
validé.
**Actions** : rejouer sélection de personnage, changement de
personnage, équipement d'une tenue et changement de skin en jeu ;
mesurer le délai d'affichage ; éliminer chaque reconstruction de corps
restante.
**Test** : chaque personnage de la liste s'affiche immédiatement ;
équiper une tenue met à jour le visuel instantanément.

### H10 — Parties de modèles noires (v3 P1.2, rouvert)
**État** : `117f922`/`10206c4` (helpers « Nub » masqués) non validés.
**Actions** : planche de rendu de tous les modèles de personnages
(5 races × 2 genres × tenues de base et équipements courants),
montures et monstres ; audit automatique des matériaux noirs ou sans
texture ; correction conversion / choix de texture.
**Diagnostic (28/10)** : `--black-audit` (CharVisual.AuditBlack) sur le
joueur ne signale qu'une surface sombre : `#B202` (`material_46`,
texture dyée DARK) — composite `DyeMeshSurfaces` avec masque
`B202AS01B` (3 canaux R/V/B) + artwork `A202AS01A`. Les épaulières
noires viennent de ce composite ; piste : zones masque ≈ noir hors des
3 canaux + artwork noir.
**Test** : l'utilisateur passe en revue la planche et ses propres
personnages en jeu : aucune zone noire.

### H11 — Système de coordonnées cassé (nouveau)
**Constat** : dans la Region Map le point du joueur est à un endroit
faux ; dans la minimap du HUD la position est encore éloignée de la
réalité (marqueurs, flèche, coordonnées `X:, Y:`).
**Pistes** : `WorldSize` et l'origine de la texture `<code>s.png`
(`MinimapPanel.UpdateView`), conversion `WorldConv.ToEdenGround`,
bornes réelles de chaque map (pas une taille unique), axe Y inversé,
échelle différente entre minimap et Region Map, décalage de la
migration (`SetSelfPos` avant `RepositionPlayer`).
**Actions** : une seule fonction monde → carte normalisée, utilisée par
minimap, Region Map, marqueurs PNJ/portails ; bornes par map tirées des
données ; vérification avec 3 points connus par map.
**Test** : se placer sur un PNJ/portail repérable sur la carte : le
point joueur est dessus (minimap et Region Map), sur 3 maps.

### H12 — Panels / HUD et curseur sur un NPC (nouveau, plus tard)
**Constat** : les panels et le HUD passent « derrière le curseur » quand le
curseur est dirigé vers un NPC (le NPC prend le clic / le curseur NPC
s'affiche à travers une fenêtre).
**Pistes** : `PlayerController.PickEntity` ne vérifie pas
`GuiGetHoveredControl()` ; le curseur contextuel (`--cursor-test`)
est posé par picking monde sans tenir compte du HUD.
**Actions** : priorité absolue au HUD : si un `Control` du HUD est
survolé, aucun picking monde (ni curseur NPC, ni sélection, ni clic-sol).
**Test** : ouvrir un panel devant un NPC → clic et survol sur le panel
n'ont aucun effet sur le NPC.

### H13 — NPC sélectionnable trop loin (nouveau, plus tard)
**Constat** : un NPC très éloigné peut être sélectionné / ciblé.
**Actions** : distance maximale de sélection (rayon visible du streaming,
~ 40 u à confirmer) dans `PickEntity` et le ciblage Tab ; au-delà : pas de
curseur NPC, pas de cadre de cible.
**Test** : un NPC hors rayon n'est ni cliquable ni ciblable ; il l'est
en s'approchant.

---

## MOYENNE

### M1 — Socle visuel : thèmes fonctionnels (v3 P3.5, non fait)
`ThemePalette` (jetons corps, bandeau, liseré, parchemin, texte,
accent, boutons) pour Irongray, Blue, Coffee, Green, Purple, Red ;
instances de StyleBox partagées par rôle dans `EeSkin`, modifiées en
place au changement de thème (pas de reconstruction) ; atlas remplacés
en même temps ; aucune couleur en dur hors palette dans `scripts/hud`.
**Test** : changer de thème dans Settings → HUD et fenêtres ouvertes
changent dans la frame, sans saccade, pour chaque thème.

### M2 — Contours des fenêtres (v3 P3.6, non fait)
Reproduire au pixel le cadre de la ref (bandeau titre sombre en
dégradé, coins arrondis, liseré argenté 2 px, coins ornés) depuis
`DejaVu/FrameWindow` + `Titlebar*`, et les encadrés internes
(onglets, listes, boutons « ? »). Comparaison crop ref / capture
côte à côte pour chaque type d'encadré.

### M3 — Inventaire et hotbar (v3 P4.1, non fait)
- Fenêtre plus compacte ; lignes et colonnes configurables ;
  défilement case par case.
- Cases utilisables vs verrouillées : sac de base (32, serveur
  `GetBagCapacity ?? 32`) + capacité des sacs équipés (`BagCapacity`,
  `InventoryHandlers` l.1255-1300).
- Barre de fonctions avec icônes : Archive, Fortify, Fusion, Craft
  racial, Backpack manager, Stall (devenir marchand), Special
  currency, tri auto (`ArrangeItem` → `ClientArrangeItemOK`). Icônes
  à retrouver par comparaison d'images de `shot_0017` contre
  `UI/uiicon` (+ `Stall*`, `SpecialCoin*`, `AlpacaCoin`, `Bag*`
  existants dans les imagesets).
- Déplacer avec soit un drag&drop soit 2 clique (1 click drag 1 clique drop, a config selon préférences dans le système parameter Chr) les objets dans le sac (NCID 7 / ack 277) — réparer.
- Glisser objets et sorts vers la hotbar → `UpdateShortcut` (serveur
  prêt, `PlayerHandlers` l.120-136) ; la hotbar affiche les raccourcis
  serveur (`EnterNodeShortcut`, 352, 510) ; touche ou clic droit =
  sort, consommable (19) ou équipement (7+8).
**Test** : ranger, déplacer, poser un objet en hotbar, l'utiliser par
sa touche, se reconnecter → raccourci conservé.

### M4 — Character Info refait selon `profile.png` (v3 P4.2, non fait)
En-tête (couronne, nom, LV, icône et nom de classe, LV de classe) ;
4 blocs de stats en 2 colonnes (valeur + %), vert = au-dessus de la
base (492 vs 296), rouge pour P-ATK / M-ATK, boutons « ? » ; aperçu 3D
sur fond de pierre ; 7 slots d'accessoires (silhouettes + losanges
bleus), « Gear Upgrade » / « Gear Swap » ; armes et bijoux (2
rangées) ; sets « Cloth Armor (Current Gear) » / « Light Armor » /
« Heavy Armor » (5 pièces) ; colonne des 9 résistances
élémentaires ; onglets Title, Fame, Achievement selon
`profile-Title.png` et `Profile-Achievements*.png`. Décoder l'échelle
des % (ACC, CRIT, EVA…) depuis le natif.

### M5 — Classe complète selon les 7 refs (v3 P4.3, non fait)
Toutes les classes (verrouillées comprises, `c_class.ini`) avec
cadenas et tooltip « Class Requirement » ; License Knowledge = arbre
« Class Talent » (148/149) + « Certificates » (181/182/183 → 509/489),
serveur prêt ; Class News (description, Characteristics, Class Stat
Bonus, Weekly Performance, aperçu de tenue + Set Outfit, rangées
Class Skills / Class Certificates) ; tooltips de sort complets ;
bandeau « Switched Class to X ».

### M6 — Quest tracker (v3 P3.2, non fait)
Contours de ligne et surbrillance au survol ; « Use Portal Stone »
cochée = clic objectif → NCID 160 MissionGoto `[mid][type][npcId]
[flag=1]` (serveur prêt) ; décochée = auto-follow ; « Background »
affiche / masque le fond ; « Track Quests » / « Unacquired Quests »
changent la liste (suivi NCID 186).

### M7 — Minimap : icônes, boutons et zoom (v3 P3.3 + nouveau)
- Zoom +/− **ne fonctionne pas** : vérifier `ZoomInBtn`/`ZoomOutBtn`
  (`MinimapPanel.WireButton` → `Zoom`) et l'application du facteur
  dans `UpdateView` ; ajouter la molette sur la minimap (sans zoomer la
  caméra, cf. H2).
- Contenu faux (comme la Region Map) : même fond, même échelle et mêmes
  icônes que M8.
- Boutons, mapping vérifié dans `Radar.imageset` :

| Bouton | Image | Fonction |
|---|---|---|
| `BattleBtn` | `MapNormal` (épée rose/rouge) | Arena (3v3, 10v10, monster arena) |
| `TerritoryBtn` | `TerritoryNormal` (épée violette) | Battlefields (GvG, territory war…) |
| ajouté « Events » | `BONAN` (sac + flèches) | Tri automatique de l'inventaire |
| `FtyBtn` | `LtyNormal` (bleu, boule) | Autels Star Diamond / Eden Crystal |
| `InGameBtn` | `IGLtyNormal` (rose, boule) | Autel Eden Gem |
| `ALBtn` | `ALNormal` (sac orange) | à confirmer dans `re/` |
| ajouté « Arena » | `ColNormal` (coffre) | à confirmer dans `re/` |

Chaque bouton ouvre une vraie fenêtre (autels à créer).

### M8 — Region Map : icônes des PNJ et entités (v3 P4.5 + retour)
Les marqueurs sont faux ou absents : icônes par fonction de PNJ
(marchands, Fame shop, Auction, Revival point, Warp portal, Quest NPC,
New quest…) depuis les imagesets carte (`map*.imageset`,
`Bigworld*`), fonction déduite de `c_npc`/`t_npc` ; légende « City
Features » avec ces icônes ; vue zoomée sur la ville comme la ref ;
marqueurs de quête « ! » / « ? ».

### M9 — QuestLog : récompenses (v3 P4.4, non fait)
Récompenses en icônes d'objets + quantités, EXP, or, points (au lieu
de `MissionObjectives.RewardLines` en texte brut) ; habillage M2.

### M10 — PNJ trop hauts ou trop bas (nouveau)
**Pistes** : `EntityRegistry` place l'entité via
`GroundPos(m.X, m.Y, m.FieldZ)` ; selon les maps, hauteur serveur vs
hauteur du sol, PNJ posés sur des plateformes / ponts (sol
« walkable » vs heightfield), origine du modèle.
**Actions** : snap au sol réel (raycast sur collisions) sauf PNJ
volants ; contrôle sur S001, S002 et 3 autres maps.
**Test** : tous les PNJ d'Aven et de Limestone ont les pieds au sol.

### M11 — Armes mal positionnées (nouveau)
**Constat** : les armes ne tombent pas vers le milieu-bas comme dans le
client d'origine.
**Pistes** : `CharVisual.ApplyEquipment` (armes GLB sur `Bip01 Prop1`
/ `Prop2`) : rotation et offset de l'os d'attache, conversion Z-up.
**Test** : planche des types d'armes (épée, bâton, arc, dague, bouclier)
au repos et en course, comparée au client d'origine.

### M12 — TopLeftVitals : rectangles trait héroïque et classe actuelle (nouveau)
Les deux mini-rectangles du bandeau (icône orange = trait héroïque,
icône bleue = classe actuelle, avec leurs niveaux) manquent : retrouver
les images (assignées par code chez le natif — chercher dans `re/`
les noms d'images de `CharIcon`/`classIcon`), puis les afficher avec
les vraies valeurs.

### M13 — Écrans connexion et canal (v3 P2.2, non fait)
Layouts natifs `Login.layout` et `CWorldServerListWnd.layout`, fidèles
à `Selection_channel.png` (emblème cristal, listes parchemin,
`Ch01(Recommended)` en rouge, `Ch02 (Maintenance)` grisé, barre ◀●▶,
zone de message, Confirm/Cancel, Official Website/Quit, trademark).

### M14 — Sélection de personnage (v3 P2.3, non fait)
Layout natif `SelectCharacterWnd.layout` : carte du perso en haut à
droite, flèches de rotation et « Start Game » en bas au centre, pile
Set password / Remove password / Select Server / Logout, sans panneau
Preview ni onglets de personnages.

### M15 — Fond de l'écran de connexion qui change (v3 P2.1, rouvert)
**État** : `8fcef2a` (fondu splash → temple 3D) non validé.
**Test** : lancer le jeu 3 fois → aucun changement brusque de fond.

### M16 — Fenêtres restantes (v3 P4.6)
Community, Guild Recruitment, Mail, Event/Daily, Pet, Item Mall /
Loyalty, Portal, Channel Settings, autels (M7) — même méthode.

---

## BASSE

### B1 — Système d'ombres et de lumière (nouveau)
Ombres portées dynamiques (soleil `DirectionalLight3D`, cascades
réglées par preset), ombres de contact pour entités (remplacer ou
compléter `BlobShadow`), éclairage par map (couleur/intensité du ciel,
ambiance jour, intérieurs), lumières ponctuelles des lanternes et
cristaux émissifs, réglage dans Settings (Shadows Off/Low/High).
Contrainte : rester ≥ 60 fps (C5).

### B2 — Portrait des vitals trop zoomé (nouveau)
Caméra du portrait (`TopLeftVitals.FramePortrait` : +0,62 u devant
l'os tête) trop proche : reculer et recadrer pour avoir front → menton
dans le cercle, comme `TopLeftVitals.png`.

### B3 — Portrait sans yeux (constaté au lot HUD)
Visage sans traits dans le portrait : texture de visage / yeux non
résolue sur le corps dupliqué ; à vérifier avec H10.

### B4 — Icônes niveau / classe des barres d'EXP
Mêmes icônes que M12, à gauche des deux barres d'EXP
(`DoubleEXPbar.png`).

### B5 — Tooltip des buffs stylé
Titre blanc, description rouge, comme `shot_0045` ; même style pour
tous les tooltips du HUD.

### B6 — Chevauchement barre du familier / castbar
La PetBar (-180..-135) recouvre la castbar (-170..-152) quand un
familier est invoqué pendant un sort : décaler l'une des deux.

---

## Récapitulatif

| Id | Sujet | Importance | Origine | État |
|---|---|---|---|---|
| C1 | Exceptions au changement de map | Critique | `erreur.txt` | corrigé, validé user |
| C2 | Gel au changement de map | Critique | v3 P0.1 | partiel (thread témoin), à valider |
| C3 | Crash connexion lente | Critique | v3 P0.2 | rouvert |
| C4 | PNJ en double | Critique | v3 P0.3 | rouvert |
| C5 | Perfs à l'arrivée en jeu | Critique | v3 P0.4 | mesuré (PERF.md), optimisation à faire |
| H1 | Curseur qui disparaît / recentré | Haute | nouveau | codé, à valider |
| H2 | Molette qui défile plusieurs éléments | Haute | nouveau | codé (caméra), à valider |
| H3 | Fenêtres difficiles à fermer / déplacer | Haute | nouveau | codé, à valider |
| H4 | Échap ferme les panels | Haute | v3 P3.4 | codé, à valider |
| H5 | Marqueur de ciblage | Haute | nouveau | codé (cible locale + cercle à l'échelle), à valider |
| H6 | Faux PV du cadre de cible | Haute | nouveau | codé, à valider |
| H7 | Mobs invisibles dans Aven | Haute | nouveau | serveur : villes sans monstres (redémarrer le serveur), à valider |
| H8 | Barres d'EXP | Haute | v3 P3.1 | codé, à valider |
| H9 | Modèle joueur en retard | Haute | v3 P1.3 | rouvert |
| H10 | Parties noires des modèles | Haute | v3 P1.2 | partiel (épaulières corrigées) |
| H11 | Coordonnées carte / minimap | Haute | nouveau | codé (taille monde par map), à valider |
| H12 | HUD/panels vs curseur sur NPC | Haute | nouveau | à faire (plus tard) |
| H13 | NPC sélectionnable trop loin | Haute | nouveau | à faire (plus tard) |
| M1 | Thèmes | Moyenne | v3 P3.5 | partiel (thème Irongray par défaut), à valider |
| M2 | Contours des fenêtres | Moyenne | v3 P3.6 | partiel (cadre Irongray), à valider |
| M3 | Inventaire + hotbar | Moyenne | v3 P4.1 | codé (fonctions icônes, tri, déplacement, hotbar sorts/objets, lignes/colonnes), à valider |
| M4 | Character Info | Moyenne | v3 P4.2 | refait sur le layout natif EquipmentWnd_new2 (stats 4 blocs, slots réels, sets, résistances), % à décoder, à valider |
| M5 | Classe complète | Moyenne | v3 P4.3 | refait (classe active 280/0x2c, HUD/profil suivent, Class News réel, garde-fou CheckClass serveur), talents/certificats à faire |
| M6 | Quest tracker | Moyenne | v3 P3.2 | codé (portal, bg, onglets, survol), à valider |
| M7 | Minimap (icônes, boutons, zoom) | Moyenne | v3 P3.3 + nouveau | codé (boutons, autels, minimap élargie), à valider |
| M8 | Region Map (icônes) | Moyenne | v3 P4.5 | partiel (PNJ par catégorie c_npc), icônes à faire |
| M9 | QuestLog (récompenses) | Moyenne | v3 P4.4 | codé (QuestLog : arbre par zone, couleurs, Rewards), à valider |
| M10 | PNJ trop hauts / bas | Moyenne | nouveau | codé (snap ≤ 4 m), à valider |
| M11 | Armes mal positionnées | Moyenne | nouveau | à faire |
| M12 | Trait héroïque + classe (vitals) | Moyenne | nouveau | codé (couronne + icône classe), trait héroïque réel à faire |
| M13 | Écrans connexion / canal | Moyenne | v3 P2.2 | partiel (login/canal gris + parchemin), emblème à faire |
| M14 | Sélection de personnage | Moyenne | v3 P2.3 | partiel (boutons, carte grise), fond à faire |
| M15 | Fond de connexion | Moyenne | v3 P2.1 | rouvert |
| M16 | Fenêtres restantes | Moyenne | v3 P4.6 | partiel (ItemMall, Mail, Community, Guilde native, Familier PetWndV3) |
| B1 | Ombres et lumière | Basse | nouveau | partiel (cascades par qualité), à valider |
| B2 | Portrait trop zoomé | Basse | nouveau | codé, à valider |
| B3 | Portrait sans yeux | Basse | lot HUD | codé (visage complet), à valider |
| B4 | Icônes des barres d'EXP | Basse | lot HUD | codé (couronne + icône de classe), à valider |
| B5 | Tooltip des buffs | Basse | lot HUD | codé, à valider |
| B6 | PetBar / castbar | Basse | lot HUD | codé, à valider |
| — | Textures de map (v3 P1.1) | — | v3 | **validé** |
| — | Écran de chargement (v3 P1.4) | — | v3 | **validé** |
