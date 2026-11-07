# Exécution — Vagues 0 et 1 (journal, 2026-11-07)

Suite de `docs/architecture/PLAN-ACTION-3-EXECUTION.md`. Baseline : tag `pre-plan3-baseline`.
Résultat global : **build 0 erreur / 0 avertissement ; NetTests 939, Eden.Core.Tests 967, Eden.Tests 1 894 (+95 nouveaux), 0 échec.**

## Fait et prouvé
| Lot | Livré | Preuve |
|---|---|---|
| Q-A | `SRV/Eden.Core/Game/Foundation/` : `GameClock` (reset 06:00 + jour réel, `FakeGameClock`), `GameRandom` (`IRandom`, `SeededRandom`, `Fork`), `GameError`/`Result<T>`/`ErrorCatalog` + `errors.tsv` embarqué (FR/EN) | 29 tests `Foundation/*` verts ; test « chaque code a un texte FR » |
| Q-B | `Eden.Tests/Architecture/` : taille ≤ 400 lignes (code nouveau), pas de `catch {}` vide, pas de `Random.Shared`/`DateTime.Now` hors Foundation, `ZoneEntryFlow` ne doit pas grossir (baseline 97 fichiers / 42 216 lignes), Core sans Godot ; baseline `Data/architecture-baseline.tsv` (ne peut que baisser) | 10 tests ; le garde-fou a **bloqué mon propre ajout** (+7 lignes) que j'ai compensé par des suppressions |
| Q-C | Sonde `@wins` (`CLI/hud/WindowProbe.cs`) : liste couche/rang/rectangle/recouvrement et vérifie que la fenêtre ouverte en dernier est la dernière dessinée | sortie `[wins] OK frontmost=…` ci-dessous |
| A1 | **Cause racine corrigée** : `ZoneWindows` (CanvasLayer 9) → toutes les fenêtres sous la couche du HUD (10) via `ZoneWindowHost` (détachées avant leur `_Ready`, rattachées au HUD). **Régression trouvée et corrigée en test réel** : `HudRoot.BuildAll` refusait de construire si le HUD avait des enfants (`GetChildCount() > 0`) → drapeau `_built` | Live (client réel, S502) : `@open townlist/guild/craft` + `@wins` → `GuildPanel` puis `Root_TownList` successivement `frontmost`, `covered=0%` ; capture `/tmp/ui/a1_after.png` regardée : « Villes de guilde » devant « Fabrication » et Guilde |
| C2 | `CraftPlanRules` (plan = 1ᵉʳ matériau de kind 53-56 ; plans de récolte kind 50-52) | 8 tests dont **preuve sur `c_familycombine.ini` réel** (> 2 000 recettes à plan unique, toutes avec le plan en 1ʳᵉ position) |
| C1 (partie) | `MapKind` + `MapClassifier` (une seule définition) + `DeathRules` (**perte d'EXP uniquement sur map normale ET mort PvE**) ; `MapKindResolver` ; **câblé dans le revive** (`Combat.Death.cs`) à la place de `IsDungeonNode` ; cause de mort mémorisée (`LastDeathKiller`) | 24 tests ; corpus : BTS 224, DD 216, Labo Vingot 212 = seules instances partagées de type 1 ; Angor 101/603/630 donjons ; 655/829 épreuves ; 501/502 villes ; 96 (Sakura) mode spécial |
| Q-D (partie) | `Eden.Core/Game/Modifiers/` : `ModifierKey` (registre), `IModifierSource`, `ModifierResolver` (Set → ×Multiply → ×(1+ΣAddPercent) → +AddFlat → bornes/Cap, `Explain`), `VipModifierSource` (−25 % temps, +10 % EXP ×3), `StaticModifierSource` | 20 tests (composition, VIP actif/expiré, classe normale ≠ éveillée, VIP + charme additifs) |
| A3 (partie pure) | `Loot/WeightedPool` (Q7) + `Gather/GatherRules` : tirage pondéré par taux de `c_familycollect`, quantité plafonnée par rareté (≥ 25 % → 4, ≥ 5 % → 2, sinon 1), durabilité 1 pt/10 s avec report de fraction (180 pts = 30 min ; VIP ×0,75 = 135) | 17 tests |
| DB-A | `tools/db/inventory.py` → `docs/reference/DB-INVENTORY.md` : 187 tables, **62 à supprimer** (0 ligne, 0 référence), **16 à examiner**, 109 à garder ; 43/187 tables et 26/2 407 colonnes commentées | généré sur la base réelle |
| A5 (partie) | `tools/fixtures/vdg-verify.sh` (état des 2 villes : OK — A 5 bâtiments niv. 12 + 25 PNJ, B 3 bâtiments niv. 12 + 5 PNJ, les deux ouvertes) et `vdg-two-towns.sh` (rejouable) ; `EDEN_TOWN_RECYCLE=off` (World + `start-native.sh`) | `vdg-verify.sh` : « OK : les deux villes sont prêtes » |
| A4 (partie) | `TownListWindow` : « Bâtiments actifs : N », nom du **type** de bâtiment (plus un identifiant), niveau, durabilité, contrat limité | build vert ; non revérifié en jeu |

## Pas encore fait (reste du plan)
- **A2** : cause probable (fenêtres derrière le HUD) corrigée par A1 ; **le parcours par clics réels (PNJ → option → fenêtre → plan → ordre → récupération) reste à rejouer** sur les 15+ PNJ des deux villes ; fenêtre toujours vide par conception native (liste de plans du sac, libellés `[i18n-miss]`).
- **A3** : session de récolte serveur (canal 3 s → statut → tick 30 s → annulation au mouvement), statut visible, animations `collect01/02`, messages d'erreur : à brancher (règles pures prêtes).
- **A4** : PNJ actifs et vendeurs par ville (nécessite une extension de protocole ou la dérivation `FamilyCityRules` côté client).
- **A5** : kit de test dans le sac, contrats hôtel/marché lus dans les tables FR (candidats : PNJ 53625-53633).
- **Q-D** : registre de configuration typé (`server.config.txt`), migration des 5 sites d'EXP vers le moteur (F6) ; **Q5-Q8** (bus d'événements, déclencheurs, `IZoneFeature`).
- **C1** : remplacer `ZoneLiveWorld.IsDungeonNode` (2ᵉ définition) ; curation (`187`, `765`, `771`…).
- **DB-A** : exécuter les décisions (suppression des 62 tables mortes) : demande une sauvegarde + migration réversible (aucune base modifiée à ce stade).

