# Etat des NCIDs — protocole client <-> zone

> Regenere par `tools/gen_ncid_etat.py`.

- Entrants C>Z (CNE_CZ_) : **407** connus, **262** traites (flow/handler/replay).
- Sortants S>C (CNC_CZ_) : **464** connus, **200** emis quelque part.
- Golden byte-a-byte : **124** NCIDs.

Legende : `FAIT(flow)` = case du dispatch principal ; `FAIT(handler)` = handler PacketDispatcher ; `REPLAY` = corps capture natif rejoue ; `SILENT` = ignore comme le natif ; `ABSENT` = rien.

## Verdict — les gros systèmes sont-ils codés ?

### Combat : codé, mais incomplet

**Ce qui existe** (`ZoneEntryFlow.Combat*.cs`, ~4 100 lignes) :

- Pipeline mêlée natif complet : portée `CanAttack`, rolls hit/miss/
  crit (`NativeCombatFormulas`), swing + kfId par arme équipée, hurt
  du mob (495), mort → XP/XP-classe/loot/respawn, leash 50 u.
- Aggro mob : boucle de combat par mob (phases 302, attaques 271,
  retour au spawn, déaggro par distance à l'ancre).
- Sorts ciblés (9), de zone (16), autour (59), instant (17) :
  CastOK/Blast/Hit nativement ordonnés, enchant procs (85/127/128),
  résurrection (20), régénération HP/MP différenciée combat.
- PvP joueur↔joueur via le même pipeline.

**Ce qui manque ou coince (constats live)** :

- **Aggro passive absente** : `MonsterAiService.ShouldAggro` existe
  (profil `AggroRange` chargé) mais n'est **jamais appelé** — un mob
  n'agresse que s'il est attaqué. Les « noms rouges » ne sautent
  jamais sur le joueur, et l'écart de niveau n'est pas évalué.
- **Mode combat sur auto-attaque** : `st.InCombat` est posé côté
  serveur mais le flag 302 « engagé » joueur n'est émis que hors
  portée — à vérifier contre la capture native pour la bannière.
- **Cast en mouvement** : pas de règle timed-cast-vs-déplacement
  (le natif autorise l'incantation en marche pour certains sorts).
- **AoE partiel** : `HandleCastLocationAsync` applique des dégâts
  fixes (60, rayon 8 u) au lieu de la résolution de sort réelle.
- **Latence skill→dégât** : la séquence de cast est cadencée par des
  `ScheduleOnLoop(300 ms+)` ; une file trop granulaire peut rendre
  le hit perceptiblement tardif.

### Équipement / inventaire : codé, bugs connus

- `NativeItemCodec` (274/291/312/348/571…), `InventoryHandlers`,
  `EquipTrainHandlers`, `NativeElfCodec` existent et passent des
  goldens — mais l'équipement **pet-vs-armure** et le bitmap
  d'inventaire ont des défauts structurels remontés en live
  (objets équipés dans le mauvais slot, inventaire « plein » non
  détecté → drop au sol).

### Quêtes — régression compteur

Le loot appelle bien `SendQuestItemChangedAsync` (146 zone→mission)
mais les objectifs « collecter N items » restent à 0/N en live :
soit le lien mission ne réévalue pas, soit la réponse compteur
n'est pas émise/patchée. À tracer côté service Mission.

---

## Paquets entrants (C>Z)

### Combat / sorts — 15/17 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 5 | `CNE_CZ_ZoneServerMeleeAttack` | FAIT(flow) / FAIT(handler) / partiel | O |
| 9 | `CNE_CZ_ZoneServerSpellCastToLife` | FAIT(flow) / FAIT(handler) / partiel | O |
| 13 | `CNE_CZ_ZoneServerSpellBreak` | FAIT(flow) / FAIT(handler) | — |
| 16 | `CNE_CZ_ZoneServerSpellCastToLocation` | FAIT(flow) / FAIT(handler) | — |
| 17 | `CNE_CZ_ZoneServerSpellCastToNowhere` | FAIT(flow) / FAIT(handler) | — |
| 18 | `CNE_CZ_ZoneServerEnchantUpdate` | FAIT(flow) / FAIT(handler) | — |
| 20 | `CNE_CZ_ZoneServerRevive` | FAIT(flow) / FAIT(handler) | — |
| 24 | `CNE_CZ_ZoneServerEnterNodeEquipAndSpell` | FAIT(handler) / REPLAY / partiel | O |
| 52 | `CNE_CZ_ZoneServerBuySpell` | FAIT(handler) | — |
| 59 | `CNE_CZ_ZoneServerSpellCastToNearby` | FAIT(flow) / FAIT(handler) | — |
| 85 | `CNE_CZ_ZoneServerEnchantCancel` | FAIT(handler) / partiel | O |
| 121 | `CNE_CZ_ZoneServerSetSpellStone` | FAIT(handler) | — |
| 127 | `CNE_CZ_ZoneServerEnchantPause` | FAIT(handler) | — |
| 128 | `CNE_CZ_ZoneServerEnchantStart` | FAIT(handler) | — |
| 229 | `CNE_CZ_ZoneServerElfRevive` | FAIT(handler) | — |
| 678 | `CNE_DBA_ClientEnchant` | ABSENT | — |
| 691 | `CNE_DBA_ClientSpell` | ABSENT | — |

### Inventaire / equipement — 46/58 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 6 | `CNE_CZ_ZoneServerDropItem` | FAIT(handler) | — |
| 7 | `CNE_CZ_ZoneServerSwitchItemLocation` | FAIT(handler) / partiel | O |
| 8 | `CNE_CZ_ZoneServerUpdateEquipment` | FAIT(handler) | — |
| 19 | `CNE_CZ_ZoneServerItemUse` | FAIT(handler) / partiel | O |
| 22 | `CNE_CZ_ZoneServerBuyItems` | FAIT(flow) / FAIT(handler) | — |
| 23 | `CNE_CZ_ZoneServerSellItems` | FAIT(flow) / FAIT(handler) / partiel | O |
| 38 | `CNE_CZ_ZoneServerTradeAddItem` | FAIT(handler) | — |
| 39 | `CNE_CZ_ZoneServerTradeRemoveItem` | FAIT(handler) | — |
| 45 | `CNE_CZ_ZoneServerAskRepairEquipGold` | FAIT(handler) | — |
| 46 | `CNE_CZ_ZoneServerRepairEquipment` | FAIT(handler) | — |
| 47 | `CNE_CZ_ZoneServerAskRepairOneItemGold` | FAIT(handler) | — |
| 48 | `CNE_CZ_ZoneServerRepairOneItem` | FAIT(handler) | — |
| 64 | `CNE_CZ_ZoneServerAskIdentifyGold` | FAIT(handler) | — |
| 65 | `CNE_CZ_ZoneServerIdentifyItem` | FAIT(handler) | — |
| 66 | `CNE_CZ_ZoneServerExchangeItemPin` | FAIT(handler) | — |
| 67 | `CNE_CZ_ZoneServerInlayItem` | FAIT(flow) | — |
| 70 | `CNE_CZ_ZoneServerItemMallReceivableEnter` | FAIT(flow) | — |
| 77 | `CNE_CZ_ZoneServerVendorSellItem` | FAIT(flow) | — |
| 80 | `CNE_CZ_ZoneServerVendorUseItem` | FAIT(flow) | — |
| 83 | `CNE_CZ_ZoneServerItemMallUseItem` | FAIT(handler) | — |
| 84 | `CNE_CZ_ZoneServerDivideItem` | FAIT(handler) | — |
| 89 | `CNE_CZ_ZoneServerBagDueDate` | FAIT(handler) | — |
| 100 | `CNE_CZ_ZoneServerMailGetItem` | FAIT(handler) | — |
| 119 | `CNE_CZ_ZoneServerStrengthenItem` | FAIT(flow) | — |
| 130 | `CNE_CZ_ZoneServerFamilyContainerPutItem` | FAIT(flow) | — |
| 131 | `CNE_CZ_ZoneServerFamilyContainerGetItem` | FAIT(flow) | — |
| 143 | `CNE_CZ_ZoneServerLookEquip` | FAIT(handler) | — |
| 165 | `CNE_CZ_ZoneServerQueryClosets` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 167 | `CNE_CZ_ZoneServerClosetSet` | FAIT(flow) / FAIT(handler) | — |
| 177 | `CNE_CZ_ZoneServerFamilyStoragePutItem` | FAIT(handler) | — |
| 178 | `CNE_CZ_ZoneServerFamilyStorageGetItem` | FAIT(handler) | — |
| 180 | `CNE_CZ_ZoneServerFamilyBuildingRepair` | FAIT(handler) | — |
| 221 | `CNE_CZ_ZoneServerChanceLuckyBagGetReward` | FAIT(flow) / FAIT(handler) | — |
| 222 | `CNE_CZ_ZoneServerChanceLuckyBagReTry` | FAIT(flow) / FAIT(handler) | — |
| 223 | `CNE_CZ_ZoneServerQueryFortuneBagCounter` | FAIT(flow) / FAIT(handler) / REPLAY | O |
| 230 | `CNE_CZ_ZoneServerEquipTrainSetItem` | FAIT(handler) | — |
| 231 | `CNE_CZ_ZoneServerPVPRankLookEquip` | FAIT(flow) / FAIT(handler) | — |
| 240 | `CNE_CZ_ZoneServerFixEquipByFamily` | FAIT(handler) | — |
| 243 | `CNE_CZ_ZoneServerEnterNodeFastDress` | FAIT(flow) / FAIT(handler) / REPLAY | O |
| 244 | `CNE_CZ_ZoneServerApplyFastDressData` | FAIT(flow) / FAIT(handler) | — |
| 245 | `CNE_CZ_ZoneServerSaveFastDressData` | FAIT(flow) / FAIT(handler) | — |
| 246 | `CNE_CZ_ZoneServerRecordFastDressInfo` | FAIT(flow) / FAIT(handler) | — |
| 247 | `CNE_CZ_ZoneServerUpdateFastDressPage` | FAIT(flow) / FAIT(handler) | — |
| 248 | `CNE_CZ_ZoneServerRemoveFastDressInfo` | FAIT(flow) / FAIT(handler) | — |
| 257 | `CNE_CZ_ZoneServerExecuteMagicBox` | FAIT(flow) | — |
| 260 | `CNE_CZ_ZoneServerArrangeItem` | FAIT(handler) | — |
| 649 | `CNE_ZW_ZoneServerItemMallReceieveAttempt` | ABSENT | — |
| 650 | `CNE_ZW_ZoneServerGetCloneItemID` | ABSENT | — |
| 651 | `CNE_ZW_ZoneServerUpdateFortuneBag` | ABSENT | — |
| 700 | `CNE_DBA_ClientFortuneBagCounter` | ABSENT | — |
| 728 | `CNE_ZMis_ZoneServerPutItem` | ABSENT | — |
| 729 | `CNE_ZMis_ZoneServerDropItem` | ABSENT | — |
| 730 | `CNE_ZMis_ZoneServerMailGetItemOK` | ABSENT | — |
| 765 | `CNE_ZMis_ZoneServerBuyBackGetItem` | ABSENT | — |
| 767 | `CNE_ZMis_ZoneServerDeallocateItemID` | ABSENT | — |
| 781 | `CNE_ZMis_ZoneServerUpdateRoomItem` | ABSENT | — |
| 846 | `CNE_DBA_ClientFastDress` | ABSENT | — |
| 847 | `CNE_ZMis_ZoneServerFastDressEquipEnd` | ABSENT | — |

### Pet / elf — 14/15 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 105 | `CNE_CZ_ZoneServerElfStoneUpdate` | FAIT(handler) | — |
| 106 | `CNE_CZ_ZoneServerElfStoneUse` | FAIT(handler) | — |
| 107 | `CNE_CZ_ZoneServerElfWork` | FAIT(flow) / FAIT(handler) | — |
| 108 | `CNE_CZ_ZoneServerElfReturn` | FAIT(flow) | — |
| 109 | `CNE_CZ_ZoneServerElfCallElf` | FAIT(flow) | — |
| 110 | `CNE_CZ_ZoneServerEnterNodeElfInfo` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 111 | `CNE_CZ_ZoneServerElfWorkResultOK` | FAIT(handler) | — |
| 112 | `CNE_CZ_ZoneServerElfCheckData` | FAIT(handler) | — |
| 113 | `CNE_CZ_ZoneServerEnterNodeElfContainer` | FAIT(handler) / REPLAY / partiel | O |
| 137 | `CNE_CZ_ZoneServerElfTalk` | FAIT(handler) | — |
| 187 | `CNE_CZ_ZoneServerPetModeChange` | FAIT(flow) | — |
| 188 | `CNE_CZ_ZoneServerSendPetCmd` | FAIT(flow) | — |
| 227 | `CNE_CZ_ZoneServerElfMix` | FAIT(flow) | — |
| 228 | `CNE_CZ_ZoneServerElfChangeName` | FAIT(flow) / FAIT(handler) | — |
| 836 | `CNE_DBA_ClientElfData` | ABSENT | — |

### Equipe / social — 31/53 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 28 | `CNE_CZ_ZoneServerTeamLeave` | FAIT(handler) | — |
| 29 | `CNE_CZ_ZoneServerTeamInvite` | FAIT(handler) | — |
| 30 | `CNE_CZ_ZoneServerTeamInviteAnswer` | FAIT(handler) | — |
| 31 | `CNE_CZ_ZoneServerTeamInviteCancel` | FAIT(handler) | — |
| 34 | `CNE_CZ_ZoneServerChannelMsg` | FAIT(flow) / FAIT(handler) | — |
| 49 | `CNE_CZ_ZoneServerFamilyCreate` | FAIT(handler) | — |
| 79 | `CNE_CZ_ZoneServerTeamOperation` | FAIT(flow) / FAIT(handler) | — |
| 81 | `CNE_CZ_ZoneServerUpdateFamily` | FAIT(handler) | — |
| 82 | `CNE_CZ_ZoneServerSetFamilyEmblem` | FAIT(handler) | — |
| 86 | `CNE_CZ_ZoneServerTeamRadarAttention` | FAIT(handler) | — |
| 90 | `CNE_CZ_ZoneServerTeamChangeLeader` | FAIT(handler) | — |
| 132 | `CNE_CZ_ZoneServerFamilyInstanceGetData` | FAIT(flow) | — |
| 159 | `CNE_CZ_ZoneServerFamilyDonateMoney` | FAIT(handler) | — |
| 161 | `CNE_CZ_ZoneServerFamilyStatue` | FAIT(flow) | — |
| 173 | `CNE_CZ_ZoneServerFamilyInstanceCreate` | FAIT(flow) | — |
| 174 | `CNE_CZ_ZoneServerFamilyTownQuery` | FAIT(flow) | — |
| 175 | `CNE_CZ_ZoneServerFamilyTownMemberQuery` | FAIT(flow) | — |
| 176 | `CNE_CZ_ZoneServerFamilyTownEnter` | FAIT(flow) | — |
| 179 | `CNE_CZ_ZoneServerFamilyStorageQueryLog` | FAIT(handler) | — |
| 191 | `CNE_CZ_ZoneServerTeamRecruitMsg` | FAIT(handler) | — |
| 192 | `CNE_CZ_ZoneServerSearchTeam` | FAIT(handler) | — |
| 196 | `CNE_CZ_ZoneServerGladiatorTeamCreate` | FAIT(flow) / FAIT(handler) | — |
| 197 | `CNE_CZ_ZoneServerGladiatorTeamQuery` | FAIT(flow) / FAIT(handler) | — |
| 200 | `CNE_CZ_ZoneServerColosseumTeamQuery` | FAIT(flow) / FAIT(handler) | — |
| 225 | `CNE_CZ_ZoneServerFamilyBattleAskInfo` | FAIT(flow) / FAIT(handler) | — |
| 235 | `CNE_CZ_ZoneServerSearchTeamV2` | FAIT(handler) | — |
| 236 | `CNE_CZ_ZoneServerTeamRecruitV2` | FAIT(handler) | — |
| 237 | `CNE_CZ_ZoneServerTeamRecruitTransport` | FAIT(handler) | — |
| 239 | `CNE_CZ_ZoneServerFamilyTalkToNpc` | FAIT(handler) | — |
| 249 | `CNE_CZ_ZoneServerMarryOperation` | FAIT(handler) | — |
| 251 | `CNE_CZ_ZoneServerWeddingInfoQuery` | FAIT(handler) | — |
| 644 | `CNE_ZW_ZoneServerChannelMsg` | ABSENT | — |
| 645 | `CNE_ZW_ZoneServerChangeFamily` | ABSENT | — |
| 646 | `CNE_ZW_ZoneServerNewFamily` | ABSENT | — |
| 647 | `CNE_ZW_ZoneServerRemoveFamily` | ABSENT | — |
| 683 | `CNE_DBA_ClientAllFamily` | ABSENT | — |
| 684 | `CNE_DBA_ClientNewFamily` | ABSENT | — |
| 685 | `CNE_DBA_ClientChangeFamily` | ABSENT | — |
| 686 | `CNE_DBA_ClientRemoveFamily` | ABSENT | — |
| 687 | `CNE_DBA_ClientFamilyAllMember` | ABSENT | — |
| 688 | `CNE_DBA_ClientAddFamilyMember` | ABSENT | — |
| 689 | `CNE_DBA_ClientChannelMsg` | ABSENT | — |
| 696 | `CNE_DBA_ClientFamilyStorage` | ABSENT | — |
| 698 | `CNE_DBA_ClientGladiatorTeamInfo` | ABSENT | — |
| 701 | `CNE_DBA_ClientFamilyContainerData` | ABSENT | — |
| 702 | `CNE_DBA_ClientAllFamilyInstnce` | ABSENT | — |
| 755 | `CNE_ZMis_ZoneServerFamilyStorageUpdate` | ABSENT | — |
| 756 | `CNE_ZMis_ZoneServerFamilyStorageLog` | ABSENT | — |
| 757 | `CNE_ZMis_ZoneServerFamilyContainerUpdate` | ABSENT | — |
| 758 | `CNE_ZMis_ZoneServerFamilyTownMemberQuery` | ABSENT | — |
| 817 | `CNE_ZMis_ZoneServerReturnTeamInviteeID` | ABSENT | — |
| 833 | `CNE_ZMis_ZoneServerFamilyBattleInfo` | ABSENT | — |
| 848 | `CNE_ZMis_ZoneServerWeddingsInfo` | ABSENT | — |

### Quetes / missions — 6/10 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 43 | `CNE_CZ_ZoneServerCloseMissionBrief` | FAIT(handler) | — |
| 50 | `CNE_CZ_ZoneServerQueryMissionStates` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 51 | `CNE_CZ_ZoneServerGiveUpMission` | FAIT(handler) | — |
| 87 | `CNE_CZ_ZoneServerMissionListOp` | FAIT(handler) | O |
| 160 | `CNE_CZ_ZoneServerMissionGoto` | FAIT(flow) / FAIT(handler) / partiel | O |
| 186 | `CNE_CZ_ZoneServerSetMissionTrace` | FAIT(handler) / SILENT / partiel | O |
| 681 | `CNE_DBA_ClientMissionState` | ABSENT | — |
| 722 | `CNE_ZMis_ZoneServerUpdateMissionState` | ABSENT | — |
| 819 | `CNE_ZMis_ZoneServerGMToolGetMission` | ABSENT | — |
| 825 | `CNE_ZMis_ZoneServerResetDailyMission` | ABSENT | — |

### Commerce / echanges — 28/42 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 36 | `CNE_CZ_ZoneServerTradeRequest` | FAIT(handler) | — |
| 37 | `CNE_CZ_ZoneServerTradeRequestAccept` | FAIT(handler) | — |
| 40 | `CNE_CZ_ZoneServerTradeModifyGold` | FAIT(handler) | — |
| 41 | `CNE_CZ_ZoneServerTradeConfirm` | FAIT(handler) | — |
| 42 | `CNE_CZ_ZoneServerTradeCancel` | FAIT(handler) | — |
| 56 | `CNE_CZ_ZoneServerTradeReset` | FAIT(handler) | — |
| 63 | `CNE_CZ_ZoneServerLeaveShop` | FAIT(flow) / FAIT(handler) / partiel | O |
| 71 | `CNE_CZ_ZoneServerAskVendor` | FAIT(flow) / FAIT(handler) | — |
| 72 | `CNE_CZ_ZoneServerAskVendorContainer` | FAIT(flow) | — |
| 74 | `CNE_CZ_ZoneServerVendorClose` | FAIT(flow) | — |
| 75 | `CNE_CZ_ZoneServerVendorCloseView` | FAIT(flow) | — |
| 76 | `CNE_CZ_ZoneServerVendorBuy` | FAIT(flow) | — |
| 78 | `CNE_CZ_ZoneServerVendorCloseVending` | FAIT(flow) | — |
| 91 | `CNE_CZ_ZoneServerTradeReady` | FAIT(handler) | — |
| 92 | `CNE_CZ_ZoneServerTradeUnConfirm` | FAIT(handler) | — |
| 95 | `CNE_CZ_ZoneServerMailOpenBox` | FAIT(flow) / FAIT(handler) | — |
| 96 | `CNE_CZ_ZoneServerMailQuery` | FAIT(handler) | — |
| 97 | `CNE_CZ_ZoneServerMailRead` | FAIT(handler) | — |
| 98 | `CNE_CZ_ZoneServerMailDelete` | FAIT(handler) | — |
| 99 | `CNE_CZ_ZoneServerMailSend` | FAIT(handler) | — |
| 101 | `CNE_CZ_ZoneServerMailGetGold` | FAIT(handler) | — |
| 102 | `CNE_CZ_ZoneServerMailReturn` | FAIT(handler) | — |
| 114 | `CNE_CZ_ZoneServerAuctionBuyQuery` | FAIT(handler) | — |
| 115 | `CNE_CZ_ZoneServerAuctionBuy` | FAIT(handler) | — |
| 116 | `CNE_CZ_ZoneServerAuctionSellQuery` | FAIT(handler) | — |
| 117 | `CNE_CZ_ZoneServerAuctionSell` | FAIT(handler) | — |
| 118 | `CNE_CZ_ZoneServerAuctionCancel` | FAIT(handler) | — |
| 185 | `CNE_CZ_ZoneServerBuyBackOpen` | FAIT(handler) | — |
| 723 | `CNE_ZMis_ZoneServerMailList` | ABSENT | — |
| 724 | `CNE_ZMis_ZoneServerMailRead` | ABSENT | — |
| 725 | `CNE_ZMis_ZoneServerMailDeleteResult` | ABSENT | — |
| 726 | `CNE_ZMis_ZoneServerMailSendResult` | ABSENT | — |
| 727 | `CNE_ZMis_ZoneServerMailReceive` | ABSENT | — |
| 731 | `CNE_ZMis_ZoneServerMailGetGoldOK` | ABSENT | — |
| 732 | `CNE_ZMis_ZoneServerMailReturnResult` | ABSENT | — |
| 733 | `CNE_ZMis_ZoneServerAuctionBuyList` | ABSENT | — |
| 734 | `CNE_ZMis_ZoneServerAuctionBuyOK` | ABSENT | — |
| 735 | `CNE_ZMis_ZoneServerAuctionCancelOK` | ABSENT | — |
| 736 | `CNE_ZMis_ZoneServerAuctionSellList` | ABSENT | — |
| 737 | `CNE_ZMis_ZoneServerAuctionSellResult` | ABSENT | — |
| 764 | `CNE_ZMis_ZoneServerBuyBackList` | ABSENT | — |
| 824 | `CNE_ZMis_ZoneServerAchievementRewardMail` | ABSENT | — |

### Instances / PvP / events — 18/40 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 135 | `CNE_CZ_ZoneServerDuelRequest` | FAIT(flow) | — |
| 136 | `CNE_CZ_ZoneServerDuelRequestAnswer` | FAIT(flow) | — |
| 144 | `CNE_CZ_ZoneServerBattlefieldJoin` | FAIT(handler) | — |
| 145 | `CNE_CZ_ZoneServerEnterBattlefieldAnswer` | FAIT(handler) | — |
| 146 | `CNE_CZ_ZoneServerBattlefieldLeave` | FAIT(handler) | — |
| 162 | `CNE_CZ_ZoneServerQueryBattlefieldData` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 163 | `CNE_CZ_ZoneServerNodeGroupReset` | FAIT(flow) | — |
| 189 | `CNE_CZ_ZoneServerTerritoryQuery` | FAIT(flow) / FAIT(handler) | — |
| 190 | `CNE_CZ_ZoneServerTerritoryJoin` | FAIT(flow) / FAIT(handler) | — |
| 198 | `CNE_CZ_ZoneServerColosseumJoinAnswer` | FAIT(flow) / FAIT(handler) | — |
| 199 | `CNE_CZ_ZoneServerColosseumJoinRequest` | FAIT(flow) / FAIT(handler) | — |
| 201 | `CNE_CZ_ZoneServerColosseumLeaveJoin` | FAIT(flow) / FAIT(handler) | — |
| 219 | `CNE_CZ_ZoneServerColosseumModeQuery` | FAIT(flow) / FAIT(handler) | — |
| 220 | `CNE_CZ_ZoneServerRecruitInstance` | FAIT(flow) | — |
| 241 | `CNE_CZ_ZoneServerBattleRoyaleJoinRequest` | FAIT(flow) | — |
| 250 | `CNE_CZ_ZoneServerChurchReserve` | FAIT(handler) | — |
| 255 | `CNE_CZ_ZoneServerEvaluateLotteryOpen` | FAIT(flow) | — |
| 259 | `CNE_CZ_ZoneServerBattleCrusadeJoin` | FAIT(handler) | — |
| 697 | `CNE_DBA_ClientAllTerritory` | ABSENT | — |
| 746 | `CNE_ZMis_ZoneServerBattlefieldJoinResult` | ABSENT | — |
| 747 | `CNE_ZMis_ZoneServerEnterBattlefield` | ABSENT | — |
| 748 | `CNE_ZMis_ZoneServerStartBattlefield` | ABSENT | — |
| 749 | `CNE_ZMis_ZoneServerBattlefieldList` | ABSENT | — |
| 750 | `CNE_ZMis_ZoneServerBattlefieldEraseMember` | ABSENT | — |
| 751 | `CNE_ZMis_ZoneServerEnterBattlefieldAnswer` | ABSENT | — |
| 752 | `CNE_ZMis_ZoneServerBattlefieldSetup` | ABSENT | — |
| 753 | `CNE_ZMis_ZoneServerBattlefieldChangeCharNum` | ABSENT | — |
| 760 | `CNE_ZMis_ZoneServerTerritoryInfo` | ABSENT | — |
| 761 | `CNE_ZMis_ZoneServerTerritoryQuery` | ABSENT | — |
| 762 | `CNE_ZMis_ZoneServerTerritoryJoin` | ABSENT | — |
| 773 | `CNE_ZMis_ZoneServerEnterColosseum` | ABSENT | — |
| 774 | `CNE_ZMis_ZoneServerColosseumJoinResult` | ABSENT | — |
| 775 | `CNE_ZMis_ZoneServerStartColosseum` | ABSENT | — |
| 776 | `CNE_ZMis_ZoneServerColosseumLeaveJoinResult` | ABSENT | — |
| 777 | `CNE_ZMis_ZoneServerColosseumModeUpdate` | ABSENT | — |
| 841 | `CNE_ZMis_ZoneServerColosseumQuery` | ABSENT | — |
| 843 | `CNE_ZMis_ZoneServerStartBattleRoyale` | ABSENT | — |
| 855 | `CNE_ZMis_ZoneServerStartBattleCrusade` | ABSENT | — |
| 856 | `CNE_ZMis_ZoneServerEnterBattleCrusadeAnswer` | ABSENT | — |
| 857 | `CNE_ZMis_ZoneServerBattleCrusadeInfo` | ABSENT | — |

### Deplacement / monde — 14/17 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 1 | `CNE_CZ_ZoneServerMoveSelf` | FAIT(flow) / FAIT(handler) / partiel | O |
| 4 | `CNE_CZ_ZoneServerMigrationDone` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 15 | `CNE_CZ_ZoneServerCheckNodeExit` | FAIT(flow) / partiel | O |
| 25 | `CNE_CZ_ZoneServerEnterNodeInventory` | FAIT(handler) | O |
| 26 | `CNE_CZ_ZoneServerEnterNodePlayerData` | FAIT(handler) / REPLAY / partiel | O |
| 53 | `CNE_CZ_ZoneServerEnterNodeShortcut` | FAIT(handler) / partiel | O |
| 57 | `CNE_CZ_ZoneServerEcho` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 88 | `CNE_CZ_ZoneServerTransport` | FAIT(flow) / FAIT(handler) | — |
| 123 | `CNE_CZ_ZoneServerTurnSelf` | FAIT(flow) / FAIT(handler) / partiel | O |
| 133 | `CNE_CZ_ZoneServerEnterNodePlayerCard` | FAIT(handler) / REPLAY / partiel | O |
| 195 | `CNE_CZ_ZoneServerEnterNodePlayerRoom` | FAIT(handler) / REPLAY / partiel | O |
| 207 | `CNE_CZ_ZoneServerEnterRoom` | FAIT(flow) / FAIT(handler) | — |
| 238 | `CNE_CZ_ZoneServerChangePositionV2` | FAIT(handler) | — |
| 256 | `CNE_CZ_ZoneServerEnterNodeStarWay` | FAIT(handler) / REPLAY | O |
| 679 | `CNE_DBA_ClientNodeID` | ABSENT | — |
| 834 | `CNE_ZMis_ZoneServerResetNodeTimes` | ABSENT | — |
| 854 | `CNE_ZMis_ZoneServerTransportGMCharacterToCharacter` | ABSENT | — |

### Perso / progression — 22/31 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 12 | `CNE_CZ_ZoneServerUseSkillPoint` | FAIT(handler) | — |
| 14 | `CNE_CZ_ZoneServerPlayerAction` | FAIT(flow) / FAIT(handler) / partiel | O |
| 21 | `CNE_CZ_ZoneServerPlayerEmotion` | FAIT(handler) | — |
| 69 | `CNE_CZ_ZoneServerStorageExpand` | FAIT(handler) | — |
| 129 | `CNE_CZ_ZoneServerGoLottery` | FAIT(flow) / FAIT(handler) | — |
| 134 | `CNE_CZ_ZoneServerQueryPlayerCard` | FAIT(handler) | — |
| 148 | `CNE_CZ_ZoneServerSetAAPoint` | FAIT(flow) | — |
| 166 | `CNE_CZ_ZoneServerQueryClassList` | FAIT(flow) / REPLAY / partiel | O |
| 168 | `CNE_CZ_ZoneServerChangeClass` | FAIT(flow) / partiel | O |
| 169 | `CNE_CZ_ZoneServerPlayerCardUpdate` | FAIT(handler) | — |
| 170 | `CNE_CZ_ZoneServerPlayerWork` | FAIT(flow) | — |
| 171 | `CNE_CZ_ZoneServerPlayerWorkResultOK` | FAIT(flow) | — |
| 172 | `CNE_CZ_ZoneServerPlayerWorkCheckData` | FAIT(flow) / SILENT / partiel | O |
| 181 | `CNE_CZ_ZoneServerQueryLicense` | FAIT(flow) / REPLAY | O |
| 182 | `CNE_CZ_ZoneServerPlayerLicense` | FAIT(flow) | — |
| 183 | `CNE_CZ_ZoneServerQueryLicenseSet` | FAIT(flow) | — |
| 184 | `CNE_CZ_ZoneServerCheckUpdateLottery` | FAIT(flow) / FAIT(handler) | — |
| 213 | `CNE_CZ_ZoneServerQueryLifeSkill` | FAIT(flow) / REPLAY | O |
| 214 | `CNE_CZ_ZoneServerLifeSkill` | FAIT(flow) | — |
| 218 | `CNE_CZ_ZoneServerNewPlayerGuide` | FAIT(flow) / FAIT(handler) | O |
| 224 | `CNE_CZ_ZoneServerUpdateLotteryBonus` | FAIT(flow) / FAIT(handler) | — |
| 242 | `CNE_CZ_ZoneServerPlayerCCDataSave` | FAIT(flow) / FAIT(handler) | — |
| 652 | `CNE_ZW_ZoneServerUpdateLottery` | ABSENT | — |
| 680 | `CNE_DBA_ClientPlayerData` | ABSENT | — |
| 692 | `CNE_DBA_ClientFinishPlayerData` | ABSENT | — |
| 699 | `CNE_DBA_ClientAllPlayerRoom` | ABSENT | — |
| 759 | `CNE_ZMis_ZoneServerQueryPlayerCard` | ABSENT | — |
| 780 | `CNE_ZMis_ZoneServerSwitchPlayerRoom` | ABSENT | — |
| 826 | `CNE_DBA_ClientNewPlayerGuideData` | ABSENT | — |
| 827 | `CNE_ZMis_ZoneServerUpdateLifelongPoint` | ABSENT | — |
| 859 | `CNE_DBA_ClientLotteryCount` | ABSENT | — |

### Divers — 68/124 traites

| NCID | Classe | Statut | Golden |
|---|---|---|---|
| 0 | `CNE_CZ_ZoneServerReceiveTicket` | FAIT(flow) / FAIT(handler) / partiel | O |
| 2 | `CNE_CZ_ZoneServerWhoIs` | FAIT(handler) | — |
| 3 | `CNE_CZ_ZoneServerTextCommand` | FAIT(flow) / FAIT(handler) / partiel | O |
| 10 | `CNE_CZ_ZoneServerTalkToNPC` | FAIT(flow) / FAIT(handler) / partiel | O |
| 11 | `CNE_CZ_ZoneServerReplyDialog` | FAIT(flow) / FAIT(handler) / partiel | O |
| 27 | `CNE_CZ_ZoneServerSafeQuitReq` | FAIT(handler) | — |
| 32 | `CNE_CZ_ZoneServerSafeQuitCancel` | FAIT(handler) | — |
| 33 | `CNE_CZ_ZoneServerSafeQuitCountDownOver` | FAIT(handler) | — |
| 35 | `CNE_CZ_ZoneServerTreasurePickup` | FAIT(flow) / FAIT(handler) | — |
| 44 | `CNE_CZ_ZoneServerResurrect` | FAIT(flow) / FAIT(handler) | — |
| 54 | `CNE_CZ_ZoneServerUpdateShortcut` | FAIT(handler) / partiel | O |
| 55 | `CNE_CZ_ZoneServerGetShortcutOfPage` | FAIT(handler) / partiel | O |
| 58 | `CNE_CZ_ZoneServerLogoutReq` | FAIT(handler) | — |
| 60 | `CNE_CZ_ZoneServerStorageDeposit` | FAIT(handler) / partiel | O |
| 61 | `CNE_CZ_ZoneServerStorageWithdraw` | FAIT(handler) | — |
| 62 | `CNE_CZ_ZoneServerGetStorageData` | FAIT(handler) / REPLAY / partiel | O |
| 68 | `CNE_CZ_ZoneServerReportProcessInfos` | FAIT(handler) | — |
| 73 | `CNE_CZ_ZoneServerSloganUpdate` | FAIT(handler) | — |
| 93 | `CNE_CZ_ZoneServerUpdateAppellation` | FAIT(flow) | — |
| 94 | `CNE_CZ_ZoneServerQueryAppellation` | FAIT(flow) | — |
| 103 | `CNE_CZ_ZoneServerSearchCondition` | FAIT(handler) | — |
| 104 | `CNE_CZ_ZoneServerSearchOpt` | FAIT(handler) / SILENT / partiel | O |
| 120 | `CNE_CZ_ZoneServerStrengthenClose` | FAIT(flow) | — |
| 122 | `CNE_CZ_ZoneServerStateCancel` | FAIT(handler) | — |
| 124 | `CNE_CZ_ZoneServerTargetUpdate` | FAIT(flow) / FAIT(handler) / partiel | O |
| 125 | `CNE_CZ_ZoneServerGetSelectLifeTarget` | FAIT(handler) | — |
| 126 | `CNE_CZ_ZoneServerPVPFlagModify` | FAIT(handler) | — |
| 138 | `CNE_CZ_ZoneServerMessageListQuery` | FAIT(handler) | — |
| 139 | `CNE_CZ_ZoneServerMessageWrite` | FAIT(handler) | — |
| 140 | `CNE_CZ_ZoneServerMessageRead` | FAIT(handler) | — |
| 141 | `CNE_CZ_ZoneServerMessageDelete` | FAIT(handler) | — |
| 142 | `CNE_CZ_ZoneServerMessageExit` | FAIT(handler) | — |
| 147 | `CNE_CZ_ZoneServerBFListQuery` | FAIT(handler) | — |
| 149 | `CNE_CZ_ZoneServerSetAARate` | FAIT(flow) | — |
| 150 | `CNE_CZ_ZoneServerSaveCollection` | FAIT(flow) / FAIT(handler) | — |
| 151 | `CNE_CZ_ZoneServerGetCollection` | FAIT(flow) / FAIT(handler) | — |
| 152 | `CNE_CZ_ZoneServerQueryCollection` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 153 | `CNE_CZ_ZoneServerHello` | FAIT(handler) | — |
| 154 | `CNE_CZ_ZoneServerLoverInvite` | FAIT(handler) | — |
| 155 | `CNE_CZ_ZoneServerLoverMessage` | FAIT(handler) | — |
| 156 | `CNE_CZ_ZoneServerQueryLoverInfo` | FAIT(handler) / SILENT / partiel | O |
| 157 | `CNE_CZ_ZoneServerLoverBreak` | FAIT(handler) | — |
| 158 | `CNE_CZ_ZoneServerVictoryDeclaration` | FAIT(handler) | — |
| 164 | `CNE_CZ_ZoneServerUpdateBotData` | FAIT(flow) / FAIT(handler) / REPLAY / partiel | O |
| 193 | `CNE_CZ_ZoneServerRollTreasure` | FAIT(handler) | — |
| 194 | `CNE_CZ_ZoneServerRankInfoQuery` | FAIT(flow) / FAIT(handler) | — |
| 202 | `CNE_CZ_ZoneServerCoRideInvite` | FAIT(handler) | — |
| 203 | `CNE_CZ_ZoneServerDestinyDice` | FAIT(flow) | — |
| 204 | `CNE_CZ_ZoneServerQueryDestinyRoad` | FAIT(flow) / SILENT / partiel | O |
| 205 | `CNE_CZ_ZoneServerSearchRoom` | FAIT(handler) | — |
| 206 | `CNE_CZ_ZoneServerRentRoom` | FAIT(handler) | — |
| 208 | `CNE_CZ_ZoneServerSetRoomPrivilege` | FAIT(handler) | — |
| 209 | `CNE_CZ_ZoneServerSwitchSetRoomMode` | FAIT(handler) | — |
| 210 | `CNE_CZ_ZoneServerRoomGetData` | FAIT(flow) / FAIT(handler) | — |
| 211 | `CNE_CZ_ZoneServerUpdateDecoration` | FAIT(handler) | — |
| 212 | `CNE_CZ_ZoneServerPayRent` | FAIT(handler) | — |
| 215 | `CNE_CZ_ZoneServerChangeRoomTexture` | FAIT(handler) | — |
| 216 | `CNE_CZ_ZoneServerStartParty` | FAIT(handler) | — |
| 217 | `CNE_CZ_ZoneServerExtendRoom` | FAIT(handler) | — |
| 226 | `CNE_CZ_ZoneServerSetRecommendActivityTracing` | FAIT(handler) | — |
| 232 | `CNE_CZ_ZoneServerVerifyCubeCosts` | FAIT(flow) | — |
| 233 | `CNE_CZ_ZoneServerGreedyIslandJoin` | FAIT(handler) | — |
| 234 | `CNE_CZ_ZoneServerGreedyIslandRankQuery` | FAIT(handler) | — |
| 252 | `CNE_CZ_ZoneServerVersusInstanceJoinRequest` | FAIT(flow) / FAIT(handler) | — |
| 253 | `CNE_CZ_ZoneServerPayEventSelect` | FAIT(flow) / FAIT(handler) | — |
| 254 | `CNE_CZ_ZoneServerPayEventResponse` | FAIT(handler) | — |
| 258 | `CNE_CZ_ZoneServerDailyAwardOP` | FAIT(flow) / FAIT(handler) / REPLAY | O |
| 558 | `CNE_CZ_ZoneServerQueryRecommendActivities` | FAIT(flow) / FAIT(handler) / REPLAY | O |
| 623 | `CNE_T_ClientGetTicket` | ABSENT | — |
| 624 | `CNE_T_ClientUseTicket` | ABSENT | — |
| 641 | `CNE_ZW_ZoneServerReportID` | ABSENT | — |
| 642 | `CNE_ZW_ZoneServerDestShutdown` | ABSENT | — |
| 643 | `CNE_ZW_ZoneServerCharacterArrivedDest` | ABSENT | — |
| 648 | `CNE_ZW_ZoneServerTextCommand` | ABSENT | — |
| 653 | `CNE_ZW_ZoneServerTextExCmd` | ABSENT | — |
| 654 | `CNE_ZW_ZoneServerUpdateStarWay` | ABSENT | — |
| 673 | `CNE_ZM_ZoneServerUpdatePKList` | ABSENT | — |
| 675 | `CNE_MW_WorldTextCommand` | ABSENT | — |
| 677 | `CNE_DBA_ClientContainer` | ABSENT | — |
| 690 | `CNE_DBA_ClientShortcut` | ABSENT | — |
| 693 | `CNE_DBA_ClientAllLover` | ABSENT | — |
| 694 | `CNE_DBA_ClientCollection` | ABSENT | — |
| 707 | `CNE_ZMis_ZoneServerReportID` | ABSENT | — |
| 711 | `CNE_ZMis_ZoneServerListDialogue` | ABSENT | — |
| 712 | `CNE_ZMis_ZoneServerReplyDialogue` | ABSENT | — |
| 713 | `CNE_ZMis_ZoneServerTransitionCommands` | ABSENT | — |
| 714 | `CNE_ZMis_ZoneServerTextOutput` | ABSENT | — |
| 715 | `CNE_ZM_ZoneServerReportMonster` | ABSENT | — |
| 716 | `CNE_ZM_ZoneServerRebornMonster` | ABSENT | — |
| 717 | `CNE_ZM_ZoneServerSetRebornMonsterResult` | ABSENT | — |
| 718 | `CNE_ZM_ZoneServerMessageToCharacter` | ABSENT | — |
| 719 | `CNE_ZM_ZoneServerCharRegenerate` | ABSENT | — |
| 720 | `CNE_ZMis_ZoneServerUpdateNPCSymbols` | ABSENT | — |
| 721 | `CNE_ZMis_ZoneServerTextOutputIndex` | ABSENT | — |
| 738 | `CNE_ZMis_ZoneServerSearchInfo` | ABSENT | — |
| 739 | `CNE_ZMis_ZoneServerMessageList` | ABSENT | — |
| 740 | `CNE_ZMis_ZoneServerMessageWriteResult` | ABSENT | — |
| 741 | `CNE_ZMis_ZoneServerMessageRead` | ABSENT | — |
| 742 | `CNE_ZMis_ZoneServerMessageDeleteResult` | ABSENT | — |
| 743 | `CNE_ZMis_ZoneServerCharNotQuit` | ABSENT | — |
| 744 | `CNE_ZMis_ZoneServerLoverUpdate` | ABSENT | — |
| 745 | `CNE_ZMis_ZoneServerAchievementUpdate` | ABSENT | — |
| 754 | `CNE_ZMis_ZoneServerCrossZoneResult` | ABSENT | — |
| 763 | `CNE_ZMis_ZoneServerRankInfo` | ABSENT | — |
| 766 | `CNE_ZMis_ZoneServerBuybackInitAllocate` | ABSENT | — |
| 768 | `CNE_ZMis_ZoneServerNPCTalkCheck` | ABSENT | — |
| 769 | `CNE_ZMis_ZoneServerRemoteClient` | ABSENT | — |
| 770 | `CNE_ZMis_ZoneServerTextExCmd` | ABSENT | — |
| 771 | `CNE_ZMis_ZoneServerTextExCmdRemoteWorld` | ABSENT | — |
| 772 | `CNE_ZMis_ZoneServerTextExCmdRemoteZone` | ABSENT | — |
| 778 | `CNE_ZMis_ZoneServerSearchRoomResult` | ABSENT | — |
| 779 | `CNE_ZMis_ZoneServerUpdateRoom` | ABSENT | — |
| 818 | `CNE_ZMis_ZoneServerGMToolGetInfo` | ABSENT | — |
| 828 | `CNE_ZMis_ZoneServerUpdateLifelongGold` | ABSENT | — |
| 829 | `CNE_ZMis_ZoneServerResetDailyMonsterKill` | ABSENT | — |
| 830 | `CNE_DBA_ClientRecommendActivityState` | ABSENT | — |
| 832 | `CNE_ZMis_ZoneServerUpdateRecommendActivities` | ABSENT | — |
| 837 | `CNE_ZMis_ZoneServerPVPTopData` | ABSENT | — |
| 838 | `CNE_ZMis_ZoneServerGreedyIslandJoin` | ABSENT | — |
| 839 | `CNE_ZMis_ZoneServerGreedyIslandInfo` | ABSENT | — |
| 850 | `CNE_ZMis_ZoneServerStartVersusInstance` | ABSENT | — |
| 852 | `CNE_ZMis_ZoneServerFindTargetLocation` | ABSENT | — |
| 860 | `CNE_CG_ServerTextCommand` | ABSENT | — |
| 861 | `CNE_CG_ServerLogin` | ABSENT | — |

## Paquets sortants (S>C) — emis

| NCID | Classe | Golden |
|---|---|---|
| 262 | `CNC_CZ_ClientTicketInvalid` | — |
| 264 | `CNC_CZ_ClientMoveChar` | O |
| 265 | `CNC_CZ_ClientSeeChar` | — |
| 266 | `CNC_CZ_ClientTextOutput` | — |
| 267 | `CNC_CZ_ClientGoto` | O |
| 268 | `CNC_CZ_ClientSay` | O |
| 269 | `CNC_CZ_ClientLifeEradicate` | — |
| 271 | `CNC_CZ_ClientDisplayMonster` | O |
| 272 | `CNC_CZ_ClientMeleeAttack` | O |
| 274 | `CNC_CZ_ClientPutItemIntoInventory` | O |
| 275 | `CNC_CZ_ClientDropItem` | — |
| 277 | `CNC_CZ_ClientSwitchItemLocationOK` | O |
| 278 | `CNC_CZ_ClientSpellCast` | — |
| 279 | `CNC_CZ_ClientSpellCastOK` | O |
| 280 | `CNC_CZ_ClientUpdateAttr` | O |
| 281 | `CNC_CZ_ClientSpellHitToLife` | O |
| 282 | `CNC_CZ_ClientUpdateEquipment` | — |
| 283 | `CNC_CZ_ClientModelModify` | O |
| 284 | `CNC_CZ_ClientLevelUp` | O |
| 285 | `CNC_CZ_ClientDisplayDialogView` | O |
| 286 | `CNC_CZ_ClientGetSpell` | O |
| 288 | `CNC_CZ_ClientSpellBlastOK` | O |
| 290 | `CNC_CZ_ClientPlayerAction` | — |
| 291 | `CNC_CZ_ClientModifyItemDurability` | — |
| 292 | `CNC_CZ_ClientEffectReport` | O |
| 293 | `CNC_CZ_ClientNodeExit` | O |
| 298 | `CNC_CZ_ClientSeeMob` | O |
| 299 | `CNC_CZ_ClientEnchantAdd` | O |
| 300 | `CNC_CZ_ClientEnchantRemove` | O |
| 301 | `CNC_CZ_ClientEnchantUpdate` | O |
| 302 | `CNC_CZ_ClientFlagModify` | O |
| 303 | `CNC_CZ_ClientItemUse` | O |
| 304 | `CNC_CZ_ClientEnterShop` | — |
| 305 | `CNC_CZ_ClientDie` | O |
| 306 | `CNC_CZ_ClientRevive` | — |
| 307 | `CNC_CZ_ClientPlayerEmotion` | — |
| 308 | `CNC_CZ_ClientBuyUp` | — |
| 309 | `CNC_CZ_ClientSellOut` | — |
| 310 | `CNC_CZ_ClientEnterNodePlayerData` | O |
| 311 | `CNC_CZ_ClientEnterNodeEquipAndSpell` | O |
| 312 | `CNC_CZ_ClientEnterNodeInventory` | O |
| 313 | `CNC_CZ_ClientEnterNodeNodeData` | O |
| 316 | `CNC_CZ_ClientTextOutputIndex` | O |
| 317 | `CNC_CZ_ClientTeamLeaved` | — |
| 318 | `CNC_CZ_ClientTeamInviteNotice` | — |
| 320 | `CNC_CZ_ClientSafeQuitCancel` | — |
| 321 | `CNC_CZ_ClientSafeQuitResp` | — |
| 326 | `CNC_CZ_ClientMovePrediction` | O |
| 327 | `CNC_CZ_ClientChannelMsg` | — |
| 329 | `CNC_CZ_ClientSeeTreasure` | — |
| 330 | `CNC_CZ_ClientTreasureDisappear` | — |
| 331 | `CNC_CZ_ClientTradeRequest` | — |
| 333 | `CNC_CZ_ClientTradeAddItem` | — |
| 334 | `CNC_CZ_ClientTradeRemoveItem` | — |
| 335 | `CNC_CZ_ClientTradeAddItemFail` | — |
| 336 | `CNC_CZ_ClientTradeModifyGold` | — |
| 337 | `CNC_CZ_ClientTradeModifyGoldFail` | — |
| 339 | `CNC_CZ_ClientTradeCancel` | — |
| 341 | `CNC_CZ_ClientResurrect` | — |
| 342 | `CNC_CZ_ClientAskRepairEquipGold` | — |
| 343 | `CNC_CZ_ClientAskRepairOneItemGold` | — |
| 345 | `CNC_CZ_ClientUpdateNPCSymbols` | — |
| 346 | `CNC_CZ_ClientChangeFamily` | — |
| 347 | `CNC_CZ_ClientEnterFamilyCreation` | — |
| 349 | `CNC_CZ_ClientEnterSpellShop` | — |
| 350 | `CNC_CZ_ClientEnterRepairShop` | — |
| 353 | `CNC_CZ_ClientTradeReset` | — |
| 355 | `CNC_CZ_ClientLogoutResp` | — |
| 356 | `CNC_CZ_ClientCanLogout` | — |
| 357 | `CNC_CZ_ClientEnterStorage` | — |
| 358 | `CNC_CZ_ClientGetStorageData` | O |
| 363 | `CNC_CZ_ClientAskIdentifyGold` | — |
| 364 | `CNC_CZ_ClientIdentifyItem` | — |
| 365 | `CNC_CZ_ClientEnterIdentifyShop` | — |
| 369 | `CNC_CZ_ClientModifyItemInfo` | — |
| 370 | `CNC_CZ_ClientEnterInlayShop` | — |
| 371 | `CNC_CZ_ClientInlayItemResult` | — |
| 372 | `CNC_CZ_ClientTeamMemberEnchant` | — |
| 375 | `CNC_CZ_ClientVendorClose` | — |
| 376 | `CNC_CZ_ClientVendorCloseView` | — |
| 377 | `CNC_CZ_ClientVendorStatus` | — |
| 378 | `CNC_CZ_ClientVendorUpdateView` | — |
| 381 | `CNC_CZ_ClientTeamInfo` | — |
| 382 | `CNC_CZ_ClientMigrationDone` | O |
| 383 | `CNC_CZ_ClientPlayEffect` | — |
| 385 | `CNC_CZ_ClientOpenExploitRank` | — |
| 386 | `CNC_CZ_ClientTreasurePickupOK` | — |
| 388 | `CNC_CZ_ClientEnterMissionList` | O |
| 389 | `CNC_CZ_ClientBagDueDate` | — |
| 391 | `CNC_CZ_ClientUpdateMissionState` | O |
| 396 | `CNC_CZ_ClientChangeAppellation` | — |
| 397 | `CNC_CZ_ClientUpdateAppellation` | — |
| 399 | `CNC_CZ_ClientMailOpenBox` | — |
| 401 | `CNC_CZ_ClientMailRead` | — |
| 402 | `CNC_CZ_ClientMailDeleteResult` | — |
| 403 | `CNC_CZ_ClientMailSendResult` | — |
| 404 | `CNC_CZ_ClientMailReceive` | O |
| 405 | `CNC_CZ_ClientMailGetItemOK` | — |
| 406 | `CNC_CZ_ClientMailGetGoldOK` | — |
| 409 | `CNC_CZ_ClientMailReturnResult` | — |
| 410 | `CNC_CZ_ClientCommandFailReturn` | — |
| 412 | `CNC_CZ_ClientEnterNodeElfInfo` | O |
| 413 | `CNC_CZ_ClientElfAdd` | — |
| 414 | `CNC_CZ_ClientElfReturnOK` | — |
| 418 | `CNC_CZ_ClientElfUpdateAttr` | — |
| 424 | `CNC_CZ_ClientEnterNodeElfContainer` | O |
| 426 | `CNC_CZ_ClientAuctionBuyOK` | — |
| 427 | `CNC_CZ_ClientAuctionCancelOK` | — |
| 429 | `CNC_CZ_ClientAuctionSellResult` | — |
| 430 | `CNC_CZ_ClientAuctionHouse` | — |
| 432 | `CNC_CZ_ClientEnterStrengthen` | — |
| 433 | `CNC_CZ_ClientStrengthenCasting` | — |
| 434 | `CNC_CZ_ClientStrengthenItemCancel` | — |
| 435 | `CNC_CZ_ClientStrengthenItemResult` | — |
| 436 | `CNC_CZ_ClientModifyItemStrengthen` | — |
| 441 | `CNC_CZ_ClientTurnChar` | — |
| 444 | `CNC_CZ_ClientGetSelectLifeTarget` | O |
| 445 | `CNC_CZ_ClientEnchantPause` | — |
| 446 | `CNC_CZ_ClientEnchantStart` | — |
| 448 | `CNC_CZ_ClientLotteryResult` | — |
| 449 | `CNC_CZ_ClientLotteryEnd` | — |
| 450 | `CNC_CZ_ClientLotteryUpdatePlus` | O |
| 451 | `CNC_CZ_ClientDuelRequest` | — |
| 453 | `CNC_CZ_ClientDuelBegin` | — |
| 454 | `CNC_CZ_ClientDuelResult` | — |
| 456 | `CNC_CZ_ClientMessageList` | — |
| 457 | `CNC_CZ_ClientMessageWriteResult` | — |
| 458 | `CNC_CZ_ClientMessageRead` | — |
| 459 | `CNC_CZ_ClientMessageDeleteResult` | — |
| 461 | `CNC_CZ_ClientItemRent` | — |
| 462 | `CNC_CZ_ClientBattlefieldList` | — |
| 464 | `CNC_CZ_ClientBattlefieldJoinResult` | — |
| 470 | `CNC_CZ_ClientPlayElfPetAction` | — |
| 471 | `CNC_CZ_ClientUpdateAdvancedAbility` | — |
| 473 | `CNC_CZ_ClientHello` | O |
| 482 | `CNC_CZ_ClientUpdateBFData` | O |
| 484 | `CNC_CZ_ClientFamilyContainerData` | — |
| 487 | `CNC_CZ_ClientFamilyInstanceData` | — |
| 488 | `CNC_CZ_ClientPlayerClosets` | O |
| 490 | `CNC_CZ_ClientEnterNodePlayerCard` | O |
| 491 | `CNC_CZ_ClientQueryPlayerCard` | — |
| 496 | `CNC_CZ_ClientEnterWorks` | — |
| 497 | `CNC_CZ_ClientPlayerWorkUpdate` | — |
| 498 | `CNC_CZ_ClientPlayerWorkResult` | — |
| 502 | `CNC_CZ_ClientFamilyTownQuery` | — |
| 503 | `CNC_CZ_ClientFamilyTownMemberQuery` | — |
| 508 | `CNC_CZ_ClientFamilyStorageData` | — |
| 509 | `CNC_CZ_ClientPlayerLicense` | O |
| 511 | `CNC_CZ_ClientUpdateLottery` | — |
| 512 | `CNC_CZ_ClientBuyBackList` | — |
| 513 | `CNC_CZ_ClientPetShortCut` | — |
| 516 | `CNC_CZ_ClientTerritoryInfo` | — |
| 519 | `CNC_CZ_ClientTeamRecruitMsg` | O |
| 521 | `CNC_CZ_ClientRankInfo` | — |
| 523 | `CNC_CZ_ClientEnterNodePlayerRoom` | O |
| 525 | `CNC_CZ_ClientGladiatorTeamInfo` | — |
| 531 | `CNC_CZ_ClientReviveInfo` | O |
| 535 | `CNC_CZ_ClientPlayerDestinyRoadEvent` | — |
| 537 | `CNC_CZ_ClientSearchRoomResult` | — |
| 538 | `CNC_CZ_ClientUpdateRoom` | — |
| 539 | `CNC_CZ_ClientOpenHotel` | — |
| 541 | `CNC_CZ_ClientRoomData` | — |
| 542 | `CNC_CZ_ClientUpdateRoomItem` | — |
| 544 | `CNC_CZ_ClientLifeSkillUpdate` | — |
| 545 | `CNC_CZ_ClientRideSpell` | — |
| 547 | `CNC_CZ_ClientTerritoryEnd` | — |
| 554 | `CNC_CZ_ClientUpdateLifelongPoint` | O |
| 555 | `CNC_CZ_ClientUpdateLifelongGold` | — |
| 556 | `CNC_CZ_ClientChanceLuckyBagResult` | — |
| 563 | `CNC_CZ_ClientUpdateNodeTimes` | O |
| 566 | `CNC_CZ_ClientTerritoryFamilyScore` | — |
| 567 | `CNC_CZ_ClientUpdateMixElf` | — |
| 568 | `CNC_CZ_ClientUpdateElfMixElf` | — |
| 571 | `CNC_CZ_ClientPVPRankLookEquip` | — |
| 572 | `CNC_CZ_ClientLoadCubeItems` | — |
| 574 | `CNC_CZ_ClientPetUpgradeCasting` | — |
| 575 | `CNC_CZ_ClientPetUpgradeCancel` | — |
| 577 | `CNC_CZ_ClientTeamCheckNotify` | — |
| 579 | `CNC_CZ_ClientOpenAreaDialog` | — |
| 588 | `CNC_CZ_ClientUpdateElfSpell` | — |
| 589 | `CNC_CZ_ClientUpdateDestinyRoad` | O |
| 590 | `CNC_CZ_ClientDestinyDiceFailed` | — |
| 591 | `CNC_CZ_ClientBattleRoyaleInfo` | — |
| 592 | `CNC_CZ_ClientPlayerClassCustomData` | O |
| 593 | `CNC_CZ_ClientEnterNodeFastDress` | O |
| 594 | `CNC_CZ_ClientUpdateFastDressData` | — |
| 597 | `CNC_CZ_ClientMarryOperation` | — |
| 599 | `CNC_CZ_ClientWeddingsInfo` | — |
| 600 | `CNC_CZ_ClientUpdateAttrNewExp` | O |
| 601 | `CNC_CZ_ClientVersusInstanceInfo` | — |
| 602 | `CNC_CZ_ClientOpenPaySystem` | — |
| 604 | `CNC_CZ_ClientPayEventOK` | — |
| 606 | `CNC_CZ_ClientUpdateAttrNewHp` | — |
| 607 | `CNC_CZ_ClientEvaluateInfo` | — |
| 608 | `CNC_CZ_ClientUpdateEvaluateInfo` | — |
| 609 | `CNC_CZ_ClientEnterStarWayData` | O |
| 612 | `CNC_CZ_ClientOpenMagicBoxDialog` | — |
| 613 | `CNC_CZ_ClientMagicBoxResult` | — |
| 615 | `CNC_CZ_ClientUpdateDailyAwards` | O |
| 620 | `CNC_CZ_ClientArrangeItemOK` | — |

## Sortants jamais emis (264)

| NCID | Classe |
|---|---|
| 261 | `CNC_CZ_ClientElfCastEnchant` |
| 263 | `CNC_CZ_ClientDisplayChar` |
| 270 | `CNC_CZ_ClientResetCoord` |
| 273 | `CNC_CZ_ClientRestore` |
| 276 | `CNC_CZ_ClientChangeEquip` |
| 287 | `CNC_CZ_ClientSpellBreak` |
| 289 | `CNC_CZ_ClientSpellBlastToLife` |
| 294 | `CNC_CZ_ClientLeaveNode` |
| 295 | `CNC_CZ_ClientDestShutdown` |
| 296 | `CNC_CZ_ClientAttrRefresh` |
| 297 | `CNC_CZ_ClientSpellBlastToLocation` |
| 314 | `CNC_CZ_ClientCanCloseConnection` |
| 315 | `CNC_CZ_ClientEnchantReset` |
| 319 | `CNC_CZ_ClientTeamInviteResult` |
| 322 | `CNC_CZ_ClientNonSafeQuit` |
| 323 | `CNC_CZ_ClientDisplayCharSimple` |
| 324 | `CNC_CZ_ClientTeamAllMembersInfo` |
| 325 | `CNC_CZ_ClientTeamNewMemberInfo` |
| 328 | `CNC_CZ_ClientModifyAttr` |
| 332 | `CNC_CZ_ClientTradeStart` |
| 338 | `CNC_CZ_ClientTradeConfirm` |
| 340 | `CNC_CZ_ClientTradeComplete` |
| 344 | `CNC_CZ_ClientUpdateSystemVar` |
| 348 | `CNC_CZ_ClientMissionStates` |
| 351 | `CNC_CZ_ClientEnterNodeShortcut` |
| 352 | `CNC_CZ_ClientReceiveShortcutOfPage` |
| 354 | `CNC_CZ_ClientUseSkillPointOK` |
| 359 | `CNC_CZ_ClientPutItemIntoStorage` |
| 360 | `CNC_CZ_ClientPutItemIntoContainer` |
| 361 | `CNC_CZ_ClientSpellLoopCastStop` |
| 362 | `CNC_CZ_ClientQueryNpc` |
| 366 | `CNC_CZ_ClientEnterExchageItemService` |
| 367 | `CNC_CZ_ClientResetSpell` |
| 368 | `CNC_CZ_ClientUpdateCoolDown` |
| 373 | `CNC_CZ_ClientStorageExpandResult` |
| 374 | `CNC_CZ_ClientSloganUpdate` |
| 379 | `CNC_CZ_ClientVendorContainer` |
| 380 | `CNC_CZ_ClientMobTransform` |
| 384 | `CNC_CZ_ClientSelectFamilyEmblem` |
| 387 | `CNC_CZ_ClientTeamRadarAttention` |
| 390 | `CNC_CZ_ClientUpdateInteraction` |
| 392 | `CNC_CZ_ClientTeamMemberPosition` |
| 393 | `CNC_CZ_ClientEnterTransport` |
| 394 | `CNC_CZ_ClientTradeReady` |
| 395 | `CNC_CZ_ClientTradeUnConfirm` |
| 398 | `CNC_CZ_ClientAddAppellation` |
| 400 | `CNC_CZ_ClientMailList` |
| 407 | `CNC_CZ_ClientUseItemTime` |
| 408 | `CNC_CZ_ClientUseItemTimeBreak` |
| 411 | `CNC_CZ_ClientSearchInfo` |
| 415 | `CNC_CZ_ClientElfWorkResult` |
| 416 | `CNC_CZ_ClientElfWorkResultEnd` |
| 417 | `CNC_CZ_ClientElfEvent` |
| 419 | `CNC_CZ_ClientElfUpdateSkill` |
| 420 | `CNC_CZ_ClientElfUpdateSoulAbility` |
| 421 | `CNC_CZ_ClientElfGoToWork` |
| 422 | `CNC_CZ_ClientElfDecompose` |
| 423 | `CNC_CZ_ClientElfStoneUse` |
| 425 | `CNC_CZ_ClientAuctionBuyList` |
| 428 | `CNC_CZ_ClientAuctionSellList` |
| 431 | `CNC_CZ_ClientChangeElf` |
| 437 | `CNC_CZ_ClientSetSpellStone` |
| 438 | `CNC_CZ_ClientSummonPet` |
| 439 | `CNC_CZ_ClientEnterElfReturn` |
| 440 | `CNC_CZ_ClientMobChangeAI` |
| 442 | `CNC_CZ_ClientElfAction` |
| 443 | `CNC_CZ_ClientElfReturnEffect` |
| 447 | `CNC_CZ_ClientPVPTime` |
| 452 | `CNC_CZ_ClientDuelRequestResult` |
| 455 | `CNC_CZ_ClientEnterMessageBoard` |
| 460 | `CNC_CZ_ClientLookEquip` |
| 463 | `CNC_CZ_ClientEnterBattlefield` |
| 465 | `CNC_CZ_ClientBattlefieldResult` |
| 466 | `CNC_CZ_ClientBattlefieldUpdateScore` |
| 467 | `CNC_CZ_ClientAltarState` |
| 468 | `CNC_CZ_ClientBattleMemberPosition` |
| 469 | `CNC_CZ_ClientBattlefieldMemberInfo` |
| 472 | `CNC_CZ_ClientUpdateCollection` |
| 474 | `CNC_CZ_ClientScreenMessage` |
| 475 | `CNC_CZ_ClientLoverInvite` |
| 476 | `CNC_CZ_ClientLoverUpdate` |
| 477 | `CNC_CZ_ClientLoverInfo` |
| 478 | `CNC_CZ_ClientLoverBreak` |
| 479 | `CNC_CZ_ClientLoverPosition` |
| 480 | `CNC_CZ_ClientAchievementUpdate` |
| 481 | `CNC_CZ_ClientTextOutputItem` |
| 483 | `CNC_CZ_ClientEnterFamilyBuilding` |
| 485 | `CNC_CZ_ClientFamilyContainerDeleteItem` |
| 486 | `CNC_CZ_ClientFamilyContainerPutItem` |
| 489 | `CNC_CZ_ClientPlayerClassList` |
| 492 | `CNC_CZ_ClientBaseAttrRefresh` |
| 493 | `CNC_CZ_ClientAttrLevelRefresh` |
| 494 | `CNC_CZ_ClientPlaySound` |
| 495 | `CNC_CZ_ClientPlayMonsterAction` |
| 499 | `CNC_CZ_ClientPlayerEvent` |
| 500 | `CNC_CZ_ClientPlayerWorkResultEnd` |
| 501 | `CNC_CZ_ClientEnterFamilyInstanceCreation` |
| 504 | `CNC_CZ_ClientFamilyStorageDeleteItem` |
| 505 | `CNC_CZ_ClientFamilyStoragePutItem` |
| 506 | `CNC_CZ_ClientFamilyStorageLog` |
| 507 | `CNC_CZ_ClientEnterFamilyStorage` |
| 510 | `CNC_CZ_ClientChangeClassShortcut` |
| 514 | `CNC_CZ_ClientEnterFamilyTownList` |
| 515 | `CNC_CZ_ClientTerritoryQuery` |
| 517 | `CNC_CZ_ClientCanJoinTeam` |
| 518 | `CNC_CZ_ClientSearchTeamInfo` |
| 520 | `CNC_CZ_ClientRollTreasure` |
| 522 | `CNC_CZ_ClientHateReport` |
| 524 | `CNC_CZ_ClientEnterGladiatorTeamCreation` |
| 526 | `CNC_CZ_ClientDisbandGladiatorTeam` |
| 527 | `CNC_CZ_ClientEnterColosseum` |
| 528 | `CNC_CZ_ClientColosseumJoinResult` |
| 529 | `CNC_CZ_ClientColosseumTeamInfo` |
| 530 | `CNC_CZ_ClientColosseumLeaveJoinResult` |
| 532 | `CNC_CZ_ClientTerritoryKilledInfo` |
| 533 | `CNC_CZ_ClientCoRideInvite` |
| 534 | `CNC_CZ_ClientPlayerDestinyRoad` |
| 536 | `CNC_CZ_ClientUpdateZoneMonster` |
| 540 | `CNC_CZ_ClientSetRoomResult` |
| 543 | `CNC_CZ_ClientPlayerLifeSkill` |
| 546 | `CNC_CZ_ClientSpellFlyToLocation` |
| 548 | `CNC_CZ_ClientUpdateSpellAttr` |
| 549 | `CNC_CZ_ClientUpdateHealthOnlineTime` |
| 550 | `CNC_CZ_ClientResetDailyMission` |
| 551 | `CNC_CZ_ClientNewPlayerGuide` |
| 552 | `CNC_CZ_ClientColosseumModeQueryResult` |
| 553 | `CNC_CZ_ClientRecruitInstanceResult` |
| 557 | `CNC_CZ_ClientUpdateFortuneBagCounter` |
| 559 | `CNC_CZ_ClientRecommendActivities` |
| 560 | `CNC_CZ_ClientUpdateRecommendActivities` |
| 561 | `CNC_CZ_ClientFamilyBattleInfo` |
| 562 | `CNC_CZ_ClientFamilyBattleKilledRank` |
| 564 | `CNC_CZ_ClientOpenInstanceDialog` |
| 565 | `CNC_CZ_ClientR25UpdateFamily` |
| 569 | `CNC_CZ_ClientLookElfPetEquip` |
| 570 | `CNC_CZ_ClientEquipTrainItemChanged` |
| 573 | `CNC_CZ_ClientGreedyIslandInfo` |
| 576 | `CNC_CZ_ClientUpdateWarCoin` |
| 578 | `CNC_CZ_ClientUpdateFloorInfo` |
| 580 | `CNC_CZ_ClientUpdateAlpacaCoin` |
| 581 | `CNC_CZ_ClientLifeFloat` |
| 582 | `CNC_CZ_ClientSearchTeamInfoV2` |
| 583 | `CNC_CZ_ClientTeamRecruitMsgV2` |
| 584 | `CNC_CZ_ClientCheckTeamTransport` |
| 585 | `CNC_CZ_ClientOpenMissionShop` |
| 586 | `CNC_CZ_ClientTerritoryPlayerInfo` |
| 587 | `CNC_CZ_ClientColosseumQuery` |
| 595 | `CNC_CZ_ClientInGameLotteryNotify` |
| 596 | `CNC_CZ_ClientDiamondLotteryNotify` |
| 598 | `CNC_CZ_ClientOpenChurchDialog` |
| 603 | `CNC_CZ_ClientPayEvent` |
| 605 | `CNC_CZ_ClientSlotFlicker` |
| 610 | `CNC_CZ_ClientUIControlNotify` |
| 611 | `CNC_CZ_ClientChangeSpell` |
| 614 | `CNC_CZ_ClientBackItem` |
| 616 | `CNC_CZ_ClientUpdateBattleCrusadeInfo` |
| 617 | `CNC_CZ_ClientOpenBattleCrusadeDialog` |
| 618 | `CNC_CZ_ClientPetLotteryResult` |
| 619 | `CNC_CZ_ClientPetLotteryEnd` |
| 621 | `CNC_T_ServerGetTicket` |
| 622 | `CNC_T_ServerUseTicket` |
| 625 | `CNC_ZW_WorldServerIDReport` |
| 626 | `CNC_ZW_WorldServerTell` |
| 627 | `CNC_ZW_WorldServerAllUsers` |
| 628 | `CNC_ZW_WorldServerNotifyEntry` |
| 629 | `CNC_ZW_WorldServerCharacterArrived` |
| 630 | `CNC_ZW_WorldServerResetTime` |
| 631 | `CNC_ZW_WorldServerReturnToCharacterList` |
| 632 | `CNC_ZW_WorldServerTeamLeavedNotify` |
| 633 | `CNC_ZW_WorldServerChannelMsg` |
| 634 | `CNC_ZW_WorldServerFamilyCreation` |
| 635 | `CNC_ZW_WorldServerLogout` |
| 636 | `CNC_ZW_WorldServerTextCommand` |
| 637 | `CNC_ZW_WorldServerItemMallReceieveAttempt` |
| 638 | `CNC_ZW_WorldServerGetCloneItemID` |
| 639 | `CNC_ZW_WorldServerTextExCmd` |
| 640 | `CNC_ZW_WorldServerFBMsgUpdate` |
| 655 | `CNC_MW_WorldServerItemMallMailSendResult` |
| 656 | `CNC_MW_WorldServerItemMallMailCheckOK` |
| 657 | `CNC_MW_WorldServerUpdateBlackList` |
| 658 | `CNC_MW_WorldServerUpdatePrestigeData` |
| 659 | `CNC_MW_WorldServerUpdateFriendMember` |
| 660 | `CNC_MW_WorldServerCrossZone` |
| 661 | `CNC_MW_WorldServerFBMsgUpdate` |
| 662 | `CNC_MW_WorldServerFriendNotify` |
| 663 | `CNC_MW_WorldServerUpdateSpecialCoin` |
| 664 | `CNC_MW_WorldServerCheckFamilyInvite` |
| 665 | `CNC_ZMis_MissionServerCheckFamilyInvite` |
| 666 | `CNC_MW_WorldServerTell` |
| 667 | `CNC_MW_WorldServerTextExCmd` |
| 668 | `CNC_MW_WorldServerTextExCmdRemoteWorld` |
| 669 | `CNC_MW_WorldServerTextExCmdRemoteZone` |
| 670 | `CNC_MW_MissionServerTextExCmd` |
| 671 | `CNC_MW_MissionServerTextExCmdRemoteWorld` |
| 672 | `CNC_MW_MissionServerTextExCmdRemoteZone` |
| 674 | `CNC_ZMis_MissionServerUpdatePKList` |
| 676 | `CNC_DBA_AgentDBACommand` |
| 682 | `CNC_DBA_ClientDelPlayerOK` |
| 695 | `CNC_DBA_ClientFamilyAllMessage` |
| 703 | `CNC_ZMis_MissionServerUpdatePlayerCard` |
| 704 | `CNC_ZMis_MissionServerItemMallMailSend` |
| 705 | `CNC_ZMis_MissionServerItemMallMailCheck` |
| 706 | `CNC_ZMis_MissionServerAddFamilyMessage` |
| 708 | `CNC_ZMis_MissionServerIDReport` |
| 709 | `CNC_MW_WorldServerCrossWorldEndClearAccount` |
| 710 | `CNC_ZMis_MissionServerNodeCreate` |
| 782 | `CNC_ZMis_MissionServerListDialogue` |
| 783 | `CNC_ZMis_MissionServerOnDialogueSelected` |
| 784 | `CNC_ZM_MonsterServerMonsterReport` |
| 785 | `CNC_ZM_MonsterServerMonsterDieReport` |
| 786 | `CNC_ZM_MonsterServerListRebornMonster` |
| 787 | `CNC_ZM_MonsterServerSetRebornMonster` |
| 788 | `CNC_ZM_MonsterServerModifyRebornMonster` |
| 789 | `CNC_ZM_MonsterServerRemoveRebornMonster` |
| 790 | `CNC_ZM_MonsterServerUnRegRebornMonster` |
| 791 | `CNC_ZM_MonsterServerRemoveNode` |
| 792 | `CNC_ZM_MonsterServerStartCharRegenerate` |
| 793 | `CNC_ZM_MonsterServerStopCharRegenerate` |
| 794 | `CNC_ZM_MonsterServerRegenerationReport` |
| 795 | `CNC_ZMis_MissionServerRemovePMS` |
| 796 | `CNC_ZMis_MissionServerCloseMissionBrief` |
| 797 | `CNC_ZMis_MissionServerMigrationDone` |
| 798 | `CNC_ZMis_MissionServerGiveUpMission` |
| 799 | `CNC_ZMis_MissionServerMobDieReport` |
| 800 | `CNC_ZMis_MissionServerMailSend` |
| 801 | `CNC_ZMis_MissionServerSendSystemMail` |
| 802 | `CNC_ZMis_MissionServerMessageWrite` |
| 803 | `CNC_ZMis_MissionServerChannelMsg` |
| 804 | `CNC_ZMis_MissionServerBattlefieldJoin` |
| 805 | `CNC_ZMis_MissionServerBattlefieldEraseMember` |
| 806 | `CNC_ZMis_MissionServerBattlefieldClose` |
| 807 | `CNC_ZMis_MissionServerBattlefieldList` |
| 808 | `CNC_ZMis_MissionServerEnterBattlefieldAnswer` |
| 809 | `CNC_ZMis_MissionServerBattlefieldUpdateStatus` |
| 810 | `CNC_ZMis_MissionServerCrossZoneRequest` |
| 811 | `CNC_ZMis_MissionServerCrossZone` |
| 812 | `CNC_ZMis_MissionServerRemoteClient` |
| 813 | `CNC_ZMis_MissionServerTextExCmd` |
| 814 | `CNC_ZMis_MissionServerTextExCmdRemoteWorld` |
| 815 | `CNC_ZMis_MissionServerTextExCmdRemoteZone` |
| 816 | `CNC_ZMis_MissionServerGetTeamInviteeID` |
| 820 | `CNC_ZMis_MissionServerColosseumJoinAnswer` |
| 821 | `CNC_ZMis_MissionServerColosseumJoinRequest` |
| 822 | `CNC_ZMis_MissionServerColosseumClose` |
| 823 | `CNC_ZMis_MissionServerColosseumLeaveJoin` |
| 831 | `CNC_ZMis_MissionServerRefreshRecommendActivities` |
| 835 | `CNC_ZMis_MissionServerSetRecommendActivityTracing` |
| 840 | `CNC_ZMis_MissionServerGreedyIslandRankQuery` |
| 842 | `CNC_ZMis_MissionServerBattleRoyaleJoinRequest` |
| 844 | `CNC_ZMis_MissionServerBattleRoyaleClose` |
| 845 | `CNC_ZMis_MissionServerPlayerCCDataSave` |
| 849 | `CNC_ZMis_MissionServerVersusInstanceJoinRequest` |
| 851 | `CNC_ZMis_MissionServerFindCharacter` |
| 853 | `CNC_ZMis_MissionServerSendTargetLocation` |
| 858 | `CNC_ZMis_MissionServerArrangeItem` |
| 862 | `CNC_CG_ClientCharacterList` |
| 863 | `CNC_CG_ClientCharacter` |
| 864 | `CNC_CG_ClientTextOutput` |
| 865 | `CNC_CG_ClientChannel` |
| 866 | `CNC_CG_ClientGetContainerItem` |
| 867 | `CNC_CG_ClientDataTest` |
| 868 | `CNC_CG_ClientCharLocate` |
| 869 | `CNC_CG_ClientGetMissionState` |
| 870 | `CNC_CG_ClientGetMoney` |

## Protocoles internes (serveur<->serveur)

### World — 157 classes

| NCID | Classe |
|---|---|
| 0 | `CNE_CW_ServerHello` |
| 1 | `CNE_CW_ServerReceiveTicket` |
| 2 | `CNE_CW_ServerKeepAlive` |
| 3 | `CNE_CW_ServerLoginCharacter` |
| 4 | `CNE_CW_ServerCreateCharacterName` |
| 5 | `CNE_CW_ServerCreateCharacterData` |
| 6 | `CNE_CW_ServerRemoveCharacter` |
| 7 | `CNE_CW_ServerCancelCreateCharacterReq` |
| 8 | `CNE_CW_ServerLookingForMember` |
| 9 | `CNE_CW_ServerFamilyOp` |
| 10 | `CNE_CW_ServerQueryFamilyInfo` |
| 11 | `CNE_CW_ServerLookingForGroup` |
| 12 | `CNE_CW_ServerAddFriend` |
| 13 | `CNE_CW_ServerQueryFriendInfo` |
| 14 | `CNE_CW_ServerFriendInvitation` |
| 15 | `CNE_CW_ServerItemMallAnythingReceivable` |
| 16 | `CNC_CW_ServerItemMallReceivableList` |
| 17 | `CNE_CW_ServerItemMallReceivableQuery` |
| 18 | `CNE_CW_ServerItemMallReceieveAttempt` |
| 19 | `CNE_CW_ServerQueryExploitRank` |
| 20 | `CNE_CW_ServerChangeWorld` |
| 21 | `CNE_CW_ServerChangeWorldAck` |
| 22 | `CNE_CW_ServerRemoveFriend` |
| 23 | `CNE_CW_ServerItemMallReqBuyItem` |
| 24 | `CNE_CW_ServerItemMallListQuery` |
| 25 | `CNE_CW_ServerItemMallPointInfo` |
| 26 | `CNE_CW_ServerCaptchaReturn` |
| 27 | `CNE_CW_ServerPlayerState` |
| 28 | `CNE_CW_ServerIllPluginInfo` |
| 29 | `CNE_CW_ServerUpdateWorldServers` |
| 30 | `CNE_CW_ServerCheckHasCharPassword` |
| 31 | `CNE_CW_ServerCharPassword` |
| 32 | `CNE_CW_ServerCreateCharacterNameCheck` |
| 33 | `CNE_CW_ServerMutuallyHelpSearch` |
| 34 | `CNC_CW_ClientHello` |
| 35 | `CNC_CW_ClientTicketInvalid` |
| 36 | `CNC_CW_ClientTextOutput` |
| 37 | `CNC_CW_ClientReceiveTicketToZoneServer` |
| 38 | `CNC_CW_ClientGetCharacters` |
| 39 | `CNC_CW_ClientVerifyCharacterName` |
| 40 | `CNC_CW_ClientResetTime` |
| 41 | `CNC_CW_ClientCancelCreateCharacterResp` |
| 42 | `CNC_CW_ClientTell` |
| 43 | `CNC_CW_ClientTextOutputIndex` |
| 44 | `CNC_CW_ClientChannelMsg` |
| 45 | `CNC_CW_ClientLookingForMemberResult` |
| 46 | `CNC_CW_ClientUpdateFamilyMember` |
| 47 | `CNC_CW_ClientFamilyOpException` |
| 48 | `CNC_CW_ClientFamilyInvitation` |
| 49 | `CNC_CW_ClientFamilyQuit` |
| 50 | `CNC_CW_ClientQueryFamilyInfo` |
| 51 | `CNC_CW_ClientFamilySchedule` |
| 52 | `CNC_CW_ClientUpdateFriendMember` |
| 53 | `CNC_CW_ClientFriendInvitation` |
| 54 | `CNC_CW_ClientRepeatedLogin` |
| 55 | `CNC_CW_ClientException` |
| 56 | `CNC_CW_ClientItemMallAnythingReceivable` |
| 57 | `CNC_CW_ClientItemMallReceivableQuery` |
| 58 | `CNC_CW_ClientItemMallReceieveAttempt` |
| 59 | `CNC_CW_ClientQueryExploitRank` |
| 60 | `CNC_CW_ClientUpdatePrestigeData` |
| 61 | `CNC_CW_ClientUpdateBlackList` |
| 62 | `CNC_CW_ClientItemMallList` |
| 63 | `CNC_CW_ClientItemMallReqBuyItem` |
| 64 | `CNC_CW_ClientItemMallPointInfo` |
| 65 | `CNC_CW_ClientReceiveTicketToWorld` |
| 66 | `CNC_CW_ClientGetWorldServers` |
| 67 | `CNC_CW_ClientFamilyAchievementUpdate` |
| 68 | `CNC_CW_ClientFamilyLogUpdate` |
| 69 | `CNC_CW_ClientCaptcha` |
| 70 | `CNC_CW_ClientCrossZone` |
| 71 | `CNE_CW_ServerApexBufferTransfer` |
| 72 | `CNC_CW_ClientApexBufferTransfer` |
| 73 | `CNE_CW_ServerQueryFamilyEmblem` |
| 74 | `CNC_CW_ClientFamilyEmblem` |
| 75 | `CNC_CW_ClientCheckHasCharPassword` |
| 76 | `CNC_CW_ClientCharPassword` |
| 77 | `CNC_CW_ClientSelectCharEnableBtn` |
| 78 | `CNC_CW_ClientUpdateSpecialCoin` |
| 79 | `CNC_CW_ClientItemMallLimit` |
| 80 | `CNC_CW_ClientQueryFamilyRecruitList` |
| 81 | `CNC_CW_ClientMutuallyHelpUpdate` |
| 82 | `CNC_CW_ClientNotifyChangeWorld` |
| 83 | `CNC_CW_ClientUpdateFortuneBagContentList` |
| 108 | `CNC_T_ServerCheckPin` |
| 109 | `CNE_T_ClientCheckPin` |
| 110 | `CNC_G_ServerAccountLogin` |
| 111 | `CNC_G_ServerAccountLogout` |
| 112 | `CNC_G_ServerBillingLogin` |
| 113 | `CNC_G_ServerClientTypeReport` |
| 114 | `CNC_G_ServerGetPointInfo` |
| 115 | `CNC_G_ServerReqBuyItem` |
| 116 | `CNE_G_ClientAccountLogin` |
| 117 | `CNE_G_ClientAccountLogout` |
| 118 | `CNE_G_ClientKickUser` |
| 119 | `CNE_G_ClientServerTypeReport` |
| 120 | `CNC_G_ClientSetWorldStates` |
| 121 | `CNE_G_ClientBillingLogin` |
| 122 | `CNE_G_ClientGetPointInfo` |
| 123 | `CNE_G_ClientReqBuyItem` |
| 124 | `CNC_G_ServerTextCommand` |
| 125 | `CNE_G_ClientTextCommand` |
| 126 | `CNC_G_ServerCharacterLogin` |
| 127 | `CNC_G_ServerCharacterLogout` |
| 128 | `CNC_G_ServerCrossWorldAccountLogoutAck` |
| 129 | `CNE_G_ClientCrossWorldAccountLogout` |
| 130 | `CNC_G_ServerCheckCommunicateLock` |
| 131 | `CNC_G_ServerExchangePinItem` |
| 132 | `CNC_G_ServerExchangePinUpdateState` |
| 133 | `CNE_G_ClientExchangePinItemResult` |
| 134 | `CNE_ZW_WorldServerIDReport` |
| 135 | `CNE_ZW_WorldServerTell` |
| 136 | `CNE_ZW_WorldServerAllUsers` |
| 137 | `CNE_ZW_WorldServerNotifyEntry` |
| 138 | `CNE_ZW_WorldServerCharacterArrived` |
| 139 | `CNE_ZW_WorldServerResetTime` |
| 140 | `CNE_ZW_WorldServerReturnToCharacterList` |
| 141 | `CNE_ZW_WorldServerTeamLeavedNotify` |
| 142 | `CNE_ZW_WorldServerChannelMsg` |
| 143 | `CNE_ZW_WorldServerFamilyCreation` |
| 144 | `CNE_ZW_WorldServerLogout` |
| 145 | `CNE_ZW_WorldServerTextCommand` |
| 146 | `CNE_ZW_WorldServerItemMallReceieveAttempt` |
| 147 | `CNE_ZW_WorldServerGetCloneItemID` |
| 148 | `CNE_ZW_WorldServerTextExCmd` |
| 149 | `CNE_ZW_WorldServerFBMsgUpdate` |
| 150 | `CNC_ZW_ZoneServerReportID` |
| 151 | `CNC_ZW_ZoneServerDestShutdown` |
| 152 | `CNC_ZW_ZoneServerCharacterArrivedDest` |
| 153 | `CNC_ZW_ZoneServerChannelMsg` |
| 154 | `CNC_ZW_ZoneServerChangeFamily` |
| 155 | `CNC_ZW_ZoneServerNewFamily` |
| 156 | `CNC_ZW_ZoneServerRemoveFamily` |
| 157 | `CNC_ZW_ZoneServerTextCommand` |
| 158 | `CNC_ZW_ZoneServerItemMallReceieveAttempt` |
| 159 | `CNC_ZW_ZoneServerGetCloneItemID` |
| 160 | `CNC_ZW_ZoneServerUpdateFortuneBag` |
| 161 | `CNC_ZW_ZoneServerUpdateLottery` |
| 162 | `CNC_ZW_ZoneServerTextExCmd` |
| 163 | `CNC_ZW_ZoneServerUpdateStarWay` |
| 164 | `CNE_MW_WorldServerItemMallMailSendResult` |
| 165 | `CNE_MW_WorldServerItemMallMailCheckOK` |
| 166 | `CNE_MW_WorldServerUpdateBlackList` |
| 167 | `CNE_MW_WorldServerUpdatePrestigeData` |
| 168 | `CNE_MW_WorldServerUpdateFriendMember` |
| 169 | `CNE_MW_WorldServerCrossZone` |
| 170 | `CNE_MW_WorldServerFBMsgUpdate` |
| 171 | `CNE_MW_WorldServerFriendNotify` |
| 172 | `CNE_MW_WorldServerUpdateSpecialCoin` |
| 173 | `CNE_MW_WorldServerCheckFamilyInvite` |
| 175 | `CNE_MW_WorldServerTell` |
| 176 | `CNE_MW_WorldServerTextExCmd` |
| 177 | `CNE_MW_WorldServerTextExCmdRemoteWorld` |
| 178 | `CNE_MW_WorldServerTextExCmdRemoteZone` |
| 191 | `CNE_DBA_ClientDelPlayerOK` |
| 204 | `CNE_DBA_ClientFamilyAllMessage` |
| 218 | `CNE_MW_WorldServerCrossWorldEndClearAccount` |

### Mission — 165 classes

| NCID | Classe |
|---|---|
| 10 | `CNE_ZMis_MissionServerCheckFamilyInvite` |
| 15 | `CNE_MW_MissionServerTextExCmd` |
| 16 | `CNE_MW_MissionServerTextExCmdRemoteWorld` |
| 17 | `CNE_MW_MissionServerTextExCmdRemoteZone` |
| 19 | `CNE_ZMis_MissionServerUpdatePKList` |
| 20 | `CNC_MW_WorldTextCommand` |
| 21 | `CNE_DBA_AgentDBACommand` |
| 28 | `CNC_DBA_ClientAllFamily` |
| 29 | `CNC_DBA_ClientNewFamily` |
| 30 | `CNC_DBA_ClientChangeFamily` |
| 31 | `CNC_DBA_ClientRemoveFamily` |
| 32 | `CNC_DBA_ClientFamilyAllMember` |
| 34 | `CNC_DBA_ClientChannelMsg` |
| 48 | `CNE_ZMis_MissionServerUpdatePlayerCard` |
| 49 | `CNE_ZMis_MissionServerItemMallMailSend` |
| 50 | `CNE_ZMis_MissionServerItemMallMailCheck` |
| 51 | `CNE_ZMis_MissionServerAddFamilyMessage` |
| 52 | `CNC_ZMis_ZoneServerReportID` |
| 53 | `CNE_ZMis_MissionServerIDReport` |
| 55 | `CNE_ZMis_MissionServerNodeCreate` |
| 56 | `CNC_ZMis_ZoneServerListDialogue` |
| 57 | `CNC_ZMis_ZoneServerReplyDialogue` |
| 58 | `CNC_ZMis_ZoneServerTransitionCommands` |
| 59 | `CNC_ZMis_ZoneServerTextOutput` |
| 60 | `CNC_ZM_ZoneServerReportMonster` |
| 61 | `CNC_ZM_ZoneServerRebornMonster` |
| 62 | `CNC_ZM_ZoneServerSetRebornMonsterResult` |
| 63 | `CNC_ZM_ZoneServerMessageToCharacter` |
| 64 | `CNC_ZM_ZoneServerCharRegenerate` |
| 65 | `CNC_ZMis_ZoneServerUpdateNPCSymbols` |
| 66 | `CNC_ZMis_ZoneServerTextOutputIndex` |
| 67 | `CNC_ZMis_ZoneServerUpdateMissionState` |
| 68 | `CNC_ZMis_ZoneServerMailList` |
| 69 | `CNC_ZMis_ZoneServerMailRead` |
| 70 | `CNC_ZMis_ZoneServerMailDeleteResult` |
| 71 | `CNC_ZMis_ZoneServerMailSendResult` |
| 72 | `CNC_ZMis_ZoneServerMailReceive` |
| 73 | `CNC_ZMis_ZoneServerPutItem` |
| 74 | `CNC_ZMis_ZoneServerDropItem` |
| 75 | `CNC_ZMis_ZoneServerMailGetItemOK` |
| 76 | `CNC_ZMis_ZoneServerMailGetGoldOK` |
| 77 | `CNC_ZMis_ZoneServerMailReturnResult` |
| 78 | `CNC_ZMis_ZoneServerAuctionBuyList` |
| 79 | `CNC_ZMis_ZoneServerAuctionBuyOK` |
| 80 | `CNC_ZMis_ZoneServerAuctionCancelOK` |
| 81 | `CNC_ZMis_ZoneServerAuctionSellList` |
| 82 | `CNC_ZMis_ZoneServerAuctionSellResult` |
| 83 | `CNC_ZMis_ZoneServerSearchInfo` |
| 84 | `CNC_ZMis_ZoneServerMessageList` |
| 85 | `CNC_ZMis_ZoneServerMessageWriteResult` |
| 86 | `CNC_ZMis_ZoneServerMessageRead` |
| 87 | `CNC_ZMis_ZoneServerMessageDeleteResult` |
| 88 | `CNC_ZMis_ZoneServerCharNotQuit` |
| 89 | `CNC_ZMis_ZoneServerLoverUpdate` |
| 90 | `CNC_ZMis_ZoneServerAchievementUpdate` |
| 91 | `CNC_ZMis_ZoneServerBattlefieldJoinResult` |
| 92 | `CNC_ZMis_ZoneServerEnterBattlefield` |
| 93 | `CNC_ZMis_ZoneServerStartBattlefield` |
| 94 | `CNC_ZMis_ZoneServerBattlefieldList` |
| 95 | `CNC_ZMis_ZoneServerBattlefieldEraseMember` |
| 96 | `CNC_ZMis_ZoneServerEnterBattlefieldAnswer` |
| 97 | `CNC_ZMis_ZoneServerBattlefieldSetup` |
| 98 | `CNC_ZMis_ZoneServerBattlefieldChangeCharNum` |
| 99 | `CNC_ZMis_ZoneServerCrossZoneResult` |
| 100 | `CNC_ZMis_ZoneServerFamilyStorageUpdate` |
| 101 | `CNC_ZMis_ZoneServerFamilyStorageLog` |
| 102 | `CNC_ZMis_ZoneServerFamilyContainerUpdate` |
| 103 | `CNC_ZMis_ZoneServerFamilyTownMemberQuery` |
| 104 | `CNC_ZMis_ZoneServerQueryPlayerCard` |
| 105 | `CNC_ZMis_ZoneServerTerritoryInfo` |
| 106 | `CNC_ZMis_ZoneServerTerritoryQuery` |
| 107 | `CNC_ZMis_ZoneServerTerritoryJoin` |
| 108 | `CNC_ZMis_ZoneServerRankInfo` |
| 109 | `CNC_ZMis_ZoneServerBuyBackList` |
| 110 | `CNC_ZMis_ZoneServerBuyBackGetItem` |
| 111 | `CNC_ZMis_ZoneServerBuybackInitAllocate` |
| 112 | `CNC_ZMis_ZoneServerDeallocateItemID` |
| 113 | `CNC_ZMis_ZoneServerNPCTalkCheck` |
| 114 | `CNC_ZMis_ZoneServerRemoteClient` |
| 115 | `CNC_ZMis_ZoneServerTextExCmd` |
| 116 | `CNC_ZMis_ZoneServerTextExCmdRemoteWorld` |
| 117 | `CNC_ZMis_ZoneServerTextExCmdRemoteZone` |
| 118 | `CNC_ZMis_ZoneServerEnterColosseum` |
| 119 | `CNC_ZMis_ZoneServerColosseumJoinResult` |
| 120 | `CNC_ZMis_ZoneServerStartColosseum` |
| 121 | `CNC_ZMis_ZoneServerColosseumLeaveJoinResult` |
| 122 | `CNC_ZMis_ZoneServerColosseumModeUpdate` |
| 123 | `CNC_ZMis_ZoneServerSearchRoomResult` |
| 124 | `CNC_ZMis_ZoneServerUpdateRoom` |
| 125 | `CNC_ZMis_ZoneServerSwitchPlayerRoom` |
| 126 | `CNC_ZMis_ZoneServerUpdateRoomItem` |
| 127 | `CNE_ZMis_MissionServerListDialogue` |
| 128 | `CNE_ZMis_MissionServerOnDialogueSelected` |
| 129 | `CNE_ZM_MonsterServerMonsterReport` |
| 130 | `CNE_ZM_MonsterServerMonsterDieReport` |
| 131 | `CNE_ZM_MonsterServerListRebornMonster` |
| 132 | `CNE_ZM_MonsterServerSetRebornMonster` |
| 133 | `CNE_ZM_MonsterServerModifyRebornMonster` |
| 134 | `CNE_ZM_MonsterServerRemoveRebornMonster` |
| 135 | `CNE_ZM_MonsterServerUnRegRebornMonster` |
| 136 | `CNE_ZM_MonsterServerRemoveNode` |
| 137 | `CNE_ZM_MonsterServerStartCharRegenerate` |
| 138 | `CNE_ZM_MonsterServerStopCharRegenerate` |
| 139 | `CNE_ZM_MonsterServerRegenerationReport` |
| 140 | `CNE_ZMis_MissionServerRemovePMS` |
| 141 | `CNE_ZMis_MissionServerCloseMissionBrief` |
| 142 | `CNE_ZMis_MissionServerMigrationDone` |
| 143 | `CNE_ZMis_MissionServerGiveUpMission` |
| 144 | `CNE_ZMis_MissionServerMobDieReport` |
| 145 | `CNE_ZMis_MissionServerMailSend` |
| 146 | `CNE_ZMis_MissionServerSendSystemMail` |
| 147 | `CNE_ZMis_MissionServerMessageWrite` |
| 148 | `CNE_ZMis_MissionServerChannelMsg` |
| 149 | `CNE_ZMis_MissionServerBattlefieldJoin` |
| 150 | `CNE_ZMis_MissionServerBattlefieldEraseMember` |
| 151 | `CNE_ZMis_MissionServerBattlefieldClose` |
| 152 | `CNE_ZMis_MissionServerBattlefieldList` |
| 153 | `CNE_ZMis_MissionServerEnterBattlefieldAnswer` |
| 154 | `CNE_ZMis_MissionServerBattlefieldUpdateStatus` |
| 155 | `CNE_ZMis_MissionServerCrossZoneRequest` |
| 156 | `CNE_ZMis_MissionServerCrossZone` |
| 157 | `CNE_ZMis_MissionServerRemoteClient` |
| 158 | `CNE_ZMis_MissionServerTextExCmd` |
| 159 | `CNE_ZMis_MissionServerTextExCmdRemoteWorld` |
| 160 | `CNE_ZMis_MissionServerTextExCmdRemoteZone` |
| 161 | `CNE_ZMis_MissionServerGetTeamInviteeID` |
| 162 | `CNC_ZMis_ZoneServerReturnTeamInviteeID` |
| 163 | `CNC_ZMis_ZoneServerGMToolGetInfo` |
| 164 | `CNC_ZMis_ZoneServerGMToolGetMission` |
| 165 | `CNE_ZMis_MissionServerColosseumJoinAnswer` |
| 166 | `CNE_ZMis_MissionServerColosseumJoinRequest` |
| 167 | `CNE_ZMis_MissionServerColosseumClose` |
| 168 | `CNE_ZMis_MissionServerColosseumLeaveJoin` |
| 169 | `CNC_ZMis_ZoneServerAchievementRewardMail` |
| 170 | `CNC_ZMis_ZoneServerResetDailyMission` |
| 172 | `CNC_ZMis_ZoneServerUpdateLifelongPoint` |
| 173 | `CNC_ZMis_ZoneServerUpdateLifelongGold` |
| 174 | `CNC_ZMis_ZoneServerResetDailyMonsterKill` |
| 176 | `CNE_ZMis_MissionServerRefreshRecommendActivities` |
| 178 | `CNC_ZMis_ZoneServerFamilyBattleInfo` |
| 179 | `CNC_ZMis_ZoneServerResetNodeTimes` |
| 180 | `CNE_ZMis_MissionServerSetRecommendActivityTracing` |
| 182 | `CNC_ZMis_ZoneServerPVPTopData` |
| 183 | `CNC_ZMis_ZoneServerGreedyIslandJoin` |
| 184 | `CNC_ZMis_ZoneServerGreedyIslandInfo` |
| 185 | `CNE_ZMis_MissionServerGreedyIslandRankQuery` |
| 186 | `CNC_ZMis_ZoneServerColosseumQuery` |
| 187 | `CNE_ZMis_MissionServerBattleRoyaleJoinRequest` |
| 188 | `CNC_ZMis_ZoneServerStartBattleRoyale` |
| 189 | `CNE_ZMis_MissionServerBattleRoyaleClose` |
| 190 | `CNE_ZMis_MissionServerPlayerCCDataSave` |
| 191 | `CNC_DBA_ClientFastDress` |
| 192 | `CNC_ZMis_ZoneServerFastDressEquipEnd` |
| 193 | `CNC_ZMis_ZoneServerWeddingsInfo` |
| 194 | `CNE_ZMis_MissionServerVersusInstanceJoinRequest` |
| 195 | `CNC_ZMis_ZoneServerStartVersusInstance` |
| 196 | `CNE_ZMis_MissionServerFindCharacter` |
| 197 | `CNC_ZMis_ZoneServerFindTargetLocation` |
| 198 | `CNE_ZMis_MissionServerSendTargetLocation` |
| 199 | `CNC_ZMis_ZoneServerTransportGMCharacterToCharacter` |
| 200 | `CNC_ZMis_ZoneServerStartBattleCrusade` |
| 201 | `CNC_ZMis_ZoneServerEnterBattleCrusadeAnswer` |
| 202 | `CNC_ZMis_ZoneServerBattleCrusadeInfo` |
| 203 | `CNE_ZMis_MissionServerArrangeItem` |
| 204 | `CNC_DBA_ClientLotteryCount` |

### Login — 10 classes

| NCID | Classe |
|---|---|
| 0 | `CNC_CL_ServerHello` |
| 1 | `CNC_CL_ServerLoginAccount` |
| 2 | `CNC_CL_ServerLoginWorld` |
| 3 | `CNC_CL_ClientHello` |
| 4 | `CNC_CL_ClientLoginFail` |
| 5 | `CNC_CL_ClientGetWorldServers` |
| 6 | `CNC_CL_ClientReceiveTicketToWorldServer` |
| 253 | `CNC_G_ServerSCardVerify` |
| 254 | `CNC_CL_ServerSCardVerify` |
| 255 | `CNC_CL_ClientSCardInfo` |

### Gateway — 11 classes

| NCID | Classe |
|---|---|
| 0 | `CNC_GJ_ServerUsePoint` |
| 6 | `CNC_G_ClientAccountLogin` |
| 7 | `CNC_G_ClientAccountLogout` |
| 8 | `CNC_G_ClientKickUser` |
| 9 | `CNC_G_ClientServerTypeReport` |
| 11 | `CNC_G_ClientBillingLogin` |
| 12 | `CNC_G_ClientGetPointInfo` |
| 13 | `CNC_G_ClientReqBuyItem` |
| 15 | `CNC_G_ClientTextCommand` |
| 19 | `CNC_G_ClientCrossWorldAccountLogout` |
| 22 | `CNC_G_ClientExchangePinItemResult` |

### Ticket — 1 classes

| NCID | Classe |
|---|---|
| 0 | `CNC_T_ClientCheckPin` |
