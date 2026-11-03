# Contrat de tâche agent

À remplir **avant** d'implémenter, pour toute tâche de plus de quelques
lignes. Pour une sous-tâche déléguée, le contrat est le brief.

```text
TASK:
GOAL:                       comportement attendu, invariants
CONTEXT / FILES TO READ:
ENGINE / .NET:              sortie de tools/setup.sh
CONSTRAINTS:                EDENFR seul, lecture seule corpus/re/portage, liens physiques assets…
OUT OF SCOPE:
PROPOSED DESIGN:
FILES EXPECTED TO CHANGE:   (fichiers possédés ; personne d'autre n'y touche en parallèle)
TESTS TO ADD OR RUN:
RUNTIME / VISUAL VALIDATION: tools/capture.sh <map> + comparaison ref-shots/
PERFORMANCE BASELINE:        tools/benchmark.sh avant (si perf concernée)
ACCEPTANCE CRITERIA:
RISKS / OPEN QUESTIONS:
DELIVERABLES:
```

## Boucle

Inspecter → comprendre → planifier → petit changement réversible → tester →
exécuter dans le moteur épinglé → observer (logs, capture, métriques) →
relire (régressions, duplication, couplage) → documenter (ADR ou knowledge) →
livrer.

Pour la performance : profiler d'abord, puis un seul changement par
mesure. On compare `frame_p50_ms`, `frame_p95_ms` et `gpu_p50_ms` sur le
même scénario, avec N ≥ 3 runs (`tools/compare.sh`). Un gain qui dégrade le
rendu est refusé.

## Livraison (format)

```text
STATUT:        NOT_STARTED | IN_PROGRESS | IMPLEMENTED | TESTED | VISUALLY_VALIDATED |
               PERFORMANCE_VALIDATED | COMPLETED | BLOCKED
RÉSUMÉ:
FICHIERS:
DÉCISIONS:     (+ ADR si contrat public)
COMMANDES:     exactes
RÉSULTATS:     sorties de tests, chemins des captures et benchs
LIMITES:
RISQUES:
```