## Constats utiles
- Les commandes écrites dans le fichier GM **avant** que la sonde soit prête sont ignorées (offset initial = taille du fichier) ; attendre `[gmfile] canal actif`.
- `SceneParser` possède déjà `PatrolRoute` : une partie des trajets de mobs (lot M1) existe.
- Les anciennes tables morte comptées « 71 » incluaient des tables à 0 ligne mais référencées par un mot courant ; le décompte fiable est 62 + 16.


---

# A3 — Récolte continue (pioche / gants / boule de cristal) — FAIT et prouvé en jeu

**Livré**
| Élément | Détail |
|---|---|
| Règles pures | `Eden.Core/Game/Gather/GatherRules.cs`, `GatherSession.cs`, `Loot/WeightedPool.cs` (Q7) : tirage pondéré par les taux de `c_familycollect`, quantité plafonnée par rareté (≥ 25 % → 1-4, ≥ 5 % → 1-2, sinon 1), durabilité 1 pt / 10 s avec report de fraction, tolérance de mouvement 0,5 u, `NoticeIdFor` |
| Module de zone | `Eden.Zone/Features/Gather/GatherService.cs` + `GatherPackets.cs` (**premier module `IZoneFeature`-style, 0 ligne ajoutée à `ZoneEntryFlow`** : l'ancien tirage instantané de ~65 lignes a été supprimé) ; état de session par personnage ; ticks planifiés sur la boucle de zone ; modificateurs `gather.tick_seconds` / `gather.durability_cost` (VIP prêt, `IsVip` = false tant que F4 n'existe pas) |
| Câblage | `Items.cs` (`AttrGainCollectItem` → `StartGatherAsync`), `Movement.cs` (annulation au mouvement), `Combat.Spells.cs` `BreakCastAsync` (sort brisé, mort, migration), `ZoneEntryFlow.cs` `OnDisconnectAsync` (déconnexion), `QuestItems.cs` (refus hors aire **signalé**) |
| Paquet custom | `9063` S→C : début / tick (ressource + quantité) / arrêt / notice (15 octets, test de layout) |
| Client | `hud/GatherUi.cs` : bandeau « En cours de minage… » (cueillette / divination) avec barre jusqu'au prochain tirage, **animation en boucle**, message « Récolte : <nom> xN », notices FR ; `AnimNames` précharge `collect02` |

