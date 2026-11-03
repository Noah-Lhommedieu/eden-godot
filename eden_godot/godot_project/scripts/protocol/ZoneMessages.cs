using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages du ZoneServer (:5201).
// Client→serveur : famille CNE_CZ_ZoneServer* du
//   $EDEN/server/.../Generated/ZoneMessages.g.cs
// Serveur→client : famille CNC_CZ_Client* (idem).
// Ordre d'envoi du burst d'entrée : captures/zsniff_cs_entry_success.bin
// (dernière session @18:26, canal principal ch=1dc8ddb0) — cf. EdenSession.

/// <summary>C→S NCID 0 — UseTicket : CTicket 22B.
/// CNE_CZ_ZoneServerReceiveTicket (ZoneMessages.g.cs) + zsniff NCID=0 len=24.</summary>
public sealed class ZoneUseTicket : NetMessage
{
    public override int Opcode => 0;
    public override string Name => "Zone.UseTicket";
    public byte[] Ticket { get; set; } = new byte[22];
    public override void Serialize(BinWriter s) => s.WriteBytes(Ticket);
    public override void Deserialize(BinReader s) => Ticket = s.ReadBytes(22);
}

/// <summary>
/// C→S NCID 1 — MoveSelf. CNE_CZ_ZoneServerMoveSelf (désasm Deserialize
/// @0x826b7a0) : s16 count + count×(f32 path, f32 aux) + f32×5 + s16 type.
/// zsniff : 0100 0000 <f32×5> 0700.
/// </summary>
public sealed class ZoneMoveSelf : NetMessage
{
    public override int Opcode => 1;
    public override string Name => "Zone.MoveSelf";
    public List<(float Path, float Aux)> Segments { get; } = new();
    public float[] Tail { get; } = new float[5];
    public short MoveType { get; set; } = 7;
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Segments.Count);
        foreach (var (p, a) in Segments)
        {
            s.WriteFloat(p);
            s.WriteFloat(a);
        }
        foreach (var f in Tail) s.WriteFloat(f);
        s.WriteShort(MoveType);
    }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadShort();
        for (var i = 0; i < count; i++)
            Segments.Add((s.ReadFloat(), s.ReadFloat()));
        for (var i = 0; i < 5; i++) Tail[i] = s.ReadFloat();
        MoveType = s.ReadShort();
    }
}

/// <summary>C→S NCID 4 — MigrationDone (vide). CNE_CZ_ZoneServerMigrationDone.</summary>
public sealed class ZoneMigrationDone : NetMessage
{
    public override int Opcode => 4;
    public override string Name => "Zone.MigrationDone";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 10 — TalkToNPC [i32 handle] (sight handle 0x88xxxxxx).
/// CNE_CZ_ZoneServerTalkToNPC.</summary>
public sealed class ZoneTalkToNpc : NetMessage
{
    public override int Opcode => 10;
    public override string Name => "Zone.TalkToNpc";
    public int NpcId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(NpcId);
    public override void Deserialize(BinReader s) => NpcId = s.ReadInt();
}

/// <summary>C→S NCID 11 — ReplyDialog [s16]. CNE_CZ_ZoneServerReplyDialog.</summary>
public sealed class ZoneReplyDialog : NetMessage
{
    public override int Opcode => 11;
    public override string Name => "Zone.ReplyDialog";
    public short DialogId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(DialogId);
    public override void Deserialize(BinReader s) => DialogId = s.ReadShort();
}

/// <summary>
/// C→S NCID 14 — PlayerAction [u8 action][i32 p1][i32 p2][i32 p3].
/// CNE_CZ_ZoneServerPlayerAction. zsniff : 0e00 08 ffffffff 3b030000 00000000.
/// </summary>
public sealed class ZonePlayerAction : NetMessage
{
    public override int Opcode => 14;
    public override string Name => "Zone.PlayerAction";
    public byte Action { get; set; }
    public int Param1 { get; set; }
    public int Param2 { get; set; }
    public int Param3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Action);
        s.WriteInt(Param1);
        s.WriteInt(Param2);
        s.WriteInt(Param3);
    }
    public override void Deserialize(BinReader s)
    {
        Action = s.ReadByte();
        Param1 = s.ReadInt();
        Param2 = s.ReadInt();
        Param3 = s.ReadInt();
    }
}

