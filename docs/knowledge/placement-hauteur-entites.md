# Placement vertical des entités — « plus proche du z »

Statut : **verified** en jeu (S002, S191, S110, S764, S773, S177, S045 —
client Godot 4.8-beta2, 2026-11-06).

## Le problème historique

Deux règles opposées se contredisaient :

- `max(z serveur, hauteur heightfield)` — posait les entités **au-dessus**
  de la map dès que le fsm code une surface haute. Cas réel S002 : le
  heightfield à (332,298) vaut 25.8 (sommet de la place / canopée de
  l'arbre géant) ; un z serveur ≈ 0 propulsait le joueur sur la face de
  branche à 35.9 — perché au-dessus du village.
- `PeekLowest` (face la plus basse de la colonne) — posait les entités
  **sous** les donjons multi-étages (plafond d'une grotte pris pour le
  sol) et ignorait les étages hauts.

## La règle retenue : `WalkableGround.PeekNearestY`

Candidats : terrain fsm + face OBJ `FloorLayer` la plus basse au-dessus
de `refY` + face la plus haute en dessous (scans **non bornés** — la
comparaison `|y − refY|` écarte elle-même les surfaces lointaines ;
borner à `MaxDrop` ratait un plancher de donjon empilé). Le candidat le
plus proche de `refY` gagne ; `PeekLowest` en dernier recours.

- `refY` = le z transmis : spawn/téléport joueur (`SnapToGround`),
  z wire des entités distantes quand il est réel (`ez≠0`), sinon le
  plancher du joueur local (même salle la plupart du temps).
- S002 spawn (332,298) : terrain 25.8 bat la branche 35.9 → place du
  village. S191 : z réel 709.9 → plateforme haute. S110 : z 31.5 →
  plateforme donjon. Vérifié par captures.

## Ce que le wire ne dit pas

- **Le z joueur n'est JAMAIS envoyé** : `ClientEnterNodeNodeData` porte
  `(node, x, y, facing)` — le 3e champ lu comme « z » est le facing.
  Le placement du joueur résout donc « l'étage plausible le plus proche
  de ~0 », terrain inclus — correct tant que le perso a été posé par un
  mécanisme normal.
- **z persisté = 0 en DB** (`game.player_characters.z` non résolu par
  la zone — pas de géométrie côté serveur). Conséquence connue : un
  relog depuis un étage haut peut reposer au niveau bas (plausible et
  jouable, pas natif-parfait). Les entités ont un vrai z (la zone le
  résout depuis les records de scène).
- La marche (`SampleAt`/`Sample`) garde sa sémantique « suivre le sol
  vers le haut » (monter les pentes/dalles) — ne pas y appliquer le
  « plus proche » : sinon on ne grimpe jamais sur une dalle au-dessus.

## Cas restant ouvert

- Position persistée **dans** un prop (ex. tronc S002 à (338.8,300)) :
  aucune face de sol dans la colonne → le terrain (intérieur du tronc)
  gagne. Jouable (le tronc est FloorLayer, on en sort en marchant) mais
  pas natif-parfait ; la correction propre = résolution z côté zone ou
  snap à la case .smp marchable la plus proche.
