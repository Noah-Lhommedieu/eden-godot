namespace Eden.Core.Networking.Messages.Gateway;

/// <summary>
/// Commande d'expulsion de session envoyée par le GatewayServer.
/// Reversé depuis lapis::CNC_CG_KickUser (opcode 3).
/// </summary>
public sealed class GatewayKickUserMessage : NetCommand
{
    public override int Opcode => 3;
    public override string Name => "CNC_CG_KickUser";

    public int ReasonCode { get; set; }
    public string ReasonMessage { get; set; } = string.Empty;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteInt(ReasonCode);
        stream.WriteString(ReasonMessage);
    }

    public override void Deserialize(InBinStream stream)
    {
        ReasonCode = stream.ReadInt();
        ReasonMessage = stream.ReadString();
    }
}
