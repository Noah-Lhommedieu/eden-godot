# Master plan — Gododen / Eden Client Reborn

## But et règle d'arrêt

Livrer un client Godot 4 C# jouable, stable et visuellement fidèle aux captures Reforged de `../ref-shots/`. Les plaintes récentes et les captures montrant le hall absent, le terrain gris, des entités sous le sol, des panneaux superposés et un personnage mal orienté prouvent que des jalons déclarés « terminés » ne le sont pas.

**Aucun jalon visuel n'est terminé sur la seule base d'un build, d'un test codec, d'un log de chargement ou d'un screenshot debug.** Une tâche visuelle passe à `[x]` après comparaison de captures à cadrage/résolution équivalents et inspection du rendu Godot. Les tests code, tests headless et tests client sont des preuves distinctes.

### Légende
- `[ ]` À faire.
- `[~]` Code présent, résultat non prouvé ou partiellement observé.
- `[!]` Échec visuel rapporté ou reproduction échouée.
- `[x]` Critère propre à la tâche validé, preuve référencée.

Au début de cette révision, les retours joueurs rendent `[!]` : hall absent, terrain sans texture, NPC sous la map/skin erroné, HUD non déplaçable, curseur contextuel, skillbar, sens du personnage, latence animation et ciblage NPC. Les 275 tests NetTests passent et la build vient d'être remise à zéro erreur après un état de travail cassé ; cela ne clôt aucun P0 visuel.

## Sources et règles de preuve

- Référence client/asset Reforged en lecture seule : `../raw_assets` ; serveur, binaires et données d'origine ne sont jamais modifiés.
- Captures natives : `../ref-shots/`. Captures de cette branche : `../godot-shots/`.
- Pour le comportement natif, chercher en priorité les sources/layouts/assets et les exports déjà présents avec `reindex`; citer binaire/fonction/adresse. Ne pas deviner les channels, skins, indices, axes ni UX.
- Une QA visuelle identifie la scène réelle (PID), résolution, commit/hash, map et arguments. Les anciennes captures et les captures de diagnostic ne sont pas une validation.
- Trois niveaux à reporter séparément : build/tests ; protocole/serveur headless ; client Godot rendu et manipulé en live. « Fonctionne » signifie le dernier, lorsque la fonction est visuelle.
- Protéger les modifications locales pré-existantes ; aucun `pkill` générique. Terminer le PID exact du processus de QA.
- Une régression rouvre toute tâche précédente. Le plan de secours reste bloqué tant que le présent master plan n'est pas validé.

## 0. Banc de QA et inventaire des références

- [x] 0.1 Recueillir les symptômes et noter les captures du joueur comme défauts P0.
- [ ] 0.2 Ouvrir et indexer chaque ref-shot : login, channel, char select, jeu, caméra, daily, HUD, interfaces.
- [ ] 0.3 Harness `--screen=login|channel|chars|create`, `--map=S001|S002`, HUD et interactions ; vérifier PID, attendre phase de rendu stable et sauvegarder log.
- [ ] 0.4 Mesurer les étapes via horodatage, chargement asset/phase/exception ; barre de progression tirée du travail réellement terminé.
- [ ] 0.5 Matrice de capture aux résolutions 1280×720, 1600×900, 1920×1080 et DPI/scale différents.
- [ ] 0.6 Mode debug donnant asset résolu, instance, AABB, transform, sol, mat/texture, animation et GUI rect sans spam par frame.
- [ ] 0.7 Établir captures avant/après identifiées, tests de régression et courte séquence vidéo pour les problèmes temporels.

## 1. Connexion et pré-partie — P0

