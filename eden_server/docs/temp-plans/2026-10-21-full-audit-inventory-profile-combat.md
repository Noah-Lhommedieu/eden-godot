# Plan d'action — audit complet prompt3 + trois régressions live

Date : 2026-10-21

## Règle de reprise

Repartir de zéro sur l'évaluation de `2026-10-21-prompt3-live-bugfixes.md`.
Les cases cochées dans les anciens plans, les commentaires de code, les
logs de build et les commits antérieurs ne prouvent pas qu'une fonction
marche. Pour chaque exigence : retrouver le chemin complet DB → logique →
wire → affichage client, produire une preuve (désassemblage/capture/test),
puis marquer séparément « implémenté », « testé automatisé » et « validé
en jeu ». Aucun item ne sera déclaré fini seulement parce qu'un paquet est
émis.

## Symptômes live prioritaires

1. Le personnage `test3` voit l'inventaire vide.
2. Les bonus de certificat n'apparaissent pas dans l'interface profil.
3. Des items non équipables (Broken Demon Pot, livres de quête) apparaissent
   dans des emplacements d'arme/armure; le joueur ne peut pas les déséquiper.
4. Les nombres bruts du profil (ACC/EVA/Parry/Block/crit/vitesses) et leurs
   pourcentages ne respectent pas les conversions client/natives; vérifier
   explicitement la base « environ 3800 EVA = 100% » rapportée par l'utilisateur.
5. Après s'être éloigné puis avoir arrêté d'attaquer, un mob affiché à 0 PV
   continue de frapper, le client est bloqué et le personnage reçoit un DOT
   inattendu; l'habillage alterne beige/marron/noir.

## État de preuve initial (avant correction)

- `test3` = personnage `200000004`; la vue PostgreSQL `inventory` contient
  actuellement 46 lignes, 22 en conteneur 0 et 24 en conteneur 1; aucun
  doublon `(container_index, loc)`.
- Le handler de la requête client 25 émet effectivement NCID 312 puis 280
  attr `0x3f0=1`. L'ancienne réponse 17:35:01.310 faisait 1 060 octets
  (conteneur0 seul); une exécution ultérieure avec test3 a produit 2 261
  octets et deux conteneurs, mais aucun de ces logs ne prouve l'affichage UI.
- La capture native `captures/zsniff_native_entry.bin` NCID 312 (17:22:12.907)
  annonce un sac de base de 32 slots (`u16 capacity=0x20`); le serveur et
  `inventory_slots` avaient fixé 20. Le 312 client doit suivre la taille
  réelle envoyée par le serveur, donc le mismatch est une deuxième cause
  concrète de slots/UI incohérents.
- Migration additive `Database/006-inventory-native-base32.sql` appliquée
  à `fndb1`: les trois lignes `bag_0_slots=20` sont passées à 32; test3
  avait 44 items au moment du changement, 46 au relevé actuel, bag1=30.
- `GetLiveItemsAsync` filtrait uniquement `container_index=0`. Le payload
  observé annonce deux sacs ouverts, mais le bloc du sac 1 était vide,
  alors que la DB porte 24 items dans ce conteneur. Le filtre a été retiré
  pour les conteneurs 0..4; le test de codec vérifie les items des deux
  sacs, et un test additionnel parse les champs optionnels/queue du record.
  `inventory_slots` porte actuellement 32+30 slots. L'affichage UI de test3
  après le dernier déploiement reste non vérifié.
- `player_license` de test3 contient le certificat `33281` en slot 1.
  `S_Item[33281].UseEnchantId=52515`, `T_Enchant[52515]` indique
  « EVA +4% ». L'enchant était stocké en s16 négatif puis recherché avec sa
  clé négative dans les tables dont les ids sont u16; `ActiveEnchantIds`
  normalise désormais en u16. Un refresh 296/492/493 est ajouté à
  MigrationDone; la contribution EVA est propagée au profil et au snapshot
  combat. Le lien exact avec la description doit encore être confirmé par
  comparaison native/live.
- La table `equipment` actuelle de test3 a deux armes actives persistées
  en group 0, loc 9 (`EquipLoc=9`, Mana Gathering Wand) et loc 35
  (`EquipLoc=35`, Apprentice Bow). Le natif `CItemFactory::Equip`
  remappe 9→7 et 35→10, et `CCharacter::GetWeaponItem` ne lit que 7/8/10.
  Ces deux lignes ne sont donc pas des slots canonisés; le code C# de 311
  filtre loc 35 et le code combat traitait loc 9/35 comme des emplacements
  valides. Il faut canoniser sans perdre le serial avant l'émission 311/282.
