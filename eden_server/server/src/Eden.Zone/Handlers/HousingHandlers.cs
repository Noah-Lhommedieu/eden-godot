using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Housing (salle personnelle), tenues rapides (FastDress), placard,
/// collection, loterie — commandes d'état/préférences du joueur.
/// </summary>
public static class HousingHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // --- Salle personnelle (CGlobalInfo::PlayerRoomSwitch natif) ---
        // 206 RentRoom [s16 hotel_type] — Execute @0x827e610 :
        // CDBAgent::Command « Rent Player Room hotel_type= %hd ».
        d.RegisterChar(() => new CNE_CZ_ZoneServerRentRoom(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Rent Player Room hotel_type= {msg.Value}", token);
            });

        // 212 PayRent [] — Execute @0x826e900 :
        // « PlayerRoom Pay Rent ».
        d.RegisterChar(() => new CNE_CZ_ZoneServerPayRent(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        "PlayerRoom Pay Rent", token);
            });

        // 217 ExtendRoom [i32 room_id] — zone-side natif
        // CPlayerRoomManager::RoomExtend(char,room_id) @0x8150180 :
        // salle dans le registre + own-room (0x10d9) + or ≥ param
        // (409+extend)×1000 (0x232) → « RoomExtend room_id = %d ».
        d.RegisterChar(() => new CNE_CZ_ZoneServerExtendRoom(),
            async (session, msg, charId, token) =>
            {
                var room = s.Rooms.Get(msg.Value);
                if (room == null || room.RoomId != charId)
                    return; // natif : 0x10d9 (pas sa salle)
                // Check or natif (param 409+extend ×1000) — la
                // vérification exacte vit côté mission (exec+0x8c),
                // la zone relaie au format natif quoi qu'il arrive.
                var cost = (int)(s.GameData.Parameters.Get(
                    409 + room.Extend) * 1000f);
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"RoomExtend room_id = {room.RoomId}", token);
            });

        // 210 RoomGetData [] — intercepté par ZoneEntryFlow
        // (HandleRoomGetDataAsync : 541 depuis le registre Rooms).
        // L'enregistrement ci-dessous n'est jamais atteint
        // (TryHandleAsync court-circuite le dispatcher) — conservé
        // pour documenter le NCID dans la matrice.
        d.RegisterChar(() => new CNE_CZ_ZoneServerRoomGetData(),
            (session, msg, charId, token) => Task.CompletedTask);

        // 215 ChangeRoomTexture [i32 room_id][s16 index][s16 loc] —
        // Execute @0x8240c40 : « Change Room Texture room_id= %d
        // index= %hd loc= %hd ».
        d.RegisterChar(() => new CNE_CZ_ZoneServerChangeRoomTexture(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Change Room Texture room_id= {msg.Field1} " +
                        $"index= {msg.Field2} loc= {msg.Field3}", token);
            });

        // 205 SearchRoom [s16 hotel_type][str name] — Execute
        // @0x82844d0 : rejet si le nom contient '%' (pattern LIKE
        // injecté), nom wrapé « '%...%' » puis
        // « Search Player Room search_name= %s hotel_type= %hd ».
        d.RegisterChar(() => new CNE_CZ_ZoneServerSearchRoom(),
            async (session, msg, charId, token) =>
            {
                if (msg.Field2.Contains('%')) return;
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Search Player Room search_name= " +
                        $"'%{msg.Field2}%' hotel_type= {msg.Field1}",
                        token);
            });

        // 208 SetRoomPrivilege [i32 lover][i32 friend][i32 family]
        // [i32 other] — Execute @0x82897d0 :
        // « Set Room Privilege lover_pri= %d friend_pri= %d
        //  family_pri= %d other_pri= %d » (ordre des champs wire).
        d.RegisterChar(() => new CNE_CZ_ZoneServerSetRoomPrivilege(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Set Room Privilege lover_pri= {msg.Field1} " +
                        $"friend_pri= {msg.Field2} " +
                        $"family_pri= {msg.Field3} " +
                        $"other_pri= {msg.Field4}", token);
            });

        // 211 UpdateDecoration [s16 index][s16 loc][u16 is_decorate]
        // [u16 pos_x][u16 pos_y][u16 pos_z][u16 dir_x][u16 dir_y]
        // [u16 dir_z][u16 rotate] — Execute @0x829ab00 : commande
        // « Update Room Decoration index= ... » à 10 champs.
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateDecoration(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Update Room Decoration index= {msg.Field1} " +
                        $"loc= {msg.Field2} is_decorate= {msg.Field3} " +
                        $"pos_x= {msg.Field4} pos_y= {msg.Field5} " +
                        $"pos_z= {msg.Field6} dir_x= {msg.Field7} " +
                        $"dir_y= {msg.Field8} dir_z= {msg.Field9} " +
                        $"rotate= {msg.Field10}", token);
            });

        // 209 SwitchSetRoomMode [s16 mode] — Execute @0x828d790 :
        // « Switch Room Setting Mode room_id= %d mode= %hd » — le
        // room_id natif = char+0xbf0 (salle du joueur) ; le serveur
        // mission résout via le charUid.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSwitchSetRoomMode(),
            async (session, msg, charId, token) =>
            {
                if (s.Mission?.Connected == true)
                    await s.Mission.SendAgentDbaCommandAsync((uint)charId,
                        $"Switch Room Setting Mode room_id= {charId} " +
                        $"mode= {msg.Value}", token);
            });

        // SloganUpdate [int roomId] — slogan de la salle
        // MORT EN NATIF : Execute = tail-call GetType() ignoré.
        d.RegisterChar(() => new CNE_CZ_ZoneServerSloganUpdate(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Tenues rapides (FastDress — sets d'équipement sauvegardés) ---
        // SaveFastDressData [int slot] — sauvegarde l'équipement courant
        d.RegisterChar(() => new CNE_CZ_ZoneServerSaveFastDressData(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] SaveFastDress char={charId} set={msg.Value}");
                // TODO(phase reverse): table character_fastdress
                return Task.CompletedTask;
            });

        // ApplyFastDressData [int slot, int ?] — équipe le set sauvegardé
        d.RegisterChar(() => new CNE_CZ_ZoneServerApplyFastDressData(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] ApplyFastDress char={charId} set={msg.Field1}");
                return Task.CompletedTask;
            });

        // RecordFastDressInfo [int×5] — enregistre un item dans un set
        d.RegisterChar(() => new CNE_CZ_ZoneServerRecordFastDressInfo(),
            (session, msg, charId, token) => Task.CompletedTask);

        // RemoveFastDressInfo [short setId, int itemUid, int ?]
        d.RegisterChar(() => new CNE_CZ_ZoneServerRemoveFastDressInfo(),
            (session, msg, charId, token) => Task.CompletedTask);

        // UpdateFastDressPage [int page]
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateFastDressPage(),
            (session, msg, charId, token) => Task.CompletedTask);

        // EnterNodeFastDress — requête initiale des sets à l'entrée
        d.RegisterChar(() => new CNE_CZ_ZoneServerEnterNodeFastDress(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Placard (Closet — stockage cosmétique) ---
        // Interceptés par TryHandleOnLoopAsync (cases 165/167 —
        // garde-robe costumes, garde-fous pré-entrée seulement).
        d.RegisterChar(() => new CNE_CZ_ZoneServerClosetSet(),
            (session, msg, charId, token) => Task.CompletedTask);

        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryClosets(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Collection (cartes de monstres) ---
        // Interceptés par TryHandleOnLoopAsync (cases 150/151/152 —
        // dépôt/retrait/requête, voir ZoneEntryFlow.Collection) ;
        // ces stubs ne servent que si le paquet arrive avant
        // l'entrée complète du perso.
        d.RegisterChar(() => new CNE_CZ_ZoneServerGetCollection(),
            (session, msg, charId, token) => Task.CompletedTask);

        d.RegisterChar(() => new CNE_CZ_ZoneServerQueryCollection(),
            (session, msg, charId, token) => Task.CompletedTask);

        d.RegisterChar(() => new CNE_CZ_ZoneServerSaveCollection(),
            (session, msg, charId, token) => Task.CompletedTask);

        // --- Loterie / dés du destin ---
        d.RegisterChar(() => new CNE_CZ_ZoneServerCheckUpdateLottery(),
            (session, msg, charId, token) => Task.CompletedTask);

        // GoLottery [short,ushort,short,short,short] — tirage
        d.RegisterChar(() => new CNE_CZ_ZoneServerGoLottery(),
            (session, msg, charId, token) =>
            {
                EdenLog.Write($"[Zone] GoLottery char={charId}");
                return Task.CompletedTask;
            });

        // EvaluateLotteryOpen (255) — intercepté par
        // TryHandleOnLoopAsync → ZoneEntryFlow.Evaluate.cs
        // (CEvaluate::LotteryOpen natif complet : pick/claim/buy/auto).

        // UpdateLotteryBonus — intercepté par TryHandleOnLoopAsync
        // (224 → Lottery.cs : validation anti-dérive du bonus exact,
        // reset + resync 448 si mismatch).
        d.RegisterChar(() => new CNE_CZ_ZoneServerUpdateLotteryBonus(),
            (session, msg, charId, token) => Task.CompletedTask);

        // 203 DestinyDice + 204 QueryDestinyRoad : interceptés dans
        // ZoneEntryFlow.TryHandleOnLoopAsync → ZoneEntryFlow.Destiny.cs
        // (DestinyRoadAction natif complet).
    }
}
