# Plan — sous-système salles personnelles (PlayerRoom / housing)

## Objectif

Porter le housing natif `CPlayerRoomManager` + `player_room*` DB +
relais mission, fidèle aux Execute zone/mission désassemblés.

## Preuves natives

### Client → Zone (CNE_CZ, tous gated par `CGlobalInfo::PlayerRoomSwitch`)

| NCID | Nom | Execute | Action native |
|---|---|---|---|
| 205 | SearchRoom | @0x82844d0 | `CDBAgent::Command "Search Player Room search_name= %s hotel_type= %hd"` |
| 206 | RentRoom | @0x827e610 | `"Rent Player Room hotel_type= %hd"` (s16 +0x10) |
| 207 | EnterRoom | — | « Enter Player Room room_id= %d » / « Visit Player Room room_id= %d » |
| 208 | SetRoomPrivilege | @0x82897d0 | `"Set Room Privilege lover_pri= %d friend_pri= %d family_pri= %d other_pri= %d"` |
| 209 | SwitchSetRoomMode | @0x828d790 | `"Switch Room Setting Mode room_id= %d mode= %hd"` |
| 210 | RoomGetData | @0x8283710 | zone-side : sert CPlayerRoom + container |
| 211 | UpdateDecoration | @0x829ab00 | `"Update Room Decoration index= %hd loc= %hd is_decorate= %hu pos_x= %hu pos_y= %hu pos_z= %hu dir_x= %hu dir_y= %hu dir_z= %hu rotate= %hu"` |
| 212 | PayRent | @0x826e900 | `"PlayerRoom Pay Rent"` |
| 215 | ChangeRoomTexture | @0x8240c40 | `"Change Room Texture room_id= %d index= %hd loc= %hd"` |
| 216 | StartParty | @0x828bb60 | zone-side CPlayerRoomManager::StartParty |
| 217 | ExtendRoom | @0x8252fe0 | zone-side CPlayerRoomManager::RoomExtend |
| 195 | EnterNodePlayerRoom | — | entrée dans le node salle |

### Retours

| NCID | Sens | Nom |
|---|---|---|
| 523 | Z→C | ClientEnterNodePlayerRoom |
| 537 | Z→C | ClientSearchRoomResult |
| 538 | Z→C | ClientUpdateRoom |
| 540 | Z→C | ClientSetRoomResult |
| 541 | Z→C | ClientRoomData |
| 542 | Z→C | ClientUpdateRoomItem |
| 778 | M→Z | ZoneServerSearchRoomResult |
| 779 | M→Z | ZoneServerUpdateRoom |
| 780 | M→Z | ZoneServerSwitchPlayerRoom |
| 781 | M→Z | ZoneServerUpdateRoomItem |

### Schéma DB natif (déjà dans fndb1)

`player_room(player_id PK, player_name, hotel_type, friend/family/
lover/other_privilege, populity, extend, due_date, wallpaper,
living_floor, bed_floor, party_time)` + `player_room_container`
(meubles) + `room_storage`.

Mission TC : TC_RentPlayerRoom @0x81fe220, TC_EnterPlayerRoom
@0x81eeab0, TC_ChangeRoomTexture @0x81f6020, TC_SetRoomPrivilege
@0x81fa570, TC_RoomExtend @0x81fe010, TC_SearchPlayerRoom @0x821a9c0
(+ PayRent / UpdateRoomDecoration / SwitchRoomSetting / UpdateRoomData
"UPDATE player_room SET ..." textuels).

## Étapes

- [ ] 1. Relais zone → mission (7 commandes texte fidèles).
- [ ] 2. MissionDb : CRUD player_room + player_room_container.
- [ ] 3. MissionServer : TC_* fidèles + réponses 778-781.
- [ ] 4. Zone : registre salles (Rooms service) + RoomGetData → 541,
       SearchRoom → 537, UpdateRoom 779→538, privilege → 540.
- [ ] 5. EnterRoom/VisitRoom → migration vers node salle.
- [ ] 6. StartParty/ExtendRoom zone-side + due_date/party_time.
- [ ] 7. Tests byte-level + build/test + commit.
