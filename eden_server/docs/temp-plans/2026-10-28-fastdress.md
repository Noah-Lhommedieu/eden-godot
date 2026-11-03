# Plan — FastDress (tenues rapides, NCIDs 243-248)

## Sources natives (preuves)

| Pièce | Preuve |
|---|---|
| `CNE_CZ_ZoneServerEnterNodeFastDress::Execute` (243) | zone @0x824f5e0 — corps vide → envoie 593 (tous les records live) + `ILifeEntity::Action(5)` (compteur délai interne, pas un paquet) |
| `CNE_CZ_ZoneServerApplyFastDressData::Execute` (244) | zone @0x8238240 — `[i32 page][i32 mask]` → `ApplyFastDress(char,page,mask,0x32)` |
| `CNE_CZ_ZoneServerSaveFastDressData::Execute` (245) | zone @0x8282f00 — `[i32 page]` → `CItemFactory::SaveFastDressInfo` |
| `CNE_CZ_ZoneServerRecordFastDressInfo::Execute` (246) | zone @0x827b310 — `[5×i32 type,slot,con_id,tar_loc,page]` → `CheckFastDressUseItem` (enregistrement inclus) ; échec → texte 0x32a + 594 mode2 |
| `CNE_CZ_ZoneServerUpdateFastDressPage::Execute` (247) | zone @0x829d8a0 — `[i32 page]` → revert working←live des 24 clés + 594 mode3 |
| `CNE_CZ_ZoneServerRemoveFastDressInfo::Execute` (248) | zone @0x827b900 — `[s16 all][i32 page][i32 key]` → all==1 : `RemoveAllFastDressInfo` (594 mode4) sinon `RemoveFastDressInfo` (594 mode5) |
| `CNC_CZ_ClientEnterNodeFastDress` (593) | Serialize zone @0x8232e70 — `[u16 n][n×(6×i32)]` records live, toutes pages |
| `CNC_CZ_ClientUpdateFastDressData` (594) | Serialize zone @0x8232e70+ — `[s16 mode]` ; si mode∉{1,7,8} : `[s16 n][i32 page][n×(6×i32)]`. Ctor @0x813e6d0 : modes 0/3→records live, 2/4→working, 1/5/7/8→vide |
| Record wire (6 i32) | `{item_id, container_index, from_loc, from_index, page, tar_loc}` = champs record[1..6] — uid[0] et flag[7] internes |
| `SFastDressInfo` | node map +0x14 : `{uid,item_id,con_type,loc,con_idx,page,tar_loc,flag}` (8×i32) ; clear = uid=0,item_id=0,con=0,loc=-1,idx=-1 |
| `char+0x58c` map<page,map> | map LIVE (appliquée/sauvegardée) — Apply/Save lisent ici |
| `char+0x5a4` map<page,map> | map WORKING (éditée) — Record/Remove écrivent ici |
| `char+0x5bc` map<u16,u16> | clé dress → bitpos du mask (`FastDressEquipLoc[i]` → bit i) — init char @0x8435xxx |
| `GameData::FastDressEquipLoc` | zone .rodata @0x87c36e0 — 24 clés : [7,8,14,15,11,12,13,6,10, 601-605, 611-615, 621-625] |
| `fast_dress` table | PK(player_id,page,tar_loc) ; seed natif = 72 lignes (24 clés×3 pages, uid=0/loc=-1) — `CExecutionerDBO::LoadFastDress` mission @0x8156820 |
| SQL natif | SELECT 8 cols WHERE player_id ; UPDATE set id,item_id,container_index,from_loc,from_index WHERE player_id+page+tar_loc |
| `CheckFastDressUseItem` | zone @0x80ff560 — validations : versus(0x3b&0x40), container[f3] existe, slot f2 < taille, item présent (sinon 0x5f7), byte0x3b&0x20, page[f5] existe, kind→catégorie (masks 0x9248/0x4924/0x2492), FastDressCheckEquip (paires d'armes/accessoires + anti-dup uid), CheckLimit, bind `item+0x24|=0x10` (`FastDressDoItemBind` @0x80b9d60) → write record → 594 mode4 |
| `SaveFastDressInfo` | zone @0x80939xx — pour les 24 clés : si uid working≠live → copie working→live + `UpdateFastDressData` → 594 mode0 ; page absente → 0x1a2e |
| `ApplyFastDress` | zone @0x8124f00 — gates : 0x3c&4→0x1ac0 ; 0x3a&1/ChangeClassID→0x1ac1 ; page absente→0x1a65 ; `FastDressID` enchant → reject ; sacs vides requis (0x4f9). Colonne armure = `class_data+0x28` (∈{1,2,3} → clés 601-605/611-615/621-625 filtrées). Bits via char+0x5bc. Record vide(loc=-1) + uid>0 → warn 0x1950 ; sinon `SwitchItemLocation(type,loc,idx → 1,eqLoc)` |
| `GetFastDressEquipLocTranslate` | zone @0x80ed0c0 — kind 1-5 + arch{2→610,3→600,1→620} ; sinon clé brute |
| `GetTypeFromSlotLoc` | zone @0x80ed210 — 601-605→2, 611-615→3, 621-625→4 |
| `UpdateFastDressFromInfo`/`ChangeFastDressInfo` | zone @0x808b7xx/0x8414xxx — hook move item : records avec uid → met à jour {con,loc,idx} + `UpdateFastDressInfo` ; kind9/0x23→clé7/10, kind7/0x13→{7,8}, kind0xa/0x22→11, kind0xb/0x14→{12,13}, kind0xc→{14,15}, kind1-5→kind+col, kind8→kind+col(EquipLoc) |
| DBA 846 / ZMis 847 | `CNE_DBA_ClientFastDress` (load→2 maps ou kick) ; mission texte `UpdateFastDressInfo`/`UpdateFastDressData` ; notre archi = repo zone direct (documenté) |

## Catégories d'items (CheckFastDressUseItem)

- kind<16 : `1<<kind & 0x9248`→cat2, `0x4924`→cat3, `0x2492`→cat4, sinon cat-1 (refus 0x59e)
- kind 16-39 → cat0 ; ≥40 → refus
- cat = `GetTypeFromSlotLoc`-like : {1,4,7,10,13}→4(+620), {2,5,8,11,14}→3(+610), {3,6,9,12,15}→2(+600), 16-39→0

## Implémentation

- [ ] `Eden.Core/Game/Inventory/FastDressSlots.cs` — table 24 clés, bitpos, traductions kind↔clé (archetype)
- [ ] `Eden.Core/Game/Inventory/FastDressRecord.cs` + `FastDressState` (Saved+Edited, 3 pages)
- [ ] `Eden.Core/Database/Repositories/FastDressRepository.cs` — load (seed 72 si vide) + upsert + delete
- [ ] `FastDressPackets.cs` — codec 593/594 (records 6×i32)
- [ ] `EntryState.FastDress` + chargement Lifecycle
- [ ] `ZoneEntryFlow.FastDress.cs` — handlers 243-248 + ApplyFastDress (gates + bitmask + colonne + SwitchItemAsync)
- [ ] Fix générés : 243 (vide), 593/594 (codecs dédiés, flatten erroné)
- [ ] Hook move item → `ChangeFastDressInfo` (positions records à jour + UPDATE)
- [ ] Tests : clés, codec bytes, état, handlers
