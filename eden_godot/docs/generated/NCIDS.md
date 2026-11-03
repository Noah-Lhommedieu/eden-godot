# NCIDs Eden — table complète

> Régénéré par `tools/gen_ncids.py` depuis `eden_server/docs/generated/ncid_map.json`, les `*.g.cs` (opcode wire), `FEATURE-MATRIX.md` et `godot_project/scripts/protocol/`. Ne pas éditer à la main.

**NCID** = valeur wire quand la classe existe dans les `.g.cs` du serveur (vérifiée par captures) ; sinon l'index de la table de commandes du binaire (marqué `†`). Les deux diffèrent pour les protocoles d'entrée — la table du binaire enregistre les commandes client après les autres espaces de noms.

Statuts : **implémenté** = codec client (`scripts/protocol/`) ; **décodé** = layout prouvé côté serveur sans codec client ; **inconnu** = aucune preuve.

Implémentés côté client : **6** login, **22** world, **96** zone — 130 classes NetMessage recensées.

## LoginServer (35 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 0 †`35` | `CNC_CL_ServerHello` | C→S client→serveur | inconnu | — |
| 0 †`9` | `CNC_T_ClientGetTicket` | S→C serveur→client | décodé (incomplet) | INCOMPLET |
| 0 †`10` | `CNC_T_ClientUseTicket` | S→C serveur→client | inconnu | — |
| 1 †`36` | `CNC_CL_ServerLoginAccount` | C→S client→serveur | implémenté `LoginAccount` | `LoginMessages.cs` |
| 2 †`37` | `CNC_CL_ServerLoginWorld` | C→S client→serveur | implémenté `LoginWorldSelect` | `LoginMessages.cs` |
| 3 †`38` | `CNC_CL_ClientHello` | S→C serveur→client | implémenté `LoginClientHello` | `LoginMessages.cs` |
| 4 †`39` | `CNC_CL_ClientLoginFail` | S→C serveur→client | implémenté `LoginFail` | `LoginMessages.cs` |
| 5 †`40` | `CNC_CL_ClientGetWorldServers` | S→C serveur→client | implémenté `WorldList` | `LoginMessages.cs` |
| 6 †`41` | `CNC_CL_ClientReceiveTicketToWorldServer` | S→C serveur→client | implémenté `WorldTicket` | `LoginMessages.cs` |
| 6 †`17` | `CNC_G_ClientAccountLogin` | S→C serveur→client | inconnu | — |
| 7 †`18` | `CNC_G_ClientAccountLogout` | S→C serveur→client | inconnu | — |
| 8 †`19` | `CNC_G_ClientKickUser` | S→C serveur→client | inconnu | — |
| 9 †`20` | `CNC_G_ClientServerTypeReport` | S→C serveur→client | inconnu | — |
| 11 †`22` | `CNC_G_ClientBillingLogin` | S→C serveur→client | inconnu | — |
| 12 †`23` | `CNC_G_ClientGetPointInfo` | S→C serveur→client | inconnu | — |
| 13 †`24` | `CNC_G_ClientReqBuyItem` | S→C serveur→client | inconnu | — |
| 15 †`26` | `CNC_G_ClientTextCommand` | S→C serveur→client | inconnu | — |
| 19 †`30` | `CNC_G_ClientCrossWorldAccountLogout` | S→C serveur→client | inconnu | — |
| 22 †`34` | `CNC_G_ClientExchangePinItemResult` | S→C serveur→client | inconnu | — |
| 110 †`11` | `CNC_G_ServerAccountLogin` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 111 †`12` | `CNC_G_ServerAccountLogout` | C→S client→serveur | inconnu | — |
| 112 †`13` | `CNC_G_ServerBillingLogin` | C→S client→serveur | inconnu | — |
| 113 †`14` | `CNC_G_ServerClientTypeReport` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 114 †`15` | `CNC_G_ServerGetPointInfo` | C→S client→serveur | inconnu | — |
| 115 †`16` | `CNC_G_ServerReqBuyItem` | C→S client→serveur | inconnu | — |
| 120 †`21` | `CNC_G_ClientSetWorldStates` | S→C serveur→client | inconnu | — |
| 124 †`25` | `CNC_G_ServerTextCommand` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 126 †`27` | `CNC_G_ServerCharacterLogin` | C→S client→serveur | inconnu | — |
| 127 †`28` | `CNC_G_ServerCharacterLogout` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 128 †`29` | `CNC_G_ServerCrossWorldAccountLogoutAck` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 130 †`31` | `CNC_G_ServerCheckCommunicateLock` | C→S client→serveur | inconnu | — |
| 131 †`32` | `CNC_G_ServerExchangePinItem` | C→S client→serveur | inconnu | — |
| 132 †`33` | `CNC_G_ServerExchangePinUpdateState` | C→S client→serveur | inconnu | — |
| 621 †`7` | `CNC_T_ServerGetTicket` | C→S client→serveur | inconnu | — |
| 622 †`8` | `CNC_T_ServerUseTicket` | C→S client→serveur | inconnu | — |

## GatewayServer (23 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 6 | `CNC_G_ClientAccountLogin` | S→C serveur→client | inconnu | — |
| 7 | `CNC_G_ClientAccountLogout` | S→C serveur→client | inconnu | — |
| 8 | `CNC_G_ClientKickUser` | S→C serveur→client | inconnu | — |
| 9 | `CNC_G_ClientServerTypeReport` | S→C serveur→client | inconnu | — |
| 11 | `CNC_G_ClientBillingLogin` | S→C serveur→client | inconnu | — |
| 12 | `CNC_G_ClientGetPointInfo` | S→C serveur→client | inconnu | — |
| 13 | `CNC_G_ClientReqBuyItem` | S→C serveur→client | inconnu | — |
| 15 | `CNC_G_ClientTextCommand` | S→C serveur→client | inconnu | — |
| 19 | `CNC_G_ClientCrossWorldAccountLogout` | S→C serveur→client | inconnu | — |
| 22 | `CNC_G_ClientExchangePinItemResult` | S→C serveur→client | inconnu | — |
| 110 †`0` | `CNC_G_ServerAccountLogin` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 111 †`1` | `CNC_G_ServerAccountLogout` | C→S client→serveur | inconnu | — |
| 112 †`2` | `CNC_G_ServerBillingLogin` | C→S client→serveur | inconnu | — |
| 113 †`3` | `CNC_G_ServerClientTypeReport` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 114 †`4` | `CNC_G_ServerGetPointInfo` | C→S client→serveur | inconnu | — |
| 115 †`5` | `CNC_G_ServerReqBuyItem` | C→S client→serveur | inconnu | — |
| 120 †`10` | `CNC_G_ClientSetWorldStates` | S→C serveur→client | inconnu | — |
| 124 †`14` | `CNC_G_ServerTextCommand` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 126 †`16` | `CNC_G_ServerCharacterLogin` | C→S client→serveur | inconnu | — |
| 127 †`17` | `CNC_G_ServerCharacterLogout` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 128 †`18` | `CNC_G_ServerCrossWorldAccountLogoutAck` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 131 †`20` | `CNC_G_ServerExchangePinItem` | C→S client→serveur | inconnu | — |
| 132 †`21` | `CNC_G_ServerExchangePinUpdateState` | C→S client→serveur | inconnu | — |

## TicketServer (6 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 0 †`5` | `CNC_T_ClientCheckPin` | S→C serveur→client | inconnu | — |
| 0 †`2` | `CNC_T_ClientGetTicket` | S→C serveur→client | décodé (incomplet) | INCOMPLET |
| 0 †`3` | `CNC_T_ClientUseTicket` | S→C serveur→client | inconnu | — |
| 108 †`4` | `CNC_T_ServerCheckPin` | C→S client→serveur | inconnu | — |
| 621 †`0` | `CNC_T_ServerGetTicket` | C→S client→serveur | inconnu | — |
| 622 †`1` | `CNC_T_ServerUseTicket` | C→S client→serveur | inconnu | — |

