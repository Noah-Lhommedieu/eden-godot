# FIX-K-camera (souris : clic gauche / clic droit)

Statut : code modifié, NON compilé, NON exécuté (voir 4).

1) Constat : le code de PlayerController.cs (HandleMouseButton/HandleMouseMotion, l.508-597) fait déjà le schéma voulu.
   Glisser gauche (_lDrag) = orbite caméra seule. Glisser droit (_rDrag) = orbite + `_model.Rotation = _yaw` (l.594-595),
   EndSteer envoie ZoneTurnSelf au relâchement. Seuil drag = DragThresholdPx 6 px. Clic simple (sans glisser) :
   gauche -> OnLeftClick (sélection / clic-déplacement), droit -> OnRightClick (PNJ talk, mob attaque, vide désélection).
   Curseur reste visible (UpdateMouseMode), molette = Zoom hors HUD. Aucun autre fichier du périmètre (input/*) ne gère ces boutons.
   Décompilé : rien de trouvé sur seuils/capture curseur (strings rf_game : seul "DragThreshold" CEGUI) ; seuil 6 px conservé.
2) Modifié : scripts/player/PlayerController.cs — ajout de MouseSchemeTest (+ TestBtn/TestMove), appelé en fin de DemoInput (--input-test).
   Passe par les vrais handlers. Vérifie : drag gauche = camYaw change, ModelYaw inchangé, pas de _moveTarget ;
   drag droit = camYaw change, ModelYaw == camYaw ; clic droit simple sur PNJ => _talkTarget == PNJ.
3) Preuve : AUCUNE (build bloqué).
4) Non vérifié : compilation + sortie du test. /tmp/build.lock est tenu par des démons dotnet (VBCSCompiler/MSBuild) qui ont
   hérité du fd du flock, donc ~15 builds d'agents attendent indéfiniment. Tentative de `dotnet build-server shutdown` refusée par le
   classifieur de permissions. Pour débloquer : `dotnet build-server shutdown` puis relancer
   `tools/build.sh && tools/run.sh -- --map=S002 --solo --input-test | grep input-test`.
