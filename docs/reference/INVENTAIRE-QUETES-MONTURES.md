# Inventaire quêtes et montures (EDENFR, corpus/fr-full) — 2026-11-02

Source : tables de `corpus/fr-full/data/db` (vérifiées contre le manifeste officiel, `tools/edenfr-verify`).

## Quêtes
- `c_mission.ini` : **2 392** définitions ; `t_mission.ini` : **2 465** textes (2 449 avec un titre FR).
- Chaque définition a son texte (0 définition sans texte). **73 textes n'ont pas de définition** (quêtes retirées ou
  non livrées) : ils ne sont jamais proposés par le jeu.
- Lecture : `MissionTable` (Core) + `MissionObjectives` ; non vérifié : la jouabilité de bout en bout de chaque quête
  (dépend du serveur : `eden_server`, 1 429 tests serveur).

## Montures
- `ridelist.ini` : **179** variantes (codes `r<modèle><variante>`), **159** modèles distincts ; les **159 NIF**
  existent dans `ride/model/` et leurs textures sont toutes résolues (`native-coverage --textures`, montures : 298 références, 0 manquante).
- Le dictionnaire d'items (`items.tsv`) contient ~200 objets qui parlent de monture (172 « Objet », 12 sans ligne `c_item`).
- Les écarts avec d'autres versions du jeu (fusion EN/Reforged : 29 284/29 782 montures absentes, EDENFR-GAPS G6) ne se
  corrigent pas sans données hors EDENFR : le client EDENFR ne livre que ces 179 variantes.
- Animations des montures : squelette (`ride/animation/*.kf`) + contrôleurs de matériau/particules des NIF
  (`NativeFxAttach`) ; voir PLAN-ACTION-2 lot 3 pour les limites.
