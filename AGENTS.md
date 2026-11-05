# AGENTS.md — eden-godot

Règles pour tout agent (Claude, Devin, Codex…). `CLAUDE.md` n'en est qu'un
démarrage rapide. En cas de conflit, ce fichier gagne.

## Le projet

Client **Eden Eternal EDENFR** sous **Godot 4.8-beta2 .NET (C#/.NET 10)**, plus
le serveur C# recodé. Il est issu du portage Godot 4.7 (`../portage/`, référence
**lecture seule**) et sert de banc d'essai :

1. **EDENFR pur** : la seule donnée est `corpus/fr-full` ; l'anglais est en pause ;
2. Godot 4.8-beta2 : performance, stabilité, facilité de mise à jour du moteur ;
3. nouvelles méthodes de travail : contrat de tâche, preuves, ADR, skills.

Git local actif depuis 2026-11-03 (remote GitHub à renseigner) ; gros dossiers exclus par .gitignore.
modification se fait donc avec prudence, en petits pas réversibles.

## Avant toute tâche (dans cet ordre)

1. `docs/architecture/MASTER-PLAN.md` : le lot en cours et son état.
2. `docs/architecture/ARCHITECTURE.md` : où vit le code.
3. `docs/knowledge/godot-version-matrix.md` et `known-engine-limitations.md`.
4. `docs/workflows/EDENFR-GAPS.md` si la tâche touche des données ou des assets,
   et `knowledge/README.md` si elle touche le gameplay (table « Où chercher
   quoi » : wikis, `ref-shots/` + catalogue, tables `data/db/*.ini`,
   décompilé `re/`, synthèses `docs/reference/harvest/`).
5. **Les graphes** (skill `code-graph`) : `graphify-out/GRAPH_REPORT.md`
   (eden-godot) et `tools/graph/portage/src/graphify-out/GRAPH_REPORT.md`
   (portage 4.7 — son graphe couvre aussi le code/docs 4.7, dont le
   catalogue ref-shots exclu du graphe eden-godot). Ensuite `tools/graph/graph.sh [portage]
   query "…"` **avant** tout grep ou lecture large. Pour le décompilé `re/` :
   `re/reindex` (règles `re/AGENTS.md`), reagraph ne sert qu'aux chaînes
   d'appels. Pour le wiki : le graphe `knowledge/graphify-out`.
6. Le skill de la tâche (`.agents/skills/<nom>/SKILL.md`), et la règle de
   méthode `.agents/rules/ponytail.md` (skill `ponytail` : la plus petite
   modification complète).
7. La doc officielle ciblée (versionnée 4.8 ; ne pas copier `/latest/` sans
   vérifier sur le binaire).
8. Le code et les tests existants.

Les gros dossiers ne se chargent jamais en entier : `corpus/`, `re/`,
`knowledge/`, `docs/legacy/`, `eden_godot/docs/archive/`, `artifacts/`.

## Règles cardinales

- **EDENFR seulement.** N'utiliser ni `corpus/official`, ni le merge EN/US,
  ni Reforged, ni `../portage/corpus`. Un asset dérivé sans source dans
  `fr-full` est interdit : `tools/validate-assets.sh` doit passer. Une donnée
  absente de fr-full est consignée dans `docs/workflows/EDENFR-GAPS.md`, sans
  bouche-trou inventé.
- **Lecture seule** : `corpus/`, `re/`, `../portage/`.
  `godot_project/assets/` est fait de **liens physiques** vers les dérivés de
  portage : un outil qui écrit dedans doit d'abord **délier**
  (`os.unlink`) le fichier, sinon il modifie aussi `portage/`
  (ADR-0004).
