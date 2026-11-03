# FIX-A-audit — audit des méthodes réelles (PLAN-ACTION-2 + MASTER-PLAN)

Lecture seule ; seul ce fichier est écrit. Date : 2026-11-02. Méthode : lecture de `tools/`, `docs/`, `artifacts/`,
`tests/`, et captures regardées (`artifacts/shots/20261102-113239-S001-hud-class-inv.png`, `...-113038-S001-kitsune.png`).

## Constats transversaux
1. **Aucune comparaison automatique ni tracée.** `docs/knowledge/testing-strategy.md:12` : « manuel ; pas de diff automatique ».
   Un seul fichier de comparaison existe (`compare-s002-47official-vs-48edenfr.png`). Aucun artefact ne trace une comparaison
   écran par écran aux 122 `ref-shots/`. « Comparés à ref-shots » (lot 5) = non démontré.
2. **Captures en solo, sans serveur** (`capture.sh` = `--solo --shot`, après ~240 frames). HUD : HP 0/0, « Solo », barre d'EXP à 0.
   Les panneaux se remplissent via `--hud-open=` avec des **données de démo** (`EnsureDemo`, `DemoTarget`, "Données de démo"
   dans Bank/ItemMall/Neobank/Archive/Mail/Destiny/Couple) : ça prouve la mise en page, pas le contenu dynamique du jeu.
3. **Auto-attestation maps** (`tools/map-attest/parse_log.py`) : critères = ligne `[MapLoader]`, 0 ERROR/« texture absente »
   dans le log AVANT `[shot]`, spawn non NaN, luminosité/écart-type/nb de teintes de l'image. Ne détecte ni sol fragmenté,
   ni texture mal appliquée, ni mesh au mauvais matériau. Une capture/map, vue réelle = une partie seulement.
4. **Audit textures** (`native-coverage --textures`) : rejoue le *résolveur* (port Core de `ResolveCore`) et décode l'en-tête DDS.
   Prouve « nom → fichier existant et lisible ». Ne prouve PAS : texture liée au bon slot, UV valides, matériau Godot
   effectivement assigné, teinte (CharTint/dye), alpha/cutout. Sync garantie seulement par comparaison du texte de
   `ResolveCore`, pas du reste de la chaîne de rendu.
5. **Tests Core** (`tests/Eden.Core.Tests`) : logique pure hors moteur. Prouvent parsing/tables/protocole, pas le rendu.
6. **Décompilé** : aucune trace dans tools/ ni docs/ de la lecture systématique du décompilé (re/ghidra) comme méthode de
   validation des règles (procs, skills de classe, quêtes). Les décisions viennent des tables INI + portage 4.7.
7. Preuve visuelle regardée contredit le statut : (a) panneau Classe = « En attente de la liste », aucune compétence, zone
   Talents vide ; (b) cavalier du kitsune = silhouette grise sans texture ni couleurs (la capture « kitsune » ne cadre que
   la monture) ; (c) particules du kitsune = traînées/quads plats ; (d) sol dallé S001 : joints/découpes visibles.

## Tableau lot -> méthode réelle -> limite -> statut honnête