- `MobCombatLoopAsync` était lancé depuis plusieurs voies de combat et
  n'avait ni garde atomique par mob ni identifiant d'engagement annulable.
  Une génération synchronisée, une boucle unique, l'invalidation sur mort/
  respawn et des gardes après les await sont ajoutées. Les tests couvrent
  exclusivité/invalidation; la reproduction live reste requise pour
  attribuer avec certitude le freeze.
- Les logs d'une ancienne exécution contiennent deux NCID 292 par riposte
  avec `effect_id=0xcb36` (=52022), `delta=-15/-6`, en inconditionnel.
  `T_Enchant[52022]` dit Perforation (150 dégâts périodiques / -100 MP);
  le report cible `mob.Handle`, sans décrémenter `LiveMob.Hp`, ce qui
  explique un mob visuellement mort mais attaquant côté serveur. Le code
  courant a déjà retiré ces deux sends génériques de `MobCombatLoopAsync`;
  le log est append-only et contient des paquets d'une ancienne exécution.
  Il faut retester la build réellement lancée et garder une régression qui
  interdit leur réintroduction.
- `player_enchants` de test3 ne contient pas l'effet 52022; le DOT du log
  ne vient donc pas d'un statut persistant restauré au login.
- `S_Item` identifie Broken Demon Pot (34539-34546, 35414-35416, 37558-37563)
  comme kind 44 sans equip-location; le handler d'équipement prétend rejeter
  ces items. Il faut distinguer une corruption de la table `equipment`, un
  mapping/sérialisation de 311/282 erroné et une voie de move qui contourne
  `ResolveEquipLocAsync`. Ne pas réparer la DB avant ce diagnostic.
- La capture d'écran fournit les paires profil :
  ACC 77 / 4.52%, EVA 69 / 13.24%, Parry 73 / 3.28%, Block 75 / 0.68%,
  ATK SPD 7.5 / 74%, CRIT 59.34 / 5.87%. Reverser les consommateurs UI du
  client `rf_game`/`client_game` pour établir quelles valeurs sont les points
  d'entrée et lesquelles sont calculées en %; ne pas multiplier ni
  normaliser à l'aveugle.
- Première preuve client Reforged : `FUN_00a44bb0 @0x00a44bb0`
  (rf_game/decompiled.c) lit plusieurs champs distincts de l'état profil;
  les valeurs entières de ratings affichées utilisent `raw * 0.01`
  (`DAT_00ecb57c=0.01`, confirmé dans `.rdata` de `game_.exe`), tandis que
  les cellules de taux lisent des champs `float` séparés (ex. `+0xec` et
  `+0xf4`, ou `+0xf0` et `+0xf8`). Donc les colonnes 296 ne peuvent pas être
  supposées être un seul « pourcentage » qui ferait également la valeur
  brute. Le routage exact de chaque valeur 296 vers ces offsets est encore
  à suivre dans le handler client avant de modifier les formules serveur.

## Étapes et critères de sortie

### 0. Préserver et inventorier

- Ne pas annuler les modifications déjà présentes dans le worktree.
- Lire `server/AGENTS.md`, `docs/MASTER-IMPLEMENTATION-PLAN.md`, les deux
  clients cibles et les anciens plans, puis construire une matrice de
  preuve exigence par exigence.
- Relever les versions de DLL/processus effectivement écoutés, la révision
  des fichiers et les tables DB concernées sans exposer les secrets `.env`.
- Aucun déploiement ou test client ne doit écraser les données du joueur.

### 1. Inventaire test3 — bloquant

- Reconstituer la requête NCID 25 et la réponse 312 depuis
  `CNC_CZ_ClientEnterNodeInventory::Serialize @0x8233xxx` et le parser de
  `client_game`/`rf_game`; relever tous les champs, tailles, bitmap, flags,
  containers et le paquet 280 de disponibilité.
- Comparer par bytes un corps natif et le corps serveur ; ajouter un test
  golden pour au moins un sac avec slots vides/occupés, stacks, maker,
  enchant, items container 0/1 et holder.
- Vérifier `GetLiveItemsAsync`, `GetAllBagCapacitiesAsync`, le mapping
  Dapper et les doublons/dépassements des deux buffers natifs.
- Corriger le chargement de TOUS les conteneurs ouverts de test3 sans
  déplacer/supprimer d'items. Valider visuellement chaque onglet/sac sur les
  deux clients, puis achat/usage/déplacement et reconnexion.

