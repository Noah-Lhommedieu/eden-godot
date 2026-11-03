namespace Eden.Core.Networking.Messages.Gateway;

/// <summary>
/// Message de pulsation / keepalive envoyé périodiquement pour maintenir la connexion au GatewayServer.
/// Reversé depuis lapis::CNC_G_Heartbeat (opcode 0).
/// </summary>
public sealed class GatewayHeartbeatMessage : NetCommand
{
    public override int Opcode => 0;
    public override string Name => "CNC_G_Heartbeat";

    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteLong(Timestamp);
    }

    public override void Deserialize(InBinStream stream)
    {
        Timestamp = stream.ReadLong();
    }
}
