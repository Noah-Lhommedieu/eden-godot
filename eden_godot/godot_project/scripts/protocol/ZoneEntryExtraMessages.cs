using EdenClient.Net;

namespace EdenClient.Protocol;

// Parseurs « complétude » des NCIDs zone émis par le serveur C# et
// jusque-là ignorés — layouts traduits verbatim de
// `server/.../ZoneMessages.g.cs` (prouvés natifs). Les autres NCIDs
// du même lot (351/368/472/490/509/523/531/543/559/563/609…) ont
// déjà une classe liée dans HudExtensions → pas de doublon ici.

/// <summary>CNC_CZ_ClientLevelUp — 284 : montée de niveau du self
/// (u32 + u8 + s16 + i64 + i32 — positionnels, rôles à prouver).</summary>
public sealed class ZoneClientLevelUp : NetMessage
{
    public override int Opcode => 284;
    public override string Name => "CNC_CZ_ClientLevelUp";
    public uint Field1 { get; set; }
    public byte Field2 { get; set; }
    public short Field3 { get; set; }
    public long Field4 { get; set; }
    public int Field5 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1); s.WriteByte(Field2);
        s.WriteShort(Field3); s.WriteLong(Field4); s.WriteInt(Field5);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadUInt(); Field2 = s.ReadByte();
        Field3 = s.ReadShort(); Field4 = s.ReadLong();
        Field5 = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientUpdateRecommendActivities — 560 : maj
/// d'une activité recommandée (u16 + u32 + u16).</summary>
public sealed class ZoneClientUpdateRecommendActivities : NetMessage
{
    public override int Opcode => 560;
    public override string Name => "CNC_CZ_ClientUpdateRecommendActivities";
    public ushort Field1 { get; set; }
    public uint Field2 { get; set; }
    public ushort Field3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1); s.WriteInt((int)Field2);
        s.WriteShort((short)Field3);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadUShort(); Field2 = s.ReadUInt();
        Field3 = s.ReadUShort();
    }
}

/// <summary>CNC_CZ_ClientEquipTrainItemChanged — 570 : item
/// d'entraînement d'équipement changé (container/index/key s16×3).</summary>
public sealed class ZoneClientEquipTrainItemChanged : NetMessage
{
    public override int Opcode => 570;
    public override string Name => "CNC_CZ_ClientEquipTrainItemChanged";
    public short ContainerType { get; set; }
    public short Index { get; set; }
    public short Key { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ContainerType); s.WriteShort(Index);
        s.WriteShort(Key);
    }
    public override void Deserialize(BinReader s)
    {
        ContainerType = s.ReadShort(); Index = s.ReadShort();
        Key = s.ReadShort();
    }
}