/// <summary>C→S NCID 62 — GetStorageData [s16]. zsniff : 3e00 ffff (-1).</summary>
public sealed class ZoneGetStorageData : NetMessage
{
    public override int Opcode => 62;
    public override string Name => "Zone.GetStorageData";
    public short Value { get; set; } = -1;
    public override void Serialize(BinWriter s) => s.WriteShort(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadShort();
}

/// <summary>C→S NCID 87 — MissionListOp [s16][i32][u16].
/// zsniff : 5700 0200 f1030000 ffff.</summary>
public sealed class ZoneMissionListOp : NetMessage
{
    public override int Opcode => 87;
    public override string Name => "Zone.MissionListOp";
    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public ushort Field3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteShort((short)Field3);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = (ushort)s.ReadShort();
    }
}

/// <summary>C→S NCID 104 — SearchOpt [s16][s16]. zsniff : 6800 0000 0000.</summary>
public sealed class ZoneSearchOpt : NetMessage
{
    public override int Opcode => 104;
    public override string Name => "Zone.SearchOpt";
    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
    }
}

/// <summary>C→S NCID 123 — TurnSelf [f32]. zsniff : 7b00 ae90673f.</summary>
public sealed class ZoneTurnSelf : NetMessage
{
    public override int Opcode => 123;
    public override string Name => "Zone.TurnSelf";
    public float Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteFloat(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadFloat();
}

/// <summary>C→S NCID 124 — TargetUpdate [i32]. zsniff : 7c00 0d93c988.</summary>
public sealed class ZoneTargetUpdate : NetMessage
{
    public override int Opcode => 124;
    public override string Name => "Zone.TargetUpdate";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>C→S NCID 164 — UpdateBotData [u8][u8]. zsniff : a400 0000.</summary>
public sealed class ZoneUpdateBotData : NetMessage
{
    public override int Opcode => 164;
    public override string Name => "Zone.UpdateBotData";
    public byte Field1 { get; set; }
    public byte Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
    }
}

/// <summary>C→S NCID 186 — SetMissionTrace [u16 count][count × i32].
/// zsniff : ba00 0a00 f1030000 + 9×0 (10 slots).</summary>
public sealed class ZoneSetMissionTrace : NetMessage
{
    public override int Opcode => 186;
    public override string Name => "Zone.SetMissionTrace";
    public List<int> MissionIds { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)MissionIds.Count);
        foreach (var id in MissionIds) s.WriteInt(id);
    }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadUShort();
        for (var i = 0; i < count; i++) MissionIds.Add(s.ReadInt());
    }
}

/// <summary>C→S NCID 218 — NewPlayerGuide [u16][u16]. zsniff : da00 0000 0300.</summary>
public sealed class ZoneNewPlayerGuide : NetMessage
{
    public override int Opcode => 218;
    public override string Name => "Zone.NewPlayerGuide";
    public ushort Field1 { get; set; }
    public ushort Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
    }
}

/// <summary>C→S NCID 258 — DailyAwardOP [s16][u32]. zsniff : 0201 0000 02000000.</summary>
public sealed class ZoneDailyAwardOp : NetMessage
{
    public override int Opcode => 258;
    public override string Name => "Zone.DailyAwardOp";
    public short Field1 { get; set; }
    public uint Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt((int)Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (uint)s.ReadInt();
    }
}

/// <summary>C→S NCID 558 — QueryRecommendActivities [u8][u16].
/// zsniff : 2e02 00 0000.</summary>
public sealed class ZoneQueryRecommendActivities : NetMessage
{
    public override int Opcode => 558;
    public override string Name => "Zone.QueryRecommendActivities";
    public byte Field1 { get; set; }
    public ushort Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort((short)Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = (ushort)s.ReadShort();
    }
}

