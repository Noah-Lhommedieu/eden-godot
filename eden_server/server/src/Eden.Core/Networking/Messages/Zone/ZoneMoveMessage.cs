using Eden.Core.Networking;

namespace Eden.Core.Networking.Messages.Zone;

/// <summary>
/// Paquet de déplacement spatial envoyé par le client et diffusé par le serveur de zone.
/// Reversé depuis lapis::CNC_CZ_ServerMoveSelf / CNC_CZ_ClientMoveSelf.
/// </summary>
public sealed class ZoneMoveMessage : NetCommand
{
    public override int Opcode => 0x0301;
    public override string Name => "CNC_CZ_MoveSelf";

    public int EntityId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float FacingDirection { get; set; }
    public float MoveSpeed { get; set; } = 5.0f;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteFloat(PositionX);
        stream.WriteFloat(PositionY);
        stream.WriteFloat(PositionZ);
        stream.WriteFloat(FacingDirection);
        stream.WriteFloat(MoveSpeed);
    }

    public override void Deserialize(InBinStream stream)
    {
        EntityId = stream.ReadInt();
        PositionX = stream.ReadFloat();
        PositionY = stream.ReadFloat();
        PositionZ = stream.ReadFloat();
        FacingDirection = stream.ReadFloat();
        MoveSpeed = stream.ReadFloat();
    }
}