### 2. Certificats et profil — bloquant

- Suivre le certificat possédé/sloté : `player_license` → `LicenseService`
  → `EnchantService.GetUseCommands` → contribution → calcul des champs 296,
  492 et 493.
- Décoder l'enchant 52515 et les commandes de stats à partir de la table
  native et du `decompiled.c`; ne pas interpréter la description traduite
  comme preuve du nombre ou de l'unité wire.
- Étendre le modèle de contribution uniquement aux attributs confirmés
  (EVA/ACC, ratings, primaires, PV/PM, etc.) et inclure les certificats
  équipés et combos actifs. Maintenir les stats de combat et de profil
  cohérentes.
- Ajouter tests de commande/enchant → delta profil + tests de slot,
  déséquipement, changement de classe et reconnexion.
- Valider les valeurs affichées et le changement immédiat sur les deux
  clients.
- Reverser la conversion des colonnes « points » et « % » du profil
  (ACC/EVA/Parry/Block/crit/vitesses) sur le client décompilé; corriger le
  contenu 296/492/493 uniquement à partir de cette preuve.
- Tracer, dans `client_game` puis `rf_game`, la désérialisation 296 vers les
  champs d'état lus par `FUN_00a44bb0`; établir pour chaque ligne profile
  le champ entier `raw*0.01`, le ou les floats de taux, le signe, le cap et
  la source 296/492/493. Ne pas reprendre les `Physical*Rate` C# comme
  équivalents de la valeur brute sans cette correspondance.

### 2b. Équipement et catégorisation — bloquant

- Capturer la table `equipment1/2` active de test3 (group, loc, item_id) et
  rapprocher chaque ligne du `S_Item` (kind, EquipLoc, item class/type).
- Reverser le handler d'équipement côté client et le chemin natif
  `CItemFactory::Equip`/`SwitchItemLocation`: la réussite/refus, les
  conteneurs, locs canoniques 7/8/10, dual-wield et conditions de
  déséquipement. Les locs 9/35 sont des types bruts remappés en 7/10 par
  le serveur natif; ils ne sont pas les locs d'équipement canoniques.
- Le port C# couvre maintenant `DoWeaponUnEquip @0x8092e70`: la classe
  native 0x14 conserve le slot distance10 avec les armes7/8; hors classe
  0x14, l'équipe du slot10 retire 7/8 et l'équipe 7/8 retire10. Un 277
  distinct part par item déplacé et le code vérifie d'abord le nombre de
  slots libres. Reste à confirmer en live le ClassId actif (test3 stocke4)
  et les notifications/modèle sur les deux clients.
- Pour chaque auto-déséquipement natif, émettre son propre NCID 277 avant/
  avec l'ack de l'équipement demandé: `[s16 srcType=1][s16 srcLoc]
  [s16 srcGroup][s16 dstType=0][s16 dstLoc][s16 dstBag][u8 success=1]`.
  L'item doit rester visible dans un sac ouvert; ne jamais déplacer plusieurs
  armes en DB sans réserver une destination pour chacune.
- Valider que la requête equip ne peut réussir que si le type client natif
  autorise l'item; ajouter tests négatifs pour Broken Demon Pot et livres de
  quête, tests positifs pour 1H/2H/armures/accessoires/munitions/montures.
