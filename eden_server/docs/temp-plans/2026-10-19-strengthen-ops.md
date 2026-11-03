# Lot — Renforcement (fortify) + opérations shop/magicbox

Date : 2026-10-19 — suivi live.

## Preuves natives (zone/decompiled.c)

- `CNE_CZ_ZoneServerPlayerAction` (opcode 14) → `SimpleAction(code)` :
  code 13 → CStrengthenOp + `ClientEnterStrengthen`(432) `[i32 npcUid=0][s16 9][s16 0][s16 -1][s16 0]` ;
  14 → CCombineOp + 432 `[...10]` ; 16 → CLifeSkillOp + 432 `[...104]` ;
  101 → CCubeOp + 432 `[...28]` ; 7/8 → EquipAction.
- `CEC_OpenShop` (commande zone `OpenShop <n>`) : 8=strengthen(432[9]),
  9=combine(432[10]), autres types = spell/eq/storage/mail/transport/auction.
- `CNE_CZ_ZoneServerStrengthenItem` (119) → `CItemFactory::StrengthenItem`
  : op 0x26 requis + `TargetInTalkingRange` (npcUid 0 → toujours vrai).
  Field2=int recette combine (S_FamilyCombine ids 1-20 : 1-10 armes,
  11-20 armures). Field3..8 = 3 paires (sac,slot) : cible, matériau,
  chance. Field9 : 1→cast 2s sinon 4s (0x14/0x28 = ×100ms).
- `CheckStrengthenItem` : indices ≥0, cible existe, kind≠0x10,
  niveau <14 ; recette 1-10 → arme(kind 0x11-0x21) OU bouclier 0x22 ;
  11-20 → armure (kind<0x11) ET ≠0x22. Matériau kind==46 (ou EquipLoc
  46), id ∈ liste de la recette ; 40192/40193 (0x9d00/01) exigent
  cible +9. Item chance : CChanceItem kind 8002 (+0x122==0x1f42),
  plage de niveau +0x12a/+0x12c.
- `StrengthenItem` : coût = `repairPrice*0.1 + 10` (DAT_087cc618/620) ;
  enchant 13 (cast) + `ClientStrengthenCasting`(433) `[9][uVar9][0]`.
- `DoStrengthenItem` (fin de cast) : recette S_FamilyCombine col13 =
  taux de base si matériau ∈ liste (sinon annul) ; niveau 0 → taux ;
  1-9 → taux/2^niveau ; 10-13 → S_Parameter 517-520 (armes) /
  521-524 (armures). Pitié : `failCount × EquipAddition[niveau]`
  (armures/kind 0x51) ou `[niveau+1]` (armes) — tableau rodata
  [0,0,0,5,3,1.53,0.5,0.15,0.04,0.01,0.003×5]. Chance item : bonus
  +0x124, garantie si niveau ≤ +0x126, protection +0x129.
  Roll rand×100 < total → succès : niveau+1, broadcast
  `ClientPlayerAction` 0x2b aux voyeurs, reset failCount,
  `ClientModifyItemStrengthen`(436) `[contType][slot][level][contIdx][protected]`,
  `ClientStrengthenItemResult`(435) `[9][1][0][0][0][0]`, persistance
  `strengthen`, paliers 6/8/10/12/14 → AddComboStrengthen (combos
  incrustés — non modélisés encore), niveau>7 → annonce screen type 8.
  Échec protégé → 435 `[9][2]` + failCount++ ; échec simple → 435
  `[9][0]` + eradicate + `ClientDropItem`(275) `[type][slot][idx]`.
- `CNE_CZ_ZoneServerStrengthenClose` (120) : Field1 = type fenêtre
  (9→op 0x26, 10→0x27, 22→0x39, 28→0x52) → ResetOperation +
  Cancel (retire enchant 13 + 434 `[9]` si cast en cours).
- `ExecuteMagicBox` (257) : op 0x58 requis → InitMagicBox →
  CheckMagicBoxCosts fail → `ClientStrengthenItemCancel` [0x1f].
- `VerifyCubeCosts` : op 0x52 requis → InitCubeOp.

## Implémentation

- [x] Preuves natives complètes (ci-dessus).
- [x] EntryState : `ShopOpType`/`ShopOpNpcUid`/`ShopOpNpcHandle` (op
  active), `PendingStrengthen`, `StrengthenFailCount`.
- [x] `PlayerAction` : codes 13/14/16/101 → op + 432 natif (15 no-op).
- [x] Services 9/10 (dialogue NPC) : op + 432 avec npcUid ; 31 : op 31.
- [x] `CNE_119` : validations complètes → cast 433 + pending.
- [x] `DoStrengthenItem` : roll/coûts/persistance/paquets 435/436/275.
- [x] `StrengthenClose` : cancel + 434.
- [x] MagicBox/cube : op lifecycle + cancel natif [31]/[28] (transform
  complet = lot suivant — colonnes magicbox_* à vérifier en schéma).
- [x] Parser S_FamilyCombine : taux (col13) + matériaux (triplets).
- [x] Build 0 erreur / 0 warning.
- [ ] Palier 6/8/10/12/14 → AddComboStrengthen (combos incrustés —
  champs extra_combo* à modéliser).
- [ ] Item de chance kind 8001-8008 (données absentes de cette
  version — validation conservée pour compatibilité si ajoutés).
- [ ] Cube/transmutation complet (S_ItemChest + ExecuteCubeOp).
- [ ] Magic box transform complet (colonnes magicbox_* + ExecuteCubeOp).

