using System.Collections.Generic;
using EdenClient.Net;

namespace EdenClient.Protocol;

// Senders C→S « complétude » générés depuis les codecs entrants
// serveur CNE_CZ (layouts prouvés natifs) — Serialize émet le même
// ordre de champs que le Deserialize serveur.

/// <summary>CNE_CZ_ZoneServerPlayerEmotion — 21 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerPlayerEmotion : NetMessage
{
    public override int Opcode => 21;
    public override string Name => "CNE_CZ_ZoneServerPlayerEmotion";

    public byte EmotionId  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(EmotionId);    }

    public override void Deserialize(BinReader s)
    {
        EmotionId = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerSafeQuitReq — 27 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSafeQuitReq : NetMessage
{
    public override int Opcode => 27;
    public override string Name => "CNE_CZ_ZoneServerSafeQuitReq";

    public uint CharId  { get; set; }
    public byte Reason  { get; set; }
    public int QuitType  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteByte(Reason);
        s.WriteInt(QuitType);    }

    public override void Deserialize(BinReader s)
    {
        CharId = (uint)s.ReadInt();
        Reason = s.ReadByte();
        QuitType = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerSafeQuitCountDownOver — 33 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSafeQuitCountDownOver : NetMessage
{
    public override int Opcode => 33;
    public override string Name => "CNE_CZ_ZoneServerSafeQuitCountDownOver";

    public byte Flags  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Flags);    }

    public override void Deserialize(BinReader s)
    {
        Flags = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerResurrect — 44 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerResurrect : NetMessage
{
    public override int Opcode => 44;
    public override string Name => "CNE_CZ_ZoneServerResurrect";

    public byte Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerAskRepairEquipGold — 45 : demande du
/// coût « Repair All » (corps vide — serveur
/// <c>Handlers/InventoryHandlers.cs:214</c> → répond 342).</summary>
public sealed class ZoneServerAskRepairEquipGold : NetMessage
{
    public override int Opcode => 45;
    public override string Name => "CNE_CZ_ZoneServerAskRepairEquipGold";

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>CNE_CZ_ZoneServerRepairEquipment — 46 : confirme la
/// réparation de tout l'équipement porté (corps vide).</summary>
public sealed class ZoneServerRepairEquipment : NetMessage
{
    public override int Opcode => 46;
    public override string Name => "CNE_CZ_ZoneServerRepairEquipment";

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>CNE_CZ_ZoneServerAskRepairOneItemGold — 47 : demande du
/// coût pour UN item [u8 bag][u8 ?][i16 slot] → répond 343 (Field3
/// = coût calculé serveur).</summary>
public sealed class ZoneServerAskRepairOneItemGold : NetMessage
{
    public override int Opcode => 47;
    public override string Name => "CNE_CZ_ZoneServerAskRepairOneItemGold";

    public byte Field1 { get; set; }
    public byte Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
        s.WriteShort(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerRepairOneItem — 48 : confirme la
/// réparation d'un item [u8 bag][u8 ?][i16 slot].</summary>
public sealed class ZoneServerRepairOneItem : NetMessage
{
    public override int Opcode => 48;
    public override string Name => "CNE_CZ_ZoneServerRepairOneItem";

    public byte Field1 { get; set; }
    public byte Field2 { get; set; }
    public short Field3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
        s.WriteShort(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerGetShortcutOfPage — 55 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerGetShortcutOfPage : NetMessage
{
    public override int Opcode => 55;
    public override string Name => "CNE_CZ_ZoneServerGetShortcutOfPage";

    public short Field1  { get; set; }
    public byte Field2  { get; set; }
    public short Field3  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteByte(Field2);
        s.WriteShort(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadByte();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerExchangeItemPin — 66 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerExchangeItemPin : NetMessage
{
    public override int Opcode => 66;
    public override string Name => "CNE_CZ_ZoneServerExchangeItemPin";

    public string Field1 { get; set; } = "";
    public string Field2 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteString(Field1);
        s.WriteString(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadString();
        Field2 = s.ReadString();
    }
}

/// <summary>CNE_CZ_ZoneServerEnterNodeElfInfo — 110 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerEnterNodeElfInfo : NetMessage
{
    public override int Opcode => 110;
    public override string Name => "CNE_CZ_ZoneServerEnterNodeElfInfo";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerStrengthenItem — 119 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerStrengthenItem : NetMessage
{
    public override int Opcode => 119;
    public override string Name => "CNE_CZ_ZoneServerStrengthenItem";

    public short Field1  { get; set; }
    public int Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }
    public short Field5  { get; set; }
    public short Field6  { get; set; }
    public short Field7  { get; set; }
    public short Field8  { get; set; }
    public short Field9  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);
        s.WriteShort(Field8);
        s.WriteShort(Field9);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
        Field8 = s.ReadShort();
        Field9 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerStrengthenClose — 120 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerStrengthenClose : NetMessage
{
    public override int Opcode => 120;
    public override string Name => "CNE_CZ_ZoneServerStrengthenClose";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerGoLottery — 129 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerGoLottery : NetMessage
{
    public override int Opcode => 129;
    public override string Name => "CNE_CZ_ZoneServerGoLottery";

    public short Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }
    public short Field5  { get; set; }
    public uint Field6  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);
        s.WriteInt((int)Field6);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = (uint)s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyContainerPutItem — 130 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyContainerPutItem : NetMessage
{
    public override int Opcode => 130;
    public override string Name => "CNE_CZ_ZoneServerFamilyContainerPutItem";

    public short Field1  { get; set; }
    public short Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }
    public short Field5  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
        s.WriteShort(Field5);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyContainerGetItem — 131 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyContainerGetItem : NetMessage
{
    public override int Opcode => 131;
    public override string Name => "CNE_CZ_ZoneServerFamilyContainerGetItem";

    public short Field1  { get; set; }
    public short Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyInstanceGetData — 132 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyInstanceGetData : NetMessage
{
    public override int Opcode => 132;
    public override string Name => "CNE_CZ_ZoneServerFamilyInstanceGetData";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerLookEquip — 143 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerLookEquip : NetMessage
{
    public override int Opcode => 143;
    public override string Name => "CNE_CZ_ZoneServerLookEquip";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerSetAAPoint — 148 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSetAAPoint : NetMessage
{
    public override int Opcode => 148;
    public override string Name => "CNE_CZ_ZoneServerSetAAPoint";

    public short Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerSetAARate — 149 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSetAARate : NetMessage
{
    public override int Opcode => 149;
    public override string Name => "CNE_CZ_ZoneServerSetAARate";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerSaveCollection — 150 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSaveCollection : NetMessage
{
    public override int Opcode => 150;
    public override string Name => "CNE_CZ_ZoneServerSaveCollection";

    public byte Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }
    public ushort Field4  { get; set; }
    public short Field5  { get; set; }
    public short Field6  { get; set; }
    public short Field7  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort((short)Field4);
        s.WriteShort(Field5);
        s.WriteShort(Field6);
        s.WriteShort(Field7);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = (ushort)s.ReadShort();
        Field5 = s.ReadShort();
        Field6 = s.ReadShort();
        Field7 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerGetCollection — 151 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerGetCollection : NetMessage
{
    public override int Opcode => 151;
    public override string Name => "CNE_CZ_ZoneServerGetCollection";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerQueryCollection — 152 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryCollection : NetMessage
{
    public override int Opcode => 152;
    public override string Name => "CNE_CZ_ZoneServerQueryCollection";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerMissionGoto — 160 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerMissionGoto : NetMessage
{
    public override int Opcode => 160;
    public override string Name => "CNE_CZ_ZoneServerMissionGoto";

    public short Field1  { get; set; }
    public short Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyStatue — 161 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyStatue : NetMessage
{
    public override int Opcode => 161;
    public override string Name => "CNE_CZ_ZoneServerFamilyStatue";

    public int Field1  { get; set; }
    public int Field2  { get; set; }
    public string Field3 { get; set; } = "";
    public int Field4  { get; set; }
    public float Field5  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteString(Field3);
        s.WriteInt(Field4);
        s.WriteFloat(Field5);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadString();
        Field4 = s.ReadInt();
        Field5 = s.ReadFloat();
    }
}

/// <summary>CNE_CZ_ZoneServerQueryBattlefieldData — 162 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryBattlefieldData : NetMessage
{
    public override int Opcode => 162;
    public override string Name => "CNE_CZ_ZoneServerQueryBattlefieldData";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerNodeGroupReset — 163 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerNodeGroupReset : NetMessage
{
    public override int Opcode => 163;
    public override string Name => "CNE_CZ_ZoneServerNodeGroupReset";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerTeamOperation — 79 `[s16 op][s16 p1][s16 p2]`
/// : op 0 = règle de loot (p1 : 1 Free/2 Random/3 Taunt-Exclusive),
/// op 3 = règle de roll (p1 : seuil qualité 0 White/1 Green/2 Blue) —
/// leader only, côté serveur `ChageLootRule`/`ChageRollRule`
/// (TeamHandlers.cs, désasm zone:532135).</summary>
public sealed class ZoneServerTeamOperation : NetMessage
{
    public override int Opcode => 79;
    public override string Name => "CNE_CZ_ZoneServerTeamOperation";

    public short Op { get; set; }
    public short Param1 { get; set; }
    public short Param2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Op); s.WriteShort(Param1); s.WriteShort(Param2);
    }

    public override void Deserialize(BinReader s)
    {
        Op = s.ReadShort(); Param1 = s.ReadShort(); Param2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerQueryClosets — 165 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryClosets : NetMessage
{
    public override int Opcode => 165;
    public override string Name => "CNE_CZ_ZoneServerQueryClosets";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerQueryClassList — 166 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryClassList : NetMessage
{
    public override int Opcode => 166;
    public override string Name => "CNE_CZ_ZoneServerQueryClassList";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerClosetSet — 167 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerClosetSet : NetMessage
{
    public override int Opcode => 167;
    public override string Name => "CNE_CZ_ZoneServerClosetSet";

    public byte Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerPlayerWorkCheckData — 172 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerPlayerWorkCheckData : NetMessage
{
    public override int Opcode => 172;
    public override string Name => "CNE_CZ_ZoneServerPlayerWorkCheckData";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyInstanceCreate — 173 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyInstanceCreate : NetMessage
{
    public override int Opcode => 173;
    public override string Name => "CNE_CZ_ZoneServerFamilyInstanceCreate";

    public short Field1  { get; set; }
    public byte Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteByte(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyTownQuery — 174 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyTownQuery : NetMessage
{
    public override int Opcode => 174;
    public override string Name => "CNE_CZ_ZoneServerFamilyTownQuery";

    public short Field1  { get; set; }
    public short Field2  { get; set; }
    public int Field3  { get; set; }
    public string Field4 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteInt(Field3);
        s.WriteString(Field4);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadInt();
        Field4 = s.ReadString();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyTownMemberQuery — 175 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyTownMemberQuery : NetMessage
{
    public override int Opcode => 175;
    public override string Name => "CNE_CZ_ZoneServerFamilyTownMemberQuery";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyTownEnter — 176 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyTownEnter : NetMessage
{
    public override int Opcode => 176;
    public override string Name => "CNE_CZ_ZoneServerFamilyTownEnter";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerQueryLicense — 181 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryLicense : NetMessage
{
    public override int Opcode => 181;
    public override string Name => "CNE_CZ_ZoneServerQueryLicense";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerPlayerLicense — 182 : pose/déplace/
/// déséquipe des certificats. Codec serveur
/// <c>CNE_CZ_ZoneServerPlayerLicense</c> : [u16 n]{s16 lic,u8 slot}n
/// [s16 classId] — classId active → locs player_license, autre
/// classe → réécrit lic_1..7 (envoyer le layout complet).</summary>
public sealed class ZoneServerPlayerLicense : NetMessage
{
    public override int Opcode => 182;
    public override string Name => "CNE_CZ_ZoneServerPlayerLicense";

    /// <summary>Paires (licenseId s16, slot u8) — slot 0 =
    /// déséquipe.</summary>
    public List<(short LicenseId, byte Slot)> Selections { get; } = new();
    /// <summary>Classe cible (s16).</summary>
    public short ClassId { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Selections.Count);          // u16 count
        foreach (var (lic, slot) in Selections)
        {
            s.WriteShort(lic);                          // s16 id licence
            s.WriteByte(slot);                          // u8 slot
        }
        s.WriteShort(ClassId);                          // s16 classId
    }

    public override void Deserialize(BinReader s)
    {
        var count = (ushort)s.ReadShort();
        for (var i = 0; i < count; i++)
        {
            var lic = s.ReadShort();
            var slot = s.ReadByte();
            Selections.Add((lic, slot));
        }
        ClassId = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerQueryLicenseSet — 183 : active un combo
/// de certificats posés. Codec serveur
/// <c>CNE_CZ_ZoneServerQueryLicenseSet</c> : [u16 n]{u16 lic}n — le
/// dernier u16 est le flag de moitié (0 = slots 1-4, 1 = 4-7).</summary>
public sealed class ZoneServerQueryLicenseSet : NetMessage
{
    public override int Opcode => 183;
    public override string Name => "CNE_CZ_ZoneServerQueryLicenseSet";

    /// <summary>Ids des certificats du combo + flag en dernière
    /// position.</summary>
    public List<ushort> LicenseIds { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)LicenseIds.Count);          // u16 count
        foreach (var id in LicenseIds)
            s.WriteShort((short)id);                    // u16 id
    }

    public override void Deserialize(BinReader s)
    {
        var count = (ushort)s.ReadShort();
        for (var i = 0; i < count; i++)
            LicenseIds.Add((ushort)s.ReadShort());
    }
}

/// <summary>CNE_CZ_ZoneServerCheckUpdateLottery — 184 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerCheckUpdateLottery : NetMessage
{
    public override int Opcode => 184;
    public override string Name => "CNE_CZ_ZoneServerCheckUpdateLottery";

    public ushort Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerTerritoryQuery — 189 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerTerritoryQuery : NetMessage
{
    public override int Opcode => 189;
    public override string Name => "CNE_CZ_ZoneServerTerritoryQuery";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerTerritoryJoin — 190 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerTerritoryJoin : NetMessage
{
    public override int Opcode => 190;
    public override string Name => "CNE_CZ_ZoneServerTerritoryJoin";

    public int Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerRollTreasure — 193 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerRollTreasure : NetMessage
{
    public override int Opcode => 193;
    public override string Name => "CNE_CZ_ZoneServerRollTreasure";

    public uint Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerRankInfoQuery — 194 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerRankInfoQuery : NetMessage
{
    public override int Opcode => 194;
    public override string Name => "CNE_CZ_ZoneServerRankInfoQuery";

    public short Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerGladiatorTeamCreate — 196 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerGladiatorTeamCreate : NetMessage
{
    public override int Opcode => 196;
    public override string Name => "CNE_CZ_ZoneServerGladiatorTeamCreate";

    public string Field1 { get; set; } = "";
    public byte Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteString(Field1);
        s.WriteByte(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadString();
        Field2 = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerGladiatorTeamQuery — 197 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerGladiatorTeamQuery : NetMessage
{
    public override int Opcode => 197;
    public override string Name => "CNE_CZ_ZoneServerGladiatorTeamQuery";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerColosseumJoinAnswer — 198 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerColosseumJoinAnswer : NetMessage
{
    public override int Opcode => 198;
    public override string Name => "CNE_CZ_ZoneServerColosseumJoinAnswer";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerColosseumJoinRequest — 199 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerColosseumJoinRequest : NetMessage
{
    public override int Opcode => 199;
    public override string Name => "CNE_CZ_ZoneServerColosseumJoinRequest";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerColosseumTeamQuery — 200 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerColosseumTeamQuery : NetMessage
{
    public override int Opcode => 200;
    public override string Name => "CNE_CZ_ZoneServerColosseumTeamQuery";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerColosseumLeaveJoin — 201 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerColosseumLeaveJoin : NetMessage
{
    public override int Opcode => 201;
    public override string Name => "CNE_CZ_ZoneServerColosseumLeaveJoin";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerCoRideInvite — 202 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerCoRideInvite : NetMessage
{
    public override int Opcode => 202;
    public override string Name => "CNE_CZ_ZoneServerCoRideInvite";

    public uint Field1  { get; set; }
    public byte Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteByte(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerEnterRoom — 207 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerEnterRoom : NetMessage
{
    public override int Opcode => 207;
    public override string Name => "CNE_CZ_ZoneServerEnterRoom";

    public int Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerRoomGetData — 210 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerRoomGetData : NetMessage
{
    public override int Opcode => 210;
    public override string Name => "CNE_CZ_ZoneServerRoomGetData";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerQueryLifeSkill — 213 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerQueryLifeSkill : NetMessage
{
    public override int Opcode => 213;
    public override string Name => "CNE_CZ_ZoneServerQueryLifeSkill";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerLifeSkill — 214 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerLifeSkill : NetMessage
{
    public override int Opcode => 214;
    public override string Name => "CNE_CZ_ZoneServerLifeSkill";

    public byte Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }
    public short Field4  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerColosseumModeQuery — 219 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerColosseumModeQuery : NetMessage
{
    public override int Opcode => 219;
    public override string Name => "CNE_CZ_ZoneServerColosseumModeQuery";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerRecruitInstance — 220 : format variable
/// <c>[s16 count][count×s16 nodeIds][s16 role][u8 flag]</c> (flag 0 =
/// inscription, 1 = retrait) — désasm Deserialize zone @0x0827c210.
/// </summary>
public sealed class ZoneServerRecruitInstance : NetMessage
{
    public override int Opcode => 220;
    public override string Name => "CNE_CZ_ZoneServerRecruitInstance";

    /// <summary>NodeIds de donjon demandés (≤4).</summary>
    public short[] Nodes { get; set; } = [];
    /// <summary>s16 — rôle.</summary>
    public short Role { get; set; }
    /// <summary>u8 — 0 = inscription, 1 = retrait.</summary>
    public byte Flag { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Nodes.Length);
        foreach (var n in Nodes) s.WriteShort(n);
        s.WriteShort(Role);
        s.WriteByte(Flag);
    }

    public override void Deserialize(BinReader s)
    {
        var count = s.ReadShort();
        var nodes = new short[Math.Max((short)0, count)];
        for (var i = 0; i < nodes.Length; i++) nodes[i] = s.ReadShort();
        Nodes = nodes;
        Role = s.ReadShort();
        Flag = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerChanceLuckyBagGetReward — 221 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerChanceLuckyBagGetReward : NetMessage
{
    public override int Opcode => 221;
    public override string Name => "CNE_CZ_ZoneServerChanceLuckyBagGetReward";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerChanceLuckyBagReTry — 222 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerChanceLuckyBagReTry : NetMessage
{
    public override int Opcode => 222;
    public override string Name => "CNE_CZ_ZoneServerChanceLuckyBagReTry";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerUpdateLotteryBonus — 224 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerUpdateLotteryBonus : NetMessage
{
    public override int Opcode => 224;
    public override string Name => "CNE_CZ_ZoneServerUpdateLotteryBonus";

    public short Field1  { get; set; }
    public ushort Field2  { get; set; }
    public short Field3  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = (ushort)s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyBattleAskInfo — 225 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerFamilyBattleAskInfo : NetMessage
{
    public override int Opcode => 225;
    public override string Name => "CNE_CZ_ZoneServerFamilyBattleAskInfo";

    public short Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerPVPRankLookEquip — 231 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerPVPRankLookEquip : NetMessage
{
    public override int Opcode => 231;
    public override string Name => "CNE_CZ_ZoneServerPVPRankLookEquip";

    public short Field1  { get; set; }
    public string Field2 { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteString(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadString();
    }
}

/// <summary>CNE_CZ_ZoneServerVerifyCubeCosts — 232 (C→S) : lancer
/// une recette de cube. Format prouvé par
/// <c>HandleVerifyCubeCostsAsync</c> (Execute natif @0x829f0d0) :
/// <code>[u32 recipe_id][u16 n][s16 bags ×n][u16 n][s16 slots ×n]
/// [s16 lucky_bag][s16 lucky_slot]</code> — n bags/slots = les items
/// placés dans la grille (n1 == n2 obligatoire côté serveur), lucky =
/// sac/slot du sac de chance (−1 si absent). La classe générée
/// aplatissait le format — codec dédié.</summary>
public sealed class ZoneServerVerifyCubeCosts : NetMessage
{
    public override int Opcode => 232;
    public override string Name => "CNE_CZ_ZoneServerVerifyCubeCosts";

    public int RecipeId { get; set; }
    public short[] Bags { get; set; } = Array.Empty<short>();
    public short[] Slots { get; set; } = Array.Empty<short>();
    public short LuckyBag { get; set; } = -1;
    public short LuckySlot { get; set; } = -1;

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(RecipeId);                       // u32 : id recette
        s.WriteShort((short)Bags.Length);           // u16 : nb sacs
        foreach (var b in Bags) s.WriteShort(b);    // s16 : sac
        s.WriteShort((short)Slots.Length);          // u16 : nb slots
        foreach (var sl in Slots) s.WriteShort(sl); // s16 : slot
        s.WriteShort(LuckyBag);                     // s16 : sac chance
        s.WriteShort(LuckySlot);                    // s16 : slot chance
    }

    public override void Deserialize(BinReader s)
    {
        RecipeId = s.ReadInt();
        var n = s.ReadUShort();
        Bags = new short[n];
        for (var i = 0; i < n; i++) Bags[i] = s.ReadShort();
        n = s.ReadUShort();
        Slots = new short[n];
        for (var i = 0; i < n; i++) Slots[i] = s.ReadShort();
        LuckyBag = s.ReadShort();
        LuckySlot = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerBattleRoyaleJoinRequest — 241 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerBattleRoyaleJoinRequest : NetMessage
{
    public override int Opcode => 241;
    public override string Name => "CNE_CZ_ZoneServerBattleRoyaleJoinRequest";

    public int Field1  { get; set; }
    public int Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerPlayerCCDataSave — 242 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerPlayerCCDataSave : NetMessage
{
    public override int Opcode => 242;
    public override string Name => "CNE_CZ_ZoneServerPlayerCCDataSave";

    public byte Field1  { get; set; }
    public byte Field2  { get; set; }
    public byte Field3  { get; set; }
    public ushort Field4  { get; set; }
    public uint Field5  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Field1);
        s.WriteByte(Field2);
        s.WriteByte(Field3);
        s.WriteShort((short)Field4);
        s.WriteInt((int)Field5);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadByte();
        Field2 = s.ReadByte();
        Field3 = s.ReadByte();
        Field4 = (ushort)s.ReadShort();
        Field5 = (uint)s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerEnterNodeFastDress — 243 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerEnterNodeFastDress : NetMessage
{
    public override int Opcode => 243;
    public override string Name => "CNE_CZ_ZoneServerEnterNodeFastDress";


    public override void Serialize(BinWriter s)
    {
        // aucun champ
    }

    public override void Deserialize(BinReader s)
    {
        // aucun champ
    }
}

/// <summary>CNE_CZ_ZoneServerApplyFastDressData — 244 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerApplyFastDressData : NetMessage
{
    public override int Opcode => 244;
    public override string Name => "CNE_CZ_ZoneServerApplyFastDressData";

    public int Field1  { get; set; }
    public int Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerSaveFastDressData — 245 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerSaveFastDressData : NetMessage
{
    public override int Opcode => 245;
    public override string Name => "CNE_CZ_ZoneServerSaveFastDressData";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerRecordFastDressInfo — 246 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerRecordFastDressInfo : NetMessage
{
    public override int Opcode => 246;
    public override string Name => "CNE_CZ_ZoneServerRecordFastDressInfo";

    public int Field1  { get; set; }
    public int Field2  { get; set; }
    public int Field3  { get; set; }
    public int Field4  { get; set; }
    public int Field5  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);
        s.WriteInt(Field4);
        s.WriteInt(Field5);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerUpdateFastDressPage — 247 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerUpdateFastDressPage : NetMessage
{
    public override int Opcode => 247;
    public override string Name => "CNE_CZ_ZoneServerUpdateFastDressPage";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerRemoveFastDressInfo — 248 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerRemoveFastDressInfo : NetMessage
{
    public override int Opcode => 248;
    public override string Name => "CNE_CZ_ZoneServerRemoveFastDressInfo";

    public short Field1  { get; set; }
    public int Field2  { get; set; }
    public int Field3  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Field2);
        s.WriteInt(Field3);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadInt();
        Field3 = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerVersusInstanceJoinRequest — 252 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerVersusInstanceJoinRequest : NetMessage
{
    public override int Opcode => 252;
    public override string Name => "CNE_CZ_ZoneServerVersusInstanceJoinRequest";

    public int Field1  { get; set; }
    public int Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerPayEventSelect — 253 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerPayEventSelect : NetMessage
{
    public override int Opcode => 253;
    public override string Name => "CNE_CZ_ZoneServerPayEventSelect";

    public uint Field1  { get; set; }
    public short Field2  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteShort(Field2);    }

    public override void Deserialize(BinReader s)
    {
        Field1 = (uint)s.ReadInt();
        Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerEvaluateLotteryOpen — 255 : layout verbatim codec serveur (C→S).</summary>
public sealed class ZoneServerEvaluateLotteryOpen : NetMessage
{
    public override int Opcode => 255;
    public override string Name => "CNE_CZ_ZoneServerEvaluateLotteryOpen";

    public int Value  { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Value);    }

    public override void Deserialize(BinReader s)
    {
        Value = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerExecuteMagicBox — 257 : wire réel
/// (vecteurs bag/slot + combos + catalyseur), lu par
/// <c>ZoneEntryFlow.MagicBox.TryParseMagicBoxRequest</c> — le codec
/// .g.cs aplati est FAUX pour cet opcode.</summary>
public sealed class ZoneServerExecuteMagicBox : NetMessage
{
    public override int Opcode => 257;
    public override string Name => "CNE_CZ_ZoneServerExecuteMagicBox";

    /// <summary>u16 — type d'opération boîte magique (1/2/3).</summary>
    public ushort Type { get; set; }
    /// <summary>Sacrifices (bag,slot) posés — wire [u16 n][s16×n bags]
    /// [u16 n][s16×n slots].</summary>
    public List<(short Bag, short Slot)> Sacrifices { get; } = new();
    /// <summary>Item principal [s16 bag][s16 slot].</summary>
    public short MainBag { get; set; } = -1;
    public short MainSlot { get; set; } = -1;
    /// <summary>Combos sélectionnés (≤3 s16).</summary>
    public List<short> SelectedCombos { get; } = new();
    /// <summary>Catalyseur item mall [s16 bag][s16 slot] (-1 = aucun).</summary>
    public short MallBag { get; set; } = -1;
    public short MallSlot { get; set; } = -1;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Type);
        s.WriteShort((short)Sacrifices.Count);
        foreach (var (bag, _) in Sacrifices) s.WriteShort(bag);
        s.WriteShort((short)Sacrifices.Count);
        foreach (var (_, slot) in Sacrifices) s.WriteShort(slot);
        s.WriteShort(MainBag); s.WriteShort(MainSlot);
        s.WriteShort((short)SelectedCombos.Count);
        foreach (var c in SelectedCombos) s.WriteShort(c);
        s.WriteShort(MallBag); s.WriteShort(MallSlot);
    }
    public override void Deserialize(BinReader s)
    {
        Type = s.ReadUShort();
        var n = s.ReadUShort();
        var bags = new short[n];
        for (var i = 0; i < n; i++) bags[i] = s.ReadShort();
        var m = s.ReadUShort();
        Sacrifices.Clear();
        for (var i = 0; i < m; i++)
            Sacrifices.Add((bags[i], s.ReadShort()));
        MainBag = s.ReadShort(); MainSlot = s.ReadShort();
        var k = s.ReadUShort();
        SelectedCombos.Clear();
        for (var i = 0; i < k; i++) SelectedCombos.Add(s.ReadShort());
        MallBag = s.ReadShort(); MallSlot = s.ReadShort();
    }
}


/// <summary>CNE_CZ_ZoneServerUpdateShortcut — 54 : [s16 page][s16 slot]
/// [i32 valeur][i32 ancienneValeur] (désasm CCharacter::UpdateShortcut ;
/// ancienne valeur ≠ 0 = déplacement).</summary>
public sealed class ZoneServerUpdateShortcut : NetMessage
{
    public override int Opcode => 54;
    public override string Name => "CNE_CZ_ZoneServerUpdateShortcut";
    public short Page { get; set; }
    public short Slot { get; set; }
    public int Value { get; set; }
    public int OldValue { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Page);
        s.WriteShort(Slot);
        s.WriteInt(Value);
        s.WriteInt(OldValue);
    }

    public override void Deserialize(BinReader s)
    {
        Page = s.ReadShort();
        Slot = s.ReadShort();
        Value = s.ReadInt();
        OldValue = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerArrangeItem — 260 : tri auto d'un sac
/// [s16 sac][s16 0] → réponse 620 ClientArrangeItemOK.</summary>
public sealed class ZoneArrangeItem : NetMessage
{
    public override int Opcode => 260;
    public override string Name => "CNE_CZ_ZoneServerArrangeItem";
    public short Bag { get; set; }
    public short Reserved { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Bag);
        s.WriteShort(Reserved);
    }
    public override void Deserialize(BinReader s)
    {
        Bag = s.ReadShort();
        Reserved = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerFamilyBuildingRepair — 180 : réparation
/// d'un bâtiment du hall [s16 loc][u8 repairAll] — réponse 487 +
/// éventuel refus 43 (texte 900 si privilège 10 absent).</summary>
public sealed class ZoneServerFamilyBuildingRepair : NetMessage
{
    public override int Opcode => 180;
    public override string Name => "CNE_CZ_ZoneServerFamilyBuildingRepair";
    public short Loc { get; set; }
    public byte RepairAll { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc);
        s.WriteByte(RepairAll);
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();
        RepairAll = s.ReadByte();
    }
}

/// <summary>CNE_CZ_ZoneServerBattleCrusadeJoin — 259 : demande de
/// participation à une croisade [i32 crusadeId] — relais mission
/// « CrusadeJoin crusade_id= » côté zone.</summary>
public sealed class ZoneServerBattleCrusadeJoin : NetMessage
{
    public override int Opcode => 259;
    public override string Name => "CNE_CZ_ZoneServerBattleCrusadeJoin";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}
