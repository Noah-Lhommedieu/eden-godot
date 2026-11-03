# Feature-matrix protocole — generee par `tools/gen_feature_matrix.py`

> Ne pas editer a la main — relancer le script.

| NCID | Classe | Statut | Captures | Emission |
|---|---|---|---|---|
| 0 | `CNC_T_ClientGetTicket` | INCOMPLET | 2 | `Program.cs` |
| 1 | `CNE_CW_ServerReceiveTicket` | INCOMPLET | 2 | `Program.cs` |
| 2 | `CNE_CW_ServerKeepAlive` | DESASM | — | `LoginPacketHandlers.cs`, `Program.cs` |
| 3 | `CNE_CW_ServerLoginCharacter` | INCOMPLET | 2 | `LoginPacketHandlers.cs`, `Program.cs` |
| 4 | `CNE_CW_ServerCreateCharacterName` | INCOMPLET | 2 | `LoginPacketHandlers.cs` |
| 5 | `CNE_CW_ServerCreateCharacterData` | INCOMPLET | 2 | `LoginPacketHandlers.cs` |
| 6 | `CNE_CW_ServerRemoveCharacter` | DESASM | — | `LoginPacketHandlers.cs` |
| 7 | `CNE_CW_ServerCancelCreateCharacterReq` | INCOMPLET | 2 | `ZoneEntryFlow.Destiny.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 8 | `CNE_CW_ServerLookingForMember` | PASSTHRU | — | `FamilyHandlers.cs`, `ZoneEntryFlow.Items.cs` |
| 9 | `CNE_CW_ServerFamilyOp` | INCOMPLET | 2 | `TeamHandlers.cs` |
| 10 | `CNE_CW_ServerQueryFamilyInfo` | INCOMPLET | 2 | `TeamHandlers.cs`, `ZoneWorldLink.cs` |
| 11 | `CNE_CW_ServerLookingForGroup` | INCOMPLET | 2 | — |
| 12 | `CNE_CW_ServerAddFriend` | PASSTHRU | — | `Program.cs`, `ZoneWorldLink.cs` |
| 13 | `CNE_CW_ServerQueryFriendInfo` | NON PROUVE | — | — |
| 14 | `CNE_CW_ServerFriendInvitation` | INCOMPLET | 2 | — |
| 15 | `CNE_CW_ServerItemMallAnythingReceivable` | INCOMPLET | 2 | — |
| 16 | `CNC_CW_ServerItemMallReceivableList` | PASSTHRU | — | `ZoneLinkServer.cs` |
| 17 | `CNE_CW_ServerItemMallReceivableQuery` | NON PROUVE | — | — |
| 18 | `CNE_CW_ServerItemMallReceieveAttempt` | PASSTHRU | — | `ZoneLinkServer.cs` |
| 19 | `CNE_CW_ServerQueryExploitRank` | INCOMPLET | 2 | — |
| 20 | `CNE_CW_ServerChangeWorld` | PASSTHRU | — | `MissionServer.cs` |
| 21 | `CNE_CW_ServerChangeWorldAck` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 22 | `CNE_CW_ServerRemoveFriend` | NON PROUVE | — | — |
| 23 | `CNE_CW_ServerItemMallReqBuyItem` | INCOMPLET | 2 | `ZoneLinkServer.cs` |
| 24 | `CNE_CW_ServerItemMallListQuery` | INCOMPLET | 2 | `ItemMallHandlers.cs` |
| 25 | `CNE_CW_ServerItemMallPointInfo` | GOLDEN | 2 | — |
| 26 | `CNE_CW_ServerCaptchaReturn` | INCOMPLET | 2 | `ZoneLinkServer.cs` |
| 27 | `CNE_CW_ServerPlayerState` | NON PROUVE | — | — |
| 28 | `CNE_CW_ServerIllPluginInfo` | NON PROUVE | — | — |
| 29 | `CNE_CW_ServerUpdateWorldServers` | PASSTHRU | — | `ZoneLinkServer.cs` |
| 30 | `CNE_CW_ServerCheckHasCharPassword` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 31 | `CNE_CW_ServerCharPassword` | PASSTHRU | — | `FamilyHandlers.cs` |
| 32 | `CNE_CW_ServerCreateCharacterNameCheck` | PASSTHRU | — | `ZoneLinkServer.cs` |
| 33 | `CNE_CW_ServerMutuallyHelpSearch` | PASSTHRU | — | `FamilyHandlers.cs` |
| 34 | `CNC_CW_ClientHello` | DESASM | — | `Program.cs` |
| 35 | `CNC_CW_ClientTicketInvalid` | DESASM | — | `Program.cs` |
| 36 | `CNC_CW_ClientTextOutput` | NON PROUVE | — | — |
| 37 | `CNC_CW_ClientReceiveTicketToZoneServer` | DESASM | — | `Program.cs` |
| 38 | `CNC_CW_ClientGetCharacters` | DESASM | — | `Program.cs` |
| 39 | `CNC_CW_ClientVerifyCharacterName` | DESASM | — | `Program.cs` |
| 40 | `CNC_CW_ClientResetTime` | DESASM | — | `Program.cs`, `ZoneLinkServer.cs` |
| 41 | `CNC_CW_ClientCancelCreateCharacterResp` | DESASM | — | `Program.cs` |
| 42 | `CNC_CW_ClientTell` | NON PROUVE | — | — |
| 43 | `CNC_CW_ClientTextOutputIndex` | PASSTHRU | — | `ZoneLinkServer.cs` |
| 44 | `CNC_CW_ClientChannelMsg` | NON PROUVE | — | — |
| 45 | `CNC_CW_ClientLookingForMemberResult` | PASSTHRU | — | `MiscHandlers.cs` |
| 46 | `CNC_CW_ClientUpdateFamilyMember` | PASSTHRU | — | `FamilyHandlers.cs` |
| 47 | `CNC_CW_ClientFamilyOpException` | PASSTHRU | — | `FamilyHandlers.cs` |
| 48 | `CNC_CW_ClientFamilyInvitation` | PASSTHRU | — | `FamilyHandlers.cs` |
| 49 | `CNC_CW_ClientFamilyQuit` | PASSTHRU | — | `FamilyHandlers.cs` |
| 50 | `CNC_CW_ClientQueryFamilyInfo` | INCOMPLET | 2 | `FamilyHandlers.cs`, `ZoneLinkServer.cs` |
| 51 | `CNC_CW_ClientFamilySchedule` | PASSTHRU | — | `FamilyHandlers.cs` |
| 52 | `CNC_CW_ClientUpdateFriendMember` | PASSTHRU | — | `MissionSession.cs`, `FriendHandlers.cs` |
| 53 | `CNC_CW_ClientFriendInvitation` | INCOMPLET | 2 | `ZoneMissionLink.cs` |
| 54 | `CNC_CW_ClientRepeatedLogin` | INCOMPLET | 2 | — |
| 55 | `CNC_CW_ClientException` | INCOMPLET | 2 | `ZoneMissionLink.cs` |
| 56 | `CNC_CW_ClientItemMallAnythingReceivable` | DESASM | — | `ItemMallHandlers.cs` |
| 57 | `CNC_CW_ClientItemMallReceivableQuery` | INCOMPLET | 2 | `QuestEngine.Dialogue.cs`, `ItemMallHandlers.cs` |
| 58 | `CNC_CW_ClientItemMallReceieveAttempt` | DESASM | — | `ItemMallHandlers.cs` |
| 59 | `CNC_CW_ClientQueryExploitRank` | PASSTHRU | — | `MiscHandlers.cs` |
| 60 | `CNC_CW_ClientUpdatePrestigeData` | INCOMPLET | 2 | `FamilyHandlers.cs`, `ZoneLinkServer.cs` |
| 61 | `CNC_CW_ClientUpdateBlackList` | PASSTHRU | — | `FriendHandlers.cs` |
| 62 | `CNC_CW_ClientItemMallList` | INCOMPLET | 2 | `ItemMallHandlers.cs` |
| 63 | `CNC_CW_ClientItemMallReqBuyItem` | INCOMPLET | 2 | `ItemMallHandlers.cs` |
| 64 | `CNC_CW_ClientItemMallPointInfo` | DESASM | — | `ItemMallHandlers.cs` |
| 65 | `CNC_CW_ClientReceiveTicketToWorld` | PASSTHRU | — | `MiscHandlers.cs` |
| 66 | `CNC_CW_ClientGetWorldServers` | PASSTHRU | — | `MissionServer.cs`, `MiscHandlers.cs` |
| 67 | `CNC_CW_ClientFamilyAchievementUpdate` | PASSTHRU | — | `QuestEngine.Events.cs` |
| 68 | `CNC_CW_ClientFamilyLogUpdate` | PASSTHRU | — | `MissionServer.cs`, `FamilyHandlers.cs` |
| 69 | `CNC_CW_ClientCaptcha` | PASSTHRU | — | `MissionServer.cs` |
| 70 | `CNC_CW_ClientCrossZone` | PASSTHRU | — | `MissionServer.cs` |
| 71 | `CNE_CW_ServerApexBufferTransfer` | PASSTHRU | — | `MissionServer.cs` |
| 72 | `CNC_CW_ClientApexBufferTransfer` | DESASM | — | `Program.cs` |
| 73 | `CNE_CW_ServerQueryFamilyEmblem` | NON PROUVE | — | — |
| 74 | `CNC_CW_ClientFamilyEmblem` | PASSTHRU | — | `FamilyHandlers.cs` |
| 75 | `CNC_CW_ClientCheckHasCharPassword` | DESASM | — | `MiscHandlers.cs` |
| 76 | `CNC_CW_ClientCharPassword` | DESASM | — | `MiscHandlers.cs` |
| 77 | `CNC_CW_ClientSelectCharEnableBtn` | PASSTHRU | — | `MissionServer.cs` |
| 78 | `CNC_CW_ClientUpdateSpecialCoin` | PASSTHRU | — | `Program.cs`, `ZoneLinkServer.cs` |
| 79 | `CNC_CW_ClientItemMallLimit` | PASSTHRU | — | `ItemMallHandlers.cs` |
| 80 | `CNC_CW_ClientQueryFamilyRecruitList` | PASSTHRU | — | `MissionServer.cs`, `FamilyHandlers.cs` |
| 81 | `CNC_CW_ClientMutuallyHelpUpdate` | PASSTHRU | — | `MutuallyHelpHandlers.cs` |
| 82 | `CNC_CW_ClientNotifyChangeWorld` | NON PROUVE | — | — |
| 83 | `CNC_CW_ClientUpdateFortuneBagContentList` | NON PROUVE | — | — |
| 84 | `CNE_CZ_ZoneServerDivideItem` | NON PROUVE | — | — |
| 85 | `CNE_CZ_ZoneServerEnchantCancel` | INCOMPLET | 2 | — |
| 86 | `CNE_CZ_ZoneServerTeamRadarAttention` | NON PROUVE | — | — |
| 87 | `CNE_CZ_ZoneServerMissionListOp` | GOLDEN | 2 | — |
| 88 | `CNE_CZ_ZoneServerTransport` | NON PROUVE | — | — |
| 89 | `CNE_CZ_ZoneServerBagDueDate` | NON PROUVE | — | — |
| 90 | `CNE_CZ_ZoneServerTeamChangeLeader` | NON PROUVE | — | — |
| 91 | `CNE_CZ_ZoneServerTradeReady` | NON PROUVE | — | — |
| 92 | `CNE_CZ_ZoneServerTradeUnConfirm` | NON PROUVE | — | — |
| 93 | `CNE_CZ_ZoneServerUpdateAppellation` | NON PROUVE | — | — |
| 94 | `CNE_CZ_ZoneServerQueryAppellation` | NON PROUVE | — | — |
| 95 | `CNE_CZ_ZoneServerMailOpenBox` | NON PROUVE | — | — |
| 96 | `CNE_CZ_ZoneServerMailQuery` | NON PROUVE | — | — |
| 97 | `CNE_CZ_ZoneServerMailRead` | NON PROUVE | — | — |
| 98 | `CNE_CZ_ZoneServerMailDelete` | NON PROUVE | — | — |
| 99 | `CNE_CZ_ZoneServerMailSend` | NON PROUVE | — | — |
| 100 | `CNE_CZ_ZoneServerMailGetItem` | PASSTHRU | — | `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.cs` |
| 101 | `CNE_CZ_ZoneServerMailGetGold` | NON PROUVE | — | — |
| 102 | `CNE_CZ_ZoneServerMailReturn` | NON PROUVE | — | — |
| 103 | `CNE_CZ_ZoneServerSearchCondition` | NON PROUVE | — | — |
| 104 | `CNE_CZ_ZoneServerSearchOpt` | INCOMPLET | 2 | `MissionServer.cs` |
| 105 | `CNE_CZ_ZoneServerElfStoneUpdate` | NON PROUVE | — | — |
| 106 | `CNE_CZ_ZoneServerElfStoneUse` | NON PROUVE | — | — |
| 107 | `CNE_CZ_ZoneServerElfWork` | NON PROUVE | — | — |
| 108 | `CNC_T_ServerCheckPin` | NON PROUVE | — | — |
| 109 | `CNE_T_ClientCheckPin` | NON PROUVE | — | — |
| 110 | `CNC_G_ServerAccountLogin` | INCOMPLET | 2 | — |
| 111 | `CNC_G_ServerAccountLogout` | NON PROUVE | — | — |
| 112 | `CNC_G_ServerBillingLogin` | NON PROUVE | — | — |
| 113 | `CNC_G_ServerClientTypeReport` | INCOMPLET | 2 | — |
| 114 | `CNC_G_ServerGetPointInfo` | NON PROUVE | — | — |
| 115 | `CNC_G_ServerReqBuyItem` | NON PROUVE | — | — |
| 116 | `CNE_G_ClientAccountLogin` | NON PROUVE | — | — |
| 117 | `CNE_G_ClientAccountLogout` | NON PROUVE | — | — |
| 118 | `CNE_G_ClientKickUser` | NON PROUVE | — | — |
| 119 | `CNE_G_ClientServerTypeReport` | NON PROUVE | — | — |
| 120 | `CNC_G_ClientSetWorldStates` | NON PROUVE | — | — |
| 121 | `CNE_G_ClientBillingLogin` | NON PROUVE | — | — |
| 122 | `CNE_G_ClientGetPointInfo` | NON PROUVE | — | — |
| 123 | `CNE_G_ClientReqBuyItem` | INCOMPLET | 2 | — |
| 124 | `CNC_G_ServerTextCommand` | INCOMPLET | 2 | — |
| 125 | `CNE_G_ClientTextCommand` | NON PROUVE | — | — |
| 126 | `CNC_G_ServerCharacterLogin` | NON PROUVE | — | — |
| 127 | `CNC_G_ServerCharacterLogout` | PASSTHRU | — | `ZoneMissionLink.cs`, `ZoneEntryFlow.Migration.cs` |
| 128 | `CNC_G_ServerCrossWorldAccountLogoutAck` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 129 | `CNE_G_ClientCrossWorldAccountLogout` | NON PROUVE | — | — |
| 130 | `CNC_G_ServerCheckCommunicateLock` | NON PROUVE | — | — |
| 131 | `CNC_G_ServerExchangePinItem` | NON PROUVE | — | — |
| 132 | `CNC_G_ServerExchangePinUpdateState` | NON PROUVE | — | — |
| 133 | `CNE_G_ClientExchangePinItemResult` | INCOMPLET | 2 | — |
| 134 | `CNE_ZW_WorldServerIDReport` | NON PROUVE | — | — |
| 135 | `CNE_ZW_WorldServerTell` | NON PROUVE | — | — |
| 136 | `CNE_ZW_WorldServerAllUsers` | NON PROUVE | — | — |
| 137 | `CNE_ZW_WorldServerNotifyEntry` | NON PROUVE | — | — |
| 138 | `CNE_ZW_WorldServerCharacterArrived` | NON PROUVE | — | — |
| 139 | `CNE_ZW_WorldServerResetTime` | NON PROUVE | — | — |
| 140 | `CNE_ZW_WorldServerReturnToCharacterList` | NON PROUVE | — | — |
| 141 | `CNE_ZW_WorldServerTeamLeavedNotify` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 142 | `CNE_ZW_WorldServerChannelMsg` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 143 | `CNE_ZW_WorldServerFamilyCreation` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 144 | `CNE_ZW_WorldServerLogout` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 145 | `CNE_ZW_WorldServerTextCommand` | NON PROUVE | — | — |
| 146 | `CNE_ZW_WorldServerItemMallReceieveAttempt` | NON PROUVE | — | — |
| 147 | `CNE_ZW_WorldServerGetCloneItemID` | NON PROUVE | — | — |
| 148 | `CNE_ZW_WorldServerTextExCmd` | NON PROUVE | — | — |
| 149 | `CNE_ZW_WorldServerFBMsgUpdate` | NON PROUVE | — | — |
| 150 | `CNC_ZW_ZoneServerReportID` | NON PROUVE | — | — |
| 151 | `CNC_ZW_ZoneServerDestShutdown` | NON PROUVE | — | — |
| 152 | `CNC_ZW_ZoneServerCharacterArrivedDest` | INCOMPLET | 2 | — |
| 153 | `CNC_ZW_ZoneServerChannelMsg` | NON PROUVE | — | — |
| 154 | `CNC_ZW_ZoneServerChangeFamily` | NON PROUVE | — | — |
| 155 | `CNC_ZW_ZoneServerNewFamily` | NON PROUVE | — | — |
| 156 | `CNC_ZW_ZoneServerRemoveFamily` | INCOMPLET | 2 | — |
| 157 | `CNC_ZW_ZoneServerTextCommand` | NON PROUVE | — | — |
| 158 | `CNC_ZW_ZoneServerItemMallReceieveAttempt` | NON PROUVE | — | — |
| 159 | `CNC_ZW_ZoneServerGetCloneItemID` | NON PROUVE | — | — |
| 160 | `CNC_ZW_ZoneServerUpdateFortuneBag` | INCOMPLET | 2 | — |
| 161 | `CNC_ZW_ZoneServerUpdateLottery` | NON PROUVE | — | — |
| 162 | `CNC_ZW_ZoneServerTextExCmd` | INCOMPLET | 2 | — |
| 163 | `CNC_ZW_ZoneServerUpdateStarWay` | NON PROUVE | — | — |
| 164 | `CNE_MW_WorldServerItemMallMailSendResult` | INCOMPLET | 2 | — |
| 165 | `CNE_MW_WorldServerItemMallMailCheckOK` | INCOMPLET | 2 | — |
| 166 | `CNE_MW_WorldServerUpdateBlackList` | INCOMPLET | 2 | — |
| 167 | `CNE_MW_WorldServerUpdatePrestigeData` | NON PROUVE | — | — |
| 168 | `CNE_MW_WorldServerUpdateFriendMember` | INCOMPLET | 2 | — |
| 169 | `CNE_MW_WorldServerCrossZone` | NON PROUVE | — | — |
| 170 | `CNE_MW_WorldServerFBMsgUpdate` | NON PROUVE | — | — |
| 171 | `CNE_MW_WorldServerFriendNotify` | NON PROUVE | — | — |
| 172 | `CNE_MW_WorldServerUpdateSpecialCoin` | INCOMPLET | 2 | — |
| 173 | `CNE_MW_WorldServerCheckFamilyInvite` | NON PROUVE | — | — |
| 174 | `CNE_CZ_ZoneServerFamilyTownQuery` | NON PROUVE | — | — |
| 175 | `CNE_MW_WorldServerTell` | NON PROUVE | — | — |
| 176 | `CNE_MW_WorldServerTextExCmd` | NON PROUVE | — | — |
| 177 | `CNE_MW_WorldServerTextExCmdRemoteWorld` | NON PROUVE | — | — |
| 178 | `CNE_MW_WorldServerTextExCmdRemoteZone` | NON PROUVE | — | — |
| 179 | `CNE_CZ_ZoneServerFamilyStorageQueryLog` | NON PROUVE | — | — |
| 180 | `CNE_CZ_ZoneServerFamilyBuildingRepair` | NON PROUVE | — | — |
| 181 | `CNE_CZ_ZoneServerQueryLicense` | GOLDEN | 2 | — |
| 182 | `CNE_CZ_ZoneServerPlayerLicense` | NON PROUVE | — | — |
| 183 | `CNE_CZ_ZoneServerQueryLicenseSet` | NON PROUVE | — | — |
| 184 | `CNE_CZ_ZoneServerCheckUpdateLottery` | NON PROUVE | — | — |
| 185 | `CNE_CZ_ZoneServerBuyBackOpen` | NON PROUVE | — | — |
| 186 | `CNE_CZ_ZoneServerSetMissionTrace` | INCOMPLET | 2 | — |
| 187 | `CNE_CZ_ZoneServerPetModeChange` | NON PROUVE | — | — |
| 188 | `CNE_CZ_ZoneServerSendPetCmd` | NON PROUVE | — | — |
| 189 | `CNE_CZ_ZoneServerTerritoryQuery` | NON PROUVE | — | — |
| 190 | `CNE_CZ_ZoneServerTerritoryJoin` | NON PROUVE | — | — |
| 191 | `CNE_DBA_ClientDelPlayerOK` | NON PROUVE | — | — |
| 192 | `CNE_CZ_ZoneServerSearchTeam` | NON PROUVE | — | — |
| 193 | `CNE_CZ_ZoneServerRollTreasure` | PASSTHRU | — | `MissionServer.cs` |
| 194 | `CNE_CZ_ZoneServerRankInfoQuery` | NON PROUVE | — | — |
| 195 | `CNE_CZ_ZoneServerEnterNodePlayerRoom` | INCOMPLET | 2 | — |
| 196 | `CNE_CZ_ZoneServerGladiatorTeamCreate` | NON PROUVE | — | — |
| 197 | `CNE_CZ_ZoneServerGladiatorTeamQuery` | NON PROUVE | — | — |
| 198 | `CNE_CZ_ZoneServerColosseumJoinAnswer` | NON PROUVE | — | — |
| 199 | `CNE_CZ_ZoneServerColosseumJoinRequest` | NON PROUVE | — | — |
| 200 | `CNE_CZ_ZoneServerColosseumTeamQuery` | NON PROUVE | — | — |
| 201 | `CNE_CZ_ZoneServerColosseumLeaveJoin` | NON PROUVE | — | — |
| 202 | `CNE_CZ_ZoneServerCoRideInvite` | NON PROUVE | — | — |
| 203 | `CNE_CZ_ZoneServerDestinyDice` | NON PROUVE | — | — |
| 204 | `CNE_DBA_ClientFamilyAllMessage` | INCOMPLET | 2 | — |
| 205 | `CNE_CZ_ZoneServerSearchRoom` | NON PROUVE | — | — |
| 206 | `CNE_CZ_ZoneServerRentRoom` | NON PROUVE | — | — |
| 207 | `CNE_CZ_ZoneServerEnterRoom` | NON PROUVE | — | — |
| 208 | `CNE_CZ_ZoneServerSetRoomPrivilege` | NON PROUVE | — | — |
| 209 | `CNE_CZ_ZoneServerSwitchSetRoomMode` | NON PROUVE | — | — |
| 210 | `CNE_CZ_ZoneServerRoomGetData` | NON PROUVE | — | — |
| 211 | `CNE_CZ_ZoneServerUpdateDecoration` | NON PROUVE | — | — |
| 212 | `CNE_CZ_ZoneServerPayRent` | NON PROUVE | — | — |
| 213 | `CNE_CZ_ZoneServerQueryLifeSkill` | GOLDEN | 2 | — |
| 214 | `CNE_CZ_ZoneServerLifeSkill` | NON PROUVE | — | — |
| 215 | `CNE_CZ_ZoneServerChangeRoomTexture` | NON PROUVE | — | — |
| 216 | `CNE_CZ_ZoneServerStartParty` | NON PROUVE | — | — |
| 217 | `CNE_CZ_ZoneServerExtendRoom` | NON PROUVE | — | — |
| 218 | `CNE_MW_WorldServerCrossWorldEndClearAccount` | GOLDEN | 2 | — |
| 219 | `CNE_CZ_ZoneServerColosseumModeQuery` | NON PROUVE | — | — |
| 220 | `CNE_CZ_ZoneServerRecruitInstance` | NON PROUVE | — | — |
| 221 | `CNE_CZ_ZoneServerChanceLuckyBagGetReward` | NON PROUVE | — | — |
| 222 | `CNE_CZ_ZoneServerChanceLuckyBagReTry` | NON PROUVE | — | — |
| 223 | `CNE_CZ_ZoneServerQueryFortuneBagCounter` | GOLDEN | 2 | — |
| 224 | `CNE_CZ_ZoneServerUpdateLotteryBonus` | NON PROUVE | — | — |
| 225 | `CNE_CZ_ZoneServerFamilyBattleAskInfo` | NON PROUVE | — | — |
| 226 | `CNE_CZ_ZoneServerSetRecommendActivityTracing` | NON PROUVE | — | — |
| 227 | `CNE_CZ_ZoneServerElfMix` | NON PROUVE | — | — |
| 228 | `CNE_CZ_ZoneServerElfChangeName` | NON PROUVE | — | — |
| 229 | `CNE_CZ_ZoneServerElfRevive` | NON PROUVE | — | — |
| 230 | `CNE_CZ_ZoneServerEquipTrainSetItem` | NON PROUVE | — | — |
| 231 | `CNE_CZ_ZoneServerPVPRankLookEquip` | NON PROUVE | — | — |
| 232 | `CNE_CZ_ZoneServerVerifyCubeCosts` | NON PROUVE | — | — |
| 233 | `CNE_CZ_ZoneServerGreedyIslandJoin` | NON PROUVE | — | — |
| 234 | `CNE_CZ_ZoneServerGreedyIslandRankQuery` | NON PROUVE | — | — |
| 235 | `CNE_CZ_ZoneServerSearchTeamV2` | NON PROUVE | — | — |
| 236 | `CNE_CZ_ZoneServerTeamRecruitV2` | NON PROUVE | — | — |
| 237 | `CNE_CZ_ZoneServerTeamRecruitTransport` | NON PROUVE | — | — |
| 238 | `CNE_CZ_ZoneServerChangePositionV2` | NON PROUVE | — | — |
| 239 | `CNE_CZ_ZoneServerFamilyTalkToNpc` | NON PROUVE | — | — |
| 240 | `CNE_CZ_ZoneServerFixEquipByFamily` | NON PROUVE | — | — |
| 241 | `CNE_CZ_ZoneServerBattleRoyaleJoinRequest` | NON PROUVE | — | — |
| 242 | `CNE_CZ_ZoneServerPlayerCCDataSave` | NON PROUVE | — | — |
| 243 | `CNE_CZ_ZoneServerEnterNodeFastDress` | GOLDEN | 2 | — |
| 244 | `CNE_CZ_ZoneServerApplyFastDressData` | NON PROUVE | — | — |
| 245 | `CNE_CZ_ZoneServerSaveFastDressData` | NON PROUVE | — | — |
| 246 | `CNE_CZ_ZoneServerRecordFastDressInfo` | NON PROUVE | — | — |
| 247 | `CNE_CZ_ZoneServerUpdateFastDressPage` | NON PROUVE | — | — |
| 248 | `CNE_CZ_ZoneServerRemoveFastDressInfo` | NON PROUVE | — | — |
| 249 | `CNE_CZ_ZoneServerMarryOperation` | NON PROUVE | — | — |
| 250 | `CNE_CZ_ZoneServerChurchReserve` | NON PROUVE | — | — |
| 251 | `CNE_CZ_ZoneServerWeddingInfoQuery` | NON PROUVE | — | — |
| 252 | `CNE_CZ_ZoneServerVersusInstanceJoinRequest` | NON PROUVE | — | — |
| 253 | `CNE_CZ_ZoneServerPayEventSelect` | NON PROUVE | — | — |
| 254 | `CNE_CZ_ZoneServerPayEventResponse` | NON PROUVE | — | — |
| 255 | `CNE_CZ_ZoneServerEvaluateLotteryOpen` | NON PROUVE | — | — |
| 256 | `CNE_CZ_ZoneServerEnterNodeStarWay` | GOLDEN | 2 | — |
| 257 | `CNE_CZ_ZoneServerExecuteMagicBox` | NON PROUVE | — | — |
| 258 | `CNE_CZ_ZoneServerDailyAwardOP` | GOLDEN | 2 | — |
| 259 | `CNE_CZ_ZoneServerBattleCrusadeJoin` | NON PROUVE | — | — |
| 260 | `CNE_CZ_ZoneServerArrangeItem` | NON PROUVE | — | — |
| 261 | `CNC_CZ_ClientElfCastEnchant` | NON PROUVE | — | — |
| 262 | `CNC_CZ_ClientTicketInvalid` | DESASM | — | `EntryHandlers.cs` |
| 263 | `CNC_CZ_ClientDisplayChar` | PASSTHRU | — | `ZoneEntryFlow.DisplayChar.cs` |
| 264 | `CNC_CZ_ClientMoveChar` | GOLDEN | 2 | `MovementHandlers.cs`, `ZoneEntryFlow.Items.cs`, `ZoneEntryFlow.Movement.cs` |
| 265 | `CNC_CZ_ClientSeeChar` | PASSTHRU | — | `ZoneEntryFlow.SeeChar.cs`, `ZoneEntryFlow.Sight.cs` |
| 266 | `CNC_CZ_ClientTextOutput` | DESASM | — | `GmHandlers.cs`, `MiscHandlers.cs`, `ZoneEntryFlow.Admin.cs`, `ZoneEntryFlow.FamilyInstance.cs` |
| 267 | `CNC_CZ_ClientGoto` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Migration.cs` |
| 268 | `CNC_CZ_ClientSay` | GOLDEN | 2 | `ZoneEntryFlow.Chat.cs`, `ZoneEntryFlow.Events.cs`, `ZoneEntryFlow.Probes.cs` |
| 269 | `CNC_CZ_ClientLifeEradicate` | DESASM | — | `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Elf.cs`, `ZoneEntryFlow.Sight.cs`, `ZoneEntryFlow.Summon.cs` |
| 270 | `CNC_CZ_ClientResetCoord` | DESASM | — | `ZoneEntryFlow.Movement.cs` |
| 271 | `CNC_CZ_ClientDisplayMonster` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Probes.cs` |
| 272 | `CNC_CZ_ClientMeleeAttack` | GOLDEN | 2 | `CombatHandlers.cs`, `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.Summon.cs` |
| 273 | `CNC_CZ_ClientRestore` | DESASM | — | `ZoneEntryFlow.BattleRoyale.cs`, `ZoneEntryFlow.Versus.cs` |
| 274 | `CNC_CZ_ClientPutItemIntoInventory` | INCOMPLET | 2 | `NativeItemCodec.cs`, `InventoryHandlers.cs`, `ShopHandlers.cs`, `ZoneEntryFlow.Collection.cs`, `ZoneEntryFlow.FastDress.cs`, `ZoneEntryFlow.Mission.cs`, `ZoneEntryFlow.Vending.cs`, `ZoneEntryFlow.Work.cs` |
| 275 | `CNC_CZ_ClientDropItem` | DESASM | — | `InventoryHandlers.cs`, `ItemActionHandlers.cs`, `ShopHandlers.cs`, `SocialHandlers.cs`, `ZoneEntryFlow.Cube.cs`, `ZoneEntryFlow.FastDress.cs`, `ZoneEntryFlow.ItemsExpiry.cs`, `ZoneEntryFlow.MagicBox.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 276 | `CNC_CZ_ClientChangeEquip` | PASSTHRU | — | `InventoryHandlers.Helpers.cs` |
| 277 | `CNC_CZ_ClientSwitchItemLocationOK` | GOLDEN | 2 | `InventoryHandlers.Helpers.cs`, `InventoryHandlers.cs`, `ItemActionHandlers.cs` |
| 278 | `CNC_CZ_ClientSpellCast` | DESASM | — | `CombatHandlers.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.Spells.cs` |
| 279 | `CNC_CZ_ClientSpellCastOK` | GOLDEN | 2 | `CombatHandlers.cs`, `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.Spells.cs` |
| 280 | `CNC_CZ_ClientUpdateAttr` | GOLDEN | 2 | `CombatHandlers.cs`, `EntryHandlers.cs`, `GmHandlers.cs`, `InstanceHandlers.cs`, `PlayerHandlers.cs`, `ShopHandlers.cs`, `SocialHandlers.cs`, `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.Mission.cs`, `ZoneEntryFlow.Summon.cs` |
| 281 | `CNC_CZ_ClientSpellHitToLife` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Spells.cs`, `ZoneEntryFlow.Combat.cs` |
| 282 | `CNC_CZ_ClientUpdateEquipment` | PASSTHRU | — | `InventoryHandlers.Helpers.cs` |
| 283 | `CNC_CZ_ClientModelModify` | GOLDEN | 2 | `ItemActionHandlers.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 284 | `CNC_CZ_ClientLevelUp` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Rewards.cs` |
| 285 | `CNC_CZ_ClientDisplayDialogView` | GOLDEN | 2 | `ZoneEntryFlow.Dialogue.cs`, `ZoneEntryFlow.Mission.cs` |
| 286 | `CNC_CZ_ClientGetSpell` | GOLDEN | 2 | `InstanceHandlers.cs`, `ItemActionHandlers.cs`, `PlayerHandlers.cs`, `ZoneEntryFlow.Services.cs` |
| 287 | `CNC_CZ_ClientSpellBreak` | DESASM | — | `ZoneEntryFlow.Combat.Spells.cs` |
| 288 | `CNC_CZ_ClientSpellBlastOK` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.Spells.cs` |
| 289 | `CNC_CZ_ClientSpellBlastToLife` | DESASM | — | `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.Spells.cs` |
| 290 | `CNC_CZ_ClientPlayerAction` | DESASM | — | `PlayerHandlers.cs`, `ZoneEntryFlow.Cube.cs`, `ZoneEntryFlow.Movement.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 291 | `CNC_CZ_ClientModifyItemDurability` | DESASM | — | `InventoryHandlers.cs`, `ItemActionHandlers.cs`, `ShopHandlers.cs` |
| 292 | `CNC_CZ_ClientEffectReport` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Spells.cs` |
| 293 | `CNC_CZ_ClientNodeExit` | GOLDEN | 2 | `ZoneEntryFlow.Migration.cs` |
| 294 | `CNC_CZ_ClientLeaveNode` | NON PROUVE | — | — |
| 295 | `CNC_CZ_ClientDestShutdown` | NON PROUVE | — | — |
| 296 | `CNC_CZ_ClientAttrRefresh` | GOLDEN | 2 | — |
| 297 | `CNC_CZ_ClientSpellBlastToLocation` | DESASM | — | `ZoneEntryFlow.Combat.Spells.Resolution.cs` |
| 298 | `CNC_CZ_ClientSeeMob` | GOLDEN | 2 | `ZoneEntryFlow.Sight.cs` |
| 299 | `CNC_CZ_ClientEnchantAdd` | GOLDEN | 2 | `ItemActionHandlers.cs`, `ZoneEntryFlow.Events.cs`, `ZoneEntryFlow.Items.cs`, `ZoneEntryFlow.Status.cs` |
| 300 | `CNC_CZ_ClientEnchantRemove` | GOLDEN | 2 | `CombatHandlers.cs`, `InventoryHandlers.cs`, `ItemActionHandlers.cs`, `MiscHandlers.cs`, `ZoneEntryFlow.Combat.Ride.cs`, `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.Events.cs`, `ZoneEntryFlow.Items.cs`, `ZoneEntryFlow.Status.cs` |
| 301 | `CNC_CZ_ClientEnchantUpdate` | GOLDEN | 2 | `MiscHandlers.cs`, `ZoneEntryFlow.DisplayChar.cs`, `ZoneEntryFlow.cs` |
| 302 | `CNC_CZ_ClientFlagModify` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.Migration.cs` |
| 303 | `CNC_CZ_ClientItemUse` | GOLDEN | 2 | `ItemActionHandlers.cs` |
| 304 | `CNC_CZ_ClientEnterShop` | DESASM | — | `ShopHandlers.cs`, `ZoneEntryFlow.Services.cs` |
| 305 | `CNC_CZ_ClientDie` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Death.cs` |
| 306 | `CNC_CZ_ClientRevive` | DESASM | — | `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Items.cs` |
| 307 | `CNC_CZ_ClientPlayerEmotion` | DESASM | — | `PlayerHandlers.cs` |
| 308 | `CNC_CZ_ClientBuyUp` | DESASM | — | `ShopHandlers.cs` |
| 309 | `CNC_CZ_ClientSellOut` | DESASM | — | `ShopHandlers.cs` |
| 310 | `CNC_CZ_ClientEnterNodePlayerData` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 311 | `CNC_CZ_ClientEnterNodeEquipAndSpell` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 312 | `CNC_CZ_ClientEnterNodeInventory` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 313 | `CNC_CZ_ClientEnterNodeNodeData` | GOLDEN | 2 | `ZoneEntryFlow.Admin.cs`, `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Migration.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 314 | `CNC_CZ_ClientCanCloseConnection` | NON PROUVE | — | — |
| 315 | `CNC_CZ_ClientEnchantReset` | NON PROUVE | — | — |
| 316 | `CNC_CZ_ClientTextOutputIndex` | GOLDEN | 2 | `FamilyHandlers.cs`, `GmHandlers.cs`, `InventoryHandlers.cs`, `ItemActionHandlers.cs`, `MiscHandlers.cs`, `ShopHandlers.cs`, `SocialHandlers.cs`, `TeamHandlers.cs`, `TradeHandlers.cs`, `ZoneEntryFlow.BattleRoyale.cs`, `ZoneEntryFlow.Collection.cs`, `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Combat.Rewards.cs`, `ZoneEntryFlow.Destiny.cs`, `ZoneEntryFlow.Elf.Mix.cs`, `ZoneEntryFlow.Evaluate.cs`, `ZoneEntryFlow.FamilyInstance.cs`, `ZoneEntryFlow.ItemMall.cs`, `ZoneEntryFlow.ItemsExpiry.cs`, `ZoneEntryFlow.License.cs`, `ZoneEntryFlow.LifeSkill.cs`, `ZoneEntryFlow.Lottery.cs`, `ZoneEntryFlow.Migration.cs`, `ZoneEntryFlow.Recommend.cs`, `ZoneEntryFlow.Recruit.cs`, `ZoneEntryFlow.Services.cs`, `ZoneEntryFlow.Treasure.Roll.cs`, `ZoneEntryFlow.Versus.cs` |
| 317 | `CNC_CZ_ClientTeamLeaved` | DESASM | — | `TeamHandlers.cs`, `ZoneEntryFlow.Recruit.cs` |
| 318 | `CNC_CZ_ClientTeamInviteNotice` | DESASM | — | `TeamHandlers.cs` |
| 319 | `CNC_CZ_ClientTeamInviteResult` | NON PROUVE | — | — |
| 320 | `CNC_CZ_ClientSafeQuitCancel` | DESASM | — | `PlayerHandlers.cs` |
| 321 | `CNC_CZ_ClientSafeQuitResp` | DESASM | — | `PlayerHandlers.cs` |
| 322 | `CNC_CZ_ClientNonSafeQuit` | NON PROUVE | — | — |
| 323 | `CNC_CZ_ClientDisplayCharSimple` | NON PROUVE | — | — |
| 324 | `CNC_CZ_ClientTeamAllMembersInfo` | DESASM | — | `TeamHandlers.cs` |
| 325 | `CNC_CZ_ClientTeamNewMemberInfo` | NON PROUVE | — | — |
| 326 | `CNC_CZ_ClientMovePrediction` | GOLDEN | 2 | `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.Movement.cs` |
| 327 | `CNC_CZ_ClientChannelMsg` | DESASM | — | `SocialHandlers.cs`, `ZoneEntryFlow.Chat.cs` |
| 328 | `CNC_CZ_ClientModifyAttr` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Spells.Resolution.cs` |
| 329 | `CNC_CZ_ClientSeeTreasure` | DESASM | — | `ZoneEntryFlow.Sight.cs`, `ZoneEntryFlow.Treasure.cs` |
| 330 | `CNC_CZ_ClientTreasureDisappear` | DESASM | — | `ZoneEntryFlow.Sight.cs` |
| 331 | `CNC_CZ_ClientTradeRequest` | DESASM | — | `TradeHandlers.cs` |
| 332 | `CNC_CZ_ClientTradeStart` | DESASM | — | `TradeHandlers.cs` |
| 333 | `CNC_CZ_ClientTradeAddItem` | DESASM | — | `TradeHandlers.cs` |
| 334 | `CNC_CZ_ClientTradeRemoveItem` | DESASM | — | `TradeHandlers.cs` |
| 335 | `CNC_CZ_ClientTradeAddItemFail` | DESASM | — | `TradeHandlers.cs` |
| 336 | `CNC_CZ_ClientTradeModifyGold` | DESASM | — | `TradeHandlers.cs` |
| 337 | `CNC_CZ_ClientTradeModifyGoldFail` | DESASM | — | `TradeHandlers.cs` |
| 338 | `CNC_CZ_ClientTradeConfirm` | DESASM | — | `TradeHandlers.cs` |
| 339 | `CNC_CZ_ClientTradeCancel` | DESASM | — | `TradeHandlers.cs` |
| 340 | `CNC_CZ_ClientTradeComplete` | NON PROUVE | — | — |
| 341 | `CNC_CZ_ClientResurrect` | DESASM | — | `PlayerHandlers.cs` |
| 342 | `CNC_CZ_ClientAskRepairEquipGold` | DESASM | — | `InventoryHandlers.cs` |
| 343 | `CNC_CZ_ClientAskRepairOneItemGold` | DESASM | — | `InventoryHandlers.cs` |
| 344 | `CNC_CZ_ClientUpdateSystemVar` | GOLDEN | 2 | — |
| 345 | `CNC_CZ_ClientUpdateNPCSymbols` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 346 | `CNC_CZ_ClientChangeFamily` | DESASM | — | `FamilyHandlers.cs`, `ZoneEntryFlow.Family.cs` |
| 347 | `CNC_CZ_ClientEnterFamilyCreation` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 348 | `CNC_CZ_ClientMissionStates` | GOLDEN | 2 | `TeamHandlers.cs`, `ZoneEntryFlow.Mission.cs` |
| 349 | `CNC_CZ_ClientEnterSpellShop` | DESASM | — | `ZoneEntryFlow.Items.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 350 | `CNC_CZ_ClientEnterRepairShop` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 351 | `CNC_CZ_ClientEnterNodeShortcut` | GOLDEN | 2 | `ZoneMessages.g.cs`, `EntryHandlers.cs`, `ZoneEntryFlow.Services.cs` |
| 352 | `CNC_CZ_ClientReceiveShortcutOfPage` | DESASM | — | `PlayerHandlers.cs` |
| 353 | `CNC_CZ_ClientTradeReset` | DESASM | — | `TradeHandlers.cs` |
| 354 | `CNC_CZ_ClientUseSkillPointOK` | NON PROUVE | — | — |
| 355 | `CNC_CZ_ClientLogoutResp` | DESASM | — | `PlayerHandlers.cs` |
| 356 | `CNC_CZ_ClientCanLogout` | DESASM | — | `ZoneEntryFlow.Versus.cs` |
| 357 | `CNC_CZ_ClientEnterStorage` | DESASM | — | `ZoneEntryFlow.Dialogue.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 358 | `CNC_CZ_ClientGetStorageData` | INCOMPLET | 2 | `InventoryHandlers.Helpers.cs` |
| 359 | `CNC_CZ_ClientPutItemIntoStorage` | NON PROUVE | — | — |
| 360 | `CNC_CZ_ClientPutItemIntoContainer` | NON PROUVE | — | — |
| 361 | `CNC_CZ_ClientSpellLoopCastStop` | NON PROUVE | — | — |
| 362 | `CNC_CZ_ClientQueryNpc` | NON PROUVE | — | — |
| 363 | `CNC_CZ_ClientAskIdentifyGold` | DESASM | — | `InventoryHandlers.cs` |
| 364 | `CNC_CZ_ClientIdentifyItem` | DESASM | — | `ItemActionHandlers.cs` |
| 365 | `CNC_CZ_ClientEnterIdentifyShop` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 366 | `CNC_CZ_ClientEnterExchageItemService` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 367 | `CNC_CZ_ClientResetSpell` | NON PROUVE | — | — |
| 368 | `CNC_CZ_ClientUpdateCoolDown` | GOLDEN | 2 | `ItemCooldownService.cs`, `SpellCooldownService.cs` |
| 369 | `CNC_CZ_ClientModifyItemInfo` | PASSTHRU | — | `EquipTrainHandlers.cs`, `ZoneEntryFlow.Combat.Rewards.cs`, `ZoneEntryFlow.MagicBox.cs` |
| 370 | `CNC_CZ_ClientEnterInlayShop` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 371 | `CNC_CZ_ClientInlayItemResult` | DESASM | — | `ZoneEntryFlow.Strengthen.cs` |
| 372 | `CNC_CZ_ClientTeamMemberEnchant` | PASSTHRU | — | `ZoneEntryFlow.Status.cs` |
| 373 | `CNC_CZ_ClientStorageExpandResult` | DESASM | — | `InventoryHandlers.cs` |
| 374 | `CNC_CZ_ClientSloganUpdate` | NON PROUVE | — | — |
| 375 | `CNC_CZ_ClientVendorClose` | DESASM | — | `ShopHandlers.cs`, `ZoneEntryFlow.Vending.cs` |
| 376 | `CNC_CZ_ClientVendorCloseView` | DESASM | — | `ZoneEntryFlow.Vending.cs` |
| 377 | `CNC_CZ_ClientVendorStatus` | DESASM | — | `ZoneEntryFlow.Vending.cs` |
| 378 | `CNC_CZ_ClientVendorUpdateView` | PASSTHRU | — | `ZoneEntryFlow.Vending.cs` |
| 379 | `CNC_CZ_ClientVendorContainer` | NON PROUVE | — | — |
| 380 | `CNC_CZ_ClientMobTransform` | NON PROUVE | — | — |
| 381 | `CNC_CZ_ClientTeamInfo` | DESASM | — | `TeamHandlers.cs` |
| 382 | `CNC_CZ_ClientMigrationDone` | GOLDEN | 2 | `EntryHandlers.cs` |
| 383 | `CNC_CZ_ClientPlayEffect` | PASSTHRU | — | `ZoneEntryFlow.Summon.cs` |
| 384 | `CNC_CZ_ClientSelectFamilyEmblem` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 385 | `CNC_CZ_ClientOpenExploitRank` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 386 | `CNC_CZ_ClientTreasurePickupOK` | DESASM | — | `ZoneEntryFlow.Treasure.Roll.cs`, `ZoneEntryFlow.Treasure.cs` |
| 387 | `CNC_CZ_ClientTeamRadarAttention` | DESASM | — | `MiscHandlers.cs` |
| 388 | `CNC_CZ_ClientEnterMissionList` | GOLDEN | 2 | `ZoneEntryFlow.Mission.Commands.cs` |
| 389 | `CNC_CZ_ClientBagDueDate` | DESASM | — | `MiscHandlers.cs` |
| 390 | `CNC_CZ_ClientUpdateInteraction` | NON PROUVE | — | — |
| 391 | `CNC_CZ_ClientUpdateMissionState` | GOLDEN | 2 | `ZoneEntryFlow.Mission.cs` |
| 392 | `CNC_CZ_ClientTeamMemberPosition` | DESASM | — | `ZoneEntryFlow.Team.cs` |
| 393 | `CNC_CZ_ClientEnterTransport` | DESASM | — | `ShopHandlers.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 394 | `CNC_CZ_ClientTradeReady` | DESASM | — | `TradeHandlers.cs` |
| 395 | `CNC_CZ_ClientTradeUnConfirm` | DESASM | — | `TradeHandlers.cs` |
| 396 | `CNC_CZ_ClientChangeAppellation` | DESASM | — | `ZoneEntryFlow.Titles.cs` |
| 397 | `CNC_CZ_ClientUpdateAppellation` | DESASM | — | `ZoneEntryFlow.Titles.cs` |
| 398 | `CNC_CZ_ClientAddAppellation` | DESASM | — | `ZoneEntryFlow.Mission.Commands.cs` |
| 399 | `CNC_CZ_ClientMailOpenBox` | DESASM | — | `SocialHandlers.cs`, `ZoneEntryFlow.Dialogue.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 400 | `CNC_CZ_ClientMailList` | DESASM | — | `ZoneEntryFlow.Mission.cs` |
| 401 | `CNC_CZ_ClientMailRead` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 402 | `CNC_CZ_ClientMailDeleteResult` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 403 | `CNC_CZ_ClientMailSendResult` | DESASM | — | `SocialHandlers.cs` |
| 404 | `CNC_CZ_ClientMailReceive` | GOLDEN | 2 | `ZoneEntryFlow.Mission.cs` |
| 405 | `CNC_CZ_ClientMailGetItemOK` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 406 | `CNC_CZ_ClientMailGetGoldOK` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 407 | `CNC_CZ_ClientUseItemTime` | NON PROUVE | — | — |
| 408 | `CNC_CZ_ClientUseItemTimeBreak` | NON PROUVE | — | — |
| 409 | `CNC_CZ_ClientMailReturnResult` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 410 | `CNC_CZ_ClientCommandFailReturn` | DESASM | — | `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.Spells.cs`, `ZoneEntryFlow.DisplayChar.cs` |
| 411 | `CNC_CZ_ClientSearchInfo` | PASSTHRU | — | `MiscHandlers.cs` |
| 412 | `CNC_CZ_ClientEnterNodeElfInfo` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 413 | `CNC_CZ_ClientElfAdd` | PASSTHRU | — | `ZoneEntryFlow.Elf.cs` |
| 414 | `CNC_CZ_ClientElfReturnOK` | DESASM | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 415 | `CNC_CZ_ClientElfWorkResult` | NON PROUVE | — | — |
| 416 | `CNC_CZ_ClientElfWorkResultEnd` | DESASM | — | `ElfHandlers.cs` |
| 417 | `CNC_CZ_ClientElfEvent` | NON PROUVE | — | — |
| 418 | `CNC_CZ_ClientElfUpdateAttr` | DESASM | — | `InventoryHandlers.cs`, `ZoneEntryFlow.Elf.cs` |
| 419 | `CNC_CZ_ClientElfUpdateSkill` | NON PROUVE | — | — |
| 420 | `CNC_CZ_ClientElfUpdateSoulAbility` | NON PROUVE | — | — |
| 421 | `CNC_CZ_ClientElfGoToWork` | NON PROUVE | — | — |
| 422 | `CNC_CZ_ClientElfDecompose` | NON PROUVE | — | — |
| 423 | `CNC_CZ_ClientElfStoneUse` | NON PROUVE | — | — |
| 424 | `CNC_CZ_ClientEnterNodeElfContainer` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 425 | `CNC_CZ_ClientAuctionBuyList` | DESASM | — | `ShopHandlers.cs` |
| 426 | `CNC_CZ_ClientAuctionBuyOK` | DESASM | — | `ShopHandlers.cs` |
| 427 | `CNC_CZ_ClientAuctionCancelOK` | DESASM | — | `ShopHandlers.cs` |
| 428 | `CNC_CZ_ClientAuctionSellList` | DESASM | — | `ShopHandlers.cs` |
| 429 | `CNC_CZ_ClientAuctionSellResult` | DESASM | — | `ShopHandlers.cs` |
| 430 | `CNC_CZ_ClientAuctionHouse` | DESASM | — | `ZoneEntryFlow.Dialogue.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 431 | `CNC_CZ_ClientChangeElf` | DESASM | — | `ElfHandlers.cs` |
| 432 | `CNC_CZ_ClientEnterStrengthen` | DESASM | — | `SocialHandlers.cs`, `ZoneEntryFlow.Lottery.cs`, `ZoneEntryFlow.LuckyBag.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 433 | `CNC_CZ_ClientStrengthenCasting` | DESASM | — | `ZoneEntryFlow.CombineStation.cs`, `ZoneEntryFlow.Cube.cs`, `ZoneEntryFlow.LifeSkill.cs`, `ZoneEntryFlow.Lottery.cs`, `ZoneEntryFlow.MagicBox.cs`, `ZoneEntryFlow.Strengthen.cs`, `ZoneEntryFlow.Work.cs` |
| 434 | `CNC_CZ_ClientStrengthenItemCancel` | DESASM | — | `ZoneEntryFlow.CombineStation.cs`, `ZoneEntryFlow.Cube.cs`, `ZoneEntryFlow.LifeSkill.cs`, `ZoneEntryFlow.Lottery.cs`, `ZoneEntryFlow.LuckyBag.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 435 | `CNC_CZ_ClientStrengthenItemResult` | DESASM | — | `ZoneEntryFlow.CombineStation.cs`, `ZoneEntryFlow.Cube.cs`, `ZoneEntryFlow.LifeSkill.cs`, `ZoneEntryFlow.Strengthen.cs` |
| 436 | `CNC_CZ_ClientModifyItemStrengthen` | DESASM | — | `ZoneEntryFlow.Strengthen.cs` |
| 437 | `CNC_CZ_ClientSetSpellStone` | NON PROUVE | — | — |
| 438 | `CNC_CZ_ClientSummonPet` | DESASM | — | `ZoneEntryFlow.Summon.cs` |
| 439 | `CNC_CZ_ClientEnterElfReturn` | DESASM | — | `ZoneEntryFlow.Items.cs` |
| 440 | `CNC_CZ_ClientMobChangeAI` | NON PROUVE | — | — |
| 441 | `CNC_CZ_ClientTurnChar` | DESASM | — | `PlayerHandlers.cs`, `ZoneEntryFlow.cs` |
| 442 | `CNC_CZ_ClientElfAction` | NON PROUVE | — | — |
| 443 | `CNC_CZ_ClientElfReturnEffect` | NON PROUVE | — | — |
| 444 | `CNC_CZ_ClientGetSelectLifeTarget` | GOLDEN | 2 | `PlayerHandlers.cs`, `ZoneEntryFlow.Combat.Aggro.cs`, `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Combat.Mob.cs`, `ZoneEntryFlow.Combat.Spells.Resolution.cs`, `ZoneEntryFlow.Combat.cs`, `ZoneEntryFlow.cs` |
| 445 | `CNC_CZ_ClientEnchantPause` | DESASM | — | `CombatHandlers.cs` |
| 446 | `CNC_CZ_ClientEnchantStart` | DESASM | — | `CombatHandlers.cs` |
| 447 | `CNC_CZ_ClientPVPTime` | NON PROUVE | — | — |
| 448 | `CNC_CZ_ClientLotteryResult` | DESASM | — | `ZoneEntryFlow.Lottery.cs` |
| 449 | `CNC_CZ_ClientLotteryEnd` | DESASM | — | `ZoneEntryFlow.Lottery.cs` |
| 450 | `CNC_CZ_ClientLotteryUpdatePlus` | GOLDEN | 2 | `ZoneEntryFlow.Lottery.cs` |
| 451 | `CNC_CZ_ClientDuelRequest` | DESASM | — | `ZoneEntryFlow.Duel.cs` |
| 452 | `CNC_CZ_ClientDuelRequestResult` | DESASM | — | `ZoneEntryFlow.Duel.cs` |
| 453 | `CNC_CZ_ClientDuelBegin` | DESASM | — | `ZoneEntryFlow.Duel.cs` |
| 454 | `CNC_CZ_ClientDuelResult` | DESASM | — | `ZoneEntryFlow.Duel.cs` |
| 455 | `CNC_CZ_ClientEnterMessageBoard` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 456 | `CNC_CZ_ClientMessageList` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 457 | `CNC_CZ_ClientMessageWriteResult` | DESASM | — | `MiscHandlers.cs` |
| 458 | `CNC_CZ_ClientMessageRead` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 459 | `CNC_CZ_ClientMessageDeleteResult` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 460 | `CNC_CZ_ClientLookEquip` | DESASM | — | `MiscHandlers.cs` |
| 461 | `CNC_CZ_ClientItemRent` | DESASM | — | `ItemActionHandlers.cs` |
| 462 | `CNC_CZ_ClientBattlefieldList` | DESASM | — | `InstanceHandlers.cs` |
| 463 | `CNC_CZ_ClientEnterBattlefield` | NON PROUVE | — | — |
| 464 | `CNC_CZ_ClientBattlefieldJoinResult` | DESASM | — | `InstanceHandlers.cs` |
| 465 | `CNC_CZ_ClientBattlefieldResult` | NON PROUVE | — | — |
| 466 | `CNC_CZ_ClientBattlefieldUpdateScore` | NON PROUVE | — | — |
| 467 | `CNC_CZ_ClientAltarState` | NON PROUVE | — | — |
| 468 | `CNC_CZ_ClientBattleMemberPosition` | NON PROUVE | — | — |
| 469 | `CNC_CZ_ClientBattlefieldMemberInfo` | NON PROUVE | — | — |
| 470 | `CNC_CZ_ClientPlayElfPetAction` | DESASM | — | `ZoneEntryFlow.Elf.cs` |
| 471 | `CNC_CZ_ClientUpdateAdvancedAbility` | PASSTHRU | — | `ZoneEntryFlow.AdvancedAbility.cs` |
| 472 | `CNC_CZ_ClientUpdateCollection` | GOLDEN | 2 | `ZoneEntryFlow.Collection.cs` |
| 473 | `CNC_CZ_ClientHello` | GOLDEN | 2 | `EntryHandlers.cs`, `ZoneEntryFlow.Lifecycle.cs` |
| 474 | `CNC_CZ_ClientScreenMessage` | DESASM | — | `ZoneEntryFlow.Mission.cs` |
| 475 | `CNC_CZ_ClientLoverInvite` | NON PROUVE | — | — |
| 476 | `CNC_CZ_ClientLoverUpdate` | NON PROUVE | — | — |
| 477 | `CNC_CZ_ClientLoverInfo` | NON PROUVE | — | — |
| 478 | `CNC_CZ_ClientLoverBreak` | NON PROUVE | — | — |
| 479 | `CNC_CZ_ClientLoverPosition` | NON PROUVE | — | — |
| 480 | `CNC_CZ_ClientAchievementUpdate` | INCOMPLET | 2 | — |
| 481 | `CNC_CZ_ClientTextOutputItem` | NON PROUVE | — | — |
| 482 | `CNC_CZ_ClientUpdateBFData` | INCOMPLET | 2 | `ZoneEntryFlow.PlayerGuide.cs` |
| 483 | `CNC_CZ_ClientEnterFamilyBuilding` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 484 | `CNC_CZ_ClientFamilyContainerData` | PASSTHRU | — | `ZoneEntryFlow.Dialogue.cs`, `ZoneEntryFlow.FamilyInstance.cs` |
| 485 | `CNC_CZ_ClientFamilyContainerDeleteItem` | NON PROUVE | — | — |
| 486 | `CNC_CZ_ClientFamilyContainerPutItem` | NON PROUVE | — | — |
| 487 | `CNC_CZ_ClientFamilyInstanceData` | INCOMPLET | — | `FamilyHandlers.cs`, `ZoneEntryFlow.FamilyInstance.cs` |
| 488 | `CNC_CZ_ClientPlayerClosets` | GOLDEN | 2 | `ZoneEntryFlow.Services.cs` |
| 489 | `CNC_CZ_ClientPlayerClassList` | GOLDEN | 2 | `ZoneEntryFlow.Services.cs` |
| 490 | `CNC_CZ_ClientEnterNodePlayerCard` | GOLDEN | 2 | `EntryHandlers.cs` |
| 491 | `CNC_CZ_ClientQueryPlayerCard` | PASSTHRU | — | `ZoneEntryFlow.Mission.cs` |
| 492 | `CNC_CZ_ClientBaseAttrRefresh` | GOLDEN | 2 | — |
| 493 | `CNC_CZ_ClientAttrLevelRefresh` | GOLDEN | 2 | — |
| 494 | `CNC_CZ_ClientPlaySound` | NON PROUVE | — | — |
| 495 | `CNC_CZ_ClientPlayMonsterAction` | DESASM | — | `ZoneEntryFlow.Combat.Mob.cs` |
| 496 | `CNC_CZ_ClientEnterWorks` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 497 | `CNC_CZ_ClientPlayerWorkUpdate` | PASSTHRU | — | `ZoneEntryFlow.Work.cs` |
| 498 | `CNC_CZ_ClientPlayerWorkResult` | PASSTHRU | — | `ZoneEntryFlow.Work.cs` |
| 499 | `CNC_CZ_ClientPlayerEvent` | NON PROUVE | — | — |
| 500 | `CNC_CZ_ClientPlayerWorkResultEnd` | DESASM | — | `ZoneEntryFlow.Work.cs` |
| 501 | `CNC_CZ_ClientEnterFamilyInstanceCreation` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 502 | `CNC_CZ_ClientFamilyTownQuery` | PASSTHRU | — | `ZoneEntryFlow.FamilyInstance.cs` |
| 503 | `CNC_CZ_ClientFamilyTownMemberQuery` | PASSTHRU | — | `ZoneEntryFlow.FamilyInstance.cs` |
| 504 | `CNC_CZ_ClientFamilyStorageDeleteItem` | DESASM | — | `InventoryHandlers.cs` |
| 505 | `CNC_CZ_ClientFamilyStoragePutItem` | INCOMPLET | — | `InventoryHandlers.cs` |
| 506 | `CNC_CZ_ClientFamilyStorageLog` | DESASM | — | `FamilyHandlers.cs`, `InventoryHandlers.cs` |
| 507 | `CNC_CZ_ClientEnterFamilyStorage` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 508 | `CNC_CZ_ClientFamilyStorageData` | INCOMPLET | — | `ZoneEntryFlow.Dialogue.cs` |
| 509 | `CNC_CZ_ClientPlayerLicense` | GOLDEN | 2 | `GmHandlers.cs`, `ZoneEntryFlow.Combat.Rewards.cs`, `ZoneEntryFlow.License.cs`, `ZoneEntryFlow.Services.cs` |
| 510 | `CNC_CZ_ClientChangeClassShortcut` | GOLDEN | 2 | `ZoneEntryFlow.Services.cs` |
| 511 | `CNC_CZ_ClientUpdateLottery` | PASSTHRU | — | `ZoneEntryFlow.Lottery.cs` |
| 512 | `CNC_CZ_ClientBuyBackList` | PASSTHRU | — | `ShopHandlers.cs` |
| 513 | `CNC_CZ_ClientPetShortCut` | DESASM | — | `ZoneEntryFlow.Elf.cs` |
| 514 | `CNC_CZ_ClientEnterFamilyTownList` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 515 | `CNC_CZ_ClientTerritoryQuery` | PASSTHRU | — | `ZoneEntryFlow.Territory.cs` |
| 516 | `CNC_CZ_ClientTerritoryInfo` | DESASM | — | `ZoneEntryFlow.Territory.War.cs` |
| 517 | `CNC_CZ_ClientCanJoinTeam` | NON PROUVE | — | — |
| 518 | `CNC_CZ_ClientSearchTeamInfo` | DESASM | — | `MiscHandlers.cs` |
| 519 | `CNC_CZ_ClientTeamRecruitMsg` | GOLDEN | 2 | `MiscHandlers.cs` |
| 520 | `CNC_CZ_ClientRollTreasure` | DESASM | — | `ZoneEntryFlow.Treasure.Roll.cs` |
| 521 | `CNC_CZ_ClientRankInfo` | PASSTHRU | — | `ZoneEntryFlow.Room.cs` |
| 522 | `CNC_CZ_ClientHateReport` | NON PROUVE | — | — |
| 523 | `CNC_CZ_ClientEnterNodePlayerRoom` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 524 | `CNC_CZ_ClientEnterGladiatorTeamCreation` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 525 | `CNC_CZ_ClientGladiatorTeamInfo` | DESASM | — | `ZoneEntryFlow.Evaluate.cs` |
| 526 | `CNC_CZ_ClientDisbandGladiatorTeam` | NON PROUVE | — | — |
| 527 | `CNC_CZ_ClientEnterColosseum` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 528 | `CNC_CZ_ClientColosseumJoinResult` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 529 | `CNC_CZ_ClientColosseumTeamInfo` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 530 | `CNC_CZ_ClientColosseumLeaveJoinResult` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 531 | `CNC_CZ_ClientReviveInfo` | GOLDEN | 2 | `ZoneEntryFlow.Combat.Death.cs`, `ZoneEntryFlow.Events.cs`, `ZoneEntryFlow.Mission.Commands.cs` |
| 532 | `CNC_CZ_ClientTerritoryKilledInfo` | DESASM | — | `ZoneEntryFlow.Territory.War.cs` |
| 533 | `CNC_CZ_ClientCoRideInvite` | NON PROUVE | — | — |
| 534 | `CNC_CZ_ClientPlayerDestinyRoad` | DESASM | — | `ZoneEntryFlow.Destiny.cs` |
| 535 | `CNC_CZ_ClientPlayerDestinyRoadEvent` | DESASM | — | `ZoneEntryFlow.Destiny.cs` |
| 536 | `CNC_CZ_ClientUpdateZoneMonster` | NON PROUVE | — | — |
| 537 | `CNC_CZ_ClientSearchRoomResult` | PASSTHRU | — | `ZoneEntryFlow.Room.cs` |
| 538 | `CNC_CZ_ClientUpdateRoom` | PASSTHRU | — | `ZoneEntryFlow.Room.cs` |
| 539 | `CNC_CZ_ClientOpenHotel` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 540 | `CNC_CZ_ClientSetRoomResult` | NON PROUVE | — | — |
| 541 | `CNC_CZ_ClientRoomData` | PASSTHRU | — | `ZoneEntryFlow.Room.cs` |
| 542 | `CNC_CZ_ClientUpdateRoomItem` | PASSTHRU | — | `ZoneEntryFlow.Room.cs` |
| 543 | `CNC_CZ_ClientPlayerLifeSkill` | GOLDEN | 2 | `ZoneEntryFlow.LifeSkill.cs` |
| 544 | `CNC_CZ_ClientLifeSkillUpdate` | DESASM | — | `ZoneEntryFlow.LifeSkill.cs` |
| 545 | `CNC_CZ_ClientRideSpell` | PASSTHRU | — | `ItemActionHandlers.cs`, `ZoneEntryFlow.Combat.Ride.cs` |
| 546 | `CNC_CZ_ClientSpellFlyToLocation` | NON PROUVE | — | — |
| 547 | `CNC_CZ_ClientTerritoryEnd` | DESASM | — | `ZoneEntryFlow.Territory.War.cs` |
| 548 | `CNC_CZ_ClientUpdateSpellAttr` | DESASM | — | `ZoneEntryFlow.SpellAttr.cs` |
| 549 | `CNC_CZ_ClientUpdateHealthOnlineTime` | NON PROUVE | — | — |
| 550 | `CNC_CZ_ClientResetDailyMission` | NON PROUVE | — | — |
| 551 | `CNC_CZ_ClientNewPlayerGuide` | GOLDEN | 2 | — |
| 552 | `CNC_CZ_ClientColosseumModeQueryResult` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 553 | `CNC_CZ_ClientRecruitInstanceResult` | DESASM | — | `ZoneEntryFlow.Recruit.cs` |
| 554 | `CNC_CZ_ClientUpdateLifelongPoint` | GOLDEN | 2 | `ZoneEntryFlow.Lifelong.cs` |
| 555 | `CNC_CZ_ClientUpdateLifelongGold` | DESASM | — | `ZoneEntryFlow.Admin.cs`, `ZoneEntryFlow.Lifelong.cs` |
| 556 | `CNC_CZ_ClientChanceLuckyBagResult` | DESASM | — | `ZoneEntryFlow.LuckyBag.cs` |
| 557 | `CNC_CZ_ClientUpdateFortuneBagCounter` | GOLDEN | 2 | `ZoneEntryFlow.FortuneBag.cs` |
| 558 | `CNE_CZ_ZoneServerQueryRecommendActivities` | GOLDEN | 2 | — |
| 559 | `CNC_CZ_ClientRecommendActivities` | INCOMPLET | 2 | `ZoneEntryFlow.Recommend.cs` |
| 560 | `CNC_CZ_ClientUpdateRecommendActivities` | DESASM | — | `ZoneEntryFlow.Lifelong.cs`, `ZoneEntryFlow.LuckyBag.cs`, `ZoneEntryFlow.Recommend.cs` |
| 561 | `CNC_CZ_ClientFamilyBattleInfo` | PASSTHRU | — | `ZoneEntryFlow.Territory.cs` |
| 562 | `CNC_CZ_ClientFamilyBattleKilledRank` | DESASM | — | `ZoneEntryFlow.Territory.War.cs` |
| 563 | `CNC_CZ_ClientUpdateNodeTimes` | GOLDEN | 2 | `ZoneEntryFlow.Evaluate.cs`, `ZoneEntryFlow.Migration.cs` |
| 564 | `CNC_CZ_ClientOpenInstanceDialog` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 565 | `CNC_CZ_ClientR25UpdateFamily` | DESASM | — | `ZoneEntryFlow.Family.cs` |
| 566 | `CNC_CZ_ClientTerritoryFamilyScore` | DESASM | — | `ZoneEntryFlow.Territory.War.cs` |
| 567 | `CNC_CZ_ClientUpdateMixElf` | PASSTHRU | — | `ZoneEntryFlow.Elf.Work.cs` |
| 568 | `CNC_CZ_ClientUpdateElfMixElf` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 569 | `CNC_CZ_ClientLookElfPetEquip` | NON PROUVE | — | — |
| 570 | `CNC_CZ_ClientEquipTrainItemChanged` | DESASM | — | `EquipTrainHandlers.cs` |
| 571 | `CNC_CZ_ClientPVPRankLookEquip` | PASSTHRU | — | `ZoneEntryFlow.PvpRankEquip.cs` |
| 572 | `CNC_CZ_ClientLoadCubeItems` | DESASM | — | `ZoneEntryFlow.Cube.cs` |
| 573 | `CNC_CZ_ClientGreedyIslandInfo` | NON PROUVE | — | — |
| 574 | `CNC_CZ_ClientPetUpgradeCasting` | DESASM | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 575 | `CNC_CZ_ClientPetUpgradeCancel` | DESASM | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 576 | `CNC_CZ_ClientUpdateWarCoin` | NON PROUVE | — | — |
| 577 | `CNC_CZ_ClientTeamCheckNotify` | DESASM | — | `ZoneEntryFlow.Evaluate.cs` |
| 578 | `CNC_CZ_ClientUpdateFloorInfo` | NON PROUVE | — | — |
| 579 | `CNC_CZ_ClientOpenAreaDialog` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 580 | `CNC_CZ_ClientUpdateAlpacaCoin` | NON PROUVE | — | — |
| 581 | `CNC_CZ_ClientLifeFloat` | NON PROUVE | — | — |
| 582 | `CNC_CZ_ClientSearchTeamInfoV2` | DESASM | — | `TeamHandlers.cs` |
| 583 | `CNC_CZ_ClientTeamRecruitMsgV2` | GOLDEN | 2 | `TeamHandlers.cs` |
| 584 | `CNC_CZ_ClientCheckTeamTransport` | NON PROUVE | — | — |
| 585 | `CNC_CZ_ClientOpenMissionShop` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 586 | `CNC_CZ_ClientTerritoryPlayerInfo` | NON PROUVE | — | — |
| 587 | `CNC_CZ_ClientColosseumQuery` | PASSTHRU | — | `ZoneEntryFlow.Colosseum.cs` |
| 588 | `CNC_CZ_ClientUpdateElfSpell` | PASSTHRU | — | `ZoneEntryFlow.Elf.Spell.cs` |
| 589 | `CNC_CZ_ClientUpdateDestinyRoad` | GOLDEN | 2 | `ZoneEntryFlow.Destiny.cs` |
| 590 | `CNC_CZ_ClientDestinyDiceFailed` | DESASM | — | `ZoneEntryFlow.Destiny.cs` |
| 591 | `CNC_CZ_ClientBattleRoyaleInfo` | DESASM | — | `ZoneEntryFlow.BattleRoyale.cs` |
| 592 | `CNC_CZ_ClientPlayerClassCustomData` | INCOMPLET | 2 | `ZoneEntryFlow.CustomClass.cs`, `ZoneEntryFlow.Services.cs` |
| 593 | `CNC_CZ_ClientEnterNodeFastDress` | INCOMPLET | 2 | `ZoneEntryFlow.FastDress.cs` |
| 594 | `CNC_CZ_ClientUpdateFastDressData` | PASSTHRU | — | `ZoneEntryFlow.FastDress.cs` |
| 595 | `CNC_CZ_ClientInGameLotteryNotify` | NON PROUVE | — | — |
| 596 | `CNC_CZ_ClientDiamondLotteryNotify` | NON PROUVE | — | — |
| 597 | `CNC_CZ_ClientMarryOperation` | DESASM | — | `SocialHandlers.cs` |
| 598 | `CNC_CZ_ClientOpenChurchDialog` | NON PROUVE | — | — |
| 599 | `CNC_CZ_ClientWeddingsInfo` | PASSTHRU | — | `SocialHandlers.cs`, `ZoneEntryFlow.Mission.cs` |
| 600 | `CNC_CZ_ClientUpdateAttrNewExp` | GOLDEN | 2 | `CombatHandlers.cs`, `ZoneEntryFlow.Combat.Rewards.cs` |
| 601 | `CNC_CZ_ClientVersusInstanceInfo` | DESASM | — | `ZoneEntryFlow.Versus.cs` |
| 602 | `CNC_CZ_ClientOpenPaySystem` | DESASM | — | `ZoneEntryFlow.Dialogue.cs` |
| 603 | `CNC_CZ_ClientPayEvent` | NON PROUVE | — | — |
| 604 | `CNC_CZ_ClientPayEventOK` | DESASM | — | `ZoneEntryFlow.PayEvent.cs` |
| 605 | `CNC_CZ_ClientSlotFlicker` | NON PROUVE | — | — |
| 606 | `CNC_CZ_ClientUpdateAttrNewHp` | DESASM | — | `CombatHandlers.cs` |
| 607 | `CNC_CZ_ClientEvaluateInfo` | PASSTHRU | — | `ZoneEntryFlow.Evaluate.cs` |
| 608 | `CNC_CZ_ClientUpdateEvaluateInfo` | DESASM | — | `ZoneEntryFlow.Evaluate.cs` |
| 609 | `CNC_CZ_ClientEnterStarWayData` | INCOMPLET | 2 | `EntryHandlers.cs` |
| 610 | `CNC_CZ_ClientUIControlNotify` | NON PROUVE | — | — |
| 611 | `CNC_CZ_ClientChangeSpell` | NON PROUVE | — | — |
| 612 | `CNC_CZ_ClientOpenMagicBoxDialog` | PASSTHRU | — | `ZoneEntryFlow.Dialogue.cs` |
| 613 | `CNC_CZ_ClientMagicBoxResult` | PASSTHRU | — | `ZoneEntryFlow.MagicBox.cs` |
| 614 | `CNC_CZ_ClientBackItem` | NON PROUVE | — | — |
| 615 | `CNC_CZ_ClientUpdateDailyAwards` | INCOMPLET | 2 | `ZoneEntryFlow.DailyAward.cs` |
| 616 | `CNC_CZ_ClientUpdateBattleCrusadeInfo` | NON PROUVE | — | — |
| 617 | `CNC_CZ_ClientOpenBattleCrusadeDialog` | NON PROUVE | — | — |
| 618 | `CNC_CZ_ClientPetLotteryResult` | NON PROUVE | — | — |
| 619 | `CNC_CZ_ClientPetLotteryEnd` | NON PROUVE | — | — |
| 620 | `CNC_CZ_ClientArrangeItemOK` | DESASM | — | `InventoryHandlers.cs` |
| 621 | `CNC_T_ServerGetTicket` | NON PROUVE | — | — |
| 622 | `CNC_T_ServerUseTicket` | NON PROUVE | — | — |
| 623 | `CNE_T_ClientGetTicket` | NON PROUVE | — | — |
| 624 | `CNE_T_ClientUseTicket` | NON PROUVE | — | — |
| 625 | `CNC_ZW_WorldServerIDReport` | NON PROUVE | — | — |
| 626 | `CNC_ZW_WorldServerTell` | NON PROUVE | — | — |
| 627 | `CNC_ZW_WorldServerAllUsers` | NON PROUVE | — | — |
| 628 | `CNC_ZW_WorldServerNotifyEntry` | NON PROUVE | — | — |
| 629 | `CNC_ZW_WorldServerCharacterArrived` | NON PROUVE | — | — |
| 630 | `CNC_ZW_WorldServerResetTime` | NON PROUVE | — | — |
| 631 | `CNC_ZW_WorldServerReturnToCharacterList` | NON PROUVE | — | — |
| 632 | `CNC_ZW_WorldServerTeamLeavedNotify` | NON PROUVE | — | — |
| 633 | `CNC_ZW_WorldServerChannelMsg` | NON PROUVE | — | — |
| 634 | `CNC_ZW_WorldServerFamilyCreation` | NON PROUVE | — | — |
| 635 | `CNC_ZW_WorldServerLogout` | NON PROUVE | — | — |
| 636 | `CNC_ZW_WorldServerTextCommand` | PASSTHRU | — | `ZoneWorldLink.cs` |
| 637 | `CNC_ZW_WorldServerItemMallReceieveAttempt` | NON PROUVE | — | — |
| 638 | `CNC_ZW_WorldServerGetCloneItemID` | NON PROUVE | — | — |
| 639 | `CNC_ZW_WorldServerTextExCmd` | NON PROUVE | — | — |
| 640 | `CNC_ZW_WorldServerFBMsgUpdate` | NON PROUVE | — | — |
| 641 | `CNE_ZW_ZoneServerReportID` | NON PROUVE | — | — |
| 642 | `CNE_ZW_ZoneServerDestShutdown` | NON PROUVE | — | — |
| 643 | `CNE_ZW_ZoneServerCharacterArrivedDest` | NON PROUVE | — | — |
| 644 | `CNE_ZW_ZoneServerChannelMsg` | NON PROUVE | — | — |
| 645 | `CNE_ZW_ZoneServerChangeFamily` | NON PROUVE | — | — |
| 646 | `CNE_ZW_ZoneServerNewFamily` | NON PROUVE | — | — |
| 647 | `CNE_ZW_ZoneServerRemoveFamily` | NON PROUVE | — | — |
| 648 | `CNE_ZW_ZoneServerTextCommand` | NON PROUVE | — | — |
| 649 | `CNE_ZW_ZoneServerItemMallReceieveAttempt` | NON PROUVE | — | — |
| 650 | `CNE_ZW_ZoneServerGetCloneItemID` | NON PROUVE | — | — |
| 651 | `CNE_ZW_ZoneServerUpdateFortuneBag` | NON PROUVE | — | — |
| 652 | `CNE_ZW_ZoneServerUpdateLottery` | NON PROUVE | — | — |
| 653 | `CNE_ZW_ZoneServerTextExCmd` | NON PROUVE | — | — |
| 654 | `CNE_ZW_ZoneServerUpdateStarWay` | NON PROUVE | — | — |
| 655 | `CNC_MW_WorldServerItemMallMailSendResult` | NON PROUVE | — | — |
| 656 | `CNC_MW_WorldServerItemMallMailCheckOK` | NON PROUVE | — | — |
| 657 | `CNC_MW_WorldServerUpdateBlackList` | NON PROUVE | — | — |
| 658 | `CNC_MW_WorldServerUpdatePrestigeData` | NON PROUVE | — | — |
| 659 | `CNC_MW_WorldServerUpdateFriendMember` | NON PROUVE | — | — |
| 660 | `CNC_MW_WorldServerCrossZone` | NON PROUVE | — | — |
| 661 | `CNC_MW_WorldServerFBMsgUpdate` | NON PROUVE | — | — |
| 662 | `CNC_MW_WorldServerFriendNotify` | NON PROUVE | — | — |
| 663 | `CNC_MW_WorldServerUpdateSpecialCoin` | NON PROUVE | — | — |
| 664 | `CNC_MW_WorldServerCheckFamilyInvite` | NON PROUVE | — | — |
| 665 | `CNC_ZMis_MissionServerCheckFamilyInvite` | NON PROUVE | — | — |
| 666 | `CNC_MW_WorldServerTell` | NON PROUVE | — | — |
| 667 | `CNC_MW_WorldServerTextExCmd` | NON PROUVE | — | — |
| 668 | `CNC_MW_WorldServerTextExCmdRemoteWorld` | NON PROUVE | — | — |
| 669 | `CNC_MW_WorldServerTextExCmdRemoteZone` | NON PROUVE | — | — |
| 670 | `CNC_MW_MissionServerTextExCmd` | NON PROUVE | — | — |
| 671 | `CNC_MW_MissionServerTextExCmdRemoteWorld` | NON PROUVE | — | — |
| 672 | `CNC_MW_MissionServerTextExCmdRemoteZone` | NON PROUVE | — | — |
| 673 | `CNE_ZM_ZoneServerUpdatePKList` | NON PROUVE | — | — |
| 674 | `CNC_ZMis_MissionServerUpdatePKList` | NON PROUVE | — | — |
| 675 | `CNE_MW_WorldTextCommand` | NON PROUVE | — | — |
| 676 | `CNC_DBA_AgentDBACommand` | NON PROUVE | — | — |
| 677 | `CNE_DBA_ClientContainer` | NON PROUVE | — | — |
| 678 | `CNE_DBA_ClientEnchant` | NON PROUVE | — | — |
| 679 | `CNE_DBA_ClientNodeID` | NON PROUVE | — | — |
| 680 | `CNE_DBA_ClientPlayerData` | NON PROUVE | — | — |
| 681 | `CNE_DBA_ClientMissionState` | NON PROUVE | — | — |
| 682 | `CNC_DBA_ClientDelPlayerOK` | NON PROUVE | — | — |
| 683 | `CNE_DBA_ClientAllFamily` | NON PROUVE | — | — |
| 684 | `CNE_DBA_ClientNewFamily` | NON PROUVE | — | — |
| 685 | `CNE_DBA_ClientChangeFamily` | NON PROUVE | — | — |
| 686 | `CNE_DBA_ClientRemoveFamily` | NON PROUVE | — | — |
| 687 | `CNE_DBA_ClientFamilyAllMember` | NON PROUVE | — | — |
| 688 | `CNE_DBA_ClientAddFamilyMember` | NON PROUVE | — | — |
| 689 | `CNE_DBA_ClientChannelMsg` | NON PROUVE | — | — |
| 690 | `CNE_DBA_ClientShortcut` | NON PROUVE | — | — |
| 691 | `CNE_DBA_ClientSpell` | NON PROUVE | — | — |
| 692 | `CNE_DBA_ClientFinishPlayerData` | NON PROUVE | — | — |
| 693 | `CNE_DBA_ClientAllLover` | NON PROUVE | — | — |
| 694 | `CNE_DBA_ClientCollection` | NON PROUVE | — | — |
| 695 | `CNC_DBA_ClientFamilyAllMessage` | NON PROUVE | — | — |
| 696 | `CNE_DBA_ClientFamilyStorage` | NON PROUVE | — | — |
| 697 | `CNE_DBA_ClientAllTerritory` | NON PROUVE | — | — |
| 698 | `CNE_DBA_ClientGladiatorTeamInfo` | NON PROUVE | — | — |
| 699 | `CNE_DBA_ClientAllPlayerRoom` | NON PROUVE | — | — |
| 700 | `CNE_DBA_ClientFortuneBagCounter` | NON PROUVE | — | — |
| 701 | `CNE_DBA_ClientFamilyContainerData` | NON PROUVE | — | — |
| 702 | `CNE_DBA_ClientAllFamilyInstnce` | NON PROUVE | — | — |
| 703 | `CNC_ZMis_MissionServerUpdatePlayerCard` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 704 | `CNC_ZMis_MissionServerItemMallMailSend` | NON PROUVE | — | — |
| 705 | `CNC_ZMis_MissionServerItemMallMailCheck` | NON PROUVE | — | — |
| 706 | `CNC_ZMis_MissionServerAddFamilyMessage` | NON PROUVE | — | — |
| 707 | `CNE_ZMis_ZoneServerReportID` | NON PROUVE | — | — |
| 708 | `CNC_ZMis_MissionServerIDReport` | NON PROUVE | — | — |
| 709 | `CNC_MW_WorldServerCrossWorldEndClearAccount` | NON PROUVE | — | — |
| 710 | `CNC_ZMis_MissionServerNodeCreate` | NON PROUVE | — | — |
| 711 | `CNE_ZMis_ZoneServerListDialogue` | NON PROUVE | — | — |
| 712 | `CNE_ZMis_ZoneServerReplyDialogue` | NON PROUVE | — | — |
| 713 | `CNE_ZMis_ZoneServerTransitionCommands` | NON PROUVE | — | — |
| 714 | `CNE_ZMis_ZoneServerTextOutput` | NON PROUVE | — | — |
| 715 | `CNE_ZM_ZoneServerReportMonster` | NON PROUVE | — | — |
| 716 | `CNE_ZM_ZoneServerRebornMonster` | NON PROUVE | — | — |
| 717 | `CNE_ZM_ZoneServerSetRebornMonsterResult` | NON PROUVE | — | — |
| 718 | `CNE_ZM_ZoneServerMessageToCharacter` | NON PROUVE | — | — |
| 719 | `CNE_ZM_ZoneServerCharRegenerate` | NON PROUVE | — | — |
| 720 | `CNE_ZMis_ZoneServerUpdateNPCSymbols` | NON PROUVE | — | — |
| 721 | `CNE_ZMis_ZoneServerTextOutputIndex` | NON PROUVE | — | — |
| 722 | `CNE_ZMis_ZoneServerUpdateMissionState` | NON PROUVE | — | — |
| 723 | `CNE_ZMis_ZoneServerMailList` | NON PROUVE | — | — |
| 724 | `CNE_ZMis_ZoneServerMailRead` | NON PROUVE | — | — |
| 725 | `CNE_ZMis_ZoneServerMailDeleteResult` | NON PROUVE | — | — |
| 726 | `CNE_ZMis_ZoneServerMailSendResult` | NON PROUVE | — | — |
| 727 | `CNE_ZMis_ZoneServerMailReceive` | NON PROUVE | — | — |
| 728 | `CNE_ZMis_ZoneServerPutItem` | NON PROUVE | — | — |
| 729 | `CNE_ZMis_ZoneServerDropItem` | NON PROUVE | — | — |
| 730 | `CNE_ZMis_ZoneServerMailGetItemOK` | NON PROUVE | — | — |
| 731 | `CNE_ZMis_ZoneServerMailGetGoldOK` | NON PROUVE | — | — |
| 732 | `CNE_ZMis_ZoneServerMailReturnResult` | NON PROUVE | — | — |
| 733 | `CNE_ZMis_ZoneServerAuctionBuyList` | NON PROUVE | — | — |
| 734 | `CNE_ZMis_ZoneServerAuctionBuyOK` | NON PROUVE | — | — |
| 735 | `CNE_ZMis_ZoneServerAuctionCancelOK` | NON PROUVE | — | — |
| 736 | `CNE_ZMis_ZoneServerAuctionSellList` | NON PROUVE | — | — |
| 737 | `CNE_ZMis_ZoneServerAuctionSellResult` | NON PROUVE | — | — |
| 738 | `CNE_ZMis_ZoneServerSearchInfo` | NON PROUVE | — | — |
| 739 | `CNE_ZMis_ZoneServerMessageList` | PASSTHRU | — | `MissionServer.cs` |
| 740 | `CNE_ZMis_ZoneServerMessageWriteResult` | PASSTHRU | — | `MissionServer.cs` |
| 741 | `CNE_ZMis_ZoneServerMessageRead` | PASSTHRU | — | `MissionServer.cs` |
| 742 | `CNE_ZMis_ZoneServerMessageDeleteResult` | PASSTHRU | — | `MissionServer.cs` |
| 743 | `CNE_ZMis_ZoneServerCharNotQuit` | NON PROUVE | — | — |
| 744 | `CNE_ZMis_ZoneServerLoverUpdate` | NON PROUVE | — | — |
| 745 | `CNE_ZMis_ZoneServerAchievementUpdate` | NON PROUVE | — | — |
| 746 | `CNE_ZMis_ZoneServerBattlefieldJoinResult` | NON PROUVE | — | — |
| 747 | `CNE_ZMis_ZoneServerEnterBattlefield` | NON PROUVE | — | — |
| 748 | `CNE_ZMis_ZoneServerStartBattlefield` | NON PROUVE | — | — |
| 749 | `CNE_ZMis_ZoneServerBattlefieldList` | NON PROUVE | — | — |
| 750 | `CNE_ZMis_ZoneServerBattlefieldEraseMember` | NON PROUVE | — | — |
| 751 | `CNE_ZMis_ZoneServerEnterBattlefieldAnswer` | NON PROUVE | — | — |
| 752 | `CNE_ZMis_ZoneServerBattlefieldSetup` | NON PROUVE | — | — |
| 753 | `CNE_ZMis_ZoneServerBattlefieldChangeCharNum` | NON PROUVE | — | — |
| 754 | `CNE_ZMis_ZoneServerCrossZoneResult` | NON PROUVE | — | — |
| 755 | `CNE_ZMis_ZoneServerFamilyStorageUpdate` | NON PROUVE | — | — |
| 756 | `CNE_ZMis_ZoneServerFamilyStorageLog` | NON PROUVE | — | — |
| 757 | `CNE_ZMis_ZoneServerFamilyContainerUpdate` | NON PROUVE | — | — |
| 758 | `CNE_ZMis_ZoneServerFamilyTownMemberQuery` | NON PROUVE | — | — |
| 759 | `CNE_ZMis_ZoneServerQueryPlayerCard` | NON PROUVE | — | — |
| 760 | `CNE_ZMis_ZoneServerTerritoryInfo` | NON PROUVE | — | — |
| 761 | `CNE_ZMis_ZoneServerTerritoryQuery` | NON PROUVE | — | — |
| 762 | `CNE_ZMis_ZoneServerTerritoryJoin` | NON PROUVE | — | — |
| 763 | `CNE_ZMis_ZoneServerRankInfo` | PASSTHRU | — | `MissionServer.cs` |
| 764 | `CNE_ZMis_ZoneServerBuyBackList` | NON PROUVE | — | — |
| 765 | `CNE_ZMis_ZoneServerBuyBackGetItem` | NON PROUVE | — | — |
| 766 | `CNE_ZMis_ZoneServerBuybackInitAllocate` | NON PROUVE | — | — |
| 767 | `CNE_ZMis_ZoneServerDeallocateItemID` | NON PROUVE | — | — |
| 768 | `CNE_ZMis_ZoneServerNPCTalkCheck` | NON PROUVE | — | — |
| 769 | `CNE_ZMis_ZoneServerRemoteClient` | NON PROUVE | — | — |
| 770 | `CNE_ZMis_ZoneServerTextExCmd` | NON PROUVE | — | — |
| 771 | `CNE_ZMis_ZoneServerTextExCmdRemoteWorld` | NON PROUVE | — | — |
| 772 | `CNE_ZMis_ZoneServerTextExCmdRemoteZone` | NON PROUVE | — | — |
| 773 | `CNE_ZMis_ZoneServerEnterColosseum` | NON PROUVE | — | — |
| 774 | `CNE_ZMis_ZoneServerColosseumJoinResult` | NON PROUVE | — | — |
| 775 | `CNE_ZMis_ZoneServerStartColosseum` | NON PROUVE | — | — |
| 776 | `CNE_ZMis_ZoneServerColosseumLeaveJoinResult` | NON PROUVE | — | — |
| 777 | `CNE_ZMis_ZoneServerColosseumModeUpdate` | NON PROUVE | — | — |
| 778 | `CNE_ZMis_ZoneServerSearchRoomResult` | NON PROUVE | — | — |
| 779 | `CNE_ZMis_ZoneServerUpdateRoom` | PASSTHRU | — | `MissionServer.cs` |
| 780 | `CNE_ZMis_ZoneServerSwitchPlayerRoom` | NON PROUVE | — | — |
| 781 | `CNE_ZMis_ZoneServerUpdateRoomItem` | NON PROUVE | — | — |
| 782 | `CNC_ZMis_MissionServerListDialogue` | NON PROUVE | — | — |
| 783 | `CNC_ZMis_MissionServerOnDialogueSelected` | NON PROUVE | — | — |
| 784 | `CNC_ZM_MonsterServerMonsterReport` | NON PROUVE | — | — |
| 785 | `CNC_ZM_MonsterServerMonsterDieReport` | NON PROUVE | — | — |
| 786 | `CNC_ZM_MonsterServerListRebornMonster` | NON PROUVE | — | — |
| 787 | `CNC_ZM_MonsterServerSetRebornMonster` | NON PROUVE | — | — |
| 788 | `CNC_ZM_MonsterServerModifyRebornMonster` | NON PROUVE | — | — |
| 789 | `CNC_ZM_MonsterServerRemoveRebornMonster` | NON PROUVE | — | — |
| 790 | `CNC_ZM_MonsterServerUnRegRebornMonster` | NON PROUVE | — | — |
| 791 | `CNC_ZM_MonsterServerRemoveNode` | NON PROUVE | — | — |
| 792 | `CNC_ZM_MonsterServerStartCharRegenerate` | NON PROUVE | — | — |
| 793 | `CNC_ZM_MonsterServerStopCharRegenerate` | NON PROUVE | — | — |
| 794 | `CNC_ZM_MonsterServerRegenerationReport` | NON PROUVE | — | — |
| 795 | `CNC_ZMis_MissionServerRemovePMS` | NON PROUVE | — | — |
| 796 | `CNC_ZMis_MissionServerCloseMissionBrief` | NON PROUVE | — | — |
| 797 | `CNC_ZMis_MissionServerMigrationDone` | NON PROUVE | — | — |
| 798 | `CNC_ZMis_MissionServerGiveUpMission` | NON PROUVE | — | — |
| 799 | `CNC_ZMis_MissionServerMobDieReport` | NON PROUVE | — | — |
| 800 | `CNC_ZMis_MissionServerMailSend` | NON PROUVE | — | — |
| 801 | `CNC_ZMis_MissionServerSendSystemMail` | NON PROUVE | — | — |
| 802 | `CNC_ZMis_MissionServerMessageWrite` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 803 | `CNC_ZMis_MissionServerChannelMsg` | NON PROUVE | — | — |
| 804 | `CNC_ZMis_MissionServerBattlefieldJoin` | NON PROUVE | — | — |
| 805 | `CNC_ZMis_MissionServerBattlefieldEraseMember` | NON PROUVE | — | — |
| 806 | `CNC_ZMis_MissionServerBattlefieldClose` | NON PROUVE | — | — |
| 807 | `CNC_ZMis_MissionServerBattlefieldList` | NON PROUVE | — | — |
| 808 | `CNC_ZMis_MissionServerEnterBattlefieldAnswer` | NON PROUVE | — | — |
| 809 | `CNC_ZMis_MissionServerBattlefieldUpdateStatus` | NON PROUVE | — | — |
| 810 | `CNC_ZMis_MissionServerCrossZoneRequest` | NON PROUVE | — | — |
| 811 | `CNC_ZMis_MissionServerCrossZone` | NON PROUVE | — | — |
| 812 | `CNC_ZMis_MissionServerRemoteClient` | NON PROUVE | — | — |
| 813 | `CNC_ZMis_MissionServerTextExCmd` | NON PROUVE | — | — |
| 814 | `CNC_ZMis_MissionServerTextExCmdRemoteWorld` | NON PROUVE | — | — |
| 815 | `CNC_ZMis_MissionServerTextExCmdRemoteZone` | NON PROUVE | — | — |
| 816 | `CNC_ZMis_MissionServerGetTeamInviteeID` | NON PROUVE | — | — |
| 817 | `CNE_ZMis_ZoneServerReturnTeamInviteeID` | NON PROUVE | — | — |
| 818 | `CNE_ZMis_ZoneServerGMToolGetInfo` | NON PROUVE | — | — |
| 819 | `CNE_ZMis_ZoneServerGMToolGetMission` | NON PROUVE | — | — |
| 820 | `CNC_ZMis_MissionServerColosseumJoinAnswer` | NON PROUVE | — | — |
| 821 | `CNC_ZMis_MissionServerColosseumJoinRequest` | NON PROUVE | — | — |
| 822 | `CNC_ZMis_MissionServerColosseumClose` | NON PROUVE | — | — |
| 823 | `CNC_ZMis_MissionServerColosseumLeaveJoin` | NON PROUVE | — | — |
| 824 | `CNE_ZMis_ZoneServerAchievementRewardMail` | NON PROUVE | — | — |
| 825 | `CNE_ZMis_ZoneServerResetDailyMission` | NON PROUVE | — | — |
| 826 | `CNE_DBA_ClientNewPlayerGuideData` | NON PROUVE | — | — |
| 827 | `CNE_ZMis_ZoneServerUpdateLifelongPoint` | NON PROUVE | — | — |
| 828 | `CNE_ZMis_ZoneServerUpdateLifelongGold` | NON PROUVE | — | — |
| 829 | `CNE_ZMis_ZoneServerResetDailyMonsterKill` | NON PROUVE | — | — |
| 830 | `CNE_DBA_ClientRecommendActivityState` | NON PROUVE | — | — |
| 831 | `CNC_ZMis_MissionServerRefreshRecommendActivities` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 832 | `CNE_ZMis_ZoneServerUpdateRecommendActivities` | PASSTHRU | — | `MissionServer.cs` |
| 833 | `CNE_ZMis_ZoneServerFamilyBattleInfo` | NON PROUVE | — | — |
| 834 | `CNE_ZMis_ZoneServerResetNodeTimes` | NON PROUVE | — | — |
| 835 | `CNC_ZMis_MissionServerSetRecommendActivityTracing` | PASSTHRU | — | `ZoneMissionLink.cs` |
| 836 | `CNE_DBA_ClientElfData` | NON PROUVE | — | — |
| 837 | `CNE_ZMis_ZoneServerPVPTopData` | NON PROUVE | — | — |
| 838 | `CNE_ZMis_ZoneServerGreedyIslandJoin` | NON PROUVE | — | — |
| 839 | `CNE_ZMis_ZoneServerGreedyIslandInfo` | NON PROUVE | — | — |
| 840 | `CNC_ZMis_MissionServerGreedyIslandRankQuery` | NON PROUVE | — | — |
| 841 | `CNE_ZMis_ZoneServerColosseumQuery` | NON PROUVE | — | — |
| 842 | `CNC_ZMis_MissionServerBattleRoyaleJoinRequest` | NON PROUVE | — | — |
| 843 | `CNE_ZMis_ZoneServerStartBattleRoyale` | NON PROUVE | — | — |
| 844 | `CNC_ZMis_MissionServerBattleRoyaleClose` | NON PROUVE | — | — |
| 845 | `CNC_ZMis_MissionServerPlayerCCDataSave` | NON PROUVE | — | — |
| 846 | `CNE_DBA_ClientFastDress` | NON PROUVE | — | — |
| 847 | `CNE_ZMis_ZoneServerFastDressEquipEnd` | NON PROUVE | — | — |
| 848 | `CNE_ZMis_ZoneServerWeddingsInfo` | NON PROUVE | — | — |
| 849 | `CNC_ZMis_MissionServerVersusInstanceJoinRequest` | NON PROUVE | — | — |
| 850 | `CNE_ZMis_ZoneServerStartVersusInstance` | NON PROUVE | — | — |
| 851 | `CNC_ZMis_MissionServerFindCharacter` | NON PROUVE | — | — |
| 852 | `CNE_ZMis_ZoneServerFindTargetLocation` | NON PROUVE | — | — |
| 853 | `CNC_ZMis_MissionServerSendTargetLocation` | NON PROUVE | — | — |
| 854 | `CNE_ZMis_ZoneServerTransportGMCharacterToCharacter` | NON PROUVE | — | — |
| 855 | `CNE_ZMis_ZoneServerStartBattleCrusade` | NON PROUVE | — | — |
| 856 | `CNE_ZMis_ZoneServerEnterBattleCrusadeAnswer` | NON PROUVE | — | — |
| 857 | `CNE_ZMis_ZoneServerBattleCrusadeInfo` | NON PROUVE | — | — |
| 858 | `CNC_ZMis_MissionServerArrangeItem` | NON PROUVE | — | — |
| 859 | `CNE_DBA_ClientLotteryCount` | NON PROUVE | — | — |
| 860 | `CNE_CG_ServerTextCommand` | NON PROUVE | — | — |
| 861 | `CNE_CG_ServerLogin` | NON PROUVE | — | — |
| 862 | `CNC_CG_ClientCharacterList` | NON PROUVE | — | — |
| 863 | `CNC_CG_ClientCharacter` | NON PROUVE | — | — |
| 864 | `CNC_CG_ClientTextOutput` | NON PROUVE | — | — |
| 865 | `CNC_CG_ClientChannel` | NON PROUVE | — | — |
| 866 | `CNC_CG_ClientGetContainerItem` | NON PROUVE | — | — |
| 867 | `CNC_CG_ClientDataTest` | NON PROUVE | — | — |
| 868 | `CNC_CG_ClientCharLocate` | NON PROUVE | — | — |
| 869 | `CNC_CG_ClientGetMissionState` | NON PROUVE | — | — |
| 870 | `CNC_CG_ClientGetMoney` | NON PROUVE | — | — |
| 1000 | `—` | PASSTHRU | — | `ZoneEntryFlow.Items.cs` |
| 1499 | `—` | PASSTHRU | — | `ZoneEntryFlow.Migration.cs` |
| 1527 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 1637 | `—` | PASSTHRU | — | `ZoneEntryFlow.Migration.cs` |
| 5232 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5259 | `—` | PASSTHRU | — | `ZoneEntryFlow.Events.cs`, `ZoneEntryFlow.Migration.cs` |
| 5466 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5475 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5476 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5478 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5509 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 5510 | `—` | PASSTHRU | — | `ZoneEntryFlow.Elf.Mix.cs` |
| 50003 | `—` | PASSTHRU | — | `ZoneEntryFlow.Probes.cs` |
| 56001 | `—` | PASSTHRU | — | `ZoneEntryFlow.Probes.cs` |

## Bilan

| Statut | NCIDs |
|---|---|
| GOLDEN | 67 |
| INCOMPLET | 60 |
| PASSTHRU | 119 |
| DESASM | 169 |
| NON PROUVE | 470 |