## Suite 2026-10-19 (b) — EquipAction + réparation native + CCDataSave

### Fait
- **PlayerAction codes 7/8 = EquipAction natif** (`CPlayerFactory::EquipAction`
  @0x8104540) : code 7 = toggle bit2 de char+0x4ac → ModelModify(0x2000) +
  broadcast PlayerAction aux voyeurs ; code 8 = masque visibilité 10 bits
  (param2==-1 → remplace, sinon toggle bit) → ModelModify(0x4000). Corps
  283 vérifié (Serialize @0x813f8xx) : `[i32 charId][u8 +0x4dc][i32 flags]
  [u16 n][u32×n][u16 0]` — flag 0x2000→`+0x4ac>>2&1`, 0x4000→masque
  `(+0x4ac&3)<<8|+0x4ab`, 0x8000→`+0x4ad>>2&1`.
- **PlayerCCDataSave (NCID 242)** intercepté : wire réel `[u8 idx][u8 f13]
  [u8 f14][u16 count][u32×count]` (Deserialize @0x8273a30) — le généré
  tronquait à 1 élément. Pose `Vis4ad` bit2 depuis f14 + `ActiveCCIndex`
  (char+0x4ad/+0xd98 natifs). `ClientPlayerLicense` pas encore émis
  (format non vérifié).
- **Réparation native** : `GetItemRepairPrice` @0x808c480 +
  `GetItemRepairAllEquipPrice` @0x808c570 —
  `cost = max(1, (int)((int)(repairPrice×0.2)/maxDur × missingDur))`.
  - `repairPrice` = S_Item **col 76** (GameData::CItemData+0x114 → lapis
    CItem+0x14 ; validé par données : épée lv1→4, arme lv93→5082).
  - facteur = `AllFormulaParas+0x56c` = **S_Parameter id 347 = 0.2**
    (« 裝備修復費用參數 »).
  - Éligible = kinds armes 17-29 / armures 1-16+37-40
    (GetAllWeapons/GetAllArmors, `GetType<2`).
  - `RepairAllAsync` utilise la même formule et restaure maxDur.
- **Coût de renforcement** utilise désormais `def.RepairPrice`
  (formule native repairPrice×0.1+10) au lieu de l'approximation
  BuyPrice/SellPrice.

### À faire (inchangé)
- addattr +0xd8/0xdc/0xe0 : scaling de prix par attributs roulés.
- `ClientPlayerLicense` pour CCDataSave.
- Banque/mail/cube complets ; DrillItem ; inlay natif = stub mort.


## Lot banque (commit suivant)

- **Persistence count** : `storage1/2` n'a pas de colonne `quantity` —
  convention native = count des stackables dans `durability` (comme
  l'inventaire). `ToEntity` mappait `Quantity=1` codé en dur → stacks
  perdus au relog. Corrigé : `Quantity=durability`, `SyncStack` après
  chaque mutation, merges/nouveaux stacks écrivent durability=count.
- **Métadonnées** : INSERT/UPDATE storage1 propagent désormais
  `strengthen`, `bind`, `create_time` (colonnes natives présentes).
- **Stackable natif** : `IsStackable = MaxStack>1 && EquipLoc<=0`
  (règle CheckAutoStackItem, identique à InventoryService) — un
  équipement ne fusionne plus jamais en banque, dépôt/retrait = 1 loc.
- **Capacité réelle** : slot libre recherché dans 40+20×expand
  (`expand_storage_cnt`) au lieu de 50 fixes.
- **Deltas client natifs** (désasm CItemFactory::SwitchItemLocation/
  GainItem @0x809cxxx) : source shrink→291 ModifyItemDurability,
  vidé→275 DropItem, destination→274 PutItemIntoInventory ; côté
  banque → 358 ClientGetStorageData complet (format éprouvé).
- **contType 4 = STO** : SwitchItemLocation (0↔4, 4↔4) routé vers
  BankService — auparavant traité comme sac inexistant (désasm
  CPlayerFactory::SwitchItemLocation : 0=INV,1=EQU,4=STO,5=BAGS).
- `AddItemAsync` rapporte les slots touchés (merged/nouveau) pour
  émettre 291 vs 274 correctement.

## Lot courrier (commit suivant)

- **`Arg()` cassé** : le parseur mission prenait le token après la clé —
  avec le format `mail_id = 5` il lisait "=" → mail_id=0 pour TOUS les
  verbes Mail (delete/read/return/get no-op silencieux). Corrigé :
  saute "=" puis premier entier. `CheckTrigger` (format `key= N`)
  inchangé.
- **MailGetGold réel** : mission prélève `player_mail.gold`
  (RETURNING), crédite `player_characters.gold`, répond 76 avec le
  delta → zone émet 406 + attr or 280.
- **MailGetItem réel** : `mailitem→inventory1` en copie intégrale des
  55 colonnes natives (mail_id→loc), serial via ItemSerialAllocator,
  slot = inv_loc/con_index du client si libre sinon premier libre,
  nettoie mailitem + `player_mail.item_id=0`. Zone émet 405 + 274
  ClientPutItemIntoInventory.
- **MailSend natif** (désasm Execute @0x82657f0) : niveau ≥ param 377
  (20), item joint (Field4=bag, Field5=slot) non équipé/lié, or joint
  + taxe fixe 4 ≤ or. Débite or (280), déplace l'item vers mailitem
  (275), insère player_mail + flag pièce jointe.
