---
name: code-graph
description: Se repérer dans le code d'eden-godot ET du portage Godot 4.7 (référence) via les graphes graphify (et reagraph pour le décompilé) avant tout grep ou lecture large. À utiliser pour « où est », « qui appelle », « comment marche X dans le portage », « équivalent de Y dans l'ancien client », et en début de toute tâche de code.
---

# Graphes de code (graphify + reagraph)

Deux graphes, **toujours consultés ensemble** : le code actuel, et le code
de référence du portage 4.7.

| Graphe | Mise à jour | Rapport à lire d'abord | Requêtes |
|---|---|---|---|
| eden-godot | `tools/graph/graph.sh` | `graphify-out/GRAPH_REPORT.md` | `tools/graph/graph.sh query "…"`, `path "A" "B"`, `explain "X"` |
| portage 4.7 (lecture seule) | `tools/graph/graph.sh portage` | `tools/graph/portage/src/graphify-out/GRAPH_REPORT.md` | `tools/graph/graph.sh portage query "…"` |
| décompilé `re/ghidra` (call-graph seul) | `tools/graph/re-graph.sh` | `tools/graph/reagraph.json` (68 749 nœuds) | `tools/graph/.venv/bin/graphify query "…" --graph tools/graph/reagraph.json` |
| knowledge (wiki md, hors code) | `tools/graph/.venv/bin/graphify update knowledge` | `knowledge/graphify-out/` (2 139 nœuds) | `tools/graph/.venv/bin/graphify query "…" --graph knowledge/graphify-out/graph.json` |

## Méthode
1. Lire les deux `GRAPH_REPORT.md` (communautés et nœuds centraux) avant
   de faire un `grep`.
2. `query "<symbole ou concept>" --budget 1500` sur eden-godot, puis la
   même requête sur le portage. On voit ainsi ce qui a divergé (ex. un
   `FsmFile` repris de Flax) et où se trouvait la logique d'origine.
3. `explain "Classe.Methode()"` pour les voisins (appelants et appelés) ;
   `path "A" "B"` pour la chaîne d'appels.
4. Ne lire les fichiers qu'ensuite, et seulement les lignes désignées
   (`src=… loc=L…`).
5. **En fin de tâche** qui modifie le code : `tools/graph/graph.sh` (local,
   sans LLM, ~45 s).

## Règles
- Le portage n'est jamais modifié : son graphe est construit sur un
  miroir en liens physiques (`tools/graph/portage/src`, code et `.md`
  seulement).
- Un nœud du graphe est un indice, pas une preuve : vérifier dans le
  fichier avant d'agir.
- Le graphe eden-godot exclut `knowledge/`, `ref-shots/` et
  `eden_godot/docs/client-assets/` (`.graphifyignore`) : pour le wiki,
  utiliser le graphe knowledge ; pour le catalogue ref-shots, le graphe
  du portage (ou lire le fichier directement). Les synthèses gameplay
  sont locales : `docs/reference/harvest/` (dans le graphe eden-godot).
- Pour le décompilé, `re/reindex` (lookup/search/strings/xref — voir
  `re/AGENTS.md`) répond mieux que le graphe aux recherches par nom,
  chaîne ou adresse ; reagraph ne sert qu'à la structure d'appels
  (noms `lapis::*` serveur, `FUN_*` client strippé).