### PLAN-ACTION-2
| Lot | Méthode réelle | Limite | Statut honnête |
|---|---|---|---|
| 1 Items | `tools/item-dictionary` extrait les tables en `items.tsv`; test Core `ItemDictionaryTests` (comptes). | Compte/forme seulement. Procs, effets, conditions, « restriction de classe inférée » non vérifiés ni contre le décompilé ni en jeu. Contredit : procs faux (anneau de Silin), sacs d'extension non équipables. | PARTIEL (table = TESTED ; sémantique d'items = contredit par le propriétaire) |
| 2 Textures | `native-coverage --textures` : résolveur + décodage en-tête DDS sur tous NIF/tables. | Prouve existence/lisibilité, pas l'application au rendu. 1 capture N=quelques modèles. Contredit : joueur sans texture/couleurs moches, plantes/champignons sans texture. | PARTIEL (audit fichiers prouvé ; rendu non prouvé ; contredit) |
| 3 Anim. matériaux | Captures kitsune (`--ride-key`) regardées, logs 0 erreur `add_child`, garde `IsFinite` (5404 -> 0 erreurs NaN). | Une monture, un angle, slot 0 seul ; 656 cas non faits ; pas de comparaison à l'original ; la capture montre des particules en traînées plates. Contredit : particules en quads. | PARTIEL, rétrograder |
| 4 Latences | Sonde `--perf`, comptage d'images > 12 ms en 40 s. | Aucune latence de sort mesurée ; avant/après non fait. | IMPLEMENTED (honnête déjà) |
| 5 Panneaux/HUD | `capture.sh` + `--hud-open` solo, données de démo, 20 captures S001/S002. | Pas de comparaison tracée aux ref-shots ; pas de données serveur ; Classe vide dans la capture même ; barre d'EXP/traits héroïques absents. Contredit. | NON PROUVÉ / contredit (au mieux IMPLEMENTED pour la mise en page) |
| 6 Maps | Attestation auto (`map-attest`) 389 codes + 1 capture/map (analyse lum/std) ; `walk-sweep` `--walk-test=10` (distance, y début/fin, nb ERROR) ; matrice portails par tables ; 2 traversées live. | « 389 OK » = chargement + non-plat, pas rendu correct. Aucune détection de sol coupé, textures de props/plantes mal appliquées. Contredit : sol en morceaux, plantes sans texture. Walk = hauteur suivie, pas visuel. | Chargement/spawn/collision : PROUVÉ (logs). Rendu : NON PROUVÉ / contredit. Rétrograder |
| 7 Formats | `native-coverage --all` (parse + DDS) à 100 %. | « lu par le client en jeu » non démontré ; parse != rendu juste. | TESTED (parse) ; « en jeu » non prouvé |
| 8 Quêtes/montures/objets | Inventaire table ↔ fichiers (stems .nif/.kfm/.kf/texture), chaîne item→monture lue dans les tables ; `mount-sweep.sh` (capture par ride key, auto-ok = shot non vide). | Présence de fichiers, pas exactitude visuelle ; sweep sans comparaison. Quêtes : « validées en capture » = layout démo ; aucun test d'état (faites/en cours). Contredit : archive montures incomplète (kitsune de neige blanc), quêtes faites en cours. | PARTIEL ; quêtes contredit |
| 9 Entrées | `--input-test`, `--walk-test`, `--combat-test` (sondes logs). | Sondes réseau/logs, pas d'entrée OS ; hitbox non rejouée (l'aveu y est). | TESTED limité (sondes), hitbox NON PROUVÉ |
| 10 Session live | Sondes `--walk-test/--mob-test/--combat-test` live, EXP +2500 en log, 2 traversées de portail, captures `live-*.png`. | Aucune session interactive, aucun contenu d'inventaire/équipement/quête/skills réel. Procs/sacs/quêtes/traits non couverts. | PARTIEL (pile réseau prouvée) |

### MASTER-PLAN
| Lot | Méthode réelle | Limite | Statut honnête |
|---|---|---|---|
| 0 Fondations | `tools/setup.sh`, `tools/test.sh`, `benchmark.sh` 3 runs | Honnête, MCP non évalué | TESTED |
| 0b EDENFR pur | Casse/CP1252/NativeOnly, `validate-assets.sh` (provenance), capture S001 unique | Une map, une capture ; maintenant contredite (sol, plantes, joueur) | PARTIEL, rétrograder de VISUALLY_VALIDATED (S001) |
| 1-9 | Non démarrés selon le tableau (dépassé par PLAN-ACTION-2 : natif, UI, maps déjà faits en partie) | Plan non synchronisé avec la réalité | NOT_STARTED (document périmé, incohérent avec le plan 2) |

## VISUALLY_VALIDATED à rétrograder
- PLAN-ACTION-2 lot 3 (matériaux/kitsune) -> IMPLEMENTED ; particules en quads contredisent.
- PLAN-ACTION-2 lot 5 (panneaux/HUD, 14 panneaux listés) -> IMPLEMENTED ; Classe contredit explicitement ; aucun diff aux ref-shots.
- PLAN-ACTION-2 lot 6 (maps 389) -> TESTED pour chargement/collision/déplacement ; rendu non validé.
- MASTER-PLAN lot 0b (S001) -> IMPLEMENTED.
- Sous-affirmations : « Aplats de couleur = clos », « captures S228/S229/S231/S401 propres », « kitsune légendaire monté (dorée) » : une capture, pas de comparaison ; kitsune de neige blanc contredit.

## Non vérifié par moi
Je n'ai pas relancé de build, de capture ni de test. Je n'ai regardé que 2 captures. Contradictions du propriétaire non reproduites.