## WorldServer (204 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 0 †`84` | `CNE_CW_ServerHello` | C→S client→world | inconnu | — |
| 1 †`85` | `CNE_CW_ServerReceiveTicket` | C→S client→world | implémenté `WorldLoginTicket` | `WorldMessages.cs` |
| 2 †`86` | `CNE_CW_ServerKeepAlive` | C→S client→world | décodé (desasm) | DESASM |
| 3 †`87` | `CNE_CW_ServerLoginCharacter` | C→S client→world | implémenté `WorldSelectCharacter` | `WorldMessages.cs` |
| 4 †`88` | `CNE_CW_ServerCreateCharacterName` | C→S client→world | implémenté `WorldCreateCharacterName` | `WorldMessages.cs` |
| 5 †`89` | `CNE_CW_ServerCreateCharacterData` | C→S client→world | implémenté `WorldCreateCharacterData` | `WorldMessages.cs` |
| 6 †`90` | `CNE_CW_ServerRemoveCharacter` | C→S client→world | implémenté `WorldRemoveCharacter` | `WorldMessages.cs` |
| 7 †`91` | `CNE_CW_ServerCancelCreateCharacterReq` | C→S client→world | implémenté `WorldCancelCreateCharacter` | `WorldMessages.cs` |
| 8 | `CNE_CW_ServerLookingForMember` | C→S client→world | décodé (passthru) | PASSTHRU |
| 9 | `CNE_CW_ServerFamilyOp` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 10 | `CNE_CW_ServerQueryFamilyInfo` | C→S client→world | implémenté `WorldQueryFamilyInfo` | `WorldMessages.cs` |
| 11 | `CNE_CW_ServerLookingForGroup` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 12 | `CNE_CW_ServerAddFriend` | C→S client→world | décodé (passthru) | PASSTHRU |
| 13 | `CNE_CW_ServerQueryFriendInfo` | C→S client→world | implémenté `WorldQueryFriendInfo` | `WorldMessages.cs` |
| 14 | `CNE_CW_ServerFriendInvitation` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 15 | `CNE_CW_ServerItemMallAnythingReceivable` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 16 | `CNC_CW_ServerItemMallReceivableList` | S→C world→client | décodé (passthru) | PASSTHRU |
| 17 | `CNE_CW_ServerItemMallReceivableQuery` | C→S client→world | implémenté `WorldItemMallReceivableQuery` | `WorldMessages.cs` |
| 18 | `CNE_CW_ServerItemMallReceieveAttempt` | C→S client→world | décodé (passthru) | PASSTHRU |
| 19 | `CNE_CW_ServerQueryExploitRank` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 20 | `CNE_CW_ServerChangeWorld` | C→S client→world | décodé (passthru) | PASSTHRU |
| 21 | `CNE_CW_ServerChangeWorldAck` | C→S client→world | décodé (passthru) | PASSTHRU |
| 22 | `CNE_CW_ServerRemoveFriend` | C→S client→world | inconnu | — |
| 23 | `CNE_CW_ServerItemMallReqBuyItem` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 24 | `CNE_CW_ServerItemMallListQuery` | C→S client→world | décodé (incomplet) | INCOMPLET |
| 25 | `CNE_CW_ServerItemMallPointInfo` | C→S client→world | implémenté `WorldItemMallPointInfoQuery` | `WorldMessages.cs` |
| 26 | `CNE_CW_ServerCaptchaReturn` | C→S client→world | implémenté `WorldSelectZone` | `WorldMessages.cs` |
| 27 †`92` | `CNE_CW_ServerPlayerState` | C→S client→world | inconnu | — |
| 28 | `CNE_CW_ServerIllPluginInfo` | C→S client→world | inconnu | — |
| 29 | `CNE_CW_ServerUpdateWorldServers` | C→S client→world | décodé (passthru) | PASSTHRU |
| 30 | `CNE_CW_ServerCheckHasCharPassword` | C→S client→world | décodé (passthru) | PASSTHRU |
| 31 | `CNE_CW_ServerCharPassword` | C→S client→world | décodé (passthru) | PASSTHRU |
| 32 †`103` | `CNE_CW_ServerCreateCharacterNameCheck` | C→S client→world | implémenté `WorldCreateNameCheck` | `WorldMessages.cs` |
| 33 | `CNE_CW_ServerMutuallyHelpSearch` | C→S client→world | décodé (passthru) | PASSTHRU |
| 34 †`93` | `CNC_CW_ClientHello` | S→C world→client | implémenté `WorldClientHello` | `WorldMessages.cs` |
| 35 †`94` | `CNC_CW_ClientTicketInvalid` | S→C world→client | décodé (desasm) | DESASM |
| 36 †`95` | `CNC_CW_ClientTextOutput` | S→C world→client | inconnu | — |
| 37 †`96` | `CNC_CW_ClientReceiveTicketToZoneServer` | S→C world→client | implémenté `ZoneTicket` | `WorldMessages.cs` |
| 38 †`97` | `CNC_CW_ClientGetCharacters` | S→C world→client | implémenté `CharacterListChunk` | `WorldMessages.cs` |
| 39 †`98` | `CNC_CW_ClientVerifyCharacterName` | S→C world→client | implémenté `WorldVerifyNameResult` | `WorldMessages.cs` |
| 40 | `CNC_CW_ClientResetTime` | S→C world→client | implémenté `WorldResetTime` | `WorldMessages.cs` |
| 41 | `CNC_CW_ClientCancelCreateCharacterResp` | S→C world→client | implémenté `WorldCancelCreateCharacterResp` | `WorldMessages.cs` |
| 42 | `CNC_CW_ClientTell` | S→C world→client | inconnu | — |
| 43 | `CNC_CW_ClientTextOutputIndex` | S→C world→client | décodé (passthru) | PASSTHRU |
| 44 †`100` | `CNC_CW_ClientChannelMsg` | S→C world→client | inconnu | — |
| 45 | `CNC_CW_ClientLookingForMemberResult` | S→C world→client | décodé (passthru) | PASSTHRU |
| 46 | `CNC_CW_ClientUpdateFamilyMember` | S→C world→client | décodé (passthru) | PASSTHRU |
| 47 | `CNC_CW_ClientFamilyOpException` | S→C world→client | décodé (passthru) | PASSTHRU |
| 48 | `CNC_CW_ClientFamilyInvitation` | S→C world→client | décodé (passthru) | PASSTHRU |
| 49 | `CNC_CW_ClientFamilyQuit` | S→C world→client | décodé (passthru) | PASSTHRU |
| 50 | `CNC_CW_ClientQueryFamilyInfo` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 51 | `CNC_CW_ClientFamilySchedule` | S→C world→client | décodé (passthru) | PASSTHRU |
| 52 | `CNC_CW_ClientUpdateFriendMember` | S→C world→client | décodé (passthru) | PASSTHRU |
| 53 | `CNC_CW_ClientFriendInvitation` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 54 | `CNC_CW_ClientRepeatedLogin` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 55 †`101` | `CNC_CW_ClientException` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 56 | `CNC_CW_ClientItemMallAnythingReceivable` | S→C world→client | décodé (desasm) | DESASM |
| 57 | `CNC_CW_ClientItemMallReceivableQuery` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 58 | `CNC_CW_ClientItemMallReceieveAttempt` | S→C world→client | décodé (desasm) | DESASM |
| 59 | `CNC_CW_ClientQueryExploitRank` | S→C world→client | décodé (passthru) | PASSTHRU |
| 60 | `CNC_CW_ClientUpdatePrestigeData` | S→C world→client | implémenté `WorldUpdatePrestigeData` | `WorldMessages.cs` |
| 61 | `CNC_CW_ClientUpdateBlackList` | S→C world→client | décodé (passthru) | PASSTHRU |
| 62 | `CNC_CW_ClientItemMallList` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 63 | `CNC_CW_ClientItemMallReqBuyItem` | S→C world→client | décodé (incomplet) | INCOMPLET |
| 64 | `CNC_CW_ClientItemMallPointInfo` | S→C world→client | implémenté `WorldItemMallPointInfo` | `WorldMessages.cs` |
| 65 | `CNC_CW_ClientReceiveTicketToWorld` | S→C world→client | décodé (passthru) | PASSTHRU |
| 66 †`102` | `CNC_CW_ClientGetWorldServers` | S→C world→client | décodé (passthru) | PASSTHRU |
| 67 | `CNC_CW_ClientFamilyAchievementUpdate` | S→C world→client | décodé (passthru) | PASSTHRU |
| 68 | `CNC_CW_ClientFamilyLogUpdate` | S→C world→client | décodé (passthru) | PASSTHRU |
| 69 †`99` | `CNC_CW_ClientCaptcha` | S→C world→client | implémenté `CharacterDataChunk` | `WorldMessages.cs` |
| 70 | `CNC_CW_ClientCrossZone` | S→C world→client | décodé (passthru) | PASSTHRU |
| 71 | `CNE_CW_ServerApexBufferTransfer` | C→S client→world | décodé (passthru) | PASSTHRU |
| 72 | `CNC_CW_ClientApexBufferTransfer` | S→C world→client | implémenté `WorldApexBuffer` | `WorldMessages.cs` |
| 73 | `CNE_CW_ServerQueryFamilyEmblem` | C→S client→world | inconnu | — |
| 74 | `CNC_CW_ClientFamilyEmblem` | S→C world→client | décodé (passthru) | PASSTHRU |
| 75 | `CNC_CW_ClientCheckHasCharPassword` | S→C world→client | décodé (desasm) | DESASM |
| 76 | `CNC_CW_ClientCharPassword` | S→C world→client | décodé (desasm) | DESASM |
| 77 | `CNC_CW_ClientSelectCharEnableBtn` | S→C world→client | décodé (passthru) | PASSTHRU |
| 78 | `CNC_CW_ClientUpdateSpecialCoin` | S→C world→client | décodé (passthru) | PASSTHRU |
| 79 | `CNC_CW_ClientItemMallLimit` | S→C world→client | décodé (passthru) | PASSTHRU |
| 80 | `CNC_CW_ClientQueryFamilyRecruitList` | S→C world→client | décodé (passthru) | PASSTHRU |
| 81 | `CNC_CW_ClientMutuallyHelpUpdate` | S→C world→client | décodé (passthru) | PASSTHRU |
| 82 | `CNC_CW_ClientNotifyChangeWorld` | S→C world→client | inconnu | — |
| 83 | `CNC_CW_ClientUpdateFortuneBagContentList` | S→C world→client | inconnu | — |
| 108 | `CNC_T_ServerCheckPin` | C→S client→serveur | inconnu | — |
| 109 | `CNE_T_ClientCheckPin` | S→C serveur→client | inconnu | — |
| 110 | `CNC_G_ServerAccountLogin` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 111 | `CNC_G_ServerAccountLogout` | C→S client→serveur | inconnu | — |
| 112 | `CNC_G_ServerBillingLogin` | C→S client→serveur | inconnu | — |
| 113 | `CNC_G_ServerClientTypeReport` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 114 | `CNC_G_ServerGetPointInfo` | C→S client→serveur | inconnu | — |
| 115 | `CNC_G_ServerReqBuyItem` | C→S client→serveur | inconnu | — |
| 116 | `CNE_G_ClientAccountLogin` | S→C serveur→client | inconnu | — |
| 117 | `CNE_G_ClientAccountLogout` | S→C serveur→client | inconnu | — |
| 118 | `CNE_G_ClientKickUser` | S→C serveur→client | inconnu | — |
| 119 | `CNE_G_ClientServerTypeReport` | S→C serveur→client | inconnu | — |
| 120 | `CNC_G_ClientSetWorldStates` | S→C serveur→client | inconnu | — |
| 121 | `CNE_G_ClientBillingLogin` | S→C serveur→client | inconnu | — |
| 122 | `CNE_G_ClientGetPointInfo` | S→C serveur→client | inconnu | — |
| 123 | `CNE_G_ClientReqBuyItem` | S→C serveur→client | décodé (incomplet) | INCOMPLET |
| 124 | `CNC_G_ServerTextCommand` | C→S client→serveur | décodé (incomplet) | INCOMPLET |
| 125 | `CNE_G_ClientTextCommand` | S→C serveur→client | inconnu | — |
| 126 | `CNC_G_ServerCharacterLogin` | C→S client→serveur | inconnu | — |
| 127 | `CNC_G_ServerCharacterLogout` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 128 | `CNC_G_ServerCrossWorldAccountLogoutAck` | C→S client→serveur | décodé (passthru) | PASSTHRU |
| 129 | `CNE_G_ClientCrossWorldAccountLogout` | S→C serveur→client | inconnu | — |
| 130 | `CNC_G_ServerCheckCommunicateLock` | C→S client→serveur | inconnu | — |
| 131 | `CNC_G_ServerExchangePinItem` | C→S client→serveur | inconnu | — |
| 132 | `CNC_G_ServerExchangePinUpdateState` | C→S client→serveur | inconnu | — |
| 133 | `CNE_G_ClientExchangePinItemResult` | S→C serveur→client | décodé (incomplet) | INCOMPLET |
| 134 | `CNE_ZW_WorldServerIDReport` | interne zone→world | inconnu | — |
| 135 | `CNE_ZW_WorldServerTell` | interne zone→world | inconnu | — |
| 136 | `CNE_ZW_WorldServerAllUsers` | interne zone→world | inconnu | — |
| 137 | `CNE_ZW_WorldServerNotifyEntry` | interne zone→world | inconnu | — |
| 138 | `CNE_ZW_WorldServerCharacterArrived` | interne zone→world | inconnu | — |
| 139 | `CNE_ZW_WorldServerResetTime` | interne zone→world | inconnu | — |
| 140 | `CNE_ZW_WorldServerReturnToCharacterList` | interne zone→world | inconnu | — |
| 141 | `CNE_ZW_WorldServerTeamLeavedNotify` | interne zone→world | décodé (passthru) | PASSTHRU |
| 142 | `CNE_ZW_WorldServerChannelMsg` | interne zone→world | décodé (passthru) | PASSTHRU |
| 143 | `CNE_ZW_WorldServerFamilyCreation` | interne zone→world | décodé (passthru) | PASSTHRU |
| 144 | `CNE_ZW_WorldServerLogout` | interne zone→world | décodé (passthru) | PASSTHRU |
| 145 | `CNE_ZW_WorldServerTextCommand` | interne zone→world | inconnu | — |
| 146 | `CNE_ZW_WorldServerItemMallReceieveAttempt` | interne zone→world | inconnu | — |
| 147 | `CNE_ZW_WorldServerGetCloneItemID` | interne zone→world | inconnu | — |
| 148 | `CNE_ZW_WorldServerTextExCmd` | interne zone→world | inconnu | — |
| 149 | `CNE_ZW_WorldServerFBMsgUpdate` | interne zone→world | inconnu | — |
| 150 | `CNC_ZW_ZoneServerReportID` | interne world→zone | inconnu | — |
| 151 | `CNC_ZW_ZoneServerDestShutdown` | interne world→zone | inconnu | — |
| 152 | `CNC_ZW_ZoneServerCharacterArrivedDest` | interne world→zone | décodé (incomplet) | INCOMPLET |
| 153 | `CNC_ZW_ZoneServerChannelMsg` | interne world→zone | inconnu | — |
| 154 | `CNC_ZW_ZoneServerChangeFamily` | interne world→zone | inconnu | — |
| 155 | `CNC_ZW_ZoneServerNewFamily` | interne world→zone | inconnu | — |
| 156 | `CNC_ZW_ZoneServerRemoveFamily` | interne world→zone | décodé (incomplet) | INCOMPLET |
| 157 | `CNC_ZW_ZoneServerTextCommand` | interne world→zone | inconnu | — |
| 158 | `CNC_ZW_ZoneServerItemMallReceieveAttempt` | interne world→zone | inconnu | — |
| 159 | `CNC_ZW_ZoneServerGetCloneItemID` | interne world→zone | inconnu | — |
| 160 | `CNC_ZW_ZoneServerUpdateFortuneBag` | interne world→zone | décodé (incomplet) | INCOMPLET |
| 161 | `CNC_ZW_ZoneServerUpdateLottery` | interne world→zone | inconnu | — |
| 162 | `CNC_ZW_ZoneServerTextExCmd` | interne world→zone | décodé (incomplet) | INCOMPLET |
| 163 | `CNC_ZW_ZoneServerUpdateStarWay` | interne world→zone | inconnu | — |
| 164 | `CNE_MW_WorldServerItemMallMailSendResult` | interne mission→world | décodé (incomplet) | INCOMPLET |
| 165 | `CNE_MW_WorldServerItemMallMailCheckOK` | interne mission→world | décodé (incomplet) | INCOMPLET |
| 166 | `CNE_MW_WorldServerUpdateBlackList` | interne mission→world | décodé (incomplet) | INCOMPLET |
| 167 | `CNE_MW_WorldServerUpdatePrestigeData` | interne mission→world | inconnu | — |
| 168 | `CNE_MW_WorldServerUpdateFriendMember` | interne mission→world | décodé (incomplet) | INCOMPLET |
| 169 | `CNE_MW_WorldServerCrossZone` | interne mission→world | inconnu | — |
| 170 | `CNE_MW_WorldServerFBMsgUpdate` | interne mission→world | inconnu | — |
| 171 | `CNE_MW_WorldServerFriendNotify` | interne mission→world | inconnu | — |
| 172 | `CNE_MW_WorldServerUpdateSpecialCoin` | interne mission→world | décodé (incomplet) | INCOMPLET |
| 173 | `CNE_MW_WorldServerCheckFamilyInvite` | interne mission→world | inconnu | — |
| 175 | `CNE_MW_WorldServerTell` | interne mission→world | inconnu | — |
| 176 | `CNE_MW_WorldServerTextExCmd` | interne mission→world | inconnu | — |
| 177 | `CNE_MW_WorldServerTextExCmdRemoteWorld` | interne mission→world | inconnu | — |
| 178 | `CNE_MW_WorldServerTextExCmdRemoteZone` | interne mission→world | inconnu | — |
| 182 † | `CNC_ZM_ZoneServerUpdatePKList` | interne mission→zone | inconnu | — |
| 186 † | `CNC_DBA_ClientContainer` | interne dba→* | inconnu | — |
| 187 † | `CNC_DBA_ClientEnchant` | interne dba→* | inconnu | — |
| 188 † | `CNC_DBA_ClientNodeID` | interne dba→* | inconnu | — |
| 189 † | `CNC_DBA_ClientPlayerData` | interne dba→* | inconnu | — |
| 190 † | `CNC_DBA_ClientMissionState` | interne dba→* | inconnu | — |
| 191 | `CNE_DBA_ClientDelPlayerOK` | interne *→dba | inconnu | — |
| 199 † | `CNC_DBA_ClientShortcut` | interne dba→* | inconnu | — |
| 200 † | `CNC_DBA_ClientSpell` | interne dba→* | inconnu | — |
| 201 † | `CNC_DBA_ClientFinishPlayerData` | interne dba→* | inconnu | — |
| 202 † | `CNC_DBA_ClientAllLover` | interne dba→* | inconnu | — |
| 203 † | `CNC_DBA_ClientCollection` | interne dba→* | inconnu | — |
| 204 | `CNE_DBA_ClientFamilyAllMessage` | interne *→dba | décodé (incomplet) | INCOMPLET |
| 205 † | `CNC_DBA_ClientFamilyStorage` | interne dba→* | inconnu | — |
| 206 † | `CNC_DBA_ClientAllTerritory` | interne dba→* | inconnu | — |
| 207 † | `CNC_DBA_ClientGladiatorTeamInfo` | interne dba→* | inconnu | — |
| 208 † | `CNC_DBA_ClientAllPlayerRoom` | interne dba→* | inconnu | — |
| 209 † | `CNC_DBA_ClientFortuneBagCounter` | interne dba→* | inconnu | — |
| 210 † | `CNC_DBA_ClientFamilyContainerData` | interne dba→* | inconnu | — |
| 211 † | `CNC_DBA_ClientAllFamilyInstnce` | interne dba→* | inconnu | — |
| 218 | `CNE_MW_WorldServerCrossWorldEndClearAccount` | interne mission→world | décodé (golden) | GOLDEN |
| 219 † | `CNC_DBA_ClientNewPlayerGuideData` | interne dba→* | inconnu | — |
| 220 † | `CNC_DBA_ClientRecommendActivityState` | interne dba→* | inconnu | — |
| 222 † | `CNC_ZMis_ZoneServerUpdateRecommendActivities` | interne mission→zone | inconnu | — |
| 223 † | `CNC_DBA_ClientElfData` | interne dba→* | inconnu | — |
| 621 †`104` | `CNC_T_ServerGetTicket` | C→S client→serveur | inconnu | — |
| 622 †`105` | `CNC_T_ServerUseTicket` | C→S client→serveur | inconnu | — |
| 623 †`106` | `CNE_T_ClientGetTicket` | S→C serveur→client | inconnu | — |
| 624 †`107` | `CNE_T_ClientUseTicket` | S→C serveur→client | inconnu | — |
| 665 †`174` | `CNC_ZMis_MissionServerCheckFamilyInvite` | interne mission→zone | inconnu | — |
| 670 †`179` | `CNC_MW_MissionServerTextExCmd` | interne world→mission | inconnu | — |
| 671 †`180` | `CNC_MW_MissionServerTextExCmdRemoteWorld` | interne world→mission | inconnu | — |
| 672 †`181` | `CNC_MW_MissionServerTextExCmdRemoteZone` | interne world→mission | inconnu | — |
| 674 †`183` | `CNC_ZMis_MissionServerUpdatePKList` | interne mission→zone | inconnu | — |
| 675 †`184` | `CNE_MW_WorldTextCommand` | interne mission→world | inconnu | — |
| 676 †`185` | `CNC_DBA_AgentDBACommand` | interne dba→* | inconnu | — |
| 683 †`192` | `CNE_DBA_ClientAllFamily` | interne *→dba | inconnu | — |
| 684 †`193` | `CNE_DBA_ClientNewFamily` | interne *→dba | inconnu | — |
| 685 †`194` | `CNE_DBA_ClientChangeFamily` | interne *→dba | inconnu | — |
| 686 †`195` | `CNE_DBA_ClientRemoveFamily` | interne *→dba | inconnu | — |
| 687 †`196` | `CNE_DBA_ClientFamilyAllMember` | interne *→dba | inconnu | — |
| 688 †`197` | `CNE_DBA_ClientAddFamilyMember` | interne *→dba | inconnu | — |
| 689 †`198` | `CNE_DBA_ClientChannelMsg` | interne *→dba | inconnu | — |
| 703 †`212` | `CNC_ZMis_MissionServerUpdatePlayerCard` | interne mission→zone | décodé (passthru) | PASSTHRU |
| 704 †`213` | `CNC_ZMis_MissionServerItemMallMailSend` | interne mission→zone | inconnu | — |
| 705 †`214` | `CNC_ZMis_MissionServerItemMallMailCheck` | interne mission→zone | inconnu | — |
| 706 †`215` | `CNC_ZMis_MissionServerAddFamilyMessage` | interne mission→zone | inconnu | — |
| 707 †`216` | `CNE_ZMis_ZoneServerReportID` | interne zone→mission | inconnu | — |
| 708 †`217` | `CNC_ZMis_MissionServerIDReport` | interne mission→zone | inconnu | — |
| 831 †`221` | `CNC_ZMis_MissionServerRefreshRecommendActivities` | interne mission→zone | décodé (passthru) | PASSTHRU |

