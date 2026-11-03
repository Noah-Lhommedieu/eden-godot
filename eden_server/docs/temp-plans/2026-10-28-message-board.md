# Lot : message board (panneau d'affichage) — CNE_CZ 138-142

Preuves natives (zone + mission désasm) :

- `CNE_CZ_ZoneServerMessageWrite::Execute` @0x8269960 : requiert perso
  auth, objet focus `char+0x670` type 0x2b à portée de dialogue, et
  `CheckItemNum(0, char, 0x7ceb, 1, 0)` — sinon 457 `[s16=3]`.
  Succès → `CNC_ZMis_MissionServerMessageWrite` (802)
  `[i32 charUid][str titre][str contenu]`.
- `MessageRead/ListQuery/Delete::Execute` : relais texte DBA
  `MessageRead id = %d` / `MessageListQuery page = %hd` /
  `MessageDelete id = %d` (AgentDBACommand 21).
- `MessageExit` : clear du focus board (+0x670) — no-op chez nous.
- Mission `CNE_ZMis_MissionServerMessageWrite::Execute` @0x813e440 :
  cap ~100 messages (`AllMessageData`), id via `MessageIDAllocator`,
  `CExecutionerDBO::SaveMessage` → `INSERT INTO message_board
  (message_id, title, content, player_name, create_date)` → réponse
  740 `[i32 char][s16 node][i32 msgId][s16 res]` (0=ok, 1=fail).
- `CNE_ZMis_ZoneServerMessageWriteResult::Execute` @0x8329xxx :
  res==0 && `DropItemByItemID(0x7ceb,1)` → renvoie DBA
  `MessageDelete id = msgId` (le post est créé puis PURGÉ —
  feature bridée nativement) + client 457 `[s16=2]` ;
  sinon 457 `[s16=res]`.
- `ZoneServerMessageList::Execute` @0x83295b0 : **ignore le payload**
  → 456 toujours vide `[u16 0][s16 0][s16 0][s16 0]`.
- `ZoneServerMessageRead::Execute` → 458 `[i32 id][str][u8]`=body[6..].
- `ZoneServerMessageDeleteResult::Execute` → 459 `[s16]`=body[6..8].
- Mission `ZoneServerMessageList` Serialize @0x8119d40 :
  `[i32][s16][u16 n][n×i32 ids][u16 n][n×str titres][u16 n][n×str
  noms][u16 n][n×i32 dates][s16][s16][s16]` (TC_MessageListQuery
  filtre create_date < 24 h).
- `TC_MessageRead` @0x8220250 : cherche l'id dans AllMessageData →
  répond 741 `[i32 char][s16][i32 id][str][u8]`.

## Éditions

- [ ] `Database/007-message-board.sql` — table `message_board`.
- [ ] `MissionDb` — BoardWrite/Read/Delete/List/Count.
- [ ] `MissionServer` — case 802 + verbes Message* dans OnDbaCommand.
- [ ] `ZoneMissionLink.SendMessageWriteAsync` — 802.
- [ ] `MiscHandlers` — 138-142 (139 : check item 0x7ceb → 802 ou 457[3]).
- [ ] `ZoneEntryFlow.Mission` — 739/740/741/742.
- [ ] Tests byte-level.
