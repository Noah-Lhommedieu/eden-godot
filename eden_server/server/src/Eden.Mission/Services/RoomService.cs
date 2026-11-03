using Eden.Core.Logging;
using Eden.Mission.Services;
namespace Eden.Mission.Services;

/// <summary>
/// Sous-système « salles personnelles » (housing) — port de
/// `CPlayerRoomDBA`/`CPlayerRoomDBO` + des `TC_*Room*` natifs
/// (mission @0x81fe220-0x821bcxx).
///
/// Modèle natif : une salle par perso (`player_room.player_id`),
/// chargée en cache mission au boot ; chaque mutation persiste puis
/// diffuse `CNC_ZMis_ZoneServerUpdateRoom` (ZMis 779, broadcast zones)
/// — les zones entretiennent leur propre `map&lt;room_id,CPlayerRoom&gt;`
/// (`CZoneServer+0x564`) qui sert à `EnterRoomCheck`.
///
/// Commandes texte natives relayées par la zone via `AgentDBACommand`
/// (même mécanisme que le courrier) :
///   « Rent Player Room hotel_type= %hd »      → TC_RentPlayerRoom
///   « PlayerRoom Pay Rent »                    → TC_RoomPayRent
///   « Search Player Room search_name= %s hotel_type= %hd »
///                                             → TC_SearchPlayerRoom
///   « Set Room Privilege lover_pri= .. »       → TC_SetRoomPrivilege
///   « Change Room Texture room_id= %d index= %hd loc= %hd »
///                                             → TC_ChangeRoomTexture
///   « Switch Room Setting Mode room_id= %d mode= %hd »
///                                             → TC_SwitchRoomSettingMode
///   « Update Room Decoration index= .. »       → TC_UpdateRoomDecoration
///   « Update RoomData room_id= %d type= %hd value= %d »
///                                             → TC_UpdateRoomData
///   « RoomExtend room_id = %d »                → TC_RoomExtend
///   « Enter Player Room room_id= %d »          → TC_EnterPlayerRoom
///
/// Coûts/validations natifs reproduits :
///   rent    : niveau &gt; 0x22, h1=niv×1500 / h2=niv×1000 or, h3=famille,
///             due_date = now+0x93a80 (7 j) — TC_RentPlayerRoom
///   payrent : unités en retard = (now-due)/0x93a80+1, coût/unité idem,
///             due += unités×0x93a80 — TC_RoomPayRent/DBO::PayRent
///   extend  : coût = S_Parameter[409+extend]×1000, extend++ —
///             TC_RoomExtend (params 409-412)
/// </summary>
public sealed class RoomService
{
    /// <summary>Durée de location native : 0x93a80 s = 7 jours.</summary>
    public const int RentDurationSec = 0x93a80;
    /// <summary>Niveau minimal natif (check `0x22 &lt; level`).</summary>
    public const int MinLevel = 0x22;
    /// <summary>Cap natif de résultats de recherche (0x31).</summary>
    public const int SearchCap = 0x31;
    /// <summary>Premier id S_Parameter du coût d'extension
    /// (`AllFormulaParas+0x664` → index 409 ; 409=1500,410=3500,
    /// 411=7500, 412=0 → gratuit au-delà — bizarrerie native).</summary>
    public const int ExtendCostParamBase = 409;

    // Indices de texte natifs (canal TextOutputIndex flag 0x9b).
    private const short TextLevelLow = 0xdfa;     // niveau < 35
    private const short TextSameType = 0xdfb;     // même hotel_type
    private const short TextNoGold = 0xdfc;       // or insuffisant
    private const short TextExpired = 0xf2d;      // location expirée
    private const short TextRentOk = 0xf2e;       // location réussie
    private const short TextPayFail = 0xf2f;      // UPDATE due_date ko
    private const short TextPayOk = 0xf2a;        // loyer payé
    private const short TextNotDue = 0xf20;       // pas encore d'échéance
    private const short TextExtendFail = 0x232;   // or insuffisant
    private const short TextNoFamily = 0xf48;     // pas de famille
    private const short TextFamilyNoRoom = 0xf49; // famille sans salle
    private const short TextRoomMissing = 0xdf7;  // salle introuvable

    /// <summary>Op du push 779 (champ s16 tête) : 1 suppression,
    /// 2 création/mise à jour même type, 3 changement d'hotel_type —
    /// `local_2a = (oldHotel != newHotel) + 2` dans TC_RentPlayerRoom.
    /// Les TC de mise à jour (payrent/extend/privilege…) poussent 2.</summary>
    private const short OpDelete = 1;
    private const short OpUpdate = 2;
    private const short OpRetype = 3;

