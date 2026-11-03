# PLAN D'ACTION LIVE — synchronisation client↔serveur

## État : Zone+Mission+World redéployés (0 err/0 warn)

### Corrigé (session précédente — confirmé partiellement)
- Quêtes Defeat (Required restauré) ; Collect dynamique (op 145) ;
  MP coût $27 + regen ; XP classe au kill ; NCID 7 move/swap/équipe ;
  montures ride toggle + due_date ; shortcuts par classe ;
  shop serial+slot réel ; sit/freeze 290 ; scènes 411.

### Corrigé cette session (à retester)
- [x] **Lv0 sélection perso** : le natif met `class_lv` à +0x14 de
      l'entrée — on y écrivait `create_flag` (=0). Corrigé.
- [x] **XP classe jamais level-up** : seuil = S_Level col11
      (table[classLv] → lv1→2 = 2000), pas col25 (10000). Cap = niveau
      perso (natif char+0x470). Persist class_lv/class_point.
- [x] **Sauvegarde** : SaveFullStateAsync persist aussi gold +
      class_lv + class_point (avant : seulement pos/hp/mp/exp/level).
- [x] **SetMissionTrace** : wire natif = u16 count + N×i32 (notre classe
      ne lisait qu'un id) ; zone stocke + forward AgentDBACommand à la
      mission → UPDATE mission_trace (UPSERT 10 slots). Le tracker HUD
      est désormais alimenté des deux côtés.
- [x] **Flood auto-quêtes** : auto-grant = flag col6==10 ET col11 vide
      (donneur) — plus que les vraies quêtes auto (1009 tuto…).
- [x] **Équipement** : `EquipLocOf` réel via EquipType.ini (kind→loc :
      1H=7, bouclier=8, 2H=9, distance=10, accessoire=11, munition=13) ;
      clic droit sur équipable = équipe (277 envoyé) au lieu d'ignorer ;
      MoveToEquipmentAsync fait le SWAP de la pièce déjà portée en
      transaction (fix violation unique equipment1_loc_index).
- [x] **Dungeon "niveau non qualifié"** : aucun check serveur trouvé →
      erreur client-side. À confirmer : le client lit class_lv/level de
      son état — le fix class_lv à la sélection peut suffire.

### En investigation — nécessite re-test client
- [ ] HUD tracker : 391 émis sur progression ; SetMissionTrace désormais
      persisté — vérifier que cocher/décocher dans le journal met à jour.
- [ ] "Available quest"/"Unacquired" : ops mission-open/reward/finish
      branchées (388) ; à vérifier les données renvoyées.
- [ ] Broadcast monture aux autres joueurs du node (HYPOTHESIS self OK).
- [ ] Focus perdu → déco : monitorer.
