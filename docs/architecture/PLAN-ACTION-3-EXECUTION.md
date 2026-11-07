# PLAN-ACTION-3 — FEUILLE D'EXÉCUTION

**Préparée le 2026-11-07.** Complète `PLAN-ACTION-3.md` (le « quoi » et le « pourquoi » de chaque lot) par le « dans quel ordre, avec quels garde-fous, comment démarrer ». Une IA qui exécute lit **ce fichier d'abord**, puis la section du lot dans `PLAN-ACTION-3.md`.

## 0. État de départ (baseline figée avant exécution)
| Élément | Valeur |
|---|---|
| Étiquette git locale | `pre-plan3-baseline` (point de retour du code) |
| Sauvegarde base | `artifacts/db-backups/edenfr-2026-11-07-pre-plan3.dump` (pg_dump, 2,4 Mo, conteneur `edengodot-postgres` :5433) |
| Build | `tools/build.sh` : 0 erreur, 0 avertissement |
| Tests | NetTests 939 (+3 ignorés), Eden.Core.Tests 967, Eden.Tests 1 799 : **0 échec** (`tools/test.sh`, ≈ 2 min 20) |
| Disque | 82 Go libres ; `portage/` supprimé ; archive de 9,6 Go dans `archive/portage/` |
| Dette mesurée | `ZoneEntryFlow` = **97 fichiers partiels, 42 119 lignes** (une seule classe) ; 71 tables SQL mortes ; 26/2 407 colonnes commentées ; formule d'EXP éparpillée en 5 endroits ; `IsDungeonNode` défini 2 fois |
| Arbre de travail | ≈ 588 fichiers modifiés **non commités** par le propriétaire : ne jamais `git add -A` ; ne stager que ses propres chemins |

## 1. Protocole d'exécution (valable pour chaque lot)
1. **Fiche de lot** (copier `docs/workflows/task-contract.md`) remplie *avant* de coder : fichiers attendus, tests, preuves, critères.
2. **Un lot = une branche de travail logique, des petits pas réversibles** ; un commit local par lot terminé, **jamais de push**, jamais de `git add -A` ; message en français, ligne d'attribution requise.
3. **Preuve minimale** : `tools/build.sh` + `tools/test.sh` verts ; test automatisé nouveau ; pour tout ce qui se voit : captures **regardées** et parcours **par clics réels** (`@click`), pas par raccourci (`@talk`, `@dlg`, `@open`) ; **deux exécutions concluantes** ; mesures chiffrées pour la perf.
4. **Statuts** (task-contract) : `IMPLEMENTED` → `TESTED` → `VISUALLY_VALIDATED`. Interdit d'écrire « fait » sans preuve collée dans le journal du lot.
5. **Qualité (§0.4 du plan)** : fichier ≤ 400 lignes pour tout code **nouveau** ; aucune valeur de gameplay en dur (registre de config) ; une seule implémentation par règle ; commentaire du *pourquoi* ; revue du diff (`ponytail-review`) avant clôture ; graphe régénéré (`tools/graph/graph.sh`).
6. **Journal** : `docs/temp-plans/2026-11-DD-<lot>.md` (constats, preuves, écarts) + case cochée dans le tableau §3 ci-dessous.
7. **Arrêt et question** si : une hypothèse du plan est fausse, un test existant doit être modifié pour passer, ou une opération est irréversible (suppression de données, migration de base). Sauvegarde `pg_dump` obligatoire avant toute migration SQL.
8. **Fichiers temporaires** : captures d'agents, comparatifs et scripts jetables vont dans des dossiers ignorés (`.gitignore` : `godot-shots/`, `screenshots/`, `web-shots/`, `shots/`, `captures/`, `scratch/`, `_tmp/`, `tmp_*`) ; les références voulues restent dans `ref-shots/`.
9. **Interdits** : modifier `corpus/`, `re/`, `archive/` ; réintroduire une donnée non EDENFR ; écrire « absent des données FR » sans avoir lu la chaîne FR complète (D7).