    /// <summary>Cache room_id → ligne (miroir `map&lt;id,CPlayerRoomDBA*&gt;`
    /// de `CGlobalInfo::PlayerRoomDBO`).</summary>
    private readonly Dictionary<int, MissionDb.RoomRow> _rooms = new();

    private readonly Func<ushort, byte[], Task> _broadcast;
    private readonly Func<int, ushort, byte[], Task> _sendToChar;
    private readonly Func<int, short, Task> _sendText;
    private readonly Func<string, Task> _sendWorldText;
    private readonly Func<int, float> _extendCost;
    private readonly Func<int, Task<(int Level, int Gold, int FamilyId, string Name)?>> _execStats;

    /// <param name="broadcast">ZMis → toutes les zones (779/781).</param>
    /// <param name="sendToChar">ZMis → zone propriétaire du char (778).</param>
    /// <param name="sendText">TextOutputIndex (charUid, index).</param>
    /// <param name="sendWorldText">CNC_MW_WorldTextCommand (20) monde —
    /// natif : « VisitPlayerRoom ch_id= %d room_id= %d ».</param>
    /// <param name="extendCost">S_Parameter.Get(id) côté mission.</param>
    /// <param name="execStats">(level, gold, familyId) du char —
    /// `CExecutionerData+0x5c/+0x8c/+0xb0` natifs.</param>
    public RoomService(
        Func<ushort, byte[], Task> broadcast,
        Func<int, ushort, byte[], Task> sendToChar,
        Func<int, short, Task> sendText,
        Func<string, Task> sendWorldText,
        Func<int, float> extendCost,
        Func<int, Task<(int, int, int, string)?>> execStats)
    {
        _broadcast = broadcast;
        _sendToChar = sendToChar;
        _sendText = sendText;
        _sendWorldText = sendWorldText;
        _extendCost = extendCost;
        _execStats = execStats;
    }

    /// <summary>Charge toutes les salles (SELECT natif `Load
    /// PlayerRoom` au boot de la mission).</summary>
    public async Task LoadAsync(MissionDb db)
    {
        foreach (var r in await db.LoadRoomsAsync())
            _rooms[r.PlayerId] = r;
        EdenLog.Write($"[Mission] player_room: {_rooms.Count} salles");
    }

    /// <summary>Salle d'un perso (`CGlobalInfo::GetPlayerRoom`).</summary>
    public MissionDb.RoomRow? Get(int playerId)
        => _rooms.TryGetValue(playerId, out var r) ? r : null;

    /// <summary>Push de l'état courant vers une zone qui vient de se
    /// connecter — substitut du `CNE_DBA_ClientAllPlayerRoom` (699)
    /// natif qui remplissait `CZoneServer+0x564` au démarrage.</summary>
    public async Task PushAllToAsync(ushort ncid, Func<byte[], Task> send)
    {
        foreach (var r in _rooms.Values)
            await send(BuildUpdateRoom(OpUpdate, r, 0));
    }

    /// <summary>Dispatch d'une commande room — retourne true si la
    /// commande a été consommée. `cmd` = commande texte complète
    /// (les noms peuvent contenir des espaces).</summary>
    public async Task<bool> TryHandleAsync(MissionDb db, int charUid,
        string cmd, string[] w, Func<string, int> arg)
    {
        switch (w[0])
        {
            case "Rent" when w.Length > 2 && w[1] == "Player":
                await RentAsync(db, charUid, (short)arg("hotel_type="));
                return true;
            case "PlayerRoom" when w.Length > 2 && w[1] == "Pay":
                await PayRentAsync(db, charUid);
                return true;
            case "Search" when w.Length > 2 && w[1] == "Player":
                // « search_name= <motif LIKE> hotel_type= <n> » — le
                // motif peut contenir des espaces : sous-chaîne entre
                // « search_name= » et « hotel_type= ».
                var name = SearchName(cmd);
                await SearchAsync(db, charUid, name,
                    (short)arg("hotel_type="));
                return true;
            case "Set" when w.Length > 2 && w[1] == "Room":
                await SetPrivilegeAsync(db, charUid,
                    arg("lover_pri="), arg("friend_pri="),
                    arg("family_pri="), arg("other_pri="));
                return true;
            case "Change" when w.Length > 2 && w[1] == "Room":
                await ChangeTextureAsync(db, charUid, arg("room_id="),
                    (short)arg("index="), (short)arg("loc="));
                return true;
            case "Switch" when w.Length > 2 && w[1] == "Room":
                await SwitchModeAsync(db, charUid, arg("room_id="),
                    (short)arg("mode="));
                return true;
            case "Update" when w.Length > 2 && w[1] == "RoomData":
                await UpdateRoomDataAsync(db, charUid, arg("room_id="),
                    (short)arg("type="), arg("value="));
                return true;
            case "Update" when w.Length > 2 && w[1] == "Room":
                await UpdateDecorationAsync(db, charUid,
                    (short)arg("index="), (short)arg("loc="),
                    (ushort)arg("is_decorate="),
                    (ushort)arg("pos_x="), (ushort)arg("pos_y="),
                    (ushort)arg("pos_z="), (ushort)arg("dir_x="),
                    (ushort)arg("dir_y="), (ushort)arg("dir_z="),
                    (ushort)arg("rotate="));
                return true;
            case "RoomExtend":
                await ExtendAsync(db, charUid, arg("room_id"));
                return true;
            case "Enter" when w.Length > 2 && w[1] == "Player":
                await EnterAsync(charUid, arg("room_id="));
                return true;
        }
        return false;
    }

