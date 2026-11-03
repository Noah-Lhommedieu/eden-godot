# Lot — famille noyau (world + cache zone)

Sous-système famille/guilde natif. Source de vérité : `world` binary
(`CGlobalInfo::Family*`, `CFamily`, `AllFamilys`), `mission`
(`CFamilyDBO`, tables), `zone` (cache `CZoneServer::Ins+0x460`,
pushes ZW). Tables natives présentes dans fndb1 : `family`,
`family_cadre_members`, `family_emblem`, `family_msgboard`,
`family_schedule`, `family_log`, `family_storage_log`,
`family_achievement*`, `family_storage`, `family_building_container`.
Membres = `player_characters.family_id` (+ `family_comment`,
`family_contribution_point`, `family_msgboard_state`,
`add_family_time`) — pas de table member.

## Architecture native observée

- Le **monde est client-facing** : `CNE_CW_ServerFamilyOp` (NCID 9,
  fil `[u8 op][i32 p1][i32 p2][str s1][str s2]`,
  Deserialize @world 0x80da3f0) dispatch ~24 ops vers
  `CGlobalInfo::Family*` (Execute @0x80d9ea0).
- `CNE_CW_ServerQueryFamilyInfo` (10) fil `[u8 flag]` ; flag≠0 →
  `CNC_CW_ClientQueryFamilyInfo` (50) type 0 (header complet) puis
  type 8 par chunks de 30 membres (Execute @0x80e74a0, Serialize
  @0x80ce750).
