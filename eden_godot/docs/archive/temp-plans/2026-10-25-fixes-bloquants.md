# Plan correctif — bugs bloquants rapportés par le joueur (25/10)

## Gameplay/runtime (agent G) — fait (26/10)
- [x] **Animations se relancent chaque frame** — le garde `_current` existait déjà mais deux trous restaient : `PlayOnce` relançait la même anim à chaque swing (attaque re-jouée au milieu) et la bascule stand↔move clignotait sous le bruit d'interpolation. Fix EntityAnimator : no-restart si même one-shot en cours, hystérésis + délai de tenue 150 ms sur la locomotion, et rejeu autorisé si l'anim courante est terminée (non-bouclée).
- [x] **Pas de murs/collision** — nouveau `scripts/world/CollisionBuilder.cs` : les meshes « helper » masqués par ToonMaterializer (meta `collision_helper` / noms true wall, blo, Box, knock…) deviennent StaticBody3D + ConcavePolygonShape3D (repli BoxShape3D AABB si >10 k tris, cap 8/objet, végétation UE7xx exclue). Joueur : capsule CollisionShape3D + MoveAndCollide avec glissement + bloc hors-carte (GroundHeight null alors qu'on est dedans). Mesuré : 2 811 objets avec colliders sur S001, 940 sur S002.
- [x] **Texture de sol absente sur certaines zones** — diagnostic : les textures et la splatmap chargent bien (VisDiag étendu, --splat-layer prouve chaque couche) ; le washout gris vient des poids splat natifs dont la somme > 1 (S002 : R+G ≈ 1.3 → aplats clairs). Fix : renormalisation des poids dans `terrain_splat.gdshader` (inv = 1/tot quand tot>1) — détail texturé restauré (std 6 → 74 sur la zone test). Attention : un `return` en fragment Godot fait échouer TOUTE la compilation du shader (silencieux → terrain gris) ; retiré.
- [x] **Combat sans anims** — même cause que le point 1 : `PlayOnce` ne relance plus l'attaque en cours, la locomotion ne clignote plus. Live : entrée zone OK, spawn 298 avec stems+anims attachées, auto-attack envoie les NCID 5 + PlayOnceAny(attack01) (zone spawn S002 sans mob à vue — le flux reste prouvé par le code path + les 272 précédents).
- [x] **Perf** — mesures `--perf` : S002 solo ≈ 91 fps / 1 100 draws ; S001 ≈ 32 fps / ~2 000 draws / 49 k nœuds. Dominant = rendu (draw calls + 2,7 M prims), pas les labels. Actions : Label3D (NPC scène + entités réseau) → VisibilityRangeEnd 80-90 u ; ombres désactivées sur les props < 1,5 u de rayon (le soleil 4-splits redessinait chaque petit caillou). S001 : ~32,5 → 32,7 fps (l'écart est GPU/draws, pas CPU-anim — le fix anims supprime aussi le spam de Play()).

## Écrans/UI (agent U)
- [ ] **Char select** : perso de dos/mal orienté (le joueur a dû le tourner) → face caméra par défaut ; cercle jaune géant hideux → retirer ou réduire (le natif a une scène temple) ; fond dégradé vide → fond 3D ou artwork ; icônes manquantes sur boutons.
- [ ] **HUD : overlapping** — panneaux les uns sur les autres (inventaire/questtracker/vitals) → positions par défaut disjointes vérifiées en 1280×720 ET 1600×900.
- [ ] **Drag HUD pas fonctionnel** — DragHelper existe mais les fenêtres ne bougent pas : vérifier CeguiTitleBar reçoit bien les drags (gui_input handler), test manuel décrit.
- [ ] **UI malformées** — boutons/icônes manquants, texte tronqué : audit de chaque fenêtre (inventaire, profil, shop, banque, skills, map, social) avec capture chacune.

Références natives : ../ref-shots/ (Selection_personnage = temple+perso debout face caméra).
Critère : captures avant→après par point, dotnet 0/0, tests verts.