## 2. Fondations transverses (lot Q) — spécifications complètes : `PLAN-ACTION-3.md` §10 — améliorer la qualité ET ouvrir des possibilités de gameplay
**Règle anti-sur-ingénierie** : une abstraction n'est créée que si elle a **au moins 2 consommateurs dans ce plan** et un test. Chaque ligne ci-dessous indique ses consommateurs et ce qu'elle débloque.
| # | Abstraction | Consommateurs dans le plan | Ce que ça débloque (gameplay / maintenance) |
|---|---|---|---|
| Q1 | **`GameClock`** injectable (deux systèmes de jours : *reset à 06:00* et *jour réel*) | entrées de donjon, quêtes journalières, éphéméride, VIP `until`, contrats `due_date`, recyclage de ville | événements à heure fixe, tests à 05:59/06:00, fin du « minuit local » codé en dur |
| Q2 | **`IRandom`** (graine injectable) | tirage de récolte (A3), rotation de l'éphéméride, drops, réussite de craft | tests déterministes, « jours de chance », rejouer un bug de loot |
| Q3 | **`GameError` + codes → textes localisés** (type `Result<T>`) | refus de récolte hors aire (aujourd'hui silencieux), refus de craft, d'entrée de donjon, admin | plus de refus muets ; messages FR/EN cohérents ; tests sur le code d'erreur |
| Q4 | **Registre de configuration typé** (F1) + **moteur de modificateurs** (F6) | craft, récolte, EXP/drop, donjons, VIP, événements, éphéméride, règles de map | réglage de tout le gameplay sans recompiler ; explications (`Explain`) ; VIP/événements = simples sources |
| Q5 | **Bus d'événements de domaine** (`MobKilled`, `ItemCrafted`, `GatherTick`, `DungeonEntered`, `PlayerDied`, `LevelUp`) | succès, quêtes, compteurs d'éphéméride, audit, statistiques, déclencheurs de donjon | **fin du couplage dans le monstre de 42 000 lignes** : un nouveau système *s'abonne* au lieu de modifier `ZoneEntryFlow` ; idées de gameplay (« tuer 100 mobs d'Angor aujourd'hui = récompense ») sans toucher au combat |
| Q6 | **Moteur Déclencheur → Condition → Action** (données) | mobs cachés/déclenchés (M1), éphéméride (`quand/alors`), quêtes à conditions, événements de scène | scripts de donjon éditables (« tuer X fait apparaître le boss »), sans code par donjon |
| Q7 | **`WeightedPool` / `LootRule` composables** | tick de récolte, drops de mobs, coffres, récompenses d'éphéméride, loteries | un seul modèle de tirage pondéré avec conditions ; nouvelles mécaniques de butin = données |
| Q8 | **Modules de fonctionnalité `IZoneFeature`** (un fichier par fonctionnalité, enregistrement par DI) | Gather, Modifiers, Almanax, Vip, Config, Admin RPC | **stratégie d'étranglement** de `ZoneEntryFlow` : tout nouveau code est un module ; l'ancien est migré quand on le touche ; test d'architecture qui **interdit d'agrandir** `ZoneEntryFlow` |
| Q9 | **Tests d'architecture** (références interdites : Core sans Godot, UI sans règles de jeu, taille des fichiers, pas de `catch {}` vide, pas de `Random.Shared` direct) | tout le code | la qualité devient mécanique, pas déclarative |
| Q10 | **Sonde `@wins` + banc de captures** (liste des fenêtres, couche, recouvrement) | A1, A2, A4, D6, F5 | preuves UI objectives |
| Q11 | **Fixtures idempotentes** (`tools/fixtures/*`) | deux villes (A5), comptes normal/VIP, joueur « sac prêt » | tests rejouables sans commandes GM manuelles |
| Q12 | **`explain` / observabilité** (`/metrics` existant + journaux `[Modifiers]`, `[Gather]`, `[Content]`) | F6, N, D | diagnostiquer « pourquoi cette valeur / ce lag » |
Pistes **non retenues** (trop tôt) : moteur de script embarqué (Lua/JS) ; ECS complet ; microservices supplémentaires — pas de besoin démontré, risque de dette.

## 3. Feuille de route par vagues
Taille : **S** ≤ 1 j · **M** 2-4 j · **L** 1-2 sem. Statut : ☐ à faire · ◐ en cours · ☑ fait.
### Vague 0 — Fondations (Q1-Q3, Q9-Q11) · avant tout le reste
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| Z | Libération d'espace | — | — | ☑ |
| Q-A | `GameClock`, `IRandom`, `GameError`/textes (Q1-Q3) dans `SRV/Eden.Core/Game/Foundation/` + tests | — | M | ☐ |
| Q-B | Tests d'architecture (Q9) + règle « ne pas agrandir `ZoneEntryFlow` » (baseline = 42 119 lignes / 97 fichiers) | — | S | ☐ |
| Q-C | Sonde `@wins` (Q10) dans `GmFileProbe.cs` ; script de fixtures (Q11, squelette) | — | S | ☐ |
### Vague 1 — Bugs joueur (ordre strict)
| Lot | Contenu (voir plan §2) | Dép. | Taille | Statut |
|---|---|---|---|---|
| A1 | Conteneur de fenêtres unique (couche 9 vs 10) + `@wins` avant/après | Q-C | M | ☐ |
| A2 | PNJ de plan/récolte/craft : parcours par clics réels, 15+ PNJ, 2 villes ; libellés FR ; couverture service → fenêtre (B5) | A1, A5 | L | ☐ |
| A5 | Fixtures des 2 villes, recyclage coupé en test, sac/outils/plans prêts, contrats lus **depuis les tables FR** | Q-C | M | ☐ |
| A4 | Panneau « villes de guilde » enrichi (bâtiments, niveaux, PNJ) | A1, A5 | M | ☐ |
| Q-D | Registre de config minimal + moteur de modificateurs (squelette F1/F6, sans migrer les 5 sites) | Q-A | M | ☐ |
| A3 | Récolte : canal 3 s, statut, animations `collect01/02` (association à prouver), tick 30 s, durabilité −1/10 s, annulation au mouvement | Q-A, Q-D, A5 | L | ☐ |
### Vague 2 — Définitions et base de données
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| C1 | `map_kind` (classes §4.1/§4.4) généré + curation ; remplace les 2 `IsDungeonNode` ; règle d'EXP PvE/champ uniquement | Q-A | M | ☐ |
| C2 | Règle « plan de craft » (§4.2) dans une seule librairie + test sur 3 178 recettes | — | S | ☐ |
| C3 | `scope.json` (« présent en jeu ≤ Lv90 ») | C1, C2 | M | ☐ |
| DB-A | Inventaire des 187 tables → décision par table (71 mortes) | — | S | ☐ |
| DB-B | Séparation `eden_live` / `eden_content`, renommages clairs, commentaires, suppression des anciennes bases | DB-A | L | ☐ |
| DB-C | `doc-lint` + `DB-SCHEMA.md` | DB-B | S | ☐ |
| B0 | DDL typé de `eden_content`, `IContentStore`, `seed`, `export`, `history.change` | DB-C | L | ☐ |
### Vague 3 — Contenu unique, hot-reload, réseau
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| B1 | `Eden.Content` (lib partagée), ADR-0003 appliqué | B0 | L | ☐ |
| B2 | Migration `S_*` → contenu FR table par table + rapport de divergence | B1 | L | ☐ |
| B3 | Hot-reload (H0-H1, `ILiveBound`) | B1 | M | ☐ |
| B5 | Couverture service → fenêtre, suppression des stubs | A2 | S | ☐ |
| B6 | Sync de contenu client (manifeste, deltas, cache par hash) | B1, B3 | L | ☐ |
| N | Réseau no-lag : enquête « Broken pipe », scénario de charge « contenu », 50/150/300 bots | B6 | M | ☐ |
| B4 | Hot-reload des layouts/thèmes | A1 | S | ☐ |
### Vague 4 — Configuration, gameplay, VIP, éphéméride
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| F1 | Registre complet (60+ clés), `server.config.txt` validé | Q-D | M | ☐ |
| F6 | Migration des 5 sites d'EXP vers le moteur ; séparation classe/éveil (après vérif. natif) | Q-D, F1 | M | ☐ |
| F2 | Entrées de donjon 10/10, bonus, reset 06:00 | F1, Q-A | M | ☐ |
| F3 | Événements datés (extension) | F1, F6 | S | ☐ |
| F4 | VIP (−25 % temps, +10 % EXP ×3) + icône | F6 | M | ☐ |
| F5 | Éphéméride (21 jours, panneau « battle pass », éditeur) | F3, Q-A, Q-D | L | ☐ |
| M1 | Audit des 4 comportements de mobs (immobile / errance / trajet fixe / caché-déclenché) + correctifs | Q6 (déclencheurs) | L | ☐ |
| Q-E | Bus d'événements (Q5) + moteur Déclencheur/Condition/Action (Q6) + `WeightedPool` (Q7), branchés sur A3 et M1 | Q-A | L | ☐ |
### Vague 5 — Wiki, site, administration par compte
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| D0 | ADR + `accounts.admin_level` + dérivation du privilège à l'entrée | B1 | M | ☐ |
| D1 | Limitless : secrets en environnement, bases actuelles, builds verts | D0 | M | ☐ |
| D2 | `SourceGraph` (drop, boutique, quête, coffre, plan, donjon, PNJ de guilde) | B1, C1-C3 | L | ☐ |
| D3-D4 | Wiki Limitless + live guildes | D2 | L | ☐ |
| D5-D6 | Commandes admin partagées + RPC jeu + panneau admin en jeu | D0 | L | ☐ |
| D7 | Éditeur de contenu (brouillon → diff → release → annulation par l'historique) + éditeur de map (§4.4) + bac à sable | B3, D5 | L | ☐ |
| D8 | Durcissement (2FA, limites, sauvegardes) | D5 | M | ☐ |
### Vague 6 — Anglais
| Lot | Contenu | Dép. | Taille | Statut |
|---|---|---|---|---|
| E0-E1 | ADR langue ; inventaire + glossaire | C3 | M | ☐ |
| E2 | UI / menus / statuts / messages | E1 | M | ☐ |
| E3 | Objets ≤ 90 | E1 | L | ☐ |
| E4-E5 | PNJ, monstres, cartes, quêtes, succès, textes serveur, wiki | E3 | L | ☐ |
| E6 | Rollback FR-only prouvé | E2 | S | ☐ |

## 4. Parallélisation (sessions) — propriété des fichiers
Deux sessions ne touchent **jamais** les mêmes fichiers. Pistes indépendantes après la Vague 0 :
| Piste | Lots | Zone de fichiers possédée |
|---|---|---|
| **UI client** | A1, A2, A4, B4, B5, D6 | `CLI/hud/**`, `scenes/game.tscn`, `CLI/debug/GmFileProbe.cs` |
| **Villes / fixtures** | A5 | `SRV/Eden.Zone/Handlers/GmHallSetupHandlers.cs`, `ZoneEntryFlow.FamilyCity.cs`, `tools/fixtures/**` |
| **Fondations serveur** | Q-A, Q-D, Q-E, F1, F6 | `SRV/Eden.Core/Game/Foundation/**`, `…/Modifiers/**`, `ServerConfig.cs`, `Combat.Rewards.cs` |
| **Base de données** | DB-A→C, B0→B2 | `eden_server/Database/**`, `tools/db/**`, `tools/content/**`, `SRV/Eden.Core/Data/**` |
| **Récolte** | A3 | `ZoneEntryFlow.Gather.cs` (nouveau), `CLI/game/EntityAnimator.cs`, `AnimNames.cs` |
| **Site** | D1-D4 | `eden_server/limitless/**` |
| **Langue** | E* | `content/lang/**`, `tools/content/lang-*` |
Un lot qui doit sortir de sa zone s'arrête et demande.

## 5. Questions qui peuvent encore bloquer (à poser **au moment** du lot, pas avant)
| Lot | Question |
|---|---|
| A3 | L'association pioche/gants/boule ↔ `collect01`/`collect02` n'est pas prouvée : validation par captures montrées au propriétaire si le décompilé ne tranche pas (déjà accepté en principe) |
| F6 | **Tranché (propriétaire, 2026-11-07)** : le jeu sépare bien les bonus d'EXP de classe normale et de classe éveillée → à appliquer tel quel (changement de comportement noté dans le journal du lot) |
| DB-A | **Tranché** : une table morte est supprimée ; une table utile mais peu rentable est optimisée/migrée (fusion, Redis/config, vue) ; plus de question au propriétaire sauf cas ambigu |
| DB-B | Fenêtre de coupure pour migrer `edenfr` → `eden_live` (le serveur doit être arrêté quelques minutes) |
| D1 | Aucun : copie déjà en dépôt |
| E3 | Relecture humaine d'un échantillon d'anglais par vague (qui relit ?) |

## 6. Risques et retours arrière par vague
| Vague | Risque principal | Retour arrière |
|---|---|---|
| 0 | Tests d'architecture trop stricts qui bloquent | seuils relatifs à la baseline, jamais absolus |
| 1 | A1 modifie la scène principale (`game.tscn`) | commit isolé ; `git revert` ; captures avant/après ; `@wins` |
| 2 | Migration de base | `pg_dump` avant ; scripts de migration **réversibles** ; anciennes bases supprimées seulement après 1 semaine de recette |
| 3 | Serveur dépendant de la base au démarrage | export JSONL commité → démarrage sans base (lecture seule) |
| 4 | Changement de comportement d'EXP | test golden « mêmes résultats qu'avant » avant de migrer chaque site |
| 5 | Panneau admin exposé | loopback par défaut ; niveaux ; audit ; 2FA avant exposition |
| 6 | Anglais incohérent | rollback FR-only (plan §6.4) ; EN dans un pack séparé |

## 7. Démarrage (première session d'exécution)
1. Vérifier la baseline : `git tag | grep pre-plan3-baseline`, `docker ps` (postgres 5433, redis 6380), `tools/build.sh`, `tools/test.sh`.
2. Lire `PLAN-ACTION-3.md` §0, §0.4, puis §2 A1.
3. Exécuter **Q-B** (tests d'architecture, baseline `ZoneEntryFlow`), **Q-C** (`@wins`), puis **A1** avec captures avant/après.
4. Enchaîner **A5 → A2 → A4** (Vague 1) ; ouvrir en parallèle la piste « Fondations serveur » (Q-A, Q-D) dans une session séparée.
5. Après chaque lot : cocher la case du §3, écrire le journal, commit local ciblé.