    /// <summary>TC_RentPlayerRoom @0x81fe220 : validations puis
    /// INSERT/UPDATE + push 779 (coût déduit par la zone au champ
    /// `cost` — `iVar25 = -cost` dans l'Execute zone).</summary>
    private async Task RentAsync(MissionDb db, int charUid,
        short hotelType)
    {
        var st = await _execStats(charUid);
        if (st == null) return;
        var (level, gold, familyId, charName) = st.Value;
        var result = TextLevelLow;
        if (level <= MinLevel)
        {
            await _sendText(charUid, result);
            return;
        }
        var canRent = true;
        var cost = 0;
        short fail = 0;
        if (hotelType == 1) cost = level * 0x5dc;
        else if (hotelType == 2) cost = level * 1000;
        else if (hotelType == 3)
        {
            // Salle familiale : natif exige `CFamily+0x3e != 0`
            // (famille propriétaire d'une salle). Notre sous-système
            // famille ne porte pas ce drapeau → 0xf48/0xf49.
            canRent = false;
            fail = familyId == 0 ? TextNoFamily : TextFamilyNoRoom;
        }
        else return; // hotel_type hors 1..3 → return natif
        var room = Get(charUid);
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        short op = OpUpdate;
        if (gold < cost) { canRent = false; fail = TextNoGold; }
        if (room != null)
        {
            op = (short)((room.HotelType != hotelType ? 1 : 0) + 2);
            if (room.HotelType == hotelType)
            {
                canRent = false;
                fail = TextSameType;
            }
            if (room.DueDate < now && room.HotelType != 3)
            {
                await _sendText(charUid, TextExpired);
                return;
            }
        }
        if (!canRent)
        {
            await _sendText(charUid, fail);
            return;
        }
        var due = now + RentDurationSec;
        if (room == null)
        {
            room = new MissionDb.RoomRow
            {
                PlayerId = charUid, PlayerName = charName,
                HotelType = hotelType, LoverPrivilege = 1,
                FriendPrivilege = 1, FamilyPrivilege = 1,
                OtherPrivilege = 1, DueDate = due,
            };
            await db.InsertRoomAsync(charUid, room.PlayerName,
                hotelType, due);
            _rooms[charUid] = room;
        }
        else
        {
            // Re-location : le natif ré-initialise stats+textures et,
            // si hotel_type change (op3), vide les conteneurs (meubles).
            room.HotelType = hotelType;
            room.DueDate = due;
            room.Stat1 = room.Stat2 = room.Stat3 = 0;
            room.Wallpaper = room.LivingFloor = room.BedFloor = 0;
            if (op == OpRetype)
                await db.ClearRoomContainersAsync(charUid);
            await db.UpdateRoomAsync(room);
        }
        await _broadcast(779, BuildUpdateRoom(op, room, cost));
        await _sendText(charUid, TextRentOk);
    }

