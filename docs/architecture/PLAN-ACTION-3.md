# PLAN D'ACTION 3 — bugs bloquants, couche « contenu », wiki, admin, FR/EN

**Rédigé le 2026-11-07** après la revue du propriétaire. **Remplace le §11 (« Plan d'action par phases »)
et les décisions §1.1 / §12 de `CONCEPT-WIKI-ADMIN.md`** (ce document reste une annexe d'analyse : ses §2, §5, §8, §9
restent valables, sauf là où ce plan le contredit explicitement).
Statut : conception. **Aucun code n'a été modifié pour rédiger ce plan.**

Ce plan est écrit pour une IA qui **ne connaît rien** du projet. Chaque lot dit : le constat, les preuves
(chemins + lignes), où regarder, ce qu'il faut faire, comment prouver. Les constats sont marqués
**[VÉRIFIÉ]** (lu dans le code/les logs/les données ce jour) ou **[HYPOTHÈSE]** (à prouver avant de coder).

---------------------------------------------------------------------------
## 0. Mode d'emploi pour l'IA qui reprend ce chantier
---------------------------------------------------------------------------

### 0.1 Lectures obligatoires, dans cet ordre (≈ 30 min)
1. `AGENTS.md` (règles cardinales : EDENFR pur, `corpus/` `re/` `portage/` en lecture seule, « fait = prouvé »).
2. `docs/workflows/task-contract.md` (remplir le contrat AVANT de coder) et `docs/knowledge/known-bugs.md`.
3. `docs/knowledge/agent-lessons-learned.md` (pièges déjà payés).
4. Ce plan, puis `docs/architecture/CONCEPT-WIKI-ADMIN.md` §2 (existant serveur/client).
5. Selon le lot : `docs/temp-plans/2026-11-06-vdg-ressources.md` + `2026-11-06-vdg-verif.md` +
   `2026-11-07-ui-guilde-craft-inventaire.md` (ville de guilde), `2026-11-05-item-usetime.md` (canal d'usage d'item 407/408),
   `docs/knowledge/craft-guild-systems.md`, `docs/reports/craft-guild-recon-2026-11-03/` (K5 économie, D3 items, G2 protocole).

### 0.2 Pièges qui ont déjà coûté cher
| Piège | Détail |
|---|---|
| **DEUX arbres `eden_server`** | Le serveur qui tourne est `eden-godot/eden_server/` (cwd du process `Eden.Zone`, vérifié par `/proc/<pid>/cwd`). Le dossier **frère** `reverseden/eden_server/` est une copie ancienne : il ne contient plus d'utile que les docs Limitless (le portail lui-même a été **copié** dans `eden-godot/eden_server/limitless/`, versionné). Ne jamais éditer le code serveur dans le frère. Toute recherche de code serveur se fait dans `eden-godot/eden_server/server/src`. |
| Chemins relatifs | Dans ce plan : `SRV` = `eden_server/server/src`, `CLI` = `eden_godot/godot_project/scripts`, `CORPUS` = `corpus/fr-full/data/db` (tables) et `corpus/fr-full/data/scene` (scènes). Racine = `eden-godot/`. |
| Encodages | Tables `c_*.ini` = **Big5**, `t_*.ini` = **CP1252** (sinon « Contrema褾re »). Fins de ligne CRLF. Premier champ = id numérique ; certaines lignes de continuation n'en ont pas. Parser tolérant obligatoire. |
| Moteur | Uniquement `engine/current/godot` (via `tools/env.sh`). Jamais le `godot` du PATH. Un seul client graphique à la fois (`flock /tmp/godot.lock`). |
| Pas de git | Décision du propriétaire : ne pas commiter. Travailler en petits pas réversibles. |
| Logs utiles | `/tmp/client.log`, `/tmp/zone.log`, `/tmp/world.log`, `/tmp/mission.log`. Lancer : `tools/run.sh -- --login=test123:test123 --channel --char`. Les commandes GM se passent par `--cmd-file=/tmp/gm.cmd` (sondes `@click x y`, `@talk`, `@dlg`, `@shot chemin`, `@closewin`… : `CLI/debug/GmFileProbe.cs`) et par le chat (`//hall_setup`, `//npc <id>`, `//level`, `//give_gold`…). |
| `psql` absent du PATH | Chercher le binaire (`locate psql`, conteneur docker `start-docker.sh`) ou passer par `SRV/Eden.Admin` ; ne pas écrire en SQL sans l'avoir retrouvé. |
| Mot de passe en clair | Le dossier frère `reverseden/eden_server/limitless/…/appsettings.json` contient un mot de passe PostgreSQL en clair (la copie du dépôt a `CHANGE_ME_USE_ENV`) ; ne jamais le recopier dans un livrable. |

### 0.3 Définition de « fait » — durcie après les faux positifs
Le propriétaire a constaté que des fonctions déclarées « validées » ne marchaient pas du tout (PNJ de forge, etc.).
Cause racine de ces faux positifs [VÉRIFIÉ] : les validations passaient par les **raccourcis de sonde** (`@talk`, `@dlg N`,
`//npc`, `@open`) qui court-circuitent le chemin réel du joueur (clic sur le PNJ → fenêtre de dialogue → clic sur l'option →
fenêtre de service **visible et au premier plan**). Désormais :

1. **Aucun lot « bug joueur » n'est `TESTED` sans le parcours complet par clics** : `@click` sur le PNJ dans le monde,
   capture du dialogue, `@click` (coordonnées) sur l'option de service, capture de la fenêtre de service, puis une
   **action de bout en bout** (déposer un plan, lancer, récupérer). Les captures sont regardées (pas seulement produites).
2. Une **sonde d'assertion `@wins`** (à écrire au lot A1) liste chaque fenêtre visible : nom, `CanvasLayer.Layer`, index,
   rectangle, « recouverte par » (fenêtre au-dessus qui couvre son centre). Un test échoue si une fenêtre ouverte est
   recouverte à plus de 50 % ou hors écran.
3. **Deux exécutions concluantes** par comportement (règle déjà posée dans `2026-11-06-vdg-verif.md`), sur **les deux villes**.
4. Chaque lot ajoute un test automatisé quand c'est possible (`tools/test.sh`), et note ses preuves dans le journal du lot
   (`docs/temp-plans/2026-11-07-…` pour A, `docs/architecture/` pour le reste).

### 0.4 Exigence de qualité de code (non négociable, décision du propriétaire)
La qualité de code doit être **irréprochable** : le code doit rester maintenable sur des années. Concrètement, tout lot est refusé s'il ne respecte pas :
1. **Une responsabilité par classe/fichier** ; fichier > 400 lignes = à découper (aujourd'hui `GuildWindow.cs` 1 867 l., `CraftWindow.cs` 864 l. : ne pas y ajouter, extraire). Pas de logique de jeu dans une classe d'UI.
2. **Aucun nombre magique ni chaîne de gameplay en dur** : valeur de gameplay = donnée (§3 base de contenu, §8 configuration). Un indice de colonne n'apparaît qu'à **un** endroit (descripteur de table, `Schema/columns/*.json` du concept §4.6).
3. **Une seule implémentation** de chaque règle (plan de craft §4.2, classe de donjon §4.1, tirage de récolte) dans la lib partagée, appelée par serveur, client, wiki et admin.
4. **Tests** : toute règle nouvelle a un test unitaire ; tout bug corrigé a un test de non-régression ; les tests de contenu (comptes, références) sont automatiques.
5. **Pas de `catch {}` muet, pas de repli qui masque une erreur** (règle `AGENTS.md`), journalisation structurée avec un préfixe par module (`[Gather]`, `[Content]`, `[Config]`).
6. **Documentation courte et utile** : un commentaire explique le *pourquoi* (et cite l'adresse natif `@0x…` quand une règle est native) ; un ADR par décision d'interface partagée ; mise à jour de `docs/knowledge/` au même moment que le code.
7. **Revue** : avant de déclarer un lot terminé, relire le diff avec `/code-review` ou `ponytail-review`, supprimer le code mort, régénérer le graphe (`tools/graph/graph.sh`).

---------------------------------------------------------------------------
## 1. Décisions du propriétaire (figées — ne plus les rediscuter)
---------------------------------------------------------------------------

| # | Décision | Conséquence directe |
|---|---|---|
| D1 | **Option A** (service C# + page web) **+** un **panneau complet EN JEU**. Le site web = amélioration du portail **Limitless existant**, pas un nouveau site. **Fait le 2026-11-07 : copie versionnée dans `eden_server/limitless/` (commit `10dc582`, sans push)** ; le dossier frère n'est plus modifié. **Admin = COMPTE admin, pas personnage admin.** | Plus de `admin.grants` par personnage. Le niveau admin vit sur le **compte** (`accounts`, déjà `role='Admin'`) ; tous les personnages d'un compte admin héritent. Lot D. |
| D2 | **Source de vérité = FR.** L'anglais est une version supplémentaire **bien faite**, **dérivée du contenu FR actuel**. | Aucune donnée US/Reforged ne définit le gameplay. `S_*` (US) côté serveur est une dette à migrer vers `c_*` FR (lot B). Lot E. |
| D3 | « Donjon » doit être **défini concrètement**, en distinguant : donjon simple, instance partagée, donjon spécial/variante (Illusion Destructrice…). | §4.1 : règle calculable depuis les données, avec table de classification générée. |
| D4 | Comment repérer le **plan de craft** d'un item. | §4.2 : règle prouvée sur les 3 178 recettes. |
| D5 | Séparer **logique/informations (serveur)** et **visuel (client)** ; supprimer les doubles sources ; chercher hot-reload et suppression de dette technique de l'ancien client. | Lot B. |
| D6 | Traduction EN : **uniquement le contenu présent en jeu, niveau ≤ 90**. Le reste n'est pas traduit. **Prévoir un rollback FR-only.** | Lot E. |
| D7 | **EDENFR = le jeu officiel français complet (≤ Lv90).** Les autres clients étaient des serveurs privés au contenu custom. Donc : tout contenu du jeu est dans `corpus/fr-full` ; une « lacune » est d'abord une lecture incomplète des tables FR, pas un trou à documenter. L'anglais ne sert qu'aux joueurs non francophones. | Interdit d'écrire « absent des données FR » sans avoir remonté la chaîne FR complète. Les tables US (`S_*`) ne sont jamais une preuve d'absence. |
| D8 | **Source d'information = la base de données** (idéal) ; à défaut, uniquement des fichiers externes **modifiables avec sauvegarde à chaque édition**. Le client n'est qu'un afficheur. | §3.7. |
| D9 | **Configuration serveur massive** (temps de craft, respawn par instance, entrées de donjon, événements, **VIP**), éditable à chaud. | Lot F. |
| D10 | Durabilité des outils de récolte : 180 points ≈ 30 min (1 point / 10 s). Animations de récolte : elles existent, il faut les retrouver (§A3). `ui_text_en.tsv` = banque accessoire, jamais dépendance. | A3, E. |

---------------------------------------------------------------------------
## 2. LOT A — Bugs bloquants constatés par le propriétaire (priorité absolue)
---------------------------------------------------------------------------

Ordre conseillé : **A1 → A2 → A5 → A4 → A3** (A1 est la cause probable de A2 et A4 ; A5 prépare les données pour tester A2-A4 ; A3 est le plus gros).

### A1 — Les fenêtres « craft / ville de guilde » passent DERRIÈRE les autres panneaux
**Constat du propriétaire** : le panneau « liste des villes de guilde » s'ouvre derrière tous les autres ; un panneau cliqué/ouvert doit passer devant.
**Pourquoi le correctif précédent n'a rien changé [VÉRIFIÉ]** : le fichier `2026-11-07-ui-guilde-craft-inventaire.md` annonce « CORRIGÉ (`HudWindows.Front`) »,
mais `HudWindows.Front` (`CLI/hud/HudWindows.cs:96`) ne déplace que des nœuds **à l'intérieur d'un même `CanvasLayer`**
(`MoveChild` sur le dernier enfant du layer). Or il existe **deux couches** :
- `scenes/game.tscn:19-20` → nœud `ZoneWindows` = `CanvasLayer` **`layer = 9`**, qui contient `CraftWindow`, `StrengthenWindow`, `LifeSkillWindow`,
  `CombineWindow`, `TownListWindow`, `FamilyBuildingWindow`, `MagicCubeWindow`, `MagicBoxWindow`, `SpecialCoinWindow`, `PaySystemWindow`, `ClassChangeDialog` ;
- `CLI/hud/HudRoot.cs:68` → `Layer = 10` (HUD : guilde, inventaire, dialogue PNJ, chat…) ; `CLI/hud/HudExtensions.cs:82` → `ExtHud` `Layer = 10`.
Un `CanvasLayer` de numéro plus élevé est **toujours** dessiné au-dessus, quel que soit l'ordre des enfants. Donc toutes les fenêtres de la couche 9
sont **structurellement derrière** toutes les fenêtres de la couche 10 (dont la fenêtre de dialogue du PNJ qui vient de les ouvrir).
**Conséquence probable pour A2** : le panneau de forge/ferme/mine/alchimie **s'ouvre** (le client log `[verify] craft : EnterWorks 496 type=16 … — fenêtre CombineWnd`)
mais est **caché derrière** le dialogue et les panneaux HUD → le joueur voit « rien ne s'ouvre ». [HYPOTHÈSE forte, à confirmer par capture avant/après.]

**Travail**
1. Écrire la sonde `@wins` (§0.3) dans `CLI/debug/GmFileProbe.cs`. Capturer l'état AVANT correction (preuve du bug : `CraftWindow` layer 9 derrière le dialogue layer 10).
2. **Un seul conteneur de fenêtres** : reparenter tous les contrôles de `ZoneWindows` dans le même `CanvasLayer` que le HUD (ou, à défaut, faire
   porter à `ZoneWindows` le même `Layer` que `HudRoot` et gérer l'ordre par un seul registre). Le registre `HudWindows` devient l'**unique** gestionnaire d'ordre :
   `Front(w)` s'applique à toute fenêtre inscrite, quel que soit son parent d'origine. Ne plus dépendre de l'ordre des enfants d'un layer.
3. Règle UX demandée : **toute fenêtre ouverte OU cliquée passe devant** (`OnPress` déjà prévu dans `HudWindows.cs:112` — vérifier qu'il couvre les fenêtres reparentées) ; Échap ferme la plus haute.
4. Supprimer les `MoveToFront()` / `MoveChild` ad hoc (`CLI/hud/TownListWindow.cs:109-112`, `FamilyBuildingWindow.cs:175`) une fois le registre unique en place.
5. Test automatisé (NetTests ou test de scène) : « aucune fenêtre inscrite n'a un ancêtre `CanvasLayer` différent du conteneur unique ».
**Acceptation** : ouvrir successivement Guilde → Villes de guilde → Bâtiments → Coffre : chacune apparaît **devant** les précédentes ; cliquer une fenêtre du fond la remonte ; capture de la séquence + sortie `@wins` jointe. Vérifier aussi `StrengthenWindow` (fortification), `LifeSkillWindow`, `MagicBox/Cube`.

### A2 — AUCUN PNJ de bâtiment de plan/récolte/craft n'ouvre son panneau
**Constat du propriétaire** : forge, alchimiste, fermier… « TOUS les PNJ de bâtiments dédiés au plan de récolte, craft via plan d'item, récolte via pioche, etc. NE MARCHENT PAS », alors qu'ils avaient été déclarés fonctionnels.
**Chaîne réelle** (à connaître pour déboguer) :
`clic PNJ` → client NCID 10 → `SRV/Eden.Zone/ZoneEntryFlow.Dialogue.cs:30 HandleTalkToNpcAsync` → si `npc.FuncId>0` service direct, sinon liste de dialogue via Mission
(`mission 127` → réponse `56` → `285 ClientDisplayDialogView`) → le joueur clique une option (`ReplyDialog`) →
`Dialogue.cs:312` `case 12..17` (mining/planting/summon/alchemy/casting/enchanting) → paquet **496 `ClientEnterWorks`** →
client `CLI/hud/CraftWindow.cs:120 OpenWorks` (`ShowDetail`, l. 518) → fenêtre `collectwnd.layout` (12-14) ou `combinewnd.layout` (15-17).
**Constats [VÉRIFIÉ] (logs 2026-11-07 02:14-02:44)** :
- La chaîne serveur fonctionne : `/tmp/zone.log` → `TalkToNPC npc=53046 -> mission 127`, `56->285 npc=53046 entries=2`; `/tmp/mission.log` → `talks=1 quests=0`.
- Le client reçoit 496 et construit la fenêtre : `/tmp/client.log:1281` `[verify] craft : EnterWorks 496 type=16 npc=53046 bld=12 — fenêtre CombineWnd`.
- La fenêtre construite est **vide par conception native** (« la recette vient de la formule déposée dans UseItemSlot », `CraftWindow.cs:161-165`) et plusieurs libellés manquent
  en FR (`/tmp/client.log` : `[i18n-miss] Matériaux requis :`, `Produit :`, `Déposez une formule dans l'emplacement`, `Objet à utiliser`, `Résultat du travail`).
- La fenêtre est dans la couche 9 (cf. A1) et `ShowDetail` ne fait jamais `HudWindows.Front` (`grep Front CraftWindow.cs` → aucun résultat).
**Hypothèses à départager (dans cet ordre)** : (H1) panneau caché derrière le dialogue/HUD (A1) ; (H2) panneau hors écran ou de taille nulle
(`HudExtensions.CenterNative(_detailWnd, new Vector2(300,330))`) ; (H3) l'option de service n'est pas cliquable/visible dans le dialogue réel (`entries=2` : vérifier que la 2ᵉ entrée est bien « service » et son libellé en FR) ;
(H4) sans guilde/bâtiment actif la fenêtre n'est pas construite côté réel (le propriétaire a une autre guilde que les fixtures) ; (H5) la fenêtre est vide donc perçue comme « ne marche pas ».
**Travail**
1. Rejouer **avec clics réels** (§0.3) sur : fermier 53008, mineur 53018, invocateur 53028, alchimiste 53036, fondeur 53045/53046/53047/53048, artisan (mare) 53074-53078, **dans les deux villes** (A5). Pour chaque PNJ : capture dialogue, capture panneau, `@wins`.
2. Corriger la cause réelle (A1 + éventuellement H2/H3/H4). Appeler `HudWindows.Front` sur ouverture dans `OpenWorks`/`ShowDetail`/`ApplyWorkUpdate`.
3. **Rendre la fenêtre utile sans deviner** : afficher, quand la fenêtre est vide, la liste des **plans compatibles présents dans le sac** (clic = pré-remplir `UseItemSlot`), et un texte d'aide FR « Déposez un plan dans l'emplacement ». Les plans se repèrent par la règle §4.2.
4. Compléter les libellés FR manquants (`[i18n-miss]`) dans `tools/ui_text_overrides.tsv` → `tools/gen_ui_text.py` (voir lot E pour la mécanique).
5. Bout en bout par PNJ : déposer un plan valide → lancer (NCID 170 `WorkId` = **id de l'item plan**, pas l'id de recette) → barre/ordre dans `worklistwnd` → récupération (`workresultwnd`) → l'objet est dans le sac. Tester aussi les refus natifs : niveau de bâtiment insuffisant (texte 0x392), sac plein, plan inadapté.
6. Remplacer le stub `CLI/hud/FeatureDialogWindow.cs` (« Service local » / « Area dialog {0}/{1} », l. 83-86) : tout service sans fenêtre dédiée doit être **listé dans un tableau de couverture** (service type → fenêtre) ; tester qu'aucun service `c_npc` n'aboutit à ce stub (test de couverture, voir lot B5).
**Acceptation** : pour chacun des 15+ PNJ de service des deux villes, le panneau est visible au premier plan et le cycle plan→ordre→récupération réussit ; aucune clé `[i18n-miss]` sur ces panneaux ; test automatisé de couverture « service type → fenêtre ».

### A3 — Outils de récolte (pioche / gants / boule de cristal) : canal, statut, animation, tirage toutes les ~30 s
**Constat du propriétaire** : les outils « ne marchent pas dans les zones ». Comportement voulu :
- à l'activation de l'outil : **chargement (cast)**, puis statut **« En cours de minage »** (ou récolte / divination selon l'outil) ;
- **animation** de minage (pioche), de jardinage (gants), de divination (boule) tant que le statut est actif ;
- **bouger annule** le statut ;
- **toutes les ~30 s** : une ressource **aléatoire parmi celles disponibles**, **quantité aléatoire raisonnée selon la rareté** ;
- ressources d'une **tranche de niveau** déterminée par **le niveau et la rareté de l'outil**.
**État actuel [VÉRIFIÉ]**
- Les outils (`c_item` kind 44, emplacement équipement 32) portent `col20 = 30` (3 s d'usage), `col32 = "501 502;60|61|62"` = nodes autorisés `501 502` (les deux villes de guilde) et type d'aire 60 mine / 61 champ / 62 fissure de mana.
  Ex. 36819 pioche niv. 30 ; 36825 niv. 45 ; 36831 niv. 60 ; 37097 niv. 70 ; 37118 niv. 85 ; 29191-29193 niv. 80 (liste complète : `docs/reports/craft-guild-recon-2026-11-03/D3-items-craft.md` §(a)).
- Côté serveur, l'usage est **instantané après 3 s** : `SRV/Eden.Zone/ZoneEntryFlow.Items.cs:528 case "AttrGainCollectItem"` fait un jet par produit de `c_familycollect` (clé = `CatalystItemId` = l'outil), puis consomme 1 durabilité (`ItemActionHandlers.cs`, correctif du 2026-11-07). La condition d'aire est `ZoneEntryFlow.FamilyCity.cs:58 IsInCollectArea` (event_area GATEWAY `keyId` 60/61/62 + rayon + 2). Le canal 3 s est `ItemUseChannel` (407/408, `2026-11-05-item-usetime.md`).
- Données de tirage : `CORPUS/c_familycollect.ini` — ligne par outil (catalyseur col2) avec produits `{id, nom, qté, taux%}` depuis col14. Ex. ligne 245 (pioche 30 blanche) : produits 36801 (40 %), 36802 (10 %), 36791 (0,2 %) ; ligne 246 (pioche 30 verte) : 50 %, 15 %, 0,4 % ; ligne 254 (pioche 45) : 40/10/3/0,5 %. **La tranche de niveau et la rareté sont donc déjà dans les données** (produits différents par outil, taux plus hauts pour la version verte).
- Aucun statut visible n'est posé aujourd'hui ; aucune animation n'est jouée ; bouger n'annule pas (le break au mouvement du canal d'usage est volontairement **désactivé** : `item-usetime.md` « Limites connues »).
- Les aires 60/61/62 n'existent que dans les scènes de ville de guilde `CORPUS/../scene/s501.ini` `s502.ini` (`[gateway_area]`, keyId 60-62 ; coordonnées dans `vdg-ressources.md` §2).
- Interprétation retenue pour « dans les zones » : **les aires de récolte des villes de guilde** (nodes 501/502). Le design doit rester **piloté par les données** pour qu'ajouter d'autres zones (mines hors ville) = ajouter des lignes (cf. B).
**Écart avec le natif (à assumer, noter en ADR)** : le natif tire une fois, immédiatement après le canal de 3 s. Le comportement demandé (canal continu, tirage toutes les 30 s) est une **règle maison voulue par le propriétaire**. Les tables de produits restent celles du natif.
**Conception**
1. **Machine d'état serveur `GatherSession`** (nouveau fichier `SRV/Eden.Zone/ZoneEntryFlow.Gather.cs`, état dans `EntryState`) : `Idle → Casting (3 s, canal 407 existant) → Gathering (statut posé) → Idle`.
   - Entrée : usage d'un outil kind 44 dans une aire valide (`IsInCollectArea`). Hors aire : message FR clair (aujourd'hui refus **silencieux** : à remplacer ; pas d'id de texte natif connu → créer une entrée de texte serveur FR).
   - Pendant `Gathering` : timer de **30 s** (constante en donnée : `gather.tick_seconds`, défaut 30). À chaque tick : (a) re-vérifier aire/outil/durabilité/sac ; (b) **choisir 1 produit parmi ceux de la ligne `c_familycollect` de l'outil, pondéré par son taux** (rare = rarement) ; (c) **quantité** `1..N` où N dépend de la rareté du produit (taux ≥ 25 % → jusqu'à 3-5 ; 5-25 % → 1-2 ; < 5 % → 1) — table de rareté en donnée, pas en dur ; (d) ajouter au sac (sac plein → **message client** + arrêt propre, aujourd'hui le loot est perdu sans message) ; (e) **durabilité −1 toutes les 10 s de canal** (décision du propriétaire 2026-11-07 : 180 points ≈ 30 min ; valeur `gather.durability_seconds` en configuration, §8) — donc −3 par tick de 30 s ; (f) XP/succès de récolte si la table l'indique.
   - **Annulation** : déplacement (le serveur voit déjà les paquets de mouvement ; `BreakCastAsync` en `MoveSelf` est commenté, `Combat.Mob.cs`/`Movement.cs`), `SpellBreak` (NCID 13, Shift+S), dégâts reçus, mort, migration, déconnexion, changement d'outil, durabilité 0. Ces chemins existent déjà pour le canal d'usage (`CancelItemUseChannelAsync`) : les réutiliser, **ajouter le déplacement**.
   - Quelques secondes de **grâce de mouvement** (tolérance 0,5 u) pour éviter l'annulation par jitter de position.
2. **Statut visible « En cours de minage / de récolte / de divination »** : réutiliser le **système de statuts** (enchants persistants + paquets 299/300 ; `docs/temp-plans/2026-11-03-status-system.md`) → créer 3 entrées de statut dans les données de contenu (icône, nom FR, description), posées/retirées par `GatherSession`. Persistant sans durée fixe, retiré à l'annulation. **Ne pas inventer d'icône** : prendre une icône existante de `ui/` (`itemicon`) ou l'icône de l'outil ; consigner dans `EDENFR-GAPS.md` si aucune ne convient.
3. **Animation** — **elles existent [VÉRIFIÉ]**. `corpus/fr-full/data/db/motion.ini` (table KFM→action, voir `CLI/combat/MotionTable.cs`) contient pour **les 7 races joueurs** (stems `b c f g k l m` = `char/animation/<stem>.kfm`) deux actions de récolte :
   - `collect01` = kf **812**, clés `start 0` → `end 8.9`, son `m1` à 0,534 = `sound/conc01.wav` (boucle courte) ;
   - `collect02` = kf **813**, `end 31.83`, trois sons `m1 0.318`, `m2 12.22`, `m3 21.90` = `sound/conc02.wav` (séquence longue à 3 temps).
   Le client précharge déjà `collect01` (`CLI/game/AnimNames.cs:15`) mais **jamais** `collect02` ni ne la joue (`grep 812|813|collect0` dans `CLI/` → rien d'autre). Seuls 2 clips pour 3 outils : l'association outil → clip est **[HYPOTHÈSE]** : pioche (aire 60) = `collect01` (gestes répétés courts), gants (61) et boule (62) = `collect02` ou l'inverse. **À trancher dans l'ordre** : (a) décompilé : `python3 re/reindex search "0x32c" -b client_game` / `0x32d` (812/813 en hexadécimal) ont donné des faux positifs ; chercher plutôt les appelants du joueur d'action (`reindex callers` sur la fonction qui prend un kfId de `motion.ini`), l'écrit `ZoneClientLifeFloat`/état de canal `LifeEntityDo(char,6)` côté zone (`item-usetime.md`), et les paquets d'action envoyés par `CEC_AttrGainCollectItem` ; (b) test visuel : jouer 812 puis 813 sur chaque race avec l'outil en main (vue diagonale en hauteur) et choisir ce qui correspond à un mouvement de pioche / de cueillette / de divination ; (c) en dernier recours seulement : animation de canalisation générique + son, **et noter l'écart dans `EDENFR-GAPS.md` avec une tâche ouverte** — le propriétaire exige de retrouver les vraies animations (« elles existent à 100 % »). Côté client : précharger `collect01`/`collect02` (`AnimNames.CharWanted`), les rejouer **en boucle** tant que le statut est actif (`CLI/game/EntityAnimator.cs`, `CharVisual.cs`) ; sons `conc01.wav`/`conc02.wav` déclenchés par les clés `m1..m3` (`MotionTable` lit déjà ces clés).
4. **Client** : `CLI/hud/InventoryWindow.cs` (binds 407/408 + `CastBar`) → ajouter l'affichage du statut dans la barre de statuts du HUD (voir le code qui consomme 299/300) et la bascule d'animation. Nouveaux paquets seulement si l'existant ne suffit pas (alors ADR, `docs/knowledge/protocol-map.md`).
5. **Données** : un fichier de contenu `gather_rules` (outil→aire→tick→table de rareté) au lieu de constantes C#, pour que l'admin/éditeur (lots B/D) puisse l'ajuster et que l'on puisse activer d'autres zones.
**Acceptation (par outil : pioche, gants, boule ; blanc + vert ; 2 villes)** : cast 3 s → statut visible + animation → bouger annule (statut retiré, animation arrêtée, outil conservé) → rester immobile 65 s donne **2 tirages**, produits issus de la ligne `c_familycollect` de l'outil (logs `[Gather] tick tool=… produit=… qté=…`) → durabilité décrémentée selon la règle validée → sac plein = message + arrêt. Test unitaire du tirage pondéré (distribution sur 10 000 tirages proche des taux) et de la machine d'état (annulations). Captures : statut, animation (vue diagonale en hauteur, règle `AGENTS.md`), inventaire avant/après.
**Risques** : l'association outil→clip (812/813) est à prouver ; ne pas casser le chemin natif quest-item (`ZoneEntryFlow.QuestItems.cs:31` réutilise `IsInCollectArea`).

### A4 — Le panneau « liste des villes de guilde » n'affiche ni bâtiments actifs, ni PNJ, ni niveaux, ni vendeurs
**Constat** : le panneau ne montre que « Niv.N Nom » (`CLI/hud/TownListWindow.cs:273 FillTowns` : `_list.AddItem($"Niv.{t.Level} {t.Name}")`).
**Données déjà reçues et ignorées [VÉRIFIÉ]** : le paquet 502 (`CLI/protocol/ZoneMiscMessages.cs:3234 ZoneClientFamilyTownQuery`) transporte pour chaque ville
`FamilyId, Level, Name, Leader, Exp, BusinessTax, Members, HouseOpen` et la liste `Buildings` `{Loc, ItemTid, Tid, ItemType, Durability, Level, Exp, DueDate}`.
Le panneau natif (`familytownlistwnd.layout`, `ref-shots/` : voir `eden_godot/docs/client-assets/ref-shots-catalog.md`) a des champs de détail que le code ne remplit pas.
**Ce qui manque côté paquet** : les PNJ réellement posés (contrats) et les vendeurs présents ne sont pas dans le 502. Deux options (à trancher dans le lot, la 1ʳᵉ d'abord) :
1. **Dérivation** : PNJ de service = `FamilyCityRules.StationNpcsFor(type, level)` + housekeeper (`SRV/Eden.Core/Game/Family/FamilyCityRules.cs:56-70`) — fidèle aux règles natives mais n'indique pas les PNJ « non posés ».
2. **Extension de protocole** : nouveau sous-bloc ou nouvelle requête « détail de ville » (ADR) renvoyant les PNJ posés (tid, loc, due_date) ; source serveur : `family_building_container` / `FamilyBuildingItem` (`SRV/Eden.Zone/ZoneEntryFlow.FamilyInstance.cs`, `SpawnFamilyHallNpcsAsync`).
**Travail**
1. Au clic sur une ville : panneau de détail avec **bâtiments (nom FR, type, niveau, durabilité/échéance)**, **PNJ actifs** (nom FR + rôle : vendeur de plans, de composants, housekeeper, quêtes de ville 53006), **vendeurs d'outils** (53199-53201 si posés), **nombre de membres**, **taxe**, **niveau de ville**, état ouvert/fermé. Noms via `TextResolver` (jamais d'ID ni de CJK).
2. Colonnes/tri utiles dans la liste (niveau, nom, nb bâtiments, ville ouverte). Filtres du layout (cases 1-6, bornes de niveau) déjà câblés (`Query()`), vérifier leur effet.
3. Rafraîchir quand un bâtiment/PNJ change (hot-update, voir lot B3).
**Acceptation** : avec les deux villes de A5, la liste montre les deux ; sélectionner l'une affiche exactement ses bâtiments/niveaux/PNJ (comparer avec la base) ; capture du panneau devant les autres fenêtres (A1) ; aucun CJK.

### A5 — Préparer les DEUX villes de guilde (non fait malgré la demande) — fixtures reproductibles
**Constat [VÉRIFIÉ]** : la préparation du 2026-11-07 02:10 (`//hall_setup 5001 reset rebuild` puis `//hall_setup 5000 reset rebuild set2`, `/tmp/zone.log`) est **volatile** : elle repose sur des commandes GM à la main, et le service `Eden.World/TownRecycleService.cs` a déjà **recyclé** les villes pendant les tests (« ATTENTION données test : les essais ont recyclé les guildes 5000/5002/5001 », `vdg-verif.md` Passe 6) ; `world.log` montre `tick 1/72` pour les trois guildes. Conséquences : le propriétaire se connecte et ne trouve pas de ville prête, ou une seule.
**Travail**
1. **Fixture idempotente** `tools/fixtures/vdg-two-towns.sh` (+ code serveur d'appui réutilisant `GmHallSetupHandlers.cs`, `SRV/Eden.Zone/Handlers/`) qui, à partir d'une base vierge ou existante, produit **exactement** :
   - **Ville A** (guilde `QAForge`, id 5001, node 502, niveau 12) : manoir + **ferme, mine, autel, alchimie, fonderie** tous niv. 12, housekeepers + PNJ de service à cap (4 PNJ/bâtiment min.) ;
   - **Ville B** (guilde `livetest`, id 5000, node 501, niveau 12) : **mare magique, hôtel de brique, marché** niv. 12, housekeeper + PNJ de la mare (hôtel/marché : cf. note ci-dessous) ;
   - les deux `house_open` renseignés, **visibles dans la liste des villes**, trésorerie suffisante, 30 membres (fixtures `membre1-29`, voir `craft-guild-systems.md`), le compte `test123` membre/chef de chacune via deux personnages (le propriétaire doit pouvoir entrer dans **chacune**).
2. **Désactiver le recyclage en environnement de test** : variables déjà prévues (`EDEN_TOWN_RECYCLE_FIRST_SEC/TICK_SEC/TICKS/ONLINE`) → les poser dans `start-native.sh` pour `dev`, ou ajouter `EDEN_TOWN_RECYCLE=off`. Vérifier que `TownRecycleService` ne touche pas aux guildes de fixture.
3. **Un jeu d'outils et de plans prêts** dans le sac du personnage de test : pioche/gants/boule (blancs et verts, niveaux 30/45/60/70/85), plans de ferme/mine/autel/alchimie/fonderie/mare (un par niveau de travail 1-12), matériaux, or suffisant, **sac avec ≥ 40 cases libres** (le sac plein a déjà faussé des tests).
4. **Hôtel et marché — PNJ à retrouver, pas à supposer.** Le texte « aucun mapping contrat → PNJ marchand dans les données FR » était **faux dans sa portée** : il venait de la table serveur **US** `S_Item` (18 contrats), alors que le jeu FR est complet (décision du propriétaire : EDENFR = jeu officiel entier ≤ Lv90). Dans `c_item.ini` FR, 352 items sont de kind 42 ; 15 portent un PNJ en col35, dont **`27332-27340` « Marchand Agricole célèbre / de Plantes évoluées / de Minéraux célèbres / de Minerai évolué / d'Invocations célèbres / originales / Fille mystérieuse » → PNJ `53625-53633`** : candidats sérieux pour le **marché (type 10)**. Les autres contrats passent par la chaîne `c_buliding.ini` (136 lignes, col6 = item d'assignation) → `c_item` → `c_npc`, que le serveur actuel lit via `S_Buliding`/`S_Item`. Travail : reconstruire la table « bâtiment (type, niveau) → contrats → PNJ » **depuis les seules tables FR** (c'est un cas d'école du lot B2) et en déduire hôtel (type 2) et marché (type 10) ; aucune de ces lacunes ne va dans `EDENFR-GAPS.md` tant que la chaîne FR n'a pas été lue jusqu'au bout.
5. Script de **vérification** `tools/fixtures/vdg-verify.sh` : interroge la base/les logs et imprime un tableau « ville → bâtiments (type, niveau) → PNJ (tid, loc) → ouverte oui/non » ; il doit passer avant chaque session de test.
**Acceptation** : après redémarrage complet du serveur, `vdg-verify.sh` affiche les deux villes conformes ; la liste de villes (A4) les montre ; le propriétaire peut entrer dans l'une puis l'autre ; la fixture rejouée deux fois donne le même état.

---------------------------------------------------------------------------
## 3. LOT B — Séparer logique et visuel, une seule source d'information, hot-reload
---------------------------------------------------------------------------

### 3.1 Principe
> **Serveur = informations (autorité). Client = présentation (visuel).**
> Une *information* est tout ce qui change le gameplay ou un libellé : nom, description, stats, prix, niveau, taux de drop, recettes, tables de récolte, textes FR/EN, règles de zone.
> Un *visuel* est tout ce qui se voit/s'entend sans changer le gameplay : modèles NIF, animations KF/KFM, textures DDS, FX, sons, layouts CEGUI, thèmes.
> **Règle d'or** : une information n'existe qu'à **un seul endroit** (le « bundle de contenu » publié par le serveur) ; le client la reçoit, la met en cache par hash, ne l'édite jamais.
> Le contenu référence les visuels **par clé** (`model="w631"`, `icon="UI012"`, `fx="s52121"`), jamais l'inverse.

### 3.2 Constat de départ [VÉRIFIÉ]
| Doublon / dette | Preuve |
|---|---|
| **Le serveur lit les tables US `S_*`**, le client lit les tables FR `c_*` : drops/PNJ/quêtes réellement appliqués ≠ ceux affichés | `CONCEPT-WIKI-ADMIN.md` §2.1 (comptes de lignes divergents) ; `SRV/Eden.Core/Game/Services/GameDataService.cs:278-476` |
| **Deux copies du domaine pur** : `src/Eden.Core` et `eden_godot/godot_project/scripts/{net,protocol,session,data,vfs,…}` (ex. `SceneTable.cs` en double, correctif KB-21 à porter dans les deux) | `docs/decisions/ADR-0003-core-source-of-truth.md` (statut « proposé », non appliqué) ; `docs/knowledge/known-bugs.md` KB-21 |
| **Deux couches de fenêtres** (`ZoneWindows` 9 / HUD 10) — cause des panneaux cachés (A1) | `game.tscn:19`, `HudRoot.cs:68` |
| **Dualité de langue héritée** (`TextResolver.NativeOnly`, repli « en » par défaut, `lang/fr` partiel) | `CLI/data/TextResolver.cs:289-330` |
| **Stubs de fenêtres** (« Service local », « Area dialog ») | `CLI/hud/FeatureDialogWindow.cs:83` |
| Aucun hot-reload de tables ; listes capturées par référence au boot (`new InventoryService(…, gameData.Items)`) | `SRV/Eden.Zone/Program.cs:95` ; `GameDataService.cs` |
| Mise en place de test par commandes GM manuelles non rejouables | A5 |

### 3.3 Architecture cible
```
 content/                    (racine repo, hors corpus/ ; ÉCRIT par l'admin, versionné)
   base/        tables FR canoniques (c_* / t_* UTF-8 normalisé, générées une fois depuis corpus/fr-full, jamais éditées à la main)
   overlay/     changesets admin compilés (JSONL → lignes) + tombstones
   lang/fr/ lang/en/   textes (t_*), un fichier par table et par langue (UTF-8)
   rules/       gather_rules, dungeon_classes, plan_kinds, service_coverage…  (JSON/TSV, petites règles de gameplay)
   manifest.json  { rev, tables:{nom:{sha256,rows}}, langs:[…] }

 Eden.Content (lib .NET, SANS Godot, ex-Core)   ← UNIQUE implémentation : parsers, snapshot immuable, index, divergence
      ├── serveur : Zone / Mission / World (références un ContentSnapshot courant, jamais une List capturée)
      ├── Eden.Panel / Limitless.Api (wiki + admin)
      └── client Godot (référence la même assembly via ProjectReference — ADR-0003 appliqué)

 Transport : le serveur publie ContentManifest au login ; le client demande les tables dont le hash a changé
             (canal HTTP loopback/CDN ou paquet dédié) ; vérif de signature ; cache disque par hash.
 Visuels   : EPK (modèles/anims/textures/layouts) — versionnés à part ; le contenu ne les embarque jamais.
```
**Décisions techniques**
1. **`ContentSnapshot` immuable** + pointeur atomique `Content.Current` (swap `Interlocked.Exchange`). Les consommateurs lisent `Content.Current.X` **à chaque usage** (pas de liste capturée). Remplace le pattern de `Program.cs:95`.
2. **Migration table par table** de `S_*` vers `c_*` FR (plan P-B de `CONCEPT-WIKI-ADMIN.md` §8.3), chaque table protégée par un test « serveur et client voient la même ligne ». Ordre : `c_store` (déjà FR) → `c_dropitem` → `c_npc` → `c_mission` → `c_itemcombo` → `c_familycollect/combine` (déjà FR) → reste. Un **rapport de divergence** (`tools/content/divergence`) chiffre l'écart avant/après (C et le badge « écart serveur » du wiki en dépendent).
3. **Le client cesse d'embarquer ses propres copies des tables de gameplay** : il charge le bundle reçu (cache par hash) ; hors-ligne (écran de connexion) il utilise le bundle FR de base livré dans l'EPK. Les tables purement visuelles (`motion.ini`, `boundinfo`, layouts) restent des assets.
4. **Hook de fusion unique** : `IniTable.ParseFile` (base + `*.overlay` + tombstones) côté lib partagée (un seul code, plus deux). `WorldParsers.ParseNpcs` lit des octets directement (`WorldParsers.cs:71`) : à faire passer par la même porte (risque R9 du concept).
5. **Fin des constantes de gameplay en dur** : `FamilyCityRules.StationNpcs`, taux de récolte, tick de 30 s, table de rareté, services→fenêtres passent en `content/rules/*` (éditables, hot-reloadables, testables).

### 3.7 Source de vérité = la BASE DE DONNÉES (décision D8, idée du propriétaire « à la Dofus »)
**Idée retenue** : le serveur est l'unique propriétaire des *informations* (noms, stats, prix, recettes, drops, textes, règles) ; le client est un afficheur (comme les fichiers `lang`/`d2o` poussés par le serveur de Dofus). Le client ne contient plus de copie éditable.
**État actuel [VÉRIFIÉ]** : la base PostgreSQL ne contient que l'**état des joueurs** (persos, inventaires, guildes, enchères…, voir `eden_server/Database/*.sql`) ; **toutes** les données de jeu sont des fichiers INI (53 `Load(…)` dans `GameDataService.cs`, lus une fois au boot). Il n'existe aucun schéma de contenu.
**Faisabilité technique [MESURÉ]** : le contenu de gameplay complet fait 46 Mo bruts / **10,9 Mo compressés** (gzip, `corpus/fr-full/data/db`, 141 fichiers) ; `c_item`+`t_item` = 1,5 Mo compressés. Un client qui met le tout en cache disque par hash ne le télécharge **qu'une fois** ; ensuite seuls des **deltas par ligne** circulent (quelques Ko). Les panneaux (archive, wiki, admin) lisent l'**index mémoire local** : zéro aller-retour réseau, donc zéro lag.
**Architecture proposée**
```
 PostgreSQL schéma `content`  ← SOURCE DE VÉRITÉ (le serveur de jeu et l'admin lisent/écrivent ici)
   content.row(table, id, cells jsonb, rev, updated_at)          1 ligne = 1 ligne de table de jeu
   content.text(table, id, field, lang, value, status, rev)      textes FR (vérité) + EN (overlay)
   content.rule(key, value jsonb, rev)                            règles de gameplay (§8)
   content.changelog(rev, actor_account, table, id, before, after, ts, reason)   APPEND-ONLY = sauvegarde de CHAQUE édition
   content.release(n, rev_from, rev_to, sha256, signed_by)        points de restauration nommés
        │  LISTEN/NOTIFY (PostgreSQL) → ContentSnapshot reconstruit/patché (swap atomique, §3.3)
        ▼
 Serveurs Zone/Mission/World : `Content.Current` (mémoire) — plus de lecture de fichiers INI au runtime
        │  ContentDelta{rev, table, id, cells}  (diffusé aux clients connectés) ; ContentManifest{rev, hash par table} au login
        ▼
 Client : cache disque par hash + index mémoire ; panneaux = lecture locale ; hot-reload des panneaux visibles (ILiveBound)
 Visuels : EPK (modèles/anims/layouts) — référencés par clé depuis `content.row`
```
**Pourquoi la base** : transactions (une édition admin est atomique), historique et annulation par ligne (`changelog`), droits par rôle PG, `LISTEN/NOTIFY` gratuit pour le hot-reload, requêtes pour le wiki/admin (jointures « quel PNJ vend l'item X »), une seule vérité même avec plusieurs processus (Zone et Mission chargent chacun leur copie aujourd'hui).
**Garde-fous (obligatoires)**
1. **Import initial reproductible** : `tools/content/seed` lit `corpus/fr-full/data/db` (lecture seule) et remplit `content.*` ; idempotent ; rejouable sur base vierge ; rapport de comptage ligne par ligne (test : `n` lignes du fichier = `n` lignes en base, hash par table).
2. **Export de secours** : `tools/content/export` écrit `content/export/*.jsonl` (déterministe, trié) à chaque release et chaque nuit, **commité dans git** : on peut diff, restaurer et **démarrer sans base** (mode dégradé en lecture seule depuis l'export).
3. **Sauvegarde par édition** : toute écriture passe par une fonction unique `Content.Edit(...)` qui insère le `changelog` dans la **même transaction** ; annulation = changeset inverse ; `pg_dump -n content` quotidien + avant toute action de masse.
4. **Repli « fichiers seulement » (option minimale du propriétaire)** : si la base de contenu n'est pas retenue, la même API `Content.Edit` écrit dans `content/overlay/*.jsonl` avec copie `.bak` horodatée avant chaque écriture et entrée de journal ; le reste de l'architecture (snapshot, deltas, hot-reload) est identique. **Le choix base/fichiers n'est donc qu'un détail du magasin (`IContentStore`)**, jamais du reste du code.
5. **Formats** : cellules typées par descripteur de table (jamais d'indice de colonne dans le code métier) ; textes UTF-8 en base (fin du problème Big5/CP1252).
6. **Perf** : snapshot indexé par id (tableaux/dictionnaires), index inverses construits au swap (item→recettes, mob→drops, PNJ→boutiques) ; objectif mesuré : swap < 200 ms, delta appliqué < 5 ms côté client, panneau de recherche < 50 ms sur 60 000 entités.
**Ce que le client garde** : layouts CEGUI, thèmes, modèles, animations, textures, FX, sons, polices, **préférences utilisateur**. **Ce qu'il perd** : toute table de gameplay éditable (items, mobs, PNJ, quêtes, sorts, recettes, drops, textes de jeu). Il garde un bundle FR de base empaqueté pour l'écran de connexion (avant d'être connecté).
**Risques** : (R-DB1) migration lourde des 53 parsers : se fait table par table derrière `IContentStore` (lot B2) ; (R-DB2) le serveur dépend de la base au démarrage : couvert par l'export de secours (point 2) ; (R-DB3) volume des deltas lors d'une release massive : envoi par lots + compression ; (R-DB4) cohérence client/serveur : `rev` dans le handshake, ADR.

### 3.4 Hot-reload (innovations concrètes, par palier)
| Palier | Quoi | Dépendance | Apport |
|---|---|---|---|
| H0 (dev) | Commande `//reload_content [table]` + surveillance de fichiers (poll 2 s, comme `server.config.txt` : `SRV/Eden.Core/Game/ServerConfig.cs:145`) | B1 | Modifier un drop/une règle sans redémarrer Zone |
| H1 | **Client** : `ContentChanged` → `TextResolver.ResetCaches()` + `UiTextEn.Reset()` (existent déjà pour le changement de langue : `TextResolver.SetLanguage` fait déjà un reset à chaud) + rebind des panneaux visibles via une interface `ILiveBound.OnContentChanged()` | B2 | Mise à jour d'un nom/prix en direct |
| H2 | **Layouts/UI** : rechargement à chaud des `.layout` CEGUI et du thème (touche dev), capture automatique avant/après | — | Itérer l'UI sans relancer le client (supprime des allers-retours de test coûteux) |
| H3 | **Release serveur** : activation d'une release de contenu = swap de snapshot (pas de redémarrage) ; rollback = swap du snapshot précédent | B1 | Mises à jour de contenu sans coupure |
| H4 | **Client joueur** : sync de contenu au login et sur diffusion `ContentRevChanged` (le client demande le delta) | B2 + signature | « Majs sans redémarrer le client » pour toutes les informations ; seuls les visuels (EPK) exigent un redémarrage |
Le code C# du client (scripts Godot) n'est **pas** rechargeable à chaud en jeu packagé : ne pas promettre cela ; la valeur est dans le déplacement des informations vers les données.

### 3.5 Dette technique de l'ancien client à supprimer (avec garde-fou : test avant/après)
1. Deuxième copie du domaine (`scripts/{net,protocol,session,data,vfs}` vs `src/Eden.Core`) → ADR-0003 appliqué : **une** assembly.
2. Double couche `ZoneWindows`/HUD → conteneur unique (A1).
3. `NativeOnly` / repli `en` par défaut / `lang/fr` partiel → langue = paramètre de contenu (lot E).
4. Stubs de fenêtres (`FeatureDialogWindow`) → tableau de couverture + test.
5. Constantes de gameplay dans le code (§3.3.5).
6. Commandes GM comme seul moyen de « préparer » un état de test → fixtures idempotentes (A5).
7. Tables `S_*` US côté serveur → déclassées en référence morte, supprimées du chemin de chargement table par table.
**Pas de suppression sans** : le test qui prouvait l'ancien comportement continue de passer, et une capture avant/après si l'écran change.

### 3.6 Lots B
| Lot | Contenu | Acceptation |
|---|---|---|
| B0 | ADR « source de vérité = base `content` (repli fichiers + sauvegarde) » ; schéma SQL `content.*` ; `IContentStore` (2 implémentations : PostgreSQL, fichiers JSONL) ; `tools/content/seed` + `export` | Seed rejoué 2× = mêmes hashs ; export commité ; table `changelog` remplie par une édition de test ; arrêt de la base → boot depuis l'export |
| B1 | Lib `Eden.Content` + `ContentSnapshot` + `content/` + `manifest.json` ; ADR-0003 appliqué ; ADR « couche contenu » | Serveur et client compilent contre la même assembly ; `tools/test.sh` vert ; 0 `S_*` encore lue pour les tables migrées en B2 |
| B2 | Migration P-B table par table + rapport de divergence + tests « même ligne » | Rapport : divergence → 0 sur chaque table migrée ; gameplay rejoué sur un scénario par table (drop, PNJ, quête, recette) |
| B3 | Hot-reload H0-H1 (+ `ILiveBound`) | Modifier `content/rules/gather_rules` pendant une session : le prochain tick de récolte applique la nouvelle valeur sans redémarrage |
| B4 | Layout/thème hot-reload H2 | Capture avant/après sans relancer le client |
| B5 | Tableau de couverture `service → fenêtre` + suppression des stubs | Test : tout service `c_npc` ouvre une fenêtre dédiée |
| B6 | Sync de contenu client (H4) : manifeste + deltas par ligne + cache par hash + signature ; mesures de perf du point 6 du §3.7 | Un client avec un bundle périmé se met à jour au login et affiche la nouvelle donnée sans redémarrer |

---------------------------------------------------------------------------
## 4. LOT C — Définitions à figer (donjon, plan de craft, « présent en jeu »)
---------------------------------------------------------------------------

### 4.1 Définition concrète de « donjon » (décision D3)
Trois critères **calculables depuis les données** (générés dans `content/rules/dungeon_classes.json` par `tools/content/classify-dungeons`, puis relus à la main) :
- **Type de node** : `CORPUS/c_node.ini` col 3. Comptes mesurés : type 1 = 57 nodes (monde ouvert, mais aussi 212 Labo Vingot et 216 Dimension Dévastée) ; 5 = 237 ; 7 = 29 (tous « Héroïque ») ; 18 = 14 (tous « Illusion Destructrice … DJN multijoueur ») ; 11 = 8 ; 14 = 4 (Tour des Cieux) ; 12 = 5 ; 10 = 4 (arènes) ; 8 = 7 (champs de bataille) ; 13/15/16/17/9/3/4 = cas uniques. **Les significations de 5, 11, 12 sont déduites des noms et des spawns, pas lues dans un document natif** [HYPOTHÈSE à confirmer dans le décompilé `re/reindex` : consommateurs de `NodeDef.Type`, `SRV/Eden.Core/Data/Parsers/GameDataParsers.cs:259`].
- **Politique de réapparition des mobs** : `CORPUS/../scene/sNNN.ini`, section `[reborn_monsters]`, **champ 4 = délai de réapparition en secondes** (ex. boss 3600/10800/21600 ; mobs « rapides » 20/50/90). Mesure faite : Labo Vingot (212) = mobs à **20 s** + boss à 600/10800 s ; Dimension Dévastée (216) = 113 mobs à 20 s, 64 à 50 s, boss 10800 ; Carrière Angor (101) = boss 3600/21600 et aucun mob rapide.
- **Instanciation** : une map est **partagée** (tous les joueurs d'un canal) ou **privée** (groupe/personnage) : nodes de type 5/7/18 sont instanciés par groupe (`groupe -200000020` = solo, `SRV/Eden.Zone/ZoneEntryFlow.Instancing.cs`), le type 1 est partagé.
- **Étiquette de nom FR** : `CORPUS/t_node.ini` porte « (DJN) » / « (DJN multijoueur) » / « (Solo) » / « Héroïque » / « Épreuve » dans le nom (18 nodes de type 5 + 14 de type 18 portent « DJN »). Indice, pas règle (beaucoup de donjons n'ont pas l'étiquette).

**Classification proposée (6 classes — à matérialiser dans la table générée)**
| Classe | Règle | Exemples (vérifiés) |
|---|---|---|
| **D1 Donjon simple** (instance privée de groupe, boss qui ne réapparaissent pas vite) | `type=5`, ≥ 1 mob avec réapparition ≥ 3600 s, **aucun** mob rapide (≤ 30 s) en majorité, nom sans « Illusion/Héroïque/Épreuve/Solo » | Carrière Angor **101** ; 35, 43, 45, 46, 47… (178 nodes type 5 « boss ») |
| **D2 Donjon héroïque** (même map que D1, difficulté/loot supérieurs) | `type=7`, ou nom « Héroïque », partage le **gabarit de map** (`c_node` col 5 minimap `S101M`) avec une D1 | **603** Carrière Angor Héroïque ; 601, 602, 605, 606… (29 nodes) |
| **D3 Épreuve** | nom « Épreuve : … » (même gabarit de map que la D1) | **630** Épreuve : Angor S ; 609 |
| **D4 Illusion Destructrice** (version **Lv90** d'un donjon normal, récompenses Lv90, multijoueur) | `type=18` (655-684) **ou** nom « Illusion Destructrice » ; **solo** : type 5 + « (Solo) » (829-858) | **655** Carrière Angor (multi) ; **829** Carrière Angor (solo) |
| **I1 Instance partagée** (map commune à tous les joueurs, mélange donjon + monde : boss très durs, zones définies, **mobs qui réapparaissent en ≈ 15-30 s**) | map **partagée** (type 1 ou 11) **ET** majorité des mobs avec réapparition ≤ 90 s **ET** ≥ 1 boss ≥ 600 s | **212 Labo Vingot** (162 spawns : 20 s) ; **216 Dimension Dévastée** « DD » (113 × 20 s + boss 10800 s) ; **224 « Bataille Spatiotemporelle » = BTS** (*Battle of Space and Time*, 時空戰役 ; type 1, 425 spawns : 165 × 50 s, 98 × 90 s, 63 × 20 s, 6 boss à 10800 s) ; 797 Ancien Autel (36/50 rapides) |
| **S1 Spécial** | tours, arènes, expéditions, champs de bataille, survie… | Tour des Cieux 280-283 (type 14), Arènes 401-404 (10), Expédition anti-dragon 619 (12), Champs de bataille 301-308 (8) |
**Famille de donjon** : regrouper les variantes par **gabarit de map** (`c_node` col 5/6) : Carrière Angor = {101 D1, 701 solo, 603 D2, 630 D3, 655 D4 multi, 829 D4 solo}. Le wiki affiche la **famille** (lien entre variantes) et l'étiquette « Donjon : <nom> » du propriétaire n'est utilisée que pour D1-D4 ; I1 → « Instance : <nom> » ; S1 → « Mode spécial : <nom> ».
**Règle de départage** (ordre d'évaluation) : S1 (types spéciaux) → D4 (type 18 / « Illusion ») → D3 (« Épreuve ») → D2 (type 7 / « Héroïque ») → I1 (partagé + respawn rapide) → D1 (type 5) → sinon « map ordinaire ».
**BTS** : nom officiel FR présent dans `t_node.ini` = « Bataille Spatiotemporelle » (node 224) ; le wiki et l'interface affichent « BTS (Bataille Spatiotemporelle) » ; en anglais « Battle of Space and Time (BTS) ». Le node voisin **766 « Passage spatiotemporel »** (時空謎境, type 5, 35 spawns à 0 s + 5 boss à 21600 s) est une instance privée liée au même thème : classe D1 à confirmer. **764 « Bataille d'Hélios [Groupe] »** est à relire (nom de bataille, type à vérifier). Les abréviations (BTS, DD) sont des **alias de recherche** déclarés dans `content/rules/dungeon_classes.json`, pas des noms de jeu.
**Cas à relire à la main (écrits dans `curation/dungeons.json`)** : 771 « Dimension du chaos <Groupe> » (type 5 mais gabarit de 216 → instance de groupe d'une I1), 187 « Palais des Bourbes » (type 5, 43/78 spawns rapides → I1 ou D1 ? à trancher), 765 « Sortir des méandres… » (337/355 rapides → I1), 126/129/39/40/41 (type 5 sans spawns : lieux d'événement, **pas** des donjons), 100 « Mine Andrew », 791 « Génération Future <Groupe> », 792/796 (type 11 sans boss rapide). Sources déjà existantes à croiser : `docs/knowledge/donjons-balayage-2026-11-05.md` (balayage de 40 nodes), `docs/temp-plans/2026-11-04-mission-craft.md` §« Donjons » (21 donjons solo/multi validés, trials Evaluate 655-668).
**Acceptation** : la table générée classe 100 % des nodes de type ≠ 1,3,4,9,16 ; chaque classe a ≥ 3 exemples relus par un humain ; un test garantit que 101, 603, 630, 655, 829 sont dans la même famille et que 212 et 216 sont `I1`.

### 4.2 Définition du « plan de craft » d'un item (décision D4)
Règle **prouvée** sur `CORPUS/c_familycombine.ini` (3 178 recettes) et `c_item.ini` :
- Colonnes d'une recette : col0 id, col1 catégorie, col2 niveau de travail, col10 item produit, col13/14 succès/malédiction, **matériaux par groupes de 3 `{item, nom, qté}` depuis col15**.
- **Le plan est le PREMIER matériau (col15)** dont l'item a `c_item` col8 (kind) ∈ **{53 formule d'alchimie, 54 formule de fonderie, 55 formule de mare magique/artisan, 56 formule d'enchantement}**. Mesure : catégories 3/4/5 → **2 569 recettes ont exactement un matériau de ces kinds et il est toujours en 1ʳᵉ position (2 569/2 569)** ; 130 recettes (cat. 3) et 178 (cat. 5) en ont deux (le second est un intermédiaire à garder comme matériau normal) ; catégories 6 (266, fragments de cartes, kind 49) et 7 (20) n'ont **pas** de plan.
- **Plans de récolte** (`c_familycollect.ini`) : le plan est le **catalyseur col2** ; son kind est 50 (ferme/agricole), 51 (mine), 52 (invocation) (catégorie col1 = kind − 0x32, prouvé 2026-11-06, `CraftTable.cs` en-tête).
- Pour repérer « de quel plan vient l'item X » : `produit (col10) → recette → plan = matériau n°0`. Réciproque « que fabrique le plan P » : `matériau n°0 == P`. L'item référent est **cliquable** dans le wiki (W-07).
- Cas limites à tester : un produit fabriqué par plusieurs recettes (plusieurs plans), un plan lui-même fabriqué, recettes sans plan (cat. 6/7), kind 49 « fragments ».
**Acceptation** : test automatisé sur les 3 178 recettes (comptes ci-dessus reproduits) ; le wiki affiche « Craft — plan : [item] » pour 40002 et 5 autres échantillons vérifiés à la main ; la fenêtre de craft du jeu (A2) propose les plans par ce même code (une seule implémentation dans `Eden.Content`).

### 4.3 « Contenu présent en jeu » (périmètre du wiki et de l'anglais)
Un contenu est **présent en jeu** s'il est atteignable par le gameplay normal (≠ présent dans une table). Générer `content/rules/scope.json` par `tools/content/scope` depuis le graphe de sources (lot D2) :
- **Item** : référencé par au moins une source (drop de mob placé dans une scène, boutique de PNJ placé dans une scène, récompense de quête, coffre, recette dont les matériaux sont eux-mêmes atteignables, boutique mall/échange active) **et** niveau requis ≤ 90 (ou, sans niveau requis, dérivé du niveau de sa source la plus basse).
- **Monstre** : placé dans une scène (`[reborn_monsters]`) de niveau ≤ 90 ; **PNJ** : posé dans une scène accessible (hors dev) ; **quête** : niveau ≤ 90 et donneur placé ; **sort/classe** : classes jouables, rang ≤ 90 ; **carte** : node accessible.
- Écarts connus à exclure : ids `Testing ID`/placeholders (`測試用編號`, R15 du concept), items « (sans nom) », 55 icônes d'événement `e0###` absentes (G23), 12 classes éveillées sans nom FR (G24/G25).
Ce périmètre est la **seule** définition utilisée par le wiki, par l'admin (filtre « valide ») et par l'anglais.

---------------------------------------------------------------------------
## 5. LOT D — Wiki, site Limitless amélioré, panneau d'administration par COMPTE
---------------------------------------------------------------------------

### 5.1 Ce qui existe [VÉRIFIÉ] et l'état du portail Limitless
Emplacement : `reverseden/eden_server/limitless/` (**dossier frère**, ~1,6 Go avec `node_modules`) et docs `reverseden/eden_project/docs/limitless/`, `reverseden/eden_server/docs/limitless/`.
- `web/frontend` : Next.js 16 + Tailwind (`app/page.tsx`, `wiki/`, `rankings/`, `dashboard/`, `admin/page.tsx` 694 l., `guides/`, `login`, `register`), i18n FR/EN/PT-BR par cookie (`lib/i18n.ts`), direction visuelle « aetherfall » (`docs/limitless/DESIGN-WEB.md`).
- `web/backend` : `Limitless.Api` (.NET) : contrôleurs Wiki / Admin (news, joueurs, bannissement, commande) / Rankings / Progress / Guides / Talents ; **les données du wiki viennent de JSON exportés par `tools/export_web_data.py`** (`frontend/lib/datasets.ts`, datasets : items, certificates, monsters, missions, spells, classes, achievements, crafts, titles, npcs, maps).
- `db/001_limitless.sql`, `studio/` (Avalonia), `dev-up.sh`, `docker-compose.yml`.
**Problèmes constatés** : (1) base de données `fnaccount`/`fndb1` (anciens noms) avec **mot de passe en clair** dans `backend/appsettings.json` ; (2) le wiki s'appuie sur des exports JSON périmés/hors EDENFR (monde « en EN » selon `DESIGN-WEB.md`) ; (3) pas de graphe de sources item→PNJ/mob/donjon/plan ; (4) l'admin ne gère ni give/remove en direct ni éditeur de données ; (5) il n'est branché ni sur le serveur de jeu actuel (`eden-godot/eden_server`) ni sur la couche contenu (B).

### 5.2 Décisions d'architecture (révisent le concept)
1. **Pas de nouveau service `Eden.Panel` à côté de Limitless.** Le service HTTP est **`Limitless.Api` modernisé**, qui référence la lib `Eden.Content` (B1) et appelle le serveur de jeu par un canal interne loopback signé (HMAC). Un seul site, un seul back-office.
2. **Le panneau en jeu est complet et autonome** : il fonctionne sans le site. Le site apporte la gestion hors connexion. Les deux utilisent **la même couche de commandes** `Eden.Admin.Core` (give, remove, set, bannir, brouillons…) ; aucune logique admin dans l'UI.
   - En jeu : un **RPC admin** sur la connexion zone existante (un couple d'opcodes « AdminRpc » portant du JSON, ADR obligatoire + `docs/knowledge/protocol-map.md`) ; le client n'affiche le panneau que si le serveur a annoncé `admin_level>0` à l'entrée.
   - Web : `Limitless.Api` → endpoint interne Zone/World (HMAC) → mêmes commandes.
3. **Admin par COMPTE** (décision D1). Le niveau est une colonne du compte (`accounts.admin_level` 0-5, migration `eden_server/Database/0xx-admin-account-level.sql`). Aujourd'hui `privilege=5` est copié **à la création du personnage** depuis `accounts.role='Admin'` (`SRV/Eden.Core/Database/Repositories/CharacterRepository.cs:170-172`) : à remplacer par **une dérivation à chaque entrée en zone** (`EntryState.Privilege = f(compte)` à `ZoneEntryFlow.EntryPipeline.cs:486` / `…Install.cs:113`). Révoquer le compte révoque tous ses personnages au prochain login (et immédiatement pour les connectés via le canal interne). Plus de table `admin.grants` par personnage ; l'audit enregistre `compte` + `personnage actif`.
4. Niveaux 1-5 conservés (viewer, support, content, publisher, root) ; 2FA TOTP dès que le site est exposé au-delà de la machine locale ; audit chaîné append-only (concept §9.2) ; écritures sur joueur connecté **jamais en base directe** (Redis chaud, caches World/Zone : concept §9.3 S3/S4).
5. **Localisation du code** : la copie versionnée est dans **`eden_server/limitless/`** (frontend Next.js, backend `Limitless.Api`, studio Avalonia, `db/`). Exclus de la copie et du dépôt (`.gitignore` local) : `node_modules`, `.next`, `bin`, `obj`, `App_Data` (exports wiki JSON ~25 Mo, **régénérés** par le lot D2, plus par `export_web_data.py`). Le mot de passe PostgreSQL en clair a été remplacé par `CHANGE_ME_USE_ENV` dans les 3 fichiers qui le contenaient (`appsettings.json`, `Program.cs`, `Controllers/CharacterController.cs`) : **les chaînes de connexion doivent maintenant venir des variables d'environnement** (lot D1). Premier build à vérifier (`dotnet build`, `npm ci && npm run build`) : non fait.

### 5.3 Fonctions à livrer (reprend les exigences W-* / A-* du concept, révisées)
- **Wiki** : objets (tous, filtre `scope.json`), mobs, PNJ & boutiques, quêtes (chaînes), donjons (familles §4.1, boss, mobs, drops), cartes (PNJ/mobs/portails), classes/sorts, recettes, titres/succès ; recherche plein texte ; fil d'Ariane + « Retour » ; liens inverses ; **moyens d'obtention précis** : PNJ (nom + carte + x,y), mob (nom + carte + niveau + taux), « Donjon : <nom> » (§4.1), plan de craft cliquable (§4.2), **PNJ de ville de guilde en direct** (quelles guildes ont le bâtiment du bon niveau), quête, coffre ; guildes en direct (nom, niveau, membres, emblème, bâtiments+niveaux, PNJ actifs — **jamais** trésorerie, membres nominatifs, messages) ; bascule FR/EN avec repli.
- **Admin (web + jeu)** : give (soi / joueur nommé / connecté aléatoire / tous / guilde) avec dry-run, plafonds, raison, idempotence ; remove ; édition joueur/guilde/mob/quête/donjon ; brouillons → diff → release → rollback ; audit consultable.
- **Panneau en jeu** : fenêtre native Godot (HUD unique, conteneur A1) avec les mêmes écrans ; la lecture du wiki en jeu s'appuie sur le **contenu local** (le client a déjà le bundle, B2/B6) — pas d'appel réseau pour consulter.
- **Désynchronisation** : item créé inconnu du client → empêché par le bundle versionné (`rev` au handshake, ADR) ; give refusé si le client connecté n'a pas la révision.

### 5.4 Lots D
| Lot | Contenu | Dépend | Acceptation |
|---|---|---|---|
| D0 | ADR : service = Limitless.Api ; admin par compte ; RPC admin ; rev de contenu au handshake. Migration `accounts.admin_level` + dérivation du privilège à l'entrée | B1 | Compte révoqué : `//give` refusé au login suivant (test live) |
| D1 | Secrets de Limitless en variables d'environnement (copie déjà dans le dépôt), noms de bases `fnaccount`/`fndb1` → `edenfr` actuelle, `dotnet build` + `npm run build` verts | D0 | Le site démarre contre la base actuelle ; `grep` de secrets = 0 |
| D2 | `SourceGraph` (drop, boutique, quête, coffre, craft/plan, donjon, PNJ de guilde) dans `Eden.Content` + export remplaçant `export_web_data.py` | B1, C | `GET /api/wiki/item/40002/sources` conforme au concept §5.3 ; tests de graphe |
| D3 | Wiki Limitless (pages items/mobs/PNJ/quêtes/donjons/cartes, recherche, fil d'Ariane, liens inverses) | D2 | Parcours item → plan → item référent → Retour restitue la page ; captures |
| D4 | Live guildes (lecture seule, liste blanche) | D1 | 2 villes de A5 visibles avec bâtiments/niveaux/PNJ ; test : champs interdits absents |
| D5 | Commandes admin partagées (`Eden.Admin.Core`) + RPC jeu + endpoints web : give/remove/édition joueur | D0 | Give à un connecté visible dans son sac, idempotent (rejouer = 1 item), audit |
| D6 | Panneau admin en jeu (écrans give/remove/joueurs/données/brouillons/audit) | D5, A1 | Capture de chaque écran ; compte non-admin ne voit rien |
| D7 | Éditeur de données (brouillon → diff → release → rollback) sur la couche contenu | B1, B3, D5 | Créer un item test : visible en jeu sans redémarrage (H1/H3), rollback le retire |
| D8 | Durcissement : 2FA, rate-limit, règle des 4 yeux en prod, sauvegardes avant actions de masse | D5 | Tests des garde-fous |

---------------------------------------------------------------------------
## 6. LOT E — Anglais « bien fait », FR source de vérité, rollback FR-only
---------------------------------------------------------------------------

### 6.1 Principes
- **FR = vérité** (D2). L'EN est un **overlay de langue** `content/lang/en/*` dérivé du FR courant ; supprimer l'EN ne change **aucun octet** du comportement FR.
- **Périmètre (D6)** : seulement `scope.json` (§4.3) : contenu **présent en jeu et ≤ Lv90**. Hors périmètre : non traduit, repli FR.
- **Qualité** : traduire du FR vers l'EN (jamais depuis le chinois ni depuis des tables US). Un **glossaire** de termes de jeu (noms de classes, statuts, bâtiments, ressources) est construit à partir de sources **de référence uniquement** (wiki EN `knowledge/wiki-en/`, dictionnaire UI `eden_godot/godot_project/gamedata/ui_text_en.tsv`, textindex) — lecture seule, **jamais copiées comme données de jeu** — puis figé dans `content/lang/en/glossary.tsv`.
- **Provenance à auditer** : `ui_text_en.tsv` (1 045 lignes) est généré par `tools/gen_ui_text.py` depuis des tables anglaises héritées ; `AGENTS.md` dit « l'anglais est en pause » et EDENFR pur. **Décision du propriétaire : le conserver comme banque d'information accessoire**, jamais comme dépendance, et seulement pour les entrées qui correspondent au contenu FR actuel (filtre automatique : une entrée dont la clé FR n'existe plus ou dont la source n'est pas dans `scope.json` est ignorée). La version anglaise livrée doit être propre : toute chaîne EN est *dérivée du FR courant*.

### 6.2 Ce qu'il y a à traduire (inventaire à produire en premier : `tools/content/lang-inventory`)
| Catégorie | Source FR | Volume indicatif (avant filtre ≤ 90) |
|---|---|---|
| Objets (nom, description, effet d'usage) | `t_item.ini`, `t_itemmall.ini` | 18 794 + 7 461 |
| Monstres, PNJ (titre + nom), cartes (nom + description) | `t_monster`, `t_npc`, `t_node` | 10 923 / 2 486 / 374 |
| Quêtes (titre, dialogues, objectifs) | `t_mission`, `t_dialogue` | 2 392 |
| Sorts, classes, talents, statuts/enchants | `t_spell`, `t_class`, `t_enchant`, `t_equipset` | à mesurer |
| Succès, titres, collections, bâtiments, événements | `t_achievement`, `t_title`, `t_collection`, `t_buliding`, `t_festival`, `t_destinyevent` | à mesurer |
| Textes d'interface (layouts) | `ui_text_*.tsv`, `textindex.ini`, `[i18n-miss]` du client | 1 045 + manques |
| Textes serveur (chat, refus, annonces) | `SendTextIndexAsync`, textes en dur côté serveur | à recenser (`grep` des chaînes françaises/CJK dans `SRV`) |
Les volumes réels sont obtenus **après** application de `scope.json` (≤ Lv90, présent en jeu).

### 6.3 Pipeline
1. **Extraction** : `lang-inventory` produit `to_translate.tsv` {table, id, champ, fr, contexte (type d'objet, niveau, nom lié), statut}. Les codes de mise en forme (`$N$`, `#c…#`, retours ligne) sont protégés par jetons.
2. **Traduction** assistée par IA par lots cohérents (même fiche d'objet/de quête traduite ensemble pour garder les noms identiques), avec glossaire imposé et mémoire de traduction (une chaîne FR identique = une seule traduction).
3. **Contrôles automatiques** : mêmes jetons de formatage ; longueur max pour les champs UI (cas de troncature déjà vus : « Ni », « Guilde propriét… ») ; pas de CJK résiduel ; mêmes noms propres partout ; test de cohérence nom d'item ↔ noms dans descriptions/recettes/quêtes.
4. **Relecture** : colonne `statut` ∈ {auto, relu, validé} ; échantillon relu par un humain par catégorie avant bascule ; les chaînes les plus visibles (UI, classes, 1-30) en priorité.
5. **Livraison** : `content/lang/en/t_*.ini` (UTF-8) + `lang.en.epk` **séparé** (pack optionnel) ; hot-switch à chaud par `TextResolver.SetLanguage` (existe déjà) ; sélection dans les options (`SettingsApplier.cs:146`).
6. **Wiki/Admin** : bascule FR/EN par page avec repli propre (`fallback:true`).

### 6.4 Rollback FR-only (obligatoire)
| Niveau | Mécanisme | Preuve |
|---|---|---|
| Joueur | Option de langue « Français » (défaut) ; si le pack EN est absent ou invalide → FR | Test : retirer `lang.en.epk` → le client démarre en FR sans erreur |
| Client | Drapeau de build/contenu `Languages=fr` (remplace `TextResolver.NativeOnly`) : aucun chargement de `lang/en/*`, `UiTextEn` non initialisé | Test de non-régression : sorties FR byte-identiques avec/sans le pack EN |
| Serveur | `Content.Languages=fr` : textes EN non chargés ; réponses RPC/wiki sans champ EN | Test serveur idem |
| Données | `content/lang/en/` est un dossier **autonome** ; le supprimer ne laisse aucune référence orpheline (tests de références) | `grep -r "lang/en"` hors du chargeur = 0 |
| Release | Release de contenu marquée « avec EN » ; **rollback de release** = retour à la précédente (H3) ; script `tools/content/rollback-fr-only.sh` retire l'EN et republie | Rejouer le script sur une copie : état FR identique au hash d'avant l'EN |
**Règle** : aucune table FR, aucun asset FR, aucun identifiant n'est modifié pour ajouter l'EN (clés ASCII + `lang/en` uniquement).

### 6.5 Lots E
| Lot | Contenu | Acceptation |
|---|---|---|
| E0 | ADR langue (FR vérité, EN overlay, rollback) ; audit de `ui_text_en.tsv` | ADR relu |
| E1 | `scope.json` (§4.3) + `lang-inventory` | Comptes par catégorie chiffrés dans le journal |
| E2 | Glossaire + UI/menus/statuts/classes (visible en priorité) | 0 `[i18n-miss]` sur les fenêtres craft/guilde/récolte en EN et en FR |
| E3 | Objets ≤ 90 présents en jeu | Contrôles auto verts ; échantillon relu |
| E4 | Quêtes, PNJ, monstres, cartes, succès | idem |
| E5 | Textes serveur + wiki + admin | Aucun CJK/FR en dur côté EN |
| E6 | Test et script de rollback FR-only | Preuve §6.4 |

---------------------------------------------------------------------------
## 7. Ordre d'exécution, dépendances, preuves
---------------------------------------------------------------------------
```
Phase 0 (bloquants joueur)   A1 → A2 → A5 → A4 → A3      (indépendant du reste, démarrer ici)
Phase 1 (fondations)         C (définitions) ‖ B1 (Eden.Content, ADR-0003) ‖ D0 (ADR)
Phase 2 (contenu unique)     B2 (migration S_* → c_*) → B3 (hot-reload) → B5 (couverture services)
Phase 3 (wiki + site)        D1 → D2 → D3 → D4
Phase 4 (admin)              D5 → D6 → D7 → D8             (B3/B6 en parallèle)
Phase 5 (anglais)            E0 → E1 → E2 → E3/E4 → E5 → E6   (après B1 ; E2 peut démarrer dès A2)
```
Phase 6 (config + VIP)       F1 → F2 → F3 → F4      (F1 peut démarrer dès la phase 1 ; A3 en dépend pour ses constantes)
**Preuves à fournir par lot** : contrat de tâche rempli ; `tools/build.sh` + `tools/test.sh` verts ; logs/commandes collés ; captures **regardées** (`tools/capture.sh`, `ref-shots/` pour l'UI native) ; régénération du graphe (`tools/graph/graph.sh`) ; entrée dans `docs/knowledge/known-bugs.md` pour tout bug trouvé.

### 7.1 « Où regarder » — carte rapide
| Besoin | Où |
|---|---|
| Fenêtres, ordre d'affichage | `CLI/hud/HudWindows.cs`, `HudRoot.cs`, `HudExtensions.cs`, `scenes/game.tscn` |
| Dialogue PNJ → services | `SRV/Eden.Zone/ZoneEntryFlow.Dialogue.cs` (talk l.30, services l.280-380) ; `Eden.Mission` (liste 56) ; `CORPUS/c_npc.ini` |
| Craft / ordres de travail | `CLI/hud/CraftWindow.cs`, `CombineWindow.cs` ; `SRV/Eden.Zone/ZoneEntryFlow.Work.cs` ; `CLI/data/tables/CraftTable.cs` ; `CORPUS/c_familycollect.ini`, `c_familycombine.ini` |
| Ville de guilde | `SRV/Eden.Zone/ZoneEntryFlow.FamilyInstance.cs`, `ZoneEntryFlow.FamilyCity.cs`, `Handlers/GmHallSetupHandlers.cs`, `Eden.Core/Game/Family/FamilyCityRules.cs`, `Eden.World/TownRecycleService.cs` ; client `CLI/world/FamilyHallBuildings.cs`, `CLI/hud/TownListWindow.cs`, `FamilyBuildingWindow.cs`, `GuildWindow.cs` |
| Outils de récolte | `SRV/Eden.Zone/ZoneEntryFlow.Items.cs:528`, `FamilyCity.cs:58`, `Handlers/ItemActionHandlers.cs` ; items : `CORPUS/c_item.ini` (col20, col32), `docs/reports/craft-guild-recon-2026-11-03/D3-items-craft.md` |
| Canal d'usage 407/408 | `docs/temp-plans/2026-11-05-item-usetime.md` ; `CLI/hud/InventoryWindow.cs` |
| Statuts | `docs/temp-plans/2026-11-03-status-system.md`, `SRV/Eden.Zone/ZoneEntryFlow.Status.cs` |
| Donjons | `CORPUS/c_node.ini`, `t_node.ini`, `scene/sNNN.ini`, `docs/knowledge/donjons-balayage-2026-11-05.md`, `SRV/Eden.Zone/ZoneEntryFlow.Instancing.cs`, `Evaluate.cs` |
| Langue | `CLI/data/TextResolver.cs`, `UiTextEn.cs`, `tools/gen_ui_text.py`, `tools/ui_text_overrides.tsv`, `CLI/settings/SettingsApplier.cs:146` |
| Admin existant | `SRV/Eden.Zone/ZoneEntryFlow.Admin.cs`, `Handlers/GmHandlers.cs`, `SRV/Eden.Admin/`, `SRV/Eden.Core/Game/Services/GmCommandService.cs` |
| Site Limitless | `eden_server/limitless/web/{frontend,backend}` (copie versionnée, commit `10dc582`) ; docs : `reverseden/eden_server/docs/limitless/DESIGN-WEB.md` (dossier frère) |
| Écarts de données connus | `docs/workflows/EDENFR-GAPS.md` (G1-G40) |
| Se repérer dans le code | `tools/graph/graph.sh query "…"` ; `tools/graph/graph.sh portage query "…"` |

### 7.2 Décisions closes (2026-11-07) et points à préciser
**Closes** : durabilité 1 pt / 10 s ; animations à retrouver (collect01/02 trouvées, association à prouver) ; contrats PNJ = à lire dans les tables FR (D7) ; Limitless copié et versionné ; `ui_text_en.tsv` accessoire.
**À préciser avant le lot concerné (propositions, une réponse courte suffit)**
1. **B0 – base ou fichiers ?** Je recommande la base `content` + export git (§3.7). Confirmer. Si oui : la base de contenu partage-t-elle l'instance PostgreSQL `edenfr` (schéma `content`) ?
2. **F – VIP** : paliers (VIP 1-3 ?) ou un seul statut ? Source d'attribution (achat boutique, commande admin, date d'expiration) ? Un VIP expire-t-il (`vip_until`) ? Les bonus VIP s'appliquent-ils aussi aux autres personnages du compte (par compte, cohérent avec l'admin par compte) ?
3. **F – entrées de donjon** : le code fixe aujourd'hui 10 entrées/jour/intensité (`DungeonEntryRules.MaxDailyEntries`, décision antérieure : le wiki FR dit 2 solo / 10 multi). Quelle valeur par défaut visée : 10, ou 2/10 natif ? Les entrées bonus d'événement s'ajoutent-elles au plafond du jour ou donnent-elles des « tickets » reportables ?
4. **F – respawn** : un réglage **par classe** d'instance (I1 : BTS/DD/Vingot) suffit-il, ou veux-tu aussi un réglage **par node** (ex. un boss précis) ?
5. **A3 – association outil→animation** : si le décompilé ne tranche pas, acceptes-tu que je valide visuellement (jouer 812 et 813 sur chaque outil et te montrer les captures) ?
6. **E – priorité de langue** : EN complet des objets d'abord, ou UI/menus d'abord (déjà prévu : UI d'abord) ?
7. **Aide à la restauration** : veux-tu un écran admin « historique d'une entité » (voir/annuler les éditions précédentes, §3.7 point 3) en v1 ? (je le recommande : c'est la contrepartie de l'édition en direct.)
**Ajouts pertinents que je propose (à valider)** : (a) *mode « bac à sable »* : une base `content` clonée pour tester une release avant publication ; (b) *tableau de bord de santé du contenu* (références cassées, items sans nom FR, textes CJK résiduels) généré à chaque release ; (c) *journal d'activité joueur VIP/admin* dans l'audit ; (d) *calendrier d'événements* (début/fin automatiques des événements et entrées bonus) lu dans la configuration plutôt que commutateurs manuels.

### 7.3 Journal de ce plan
- 2026-11-07 (2) : retour du propriétaire. Fait : copie Limitless versionnée (commit `10dc582`, sans push) ; animations `collect01/02` retrouvées dans `motion.ini` ; BTS = node 224 ; contrats PNJ FR à relire (D7) ; ajout §0.4, §3.7, lot F.
- 2026-11-07 : rédaction. Recherches faites : code client/serveur (layers, dialogue, récolte, 502), logs du jour, données `c_node`/`scene`/`c_familycombine`/`c_item`/`c_familycollect`, portail Limitless. **Non vérifié** : la cause exacte de A2 (hypothèses H1-H5 à départager par capture), l'existence d'une animation de minage native, le contenu réel de la base (`psql` introuvable), l'état de la base de test du propriétaire.

---------------------------------------------------------------------------
## 8. LOT F — Configuration serveur massive, événements, entrées de donjon, base VIP
---------------------------------------------------------------------------
**Demande** : dans `server.config.txt`, « énormément de config spéciale » pour éditer rapidement un gameplay entier : temps de cast par type de craft, respawn par instance (BTS, DD, Labo Vingot…), nombre d'entrées max des donjons + événements d'entrées bonus, **système VIP complet** (base seulement : pouvoir modifier des pans entiers du gameplay pour des joueurs VIP : durée de craft, niveau max, etc.).
**État actuel [VÉRIFIÉ]** : `SRV/Eden.Core/Game/ServerConfig.cs` (261 l.) lit `eden_server/server.config.txt` (poll 2 s, rechargement sans redémarrage) mais ne connaît qu'**~15 clés codées dans un `switch`** (`ExpRate`, `DropRate`, `GoldRate`, `CraftSuccessBonusPct`, `MoveSpeedBasePct`, événements Christmas/Halloween…). Les entrées de donjon sont une **constante** (`DungeonEntryRules.MaxDailyEntries = 10`, `DungeonCatalog.cs:64`) ; `Event.*` n'accepte que EXP/Drop/Gold %. Aucune notion de VIP côté joueur (seul `FamilyInstanceCreate [u8 vip]` existe, sans rapport).

### F1 — Registre de configuration typé (remplace le `switch`)
- **Un registre déclaratif** `ConfigKeys` : chaque clé = `{ nom, type (int/double/bool/duration/percent/enum), défaut, min, max, portée, description FR, catégorie }`. Parsing, validation, bornes, documentation et génération d'un `server.config.reference.md` viennent de ce seul registre ; ajouter une clé = **une ligne**, jamais un `case`.
- **Portées en cascade** : `global` → `par classe d'instance` (BTS/DD/Vingot) → `par node` → `par type de craft` → **profil VIP** → événement actif ; résolution unique `Config.Resolve(key, context)` (testée), où le contexte porte `{compte, personnage, node, type de craft…}`.
- **Hot-reload + validation** : une valeur invalide est refusée avec un message précis dans `[ServerConfig]` et l'ancienne valeur est conservée (aujourd'hui : silencieux). Journal des changements de valeur (qui/quand, via l'admin).
- **Stockage** : fichier `server.config.txt` conservé (lisible, éditable), **sauvegardé automatiquement** (`.bak` horodaté) à chaque édition par l'admin ; à terme les règles passent dans `content.rule` (§3.7) avec le même registre. Le fichier doit être modifiable par le panneau admin en jeu et sur le site.
- **Clés à créer (premier jet, à compléter par recherche dans `GameDataService`/`ZoneEntryFlow.*` : tout nombre de gameplay trouvé en dur devient une clé)** :
  - *Craft* : `Craft.<type>.CastSeconds` pour chaque type (mining 12, planting 13, summon 14, alchemy 15, casting 16, enchanting 17, fusion, fortification, sertissage, enchant) ; `Craft.<type>.DurationMultiplier` (sur la durée en minutes de `c_familycollect`/`c_familycombine`) ; `Craft.<type>.SuccessBonusPct` ; `Craft.GoldCostMultiplier` ; `Craft.MaxConcurrentOrders` ; `DEV_WORK_CAP` actuel (`EDEN_DEV_WORK_CAP`) migré ici.
  - *Récolte (A3)* : `Gather.TickSeconds` (30), `Gather.DurabilitySeconds` (10), `Gather.CastSeconds` (3), `Gather.QuantityByRarity.*`, `Gather.MoveToleranceUnits`.
  - *Instances* : `Instance.<classe>.RespawnSeconds` et `Instance.Node<id>.RespawnSeconds` pour BTS (224), DD (216), Labo Vingot (212)… (remplace/override le champ 4 de `[reborn_monsters]` ; le fichier de scène reste la valeur par défaut) ; `Instance.<classe>.BossRespawnSeconds`, `Instance.<classe>.MobHpMultiplier`/`DamageMultiplier`.
  - *Donjons* : `Dungeon.MaxDailyEntries.Solo|Multi|Heroic|Illusion|Trial`, `Dungeon.ResetHour`, `Dungeon.MultiRewardMultiplier` (déjà 1,5 en dur), `Dungeon.Node<id>.MaxDailyEntries`.
  - *Progression* : `Level.Max` (cap de niveau), `ExpRate`/`ClassExpRate`/`WakenClassExpRate` (existants), `DropRate…` (existants), `Gold…`, `Durability.LossMultiplier`, `Death.ExpLossPct`, `Regen.*`.
  - *Ville de guilde* : `Town.RecycleEnabled`, `Town.Recycle.*` (aujourd'hui `EDEN_TOWN_RECYCLE_*` en variables d'environnement), `Town.MaxBuildingLevel`.
  - *Événements* : voir F3.
- **Acceptation** : 60+ clés au registre ; test « clé inconnue / hors bornes → refus + ancienne valeur » ; test de résolution en cascade ; modifier `Craft.alchemy.CastSeconds` pendant une session change le prochain cast sans redémarrage (log + capture de la barre de cast).

### F2 — Entrées de donjon configurables
- Remplacer `MaxDailyEntries` (constante) par `Config.Resolve("Dungeon.MaxDailyEntries.*", {node, classe, compte})`. Compteurs existants : table `game.player_dungeon_entries` (`eden_server/Database/025-dungeon-entries.sql`, `EvaluateRepository.cs:138-156`).
- **Entrées bonus** : modèle « solde d'entrées bonus » par compte et par classe (`dungeon_bonus_entries`), alimenté par (a) un événement (F3), (b) une commande admin, (c) le profil VIP (F4). Consommées **après** le quota du jour ; expiration configurable.
- Le client affiche le quota restant (paquet de statut de donjon existant, `ZoneEntryFlow.Dungeon.cs`) : vérifier que bonus + quota s'additionnent à l'écran.
- **Acceptation** : réglage 3 entrées/jour → 4ᵉ refusée avec message FR ; événement « +2 entrées » → 5 autorisées ; reset à `Dungeon.ResetHour` ; tests unitaires de la règle.

### F3 — Événements configurables (extension du `Event.*` actuel)
- Événement = bloc de configuration **daté** : `Event.<nom>.Start/End` (calendrier automatique), `ExpPct/DropPct/GoldPct` (existants) + `DropItem` (déjà prévu en commentaire) + **`DungeonBonusEntries`**, `CraftDurationPct`, `RespawnPct`, `GatherRatePct`, `ShopDiscountPct`. Plusieurs événements actifs se cumulent selon une règle documentée (additif par défaut, plafonds configurables).
- **Acceptation** : un événement planifié s'active et s'éteint tout seul (horloge simulée en test) ; annonce en jeu (broadcast existant).

### F4 — Base VIP (squelette extensible, pas le système complet)
- **Modèle** : `account.vip` {`account_id`, `tier` (0 = non VIP), `vip_until` (nullable = permanent), `granted_by`, `granted_at`} ; **par compte** (cohérent avec l'admin par compte). Dérivé à chaque entrée en zone dans `EntryState.VipTier` (comme le privilège admin, lot D0) ; rafraîchi à l'expiration.
- **Profils VIP = couche de configuration** : `Vip.<tier>.<clé de F1> = valeur` (ex. `Vip.1.Craft.alchemy.DurationMultiplier = 0.8`, `Vip.2.Level.Max = 95`, `Vip.1.Dungeon.BonusEntries = 2`, `Vip.1.ExpRate = 120`, `Vip.1.Gather.TickSeconds = 25`). Aucun code spécifique « VIP » dans les systèmes de jeu : ils appellent `Config.Resolve(clé, contexte)` et le contexte contient le palier — **c'est ce qui permet de modifier des pans entiers du gameplay sans coder**.
- **Administration** : commande admin `//vip <compte> <palier> [jours]` + écran du panneau (lot D6) + page du site ; historique dans l'audit.
- **Client** : badge/état VIP dans le profil (affichage simple), aucune logique côté client.
- **À ne PAS faire en v1** : boutique VIP, paiement, avantages cosmétiques, calendrier d'expiration automatisé complexe. Seulement la base.
- **Acceptation** : un compte VIP 1 voit un craft durer 20 % de moins qu'un compte normal (deux comptes, même plan, logs `[Config] resolve … vip=1`) ; retrait du VIP → retour au comportement normal au prochain calcul ; test unitaire de la cascade avec VIP.
- **Garde-fous** : `Level.Max` VIP ne dépasse jamais le plafond de données (90) sans clé explicite `Level.AbsoluteMax` ; chaque clé déclare si elle est « VIP-surchargeable » (liste blanche dans le registre).

| Lot | Dépend | Preuves |
|---|---|---|
| F1 | — | Tests registre + capture du cast modifié à chaud |
| F2 | F1, A5 | Test live quota/bonus sur 2 comptes |
| F3 | F1, F2 | Test calendrier + annonce |
| F4 | F1, D0 | Test live 2 comptes (normal/VIP) |
