# Plan — Pets (Elf), cast en mouvement, stabilité combat, fréquence position→mobs

Date : 2026-10-21

## Symptômes live
- Équiper un pet (ex. Combat Bunny Sprite) n'ajoute rien au panneau Pet
  (Pet List vide, « Number: 0/10 » — screenshot).
- Le mode combat s'interrompt/reprend autour d'un cast; courir en rond
  en combat peut figer le perso.
- Le cast ne doit PAS s'annuler au mouvement (OK) mais doit pouvoir
  COMMENCER pendant un déplacement.
- Les mobs ne reçoivent la position du joueur que ~1 fois/seconde →
  poursuite saccadée; la fréquence doit être augmentée.

## Preuves natives déjà relevées
- Schéma natif : tables `elf`, `elf_container`, `elf_work`, `pet_lottery`,
  `pet_new_lottery` — `elf`/`elf_container` sont vides pour test3;
  `player_elfs` est une table maison non reliée au wire natif.
- `CNode::BornElfPet(CCharacter*,int,short)` = naissance d'un pet équipé.
- Entrée : `CNC_CZ_ClientEnterNodeElfInfo` (412) +
  `CNC_CZ_ClientEnterNodeElfContainer` (424) — formats générés connus.
- `CElfPet` (GetMovement, CastSpell, RefreshEQsAttr, pet gear container
  4 slots), `CPetController`, `CKFElfManager`, `S_Partner.ini` (PetExp),
  `CEC_SummonEquipPet` (enchant command qui crée le pet à l'équipe),
  `CNE_CZ_ZoneServerSendPetCmd`, `CNE_CZ_ZoneServerPetModeChange`,
  `ClientPetShortCut`, `ClientLookElfPetEquip`, `ClientPlayElfPetAction`,
  `ClientPetLotteryEnd/Result`, `ClientPetUpgradeCancel/Casting`.

## Étapes
1. **Pet équipé** : reverser `CEC_SummonEquipPet` (création elf à
   l'équipe), `BornElfPet`, `EnterNodeElfInfo`/`ElfContainer` (entrée),
   persistence `elf`+`elf_container`; émettre la liste pet à l'entrée
   et à l'équipe; `PetModeChange`, `SendPetCmd`, `ClientPetShortCut`.
   Limiter le lot à : création/équipe/déséquipe + liste UI + summon
   visuel; les menus Battle/Pray/lottery/upgrade restent un second lot.
2. **Cast en mouvement** : vérifier que la requête de cast n'est pas
   refusée quand `moving`; tracer le rejet côté handler 288/278.
3. **Stabilité combat** : pourquoi un cast fait sortir/rentrer de combat;
   fige au mouvement en boucle — chercher une émission de stop/délock
   autour du cast et le guard génération.
4. **Position joueur→mobs** : mesurer l'intervalle actuel (tick 1 s?),
   re-émettre la position aux mobs en poursuite à une fréquence plus
   élevée ou sur paquet move du client; vérifier le format 326/267.

## Critères de sortie
- Build Release 0 erreur/0 warning, tests complets verts.
- Pet équipé → ligne dans `elf`, apparition dans le panneau Pet, summon
  visible; déséquipe → retrait propre; reconnexion → persistance.
- Cast démarré en mouvement; mouvement pendant cast ne fige pas;
- position propagée aux mobs en poursuite à cadence >1 Hz (testable en
  log/trace).
