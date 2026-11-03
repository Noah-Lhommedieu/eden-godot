# Plan — Pets (CElfPet), cast en mouvement, cadence poursuite mobs

Date : 2026-10-22 — demande live : Combat Bunny Sprite inerte, combat instable
(sortie/retour de mode combat au cast, freeze en courant en cercle), cast à
démarrer en mouvement, mobs qui poursuivent par sauts (~1 pos/s).

Tous les formats listés ci-dessous sont issus du désasm
`re/ghidra/zone/decompiled.c` — aucun paquet spéculatif.

## A. Sous-système pets natif (CKFElf / CElfPet)

### Preuves natives
- `CItemFactory::DoUseItem` @0x80b8a10 : item kind **0x3c (60)** →
  `CKFElfManager::AddElf(char, cloneItem, true)` — le pet s'ajoute à la
  liste en **utilisant** l'item, jamais via les slots d'équipement.
- `CKFElfManager::AddElf` @0x8073bf0 : slot libre dans `char+0x8ac`
  (tableau **10 pets** → UI "Number: 0/10"), `CPartnerData`
  (S_Partner.ini clé = item_id) + `CPartnerGrowData` (S_PartnerGrow.ini
  clé = niveau). Émet `ClientElfAdd` **413** + insère `elf` /
  `elf_container` (4 slots de gear pet) / `pet_lottery` / `pet_new_lottery`.
- `CKFElfManager::CallElf` @0x8072c30 : `CNode::BornElfPet(node, char, 0,
  loc)` → entité `CElfPet` modèle `m%06d` (S_Partner col2 M279010 →
  monster template), `SetElfStatus(2)`, notify `ClientPetShortCut`
  **513** `[u8 1][s16 loc]`, `ClientElfUpdateAttr` **418**
  `[s16 loc][s16?][i32 elfAttr][str ""]`.
- Broadcast aux témoins : `ClientSummonPet` **438**
  `[i32 ownerCharId][i32 petObjId][u16 nodeId][i32 sightHandle]
  [i32 pet+0x18][i32 mode 0=summon/2=despawn]` (désasm @0x810c6xx).
- `CNE_CZ_ZoneServerElfCallElf` **109** `[s16 loc]…` → CallElf.
- `CNE_CZ_ZoneServerElfReturn` **108** → renvoi → `ClientElfReturnOK` 414.
- `CNE_CZ_ZoneServerEnterNodeElfInfo` **110** → réponse **412**
  `[u16 n][n × record ~46 champs][s16]` (désasm Serialize @0x81e7xxx).
- `CNE_CZ_ZoneServerEnterNodeElfContainer` **113** → réponse **424**
  `[u8 nBags?][per bag: s16 loc, u8 idx, u8 maskLen+mask, items au format
  masked-record de 312]` — même sérialiseur d'item que l'inventaire.
- `CNE_CZ_ZoneServerPetModeChange` **187** → battle/passive.
- `CNE_CZ_ZoneServerSendPetCmd` **188** → ordre d'attaque pet.
- `CNE_CZ_ZoneServerElfWork` (déjà géré) : mode 2 = CallElf.
- `Combat Bunny Sprite` item **23993** : S_Item kind 60, EquipLoc 0 →
  S_Partner 23993 (I00948, M279010, battle spell 58016, grow lv auto).
- Tables `elf` (elf_id, player_id, template_id, status, given_name,
  level, exp, hp, grow×4, loc, mix_star, battle_spell_1..5,
  integrate_spell_1..5), `elf_container` (items gear pet), `elf_work`.

### Implémentation
- [ ] Parser `S_Partner.ini` (31 cols) + `S_PartnerGrow.ini` →
  `GameData.Partners` / `PartnerGrow`.
- [ ] `ElfRepository` : load all elfs du joueur à l'entrée, insert elf +
  `elf_container` (4 slots) à l'AddElf, update status/name/hp.
- [ ] `ZoneEntryFlow.Elf.cs` partial : AddElfAsync (kind 60 dans
  UseItemFinal), CallElfAsync (spawn entité + paquets), ReturnElf,
  PetModeChange, SendPetCmd.
- [ ] Corriger `CNC_CZ_ClientElfAdd` (413) : le généré a aplati les 3
  vecteurs — wire réel = `[s16 loc][i32 elf_id][i32 template][s16 level]
  [i32 exp][s16 hp][str name][s16 quality][u16 n][n×s16 grow]
  [s16 mix_star][u16 n][n×s16 battle][u16 n][n×s16 integrate]`.
