# MASTER-PLAN — eden-godot

Mis à jour : 2026-11-02. Les statuts suivent `workflows/task-contract.md`.
Les durées ne sont **pas** estimées : on mesure la vélocité réelle avant.

## Objectifs du banc d'essai

1. Client identique au portage 4.7, mais **EDENFR pur** (ADR-0002).
2. **Godot 4.8-beta2** : performance, stabilité, facilité de mise à jour (ADR-0001).
3. Nouvelle méthode : contrat de tâche, preuves, scripts, ADR, skills.

## Lots

| Lot | Contenu | Statut | Preuves / prochaine action |
|---|---|---|---|
| 0 | Audit et fondations : moteur épinglé, scripts, règles, ADR, baselines | **TESTED** | `tools/setup.sh` vert ; 1601 tests .NET + 1429 serveur ; bench 3 runs 4.7 vs 4.8 ; MCP **non évalué** (voir plus bas) |
| 0b | EDENFR pur dans le client 4.7 copié | **IMPLEMENTED** (audit A 2026-11-02 : rendu non validé) | Casse, CP1252, `NativeOnly`, FSM V7, 43 594 dérivés sourcés. Capture S001 en 4.8. Lacunes : `workflows/EDENFR-GAPS.md` |
| 1 | Core unique (`src/Eden.Core` → référencé par Godot) | NOT_STARTED | ADR-0003 ; fusionner `WorldConv`, `ThemePalette`, `ClientSettings`, `EdenVfs` |
| 2 | Loaders natifs dans Godot (NIF, KF, KFM, DDS) — fin des dérivés | NOT_STARTED | Plan : `PLAN-ACTION-NATIF-EDENFR.md` (données authentiques A, socle B, natif C→H) |
| 3 | Prototype de rendu : SceneTree vs MultiMesh vs RenderingServer | NOT_STARTED | Profil CPU d'abord : S001 est CPU-bound (frame 12 ms, GPU 3,7 ms) |
| 4 | Monde et streaming (culling, LOD, occlusion) | NOT_STARTED | — |
| 5 | Personnages et animation (LOD d'anim, transitions `.kfm`) | NOT_STARTED | Dépend du Lot 2 |
| 6 | Réseau et gameplay sur la pile EDENFR | NOT_STARTED | Test live login → zone sur 4.8 à faire (`eden-server-stack`) |
| 7 | UI : 19 layouts FR mal formés (parseur tolérant), HUD | NOT_STARTED | G4 |
| 8 | Optimisation mesurée | NOT_STARTED | Leviers : skill `godot-rendering-performance` |
| 9 | Validation de livraison (stabilité ≥ 1 h, couverture des maps) | NOT_STARTED | — |

> **Périmé (audit A, 2026-11-02)** : les lots 1-9 ci-dessus ne reflètent plus l'état réel ; la source de vérité est `PLAN-ACTION-2.md` (statuts corrigés) et `docs/reports/HANDOFF-2026-11-02.md` (14 demandes ouvertes).

## Prochaines actions recommandées (ordre)

1. **Décider des sources EDENFR** (G2, G3) : faut-il autoriser
   `EDENFR/extracted/` comme seconde source FR pour les ~1 900 NIF/KF
   dégradés ?
2. Test live sur 4.8 : serveur, login, zone, combat ; capture avec HUD.
3. Profil CPU d'S001 en 4.8, pour ventiler les ~8 ms CPU hors GPU.
4. Lot 1 (Core unique), puis spike du Lot 2.
5. Évaluer **un seul** MCP sur un projet jetable (voir plus bas), puis
   GdUnit4Net compatible 4.8-beta2.

## Plan source : ce qui est retenu ou différé

Source : `Plan_Godot_4.8_Agents_IA_Eden_Eternal.md` (2026-11-02).

| Recommandation | Décision |
|---|---|
| Épingler moteur, SDK et templates ; pas d'auto-update ; ADR de version | **Fait** : `engine/current`, nupkgs locaux, templates, `engine-update.sh`, ADR-0001 |
| Séparer métier et moteur (projets `Eden.*`) | **Retenu, par étapes** : ADR-0003 (Core d'abord), découpage seulement sur besoin mesuré |
| Contrat de tâche, statuts, format de livraison | **Fait** : `workflows/task-contract.md`, AGENTS.md |
| `docs/knowledge/` avec statut, version et date | **Fait** pour 7 notes à contenu vérifié. `rendering-playbook`, `animation-pipeline`, `networking-contract` et `ui-system` seront créées quand il y aura un contenu vérifié (pas de coquille vide) |
| Scripts `setup/build/test/run/capture/benchmark/compare/validate-assets` | **Fait**, et testé. En plus : `engine-update.sh`. `agent-report.sh` non fait (la livraison suit le contrat) |
| Skills locaux, indépendants du fournisseur | **Fait** : 7 skills ciblés dans `.agents/skills` (lien `.claude/skills`), au lieu de 15 génériques |
| CI (GitHub Actions) | **Différé** : pas de git pour l'instant (décision du propriétaire) |
| GdUnit4 | **Différé** : compatibilité 4.8-beta2 à vérifier ; ne jamais prendre `master` |
| MCP Godot (benchmark 1-2 jours, un seul) | **Différé** : sous Linux, le binaire Workbench est annoncé Windows et Funplay est marqué instable. Zips locaux `godot-mcp-0.2.0.zip` et `godot-ai-plugin.zip` à tester sur un projet jetable, en localhost avec token |
| Cibles FPS 120/90/90/60 | **Provisoires** ; baseline S001 réelle dans `knowledge/performance-baselines.md` |
| Git LFS / stockage externe | **Sans objet** pour l'instant (liens physiques, ADR-0004) |
