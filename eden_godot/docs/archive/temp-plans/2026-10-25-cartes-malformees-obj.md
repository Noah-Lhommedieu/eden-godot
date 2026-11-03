# Cartes malformées — convention OBJ et passage S002

Constat joueur : sur la première carte, le pont/passage est dans la terre
alors qu'il doit suivre la géométrie et franchir la crevasse. Ne pas
corriger en décalant un objet à la main : prouver la convention
`.fsm OBJ → GLB` fautive.

- [x] Identifier les records OBJ structuraux : la courbe de parapets
  `UB107/UB108` (`S002`, x≈263..327, y≈283..330) donne un oraculaire
  indépendant — chaque segment doit suivre la tangente de la courbe.
- [x] Comparer matrice directe vs transposée : l'axe local +x suit le
  chemin à |dot|≈0,997 avec les colonnes, contre ≈0,456 avec la ligne
  utilisée jusqu'ici comme colonne.
- [x] Corriger le pipeline commun : `MapLoader.ObjTransform` construit
  désormais `Basis` avec les colonnes `.fsm` (col0=M11,M21,M31), au lieu
  de lui passer les lignes row-major.
- [x] Ajouter un test reproductible :
  `S002_ObjMatrix_ColumnsFollowParapetPath` dans `tools/NetTests/FsmTests.cs`.
- [x] Captures avant/après : `/tmp/s002-obj-before.png` vs
  `/tmp/s002-obj-after.png`, `/tmp/s002-ub-bridge.png` vs
  `/tmp/s002-bridge-after.png` — le moulin, le quai et les structures
  apparaissent correctement orientés/visibles après correction.
- [x] Propagation : le même `ObjTransform` alimente les GLB directs, le
  batching `PropBatcher` et les colliders — visuel et collision utilisent
  donc maintenant la même convention.
- [x] Build/tests : `dotnet build` 0 erreur/0 warning ;
  `dotnet test --filter FullyQualifiedName~FsmTests` : 8/8.
