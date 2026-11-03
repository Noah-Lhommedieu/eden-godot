using EdenClient.Net;

namespace EdenClient.Protocol;

/// <summary>Parseurs S→C monde portés verbatim des codecs serveur
/// <c>Messages/Generated/*.g.cs</c> — lot « complétude NCID » : chaque
/// trame émise par le serveur est désormais désérialisable (plus de
/// « non géré »). Le bind générique vit dans
/// <see cref="HudExtensions"/> ; les états dédiés se branchent dessus.</summary>


/// <summary>CNC_CW_ClientCancelCreateCharacterResp — layout porté verbatim du codec serveur
/// <c>CNC_CW_ClientCancelCreateCharacterResp</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class WorldClientCancelCreateCharacterResp : NetMessage
{
    public override int Opcode => 41;
    public override string Name => "CNC_CW_ClientCancelCreateCharacterResp";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CW_ClientTell — layout porté verbatim du codec serveur
/// <c>CNC_CW_ClientTell</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class WorldClientTell : NetMessage
{
    public override int Opcode => 42;
    public override string Name => "CNC_CW_ClientTell";

    public string Field1 { get; set; } = "";
    public string Field2 { get; set; } = "";
    public string Field3 { get; set; } = "";
    public uint Field4 { get; set; }
    public byte Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteString(Field1);
        s.WriteString(Field2);
        s.WriteString(Field3);
        s.WriteInt((int)Field4);
        s.WriteByte(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadString();
        Field2 = s.ReadString();
        Field3 = s.ReadString();
        Field4 = (uint)s.ReadInt();
        Field5 = s.ReadByte();
    }
}

/// <summary>CNC_CW_ClientTextOutputIndex — layout porté verbatim du codec serveur
/// <c>CNC_CW_ClientTextOutputIndex</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class WorldClientTextOutputIndex : NetMessage
{
    public override int Opcode => 43;
    public override string Name => "CNC_CW_ClientTextOutputIndex";

    public short Field1 { get; set; }
    public string Field2 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteString(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadString();
    }
}

/// <summary>CNC_CW_ClientLookingForMemberResult — layout porté verbatim du codec serveur
/// <c>CNC_CW_ClientLookingForMemberResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class WorldClientLookingForMemberResult : NetMessage
{
    public override int Opcode => 45;
    public override string Name => "CNC_CW_ClientLookingForMemberResult";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public string Field3 { get; set; } = "";
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteString(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadString();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
    }
}

/// <summary>CNC_CW_ClientQueryExploitRank — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientQueryExploitRank : NetMessage
{
    public override int Opcode => 59;
    public override string Name => "CNC_CW_ClientQueryExploitRank";

    public byte Field1  { get; set; }
    public short Field2  { get; set; }
    public string Field3 { get; set; } = "";
    public short Field4  { get; set; }
    public string Field5 { get; set; } = "";
    public string Field6 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort(Field2);
        s.WriteString(Field3);
        s.WriteShort(Field4);
        s.WriteString(Field5);
        s.WriteString(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadShort();
        Field3 = s.ReadString();
        Field4 = s.ReadShort();
        Field5 = s.ReadString();
        Field6 = s.ReadString();
    }
}

/// <summary>CNC_CW_ClientReceiveTicketToWorld — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientReceiveTicketToWorld : NetMessage
{
    public override int Opcode => 65;
    public override string Name => "CNC_CW_ClientReceiveTicketToWorld";

    public byte Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNC_CW_ClientFamilyAchievementUpdate — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientFamilyAchievementUpdate : NetMessage
{
    public override int Opcode => 67;
    public override string Name => "CNC_CW_ClientFamilyAchievementUpdate";

    public byte Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }
    public uint Field4  { get; set; }
    public int Field5  { get; set; }
    public uint Field6  { get; set; }
    public ushort Field7  { get; set; }
    public int Field8  { get; set; }
    public int Field9  { get; set; }
    public int Field10  { get; set; }
    public short Field11  { get; set; }
    public ushort Field12  { get; set; }
    public ushort Field13  { get; set; }
    public uint Field14  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteInt((int)Field4);
        s.WriteInt(Field5);
        s.WriteInt((int)Field6);
        s.WriteShort((short)Field7);
        s.WriteInt(Field8);
        s.WriteInt(Field9);
        s.WriteInt(Field10);
        s.WriteShort(Field11);
        s.WriteShort((short)Field12);
        s.WriteShort((short)Field13);
        s.WriteInt((int)Field14);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = (uint)s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = (uint)s.ReadInt();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadInt();
        Field9 = s.ReadInt();
        Field10 = s.ReadInt();
        Field11 = s.ReadShort();
        Field12 = (ushort)s.ReadShort();
        Field13 = (ushort)s.ReadShort();
        Field14 = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CW_ClientCrossZone — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientCrossZone : NetMessage
{
    public override int Opcode => 70;
    public override string Name => "CNC_CW_ClientCrossZone";

    public ushort Field1  { get; set; }
    public int Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CW_ClientCheckHasCharPassword — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientCheckHasCharPassword : NetMessage
{
    public override int Opcode => 75;
    public override string Name => "CNC_CW_ClientCheckHasCharPassword";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CW_ClientCharPassword — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientCharPassword : NetMessage
{
    public override int Opcode => 76;
    public override string Name => "CNC_CW_ClientCharPassword";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CW_ClientSelectCharEnableBtn — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientSelectCharEnableBtn : NetMessage
{
    public override int Opcode => 77;
    public override string Name => "CNC_CW_ClientSelectCharEnableBtn";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CW_ClientMutuallyHelpUpdate — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientMutuallyHelpUpdate : NetMessage
{
    public override int Opcode => 81;
    public override string Name => "CNC_CW_ClientMutuallyHelpUpdate";

    public short Field1  { get; set; }
    public uint Field2  { get; set; }
    public string Field3 { get; set; } = "";
    public short Field4  { get; set; }
    public byte Field5  { get; set; }
    public byte Field6  { get; set; }
    public short Field7  { get; set; }
    public short Field8  { get; set; }
    public byte Field9  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt((int)Field2);
        s.WriteString(Field3);
        s.WriteShort(Field4);
        s.WriteByte(Field5);
        s.WriteByte(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteByte(Field9);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (uint)s.ReadInt();
        Field3 = s.ReadString();
        Field4 = s.ReadShort();
        Field5 = s.ReadByte();
        Field6 = s.ReadByte();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadByte();
    }
}

/// <summary>CNC_CW_ClientNotifyChangeWorld — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientNotifyChangeWorld : NetMessage
{
    public override int Opcode => 82;
    public override string Name => "CNC_CW_ClientNotifyChangeWorld";

    public ushort Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CW_ClientUpdateFortuneBagContentList — layout porté verbatim du codec serveur.</summary>
public sealed class WorldClientUpdateFortuneBagContentList : NetMessage
{
    public override int Opcode => 83;
    public override string Name => "CNC_CW_ClientUpdateFortuneBagContentList";

    public ushort Field1  { get; set; }
    public short Field2  { get; set; }
    public ushort Field3  { get; set; }
    public short Field4  { get; set; }
    public ushort Field5  { get; set; }
    public short Field6  { get; set; }
    public float Field7  { get; set; }
    public float Field8  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort(Field2);
        s.WriteShort((short)Field3);
        s.WriteShort(Field4);
        s.WriteShort((short)Field5);
        s.WriteShort(Field6);
        s.WriteFloat(Field7);
        s.WriteFloat(Field8);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadFloat();
        Field8 = s.ReadFloat();
    }
}

/// <summary>CNC_CW_ClientApexBufferTransfer — 72 : transfert d'un
/// buffer apex (u8 flag + u16 count + bytes bruts).</summary>
public sealed class WorldClientApexBufferTransfer : NetMessage
{
    public override int Opcode => 72;
    public override string Name => "CNC_CW_ClientApexBufferTransfer";
    public byte Field1 { get; set; }
    public byte[] Buffer { get; set; } = System.Array.Empty<byte>();
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1); s.WriteShort((short)Buffer.Length);
        foreach (var b in Buffer) s.WriteByte(b);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        var count = s.ReadUShort();
        Buffer = s.ReadBytes(count);
    }
}
