# Livraison 2026-11-02 — création d'eden-godot (Godot 4.8-beta2, EDENFR pur)

STATUT : **TESTED** pour le socle. **VISUALLY_VALIDATED** sur S001 et S002
(solo), avec les lacunes listées. Le live serveur n'a pas été testé sur 4.8.

## Ce qui a été fait

1. **Archive Flax** : `~/Téléchargements/eden-flax-2026-11-02.7z`, 3,35 Go
   pour 15,16 Go (7z LZMA2, dictionnaire 512 Mo, solide). Choix sur
   échantillon de 300 Mo : 7z 69,9 Mo, xz -9e 74,6 Mo, zstd -22 79,2 Mo.
   `7z t` OK, 94 670 entrées, même nombre que sur disque, `.git` compris.
   Le dossier eden-flax est ensuite parti à la corbeille.
2. **Copie de l'essentiel** (sans git, sans `assets/` 27 Go, sans `dist/`
   18 Go, sans skills ni `.claude` de l'ancien projet) :
   - client `godot_project`, tools et docs du portage ;
   - serveur EDENFR et `src/Eden.Core` avec ses 890 tests, issus d'eden-flax ;
   - `corpus/fr-full` et `re/` en liens physiques ;
   - `knowledge/`, `ref-shots/`, docs Flax (`docs/legacy`).
3. **Moteur 4.8-beta2** : `engine/current`, SDK `4.8.0-beta.2`, `net10.0`,
   nupkgs locaux, templates installés.
4. **EDENFR pur** (ADR-0002) : résolution sans casse en DFS, `t_*.ini` en
   CP1252, `NativeOnly`, `FsmFile` V7 (Flax) avec lecture séquentielle sur
   grille régulière, retrait des layouts Reforged, tests recalés.
5. **Assets** (ADR-0004) : 43 321 liens physiques, 179 reconversions,
   40 PNG d'UI, 37 390 dérivés exclus ; convertisseurs « délier avant
   d'écrire ».
6. **Méthode** : AGENTS/CLAUDE, 4 ADR, 7 notes knowledge, contrat de tâche,
   lacunes, 7 skills, 10 scripts `tools/`.
7. **Pile Docker** reprise : projet `edengodot`, mêmes volumes `edenflax_*`,
   base `edenfr` intacte.

## Résultats

| Vérification | Résultat |
|---|---|
| `tools/setup.sh` | 8/8 ok |
| build client, Core, serveur | 0 erreur, 0 warning |
| NetTests | 711 ok, 3 skip, 0 échec (smoke headless 4.8 inclus) |
| Eden.Core.Tests | 890/890 |
| Serveur | 1429/1429 |
| `tools/validate-assets.sh` | 43 594 dérivés, 0 sans source fr-full |
| `tools/engine-update.sh` (même zip) | 71 s du zip aux tests verts |
| Bench S001, 3 runs (4.7.2 → 4.8) | frame P50 18,8 → 12,0 ms (−36 %), GPU 5,14 → 3,70 ms |
| Captures | `artifacts/shots/` : S001 et S002 en 4.8, comparées au 4.7 |

## Limites et risques

- **S002 EDENFR : arbres et structures en bois absents** par rapport au
  4.7/official (lacunes G1 et G3). C'est la décision de données prioritaire.
- L'eau d'S001 est visible ou non selon le timing de capture (non
  déterministe).
- Live serveur, HUD complet, combat, stabilité ≥ 1 h : non testés sur 4.8.
- Double source du Core (ADR-0003) : les correctifs doivent être portés
  deux fois tant que le Lot 1 n'est pas fait.
- `assets/` partage ses inodes avec `portage/` : un outil externe qui
  écrit en place modifierait portage.
- MCP et GdUnit4 n'ont pas été évalués (différés, voir MASTER-PLAN).
