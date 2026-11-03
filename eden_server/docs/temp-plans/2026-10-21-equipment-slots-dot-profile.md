# Correctif prioritaire — slots équipement, faux DOT et profils

Date : 2026-10-21

## Symptômes / preuves

- Screenshot fourni : items visibles dans les catégories d'équipement
  incorrectes; déséquipement impossible; profil affiche valeur brute + %
  incohérents.
- DB `test3` actuellement : 11 lignes equipment; group 0 loc 9 = item 31926
  (`EquipLoc=9`), loc 35 = item 23860 (`EquipLoc=35`).
- Serveur natif `CItemFactory::Equip` @0x80b5810 réécrit les destinations :
  EquipType 9→loc7, 35→loc10, 19→7, 10→11, 11/20→12/13, 12→14/15.
  `CCharacter::GetWeaponItem` @0x840cea0 consulte les slots 7, 8, 10.
  Donc loc9/35 stockés par le port C# historique ne sont pas les slots
  d'équipement canoniques du client.
- `DoWeaponUnEquip @0x8092e70` gère aussi les interactions loc10↔7/8 avec
  exception de la classe native `0x14`; le handler C# actuel traite 2H/
  off-hand uniquement. Le natif émet un 277 par item auto-déséquipé avant
  l'ack de la demande principale.
- `S_Item` Broken Demon Pot ids 34539-34546, 35414-35416, 37558-37563 ont
  Kind 44 et `EquipLoc` vide; quête book 39329 a aussi `EquipLoc` vide.
  Le handler les refuse maintenant, mais l'UI peut porter un clone stale ou
  un packet d'entrée mal formé.
- Les logs de l'ancienne DLL contiennent NCID 292/effect 52022
  (`0xcb36`) sur le handle mob. `T_Enchant[52022]` = Perforation, DoT
  150/s + perte 100 MP. La source courante ne contient plus le send
  inconditionnel du `MobCombatLoopAsync`; le binaire lancé doit être rebâti/
  redémarré, puis la régression vérifiée sur une nouvelle session.
- Les sorties 296 actuelles sont non validées; le screenshot prouve que
  l'interface attend des paires valeur/ratio. La conversion doit venir du
  consommateur UI du client, pas d'une approximation dans le serveur.
- `rf_game` `FUN_00a44bb0 @0x00a44bb0` lit des ratings entiers séparés des
  floats de taux; les entiers d'affichage sont multipliés par
  `DAT_00ecb57c=0.01` (constant confirmé dans le `.rdata` du jeu). Le mapping
  du parse 296 vers ces offsets reste à prouver avant une correction.

## Modifications permises

1. Extraire la table de correspondance EquipType→loc canonique en composant
   testé et la partager entre les handlers, l'entrée 311, 282/283 et les
   calculs d'armes.
2. Avant d'émettre 311, analyser les items actifs group0. Remettre les
   anciennes locs brutes 9/35 en 7/10 si libre; sortir un item non
   équipable vers un sac ouvert seulement si destination disponible.
   Préserver le serial et toutes les colonnes. Conflit/aucun slot => aucune
   suppression; journaliser et refuser une mutation.
3. Compléter les conflits mains/distance par le natif `DoWeaponUnEquip`;
   réserver assez de slots avant toute mutation et envoyer le 277
   auto-déséquipement pour chaque clone déplacé.
4. Vérifier que la dernière DLL n'envoie aucun NCID 292/52022 générique au
   tick de riposte. Un effet ne sera ajouté que par une voie S_Spell/
   S_Enchant de mob/joueur vérifiée.
5. Réauditer tous les NCID 292/299/300 et la boucle combat; ne pas désactiver
   les vrais procs/DoT configurés.
6. Reverse le client `client_game` puis `rf_game` pour chaque couple raw/% du
   profil. Ne modifier 296/492/493 avant d'avoir identifié formule, échelle
   et cap de chaque champ.

## Tests / validation requise

- Tests mapping natif: 7/8/10, 9→7, 35→10, paires 11/20 et 12/13, item
  non équipable; aucune collision ni perte d'items.
- Test entrée 311 + refresh 282 pour arme principale, offhand, ranged,
  ensembles sauvegardés et inventory overflow.
- Test équipement: clic droit/drag, swap, conflits 2H et 7/8/10, ack 277
  de chaque auto-unequip, déséquipement initial login, reconnexion et DB
  row unchanged except canonical loc.
- Test combat sans proc: aucun 292/52022, HP mob serveur/client cohérents,
  aucune teinte ou DOT après stop/mort/deaggro, caméra/move/action actifs.
- Test mob avec vrai proc S_Ai/S_Spell si le data/decomp confirme ce chemin.
- Cartographier les champs raw et taux que `FUN_00a44bb0` affiche depuis
  296/492/493, puis tests en bytes pour ACC/EVA/Parry/Block/AS/Cast/Crit;
  capture et vérification visuelle sur référence et Reforged.
- Build Release zéro warning/erreur, tests complets, ports internes vérifiés.

## Statut

- [x] Symptômes et preuves de départ consignés.
- [x] Resolver type→slots natifs; normalizer group0 appelé avant 311;
  tests unitaires et codec 312 étendus.
- [x] `DoWeaponUnEquip` slots7/8/10, exception native class0x14 et 277
  par item auto-déplacé câblés; body 277 testé byte-exact.
- [x] Source `MobCombatLoopAsync` sans NCID 292/52022 générique; ancienne
  DLL avec ces paquets remplacée par Release Zone PID 76725.
- [ ] Conversion du profil reversée depuis les deux clients.
- [ ] Au prochain login test3, confirmer DB canonical 9→7/35→10 et que les
  items non équipables vont au sac sans perte.
- [ ] Reproduire combat sur la DLL courante et confirmer zéro DOT/tint/freeze.
- [x] Release build 0 warning/0 erreur; 415/415 tests passent.
- [x] Zone Release relancée; ports inter-services reconnectés.
- [ ] Validation live des deux clients.