- Recomposer et parser les 311 (entrée), 282/276 (refresh d'équipement),
  277 (ack de move) et 283 (modèle) à byte près. Ne pas émettre un `OK`
  ni persister une ligne equipment si le move/compatibilité a échoué.
- Avant le 311, traiter les anciennes lignes actives hors slot canonique
  (ex. 9→7, 35→10) de façon transactionnelle; déplacer les items non
  équipables vers un sac libre uniquement si toutes les lignes et serials
  sont préservés. En cas de conflit ou d'inventaire plein, ne rien effacer
  et produire un diagnostic; aucun SQL de nettoyage spécifique à test3.
- Canoniser aussi le combat/profil sur les indices natifs 7/8/10; ne plus
  traiter 9/35 comme des emplacements d'arme valides.
- Au prochain ticket test3, vérifier qu'aucun 311/282 n'annonce de kind44/
  EquipLoc0 comme item équipé; si le normalizer le renvoie au sac, le 312
  qui suit doit l'afficher avec le même serial.
- Tester équiper, changer d'arme, déséquiper chaque slot, relog et comparer
  DB + client sur les deux clients; préserver toute la DB existante.

### 3. Combat — priorité critique (freeze)

- Capturer une reproduction complète : RX attaque/mouvement/stop, TX 302,
  267/271, 272, 280, 300/305, 326/444, état serveur du mob et joueur.
- Tracer chaque NCID 292/299/300 et l'enchant id avec l'état des buffs
  client/serveur. Ne pas coder d'EnchantReport générique sur chaque hit.
  NCID 292 `effect=52022` est un DOT réel; l'envoyer seulement si une
  capacité native précise le déclenche.
- Vérifier dans la source et la nouvelle capture que le tick d'auto-attaque
  n'envoie aucun NCID 292/52022 générique; si la teinte/DOT persiste sans
  292, tracer 299/300, enchants restaurés et effets des spells mob.
- Examiner `HandleMeleeAsync`, casts, AoE, `MobCombatLoopAsync`,
  `FinishMobDeathAsync`, `ScheduleCorpseDespawn`, `ScheduleRespawn`,
  `HandleMoveSelfAsync` et `SendCombatExitAsync` ; retrouver le sens des
  flags de contrôle caméra/acteur dans le client désassemblé.
- Introduire une seule boucle de riposte par mob et un jeton/génération par
  engagement. Invalider et observer l'annulation à la mort, deaggro,
  changement de cible, migration, logout et fermeture de session.
- Aucune trame d'attaque/302/272 ne doit être émise après la transition
  `Alive=false` ou après invalidation de la génération. Les HP client et
  serveur restent cohérents ; le mob à 0 PV ne peut plus cibler/frapper.
- Ajouter tests déterministes de concurrence : mort entre chaque await,
  deux requêtes d'aggro simultanées, fuite hors leash, arrêt du joueur,
  plusieurs mobs, logout/migration pendant le tick.
- N'émettre que les séquences native-byte-proven. Après le fix, faire une
  session de test longue sans freeze, puis une reconnexion.

### 4. Ré-audit intégral du prompt3

Pour CHAQUE ligne du prompt original, suivre les couches DB, modèle,
service, handler, codec, paquet client et persistance. Le résultat doit
indiquer : code trouvé, preuve protocole, tests automatisés, tests live sur
référence et Reforged, lacunes restantes. Aucune ancienne case `[x]` n'est
conservée sans refaire cette vérification.

Domaines à auditer explicitement :

1. Combat/aggro/leash multi-mobs, sélection au curseur, attaque après stop,
   mort et retour spawn.
2. `/help`, commandes GM, arguments et commandes réellement disponibles.
3. Casts mobiles, interruption, délais et animation.
4. Status positif/négatif, HoT/DoT, invincibilité, montures, persistance.
5. Objectifs de collecte/kill, dialogues et proposition/acceptation des
   quêtes ; Quest Log, Available Quests et tracker.
6. Vente NPC, achat/revente, buyback, synchronisation stack et potions.
7. Communauté : amis, états online/offline, blacklist, recherche joueurs,
   famille et entraide.
8. Icônes NPC, raccourcis classe, cooldowns.
9. Montures : use, durée, déplacement et reconnect.
10. Profil : statistiques de base, équipement, titres, certificats, combos,
    rafraîchissement immédiat et reconnexion.
11. Progression perso/classe : niveaux, CP, spells généraux/privés,
    persistance et affichage après reconnect.
12. Item mall et loyalty : groupes, limites, achats/livraisons et monnaies.
13. Sacs porte-sacs : placement, capacité ouverte, rangement, persistance.
14. Trésors au sol : overflow, ownership, pickup, expiration, migration.
15. License Knowledge/KP, certificats (octroi tiers I/II, cross-class) et
    combos.
16. Rate EXP perso/classe x100, sur mobs réels et sans double attribution.
17. Mobs immobiles (coffres/objets de quête), handles et sélection.
18. Stats/équations natives demandées : STR/INT, AGI/LCK/WIS, vitesse 300,
    ACC/EVA, critique, DEF/MDEF, parry/block et résistances élémentaires.
19. Cohérence UI pour chaque mutation en session et après reconnexion.
20. Catégorisation/compatibilité équipement, placement et déséquipement
    des items normaux, quest-only et consommables.
21. Points bruts vs pourcentages de l'UI profile; conversion native client.
22. Paquets d'effet mob/hit, DOT, MP loss, tint et suppression du statut à
    la fin de combat/mort/deaggro.

Pour chaque domaine, si aucune preuve ne peut être obtenue, état
« non vérifié », jamais « fait ». Toute régression client est bloquante.

### Lacunes explicites déjà retrouvées pendant le premier scan

Ces points ne sont pas déclarés « à finir plus tard » dans l'ancien plan :
ils sont des implémentations manquantes à vérifier et traiter dans l'audit
intégral.

- `FamilyHandlers.cs`: `FamilyInstanceGetData`, `FamilyTownQuery`,
  `FamilyTownMemberQuery`, `FamilyBuildingRepair` et `FamilyStatue` ont des
  delegates silencieux; `FamilyTownEnter` ne fait qu'un log.
- `HousingHandlers.cs`: plusieurs opérations de logement renvoient
  `Task.CompletedTask`; création de salle/loyer et `character_fastdress`
  portent encore un TODO explicite.
- `TeamHandlers.cs`: la recherche/itération des annonces de recrutement
  reste marquée TODO.
- `InventoryHandlers.cs`: l'échange de socket non pris en charge refuse
  explicitement; `ItemActionHandlers.cs` a encore les TODO du slot de gemme
  et un ack sans effet.
- `CombatHandlers.cs`: un chemin de combat alternatif garde des stats
  d'attaquant provisoires, cast timer non annulé et revive-area TODO.
- `QuestEngine.cs`: certaines familles d'actions mission sont ignorées;
  `MissionServer.cs` documente encore une réponse 71→403 non implémentée.

### 5. Validation et livraison

- Build Release : zéro erreur, zéro warning.
- Exécuter tous les tests ; ajouter les tests de régression ci-dessus.
- Vérifier longueurs exactes de paquets contre désassemblage/captures.
- Vérifier ports et liens internes, puis redéployer seulement si les
  tests réussissent et préserver PostgreSQL/données de test.
- Validation jeu : référence et Reforged ; log seul ne vaut pas validation.
- Mettre à jour le plan principal et ce plan à chaque preuve, en séparant
  fait/test automatisé/validation live.

## Avancement vérifiable

- [x] Inventaire des données initiales et chemin de réponse 25→312 relevé.
- [x] Certificat de test3 identifié : item 33281 → enchant 52515.
- [x] Repository 312 lit tous les conteneurs 0..4; slots négatifs exclus.
- [x] Capacité 32 prouvée par capture native, schéma/fallback corrigés et
  migration additive appliquée (test3: 32+30; 46 rows actuellement).
- [x] Enchants de certificats normalisés s16→u16; contribution EVA et
  refresh de stats login ajoutés.
- [x] Boucle mob unique par génération; invalidation mort/respawn et
  contrôles différés ajoutés.
- [x] EquipType→slots natifs factorisé/testé; normalizer du group0 appelé
  avant 311, mais son déplacement réel sur le compte test3 reste à vérifier.
- [x] 292/52022 générique absent du source `MobCombatLoopAsync`; ancienne
  trace append-only identifiée; Zone relancée PID 76725 sur la DLL Release.
- [x] Build Release : 0 avertissement / 0 erreur; 415/415 tests passés.
- [x] Zone relancée depuis la DLL Release PID 76725; port 5201 et liens
  Ticket/World/Mission rétablis.
- [ ] Captures/format client 312 comparés byte à byte; la capacité native
  du sac de base est déjà prouvée à 32 par la capture.
- [ ] Inventaire test3 affiché correctement sur les deux clients.
- [ ] Après reconnexion test3, logs 312 attendus : 22/32 items sac0,
  24/30 sac1, `invalidSlots=0`; vérifier visuellement les onglets.
- [ ] Le normalizer a canonisé les armes actives 9→7 et 35→10 sans perdre
  serial, puis déséquipement/rééquipement validé en jeu.
- [ ] Sémantique/unité EVA de 52515 confirmée par preuve native.
- [ ] Stats certificat visibles et rafraîchies sur les deux clients.
- [ ] Conversion raw/% profile mappée depuis `FUN_00a44bb0` vers 296/492/493
  et testée sur les deux clients (l'ancrage 3800 EVA reste à vérifier).
- [ ] Cause du freeze confirmée par trace/repro.
- [ ] Nouvelle session Zone sans 292/52022 sur attaque standard; pas de
  DOT, cible morte qui frappe ou blocage caméra/mouvement.
- [ ] Boucle combat exclusive, annulable et testée sous concurrence live.
- [ ] Audit intégral prompt3 réalisé sans héritage des cases anciennes.
- [x] Build Release et suite complète automatisée 415/415.
- [ ] Validation jeu des deux clients.
