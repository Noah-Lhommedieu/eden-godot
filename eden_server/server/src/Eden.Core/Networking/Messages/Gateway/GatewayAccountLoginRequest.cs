namespace Eden.Core.Networking.Messages.Gateway;

/// <summary>
/// Requête d'authentification envoyée au GatewayServer.
/// Reversé depuis lapis::CNC_CG_AccountLogin (opcode 1).
/// </summary>
public sealed class GatewayAccountLoginRequest : NetCommand
{
    public override int Opcode => 1;
    public override string Name => "CNC_CG_AccountLogin";

    public string Username { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public int ClientVersion { get; set; }

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteString(Username);
        stream.WriteString(SessionToken);
        stream.WriteInt(ClientVersion);
    }

    public override void Deserialize(InBinStream stream)
    {
        Username = stream.ReadString();
        SessionToken = stream.ReadString();
        ClientVersion = stream.ReadInt();
    }
}
