# Lot plan-de-secours S3+S5 — dialogues/quêtes/transport/portails/montures

## Preuves (eden_server, lecture seule)
- Dialogue : clic droit NPC → C→S 10 TalkToNpc → S→C 285 `[i32 ctx][s16 n][n×(i32 displayId,str,s16 flag)][u8 type][i32 npcUid]` ; clic option → C→S 11 `[s16 index]` → mission OnDialogueSelected → nouveau 285 OU 58→388 EnterMissionList `[s16 op][i32 mid][i32 npcUid][s16]` (ops:1=open/offer,2=reward,3=on-going,4=finish,7=share).
- Quêtes : C→S 87 MissionListOp `[s16 op][i32 mid][u16 npcUid]` — op 1=accept, 2=reward(claim), 3=on-going, 4=finish (SocialHandlers.cs:425-466, QuestEngine.Events OnServiceOp) ; abandon = C→S 51 GiveUpMission `[s16 mid]` (TeamHandlers.cs:118).
- 391 kind=0 `[u8 0][i32 mid][s16 state][s16 progress]` ; kind=1 `[u8 1][u32 pmsKey][u16 count][s16 0]`. PMS key = mid×1e6 + type×1e4 + (objId−base) (QuestEngine.Objectives.cs:143).
- 345 UpdateNPCSymbols `[s16 n][n×(i32 npcUid, s16 sym1, s16 sym2)]` (ZoneEntryFlow.Mission.cs:215 passthru).
- 393 EnterTransport `[u16 n][n×s16 routeId][i32 npcUid]` ; C→S 88 `[s16 routeId|index]` ; coûts C_Transport.ini paliers level/10.
- CheckNodeExit 15 `[s16 keyId(col4 gateway_area)][s16 dstNode(0=auto)]` (ZoneEntryFlow.Migration.cs) — envoi à l'entrée dans l'event_area GATEWAY.
- Monture : PAS de NCID dédié — ItemUse 19 `[s16 contType][s16 bag][s16 slot][i32][u8str][3×s16]` toggle ride (ItemActionHandlers.cs:262) ; état via 283 mask=2 `[u32 rideKey][u32 flag]` + 545. rideKey → « r »+key:D6 → ridelist.ini → stem → ride/model/<STEM>.glb.

## Points d'édition (ownership)
- scripts/protocol/ZoneMissionMessages.cs (nouveau)
- scripts/hud/MissionBridge.cs (nouveau — binds + envois, ajouté par HudNetBridge)
- scripts/hud/{TransportWindow,MountHud}.cs (nouveaux)
- scripts/hud/{NpcDialogueWindow,QuestWindow,QuestTracker,HudNetBridge}.cs (modifs)
- tools/NetTests/ZoneMissionTests.cs (nouveau)
