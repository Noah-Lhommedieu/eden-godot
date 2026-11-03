using Eden.Core.Logging;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;
using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>
/// Salles personnelles côté zone — `CPlayerRoomManager` natif :
/// registre `CZoneServer+0x564` rempli par les pushes mission
/// (779 `CNE_ZMis_ZoneServerUpdateRoom`), `EnterRoomCheck`
/// @0x814eaf0 pour le CNE 207, traduction des retours mission en
/// paquets client (778→537, 779→538, 781→542).
///
/// Wires natifs (preuves mission/zone decompiled.c) :
///   779 : [s16 op][i32 roomId][str name][s16 hotel]
///         [i32 friend][i32 family][i32 lover][i32 other]   (perm.)
///         [s16 populity][i32 stat×3][s16 extend][u32 due]
///         [i32 wall][i32 liv][i32 bed][i32 party][i32 cost]
///   778 : [i32 requester][u16 n] + 7 vecteurs `[u16 n][n×elem]`
///         ids(i32) names(str) levels(s16) populity(s16) i32×3
///   781 : [i32 charUid][i32 roomId][s16 index][s16 loc]
///   537 : [s16 n] + entrées entrelacées
///         [i32 id][str name][s16 type][s16 mode][i32 owner]
///         [i32 member][i32 max]  (Serialize @0x1e8580 zone)
///   538 : [s16 op][i32 id][i32×4 privs][s16 populity][i32×3 stats]
///         [s16 extend][i32 due][i32 wall][i32 liv][i32 bed]
///         (Serialize @0x833f9c0 + build Execute @0x833fa00)
///   542 : [s16 index][s16 loc] (classe générée)
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>779 `CNE_ZMis_ZoneServerUpdateRoom` : met à jour le
    /// registre, débite `cost` d'or au propriétaire s'il est sur cette
    /// zone (`iVar25 = -cost` dans l'Execute natif), puis émet 538
    /// `CNC_CZ_ClientUpdateRoom` au proprio connecté.</summary>
    private async Task OnMissionRoomUpdateAsync(byte[] body,
        CancellationToken ct)
    {
        var pos = 0;
        var op = ReadS16(body, ref pos);
        var roomId = ReadI32(body, ref pos);
        var name = ReadStr(body, ref pos);
        var hotel = ReadS16(body, ref pos);
        var friend = ReadI32(body, ref pos);
        var family = ReadI32(body, ref pos);
        var lover = ReadI32(body, ref pos);
        var other = ReadI32(body, ref pos);
        var populity = ReadS16(body, ref pos);
        var s1 = ReadI32(body, ref pos);
        var s2 = ReadI32(body, ref pos);
        var s3 = ReadI32(body, ref pos);
        var extend = ReadS16(body, ref pos);
        var due = ReadI32(body, ref pos);
        var wall = ReadI32(body, ref pos);
        var liv = ReadI32(body, ref pos);
        var bed = ReadI32(body, ref pos);
        var party = ReadI32(body, ref pos);
        var cost = ReadI32(body, ref pos);
        if (pos > body.Length) return;

        var room = _services.Rooms.ApplyUpdate(op,
            new RoomRegistry.ZoneRoom
            {
                RoomId = roomId, Name = name, HotelType = hotel,
                FriendPrivilege = friend, FamilyPrivilege = family,
                LoverPrivilege = lover, OtherPrivilege = other,
                Populity = populity, Stat1 = s1, Stat2 = s2, Stat3 = s3,
                Extend = extend, DueDate = due, Wallpaper = wall,
                LivingFloor = liv, BedFloor = bed, PartyTime = party,
            });
        if (room == null)
        {
            EdenLog.Write($"[Zone] salle {roomId} supprimée du registre");
            return;
        }

        var owner = _services.Sessions.FindByCharacter(roomId);
        if (owner == null) return;
        if (!_states.TryGetValue(owner.SessionId, out var ost) ||
            ost == null)
            return;

        // Coût (location/loyer/extension) — débit natif zone-side.
        if (cost > 0)
            await RunOnLoopAsync(t => DeductRoomCostAsync(
                owner, ost, cost, t), ct);

        // 538 — même ordre que le build natif (piVar20 : room+0xc/0x10/
        // 0x14/0x18 = friend, family, lover, other).
        var body538 = new List<byte>();
        body538.AddRange(BitConverter.GetBytes(op));            // s16
        body538.AddRange(BitConverter.GetBytes(room.RoomId));   // i32
        body538.AddRange(BitConverter.GetBytes(room.FriendPrivilege));
        body538.AddRange(BitConverter.GetBytes(room.FamilyPrivilege));
        body538.AddRange(BitConverter.GetBytes(room.LoverPrivilege));
        body538.AddRange(BitConverter.GetBytes(room.OtherPrivilege));
        body538.AddRange(BitConverter.GetBytes(room.Populity)); // s16
        body538.AddRange(BitConverter.GetBytes(room.Stat1));
        body538.AddRange(BitConverter.GetBytes(room.Stat2));
        body538.AddRange(BitConverter.GetBytes(room.Stat3));
        body538.AddRange(BitConverter.GetBytes(room.Extend));   // s16
        body538.AddRange(BitConverter.GetBytes(room.DueDate));  // i32
        body538.AddRange(BitConverter.GetBytes(room.Wallpaper));
        body538.AddRange(BitConverter.GetBytes(room.LivingFloor));
        body538.AddRange(BitConverter.GetBytes(room.BedFloor));
        await owner.SendPacketAsync(538, body538.ToArray(), ct);
    }

    /// <summary>Débit d'or natif du coût salle (loyer/extension) —
    /// `GainGold(-cost)` : état + DB + attr 0x02 (280).</summary>
    private async Task DeductRoomCostAsync(NetSession session,
        EntryState st, int cost, CancellationToken ct)
    {
        st.Gold = (int)await _services.Characters.AddGoldAsync(
            (int)st.CharId, -cost);
        await SendAttrAsync(session, st.CharId, 0x02,
            (int)Math.Min(st.Gold, int.MaxValue), 0, ct);
    }

    /// <summary>778 `CNE_ZMis_ZoneServerSearchRoomResult` : 7 vecteurs
    /// parallèles → entrées entrelacées du 537
    /// `CNC_CZ_ClientSearchRoomResult` (Execute @0x8335900).</summary>
    private async Task OnMissionSearchRoomResultAsync(byte[] body,
        CancellationToken ct)
    {
        var pos = 0;
        var requester = ReadI32(body, ref pos);
        var count = ReadU16(body, ref pos);
        var ids = ReadVecI32(body, ref pos);
        var names = ReadVecStr(body, ref pos);
        var levels = ReadVecS16(body, ref pos);
        var pops = ReadVecS16(body, ref pos);
        var s1 = ReadVecI32(body, ref pos);
        var s2 = ReadVecI32(body, ref pos);
        var s3 = ReadVecI32(body, ref pos);
        if (pos > body.Length) return;
        var n = Math.Min(count, ids.Count);

        var target = _services.Sessions.FindByCharacter(requester);
        if (target == null) return;
        var body537 = new List<byte>();
        body537.AddRange(BitConverter.GetBytes((short)n));      // s16 n
        for (var i = 0; i < n; i++)
        {
            body537.AddRange(BitConverter.GetBytes(ids[i]));    // i32 id
            var b = Eden.Core.Data.EdenEncoding.Big5
                .GetBytes(names[i]);
            body537.AddRange(BitConverter.GetBytes((ushort)b.Length));
            body537.AddRange(b);                                // str nom
            body537.AddRange(BitConverter.GetBytes(levels[i])); // s16 type
            body537.AddRange(BitConverter.GetBytes(pops[i]));   // s16 mode
            body537.AddRange(BitConverter.GetBytes(s1[i]));     // i32 owner
            body537.AddRange(BitConverter.GetBytes(s2[i]));     // i32 member
            body537.AddRange(BitConverter.GetBytes(s3[i]));     // i32 max
        }
        await target.SendPacketAsync(537, body537.ToArray(), ct);
    }

    /// <summary>781 `CNE_ZMis_ZoneServerUpdateRoomItem` → 542
    /// `CNC_CZ_ClientUpdateRoomItem` `[s16 index][s16 loc]` — le
    /// natif diffuse aux visiteurs de la salle ; sans instances de
    /// salle portées, on notifie le propriétaire connecté.</summary>
    private async Task OnMissionUpdateRoomItemAsync(byte[] body,
        CancellationToken ct)
    {
        if (body.Length < 12) return;
        var roomId = BitConverter.ToInt32(body, 4);
        var index = BitConverter.ToInt16(body, 8);
        var loc = BitConverter.ToInt16(body, 10);
        var owner = _services.Sessions.FindByCharacter(roomId);
        if (owner == null) return;
        await owner.SendPacketAsync(542,
            Concat(BitConverter.GetBytes(index),
                BitConverter.GetBytes(loc)), ct);
    }

    /// <summary>207 `ZoneServerEnterRoom` — `EnterRoomCheck` natif
    /// @0x814eaf0 : validations contre le registre puis relais
    /// « Enter Player Room room_id= %d » vers la mission. Le s16 du
    /// paquet = hotel_type attendu (room+8 natif) ; room_id 0 = sa
    /// propre salle (param_2 == 0 → char+0x34).</summary>
    private async Task<bool> HandleEnterRoomAsync(NetSession session,
        EntryState st, byte[] payload, CancellationToken ct)
    {
        if (payload.Length < 6) return true;
        var roomId = BitConverter.ToInt32(payload, 0);
        var hotelWanted = BitConverter.ToInt16(payload, 4);
        if (roomId == 0) roomId = (int)st.CharId;

        // Flags bloquants natifs : `char+0x3c & 4` (mort/invalide)
        // → 0xb80 ; node type 5/7/9 (villes/spéciales) → 0xf4a.
        if (st.Dead)
        {
            await SendTextIndexAsync(session, 0xb80, ct);
            return true;
        }
        var nodeType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 0;
        if (nodeType is 5 or 7 or 9)
        {
            await SendTextIndexAsync(session, 0xf4a, ct);
            return true;
        }

        var room = _services.Rooms.Get(roomId);
        if (room == null || room.HotelType != hotelWanted)
        {
            // 0xdf7, ou 0xdff (own room : param==char+0x34 → +8).
            await SendTextIndexAsync(session,
                (short)(roomId == (int)st.CharId ? 0xdff : 0xdf7), ct);
            return true;
        }
        if (room.DueDate <
            (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            await SendTextIndexAsync(session, 0x10d7, ct);
            return true;
        }
        if (room.Busy != 0)
        {
            await SendTextIndexAsync(session, 0xef7, ct);
            return true;
        }
        if (_mission?.Connected == true)
            await _mission.SendAgentDbaCommandAsync(st.CharId,
                $"Enter Player Room room_id= {roomId}", ct);
        return true;
    }

    /// <summary>210 `ZoneServerRoomGetData` : le natif sert le
    /// CPlayerRoom + conteneurs de la salle en 541 — registre
    /// connu → réponse 541 statique (conteneurs vides : les meubles
    /// ne sont pas encore instanciés côté zone).</summary>
    private async Task<bool> HandleRoomGetDataAsync(NetSession session,
        EntryState st, CancellationToken ct)
    {
        var room = _services.Rooms.Get((int)st.CharId);
        if (room == null) return true;
        // 541 CNC_CZ_ClientRoomData — Serialize @0x212714 zone :
        // [i32 id][str nom][u16 hotel][i32 friend][i32 family]
        // [i32 lover][i32 other][s16 populity][i32 stat×3]
        // [s16 extend][i32 due][i32 wall][i32 liv][i32 bed]
        // [s16 room+0x48][i32 party][u8 busy][u16 n containers]
        // NB l'ordre privilèges 541 = room+0x14/0xc/0x10/0x18
        // (lover, friend, family, other) — DIFFÉRENT du 538.
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(room.RoomId));      // i32
        var b = Eden.Core.Data.EdenEncoding.Big5
            .GetBytes(room.Name);
        body.AddRange(BitConverter.GetBytes((ushort)b.Length));
        body.AddRange(b);                                       // str nom
        body.AddRange(BitConverter.GetBytes((ushort)room.HotelType));
        body.AddRange(BitConverter.GetBytes(room.LoverPrivilege));
        body.AddRange(BitConverter.GetBytes(room.FriendPrivilege));
        body.AddRange(BitConverter.GetBytes(room.FamilyPrivilege));
        body.AddRange(BitConverter.GetBytes(room.OtherPrivilege));
        body.AddRange(BitConverter.GetBytes(room.Populity));    // s16
        body.AddRange(BitConverter.GetBytes(room.Stat1));
        body.AddRange(BitConverter.GetBytes(room.Stat2));
        body.AddRange(BitConverter.GetBytes(room.Stat3));
        body.AddRange(BitConverter.GetBytes(room.Extend));      // s16
        body.AddRange(BitConverter.GetBytes(room.DueDate));     // i32
        body.AddRange(BitConverter.GetBytes(room.Wallpaper));
        body.AddRange(BitConverter.GetBytes(room.LivingFloor));
        body.AddRange(BitConverter.GetBytes(room.BedFloor));
        body.AddRange(BitConverter.GetBytes((short)0));         // s16 +0x48
        body.AddRange(BitConverter.GetBytes(room.PartyTime));   // i32
        body.Add(room.Busy);                                    // u8 +0x64
        body.AddRange(BitConverter.GetBytes((ushort)0));        // u16 containers
        await session.SendPacketAsync(541, body.ToArray(), ct);
        return true;
    }

    // ---- lecteurs scalaires bornés (complètent ReadStr/ReadVec*
    // de ZoneEntryFlow.Chat/.Mission) ----

    /// <summary>s16 borné — pousse le curseur hors-limite en cas de
    /// troncature pour que l'appelant abandonne.</summary>
    private static short ReadS16(byte[] b, ref int p)
    {
        if (p + 2 > b.Length) { p = b.Length + 1; return 0; }
        var v = BitConverter.ToInt16(b, p); p += 2; return v;
    }

    /// <summary>u16 borné (voir <see cref="ReadS16"/>).</summary>
    private static ushort ReadU16(byte[] b, ref int p)
    {
        if (p + 2 > b.Length) { p = b.Length + 1; return 0; }
        var v = BitConverter.ToUInt16(b, p); p += 2; return v;
    }

    /// <summary>i32 borné (voir <see cref="ReadS16"/>).</summary>
    private static int ReadI32(byte[] b, ref int p)
    {
        if (p + 4 > b.Length) { p = b.Length + 1; return 0; }
        var v = BitConverter.ToInt32(b, p); p += 4; return v;
    }


    // ---- classements (CZoneServer+0x5e0 natif) ----

    /// <summary>763 `CNE_ZMis_ZoneServerRankInfo` : remplit le cache
    /// type → RankSet. Wire (Deserialize @0x832e2f0 zone) :
    ///   [vec&lt;str&gt; names][9× vec&lt;i32&gt;][s16 type][s16 ver]
    ///   [str][str]
    /// Les 9 vecs i32 = champs +4/+8/+0xc/+0x10/+0x14/+0x18/+0x1c/
    /// +0x20/+0x24 du SRankInfoData reconstruit par l'Execute natif.
    /// La queue porte les str2 (+0x28, vec&lt;str&gt;) et listes
    /// (+0x2c, vec&lt;vec&lt;str&gt;&gt;) par record.</summary>
    private void OnMissionRankInfoAsync(byte[] body)
    {
        var set = Eden.Core.Game.Social.RankPackets.Parse763(
            body, out var type);
        if (set == null) return;
        _services.RankInfos.Set(type, set);
    }

    /// <summary>194 `ZoneServerRankInfoQuery` [s16 type][s16 ver] :
    /// si la version client == version du cache → pas de repush
    /// (check `+0x12 != +0x14` de l'Execute natif). Sinon émet 521
    /// `CNC_CZ_ClientRankInfo` = vec&lt;str&gt; noms + 8 vec&lt;i32&gt;
    /// (f4,f8,fc,f10, 3× vide, f24 — ordre des args du ctor natif).
    /// Type absent du cache → 521 à vecteurs vides.</summary>
    private async Task HandleRankInfoQueryAsync(NetSession session,
        byte[] payload, CancellationToken ct)
    {
        if (payload.Length < 4) return;
        var type = BitConverter.ToInt16(payload, 0);
        var clientVer = BitConverter.ToInt16(payload, 2);
        var set = _services.RankInfos.Get(type);
        if (set != null && set.Version == clientVer) return;
        await session.SendPacketAsync(521,
            Eden.Core.Game.Social.RankPackets.Build521(set), ct);
    }
}
