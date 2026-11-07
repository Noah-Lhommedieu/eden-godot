# PLAN MAÎTRE — VDG / UI guilde-craft / Wiki / Admin / i18n (2026-11-07)

> Destinataire : une IA qui n'a AUCUN contexte. Lire dans l'ordre : ce fichier → `01-ENQUETE-BUGS.md` → `02-ARCHI-DONNEES-I18N.md`
> → `03-DONJONS-TAXONOMIE.md` → `04-WIKI-ADMIN-V2.md` → `05-UI-REVUE.md`. Les anciens rapports IA de `docs/reports/craft-guild-recon-2026-11-03/`
> et la plupart des `docs/temp-plans/` sont JUGÉS FAUX par le propriétaire : sources valides = `knowledge/guides-fr/pages/guilde-et-ville-de-guile.md`,
> `le-craft.md`, `ref-shots/`, layouts natifs `corpus/fr-full/uidata/layouts/`, décompilé (`re/reindex`), tables `corpus/fr-full/data/db`.
> `docs/temp-plans/2026-11-06-vdg-verif.md` (passes 1-6, écrit par l'IA précédente) CONTIENT DES VALIDATIONS ABUSIVES : voir §3.

## 1. Règles de travail (non négociables)
- Lire `AGENTS.md`, `CLAUDE.md` à la racine de `eden-godot/`. Client Godot 4.8-beta2 .NET = `engine/current/godot` (jamais le godot du PATH). Pas de git.
- `corpus/`, `re/`, `portage/` en lecture seule. Se repérer avec `tools/graph/graph.sh query "…"` et `re/reindex lookup|search|addr|xref` AVANT grep.
- « Fait » = capture `@shot` ET log du jeu réel, **2 tests indépendants** (le propriétaire exige « deux tours »). Capturer UNIQUEMENT avec `@shot` (jamais le bureau).
- Les tests doivent suivre le chemin RÉEL du joueur (clic droit souris sur le PNJ, clic sur l'option de dialogue, clic droit sur l'item dans le sac…) et PAS des raccourcis GM
  (`@talk`, `@dlg`, `@work`, `@open` contournent le picking, les dialogues et les gardes ; `@open` force `Visible=true` sur les sous-panneaux ; `@closeall` masque les wrappers
  de fenêtres de façon à casser ensuite les panneaux qui ne se réaffichent pas — un faux « ça ne s'ouvre pas » est possible après `@closeall` : redémarrer le client).
- Canal de commande du jeu : `docs/workflows/AGENT-IA-GAMEPLAY.md` (`/tmp/gm.cmd`, `@shot`, `@cam2`, `@inv`, `@cellpos`, `//goto`, `//give`, `//guild_level`, `//hall_setup`…).
- Lancement : serveur `cd eden_server && EDEN_DEV_WORK_CAP=100 nohup setsid ./start-native.sh …` (ne jamais `pkill -f` avec un motif présent dans la ligne de commande du shell : tuer par PID) ;
  client `tools/run.sh -- --login=test123:test123 --channel --char --cmd-file=/tmp/gm.cmd`. Build : `tools/build.sh` (client+serveur), tests `cd eden_server/server && dotnet test tests/Eden.Tests`.
- Logs : `/tmp/client.log`, `/tmp/zone.log`, `/tmp/world.log`, `/tmp/mission.log`. DB : container `edengodot-postgres`, base `edenfr`, schéma `game` (voir §2).
- Unités d'or : 1 or = 1000 argent (FAQ wiki ; statue 500 po = 500000). Corrigé côté client en passe 2 (voir 05).

## 2. Carte du contexte (où regarder)
| Sujet | Où |
|---|---|
| Client UI HUD | `eden_godot/godot_project/scripts/hud/*.cs` (GuildWindow 1867 l., CraftWindow, CombineWindow, StrengthenWindow, TownListWindow, FamilyBuildingWindow, FamilyStorageWindow, HudRoot, HudWindows (z-order), DragHelper, MinimapPanel, RegionMapWindow, GmFileProbe) |
| Layouts natifs CEGUI | `corpus/fr-full/uidata/layouts/*.layout` (familywnd_na, familyinfownd_na, familytownlistwnd, familybuilding*, collectwnd/combinewnd, strengthenwnd*, worklistwnd, workresultwnd, territory*, zonemapui*, instancedungeoncontrol) |
| Textes UI | `eden_godot/godot_project/gamedata/ui_text_{fr,en,en_fr}.tsv` (ui_text_fr.tsv : clé chinoise → FR, **première entrée gagne**), `scripts/data/{UiTextEn,TextResolver}.cs` |
| Serveur zone | `eden_server/server/src/Eden.Zone/` (ZoneEntryFlow.*.cs : Items, Work*, FamilyInstance, FamilyCity, Dialogue, Mission.Commands, Status, Sight…) ; handlers `Handlers/` (ItemActionHandlers, FamilyHandlers, GmHallSetupHandlers, GmGuildHandlers) |
| Serveur monde / mission | `Eden.World/` (FamilyHandlers, ZoneLinkServer, TownRecycleService), `Eden.Mission/Services/QuestEngine*.cs` |
| Règles pures | `Eden.Core/Game/Family/{FamilyHallRules,FamilyCityRules,FamilyBuildingWorkRules}.cs`, `Eden.Core/Game/Work/WorkOrderService.cs`, `Eden.Core/Data/Parsers/*.cs` |
| Natif (preuves) | `re/reindex` ; zone `CEC_AttrGainCollectItem` @0x085061a0/0x08505930/0x085056b0, `CWorkManager::CheckToCollect` @0x08162a00, `SFamily::CheckNpcSizeLimit` @0x08464ea0, `CGlobalInfo::FamilySetLevel` world @0x08052ad0, `TC_FamilySetLevel` mission @0x081ef170, `CEventFamilyInstanceRecycleCheck::Execute` @0x081e4040, `CFamily::ClearInstanceData` @0x081b53e0 |
| Données | `corpus/fr-full/data/db/*.ini` (Big5 ; c_item, c_npc, c_mission, c_familycollect, c_familycombine, c_buliding, c_level, c_textindex) + `t_*.ini` (UTF-8, textes) ; scènes `data/scene/sNNN.ini` |
| Guides / wiki | `knowledge/README.md` (index), `knowledge/guides-fr/pages/`, `knowledge/wiki-fr`, `knowledge/wiki-en`, `knowledge/wiki-tables` ; graphes `knowledge/graphify-out/graph.json` |
| Refs visuelles | `ref-shots/` (127 captures) + `eden_godot/docs/client-assets/ref-shots-catalog.md` |
| Portail web existant « Limitless » | `eden_server/limitless/{web/backend,web/frontend (Next.js 16),studio (Avalonia)}`, docs `eden_server/docs/limitless/` |
| Plans/preuves précédents | `docs/temp-plans/2026-11-06-vdg-verif.md` (⚠ voir §3), `docs/temp-plans/2026-11-07-ui-guilde-craft-inventaire.md` (inventaire UI + défauts), `docs/architecture/CONCEPT-WIKI-ADMIN.md` (1re conception, amendée par 04) |
| DB (schéma `game`) | `family` (lv, exp, treasury, house_open, recycled_time), `family_building_container` (family_id, loc, tid, item_type 41=bâtiment/42=PNJ, level, exp, durability, due_date), `inventory1`, `player_enchants`, `player_works…` |
| Données de test | comptes `test123:test123` (ClericQA lv90, guilde QAForge #5001, hall node 502), `racetest:test123` ; guilde #5000 « livetest » (hall 501), #5002 « devinguild ». ⚠ 5000 et 5002 ont été recyclées puis remises à house_open=501 PAR DÉDUCTION (valeur d'origine perdue) |

## 3. État réel vs. validations abusives (à ne PAS croire aveuglément)
| Affirmation de `2026-11-06-vdg-verif.md` | Réalité constatée le 2026-11-07 |
|---|---|
| « Mine/ferme/invocation/pioche/gants/boule : 2/2 OK » | testés par `@talk/@dlg/@work/@use` (contournent picking, dialogues, UI) et sans la mécanique native décrite par le propriétaire (§4 R1). Le test serveur « slots=N » ne valide NI le status « en cours », NI les ticks 30 s, NI l'annulation au mouvement, NI l'animation. |
| « PNJ de bâtiment fonctionnels » | Le propriétaire constate qu'aucun ne marche. Repro du 2026-11-07 : clic **gauche** sur un PNJ = sélection seulement (`PlayerController.cs:851`) ; clic **droit** ouvre le dialogue puis l'option ouvre bien le panneau Fonderie (client propre, ville A). Cause chez le propriétaire NON reproduite → `01-ENQUETE-BUGS.md` (hypothèses : clic gauche attendu, autre ville, handles, option de dialogue, z-order, état post-GM). |
| « Liste des villes de guilde OK » | Fenêtre non devant les autres ; n'affiche ni bâtiments, ni niveaux, ni PNJ, ni vendeurs ; voir R4. |
| « 2 villes de guilde préparées » | Préparées en DB (`//hall_setup 5001 reset rebuild`, `//hall_setup 5000 reset rebuild set2`, `//guild_level … 12`) mais **non vérifiées visuellement dans la 2e ville ni via la liste des villes** ; hôtel/marché sans PNJ ; aucun PNJ vendeur d'équipement. |
| « UI revues » | seulement 17 fenêtres capturées, 10 défauts corrigés ; reste beaucoup (voir 05). |

## 4. Check-list EXHAUSTIVE des exigences du propriétaire (R = récolte/VDG ; U = UI ; D = données ; W = wiki ; A = admin)
Chaque ligne doit avoir un statut vérifiable dans le plan d'exécution (`06-PLAN-ACTION.md`). ⭐ = ajout de l'IA, non demandé.
### Ville de guilde (brief initial)
- R0.1 Entrer par G → « Entrer en ville » / « Quitter ville » ; `//hall_setup reset` pose bâtiments + PNJ.
- R0.2 Test position de récolte natif (AttrGainCollectItem vfunc +300 = `IsCoRiding`, pas une position ; contrôle d'aire réel à retrouver) ; décider des vendeurs 53199-53201 (présents seulement à Aven dans le natif).
- R0.3 Bâtiments par type/niveau ; slots PNJ hôtel/marché (guide : hôtel 8, marché 3 ; binaire : hôtel 3, marché 3, production 8 — trancher en client natif).
- R0.4 Unité d'or = 1000 (fait côté client, à vérifier côté serveur/données).
- R0.5 Tour complet de la ville (yaw 0/90/180/270, `@cam2 yaw -60 45 4`), PNJ de service visibles devant chaque bâtiment, orientation, anneaux de récolte non enterrés.
- R0.6 Tests : mine/ferme par plan, pioche/gants/boule physiques, invocation (+ kill + loot), restrictions de niveau (0x392 hall ; 0x9f1 Aven niv ≥ 3), entrée d'aire sans blocage client.
- R0.7 Rouvrir une à une (avec `@shot` et `@cjk`, zéro chinois) : Guilde (Guilde/Messages/Rangs/Ville) + sous-dialogues (don, annonce, commentaire, emblème, rangs, privilèges, création de ville, candidature : titres FR, Confirmer/Annuler, un seul dialogue à la fois, Échap ferme) ; FamilyBuildingWindow (noms de plans FR, cadres d'aire, slots PNJ) ; liste des villes, coffre, territoire, croisade ; cartes (minicarte, régionale, monde : icônes natives, légende).
- R0.8 À coder si échec : invocateur (mob+loot), plans manquants (insertion housekeeper `ZoneEntryFlow.FamilyInstance.cs`), cadres d'aire (rendu natif), Échap, blason (éditeur), HUD guerre de territoire.
### Session du 2026-11-07
- R1 Outils de récolte (pioche/gants/boule) : cast au clic droit, status « en cours de minage » (icône), animation (mine/pioche/boule) sous status, annulation si déplacement, ressource aléatoire ~toutes les 30 s selon rareté/niveau de l'outil (tranche de niveau de ressources), quantité raisonnée selon rareté. « Ne marchent pas dans les zones. »
- R2 TOUS les PNJ de bâtiments (forge, alchimiste, fermier, mineur, invocateur, artisan de mare…) doivent ouvrir leur panneau (craft par plan + récolte par plan).
- R3 Un panneau ouvert ou cliqué passe devant (tous les panneaux, y compris liste des villes).
- R4 Liste des villes de guilde : bâtiments actifs, niveaux, PNJ actifs, vendeurs présents, etc. ; bouton « Villes de guilde » = emplacement du panneau « Find Town ».
- R5 Préparer dans l'inventaire des cases libres ; **2 villes de guilde** avec tous les bâtiments répartis (5 max par ville, pas de doublon), niveau max, PNJ « concrets » posés (pour s'équiper) — à VÉRIFIER dans le jeu.
- R6 ⭐ Bouton « Quitter ville » → « Entrer en ville » : fait, à revérifier (R0.1).
### UI (demande du 2026-11-07)
- U1 Lister TOUT le code client lié (de près ou de loin) à guilde/craft ; capture globale par UI ; analyser défauts (texte qui dépasse, non traduit, cases de slot peu évidentes…) ; corriger TOUT (réf. knowledge, ref-shots).
- U2 Panels : guilde, craft, entrée en donjon, etc. comparés aux panels natifs ; « Find Town » (voir R4).
### Données / i18n / architecture (demande du 2026-11-07)
- D1 FR = source de vérité ; EN = version supplémentaire BIEN FAITE dérivée du FR ; périmètre = contenu actuellement en jeu ≤ Lv90 uniquement ; ROLLBACK FR-only prévu et testé.
- D2 Séparer logique/UI : serveur = informations (noms, stats, recettes…), client = visuel (anim, modèles) ; une seule source d'information ; hot-reload/MAJ sans redémarrage client ; supprimer la dette technique de l'ancien client.
- D3 Taxonomie « donjon » : donjon de map simple (ex. Carrière Angor) vs instances (Dimensions Dévastées, Vingot Lab : map partagée, boss durs, respawn 15-30 s) vs donjons spéciaux (Illusions destructrices = donjons normaux version Lv90 récompenses Lv90) ; et autres.
- D4 Repérage du plan de craft d'un item.
- D5 Site : améliorer Limitless existant + panel EN JEU complet ; admin = COMPTE admin (pas personnage).
### Wiki
- W1 Tous les items + description + moyens d'obtention précis (PNJ : nom/map/coordonnées ; mob : nom/map ; donjon : « Donjon : nom » ; PNJ de ville de guilde : guildes qui l'ont en live view ; plan de craft : item référent cliquable ; arborescence de recherche/retour).
- W2 Toutes les quêtes ; W3 tous les donjons avec infos ; W4 guildes actuelles (infos publiques, bâtiments, niveaux, PNJ actifs) ; W5 autres (⭐ classes/sorts, mobs, PNJ/boutiques, recettes, cartes, succès) ; W6 code propre/maintenable, MAJ complémentaires.
### Admin
- A1 Give à soi/autre (⭐ joueur connecté aléatoire, give all) ; A2 édition/ajout/suppression sur joueurs, guildes, donjons, mobs, quêtes… ; A3 système administrateur (compte) ; A4 API serveur (le serveur modifie ses données) ; A5 implications (FR/EN futur, client, admin joue en version packagée, joueur via launcher, format/chargement des données).
### Systèmes de ville (session précédente)
- S1 Montée niveau guilde (fait natif), S2 montée niveau bâtiments (fait, 2/2), S3 durabilité/réparation (réparation à rejouer), S4 durées des contrats PNJ à la pose (test unitaire seulement), S5 taxes joueurs extérieurs (code, à rejouer), S6 quêtes de guilde (fait en partie), S7 maintenance de ville (fait natif, 2/2), S8 statues de guilde/joueurs, cristal de ville, changement de chef (non faits).
