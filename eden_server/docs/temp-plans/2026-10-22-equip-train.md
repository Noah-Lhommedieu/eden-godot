# Plan — système d'entraînement d'équipement (EquipTrain / « Mana Infusion »)

Preuves natives (zone/decompiled.c) :
- `CNE_CZ_ZoneServerEquipTrainSetItem` (230) : corps `[s16 container][s16 itemIndex][s16 containerKey][u16 count][u8×count types]` — container −1=clear, 0=sacs(+0x4e4), 1=équipé(+0x4fc).
- `CPlayerFactory::SetEquipTrainItem` @0x80ee6b0 : arbres de conteneurs, index vecteur + clé ; valide existence, flags (+0x30 bit31 ou clone+0x24&0x10), types ≤14, bit dans le masque itemdata+0x2c, pas de doublons → `CItemFactory::SetItemTrainType` (types vierges) puis `SetEquipTrainItemID`.
- `CItemFactory::SetItemTrainType` @0x8099940 : exige EXACTEMENT 3 types ; si train_level==0 → 1, exp=0 ; types→+0x60/62/64, levels→+0x66/68/6a ; UpdateCloneItemAddAttr + BackupHPMPSP + RefreshEQsAttr + ResetBackupHPMPSP + CItemDBO::ModifyItemTrain + FlushAll + 369 complet.
- `CCharacter::SetEquipTrainItemID` @0x840b670 : +0xd54=id, FindEquipTrainItemLocation (équipé d'abord puis sacs → +0xd58 type, +0xd5c clé, +0xd5e index), persiste `player_characters.eqtrain_item_id`, émet **570** `[s16 type][s16 index][s16 key]` si la localisation a changé.
- `CCharacter::GainEquipExp` @0x841b1d0 : conditions (id≠0, type=1 équipé, serial match, durabilité>0, train_level<itemdata+0x3e(=col14), train_level≠0, ≥1 type/level ≠0 ; index ∈]0,6[ → check archétype classe+0x28 ∈{1,2,3} vs kind mod-3) ; seuil = S_EquipTrain[level+1].exp ; cap : train_level < player level ET < item req level ; à chaque up → slot pondéré params 504-506 (0.5/0.3/0.2) +1 ; textes 5494 (`%d` gain) / 5495 (`%d` niveau) ; 369 complet + ModifyItemTrain + FlushAll ; appelé par GiveTeamExp avec `exp × param496 (0.2)`.
- `UpdateCloneItemAddAttr` @0x8097d00 : par slot `value = slotLevel × mult(EquipLoc) × S_EquipTrain[item+0x3e].coef[type]` ; mult = params 497-503 (7/8/0x13→497=1.3 1H, 9/0x23→498=2.6 2H, 1→499, 2→500, 3→501, 4→502, 5→503).
- `CNC_CZ_ClientEquipTrainItemChanged` (570) : `[s16 type][s16 index][s16 key]` (−1 quand vidé).
- Wire item (274/312/369) : queue train = `[u16 lvl][u32 exp][t1][l1][t2][l2][t3][l3]` ENTRELACÉ (ctor 369 @0x80c85xx : +0x48=t1, +0x4e=l1, +0x4a=t2, +0x50=l2, +0x4c=t3, +0x52=l3 — param_25+8..+0x12 = [t1,t2,t3,l1,l2,l3] séquentiel).
- S_EquipTrain.ini : `[u8 level][float exp cumulée][15×f32 coef]` — coef indexé par `itemdata+0x3e` (required level col14), exp seuil indexée par train_level+1.
- CClassData+0x28 = archétype (col 12 ancrée = certsBase−10) ∈{1,2,3}.

## Lots
- [ ] Parser `EquipTrainParser` + `GameData.EquipTrain` + `ItemDef.TrainMask` (col10) + `ClassDef.Archetype` (certsBase−10)
- [ ] `ItemEntity` + `InventoryRepository` : train_* + extra_combo10/11 + gold_enchant (LiveSelect/row/ToEntity), `UpdateItemTrainAsync`, `Get/SetEquipTrainItemIdAsync`
- [ ] `NativeItemCodec.WriteItem` : remplir la queue 34B (train + ec10/11 + gold + mb1-3 + energy/star)
- [ ] `MagicBoxPackets.BuildModifyItemInfo` : corriger l'ordre wire en [t1,l1,t2,l2,t3,l3]
- [ ] `ZoneMessages.g.cs` 230 → corps variable `u8[count]`
- [ ] Codec 570 + handler 230 (validations natives) + résolution localisation équivalente
- [ ] `GainEquipExp` hook kill (×param496) : level-up pondéré, 5494/5495, 369, persist, refresh profil
- [ ] `EquipmentStatService` : +15 canaux via coef × slotLevel × mult EquipLoc
- [ ] Textes EXP chat 968/969 (char/class) absents → ajout dans les grants
- [ ] Build + tests
