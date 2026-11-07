using EdenClient.Net;

namespace EdenClient.Protocol;

/// <summary>Parseurs S→C zone portés verbatim des codecs serveur
/// <c>Messages/Generated/*.g.cs</c> — lot « complétude NCID » : chaque
/// trame émise par le serveur est désormais désérialisable (plus de
/// « non géré »). Le bind générique vit dans
/// <see cref="HudExtensions"/> ; les états dédiés se branchent dessus.</summary>


/// <summary>CNC_CZ_ClientElfCastEnchant — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfCastEnchant</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfCastEnchant : NetMessage
{
    public override int Opcode => 261;
    public override string Name => "CNC_CZ_ClientElfCastEnchant";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientTicketInvalid — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTicketInvalid</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTicketInvalid : NetMessage
{
    public override int Opcode => 262;
    public override string Name => "CNC_CZ_ClientTicketInvalid";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientResetCoord — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientResetCoord</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientResetCoord : NetMessage
{
    public override int Opcode => 270;
    public override string Name => "CNC_CZ_ClientResetCoord";

    public float Field1 { get; set; }
    public float Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteFloat(Field1);
        s.WriteFloat(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadFloat();
        Field2 = s.ReadFloat();
    }
}

/// <summary>CNC_CZ_ClientRestore — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientRestore</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientRestore : NetMessage
{
    public override int Opcode => 273;
    public override string Name => "CNC_CZ_ClientRestore";

    public uint Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientGetSpell — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientGetSpell</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientGetSpell : NetMessage
{
    public override int Opcode => 286;
    public override string Name => "CNC_CZ_ClientGetSpell";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientPlayerAction — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlayerAction</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlayerAction : NetMessage
{
    public override int Opcode => 290;
    public override string Name => "CNC_CZ_ClientPlayerAction";

    public uint Field1 { get; set; }
    public byte Field2 { get; set; }
    public float Field3 { get; set; }
    public byte Field4 { get; set; }
    public byte Field5 { get; set; }
    public int Field6 { get; set; }
    public byte Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteByte(Field2);
        s.WriteFloat(Field3);
        s.WriteByte(Field4);
        s.WriteByte(Field5);
        s.WriteInt(Field6);
        s.WriteByte(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadByte();
        Field3 = s.ReadFloat();
        Field4 = s.ReadByte();
        Field5 = s.ReadByte();
        Field6 = s.ReadInt();
        Field7 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientEffectReport — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEffectReport</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEffectReport : NetMessage
{
    public override int Opcode => 292;
    public override string Name => "CNC_CZ_ClientEffectReport";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public int Field6 { get; set; }
    public int Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteInt(Field6);
        s.WriteInt(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadInt();
        Field7 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientDestShutdown — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDestShutdown</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDestShutdown : NetMessage
{
    public override int Opcode => 295;
    public override string Name => "CNC_CZ_ClientDestShutdown";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientPlayerEmotion — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlayerEmotion</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlayerEmotion : NetMessage
{
    public override int Opcode => 307;
    public override string Name => "CNC_CZ_ClientPlayerEmotion";

    public uint Field1 { get; set; }
    public byte Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteByte(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientSellOut — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSellOut</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSellOut : NetMessage
{
    public override int Opcode => 309;
    public override string Name => "CNC_CZ_ClientSellOut";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientCanCloseConnection — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientCanCloseConnection</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientCanCloseConnection : NetMessage
{
    public override int Opcode => 314;
    public override string Name => "CNC_CZ_ClientCanCloseConnection";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientSafeQuitCancel — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSafeQuitCancel</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSafeQuitCancel : NetMessage
{
    public override int Opcode => 320;
    public override string Name => "CNC_CZ_ClientSafeQuitCancel";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientSafeQuitResp — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSafeQuitResp</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSafeQuitResp : NetMessage
{
    public override int Opcode => 321;
    public override string Name => "CNC_CZ_ClientSafeQuitResp";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientNonSafeQuit — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientNonSafeQuit</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientNonSafeQuit : NetMessage
{
    public override int Opcode => 322;
    public override string Name => "CNC_CZ_ClientNonSafeQuit";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientDisplayCharSimple — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDisplayCharSimple</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDisplayCharSimple : NetMessage
{
    public override int Opcode => 323;
    public override string Name => "CNC_CZ_ClientDisplayCharSimple";

    public uint Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public float Field3 { get; set; }
    public float Field4 { get; set; }
    public float Field5 { get; set; }
    public float Field6 { get; set; }
    public int Field7 { get; set; }
    public int Field8 { get; set; }
    public byte Field9 { get; set; }
    public byte Field10 { get; set; }
    public byte Field11 { get; set; }
    public byte Field12 { get; set; }
    public int Field13 { get; set; }
    public string Field14 { get; set; } = "";
    public short Field15 { get; set; }
    public short Field16 { get; set; }
    public byte Field17 { get; set; }
    public int Field18 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteString(Field2);
        s.WriteFloat(Field3);
        s.WriteFloat(Field4);
        s.WriteFloat(Field5);
        s.WriteFloat(Field6);
        s.WriteInt(Field7);
        s.WriteInt(Field8);
        s.WriteByte(Field9);
        s.WriteByte(Field10);
        s.WriteByte(Field11);
        s.WriteByte(Field12);
        s.WriteInt(Field13);
        s.WriteString(Field14);
        s.WriteShort(Field15);
        s.WriteShort(Field16);
        s.WriteByte(Field17);
        s.WriteInt(Field18);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadFloat();
        Field4 = s.ReadFloat();
        Field5 = s.ReadFloat();
        Field6 = s.ReadFloat();
        Field7 = s.ReadInt();
        Field8 = s.ReadInt();
        Field9 = s.ReadByte();
        Field10 = s.ReadByte();
        Field11 = s.ReadByte();
        Field12 = s.ReadByte();
        Field13 = s.ReadInt();
        Field14 = s.ReadString();
        Field15 = s.ReadShort();
        Field16 = s.ReadShort();
        Field17 = s.ReadByte();
        Field18 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientResurrect — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientResurrect</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientResurrect : NetMessage
{
    public override int Opcode => 341;
    public override string Name => "CNC_CZ_ClientResurrect";

    public uint Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientAskRepairEquipGold — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientAskRepairEquipGold</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientAskRepairEquipGold : NetMessage
{
    public override int Opcode => 342;
    public override string Name => "CNC_CZ_ClientAskRepairEquipGold";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientAskRepairOneItemGold — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientAskRepairOneItemGold</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientAskRepairOneItemGold : NetMessage
{
    public override int Opcode => 343;
    public override string Name => "CNC_CZ_ClientAskRepairOneItemGold";

    public byte Field1 { get; set; }
    public byte Field2 { get; set; }
    public int Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
        s.WriteInt(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
        Field3 = s.ReadInt();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterFamilyCreation — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterFamilyCreation</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterFamilyCreation : NetMessage
{
    public override int Opcode => 347;
    public override string Name => "CNC_CZ_ClientEnterFamilyCreation";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEnterSpellShop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterSpellShop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterSpellShop : NetMessage
{
    public override int Opcode => 349;
    public override string Name => "CNC_CZ_ClientEnterSpellShop";

    public int Field1 { get; set; }
    public ushort Field2 { get; set; }
    public ushort Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort((short)Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = (ushort)s.ReadShort();
        Field3 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterRepairShop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterRepairShop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterRepairShop : NetMessage
{
    public override int Opcode => 350;
    public override string Name => "CNC_CZ_ClientEnterRepairShop";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientUseSkillPointOK — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUseSkillPointOK</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUseSkillPointOK : NetMessage
{
    public override int Opcode => 354;
    public override string Name => "CNC_CZ_ClientUseSkillPointOK";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientLogoutResp — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLogoutResp</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLogoutResp : NetMessage
{
    public override int Opcode => 355;
    public override string Name => "CNC_CZ_ClientLogoutResp";

    public byte Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientCanLogout — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientCanLogout</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientCanLogout : NetMessage
{
    public override int Opcode => 356;
    public override string Name => "CNC_CZ_ClientCanLogout";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientPutItemIntoStorage — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPutItemIntoStorage</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPutItemIntoStorage : NetMessage
{
    public override int Opcode => 359;
    public override string Name => "CNC_CZ_ClientPutItemIntoStorage";

    public ushort Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public string Field6 { get; set; } = "";
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public short Field12 { get; set; }
    public short Field13 { get; set; }
    public short Field14 { get; set; }
    public uint Field15 { get; set; }
    public ushort Field16 { get; set; }
    public ushort Field17 { get; set; }
    public ushort Field18 { get; set; }
    public ushort Field19 { get; set; }
    public ushort Field20 { get; set; }
    public ushort Field21 { get; set; }
    public ushort Field22 { get; set; }
    public ushort Field23 { get; set; }
    public ushort Field24 { get; set; }
    public ushort Field25 { get; set; }
    public short Field26 { get; set; }
    public short Field27 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteString(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteShort(Field12);
        s.WriteShort(Field13);
        s.WriteShort(Field14);
        s.WriteInt((int)Field15);
        s.WriteShort((short)Field16);
        s.WriteShort((short)Field17);
        s.WriteShort((short)Field18);
        s.WriteShort((short)Field19);
        s.WriteShort((short)Field20);
        s.WriteShort((short)Field21);
        s.WriteShort((short)Field22);
        s.WriteShort((short)Field23);
        s.WriteShort((short)Field24);
        s.WriteShort((short)Field25);
        s.WriteShort(Field26);
        s.WriteShort(Field27);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadString();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = s.ReadShort();
        Field14 = s.ReadShort();
        Field15 = (uint)s.ReadInt();
        Field16 = (ushort)s.ReadShort();
        Field17 = (ushort)s.ReadShort();
        Field18 = (ushort)s.ReadShort();
        Field19 = (ushort)s.ReadShort();
        Field20 = (ushort)s.ReadShort();
        Field21 = (ushort)s.ReadShort();
        Field22 = (ushort)s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = (ushort)s.ReadShort();
        Field25 = (ushort)s.ReadShort();
        Field26 = s.ReadShort();
        Field27 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientPutItemIntoContainer — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPutItemIntoContainer</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPutItemIntoContainer : NetMessage
{
    public override int Opcode => 360;
    public override string Name => "CNC_CZ_ClientPutItemIntoContainer";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public string Field7 { get; set; } = "";
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public short Field12 { get; set; }
    public short Field13 { get; set; }
    public short Field14 { get; set; }
    public short Field15 { get; set; }
    public uint Field16 { get; set; }
    public ushort Field17 { get; set; }
    public ushort Field18 { get; set; }
    public ushort Field19 { get; set; }
    public ushort Field20 { get; set; }
    public ushort Field21 { get; set; }
    public ushort Field22 { get; set; }
    public ushort Field23 { get; set; }
    public ushort Field24 { get; set; }
    public ushort Field25 { get; set; }
    public ushort Field26 { get; set; }
    public short Field27 { get; set; }
    public short Field28 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteString(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteShort(Field12);
        s.WriteShort(Field13);
        s.WriteShort(Field14);
        s.WriteShort(Field15);
        s.WriteInt((int)Field16);
        s.WriteShort((short)Field17);
        s.WriteShort((short)Field18);
        s.WriteShort((short)Field19);
        s.WriteShort((short)Field20);
        s.WriteShort((short)Field21);
        s.WriteShort((short)Field22);
        s.WriteShort((short)Field23);
        s.WriteShort((short)Field24);
        s.WriteShort((short)Field25);
        s.WriteShort((short)Field26);
        s.WriteShort(Field27);
        s.WriteShort(Field28);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadString();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = s.ReadShort();
        Field14 = s.ReadShort();
        Field15 = s.ReadShort();
        Field16 = (uint)s.ReadInt();
        Field17 = (ushort)s.ReadShort();
        Field18 = (ushort)s.ReadShort();
        Field19 = (ushort)s.ReadShort();
        Field20 = (ushort)s.ReadShort();
        Field21 = (ushort)s.ReadShort();
        Field22 = (ushort)s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = (ushort)s.ReadShort();
        Field25 = (ushort)s.ReadShort();
        Field26 = (ushort)s.ReadShort();
        Field27 = s.ReadShort();
        Field28 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientSpellLoopCastStop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSpellLoopCastStop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSpellLoopCastStop : NetMessage
{
    public override int Opcode => 361;
    public override string Name => "CNC_CZ_ClientSpellLoopCastStop";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientQueryNpc — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientQueryNpc</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientQueryNpc : NetMessage
{
    public override int Opcode => 362;
    public override string Name => "CNC_CZ_ClientQueryNpc";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientAskIdentifyGold — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientAskIdentifyGold</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientAskIdentifyGold : NetMessage
{
    public override int Opcode => 363;
    public override string Name => "CNC_CZ_ClientAskIdentifyGold";

    public ushort Field1 { get; set; }
    public ushort Field2 { get; set; }
    public int Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
        s.WriteInt(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientIdentifyItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientIdentifyItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientIdentifyItem : NetMessage
{
    public override int Opcode => 364;
    public override string Name => "CNC_CZ_ClientIdentifyItem";

    public ushort Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterIdentifyShop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterIdentifyShop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterIdentifyShop : NetMessage
{
    public override int Opcode => 365;
    public override string Name => "CNC_CZ_ClientEnterIdentifyShop";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientEnterExchageItemService — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterExchageItemService</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterExchageItemService : NetMessage
{
    public override int Opcode => 366;
    public override string Name => "CNC_CZ_ClientEnterExchageItemService";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientResetSpell — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientResetSpell</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientResetSpell : NetMessage
{
    public override int Opcode => 367;
    public override string Name => "CNC_CZ_ClientResetSpell";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public ushort Field6 { get; set; }
    public ushort Field7 { get; set; }
    public int Field8 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort((short)Field6);
        s.WriteShort((short)Field7);
        s.WriteInt(Field8);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = (ushort)s.ReadShort();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientModifyItemInfo (369) — record complet natif
/// (<c>CNC_CZ_ClientModifyItemInfo::Serialize</c> zone @0x80cb170) :
/// <c>color</c> est un <c>i64</c>, la queue est <c>[u8][u8]</c>, et
/// le mode 8 (bind) s'arrête après <c>[u16][s16][u16][u8]</c>.
/// Champs positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientModifyItemInfo : NetMessage
{
    public override int Opcode => 369;
    public override string Name => "CNC_CZ_ClientModifyItemInfo";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public ushort Field4 { get; set; }
    public byte Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public short Field12 { get; set; }
    public short Field13 { get; set; }
    public string Field14 { get; set; } = "";
    public ushort Field15 { get; set; }
    public ushort Field16 { get; set; }
    public ushort Field17 { get; set; }
    public ushort Field18 { get; set; }
    public ushort Field19 { get; set; }
    public ushort Field20 { get; set; }
    public ushort Field21 { get; set; }
    public ushort Field22 { get; set; }
    public ushort Field23 { get; set; }
    public ushort Field24 { get; set; }
    public long Field25 { get; set; }
    public ushort Field26 { get; set; }
    public uint Field27 { get; set; }
    public ushort Field28 { get; set; }
    public ushort Field29 { get; set; }
    public ushort Field30 { get; set; }
    public ushort Field31 { get; set; }
    public ushort Field32 { get; set; }
    public ushort Field33 { get; set; }
    public ushort Field34 { get; set; }
    public ushort Field35 { get; set; }
    public short Field36 { get; set; }
    public ushort Field37 { get; set; }
    public short Field38 { get; set; }
    public short Field39 { get; set; }
    public short Field40 { get; set; }
    public short Field41 { get; set; }
    public ushort Field42 { get; set; }
    public ushort Field43 { get; set; }
    public byte Field44 { get; set; }
    public byte Field45 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort((short)Field4);
        if (Field1 == 8)
        {
            // Mode 8 (bind) : 3 champs + octet de queue, rien d'autre
            // (Serialize natif @0x80cb170).
            s.WriteByte(Field45);
            return;
        }
        s.WriteByte(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteShort(Field12);
        s.WriteShort(Field13);
        s.WriteString(Field14);
        s.WriteShort((short)Field15);
        s.WriteShort((short)Field16);
        s.WriteShort((short)Field17);
        s.WriteShort((short)Field18);
        s.WriteShort((short)Field19);
        s.WriteShort((short)Field20);
        s.WriteShort((short)Field21);
        s.WriteShort((short)Field22);
        s.WriteShort((short)Field23);
        s.WriteShort((short)Field24);
        s.WriteLong(Field25);
        s.WriteShort((short)Field26);
        s.WriteInt((int)Field27);
        s.WriteShort((short)Field28);
        s.WriteShort((short)Field29);
        s.WriteShort((short)Field30);
        s.WriteShort((short)Field31);
        s.WriteShort((short)Field32);
        s.WriteShort((short)Field33);
        s.WriteShort((short)Field34);
        s.WriteShort((short)Field35);
        s.WriteShort(Field36);
        s.WriteShort((short)Field37);
        s.WriteShort(Field38);
        s.WriteShort(Field39);
        s.WriteShort(Field40);
        s.WriteShort(Field41);
        s.WriteShort((short)Field42);
        s.WriteShort((short)Field43);
        s.WriteByte(Field44);
        s.WriteByte(Field45);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        if (Field1 == 8)
        {
            Field45 = s.ReadByte();
            return;
        }
        Field5 = s.ReadByte();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = s.ReadShort();
        Field14 = s.ReadString();
        Field15 = (ushort)s.ReadShort();
        Field16 = (ushort)s.ReadShort();
        Field17 = (ushort)s.ReadShort();
        Field18 = (ushort)s.ReadShort();
        Field19 = (ushort)s.ReadShort();
        Field20 = (ushort)s.ReadShort();
        Field21 = (ushort)s.ReadShort();
        Field22 = (ushort)s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = (ushort)s.ReadShort();
        Field25 = s.ReadLong();
        Field26 = (ushort)s.ReadShort();
        Field27 = (uint)s.ReadInt();
        Field28 = (ushort)s.ReadShort();
        Field29 = (ushort)s.ReadShort();
        Field30 = (ushort)s.ReadShort();
        Field31 = (ushort)s.ReadShort();
        Field32 = (ushort)s.ReadShort();
        Field33 = (ushort)s.ReadShort();
        Field34 = (ushort)s.ReadShort();
        Field35 = (ushort)s.ReadShort();
        Field36 = s.ReadShort();
        Field37 = (ushort)s.ReadShort();
        Field38 = s.ReadShort();
        Field39 = s.ReadShort();
        Field40 = s.ReadShort();
        Field41 = s.ReadShort();
        Field42 = (ushort)s.ReadShort();
        Field43 = (ushort)s.ReadShort();
        Field44 = s.ReadByte();
        Field45 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientEnterInlayShop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterInlayShop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterInlayShop : NetMessage
{
    public override int Opcode => 370;
    public override string Name => "CNC_CZ_ClientEnterInlayShop";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientInlayItemResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientInlayItemResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientInlayItemResult : NetMessage
{
    public override int Opcode => 371;
    public override string Name => "CNC_CZ_ClientInlayItemResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientSloganUpdate — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSloganUpdate</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSloganUpdate : NetMessage
{
    public override int Opcode => 374;
    public override string Name => "CNC_CZ_ClientSloganUpdate";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public byte Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteByte(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientPlayEffect — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlayEffect</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlayEffect : NetMessage
{
    public override int Opcode => 383;
    public override string Name => "CNC_CZ_ClientPlayEffect";

    public float Field1 { get; set; }
    public float Field2 { get; set; }
    public float Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public float Field6 { get; set; }
    public string Field7 { get; set; } = "";
    public int Field8 { get; set; }
    public byte Field9 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteFloat(Field1);
        s.WriteFloat(Field2);
        s.WriteFloat(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteFloat(Field6);
        s.WriteString(Field7);
        s.WriteInt(Field8);
        s.WriteByte(Field9);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadFloat();
        Field2 = s.ReadFloat();
        Field3 = s.ReadFloat();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadFloat();
        Field7 = s.ReadString();
        Field8 = s.ReadInt();
        Field9 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientSelectFamilyEmblem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSelectFamilyEmblem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSelectFamilyEmblem : NetMessage
{
    public override int Opcode => 384;
    public override string Name => "CNC_CZ_ClientSelectFamilyEmblem";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientOpenExploitRank — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenExploitRank</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenExploitRank : NetMessage
{
    public override int Opcode => 385;
    public override string Name => "CNC_CZ_ClientOpenExploitRank";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientTeamRadarAttention — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTeamRadarAttention</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTeamRadarAttention : NetMessage
{
    public override int Opcode => 387;
    public override string Name => "CNC_CZ_ClientTeamRadarAttention";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientBagDueDate — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBagDueDate</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBagDueDate : NetMessage
{
    public override int Opcode => 389;
    public override string Name => "CNC_CZ_ClientBagDueDate";

    public short Field1 { get; set; }
    public int Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateInteraction — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateInteraction</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateInteraction : NetMessage
{
    public override int Opcode => 390;
    public override string Name => "CNC_CZ_ClientUpdateInteraction";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUseItemTime — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUseItemTime</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUseItemTime : NetMessage
{
    public override int Opcode => 407;
    public override string Name => "CNC_CZ_ClientUseItemTime";

    public uint Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientUseItemTimeBreak — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUseItemTimeBreak</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUseItemTimeBreak : NetMessage
{
    public override int Opcode => 408;
    public override string Name => "CNC_CZ_ClientUseItemTimeBreak";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientSearchInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSearchInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSearchInfo : NetMessage
{
    public override int Opcode => 411;
    public override string Name => "CNC_CZ_ClientSearchInfo";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }
    public string Field4 { get; set; } = "";
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public byte Field9 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
        s.WriteString(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteByte(Field9);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadString();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientElfWorkResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfWorkResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfWorkResult : NetMessage
{
    public override int Opcode => 415;
    public override string Name => "CNC_CZ_ClientElfWorkResult";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public int Field4 { get; set; }
    public byte Field5 { get; set; }
    public ushort Field6 { get; set; }
    public ushort Field7 { get; set; }
    public int Field8 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
        s.WriteByte(Field5);
        s.WriteShort((short)Field6);
        s.WriteShort((short)Field7);
        s.WriteInt(Field8);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
        Field5 = s.ReadByte();
        Field6 = (ushort)s.ReadShort();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientElfWorkResultEnd — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfWorkResultEnd</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfWorkResultEnd : NetMessage
{
    public override int Opcode => 416;
    public override string Name => "CNC_CZ_ClientElfWorkResultEnd";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientElfGoToWork — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfGoToWork</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfGoToWork : NetMessage
{
    public override int Opcode => 421;
    public override string Name => "CNC_CZ_ClientElfGoToWork";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public int Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteInt(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientElfDecompose — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfDecompose</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfDecompose : NetMessage
{
    public override int Opcode => 422;
    public override string Name => "CNC_CZ_ClientElfDecompose";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientElfStoneUse — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfStoneUse</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfStoneUse : NetMessage
{
    public override int Opcode => 423;
    public override string Name => "CNC_CZ_ClientElfStoneUse";

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

/// <summary>CNC_CZ_ClientEnterNodeElfContainer — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterNodeElfContainer</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterNodeElfContainer : NetMessage
{
    public override int Opcode => 424;
    public override string Name => "CNC_CZ_ClientEnterNodeElfContainer";

    public byte Field1 { get; set; }
    public short Field2 { get; set; }
    public byte Field3 { get; set; }
    public ushort Field4 { get; set; }
    public byte Field5 { get; set; }
    public ushort Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public string Field9 { get; set; } = "";
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public short Field12 { get; set; }
    public short Field13 { get; set; }
    public short Field14 { get; set; }
    public short Field15 { get; set; }
    public short Field16 { get; set; }
    public short Field17 { get; set; }
    public uint Field18 { get; set; }
    public ushort Field19 { get; set; }
    public ushort Field20 { get; set; }
    public ushort Field21 { get; set; }
    public ushort Field22 { get; set; }
    public ushort Field23 { get; set; }
    public ushort Field24 { get; set; }
    public ushort Field25 { get; set; }
    public ushort Field26 { get; set; }
    public ushort Field27 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort(Field2);
        s.WriteByte(Field3);
        s.WriteShort((short)Field4);
        s.WriteByte(Field5);
        s.WriteShort((short)Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteString(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteShort(Field12);
        s.WriteShort(Field13);
        s.WriteShort(Field14);
        s.WriteShort(Field15);
        s.WriteShort(Field16);
        s.WriteShort(Field17);
        s.WriteInt((int)Field18);
        s.WriteShort((short)Field19);
        s.WriteShort((short)Field20);
        s.WriteShort((short)Field21);
        s.WriteShort((short)Field22);
        s.WriteShort((short)Field23);
        s.WriteShort((short)Field24);
        s.WriteShort((short)Field25);
        s.WriteShort((short)Field26);
        s.WriteShort((short)Field27);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadShort();
        Field3 = s.ReadByte();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadByte();
        Field6 = (ushort)s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadString();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = s.ReadShort();
        Field14 = s.ReadShort();
        Field15 = s.ReadShort();
        Field16 = s.ReadShort();
        Field17 = s.ReadShort();
        Field18 = (uint)s.ReadInt();
        Field19 = (ushort)s.ReadShort();
        Field20 = (ushort)s.ReadShort();
        Field21 = (ushort)s.ReadShort();
        Field22 = (ushort)s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = (ushort)s.ReadShort();
        Field25 = (ushort)s.ReadShort();
        Field26 = (ushort)s.ReadShort();
        Field27 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterStrengthen — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterStrengthen</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterStrengthen : NetMessage
{
    public override int Opcode => 432;
    public override string Name => "CNC_CZ_ClientEnterStrengthen";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public ushort Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort((short)Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientStrengthenItemCancel — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientStrengthenItemCancel</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientStrengthenItemCancel : NetMessage
{
    public override int Opcode => 434;
    public override string Name => "CNC_CZ_ClientStrengthenItemCancel";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientStrengthenItemResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientStrengthenItemResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientStrengthenItemResult : NetMessage
{
    public override int Opcode => 435;
    public override string Name => "CNC_CZ_ClientStrengthenItemResult";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientModifyItemStrengthen — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientModifyItemStrengthen</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientModifyItemStrengthen : NetMessage
{
    public override int Opcode => 436;
    public override string Name => "CNC_CZ_ClientModifyItemStrengthen";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientSetSpellStone — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSetSpellStone</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSetSpellStone : NetMessage
{
    public override int Opcode => 437;
    public override string Name => "CNC_CZ_ClientSetSpellStone";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientSummonPet — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSummonPet</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSummonPet : NetMessage
{
    public override int Opcode => 438;
    public override string Name => "CNC_CZ_ClientSummonPet";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public int Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteInt(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEnterElfReturn — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterElfReturn</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterElfReturn : NetMessage
{
    public override int Opcode => 439;
    public override string Name => "CNC_CZ_ClientEnterElfReturn";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientMobChangeAI — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientMobChangeAI</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientMobChangeAI : NetMessage
{
    public override int Opcode => 440;
    public override string Name => "CNC_CZ_ClientMobChangeAI";

    public int Field1 { get; set; }
    public byte Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteByte(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientTurnChar — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTurnChar</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTurnChar : NetMessage
{
    public override int Opcode => 441;
    public override string Name => "CNC_CZ_ClientTurnChar";

    public int Field1 { get; set; }
    public float Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteFloat(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadFloat();
    }
}

/// <summary>CNC_CZ_ClientElfReturnEffect — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientElfReturnEffect</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientElfReturnEffect : NetMessage
{
    public override int Opcode => 443;
    public override string Name => "CNC_CZ_ClientElfReturnEffect";

    public int Field1 { get; set; }
    public int Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientPVPTime — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPVPTime</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPVPTime : NetMessage
{
    public override int Opcode => 447;
    public override string Name => "CNC_CZ_ClientPVPTime";

    public byte Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientLotteryResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLotteryResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLotteryResult : NetMessage
{
    public override int Opcode => 448;
    public override string Name => "CNC_CZ_ClientLotteryResult";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public int Field6 { get; set; }
    public uint Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public byte Field11 { get; set; }
    public byte Field12 { get; set; }
    public ushort Field13 { get; set; }
    public ushort Field14 { get; set; }
    public short Field15 { get; set; }
    public int Field16 { get; set; }
    public short Field17 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteInt(Field6);
        s.WriteInt((int)Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteByte(Field11);
        s.WriteByte(Field12);
        s.WriteShort((short)Field13);
        s.WriteShort((short)Field14);
        s.WriteShort(Field15);
        s.WriteInt(Field16);
        s.WriteShort(Field17);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadInt();
        Field7 = (uint)s.ReadInt();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadByte();
        Field12 = s.ReadByte();
        Field13 = (ushort)s.ReadShort();
        Field14 = (ushort)s.ReadShort();
        Field15 = s.ReadShort();
        Field16 = s.ReadInt();
        Field17 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientLotteryEnd — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLotteryEnd</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLotteryEnd : NetMessage
{
    public override int Opcode => 449;
    public override string Name => "CNC_CZ_ClientLotteryEnd";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public byte Field4 { get; set; }
    public int Field5 { get; set; }
    public short Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteByte(Field4);
        s.WriteInt(Field5);
        s.WriteShort(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadByte();
        Field5 = s.ReadInt();
        Field6 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientLotteryUpdatePlus — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLotteryUpdatePlus</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLotteryUpdatePlus : NetMessage
{
    public override int Opcode => 450;
    public override string Name => "CNC_CZ_ClientLotteryUpdatePlus";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientDuelRequest — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDuelRequest</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDuelRequest : NetMessage
{
    public override int Opcode => 451;
    public override string Name => "CNC_CZ_ClientDuelRequest";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
    }
}

/// <summary>CNC_CZ_ClientDuelRequestResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDuelRequestResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDuelRequestResult : NetMessage
{
    public override int Opcode => 452;
    public override string Name => "CNC_CZ_ClientDuelRequestResult";

    public string Field1 { get; set; } = "";
    public string Field2 { get; set; } = "";
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteString(Field1);
        s.WriteString(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadString();
        Field2 = s.ReadString();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientDuelBegin — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDuelBegin</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDuelBegin : NetMessage
{
    public override int Opcode => 453;
    public override string Name => "CNC_CZ_ClientDuelBegin";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientDuelResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDuelResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDuelResult : NetMessage
{
    public override int Opcode => 454;
    public override string Name => "CNC_CZ_ClientDuelResult";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public string Field3 { get; set; } = "";
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteString(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadString();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterMessageBoard — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterMessageBoard</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterMessageBoard : NetMessage
{
    public override int Opcode => 455;
    public override string Name => "CNC_CZ_ClientEnterMessageBoard";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientMessageList — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientMessageList</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientMessageList : NetMessage
{
    public override int Opcode => 456;
    public override string Name => "CNC_CZ_ClientMessageList";

    public ushort Field1 { get; set; }
    public int Field2 { get; set; }
    public string Field3 { get; set; } = "";
    public string Field4 { get; set; } = "";
    public int Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteInt(Field2);
        s.WriteString(Field3);
        s.WriteString(Field4);
        s.WriteInt(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadString();
        Field4 = s.ReadString();
        Field5 = s.ReadInt();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientMessageWriteResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientMessageWriteResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientMessageWriteResult : NetMessage
{
    public override int Opcode => 457;
    public override string Name => "CNC_CZ_ClientMessageWriteResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientMessageRead — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientMessageRead</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientMessageRead : NetMessage
{
    public override int Opcode => 458;
    public override string Name => "CNC_CZ_ClientMessageRead";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public byte Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteByte(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientMessageDeleteResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientMessageDeleteResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientMessageDeleteResult : NetMessage
{
    public override int Opcode => 459;
    public override string Name => "CNC_CZ_ClientMessageDeleteResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientItemRent — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientItemRent</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientItemRent : NetMessage
{
    public override int Opcode => 461;
    public override string Name => "CNC_CZ_ClientItemRent";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public uint Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteInt((int)Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientBattlefieldList — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattlefieldList</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattlefieldList : NetMessage
{
    public override int Opcode => 462;
    public override string Name => "CNC_CZ_ClientBattlefieldList";

    public ushort Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public ushort Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public uint Field8 { get; set; }
    public uint Field9 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort((short)Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteInt((int)Field8);
        s.WriteInt((int)Field9);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = (uint)s.ReadInt();
        Field9 = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEnterBattlefield — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterBattlefield</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterBattlefield : NetMessage
{
    public override int Opcode => 463;
    public override string Name => "CNC_CZ_ClientEnterBattlefield";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientBattlefieldJoinResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattlefieldJoinResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattlefieldJoinResult : NetMessage
{
    public override int Opcode => 464;
    public override string Name => "CNC_CZ_ClientBattlefieldJoinResult";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientBattlefieldResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattlefieldResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattlefieldResult : NetMessage
{
    public override int Opcode => 465;
    public override string Name => "CNC_CZ_ClientBattlefieldResult";

    public byte Field1 { get; set; }
    public int Field2 { get; set; }
    public ushort Field3 { get; set; }
    public string Field4 { get; set; } = "";
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public byte Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public int Field10 { get; set; }
    public int Field11 { get; set; }
    public int Field12 { get; set; }
    public short Field13 { get; set; }
    public byte Field14 { get; set; }
    public string Field15 { get; set; } = "";
    public short Field16 { get; set; }
    public short Field17 { get; set; }
    public byte Field18 { get; set; }
    public short Field19 { get; set; }
    public short Field20 { get; set; }
    public int Field21 { get; set; }
    public int Field22 { get; set; }
    public int Field23 { get; set; }
    public short Field24 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteInt(Field2);
        s.WriteShort((short)Field3);
        s.WriteString(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteByte(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteInt(Field10);
        s.WriteInt(Field11);
        s.WriteInt(Field12);
        s.WriteShort(Field13);
        s.WriteByte(Field14);
        s.WriteString(Field15);
        s.WriteShort(Field16);
        s.WriteShort(Field17);
        s.WriteByte(Field18);
        s.WriteShort(Field19);
        s.WriteShort(Field20);
        s.WriteInt(Field21);
        s.WriteInt(Field22);
        s.WriteInt(Field23);
        s.WriteShort(Field24);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadInt();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadString();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadByte();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadInt();
        Field11 = s.ReadInt();
        Field12 = s.ReadInt();
        Field13 = s.ReadShort();
        Field14 = s.ReadByte();
        Field15 = s.ReadString();
        Field16 = s.ReadShort();
        Field17 = s.ReadShort();
        Field18 = s.ReadByte();
        Field19 = s.ReadShort();
        Field20 = s.ReadShort();
        Field21 = s.ReadInt();
        Field22 = s.ReadInt();
        Field23 = s.ReadInt();
        Field24 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientBattlefieldUpdateScore — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattlefieldUpdateScore</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattlefieldUpdateScore : NetMessage
{
    public override int Opcode => 466;
    public override string Name => "CNC_CZ_ClientBattlefieldUpdateScore";

    public byte Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientAltarState — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientAltarState</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientAltarState : NetMessage
{
    public override int Opcode => 467;
    public override string Name => "CNC_CZ_ClientAltarState";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public ushort Field3 { get; set; }
    public string Field4 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort((short)Field3);
        s.WriteString(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadString();
    }
}

/// <summary>CNC_CZ_ClientBattleMemberPosition — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattleMemberPosition</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattleMemberPosition : NetMessage
{
    public override int Opcode => 468;
    public override string Name => "CNC_CZ_ClientBattleMemberPosition";

    public int Field1 { get; set; }
    public float Field2 { get; set; }
    public float Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteFloat(Field2);
        s.WriteFloat(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadFloat();
        Field3 = s.ReadFloat();
    }
}

/// <summary>CNC_CZ_ClientBattlefieldMemberInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattlefieldMemberInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattlefieldMemberInfo : NetMessage
{
    public override int Opcode => 469;
    public override string Name => "CNC_CZ_ClientBattlefieldMemberInfo";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public int Field3 { get; set; }
    public string Field4 { get; set; } = "";
    public short Field5 { get; set; }
    public byte Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteInt(Field3);
        s.WriteString(Field4);
        s.WriteShort(Field5);
        s.WriteByte(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadString();
        Field5 = s.ReadShort();
        Field6 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientTextOutputItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTextOutputItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTextOutputItem : NetMessage
{
    public override int Opcode => 481;
    public override string Name => "CNC_CZ_ClientTextOutputItem";

    public byte Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public short Field3 { get; set; }
    public ushort Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public uint Field7 { get; set; }
    public byte Field8 { get; set; }
    public byte Field9 { get; set; }
    public byte Field10 { get; set; }
    public ushort Field11 { get; set; }
    public short Field12 { get; set; }
    public string Field13 { get; set; } = "";
    public short Field14 { get; set; }
    public ushort Field15 { get; set; }
    public short Field16 { get; set; }
    public short Field17 { get; set; }
    public short Field18 { get; set; }
    public int Field19 { get; set; }
    public short Field20 { get; set; }
    public short Field21 { get; set; }
    public ushort Field22 { get; set; }
    public uint Field23 { get; set; }
    public ushort Field24 { get; set; }
    public ushort Field25 { get; set; }
    public ushort Field26 { get; set; }
    public ushort Field27 { get; set; }
    public ushort Field28 { get; set; }
    public ushort Field29 { get; set; }
    public ushort Field30 { get; set; }
    public short Field31 { get; set; }
    public short Field32 { get; set; }
    public short Field33 { get; set; }
    public ushort Field34 { get; set; }
    public ushort Field35 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteString(Field2);
        s.WriteShort(Field3);
        s.WriteShort((short)Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteInt((int)Field7);
        s.WriteByte(Field8);
        s.WriteByte(Field9);
        s.WriteByte(Field10);
        s.WriteShort((short)Field11);
        s.WriteShort(Field12);
        s.WriteString(Field13);
        s.WriteShort(Field14);
        s.WriteShort((short)Field15);
        s.WriteShort(Field16);
        s.WriteShort(Field17);
        s.WriteShort(Field18);
        s.WriteInt(Field19);
        s.WriteShort(Field20);
        s.WriteShort(Field21);
        s.WriteShort((short)Field22);
        s.WriteInt((int)Field23);
        s.WriteShort((short)Field24);
        s.WriteShort((short)Field25);
        s.WriteShort((short)Field26);
        s.WriteShort((short)Field27);
        s.WriteShort((short)Field28);
        s.WriteShort((short)Field29);
        s.WriteShort((short)Field30);
        s.WriteShort(Field31);
        s.WriteShort(Field32);
        s.WriteShort(Field33);
        s.WriteShort((short)Field34);
        s.WriteShort((short)Field35);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadString();
        Field3 = s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = (uint)s.ReadInt();
        Field8 = s.ReadByte();
        Field9 = s.ReadByte();
        Field10 = s.ReadByte();
        Field11 = (ushort)s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = s.ReadString();
        Field14 = s.ReadShort();
        Field15 = (ushort)s.ReadShort();
        Field16 = s.ReadShort();
        Field17 = s.ReadShort();
        Field18 = s.ReadShort();
        Field19 = s.ReadInt();
        Field20 = s.ReadShort();
        Field21 = s.ReadShort();
        Field22 = (ushort)s.ReadShort();
        Field23 = (uint)s.ReadInt();
        Field24 = (ushort)s.ReadShort();
        Field25 = (ushort)s.ReadShort();
        Field26 = (ushort)s.ReadShort();
        Field27 = (ushort)s.ReadShort();
        Field28 = (ushort)s.ReadShort();
        Field29 = (ushort)s.ReadShort();
        Field30 = (ushort)s.ReadShort();
        Field31 = s.ReadShort();
        Field32 = s.ReadShort();
        Field33 = s.ReadShort();
        Field34 = (ushort)s.ReadShort();
        Field35 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterFamilyBuilding — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterFamilyBuilding</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterFamilyBuilding : NetMessage
{
    public override int Opcode => 483;
    public override string Name => "CNC_CZ_ClientEnterFamilyBuilding";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientFamilyContainerData (484, S→C) — contenu
/// d'un conteneur de hall familial (émis après 483 ou 132, puis après
/// chaque mutation 130/131). Format prouvé par Serialize @0x81e7b40 +
/// ctor @0x8578f20, miroir
/// <c>FamilyPackets.FamilyContainerData</c> :
/// <code>[s16 type][s16 container_index][u16 n] puis par item :
///   [s16 loc][i32 item_tid][i32 tid][s16 item_type][s16 durability]
///   [s16 level][u32 exp][u32 due_date][u32 aux(0)]</code>
/// La classe générée aplatissait le vecteur — codec dédié.</summary>
public sealed class ZoneClientFamilyContainerData : NetMessage
{
    public override int Opcode => 484;
    public override string Name => "CNC_CZ_ClientFamilyContainerData";

    /// <summary>Un item/bâtiment du conteneur.</summary>
    public readonly record struct Item(short Loc, int ItemTid,
        int Tid, short ItemType, short Durability, short Level,
        int Exp, int DueDate, int Aux);

    public short Type { get; private set; }
    public short Container { get; private set; }
    public List<Item> Items { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadShort();
        Container = s.ReadShort();
        Items.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
            Items.Add(new Item(s.ReadShort(), s.ReadInt(),
                s.ReadInt(), s.ReadShort(), s.ReadShort(),
                s.ReadShort(), s.ReadInt(), s.ReadInt(), s.ReadInt()));
    }
}

/// <summary>CNC_CZ_ClientFamilyContainerDeleteItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientFamilyContainerDeleteItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientFamilyContainerDeleteItem : NetMessage
{
    public override int Opcode => 485;
    public override string Name => "CNC_CZ_ClientFamilyContainerDeleteItem";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientFamilyContainerPutItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientFamilyContainerPutItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientFamilyContainerPutItem : NetMessage
{
    public override int Opcode => 486;
    public override string Name => "CNC_CZ_ClientFamilyContainerPutItem";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public int Field5 { get; set; }
    public int Field6 { get; set; }
    public short Field7 { get; set; }
    public int Field8 { get; set; }
    public short Field9 { get; set; }
    public uint Field10 { get; set; }
    public uint Field11 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteInt(Field5);
        s.WriteInt(Field6);
        s.WriteShort(Field7);
        s.WriteInt(Field8);
        s.WriteShort(Field9);
        s.WriteInt((int)Field10);
        s.WriteInt((int)Field11);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadInt();
        Field6 = s.ReadInt();
        Field7 = s.ReadShort();
        Field8 = s.ReadInt();
        Field9 = s.ReadShort();
        Field10 = (uint)s.ReadInt();
        Field11 = (uint)s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientFamilyInstanceData (487, S→C) — données du
/// hall de famille visité (réponse au 132). Format prouvé par
/// Serialize @0x81e82e0 + construction dans
/// <c>CNE_CZ_ZoneServerFamilyInstanceGetData::Execute @0x8256ee0</c>,
/// miroir <c>FamilyPackets.FamilyInstanceData</c> :
/// <code>[i32 family_id][str nom][s16 level][s16 house_open]
/// [s16 container_index][u16 n] puis par bâtiment :
///   [s16 loc][i32 item_tid][i32 tid][s16 item_type]
///   [s16 durability][s16 level][u32 exp]</code>
/// La classe générée aplatissait le vecteur — codec dédié.</summary>
public sealed class ZoneClientFamilyInstanceData : NetMessage
{
    public override int Opcode => 487;
    public override string Name => "CNC_CZ_ClientFamilyInstanceData";

    /// <summary>Un bâtiment du hall.</summary>
    public readonly record struct Building(short Loc, int ItemTid,
        int Tid, short ItemType, short Durability, short Level,
        int Exp);

    public int FamilyId { get; private set; }
    public string FamilyName { get; private set; } = "";
    public short Level { get; private set; }
    public short HouseOpen { get; private set; }
    public short Container { get; private set; }
    public List<Building> Buildings { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        FamilyId = s.ReadInt();
        FamilyName = s.ReadString();
        Level = s.ReadShort();
        HouseOpen = s.ReadShort();
        Container = s.ReadShort();
        Buildings.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
            Buildings.Add(new Building(s.ReadShort(), s.ReadInt(),
                s.ReadInt(), s.ReadShort(), s.ReadShort(),
                s.ReadShort(), s.ReadInt()));
    }
}

/// <summary>CNC_CZ_ClientPlaySound — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlaySound</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlaySound : NetMessage
{
    public override int Opcode => 494;
    public override string Name => "CNC_CZ_ClientPlaySound";

    public string Field1 { get; set; } = "";
    public float Field2 { get; set; }
    public float Field3 { get; set; }
    public float Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteString(Field1);
        s.WriteFloat(Field2);
        s.WriteFloat(Field3);
        s.WriteFloat(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadString();
        Field2 = s.ReadFloat();
        Field3 = s.ReadFloat();
        Field4 = s.ReadFloat();
    }
}

/// <summary>CNC_CZ_ClientPlayMonsterAction — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlayMonsterAction</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlayMonsterAction : NetMessage
{
    public override int Opcode => 495;
    public override string Name => "CNC_CZ_ClientPlayMonsterAction";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientPlayerEvent — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPlayerEvent</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPlayerEvent : NetMessage
{
    public override int Opcode => 499;
    public override string Name => "CNC_CZ_ClientPlayerEvent";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientEnterFamilyInstanceCreation — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterFamilyInstanceCreation</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterFamilyInstanceCreation : NetMessage
{
    public override int Opcode => 501;
    public override string Name => "CNC_CZ_ClientEnterFamilyInstanceCreation";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientFamilyTownQuery (502, S→C) — réponse à la
/// recherche de villes de guilde (174). <b>17 vecteurs parallèles</b>
/// <c>[u16 n][elems]</c> — format prouvé par
/// <c>CNC_CZ_ClientFamilyTownQuery::CNC_ ctor @0x8148b90</c> +
/// Serialize (zone), miroir
/// <c>Eden.Core/Game/Family/FamilyPackets.FamilyTownQuery</c> :
/// <code>
///  0 i32 famId      1 s16 level       2 str name     3 str leader
///  4 u32 exp        5 s16 businessTax 6 i32 members  7 s16 houseOpen
///  8 i32 nb bâtiments/famille
///  9-16 par bâtiment (total sur toutes les familles) :
///     s16 loc | i32 itemTid | i32 tid | s16 itemType | s16 durability
///     | s16 level | u32 exp | u32 dueDate
/// </code>
/// La classe générée aplatissait le format — codec dédié.</summary>
public sealed class ZoneClientFamilyTownQuery : NetMessage
{
    public override int Opcode => 502;
    public override string Name => "CNC_CZ_ClientFamilyTownQuery";

    /// <summary>Bâtiment d'une ville (vecteurs 9-16).</summary>
    public readonly record struct Building(short Loc, int ItemTid,
        int Tid, short ItemType, short Durability, short Level,
        int Exp, int DueDate);

    /// <summary>Une ville de guilde (vecteurs 0-8).</summary>
    public sealed record Entry(int FamilyId, short Level,
        string Name, string Leader, int Exp, short BusinessTax,
        int Members, short HouseOpen, List<Building> Buildings);

    public List<Entry> Entries { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var ids = ReadVec(s, r => r.ReadInt());
        var levels = ReadVec(s, r => r.ReadShort());
        var names = ReadVec(s, r => r.ReadString());
        var leaders = ReadVec(s, r => r.ReadString());
        var exps = ReadVec(s, r => r.ReadInt());
        var taxes = ReadVec(s, r => r.ReadShort());
        var members = ReadVec(s, r => r.ReadInt());
        var opens = ReadVec(s, r => r.ReadShort());
        var bcounts = ReadVec(s, r => r.ReadInt());
        var locs = ReadVec(s, r => r.ReadShort());
        var itemTids = ReadVec(s, r => r.ReadInt());
        var tids = ReadVec(s, r => r.ReadInt());
        var types = ReadVec(s, r => r.ReadShort());
        var duras = ReadVec(s, r => r.ReadShort());
        var blevels = ReadVec(s, r => r.ReadShort());
        var bexps = ReadVec(s, r => r.ReadInt());
        var dues = ReadVec(s, r => r.ReadInt());
        var bi = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            var e = new Entry(ids[i],
                i < levels.Count ? levels[i] : (short)0,
                i < names.Count ? names[i] : "",
                i < leaders.Count ? leaders[i] : "",
                i < exps.Count ? exps[i] : 0,
                i < taxes.Count ? taxes[i] : (short)0,
                i < members.Count ? members[i] : 0,
                i < opens.Count ? opens[i] : (short)0,
                new List<Building>());
            var nb = i < bcounts.Count ? bcounts[i] : 0;
            for (var b = 0; b < nb && bi < locs.Count; b++, bi++)
                e.Buildings.Add(new Building(locs[bi],
                    itemTids[bi], tids[bi], types[bi], duras[bi],
                    blevels[bi], bexps[bi], dues[bi]));
            Entries.Add(e);
        }
    }

    private static List<T> ReadVec<T>(BinReader s,
        Func<BinReader, T> read)
    {
        var n = s.ReadUShort();
        var v = new List<T>(n);
        for (var i = 0; i < n; i++) v.Add(read(s));
        return v;
    }
}

/// <summary>CNC_CZ_ClientFamilyTownMemberQuery (503, S→C) — membres
/// d'une ville de famille (réponse 175). <b>6 vecteurs parallèles</b>
/// <c>[u16 n][elems]</c> — format prouvé par
/// <c>TC_FamilyTownMemberQuery</c> mission @0x820a670, miroir
/// <c>FamilyPackets.FamilyTownMemberQuery</c> :
/// <code>
/// 0 i32 charId  1 str name  2 s16 level  3 u8 rank
/// 4 s16 nodeId  5 str titres (niveau réponse, pas par membre)
/// </code>
/// La classe générée aplatissait le format — codec dédié.</summary>
public sealed class ZoneClientFamilyTownMemberQuery : NetMessage
{
    public override int Opcode => 503;
    public override string Name => "CNC_CZ_ClientFamilyTownMemberQuery";

    /// <summary>Un membre listé (vecteurs 0-4).</summary>
    public sealed record Entry(int CharacterId, string Name,
        short Level, byte Rank, short NodeId);

    public List<Entry> Entries { get; } = new();
    /// <summary>Titres de la famille (vecteur 5, niveau réponse).</summary>
    public List<string> Titles { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Titles.Clear();
        var ids = ReadVec(s, r => r.ReadInt());
        var names = ReadVec(s, r => r.ReadString());
        var levels = ReadVec(s, r => r.ReadShort());
        var ranks = ReadVec(s, r => r.ReadByte());
        var nodes = ReadVec(s, r => r.ReadShort());
        var titles = ReadVec(s, r => r.ReadString());
        for (var i = 0; i < ids.Count; i++)
            Entries.Add(new Entry(ids[i],
                i < names.Count ? names[i] : "",
                i < levels.Count ? levels[i] : (short)0,
                i < ranks.Count ? ranks[i] : (byte)0,
                i < nodes.Count ? nodes[i] : (short)0));
        Titles.AddRange(titles);
    }

    private static List<T> ReadVec<T>(BinReader s,
        Func<BinReader, T> read)
    {
        var n = s.ReadUShort();
        var v = new List<T>(n);
        for (var i = 0; i < n; i++) v.Add(read(s));
        return v;
    }
}

/// <summary>CNC_CZ_ClientUpdateLottery — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateLottery</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateLottery : NetMessage
{
    public override int Opcode => 511;
    public override string Name => "CNC_CZ_ClientUpdateLottery";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public ushort Field3 { get; set; }
    public ushort Field4 { get; set; }
    public uint Field5 { get; set; }
    public ushort Field6 { get; set; }
    public short Field7 { get; set; }
    public ushort Field8 { get; set; }
    public short Field9 { get; set; }
    public ushort Field10 { get; set; }
    public uint Field11 { get; set; }
    public ushort Field12 { get; set; }
    public short Field13 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort((short)Field3);
        s.WriteShort((short)Field4);
        s.WriteInt((int)Field5);
        s.WriteShort((short)Field6);
        s.WriteShort(Field7);
        s.WriteShort((short)Field8);
        s.WriteShort(Field9);
        s.WriteShort((short)Field10);
        s.WriteInt((int)Field11);
        s.WriteShort((short)Field12);
        s.WriteShort(Field13);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = (uint)s.ReadInt();
        Field6 = (ushort)s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = (ushort)s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = (ushort)s.ReadShort();
        Field11 = (uint)s.ReadInt();
        Field12 = (ushort)s.ReadShort();
        Field13 = s.ReadShort();
    }
}

/// <summary>NCID 512 CNC_CZ_ClientBuyBackList — file des items
/// rachetables (vendus au shop PNJ cette session). Wire SoA prouvé par
/// le codec serveur <c>BuybackPackets.BuildClientBuyBackList</c>
/// (Serialize natif zone @0x8344xxx + remplissage mission TC_*BuyBackList) :
/// <c>[u8 flag][u16 n][n×record][s16 terminal]</c>.
///   flag=3 « ouverture » (corps vide — le natif ne remplit pas) ;
///   flag=1 « contenu » — APPEND : le client ajoute chaque entrée à
///     sa file (jamais de clear — renvoyer la liste complète duplique) ;
///   flag=2 « retrait » — count=0, le s16 terminal = index racheté.
/// </summary>
public sealed class ZoneClientBuyBackList : NetMessage
{
    public override int Opcode => 512;
    public override string Name => "CNC_CZ_ClientBuyBackList";

    /// <summary>flag=1 — contenu à ajouter (append).</summary>
    public const byte ContentFlag = 1;
    /// <summary>flag=2 — retrait du row à l'index terminal.</summary>
    public const byte RemoveFlag = 2;
    /// <summary>flag=3 — ouverture vide (pas de remplissage).</summary>
    public const byte NativeFlag = 3;

    /// <summary>u8 — 1 contenu (append), 2 retrait (index terminal),
    /// 3 ouverture vide.</summary>
    public byte Flag { get; set; }
    /// <summary>s16 terminal — index du row racheté quand Flag==2.</summary>
    public short RemovedIndex { get; set; } = -1;
    /// <summary>Entrées rachetables décodées.</summary>
    public List<BuybackRecord> Entries { get; } = new();

    /// <summary>Un item de la file buyback — champs utiles à la fenêtre
    /// (le wire transporte tout l'SItemData SoA ; cf. codec serveur).</summary>
    public sealed record BuybackRecord(
        int ItemId, int Count, int Strengthen, long DueDate,
        uint Price, short[] EmbeddedIds, short Combo,
        short[] ExtraCombos, string Maker)
    {
        /// <summary>Slot synthétique pour les tooltips/icônes —
        /// pas de loc réel (l'item est chez le vendeur).</summary>
        public InventorySlot AsSlot(int index) => new(
            -1, index, ItemId, Count, 0, 0, Count, Strengthen)
        {
            Meta = new ItemMeta
            {
                Embedded = EmbeddedIds, Combo = Combo,
                ExtraCombos = ExtraCombos
                    .Select(x => (ushort)x).ToArray(),
                DueDate = DueDate,
            },
        };
    }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Flag);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries) WriteRecord(s, e);
        s.WriteShort(RemovedIndex);
    }

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        if (s.Remaining < 3) return;
        Flag = s.ReadByte();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
        {
            if (!TryReadRecord(s, out var e)) return;
            Entries.Add(e);
        }
        if (s.Remaining >= 2) RemovedIndex = s.ReadShort();
    }

    /// <summary>Record SoA du 512 — mêmes champs que l'item-record
    /// mais aplati (ordre prouvé par BuildClientBuyBackList serveur) :
    /// id, dur/count, identify, +0x34, bind, embedded_amount u8,
    /// 6×s16 embedded, witchcraft, str maker, 11×s16 combos,
    /// strengthen, maxdur, i64 due, 20B train, +0x54, u32 price,
    /// +0x6c, 2×s16 magicbox, 2×u16 energy/star.</summary>
    private static bool TryReadRecord(BinReader s, out BuybackRecord e)
    {
        e = new BuybackRecord(0, 0, 0, 0, 0,
            new short[6], 0, new short[11], "");
        if (s.Remaining < 97) return false;
        var itemId = s.ReadUShort();
        var count = s.ReadShort();              // durability/count
        s.ReadShort();                          // identify
        s.ReadShort();                          // +0x34
        s.ReadShort();                          // bind
        s.ReadByte();                           // embedded_amount
        var emb = new short[6];
        for (var i = 0; i < 6; i++) emb[i] = s.ReadShort();
        var combo = s.ReadShort();              // witchcraft/combo
        var maker = s.ReadString();
        var extras = new short[11];
        for (var i = 0; i < 11; i++) extras[i] = s.ReadShort();
        var strengthen = s.ReadShort();
        s.ReadShort();                          // cur_maxdurability
        var due = s.ReadLong();                 // i64 due_date
        s.ReadBytes(20);                        // CEquipTrainAttrs
        s.ReadShort();                          // +0x54
        var price = (uint)s.ReadInt();          // prix de rachat
        s.ReadShort();                          // +0x6c
        s.ReadShort(); s.ReadShort();           // magicbox 1-2
        s.ReadUShort(); s.ReadUShort();         // energy/star
        e = new BuybackRecord(itemId, count, strengthen, due, price,
            emb, combo, extras, maker);
        return true;
    }

    /// <summary>Ré-écrit un record — miroir de TryReadRecord (utile
    /// aux tests goldens).</summary>
    private static void WriteRecord(BinWriter s, BuybackRecord e)
    {
        s.WriteUShort((ushort)e.ItemId);
        s.WriteShort((short)e.Count);
        s.WriteShort(0); s.WriteShort(0); s.WriteShort(0);
        s.WriteByte(0);
        for (var i = 0; i < 6; i++)
            s.WriteShort(i < e.EmbeddedIds.Length ? e.EmbeddedIds[i] : (short)0);
        s.WriteShort(e.Combo);
        s.WriteString(e.Maker);
        for (var i = 0; i < 11; i++)
            s.WriteShort(i < e.ExtraCombos.Length ? e.ExtraCombos[i] : (short)0);
        s.WriteShort((short)e.Strengthen);
        s.WriteShort(0);
        s.WriteLong(e.DueDate);
        s.WriteBytes(new byte[20]);
        s.WriteShort(0);
        s.WriteInt((int)e.Price);
        s.WriteShort(0); s.WriteShort(0); s.WriteShort(0);
        s.WriteUShort(0); s.WriteUShort(0);
    }
}

/// <summary>CNC_CZ_ClientEnterFamilyTownList — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterFamilyTownList</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterFamilyTownList : NetMessage
{
    public override int Opcode => 514;
    public override string Name => "CNC_CZ_ClientEnterFamilyTownList";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientTerritoryQuery — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryQuery</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
/// <summary>CNC_CZ_ClientTerritoryQuery (515, S→C) — réponse à la
/// requête territoires 189. Format prouvé par
/// <c>FamilyPackets.TerritoryQuery</c> (miroir TC_TerritoryDataQuery
/// natif) : <c>[s16 pad=0]</c> puis <b>8 vecteurs parallèles</b> :
/// <code>
///   [u16 n][i32 territory_id ×n]
///   [u16 m][i32 sched_type][i32 begin][i32 end] ×m
///   [u16 n][i32 own_family_id ×n]
///   [u16 n][str name ×n]
///   [u16 n][s16 owner_level ×n]
///   [u16 n][u32 time ×n]
///   [u16 n][s16 state ×n]
///   [u16 n][u8 flag ×n]
/// </code>
/// La classe générée aplatissait le premier exemplaire de chaque
/// vecteur — codec dédié.</summary>
public sealed class ZoneClientTerritoryQuery : NetMessage
{
    public override int Opcode => 515;
    public override string Name => "CNC_CZ_ClientTerritoryQuery";

    /// <summary>Un territoire (ligne i des vecteurs parallèles).</summary>
    public readonly record struct Entry(int Id, int OwnFamilyId,
        string Name, short OwnerLevel, uint Time, short State,
        byte Flag);

    /// <summary>Un créneau de guerre (sched_type, fenêtre epoch).</summary>
    public readonly record struct Schedule(int Type, int Begin,
        int End);

    public List<Entry> Entries { get; } = new();
    public List<Schedule> Schedules { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Schedules.Clear();
        s.ReadShort();                                  // pad (natif : local non init → 0)
        var ids = new List<int>();
        var n = s.ReadUShort();                         // vec1 : ids
        for (var i = 0; i < n; i++) ids.Add(s.ReadInt());
        var m = s.ReadUShort();                         // vec2 : créneaux
        for (var i = 0; i < m; i++)
            Schedules.Add(new Schedule(s.ReadInt(), s.ReadInt(),
                s.ReadInt()));
        var own = new List<int>();
        n = s.ReadUShort();                             // vec3 : familles
        for (var i = 0; i < n; i++) own.Add(s.ReadInt());
        var names = new List<string>();
        n = s.ReadUShort();                             // vec4 : noms
        for (var i = 0; i < n; i++) names.Add(s.ReadString());
        var levels = new List<short>();
        n = s.ReadUShort();                             // vec5 : niveaux
        for (var i = 0; i < n; i++) levels.Add(s.ReadShort());
        var times = new List<uint>();
        n = s.ReadUShort();                             // vec6 : time
        for (var i = 0; i < n; i++)
            times.Add((uint)s.ReadInt());
        var states = new List<short>();
        n = s.ReadUShort();                             // vec7 : états
        for (var i = 0; i < n; i++) states.Add(s.ReadShort());
        var flags = new List<byte>();
        n = s.ReadUShort();                             // vec8 : flags
        for (var i = 0; i < n; i++) flags.Add(s.ReadByte());
        for (var i = 0; i < ids.Count; i++)
            Entries.Add(new Entry(ids[i],
                i < own.Count ? own[i] : 0,
                i < names.Count ? names[i] : "",
                i < levels.Count ? levels[i] : (short)0,
                i < times.Count ? times[i] : 0,
                i < states.Count ? states[i] : (short)0,
                i < flags.Count ? flags[i] : (byte)0));
    }
}

/// <summary>CNC_CZ_ClientTerritoryInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTerritoryInfo : NetMessage
{
    public override int Opcode => 516;
    public override string Name => "CNC_CZ_ClientTerritoryInfo";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public int Field3 { get; set; }
    public short Field4 { get; set; }
    public int Field5 { get; set; }
    public ushort Field6 { get; set; }
    public int Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
        s.WriteShort(Field4);
        s.WriteInt(Field5);
        s.WriteShort((short)Field6);
        s.WriteInt(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
        Field4 = s.ReadShort();
        Field5 = s.ReadInt();
        Field6 = (ushort)s.ReadShort();
        Field7 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientCanJoinTeam — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientCanJoinTeam</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientCanJoinTeam : NetMessage
{
    public override int Opcode => 517;
    public override string Name => "CNC_CZ_ClientCanJoinTeam";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientRollTreasure — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientRollTreasure</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientRollTreasure : NetMessage
{
    public override int Opcode => 520;
    public override string Name => "CNC_CZ_ClientRollTreasure";

    public uint Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public short Field7 { get; set; }
    public short Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public short Field12 { get; set; }
    public ushort Field13 { get; set; }
    public ushort Field14 { get; set; }
    public ushort Field15 { get; set; }
    public ushort Field16 { get; set; }
    public ushort Field17 { get; set; }
    public ushort Field18 { get; set; }
    public ushort Field19 { get; set; }
    public ushort Field20 { get; set; }
    public ushort Field21 { get; set; }
    public short Field22 { get; set; }
    public int Field23 { get; set; }
    public ushort Field24 { get; set; }
    public uint Field25 { get; set; }
    public ushort Field26 { get; set; }
    public ushort Field27 { get; set; }
    public ushort Field28 { get; set; }
    public ushort Field29 { get; set; }
    public ushort Field30 { get; set; }
    public ushort Field31 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteShort(Field12);
        s.WriteShort((short)Field13);
        s.WriteShort((short)Field14);
        s.WriteShort((short)Field15);
        s.WriteShort((short)Field16);
        s.WriteShort((short)Field17);
        s.WriteShort((short)Field18);
        s.WriteShort((short)Field19);
        s.WriteShort((short)Field20);
        s.WriteShort((short)Field21);
        s.WriteShort(Field22);
        s.WriteInt(Field23);
        s.WriteShort((short)Field24);
        s.WriteInt((int)Field25);
        s.WriteShort((short)Field26);
        s.WriteShort((short)Field27);
        s.WriteShort((short)Field28);
        s.WriteShort((short)Field29);
        s.WriteShort((short)Field30);
        s.WriteShort((short)Field31);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = (ushort)s.ReadShort();
        Field14 = (ushort)s.ReadShort();
        Field15 = (ushort)s.ReadShort();
        Field16 = (ushort)s.ReadShort();
        Field17 = (ushort)s.ReadShort();
        Field18 = (ushort)s.ReadShort();
        Field19 = (ushort)s.ReadShort();
        Field20 = (ushort)s.ReadShort();
        Field21 = (ushort)s.ReadShort();
        Field22 = s.ReadShort();
        Field23 = s.ReadInt();
        Field24 = (ushort)s.ReadShort();
        Field25 = (uint)s.ReadInt();
        Field26 = (ushort)s.ReadShort();
        Field27 = (ushort)s.ReadShort();
        Field28 = (ushort)s.ReadShort();
        Field29 = (ushort)s.ReadShort();
        Field30 = (ushort)s.ReadShort();
        Field31 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientRankInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientRankInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientRankInfo : NetMessage
{
    public override int Opcode => 521;
    public override string Name => "CNC_CZ_ClientRankInfo";

    public ushort Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public ushort Field3 { get; set; }
    public int Field4 { get; set; }
    public ushort Field5 { get; set; }
    public int Field6 { get; set; }
    public ushort Field7 { get; set; }
    public int Field8 { get; set; }
    public ushort Field9 { get; set; }
    public int Field10 { get; set; }
    public ushort Field11 { get; set; }
    public int Field12 { get; set; }
    public ushort Field13 { get; set; }
    public int Field14 { get; set; }
    public ushort Field15 { get; set; }
    public int Field16 { get; set; }
    public ushort Field17 { get; set; }
    public int Field18 { get; set; }
    public ushort Field19 { get; set; }
    public int Field20 { get; set; }
    public short Field21 { get; set; }
    public short Field22 { get; set; }
    public ushort Field23 { get; set; }
    public string Field24 { get; set; } = "";
    public ushort Field25 { get; set; }
    public ushort Field26 { get; set; }
    public string Field27 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteString(Field2);
        s.WriteShort((short)Field3);
        s.WriteInt(Field4);
        s.WriteShort((short)Field5);
        s.WriteInt(Field6);
        s.WriteShort((short)Field7);
        s.WriteInt(Field8);
        s.WriteShort((short)Field9);
        s.WriteInt(Field10);
        s.WriteShort((short)Field11);
        s.WriteInt(Field12);
        s.WriteShort((short)Field13);
        s.WriteInt(Field14);
        s.WriteShort((short)Field15);
        s.WriteInt(Field16);
        s.WriteShort((short)Field17);
        s.WriteInt(Field18);
        s.WriteShort((short)Field19);
        s.WriteInt(Field20);
        s.WriteShort(Field21);
        s.WriteShort(Field22);
        s.WriteShort((short)Field23);
        s.WriteString(Field24);
        s.WriteShort((short)Field25);
        s.WriteShort((short)Field26);
        s.WriteString(Field27);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadString();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadInt();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadInt();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadInt();
        Field9 = (ushort)s.ReadShort();
        Field10 = s.ReadInt();
        Field11 = (ushort)s.ReadShort();
        Field12 = s.ReadInt();
        Field13 = (ushort)s.ReadShort();
        Field14 = s.ReadInt();
        Field15 = (ushort)s.ReadShort();
        Field16 = s.ReadInt();
        Field17 = (ushort)s.ReadShort();
        Field18 = s.ReadInt();
        Field19 = (ushort)s.ReadShort();
        Field20 = s.ReadInt();
        Field21 = s.ReadShort();
        Field22 = s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = s.ReadString();
        Field25 = (ushort)s.ReadShort();
        Field26 = (ushort)s.ReadShort();
        Field27 = s.ReadString();
    }
}

/// <summary>CNC_CZ_ClientHateReport — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientHateReport</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientHateReport : NetMessage
{
    public override int Opcode => 522;
    public override string Name => "CNC_CZ_ClientHateReport";

    public uint Field1 { get; set; }
    public int Field2 { get; set; }
    public int Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEnterGladiatorTeamCreation — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterGladiatorTeamCreation</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterGladiatorTeamCreation : NetMessage
{
    public override int Opcode => 524;
    public override string Name => "CNC_CZ_ClientEnterGladiatorTeamCreation";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientGladiatorTeamInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientGladiatorTeamInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientGladiatorTeamInfo : NetMessage
{
    public override int Opcode => 525;
    public override string Name => "CNC_CZ_ClientGladiatorTeamInfo";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public uint Field3 { get; set; }
    public byte Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteInt((int)Field3);
        s.WriteByte(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = (uint)s.ReadInt();
        Field4 = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientDisbandGladiatorTeam — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDisbandGladiatorTeam</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDisbandGladiatorTeam : NetMessage
{
    public override int Opcode => 526;
    public override string Name => "CNC_CZ_ClientDisbandGladiatorTeam";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEnterColosseum — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientEnterColosseum</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientEnterColosseum : NetMessage
{
    public override int Opcode => 527;
    public override string Name => "CNC_CZ_ClientEnterColosseum";

    public byte Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientColosseumJoinResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientColosseumJoinResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientColosseumJoinResult : NetMessage
{
    public override int Opcode => 528;
    public override string Name => "CNC_CZ_ClientColosseumJoinResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientColosseumTeamInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientColosseumTeamInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientColosseumTeamInfo : NetMessage
{
    public override int Opcode => 529;
    public override string Name => "CNC_CZ_ClientColosseumTeamInfo";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public int Field5 { get; set; }
    public byte Field6 { get; set; }
    public short Field7 { get; set; }
    public byte Field8 { get; set; }
    public short Field9 { get; set; }
    public string Field10 { get; set; } = "";
    public int Field11 { get; set; }
    public ushort Field12 { get; set; }
    public byte Field13 { get; set; }
    public byte Field14 { get; set; }
    public int Field15 { get; set; }
    public int Field16 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteInt(Field5);
        s.WriteByte(Field6);
        s.WriteShort(Field7);
        s.WriteByte(Field8);
        s.WriteShort(Field9);
        s.WriteString(Field10);
        s.WriteInt(Field11);
        s.WriteShort((short)Field12);
        s.WriteByte(Field13);
        s.WriteByte(Field14);
        s.WriteInt(Field15);
        s.WriteInt(Field16);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadInt();
        Field6 = s.ReadByte();
        Field7 = s.ReadShort();
        Field8 = s.ReadByte();
        Field9 = s.ReadShort();
        Field10 = s.ReadString();
        Field11 = s.ReadInt();
        Field12 = (ushort)s.ReadShort();
        Field13 = s.ReadByte();
        Field14 = s.ReadByte();
        Field15 = s.ReadInt();
        Field16 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientColosseumLeaveJoinResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientColosseumLeaveJoinResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientColosseumLeaveJoinResult : NetMessage
{
    public override int Opcode => 530;
    public override string Name => "CNC_CZ_ClientColosseumLeaveJoinResult";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientTerritoryKilledInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryKilledInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTerritoryKilledInfo : NetMessage
{
    public override int Opcode => 532;
    public override string Name => "CNC_CZ_ClientTerritoryKilledInfo";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientCoRideInvite — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientCoRideInvite</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientCoRideInvite : NetMessage
{
    public override int Opcode => 533;
    public override string Name => "CNC_CZ_ClientCoRideInvite";

    public uint Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public string Field3 { get; set; } = "";
    public short Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteString(Field2);
        s.WriteString(Field3);
        s.WriteShort(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadString();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientUpdateRoom — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateRoom</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateRoom : NetMessage
{
    public override int Opcode => 538;
    public override string Name => "CNC_CZ_ClientUpdateRoom";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public int Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public short Field6 { get; set; }
    public int Field7 { get; set; }
    public int Field8 { get; set; }
    public int Field9 { get; set; }
    public short Field10 { get; set; }
    public int Field11 { get; set; }
    public int Field12 { get; set; }
    public int Field13 { get; set; }
    public int Field14 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteShort(Field6);
        s.WriteInt(Field7);
        s.WriteInt(Field8);
        s.WriteInt(Field9);
        s.WriteShort(Field10);
        s.WriteInt(Field11);
        s.WriteInt(Field12);
        s.WriteInt(Field13);
        s.WriteInt(Field14);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadShort();
        Field7 = s.ReadInt();
        Field8 = s.ReadInt();
        Field9 = s.ReadInt();
        Field10 = s.ReadShort();
        Field11 = s.ReadInt();
        Field12 = s.ReadInt();
        Field13 = s.ReadInt();
        Field14 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientOpenHotel — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenHotel</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenHotel : NetMessage
{
    public override int Opcode => 539;
    public override string Name => "CNC_CZ_ClientOpenHotel";

    public short Field1 { get; set; }
    public int Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientSetRoomResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSetRoomResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSetRoomResult : NetMessage
{
    public override int Opcode => 540;
    public override string Name => "CNC_CZ_ClientSetRoomResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientRoomData — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientRoomData</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientRoomData : NetMessage
{
    public override int Opcode => 541;
    public override string Name => "CNC_CZ_ClientRoomData";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public ushort Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public int Field6 { get; set; }
    public int Field7 { get; set; }
    public short Field8 { get; set; }
    public int Field9 { get; set; }
    public int Field10 { get; set; }
    public int Field11 { get; set; }
    public short Field12 { get; set; }
    public int Field13 { get; set; }
    public int Field14 { get; set; }
    public int Field15 { get; set; }
    public int Field16 { get; set; }
    public short Field17 { get; set; }
    public int Field18 { get; set; }
    public byte Field19 { get; set; }
    public short Field20 { get; set; }
    public byte Field21 { get; set; }
    public ushort Field22 { get; set; }
    public byte Field23 { get; set; }
    public ushort Field24 { get; set; }
    public short Field25 { get; set; }
    public short Field26 { get; set; }
    public string Field27 { get; set; } = "";
    public short Field28 { get; set; }
    public short Field29 { get; set; }
    public short Field30 { get; set; }
    public short Field31 { get; set; }
    public short Field32 { get; set; }
    public short Field33 { get; set; }
    public short Field34 { get; set; }
    public short Field35 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteShort((short)Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteInt(Field6);
        s.WriteInt(Field7);
        s.WriteShort(Field8);
        s.WriteInt(Field9);
        s.WriteInt(Field10);
        s.WriteInt(Field11);
        s.WriteShort(Field12);
        s.WriteInt(Field13);
        s.WriteInt(Field14);
        s.WriteInt(Field15);
        s.WriteInt(Field16);
        s.WriteShort(Field17);
        s.WriteInt(Field18);
        s.WriteByte(Field19);
        s.WriteShort(Field20);
        s.WriteByte(Field21);
        s.WriteShort((short)Field22);
        s.WriteByte(Field23);
        s.WriteShort((short)Field24);
        s.WriteShort(Field25);
        s.WriteShort(Field26);
        s.WriteString(Field27);
        s.WriteShort(Field28);
        s.WriteShort(Field29);
        s.WriteShort(Field30);
        s.WriteShort(Field31);
        s.WriteShort(Field32);
        s.WriteShort(Field33);
        s.WriteShort(Field34);
        s.WriteShort(Field35);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadInt();
        Field7 = s.ReadInt();
        Field8 = s.ReadShort();
        Field9 = s.ReadInt();
        Field10 = s.ReadInt();
        Field11 = s.ReadInt();
        Field12 = s.ReadShort();
        Field13 = s.ReadInt();
        Field14 = s.ReadInt();
        Field15 = s.ReadInt();
        Field16 = s.ReadInt();
        Field17 = s.ReadShort();
        Field18 = s.ReadInt();
        Field19 = s.ReadByte();
        Field20 = s.ReadShort();
        Field21 = s.ReadByte();
        Field22 = (ushort)s.ReadShort();
        Field23 = s.ReadByte();
        Field24 = (ushort)s.ReadShort();
        Field25 = s.ReadShort();
        Field26 = s.ReadShort();
        Field27 = s.ReadString();
        Field28 = s.ReadShort();
        Field29 = s.ReadShort();
        Field30 = s.ReadShort();
        Field31 = s.ReadShort();
        Field32 = s.ReadShort();
        Field33 = s.ReadShort();
        Field34 = s.ReadShort();
        Field35 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientUpdateRoomItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateRoomItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateRoomItem : NetMessage
{
    public override int Opcode => 542;
    public override string Name => "CNC_CZ_ClientUpdateRoomItem";

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

/// <summary>CNC_CZ_ClientLifeSkillUpdate — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLifeSkillUpdate</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLifeSkillUpdate : NetMessage
{
    public override int Opcode => 544;
    public override string Name => "CNC_CZ_ClientLifeSkillUpdate";

    public byte Field1 { get; set; }
    public ushort Field2 { get; set; }
    public uint Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort((short)Field2);
        s.WriteInt((int)Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = (ushort)s.ReadShort();
        Field3 = (uint)s.ReadInt();
    }
}

/// <summary>S→C NCID 545 CNC_CZ_ClientRideSpell — barre de sorts de
/// monture (CEC_RideSpell::Execute @0x8550a80, commande S_Enchant 2140).
/// Layout PROUVÉ par l'émission serveur (ZoneEntryFlow.Combat.Ride.cs,
/// capture live) : <c>[s16 n][n × s16 spellId]</c> ; n=0 = barre vidée
/// (descente, RideSpell::RollBack).</summary>
public sealed class ZoneClientRideSpell : NetMessage
{
    public override int Opcode => 545;
    public override string Name => "CNC_CZ_ClientRideSpell";

    /// <summary>Sorts de monture (vide = barre retirée).</summary>
    public List<ushort> SpellIds { get; } = new();

    /// <summary>Dernière barre reçue (lecture HUD / sonde).</summary>
    public static IReadOnlyList<ushort> Latest { get; private set; } =
        System.Array.Empty<ushort>();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)SpellIds.Count);            // s16 — n
        foreach (var id in SpellIds)
            s.WriteShort((short)id);                    // s16 — spellId
    }

    public override void Deserialize(BinReader s)
    {
        SpellIds.Clear();
        var n = (ushort)s.ReadShort();                  // s16 — n
        for (var i = 0; i < n && s.Remaining >= 2; i++)
            SpellIds.Add((ushort)s.ReadShort());        // s16 — spellId
        Latest = SpellIds.ToArray();
    }
}

/// <summary>CNC_CZ_ClientSpellFlyToLocation — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSpellFlyToLocation</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSpellFlyToLocation : NetMessage
{
    public override int Opcode => 546;
    public override string Name => "CNC_CZ_ClientSpellFlyToLocation";

    public uint Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }
    public int Field4 { get; set; }
    public ushort Field5 { get; set; }
    public float Field6 { get; set; }
    public ushort Field7 { get; set; }
    public float Field8 { get; set; }
    public ushort Field9 { get; set; }
    public float Field10 { get; set; }
    public ushort Field11 { get; set; }
    public float Field12 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteShort((short)Field5);
        s.WriteFloat(Field6);
        s.WriteShort((short)Field7);
        s.WriteFloat(Field8);
        s.WriteShort((short)Field9);
        s.WriteFloat(Field10);
        s.WriteShort((short)Field11);
        s.WriteFloat(Field12);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadFloat();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadFloat();
        Field9 = (ushort)s.ReadShort();
        Field10 = s.ReadFloat();
        Field11 = (ushort)s.ReadShort();
        Field12 = s.ReadFloat();
    }
}

/// <summary>CNC_CZ_ClientTerritoryEnd — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryEnd</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTerritoryEnd : NetMessage
{
    public override int Opcode => 547;
    public override string Name => "CNC_CZ_ClientTerritoryEnd";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientUpdateSpellAttr — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateSpellAttr</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateSpellAttr : NetMessage
{
    public override int Opcode => 548;
    public override string Name => "CNC_CZ_ClientUpdateSpellAttr";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateHealthOnlineTime — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateHealthOnlineTime</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateHealthOnlineTime : NetMessage
{
    public override int Opcode => 549;
    public override string Name => "CNC_CZ_ClientUpdateHealthOnlineTime";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientResetDailyMission — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientResetDailyMission</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientResetDailyMission : NetMessage
{
    public override int Opcode => 550;
    public override string Name => "CNC_CZ_ClientResetDailyMission";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientColosseumModeQueryResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientColosseumModeQueryResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientColosseumModeQueryResult : NetMessage
{
    public override int Opcode => 552;
    public override string Name => "CNC_CZ_ClientColosseumModeQueryResult";

    public short Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientRecruitInstanceResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientRecruitInstanceResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientRecruitInstanceResult : NetMessage
{
    public override int Opcode => 553;
    public override string Name => "CNC_CZ_ClientRecruitInstanceResult";

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

/// <summary>CNC_CZ_ClientUpdateLifelongPoint — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateLifelongPoint</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateLifelongPoint : NetMessage
{
    public override int Opcode => 554;
    public override string Name => "CNC_CZ_ClientUpdateLifelongPoint";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateLifelongGold — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateLifelongGold</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateLifelongGold : NetMessage
{
    public override int Opcode => 555;
    public override string Name => "CNC_CZ_ClientUpdateLifelongGold";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientChanceLuckyBagResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientChanceLuckyBagResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientChanceLuckyBagResult : NetMessage
{
    public override int Opcode => 556;
    public override string Name => "CNC_CZ_ClientChanceLuckyBagResult";

    public ushort Field1 { get; set; }
    public short Field2 { get; set; }
    public ushort Field3 { get; set; }
    public ushort Field4 { get; set; }
    public ushort Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort(Field2);
        s.WriteShort((short)Field3);
        s.WriteShort((short)Field4);
        s.WriteShort((short)Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientFamilyBattleInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientFamilyBattleInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientFamilyBattleInfo : NetMessage
{
    public override int Opcode => 561;
    public override string Name => "CNC_CZ_ClientFamilyBattleInfo";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public string Field3 { get; set; } = "";
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public short Field6 { get; set; }
    public int Field7 { get; set; }
    public short Field8 { get; set; }
    public int Field9 { get; set; }
    public int Field10 { get; set; }
    public short Field11 { get; set; }
    public int Field12 { get; set; }
    public int Field13 { get; set; }
    public short Field14 { get; set; }
    public int Field15 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteString(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteShort(Field6);
        s.WriteInt(Field7);
        s.WriteShort(Field8);
        s.WriteInt(Field9);
        s.WriteInt(Field10);
        s.WriteShort(Field11);
        s.WriteInt(Field12);
        s.WriteInt(Field13);
        s.WriteShort(Field14);
        s.WriteInt(Field15);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadString();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadShort();
        Field7 = s.ReadInt();
        Field8 = s.ReadShort();
        Field9 = s.ReadInt();
        Field10 = s.ReadInt();
        Field11 = s.ReadShort();
        Field12 = s.ReadInt();
        Field13 = s.ReadInt();
        Field14 = s.ReadShort();
        Field15 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientOpenInstanceDialog — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenInstanceDialog</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenInstanceDialog : NetMessage
{
    public override int Opcode => 564;
    public override string Name => "CNC_CZ_ClientOpenInstanceDialog";

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

/// <summary>CNC_CZ_ClientR25UpdateFamily — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientR25UpdateFamily</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientR25UpdateFamily : NetMessage
{
    public override int Opcode => 565;
    public override string Name => "CNC_CZ_ClientR25UpdateFamily";

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

/// <summary>CNC_CZ_ClientTerritoryFamilyScore — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryFamilyScore</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTerritoryFamilyScore : NetMessage
{
    public override int Opcode => 566;
    public override string Name => "CNC_CZ_ClientTerritoryFamilyScore";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public string Field3 { get; set; } = "";
    public ushort Field4 { get; set; }
    public int Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteString(Field3);
        s.WriteShort((short)Field4);
        s.WriteInt(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadString();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientLookElfPetEquip — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLookElfPetEquip</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLookElfPetEquip : NetMessage
{
    public override int Opcode => 569;
    public override string Name => "CNC_CZ_ClientLookElfPetEquip";

    public int Field1 { get; set; }
    public string Field2 { get; set; } = "";
    public short Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public int Field6 { get; set; }
    public float Field7 { get; set; }
    public float Field8 { get; set; }
    public ushort Field9 { get; set; }
    public short Field10 { get; set; }
    public ushort Field11 { get; set; }
    public short Field12 { get; set; }
    public ushort Field13 { get; set; }
    public short Field14 { get; set; }
    public ushort Field15 { get; set; }
    public short Field16 { get; set; }
    public ushort Field17 { get; set; }
    public short Field18 { get; set; }
    public int Field19 { get; set; }
    public ushort Field20 { get; set; }
    public short Field21 { get; set; }
    public short Field22 { get; set; }
    public ushort Field23 { get; set; }
    public ushort Field24 { get; set; }
    public byte Field25 { get; set; }
    public short Field26 { get; set; }
    public short Field27 { get; set; }
    public short Field28 { get; set; }
    public short Field29 { get; set; }
    public short Field30 { get; set; }
    public short Field31 { get; set; }
    public short Field32 { get; set; }
    public short Field33 { get; set; }
    public ushort Field34 { get; set; }
    public ushort Field35 { get; set; }
    public ushort Field36 { get; set; }
    public ushort Field37 { get; set; }
    public ushort Field38 { get; set; }
    public ushort Field39 { get; set; }
    public ushort Field40 { get; set; }
    public ushort Field41 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteString(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteInt(Field6);
        s.WriteFloat(Field7);
        s.WriteFloat(Field8);
        s.WriteShort((short)Field9);
        s.WriteShort(Field10);
        s.WriteShort((short)Field11);
        s.WriteShort(Field12);
        s.WriteShort((short)Field13);
        s.WriteShort(Field14);
        s.WriteShort((short)Field15);
        s.WriteShort(Field16);
        s.WriteShort((short)Field17);
        s.WriteShort(Field18);
        s.WriteInt(Field19);
        s.WriteShort((short)Field20);
        s.WriteShort(Field21);
        s.WriteShort(Field22);
        s.WriteShort((short)Field23);
        s.WriteShort((short)Field24);
        s.WriteByte(Field25);
        s.WriteShort(Field26);
        s.WriteShort(Field27);
        s.WriteShort(Field28);
        s.WriteShort(Field29);
        s.WriteShort(Field30);
        s.WriteShort(Field31);
        s.WriteShort(Field32);
        s.WriteShort(Field33);
        s.WriteShort((short)Field34);
        s.WriteShort((short)Field35);
        s.WriteShort((short)Field36);
        s.WriteShort((short)Field37);
        s.WriteShort((short)Field38);
        s.WriteShort((short)Field39);
        s.WriteShort((short)Field40);
        s.WriteShort((short)Field41);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadString();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadInt();
        Field7 = s.ReadFloat();
        Field8 = s.ReadFloat();
        Field9 = (ushort)s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = (ushort)s.ReadShort();
        Field12 = s.ReadShort();
        Field13 = (ushort)s.ReadShort();
        Field14 = s.ReadShort();
        Field15 = (ushort)s.ReadShort();
        Field16 = s.ReadShort();
        Field17 = (ushort)s.ReadShort();
        Field18 = s.ReadShort();
        Field19 = s.ReadInt();
        Field20 = (ushort)s.ReadShort();
        Field21 = s.ReadShort();
        Field22 = s.ReadShort();
        Field23 = (ushort)s.ReadShort();
        Field24 = (ushort)s.ReadShort();
        Field25 = s.ReadByte();
        Field26 = s.ReadShort();
        Field27 = s.ReadShort();
        Field28 = s.ReadShort();
        Field29 = s.ReadShort();
        Field30 = s.ReadShort();
        Field31 = s.ReadShort();
        Field32 = s.ReadShort();
        Field33 = s.ReadShort();
        Field34 = (ushort)s.ReadShort();
        Field35 = (ushort)s.ReadShort();
        Field36 = (ushort)s.ReadShort();
        Field37 = (ushort)s.ReadShort();
        Field38 = (ushort)s.ReadShort();
        Field39 = (ushort)s.ReadShort();
        Field40 = (ushort)s.ReadShort();
        Field41 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientGreedyIslandInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientGreedyIslandInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientGreedyIslandInfo : NetMessage
{
    public override int Opcode => 573;
    public override string Name => "CNC_CZ_ClientGreedyIslandInfo";

    public short Field1 { get; set; }
    public int Field2 { get; set; }
    public int Field3 { get; set; }
    public int Field4 { get; set; }
    public ushort Field5 { get; set; }
    public string Field6 { get; set; } = "";
    public ushort Field7 { get; set; }
    public int Field8 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteShort((short)Field5);
        s.WriteString(Field6);
        s.WriteShort((short)Field7);
        s.WriteInt(Field8);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadString();
        Field7 = (ushort)s.ReadShort();
        Field8 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateWarCoin — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateWarCoin</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateWarCoin : NetMessage
{
    public override int Opcode => 576;
    public override string Name => "CNC_CZ_ClientUpdateWarCoin";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientTeamCheckNotify — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTeamCheckNotify</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTeamCheckNotify : NetMessage
{
    public override int Opcode => 577;
    public override string Name => "CNC_CZ_ClientTeamCheckNotify";

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

/// <summary>CNC_CZ_ClientUpdateFloorInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateFloorInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateFloorInfo : NetMessage
{
    public override int Opcode => 578;
    public override string Name => "CNC_CZ_ClientUpdateFloorInfo";

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

/// <summary>CNC_CZ_ClientOpenAreaDialog — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenAreaDialog</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenAreaDialog : NetMessage
{
    public override int Opcode => 579;
    public override string Name => "CNC_CZ_ClientOpenAreaDialog";

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

/// <summary>CNC_CZ_ClientUpdateAlpacaCoin — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateAlpacaCoin</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateAlpacaCoin : NetMessage
{
    public override int Opcode => 580;
    public override string Name => "CNC_CZ_ClientUpdateAlpacaCoin";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientLifeFloat — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientLifeFloat</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientLifeFloat : NetMessage
{
    public override int Opcode => 581;
    public override string Name => "CNC_CZ_ClientLifeFloat";

    public int Field1 { get; set; }
    public float Field2 { get; set; }
    public float Field3 { get; set; }
    public float Field4 { get; set; }
    public short Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteFloat(Field2);
        s.WriteFloat(Field3);
        s.WriteFloat(Field4);
        s.WriteShort(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadFloat();
        Field3 = s.ReadFloat();
        Field4 = s.ReadFloat();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientCheckTeamTransport — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientCheckTeamTransport</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientCheckTeamTransport : NetMessage
{
    public override int Opcode => 584;
    public override string Name => "CNC_CZ_ClientCheckTeamTransport";

    public byte Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNC_CZ_ClientOpenMissionShop — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenMissionShop</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenMissionShop : NetMessage
{
    public override int Opcode => 585;
    public override string Name => "CNC_CZ_ClientOpenMissionShop";

    public int Field1 { get; set; }
    public short Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientTerritoryPlayerInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientTerritoryPlayerInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientTerritoryPlayerInfo : NetMessage
{
    public override int Opcode => 586;
    public override string Name => "CNC_CZ_ClientTerritoryPlayerInfo";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientColosseumQuery — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientColosseumQuery</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientColosseumQuery : NetMessage
{
    public override int Opcode => 587;
    public override string Name => "CNC_CZ_ClientColosseumQuery";

    public short Field1 { get; set; }
    public ushort Field2 { get; set; }
    public int Field3 { get; set; }
    public ushort Field4 { get; set; }
    public int Field5 { get; set; }
    public ushort Field6 { get; set; }
    public int Field7 { get; set; }
    public ushort Field8 { get; set; }
    public uint Field9 { get; set; }
    public ushort Field10 { get; set; }
    public byte Field11 { get; set; }
    public short Field12 { get; set; }
    public ushort Field13 { get; set; }
    public short Field14 { get; set; }
    public ushort Field15 { get; set; }
    public short Field16 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteInt(Field3);
        s.WriteShort((short)Field4);
        s.WriteInt(Field5);
        s.WriteShort((short)Field6);
        s.WriteInt(Field7);
        s.WriteShort((short)Field8);
        s.WriteInt((int)Field9);
        s.WriteShort((short)Field10);
        s.WriteByte(Field11);
        s.WriteShort(Field12);
        s.WriteShort((short)Field13);
        s.WriteShort(Field14);
        s.WriteShort((short)Field15);
        s.WriteShort(Field16);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadInt();
        Field6 = (ushort)s.ReadShort();
        Field7 = s.ReadInt();
        Field8 = (ushort)s.ReadShort();
        Field9 = (uint)s.ReadInt();
        Field10 = (ushort)s.ReadShort();
        Field11 = s.ReadByte();
        Field12 = s.ReadShort();
        Field13 = (ushort)s.ReadShort();
        Field14 = s.ReadShort();
        Field15 = (ushort)s.ReadShort();
        Field16 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientBattleRoyaleInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBattleRoyaleInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBattleRoyaleInfo : NetMessage
{
    public override int Opcode => 591;
    public override string Name => "CNC_CZ_ClientBattleRoyaleInfo";

    public int Field1 { get; set; }
    public ushort Field2 { get; set; }
    public int Field3 { get; set; }
    public ushort Field4 { get; set; }
    public string Field5 { get; set; } = "";
    public ushort Field6 { get; set; }
    public int Field7 { get; set; }
    public ushort Field8 { get; set; }
    public int Field9 { get; set; }
    public int Field10 { get; set; }
    public int Field11 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
        s.WriteInt(Field3);
        s.WriteShort((short)Field4);
        s.WriteString(Field5);
        s.WriteShort((short)Field6);
        s.WriteInt(Field7);
        s.WriteShort((short)Field8);
        s.WriteInt(Field9);
        s.WriteInt(Field10);
        s.WriteInt(Field11);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadString();
        Field6 = (ushort)s.ReadShort();
        Field7 = s.ReadInt();
        Field8 = (ushort)s.ReadShort();
        Field9 = s.ReadInt();
        Field10 = s.ReadInt();
        Field11 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateFastDressData — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateFastDressData</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateFastDressData : NetMessage
{
    public override int Opcode => 594;
    public override string Name => "CNC_CZ_ClientUpdateFastDressData";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }
    public int Field4 { get; set; }
    public int Field5 { get; set; }
    public int Field6 { get; set; }
    public int Field7 { get; set; }
    public int Field8 { get; set; }
    public int Field9 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteInt(Field6);
        s.WriteInt(Field7);
        s.WriteInt(Field8);
        s.WriteInt(Field9);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Field6 = s.ReadInt();
        Field7 = s.ReadInt();
        Field8 = s.ReadInt();
        Field9 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientInGameLotteryNotify — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientInGameLotteryNotify</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientInGameLotteryNotify : NetMessage
{
    public override int Opcode => 595;
    public override string Name => "CNC_CZ_ClientInGameLotteryNotify";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientDiamondLotteryNotify — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientDiamondLotteryNotify</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientDiamondLotteryNotify : NetMessage
{
    public override int Opcode => 596;
    public override string Name => "CNC_CZ_ClientDiamondLotteryNotify";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientOpenChurchDialog — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenChurchDialog</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenChurchDialog : NetMessage
{
    public override int Opcode => 598;
    public override string Name => "CNC_CZ_ClientOpenChurchDialog";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientVersusInstanceInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientVersusInstanceInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientVersusInstanceInfo : NetMessage
{
    public override int Opcode => 601;
    public override string Name => "CNC_CZ_ClientVersusInstanceInfo";

    public int Field1 { get; set; }
    public int Field2 { get; set; }
    public int Field3 { get; set; }
    public int Field4 { get; set; }
    public ushort Field5 { get; set; }
    public int Field6 { get; set; }
    public int Field7 { get; set; }
    public ushort Field8 { get; set; }
    public int Field9 { get; set; }
    public ushort Field10 { get; set; }
    public int Field11 { get; set; }
    public ushort Field12 { get; set; }
    public string Field13 { get; set; } = "";
    public ushort Field14 { get; set; }
    public int Field15 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteShort((short)Field5);
        s.WriteInt(Field6);
        s.WriteInt(Field7);
        s.WriteShort((short)Field8);
        s.WriteInt(Field9);
        s.WriteShort((short)Field10);
        s.WriteInt(Field11);
        s.WriteShort((short)Field12);
        s.WriteString(Field13);
        s.WriteShort((short)Field14);
        s.WriteInt(Field15);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadInt();
        Field7 = s.ReadInt();
        Field8 = (ushort)s.ReadShort();
        Field9 = s.ReadInt();
        Field10 = (ushort)s.ReadShort();
        Field11 = s.ReadInt();
        Field12 = (ushort)s.ReadShort();
        Field13 = s.ReadString();
        Field14 = (ushort)s.ReadShort();
        Field15 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientOpenPaySystem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenPaySystem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenPaySystem : NetMessage
{
    public override int Opcode => 602;
    public override string Name => "CNC_CZ_ClientOpenPaySystem";

    public int Field1 { get; set; }
    public ushort Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = (ushort)s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientPayEvent — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPayEvent</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPayEvent : NetMessage
{
    public override int Opcode => 603;
    public override string Name => "CNC_CZ_ClientPayEvent";

    public short Field1 { get; set; }
    public int Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientPayEventOK — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPayEventOK</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPayEventOK : NetMessage
{
    public override int Opcode => 604;
    public override string Name => "CNC_CZ_ClientPayEventOK";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientSlotFlicker — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientSlotFlicker</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientSlotFlicker : NetMessage
{
    public override int Opcode => 605;
    public override string Name => "CNC_CZ_ClientSlotFlicker";

    public int Value { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);
    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateAttrNewHp — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateAttrNewHp</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateAttrNewHp : NetMessage
{
    public override int Opcode => 606;
    public override string Name => "CNC_CZ_ClientUpdateAttrNewHp";

    public int Field1 { get; set; }
    public short Field2 { get; set; }
    public int Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientEvaluateInfo — 607 : corps sous-typé
/// (Serialize natif zone @0x080846b0 — cf. EvaluatePackets serveur ;
/// la classe générée aplatie lisait 33 champs et jetait sur les corps
/// réels 12-96 octets) :
/// type 0 statut <c>[0][status][sec]</c> ; type 1 compteurs
/// <c>[1][raw][deaths][mon][elite][boss][maxDeaths][maxMon][maxElite]
/// [maxBoss]</c> ; type 2 résultat <c>[2][elapsed][timeBonus][deaths]
/// [mon][elite][boss][max×4][dieBonus][monScore][eliteScore]
/// [bossScore][raw][total][str grade][opens][buys][cost]</c> ;
/// type 3 loterie <c>[3][opens][itemId][count][index]</c>.</summary>
public sealed class ZoneClientEvaluateInfo : NetMessage
{
    public override int Opcode => 607;
    public override string Name => "CNC_CZ_ClientEvaluateInfo";

    /// <summary>Sous-type du corps (0 statut, 1 compteurs,
    /// 2 résultat, 3 loterie).</summary>
    public int SubType { get; set; }
    /// <summary>Entiers après le sous-type, dans l'ordre du fil.</summary>
    public int[] Values { get; set; } = System.Array.Empty<int>();
    /// <summary>Grade — type 2 uniquement.</summary>
    public string Grade { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(SubType);
        if (SubType == 2)
        {
            for (var i = 0; i < System.Math.Min(Values.Length, 15); i++)
                s.WriteInt(Values[i]);
            for (var i = Values.Length; i < 15; i++) s.WriteInt(0);
            s.WriteString(Grade);
            for (var i = 15; i < Values.Length; i++)
                s.WriteInt(Values[i]);
            return;
        }
        foreach (var v in Values) s.WriteInt(v);
    }

    public override void Deserialize(BinReader s)
    {
        SubType = s.ReadInt();
        var ints = new System.Collections.Generic.List<int>();
        switch (SubType)
        {
            case 0: for (var i = 0; i < 2; i++) ints.Add(I(s)); break;
            case 1: for (var i = 0; i < 9; i++) ints.Add(I(s)); break;
            case 2:
                for (var i = 0; i < 15; i++) ints.Add(I(s));
                if (s.Remaining > 0) Grade = s.ReadString();
                for (var i = 0; i < 3; i++) ints.Add(I(s));
                break;
            case 3: for (var i = 0; i < 4; i++) ints.Add(I(s)); break;
            default:
                while (s.Remaining >= 4) ints.Add(s.ReadInt());
                break;
        }
        Values = ints.ToArray();
        static int I(BinReader r) => r.Remaining >= 4 ? r.ReadInt() : 0;
    }
}

/// <summary>CNC_CZ_ClientUpdateEvaluateInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateEvaluateInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateEvaluateInfo : NetMessage
{
    public override int Opcode => 608;
    public override string Name => "CNC_CZ_ClientUpdateEvaluateInfo";

    public uint Field1 { get; set; }
    public uint Field2 { get; set; }
    public uint Field3 { get; set; }
    public string Field4 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteInt((int)Field2);
        s.WriteInt((int)Field3);
        s.WriteString(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = (uint)s.ReadInt();
        Field3 = (uint)s.ReadInt();
        Field4 = s.ReadString();
    }
}

/// <summary>CNC_CZ_ClientUIControlNotify — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUIControlNotify</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUIControlNotify : NetMessage
{
    public override int Opcode => 610;
    public override string Name => "CNC_CZ_ClientUIControlNotify";

    public short Field1 { get; set; }
    public int Field2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientChangeSpell — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientChangeSpell</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientChangeSpell : NetMessage
{
    public override int Opcode => 611;
    public override string Name => "CNC_CZ_ClientChangeSpell";

    public ushort Field1 { get; set; }
    public ushort Field2 { get; set; }
    public ushort Field3 { get; set; }
    public ushort Field4 { get; set; }
    public ushort Field5 { get; set; }
    public short Field6 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
        s.WriteShort((short)Field3);
        s.WriteShort((short)Field4);
        s.WriteShort((short)Field5);
        s.WriteShort(Field6);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = (ushort)s.ReadShort();
        Field6 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientOpenMagicBoxDialog — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenMagicBoxDialog</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenMagicBoxDialog : NetMessage
{
    public override int Opcode => 612;
    public override string Name => "CNC_CZ_ClientOpenMagicBoxDialog";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientMagicBoxResult (613, S→C) — résultat
/// d'une opération boîte magique (257). Format prouvé par
/// <c>MagicBoxPackets.BuildMagicBoxResult</c> (Serialize natif
/// @0x80cxxxx) :
/// <code>[s16 type][u16 n][s16 combos ×n][u16 energy][u16 star]
/// [u16 n][s16 sacs consommés ×n][u16 n][s16 slots consommés ×n]
/// [s16 mall_bag][s16 mall_slot]</code>
/// La classe générée aplatissait les vecteurs — codec dédié.</summary>
public sealed class ZoneClientMagicBoxResult : NetMessage
{
    public override int Opcode => 613;
    public override string Name => "CNC_CZ_ClientMagicBoxResult";

    /// <summary>s16 — type d'op (1 conversion, 2 scouring).</summary>
    public short Type { get; private set; }
    /// <summary>Combos magicbox_enchant1-3 courants de l'item.</summary>
    public List<short> Combos { get; } = new();
    /// <summary>u16 — énergie courante.</summary>
    public ushort Energy { get; private set; }
    /// <summary>u16 — niveau d'étoile.</summary>
    public ushort StarLevel { get; private set; }
    /// <summary>Positions (bag,slot) des sacrifices consommés.</summary>
    public List<(short Bag, short Slot)> Consumed { get; } = new();
    /// <summary>Position du catalyseur mall consommé (−1 sinon).</summary>
    public short MallBag { get; private set; }
    public short MallSlot { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadShort();
        Combos.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++) Combos.Add(s.ReadShort());
        Energy = s.ReadUShort();
        StarLevel = s.ReadUShort();
        Consumed.Clear();
        var bags = new short[s.ReadUShort()];
        for (var i = 0; i < bags.Length; i++) bags[i] = s.ReadShort();
        var slots = new short[s.ReadUShort()];
        for (var i = 0; i < slots.Length; i++) slots[i] = s.ReadShort();
        for (var i = 0; i < Math.Min(bags.Length, slots.Length); i++)
            Consumed.Add((bags[i], slots[i]));
        MallBag = s.ReadShort();
        MallSlot = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientBackItem — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientBackItem</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientBackItem : NetMessage
{
    public override int Opcode => 614;
    public override string Name => "CNC_CZ_ClientBackItem";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientUpdateBattleCrusadeInfo — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientUpdateBattleCrusadeInfo</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientUpdateBattleCrusadeInfo : NetMessage
{
    public override int Opcode => 616;
    public override string Name => "CNC_CZ_ClientUpdateBattleCrusadeInfo";


    public override void Serialize(BinWriter s)
    {
        // Aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // Aucun champ
    }
}

/// <summary>CNC_CZ_ClientOpenBattleCrusadeDialog — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientOpenBattleCrusadeDialog</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientOpenBattleCrusadeDialog : NetMessage
{
    public override int Opcode => 617;
    public override string Name => "CNC_CZ_ClientOpenBattleCrusadeDialog";

    public int Field1 { get; set; }
    public ushort Field2 { get; set; }
    public int Field3 { get; set; }
    public ushort Field4 { get; set; }
    public int Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
        s.WriteInt(Field3);
        s.WriteShort((short)Field4);
        s.WriteInt(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientPetLotteryResult — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPetLotteryResult</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPetLotteryResult : NetMessage
{
    public override int Opcode => 618;
    public override string Name => "CNC_CZ_ClientPetLotteryResult";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public ushort Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public int Field7 { get; set; }
    public uint Field8 { get; set; }
    public short Field9 { get; set; }
    public short Field10 { get; set; }
    public short Field11 { get; set; }
    public byte Field12 { get; set; }
    public byte Field13 { get; set; }
    public ushort Field14 { get; set; }
    public ushort Field15 { get; set; }
    public short Field16 { get; set; }
    public int Field17 { get; set; }
    public short Field18 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort((short)Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteInt(Field7);
        s.WriteInt((int)Field8);
        s.WriteShort(Field9);
        s.WriteShort(Field10);
        s.WriteShort(Field11);
        s.WriteByte(Field12);
        s.WriteByte(Field13);
        s.WriteShort((short)Field14);
        s.WriteShort((short)Field15);
        s.WriteShort(Field16);
        s.WriteInt(Field17);
        s.WriteShort(Field18);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = (ushort)s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadInt();
        Field8 = (uint)s.ReadInt();
        Field9 = s.ReadShort();
        Field10 = s.ReadShort();
        Field11 = s.ReadShort();
        Field12 = s.ReadByte();
        Field13 = s.ReadByte();
        Field14 = (ushort)s.ReadShort();
        Field15 = (ushort)s.ReadShort();
        Field16 = s.ReadShort();
        Field17 = s.ReadInt();
        Field18 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientPetLotteryEnd — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientPetLotteryEnd</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientPetLotteryEnd : NetMessage
{
    public override int Opcode => 619;
    public override string Name => "CNC_CZ_ClientPetLotteryEnd";

    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public byte Field5 { get; set; }
    public int Field6 { get; set; }
    public short Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteByte(Field5);
        s.WriteInt(Field6);
        s.WriteShort(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadByte();
        Field6 = s.ReadInt();
        Field7 = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientArrangeItemOK — layout porté verbatim du codec serveur
/// <c>CNC_CZ_ClientArrangeItemOK</c> (Messages/Generated/*.g.cs). Champs
/// positionnels <c>FieldN</c> tant que le rôle n'est pas prouvé.</summary>
public sealed class ZoneClientArrangeItemOK : NetMessage
{
    public override int Opcode => 620;
    public override string Name => "CNC_CZ_ClientArrangeItemOK";

    public uint Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public short Field5 { get; set; }
    public short Field6 { get; set; }
    public byte Field7 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteByte(Field7);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadByte();
    }
}
