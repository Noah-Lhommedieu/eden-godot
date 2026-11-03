namespace Eden.Core.Networking.Messages.World;

/// <summary>
/// Handshake envoyé par le client au WorldServer.
/// Reversé depuis lapis::CNC_CW_ClientHello (opcode 0).
/// </summary>
public sealed class WorldClientHelloMessage : NetCommand
{
    public override int Opcode => 0;
    public override string Name => "CNC_CW_ClientHello";

    public float ClientVersion { get; set; } = 9.9f;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteFloat(ClientVersion);
    }

    public override void Deserialize(InBinStream stream)
    {
        ClientVersion = stream.ReadFloat();
    }
}
