# Correctifs secondaires (agent U2) — 25/10

## Bugs
1. **Chargement long + barre figée** — cause : l'écran de chargement
   parse 60 GLB sur le thread principal (2/frame → stall), le .fsm
   est reparsé (PreparedFsm jamais consommé) et GameScene._Ready
   bloque ~1,5-2 s (barre immobile). Fix : LoadMap en tâche de fond
   (construction hors-arbre sûre — même garantie que les protos G1),
   fsm réutilisé, progression réelle Report() thread-safe, warm GLB
   parallèle en tâche de fond (tous protos, pas 60).
2. **Curseur natif** — ui/NativeCursor.cs : UI/SystemCursors.png
   cellule 0 (flèche) → Input.SetCustomMouseCursor (hotspot).
3. **Quest tracker** — liste flottante hors panneau, chevauchement ;
   refonte : tout dans le Panel (tabs/boutons/quêtes), titre or +
   « — objectif n/max ».
4. **I ouvre inventaire + panneau équipement** — le « profil » vu par
   le joueur = EquipSlotsPanel accolé à l'inventaire. I→inventaire
   seul ; le panneau équipement suit le profil (C).
5. **Sol plat** — PREUVE rf_game FUN_00b02100 : le facteur de stage
   texture natif = uvScale0/uvScaleN appliqué à l'UV vertex (déjà
   tilée par scale0) → layerN effectif = wuv × scale0²/scaleN (notre
   code : ×scaleN → ~2-3× trop grossier). Fix uniforms dans
   BuildSplatMaterial.