- [~] 1.1 Login lisible : champs, focus, erreurs, clavier, connexion, échec, retry, Exit et anglais.
- [x] 1.2 Hall temple pré-partie : preuve reverse `FUN_00699a60(...,99)` → `c_node.ini` → `S218`; extraction légère `S218_room10b`, coques `_NDLWL` masquées, escaliers/tapis natifs restaurés, captures `final2-login/channel/chars/create.png` avec `temple=True`.
- [~] 1.3 Channel select : fond `S218` capturé ; état/latence des serveurs, sélection visible, confirmer/annuler et retour restent à éprouver live.
- [~] 1.4 Sélection : fond `S218` capturé et roster refondu ; skin/body/race/visage/yeux/cheveux/tenue réels restent à éprouver contre un personnage live.
- [ ] 1.5 Portraits, icônes, boutons et cadres depuis les imagesets CEGUI corrects ; comparer `Selection_personnage.png`.
- [~] 1.6 Création : spawn et caméra par race, body/genre, face, peau/yeux/cheveux/couleur, preview cohérente et contrôles explicites.
- [~] 1.7 Transitions live login→channel→perso→loading→S002 sans écran vide sur happy path ; retour arrière/échec/double scène encore à rejouer.
- [~] 1.8 Loading personnage→monde : phases séparées auth/ticket/zone/FSM/terrain/objets/entités/entrée ; temps de début/fin mesurés.
- [~] 1.9 Loading progresse en temps réel et a rejoint `GameScene` ; erreurs récupérables et phases >2 s à analyser.
- [~] 1.10 Parcours live complet validé happy-path avec captures `live-flow2-*.png` ; échec réseau, retour arrière et création restent ouverts.

## 2. Assets et conversion

- [~] 2.1 Correctif multi-parent NIF→GLB dans le worktree : rescaner les familles et comparer toute sortie aux fichiers sources ; valider dans Godot.
- [x] 2.2 Décor pré-partie identifié : `S406` était une fausse piste ; la scène native ID 99 est `S218` (`c_node.ini`), la salle correcte est `DummyRoom10` variante `S218_room10b`.
- [ ] 2.3 Audit GLB : arbre, transforms, axes, échelle, joints/IBM, normales, tangentes, UV, winding, alpha, vertex color, materials, textures et LOD.
- [ ] 2.4 Registre absence/casse/chemin asset ; aucune texture blanche ou scène vide en fallback silencieux.
- [ ] 2.5 Valider NIF/KF par familles character, NPC, monster, ride, map/buildings, props, item, effect et scènes spéciales.
- [ ] 2.6 Conversions reproductibles incrémentales : version outil, hash d'entrée/sortie, logs, compteurs et re-run idempotent.

## 3. Monde, terrain, textures et collisions — P0

- [x] 3.1 Surface grise `S002` résolue : winding/UV splat corrigés, overlays `__night_`/`*_BB_*` masqués, double fog neutralisé, émission parasite alpha `UD501` coupée ; capture `world-final3-s002.png` texturée.
- [~] 3.2 Captures S002 ras du sol/oblique validées ; S001 et les vues aériennes complémentaires restent à rejouer.
- [ ] 3.3 Pour chaque tuile, vérifier les cinq slots, fichier/path, splat RGBA brut, poids, canaux, UV, scales, mipmaps, filtre, normals et vertex colors.
- [ ] 3.4 Comparer pixeluellement et spatialement aux DDS/FSMP natifs ; confirmer vraie sémantique splat par désassemblage ou captures de référence.
- [ ] 3.5 Vérifier triangles, winding, trous, doublons, bords de tuile, heightmap, orientation et hauteur de l'eau.
- [ ] 3.6 Vérifier fog/lighting/exposure : aucun brouillard ou couleur d'ambiance ne doit effacer le détail du sol.
- [ ] 3.7 Eau animée (IFL), transparence, profondeur, shore fade, collisions et rendu cohérent par map.
- [ ] 3.8 Herbe/feuillage : densité, animation, alpha, ombre, distance et stabilité multi-instance.
- [~] 3.9 Murs/bâtiments : helpers/colliders existent ; déplacement bloqué et absence de traversée restent à éprouver live.
- [ ] 3.10 Vérifier barrières, falaises, intérieur, ponts, escaliers, portails, map bounds et cas spéciaux sur au moins 6 maps.

## 4. Personnage, NPC, mobs et équipement visuel — P0