**Preuves live (client réel + serveur relancé, S502, personnage ClericQA niv. 90)**
- Pioche dans l'aire 60 : canal de 3 s → `[Gather] début … kind=Mining tick=30s` ; **tick à +30 s** (`06:01:36` → `06:02:06`) : `produit=36801 qté=1 durabilité−3` ; 2ᵉ tick `qté=3` (plafond commun = 4) ; durabilité en base 180 → 176.
- **Annulation au mouvement** : `@walk` → `[Gather] arrêt raison=Moved` + client « Vous avez bougé : la récolte est annulée. » (capture `/tmp/ui/a3_after_move.png`).
- **Statut visible** : capture `/tmp/ui/a3_mine_1.png` (bandeau centré « En cours de minage… » + barre).
- **Animation jouée** (log client) : pioche → `collect01`, **gants → `collect02`**, **boule → `collect02`** (`[gather] animation=collect02 (demandée collect02)`). Les 3 types ont une animation ; **l'association outil ↔ clip reste une HYPOTHÈSE** (pioche = collect01, gants/boule = collect02) à valider visuellement (la caméra de test était gênée par le vendeur posé au centre de l'aire).
- **Déconnexion** : `arrêt raison=Disconnected` (une session fantôme survivait au départ du client avant ce correctif).
- **Refus hors aire** : message FR avec accents « Vous devez vous trouver dans une zone de récolte adaptée à cet outil. » (`/tmp/ui/a3_refus2.png`). Avant : refus silencieux.
- Double utilisation : `GATHER_ALREADY`.

**Constats / décisions**
- Le canal texte serveur (Big5) **perd les accents** : les notices passent donc par un **numéro** (9063 état 3) que le client traduit ; les 5 textes sont dupliqués côté client (`GatherUi.Notices`) — **dette** à supprimer avec la synchro de contenu (lot B6).
- Le premier usage consomme encore 1 point de durabilité par le pipeline natif, puis −3 par tick de 30 s.
- Gap à vérifier : durabilité exacte consommée par le natif sur l'usage initial (1 point) — acceptée.

**Reste pour A3** : validation visuelle de l'association clip ↔ outil (caméra dégagée) ; statut dans la barre native des statuts (pas de ligne `c_enchant` : bandeau dédié à la place) ; tick de récolte par VIP (F4) ; `gather_rules` en configuration (F1).

**Tests** : +13 (`Gather*`) ; total serveur **1 904** verts ; build 0 erreur / 0 avertissement.


---

# A2 — PNJ de plan / récolte / craft : parcours par CLICS RÉELS — FAIT et prouvé en jeu

**Outillage ajouté (sondes, `CLI/hud/GmFileProbe.cs`)** : `@npcclick <id>` (VRAI clic droit au pixel sur le PNJ : projection écran + picking 3D + approche + dialogue, au lieu du raccourci `@talk`), `@optclick <n>` (vrai clic au centre de la n-ième option du dialogue), `@uiat x y` (quel Control reçoit la souris), `@tree <fragment> [prof]` (arbre des Controls : rect, filtre souris), `@wins` (A1). Les coordonnées de clic sont celles de la capture (1600×900), pas 1280×720.

