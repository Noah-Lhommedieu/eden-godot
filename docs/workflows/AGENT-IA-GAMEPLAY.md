# Agent IA Gameplay — jouer au client Godot comme un humain

But : un agent IA doit **voir** (captures), **bouger** (marche, caméra),
**cliquer**, **glisser**, **taper** et **lire les logs** pour vérifier le
jeu en conditions réelles, sans simulation headless. Tout passe par un
seul client graphique sous `flock /tmp/godot.lock` et un fichier de
commandes lu en continu.

## 1. Lancer

```bash
# serveur (PG/Redis déjà up) : recompiler puis (re)lancer les services natifs
cd eden_server/server && dotnet build Eden.sln && cd .. && ./start-native.sh
# client (build d'abord : tools/build.sh) ; --cmd-file = canal de commandes
: > /tmp/gm.cmd
nohup tools/run.sh -- --login=test123:test123 --channel --char \
  --cmd-file=/tmp/gm.cmd > /tmp/client.log 2>&1 &
# attendre le jeu : « [gmfile] hb sess=ok zone=True »
```

Options utiles : `--look-log` (logs d'apparence/arme `[weapon-attach]`),
`EDEN_MOBMOVE_DIAG=1` (client ET serveur : `[mobmove]`). Tuer le client :
`kill` du pid dont la ligne de commande contient `engine/current/godot`
(ne jamais `pkill -f` : le motif matche le shell qui l'exécute).

## 2. Canal de commandes (`/tmp/gm.cmd`)

Une commande par ligne, ajoutée avec `echo "…" >> /tmp/gm.cmd` ; le
client lit toutes les 0,5 s. Les lignes `//…` sont des commandes GM
serveur ; `@…` sont des commandes du client (`GmFileProbe.cs`). Laisser
~2-3 s entre deux commandes qui changent d'écran.

### Voir
| Commande | Effet |
|---|---|
| `@shot /tmp/x.png` | capture du viewport du jeu SEUL (1600×900), jamais le bureau |
| `@burst /tmp/pre N MS` | N captures espacées de MS ms → `/tmp/pre_000.png`… (mini-vidéo : mouvement des mobs, anim, caméra) |
| `@cjk étiquette` | liste tous les textes chinois/japonais visibles (audit i18n) → `[cjk]` dans le log |
| `@nodepos fragment` | centre écran du 1er Control visible dont le nom contient le fragment |
| `@under x y` | chemin du Control sous le curseur (diagnostic drops/clics interceptés) |
| `@cellpos slot [bag]` | centre écran de la case de sac |
| `@inv` | liste le contenu des sacs dans le log (`[gmfile] inv bag= slot= item=`) |

Les coordonnées de `@click`/`@drag`/`@under`/`@nodepos` sont celles du
viewport = pixels de `@shot`. (Le bureau, lui, n'est pas utilisable :
ne pas utiliser `spectacle`/`import` qui capturent les autres fenêtres
de l'utilisateur.)

### Bouger / caméra
| Commande | Effet |
|---|---|
| `@hold ACTION SEC` | maintient une action d'input : `move_forward`, `move_back`, `move_left`, `move_right`, `move_run` (comme une touche) |
| `@walk x z` | marche point-and-click vers (x,z) Eden, A* sur la grille .smp |
| `@walkx x z` | marche directe sans A* |
| `@cam YAW PITCH DIST` | caméra orbitale du jeu (degrés ; pitch -20…85 ; dist 3…18, collision caméra active) — pas de vue aérienne |
| `@cam2 YAW PITCH DIST H` | caméra LIBRE (collision coupée, distance illimitée, nameplate masqué) : vue aérienne = `@cam2 0 -60 45 4` (**pitch NÉGATIF = regarde vers le bas**, dist 45 ; yaw 0/90/180/270 pour faire le tour) |
| `@key NOM` | injecte une touche (ex. `G` guilde, `Escape`) |
| `//goto N` | téléporte au node N (donjons : `//goto 717`) ; `//goto X Z` = téléport dans la carte |
| `//god` | bascule l'invincibilité (le 2e appel l'enlève) |
| `@revive 1` | « Revenir ici » (0 = point de sauvegarde) |

### Cliquer / glisser / UI
| Commande | Effet |
|---|---|
| `@click x y` / `@rclick` / `@dclick` | clic réel (viewport) |
| `@drag x1 y1 x2 y2` | drag GUI réel (sert pour les slots qui acceptent le drag) |
| **Sac natif = « objet en main »** | clic gauche sur l'objet du sac (`InventoryWindow.Held`) PUIS clic gauche sur le slot cible : le drag n'est pas le modèle du sac |
| `@open CLÉ` | ouvre une fenêtre (`guild`, `familybuilding`, `townlist`, `familystorage`, `territory`, `inventory`, `shop`, `itemmall`, `trade`, `vending`…) |
| `@tab NOM|N` | sélectionne un onglet (titre contenant NOM) |
| `@closewin FRAG` | masque la 1re fenêtre dont le nom contient FRAG |
| `@act N 0 0` | action de fonction (13 fortif, 16 craft racial, 101 cube…) |
| `@talk ID`, `@dlg N` | parler à un PNJ, choisir l'option N du dialogue |
| `@work ID bag slot`, `@workclaim N` | lancer / récupérer un ordre de craft (contourne l'UI) |
| `@str combine tb ts mb ms cb cs` | fortification (paquet 119) |
| `@type TEXTE`, `@use`, `@drop`, `@buy`, `@sell`… | actions inventaire/boutique |

### Monde / tricher pour aller vite
`//give ITEM N`, `//npc ID` (fait apparaître un PNJ à côté), `//gain_gold N`,
`//level N`, `//cc N` (classe), `//open_hall`, `//set_family_level N`,
`//crusade …`, `//team` (équipe solo), `//teleport`. Les persos de test :
`test123:test123` (ClericQA, lv90, guilde QAForge #5001) et
`racetest:test123`.

## 3. Lire ce qui se passe

* `/tmp/client.log` : `[gmfile]`, `[craft]`, `[vdg-panel]`, `[mobmove]`,
  `[mobskin]`, `[pick]`, `[fog]`, `[weapon-attach]`, `[cjk]`, `[i18n-miss]`,
  `[wire] D/E NCID=…` (paquets).
* `/tmp/zone.log` : serveur zone (`TextCommand`, `Instance resolve`,
  `[FamilyHall]`, `[VDG-Work]`, `[mobmove] srv`).
* `[i18n-miss]` = texte EN sans traduction FR ; `@cjk` = texte chinois.

## 4. Recettes

* **Entrer en ville de guilde** : `@revive 1`, `@key G`, `@click 664 274`
  (« Ville guilde ») → node 502 ; la fenêtre G affiche « Quitter ville ».
* **Faire le tour d'un lieu** : `@cam 0 28 14` puis `@shot`, répéter avec
  yaw 90/180/270 ; vue d'ensemble `@cam yaw 55 18` (si la caméra entre dans
  un toit, baisser le pitch).
* **Suivre un mob qui poursuit** : `//goto 717`, `//goto 155 56`, `//god`,
  `@hold move_back 3` + `@burst /tmp/chase 20 150`.
* **Craft par l'UI** : `//give` formule+matériaux, `//npc 50354`,
  `@talk 50354`, `@dlg 0`, `@open inventory`, `@click` (formule dans le sac),
  `@click` (slot « Objet à utiliser »), `@click` (Commander).
* **Mort** : en donjon haut niveau le perso lv90 meurt vite : `//god`,
  puis `@revive 1`.

## 5. Pièges rencontrés

* Un 2e `//god` désactive l'invincibilité.
* Les fenêtres natives de guilde s'empilent si Échap ne les ferme pas :
  `@closewin FRAG` ou relancer le client.
* Le sac natif n'est pas un drag : le dépôt dans un slot de craft se fait
  par clic « objet en main ».
* Les traductions : `ui_text_fr.tsv` (clé chinoise → FR) garde la PREMIÈRE
  entrée d'une clé ; corriger l'entrée existante, pas ajouter un doublon.
* La position est persistée : après `//goto 717` un relog revient en donjon ;
  repartir avec `//goto 1` (Aven).
* Lancer une capture du bureau capture les fenêtres de l'utilisateur :
  utiliser UNIQUEMENT `@shot`.
