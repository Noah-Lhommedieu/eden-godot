using System;
using System.Collections.Generic;
using EdenClient.Net;
using EdenClient.Session;

namespace EdenClient.Protocol;

/// <summary>Enregistrement générique des parseurs « complétude » :
/// chaque opcode bindé désérialise dans sa classe puis alimente
/// <see cref="MiscPackets"/> (dernier reçu + événement posté main).
/// Les paquets arrivent ainsi parsés — les états/UX se branchent
/// ensuite sur <see cref="MiscPackets.Received"/> sans course.</summary>
public static class MiscPackets
{
    /// <summary>Dernier message désérialisé par opcode (thread-safe).</summary>
    public static IReadOnlyDictionary<int, NetMessage> Latest => _latest;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, NetMessage> _latest = new();
    /// <summary>Émis sur le thread principal après chaque message.</summary>
    public static event Action<NetMessage>? Received;
    /// <summary>opcodes vus cette session (diag « réellement émis »).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> Seen = new();

    private static void Note(NetMessage m)
    {
        _latest[m.Opcode] = m;
        Seen.AddOrUpdate(m.Opcode, 1, (_, c) => c + 1);
    }

    /// <summary>Bind tous les parseurs zone générés (mutation + event).</summary>
    public static void BindZone(EdenClient.Session.EdenSession s,
        Action<Action> enqueue)
    {
        void B<T>(T msg) where T : NetMessage, new() =>
            s.OnZone(msg.Opcode, r =>
            {
                var m = new T();
                m.Deserialize(r);
                Note(m);
                enqueue(() => Received?.Invoke(m));
            });
        B(new ZoneClientElfCastEnchant());
        B(new ZoneClientTicketInvalid());
        B(new ZoneClientResetCoord());
        B(new ZoneClientRestore());
        B(new ZoneClientGetSpell());
        B(new ZoneClientPlayerAction());
        B(new ZoneClientEffectReport());
        B(new ZoneClientDestShutdown());
        B(new ZoneClientPlayerEmotion());
        B(new ZoneClientSellOut());
        B(new ZoneClientCanCloseConnection());
        B(new ZoneClientSafeQuitCancel());
        B(new ZoneClientSafeQuitResp());
        B(new ZoneClientNonSafeQuit());
        B(new ZoneClientDisplayCharSimple());
        B(new ZoneClientResurrect());
        B(new ZoneClientAskRepairEquipGold());
        B(new ZoneClientAskRepairOneItemGold());
        B(new ZoneClientEnterFamilyCreation());
        B(new ZoneClientEnterSpellShop());
        B(new ZoneClientEnterRepairShop());
        B(new ZoneClientUseSkillPointOK());
        B(new ZoneClientLogoutResp());
        B(new ZoneClientCanLogout());
        B(new ZoneClientPutItemIntoStorage());
        B(new ZoneClientPutItemIntoContainer());
        B(new ZoneClientSpellLoopCastStop());
        B(new ZoneClientQueryNpc());
        B(new ZoneClientAskIdentifyGold());
        B(new ZoneClientIdentifyItem());
        B(new ZoneClientEnterIdentifyShop());
        B(new ZoneClientEnterExchageItemService());
        B(new ZoneClientResetSpell());
        B(new ZoneClientModifyItemInfo());
        B(new ZoneClientEnterInlayShop());
        B(new ZoneClientInlayItemResult());
        B(new ZoneClientSloganUpdate());
        B(new ZoneClientPlayEffect());
        B(new ZoneClientSelectFamilyEmblem());
        B(new ZoneClientOpenExploitRank());
        B(new ZoneClientTeamRadarAttention());
        B(new ZoneClientBagDueDate());
        B(new ZoneClientUpdateInteraction());
        B(new ZoneClientUseItemTime());
        B(new ZoneClientUseItemTimeBreak());
        B(new ZoneClientSearchInfo());
        B(new ZoneClientElfWorkResult());
        B(new ZoneClientElfWorkResultEnd());
        B(new ZoneClientElfGoToWork());
        B(new ZoneClientElfDecompose());
        B(new ZoneClientElfStoneUse());
        B(new ZoneClientEnterNodeElfContainer());
        B(new ZoneClientEnterStrengthen());
        B(new ZoneClientStrengthenItemCancel());
        B(new ZoneClientStrengthenItemResult());
        B(new ZoneClientModifyItemStrengthen());
        B(new ZoneClientSetSpellStone());
        B(new ZoneClientSummonPet());
        B(new ZoneClientEnterElfReturn());
        B(new ZoneClientMobChangeAI());
        B(new ZoneClientTurnChar());
        B(new ZoneClientElfReturnEffect());
        B(new ZoneClientPVPTime());
        B(new ZoneClientLotteryResult());
        B(new ZoneClientLotteryEnd());
        B(new ZoneClientLotteryUpdatePlus());
        B(new ZoneClientDuelRequest());
        B(new ZoneClientDuelRequestResult());
        B(new ZoneClientDuelBegin());
        B(new ZoneClientDuelResult());
        B(new ZoneClientEnterMessageBoard());
        B(new ZoneClientMessageList());
        B(new ZoneClientMessageWriteResult());
        B(new ZoneClientMessageRead());
        B(new ZoneClientMessageDeleteResult());
        B(new ZoneClientItemRent());
        B(new ZoneClientBattlefieldList());
        B(new ZoneClientEnterBattlefield());
        B(new ZoneClientBattlefieldJoinResult());
        B(new ZoneClientBattlefieldResult());
        B(new ZoneClientBattlefieldUpdateScore());
        B(new ZoneClientAltarState());
        B(new ZoneClientBattleMemberPosition());
        B(new ZoneClientBattlefieldMemberInfo());
        B(new ZoneClientTextOutputItem());
        B(new ZoneClientEnterFamilyBuilding());
        B(new ZoneClientFamilyContainerData());
        B(new ZoneClientFamilyContainerDeleteItem());
        B(new ZoneClientFamilyContainerPutItem());
        B(new ZoneClientFamilyInstanceData());
        B(new ZoneClientPlaySound());
        B(new ZoneClientPlayMonsterAction());
        B(new ZoneClientPlayerEvent());
        B(new ZoneClientEnterFamilyInstanceCreation());
        B(new ZoneClientFamilyTownQuery());
        B(new ZoneClientFamilyTownMemberQuery());
        B(new ZoneClientUpdateLottery());
        B(new ZoneClientBuyBackList());
        B(new ZoneClientEnterFamilyTownList());
        B(new ZoneClientTerritoryQuery());
        B(new ZoneClientTerritoryInfo());
        B(new ZoneClientCanJoinTeam());
        B(new ZoneClientRollTreasure());
        B(new ZoneClientRankInfo());
        B(new ZoneClientHateReport());
        B(new ZoneClientEnterGladiatorTeamCreation());
        B(new ZoneClientGladiatorTeamInfo());
        B(new ZoneClientDisbandGladiatorTeam());
        B(new ZoneClientEnterColosseum());
        B(new ZoneClientColosseumJoinResult());
        B(new ZoneClientColosseumTeamInfo());
        B(new ZoneClientColosseumLeaveJoinResult());
        B(new ZoneClientTerritoryKilledInfo());
        B(new ZoneClientCoRideInvite());
        B(new ZoneClientUpdateRoom());
        B(new ZoneClientOpenHotel());
        B(new ZoneClientSetRoomResult());
        B(new ZoneClientRoomData());
        B(new ZoneClientUpdateRoomItem());
        B(new ZoneClientLifeSkillUpdate());
        B(new ZoneClientRideSpell());
        B(new ZoneClientSpellFlyToLocation());
        B(new ZoneClientTerritoryEnd());
        B(new ZoneClientUpdateSpellAttr());
        B(new ZoneClientUpdateHealthOnlineTime());
        B(new ZoneClientResetDailyMission());
        B(new ZoneClientColosseumModeQueryResult());
        B(new ZoneClientRecruitInstanceResult());
        B(new ZoneClientUpdateLifelongPoint());
        B(new ZoneClientUpdateLifelongGold());
        B(new ZoneClientChanceLuckyBagResult());
        B(new ZoneClientFamilyBattleInfo());
        B(new ZoneClientOpenInstanceDialog());
        B(new ZoneClientR25UpdateFamily());
        B(new ZoneClientTerritoryFamilyScore());
        B(new ZoneClientLookElfPetEquip());
        B(new ZoneClientGreedyIslandInfo());
        B(new ZoneClientUpdateWarCoin());
        B(new ZoneClientTeamCheckNotify());
        B(new ZoneClientUpdateFloorInfo());
        B(new ZoneClientOpenAreaDialog());
        B(new ZoneClientUpdateAlpacaCoin());
        B(new ZoneClientLifeFloat());
        B(new ZoneClientCheckTeamTransport());
        B(new ZoneClientOpenMissionShop());
        B(new ZoneClientTerritoryPlayerInfo());
        B(new ZoneClientColosseumQuery());
        B(new ZoneClientBattleRoyaleInfo());
        B(new ZoneClientUpdateFastDressData());
        B(new ZoneClientInGameLotteryNotify());
        B(new ZoneClientDiamondLotteryNotify());
        B(new ZoneClientOpenChurchDialog());
        B(new ZoneClientVersusInstanceInfo());
        B(new ZoneClientOpenPaySystem());
        B(new ZoneClientPayEvent());
        B(new ZoneClientPayEventOK());
        B(new ZoneClientSlotFlicker());
        B(new ZoneClientUpdateAttrNewHp());
        // Codecs complexes écrits à la main (ZoneComplexMessages.cs).
        B(new ZoneClientSpellBlastOK());
        B(new ZoneClientSpellBlastToLocation());
        B(new ZoneClientReceiveShortcutOfPage());
        B(new ZoneClientLookEquip());
        B(new ZoneClientQueryPlayerCard());
        B(new ZoneClientSearchTeamInfo());
        B(new ZoneClientSearchRoomResult());
        B(new ZoneClientFamilyBattleKilledRank());
        B(new ZoneClientPVPRankLookEquip());
        B(new ZoneClientLoadCubeItems());
        B(new ZoneClientSearchTeamInfoV2());
        // NCIDs émis jusque-là sans classe (ZoneEntryExtraMessages).
        B(new ZoneClientLevelUp());
        B(new ZoneClientUpdateRecommendActivities());
        B(new ZoneClientEquipTrainItemChanged());
        B(new ZoneClientEvaluateInfo());
        B(new ZoneClientUpdateEvaluateInfo());
        B(new ZoneClientUIControlNotify());
        B(new ZoneClientChangeSpell());
        B(new ZoneClientOpenMagicBoxDialog());
        B(new ZoneClientMagicBoxResult());
        B(new ZoneClientBackItem());
        B(new ZoneClientUpdateBattleCrusadeInfo());
        B(new ZoneClientOpenBattleCrusadeDialog());
        B(new ZoneClientPetLotteryResult());
        B(new ZoneClientPetLotteryEnd());
        B(new ZoneClientArrangeItemOK());
    }

    /// <summary>Bind les parseurs monde générés.</summary>
    public static void BindWorld(EdenClient.Session.EdenSession s,
        Action<Action> enqueue)
    {
        void B<T>(T msg) where T : NetMessage, new() =>
            s.OnWorld(msg.Opcode, r =>
            {
                var m = new T();
                m.Deserialize(r);
                Note(m);
                enqueue(() => Received?.Invoke(m));
            });
        B(new WorldClientCancelCreateCharacterResp());
        B(new WorldClientTell());
        B(new WorldClientTextOutputIndex());
        B(new WorldClientLookingForMemberResult());
        B(new WorldClientCharPassword());
        B(new WorldClientCheckHasCharPassword());
        B(new WorldClientCrossZone());
        B(new WorldClientFamilyAchievementUpdate());
        B(new WorldClientMutuallyHelpUpdate());
        B(new WorldClientNotifyChangeWorld());
        B(new WorldClientQueryExploitRank());
        B(new WorldClientReceiveTicketToWorld());
        B(new WorldClientSelectCharEnableBtn());
        B(new WorldClientUpdateFortuneBagContentList());
        B(new WorldClientApexBufferTransfer());
    }
}