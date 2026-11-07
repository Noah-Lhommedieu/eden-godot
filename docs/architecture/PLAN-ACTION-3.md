# PLAN D'ACTION 3 — bugs bloquants, couche « contenu », wiki, admin, FR/EN

**Rédigé le 2026-11-07** après la revue du propriétaire. **Remplace le §11 (« Plan d'action par phases »)
et les décisions §1.1 / §12 de `CONCEPT-WIKI-ADMIN.md`** (ce document reste une annexe d'analyse : ses §2, §5, §8, §9
restent valables, sauf là où ce plan le contredit explicitement).
Statut : conception. **Aucun code n'a été modifié pour rédiger ce plan.**

> **EXÉCUTION : lire d'abord `docs/architecture/PLAN-ACTION-3-EXECUTION.md`** (ordre des lots, protocole, fondations Q, parallélisation, baseline `pre-plan3-baseline`). Ce fichier-ci décrit le contenu détaillé de chaque lot.

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
| D11 | **2 bases maximum** : `eden_content` (statique) + `eden_live` (dynamique), 3ᵉ seulement si justifiée ; toutes les tables utiles, aucune morte, **chaque table et colonne commentée**, données lisibles. | §3.7, lot DB. |
| D12 | **Historique d'édition** hautement recommandé : visible, annulable, supprimable (purge consignée). | §3.7. |
| D13 | **Bac à sable** de contenu (si l'espace disque le permet), **tableau de santé** discret et hiérarchisé, **journal d'activité** VIP/admin, **calendrier d'événements « éphéméride »** (style Almanax de Dofus). | §3.7, F5, D8. |
| D14 | **VIP = un simple statut** (VIP ou non) donnant de petits buffs ; entrées de donjon par défaut **10 solo + 10 multi** (modifiables par VIP, `server.config.txt`, événements). | Lot F. |
| D15 | **Types de map hiérarchisés** (normale / donjon / instance / mode spécial / trial) avec **édition complète par map** (mobs, stats, drops, exp, récompenses) ; un donjon classique est **privé** (personnage ou groupe). | §4.3. |
| D16 | **Serveur → client sans lag** : 50 joueurs actifs, dimensionné pour 300, un seul serveur. | §3.8, lot N. |
| D17 | Libération d'espace disque : liste des dossiers de `portage/` supprimables (aucune suppression sans accord). | §9. |
| D10 | Durabilité des outils de récolte : 180 points ≈ 30 min (1 point / 10 s). Animations de récolte : elles existent, il faut les retrouver (§A3). `ui_text_en.tsv` = banque accessoire, jamais dépendance. | A3, E. |

---------------------------------------------------------------------------
## 1bis. LOT Z — Libération d'espace disque — **FAIT le 2026-11-07** (autorisé par le propriétaire : `portage/` n'est plus nécessaire)
**Résultat** : espace libre **29 Go → 82 Go** (disque 95 % → 85 %). **`/home/ster2lune/Bureau/reverseden/portage/` n'existe plus.** Build `tools/build.sh` : 0 erreur, 0 avertissement ; `tools/test.sh` : **NetTests 939 ✔, Eden.Core.Tests 967 ✔, Eden.Tests 1 799 ✔, 0 échec** (après les suppressions).
**Déroulé** (mesures `df` à chaque étape) : `portage/eden_godot/dist` (+17 Go) → `eden_godot/godot-shots` (+8 Go) → `portage/godot-shots` → `eden_godot/godot_project/assets` (+27 Go) → build + tests → suppression du reste de `portage/` (le reste n'avait plus rien d'unique, ou était en **liens physiques** vers `eden-godot` : `corpus/fr-full` et `re` — supprimer ces noms ne touche pas les fichiers d'`eden-godot`, vérifié : `re/index.sqlite` et `corpus/fr-full-pak-2026-10` toujours présents).
**Conservé avant suppression (déplacement instantané, même disque) dans `eden-godot/archive/portage/` — dossier ignoré par git (`/archive/` ajouté à `.gitignore`), 9,6 Go** :
| Élément | Pourquoi gardé |
|---|---|
| `corpus/official` (9,4 Go) | source non-EDENFR potentiellement irremplaçable (jamais utilisée par le jeu : règle `AGENTS.md`) ; remplace la « sauvegarde externe » demandée |
| `corpus/original-fr` (352 Mo, avec `MANIFEST.txt` et `restore.sh`) | fichiers FR d'origine pour un retour arrière du corpus |
| `docs/` (plans de migration, `RAPPORT-CORPUS-OFFICIEL`, `ROLLBACK-CORPUS`, `ANALYSE-DONNEES-PROVENANCE`…), `tools/audit`, `eden_godot/{docs,ini2cs}` | seuls exemplaires |
| `eden_godot/scripts` (ancien code du client 4.7, 5,5 Mo) | référence de lecture ; sa version indexée reste dans `tools/graph/portage/` |
| `ui_textures_themes` (3 Mo), `eden_server_uniques/` (captures réseau `mitm_native_entry.log`, `ItemMallService.cs`, migration `011-drop-fnmember1.sql`) | uniques |
**Correctifs de chemins en dur faits** : `PlayerController.cs` (`ShotsDir` → `eden-godot/artifacts/shots`), `knowledge/tools/{build_index,extract_fr}.py` (→ `eden-godot/knowledge`), `tools/graph/graph.sh` (`portage` : requêtes OK sur le graphe **figé** de `tools/graph/portage/`, la mise à jour affiche un message clair). **Reste** : `tools/legacy-flax/audit/gen_fr_supplements.py:10` (script d'archive, chemin mort, non utilisé), `ReforgedDataLocator.PortageRelative` (chemin de repli devenu inutile, désactivé par `EDEN_REFORGED_DB`) — à nettoyer au lot B (dette).
**Non touché** : `eden-godot/artifacts/` (Z6 : 17,9 Go régénérables, optionnel, pas nécessaire maintenant) et `corpus/fr-full-pak-2026-10` (9 Go, ancienne extraction, même contenu que l'ex-copie de portage ; à garder tant que `tools/` la cite).

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

### 3.7 Source de vérité = BASES DE DONNÉES claires et documentées (décisions D8, D11, D12)
**Idée du propriétaire** : le serveur est l'unique propriétaire des *informations* ; le client est un afficheur (comme Dofus avec ses fichiers de langue). **Réponse du lead dév (7.2.1 n°1) : toutes les tables utiles, aucune table morte, chaque table et chaque colonne commentée, données lisibles par un humain, 2 à 3 bases au maximum.**

**État actuel mesuré [VÉRIFIÉ, conteneur `edengodot-postgres` :5433, 2026-11-07]**
| Constat | Valeur |
|---|---|
| Base utilisée par le serveur | `edenfr` (33 Mo) : schémas `account` 23 tables, `account_archive` 1, `game` 145, `game_archive` 16, `member` 14, `public` 2 = **187 tables** |
| Anciennes bases encore présentes | `edenfr_db` (21 Mo), `edenfr_account` (17 Mo), `edenfr_member` (8 Mo), `edenfr_load` (41 Mo, test de charge) ; sur l'autre conteneur `eden-postgres` :5432 : `fnaccount`, `fndb1`, `fnmember` (utilisées par le Limitless d'origine). Rollback `EDEN_LEGACY_DB=1` dans `start-native.sh` |
| Documentation | **43 tables sur 187** ont un commentaire ; **26 colonnes sur 2 407** |
| Tables mortes | **71 tables sur 187 ne sont référencées par aucun code serveur** et ont 0 ligne (ex. `account.crashlog`, `account.itemmall_old`, `game.*_old`, `game.farm_*`, `game.statistics_*`, `member.oauth2_*`, `member.two_pass`, tout `game_archive.*`). Liste complète : `/tmp/tables.txt` régénérable par `tools/db/inventory` (lot DB-A) |
| Données de jeu en base | **aucune** : les 53 tables de gameplay sont des fichiers INI lus au boot (`GameDataService.cs`) |

**Disposition cible : DEUX bases (+ une troisième seulement si justifiée)**
| Base | Contenu | Nature | Schémas |
|---|---|---|---|
| **`eden_content`** (statique) | Tout ce que les joueurs ne modifient pas : objets, monstres, PNJ, quêtes, sorts, classes, recettes, drops, boutiques, cartes et leurs apparitions (mobs/PNJ), bâtiments, textes FR/EN, règles de gameplay (configuration), éphéméride, profils de map, **journal des éditions** | Écrite par l'admin/éditeur, lue par tout le monde, publiée par révisions | `item`, `creature`, `npc`, `quest`, `skill`, `craft`, `map`, `guild_city`, `text`, `rule`, `calendar`, `history` |
| **`eden_live`** (dynamique) | État vivant : comptes, personnages, inventaires, guildes, villes en cours, enchères, courrier, entrées de donjon, VIP, audit d'administration, classements, sessions web | Écrite en continu par le jeu | `account`, `character`, `inventory`, `guild`, `market`, `social`, `progress`, `admin`, `web` |
| *(3ᵉ, optionnelle)* | **Aucune n'est nécessaire aujourd'hui** : le site Limitless se contente de `eden_live` (schéma `web` : actualités, sessions) et lit `eden_content` en lecture seule. Le **bac à sable** (D13) n'est pas une base permanente : copie temporaire `CREATE DATABASE eden_content_sandbox TEMPLATE eden_content` supprimée après usage | | |
Les anciennes bases (`edenfr`, `edenfr_*`, `fn*`) sont **migrées puis supprimées** (sauvegarde `pg_dump` conservée). Pas de clé étrangère entre les deux bases (décision 7.2.3 n°2 : les contraintes ne sont pas obligatoires, qualité et performance d'abord, **pas de technique complexe pour rien** ; une seule source de données statique en base) : `eden_live` référence `eden_content` par **identifiant métier stable** (ex. `item_id` natif du jeu) et un test de cohérence vérifie les références à chaque release.

**Données claires, pas de relations cryptiques (réponse n°1)**
- **Tables typées par entité**, une colonne par champ **nommée en clair** : `item(item_id, name_fr, item_kind, required_level, quality, buy_price_silver, sell_price_silver, stack_max, icon_key, model_key, …)`. **Fini le `jsonb` opaque** que proposait ma première version : les colonnes viennent des descripteurs de table (`columns/*.json`, concept §4.6) qui remplacent les indices de colonne de `ItemParser`.
- **Chaque ligne se lit seule** : à côté de chaque identifiant de relation, une colonne de **libellé redondante en clair** maintenue par la base (colonne générée/trigger) — ex. `craft_recipe(recipe_id, product_item_id, product_name_fr, plan_item_id, plan_name_fr, work_level, success_percent, …)` ; `monster_drop(monster_id, monster_name_fr, item_id, item_name_fr, drop_percent)`. Les noms de tables et de colonnes sont français-lisibles en anglais technique cohérent (convention unique documentée : `snake_case`, singulier, suffixes `_id`, `_percent`, `_seconds`, `_silver`).
- **Vues de lecture pour l'humain** : `v_item_obtention`, `v_dungeon_overview`, `v_guild_city_status`… (jointures déjà résolues). Un administrateur qui ouvre `psql` ou un outil SQL **comprend ce qu'il lit** sans suivre trois jointures.
- **Documentation obligatoire et vérifiée** : `COMMENT ON TABLE` et `COMMENT ON COLUMN` pour **toutes** les tables et colonnes (rôle, unité, source native, valeurs possibles) ; `tools/db/doc-lint` échoue si un commentaire manque, si une table n'est référencée par aucun code **ni** par une vue/une fonction déclarée, ou si un nom n'est pas conforme. Document généré `docs/reference/DB-SCHEMA.md` à chaque release (dictionnaire de données : table, colonne, type, commentaire, source native).
- **Pas de table morte** : les 71 tables inutilisées sont soit **supprimées** (migration `DROP`, après `pg_dump`), soit **branchées** si la fonctionnalité est voulue (ex. `gm_tool_log` → remplacée par l'audit `admin.audit`). Règle permanente : une table sans lecteur ni écrivain dans le code est refusée par le lint.

**Import et reproductibilité** : `tools/content/seed` lit `corpus/fr-full/data/db` (lecture seule) → `eden_content` ; idempotent ; test « n lignes fichier = n lignes base, hash par table » ; textes en UTF-8 (fin du Big5/CP1252).
**Historique des éditions (réponse n°7, précisée en 7.2.3 n°7 : il ne concerne QUE les modifications faites depuis le panneau admin)** : simple **rappel de tout ce qui a été modifié + bouton « revenir à l'état précédent »** (ex. PV d'un mob changés puis rétablis). Hautement recommandé, annulable, supprimable : `history.change(change_id, entity_type, entity_id, entity_name, field, old_value, new_value, edited_by_account, edited_at, reason, release_id, reverted_by)` écrit dans la **même transaction** que l'édition. L'administrateur peut : **voir** l'historique d'une entité ; **annuler** une modification (écrit une modification inverse, tracée) ; **annuler en lot** (une release, une session, un compte) ; **supprimer** des entrées (purge par ancienneté ou sélection, niveau 5 ; la purge est elle-même consignée dans `admin.audit` et n'empêche pas de restaurer depuis un point de release conservé). Rétention configurable (`History.RetentionDays`).
**Export de secours** : `tools/content/export` écrit `content/export/*.jsonl` (déterministe, trié, **commité**) à chaque release et chaque nuit : démarrage possible sans base (mode lecture seule), diff lisible, restauration. Repli « fichiers » (option minimale) : même API `IContentStore`, écriture dans des fichiers avec `.bak` horodaté ; le reste de l'architecture est identique.
**Ce que le client garde** : layouts, thèmes, modèles, animations, textures, FX, sons, polices, préférences. **Ce qu'il perd** : toute table de gameplay éditable. Il garde un bundle FR de base empaqueté pour l'écran de connexion.
**Risques** : migration des 53 parseurs table par table derrière `IContentStore` (lot B2) ; dépendance au démarrage (couverte par l'export) ; migration de 116 tables vivantes vers `eden_live` (scripts réversibles, test de comptage) ; le Limitless d'origine pointe sur `fn*` (lot D1).

### 3.8 Distribution du contenu et du dynamique « no-lag » (50 joueurs actifs, dimensionné pour 300) — réponse n°9
**Contraintes** : un seul serveur (pas de scaling horizontal), 50 joueurs réels actifs simultanément, marge 300 CCU sans lag. **Base de départ mesurée** : test de charge du 2026-11-01 (`eden_server/docs/DB-4-LOAD-TEST.md`) = 300 sessions bots 60 s, 300/300 entrées réussies, entrée zone 171 ms en moyenne, 2-4 coupures « Broken pipe » **non expliquées** (à investiguer avant tout), saturation PG résolue par `EDEN_DB_POOL_MAX` ; l'envoi est déjà groupé par session (`NetSession.FlushBatchAsync`), la vue est un carré ±48 sur grille spatiale (`Eden.Zone/Spatial/SpatialGrid.cs`), la boucle de zone tourne à 100 ms (`Program.cs:808`).
**Méthode retenue (règles, par ordre d'impact)**
1. **Le contenu statique ne passe jamais par le socket de jeu.** Un point d'entrée HTTP/2 dédié (Kestrel, port séparé, loopback ou LAN) sert des **blocs adressés par hash** (`/content/<table>/<hash>.br`), **pré-compressés en Brotli à la publication**, `Cache-Control: immutable`, servis depuis la mémoire/`sendfile` (zéro CPU par requête). 300 clients × 11 Mo ne se produit **qu'une fois par release** (puis cache disque client) ; le débit est plafonné (jeton par client, ex. 1 Mo/s, priorité basse) pour ne jamais concurrencer le gameplay.
2. **Chargement en deux temps** : *tables critiques* d'abord (objets, textes, sorts, quêtes actives ≈ 4 Mo compressés), le reste en tâche de fond après l'entrée en jeu ; un panneau ouvert sur une table pas encore arrivée affiche un squelette, jamais un blocage.
3. **Deltas** : une modification de contenu = un message `ContentDelta{rev, table, id, cells}` **sérialisé une seule fois** puis envoyé en copie partagée aux N sessions abonnées (pas de sérialisation par client), regroupé par tick de 100 ms, compressé au-delà d'un seuil.
4. **Requêtes dynamiques identiques coalescées** (« single-flight ») : 30 joueurs ouvrant la liste des villes de guilde dans la même seconde = **1** calcul, résultat mis en cache avec `rev` et durée de vie courte (1-5 s), réponse « inchangé » (`304`-like) si le client a déjà la `rev`. Même principe pour classements, enchères (listes), boutiques, événements du jour.
5. **Jamais de base dans le chemin chaud** : contenu = snapshot mémoire (swap atomique) ; état joueur chaud = Redis (déjà en place : or/position/vitals) ; écritures en write-behind groupées (déjà : flush 15 s, 300 persos < 1 s). Les lectures de panneaux (archive, wiki, admin) se font sur l'**index local du client** : zéro réseau.
6. **Pas d'allocation dans les boucles chaudes** : `ArrayPool<byte>`, `Span<T>`, tampons réutilisés pour les paquets, pas de LINQ ni de `string` dans le chemin de déplacement/vue ; GC en mode concurrent, pas de pause > 20 ms (mesure `dotnet-counters`).
7. **Budget par joueur** : ≤ 30 ko/s moyen en jeu, rafale ≤ 200 ko ; AOI inchangé (±48), émission des déplacements dédupliquée et limitée en fréquence par distance ; priorité des paquets (combat > déplacement > cosmétique).
8. **Back-pressure** : file d'envoi bornée par session ; un client lent est dégradé (paquets cosmétiques abandonnés) sans bloquer les autres ; jamais d'`await` d'envoi dans la boucle de zone.
9. **Pool de connexions PG** dimensionné (`EDEN_DB_POOL_MAX`), `max_connections` ≥ 300, `pgbouncer` si le total dépasse ; index sur toute clé de recherche ; `pg_stat_statements` actif (déjà `020`).
**Preuves exigées (lot N)** : (a) enquêter sur « Broken pipe » ; (b) étendre `Eden.Bots` avec un scénario **« contenu »** (300 clients qui ouvrent wiki/archive/liste de villes/boutiques en même temps + une release de contenu poussée en pleine charge) ; (c) mesurer **p50/p95/p99 de latence de boucle de zone**, débit sortant, pauses GC, CPU, pour 50 / 150 / 300 bots ; (d) seuils d'acceptation : boucle de zone p99 < 50 ms, aucun paquet de gameplay retardé > 100 ms pendant une release, 0 déconnexion ; (e) rapport dans `docs/knowledge/performance-baselines.md`. Comme pour le reste : « fait » = chiffres publiés.

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
| B0 | ADR « deux bases `eden_content` / `eden_live` (repli fichiers + sauvegarde) » ; DDL des tables **typées** générées depuis les descripteurs ; `IContentStore` (PostgreSQL, fichiers JSONL) ; `tools/content/seed` + `export` | Seed rejoué 2× = mêmes hashs ; chaque table/colonne commentée (`doc-lint` vert) ; édition de test inscrite dans `history.change` ; arrêt de la base → boot depuis l'export |
| DB-A | Inventaire (`tools/db/inventory`) des 187 tables : 71 non référencées, 0 ligne → décision par table (supprimer / brancher), sauvegarde `pg_dump` avant | Liste signée par le propriétaire ; 0 table sans lecteur ni écrivain |
| DB-B | Séparation `edenfr` → `eden_live` (116 tables vivantes, renommage clair, commentaires) et suppression des bases `edenfr_db/account/member/load` et `fn*` après sauvegarde | `dotnet test` vert, login + entrée de zone + inventaire sur la nouvelle base ; 2 bases seulement dans `\l` |
| DB-C | `doc-lint` (commentaires, nommage, tables mortes) en CI + génération de `docs/reference/DB-SCHEMA.md` | Lint vert ; dictionnaire de données généré |
| N | Réseau « no-lag » §3.8 : enquête « Broken pipe », scénario de charge « contenu », 50/150/300 bots, seuils | Rapport chiffré dans `performance-baselines.md` |
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

### 4.4 Hiérarchie des types de map et édition par map (réponse n°4 et n°8)
**Règle de conception** : un seul modèle, trois niveaux d'héritage. Une valeur de gameplay se résout **du plus précis au plus général** : *réglage du mob dans la map* → *réglage de la map* → *réglage du type de map* → *défaut global*. Le type de map porte les **règles de mode** ; la map porte les **ajustements**.

| Type de map (`map_kind`) | Définition (voir §4.1) | Instanciation | Règles propres |
|---|---|---|---|
| **Monde ouvert** (`field`) | `c_node` type 1 hors instances partagées | partagée | pénalité d'EXP à la mort, PvP/zones selon node |
| **Instance partagée** (`shared_instance`) : BTS 224, DD 216, Labo Vingot 212… | type 1/11 + respawn rapide + boss durs | **partagée** (on croise des joueurs sans groupe, pas de sens imposé) | **pas de perte d'EXP à la mort** (comme un donjon), mobs 15-90 s, boss durs |
| **Donjon classique** (`dungeon`) D1-D3 | type 5/7, Épreuve | **privée au personnage ou au groupe** (décision 8) | pas de perte d'EXP, quota d'entrées/jour, boss à longue réapparition |
| **Mode spécial** (`special_mode`) | Illusion Destructrice (D4), Tour des Cieux, arènes, expéditions, champs de bataille | selon le mode | récompenses/score propres (système *Evaluate* pour ID) |
| **Trial / épreuve scorée** (`trial`) | nodes 655-668 (type 18) + 829-858 solo | privée à l'équipe | score, grade, loterie de fin |
| **Ville de guilde** (`guild_city`) | type 4 (501/502) | privée à la **famille** visitée | bâtiments, PNJ de service |
**Vérifications faites [VÉRIFIÉ]**
- *Donjons privés* : `ZoneEntryFlow.Instancing.cs` (`ResolveInstanceGroup`) donne à chaque personnage la clé `team.Id` (en équipe) ou `-charId` (solo) pour les types de la liste `InstanceNodeTypes = {4,5,6,7,8,9,10,12,14,15,16,17,18,19}` ; deux joueurs sans groupe commun **ne se voient pas** (entités, mobs et PNJ filtrés par groupe). Le type 4 (ville de guilde) est lié à la famille, pas à l'équipe.
- *Défaut trouvé* : « donjon » est défini **à deux endroits incohérents** : `ZoneEntryFlow.Dungeon.cs:29` (`catalogue OU type 5`) et `ZoneLiveWorld.cs:620` (types 5, 7, 11, 18). La **pénalité d'EXP** (`Combat.Death.cs:402`, `!IsDungeonNode`) n'est donc **pas** supprimée dans BTS/DD/Labo Vingot (type 1) alors que le propriétaire veut qu'elle le soit. Le lot C remplace ces deux définitions par `map_kind` (une seule source, §4.1).
**Règle de perte d'EXP (7.2.3 n°6, définitive)** : on perd de l'EXP **uniquement** si la mort a lieu sur une **map normale** (`field`) **ET** a été causée par un **monstre (PvE)**. **Aucune** perte d'EXP ni d'or, **jamais**, en PvP, quelle que soit la forme (duel sur map normale, Île Sakura / mêlée générale, arènes, GvG, champs de bataille), ni en donjon (classique, Illusion Destructrice, héroïque, épreuve), instance partagée, ville de guilde ou mode spécial. Implémentation : une fonction unique `DeathRules.LosesExp(map_kind, killerKind)` (remplace `!IsDungeonNode` dans `Combat.Death.cs:402`), clés `Death.ExpLossPct` et liste blanche par `map_kind` ; **tests pour chaque cas** (PvE champ = perte ; PvP champ = 0 ; PvE donjon = 0 ; PvE BTS = 0 ; duel = 0 ; Sakura = 0).
**Déplacement des mobs (7.2.3 n°5) — NON éditable, mais à PORTER fidèlement.** Le jeu connaît **4 comportements**, qui doivent exister côté serveur et client, lus des données de scène : (1) **immobile** (certains boss de monde et de donjon) ; (2) **errance aléatoire** près du spawn (mobs de monde normal) ; (3) **patrouille coordonnée / trajet prédéfini fixe** (worldboss, mobs d'instance et de donjon, allers-retours lents sur un tracé, parfois en groupe) ; (4) **caché sous la map**, révélé par **déclencheur** (tuer un mob précis, ex. donjon Rage Cap) ou par **proximité** du joueur. État actuel [VÉRIFIÉ] : `ZoneEntryFlow.MobWander.cs` implémente l'**errance aléatoire** (tick 300 ms) ; la prise en charge des trajets fixes, des groupes coordonnés et des mobs cachés/déclenchés est **à auditer** (lot M1) : chercher dans `scene/sNNN.ini` les colonnes de trajet de `[reborn_monsters]` et les sections d'événements (`ZoneEntryFlow.Probes`/`RunNodeSceneEventAsync` existe), comparer avec le décompilé et par donjon-échantillon (Rage Cap, BTS, un worldboss). L'éditeur ne propose donc **pas** de déplacer un spawn ni d'éditer un trajet (v1 **et** v2) : seules les valeurs (stats, drops, EXP, respawn, récompenses) sont éditables. *(Le §7.2.2 point 5 est tranché : surcharges seulement.)*
**Édition complète par map** (modèle de données dans `eden_content.map`) : `map_profile(map_id, map_name_fr, map_kind, recommended_level, …)` ; `map_monster_spawn(map_id, spawn_id, monster_id, monster_name_fr, respawn_seconds, x, y, …)` ; `map_npc_spawn(…)` ; `map_monster_override(map_id, monster_id, hp_percent, attack_percent, exp_percent, gold_percent, drop_rate_percent, drop_table_id, reward_item_id…)` ; `map_reward(map_id, …)` ; `map_entry_rule(map_id, daily_entries_solo, daily_entries_multi, min_level, …)`. Les spawns, aujourd'hui dans `scene/sNNN.ini` (lus par client **et** serveur), passent dans la base : **la géométrie (fsm/nif) reste un asset ; la population devient une information**. Opérations : **ajouter / modifier / supprimer** un spawn, un mob, un drop, une récompense, un PNJ ; édition en lot (« tous les boss de la famille Angor ×1,2 EXP ») ; aperçu avant/après ; annulation par l'historique.
**UX de l'éditeur (ergonomique ET optimisé)** : arbre `Type de map ▸ Famille ▸ Map ▸ (Mobs · Spawns · Drops · Récompenses · Entrées · PNJ)` ; panneau de détail avec **valeur héritée affichée en grisé** et surcharge locale en couleur ; recherche instantanée sur l'index mémoire ; validation immédiate (références, bornes) ; tableau virtualisé (milliers de lignes sans lag) ; mêmes écrans en jeu (panneau admin) et sur le site (Limitless).
**Acceptation** : BTS/DD/Vingot → plus de perte d'EXP à la mort (test + capture) ; deux comptes sans groupe dans Carrière Angor ne se voient pas, deux comptes sans groupe dans BTS se voient (test live) ; modifier l'EXP d'un mob de DD par l'éditeur change le gain au kill suivant sans redémarrage ; annulation depuis l'historique restaure l'ancienne valeur.

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

### 6.2b Organisation du travail FR → EN (réponse n°6 : items d'abord, mais pas seulement)
**Unité de traduction** = `(type d'entité, id, champ)`, stockée dans `eden_content.text.translation(entity_type, entity_id, field, lang, value, status, translated_at, reviewed_by)` avec `status` ∈ `missing / auto / reviewed / validated / outdated`. **`outdated`** : le texte FR source a changé après la traduction (hash du FR mémorisé) → retraduction signalée automatiquement. Le FR n'est **jamais** modifié par le pipeline EN.
**Ordre de livraison par vagues** (chaque vague est livrable seule, avec rollback §6.4) :
| Vague | Contenu | Pourquoi |
|---|---|---|
| 1 | **Objets ≤ 90 présents en jeu** (nom, description, effet d'usage) + glossaire | priorité du propriétaire ; sert wiki, inventaire, enchères, craft |
| 2 | Interface : menus, fenêtres, statuts, messages système, textes serveur | visible partout, volume faible |
| 3 | Classes, sorts, talents, enchants/statuts | cohérence combat |
| 4 | PNJ (nom + titre), monstres, cartes, bâtiments, recettes | dépend des objets (noms propres) |
| 5 | Quêtes (titres, dialogues, objectifs) | volume le plus lourd, dépend de tout |
| 6 | Succès, titres, collections, événements, éphéméride | contenu secondaire |
**Tableau de bord de traduction** (panneau « santé du contenu ») : par type d'entité, % `validated`, nombre de `outdated`, chaînes les plus vues en jeu non traduites. Les noms propres (PNJ, lieux) passent par le **glossaire** pour rester cohérents entre quêtes, cartes et objets.

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
Phase -1 (disque)           FAIT (lot Z)
Phase 0 (bloquants joueur)   A1 → A2 → A5 → A4 → A3      (indépendant du reste, démarrer ici)
Phase 1 (fondations)         C (définitions) ‖ B1 (Eden.Content, ADR-0003) ‖ D0 (ADR)
Phase 2 (contenu unique)     B2 (migration S_* → c_*) → B3 (hot-reload) → B5 (couverture services)
Phase 3 (wiki + site)        D1 → D2 → D3 → D4
Phase 4 (admin)              D5 → D6 → D7 → D8             (B3/B6 en parallèle)
Phase 5 (anglais)            E0 → E1 → E2 → E3/E4 → E5 → E6   (après B1 ; E2 peut démarrer dès A2)
```
Phase 6 (config + VIP)       F1 → F2 → F3 → F4 → F5     ‖  DB-A → DB-B → DB-C (avant B0)  ‖  N (réseau, en continu)      (F1 peut démarrer dès la phase 1 ; A3 en dépend pour ses constantes)
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

### 7.2 Décisions (voir §1 D7-D17) et points restant à préciser
**Closes** : durabilité 1 pt / 10 s ; animations à retrouver (`collect01/02`) ; contrats PNJ lus dans les tables FR ; Limitless copié et versionné ; `ui_text_en.tsv` accessoire ; base de données + export + historique annulable ; VIP = simple statut ; entrées 10 solo + 10 multi ; hiérarchie des types de map ; éphéméride ; réseau no-lag ; nettoyage disque listé (§9).
**Points restant à préciser (uniquement ceux qui changent le code)** : voir §7.2.2, après les réponses du lead dév ci-dessous.

### 7.2.1 Réponse du lead dév : 
1. Coté sgbd pour le contenu tu fais ce que tu veux, l'essentiel c'est : Table TOUTES SANS EXCEPTION utile (pas de table mortes) avec commentaires pour TOUTES les tables existantes, des descriptif pour CHAQUE colonne de chaque table etc, tout doit être documenté, sur la gestion des databases je veux une database selon les données statique (comme item, npc...) et une database selon les données actives (joueurs, guilde actuels...) avec donc un tri des tables attribués pour les database, je veux pas 50 database différentes, 2 a 3 MAXIMUM selon si le site web nécessaire une database ou autre, une database statique et une dynamique et SI BESOIN en évitant un pattern dégueulasse une dernière database pour autre chose. Je veux également que les noms et les data ne soit pas que des id ou des choses incompréhensibles pour un humain, il faut des nommages même si pas utile techniquement parlant, des datas claire (si je vais voir les data je dois comprendre ce que je lis et pas voir des relations entre tables ultra complexe, on veut de la performances mais surtout des datas clair).
2. Le vip n'est qu'un "simple" status, VIP ou pas VIP, le but c'est des "petits buff" pour les joueurs (mais qui techniquement sont complexe a envisagé dans un code propre comme l'exemple du temps de chargement quand on craft), ça pourrait aussi potentiellement donner des status comme un bonus d'exp % ou de drop % (comme les charmes) ou autre style de buff.
3. Il est vrai que dans l'eden officiel c'était 2 entrée solo et 10 entrée multiplayer, en revanche on va faire une modification ici pour une base de 10 entrée solo et 10 multiplayer (changeable via typiquement un status VIP ou alors le config.txt du serveur via par exemple un correctif ou un event bonus.)
4.  La question est assez complexe, il faut faire par hiérarchisation. On sait que les instances (BTS etc) sont pas comme les donjons classique (par exemple dans une instance si on meurt c'est comme un dj on perd pas d'exp) mais techniquement c'est aussi comme une vrai map (on peut croiser des gens sans être dans un groupe et y'a pas de "sens" a suivre obligatoire). Donc premièrement un tri par type de map (normal, donjons, instances...), ensuite il faut des moyens d'éditions PAR MAP dans chacun des cas, édition de stats des mobs et de leur drop et exp donné etc. (Oublie pas que les illusions destructrices incarne également un "mode de jeu" donc un type de map différents des donjons classique, des instances et des map normal, c'est des "donjons" spéciaux, et les trials aussi sont "différents" des autres types de map). Donc pour chaque hiérarchie il faut des moyens d'édition complet pour ajouter, modifier, supprimer du contenu, et surtout l'axe compliqué c'est que TOUT doit être a la fois ergonomique mais fonctionnel avec une UX/UI pratique et surtout optimisé.
5. Je veux le meilleur donc fait selon ce que tu estimes le plus propre a faire.
6. je dirais Item, mais warning y'a pas que les items, y'a les quetes les npc les textes etc, réalise une vraie organisation pour tout le contenu FR->EN a créer, dans tous les cas le FR est source de vérité absolue.
7. Oui un historique est HAUTEMENT conseillé, l'historique doit également être supprimable, permettre de revenir en arrière d'une modification etc
Pour tes idées supplémentaire, (a) Oui un bac a sable est une bonne idée si bien réalisé et si j'ai assez d'espace disque,(b) je pense que tu as pas tord sur le coté tableau de bord, tant que l'UI n'est pas envahissante et que tu fais une hiérarchisation propre des panels et sous-panels pour voir les soucis potentiel, voir des suggestions... (c) Oui ça me va tant que bien réalisé et conceptualisé. (d) Oui, une sorte d'almanax comme sur dofus mais pour eden eternal avec des buffs journalier pour farm un donjon spécifique ou un type de batiment de ville de guilde spécifique ou un taux d'exp augmenté etc, ça pourrait être une voie super intéressante tu as eu une super idée, essaie de creuser ça pour une personnalisation plus complexe et plus simple a éditer ou créer (peut être une logique comme skript ? ou des variable config.txt ? je sais pas mais essaie de trouver des idées sympa).



8. Je rajoute une informations supplémentaire, pour les donjons classique on est d'accord que t'a bien fait en sorte que si 2 joueurs rentre dans un même donjons sans être dans le même groupe, ils ne se voient pas ? un donjon c'est privé a un personnage ou au groupe d'une personne.
9. Je rajoute une informations supplémentaire, sur tout l'axe de l'idée "serveur qui envoie les informations de tout le contenu du jeu au client en no-lag only", il est obligatoire de bien réfléchir a la méthodologie technique pour faire ça "no-lag", je rappel que le jeu doit pouvoir tank 50 joueur actif qui vont pas attendre simplement, ils feront tous quelque chose en même temps et parfois certains auront besoin des mêmes infos statique ou dynamique a un temps d'intervalle court etc, il est nécessaire donc de réfléchir au moyen d'optimiser tout ce bordel réseau, conceptualisant (pour être certain que 50 joueurs pourront jouer sans lag) l'idée que 300 joueurs actif serait présent en même temps et que même là ça ne doit pas lag (le serveur n'aura pas de scaling vertical comme ferait kubernetes, il n'y a qu'un serveur qui tournera donc tout doit être millimétré et optimisé sans perdre en qualité donc cherche les bonnes méthodes a employer).
10. Vérifie si c'est possible de libérer de l'espace via le dossier "/home/ster2lune/Bureau/reverseden/portage/", peut être y-a t-il des dossier lourd et statique déjà dupliqué dans le projet actuel ? fait moi la liste des dossier ainsi que des raisons de pourquoi ces dossier serait supprimable sans danger ou perte de data importante pour le projet actuel (projet actuel étant "/home/ster2lune/Bureau/reverseden/eden-godot").

### 7.2.2 Analyse des réponses du lead dév et points à préciser (2026-11-07, 3ᵉ passe)
Chaque réponse a été traduite en décision (§1 D11-D17) et en lot. Contradictions et risques repérés :
1. **Cohérence de ma première version** : j'avais proposé des tables génériques `row(table,id,cells jsonb)`. Ce n'est **pas lisible** : remplacé par des tables typées et des colonnes de libellé (§3.7).
2. **Séparation en deux bases et clés étrangères** : impossibles entre deux bases PostgreSQL ; remplacées par identifiants métier + test de cohérence à la release. *À confirmer* : acceptes-tu l'absence de contrainte SQL entre `eden_live` et `eden_content` ? (alternative : une seule base à deux schémas, contraintes possibles, mais tu as demandé deux bases ; je recommande deux bases.)
3. **VIP simple vs « petits buffs complexes »** : une partie passe par des enchants natifs (EXP %, drop %), le reste par le moteur de modificateurs (F5). *À préciser* : liste des **premiers buffs VIP** souhaités (EXP/drop %, temps de craft, tick de récolte, entrées de donjon ?) avec leurs valeurs initiales.
4. **Éphéméride** : *à préciser* : durée d'un effet (1 jour comme Dofus, ou aussi hebdomadaire) ; l'effet est-il **annoncé la veille** (planifier sa journée) ou découvert le jour même ? ; un joueur peut-il le **consulter hors jeu** sur le site (je suppose oui) ?
5. **Spawns dans la base** : sortir les populations des fichiers `sNNN.ini` est un gros chantier (574 scènes, lus par le client **et** le serveur). Proposition : v1 = édition par **surcharges** par map (`map_monster_override`, pas de déplacement de spawn) ; v2 = édition complète des spawns. *À valider.*
6. **Pénalité d'EXP en instance partagée** : le propriétaire veut aucune perte (comme un donjon). C'est un changement de comportement par rapport au code (§4.4) : *à confirmer* que cela vaut aussi pour les champs de bataille et arènes (je suppose non).
7. **Suppression de l'historique** : *à préciser* la rétention par défaut (je propose 180 jours) et qui peut purger (niveau 5 uniquement).
8. **Disque** : 29 Go libres sur 561 (95 %). Les bases sont minuscules (≈ 120 Mo au total) : le bac à sable ne pose pas de problème ; c'est le nettoyage §9 qui compte.

### 7.2.3 Réponse au 7.2.2

1. Ok, tant qu'a la fin on a qu'une seule source de data statique en DB avec tout ce que j'ai précisé dans le 7.2.1
2. les contraintes ne sont pas obligatoire. Tant que les tables en tant que tel sont propre et bien codé, tu n'es pas obligé d'utilise les principes de contrainte ou de relation entres tables, l'essentiel c'est qualité et performance, ce n'est pas utiliser tout le coté technique impressionnant et complexe pour rien.
3. Pour une base de VIP essaie de faire -25% du temps actuel pour chaque temps de craft, de plan de récolte et de durabilité utilisé sur le temps dans les batiment farm mine et boule de crystal, avec un status qui donne 10% exp character, class, awaken class perma (retiré quand le status VIP n'est plus présent).
4. On peut créer un calendrier qui reprend le visuel des "battle pass" comme fortnite mais qui indique par jour le buff de la journée, consultable 3 semaine a l'avance par exemple (et modifiable par les dév facilement). Et oui les buffs sont journalier, sachant que tout le jeu "reset" a 6h du matin (les entrées de dj, les bonus ou autres truc considéré comme "par jour" etc). N'oublie pas que 2 "système de jours" existent dans dofus, les "vrais jours" comme les bijou auric et iris qui dure selon le vrais temps IRL (hors du jeu), et il a aussi les jours via le "reset" journalier (par exemple les daily quest, entrées de donjons...)
5. Le déplacement des mobs je pense pas qu'il faut pouvoir éditer ça car ça parait extrêmement complexe, ça serait géniale mais il faudrait reproduire l'UI des maps, indiquer via des indicateur claire les pattern des chemins précis des mobs et boss qui bougent (dans le jeu les mobs peuvent se positionner de 4 façons, soit ils ne bougent pas (cas de certains worldboss ou des boss de donjons classique), soit il bougent aléatoirement (cas ddes mobs classique dans les mondes normaux et autres exemples que j'ai pas en tete), soit ils se déplacent de façon coordonnée avec d'autre mob et/ou via un pattern de déplacement précis (comme certain worldboss ou des mobs d'instance ou de donjons qu'importe la forme, y'en a beaucoup également dans le jeu, ces patterns sont prédéfini et fixe, les mobs marchant simplement via une animation de déplacement lente a faire des aller retour de ce pattern), soit caché sous la map (comme certains mobs d'instance ou de map normal ou de donjons également, même des boss parfois, pour les proc il faut soit les faire spawn via un déclencheur comme le donjon rage cap où il faut kill un mpb pour faire spawn le dernier boss ou alors via le fait de se déplacer proche du mob pour qu'il apparaissent d'un coup pour aggresser le joueur)). Tous ces patterns sont complexe a coder et précis mais nécessaire.
6. Pour tout le contenu PVP on ne doit pas perdre d'exp qu'importe la forme ou l'endroit où est le joueur, que ce soit dans une map normal en duel ou sur l'ile sakura (mélée générale) ou sur des mode d'arène ou GvG etc, PVP = AUCUNE PERTE d'exp ou de gold etc. Pour le coté perte d'exp, il faut perdre de l'exp QUE sur les maps normal sur une mort PVE (contre des mobs). On perd pas d'exp en donjon qu'importe la forme (Illusions destructrice, dj classique etc), qu'importe les instances, qu'importe ville de guilde ou autre. Perte d'exp via mort = map normal ET mort via un mob donc pve.
7. Suppression de l'historique on parlait de l'historique des modifications depuis le panel admin, pas d'autre chose. En gros si je fais une modification des PV d'un mob et qu'au final je veux revenir en arrière, l'historique doit simplement être un rappel de tout ce qui a été modifié ainsi qu'une option pour revenir a l'état précédent cette modification.
8. Réalise la libération de stockage pour tout ce qui est sans danger ou qui nécessite une simple petite mise a jour pour être sans danger (met en premier dans ton plan d'action)

### 7.3 Journal de ce plan
- 2026-11-07 (5) : lot Z EXÉCUTÉ (`portage/` supprimé, 29 → 82 Go libres, build + 1 799 tests verts, archive 9,6 Go dans `archive/portage/`). Moteur de modificateurs F6 : le VIP ne passe plus par un enchant natif.
- 2026-11-07 (4) : réponses 7.2.3 intégrées : lot Z (disque) placé en premier, VIP −25 %/+10 % EXP, calendrier 21 jours, reset 06:00 + deux systèmes de jours, règle d'EXP PvE/champ uniquement, 4 comportements de mobs à porter (non éditables).
- 2026-11-07 (3) : réponses du lead dév 7.2.1 intégrées. Mesures : 187 tables en base (71 mortes, 43 commentées, 26/2407 colonnes) ; contenu 10,9 Mo compressé ; `IsDungeonNode` défini à 2 endroits ; donjons privés confirmés ; `portage/` : ~63 Go uniques (hardlinks vérifiés). Ajouts : §3.7 refait, §3.8, §4.4, F2/F4/F5 refaits, §6.2b, §9, lots DB-A/B/C et N.
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

### F2 — Entrées de donjon configurables (décision D14)
- **Défaut du jeu : 10 entrées solo + 10 entrées multijoueur par jour** (le natif officiel donnait 2 solo / 10 multi ; l'écart est voulu). Remplacer la constante `DungeonEntryRules.MaxDailyEntries` (`DungeonCatalog.cs:64`) par `Config.Resolve("Dungeon.MaxDailyEntries.Solo|Multi", contexte)` ; valeurs par défaut 10/10 dans `eden_content.rule`.
- Modifiable par : statut **VIP** (F4), **`server.config.txt`** (correctif global), **événement ou éphéméride** (F3/F5), commande admin, réglage **par map** (`map_entry_rule`, §4.4). Cumul documenté : `défaut de la map` → `règle de mode` → `événement/éphéméride` → `VIP`, plafonné par `Dungeon.AbsoluteMaxEntries`.
- **Entrées bonus** : solde `dungeon_bonus_entry(account_id, map_kind, remaining, expires_at, source)` dans `eden_live`, consommé après le quota du jour.
- Compteurs : `game.player_dungeon_entries` (`025-dungeon-entries.sql`, `EvaluateRepository.cs:138-156`). Le client affiche quota + bonus.
- **Acceptation** : réglage 3 → 4ᵉ entrée refusée (message FR) ; VIP +2 → 5ᵉ autorisée ; événement +2 s'ajoute ; reset à `Dungeon.ResetHour` ; tests unitaires de la cascade.

### F3 — Événements configurables (extension du `Event.*` actuel)
- Événement = bloc de configuration **daté** : `Event.<nom>.Start/End` (calendrier automatique), `ExpPct/DropPct/GoldPct` (existants) + `DropItem` (déjà prévu en commentaire) + **`DungeonBonusEntries`**, `CraftDurationPct`, `RespawnPct`, `GatherRatePct`, `ShopDiscountPct`. Plusieurs événements actifs se cumulent selon une règle documentée (additif par défaut, plafonds configurables).
- **Acceptation** : un événement planifié s'active et s'éteint tout seul (horloge simulée en test) ; annonce en jeu (broadcast existant).

### F4 — VIP : un simple statut, des « petits buffs » (réponse n°2, décision D14)
- **Modèle minimal** : `eden_live.account.vip_status(account_id, is_vip, vip_since, vip_until NULL=permanent, granted_by, note)`. **Pas de paliers.** Par compte (cohérent avec l'admin par compte).
- **Deux mécanismes complémentaires, un seul concept pour le joueur** (« je suis VIP ») :
  1. **Statut visible** (icône, infobulle) posé à l'entrée en zone via le système de statuts existant (`2026-11-03-status-system.md`) : **purement informatif** (enchant sans commande, id réservé `65100+`, texte FR/EN en contenu). **Ne porte aucune valeur de gameplay** (révision 7.2.3 : le couplage à l'enchant natif est abandonné, voir F6).
  2. **Modificateurs de gameplay** (toutes les valeurs VIP, y compris l'EXP) fournis par le moteur F6 ; contrairement à ce que je pensais d'abord, les enchants natifs ne peuvent pas exprimer le temps de craft (aucun enchant de « temps de craft », de « tick de récolte », d'« entrées de donjon » : vérifié dans `Eden.Core/Data/EnchantCommands.cs`, seuls `ElfCollectTime/Rate/CombineRate` existent pour les elfes) : fournis par le **moteur de modificateurs** (F5) — `Config.Resolve(clé, contexte)` où le contexte contient `is_vip`. **Aucun `if (vip)` dans les systèmes de jeu.**
- **Valeurs initiales décidées (7.2.3 n°3)** : **−25 % du temps actuel** pour **chaque temps de craft** (cast et durée des ordres), **chaque temps de plan de récolte**, et pour **la durabilité consommée par temps** dans l'usage des outils de ferme, mine et boule de cristal (donc ×0,75 sur la consommation : un outil dure ≈ 40 min au lieu de 30) ; plus un **statut permanent « VIP » : +10 % d'EXP personnage, +10 % d'EXP de classe, +10 % d'EXP de classe éveillée** — posé tant que le compte est VIP, **retiré automatiquement** quand le VIP s'arrête (statut recréé à chaque entrée en zone depuis `vip_status`, jamais persisté seul). Les −25 % **et** les +10 % d'EXP passent par le **moteur de modificateurs unifié (F6)** : le VIP n'est **pas** un enchant natif (voir F6 pour la raison et le remplacement). Le statut visible « VIP » (icône) est un enchant **cosmétique sans commande**, recréé à chaque entrée en zone et jamais la source de la valeur.
- **Table de bonus VIP = données** (`eden_content.rule` ou fichier `vip.rules`), ex. : `craft.cast_seconds ×0.8`, `craft.duration ×0.9`, `gather.tick_seconds ×0.85`, `dungeon.entries.solo +2`, `dungeon.entries.multi +2`, `exp ×1.10`, `drop ×1.10`. Chaque clé du registre déclare si elle est « surchargeable par VIP » (liste blanche).
- Administration : `//vip <compte> [jours]`, écran du panneau, page du site ; historique dans l'audit.
- **À ne pas faire en v1** : boutique/paiement, paliers, cosmétiques.
- **Acceptation** : deux comptes (normal/VIP), même plan : le cast VIP est 20 % plus court (log `[Config] resolve craft.cast_seconds … vip=1`) ; l'icône VIP apparaît ; retrait du VIP → comportement normal au prochain calcul ; test de cascade.

### F5 — « Éphéméride d'Eden » et moteur de modificateurs (idée Almanax, décision D13)
**Concept** : chaque jour (ou semaine/événement), un **effet du jour** s'applique à tout le serveur : « aujourd'hui +25 % d'EXP dans Carrière Angor », « +50 % de réussite des fonderies », « le Labo Vingot donne +1 entrée bonus », « récolte de pioche ×1,5 », « −20 % de temps de craft d'alchimie ». Affiché dans un panneau en jeu (aujourd'hui / demain / semaine), annoncé à la connexion, consultable sur le site.
**Un seul moteur pour VIP, événements, éphéméride, charmes et réglages de map** : le **moteur de modificateurs**.
- **Modificateur** = `{ cible, opération, valeur }` : `cible` = clé du registre (F1), `opération` ∈ {`multiplier`, `ajouter`, `fixer`, `plafonner`}, `valeur` numérique.
- **Règle** = `{ quand, alors[], jusqu'à, source, priorité }` en **JSON/TSV déclaratif** (pas de langage de script) :
  ```
  quand : { map_kind: "dungeon", map_family: "carriere_angor" }   # ou { building_type: "alchimie" } / { account.is_vip: true } / { class: 12 }
  alors : [ { cible: "exp.rate", op: "multiplier", valeur: 1.25 }, { cible: "dungeon.entries.solo", op: "ajouter", valeur: 1 } ]
  ```
- **Pourquoi pas un langage de script** : sécurité (aucun code arbitraire côté serveur), testabilité (une règle = une donnée validable), hot-reload trivial, éditable par formulaire dans le panneau, diff lisible dans l'historique. Si un besoin dépasse ce format, ajouter un **nouveau champ `quand` ou une nouvelle opération** au moteur (un fichier, testé), jamais un interpréteur.
- **Résolution** (une seule fonction, testée) : `défaut global → type de map → map → éphéméride/événement → VIP → buff personnel (charme/enchant)` ; les `multiplier` se composent, les `ajouter` se somment, `plafonner` s'applique en dernier ; un **journal de résolution** (`explain`) dit pourquoi une valeur vaut X (indispensable à l'admin et aux tests).
- **Rotation déterministe** : `effet_du_jour = pool[hash(date, graine_serveur) mod n]` avec pondération et règles d'exclusion (pas deux fois le même thème de suite, alternance donjon / ville de guilde / EXP) ; l'admin peut **forcer** un jour, **verrouiller** un calendrier ou **réutiliser un modèle** ; pas de tâche planifiée fragile : l'effet se déduit de la date.
- **Modèles d'effets** (bibliothèque dans `eden_content.calendar`) : *Journée du donjon* (cible = une famille de donjons), *Journée du bâtiment* (type de bâtiment de ville : durée/réussite), *Journée d'EXP*, *Journée de butin*, *Journée de récolte*, *Journée d'entrées bonus*. Création d'un nouveau modèle = formulaire (nom FR/EN, icône, description avec variables `{valeur}`, règles).
- **Sources de données existantes à réutiliser** : tables natives `c_festival.ini` (647 lignes, bonus de fête), `c_activity.ini` (2 679 lignes, activités datées), `c_recommendactivity.ini` (1 212 lignes), `c_destinyevent.ini` : à **lire d'abord** pour ne pas dupliquer les événements officiels ; l'éphéméride est un ajout du serveur, pas un remplaçant.
- **Calendrier façon « battle pass »** (7.2.3 n°4) : panneau en jeu présentant une **frise de jours** (grille/ruban de cartes, une par jour : icône, nom du buff, effet chiffré, jour actuel mis en évidence, jours passés grisés). **Visible 3 semaines à l'avance** (21 jours), modifiable facilement par les développeurs (écran d'édition du calendrier : clic sur un jour → choisir un modèle ou le créer ; glisser-déposer pour déplacer ; verrouiller un jour). Buffs **journaliers**.
- **Deux systèmes de jours (comme Dofus), à modéliser explicitement** (`GameClock`, lot F1) : (a) **jour de reset** = jour de jeu qui bascule **à 06:00** heure serveur (entrées de donjon, quêtes journalières, bonus « par jour », éphéméride, compteurs `DailyDayIndex`) ; (b) **jour réel** = durée écoulée en temps réel IRL, indépendante du reset (ex. objets/statuts qui durent N jours réels : VIP, contrats `due_date`, équivalent des bijoux Auric/Iris). Aujourd'hui le code utilise le **minuit local** (`DungeonEntryRules.DayIndex(DateTime.Now)`, `ZoneEntryFlow.Dungeon.cs:87,135` ; `DailyDayIndex` persisté) et **aucune clé `ResetHour` n'existe** : à créer (`Reset.Hour = 6`) et à appliquer partout où « par jour » est utilisé (grep `DayIndex`, `DailyCount`, `ResetDailyMission`). Test : horloge injectée à 05:59 / 06:00 / 06:01 sur entrées de donjon, quêtes journalières, éphéméride.
- **Client** : panneau « Éphéméride » (icône dans le HUD) alimenté par le delta de contenu `calendar` ; aucune logique.
- **Acceptation** : 7 jours simulés (horloge injectée) → séquence conforme aux règles d'exclusion ; effet actif visible en jeu ; un effet « +25 % EXP en Angor » change réellement le gain (log `explain`) ; édition d'un modèle sans redémarrage ; tests de la fonction de résolution (≥ 30 cas).


### F6 — Moteur de modificateurs unifié (refonte demandée en 7.2.3 : modularité, propreté, maintenabilité)
**Question posée** : l'EXP VIP via un « enchant natif » pose-t-elle problème ? **Réponse : oui, on l'évite, et on en profite pour nettoyer.**
**Ce que fait le code aujourd'hui [VÉRIFIÉ]** : le calcul d'EXP est **éparpillé en 5 endroits** sans modèle commun :
| Où | Quoi |
|---|---|
| `Combat.Rewards.cs:477-490` | `exp = ExpReward × ServerConfig.EffectiveExpRate(rate env) × DungeonRewardMult` ; idem classe avec `EffectiveClassExpRate(…, classe éveillée 21-60)` |
| `ServerConfig.cs:76-95` | rates globaux × (1 + `EventPct`) — événements en dur (Christmas/Halloween) |
| `Combat.Rewards.cs:818-826` (`GrantCharExpAsync`) | puis `exp += exp × Σ(charmes ExpRateBonus)/100` |
| `Combat.Rewards.cs:282-290` | EXP de classe : `Σ(CpRateBonus + WakenCpBonus)` **additionnés ensemble**, sans distinguer classe normale / éveillée |
| `Status.cs:475-491` + `Items.cs:496` | l'enchant natif `ItemExpRateUp` (2911) / `ItemWakenNpRateUp` (2912/2914) remplit `ExpRateBonus` / `CpRateBonus` / `WakenCpBonus` du statut |
**Pourquoi l'enchant natif est un mauvais support pour le VIP** : (1) un enchant est **persisté** (`player_enchants`) → risque de VIP « fantôme » après expiration ou changement de compte ; (2) la valeur vivrait dans une **ligne de contenu** `c_enchant` (+ texte) plutôt que dans la règle VIP ; (3) il faut inventer des ids d'enchant dans un espace partagé avec le natif ; (4) il ne sait exprimer ni temps de craft, ni durabilité, ni entrées de donjon → deux mécanismes pour un seul concept « VIP » ; (5) l'additionnement `CpRate+WakenCp` empêche un réglage séparé classe/éveil.
**Conception cible : `Eden.Core/Game/Modifiers/` (bibliothèque pure, testable hors serveur)**
- `ModifierKey` : registre des clés (déclaratif, F1) — ex. `exp.character`, `exp.class`, `exp.awakened_class`, `drop.rate`, `gold.rate`, `craft.cast_seconds`, `craft.duration`, `gather.tick_seconds`, `gather.durability_cost`, `dungeon.entries.solo|multi`, `death.exp_loss`. Chaque clé déclare : unité, **mode de composition**, bornes, « surchargeable par VIP ? ».
- `IModifierSource` (une classe par source, un fichier chacune) : `ConfigSource` (`server.config.txt`/`rule`), `MapKindSource` (§4.4), `EventSource`, `AlmanaxSource` (F5), `VipSource`, `StatusSource` (**adaptateur** qui expose les bonus des charmes/enchants natifs en lecture seule), plus tard `GuildSource`. Ajouter une source = ajouter une classe ; **aucun système de jeu ne change**.
- `ModifierContext` (struct immuable) : `{ accountId, charId, mapId, mapKind, classId/classKind, buildingType, craftType, isVip, now }`.
- `ModifierResolver.Resolve(key, ctx) → Resolved { Value, Explain[] }`. **Règles de composition (documentées et testées)** : (a) `Base × Π(multiplicateurs des sources « multiplicateur »)` — config, événement, éphéméride, mode de map, bonus de récompense du donjon multi ×1,5 ; (b) **`× (1 + Σ pourcentages additifs / 100)`** pour les sources « bonus additif » — charmes **et VIP** (cohérent avec le natif : « comme les charmes », les % de charmes se somment) ; (c) `fixer` puis `plafonner` en dernier ; (d) `Explain` liste chaque source et sa contribution (outil admin « pourquoi cette valeur ? » + tests).
- **Performance** : le résultat est mis en cache **par session et par clé**, invalidé uniquement sur événements (connexion, changement de statut, de map, de `rev` de configuration, minuit/6 h) ; le chemin chaud (kill → EXP) fait une lecture de tableau, **zéro allocation** (contexte struct, pas de LINQ).
- **VIP dans ce moteur** : `VipSource` renvoie, si `is_vip` : `exp.character +10 %`, `exp.class +10 %`, `exp.awakened_class +10 %` (additifs), `craft.cast_seconds ×0,75`, `craft.duration ×0,75`, `gather.tick_seconds ×0,75`, `gather.durability_cost ×0,75`. Les valeurs viennent de la table `vip.rules` (donnée). Quand le VIP s'arrête, **la source cesse de répondre** : rien à retirer, rien à nettoyer.
- **Migration des 5 sites** (refonte) : remplacer les formules de `Combat.Rewards.cs`/`ServerConfig.cs` par `Modifiers.Resolve(...)` ; séparer `CpRateBonus` et `WakenCpBonus` (un bonus d'éveil ne s'applique qu'aux classes éveillées, un bonus de classe qu'aux classes normales — **à vérifier dans le natif** `CEC_ItemWakenNpRateUp` avant de changer le comportement) ; supprimer les `case "exppct"` en dur de `ServerConfig` au profit des événements-données.
- **Tests (≥ 40)** : composition (multiplicateur seul, additif seul, mixte, plafond), invalidation du cache, VIP actif/inactif/expiré, séparation classe/éveil, charme + VIP additifs, événement + VIP, non-régression : **mêmes résultats qu'avant** pour un joueur sans VIP, sans événement et sans charme (golden sur 10 kills).
- **Acceptation** : un même kill rapporte l'EXP attendue avec (rien / charme / VIP / charme + VIP / événement + VIP) ; `Explain` imprimé dans le log `[Modifiers]` ; le fichier `Combat.Rewards.cs` n'a plus aucune formule de taux en dur ; retrait du VIP → EXP normale immédiatement au prochain kill.

| Lot | Dépend | Preuves |
|---|---|---|
| F1 | — | Tests registre + capture du cast modifié à chaud |
| F2 | F1, A5 | Test live quota/bonus sur 2 comptes |
| F3 | F1, F2 | Test calendrier + annonce |
| F4 | F1, D0 | Test live 2 comptes (normal/VIP) |
| F5 | F1, F6, B3 | Tests de résolution + simulation 7 jours + capture du panneau |
| F6 | F1 | ≥ 40 tests + golden « mêmes résultats qu'avant » + log `[Modifiers]` |

---------------------------------------------------------------------------
## 9. Libération d'espace disque (analyse initiale — EXÉCUTÉE, voir lot Z au §1bis)
---------------------------------------------------------------------------
**Disque** : 561 Go, 504 Go utilisés, **29 Go libres (95 %)**. `portage/` fait 74 Go ; `eden-godot/` 43 Go.
**Méthode** : deux arbres peuvent partager des fichiers par **liens physiques** (ADR-0004) ; supprimer un des deux ne libère alors **rien**. Mesure faite avec `du -sm eden-godot <dossier>` (compte les inodes déjà vus une seule fois) et comparaison d'inodes.
| Dossier de `portage/` | Taille propre (hors liens) | Pourquoi supprimable / risques | Verdict |
|---|---|---|---|
| `eden_godot/godot_project/assets` (dérivés GLB/PNG du client 4.7) | **27,5 Go** | `eden-godot` n'a **pas** de dossier `assets/` : il lit les formats natifs via `raw_assets -> ../corpus/fr-full` (loaders NIF/KF/DDS). Les dérivés sont **régénérables** par `tools/nif2gltf` depuis `corpus/fr-full`. Perte : on ne peut plus lancer le **client 4.7 de référence** avec ses assets (comparaison côte à côte) | Supprimable si tu n'as plus besoin de lancer le client 4.7 |
| `eden_godot/dist` (exports/builds) | **17,7 Go** | Sorties de build régénérables ; aucune source | Supprimable sans danger |
| `eden_godot/godot-shots` (captures anciennes) | **8,3 Go** | Captures de session ; `ref-shots/` (95 Mo, comparaison) est un autre dossier. **Attention** : `eden_godot/godot_project/scripts/player/PlayerController.cs:1566` écrit encore dans ce chemin en dur — à corriger avant | Supprimable après correction du chemin |
| `corpus/official` (corpus officiel/EN-US) | **9,4 Go** | Interdit d'usage par `AGENTS.md` (EDENFR pur). **Possiblement irremplaçable** (autre source) | **Sauvegarder sur un disque externe avant** ; puis supprimable |
| `corpus/original-fr` | 352 Mo | Archive FR d'origine ; petit ; peut servir de preuve de provenance | À garder |
| `eden_server` (copie ancienne du serveur) | 431 Mo | `eden-godot/eden_server` est l'arbre actif ; **seul usage restant** : l'oracle « Reforged » éventuel de `ReforgedDataLocator` (`PortageRelative`, désactivé par `EDEN_REFORGED_DB`) et `server_us` (tables US de référence, 200 Mo) | Supprimable après vérification que `EDEN_REFORGED_DB` pointe bien sur `corpus/fr-full/data/db` (c'est le cas dans `start-native.sh`) ; garder `server_us` si on veut le rapport de divergence |
| `knowledge` (150 Mo), `ref-shots` (95 Mo), `futur-feature` (8 Mo), `docs`, `tools` | ≈ 0,25 Go | Anciennes copies ; `knowledge/tools/*.py` écrit encore vers `portage/knowledge` en dur | À garder ou déplacer |
| `corpus/fr-full` (9 Go) et `re` (1,4 Go) | **0 Go** | **Liens physiques** vers `eden-godot/corpus/fr-full-pak-2026-10` et `eden-godot/re` (mêmes inodes, vérifié sur 2 000 fichiers) : les supprimer ne libère rien | Ne rien faire |
**Total réellement libérable dans `portage/`** : jusqu'à **≈ 63 Go** (27,5 + 17,7 + 8,3 + 9,4) ; sans le client 4.7 et sans `corpus/official` : ≈ **26 Go** (dist + godot-shots). 
**Dépendances à traiter AVANT toute suppression** : (1) `tools/graph/graph.sh portage …` indexe `../portage` (garder au moins `portage/eden_godot/godot_project/scripts` et `docs`, ≈ 6 Mo, pour le graphe de référence « équivalent de Y dans l'ancien client ») ; (2) `PlayerController.cs:1566` (captures) ; (3) `tools/legacy-flax/audit/gen_fr_supplements.py:10` et `knowledge/tools/*.py` (chemins en dur, outils d'archive).
**Autres gisements dans `eden-godot/` (hors demande, à titre d'information)** : `artifacts/corpus-sheets` 7,2 Go, `artifacts/packs` 6,6 Go, `artifacts/corpus-png` 4,1 Go (tous régénérables, ignorés par git) ; `corpus/fr-full-pak-2026-10` 9 Go est le **même contenu** que `portage/corpus/fr-full` (liens physiques) : à ne supprimer qu'avec lui si plus aucun outil ne le cite.
**Procédure proposée** : (1) sauvegarde externe de `corpus/official` ; (2) corriger les 3 chemins en dur ; (3) lancer un build + `tools/test.sh` ; (4) supprimer dossier par dossier en commençant par `dist`, mesurer `df` après chaque étape ; (5) consigner dans `docs/workflows/`.