**Causes racines trouvées (le « panneau ne s'ouvre pas » avait plusieurs causes empilées)**
1. **Fenêtres en couche 9 derrière le HUD** (A1, déjà corrigé) : les panneaux craft/ville étaient dessinés derrière le dialogue du PNJ.
2. **Dialogue PNJ qui ne se rouvre plus après une fermeture groupée** (`HudWindows.CloseAll`, Échap) : la fenêtre interne `Root_NpcTalkWnd` restait masquée alors que le conteneur était rendu visible → après la 1ʳᵉ fermeture groupée, **plus aucun PNJ n'affichait son dialogue** (`NpcDialogueWindow` ne rouvrait que le conteneur). Corrigé : `OpenDialogue()` → `HudWindows.Open(fenêtre interne)`.
3. **Panneaux de service jamais ré-affichés après fermeture groupée** : `CraftWindow` (détail, liste d'ordres, résultat), `CombineWindow`, `StrengthenWindow`, `LifeSkillWindow` faisaient `Visible = true` sur la fenêtre seule ; l'enveloppe masquée restait masquée. Corrigé : `HudWindows.Open()` (rend visibles les ancêtres ET passe au premier plan).
4. **Fenêtre déjà visible à l'inscription restant à son rang de création** (derrière les panneaux postérieurs) : `HudWindows.Register` appelle maintenant `Front` si la fenêtre est visible.

**Preuves (client réel, serveur relancé, villes A = S502 et B = S501, perso niv. 90)** — chaque test est précédé de `@closeall` (fermeture groupée, le cas qui cassait tout)
| PNJ / service | Parcours | Résultat |
|---|---|---|
| Fondeur 53046 (type 16) | vrai clic PNJ → dialogue « Maître Vétéran du Moulage » → clic « Mission d'Artisanat » | panneau **Fonderie**, `frontmost` ; plan 31096 glissé (`@dragitem` = vrai drag GUI) → recette affichée (Cuivre Brut 20/20, Cristal Pur 6/6, produit Poignard Cristal de Glace, instantané, 490, réussite 100 %) → **Commander** → « Résultat du travail — Commande terminée ! » → **Récupérer** → **objet 30297 en base (loc 11)** |
| Fermier 53008 (type 13) | clic → dialogue → « Missions Agricoles » | panneau **Ferme** `frontmost` ; plan 30136 déposé → « Peut obtenir : Branche Sèche 10 (100 %) », durée 20 min → Commander → `Work armé … type=13` + matériau consommé (la récupération n'a pas été attendue : 20 min) |
| Mineur 53018 (type 12) | clic → dialogue → « Missions d'Extraction » | panneau **CollectWnd** `frontmost` |
| Invocateur 53028 (type 14) | dialogue → « Missions d'Invocation de Monstres » | panneau **CollectWnd** `frontmost` |
| Alchimiste 53036 (type 15) | dialogue → « Missions d'Artisanat » | panneau **CombineWnd** `frontmost` |
| Ville B — artisan de la mare 53074 (type 17) | `@fament 5000` (entrée réelle dans la ville B) → vrai clic → dialogue → option | panneau **CombineWnd** `frontmost` |
| Vendeur de plans 53046 / 53028 (option « plans ») | option 0 | boutique `Root_shop` `frontmost` |
Captures regardées : `/tmp/ui/a2_dialog.png`, `a2_panel.png`, `a2_plan.png`, `a2_result.png`, `a2_farm_order.png`, `a2_summon.png`.

**Constats utiles**
- Les options du dialogue sont des lignes `HBoxContainer` (filtre Stop) dont la position dépend de la longueur du texte : cliquer par coordonnées fixes ne marche pas (d'où `@optclick`).
- 53046 / 53028 : la 1ʳᵉ option de certains PNJ est une **boutique de plans** ; l'option « Missions … » est ailleurs dans la liste.
- Les libellés `[i18n-miss]` du journal concernent la table EN : en français les textes s'affichent correctement.
- Le raccourci `@talk` approche à pied (route A*) : lent (> 12 s), parfois bloqué par un mur.

**Reste pour A2** : liste des plans compatibles du sac pré-affichée quand la fenêtre est vide (amélioration, non faite) ; durées des ordres de récolte non attendues (20 min) ; clic réel avec caméra dégagée pour les 15+ PNJ de service restants (même code, mêmes panneaux) ; test automatique de couverture « service → fenêtre » (B5).
**Test client à surveiller** : `NetTests.ItemProcDumpTests.ProcTextIsPerItem` échoue dans la suite complète (NRE ligne 82, `EnchantTable.Get` nul) **mais passe seul** ; dépendance d'ordre sur un cache statique, **sans lien avec mes changements** (aucun fichier lié à NetTests modifié hors `AnimNames`, test refait sans ce changement : même échec). À corriger séparément (lot qualité).
**Tests** : serveur 1 904 verts ; Core 967 verts ; NetTests 938/942 (1 échec d'ordre décrit ci-dessus, 3 ignorés).


---

# A4 + A5 — Villes de guilde : PNJ actifs, vendeurs, contrats FR, kit de test — FAIT et prouvé en jeu

**A5 — contrats hôtel / marché lus dans les tables FR (décision D7 : le jeu FR est complet)**
- `c_buliding.ini` : les bâtiments **hôtel (type 2) et marché (type 10) n'ont aucun housekeeper** (col6 vide) → leurs PNJ sont des contrats que le chef pose.
- Contrats de kind 42 portant un PNJ (`c_item` col35) : **34678 → 53003 commissaire-priseur sanglier**, **34679 → 53004 banquier**, 27332-27340 → 53625-53633 « Marchands célèbres » (plans de récolte hauts niveaux), 35001 → 53007 clerc. Aucun contrat FR n'existe pour l'**hôtel** (pas de PNJ : cohérent avec l'absence de service d'hôtel).
- Règle livrée (`FamilyCityRules`) : **marché = commissaire-priseur 53003, banquier 53004, marchand agricole célèbre 53625**, au plus `MallNpcLimit[niv−1]` (0/1/1/2/2/3… : 3 à partir du niveau 8) ; hôtel = aucun. **Effet de bord : l'hôtel des ventes devient accessible par le gameplay normal (comble le gap G36)**. `NpcCap` gère désormais les types 4-9 ET 10 ; 3 tests + 1 test sur le corpus réel (chaque PNJ du marché a un contrat FR).
- `//hall_setup 5000 reset rebuild set2` pose bien `pnj 53003@22, 53004@23, 53625@24` dans le marché de la ville B (`vdg-verify.sh` : 8 contrats PNJ).
- **Kit de test** : `tools/fixtures/vdg-kit.py` produit les `//give` depuis les tables FR (outils pioche/gants/boule niv. 30/45/60, plan le moins cher de chaque type de service — ferme 28580, mine 29185, invocation 30246, alchimie, fonderie, mare — et leurs matériaux) ; `vdg-two-towns.sh` l'appelle (niveau 90, or, kit). Les plans de récolte dont le catalyseur est un **outil** (kind 44) sont exclus.

**A4 — panneau « Villes de guilde »**
- **Nouveau paquet custom 9064** (`Features/TownDetail/TownDetailHandler.cs` + `hud/TownDetailUi.cs`) : C→S `[i32 familyId]`, S→C `[i32 familyId][u16 n]` + n × `[i32 npcTid][s16 loc][u8 rôle]` (1 responsable, 2 service, 3 vendeur d'outils). **Public uniquement** (aucune trésorerie ni membre nominatif). 3 tests de layout/rôles.
- Client : la grande liste « NPCList » du layout natif affiche « <Nom> — <rôle> — <bâtiment> » (noms FR via `TextResolver`), le cadre du haut les **bâtiments actifs** (type, niveau, durabilité) avec texte sombre lisible ; les entrées 0x2a/0x52 (PNJ) ne sont plus comptées comme bâtiments.
- **Bug trouvé** : `FindLeaf<T>` utilisait `FindChildren("*", typeof(T).Name)`, qui **ne reconnaît pas les classes de script** (`MclList`) → la liste « NPC/membres » de ce panneau était **toujours nulle et vide** depuis le début. Corrigé (filtre `is T`).
- Preuve (captures `/tmp/ui/a4_townB.png`, `a4_townA.png`) : **livetest** = « Bâtiments actifs : 3 » (mare magique, hôtel, marché, niv. 12) et PNJ : artisans, marchands de techniques/enchantements, **Commissaire-priseur sanglier, Banquier**, Marchand Agricole Célèbre, vendeurs Phio / Ri / Aidey ; **QAForge** = « Bâtiments actifs : 5 » (ferme 200, mine 200, autel 150, alchimie 100…) et 25 PNJ avec leur rôle et leur bâtiment.

**Reste** : le marché de la ville B a été posé par la fixture : vérifier **en clic réel** le dialogue du commissaire-priseur 53003 (ouverture de l'hôtel des ventes) ; hôtel sans PNJ (aucun contrat FR) à confirmer par une source native ; rafraîchissement en direct de la liste (B3) ; pas de PNJ « actifs » au sens « présent maintenant » (contrats expirés : `due_date`).
**Tests** : serveur 1 910, Core 967, NetTests 939 : tous verts (le test client d'ordre `ItemProcDumpTests` repasse : il écrit dans `/tmp/claude-1000/`, répertoire absent avant ce redémarrage de session).


---

# Vague 2 (partie) — C1 fini, DB-A exécuté, DB-C démarré

**C1 (fin)** : la 2ᵉ définition de « donjon » (`ZoneLiveWorld.IsDungeonNode`, types 5/7/11/18) est renommée `HasFixedMobLayout` : ce n'est qu'un **comportement d'IA** (scènes à mobs fixes), plus une notion de donjon. `ZoneEntryFlow.IsDungeonNode` a disparu au profit de `MapKind` (étape précédente). Il n'existe plus qu'**une** définition de « donjon » (`MapClassifier`).

**DB-A — tables mortes SUPPRIMÉES de la base réelle `edenfr` (conteneur `edengodot-postgres`)**
- Migration **`eden_server/Database/027-drop-dead-tables.sql`** (transaction, sans `CASCADE`, vérifiée en `ROLLBACK` d'abord) : **76 tables mortes + 17 tables d'archive du schéma `game_archive`** (schéma supprimé). Critères : 0 référence dans le code serveur/outils, 0 ligne ou journaux obsolètes, **aucune dépendance** (vues, clés étrangères : `pg_depend`).
- **Garde-fou qui a servi** : `game.final_mission_state2` semblait morte (aucune référence par son nom) mais la **vue `final_mission_state_v2`, utilisée par `MissionDb`, en dépend** → conservée. `account_archive.fortune_bag` (86 833 lignes, doublon de `game.fortune_bag`) conservée : **décision du propriétaire** (donnée volumineuse).
- **Sauvegardes** : `eden_server/Database/backups/dead-tables-2026-11-07.dump` (pg_dump des tables supprimées, données comprises ; restauration : `pg_restore -U postgres -d edenfr …`) + `artifacts/db-backups/edenfr-2026-11-07-pre-vague2.dump` (base entière). Les `*.dump` sont ignorés par git.
- Résultat : base **109 tables** (au lieu de 187 : account 12, game 95, member 1, account_archive 1) ; serveur **relancé** et client réel connecté jusqu'en jeu sans erreur `relation … does not exist` ; **1 910 tests serveur verts**.
- `tools/db/inventory.py` tient désormais compte des **vues et clés étrangères dépendantes** (plus de faux « morte »).
- **Doublons repérés pour DB-B** : `game.fortune_bag` = `account_archive.fortune_bag` (86 833 lignes), loteries en double entre `account.*` et `game.*` (`lottery`, `new_lottery`, `in_game_lottery`/`ingame_lottery`).

**DB-C — documentation**
- `eden_server/Database/028-core-table-comments.sql` (appliquée) : table + colonnes de `family_building_container` et `player_dungeon_entries`, et un commentaire de table pour ~35 tables centrales (vérifiés dans le code ; 2 suppositions erronées corrigées : `player_card` = fiche de profil, `bags` non documentée faute de preuve).
- `tools/db/doc-lint.py` : **65/109 tables et 1 755/1 784 colonnes sans commentaire** ; baseline `eden_server/Database/doc-lint-baseline.txt` = **1 820** manques, **ne peut que baisser** (`--baseline`).

**Reste de la Vague 2** : C3 (`scope.json`, « présent en jeu ≤ Lv90 »), DB-B (séparation `eden_live`/`eden_content`, fusion des doublons, renommages clairs), B0 (DDL typé du contenu, seed), commentaires des 1 755 colonnes restantes (rythme : par domaine, en relisant le code). Le fichier `001-initial-schema.sql` recréerait encore les tables mortes sur une installation neuve : à nettoyer avec DB-B.

**Suivi (décision du propriétaire : supprimer)** : `account_archive.fortune_bag` et le schéma `account_archive` supprimés (`029-drop-account-archive.sql`, sauvegarde `backups/account-archive-fortune-bag-2026-11-07.dump`). Vérification avant : mêmes 86 833 lignes que `game.fortune_bag` sur toutes les colonnes sauf `note`, mojibake (Big5 lu en Latin-1) dans l'archive. Base : **108 tables**. Comme `game.fortune_bag` est le contenu des sacs de chance (pas un historique), son commentaire est corrigé.
