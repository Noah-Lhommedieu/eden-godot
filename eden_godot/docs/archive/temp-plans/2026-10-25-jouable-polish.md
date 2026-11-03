# Plan d'action — rendre le client réellement jouable (25/10/2026)

Constat (captures utilisateur) : login invisible, char select cassé (blocs blancs, pas de preview), monde noir en jeu sur S002, HUD partiellement hors écran. Priorité absolue = visibilité.

## P0 — Bloquants jouabilité (lot « visibilité »)
- [ ] Login : fenêtre visible et utilisable (champs lisibles, boutons identifiables, style natif — fond sombre + cadre CEGUI fonctionnel). Le layout `Login.layout` actuel rend un carré sombre minuscule → diagnostiquer (UnifiedAreaRect du root vs taille fenêtre, imagesets manquants) ou reconstruire en panneau natif fidèle.
- [ ] Char select : slots persos remplis (nom/classe/Lv), preview 3D du modèle dans SubViewport, boutons Start/Rotate/Back visibles. Actuellement blocs blancs + cadre orné mal placé.
- [ ] Jeu noir sur S002 (et toute map sombre) : vérifier spawn vs terrain (snap hauteur via TerrainSampler au spawn — le perso doit être posé SUR la map), env par map (S002 peut manquer de LitColor → minimum ambiant), caméra jamais sous le terrain. Tester S001, S002, S009.
- [ ] HUD : chat recadré à gauche (ancrage), hotbar clippée en bas, banner OK. Corriger offsets/ancres.

## P1 — Gameplay/anims (lot « gameplay 2 »)
- [x] Routing NCID 272 → `EntityAnimator.PlayOnce` (hurt/death : choisir anim par type — `hurt01`, `die01`/death — HYPOTHESIS documentée).
- [x] Persos distants : apparence via `SeeChar 265` (app blob) → `CharVisual` (même code que joueur), sinon marker couleur distincte.
- [x] Herbe : chunks GRL (types → modèle `UE704M.nif`/nom 40o) + GRS (instances pos/scale) — instancier MultiMeshInstance3D ou GLB dupliqués, ou billboards si trop lourd.
- [x] Eau animée : `.ifl` (Image Frame List) → anime la texture water par frames ; sinon shader avec défilement UV.
- [x] Fog : approximation plafond `FogMax` (natif ~8-22 %) — rampe asymptotique custom dans le fog shader ou WorldEnvironment custom.

## P2 — Contenu UI (lot « UI 2 »)
- [ ] `LimitlessHud.layout` parse → null (probablement commentaire XML en tête) — fixer CeguiLayout.
- [ ] Inventaire : parser 312 existe → remplir la grille (itemId → ItemTable → icônes `itemicon/`).
- [ ] Quêtes : NCIDs 87/186 à décoder (ZoneMessages.g.cs + désasm si besoin) → QuestTracker réel ; sinon stub propre.
- [ ] Création perso : pickers race/classe/genre/face/peau/yeux → champs appearance blob réels.
- [ ] Mini barres EXP/Soul bas-gauche correctement placées.

## Méthode
3 sidekicks en parallèle, ownership strict :
- **A** : `scripts/screens/*` (Login/CharSelect/ChannelSelect), `GameScene.cs` (spawn/env), vérif visuelle obligatoire par captures `godot-shots/`.
- **B** : `scripts/combat/*`, `scripts/game/EntityAnimator`+, `EntityRegistry` (apparence remote), `scripts/world/` nouveaux fichiers (GrassRenderer, WaterAnim), `TerrainBuilder` (eau animée uniquement).
- **C** : `scripts/hud/*`, `scripts/ui/CeguiLayout.cs` (fix parse), `CharCreateScreen.cs` (pickers), `scripts/protocol/` nouveaux messages.

Critères de fin : captures login/chars/game lisibles, mondes S001/S002/S009 visibles, dotnet 0/0 + tests verts. Commit par étapes.