    /// <summary>TC_RoomPayRent @0x81feae0 : unités en retard, coût
    /// par type, `due_date += units×0x93a80` (DBO::PayRent
    /// @0x81a8950) + push 779.</summary>
    private async Task PayRentAsync(MissionDb db, int charUid)
    {
        var room = Get(charUid);
        var st = await _execStats(charUid);
        if (room == null || st == null) return;
        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (room.DueDate > now)
        {
            await _sendText(charUid, TextNotDue);
            return;
        }
        var units = (now - room.DueDate) / RentDurationSec + 1;
        // Natif : hotel 1 → niv×1500, hotel 2 → niv×1000, sinon 0.
        var cost = room.HotelType switch
        {
            1 => st.Value.Level * 0x5dc * units,
            2 => st.Value.Level * 1000 * units,
            _ => 0,
        };
        if (st.Value.Gold < cost)
        {
            await _sendText(charUid, TextNoGold);
            return;
        }
        room.DueDate += units * RentDurationSec;
        await db.UpdateRoomDueDateAsync(charUid, room.DueDate);
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, cost));
        await _sendText(charUid, TextPayOk);
    }

    /// <summary>TC_SearchPlayerRoom @0x821a9c0 : LIKE sur le nom,
    /// max 0x31 résultats, réponse ZMis 778 en 7 vecteurs parallèles —
    /// v3 = **niveau du propriétaire** (`CExecutionerData+0x5c`,
    /// 0 si hors-ligne).</summary>
    private async Task SearchAsync(MissionDb db, int charUid,
        string nameLike, short hotelType)
    {
        var ids = hotelType == 0
            ? await db.SearchAllRoomIdsAsync(nameLike)
            : await db.SearchRoomIdsAsync(nameLike, hotelType);
        var ids2 = new List<int>();
        var names = new List<string>();
        var levels = new List<short>();
        var pops = new List<short>();
        var s1 = new List<int>();
        var s2 = new List<int>();
        var s3 = new List<int>();
        foreach (var id in ids)
        {
            if (names.Count >= SearchCap) break;
            var r = Get(id);
            if (r == null) continue;
            var st = await _execStats(id);
            ids2.Add(id);
            names.Add(r.PlayerName);
            levels.Add((short)(st?.Level ?? 0));
            pops.Add(r.Populity);
            s1.Add(r.Stat1);
            s2.Add(r.Stat2);
            s3.Add(r.Stat3);
        }
        await _sendToChar(charUid, 778, BuildSearchResult(
            charUid, ids2, names, levels, pops, s1, s2, s3));
    }

    /// <summary>TC_SetRoomPrivilege @0x81fa570 : 4 privilèges +
    /// UPDATE + push 779.</summary>
    private async Task SetPrivilegeAsync(MissionDb db, int charUid,
        int lover, int friend, int family, int other)
    {
        var room = Get(charUid);
        if (room == null) return;
        room.LoverPrivilege = lover;
        room.FriendPrivilege = friend;
        room.FamilyPrivilege = family;
        room.OtherPrivilege = other;
        await db.UpdateRoomAsync(room);
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, 0));
    }

    /// <summary>TC_ChangeRoomTexture @0x81f6020 : loc 0x4a→wallpaper,
    /// 0x4b→living_floor, 0x4c→bed_floor ; la valeur vient de
    /// l'item-clone consommé (colonne = item_id de la déco posée).
    /// Notre version applique `index` comme item_id directement —
    /// le client envoie l'item de texture dans `index`.
    /// Persiste + push 779 + 781 UpdateRoomItem.</summary>
    private async Task ChangeTextureAsync(MissionDb db, int charUid,
        int roomId, short index, short loc)
    {
        var room = Get(roomId);
        if (room == null || roomId != charUid) return;
        // Consomme l'enregistrement décor à (index,loc) du conteneur —
        // sa colonne item_id devient la texture (natif : item+0xc).
        var itemId = await db.ConsumeRoomTextureItemAsync(
            roomId, index, loc);
        if (itemId == 0) return;
        switch (loc)
        {
            case 0x4a: room.Wallpaper = itemId; break;
            case 0x4b: room.LivingFloor = itemId; break;
            case 0x4c: room.BedFloor = itemId; break;
            default: return;
        }
        await db.UpdateRoomAsync(room);
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, 0));
        // 781 CNC_ZMis_ZoneServerUpdateRoomItem :
        // [i32 charUid][i32 roomId][s16 index][s16 loc]
        await _broadcast(781, Concat(
            BitConverter.GetBytes(charUid),
            BitConverter.GetBytes(roomId),
            BitConverter.GetBytes(index),
            BitConverter.GetBytes(loc)));
    }

    /// <summary>TC_SwitchRoomSettingMode : mode de décoration de la
    /// salle — le natif le stocke hors colonnes SQL (flag runtime
    /// `CPlayerRoomDBA`), on le porte en champ runtime de RoomRow +
    /// push 779 pour synchro des zones.</summary>
    private async Task SwitchModeAsync(MissionDb db, int charUid,
        int roomId, short mode)
    {
        var room = Get(roomId);
        if (room == null || roomId != charUid) return;
        room.SettingMode = mode;
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, 0));
    }

    /// <summary>TC_UpdateRoomData @0x821b9c0 : type 0=populity,
    /// 1=party_time, 2=nom de salle — puis UPDATE + push 779.</summary>
    private async Task UpdateRoomDataAsync(MissionDb db, int charUid,
        int roomId, short type, int value)
    {
        var room = Get(roomId);
        if (room == null) return;
        switch (type)
        {
            case 0: room.Populity = (short)value; break;
            case 1: room.PartyTime = value; break;
            case 2:
                // `value` transporte une chaîne dans le protocole
                // natif (Set RoomName) — non implémenté ici.
                return;
        }
        await db.UpdateRoomAsync(room);
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, 0));
    }

    /// <summary>TC_UpdateRoomDecoration + DBO::SaveDecorationToDB
    /// @0x81a8a40 : persiste combo_id + 7 extras par
    /// (player_id, container_index, loc) dans player_room_container
    /// — combo=is_decorate, extras=pos(3)+dir(3)+rotate.</summary>
    private async Task UpdateDecorationAsync(MissionDb db, int charUid,
        short index, short loc, ushort isDecorate,
        ushort px, ushort py, ushort pz,
        ushort dx, ushort dy, ushort dz, ushort rot)
    {
        await db.SaveRoomDecorationAsync(charUid, index, loc,
            isDecorate, px, py, pz, dx, dy, dz, rot);
        // 781 pour les visiteurs de la salle (natif broadcast zones).
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(charUid));
        body.AddRange(BitConverter.GetBytes(charUid));
        body.AddRange(BitConverter.GetBytes(index));
        body.AddRange(BitConverter.GetBytes(loc));
        await _broadcast(781, body.ToArray());
    }

    /// <summary>TC_RoomExtend @0x81fe010 : coût = param
    /// (409+extend)×1000, extend++ ; 0x232 si or insuffisant.</summary>
    private async Task ExtendAsync(MissionDb db, int charUid,
        int roomId)
    {
        var room = Get(charUid);
        var st = await _execStats(charUid);
        if (room == null || st == null) return;
        var cost = (int)(_extendCost(ExtendCostParamBase + room.Extend)
            * 1000f);
        // Natif : `cost - gold == 0 || cost < gold` → gold ≥ cost.
        if (cost != st.Value.Gold && cost >= st.Value.Gold)
        {
            await _sendText(charUid, TextExtendFail);
            return;
        }
        room.Extend++;
        await db.UpdateRoomAsync(room);
        await _broadcast(779, BuildUpdateRoom(OpUpdate, room, cost));
    }

    /// <summary>TC_EnterPlayerRoom @0x81eeab0 : mémorise la salle
    /// cible (`exec+0x6d0`) puis demande au monde le routage vers le
    /// node `PlayerRoom_%d` — « VisitPlayerRoom ch_id= %d room_id= %d »
    /// (CNC_MW_WorldTextCommand). Sans node de salle instancié la
    /// migration ne se produit pas — chaîne fidèle au natif.</summary>
    private async Task EnterAsync(int charUid, int roomId)
    {
        if (roomId != 0 && Get(roomId) == null)
        {
            await _sendText(charUid, TextRoomMissing);
            return;
        }
        if (roomId == 0) roomId = charUid;
        await _sendWorldText(
            $"VisitPlayerRoom ch_id = {charUid} room_id = {roomId}");
    }

    /// <summary>Extrait le motif LIKE de « Search Player Room
    /// search_name= %s hotel_type= %hd » (la zone enveloppe déjà le
    /// nom en %%...%% — conservé tel quel pour le LIKE SQL).</summary>
    private static string SearchName(string cmd)
    {
        const string key = "search_name= ";
        var i = cmd.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "%";
        i += key.Length;
        var j = cmd.IndexOf("hotel_type=", i, StringComparison.Ordinal);
        return (j < 0 ? cmd[i..] : cmd[i..j]).Trim();
    }

    // ---------- construction des trames ZMis ----------

    /// <summary>CNC_ZMis_ZoneServerUpdateRoom (779) —
    /// `Serialize`/ctor @0x81feb70 mission. Ordre fil exact :
    ///   [s16 op][i32 roomId][str name][s16 hotelType]
    ///   [i32 friendPri][i32 familyPri][i32 loverPri][i32 otherPri]
    ///   [s16 populity][i32 stat1][i32 stat2][i32 stat3]
    ///   [s16 extend][u32 dueDate][i32 wallpaper][i32 livingFloor]
    ///   [i32 bedFloor][i32 partyTime][i32 cost]
    /// NB : les privilèges sont **permutés** à l'émission —
    /// `piVar[5]=room+0x18, [6]=+0x1c, [7]=+0x14, [8]=+0x20`
    /// (friend, family, lover, other).</summary>
    private static byte[] BuildUpdateRoom(short op,
        MissionDb.RoomRow r, int cost)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(op));              // s16 op
        body.AddRange(BitConverter.GetBytes(r.PlayerId));      // i32 room_id
        var name = Eden.Core.Data.EdenEncoding.Big5
            .GetBytes(r.PlayerName);
        body.AddRange(BitConverter.GetBytes((ushort)name.Length));
        body.AddRange(name);                                   // str nom
        body.AddRange(BitConverter.GetBytes(r.HotelType));     // s16 hotel
        body.AddRange(BitConverter.GetBytes(r.FriendPrivilege));
        body.AddRange(BitConverter.GetBytes(r.FamilyPrivilege));
        body.AddRange(BitConverter.GetBytes(r.LoverPrivilege));
        body.AddRange(BitConverter.GetBytes(r.OtherPrivilege));
        body.AddRange(BitConverter.GetBytes(r.Populity));      // s16
        body.AddRange(BitConverter.GetBytes(r.Stat1));         // i32 ×3
        body.AddRange(BitConverter.GetBytes(r.Stat2));
        body.AddRange(BitConverter.GetBytes(r.Stat3));
        body.AddRange(BitConverter.GetBytes(r.Extend));        // s16
        body.AddRange(BitConverter.GetBytes(r.DueDate));       // u32
        body.AddRange(BitConverter.GetBytes(r.Wallpaper));     // i32 ×3
        body.AddRange(BitConverter.GetBytes(r.LivingFloor));
        body.AddRange(BitConverter.GetBytes(r.BedFloor));
        body.AddRange(BitConverter.GetBytes(r.PartyTime));     // i32
        body.AddRange(BitConverter.GetBytes(cost));            // i32
        return body.ToArray();
    }

    /// <summary>CNE_ZMis_ZoneServerSearchRoomResult (778) —
    /// `[i32 requester][u16 count]` puis **7 vecteurs comptés**
    /// `[u16 n][n×elem]` : ids(i32), names(str), levels(s16),
    /// populity(s16), stat1/2/3(i32). La zone reconstruit 537 en
    /// entrées entrelacées (Execute @0x8335900).</summary>
    private static byte[] BuildSearchResult(int charUid,
        List<int> ids, List<string> names, List<short> levels,
        List<short> pops, List<int> s1, List<int> s2, List<int> s3)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(charUid));         // i32 requester
        body.AddRange(BitConverter.GetBytes((ushort)ids.Count)); // u16 n
        WriteVec(body, ids, BitConverter.GetBytes);
        body.AddRange(BitConverter.GetBytes((ushort)names.Count));
        foreach (var n in names)
        {
            var b = Eden.Core.Data.EdenEncoding.Big5.GetBytes(n);
            body.AddRange(BitConverter.GetBytes((ushort)b.Length));
            body.AddRange(b);
        }
        WriteVec(body, levels, BitConverter.GetBytes);
        WriteVec(body, pops, BitConverter.GetBytes);
        WriteVec(body, s1, BitConverter.GetBytes);
        WriteVec(body, s2, BitConverter.GetBytes);
        WriteVec(body, s3, BitConverter.GetBytes);
        return body.ToArray();
    }

    /// <summary>Vector natif `[u16 n][n×elem]`.</summary>
    private static void WriteVec<T>(List<byte> body, List<T> v,
        Func<T, byte[]> enc)
    {
        body.AddRange(BitConverter.GetBytes((ushort)v.Count));
        foreach (var e in v) body.AddRange(enc(e));
    }

    /// <summary>Concat utilitaire (même helper que MissionServer).</summary>
    private static byte[] Concat(params byte[][] parts)
    {
        var r = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, r, o, p.Length);
            o += p.Length;
        }
        return r;
    }
}
