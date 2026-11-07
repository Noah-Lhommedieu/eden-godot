# Prompt de reprise — Plan d'action 3 (état au 2026-11-07, fin de session)

> À coller tel quel à la nouvelle IA. Rédigé par la session précédente ; tout ce qui est dit « fait » doit être **revérifié** (étape 1) avant de continuer.

## 0. Contexte et règles

Tu reprends l'exécution du **plan d'action 3** du projet Eden Eternal EDENFR : client Godot 4.8 .NET (`eden_godot/`), serveur C#/.NET 10 (`eden_server/`), dépôt `/home/ster2lune/Bureau/reverseden/eden-godot`. Le propriétaire parle français ; réponds en français.

Lis d'abord : `CLAUDE.md`, `AGENTS.md`, `docs/architecture/PLAN-ACTION-3.md` (quoi/pourquoi), `docs/architecture/PLAN-ACTION-3-EXECUTION.md` (tableau des lots ☐/◐/☑ + journal en bas), `docs/knowledge/performance-baselines.md`, `docs/architecture/adr/ADR-0004-langue-en-overlay.md`, `docs/temp-plans/2026-11-07-m1-audit-mobs.md`.

Règles non négociables du propriétaire :
- **Pas de git** de ta part (il commit/push lui-même). Aucun commit, aucun push. ~128 fichiers modifiés/non suivis dans le dépôt : c'est normal.
- **EDENFR pur** : source de vérité = FR (`corpus/fr-full`). `corpus/`, `re/`, `portage/` en lecture seule. L'anglais est une couche dérivée (ADR-0004).
- « Fait » sans preuve = non fait : tests (`tools/test.sh`, ou `cd eden_server/server && dotnet test tests/Eden.Tests`), captures (`@shot`), vérification en jeu, chiffres. Dis honnêtement ce qui n'est pas prouvé.
- Qualité « irréprochable » : fichiers neufs ≤ 400 lignes, pas de nombres magiques, une seule implémentation par règle, tests. **`ZoneEntryFlow*.cs` ne doit pas grossir** (test d'architecture, baseline `tests/Eden.Tests/Data/architecture-baseline.tsv`, à ne baisser que via `EDEN_ARCH_REGENERATE=1`) : tout nouveau code = module `Eden.Zone/Features/...` (`IZoneFeature`). Pas de `DateTime.Now`/`Random` directs (utiliser `GameTime.Clock`, `IRandom`). Pas de `catch {}` vide.
- Le propriétaire veut des **salves de plusieurs tâches à la fois**, pas de micro-étapes ; mettre à jour `PLAN-ACTION-3-EXECUTION.md` après chaque avancée.
- Outils : moteur `engine/current/godot` (jamais celui du PATH) ; ne JAMAIS `pkill -f "godot --path"` (tue ton propre shell, idem `pkill -f Limitless.Api`) : utiliser `pkill -x godot` et `ps`/`kill <pid>`. Ne jamais lancer une commande qui lit stdin (`cat >> /dev/null` bloque le shell).

## 1. Revérification obligatoire du travail déjà fait (à faire en premier)

1. `cd eden_server/server && dotnet build Eden.sln -nologo` → 0 erreur ; `dotnet test tests/Eden.Tests -nologo` → attendu **2014 tests verts** (dont tests d'architecture). Si un test d'archi échoue : ne pas relever le seuil, déplacer le code dans un module.
2. `tools/build.sh` (client + Core + serveur) → vert.
3. Base : conteneur `edengodot-postgres` (:5433) ne doit contenir que `eden_live`, `eden_content`, `postgres`. `python3 tools/db/doc-lint.py` (eden_live) et `--db eden_content` → 0 manque ; dette connue : 272 colonnes « sens natif non documenté ».
4. Démarrer le serveur : `cd eden_server && ./start-native.sh` (met `EDEN_TOWN_RECYCLE=off`) ; client : `tools/run.sh -- --login=test123:test123 --channel --char=TestBoy --cmd-file=/tmp/gm.cmd` (commandes `@shot`, `@click x y`, `@walkx`, `@dj`, `@inv`… écrites dans `/tmp/gm.cmd` APRÈS le log `[gmfile] canal actif` ; coordonnées = pixels de la capture 1600×900 ; `mkdir -p /tmp/ui` pour les captures). Le perso TestBoy peut être coincé sur un rebord (utiliser `@walkx 101 91` pour le portail de donjon de Monts Calcaires). Pour tester le décompte de donjon sans passe dev : `EDEN_DEV_DUNGEON_PASS=off`.
5. Rejouer les preuves clés (une ligne chacune) : `//vip test123 7` puis `//vip test123 off` en jeu ; badge VIP et bouton « Éphéméride » (panneau 21 jours) et « Admin » visibles dans le HUD ; give/remove admin sur connecté via canal HTTP interne (`EDEN_ADMIN_SECRET=… ./start-native.sh`, POST `127.0.0.1:7660/admin/exec` signé HMAC-SHA256 `time.body`) ; wiki `http://localhost:5154/wiki/` (Limitless.Api : `cd eden_server/limitless/web/backend && dotnet run`, chaînes `ConnectionStrings__Content/Live` + mot de passe lu dans `eden_server/.env` — ne jamais l'afficher) ; `tools/content/rollback-fr-only.sh --dry-run`.
6. Relire `git status` pour repérer des fichiers inattendus, et signaler tout écart entre ce prompt et la réalité avant de coder.

## 2. Ce qui a été fait (état déclaré, à revérifier)

**Fondations serveur (`Eden.Core/Game/…`)** : `Foundation` (IGameClock reset 06:00 + `GameTime.Clock`, IRandom, GameError/Result, errors.tsv), `Modifiers` (clés typées, résolveur Set→×→%→+flat→clamp, sources Static/Config/Almanax/Vip/Charm ; `ServerConfig` avec règles `Set./Mul./AddPct./AddFlat./Cap.` et événements `Event.<n>.Start/.End/règles`), tests d'architecture, `Events` (DomainEventBus + 6 événements ; publiés : MobKilled, LevelUp, DungeonEntered, PlayerDied, GatherTick, ItemCrafted), `Rules` (RuleEngine JSON `content/rules/triggers.json`, actions log/counter), `Loot` (WeightedPool + LootRule, branché sur le tick de récolte seulement), `Calendar` (AlmanaxCalendar, `content/rules/almanax.json`), `Admin` (voir D5-D8), `Content` (ContentScope, TranslationChecks, ContentStore Pg/JSONL).
**Gameplay** : récolte (A3, paquet 9063), détail ville (9064), EXP via moteur (F6, classe normale/éveillée séparées), entrées de donjon 10/10 + reset 06:00 (F2, prouvé en jeu), VIP (F4 : `account.vip_status`, `VipDirectory` rechargé /10 s, `//vip`, badge HUD paquet 9065, craft −25 % via `CraftTimes`), éphéméride (F5, paquet 9066 + panneau client), quêtes journalières alignées sur le jour de reset.
**Client** : conteneur de fenêtres unique (A1), panneau villes de guilde (A4), sondes `@wins`…, `VipBadge`, `AlmanaxPanel`, `AdminPanel` (9067/9068).
**Données** : DB-A (93 tables mortes supprimées), DB-B (`edenfr`→`eden_live`, anciennes bases supprimées après sauvegarde `eden_server/Database/backups/db-b-2026-11-07/`, schéma `game` éclaté en 9 schémas — migration 032 + rollback), migrations 027→036 (dont 035 `admin_level`, 036 `admin.audit` + `totp_secret`), `eden_content` (9+ tables typées, seed idempotent `eden_server/server/tools/Eden.ContentSeed`, export JSONL `tools/content/export.py` → `content/export/`, scope `content/rules/scope.json`), `tools/db/{inventory,doc-lint,doc-tables,doc-columns}.py`.
**Admin/wiki (D)** : D0 (admin par compte), D2/D3-D4 (wiki `Limitless.Api` `ContentController` + `/wiki/` + page Next.js `app/wiki/content` + live guildes), D5-D6 (couche `Eden.Core.Game.Admin`, RPC 9067/9068, `AdminOpsController` du site), D7 partiel (brouillon→diff→release→annulation), D8 (débit, TOTP, quatre yeux, sauvegarde de masse, audit chaîné).
**Anglais (E)** : ADR-0004, tables `text.translation/glossary`, `TranslationChecks`, 316 cartes traduites (`auto`, non relues), rollback FR-only prouvé.
**Réseau (N)** : `tools/perf/summarize-load.py`, `load-test.sh` réparé, mesures publiées, diffusion cosmétique non bloquante (326).

## 3. Reste à faire (ordre suggéré, par salves)

1. **Perf / N** : voir §4 (boucle de zone par carte). Au minimum : cadence de vue adaptative et métrique durable `eden_zone_command_ms{ncid}`.
2. **E (anglais)** : traduire par vagues (objets ≤ Lv90 en premier : 5 065 noms listés par `tools/content/lang-inventory.py` → `content/lang/en/to_translate.tsv` ; puis PNJ 1 387, monstres 4 323, quêtes 2 237, UI E2) en gardant glossaire + `TranslationChecks` ; relecture humaine à organiser (question ouverte propriétaire) ; câbler la langue côté client (`TextResolver`, pack `lang.en.epk` séparé) et serveur (`Content.Languages=fr` = rollback).
3. **B1/B2/B3/B6 (contenu partagé)** : lib `Eden.Content` partagée client/serveur, migration des tables `S_*` US vers le contenu FR, hot-reload, sync de contenu client. `ZoneServices` ne lit pas encore `IContentStore` (il lit toujours les INI).
4. **F1** : registre complet de config (60+ clés) validé ; **Q-D/Q-E** à finaliser (LootRule sur les drops de mobs ; M1 : moteur de déclencheurs pour mobs cachés/déclenchés, audit des 4 comportements fait mais sans correctif ; « immobile » non prouvé).
5. **D1** (Limitless : secrets en env, builds `npm run build`), **D7** reste (éditeur de map §4.4, bac à sable), D8 reste (sauvegarde `.bak` de `server.config.txt`, 2FA à la connexion du site).
6. **B4/B5**, DB-C (272 colonnes à documenter vraiment), `docs/reference/DB-SCHEMA.md` généré, regénérer le graphe en fin de tâche (`tools/graph/graph.sh update`).
7. Lots historiques non repris : craft guilde/jeu (Plan 3 §4.2), 4 comportements de mobs non éditables, animations `collect01/02` à prouver par captures (A3).

## 4. Chantier majeur expliqué : une boucle de zone par carte/instance

Aujourd'hui `ZoneLoop` est **un seul fil** (acteur) qui exécute toutes les commandes de tous les joueurs de toutes les cartes + le tick 100 ms. Mesuré (docs/knowledge/performance-baselines.md) : ≈ 0,8 ms par commande, plafond ≈ 1 250 commandes/s ; 300 bots sur une carte saturent (attente de commandes p50 137 ms). Solution : une boucle (file de commandes + fil) **par carte/instance**, donc N cœurs utilisés. Implique : (a) isoler l'état par carte (aujourd'hui `ZoneEntryFlow` + `_states` + `ZoneLiveWorld` sont partagés ; verrous ou propriété exclusive par boucle) ; (b) router chaque paquet vers la boucle de la carte du joueur ; (c) gérer les opérations inter-cartes (migration de carte, téléport, groupe, chat monde, marché, guilde) par messages entre boucles ; (d) refaire les accès DB/Redis qui supposent un seul fil ; (e) tests de concurrence + rejeu 50/150/300 bots. Gain réel seulement si les joueurs sont répartis sur plusieurs cartes ; une foule sur UNE carte reste limitée (d'où cadence de vue adaptative / plafond de joueurs visibles). Gros chantier : à planifier avec ADR avant de coder.

### 4.1 Méthode imposée par le propriétaire (« comment le faire sans casser »)

1. Un **ADR** qui liste TOUS les accès à l'état partagé (`_states`, `ZoneLiveWorld`, `Sessions`, caches, tâches de fond, écritures différées DB/Redis, `RunOnLoopAsync`/`PostOrRunAsync`/`ScheduleOnLoop`), avec pour chacun : propriétaire cible, risque, stratégie.
2. Commencer par les **instances privées de donjon**, où les joueurs sont isolés par nature (le moins risqué), avant les cartes ouvertes.
3. Un mode **« strict » en test** qui échoue si un fil non propriétaire touche l'état d'une carte.
4. Garder une **option pour revenir à une boucle unique** (drapeau d'environnement, testé dans la CI).
5. **Rejouer les runs 50, 150 et 300 bots à chaque étape** (`eden_server/tools/load-test.sh` + `tools/perf/summarize-load.py`) et comparer au tableau de `docs/knowledge/performance-baselines.md`.

### 4.2 Exigence : ZÉRO régression, uniquement un gain

Objectif produit : MMORPG avec **50 vrais joueurs actifs sans pause**, simulation de **300+** pour être certain que tout tient, côté serveur ET client. Cette mise à jour est une optimisation de qualité de code et de capacité : **elle ne doit changer aucun comportement observable**. Donc, avant d'écrire du code multi-boucle :

- **Filet de sécurité d'abord** : écrire des tests de caractérisation (golden) de ce que fait aujourd'hui le serveur sur une boucle unique — séquences de paquets par session (ordre, contenu) pour entrée de zone, déplacement, combat, mort/résurrection, butin, quêtes, donjon, groupe, chat, marché, téléport. Les rejouer à l'identique après chaque étape ; toute différence d'octets = arrêt et analyse.
- **Rejeu différentiel** : enregistrer des sessions de bots déterministes (graine `IRandom` + `FakeGameClock`) et comparer, paquet par paquet, boucle unique vs multi-boucle (ordre garanti PAR SESSION, jamais d'inversion pour un même joueur).
- **Mode ombre (shadow)** : exécuter la nouvelle répartition en observation (assertions de propriété, journal des accès hors propriétaire) AVANT de l'activer.
- **Déploiement progressif** : drapeau par type de carte (instances de donjon → instances partagées → cartes ouvertes → villes), retour immédiat à la boucle unique, jamais de big-bang.
- **Invariants vérifiés automatiquement** : un joueur n'est jamais exécuté par deux boucles ; une entité (mob, trésor, PNJ) a un seul propriétaire ; les messages inter-boucles sont ordonnés et idempotents ; pas d'interblocage (pas de verrou pris dans deux boucles, délais bornés + alerte) ; fermeture/déconnexion nettoie toutes les boucles.
- **Client** : vérifier en jeu réel (captures `@shot`, sondes) que rien ne change côté client — entrée en zone, déplacements, sorts, mort, changement de carte, donjon, fenêtres — et exécuter un scénario multi-clients (au moins un vrai client Godot parmi des bots) pendant le run 300+.
- **Critères d'arrêt (rollback automatique de l'étape)** : un test golden qui diffère ; une régression de latence (attente de commandes p95 supérieure au tableau de référence à charge égale) ; une course détectée par le mode strict ; une déconnexion non expliquée dans un run ; une erreur dans les journaux absente de la référence.
- **Critères de succès** : mêmes sorties qu'avant (golden verts, 2014+ tests verts), 150 bots : attente de commandes p95 < 100 ms et 0 coupure ; 300 bots répartis sur plusieurs cartes/instances : tick p99 < 50 ms, attente p95 < 100 ms, 0 coupure ; 300 bots sur UNE carte : pas pire qu'avant (documenter la limite et les mitigations : cadence de vue adaptative, plafond de joueurs visibles — décision de jeu à valider avec le propriétaire).
- **Ne PAS commencer ce chantier** tant que : l'ADR n'est pas validé par le propriétaire, le filet de tests de caractérisation n'est pas en place, et les gains moins risqués (cadence de vue adaptative, métrique `eden_zone_command_ms{ncid}`) n'ont pas été mesurés — ils peuvent suffire pour 50 joueurs réels (≈ 15 % de charge mesurée).

## 5. Points d'honnêteté / pièges connus

- Rien n'est commité ; ne pas écraser les modifications existantes du propriétaire.
- Traductions EN = produites par IA, statut `auto`, jamais relues.
- `scope.json` exclut désormais placeholders/noms chinois (316 cartes, 4 323 monstres, 1 388 PNJ, 5 065 objets, 2 237 quêtes).
- Passe dev de donjon (`DevPassBuffId` 65002) désactive le décompte : provisoire.
- Test `ItemProcDumpTests.ProcTextIsPerItem` a déjà échoué une fois (dossier `/tmp/claude-1000/` absent) puis passé : cause non vérifiée.
- Les 3 premiers lots de modification de `ZoneEntryFlow` ont été compensés par réduction de commentaires/lignes pour respecter la baseline ; le nouvel adaptateur `Features/Admin/ZoneFlowAdminAccess.cs` est un `partial class ZoneEntryFlow` placé côté module.
- Le canal HTTP admin et le site Limitless nécessitent `EDEN_ADMIN_SECRET` / `Admin:Secret` ; le login Limitless d'origine lit `fnaccount` (supprimée) : non fonctionnel tel quel (D1).
- Aucune clé/mot de passe en clair ne doit apparaître dans le dépôt ou les réponses.

## 6. Première réponse attendue

Résume en 10 lignes ce que tu as compris, liste les écarts éventuels trouvés à la revérification (§1), puis propose la prochaine salve (≥ 4 tâches) et enchaîne.