- **Moteur épinglé** : n'utiliser que `engine/current/godot` (via
  `tools/env.sh`), jamais le `godot` du PATH (c'est le 4.7.2 du portage). Pas
  de mise à jour moteur ou addon hors `tools/engine-update.sh`.
- **Un seul client graphique** à la fois : `flock /tmp/godot.lock` (déjà
  fait par les scripts).
- **Code pur et code moteur.** Le code sans `Godot.*`
  (`scripts/{net,protocol,session,data,vfs,…}` et `src/Eden.Core`) compile
  et se teste hors moteur. Les mutations de scène se font sur le thread
  principal.
- **Pas de `catch {}` muet**, pas de repli qui masque une corruption, pas de
  warning nouveau non expliqué.

## « Fait » = prouvé

Un statut se déclare avec ses preuves (`docs/workflows/task-contract.md`) :

| Statut | Preuve minimale |
|---|---|
| `IMPLEMENTED` | build vert |
| `TESTED` | `tools/test.sh` vert, avec un test qui couvre le cas |
| `VISUALLY_VALIDATED` | `tools/capture.sh`, image **regardée** et comparée à `ref-shots/` |
| `PERFORMANCE_VALIDATED` | `tools/benchmark.sh` avant/après, puis `tools/compare.sh` |
| `COMPLETED` | toutes les preuves applicables, limites écrites |

Un `--headless` ne prouve pas le rendu. Un compte rendu d'agent ne prouve
rien sans sortie de commande ni image.

## Commandes

```bash
tools/setup.sh                  # vérifie versions, corpus, liens, docker
tools/build.sh [Release]        # client + Core + serveur
tools/test.sh [--no-server]     # NetTests (smoke 4.8 inclus) + Core + serveur
tools/run.sh -- --map=S001 --solo
tools/capture.sh S001 [nom]     # → artifacts/shots/
tools/benchmark.sh S001 [runs] [s]   # → artifacts/benchmarks/<ts>-S001/summary.env
tools/compare.sh <baseline> <candidat>
tools/validate-assets.sh        # provenance EDENFR des dérivés
tools/engine-update.sh <zip> [tpz]   # nouveau moteur + validation + rollback auto
tools/graph/graph.sh [portage] [query "…"|path A B|explain X]   # graphes de code
```

Pile serveur : `cd eden_server && docker compose up -d postgres redis &&
./start-native.sh`. PG17 tourne sur le port **5433** (base `edenfr`, volumes
`edenflax_*` repris). Comptes de test : `admin`/`adminmdp`, `test123`/`test123`.

## Parallélisme entre agents

Un fichier, une scène, un `.csproj` ou un contrat public n'a qu'un seul
éditeur à la fois. Changer une interface partagée exige un ADR dans
`docs/decisions/`. Un agent remonte ce qu'il ne sait pas au lieu de
l'inventer.

## Fin de tâche

- Code modifié : régénérer le graphe (`tools/graph/graph.sh`, ~45 s,
  local, sans LLM).
- Mettre à jour la note `docs/knowledge/` concernée, avec un statut
  `verified`, `hypothesis` ou `deprecated`, la version testée et la date.
- Écrire la livraison selon `docs/workflows/task-contract.md` : commandes
  exactes, résultats, captures ou benchs, limites.

## Audits de référence (`docs/knowledge/`)

À lire avant de toucher classes, skins, enchants, protocole ou formules.
Vérifié le 2026-11-03 : les 15 fichiers existent ; les constats de code citent
`fichier:ligne` (statut *verified*), l'impact in-game reste *hypothesis*
(audits statiques) : re-vérifier la ligne avant d'éditer.

| Fichier | Contenu |
|---|---|
| `known-bugs.md` | **Index maître : 24 bugs** (KB-01…KB-24) par sévérité, fichier:ligne + fix + test |
| `class-bug-fixplan.md` | Ordre d'attaque F1-F12 (préalable H1 « panneau vide » tranché) |
| `class-skins-audit.md` / `class-skins-audit-v2.md` | Audit chaîne skins, puis re-audit post-édits de l'autre IA |
| `class-skills-audit.md` | Audit compétences classe (+preuve H1 `--class-mock`) |
| `rankbuff-server-audit.md` | `c_classrankbuff` vs `RefreshJPAward` natif |
| `class-skills-server-audit.md` | Chaîne serveur sorts de classe |
| `schema-drift-audit.md` | **Bug majeur** : garde-fou mort `EnchantParser.cs:84` (`row.Length == 60` jamais vrai, rows FR = 61 cellules) |
| `equip-visuals-audit.md` | Chaîne K-codes visuels équipements |
| `enchant-tables.md` | Schéma `c_enchant`/`t_enchant` décodé |
| `protocol-map.md` / `protocol-partials.md` | Carte opcodes + 57 manques natifs émis côté zone |
| `native-constants.md` / `-2` / `-3` | Formules codées en dur (stats, XP, heal, dégâts) |
| `live-vs-test-visual-parity.md` | Écart rendu live (`run.sh --char`) vs bancs de test (2026-11-03) |
| `zone-spells-audit.md` | Sorts de zone : formes natives 0-6 décodées, routage NCID, 6 fixes client (Z1-Z6), géométrie serveur restante |
| `backlog-2026-11-03.md` | Backlog demandé par le propriétaire (FX, montures, skins, sorts de zone…) |

## Vérification visuelle : la caméra compte (règle propriétaire 2026-11-03)

Un joueur regarde bien plus **de dessus** que de côté. Prouver un FX avec la
mauvaise vue fausse le verdict.
- Animations de cast / sorts : vue **diagonale en hauteur** (dessus + dos du joueur).
- Animations « plates » (Cyclone, cercles au sol, timed cast) : vue **du dessus**.
- Vues de côté : réservées aux montures, skins de costume, silhouettes.
- Tester aussi en jeu réel (`tools/run.sh -- --login=test123:test123 --channel --char`),
  pas seulement avec un banc dédié (ex. texture bug invisible en banc).
- Un mob qui passe dans le cadre ne justifie pas de valider/invalider ; refaire la capture.
- Définitions status/buff/nerf/BP/drops : `docs/knowledge/definitions.md`.