- [ ] Corriger `CNC_CZ_ClientEnterNodeElfInfo` (412) : record ~46
  champs par elf (désasm @0x81e7xxx) — réécrire le Serialize.
- [ ] Spawn pet : entité `LiveMob`-like suivant le joueur (modèle
  M279010 via monster template), sight broadcast aux témoins + 438.
- [ ] `ElfWork` mode 2 → CallElfAsync au lieu du ChangeElf actuel.
- [ ] Tests : 413 wire bytes, 412 records, AddElf→DB, CallElf→spawn.

## B. Cast en mouvement + stabilité combat

### Constats code
- `HandleSpellHitAsync` (NCID 9) n'a AUCUNE garde de mouvement — le
  cast peut théoriquement démarrer en marche. Mais si le client est en
  état "course poursuite" (cible hors portée → silence natif), ou si
  `CastingSpell` reste posé après une interruption, tout cast suivant
  reçoit 279 flag=0 → refus permanent = le "je peux pas cast en bougeant".
- `catch (OperationCanceledException) {}` dans CastSequenceAsync ne
  purge PAS CastingSpell — vérifier chaque annulation (BreakCast le
  fait, mais `st.CastCts.Cancel()` dans HandleSpellHitAsync annule
  l'ANCIEN cast dont la séquence conserve CastingSpell… → course :
  l'ancien catch OCE laisse CastingSpell posé sur le NOUVEAU sort →
  l'état est incohérent pendant toute la durée du nouveau cast).
- "Sort puis re-entre en combat" : cast → client envoie 5 type=2
  (stop auto-attaque) → `stillTargeted=false` (le mob n'a pas encore
  riposté) → SendCombatExitAsync → blast résout → aggro → re-enter.
  Fix : si `st.CastingSpell != 0` ou `st.AutoAttacking` vient d'être
  interrompu par un cast en vol → ne PAS émettre la sortie de combat.
- Freeze en cercle : à vérifier — suspects : 279 flag=0 bouclé,
  CastingSpell zombie, ou paquet malformé en mouvement (326 broadcast).

### Implémentation
- [ ] `CastSequenceAsync` : purger CastingSpell aussi sur
  OperationCanceledException (flag `interlocked`/sequence-id pour ne
  pas écraser un cast plus récent — CastSeqNo monot. dans EntryState).
- [ ] `HandleMeleeAsync` stop (5 type=2) : si cast en vol ou aggro
  encore active → sel=1 seulement, jamais de sortie complète. Ajouter
  la condition `st.CastingSpell != 0` dans le test d'exclusion.
- [ ] Cast en mouvement : vérifier aucune garde cachée côté dispatch
  (op 9 passe par TryHandleAsync avant le dispatcher — OK) + s'assurer
  que MoveSelf ne casse pas CastSequenceAsync (déjà vrai : 287
  désactivé) ET que la résolution du cast utilise la position à
  l'échéance (déjà le cas : ExecuteSpellHit relit st.PosX).
- [ ] Vérifier `HandleCastNowhereAsync`/`HandleCastLocationAsync`
  (16/17/59) : mêmes gardes CastingSpell/CastCts + OCE purge.

## C. Cadence de poursuite des mobs

### Constats
- `MobCombatLoopAsync` : `Task.Delay(1600)` → pas de 6 u par tick
  (3,75 u/s effectif) → sauts visuels + position joueur relue à chaque
  tick donc le mob saute vers une position d'il y a jusqu'à 1,6 s.
- Vitesse mob native : `S_MonsterStep.ini` (WalkSpeed/RunSpeed par code
  Mxxx) — à résoudre par le step-group du template (à trouver dans la
  conversion CMonsterData→CMonsterTemplate).
- Riposte cadencée par la même boucle → attack speed à découpler du
  tick de marche.

### Implémentation
- [ ] Tick de poursuite 250–300 ms ; pas = vitesse×tick
  (S_MonsterStep RunSpeed si résolu, sinon 3,75 u/s par défaut).
- [ ] Cadence d'attaque séparée : gate par `mob.Def.AttackSpeed`
  (ms) — la boucle frappe quand en portée ET cooldown écoulé,
  pas à chaque tick de marche.
- [ ] 326 MovePrediction à chaque tick de marche (déjà émis).
- [ ] Le mob lit `st.PosX`/`PosZ` frais à chaque tick — déjà vrai,
  la cadence seule change.

## D. Validation
- [ ] Build Release 0 warn/0 err, suite tests complète.
- [ ] Live : équiper 23993 → pet en liste (panneau) → invoquer → suit
  le perso ; cast en course ; courir en cercle autour d'un mob sans
  freeze ; mob suit en continu.