## MissionServer (205 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 10 | `CNE_ZMis_MissionServerCheckFamilyInvite` | interne zone→mission | inconnu | — |
| 15 | `CNE_MW_MissionServerTextExCmd` | interne mission→world | inconnu | — |
| 16 | `CNE_MW_MissionServerTextExCmdRemoteWorld` | interne mission→world | inconnu | — |
| 17 | `CNE_MW_MissionServerTextExCmdRemoteZone` | interne mission→world | inconnu | — |
| 18 † | `CNC_ZM_ZoneServerUpdatePKList` | interne mission→zone | inconnu | — |
| 19 | `CNE_ZMis_MissionServerUpdatePKList` | interne zone→mission | inconnu | — |
| 20 | `CNC_MW_WorldTextCommand` | interne world→mission | inconnu | — |
| 21 | `CNE_DBA_AgentDBACommand` | interne *→dba | inconnu | — |
| 22 † | `CNC_DBA_ClientContainer` | interne dba→* | inconnu | — |
| 23 † | `CNC_DBA_ClientEnchant` | interne dba→* | inconnu | — |
| 24 † | `CNC_DBA_ClientNodeID` | interne dba→* | inconnu | — |
| 25 † | `CNC_DBA_ClientPlayerData` | interne dba→* | inconnu | — |
| 26 † | `CNC_DBA_ClientMissionState` | interne dba→* | inconnu | — |
| 28 | `CNC_DBA_ClientAllFamily` | interne dba→* | inconnu | — |
| 29 | `CNC_DBA_ClientNewFamily` | interne dba→* | inconnu | — |
| 30 | `CNC_DBA_ClientChangeFamily` | interne dba→* | inconnu | — |
| 31 | `CNC_DBA_ClientRemoveFamily` | interne dba→* | inconnu | — |
| 32 | `CNC_DBA_ClientFamilyAllMember` | interne dba→* | inconnu | — |
| 33 † | `CNC_DBA_ClientAddFamilyMember` | interne dba→* | inconnu | — |
| 34 | `CNC_DBA_ClientChannelMsg` | interne dba→* | inconnu | — |
| 35 † | `CNC_DBA_ClientShortcut` | interne dba→* | inconnu | — |
| 36 † | `CNC_DBA_ClientSpell` | interne dba→* | inconnu | — |
| 37 † | `CNC_DBA_ClientFinishPlayerData` | interne dba→* | inconnu | — |
| 38 † | `CNC_DBA_ClientAllLover` | interne dba→* | inconnu | — |
| 39 † | `CNC_DBA_ClientCollection` | interne dba→* | inconnu | — |
| 41 † | `CNC_DBA_ClientFamilyStorage` | interne dba→* | inconnu | — |
| 42 † | `CNC_DBA_ClientAllTerritory` | interne dba→* | inconnu | — |
| 43 † | `CNC_DBA_ClientGladiatorTeamInfo` | interne dba→* | inconnu | — |
| 44 † | `CNC_DBA_ClientAllPlayerRoom` | interne dba→* | inconnu | — |
| 45 † | `CNC_DBA_ClientFortuneBagCounter` | interne dba→* | inconnu | — |
| 46 † | `CNC_DBA_ClientFamilyContainerData` | interne dba→* | inconnu | — |
| 47 † | `CNC_DBA_ClientAllFamilyInstnce` | interne dba→* | inconnu | — |
| 48 | `CNE_ZMis_MissionServerUpdatePlayerCard` | interne zone→mission | inconnu | — |
| 49 | `CNE_ZMis_MissionServerItemMallMailSend` | interne zone→mission | inconnu | — |
| 50 | `CNE_ZMis_MissionServerItemMallMailCheck` | interne zone→mission | inconnu | — |
| 51 | `CNE_ZMis_MissionServerAddFamilyMessage` | interne zone→mission | inconnu | — |
| 52 | `CNC_ZMis_ZoneServerReportID` | interne mission→zone | inconnu | — |
| 53 | `CNE_ZMis_MissionServerIDReport` | interne zone→mission | inconnu | — |
| 55 | `CNE_ZMis_MissionServerNodeCreate` | interne zone→mission | inconnu | — |
| 56 | `CNC_ZMis_ZoneServerListDialogue` | interne mission→zone | inconnu | — |
| 57 | `CNC_ZMis_ZoneServerReplyDialogue` | interne mission→zone | inconnu | — |
| 58 | `CNC_ZMis_ZoneServerTransitionCommands` | interne mission→zone | inconnu | — |
| 59 | `CNC_ZMis_ZoneServerTextOutput` | interne mission→zone | inconnu | — |
| 60 | `CNC_ZM_ZoneServerReportMonster` | interne mission→zone | inconnu | — |
| 61 | `CNC_ZM_ZoneServerRebornMonster` | interne mission→zone | inconnu | — |
| 62 | `CNC_ZM_ZoneServerSetRebornMonsterResult` | interne mission→zone | inconnu | — |
| 63 | `CNC_ZM_ZoneServerMessageToCharacter` | interne mission→zone | inconnu | — |
| 64 | `CNC_ZM_ZoneServerCharRegenerate` | interne mission→zone | inconnu | — |
| 65 | `CNC_ZMis_ZoneServerUpdateNPCSymbols` | interne mission→zone | inconnu | — |
| 66 | `CNC_ZMis_ZoneServerTextOutputIndex` | interne mission→zone | inconnu | — |
| 67 | `CNC_ZMis_ZoneServerUpdateMissionState` | interne mission→zone | inconnu | — |
| 68 | `CNC_ZMis_ZoneServerMailList` | interne mission→zone | inconnu | — |
| 69 | `CNC_ZMis_ZoneServerMailRead` | interne mission→zone | inconnu | — |
| 70 | `CNC_ZMis_ZoneServerMailDeleteResult` | interne mission→zone | inconnu | — |
| 71 | `CNC_ZMis_ZoneServerMailSendResult` | interne mission→zone | inconnu | — |
| 72 | `CNC_ZMis_ZoneServerMailReceive` | interne mission→zone | inconnu | — |
| 73 | `CNC_ZMis_ZoneServerPutItem` | interne mission→zone | inconnu | — |
| 74 | `CNC_ZMis_ZoneServerDropItem` | interne mission→zone | inconnu | — |
| 75 | `CNC_ZMis_ZoneServerMailGetItemOK` | interne mission→zone | inconnu | — |
| 76 | `CNC_ZMis_ZoneServerMailGetGoldOK` | interne mission→zone | inconnu | — |
| 77 | `CNC_ZMis_ZoneServerMailReturnResult` | interne mission→zone | inconnu | — |
| 78 | `CNC_ZMis_ZoneServerAuctionBuyList` | interne mission→zone | inconnu | — |
| 79 | `CNC_ZMis_ZoneServerAuctionBuyOK` | interne mission→zone | inconnu | — |
| 80 | `CNC_ZMis_ZoneServerAuctionCancelOK` | interne mission→zone | inconnu | — |
| 81 | `CNC_ZMis_ZoneServerAuctionSellList` | interne mission→zone | inconnu | — |
| 82 | `CNC_ZMis_ZoneServerAuctionSellResult` | interne mission→zone | inconnu | — |
| 83 | `CNC_ZMis_ZoneServerSearchInfo` | interne mission→zone | inconnu | — |
| 84 | `CNC_ZMis_ZoneServerMessageList` | interne mission→zone | inconnu | — |
| 85 | `CNC_ZMis_ZoneServerMessageWriteResult` | interne mission→zone | inconnu | — |
| 86 | `CNC_ZMis_ZoneServerMessageRead` | interne mission→zone | inconnu | — |
| 87 | `CNC_ZMis_ZoneServerMessageDeleteResult` | interne mission→zone | inconnu | — |
| 88 | `CNC_ZMis_ZoneServerCharNotQuit` | interne mission→zone | inconnu | — |
| 89 | `CNC_ZMis_ZoneServerLoverUpdate` | interne mission→zone | inconnu | — |
| 90 | `CNC_ZMis_ZoneServerAchievementUpdate` | interne mission→zone | inconnu | — |
| 91 | `CNC_ZMis_ZoneServerBattlefieldJoinResult` | interne mission→zone | inconnu | — |
| 92 | `CNC_ZMis_ZoneServerEnterBattlefield` | interne mission→zone | inconnu | — |
| 93 | `CNC_ZMis_ZoneServerStartBattlefield` | interne mission→zone | inconnu | — |
| 94 | `CNC_ZMis_ZoneServerBattlefieldList` | interne mission→zone | inconnu | — |
| 95 | `CNC_ZMis_ZoneServerBattlefieldEraseMember` | interne mission→zone | inconnu | — |
| 96 | `CNC_ZMis_ZoneServerEnterBattlefieldAnswer` | interne mission→zone | inconnu | — |
| 97 | `CNC_ZMis_ZoneServerBattlefieldSetup` | interne mission→zone | inconnu | — |
| 98 | `CNC_ZMis_ZoneServerBattlefieldChangeCharNum` | interne mission→zone | inconnu | — |
| 99 | `CNC_ZMis_ZoneServerCrossZoneResult` | interne mission→zone | inconnu | — |
| 100 | `CNC_ZMis_ZoneServerFamilyStorageUpdate` | interne mission→zone | inconnu | — |
| 101 | `CNC_ZMis_ZoneServerFamilyStorageLog` | interne mission→zone | inconnu | — |
| 102 | `CNC_ZMis_ZoneServerFamilyContainerUpdate` | interne mission→zone | inconnu | — |
| 103 | `CNC_ZMis_ZoneServerFamilyTownMemberQuery` | interne mission→zone | inconnu | — |
| 104 | `CNC_ZMis_ZoneServerQueryPlayerCard` | interne mission→zone | inconnu | — |
| 105 | `CNC_ZMis_ZoneServerTerritoryInfo` | interne mission→zone | inconnu | — |
| 106 | `CNC_ZMis_ZoneServerTerritoryQuery` | interne mission→zone | inconnu | — |
| 107 | `CNC_ZMis_ZoneServerTerritoryJoin` | interne mission→zone | inconnu | — |
| 108 | `CNC_ZMis_ZoneServerRankInfo` | interne mission→zone | inconnu | — |
| 109 | `CNC_ZMis_ZoneServerBuyBackList` | interne mission→zone | inconnu | — |
| 110 | `CNC_ZMis_ZoneServerBuyBackGetItem` | interne mission→zone | inconnu | — |
| 111 | `CNC_ZMis_ZoneServerBuybackInitAllocate` | interne mission→zone | inconnu | — |
| 112 | `CNC_ZMis_ZoneServerDeallocateItemID` | interne mission→zone | inconnu | — |
| 113 | `CNC_ZMis_ZoneServerNPCTalkCheck` | interne mission→zone | inconnu | — |
| 114 | `CNC_ZMis_ZoneServerRemoteClient` | interne mission→zone | inconnu | — |
| 115 | `CNC_ZMis_ZoneServerTextExCmd` | interne mission→zone | inconnu | — |
| 116 | `CNC_ZMis_ZoneServerTextExCmdRemoteWorld` | interne mission→zone | inconnu | — |
| 117 | `CNC_ZMis_ZoneServerTextExCmdRemoteZone` | interne mission→zone | inconnu | — |
| 118 | `CNC_ZMis_ZoneServerEnterColosseum` | interne mission→zone | inconnu | — |
| 119 | `CNC_ZMis_ZoneServerColosseumJoinResult` | interne mission→zone | inconnu | — |
| 120 | `CNC_ZMis_ZoneServerStartColosseum` | interne mission→zone | inconnu | — |
| 121 | `CNC_ZMis_ZoneServerColosseumLeaveJoinResult` | interne mission→zone | inconnu | — |
| 122 | `CNC_ZMis_ZoneServerColosseumModeUpdate` | interne mission→zone | inconnu | — |
| 123 | `CNC_ZMis_ZoneServerSearchRoomResult` | interne mission→zone | inconnu | — |
| 124 | `CNC_ZMis_ZoneServerUpdateRoom` | interne mission→zone | inconnu | — |
| 125 | `CNC_ZMis_ZoneServerSwitchPlayerRoom` | interne mission→zone | inconnu | — |
| 126 | `CNC_ZMis_ZoneServerUpdateRoomItem` | interne mission→zone | inconnu | — |
| 127 | `CNE_ZMis_MissionServerListDialogue` | interne zone→mission | inconnu | — |
| 128 | `CNE_ZMis_MissionServerOnDialogueSelected` | interne zone→mission | inconnu | — |
| 129 | `CNE_ZM_MonsterServerMonsterReport` | interne zone→mission | inconnu | — |
| 130 | `CNE_ZM_MonsterServerMonsterDieReport` | interne zone→mission | inconnu | — |
| 131 | `CNE_ZM_MonsterServerListRebornMonster` | interne zone→mission | inconnu | — |
| 132 | `CNE_ZM_MonsterServerSetRebornMonster` | interne zone→mission | inconnu | — |
| 133 | `CNE_ZM_MonsterServerModifyRebornMonster` | interne zone→mission | inconnu | — |
| 134 | `CNE_ZM_MonsterServerRemoveRebornMonster` | interne zone→mission | inconnu | — |
| 135 | `CNE_ZM_MonsterServerUnRegRebornMonster` | interne zone→mission | inconnu | — |
| 136 | `CNE_ZM_MonsterServerRemoveNode` | interne zone→mission | inconnu | — |
| 137 | `CNE_ZM_MonsterServerStartCharRegenerate` | interne zone→mission | inconnu | — |
| 138 | `CNE_ZM_MonsterServerStopCharRegenerate` | interne zone→mission | inconnu | — |
| 139 | `CNE_ZM_MonsterServerRegenerationReport` | interne zone→mission | inconnu | — |
| 140 | `CNE_ZMis_MissionServerRemovePMS` | interne zone→mission | inconnu | — |
| 141 | `CNE_ZMis_MissionServerCloseMissionBrief` | interne zone→mission | inconnu | — |
| 142 | `CNE_ZMis_MissionServerMigrationDone` | interne zone→mission | inconnu | — |
| 143 | `CNE_ZMis_MissionServerGiveUpMission` | interne zone→mission | inconnu | — |
| 144 | `CNE_ZMis_MissionServerMobDieReport` | interne zone→mission | inconnu | — |
| 145 | `CNE_ZMis_MissionServerMailSend` | interne zone→mission | inconnu | — |
| 146 | `CNE_ZMis_MissionServerSendSystemMail` | interne zone→mission | inconnu | — |
| 147 | `CNE_ZMis_MissionServerMessageWrite` | interne zone→mission | inconnu | — |
| 148 | `CNE_ZMis_MissionServerChannelMsg` | interne zone→mission | inconnu | — |
| 149 | `CNE_ZMis_MissionServerBattlefieldJoin` | interne zone→mission | inconnu | — |
| 150 | `CNE_ZMis_MissionServerBattlefieldEraseMember` | interne zone→mission | inconnu | — |
| 151 | `CNE_ZMis_MissionServerBattlefieldClose` | interne zone→mission | inconnu | — |
| 152 | `CNE_ZMis_MissionServerBattlefieldList` | interne zone→mission | inconnu | — |
| 153 | `CNE_ZMis_MissionServerEnterBattlefieldAnswer` | interne zone→mission | inconnu | — |
| 154 | `CNE_ZMis_MissionServerBattlefieldUpdateStatus` | interne zone→mission | inconnu | — |
| 155 | `CNE_ZMis_MissionServerCrossZoneRequest` | interne zone→mission | inconnu | — |
| 156 | `CNE_ZMis_MissionServerCrossZone` | interne zone→mission | inconnu | — |
| 157 | `CNE_ZMis_MissionServerRemoteClient` | interne zone→mission | inconnu | — |
| 158 | `CNE_ZMis_MissionServerTextExCmd` | interne zone→mission | inconnu | — |
| 159 | `CNE_ZMis_MissionServerTextExCmdRemoteWorld` | interne zone→mission | inconnu | — |
| 160 | `CNE_ZMis_MissionServerTextExCmdRemoteZone` | interne zone→mission | inconnu | — |
| 161 | `CNE_ZMis_MissionServerGetTeamInviteeID` | interne zone→mission | inconnu | — |
| 162 | `CNC_ZMis_ZoneServerReturnTeamInviteeID` | interne mission→zone | inconnu | — |
| 163 | `CNC_ZMis_ZoneServerGMToolGetInfo` | interne mission→zone | inconnu | — |
| 164 | `CNC_ZMis_ZoneServerGMToolGetMission` | interne mission→zone | inconnu | — |
| 165 | `CNE_ZMis_MissionServerColosseumJoinAnswer` | interne zone→mission | inconnu | — |
| 166 | `CNE_ZMis_MissionServerColosseumJoinRequest` | interne zone→mission | inconnu | — |
| 167 | `CNE_ZMis_MissionServerColosseumClose` | interne zone→mission | inconnu | — |
| 168 | `CNE_ZMis_MissionServerColosseumLeaveJoin` | interne zone→mission | inconnu | — |
| 169 | `CNC_ZMis_ZoneServerAchievementRewardMail` | interne mission→zone | inconnu | — |
| 170 | `CNC_ZMis_ZoneServerResetDailyMission` | interne mission→zone | inconnu | — |
| 171 † | `CNC_DBA_ClientNewPlayerGuideData` | interne dba→* | inconnu | — |
| 172 | `CNC_ZMis_ZoneServerUpdateLifelongPoint` | interne mission→zone | inconnu | — |
| 173 | `CNC_ZMis_ZoneServerUpdateLifelongGold` | interne mission→zone | inconnu | — |
| 174 | `CNC_ZMis_ZoneServerResetDailyMonsterKill` | interne mission→zone | inconnu | — |
| 175 † | `CNC_DBA_ClientRecommendActivityState` | interne dba→* | inconnu | — |
| 176 | `CNE_ZMis_MissionServerRefreshRecommendActivities` | interne zone→mission | inconnu | — |
| 177 † | `CNC_ZMis_ZoneServerUpdateRecommendActivities` | interne mission→zone | inconnu | — |
| 178 | `CNC_ZMis_ZoneServerFamilyBattleInfo` | interne mission→zone | inconnu | — |
| 179 | `CNC_ZMis_ZoneServerResetNodeTimes` | interne mission→zone | inconnu | — |
| 180 | `CNE_ZMis_MissionServerSetRecommendActivityTracing` | interne zone→mission | inconnu | — |
| 181 † | `CNC_DBA_ClientElfData` | interne dba→* | inconnu | — |
| 182 | `CNC_ZMis_ZoneServerPVPTopData` | interne mission→zone | inconnu | — |
| 183 | `CNC_ZMis_ZoneServerGreedyIslandJoin` | interne mission→zone | inconnu | — |
| 184 | `CNC_ZMis_ZoneServerGreedyIslandInfo` | interne mission→zone | inconnu | — |
| 185 | `CNE_ZMis_MissionServerGreedyIslandRankQuery` | interne zone→mission | inconnu | — |
| 186 | `CNC_ZMis_ZoneServerColosseumQuery` | interne mission→zone | inconnu | — |
| 187 | `CNE_ZMis_MissionServerBattleRoyaleJoinRequest` | interne zone→mission | inconnu | — |
| 188 | `CNC_ZMis_ZoneServerStartBattleRoyale` | interne mission→zone | inconnu | — |
| 189 | `CNE_ZMis_MissionServerBattleRoyaleClose` | interne zone→mission | inconnu | — |
| 190 | `CNE_ZMis_MissionServerPlayerCCDataSave` | interne zone→mission | inconnu | — |
| 191 | `CNC_DBA_ClientFastDress` | interne dba→* | inconnu | — |
| 192 | `CNC_ZMis_ZoneServerFastDressEquipEnd` | interne mission→zone | inconnu | — |
| 193 | `CNC_ZMis_ZoneServerWeddingsInfo` | interne mission→zone | inconnu | — |
| 194 | `CNE_ZMis_MissionServerVersusInstanceJoinRequest` | interne zone→mission | inconnu | — |
| 195 | `CNC_ZMis_ZoneServerStartVersusInstance` | interne mission→zone | inconnu | — |
| 196 | `CNE_ZMis_MissionServerFindCharacter` | interne zone→mission | inconnu | — |
| 197 | `CNC_ZMis_ZoneServerFindTargetLocation` | interne mission→zone | inconnu | — |
| 198 | `CNE_ZMis_MissionServerSendTargetLocation` | interne zone→mission | inconnu | — |
| 199 | `CNC_ZMis_ZoneServerTransportGMCharacterToCharacter` | interne mission→zone | inconnu | — |
| 200 | `CNC_ZMis_ZoneServerStartBattleCrusade` | interne mission→zone | inconnu | — |
| 201 | `CNC_ZMis_ZoneServerEnterBattleCrusadeAnswer` | interne mission→zone | inconnu | — |
| 202 | `CNC_ZMis_ZoneServerBattleCrusadeInfo` | interne mission→zone | inconnu | — |
| 203 | `CNE_ZMis_MissionServerArrangeItem` | interne zone→mission | inconnu | — |
| 204 | `CNC_DBA_ClientLotteryCount` | interne dba→* | inconnu | — |
| 655 †`0` | `CNC_MW_WorldServerItemMallMailSendResult` | interne world→mission | inconnu | — |
| 656 †`1` | `CNC_MW_WorldServerItemMallMailCheckOK` | interne world→mission | inconnu | — |
| 657 †`2` | `CNC_MW_WorldServerUpdateBlackList` | interne world→mission | inconnu | — |
| 658 †`3` | `CNC_MW_WorldServerUpdatePrestigeData` | interne world→mission | inconnu | — |
| 659 †`4` | `CNC_MW_WorldServerUpdateFriendMember` | interne world→mission | inconnu | — |
| 660 †`5` | `CNC_MW_WorldServerCrossZone` | interne world→mission | inconnu | — |
| 661 †`6` | `CNC_MW_WorldServerFBMsgUpdate` | interne world→mission | inconnu | — |
| 662 †`7` | `CNC_MW_WorldServerFriendNotify` | interne world→mission | inconnu | — |
| 663 †`8` | `CNC_MW_WorldServerUpdateSpecialCoin` | interne world→mission | inconnu | — |
| 664 †`9` | `CNC_MW_WorldServerCheckFamilyInvite` | interne world→mission | inconnu | — |
| 666 †`11` | `CNC_MW_WorldServerTell` | interne world→mission | inconnu | — |
| 667 †`12` | `CNC_MW_WorldServerTextExCmd` | interne world→mission | inconnu | — |
| 668 †`13` | `CNC_MW_WorldServerTextExCmdRemoteWorld` | interne world→mission | inconnu | — |
| 669 †`14` | `CNC_MW_WorldServerTextExCmdRemoteZone` | interne world→mission | inconnu | — |
| 682 †`27` | `CNC_DBA_ClientDelPlayerOK` | interne dba→* | inconnu | — |
| 695 †`40` | `CNC_DBA_ClientFamilyAllMessage` | interne dba→* | inconnu | — |
| 709 †`54` | `CNC_MW_WorldServerCrossWorldEndClearAccount` | interne world→mission | inconnu | — |

## ZoneServer (871 NCIDs)

