namespace Eden.Core.Networking.Messages.Gateway;

/// <summary>
/// Réponse d'authentification envoyée par le GatewayServer.
/// Reversé depuis lapis::CNC_CG_AccountLoginResult (opcode 2).
/// </summary>
public sealed class GatewayAccountLoginResponse : NetCommand
{
    public override int Opcode => 2;
    public override string Name => "CNC_CG_AccountLoginResult";

    public bool Success { get; set; }
    public int AccountId { get; set; }
    public string Message { get; set; } = string.Empty;

    public override void Serialize(OutBinStream stream)
    {
        stream.WriteByte(Success ? (byte)1 : (byte)0);
        stream.WriteInt(AccountId);
        stream.WriteString(Message);
    }

    public override void Deserialize(InBinStream stream)
    {
        Success = stream.ReadByte() == 1;
        AccountId = stream.ReadInt();
        Message = stream.ReadString();
    }
}