/// <summary>
/// Requête C→S sans corps (NCID seul). Factorise les EnterNode*/Query*/Echo
/// du burst d'entrée — toutes [len=2] dans zsniff_cs_entry_success.bin.
/// </summary>
public sealed class ZoneEmptyRequest : NetMessage
{
    private readonly int _opcode;
    private readonly string _name;
    public ZoneEmptyRequest(int opcode, string name) { _opcode = opcode; _name = name; }
    public override int Opcode => _opcode;
    public override string Name => _name;
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }

    // Requêtes vides du burst d'entrée (zsniff, ordre d'apparition).
    public static readonly ZoneEmptyRequest Echo = new(57, "Zone.Echo");
    public static readonly ZoneEmptyRequest EnterNodePlayerData = new(26, "Zone.EnterNodePlayerData");
    public static readonly ZoneEmptyRequest EnterNodeEquipAndSpell = new(24, "Zone.EnterNodeEquipAndSpell");
    public static readonly ZoneEmptyRequest EnterNodeShortcut = new(53, "Zone.EnterNodeShortcut");
    public static readonly ZoneEmptyRequest EnterNodeElfInfo = new(110, "Zone.EnterNodeElfInfo");
    public static readonly ZoneEmptyRequest EnterNodeElfContainer = new(113, "Zone.EnterNodeElfContainer");
    public static readonly ZoneEmptyRequest EnterNodePlayerRoom = new(195, "Zone.EnterNodePlayerRoom");
    public static readonly ZoneEmptyRequest EnterNodeFastDress = new(243, "Zone.EnterNodeFastDress");
    public static readonly ZoneEmptyRequest EnterNodeStarWay = new(256, "Zone.EnterNodeStarWay");
    public static readonly ZoneEmptyRequest EnterNodePlayerCard = new(133, "Zone.EnterNodePlayerCard");
    public static readonly ZoneEmptyRequest EnterNodeInventory = new(25, "Zone.EnterNodeInventory");
    public static readonly ZoneEmptyRequest QueryMissionStates = new(50, "Zone.QueryMissionStates");
    public static readonly ZoneEmptyRequest QueryLoverInfo = new(156, "Zone.QueryLoverInfo");
    public static readonly ZoneEmptyRequest QueryCollection = new(152, "Zone.QueryCollection");
    public static readonly ZoneEmptyRequest QueryBattlefieldData = new(162, "Zone.QueryBattlefieldData");
    public static readonly ZoneEmptyRequest QueryClassList = new(166, "Zone.QueryClassList");
    public static readonly ZoneEmptyRequest QueryClosets = new(165, "Zone.QueryClosets");
    public static readonly ZoneEmptyRequest QueryLicense = new(181, "Zone.QueryLicense");
    public static readonly ZoneEmptyRequest QueryDestinyRoad = new(204, "Zone.QueryDestinyRoad");
    public static readonly ZoneEmptyRequest QueryLifeSkill = new(213, "Zone.QueryLifeSkill");
    public static readonly ZoneEmptyRequest QueryFortuneBagCounter = new(223, "Zone.QueryFortuneBagCounter");
    public static readonly ZoneEmptyRequest PlayerWorkCheckData = new(172, "Zone.PlayerWorkCheckData");
    public static readonly ZoneEmptyRequest LeaveShop = new(63, "Zone.LeaveShop");
}

// ---------- S→C : messages d'entrée en zone ----------

/// <summary>S→C NCID 473 — ClientHello [f32]. CNC_CZ_ClientHello.
/// zsniff : d901 52782043 — réponse immédiate à UseTicket.</summary>
public sealed class ZoneClientHello : NetMessage
{
    public override int Opcode => 473;
    public override string Name => "Zone.ClientHello";
    public float Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteFloat(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadFloat();
}

/// <summary>S→C NCID 302 — FlagModify. CNC_CZ_ClientFlagModify [i32][u8].
/// zsniff : 2e01 04c2eb0b 00 00010100 — 9B de corps (plus que le .g.cs) ;
/// on conserve la lecture prudente i32+u8+reste brut.</summary>
public sealed class ZoneFlagModify : NetMessage
{
    public override int Opcode => 302;
    public override string Name => "Zone.FlagModify";
    public int Field1 { get; set; }
    public byte[] Rest { get; set; } = [];
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteBytes(Rest);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Rest = s.ReadBytes(s.Remaining);
    }
}

/// <summary>S→C NCID 313 — EnterNodeNodeData [s16][u8][f32×3].
/// CNC_CZ_ClientEnterNodeNodeData. zsniff : 3901 0200 01 <f32×3>.</summary>
public sealed class ZoneEnterNodeNodeData : NetMessage
{
    public override int Opcode => 313;
    public override string Name => "Zone.EnterNodeNodeData";
    public short Field1 { get; set; }
    public byte Field2 { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteByte(Field2);
        s.WriteFloat(X);
        s.WriteFloat(Y);
        s.WriteFloat(Z);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadByte();
        X = s.ReadFloat();
        Y = s.ReadFloat();
        Z = s.ReadFloat();
    }
}

/// <summary>S→C NCID 404 — MailReceive [s16][s16]. CNC_CZ_ClientMailReceive.
/// zsniff : 9401 0000 0000.</summary>
public sealed class ZoneMailReceive : NetMessage
{
    public override int Opcode => 404;
    public override string Name => "Zone.MailReceive";
    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
    }
}