- [~] 4.1 Facing mesh/logique corrigé (`(-90°,180°,0°)` sur GLB et cap modèle) ; avancer/reculer/strafe/clic-sol/caméra et interpolation restent à éprouver en vidéo.
- [~] 4.2 Grounding entités : spawns wire bornés `Y ≥ terrain`, NPC statiques snappés à `TryGetEdenHeight`, capture `entities-final-s002.png` au sol ; audit multi-map encore à faire.
- [~] 4.3 Table NPC résolue : `c_npc.ini` + `npclist.ini` → stem/skin/scale (`3127 npcId → 702 codes`), `S002` charge `53/53` GLB ; contrôle visuel par race/skin reste incomplet.
- [~] 4.4 Mobs/players réels chargés : `PickArea` `3/3`, clic live NPC (`TalkToNpc → EnterShop`) et vrais mobs `kind=12` engagés/mort serveur ; variantes/collision multi-map restent à éprouver.
- [~] 4.5 Joueur local/distant : race, body, skin, face, yeux, cheveux, tenue équipée, arme et palette natives.
- [ ] 4.6 Audit mesh séparés tête/yeux/cheveux/outfit/skin, alpha et culling ; vérifier visuellement chaque combinaison.
- [ ] 4.7 Attachement équipement et transformation bone pendant idle/move/attack/mount pour les slots supportés.
- [ ] 4.8 Test d'occlusion/sélection : collider de cible ne masque pas un NPC voisin ni les surfaces UI.

## 5. Contrôleur et système d'animations — P0

- [~] 5.1 Locomotion locale : `InputMoving` bascule l'anim sans attendre la fenêtre de vitesse ; arrêt/blend et latence perçue restent à mesurer en vidéo.
- [~] 5.2 Facing avant/arrière corrigé dans le code (`180°/Y`) ; preuve vidéo et contrôle du cap réseau restent à produire.
- [ ] 5.3 Vérifier animation d'idle, move, stop, turn, strafe, backpedal, run, jump, sit, attack, cast, hurt, death, revive et mount selon assets natifs.
- [ ] 5.4 États déterminés par input et état serveur, pas vitesse bruyante seule ; remote interpolation séparée du contrôle local.
- [ ] 5.5 Résoudre `motion.ini` et `.kfm` par espèce/arme/état ; clips réellement assignés au squelette cible et loop natif.
- [~] 5.6 Attaque/cast/dégâts/mort validés live sur `Red Cap Mushroid` (`5`, `9/17`, `272`, `279→281`, mort) ; hurt/death visuels des deux côtés et autres armes/classes à auditer.
- [ ] 5.7 Variabilité 30/60/144 FPS, jitter/perte réseau, pente, knockback, correction position et animation bloquée.
- [ ] 5.8 Test automatisé d'état + capture temporelle : comparer poses à t0, t+200ms et fin de cycle.

## 6. HUD et interfaces — P0

- [~] 6.1 Chrome/layout HUD refondu et inventaire inspecté ; la composition complète aux résolutions supportées reste à auditer fenêtre par fenêtre.
- [~] 6.2 Drag réel prouvé sur inventaire : barre de titre `(256,220)→(376,310)`, fond `(376,310)→(496,400)`, positions persistées ; généralisation aux autres fenêtres à rejouer.
- [ ] 6.3 Ne pas intercepter clic UI, drag d'item, scrolling, texte ou monde ; titre accessible et window z-order correct.
- [ ] 6.4 Placer vitals, minimap, quest tracker, target, chat, hotbar et menu selon les ref-shots ; toutes tailles visibles.
- [~] 6.5 CEGUI/DejaVu : barres de titre et stylebox natives installés sur les fenêtres déplaçables ; tous les états bouton/borders restent à comparer visuellement.
- [~] 6.6 Quest HUD : fenêtre/layout natif monté ; titre, objectifs, compteurs, actions et clipping restent à éprouver.
- [~] 6.7 Inventaire ouvert/déplacé en sonde ; isolation des raccourcis Profil/Equipment et clic bouton à rejouer.
- [ ] 6.8 Inventaire/profil/equipement : vraies icônes, slots et tooltips ; opérations serveur rafraîchissent le visuel.
- [x] 6.9 Skillbar : fallbacks décoratifs supprimés, slots branchés sur `PlayerSkills`/`SpellCatalog`, rectangle icône corrigé ; run live reçoit 141 sorts et les icônes réelles sont visibles sur `live-flow2-world.png`.
- [ ] 6.10 Dialogue, shop, bank, craft, class, map, social, settings, target et chat audités chacun par capture.
- [ ] 6.11 UI scaling, thème, fonts, résolution et modes fenêtre testés ensemble ; texte non tronqué.
- [ ] 6.12 Chaque écran UI qui figure dans `ref-shots/` a une correspondance et un test QA ; fonctionnalité manquante est explicitée.