- Mutations : monde → commande texte DBA → mission
  (`CExecutionerDBO`) → `CNE_DBA_Client*` → monde maj mémoire +
  push ZW 645/646/647 + CW clients. Chez nous : `FamilyRepository`
  direct (même schéma, même ordre d'effets observables).
- Chaque zone tient un cache `map<family_id, SFamily>` (Ins+0x460)
  rempli au boot (DBA 683-688) puis maintenu par ZW pushes ;
  `ClientChangeFamily` (346) émis au perso + can-see.
- Création : client `CNE_CZ_ZoneServerFamilyCreate` (49) →
  `CPlayerFactory::FamilyCreate` zone → ZW 143
  `CNE_ZW_WorldServerFamilyCreation [i32 charId][str name]` →
  monde → `CFamily::Create` (INSERT `family` id,name,leader,
  be_create_time,lv=1 — mission 0x81c0660 /
  `CFamilyDBO::AddNewFamily` INSERT @0x817d6b0).
- Emblème : zone `SetFamilyEmblem` (82) → texte ZW
  `set_family_emblem <charId> <packed u32>` →
  `CGlobalInfo::FamilySetEmblem`. Blob custom : FamilyOp 0x46 →
  `family_emblem` table.

## Fil — formats prouvés (désasm)

- 50 `ClientQueryFamilyInfo` : `[u8 type]` ; type∈{5:i32 exploit@fam+0x2c,
  6:i32 @+0x38 elfking?/treasury? , 7:s16 @+0x34 spell_point} ;
  type=2 = « pas de famille » (u8 seul). types<2 :
  `[u16 world_id][i32 id][i32 leader_id][str name][u8 lv]
  [i32 @0xe4][i32 exploit@0x14][i32 exp@0xc][i32 emblem@0x20]
  [s16 @0x24][str info@0x28][i32 @0x2c][s16 @0x30][s16 @0x32]
  [i32 @0x38][s16 @0x34][u8 @0x4c][s16 @0x3c][s16 @0xe8][i32 @0xec]`
  puis `[u16 0]` ; type∈{0,3} → `[u16 n][n×str]` titres (9) ;
  type∈{0,4} → `[u16 n][n×i32]` privilèges (14).
  type 8 : `[u8 8][u16 n][n×SCharacter][i32 0][i32 0]`.
  SCharacter rec : i32 char_id, i32 @0x60, str name, s16×4
  (@0xc..0x14), u8 @0x18, u16 @0x1a, u8 @0x19, u16 @0x1c,
  i32 @0x20, i32 @0x68, str @0x6c, str @0x70, i32 @0x78.
- 46 `ClientUpdateFamilyMember` : `[i32 id][str name][s16×5][u8]
  [u16][u8][u16][i32][i32][str][str][i32][u32]` (Serialize
  @0x8062e50).
- 48 `ClientFamilyInvitation` : `[i32 inviter][str inviter][str fam]`.
- 47 `ClientFamilyOpException` : `[u8 code]`.
- 49 `ClientFamilyQuit` : vide.
- 74 `ClientFamilyEmblem` : `[i32 family_id][i32 ?][s16 n][n×u8]`.
- 80 `ClientQueryFamilyRecruitList` : `[u16 n][n×{i32 id,str name,
  str leader,s16 lv,s16 cnt,s16 max}][s16][s16]`.
- 51 `ClientFamilySchedule` : u8 type ; 1=`{s16 id,str,str,str,i32}`,
  2=`{s16 id}`, 0/3/4=`{u16 n,n×{s16,i32,str,str,str},i32}`.
- ZW 645 `ChangeFamily` : `[i32 node][s16 node_idx][i32 family_id]
  [u8 flag][i32 rank][s16 emblem][s16]` → zone maj cache + 346.
- ZW 646 `NewFamily` : `[i32 id][str leader_name][s16][i32 leader_id]
  [str name]` → insert cache zone.
- ZW 647 `RemoveFamily` : `[i32 family_id]` → delete cache zone.
- ZW 143 `FamilyCreation` (Z→W) : `[i32 charId][str name]`.
- 634 `WorldServerFamilyCreation` (W→Z) : `[i32 charId][str name]`.

## Mapping `family` (LoadFamily mission @0x8181b0)

`id,name,leader_id,exploit,lv,exp,emblem,uniform,flag,info,treasury,
tax_rate,business_tax, member_title,leader_title,cadre_title_2..8 (9
str), channel_priv,invite,kickout,editinfo,setcadre,invite25r,fixequip,
msgboard,delmsg,treasury,repair,storage01..03 (14 i32), be_create_time,
spell_point,elfking_tid,house_open,family_flag,recruit,honor`.

## Étapes

- [x] `FamilyModels.cs` (Core) : Family, FamilyMemberSummary,
      MsgBoard/Schedule/Log records, FamilyRank helpers.
- [x] `FamilyRepository.cs` (Core) : load familles + cadres + emblème
      + msgboard + schedule + log ; mutations `family`,
      `family_cadre_members`, `player_characters.family_*`,
      `family_msgboard`, `family_log`, `family_emblem`.
- [x] `FamilyPackets.cs` (Core) : builders 46/47/48/49/50/51/74/80 +
      ZW 645/646/647.
- [x] `WorldFamilyService.cs` : AllFamilys, invites, ops 1-8, 10-22,
      70, 78-80 + validations privilèges + exceptions 47.
- [x] Handlers monde : NCID 9/10/73 (remplace le stub prestige —
      préserver update_prestige).
- [x] `ZoneFamilyRegistry` + lien ZW : 645/646/647 → cache + 346 +
      refresh `Vis`/name. Chargement initial des familles au boot
      zone (équiv. DBA 683-688).
- [x] Zone 49 FamilyCreate → ZW 143 ; 82 SetFamilyEmblem → texte ZW.
- [x] Pont `player_characters.family_id` : chargement + maj quand
      membre ajouté/retiré ; nom de famille affiché (346 + champ
      nom dans les paquets perso).
- [x] Tests : codecs byte-level, privilèges, invite/accept/quit/kick,
      disband, recruit list. Build 0 warn.
- [x] Commit.

## Hors lot (suivants)

- family_storage items (177/178 + mission storage push), dons (159),
  statue (161), ville (174/175/176 + town list), instance (132/173),
  bâtiments/réparation (180), battle (225 + 561/562), achievements
  (67), emblem-data upload complet si blob volumineux, territory
  (189/190 + 515/516/532/547/566/586), colosseum (196-201/219 +
  524-530/552/587).
