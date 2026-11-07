# Marche, routage et colliders — état

Statut : **verified** en jeu (S003, client Godot 4.8-beta2, 2026-11-06).

## Qui borne le déplacement

- **Grille .smp** (`map/sknodemap/Sxxx.smp`, `NodeMapFile`) : murs
  invisibles natifs. `ClipByNodeMap` applique la case **brute** ; le
  routeur A* (`Pathfinder`) marche sur la grille **dilatée** d'1 case
  (même sémantique que la 2e grille native +0x54).
- **fsm / sol marchable** (`GroundHeight` → `WalkableGround.SampleAt`) :
  sert de borne hors-carte **uniquement quand il n'y a pas de .smp**.
  Avec un .smp chargé, les trous de fsm sous les props restent
  marchables — sinon un spawn dans un décor (plante géante du spawn
  S003) enfermait le marcheur sur un îlot de sol : aucun mur grille,
  aucun collider, refus `dstG` dans toutes les directions.
- **Colliders physiques** des GLB helpers : `ColW*` (WallLayer) bloque,
  `FloorLayer` sert de sol. Le joueur est kinematic (`MoveAndCollide`).

## Overlap (corps DANS un collider)

Cas réel : spawn/dérive à l'intérieur d'un `ColW` de prop. Symptômes
mesurés : `BodyTestMotion` dans les 8 directions = libre (`oooooooo`),
`IntersectShape` = overlap `[ColW_shared<Objects<World<…]` — les tests
de cap ne voient pas l'overlap et le corps tangente sans jamais sortir
(~0,01 u/s de dérive). `TryDepenetrate` (recovery de
`BodyTestMotion{RecoveryAsCollision=true}` → push horizontal ≤0,5 u par
événement « bloqué ») sort le corps en quelques ticks ; prouvé en live :
7 pushes puis `TalkToNpc` émis en ~15 s depuis l'intérieur du collider.

`DetectStuck` évalue tout mouvement voulu (clavier compris) ;
l'événement `Stuck` (liste noire + replan `AutoRoute`) n'est émis que
pour les routes A* — un appui clavier contre un mur n'a rien à
replanifier.

## Approche NPC

`PlayerController.TalkToNpc` passe par `AutoRoute.ToEden` (A* +
blacklist + replan ×3 + abandon propre), plus la ligne droite
historique. Le rechase `_talkTarget` (0,3 s) ne réécrit la cible que si
la file de waypoints est épuisée (`_path.Count == 0`) — il écrasait les
waypoints A* par la ligne droite à chaque tick.
