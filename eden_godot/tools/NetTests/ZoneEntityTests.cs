using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages d'entités zone — payloads hex verbatim issus de
/// <c>$EDEN/captures/zsniff_cs_entry_success.bin</c> (client natif)
/// et <c>docs/netsmoke_trace.log</c> (serveur C#). Les GOLDEN (matrix)
/// sont validés byte-à-byte en round-trip ; les NON PROUVE testent
/// seulement le layout documenté.
/// </summary>
public class ZoneEntityGoldenTests
{
    private static byte[] FromHex(string s) => Convert.FromHexString(s);

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var msg = new T();
        var r = new BinReader(payload);
        Assert.Equal(msg.Opcode, r.ReadUShort());
        msg.Deserialize(r);
        return msg;
    }

    // ------------------------------------------------------------------
    // 298 ClientSeeMob — NPC (kind 8, masque 0x3d57, corps 77B)
    // zsniff @18:26:20 : NPC 50003 (大媽狄安娜, s002.ini -9) pos
    // (323.85, 302.37), f3=3.927 (facing ini), f4=0.
    // ------------------------------------------------------------------
    private const string SeeMobNpcHex =
        "2a010d93c98853c300aceca1437d2f9743ce537b400000000008" +
        "000000000000573d0a000000000000000a000000000000000a00000064000000" +
        "feffffff000000000000000000000000000000";

    [Fact]
    public void SeeMob_Npc_ParsesCapture()
    {
        var payload = FromHex(SeeMobNpcHex);
        var m = Parse<ZoneSeeMob>(payload);
        Assert.Equal(0x88C9930Du, m.Handle);          // sight-handle NPC
        Assert.Equal(unchecked((short)0xC353), m.EntityId);      // 50003 lu en s16
        Assert.Equal(0, m.Flag0);
        Assert.Equal(323.849f, m.X, 3);
        Assert.Equal(302.371f, m.Y, 3);
        Assert.Equal(3.927f, m.FieldZ, 3);            // dir natif (+0x14)
        Assert.Equal(0f, m.FieldSpeed);               // vitesse natif (+0x18)
        Assert.Equal(8, m.Kind);                      // 8 = NPC
        Assert.Equal(new byte[4], m.Raw4);
        Assert.Equal("", m.DisplayName);
        Assert.Equal(10, m.Hp);                       // bit0
        Assert.Equal(10, m.MaxHp);                    // bit1
        Assert.Equal(10, m.Unk34);                    // bit2
        Assert.Equal(100, m.Unk4C);                   // bit4
        Assert.Equal(-2, m.Unk44);                    // bit6
        Assert.Equal(0, m.Unk50);                     // bit8
        Assert.Equal(0f, m.Unk58);                    // bit10
        Assert.Equal(0, m.Unk64);                     // bit13
        // Round-trip byte-à-byte (GOLDEN).
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 298 ClientSeeMob — monstre (kind 12, masque 0x3c57, corps 76B)
    // netsmoke_trace.log : mob 56001 (黃斑菇) pos (151.81, 270.83).
    // ------------------------------------------------------------------
    private const string SeeMobMonHex =
        "2a010080ffffc1da0000cf1743aa668743000000007ff80a400c" +
        "000000000000573cd300000000000000d300000000000000b400000064000000" +
        "ffffffff0000000000000000000000000000";

    [Fact]
    public void SeeMob_Monster_ParsesCapture()
    {
        var payload = FromHex(SeeMobMonHex);
        var m = Parse<ZoneSeeMob>(payload);
        Assert.Equal(0xFFFF8000u, m.Handle);          // sight-handle mob
        Assert.Equal(unchecked((short)0xDAC1), m.EntityId); // 56001
        Assert.Equal(151.81f, m.X, 2);
        Assert.Equal(270.80f, m.Y, 2);
        Assert.Equal(0f, m.FieldZ);
        Assert.Equal(2.1709f, m.FieldSpeed, 3);
        Assert.Equal(12, m.Kind);                     // 12 = monstre
        Assert.Equal(211, m.Hp);
        Assert.Equal(211, m.MaxHp);
        Assert.Equal(180, m.Unk34);
        Assert.Equal(100, m.Unk4C);
        Assert.Equal(-1, m.Unk44);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 326 ClientMovePrediction — corps 11B (sans destination).
    // zsniff @18:26:33 : NPC 0x88c9930d à (323.875, 302.375) — la même
    // position que le SeeMob → coords ×16 confirmées.
    // ------------------------------------------------------------------
    [Fact]
    public void MovePrediction_WithoutDst_ParsesCapture()
    {
        var payload = FromHex("46010d93c9883e14e61207da43");
        var m = Parse<ZoneMovePrediction>(payload);
        Assert.Equal(unchecked((int)0x88C9930D), m.Handle);
        Assert.Equal(323.875f, m.CurPos.X, 3);
        Assert.Equal(302.375f, m.CurPos.Y, 3);
        Assert.Equal(7, m.Flag);
        Assert.Null(m.DstPos);
        Assert.Equal(3.9258f, m.FacingFloat, 3);      // half 0x43da ≈ 3.93
        Assert.Equal(payload, m.ToPayload());
    }

    /// <summary>Variante 15B avec destination (layout .g.cs documenté —
    /// corps allongé de 4 octets).</summary>
    [Fact]
    public void MovePrediction_WithDst_RoundTrips()
    {
        var m = new ZoneMovePrediction
        {
            Handle = unchecked((int)0xFFFF8000),
            CurX = 2400, CurY = 4300,    // (150.0, 268.75)
            Flag = 7,
            Facing = 0x3C00,             // half 1.0
            DstX = 2416, DstY = 4300,    // (151.0, 268.75)
        };
        var payload = m.ToPayload();
        Assert.Equal(17, payload.Length); // 2 NCID + 15 corps
        var back = Parse<ZoneMovePrediction>(payload);
        Assert.Equal((150f, 268.75f), back.CurPos);
        Assert.Equal((151f, 268.75f), back.DstPos);
        Assert.Equal(1f, back.FacingFloat, 3);
    }

    // ------------------------------------------------------------------
    // 264 ClientMoveChar [f32 x][f32 y] — zsniff : écho self qui suit
    // chaque MoveSelf D (326.91, 304.54).
    // ------------------------------------------------------------------
    [Fact]
    public void MoveChar_ParsesCapture()
    {
        var payload = FromHex("08016974a34306459843");
        var m = Parse<ZoneMoveChar>(payload);
        Assert.Equal(326.91f, m.X, 2);
        Assert.Equal(304.54f, m.Y, 2);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 267 ClientGoto [i32][f32 x][f32 y][u8] — zsniff : réponse du
    // serveur au TargetUpdate sur le NPC 0x88c9930d.
    // ------------------------------------------------------------------
    [Fact]
    public void ClientGoto_ParsesCapture()
    {
        var payload = FromHex("0b010d93c988aceca1437d2f97436a");
        var m = Parse<ZoneClientGoto>(payload);
        Assert.Equal(unchecked((int)0x88C9930D), m.Handle);
        Assert.Equal(323.849f, m.X, 3);
        Assert.Equal(302.371f, m.Y, 3);
        Assert.Equal(0x6A, m.Flag);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 280 ClientUpdateAttr — zsniff : handle = charId self
    // 200000002, attr 0 (HP), cur=max=376.
    // ------------------------------------------------------------------
    [Fact]
    public void UpdateAttr_ParsesCapture()
    {
        var payload = FromHex("180102c2eb0b0000780100007801000000000000");
        var m = Parse<ZoneUpdateAttr>(payload);
        Assert.Equal(200000002, m.Handle);
        Assert.Equal(0, m.AttrId);
        Assert.Equal(376, m.Current);
        Assert.Equal(376, m.Max);
        Assert.Equal(payload, m.ToPayload());
    }

    // Variante sur sight-handle mob (0xFFFFF4FA natif).
    [Fact]
    public void UpdateAttr_MobHandle_ParsesCapture()
    {
        var payload = FromHex("1801faf4ffff0000a6000000a600000000000000");
        var m = Parse<ZoneUpdateAttr>(payload);
        Assert.Equal(unchecked((int)0xFFFFF4FA), m.Handle);
        Assert.Equal(166, m.Current);
        Assert.Equal(166, m.Max);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 271 ClientDisplayMonster — zsniff : mob 0xFFFFF4FA, 0 enchant,
    // owner = charId 200000004.
    // ------------------------------------------------------------------
    [Fact]
    public void DisplayMonster_ParsesCapture()
    {
        var payload = FromHex("0f01faf4ffff000104c2eb0b");
        var m = Parse<ZoneDisplayMonster>(payload);
        Assert.Equal(0xFFFFF4FAu, m.MobHandle);
        Assert.Empty(m.EnchantIds);
        Assert.Equal(1, m.OwnerGeneration);
        Assert.Equal(200000004, m.OwnerHandle);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 283 ClientModelModify — netsmoke : [i32 charId][u8 0][i32 0x4000]
    // [u16 1][u32 0x3b][u16 0].
    // ------------------------------------------------------------------
    [Fact]
    public void ModelModify_ParsesCapture()
    {
        var payload = FromHex("1b0102c2eb0b000040000001003b0000000000");
        var m = Parse<ZoneModelModify>(payload);
        Assert.Equal(200000002, m.Handle);
        Assert.Equal(0, m.Field2);
        Assert.Equal(0x4000, m.Field3);
        Assert.Equal(new uint[] { 0x3B }, m.Field4);
        Assert.Empty(m.Field5);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 296 ClientAttrRefresh — netsmoke : corps 96B (98 avec NCID).
    // ------------------------------------------------------------------
    [Fact]
    public void AttrRefresh_ParsesCapture()
    {
        // netsmoke_trace.log ligne 134 (98B avec NCID).
        var payload = FromHex(
            "28010c000a000e000e000a0000000000000000000000000000000000000000000000" +
            "b400000058010000220100000000a040000020411d020000200359032e03dc0567" +
            "039600f6039600cdcc164100000000000000005b6b514200000000e79c8b41");
        var m = Parse<ZoneAttrRefresh>(payload);
        Assert.Equal(12, m.Fields1to14[0]);   // s16 #1
        Assert.Equal(10, m.Fields1to14[1]);   // s16 #2
        Assert.Equal(180, m.Fields15to18[1]); // i32
        Assert.Equal(5f, m.Fields19to20[0]);  // f32
        Assert.Equal(541, m.Field21);         // i32
        Assert.Equal(800, m.Fields22to29[0]);
        // Queue : 6 f32 — (9.425, 0, 0, 52.35, 0, 17.45).
        Assert.Equal(9.425f, m.Fields30to35[0], 3);
        Assert.Equal(52.3548f, m.Fields30to35[3], 3);
        Assert.Equal(17.4516f, m.Fields30to35[5], 3);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 310 ClientEnterNodePlayerData — INCOMPLET : on valide le préfixe
    // [u32 charId][str name] + conservation du reste brut.
    // netsmoke : 3601 04c2eb0b 05007465737433 …
    // ------------------------------------------------------------------
    [Fact]
    public void EnterNodePlayerData_Prefix_ParsesCapture()
    {
        var head = FromHex("360104c2eb0b050074657374330000cdcc16410f0f000001011e00");
        var tail = new byte[50]; // simule la queue non décodée
        var payload = head.Concat(tail).ToArray();
        var m = Parse<ZoneEnterNodePlayerData>(payload);
        Assert.Equal(200000004u, m.CharId);
        Assert.Equal("test3", m.CharName);
        Assert.Equal(payload.Length - 2 - 4 - 2 - 5, m.Rest.Length);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 313 ClientEnterNodeNodeData — netsmoke : node 2, flag 1,
    // pos (179.33, 284.27, 0).
    // ------------------------------------------------------------------
    [Fact]
    public void EnterNodeNodeData_ParsesCapture()
    {
        var payload = FromHex("39010200014b55334320238e4300000000");
        var m = Parse<ZoneEnterNodeNodeData>(payload);
        Assert.Equal(2, m.Field1);
        Assert.Equal(1, m.Field2);
        Assert.Equal(179.33f, m.X, 2);
        Assert.Equal(284.27f, m.Y, 2);
        Assert.Equal(0f, m.Z);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // C→S NCID 1 MoveSelf — zsniff D : count=0, [x][y][dstX][dstY][dir],
    // type 7. On reproduit les bytes natifs.
    // ------------------------------------------------------------------
    [Fact]
    public void MoveSelf_MatchesCapture()
    {
        // Floats reconstruits par bits exacts de la capture.
        var m = new ZoneMoveSelf();
        m.Tail[0] = BitConverter.Int32BitsToSingle(0x43A37469); // 326.909 x
        m.Tail[1] = BitConverter.Int32BitsToSingle(0x43984506); // 304.543 y
        m.Tail[2] = BitConverter.Int32BitsToSingle(0x43A08000); // 321.0 dst x
        m.Tail[3] = BitConverter.Int32BitsToSingle(0x43962450); // 300.283 dst y
        m.Tail[4] = BitConverter.Int32BitsToSingle(0x3F6790AE); // 0.9053 dir
        m.MoveType = 7;
        var expected = FromHex(
            "010000006974a343064598430080a04350249643ae90673f0700");
        Assert.Equal(expected, m.ToPayload()); // byte-à-byte
    }

    /// <summary>Round-trip C→S complet : parse + réécriture identique.</summary>
    [Fact]
    public void MoveSelf_RoundTripsCapture()
    {
        var payload = FromHex(
            "010000006974a343064598430080a04350249643ae90673f0700");
        var m = Parse<ZoneMoveSelf>(payload);
        Assert.Empty(m.Segments);
        Assert.Equal(326.91f, m.Tail[0], 2);
        Assert.Equal(0.9053f, m.Tail[4], 3);
        Assert.Equal(7, m.MoveType);
        Assert.Equal(payload, m.ToPayload());
    }

    // ------------------------------------------------------------------
    // 269 LifeEradicate / 294 LeaveNode / 293 NodeExit — layouts simples.
    // ------------------------------------------------------------------
    [Fact]
    public void LifeEradicate_RoundTrips()
    {
        var m = new ZoneLifeEradicate { Handle = unchecked((int)0x88C9930D) };
        var payload = m.ToPayload();
        var back = Parse<ZoneLifeEradicate>(payload);
        Assert.Equal(unchecked((int)0x88C9930D), back.Handle);
        Assert.Equal(payload, back.ToPayload());
    }

    [Fact]
    public void LeaveNode_RoundTrips()
    {
        var m = new ZoneLeaveNode { Value = 0x1234 };
        Assert.Equal(0x1234,
            Parse<ZoneLeaveNode>(m.ToPayload()).Value);
    }

    [Fact]
    public void SeeChar_RoundTrips()
    {
        // Layout du codec serveur (PASSTHRU — pas de golden) : on valide
        // la symétrie sérialise/désérialise sur des champs représentatifs.
        var m = new ZoneSeeChar
        {
            CharId = 200000002,
            X = 323.85f, Y = 302.37f, Facing = 3.927f, MoveSpeed = 0f,
            Level = 30, ClassLevel = 1, Hp = 376, MaxHp = 376,
            MaxHp32 = 376, MaxMp = 120,
            TerritoryTeam = -1,
        };
        var payload = m.ToPayload();
        var back = Parse<ZoneSeeChar>(payload);
        Assert.Equal(200000002u, back.CharId);
        Assert.Equal(323.85f, back.X, 2);
        Assert.Equal(302.37f, back.Y, 2);
        Assert.Equal(3.927f, back.Facing, 3);
        Assert.Equal(30, back.Level);
        Assert.Equal(376, back.Hp);
        Assert.Equal(-1, back.TerritoryTeam);
        Assert.Equal(payload, back.ToPayload());
    }

    [Fact]
    public void MobTransform_UpdateZoneMonster_RoundTrip()
    {
        var mt = new ZoneMobTransform
        {
            Handle = unchecked((int)0xFFFF8000), NewTemplateId = unchecked((short)56002),
        };
        Assert.Equal(unchecked((short)56002), Parse<ZoneMobTransform>(mt.ToPayload()).NewTemplateId);

        var um = new ZoneUpdateZoneMonster
        {
            Handle = unchecked((int)0xFFFF8000),
            Field2 = 1, Field3 = 2, Field4 = 3.5f, Field5 = -4.5f,
        };
        var back = Parse<ZoneUpdateZoneMonster>(um.ToPayload());
        Assert.Equal(1, back.Field2);
        Assert.Equal(-4.5f, back.Field5);
        Assert.Equal(um.ToPayload(), back.ToPayload());
    }

    [Fact]
    public void SeeTreasure_RoundTrips()
    {
        var m = new ZoneSeeTreasure
        {
            Handle = 42, X = 100f, Y = 200f,
            ItemId = 30001, Count = 3, Count2 = 3, OwnerCharId = 200000002,
            IsPublic = 1,
        };
        var back = Parse<ZoneSeeTreasure>(m.ToPayload());
        Assert.Equal(42u, back.Handle);
        Assert.Equal(30001, back.ItemId);
        Assert.Equal(3, back.Count);
        Assert.Equal(200000002, back.OwnerCharId);
        Assert.Equal(1, back.IsPublic);
    }
}