| NCID | Classe serveur | Direction | Statut | Preuve |
|---|---|---|---|---|
| 0 | `CNE_CZ_ZoneServerReceiveTicket` | C→S client→zone | implémenté `ZoneUseTicket` | `ZoneMessages.cs` |
| 1 | `CNE_CZ_ZoneServerMoveSelf` | C→S client→zone | implémenté `ZoneMoveSelf` | `ZoneMessages.cs` |
| 2 | `CNE_CZ_ZoneServerWhoIs` | C→S client→zone | inconnu | — |
| 3 | `CNE_CZ_ZoneServerTextCommand` | C→S client→zone | implémenté `ZoneTextCommand` | `ZoneEntityMessages.cs` |
| 4 | `CNE_CZ_ZoneServerMigrationDone` | C→S client→zone | implémenté `ZoneMigrationDone` | `ZoneMessages.cs` |
| 5 | `CNE_CZ_ZoneServerMeleeAttack` | C→S client→zone | inconnu | — |
| 6 | `CNE_CZ_ZoneServerDropItem` | C→S client→zone | inconnu | — |
| 7 | `CNE_CZ_ZoneServerSwitchItemLocation` | C→S client→zone | inconnu | — |
| 8 | `CNE_CZ_ZoneServerUpdateEquipment` | C→S client→zone | implémenté `ZoneUpdateEquipment` | `ZoneEquipMessages.cs` |
| 9 | `CNE_CZ_ZoneServerSpellCastToLife` | C→S client→zone | inconnu | — |
| 10 | `CNE_CZ_ZoneServerTalkToNPC` | C→S client→zone | implémenté `ZoneTalkToNpc` | `ZoneMessages.cs` |
| 11 | `CNE_CZ_ZoneServerReplyDialog` | C→S client→zone | implémenté `ZoneReplyDialog` | `ZoneMessages.cs` |
| 12 | `CNE_CZ_ZoneServerUseSkillPoint` | C→S client→zone | inconnu | — |
| 13 | `CNE_CZ_ZoneServerSpellBreak` | C→S client→zone | inconnu | — |
| 14 | `CNE_CZ_ZoneServerPlayerAction` | C→S client→zone | implémenté `ZonePlayerAction` | `ZoneMessages.cs` |
| 15 | `CNE_CZ_ZoneServerCheckNodeExit` | C→S client→zone | implémenté `ZoneCheckNodeExit` | `ZoneShopMessages.cs` |
| 16 | `CNE_CZ_ZoneServerSpellCastToLocation` | C→S client→zone | inconnu | — |
| 17 | `CNE_CZ_ZoneServerSpellCastToNowhere` | C→S client→zone | inconnu | — |
| 18 | `CNE_CZ_ZoneServerEnchantUpdate` | C→S client→zone | inconnu | — |
| 19 | `CNE_CZ_ZoneServerItemUse` | C→S client→zone | inconnu | — |
| 20 | `CNE_CZ_ZoneServerRevive` | C→S client→zone | implémenté `ZoneReviveRequest` | `ZoneShopMessages.cs` |
| 21 | `CNE_CZ_ZoneServerPlayerEmotion` | C→S client→zone | inconnu | — |
| 22 | `CNE_CZ_ZoneServerBuyItems` | C→S client→zone | implémenté `ZoneBuyItems` | `ZoneShopMessages.cs` |
| 23 | `CNE_CZ_ZoneServerSellItems` | C→S client→zone | implémenté `ZoneSellItems` | `ZoneShopMessages.cs` |
| 24 | `CNE_CZ_ZoneServerEnterNodeEquipAndSpell` | C→S client→zone | implémenté `EnterNodeEquipAndSpell` | `ZoneMessages.cs` |
| 25 | `CNE_CZ_ZoneServerEnterNodeInventory` | C→S client→zone | implémenté `EnterNodeInventory` | `ZoneMessages.cs` |
| 26 | `CNE_CZ_ZoneServerEnterNodePlayerData` | C→S client→zone | implémenté `EnterNodePlayerData` | `ZoneMessages.cs` |
| 27 | `CNE_CZ_ZoneServerSafeQuitReq` | C→S client→zone | inconnu | — |
| 28 | `CNE_CZ_ZoneServerTeamLeave` | C→S client→zone | inconnu | — |
| 29 | `CNE_CZ_ZoneServerTeamInvite` | C→S client→zone | inconnu | — |
| 30 | `CNE_CZ_ZoneServerTeamInviteAnswer` | C→S client→zone | inconnu | — |
| 31 | `CNE_CZ_ZoneServerTeamInviteCancel` | C→S client→zone | inconnu | — |
| 32 | `CNE_CZ_ZoneServerSafeQuitCancel` | C→S client→zone | inconnu | — |
| 33 | `CNE_CZ_ZoneServerSafeQuitCountDownOver` | C→S client→zone | inconnu | — |
| 34 | `CNE_CZ_ZoneServerChannelMsg` | C→S client→zone | implémenté `ZoneChannelMsg` | `ZoneHudMessages.cs` |
| 35 | `CNE_CZ_ZoneServerTreasurePickup` | C→S client→zone | inconnu | — |
| 36 | `CNE_CZ_ZoneServerTradeRequest` | C→S client→zone | inconnu | — |
| 37 | `CNE_CZ_ZoneServerTradeRequestAccept` | C→S client→zone | inconnu | — |
| 38 | `CNE_CZ_ZoneServerTradeAddItem` | C→S client→zone | inconnu | — |
| 39 | `CNE_CZ_ZoneServerTradeRemoveItem` | C→S client→zone | inconnu | — |
| 40 | `CNE_CZ_ZoneServerTradeModifyGold` | C→S client→zone | inconnu | — |
| 41 | `CNE_CZ_ZoneServerTradeConfirm` | C→S client→zone | inconnu | — |
| 42 | `CNE_CZ_ZoneServerTradeCancel` | C→S client→zone | inconnu | — |
| 43 | `CNE_CZ_ZoneServerCloseMissionBrief` | C→S client→zone | inconnu | — |
| 44 | `CNE_CZ_ZoneServerResurrect` | C→S client→zone | inconnu | — |
| 45 | `CNE_CZ_ZoneServerAskRepairEquipGold` | C→S client→zone | inconnu | — |
| 46 | `CNE_CZ_ZoneServerRepairEquipment` | C→S client→zone | inconnu | — |
| 47 | `CNE_CZ_ZoneServerAskRepairOneItemGold` | C→S client→zone | inconnu | — |
| 48 | `CNE_CZ_ZoneServerRepairOneItem` | C→S client→zone | inconnu | — |
| 49 | `CNE_CZ_ZoneServerFamilyCreate` | C→S client→zone | inconnu | — |
| 50 | `CNE_CZ_ZoneServerQueryMissionStates` | C→S client→zone | implémenté `QueryMissionStates` | `ZoneMessages.cs` |
| 51 | `CNE_CZ_ZoneServerGiveUpMission` | C→S client→zone | inconnu | — |
| 52 | `CNE_CZ_ZoneServerBuySpell` | C→S client→zone | inconnu | — |
| 53 | `CNE_CZ_ZoneServerEnterNodeShortcut` | C→S client→zone | implémenté `EnterNodeShortcut` | `ZoneMessages.cs` |
| 54 | `CNE_CZ_ZoneServerUpdateShortcut` | C→S client→zone | inconnu | — |
| 55 | `CNE_CZ_ZoneServerGetShortcutOfPage` | C→S client→zone | inconnu | — |
| 56 | `CNE_CZ_ZoneServerTradeReset` | C→S client→zone | inconnu | — |
| 57 | `CNE_CZ_ZoneServerEcho` | C→S client→zone | implémenté `Echo` | `ZoneMessages.cs` |
| 58 | `CNE_CZ_ZoneServerLogoutReq` | C→S client→zone | inconnu | — |
| 59 | `CNE_CZ_ZoneServerSpellCastToNearby` | C→S client→zone | inconnu | — |
| 60 | `CNE_CZ_ZoneServerStorageDeposit` | C→S client→zone | implémenté `ZoneStorageDeposit` | `ZoneShopMessages.cs` |
| 61 | `CNE_CZ_ZoneServerStorageWithdraw` | C→S client→zone | implémenté `ZoneStorageWithdraw` | `ZoneShopMessages.cs` |
| 62 | `CNE_CZ_ZoneServerGetStorageData` | C→S client→zone | implémenté `ZoneGetStorageData` | `ZoneMessages.cs` |
| 63 | `CNE_CZ_ZoneServerLeaveShop` | C→S client→zone | implémenté `LeaveShop` | `ZoneMessages.cs` |
| 64 | `CNE_CZ_ZoneServerAskIdentifyGold` | C→S client→zone | inconnu | — |
| 65 | `CNE_CZ_ZoneServerIdentifyItem` | C→S client→zone | inconnu | — |
| 66 | `CNE_CZ_ZoneServerExchangeItemPin` | C→S client→zone | inconnu | — |
| 67 | `CNE_CZ_ZoneServerInlayItem` | C→S client→zone | inconnu | — |
| 68 | `CNE_CZ_ZoneServerReportProcessInfos` | C→S client→zone | inconnu | — |
| 69 | `CNE_CZ_ZoneServerStorageExpand` | C→S client→zone | inconnu | — |
| 70 | `CNE_CZ_ZoneServerItemMallReceivableEnter` | C→S client→zone | inconnu | — |
| 71 | `CNE_CZ_ZoneServerAskVendor` | C→S client→zone | implémenté `ZoneAskVendor` | `ZoneShopMessages.cs` |
| 72 | `CNE_CZ_ZoneServerAskVendorContainer` | C→S client→zone | implémenté `ZoneAskVendorContainer` | `ZoneShopMessages.cs` |
| 73 | `CNE_CZ_ZoneServerSloganUpdate` | C→S client→zone | inconnu | — |
| 74 | `CNE_CZ_ZoneServerVendorClose` | C→S client→zone | inconnu | — |
| 75 | `CNE_CZ_ZoneServerVendorCloseView` | C→S client→zone | inconnu | — |
| 76 | `CNE_CZ_ZoneServerVendorBuy` | C→S client→zone | inconnu | — |
| 77 | `CNE_CZ_ZoneServerVendorSellItem` | C→S client→zone | inconnu | — |
| 78 | `CNE_CZ_ZoneServerVendorCloseVending` | C→S client→zone | inconnu | — |
| 79 | `CNE_CZ_ZoneServerTeamOperation` | C→S client→zone | inconnu | — |
| 80 | `CNE_CZ_ZoneServerVendorUseItem` | C→S client→zone | inconnu | — |
| 81 | `CNE_CZ_ZoneServerUpdateFamily` | C→S client→zone | inconnu | — |
| 82 | `CNE_CZ_ZoneServerSetFamilyEmblem` | C→S client→zone | inconnu | — |
| 83 | `CNE_CZ_ZoneServerItemMallUseItem` | C→S client→zone | inconnu | — |
| 84 | `CNE_CZ_ZoneServerDivideItem` | C→S client→zone | inconnu | — |
| 85 | `CNE_CZ_ZoneServerEnchantCancel` | C→S client→zone | décodé (incomplet) | INCOMPLET |
| 86 | `CNE_CZ_ZoneServerTeamRadarAttention` | C→S client→zone | inconnu | — |
| 87 | `CNE_CZ_ZoneServerMissionListOp` | C→S client→zone | implémenté `ZoneMissionListOp` | `ZoneMessages.cs` |
| 88 | `CNE_CZ_ZoneServerTransport` | C→S client→zone | implémenté `ZoneTransport` | `ZoneShopMessages.cs` |
| 89 | `CNE_CZ_ZoneServerBagDueDate` | C→S client→zone | inconnu | — |
| 90 | `CNE_CZ_ZoneServerTeamChangeLeader` | C→S client→zone | inconnu | — |
| 91 | `CNE_CZ_ZoneServerTradeReady` | C→S client→zone | inconnu | — |
| 92 | `CNE_CZ_ZoneServerTradeUnConfirm` | C→S client→zone | inconnu | — |
| 93 | `CNE_CZ_ZoneServerUpdateAppellation` | C→S client→zone | inconnu | — |
| 94 | `CNE_CZ_ZoneServerQueryAppellation` | C→S client→zone | inconnu | — |
| 95 | `CNE_CZ_ZoneServerMailOpenBox` | C→S client→zone | inconnu | — |
| 96 | `CNE_CZ_ZoneServerMailQuery` | C→S client→zone | inconnu | — |
| 97 | `CNE_CZ_ZoneServerMailRead` | C→S client→zone | inconnu | — |
| 98 | `CNE_CZ_ZoneServerMailDelete` | C→S client→zone | inconnu | — |
| 99 | `CNE_CZ_ZoneServerMailSend` | C→S client→zone | inconnu | — |
| 100 | `CNE_CZ_ZoneServerMailGetItem` | C→S client→zone | décodé (passthru) | PASSTHRU |
| 101 | `CNE_CZ_ZoneServerMailGetGold` | C→S client→zone | inconnu | — |
| 102 | `CNE_CZ_ZoneServerMailReturn` | C→S client→zone | inconnu | — |
| 103 | `CNE_CZ_ZoneServerSearchCondition` | C→S client→zone | inconnu | — |
| 104 | `CNE_CZ_ZoneServerSearchOpt` | C→S client→zone | implémenté `ZoneSearchOpt` | `ZoneMessages.cs` |
| 105 | `CNE_CZ_ZoneServerElfStoneUpdate` | C→S client→zone | inconnu | — |
| 106 | `CNE_CZ_ZoneServerElfStoneUse` | C→S client→zone | inconnu | — |
| 107 | `CNE_CZ_ZoneServerElfWork` | C→S client→zone | inconnu | — |
| 108 | `CNE_CZ_ZoneServerElfReturn` | C→S client→zone | inconnu | — |
| 109 | `CNE_CZ_ZoneServerElfCallElf` | C→S client→zone | inconnu | — |
| 110 | `CNE_CZ_ZoneServerEnterNodeElfInfo` | C→S client→zone | implémenté `EnterNodeElfInfo` | `ZoneMessages.cs` |
| 111 | `CNE_CZ_ZoneServerElfWorkResultOK` | C→S client→zone | inconnu | — |
| 112 | `CNE_CZ_ZoneServerElfCheckData` | C→S client→zone | inconnu | — |
| 113 | `CNE_CZ_ZoneServerEnterNodeElfContainer` | C→S client→zone | implémenté `EnterNodeElfContainer` | `ZoneMessages.cs` |
| 114 | `CNE_CZ_ZoneServerAuctionBuyQuery` | C→S client→zone | inconnu | — |
| 115 | `CNE_CZ_ZoneServerAuctionBuy` | C→S client→zone | inconnu | — |
| 116 | `CNE_CZ_ZoneServerAuctionSellQuery` | C→S client→zone | inconnu | — |
| 117 | `CNE_CZ_ZoneServerAuctionSell` | C→S client→zone | inconnu | — |
| 118 | `CNE_CZ_ZoneServerAuctionCancel` | C→S client→zone | inconnu | — |
| 119 | `CNE_CZ_ZoneServerStrengthenItem` | C→S client→zone | inconnu | — |
| 120 | `CNE_CZ_ZoneServerStrengthenClose` | C→S client→zone | inconnu | — |
| 121 | `CNE_CZ_ZoneServerSetSpellStone` | C→S client→zone | inconnu | — |
| 122 | `CNE_CZ_ZoneServerStateCancel` | C→S client→zone | inconnu | — |
| 123 | `CNE_CZ_ZoneServerTurnSelf` | C→S client→zone | implémenté `ZoneTurnSelf` | `ZoneMessages.cs` |
| 124 | `CNE_CZ_ZoneServerTargetUpdate` | C→S client→zone | implémenté `ZoneTargetUpdate` | `ZoneMessages.cs` |
| 125 | `CNE_CZ_ZoneServerGetSelectLifeTarget` | C→S client→zone | inconnu | — |
| 126 | `CNE_CZ_ZoneServerPVPFlagModify` | C→S client→zone | inconnu | — |
| 127 | `CNE_CZ_ZoneServerEnchantPause` | C→S client→zone | inconnu | — |
| 128 | `CNE_CZ_ZoneServerEnchantStart` | C→S client→zone | inconnu | — |
| 129 | `CNE_CZ_ZoneServerGoLottery` | C→S client→zone | inconnu | — |
| 130 | `CNE_CZ_ZoneServerFamilyContainerPutItem` | C→S client→zone | inconnu | — |
| 131 | `CNE_CZ_ZoneServerFamilyContainerGetItem` | C→S client→zone | inconnu | — |
| 132 | `CNE_CZ_ZoneServerFamilyInstanceGetData` | C→S client→zone | inconnu | — |
| 133 | `CNE_CZ_ZoneServerEnterNodePlayerCard` | C→S client→zone | implémenté `EnterNodePlayerCard` | `ZoneMessages.cs` |
| 134 | `CNE_CZ_ZoneServerQueryPlayerCard` | C→S client→zone | inconnu | — |
| 135 | `CNE_CZ_ZoneServerDuelRequest` | C→S client→zone | inconnu | — |
| 136 | `CNE_CZ_ZoneServerDuelRequestAnswer` | C→S client→zone | inconnu | — |
| 137 | `CNE_CZ_ZoneServerElfTalk` | C→S client→zone | inconnu | — |
| 138 | `CNE_CZ_ZoneServerMessageListQuery` | C→S client→zone | inconnu | — |
| 139 | `CNE_CZ_ZoneServerMessageWrite` | C→S client→zone | inconnu | — |
| 140 | `CNE_CZ_ZoneServerMessageRead` | C→S client→zone | inconnu | — |
| 141 | `CNE_CZ_ZoneServerMessageDelete` | C→S client→zone | inconnu | — |
| 142 | `CNE_CZ_ZoneServerMessageExit` | C→S client→zone | inconnu | — |
| 143 | `CNE_CZ_ZoneServerLookEquip` | C→S client→zone | inconnu | — |
| 144 | `CNE_CZ_ZoneServerBattlefieldJoin` | C→S client→zone | inconnu | — |
| 145 | `CNE_CZ_ZoneServerEnterBattlefieldAnswer` | C→S client→zone | inconnu | — |
| 146 | `CNE_CZ_ZoneServerBattlefieldLeave` | C→S client→zone | inconnu | — |
| 147 | `CNE_CZ_ZoneServerBFListQuery` | C→S client→zone | inconnu | — |
| 148 | `CNE_CZ_ZoneServerSetAAPoint` | C→S client→zone | inconnu | — |
| 149 | `CNE_CZ_ZoneServerSetAARate` | C→S client→zone | inconnu | — |
| 150 | `CNE_CZ_ZoneServerSaveCollection` | C→S client→zone | inconnu | — |
| 151 | `CNE_CZ_ZoneServerGetCollection` | C→S client→zone | inconnu | — |
| 152 | `CNE_CZ_ZoneServerQueryCollection` | C→S client→zone | implémenté `QueryCollection` | `ZoneMessages.cs` |
| 153 | `CNE_CZ_ZoneServerHello` | C→S client→zone | inconnu | — |
| 154 | `CNE_CZ_ZoneServerLoverInvite` | C→S client→zone | inconnu | — |
| 155 | `CNE_CZ_ZoneServerLoverMessage` | C→S client→zone | inconnu | — |
| 156 | `CNE_CZ_ZoneServerQueryLoverInfo` | C→S client→zone | implémenté `QueryLoverInfo` | `ZoneMessages.cs` |
| 157 | `CNE_CZ_ZoneServerLoverBreak` | C→S client→zone | inconnu | — |
| 158 | `CNE_CZ_ZoneServerVictoryDeclaration` | C→S client→zone | inconnu | — |
| 159 | `CNE_CZ_ZoneServerFamilyDonateMoney` | C→S client→zone | inconnu | — |
| 160 | `CNE_CZ_ZoneServerMissionGoto` | C→S client→zone | inconnu | — |
| 161 | `CNE_CZ_ZoneServerFamilyStatue` | C→S client→zone | inconnu | — |
| 162 | `CNE_CZ_ZoneServerQueryBattlefieldData` | C→S client→zone | implémenté `QueryBattlefieldData` | `ZoneMessages.cs` |
| 163 | `CNE_CZ_ZoneServerNodeGroupReset` | C→S client→zone | inconnu | — |
| 164 | `CNE_CZ_ZoneServerUpdateBotData` | C→S client→zone | implémenté `ZoneUpdateBotData` | `ZoneMessages.cs` |
| 165 | `CNE_CZ_ZoneServerQueryClosets` | C→S client→zone | implémenté `QueryClosets` | `ZoneMessages.cs` |
| 166 | `CNE_CZ_ZoneServerQueryClassList` | C→S client→zone | implémenté `QueryClassList` | `ZoneMessages.cs` |
| 167 | `CNE_CZ_ZoneServerClosetSet` | C→S client→zone | inconnu | — |
| 168 | `CNE_CZ_ZoneServerChangeClass` | C→S client→zone | inconnu | — |
| 169 | `CNE_CZ_ZoneServerPlayerCardUpdate` | C→S client→zone | inconnu | — |
| 170 | `CNE_CZ_ZoneServerPlayerWork` | C→S client→zone | inconnu | — |
| 171 | `CNE_CZ_ZoneServerPlayerWorkResultOK` | C→S client→zone | inconnu | — |
| 172 | `CNE_CZ_ZoneServerPlayerWorkCheckData` | C→S client→zone | implémenté `PlayerWorkCheckData` | `ZoneMessages.cs` |
| 173 | `CNE_CZ_ZoneServerFamilyInstanceCreate` | C→S client→zone | inconnu | — |
| 174 | `CNE_CZ_ZoneServerFamilyTownQuery` | C→S client→zone | inconnu | — |
| 175 | `CNE_CZ_ZoneServerFamilyTownMemberQuery` | C→S client→zone | inconnu | — |
| 176 | `CNE_CZ_ZoneServerFamilyTownEnter` | C→S client→zone | inconnu | — |
| 177 | `CNE_CZ_ZoneServerFamilyStoragePutItem` | C→S client→zone | inconnu | — |
| 178 | `CNE_CZ_ZoneServerFamilyStorageGetItem` | C→S client→zone | inconnu | — |
| 179 | `CNE_CZ_ZoneServerFamilyStorageQueryLog` | C→S client→zone | inconnu | — |
| 180 | `CNE_CZ_ZoneServerFamilyBuildingRepair` | C→S client→zone | inconnu | — |
| 181 | `CNE_CZ_ZoneServerQueryLicense` | C→S client→zone | implémenté `QueryLicense` | `ZoneMessages.cs` |
| 182 | `CNE_CZ_ZoneServerPlayerLicense` | C→S client→zone | inconnu | — |
| 183 | `CNE_CZ_ZoneServerQueryLicenseSet` | C→S client→zone | inconnu | — |
| 184 | `CNE_CZ_ZoneServerCheckUpdateLottery` | C→S client→zone | inconnu | — |
| 185 | `CNE_CZ_ZoneServerBuyBackOpen` | C→S client→zone | inconnu | — |
| 186 | `CNE_CZ_ZoneServerSetMissionTrace` | C→S client→zone | implémenté `ZoneSetMissionTrace` | `ZoneMessages.cs` |
| 187 | `CNE_CZ_ZoneServerPetModeChange` | C→S client→zone | inconnu | — |
| 188 | `CNE_CZ_ZoneServerSendPetCmd` | C→S client→zone | inconnu | — |
| 189 | `CNE_CZ_ZoneServerTerritoryQuery` | C→S client→zone | inconnu | — |
| 190 | `CNE_CZ_ZoneServerTerritoryJoin` | C→S client→zone | inconnu | — |
| 191 | `CNE_CZ_ZoneServerTeamRecruitMsg` | C→S client→zone | inconnu | — |
| 192 | `CNE_CZ_ZoneServerSearchTeam` | C→S client→zone | inconnu | — |
| 193 | `CNE_CZ_ZoneServerRollTreasure` | C→S client→zone | décodé (passthru) | PASSTHRU |
| 194 | `CNE_CZ_ZoneServerRankInfoQuery` | C→S client→zone | inconnu | — |
| 195 | `CNE_CZ_ZoneServerEnterNodePlayerRoom` | C→S client→zone | implémenté `EnterNodePlayerRoom` | `ZoneMessages.cs` |
| 196 | `CNE_CZ_ZoneServerGladiatorTeamCreate` | C→S client→zone | inconnu | — |
| 197 | `CNE_CZ_ZoneServerGladiatorTeamQuery` | C→S client→zone | inconnu | — |
| 198 | `CNE_CZ_ZoneServerColosseumJoinAnswer` | C→S client→zone | inconnu | — |
| 199 | `CNE_CZ_ZoneServerColosseumJoinRequest` | C→S client→zone | inconnu | — |
| 200 | `CNE_CZ_ZoneServerColosseumTeamQuery` | C→S client→zone | inconnu | — |
| 201 | `CNE_CZ_ZoneServerColosseumLeaveJoin` | C→S client→zone | inconnu | — |
| 202 | `CNE_CZ_ZoneServerCoRideInvite` | C→S client→zone | inconnu | — |
| 203 | `CNE_CZ_ZoneServerDestinyDice` | C→S client→zone | inconnu | — |
| 204 | `CNE_CZ_ZoneServerQueryDestinyRoad` | C→S client→zone | implémenté `QueryDestinyRoad` | `ZoneMessages.cs` |
| 205 | `CNE_CZ_ZoneServerSearchRoom` | C→S client→zone | inconnu | — |
| 206 | `CNE_CZ_ZoneServerRentRoom` | C→S client→zone | inconnu | — |
| 207 | `CNE_CZ_ZoneServerEnterRoom` | C→S client→zone | inconnu | — |
| 208 | `CNE_CZ_ZoneServerSetRoomPrivilege` | C→S client→zone | inconnu | — |
| 209 | `CNE_CZ_ZoneServerSwitchSetRoomMode` | C→S client→zone | inconnu | — |
| 210 | `CNE_CZ_ZoneServerRoomGetData` | C→S client→zone | inconnu | — |
| 211 | `CNE_CZ_ZoneServerUpdateDecoration` | C→S client→zone | inconnu | — |
| 212 | `CNE_CZ_ZoneServerPayRent` | C→S client→zone | inconnu | — |
| 213 | `CNE_CZ_ZoneServerQueryLifeSkill` | C→S client→zone | implémenté `QueryLifeSkill` | `ZoneMessages.cs` |
| 214 | `CNE_CZ_ZoneServerLifeSkill` | C→S client→zone | inconnu | — |
| 215 | `CNE_CZ_ZoneServerChangeRoomTexture` | C→S client→zone | inconnu | — |
| 216 | `CNE_CZ_ZoneServerStartParty` | C→S client→zone | inconnu | — |
| 217 | `CNE_CZ_ZoneServerExtendRoom` | C→S client→zone | inconnu | — |
| 218 | `CNE_CZ_ZoneServerNewPlayerGuide` | C→S client→zone | implémenté `ZoneNewPlayerGuide` | `ZoneMessages.cs` |
| 219 | `CNE_CZ_ZoneServerColosseumModeQuery` | C→S client→zone | inconnu | — |
| 220 | `CNE_CZ_ZoneServerRecruitInstance` | C→S client→zone | inconnu | — |
| 221 | `CNE_CZ_ZoneServerChanceLuckyBagGetReward` | C→S client→zone | inconnu | — |
| 222 | `CNE_CZ_ZoneServerChanceLuckyBagReTry` | C→S client→zone | inconnu | — |
| 223 | `CNE_CZ_ZoneServerQueryFortuneBagCounter` | C→S client→zone | implémenté `QueryFortuneBagCounter` | `ZoneMessages.cs` |
| 224 | `CNE_CZ_ZoneServerUpdateLotteryBonus` | C→S client→zone | inconnu | — |
| 225 | `CNE_CZ_ZoneServerFamilyBattleAskInfo` | C→S client→zone | inconnu | — |
| 226 | `CNE_CZ_ZoneServerSetRecommendActivityTracing` | C→S client→zone | inconnu | — |
| 227 | `CNE_CZ_ZoneServerElfMix` | C→S client→zone | inconnu | — |
| 228 | `CNE_CZ_ZoneServerElfChangeName` | C→S client→zone | inconnu | — |
| 229 | `CNE_CZ_ZoneServerElfRevive` | C→S client→zone | inconnu | — |
| 230 | `CNE_CZ_ZoneServerEquipTrainSetItem` | C→S client→zone | inconnu | — |
| 231 | `CNE_CZ_ZoneServerPVPRankLookEquip` | C→S client→zone | inconnu | — |
| 232 | `CNE_CZ_ZoneServerVerifyCubeCosts` | C→S client→zone | inconnu | — |
| 233 | `CNE_CZ_ZoneServerGreedyIslandJoin` | C→S client→zone | inconnu | — |
| 234 | `CNE_CZ_ZoneServerGreedyIslandRankQuery` | C→S client→zone | inconnu | — |
| 235 | `CNE_CZ_ZoneServerSearchTeamV2` | C→S client→zone | inconnu | — |
| 236 | `CNE_CZ_ZoneServerTeamRecruitV2` | C→S client→zone | inconnu | — |
| 237 | `CNE_CZ_ZoneServerTeamRecruitTransport` | C→S client→zone | inconnu | — |
| 238 | `CNE_CZ_ZoneServerChangePositionV2` | C→S client→zone | inconnu | — |
| 239 | `CNE_CZ_ZoneServerFamilyTalkToNpc` | C→S client→zone | inconnu | — |
| 240 | `CNE_CZ_ZoneServerFixEquipByFamily` | C→S client→zone | inconnu | — |
| 241 | `CNE_CZ_ZoneServerBattleRoyaleJoinRequest` | C→S client→zone | inconnu | — |
| 242 | `CNE_CZ_ZoneServerPlayerCCDataSave` | C→S client→zone | inconnu | — |
| 243 | `CNE_CZ_ZoneServerEnterNodeFastDress` | C→S client→zone | implémenté `EnterNodeFastDress` | `ZoneMessages.cs` |
| 244 | `CNE_CZ_ZoneServerApplyFastDressData` | C→S client→zone | inconnu | — |
| 245 | `CNE_CZ_ZoneServerSaveFastDressData` | C→S client→zone | inconnu | — |
| 246 | `CNE_CZ_ZoneServerRecordFastDressInfo` | C→S client→zone | inconnu | — |
| 247 | `CNE_CZ_ZoneServerUpdateFastDressPage` | C→S client→zone | inconnu | — |
| 248 | `CNE_CZ_ZoneServerRemoveFastDressInfo` | C→S client→zone | inconnu | — |
| 249 | `CNE_CZ_ZoneServerMarryOperation` | C→S client→zone | inconnu | — |
| 250 | `CNE_CZ_ZoneServerChurchReserve` | C→S client→zone | inconnu | — |
| 251 | `CNE_CZ_ZoneServerWeddingInfoQuery` | C→S client→zone | inconnu | — |
| 252 | `CNE_CZ_ZoneServerVersusInstanceJoinRequest` | C→S client→zone | inconnu | — |
| 253 | `CNE_CZ_ZoneServerPayEventSelect` | C→S client→zone | inconnu | — |
| 254 | `CNE_CZ_ZoneServerPayEventResponse` | C→S client→zone | inconnu | — |
| 255 | `CNE_CZ_ZoneServerEvaluateLotteryOpen` | C→S client→zone | inconnu | — |
| 256 | `CNE_CZ_ZoneServerEnterNodeStarWay` | C→S client→zone | implémenté `EnterNodeStarWay` | `ZoneMessages.cs` |
| 257 | `CNE_CZ_ZoneServerExecuteMagicBox` | C→S client→zone | inconnu | — |
| 258 | `CNE_CZ_ZoneServerDailyAwardOP` | C→S client→zone | implémenté `ZoneDailyAwardOp` | `ZoneMessages.cs` |
| 259 | `CNE_CZ_ZoneServerBattleCrusadeJoin` | C→S client→zone | inconnu | — |
| 260 | `CNE_CZ_ZoneServerArrangeItem` | C→S client→zone | inconnu | — |
| 261 | `CNC_CZ_ClientElfCastEnchant` | S→C zone→client | inconnu | — |
| 262 | `CNC_CZ_ClientTicketInvalid` | S→C zone→client | décodé (desasm) | DESASM |
| 263 | `CNC_CZ_ClientDisplayChar` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 264 | `CNC_CZ_ClientMoveChar` | S→C zone→client | implémenté `ZoneMoveChar` | `ZoneEntityMessages.cs` |
| 265 | `CNC_CZ_ClientSeeChar` | S→C zone→client | implémenté `ZoneSeeChar` | `ZoneEntityMessages.cs` |
| 266 | `CNC_CZ_ClientTextOutput` | S→C zone→client | implémenté `ZoneClientTextOutput` | `ZoneHudMessages.cs` |
| 267 | `CNC_CZ_ClientGoto` | S→C zone→client | implémenté `ZoneClientGoto` | `ZoneEntityMessages.cs` |
| 268 | `CNC_CZ_ClientSay` | S→C zone→client | implémenté `ZoneClientSay` | `ZoneHudMessages.cs` |
| 269 | `CNC_CZ_ClientLifeEradicate` | S→C zone→client | implémenté `ZoneLifeEradicate` | `ZoneEntityMessages.cs` |
| 270 | `CNC_CZ_ClientResetCoord` | S→C zone→client | décodé (desasm) | DESASM |
| 271 | `CNC_CZ_ClientDisplayMonster` | S→C zone→client | implémenté `ZoneDisplayMonster` | `ZoneEntityMessages.cs` |
| 272 | `CNC_CZ_ClientMeleeAttack` | S→C zone→client | décodé (golden) | GOLDEN |
| 273 | `CNC_CZ_ClientRestore` | S→C zone→client | décodé (desasm) | DESASM |
| 274 | `CNC_CZ_ClientPutItemIntoInventory` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 275 | `CNC_CZ_ClientDropItem` | S→C zone→client | décodé (desasm) | DESASM |
| 276 | `CNC_CZ_ClientChangeEquip` | S→C zone→client | implémenté `ZoneChangeEquip` | `ZoneEquipMessages.cs` |
| 277 | `CNC_CZ_ClientSwitchItemLocationOK` | S→C zone→client | décodé (golden) | GOLDEN |
| 278 | `CNC_CZ_ClientSpellCast` | S→C zone→client | décodé (desasm) | DESASM |
| 279 | `CNC_CZ_ClientSpellCastOK` | S→C zone→client | décodé (golden) | GOLDEN |
| 280 | `CNC_CZ_ClientUpdateAttr` | S→C zone→client | implémenté `ZoneUpdateAttr` | `ZoneEntityMessages.cs` |
| 281 | `CNC_CZ_ClientSpellHitToLife` | S→C zone→client | décodé (golden) | GOLDEN |
| 282 | `CNC_CZ_ClientUpdateEquipment` | S→C zone→client | implémenté `ZoneUpdateEquipmentView` | `ZoneEquipMessages.cs` |
| 283 | `CNC_CZ_ClientModelModify` | S→C zone→client | implémenté `ZoneModelModify` | `ZoneEntityMessages.cs` |
| 284 | `CNC_CZ_ClientLevelUp` | S→C zone→client | décodé (golden) | GOLDEN |
| 285 | `CNC_CZ_ClientDisplayDialogView` | S→C zone→client | implémenté `ZoneDisplayDialogView` | `ZoneDialogueMessages.cs` |
| 286 | `CNC_CZ_ClientGetSpell` | S→C zone→client | décodé (golden) | GOLDEN |
| 287 | `CNC_CZ_ClientSpellBreak` | S→C zone→client | décodé (desasm) | DESASM |
| 288 | `CNC_CZ_ClientSpellBlastOK` | S→C zone→client | décodé (golden) | GOLDEN |
| 289 | `CNC_CZ_ClientSpellBlastToLife` | S→C zone→client | décodé (desasm) | DESASM |
| 290 | `CNC_CZ_ClientPlayerAction` | S→C zone→client | décodé (desasm) | DESASM |
| 291 | `CNC_CZ_ClientModifyItemDurability` | S→C zone→client | décodé (desasm) | DESASM |
| 292 | `CNC_CZ_ClientEffectReport` | S→C zone→client | décodé (golden) | GOLDEN |
| 293 | `CNC_CZ_ClientNodeExit` | S→C zone→client | implémenté `ZoneNodeExit` | `ZoneEntityMessages.cs` |
| 294 | `CNC_CZ_ClientLeaveNode` | S→C zone→client | implémenté `ZoneLeaveNode` | `ZoneEntityMessages.cs` |
| 295 | `CNC_CZ_ClientDestShutdown` | S→C zone→client | inconnu | — |
| 296 | `CNC_CZ_ClientAttrRefresh` | S→C zone→client | implémenté `ZoneAttrRefresh` | `ZoneEntityMessages.cs` |
| 297 | `CNC_CZ_ClientSpellBlastToLocation` | S→C zone→client | décodé (desasm) | DESASM |
| 298 | `CNC_CZ_ClientSeeMob` | S→C zone→client | implémenté `ZoneSeeMob` | `ZoneEntityMessages.cs` |
| 299 | `CNC_CZ_ClientEnchantAdd` | S→C zone→client | décodé (golden) | GOLDEN |
| 300 | `CNC_CZ_ClientEnchantRemove` | S→C zone→client | décodé (golden) | GOLDEN |
| 301 | `CNC_CZ_ClientEnchantUpdate` | S→C zone→client | décodé (golden) | GOLDEN |
| 302 | `CNC_CZ_ClientFlagModify` | S→C zone→client | implémenté `ZoneFlagModify` | `ZoneMessages.cs` |
| 303 | `CNC_CZ_ClientItemUse` | S→C zone→client | décodé (golden) | GOLDEN |
| 304 | `CNC_CZ_ClientEnterShop` | S→C zone→client | implémenté `ZoneEnterShop` | `ZoneShopMessages.cs` |
| 305 | `CNC_CZ_ClientDie` | S→C zone→client | implémenté `ZoneDie` | `ZoneVitalsMessages.cs` |
| 306 | `CNC_CZ_ClientRevive` | S→C zone→client | implémenté `ZoneRevive` | `ZoneVitalsMessages.cs` |
| 307 | `CNC_CZ_ClientPlayerEmotion` | S→C zone→client | décodé (desasm) | DESASM |
| 308 | `CNC_CZ_ClientBuyUp` | S→C zone→client | implémenté `ZoneBuyUp` | `ZoneShopMessages.cs` |
| 309 | `CNC_CZ_ClientSellOut` | S→C zone→client | décodé (desasm) | DESASM |
| 310 | `CNC_CZ_ClientEnterNodePlayerData` | S→C zone→client | implémenté `ZoneEnterNodePlayerData` | `ZoneEntityMessages.cs` |
| 311 | `CNC_CZ_ClientEnterNodeEquipAndSpell` | S→C zone→client | implémenté `ZoneEnterNodeEquipAndSpell` | `ZoneEntityMessages.cs` |
| 312 | `CNC_CZ_ClientEnterNodeInventory` | S→C zone→client | implémenté `ZoneEnterNodeInventory` | `ZoneEntityMessages.cs` |
| 313 | `CNC_CZ_ClientEnterNodeNodeData` | S→C zone→client | implémenté `ZoneEnterNodeNodeData` | `ZoneMessages.cs` |
| 314 | `CNC_CZ_ClientCanCloseConnection` | S→C zone→client | inconnu | — |
| 315 | `CNC_CZ_ClientEnchantReset` | S→C zone→client | inconnu | — |
| 316 | `CNC_CZ_ClientTextOutputIndex` | S→C zone→client | implémenté `ZoneClientTextOutputIndex` | `ZoneHudMessages.cs` |
| 317 | `CNC_CZ_ClientTeamLeaved` | S→C zone→client | décodé (desasm) | DESASM |
| 318 | `CNC_CZ_ClientTeamInviteNotice` | S→C zone→client | décodé (desasm) | DESASM |
| 319 | `CNC_CZ_ClientTeamInviteResult` | S→C zone→client | inconnu | — |
| 320 | `CNC_CZ_ClientSafeQuitCancel` | S→C zone→client | décodé (desasm) | DESASM |
| 321 | `CNC_CZ_ClientSafeQuitResp` | S→C zone→client | décodé (desasm) | DESASM |
| 322 | `CNC_CZ_ClientNonSafeQuit` | S→C zone→client | inconnu | — |
| 323 | `CNC_CZ_ClientDisplayCharSimple` | S→C zone→client | inconnu | — |
| 324 | `CNC_CZ_ClientTeamAllMembersInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 325 | `CNC_CZ_ClientTeamNewMemberInfo` | S→C zone→client | inconnu | — |
| 326 | `CNC_CZ_ClientMovePrediction` | S→C zone→client | implémenté `ZoneMovePrediction` | `ZoneEntityMessages.cs` |
| 327 | `CNC_CZ_ClientChannelMsg` | S→C zone→client | implémenté `ZoneClientChannelMsg` | `ZoneHudMessages.cs` |
| 328 | `CNC_CZ_ClientModifyAttr` | S→C zone→client | implémenté `ZoneModifyAttr` | `ZoneVitalsMessages.cs` |
| 329 | `CNC_CZ_ClientSeeTreasure` | S→C zone→client | implémenté `ZoneSeeTreasure` | `ZoneEntityMessages.cs` |
| 330 | `CNC_CZ_ClientTreasureDisappear` | S→C zone→client | implémenté `ZoneTreasureDisappear` | `ZoneEntityMessages.cs` |
| 331 | `CNC_CZ_ClientTradeRequest` | S→C zone→client | décodé (desasm) | DESASM |
| 332 | `CNC_CZ_ClientTradeStart` | S→C zone→client | décodé (desasm) | DESASM |
| 333 | `CNC_CZ_ClientTradeAddItem` | S→C zone→client | décodé (desasm) | DESASM |
| 334 | `CNC_CZ_ClientTradeRemoveItem` | S→C zone→client | décodé (desasm) | DESASM |
| 335 | `CNC_CZ_ClientTradeAddItemFail` | S→C zone→client | décodé (desasm) | DESASM |
| 336 | `CNC_CZ_ClientTradeModifyGold` | S→C zone→client | décodé (desasm) | DESASM |
| 337 | `CNC_CZ_ClientTradeModifyGoldFail` | S→C zone→client | décodé (desasm) | DESASM |
| 338 | `CNC_CZ_ClientTradeConfirm` | S→C zone→client | décodé (desasm) | DESASM |
| 339 | `CNC_CZ_ClientTradeCancel` | S→C zone→client | décodé (desasm) | DESASM |
| 340 | `CNC_CZ_ClientTradeComplete` | S→C zone→client | inconnu | — |
| 341 | `CNC_CZ_ClientResurrect` | S→C zone→client | décodé (desasm) | DESASM |
| 342 | `CNC_CZ_ClientAskRepairEquipGold` | S→C zone→client | décodé (desasm) | DESASM |
| 343 | `CNC_CZ_ClientAskRepairOneItemGold` | S→C zone→client | décodé (desasm) | DESASM |
| 344 | `CNC_CZ_ClientUpdateSystemVar` | S→C zone→client | décodé (golden) | GOLDEN |
| 345 | `CNC_CZ_ClientUpdateNPCSymbols` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 346 | `CNC_CZ_ClientChangeFamily` | S→C zone→client | décodé (desasm) | DESASM |
| 347 | `CNC_CZ_ClientEnterFamilyCreation` | S→C zone→client | décodé (desasm) | DESASM |
| 348 | `CNC_CZ_ClientMissionStates` | S→C zone→client | implémenté `ZoneMissionStates` | `ZoneHudMessages.cs` |
| 349 | `CNC_CZ_ClientEnterSpellShop` | S→C zone→client | décodé (desasm) | DESASM |
| 350 | `CNC_CZ_ClientEnterRepairShop` | S→C zone→client | décodé (desasm) | DESASM |
| 351 | `CNC_CZ_ClientEnterNodeShortcut` | S→C zone→client | décodé (golden) | GOLDEN |
| 352 | `CNC_CZ_ClientReceiveShortcutOfPage` | S→C zone→client | décodé (desasm) | DESASM |
| 353 | `CNC_CZ_ClientTradeReset` | S→C zone→client | décodé (desasm) | DESASM |
| 354 | `CNC_CZ_ClientUseSkillPointOK` | S→C zone→client | inconnu | — |
| 355 | `CNC_CZ_ClientLogoutResp` | S→C zone→client | décodé (desasm) | DESASM |
| 356 | `CNC_CZ_ClientCanLogout` | S→C zone→client | décodé (desasm) | DESASM |
| 357 | `CNC_CZ_ClientEnterStorage` | S→C zone→client | implémenté `ZoneEnterStorage` | `ZoneShopMessages.cs` |
| 358 | `CNC_CZ_ClientGetStorageData` | S→C zone→client | implémenté `ZoneClientGetStorageData` | `ZoneShopMessages.cs` |
| 359 | `CNC_CZ_ClientPutItemIntoStorage` | S→C zone→client | inconnu | — |
| 360 | `CNC_CZ_ClientPutItemIntoContainer` | S→C zone→client | inconnu | — |
| 361 | `CNC_CZ_ClientSpellLoopCastStop` | S→C zone→client | inconnu | — |
| 362 | `CNC_CZ_ClientQueryNpc` | S→C zone→client | inconnu | — |
| 363 | `CNC_CZ_ClientAskIdentifyGold` | S→C zone→client | décodé (desasm) | DESASM |
| 364 | `CNC_CZ_ClientIdentifyItem` | S→C zone→client | décodé (desasm) | DESASM |
| 365 | `CNC_CZ_ClientEnterIdentifyShop` | S→C zone→client | décodé (desasm) | DESASM |
| 366 | `CNC_CZ_ClientEnterExchageItemService` | S→C zone→client | décodé (desasm) | DESASM |
| 367 | `CNC_CZ_ClientResetSpell` | S→C zone→client | inconnu | — |
| 368 | `CNC_CZ_ClientUpdateCoolDown` | S→C zone→client | décodé (golden) | GOLDEN |
| 369 | `CNC_CZ_ClientModifyItemInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 370 | `CNC_CZ_ClientEnterInlayShop` | S→C zone→client | décodé (desasm) | DESASM |
| 371 | `CNC_CZ_ClientInlayItemResult` | S→C zone→client | décodé (desasm) | DESASM |
| 372 | `CNC_CZ_ClientTeamMemberEnchant` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 373 | `CNC_CZ_ClientStorageExpandResult` | S→C zone→client | décodé (desasm) | DESASM |
| 374 | `CNC_CZ_ClientSloganUpdate` | S→C zone→client | inconnu | — |
| 375 | `CNC_CZ_ClientVendorClose` | S→C zone→client | décodé (desasm) | DESASM |
| 376 | `CNC_CZ_ClientVendorCloseView` | S→C zone→client | décodé (desasm) | DESASM |
| 377 | `CNC_CZ_ClientVendorStatus` | S→C zone→client | décodé (desasm) | DESASM |
| 378 | `CNC_CZ_ClientVendorUpdateView` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 379 | `CNC_CZ_ClientVendorContainer` | S→C zone→client | inconnu | — |
| 380 | `CNC_CZ_ClientMobTransform` | S→C zone→client | implémenté `ZoneMobTransform` | `ZoneEntityMessages.cs` |
| 381 | `CNC_CZ_ClientTeamInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 382 | `CNC_CZ_ClientMigrationDone` | S→C zone→client | décodé (golden) | GOLDEN |
| 383 | `CNC_CZ_ClientPlayEffect` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 384 | `CNC_CZ_ClientSelectFamilyEmblem` | S→C zone→client | décodé (desasm) | DESASM |
| 385 | `CNC_CZ_ClientOpenExploitRank` | S→C zone→client | décodé (desasm) | DESASM |
| 386 | `CNC_CZ_ClientTreasurePickupOK` | S→C zone→client | décodé (desasm) | DESASM |
| 387 | `CNC_CZ_ClientTeamRadarAttention` | S→C zone→client | décodé (desasm) | DESASM |
| 388 | `CNC_CZ_ClientEnterMissionList` | S→C zone→client | implémenté `ZoneEnterMissionList` | `ZoneHudMessages.cs` |
| 389 | `CNC_CZ_ClientBagDueDate` | S→C zone→client | décodé (desasm) | DESASM |
| 390 | `CNC_CZ_ClientUpdateInteraction` | S→C zone→client | inconnu | — |
| 391 | `CNC_CZ_ClientUpdateMissionState` | S→C zone→client | implémenté `ZoneUpdateMissionState` | `ZoneHudMessages.cs` |
| 392 | `CNC_CZ_ClientTeamMemberPosition` | S→C zone→client | décodé (desasm) | DESASM |
| 393 | `CNC_CZ_ClientEnterTransport` | S→C zone→client | implémenté `ZoneEnterTransport` | `ZoneShopMessages.cs` |
| 394 | `CNC_CZ_ClientTradeReady` | S→C zone→client | décodé (desasm) | DESASM |
| 395 | `CNC_CZ_ClientTradeUnConfirm` | S→C zone→client | décodé (desasm) | DESASM |
| 396 | `CNC_CZ_ClientChangeAppellation` | S→C zone→client | décodé (desasm) | DESASM |
| 397 | `CNC_CZ_ClientUpdateAppellation` | S→C zone→client | décodé (desasm) | DESASM |
| 398 | `CNC_CZ_ClientAddAppellation` | S→C zone→client | décodé (desasm) | DESASM |
| 399 | `CNC_CZ_ClientMailOpenBox` | S→C zone→client | décodé (desasm) | DESASM |
| 400 | `CNC_CZ_ClientMailList` | S→C zone→client | décodé (desasm) | DESASM |
| 401 | `CNC_CZ_ClientMailRead` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 402 | `CNC_CZ_ClientMailDeleteResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 403 | `CNC_CZ_ClientMailSendResult` | S→C zone→client | décodé (desasm) | DESASM |
| 404 | `CNC_CZ_ClientMailReceive` | S→C zone→client | implémenté `ZoneMailReceive` | `ZoneMessages.cs` |
| 405 | `CNC_CZ_ClientMailGetItemOK` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 406 | `CNC_CZ_ClientMailGetGoldOK` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 407 | `CNC_CZ_ClientUseItemTime` | S→C zone→client | inconnu | — |
| 408 | `CNC_CZ_ClientUseItemTimeBreak` | S→C zone→client | inconnu | — |
| 409 | `CNC_CZ_ClientMailReturnResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 410 | `CNC_CZ_ClientCommandFailReturn` | S→C zone→client | décodé (desasm) | DESASM |
| 411 | `CNC_CZ_ClientSearchInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 412 | `CNC_CZ_ClientEnterNodeElfInfo` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 413 | `CNC_CZ_ClientElfAdd` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 414 | `CNC_CZ_ClientElfReturnOK` | S→C zone→client | décodé (desasm) | DESASM |
| 415 | `CNC_CZ_ClientElfWorkResult` | S→C zone→client | inconnu | — |
| 416 | `CNC_CZ_ClientElfWorkResultEnd` | S→C zone→client | décodé (desasm) | DESASM |
| 417 | `CNC_CZ_ClientElfEvent` | S→C zone→client | inconnu | — |
| 418 | `CNC_CZ_ClientElfUpdateAttr` | S→C zone→client | décodé (desasm) | DESASM |
| 419 | `CNC_CZ_ClientElfUpdateSkill` | S→C zone→client | inconnu | — |
| 420 | `CNC_CZ_ClientElfUpdateSoulAbility` | S→C zone→client | inconnu | — |
| 421 | `CNC_CZ_ClientElfGoToWork` | S→C zone→client | inconnu | — |
| 422 | `CNC_CZ_ClientElfDecompose` | S→C zone→client | inconnu | — |
| 423 | `CNC_CZ_ClientElfStoneUse` | S→C zone→client | inconnu | — |
| 424 | `CNC_CZ_ClientEnterNodeElfContainer` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 425 | `CNC_CZ_ClientAuctionBuyList` | S→C zone→client | décodé (desasm) | DESASM |
| 426 | `CNC_CZ_ClientAuctionBuyOK` | S→C zone→client | décodé (desasm) | DESASM |
| 427 | `CNC_CZ_ClientAuctionCancelOK` | S→C zone→client | décodé (desasm) | DESASM |
| 428 | `CNC_CZ_ClientAuctionSellList` | S→C zone→client | décodé (desasm) | DESASM |
| 429 | `CNC_CZ_ClientAuctionSellResult` | S→C zone→client | décodé (desasm) | DESASM |
| 430 | `CNC_CZ_ClientAuctionHouse` | S→C zone→client | décodé (desasm) | DESASM |
| 431 | `CNC_CZ_ClientChangeElf` | S→C zone→client | décodé (desasm) | DESASM |
| 432 | `CNC_CZ_ClientEnterStrengthen` | S→C zone→client | décodé (desasm) | DESASM |
| 433 | `CNC_CZ_ClientStrengthenCasting` | S→C zone→client | décodé (desasm) | DESASM |
| 434 | `CNC_CZ_ClientStrengthenItemCancel` | S→C zone→client | décodé (desasm) | DESASM |
| 435 | `CNC_CZ_ClientStrengthenItemResult` | S→C zone→client | décodé (desasm) | DESASM |
| 436 | `CNC_CZ_ClientModifyItemStrengthen` | S→C zone→client | décodé (desasm) | DESASM |
| 437 | `CNC_CZ_ClientSetSpellStone` | S→C zone→client | inconnu | — |
| 438 | `CNC_CZ_ClientSummonPet` | S→C zone→client | décodé (desasm) | DESASM |
| 439 | `CNC_CZ_ClientEnterElfReturn` | S→C zone→client | décodé (desasm) | DESASM |
| 440 | `CNC_CZ_ClientMobChangeAI` | S→C zone→client | inconnu | — |
| 441 | `CNC_CZ_ClientTurnChar` | S→C zone→client | décodé (desasm) | DESASM |
| 442 | `CNC_CZ_ClientElfAction` | S→C zone→client | inconnu | — |
| 443 | `CNC_CZ_ClientElfReturnEffect` | S→C zone→client | inconnu | — |
| 444 | `CNC_CZ_ClientGetSelectLifeTarget` | S→C zone→client | décodé (golden) | GOLDEN |
| 445 | `CNC_CZ_ClientEnchantPause` | S→C zone→client | décodé (desasm) | DESASM |
| 446 | `CNC_CZ_ClientEnchantStart` | S→C zone→client | décodé (desasm) | DESASM |
| 447 | `CNC_CZ_ClientPVPTime` | S→C zone→client | inconnu | — |
| 448 | `CNC_CZ_ClientLotteryResult` | S→C zone→client | décodé (desasm) | DESASM |
| 449 | `CNC_CZ_ClientLotteryEnd` | S→C zone→client | décodé (desasm) | DESASM |
| 450 | `CNC_CZ_ClientLotteryUpdatePlus` | S→C zone→client | décodé (golden) | GOLDEN |
| 451 | `CNC_CZ_ClientDuelRequest` | S→C zone→client | décodé (desasm) | DESASM |
| 452 | `CNC_CZ_ClientDuelRequestResult` | S→C zone→client | décodé (desasm) | DESASM |
| 453 | `CNC_CZ_ClientDuelBegin` | S→C zone→client | décodé (desasm) | DESASM |
| 454 | `CNC_CZ_ClientDuelResult` | S→C zone→client | décodé (desasm) | DESASM |
| 455 | `CNC_CZ_ClientEnterMessageBoard` | S→C zone→client | décodé (desasm) | DESASM |
| 456 | `CNC_CZ_ClientMessageList` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 457 | `CNC_CZ_ClientMessageWriteResult` | S→C zone→client | décodé (desasm) | DESASM |
| 458 | `CNC_CZ_ClientMessageRead` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 459 | `CNC_CZ_ClientMessageDeleteResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 460 | `CNC_CZ_ClientLookEquip` | S→C zone→client | décodé (desasm) | DESASM |
| 461 | `CNC_CZ_ClientItemRent` | S→C zone→client | décodé (desasm) | DESASM |
| 462 | `CNC_CZ_ClientBattlefieldList` | S→C zone→client | décodé (desasm) | DESASM |
| 463 | `CNC_CZ_ClientEnterBattlefield` | S→C zone→client | inconnu | — |
| 464 | `CNC_CZ_ClientBattlefieldJoinResult` | S→C zone→client | décodé (desasm) | DESASM |
| 465 | `CNC_CZ_ClientBattlefieldResult` | S→C zone→client | inconnu | — |
| 466 | `CNC_CZ_ClientBattlefieldUpdateScore` | S→C zone→client | inconnu | — |
| 467 | `CNC_CZ_ClientAltarState` | S→C zone→client | inconnu | — |
| 468 | `CNC_CZ_ClientBattleMemberPosition` | S→C zone→client | inconnu | — |
| 469 | `CNC_CZ_ClientBattlefieldMemberInfo` | S→C zone→client | inconnu | — |
| 470 | `CNC_CZ_ClientPlayElfPetAction` | S→C zone→client | décodé (desasm) | DESASM |
| 471 | `CNC_CZ_ClientUpdateAdvancedAbility` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 472 | `CNC_CZ_ClientUpdateCollection` | S→C zone→client | décodé (golden) | GOLDEN |
| 473 | `CNC_CZ_ClientHello` | S→C zone→client | implémenté `ZoneClientHello` | `ZoneMessages.cs` |
| 474 | `CNC_CZ_ClientScreenMessage` | S→C zone→client | implémenté `ZoneClientScreenMessage` | `ZoneHudMessages.cs` |
| 475 | `CNC_CZ_ClientLoverInvite` | S→C zone→client | inconnu | — |
| 476 | `CNC_CZ_ClientLoverUpdate` | S→C zone→client | inconnu | — |
| 477 | `CNC_CZ_ClientLoverInfo` | S→C zone→client | inconnu | — |
| 478 | `CNC_CZ_ClientLoverBreak` | S→C zone→client | inconnu | — |
| 479 | `CNC_CZ_ClientLoverPosition` | S→C zone→client | inconnu | — |
| 480 | `CNC_CZ_ClientAchievementUpdate` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 481 | `CNC_CZ_ClientTextOutputItem` | S→C zone→client | inconnu | — |
| 482 | `CNC_CZ_ClientUpdateBFData` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 483 | `CNC_CZ_ClientEnterFamilyBuilding` | S→C zone→client | décodé (desasm) | DESASM |
| 484 | `CNC_CZ_ClientFamilyContainerData` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 485 | `CNC_CZ_ClientFamilyContainerDeleteItem` | S→C zone→client | inconnu | — |
| 486 | `CNC_CZ_ClientFamilyContainerPutItem` | S→C zone→client | inconnu | — |
| 487 | `CNC_CZ_ClientFamilyInstanceData` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 488 | `CNC_CZ_ClientPlayerClosets` | S→C zone→client | décodé (golden) | GOLDEN |
| 489 | `CNC_CZ_ClientPlayerClassList` | S→C zone→client | décodé (golden) | GOLDEN |
| 490 | `CNC_CZ_ClientEnterNodePlayerCard` | S→C zone→client | décodé (golden) | GOLDEN |
| 491 | `CNC_CZ_ClientQueryPlayerCard` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 492 | `CNC_CZ_ClientBaseAttrRefresh` | S→C zone→client | implémenté `ZoneBaseAttrRefresh` | `ZoneVitalsMessages.cs` |
| 493 | `CNC_CZ_ClientAttrLevelRefresh` | S→C zone→client | implémenté `ZoneAttrLevelRefresh` | `ZoneVitalsMessages.cs` |
| 494 | `CNC_CZ_ClientPlaySound` | S→C zone→client | inconnu | — |
| 495 | `CNC_CZ_ClientPlayMonsterAction` | S→C zone→client | décodé (desasm) | DESASM |
| 496 | `CNC_CZ_ClientEnterWorks` | S→C zone→client | décodé (desasm) | DESASM |
| 497 | `CNC_CZ_ClientPlayerWorkUpdate` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 498 | `CNC_CZ_ClientPlayerWorkResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 499 | `CNC_CZ_ClientPlayerEvent` | S→C zone→client | inconnu | — |
| 500 | `CNC_CZ_ClientPlayerWorkResultEnd` | S→C zone→client | décodé (desasm) | DESASM |
| 501 | `CNC_CZ_ClientEnterFamilyInstanceCreation` | S→C zone→client | décodé (desasm) | DESASM |
| 502 | `CNC_CZ_ClientFamilyTownQuery` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 503 | `CNC_CZ_ClientFamilyTownMemberQuery` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 504 | `CNC_CZ_ClientFamilyStorageDeleteItem` | S→C zone→client | décodé (desasm) | DESASM |
| 505 | `CNC_CZ_ClientFamilyStoragePutItem` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 506 | `CNC_CZ_ClientFamilyStorageLog` | S→C zone→client | décodé (desasm) | DESASM |
| 507 | `CNC_CZ_ClientEnterFamilyStorage` | S→C zone→client | décodé (desasm) | DESASM |
| 508 | `CNC_CZ_ClientFamilyStorageData` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 509 | `CNC_CZ_ClientPlayerLicense` | S→C zone→client | décodé (golden) | GOLDEN |
| 510 | `CNC_CZ_ClientChangeClassShortcut` | S→C zone→client | décodé (golden) | GOLDEN |
| 511 | `CNC_CZ_ClientUpdateLottery` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 512 | `CNC_CZ_ClientBuyBackList` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 513 | `CNC_CZ_ClientPetShortCut` | S→C zone→client | décodé (desasm) | DESASM |
| 514 | `CNC_CZ_ClientEnterFamilyTownList` | S→C zone→client | décodé (desasm) | DESASM |
| 515 | `CNC_CZ_ClientTerritoryQuery` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 516 | `CNC_CZ_ClientTerritoryInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 517 | `CNC_CZ_ClientCanJoinTeam` | S→C zone→client | inconnu | — |
| 518 | `CNC_CZ_ClientSearchTeamInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 519 | `CNC_CZ_ClientTeamRecruitMsg` | S→C zone→client | décodé (golden) | GOLDEN |
| 520 | `CNC_CZ_ClientRollTreasure` | S→C zone→client | décodé (desasm) | DESASM |
| 521 | `CNC_CZ_ClientRankInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 522 | `CNC_CZ_ClientHateReport` | S→C zone→client | inconnu | — |
| 523 | `CNC_CZ_ClientEnterNodePlayerRoom` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 524 | `CNC_CZ_ClientEnterGladiatorTeamCreation` | S→C zone→client | décodé (desasm) | DESASM |
| 525 | `CNC_CZ_ClientGladiatorTeamInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 526 | `CNC_CZ_ClientDisbandGladiatorTeam` | S→C zone→client | inconnu | — |
| 527 | `CNC_CZ_ClientEnterColosseum` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 528 | `CNC_CZ_ClientColosseumJoinResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 529 | `CNC_CZ_ClientColosseumTeamInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 530 | `CNC_CZ_ClientColosseumLeaveJoinResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 531 | `CNC_CZ_ClientReviveInfo` | S→C zone→client | décodé (golden) | GOLDEN |
| 532 | `CNC_CZ_ClientTerritoryKilledInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 533 | `CNC_CZ_ClientCoRideInvite` | S→C zone→client | inconnu | — |
| 534 | `CNC_CZ_ClientPlayerDestinyRoad` | S→C zone→client | décodé (desasm) | DESASM |
| 535 | `CNC_CZ_ClientPlayerDestinyRoadEvent` | S→C zone→client | décodé (desasm) | DESASM |
| 536 | `CNC_CZ_ClientUpdateZoneMonster` | S→C zone→client | implémenté `ZoneUpdateZoneMonster` | `ZoneEntityMessages.cs` |
| 537 | `CNC_CZ_ClientSearchRoomResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 538 | `CNC_CZ_ClientUpdateRoom` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 539 | `CNC_CZ_ClientOpenHotel` | S→C zone→client | décodé (desasm) | DESASM |
| 540 | `CNC_CZ_ClientSetRoomResult` | S→C zone→client | inconnu | — |
| 541 | `CNC_CZ_ClientRoomData` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 542 | `CNC_CZ_ClientUpdateRoomItem` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 543 | `CNC_CZ_ClientPlayerLifeSkill` | S→C zone→client | décodé (golden) | GOLDEN |
| 544 | `CNC_CZ_ClientLifeSkillUpdate` | S→C zone→client | décodé (desasm) | DESASM |
| 545 | `CNC_CZ_ClientRideSpell` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 546 | `CNC_CZ_ClientSpellFlyToLocation` | S→C zone→client | inconnu | — |
| 547 | `CNC_CZ_ClientTerritoryEnd` | S→C zone→client | décodé (desasm) | DESASM |
| 548 | `CNC_CZ_ClientUpdateSpellAttr` | S→C zone→client | décodé (desasm) | DESASM |
| 549 | `CNC_CZ_ClientUpdateHealthOnlineTime` | S→C zone→client | inconnu | — |
| 550 | `CNC_CZ_ClientResetDailyMission` | S→C zone→client | inconnu | — |
| 551 | `CNC_CZ_ClientNewPlayerGuide` | S→C zone→client | décodé (golden) | GOLDEN |
| 552 | `CNC_CZ_ClientColosseumModeQueryResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 553 | `CNC_CZ_ClientRecruitInstanceResult` | S→C zone→client | décodé (desasm) | DESASM |
| 554 | `CNC_CZ_ClientUpdateLifelongPoint` | S→C zone→client | décodé (golden) | GOLDEN |
| 555 | `CNC_CZ_ClientUpdateLifelongGold` | S→C zone→client | décodé (desasm) | DESASM |
| 556 | `CNC_CZ_ClientChanceLuckyBagResult` | S→C zone→client | décodé (desasm) | DESASM |
| 557 | `CNC_CZ_ClientUpdateFortuneBagCounter` | S→C zone→client | décodé (golden) | GOLDEN |
| 558 | `CNE_CZ_ZoneServerQueryRecommendActivities` | C→S client→zone | implémenté `ZoneQueryRecommendActivities` | `ZoneMessages.cs` |
| 559 | `CNC_CZ_ClientRecommendActivities` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 560 | `CNC_CZ_ClientUpdateRecommendActivities` | S→C zone→client | décodé (desasm) | DESASM |
| 561 | `CNC_CZ_ClientFamilyBattleInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 562 | `CNC_CZ_ClientFamilyBattleKilledRank` | S→C zone→client | décodé (desasm) | DESASM |
| 563 | `CNC_CZ_ClientUpdateNodeTimes` | S→C zone→client | décodé (golden) | GOLDEN |
| 564 | `CNC_CZ_ClientOpenInstanceDialog` | S→C zone→client | décodé (desasm) | DESASM |
| 565 | `CNC_CZ_ClientR25UpdateFamily` | S→C zone→client | décodé (desasm) | DESASM |
| 566 | `CNC_CZ_ClientTerritoryFamilyScore` | S→C zone→client | décodé (desasm) | DESASM |
| 567 | `CNC_CZ_ClientUpdateMixElf` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 568 | `CNC_CZ_ClientUpdateElfMixElf` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 569 | `CNC_CZ_ClientLookElfPetEquip` | S→C zone→client | inconnu | — |
| 570 | `CNC_CZ_ClientEquipTrainItemChanged` | S→C zone→client | décodé (desasm) | DESASM |
| 571 | `CNC_CZ_ClientPVPRankLookEquip` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 572 | `CNC_CZ_ClientLoadCubeItems` | S→C zone→client | décodé (desasm) | DESASM |
| 573 | `CNC_CZ_ClientGreedyIslandInfo` | S→C zone→client | inconnu | — |
| 574 | `CNC_CZ_ClientPetUpgradeCasting` | S→C zone→client | décodé (desasm) | DESASM |
| 575 | `CNC_CZ_ClientPetUpgradeCancel` | S→C zone→client | décodé (desasm) | DESASM |
| 576 | `CNC_CZ_ClientUpdateWarCoin` | S→C zone→client | inconnu | — |
| 577 | `CNC_CZ_ClientTeamCheckNotify` | S→C zone→client | décodé (desasm) | DESASM |
| 578 | `CNC_CZ_ClientUpdateFloorInfo` | S→C zone→client | inconnu | — |
| 579 | `CNC_CZ_ClientOpenAreaDialog` | S→C zone→client | décodé (desasm) | DESASM |
| 580 | `CNC_CZ_ClientUpdateAlpacaCoin` | S→C zone→client | inconnu | — |
| 581 | `CNC_CZ_ClientLifeFloat` | S→C zone→client | inconnu | — |
| 582 | `CNC_CZ_ClientSearchTeamInfoV2` | S→C zone→client | décodé (desasm) | DESASM |
| 583 | `CNC_CZ_ClientTeamRecruitMsgV2` | S→C zone→client | décodé (golden) | GOLDEN |
| 584 | `CNC_CZ_ClientCheckTeamTransport` | S→C zone→client | inconnu | — |
| 585 | `CNC_CZ_ClientOpenMissionShop` | S→C zone→client | décodé (desasm) | DESASM |
| 586 | `CNC_CZ_ClientTerritoryPlayerInfo` | S→C zone→client | inconnu | — |
| 587 | `CNC_CZ_ClientColosseumQuery` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 588 | `CNC_CZ_ClientUpdateElfSpell` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 589 | `CNC_CZ_ClientUpdateDestinyRoad` | S→C zone→client | décodé (golden) | GOLDEN |
| 590 | `CNC_CZ_ClientDestinyDiceFailed` | S→C zone→client | décodé (desasm) | DESASM |
| 591 | `CNC_CZ_ClientBattleRoyaleInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 592 | `CNC_CZ_ClientPlayerClassCustomData` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 593 | `CNC_CZ_ClientEnterNodeFastDress` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 594 | `CNC_CZ_ClientUpdateFastDressData` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 595 | `CNC_CZ_ClientInGameLotteryNotify` | S→C zone→client | inconnu | — |
| 596 | `CNC_CZ_ClientDiamondLotteryNotify` | S→C zone→client | inconnu | — |
| 597 | `CNC_CZ_ClientMarryOperation` | S→C zone→client | décodé (desasm) | DESASM |
| 598 | `CNC_CZ_ClientOpenChurchDialog` | S→C zone→client | inconnu | — |
| 599 | `CNC_CZ_ClientWeddingsInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 600 | `CNC_CZ_ClientUpdateAttrNewExp` | S→C zone→client | implémenté `ZoneUpdateAttrNewExp` | `ZoneHudMessages.cs` |
| 601 | `CNC_CZ_ClientVersusInstanceInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 602 | `CNC_CZ_ClientOpenPaySystem` | S→C zone→client | décodé (desasm) | DESASM |
| 603 | `CNC_CZ_ClientPayEvent` | S→C zone→client | inconnu | — |
| 604 | `CNC_CZ_ClientPayEventOK` | S→C zone→client | décodé (desasm) | DESASM |
| 605 | `CNC_CZ_ClientSlotFlicker` | S→C zone→client | inconnu | — |
| 606 | `CNC_CZ_ClientUpdateAttrNewHp` | S→C zone→client | décodé (desasm) | DESASM |
| 607 | `CNC_CZ_ClientEvaluateInfo` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 608 | `CNC_CZ_ClientUpdateEvaluateInfo` | S→C zone→client | décodé (desasm) | DESASM |
| 609 | `CNC_CZ_ClientEnterStarWayData` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 610 | `CNC_CZ_ClientUIControlNotify` | S→C zone→client | inconnu | — |
| 611 | `CNC_CZ_ClientChangeSpell` | S→C zone→client | inconnu | — |
| 612 | `CNC_CZ_ClientOpenMagicBoxDialog` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 613 | `CNC_CZ_ClientMagicBoxResult` | S→C zone→client | décodé (passthru) | PASSTHRU |
| 614 | `CNC_CZ_ClientBackItem` | S→C zone→client | inconnu | — |
| 615 | `CNC_CZ_ClientUpdateDailyAwards` | S→C zone→client | décodé (incomplet) | INCOMPLET |
| 616 | `CNC_CZ_ClientUpdateBattleCrusadeInfo` | S→C zone→client | inconnu | — |
| 617 | `CNC_CZ_ClientOpenBattleCrusadeDialog` | S→C zone→client | inconnu | — |
| 618 | `CNC_CZ_ClientPetLotteryResult` | S→C zone→client | inconnu | — |
| 619 | `CNC_CZ_ClientPetLotteryEnd` | S→C zone→client | inconnu | — |
| 620 | `CNC_CZ_ClientArrangeItemOK` | S→C zone→client | décodé (desasm) | DESASM |
| 621 | `CNC_T_ServerGetTicket` | C→S client→serveur | inconnu | — |
| 622 | `CNC_T_ServerUseTicket` | C→S client→serveur | inconnu | — |
| 623 | `CNE_T_ClientGetTicket` | S→C serveur→client | inconnu | — |
| 624 | `CNE_T_ClientUseTicket` | S→C serveur→client | inconnu | — |
| 625 | `CNC_ZW_WorldServerIDReport` | interne world→zone | inconnu | — |
| 626 | `CNC_ZW_WorldServerTell` | interne world→zone | inconnu | — |
| 627 | `CNC_ZW_WorldServerAllUsers` | interne world→zone | inconnu | — |
| 628 | `CNC_ZW_WorldServerNotifyEntry` | interne world→zone | inconnu | — |
| 629 | `CNC_ZW_WorldServerCharacterArrived` | interne world→zone | inconnu | — |
| 630 | `CNC_ZW_WorldServerResetTime` | interne world→zone | inconnu | — |
| 631 | `CNC_ZW_WorldServerReturnToCharacterList` | interne world→zone | inconnu | — |
| 632 | `CNC_ZW_WorldServerTeamLeavedNotify` | interne world→zone | inconnu | — |
| 633 | `CNC_ZW_WorldServerChannelMsg` | interne world→zone | inconnu | — |
| 634 | `CNC_ZW_WorldServerFamilyCreation` | interne world→zone | inconnu | — |
| 635 | `CNC_ZW_WorldServerLogout` | interne world→zone | inconnu | — |
| 636 | `CNC_ZW_WorldServerTextCommand` | interne world→zone | décodé (passthru) | PASSTHRU |
| 637 | `CNC_ZW_WorldServerItemMallReceieveAttempt` | interne world→zone | inconnu | — |
| 638 | `CNC_ZW_WorldServerGetCloneItemID` | interne world→zone | inconnu | — |
| 639 | `CNC_ZW_WorldServerTextExCmd` | interne world→zone | inconnu | — |
| 640 | `CNC_ZW_WorldServerFBMsgUpdate` | interne world→zone | inconnu | — |
| 641 | `CNE_ZW_ZoneServerReportID` | interne zone→world | inconnu | — |
| 642 | `CNE_ZW_ZoneServerDestShutdown` | interne zone→world | inconnu | — |
| 643 | `CNE_ZW_ZoneServerCharacterArrivedDest` | interne zone→world | inconnu | — |
| 644 | `CNE_ZW_ZoneServerChannelMsg` | interne zone→world | inconnu | — |
| 645 | `CNE_ZW_ZoneServerChangeFamily` | interne zone→world | inconnu | — |
| 646 | `CNE_ZW_ZoneServerNewFamily` | interne zone→world | inconnu | — |
| 647 | `CNE_ZW_ZoneServerRemoveFamily` | interne zone→world | inconnu | — |
| 648 | `CNE_ZW_ZoneServerTextCommand` | interne zone→world | inconnu | — |
| 649 | `CNE_ZW_ZoneServerItemMallReceieveAttempt` | interne zone→world | inconnu | — |
| 650 | `CNE_ZW_ZoneServerGetCloneItemID` | interne zone→world | inconnu | — |
| 651 | `CNE_ZW_ZoneServerUpdateFortuneBag` | interne zone→world | inconnu | — |
| 652 | `CNE_ZW_ZoneServerUpdateLottery` | interne zone→world | inconnu | — |
| 653 | `CNE_ZW_ZoneServerTextExCmd` | interne zone→world | inconnu | — |
| 654 | `CNE_ZW_ZoneServerUpdateStarWay` | interne zone→world | inconnu | — |
| 655 | `CNC_MW_WorldServerItemMallMailSendResult` | interne world→mission | inconnu | — |
| 656 | `CNC_MW_WorldServerItemMallMailCheckOK` | interne world→mission | inconnu | — |
| 657 | `CNC_MW_WorldServerUpdateBlackList` | interne world→mission | inconnu | — |
| 658 | `CNC_MW_WorldServerUpdatePrestigeData` | interne world→mission | inconnu | — |
| 659 | `CNC_MW_WorldServerUpdateFriendMember` | interne world→mission | inconnu | — |
| 660 | `CNC_MW_WorldServerCrossZone` | interne world→mission | inconnu | — |
| 661 | `CNC_MW_WorldServerFBMsgUpdate` | interne world→mission | inconnu | — |
| 662 | `CNC_MW_WorldServerFriendNotify` | interne world→mission | inconnu | — |
| 663 | `CNC_MW_WorldServerUpdateSpecialCoin` | interne world→mission | inconnu | — |
| 664 | `CNC_MW_WorldServerCheckFamilyInvite` | interne world→mission | inconnu | — |
| 665 | `CNC_ZMis_MissionServerCheckFamilyInvite` | interne mission→zone | inconnu | — |
| 666 | `CNC_MW_WorldServerTell` | interne world→mission | inconnu | — |
| 667 | `CNC_MW_WorldServerTextExCmd` | interne world→mission | inconnu | — |
| 668 | `CNC_MW_WorldServerTextExCmdRemoteWorld` | interne world→mission | inconnu | — |
| 669 | `CNC_MW_WorldServerTextExCmdRemoteZone` | interne world→mission | inconnu | — |
| 670 | `CNC_MW_MissionServerTextExCmd` | interne world→mission | inconnu | — |
| 671 | `CNC_MW_MissionServerTextExCmdRemoteWorld` | interne world→mission | inconnu | — |
| 672 | `CNC_MW_MissionServerTextExCmdRemoteZone` | interne world→mission | inconnu | — |
| 673 | `CNE_ZM_ZoneServerUpdatePKList` | interne zone→mission | inconnu | — |
| 674 | `CNC_ZMis_MissionServerUpdatePKList` | interne mission→zone | inconnu | — |
| 675 | `CNE_MW_WorldTextCommand` | interne mission→world | inconnu | — |
| 676 | `CNC_DBA_AgentDBACommand` | interne dba→* | inconnu | — |
| 677 | `CNE_DBA_ClientContainer` | interne *→dba | inconnu | — |
| 678 | `CNE_DBA_ClientEnchant` | interne *→dba | inconnu | — |
| 679 | `CNE_DBA_ClientNodeID` | interne *→dba | inconnu | — |
| 680 | `CNE_DBA_ClientPlayerData` | interne *→dba | inconnu | — |
| 681 | `CNE_DBA_ClientMissionState` | interne *→dba | inconnu | — |
| 682 | `CNC_DBA_ClientDelPlayerOK` | interne dba→* | inconnu | — |
| 683 | `CNE_DBA_ClientAllFamily` | interne *→dba | inconnu | — |
| 684 | `CNE_DBA_ClientNewFamily` | interne *→dba | inconnu | — |
| 685 | `CNE_DBA_ClientChangeFamily` | interne *→dba | inconnu | — |
| 686 | `CNE_DBA_ClientRemoveFamily` | interne *→dba | inconnu | — |
| 687 | `CNE_DBA_ClientFamilyAllMember` | interne *→dba | inconnu | — |
| 688 | `CNE_DBA_ClientAddFamilyMember` | interne *→dba | inconnu | — |
| 689 | `CNE_DBA_ClientChannelMsg` | interne *→dba | inconnu | — |
| 690 | `CNE_DBA_ClientShortcut` | interne *→dba | inconnu | — |
| 691 | `CNE_DBA_ClientSpell` | interne *→dba | inconnu | — |
| 692 | `CNE_DBA_ClientFinishPlayerData` | interne *→dba | inconnu | — |
| 693 | `CNE_DBA_ClientAllLover` | interne *→dba | inconnu | — |
| 694 | `CNE_DBA_ClientCollection` | interne *→dba | inconnu | — |
| 695 | `CNC_DBA_ClientFamilyAllMessage` | interne dba→* | inconnu | — |
| 696 | `CNE_DBA_ClientFamilyStorage` | interne *→dba | inconnu | — |
| 697 | `CNE_DBA_ClientAllTerritory` | interne *→dba | inconnu | — |
| 698 | `CNE_DBA_ClientGladiatorTeamInfo` | interne *→dba | inconnu | — |
| 699 | `CNE_DBA_ClientAllPlayerRoom` | interne *→dba | inconnu | — |
| 700 | `CNE_DBA_ClientFortuneBagCounter` | interne *→dba | inconnu | — |
| 701 | `CNE_DBA_ClientFamilyContainerData` | interne *→dba | inconnu | — |
| 702 | `CNE_DBA_ClientAllFamilyInstnce` | interne *→dba | inconnu | — |
| 703 | `CNC_ZMis_MissionServerUpdatePlayerCard` | interne mission→zone | décodé (passthru) | PASSTHRU |
| 704 | `CNC_ZMis_MissionServerItemMallMailSend` | interne mission→zone | inconnu | — |
| 705 | `CNC_ZMis_MissionServerItemMallMailCheck` | interne mission→zone | inconnu | — |
| 706 | `CNC_ZMis_MissionServerAddFamilyMessage` | interne mission→zone | inconnu | — |
| 707 | `CNE_ZMis_ZoneServerReportID` | interne zone→mission | inconnu | — |
| 708 | `CNC_ZMis_MissionServerIDReport` | interne mission→zone | inconnu | — |
| 709 | `CNC_MW_WorldServerCrossWorldEndClearAccount` | interne world→mission | inconnu | — |
| 710 | `CNC_ZMis_MissionServerNodeCreate` | interne mission→zone | inconnu | — |
| 711 | `CNE_ZMis_ZoneServerListDialogue` | interne zone→mission | inconnu | — |
| 712 | `CNE_ZMis_ZoneServerReplyDialogue` | interne zone→mission | inconnu | — |
| 713 | `CNE_ZMis_ZoneServerTransitionCommands` | interne zone→mission | inconnu | — |
| 714 | `CNE_ZMis_ZoneServerTextOutput` | interne zone→mission | inconnu | — |
| 715 | `CNE_ZM_ZoneServerReportMonster` | interne zone→mission | inconnu | — |
| 716 | `CNE_ZM_ZoneServerRebornMonster` | interne zone→mission | inconnu | — |
| 717 | `CNE_ZM_ZoneServerSetRebornMonsterResult` | interne zone→mission | inconnu | — |
| 718 | `CNE_ZM_ZoneServerMessageToCharacter` | interne zone→mission | inconnu | — |
| 719 | `CNE_ZM_ZoneServerCharRegenerate` | interne zone→mission | inconnu | — |
| 720 | `CNE_ZMis_ZoneServerUpdateNPCSymbols` | interne zone→mission | inconnu | — |
| 721 | `CNE_ZMis_ZoneServerTextOutputIndex` | interne zone→mission | inconnu | — |
| 722 | `CNE_ZMis_ZoneServerUpdateMissionState` | interne zone→mission | inconnu | — |
| 723 | `CNE_ZMis_ZoneServerMailList` | interne zone→mission | inconnu | — |
| 724 | `CNE_ZMis_ZoneServerMailRead` | interne zone→mission | inconnu | — |
| 725 | `CNE_ZMis_ZoneServerMailDeleteResult` | interne zone→mission | inconnu | — |
| 726 | `CNE_ZMis_ZoneServerMailSendResult` | interne zone→mission | inconnu | — |
| 727 | `CNE_ZMis_ZoneServerMailReceive` | interne zone→mission | inconnu | — |
| 728 | `CNE_ZMis_ZoneServerPutItem` | interne zone→mission | inconnu | — |
| 729 | `CNE_ZMis_ZoneServerDropItem` | interne zone→mission | inconnu | — |
| 730 | `CNE_ZMis_ZoneServerMailGetItemOK` | interne zone→mission | inconnu | — |
| 731 | `CNE_ZMis_ZoneServerMailGetGoldOK` | interne zone→mission | inconnu | — |
| 732 | `CNE_ZMis_ZoneServerMailReturnResult` | interne zone→mission | inconnu | — |
| 733 | `CNE_ZMis_ZoneServerAuctionBuyList` | interne zone→mission | inconnu | — |
| 734 | `CNE_ZMis_ZoneServerAuctionBuyOK` | interne zone→mission | inconnu | — |
| 735 | `CNE_ZMis_ZoneServerAuctionCancelOK` | interne zone→mission | inconnu | — |
| 736 | `CNE_ZMis_ZoneServerAuctionSellList` | interne zone→mission | inconnu | — |
| 737 | `CNE_ZMis_ZoneServerAuctionSellResult` | interne zone→mission | inconnu | — |
| 738 | `CNE_ZMis_ZoneServerSearchInfo` | interne zone→mission | inconnu | — |
| 739 | `CNE_ZMis_ZoneServerMessageList` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 740 | `CNE_ZMis_ZoneServerMessageWriteResult` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 741 | `CNE_ZMis_ZoneServerMessageRead` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 742 | `CNE_ZMis_ZoneServerMessageDeleteResult` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 743 | `CNE_ZMis_ZoneServerCharNotQuit` | interne zone→mission | inconnu | — |
| 744 | `CNE_ZMis_ZoneServerLoverUpdate` | interne zone→mission | inconnu | — |
| 745 | `CNE_ZMis_ZoneServerAchievementUpdate` | interne zone→mission | inconnu | — |
| 746 | `CNE_ZMis_ZoneServerBattlefieldJoinResult` | interne zone→mission | inconnu | — |
| 747 | `CNE_ZMis_ZoneServerEnterBattlefield` | interne zone→mission | inconnu | — |
| 748 | `CNE_ZMis_ZoneServerStartBattlefield` | interne zone→mission | inconnu | — |
| 749 | `CNE_ZMis_ZoneServerBattlefieldList` | interne zone→mission | inconnu | — |
| 750 | `CNE_ZMis_ZoneServerBattlefieldEraseMember` | interne zone→mission | inconnu | — |
| 751 | `CNE_ZMis_ZoneServerEnterBattlefieldAnswer` | interne zone→mission | inconnu | — |
| 752 | `CNE_ZMis_ZoneServerBattlefieldSetup` | interne zone→mission | inconnu | — |
| 753 | `CNE_ZMis_ZoneServerBattlefieldChangeCharNum` | interne zone→mission | inconnu | — |
| 754 | `CNE_ZMis_ZoneServerCrossZoneResult` | interne zone→mission | inconnu | — |
| 755 | `CNE_ZMis_ZoneServerFamilyStorageUpdate` | interne zone→mission | inconnu | — |
| 756 | `CNE_ZMis_ZoneServerFamilyStorageLog` | interne zone→mission | inconnu | — |
| 757 | `CNE_ZMis_ZoneServerFamilyContainerUpdate` | interne zone→mission | inconnu | — |
| 758 | `CNE_ZMis_ZoneServerFamilyTownMemberQuery` | interne zone→mission | inconnu | — |
| 759 | `CNE_ZMis_ZoneServerQueryPlayerCard` | interne zone→mission | inconnu | — |
| 760 | `CNE_ZMis_ZoneServerTerritoryInfo` | interne zone→mission | inconnu | — |
| 761 | `CNE_ZMis_ZoneServerTerritoryQuery` | interne zone→mission | inconnu | — |
| 762 | `CNE_ZMis_ZoneServerTerritoryJoin` | interne zone→mission | inconnu | — |
| 763 | `CNE_ZMis_ZoneServerRankInfo` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 764 | `CNE_ZMis_ZoneServerBuyBackList` | interne zone→mission | inconnu | — |
| 765 | `CNE_ZMis_ZoneServerBuyBackGetItem` | interne zone→mission | inconnu | — |
| 766 | `CNE_ZMis_ZoneServerBuybackInitAllocate` | interne zone→mission | inconnu | — |
| 767 | `CNE_ZMis_ZoneServerDeallocateItemID` | interne zone→mission | inconnu | — |
| 768 | `CNE_ZMis_ZoneServerNPCTalkCheck` | interne zone→mission | inconnu | — |
| 769 | `CNE_ZMis_ZoneServerRemoteClient` | interne zone→mission | inconnu | — |
| 770 | `CNE_ZMis_ZoneServerTextExCmd` | interne zone→mission | inconnu | — |
| 771 | `CNE_ZMis_ZoneServerTextExCmdRemoteWorld` | interne zone→mission | inconnu | — |
| 772 | `CNE_ZMis_ZoneServerTextExCmdRemoteZone` | interne zone→mission | inconnu | — |
| 773 | `CNE_ZMis_ZoneServerEnterColosseum` | interne zone→mission | inconnu | — |
| 774 | `CNE_ZMis_ZoneServerColosseumJoinResult` | interne zone→mission | inconnu | — |
| 775 | `CNE_ZMis_ZoneServerStartColosseum` | interne zone→mission | inconnu | — |
| 776 | `CNE_ZMis_ZoneServerColosseumLeaveJoinResult` | interne zone→mission | inconnu | — |
| 777 | `CNE_ZMis_ZoneServerColosseumModeUpdate` | interne zone→mission | inconnu | — |
| 778 | `CNE_ZMis_ZoneServerSearchRoomResult` | interne zone→mission | inconnu | — |
| 779 | `CNE_ZMis_ZoneServerUpdateRoom` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 780 | `CNE_ZMis_ZoneServerSwitchPlayerRoom` | interne zone→mission | inconnu | — |
| 781 | `CNE_ZMis_ZoneServerUpdateRoomItem` | interne zone→mission | inconnu | — |
| 782 | `CNC_ZMis_MissionServerListDialogue` | interne mission→zone | inconnu | — |
| 783 | `CNC_ZMis_MissionServerOnDialogueSelected` | interne mission→zone | inconnu | — |
| 784 | `CNC_ZM_MonsterServerMonsterReport` | interne mission→zone | inconnu | — |
| 785 | `CNC_ZM_MonsterServerMonsterDieReport` | interne mission→zone | inconnu | — |
| 786 | `CNC_ZM_MonsterServerListRebornMonster` | interne mission→zone | inconnu | — |
| 787 | `CNC_ZM_MonsterServerSetRebornMonster` | interne mission→zone | inconnu | — |
| 788 | `CNC_ZM_MonsterServerModifyRebornMonster` | interne mission→zone | inconnu | — |
| 789 | `CNC_ZM_MonsterServerRemoveRebornMonster` | interne mission→zone | inconnu | — |
| 790 | `CNC_ZM_MonsterServerUnRegRebornMonster` | interne mission→zone | inconnu | — |
| 791 | `CNC_ZM_MonsterServerRemoveNode` | interne mission→zone | inconnu | — |
| 792 | `CNC_ZM_MonsterServerStartCharRegenerate` | interne mission→zone | inconnu | — |
| 793 | `CNC_ZM_MonsterServerStopCharRegenerate` | interne mission→zone | inconnu | — |
| 794 | `CNC_ZM_MonsterServerRegenerationReport` | interne mission→zone | inconnu | — |
| 795 | `CNC_ZMis_MissionServerRemovePMS` | interne mission→zone | inconnu | — |
| 796 | `CNC_ZMis_MissionServerCloseMissionBrief` | interne mission→zone | inconnu | — |
| 797 | `CNC_ZMis_MissionServerMigrationDone` | interne mission→zone | inconnu | — |
| 798 | `CNC_ZMis_MissionServerGiveUpMission` | interne mission→zone | inconnu | — |
| 799 | `CNC_ZMis_MissionServerMobDieReport` | interne mission→zone | inconnu | — |
| 800 | `CNC_ZMis_MissionServerMailSend` | interne mission→zone | inconnu | — |
| 801 | `CNC_ZMis_MissionServerSendSystemMail` | interne mission→zone | inconnu | — |
| 802 | `CNC_ZMis_MissionServerMessageWrite` | interne mission→zone | décodé (passthru) | PASSTHRU |
| 803 | `CNC_ZMis_MissionServerChannelMsg` | interne mission→zone | inconnu | — |
| 804 | `CNC_ZMis_MissionServerBattlefieldJoin` | interne mission→zone | inconnu | — |
| 805 | `CNC_ZMis_MissionServerBattlefieldEraseMember` | interne mission→zone | inconnu | — |
| 806 | `CNC_ZMis_MissionServerBattlefieldClose` | interne mission→zone | inconnu | — |
| 807 | `CNC_ZMis_MissionServerBattlefieldList` | interne mission→zone | inconnu | — |
| 808 | `CNC_ZMis_MissionServerEnterBattlefieldAnswer` | interne mission→zone | inconnu | — |
| 809 | `CNC_ZMis_MissionServerBattlefieldUpdateStatus` | interne mission→zone | inconnu | — |
| 810 | `CNC_ZMis_MissionServerCrossZoneRequest` | interne mission→zone | inconnu | — |
| 811 | `CNC_ZMis_MissionServerCrossZone` | interne mission→zone | inconnu | — |
| 812 | `CNC_ZMis_MissionServerRemoteClient` | interne mission→zone | inconnu | — |
| 813 | `CNC_ZMis_MissionServerTextExCmd` | interne mission→zone | inconnu | — |
| 814 | `CNC_ZMis_MissionServerTextExCmdRemoteWorld` | interne mission→zone | inconnu | — |
| 815 | `CNC_ZMis_MissionServerTextExCmdRemoteZone` | interne mission→zone | inconnu | — |
| 816 | `CNC_ZMis_MissionServerGetTeamInviteeID` | interne mission→zone | inconnu | — |
| 817 | `CNE_ZMis_ZoneServerReturnTeamInviteeID` | interne zone→mission | inconnu | — |
| 818 | `CNE_ZMis_ZoneServerGMToolGetInfo` | interne zone→mission | inconnu | — |
| 819 | `CNE_ZMis_ZoneServerGMToolGetMission` | interne zone→mission | inconnu | — |
| 820 | `CNC_ZMis_MissionServerColosseumJoinAnswer` | interne mission→zone | inconnu | — |
| 821 | `CNC_ZMis_MissionServerColosseumJoinRequest` | interne mission→zone | inconnu | — |
| 822 | `CNC_ZMis_MissionServerColosseumClose` | interne mission→zone | inconnu | — |
| 823 | `CNC_ZMis_MissionServerColosseumLeaveJoin` | interne mission→zone | inconnu | — |
| 824 | `CNE_ZMis_ZoneServerAchievementRewardMail` | interne zone→mission | inconnu | — |
| 825 | `CNE_ZMis_ZoneServerResetDailyMission` | interne zone→mission | inconnu | — |
| 826 | `CNE_DBA_ClientNewPlayerGuideData` | interne *→dba | inconnu | — |
| 827 | `CNE_ZMis_ZoneServerUpdateLifelongPoint` | interne zone→mission | inconnu | — |
| 828 | `CNE_ZMis_ZoneServerUpdateLifelongGold` | interne zone→mission | inconnu | — |
| 829 | `CNE_ZMis_ZoneServerResetDailyMonsterKill` | interne zone→mission | inconnu | — |
| 830 | `CNE_DBA_ClientRecommendActivityState` | interne *→dba | inconnu | — |
| 831 | `CNC_ZMis_MissionServerRefreshRecommendActivities` | interne mission→zone | décodé (passthru) | PASSTHRU |
| 832 | `CNE_ZMis_ZoneServerUpdateRecommendActivities` | interne zone→mission | décodé (passthru) | PASSTHRU |
| 833 | `CNE_ZMis_ZoneServerFamilyBattleInfo` | interne zone→mission | inconnu | — |
| 834 | `CNE_ZMis_ZoneServerResetNodeTimes` | interne zone→mission | inconnu | — |
| 835 | `CNC_ZMis_MissionServerSetRecommendActivityTracing` | interne mission→zone | décodé (passthru) | PASSTHRU |
| 836 | `CNE_DBA_ClientElfData` | interne *→dba | inconnu | — |
| 837 | `CNE_ZMis_ZoneServerPVPTopData` | interne zone→mission | inconnu | — |
| 838 | `CNE_ZMis_ZoneServerGreedyIslandJoin` | interne zone→mission | inconnu | — |
| 839 | `CNE_ZMis_ZoneServerGreedyIslandInfo` | interne zone→mission | inconnu | — |
| 840 | `CNC_ZMis_MissionServerGreedyIslandRankQuery` | interne mission→zone | inconnu | — |
| 841 | `CNE_ZMis_ZoneServerColosseumQuery` | interne zone→mission | inconnu | — |
| 842 | `CNC_ZMis_MissionServerBattleRoyaleJoinRequest` | interne mission→zone | inconnu | — |
| 843 | `CNE_ZMis_ZoneServerStartBattleRoyale` | interne zone→mission | inconnu | — |
| 844 | `CNC_ZMis_MissionServerBattleRoyaleClose` | interne mission→zone | inconnu | — |
| 845 | `CNC_ZMis_MissionServerPlayerCCDataSave` | interne mission→zone | inconnu | — |
| 846 | `CNE_DBA_ClientFastDress` | interne *→dba | inconnu | — |
| 847 | `CNE_ZMis_ZoneServerFastDressEquipEnd` | interne zone→mission | inconnu | — |
| 848 | `CNE_ZMis_ZoneServerWeddingsInfo` | interne zone→mission | inconnu | — |
| 849 | `CNC_ZMis_MissionServerVersusInstanceJoinRequest` | interne mission→zone | inconnu | — |
| 850 | `CNE_ZMis_ZoneServerStartVersusInstance` | interne zone→mission | inconnu | — |
| 851 | `CNC_ZMis_MissionServerFindCharacter` | interne mission→zone | inconnu | — |
| 852 | `CNE_ZMis_ZoneServerFindTargetLocation` | interne zone→mission | inconnu | — |
| 853 | `CNC_ZMis_MissionServerSendTargetLocation` | interne mission→zone | inconnu | — |
| 854 | `CNE_ZMis_ZoneServerTransportGMCharacterToCharacter` | interne zone→mission | inconnu | — |
| 855 | `CNE_ZMis_ZoneServerStartBattleCrusade` | interne zone→mission | inconnu | — |
| 856 | `CNE_ZMis_ZoneServerEnterBattleCrusadeAnswer` | interne zone→mission | inconnu | — |
| 857 | `CNE_ZMis_ZoneServerBattleCrusadeInfo` | interne zone→mission | inconnu | — |
| 858 | `CNC_ZMis_MissionServerArrangeItem` | interne mission→zone | inconnu | — |
| 859 | `CNE_DBA_ClientLotteryCount` | interne *→dba | inconnu | — |
| 860 | `CNE_CG_ServerTextCommand` | C→S client→gateway | inconnu | — |
| 861 | `CNE_CG_ServerLogin` | C→S client→gateway | inconnu | — |
| 862 | `CNC_CG_ClientCharacterList` | S→C gateway→client | inconnu | — |
| 863 | `CNC_CG_ClientCharacter` | S→C gateway→client | inconnu | — |
| 864 | `CNC_CG_ClientTextOutput` | S→C gateway→client | inconnu | — |
| 865 | `CNC_CG_ClientChannel` | S→C gateway→client | inconnu | — |
| 866 | `CNC_CG_ClientGetContainerItem` | S→C gateway→client | inconnu | — |
| 867 | `CNC_CG_ClientDataTest` | S→C gateway→client | inconnu | — |
| 868 | `CNC_CG_ClientCharLocate` | S→C gateway→client | inconnu | — |
| 869 | `CNC_CG_ClientGetMissionState` | S→C gateway→client | inconnu | — |
| 870 | `CNC_CG_ClientGetMoney` | S→C gateway→client | inconnu | — |