## 7. Curseur, ciblage et interaction — P0

- [~] 7.1 Curseurs natifs montés ; cellules, hotspots, taille/scale DPI et alpha restent à comparer aux captures natives.
- [~] 7.2 Contextes sol/mob/NPC/loot/UI codés avec priorité UI ; matrice complète hover/loading/reset de scène à éprouver.
- [~] 7.3 Clic droit : capsules `Area3D` couche 16, raycast `CollideWithAreas`, `3/3 HIT` en sonde et clic live `marchand → TalkToNpc` ; cas chevauchement/obstruction à rejouer.
- [~] 7.4 Hit test commun hover/sélection/action prouvé sur NPC de ville ; matrice mob/loot/UI à éprouver.
- [x] 7.5 Approche/action validée live : NPC → `TalkToNpc/EnterShop`, mob → approche/attaque/dégâts/mort ; cas obstrués et portées exotiques restent couverts par les tests suivants.
- [ ] 7.6 Souris clavier et focus fenêtre : aucun input fantôme ni curseur bloqué en état précédent.

## 8. Performance et robustesse

- [~] 8.1 Prop batching et optimisations existent mais chiffres antérieurs incompatibles (7,9→11,8 fps vs 66→87 fps). Refaire mesures GPU RTX 4060 comparables ; llvmpipe n'est pas une référence.
- [ ] 8.2 Mesurer chargement chaud/froid, draw calls, CPU/GPU frame, triangles, nœuds, allocations, RAM/VRAM sur les mêmes scènes.
- [ ] 8.3 Test stable 1/2/4 instances pendant 10 minutes ; stabilité avant qualité puis optimisation.
- [ ] 8.4 Culling/LOD/ombres/Multimesh ne modifient ni collision/sélection ni rendu ; captures avant/après.
- [ ] 8.5 Toute API Godot UI/scene-tree reste sur thread principal ; annuler tâches lors des transitions.
- [ ] 8.6 Préréglages Low→Ultra ont effets réels mesurés, sont réversibles et persistants.
- [ ] 8.7 Aucun process/test ou scène n'est laissé vivant en fin de QA.

## 9. Langue et données

- [~] 9.1 Auditer l'anglais de toutes les surfaces : textes, noms, objectifs, tooltips, systèmes, maps, erreurs et chat.
- [ ] 9.2 Lier chaque feature NCID à une preuve et un test ; une entrée NCIDS.md n'est pas preuve d'intégration.
- [ ] 9.3 Comparer valeurs/races/classes/items/NPC/maps avec les données natives, pas seulement IDs plausibles.
- [ ] 9.4 Éliminer texte Big5/debug/identifiants bruts quand une table anglaise existe ; fallback utilisateur clair.

## 10. Porte de sortie — aucune clôture avant réussite

- [ ] 10.1 Captures toutes étapes connexion/loading et chaque HUD/interface, aux résolutions de référence.
- [ ] 10.2 Comparaison point par point avec `../ref-shots/` documentée ; toute différence importante reste ouverte.
- [ ] 10.3 Vidéo courte déplacement/facing/anims/attaque/clic NPC/drag HUD ; images statiques insuffisantes pour temporalité.
- [~] 10.4 Build `0/0` et `275/275` NetTests verts à cette révision ; export Linux launch et Windows/Wine restent à produire.
- [ ] 10.5 Vérifier git diff et process ; `archive/ETAT-FINAL.md` donne statut code/tests/headless/live/capturé/accepté et défauts restants.
- [ ] 10.6 Les P0 de ce plan sont clos avant le master plan de secours.

### Ordre

0 QA → 1 connexion → 2 assets → 3 terrain/collision → 4 entités → 5 animations/contrôle → 7 input → 6 UI → 8 perf → 9 contenu → 10 sortie. Un changement d'ordre doit garder toutes les preuves et tâches.
